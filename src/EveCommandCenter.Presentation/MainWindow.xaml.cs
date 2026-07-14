using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace EveCommandCenter.Presentation;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush HighContrastTextBrush = new(Color.FromRgb(242, 246, 250));
    private bool contrastRefreshQueued;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnWindowLoaded;
        LayoutUpdated += OnWindowLayoutUpdated;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e) => QueueContrastRefresh();

    private void OnWindowLayoutUpdated(object? sender, EventArgs e) => QueueContrastRefresh();

    private void QueueContrastRefresh()
    {
        if (contrastRefreshQueued)
        {
            return;
        }

        contrastRefreshQueued = true;
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                contrastRefreshQueued = false;
                ApplyHighContrastText(this);
            }));
    }

    private static void ApplyHighContrastText(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);

            if (child is TextBlock textBlock && !IsInsideComboBoxItem(textBlock))
            {
                if (textBlock.Foreground is not SolidColorBrush brush || ShouldIncreaseContrast(brush.Color))
                {
                    textBlock.Foreground = HighContrastTextBrush;
                }
            }
            else if (child is CheckBox checkBox)
            {
                checkBox.Foreground = HighContrastTextBrush;
            }

            ApplyHighContrastText(child);
        }
    }

    private static bool ShouldIncreaseContrast(Color color)
    {
        // Keep intentionally vivid status colors (green/red), but brighten muted gray/blue labels.
        int spread = Math.Max(color.R, Math.Max(color.G, color.B)) -
                     Math.Min(color.R, Math.Min(color.G, color.B));
        bool vivid = spread >= 50;
        int brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        return !vivid && brightness < 225;
    }

    private static bool IsInsideComboBoxItem(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is ComboBoxItem)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
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
