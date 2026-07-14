using System.Runtime.InteropServices;
using EveCommandCenter.Application.Diagnostics;
using Forms = System.Windows.Forms;

namespace EveCommandCenter.App.Shell;

public sealed record HotkeyBindings(
    string NextCharacter,
    string PreviousCharacter,
    string TogglePreviews);

public sealed record CharacterHotkeyBinding(
    string CharacterName,
    string Hotkey);

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int FirstCharacterHotkeyId = 100;

    private readonly HotkeyMessageWindow messageWindow;
    private readonly Action nextCharacter;
    private readonly Action previousCharacter;
    private readonly Action togglePreviews;
    private readonly Action<string> activateCharacter;
    private readonly Dictionary<int, HotkeyAction> actions = [];
    private readonly HashSet<int> registeredIds = [];
    private bool disposed;

    public GlobalHotkeyService(
        Action nextCharacter,
        Action previousCharacter,
        Action togglePreviews,
        Action<string> activateCharacter)
    {
        this.nextCharacter = nextCharacter;
        this.previousCharacter = previousCharacter;
        this.togglePreviews = togglePreviews;
        this.activateCharacter = activateCharacter;
        messageWindow = new HotkeyMessageWindow(OnHotkeyPressed);
        AppLog.Information("Hotkeys", "Global hotkey service initialized.");
    }

    public string? Apply(
        HotkeyBindings bindings,
        IReadOnlyList<CharacterHotkeyBinding> characterBindings)
    {
        UnregisterAll();
        actions.Clear();

        var requested = new List<RequestedHotkey>
        {
            new(1, "Next character", bindings.NextCharacter, nextCharacter),
            new(2, "Previous character", bindings.PreviousCharacter, previousCharacter),
            new(3, "Show / hide previews", bindings.TogglePreviews, togglePreviews),
        };

        int nextId = FirstCharacterHotkeyId;
        foreach (CharacterHotkeyBinding binding in characterBindings
                     .Where(binding => !string.IsNullOrWhiteSpace(binding.CharacterName))
                     .OrderBy(binding => binding.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            string characterName = binding.CharacterName.Trim();
            requested.Add(new RequestedHotkey(
                nextId++,
                $"Activate {characterName}",
                binding.Hotkey,
                () => activateCharacter(characterName)));
        }

        foreach (RequestedHotkey hotkey in requested)
        {
            if (string.IsNullOrWhiteSpace(hotkey.Value))
            {
                continue;
            }

            if (!TryParse(hotkey.Value, out uint modifiers, out uint virtualKey, out string? error))
            {
                RollBackRegistrations();
                string message = $"{hotkey.Name}: {error}";
                AppLog.Warning("Hotkeys", $"Could not parse hotkey '{hotkey.Value}': {message}");
                return message;
            }

            if (!RegisterHotKey(messageWindow.Handle, hotkey.Id, modifiers | ModNoRepeat, virtualKey))
            {
                RollBackRegistrations();
                string message = $"{hotkey.Name}: the hotkey '{hotkey.Value}' is unavailable or already used.";
                AppLog.Warning("Hotkeys", message);
                return message;
            }

            registeredIds.Add(hotkey.Id);
            actions[hotkey.Id] = new HotkeyAction(hotkey.Name, hotkey.Action);
            AppLog.Information("Hotkeys", $"Registered {hotkey.Name}: {hotkey.Value}.");
        }

        return null;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        UnregisterAll();
        actions.Clear();
        messageWindow.Dispose();
        AppLog.Information("Hotkeys", "Global hotkey service disposed.");
    }

    private void OnHotkeyPressed(int id)
    {
        if (!actions.TryGetValue(id, out HotkeyAction? hotkey))
        {
            AppLog.Warning("Hotkeys", $"Received unknown hotkey id {id}.");
            return;
        }

        AppLog.SetCurrentOperation($"hotkey: {hotkey.Name}");
        AppLog.Information("Hotkeys", $"Global hotkey pressed: {hotkey.Name}.");

        try
        {
            hotkey.Action();
        }
        catch (Exception exception)
        {
            AppLog.Error("Hotkeys", $"Global hotkey action failed: {hotkey.Name}.", exception);
        }
        finally
        {
            AppLog.SetCurrentOperation("idle");
        }
    }

    private void RollBackRegistrations()
    {
        UnregisterAll();
        actions.Clear();
    }

    private void UnregisterAll()
    {
        if (messageWindow.Handle == nint.Zero)
        {
            registeredIds.Clear();
            return;
        }

        foreach (int id in registeredIds)
        {
            _ = UnregisterHotKey(messageWindow.Handle, id);
        }

        registeredIds.Clear();
    }

    private static bool TryParse(
        string value,
        out uint modifiers,
        out uint virtualKey,
        out string? error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = null;

        string[] parts = value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            error = "enter a key, for example F1, 1 or Ctrl+Alt+P.";
            return false;
        }

        for (int index = 0; index < parts.Length - 1; index++)
        {
            switch (parts[index].ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= ModControl;
                    break;
                case "ALT":
                    modifiers |= ModAlt;
                    break;
                case "SHIFT":
                    modifiers |= ModShift;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= ModWin;
                    break;
                default:
                    error = $"unknown modifier '{parts[index]}'.";
                    return false;
            }
        }

        string keyName = NormalizeKeyName(parts[^1]);
        if (!Enum.TryParse(keyName, true, out Forms.Keys key) ||
            key == Forms.Keys.None ||
            IsModifierKey(key))
        {
            error = $"unknown key '{parts[^1]}'.";
            return false;
        }

        virtualKey = (uint)key;
        return true;
    }

    private static string NormalizeKeyName(string keyName) =>
        keyName.Length == 1 && char.IsDigit(keyName[0])
            ? $"D{keyName}"
            : keyName;

    private static bool IsModifierKey(Forms.Keys key) =>
        key is Forms.Keys.ControlKey
            or Forms.Keys.LControlKey
            or Forms.Keys.RControlKey
            or Forms.Keys.Menu
            or Forms.Keys.LMenu
            or Forms.Keys.RMenu
            or Forms.Keys.ShiftKey
            or Forms.Keys.LShiftKey
            or Forms.Keys.RShiftKey
            or Forms.Keys.LWin
            or Forms.Keys.RWin;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);

    private sealed class HotkeyMessageWindow : Forms.NativeWindow, IDisposable
    {
        private readonly Action<int> hotkeyPressed;

        public HotkeyMessageWindow(Action<int> hotkeyPressed)
        {
            this.hotkeyPressed = hotkeyPressed;
            CreateHandle(new Forms.CreateParams
            {
                Caption = "EVE Command Center Hotkeys",
            });
        }

        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == WmHotkey)
            {
                hotkeyPressed(message.WParam.ToInt32());
            }

            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Handle != nint.Zero)
            {
                DestroyHandle();
            }
        }
    }

    private sealed record RequestedHotkey(int Id, string Name, string Value, Action Action);

    private sealed record HotkeyAction(string Name, Action Action);
}
