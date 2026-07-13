using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using EveCommandCenter.Application.Abstractions;
using EveCommandCenter.Core.Clients;
using EveCommandCenter.Presentation;

namespace EveCommandCenter.App.Shell;

public sealed class FloatingPreviewManager : IDisposable
{
    private readonly MainWindowViewModel viewModel;
    private readonly IWindowActivationService activationService;
    private readonly Dictionary<long, FloatingPreviewWindow> windows = [];
    private bool previewsVisible = true;
    private bool disposed;

    public FloatingPreviewManager(
        MainWindowViewModel viewModel,
        IWindowActivationService activationService)
    {
        this.viewModel = viewModel;
        this.activationService = activationService;

        viewModel.Clients.CollectionChanged += OnClientsCollectionChanged;
        viewModel.PropertyChanged += OnSettingsChanged;

        foreach (DetectedClientViewModel client in viewModel.Clients)
        {
            client.PropertyChanged += OnClientChanged;
        }

        Reconcile();
    }

    public void ToggleVisibility()
    {
        previewsVisible = !previewsVisible;

        if (!previewsVisible)
        {
            foreach (FloatingPreviewWindow window in windows.Values)
            {
                window.Hide();
            }

            return;
        }

        Reconcile();
        foreach (FloatingPreviewWindow window in windows.Values)
        {
            if (!window.IsVisible)
            {
                window.Show();
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        viewModel.Clients.CollectionChanged -= OnClientsCollectionChanged;
        viewModel.PropertyChanged -= OnSettingsChanged;

        foreach (DetectedClientViewModel client in viewModel.Clients)
        {
            client.PropertyChanged -= OnClientChanged;
        }

        CloseAll();
    }

    private void OnClientsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DetectedClientViewModel client in e.OldItems)
            {
                client.PropertyChanged -= OnClientChanged;
                CloseWindow(client.SourceWindowId);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DetectedClientViewModel client in e.NewItems)
            {
                client.PropertyChanged += OnClientChanged;
            }
        }

        Reconcile();
    }

    private void OnClientChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DetectedClientViewModel.IsPreviewEligible)
            or nameof(DetectedClientViewModel.IsResponsive)
            or nameof(DetectedClientViewModel.DisplayName))
        {
            Reconcile();
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.AutoCreatePreviews))
        {
            Reconcile();
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.AlwaysOnTop)
            or nameof(MainWindowViewModel.ShowPreviewHeader)
            or nameof(MainWindowViewModel.PreviewWidth)
            or nameof(MainWindowViewModel.PreviewHeight)
            or nameof(MainWindowViewModel.PreviewOpacity))
        {
            foreach (FloatingPreviewWindow window in windows.Values)
            {
                ApplySettings(window);
            }
        }
    }

    private void Reconcile()
    {
        if (disposed)
        {
            return;
        }

        if (!viewModel.AutoCreatePreviews)
        {
            CloseAll();
            return;
        }

        HashSet<long> eligibleIds = viewModel.Clients
            .Where(client => client.IsPreviewEligible && client.IsResponsive)
            .Select(client => client.SourceWindowId)
            .ToHashSet();

        foreach (long windowId in windows.Keys.Where(id => !eligibleIds.Contains(id)).ToArray())
        {
            CloseWindow(windowId);
        }

        foreach (DetectedClientViewModel client in viewModel.Clients.Where(client =>
                     client.IsPreviewEligible && client.IsResponsive))
        {
            if (!windows.ContainsKey(client.SourceWindowId))
            {
                CreateWindow(client);
            }
        }
    }

    private void CreateWindow(DetectedClientViewModel client)
    {
        var window = new FloatingPreviewWindow(client, ActivateClientAsync);
        ApplySettings(window);
        PositionNewWindow(window, windows.Count);

        window.Closed += (_, _) => windows.Remove(client.SourceWindowId);
        windows.Add(client.SourceWindowId, window);

        if (previewsVisible)
        {
            window.Show();
        }
    }

    private async Task ActivateClientAsync(long sourceWindowId)
    {
        await activationService.ActivateAsync(new WindowId(sourceWindowId));
    }

    private void ApplySettings(FloatingPreviewWindow window)
    {
        window.Topmost = viewModel.AlwaysOnTop;
        window.ShowHeader = viewModel.ShowPreviewHeader;
        window.Width = Math.Clamp(viewModel.PreviewWidth, 220, 1920);
        window.Height = Math.Clamp(viewModel.PreviewHeight, 140, 1080);
        window.Opacity = Math.Clamp(viewModel.PreviewOpacity, 0.35, 1.0);
    }

    private static void PositionNewWindow(Window window, int index)
    {
        Rect workArea = SystemParameters.WorkArea;
        double margin = 12;
        double left = workArea.Right - window.Width - margin;
        double top = workArea.Top + margin + (index * (window.Height + margin));

        if (top + window.Height > workArea.Bottom)
        {
            int rowsPerColumn = Math.Max(1, (int)(workArea.Height / (window.Height + margin)));
            int column = index / rowsPerColumn;
            int row = index % rowsPerColumn;
            left = workArea.Right - ((column + 1) * (window.Width + margin));
            top = workArea.Top + margin + (row * (window.Height + margin));
        }

        window.Left = Math.Max(workArea.Left, left);
        window.Top = Math.Max(workArea.Top, top);
    }

    private void CloseWindow(long sourceWindowId)
    {
        if (!windows.Remove(sourceWindowId, out FloatingPreviewWindow? window))
        {
            return;
        }

        window.Close();
    }

    private void CloseAll()
    {
        foreach (FloatingPreviewWindow window in windows.Values.ToArray())
        {
            window.Close();
        }

        windows.Clear();
    }
}
