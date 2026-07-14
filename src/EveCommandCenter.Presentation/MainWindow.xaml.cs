using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace EveCommandCenter.Presentation;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnHotkeyPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        if (!textBox.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            _ = textBox.Focus();
        }

        textBox.SelectAll();
    }

    private void OnHotkeyGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.SelectAll();
        }
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        Key key = ResolveKey(e);
        e.Handled = true;

        if (IsModifierKey(key))
        {
            return;
        }

        if (key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            SetCapturedHotkey(textBox, string.Empty);
            return;
        }

        string keyName = FormatKeyName(key);
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        var parts = new List<string>(5);

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(keyName);
        SetCapturedHotkey(textBox, string.Join('+', parts));
    }

    private static void SetCapturedHotkey(TextBox textBox, string value)
    {
        textBox.Text = value;
        BindingExpression? binding = textBox.GetBindingExpression(TextBox.TextProperty);
        binding?.UpdateSource();
        textBox.CaretIndex = textBox.Text.Length;
        textBox.SelectAll();
    }

    private static Key ResolveKey(KeyEventArgs e)
    {
        if (e.Key == Key.System)
        {
            return e.SystemKey;
        }

        if (e.Key == Key.ImeProcessed)
        {
            return e.ImeProcessedKey;
        }

        if (e.Key == Key.DeadCharProcessed)
        {
            return e.DeadCharProcessedKey;
        }

        return e.Key;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LeftShift
            or Key.RightShift
            or Key.LWin
            or Key.RWin;

    private static string FormatKeyName(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString();
        }

        return key switch
        {
            Key.None => string.Empty,
            Key.Return => "Enter",
            Key.Capital => "CapsLock",
            Key.Snapshot => "PrintScreen",
            Key.Next => "PageDown",
            Key.Prior => "PageUp",
            _ => key.ToString(),
        };
    }
}
