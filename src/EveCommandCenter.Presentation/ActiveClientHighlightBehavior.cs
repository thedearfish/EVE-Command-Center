using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace EveCommandCenter.Presentation;

public static class ActiveClientHighlightBehavior
{
    private const uint GaRoot = 2;
    private const uint GaRootOwner = 3;

    private static readonly HashSet<FloatingPreviewWindow> Windows = [];
    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ActiveClientHighlightBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    static ActiveClientHighlightBehavior()
    {
        Timer.Tick += OnTimerTick;
    }

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FloatingPreviewWindow window)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            window.Loaded += OnWindowLoaded;
            window.Closed += OnWindowClosed;
        }
        else
        {
            window.Loaded -= OnWindowLoaded;
            window.Closed -= OnWindowClosed;
            Windows.Remove(window);
            window.IsClientActive = false;
            StopTimerWhenUnused();
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window)
        {
            return;
        }

        Windows.Add(window);
        UpdateHighlights();
        if (!Timer.IsEnabled)
        {
            Timer.Start();
        }
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FloatingPreviewWindow window)
        {
            return;
        }

        window.Loaded -= OnWindowLoaded;
        window.Closed -= OnWindowClosed;
        Windows.Remove(window);
        window.IsClientActive = false;
        StopTimerWhenUnused();
    }

    private static void OnTimerTick(object? sender, EventArgs e) => UpdateHighlights();

    private static void UpdateHighlights()
    {
        nint foreground = GetForegroundWindow();
        nint foregroundRoot = foreground == nint.Zero ? nint.Zero : GetAncestor(foreground, GaRoot);
        nint foregroundRootOwner = foreground == nint.Zero ? nint.Zero : GetAncestor(foreground, GaRootOwner);

        foreach (FloatingPreviewWindow window in Windows.ToArray())
        {
            if (!window.IsLoaded || window.DataContext is not DetectedClientViewModel client)
            {
                window.IsClientActive = false;
                continue;
            }

            nint source = new(client.SourceWindowId);
            bool active = source != nint.Zero &&
                          (source == foreground || source == foregroundRoot || source == foregroundRootOwner);
            if (window.IsClientActive != active)
            {
                window.IsClientActive = active;
            }
        }
    }

    private static void StopTimerWhenUnused()
    {
        if (Windows.Count == 0)
        {
            Timer.Stop();
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint windowHandle, uint flags);
}
