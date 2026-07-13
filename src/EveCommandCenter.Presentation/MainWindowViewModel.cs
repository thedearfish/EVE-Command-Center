using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using EveCommandCenter.Application.Discovery;

namespace EveCommandCenter.Presentation;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IWindowSnapshotSource windowSnapshotSource;
    private readonly EveWindowClassifier classifier;
    private readonly DispatcherTimer timer;
    private string statusMessage = "Waiting for first scan...";
    private DateTimeOffset? lastScanAt;
    private bool disposed;

    public MainWindowViewModel(
        IWindowSnapshotSource windowSnapshotSource,
        EveWindowClassifier classifier)
    {
        this.windowSnapshotSource = windowSnapshotSource;
        this.classifier = classifier;

        timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(750),
        };
        timer.Tick += OnTimerTick;
        timer.Start();

        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => "EVE Command Center";

    public ObservableCollection<DetectedClientViewModel> Clients { get; } = [];

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetField(ref statusMessage, value);
    }

    public int ClientCount => Clients.Count;

    public int CharacterCount => Clients.Count(client => client.State == "Ready");

    public int CharacterSelectionCount => Clients.Count(client => client.State == "Character Selection");

    public string LastScanText => lastScanAt is null
        ? "Never"
        : lastScanAt.Value.LocalDateTime.ToString("HH:mm:ss.fff");

    public void Refresh()
    {
        try
        {
            IReadOnlyList<WindowCandidate> windows = windowSnapshotSource.Capture();
            List<DetectedClientViewModel> detected = [];

            foreach (WindowCandidate window in windows)
            {
                EveWindowClassification classification = classifier.Classify(window);
                if (classification.Kind == EveWindowKind.NotEve)
                {
                    continue;
                }

                bool isCharacter = classification.Kind == EveWindowKind.Character;
                detected.Add(new DetectedClientViewModel(
                    classification.DisplayName,
                    isCharacter ? "Ready" : "Character Selection",
                    window.ProcessId,
                    $"0x{window.WindowId.Value:X}",
                    window.Title,
                    window.IsVisible,
                    window.IsMinimized,
                    window.IsResponsive,
                    isCharacter && window.IsResponsive));
            }

            detected.Sort((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName));

            Clients.Clear();
            foreach (DetectedClientViewModel client in detected)
            {
                Clients.Add(client);
            }

            lastScanAt = DateTimeOffset.Now;
            StatusMessage = $"Scanning every {timer.Interval.TotalMilliseconds:0} ms · " +
                            $"{ClientCount} EVE client(s) detected · Last scan {LastScanText}";
            NotifyCounters();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Window scan failed: {exception.Message}";
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        timer.Stop();
        timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(object? sender, EventArgs e) => Refresh();

    private void NotifyCounters()
    {
        OnPropertyChanged(nameof(ClientCount));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(CharacterSelectionCount));
        OnPropertyChanged(nameof(LastScanText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }
}
