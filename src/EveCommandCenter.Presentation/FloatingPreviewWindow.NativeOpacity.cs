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

    public double PreviewOpacity
    {
        get => (double)GetValue(PreviewOpacityProperty);
        set => SetValue(PreviewOpacityProperty, value);
    }

    private static object CoercePreviewOpacity(DependencyObject dependencyObject, object baseValue) =>
        Math.Clamp((double)baseValue, 0.05, 1.0);

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
