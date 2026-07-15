using System.Windows;

namespace EveCommandCenter.Presentation;

public partial class FloatingPreviewWindow
{
    public static readonly DependencyProperty PreviewOpacityProperty = DependencyProperty.Register(
        nameof(PreviewOpacity),
        typeof(double),
        typeof(FloatingPreviewWindow),
        new FrameworkPropertyMetadata(
            1.0,
            FrameworkPropertyMetadataOptions.AffectsRender,
            OnPreviewOpacityChanged,
            CoercePreviewOpacity));

    private bool transferringWindowOpacity;

    static FloatingPreviewWindow()
    {
        OpacityProperty.OverrideMetadata(
            typeof(FloatingPreviewWindow),
            new FrameworkPropertyMetadata(1.0, OnWindowOpacityChanged));
    }

    public double PreviewOpacity
    {
        get => (double)GetValue(PreviewOpacityProperty);
        set => SetValue(PreviewOpacityProperty, value);
    }

    private static object CoercePreviewOpacity(DependencyObject dependencyObject, object baseValue) =>
        Math.Clamp((double)baseValue, 0.05, 1.0);

    private static void OnWindowOpacityChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FloatingPreviewWindow window || window.transferringWindowOpacity)
        {
            return;
        }

        double requested = Math.Clamp((double)e.NewValue, 0.05, 1.0);
        window.transferringWindowOpacity = true;
        try
        {
            window.SetCurrentValue(PreviewOpacityProperty, requested);

            // The DWM thumbnail and text overlay consume PreviewOpacity directly.
            // Keep the destination HWND itself fully opaque so DWM can composite the
            // thumbnail correctly over the transparent glass client surface.
            if (Math.Abs(requested - 1.0) > 0.0001)
            {
                window.SetCurrentValue(OpacityProperty, 1.0);
            }
        }
        finally
        {
            window.transferringWindowOpacity = false;
        }
    }

    private static void OnPreviewOpacityChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FloatingPreviewWindow window)
        {
            window.QueueOverlaySync();
        }
    }
}
