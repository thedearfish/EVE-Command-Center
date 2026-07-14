using System.Windows;

namespace EveCommandCenter.Presentation;

public partial class FloatingPreviewWindow
{
    private bool normalizingWpfOpacity;

    static FloatingPreviewWindow()
    {
        OpacityProperty.OverrideMetadata(
            typeof(FloatingPreviewWindow),
            new FrameworkPropertyMetadata(1.0, OnWindowOpacityChanged));
    }

    private static void OnWindowOpacityChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FloatingPreviewWindow window || window.normalizingWpfOpacity)
        {
            return;
        }

        double requestedOpacity = Math.Clamp((double)e.NewValue, 0.05, 1.0);
        NativeWindowOpacityBehavior.SetOpacity(window, requestedOpacity);

        if (Math.Abs(requestedOpacity - 1.0) < 0.0001)
        {
            return;
        }

        window.normalizingWpfOpacity = true;
        try
        {
            // Keep WPF/DWM rendering fully opaque internally. The native HWND alpha
            // is applied separately so the desktop remains visible through the preview.
            window.SetCurrentValue(OpacityProperty, 1.0);
        }
        finally
        {
            window.normalizingWpfOpacity = false;
        }
    }
}
