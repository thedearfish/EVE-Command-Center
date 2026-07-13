using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace EveCommandCenter.App.Shell;

public sealed record HotkeyBindings(
    string NextCharacter,
    string PreviousCharacter,
    string TogglePreviews);

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly HotkeyMessageWindow messageWindow;
    private readonly Dictionary<int, Action> actions = [];
    private bool disposed;

    public GlobalHotkeyService(
        Action nextCharacter,
        Action previousCharacter,
        Action togglePreviews)
    {
        messageWindow = new HotkeyMessageWindow(OnHotkeyPressed);
        actions[1] = nextCharacter;
        actions[2] = previousCharacter;
        actions[3] = togglePreviews;
    }

    public string? Apply(HotkeyBindings bindings)
    {
        UnregisterAll();

        var requested = new[]
        {
            (Id: 1, Name: "Next character", Value: bindings.NextCharacter),
            (Id: 2, Name: "Previous character", Value: bindings.PreviousCharacter),
            (Id: 3, Name: "Show / hide previews", Value: bindings.TogglePreviews),
        };

        foreach (var hotkey in requested)
        {
            if (!TryParse(hotkey.Value, out uint modifiers, out uint virtualKey, out string? error))
            {
                UnregisterAll();
                return $"{hotkey.Name}: {error}";
            }

            if (!RegisterHotKey(messageWindow.Handle, hotkey.Id, modifiers | ModNoRepeat, virtualKey))
            {
                UnregisterAll();
                return $"{hotkey.Name}: the combination '{hotkey.Value}' is unavailable or already used.";
            }
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
        messageWindow.Dispose();
    }

    private void OnHotkeyPressed(int id)
    {
        if (actions.TryGetValue(id, out Action? action))
        {
            action();
        }
    }

    private void UnregisterAll()
    {
        if (messageWindow.Handle == nint.Zero)
        {
            return;
        }

        foreach (int id in actions.Keys)
        {
            _ = UnregisterHotKey(messageWindow.Handle, id);
        }
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

        if (parts.Length < 2)
        {
            error = "use at least one modifier and one key, for example Ctrl+Alt+Right.";
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

        string keyName = parts[^1];
        if (!Enum.TryParse(keyName, true, out Forms.Keys key) || key == Forms.Keys.None)
        {
            error = $"unknown key '{keyName}'.";
            return false;
        }

        virtualKey = (uint)key;
        return modifiers != 0;
    }

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
}
