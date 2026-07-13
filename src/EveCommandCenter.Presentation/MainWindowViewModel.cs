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

    public IEnumerable<DetectedClientViewModel> PreviewClients => Clients.Where(client => client.IsPreviewEligible);

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
            var seenWindowIds = new HashSet<long>();

            foreach (WindowCandidate window in windows)
            {
                EveWindowClassification classification = classifier.Classify(window);
                if (classification.Kind == EveWindowKind.NotEve)
                {
                    continue;
                }

                long sourceWindowId = window.WindowId.Value;
                seenWindowIds.Add(sourceWindowId);
                bool isCharacter = classification.Kind == EveWindowKind.Character;
                string state = isCharacter ? "Ready" : "Character Selection";

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

            SortClients();
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

    private void SortClients()
    {
        List<DetectedClientViewModel> ordered = Clients
            .OrderBy(client => client.DisplayName, StringComparer.OrdinalIgnoreCase)
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

    private void OnTimerTick(object? sender, EventArgs e) => Refresh();

    private void NotifyCounters()
    {
        OnPropertyChanged(nameof(ClientCount));
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(CharacterSelectionCount));
        OnPropertyChanged(nameof(LastScanText));
        OnPropertyChanged(nameof(PreviewClients));
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