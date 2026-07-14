using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using EveCommandCenter.Application.Diagnostics;
using EveCommandCenter.Application.Discovery;

namespace EveCommandCenter.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly StringComparer CharacterNameComparer = StringComparer.OrdinalIgnoreCase;

    private readonly IWindowSnapshotSource windowSnapshotSource;
    private readonly EveWindowClassifier classifier;
    private readonly DispatcherTimer timer;
    private string statusMessage = "Starting EVE client monitoring...";
    private bool autoCreatePreviews;
    private bool alwaysOnTop;
    private bool showPreviewHeader;
    private int previewWidth;
    private int previewHeight;
    private double previewOpacity;
    private string nextCharacterHotkey;
    private string previousCharacterHotkey;
    private string togglePreviewsHotkey;
    private bool refreshInProgress;
    private bool disposed;

    public MainWindowViewModel(
        IWindowSnapshotSource windowSnapshotSource,
        EveWindowClassifier classifier,
        SettingsSnapshot settings)
    {
        this.windowSnapshotSource = windowSnapshotSource;
        this.classifier = classifier;

        autoCreatePreviews = settings.AutoCreatePreviews;
        alwaysOnTop = settings.AlwaysOnTop;
        showPreviewHeader = settings.ShowPreviewHeader;
        previewWidth = settings.PreviewWidth;
        previewHeight = settings.PreviewHeight;
        previewOpacity = settings.PreviewOpacity;
        nextCharacterHotkey = settings.NextCharacterHotkey;
        previousCharacterHotkey = settings.PreviousCharacterHotkey;
        togglePreviewsHotkey = settings.TogglePreviewsHotkey;

        foreach (CharacterPreviewProfileSnapshot profile in settings.CharacterProfiles
                     .Where(profile => !string.IsNullOrWhiteSpace(profile.CharacterName))
                     .GroupBy(profile => profile.CharacterName.Trim(), CharacterNameComparer)
                     .Select(group => group.First())
                     .OrderBy(profile => profile.CharacterName, CharacterNameComparer))
        {
            AddProfile(new CharacterPreviewProfileViewModel(profile));
        }

        SaveCommand = new RelayCommand(() => SettingsSaveRequested?.Invoke(this, EventArgs.Empty));

        timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        timer.Tick += OnTimerTick;
        timer.Start();

        _ = RefreshAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? SettingsSaveRequested;

    public event EventHandler? SettingsPersistRequested;

    public string Title => "EVE Command Center — Settings";

    public ObservableCollection<DetectedClientViewModel> Clients { get; } = [];

    public ObservableCollection<CharacterPreviewProfileViewModel> CharacterProfiles { get; } = [];

    public IReadOnlyList<PreviewModeOption> PreviewModeOptions { get; } =
    [
        new(
            PreviewContentMode.Standard,
            "Standard",
            "Live DWM image with character name and optional custom label."),
        new(
            PreviewContentMode.ImageOnly,
            "Image only",
            "Live DWM image without a header."),
        new(
            PreviewContentMode.TextOnly,
            "Name only (low load)",
            "No DWM thumbnail. Shows the character name and optional custom label on a compact gray tile."),
    ];

    public IEnumerable<DetectedClientViewModel> PreviewClients => Clients.Where(client => client.IsPreviewEligible);

    public ICommand SaveCommand { get; }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public int ClientCount => Clients.Count;

    public int CharacterCount => Clients.Count(client => client.State == "Ready");

    public int CharacterSelectionCount => Clients.Count(client => client.State == "Character Selection");

    public string ClientSummary => CharacterCount switch
    {
        0 when CharacterSelectionCount == 0 => "No EVE clients detected",
        0 => $"{CharacterSelectionCount} client(s) at Character Selection",
        _ when CharacterSelectionCount == 0 => $"{CharacterCount} logged-in character(s) detected",
        _ => $"{CharacterCount} logged in · {CharacterSelectionCount} at Character Selection",
    };

    public bool AutoCreatePreviews
    {
        get => autoCreatePreviews;
        set => SetField(ref autoCreatePreviews, value);
    }

    public bool AlwaysOnTop
    {
        get => alwaysOnTop;
        set => SetField(ref alwaysOnTop, value);
    }

    public bool ShowPreviewHeader
    {
        get => showPreviewHeader;
        set => SetField(ref showPreviewHeader, value);
    }

    public int PreviewWidth
    {
        get => previewWidth;
        set => SetField(ref previewWidth, Math.Clamp(value, 220, 1920));
    }

    public int PreviewHeight
    {
        get => previewHeight;
        set => SetField(ref previewHeight, Math.Clamp(value, 140, 1080));
    }

    public double PreviewOpacity
    {
        get => previewOpacity;
        set => SetField(ref previewOpacity, Math.Clamp(value, 0.35, 1.0));
    }

    public int PreviewOpacityPercent => (int)Math.Round(PreviewOpacity * 100);

    public string NextCharacterHotkey
    {
        get => nextCharacterHotkey;
        set => SetField(ref nextCharacterHotkey, value.Trim());
    }

    public string PreviousCharacterHotkey
    {
        get => previousCharacterHotkey;
        set => SetField(ref previousCharacterHotkey, value.Trim());
    }

    public string TogglePreviewsHotkey
    {
        get => togglePreviewsHotkey;
        set => SetField(ref togglePreviewsHotkey, value.Trim());
    }

    public SettingsSnapshot CreateSettingsSnapshot() =>
        new(
            AutoCreatePreviews,
            AlwaysOnTop,
            ShowPreviewHeader,
            PreviewWidth,
            PreviewHeight,
            PreviewOpacity,
            NextCharacterHotkey,
            PreviousCharacterHotkey,
            TogglePreviewsHotkey,
            CharacterProfiles.Select(profile => profile.CreateSnapshot()).ToArray());

    public void SetSettingsResult(string message) => StatusMessage = message;

    public CharacterPreviewProfileViewModel GetOrCreateCharacterProfile(string characterName)
    {
        string normalizedName = characterName.Trim();
        CharacterPreviewProfileViewModel? existing = CharacterProfiles.FirstOrDefault(profile =>
            CharacterNameComparer.Equals(profile.CharacterName, normalizedName));

        if (existing is not null)
        {
            return existing;
        }

        var created = new CharacterPreviewProfileViewModel(new CharacterPreviewProfileSnapshot(
            normalizedName,
            string.Empty,
            PreviewContentMode.Standard,
            string.Empty,
            null,
            null,
            null,
            null));

        AddProfile(created);
        SortProfiles();
        SettingsPersistRequested?.Invoke(this, EventArgs.Empty);
        return created;
    }

    public void UpdateCharacterLayout(
        string characterName,
        double left,
        double top,
        double width,
        double height)
    {
        CharacterPreviewProfileViewModel profile = GetOrCreateCharacterProfile(characterName);
        profile.UpdateLayout(left, top, width, height);
    }

    public void Refresh() => ApplyRefresh(CaptureClassifiedWindows());

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;

        foreach (CharacterPreviewProfileViewModel profile in CharacterProfiles)
        {
            profile.PropertyChanged -= OnProfilePropertyChanged;
        }
    }

    private async Task RefreshAsync()
    {
        if (disposed || refreshInProgress)
        {
            return;
        }

        refreshInProgress = true;
        try
        {
            ClassifiedWindow[] detected = await Task.Run(CaptureClassifiedWindows);
            if (!disposed)
            {
                ApplyRefresh(detected);
            }
        }
        catch (Exception exception)
        {
            StatusMessage = $"EVE client monitoring failed: {exception.Message}";
            AppLog.Error("Discovery", "Background EVE client scan failed.", exception);
        }
        finally
        {
            refreshInProgress = false;
        }
    }

    private ClassifiedWindow[] CaptureClassifiedWindows() =>
        windowSnapshotSource
            .Capture()
            .Select(window => new ClassifiedWindow(window, classifier.Classify(window)))
            .Where(item => item.Classification.Kind != EveWindowKind.NotEve)
            .ToArray();

    private void ApplyRefresh(IReadOnlyList<ClassifiedWindow> detectedWindows)
    {
        try
        {
            var seenWindowIds = new HashSet<long>();
            var detectedCharacterNames = new HashSet<string>(CharacterNameComparer);

            foreach (ClassifiedWindow item in detectedWindows)
            {
                WindowCandidate window = item.Window;
                EveWindowClassification classification = item.Classification;
                long sourceWindowId = window.WindowId.Value;
                seenWindowIds.Add(sourceWindowId);
                bool isCharacter = classification.Kind == EveWindowKind.Character;
                string state = isCharacter ? "Ready" : "Character Selection";

                if (isCharacter)
                {
                    detectedCharacterNames.Add(classification.DisplayName);
                    _ = GetOrCreateCharacterProfile(classification.DisplayName);
                }

                DetectedClientViewModel? existing = Clients.FirstOrDefault(client =>
                    client.SourceWindowId == sourceWindowId);

                if (existing is null)
                {
                    Clients.Add(new DetectedClientViewModel(
                        sourceWindowId,
                        classification.DisplayName,
                        state,
                        window.ProcessId,
                        window.Title,
                        window.IsVisible,
                        window.IsMinimized,
                        window.IsResponsive,
                        isCharacter && window.IsResponsive));
                }
                else
                {
                    existing.Update(
                        classification.DisplayName,
                        state,
                        window.ProcessId,
                        window.Title,
                        window.IsVisible,
                        window.IsMinimized,
                        window.IsResponsive,
                        isCharacter && window.IsResponsive);
                }
            }

            for (int index = Clients.Count - 1; index >= 0; index--)
            {
                if (!seenWindowIds.Contains(Clients[index].SourceWindowId))
                {
                    Clients.RemoveAt(index);
                }
            }

            foreach (CharacterPreviewProfileViewModel profile in CharacterProfiles)
            {
                profile.IsDetected = detectedCharacterNames.Contains(profile.CharacterName);
            }

            SortClients();
            NotifyCounters();
            StatusMessage = ClientSummary;
        }
        catch (Exception exception)
        {
            StatusMessage = $"EVE client monitoring failed: {exception.Message}";
            AppLog.Error("Discovery", "Applying EVE client scan failed.", exception);
        }
    }

    private void AddProfile(CharacterPreviewProfileViewModel profile)
    {
        profile.PropertyChanged += OnProfilePropertyChanged;
        CharacterProfiles.Add(profile);
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterPreviewProfileViewModel.CustomLabel)
            or nameof(CharacterPreviewProfileViewModel.ContentMode)
            or nameof(CharacterPreviewProfileViewModel.ActivationHotkey)
            or nameof(CharacterPreviewProfileViewModel.HasSavedBounds))
        {
            SettingsPersistRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SortProfiles()
    {
        List<CharacterPreviewProfileViewModel> ordered = CharacterProfiles
            .OrderBy(profile => profile.CharacterName, CharacterNameComparer)
            .ToList();

        for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            CharacterPreviewProfileViewModel expected = ordered[targetIndex];
            int currentIndex = CharacterProfiles.IndexOf(expected);
            if (currentIndex != targetIndex)
            {
                CharacterProfiles.Move(currentIndex, targetIndex);
            }
        }
    }

    private void SortClients()
    {
        List<DetectedClientViewModel> ordered = Clients
            .OrderBy(client => client.DisplayName, CharacterNameComparer)
            .ToList();

        for (int targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
        {
            DetectedClientViewModel expected = ordered[targetIndex];
            int currentIndex = Clients.IndexOf(expected);
            if (currentIndex != targetIndex)
            {
                Clients.Move(currentIndex, targetIndex);
            }
        }
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await RefreshAsync();

    private void NotifyCounters()
    {
        OnPropertyChanged(nameof(ClientCount));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(CharacterSelectionCount));
        OnPropertyChanged(nameof(ClientSummary));
        OnPropertyChanged(nameof(PreviewClients));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);

        if (propertyName == nameof(PreviewOpacity))
        {
            OnPropertyChanged(nameof(PreviewOpacityPercent));
        }

        return true;
    }

    private sealed record ClassifiedWindow(
        WindowCandidate Window,
        EveWindowClassification Classification);
}
