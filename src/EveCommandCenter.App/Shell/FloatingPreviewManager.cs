using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using EveCommandCenter.Application.Abstractions;
using EveCommandCenter.Application.Diagnostics;
using EveCommandCenter.Core.Clients;
using EveCommandCenter.Presentation;

namespace EveCommandCenter.App.Shell;

public sealed class FloatingPreviewManager : IDisposable
{
    private static readonly StringComparer CharacterNameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly MainWindowViewModel viewModel;
    private readonly IWindowActivationService activationService;
    private readonly Dictionary<long, PreviewWindowEntry> windows = [];
    private bool previewsVisible = true;
    private bool disposed;

    public FloatingPreviewManager(
        MainWindowViewModel viewModel,
        IWindowActivationService activationService)
    {
        this.viewModel = viewModel;
        this.activationService = activationService;

        viewModel.Clients.CollectionChanged += OnClientsCollectionChanged;
        viewModel.CharacterProfiles.CollectionChanged += OnProfilesCollectionChanged;
        viewModel.PropertyChanged += OnSettingsChanged;

        foreach (DetectedClientViewModel client in viewModel.Clients)
        {
            client.PropertyChanged += OnClientChanged;
        }

        foreach (CharacterPreviewProfileViewModel profile in viewModel.CharacterProfiles)
        {
            profile.PropertyChanged += OnProfileChanged;
        }

        AppLog.Information("PreviewManager", "Floating preview manager initialized.");
        Reconcile();
    }

    public void ToggleVisibility()
    {
        previewsVisible = !previewsVisible;
        AppLog.Information(
            "PreviewManager",
            previewsVisible ? "Showing all preview windows." : "Hiding all preview windows.");

        if (!previewsVisible)
        {
            foreach (PreviewWindowEntry entry in windows.Values)
            {
                entry.Window.Hide();
            }

            return;
        }

        Reconcile();
        foreach (PreviewWindowEntry entry in windows.Values)
        {
            if (!entry.Window.IsVisible)
            {
                entry.Window.Show();
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
        viewModel.CharacterProfiles.CollectionChanged -= OnProfilesCollectionChanged;
        viewModel.PropertyChanged -= OnSettingsChanged;

        foreach (DetectedClientViewModel client in viewModel.Clients)
        {
            client.PropertyChanged -= OnClientChanged;
        }

        foreach (CharacterPreviewProfileViewModel profile in viewModel.CharacterProfiles)
        {
            profile.PropertyChanged -= OnProfileChanged;
        }

        CloseAll();
        AppLog.Information("PreviewManager", "Floating preview manager disposed.");
    }

    private void OnClientsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DetectedClientViewModel client in e.OldItems)
            {
                client.PropertyChanged -= OnClientChanged;
                AppLog.Information(
                    "Discovery",
                    $"EVE client removed: {client.DisplayName}; HWND=0x{client.SourceWindowId:X}.");
                CloseWindow(client.SourceWindowId);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (DetectedClientViewModel client in e.NewItems)
            {
                client.PropertyChanged += OnClientChanged;
                AppLog.Information(
                    "Discovery",
                    $"EVE client detected: {client.DisplayName}; state={client.State}; " +
                    $"PID={client.ProcessId}; HWND=0x{client.SourceWindowId:X}.");
            }
        }

        Reconcile();
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (CharacterPreviewProfileViewModel profile in e.OldItems)
            {
                profile.PropertyChanged -= OnProfileChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (CharacterPreviewProfileViewModel profile in e.NewItems)
            {
                profile.PropertyChanged += OnProfileChanged;
                AppLog.Information(
                    "Settings",
                    $"Character preview profile created for {profile.CharacterName}.");
            }
        }
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

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CharacterPreviewProfileViewModel profile)
        {
            return;
        }

        if (e.PropertyName == nameof(CharacterPreviewProfileViewModel.CustomLabel))
        {
            foreach (PreviewWindowEntry entry in EntriesForProfile(profile))
            {
                entry.Window.CustomLabel = profile.CustomLabel;
            }

            AppLog.Information(
                "Settings",
                $"Custom caption updated for {profile.CharacterName}.");
            return;
        }

