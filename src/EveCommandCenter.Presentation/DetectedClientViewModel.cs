using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EveCommandCenter.Presentation;

public sealed class DetectedClientViewModel : INotifyPropertyChanged
{
    private string displayName;
    private string state;
    private int processId;
    private string windowTitle;
    private bool isVisible;
    private bool isMinimized;
    private bool isResponsive;
    private bool isCycleEligible;

    public DetectedClientViewModel(
        long sourceWindowId,
        string displayName,
        string state,
        int processId,
        string windowTitle,
        bool isVisible,
        bool isMinimized,
        bool isResponsive,
        bool isCycleEligible)
    {
        SourceWindowId = sourceWindowId;
        this.displayName = displayName;
        this.state = state;
        this.processId = processId;
        this.windowTitle = windowTitle;
        this.isVisible = isVisible;
        this.isMinimized = isMinimized;
        this.isResponsive = isResponsive;
        this.isCycleEligible = isCycleEligible;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long SourceWindowId { get; }

    public string WindowId => $"0x{SourceWindowId:X}";

    public bool IsPreviewEligible => State == "Ready";

    public string DisplayName
    {
        get => displayName;
        private set => SetField(ref displayName, value);
    }

    public string State
    {
        get => state;
        private set
        {
            if (SetField(ref state, value))
            {
                OnPropertyChanged(nameof(IsPreviewEligible));
            }
        }
    }

    public int ProcessId
    {
        get => processId;
        private set => SetField(ref processId, value);
    }

    public string WindowTitle
    {
        get => windowTitle;
        private set => SetField(ref windowTitle, value);
    }

    public bool IsVisible
    {
        get => isVisible;
        private set => SetField(ref isVisible, value);
    }

    public bool IsMinimized
    {
        get => isMinimized;
        private set => SetField(ref isMinimized, value);
    }

    public bool IsResponsive
    {
        get => isResponsive;
        private set => SetField(ref isResponsive, value);
    }

    public bool IsCycleEligible
    {
        get => isCycleEligible;
        private set => SetField(ref isCycleEligible, value);
    }

    public void Update(
        string nextDisplayName,
        string nextState,
        int nextProcessId,
        string nextWindowTitle,
        bool nextIsVisible,
        bool nextIsMinimized,
        bool nextIsResponsive,
        bool nextIsCycleEligible)
    {
        DisplayName = nextDisplayName;
        State = nextState;
        ProcessId = nextProcessId;
        WindowTitle = nextWindowTitle;
        IsVisible = nextIsVisible;
        IsMinimized = nextIsMinimized;
        IsResponsive = nextIsResponsive;
        IsCycleEligible = nextIsCycleEligible;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}