        if (e.PropertyName == nameof(CharacterPreviewProfileViewModel.ContentMode))
        {
            foreach (PreviewWindowEntry entry in EntriesForProfile(profile))
            {
                ApplyProfile(entry.Window, profile);
                ApplyPreferredModeSize(entry.Window, profile.ContentMode);
                PersistWindowLayout(entry);
            }

            AppLog.Information(
                "Settings",
                $"Content mode for {profile.CharacterName} changed to {profile.ContentMode}.");
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.AutoCreatePreviews))
        {
            AppLog.Information(
                "Settings",
                $"Automatic preview creation set to {viewModel.AutoCreatePreviews}.");
            Reconcile();
            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.AlwaysOnTop)
            or nameof(MainWindowViewModel.ShowPreviewHeader)
            or nameof(MainWindowViewModel.PreviewOpacity))
        {
            foreach (PreviewWindowEntry entry in windows.Values)
            {
                ApplyGlobalSettings(entry.Window);
            }

            return;
        }

        if (e.PropertyName is nameof(MainWindowViewModel.PreviewWidth)
            or nameof(MainWindowViewModel.PreviewHeight))
        {
            foreach (PreviewWindowEntry entry in windows.Values.Where(entry => !entry.Profile.HasSavedBounds))
            {
                ApplyPreferredModeSize(entry.Window, entry.Profile.ContentMode);
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

        DetectedClientViewModel[] eligibleClients = viewModel.Clients
            .Where(client => client.IsPreviewEligible && client.IsResponsive)
            .ToArray();
        HashSet<long> eligibleIds = eligibleClients
            .Select(client => client.SourceWindowId)
            .ToHashSet();

        foreach (long windowId in windows.Keys.Where(id => !eligibleIds.Contains(id)).ToArray())
        {
            CloseWindow(windowId);
        }

        foreach (DetectedClientViewModel client in eligibleClients)
        {
            if (windows.TryGetValue(client.SourceWindowId, out PreviewWindowEntry? existing))
            {
                if (!CharacterNameComparer.Equals(existing.Profile.CharacterName, client.DisplayName))
                {
                    AppLog.Information(
                        "Discovery",
                        $"Character identity for HWND=0x{client.SourceWindowId:X} changed from " +
                        $"{existing.Profile.CharacterName} to {client.DisplayName}.");
                    CloseWindow(client.SourceWindowId);
                    CreateWindow(client);
                }

                continue;
            }

            CreateWindow(client);
        }
    }

    private void CreateWindow(DetectedClientViewModel client)
    {
        CharacterPreviewProfileViewModel profile =
            viewModel.GetOrCreateCharacterProfile(client.DisplayName);
        var window = new FloatingPreviewWindow(client, ActivateClientAsync);
        var entry = new PreviewWindowEntry(client, profile, window);

        ApplyGlobalSettings(window);
        ApplyProfile(window, profile);

        bool restored = TryGetSavedBounds(profile, out Rect savedBounds);
        if (restored)
        {
            window.ApplyBounds(
                savedBounds.Left,
                savedBounds.Top,
                savedBounds.Width,
                savedBounds.Height);
        }
        else
        {
            Size defaultSize = GetPreferredModeSize(profile.ContentMode);
            Rect defaultBounds = CalculateNewWindowBounds(defaultSize, windows.Count);
            window.ApplyBounds(
                defaultBounds.Left,
                defaultBounds.Top,
                defaultBounds.Width,
                defaultBounds.Height);
        }

        window.LayoutCommitted += OnWindowLayoutCommitted;
        window.Closed += OnWindowClosed;
        windows.Add(client.SourceWindowId, entry);

        AppLog.Information(
            "PreviewManager",
            $"Preview window created for {client.DisplayName}; source HWND=0x{client.SourceWindowId:X}; " +
            $"mode={profile.ContentMode}; restoredLayout={restored}; " +
            $"bounds={window.Left:0},{window.Top:0} {window.Width:0}x{window.Height:0}.");

        if (previewsVisible)
        {
            window.Show();
        }

        window.EnableLayoutTracking();
    }

    private async Task ActivateClientAsync(long sourceWindowId)
    {
        try
        {
            var result = await activationService.ActivateAsync(new WindowId(sourceWindowId));
            if (result.IsSuccess)
            {
                AppLog.Information(
                    "Activation",
                    $"Activated EVE client HWND=0x{sourceWindowId:X}.");
            }
            else
            {
                AppLog.Warning(
                    "Activation",
                    $"Could not activate EVE client HWND=0x{sourceWindowId:X}: {result.Message}");
            }
        }
        catch (Exception exception)
        {
            AppLog.Error(
                "Activation",
                $"Activation threw for EVE client HWND=0x{sourceWindowId:X}.",
                exception);
            throw;
        }
    }

    private void ApplyGlobalSettings(FloatingPreviewWindow window)
    {
        window.Topmost = viewModel.AlwaysOnTop;
        window.ShowHeader = viewModel.ShowPreviewHeader;
        window.Opacity = Math.Clamp(viewModel.PreviewOpacity, 0.35, 1.0);
    }

    private static void ApplyProfile(
        FloatingPreviewWindow window,
        CharacterPreviewProfileViewModel profile)
    {
        window.CustomLabel = profile.CustomLabel;
        window.ContentMode = profile.ContentMode;

        if (profile.ContentMode == PreviewContentMode.TextOnly)
        {
            window.MinWidth = 120;
            window.MinHeight = 48;
        }
        else
        {
            window.MinWidth = 220;
            window.MinHeight = 140;
        }
    }

    private void ApplyPreferredModeSize(
        FloatingPreviewWindow window,
        PreviewContentMode contentMode)
    {
        Size preferredSize = GetPreferredModeSize(contentMode);
        window.ApplySize(preferredSize.Width, preferredSize.Height);
    }

    private Size GetPreferredModeSize(PreviewContentMode contentMode) =>
        contentMode == PreviewContentMode.TextOnly
            ? new Size(240, 72)
            : new Size(
                Math.Clamp(viewModel.PreviewWidth, 220, 1920),
                Math.Clamp(viewModel.PreviewHeight, 140, 1080));

    private static bool TryGetSavedBounds(
        CharacterPreviewProfileViewModel profile,
        out Rect bounds)
    {
        bounds = Rect.Empty;
        if (!profile.HasSavedBounds)
        {
            return false;
        }

        double width = Math.Clamp(profile.Width!.Value, 120, 1920);
        double height = Math.Clamp(profile.Height!.Value, 48, 1080);
        var candidate = new Rect(profile.Left!.Value, profile.Top!.Value, width, height);
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        Rect visibleArea = Rect.Intersect(candidate, virtualScreen);

        if (visibleArea.IsEmpty || visibleArea.Width < 48 || visibleArea.Height < 32)
        {
            AppLog.Warning(
                "PreviewManager",
                $"Saved layout for {profile.CharacterName} is outside the current virtual desktop and will be reset.");
            return false;
        }

        bounds = candidate;
        return true;
    }

    private static Rect CalculateNewWindowBounds(Size size, int index)
    {
        Rect workArea = SystemParameters.WorkArea;
        const double margin = 12;
        double left = workArea.Right - size.Width - margin;
        double top = workArea.Top + margin + (index * (size.Height + margin));

        if (top + size.Height > workArea.Bottom)
        {
            int rowsPerColumn = Math.Max(1, (int)(workArea.Height / (size.Height + margin)));
            int column = index / rowsPerColumn;
            int row = index % rowsPerColumn;
            left = workArea.Right - ((column + 1) * (size.Width + margin));
            top = workArea.Top + margin + (row * (size.Height + margin));
        }

        left = Math.Max(workArea.Left, left);
        top = Math.Max(workArea.Top, top);
        return new Rect(left, top, size.Width, size.Height);
    }

    private IEnumerable<PreviewWindowEntry> EntriesForProfile(
        CharacterPreviewProfileViewModel profile) =>
        windows.Values.Where(entry => CharacterNameComparer.Equals(
            entry.Profile.CharacterName,
            profile.CharacterName));

    private void OnWindowLayoutCommitted(
        object? sender,
        PreviewWindowLayoutChangedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window)
        {
            return;
        }

        PreviewWindowEntry? entry = windows.Values.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Window, window));
        if (entry is null)
        {
            return;
        }

        AppLog.Information(
            "Layout",
            $"Committing layout for {entry.Profile.CharacterName}: " +
            $"{e.Left:0},{e.Top:0} {e.Width:0}x{e.Height:0}.");
        viewModel.UpdateCharacterLayout(
            entry.Profile.CharacterName,
            e.Left,
            e.Top,
            e.Width,
            e.Height);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FloatingPreviewWindow window)
        {
            return;
        }

        long? sourceWindowId = windows
            .Where(pair => ReferenceEquals(pair.Value.Window, window))
            .Select(pair => (long?)pair.Key)
            .FirstOrDefault();
        if (sourceWindowId.HasValue)
        {
            windows.Remove(sourceWindowId.Value);
        }
    }

    private void PersistWindowLayout(PreviewWindowEntry entry)
    {
        PreviewWindowLayoutChangedEventArgs layout = entry.Window.CaptureLayout();
        viewModel.UpdateCharacterLayout(
            entry.Profile.CharacterName,
            layout.Left,
            layout.Top,
            layout.Width,
            layout.Height);
    }

    private void CloseWindow(long sourceWindowId)
    {
        if (!windows.Remove(sourceWindowId, out PreviewWindowEntry? entry))
        {
            return;
        }

        PersistWindowLayout(entry);
        entry.Window.LayoutCommitted -= OnWindowLayoutCommitted;
        entry.Window.Closed -= OnWindowClosed;
        AppLog.Information(
            "PreviewManager",
            $"Closing preview window for {entry.Profile.CharacterName}; source HWND=0x{sourceWindowId:X}.");
        entry.Window.Close();
    }

    private void CloseAll()
    {
        foreach ((long sourceWindowId, PreviewWindowEntry entry) in windows.ToArray())
        {
            PersistWindowLayout(entry);
            entry.Window.LayoutCommitted -= OnWindowLayoutCommitted;
            entry.Window.Closed -= OnWindowClosed;
            AppLog.Information(
                "PreviewManager",
                $"Closing preview window for {entry.Profile.CharacterName}; source HWND=0x{sourceWindowId:X}.");
            entry.Window.Close();
            windows.Remove(sourceWindowId);
        }
    }

    private sealed record PreviewWindowEntry(
        DetectedClientViewModel Client,
        CharacterPreviewProfileViewModel Profile,
        FloatingPreviewWindow Window);
}
