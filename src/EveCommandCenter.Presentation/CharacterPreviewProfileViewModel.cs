using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EveCommandCenter.Presentation;

public sealed record CharacterPreviewProfileSnapshot(
    string CharacterName,
    string CustomLabel,
    PreviewContentMode ContentMode,
    double? Left,
    double? Top,
    double? Width,
    double? Height);

public sealed class CharacterPreviewProfileViewModel : INotifyPropertyChanged
{
    private string customLabel;
    private PreviewContentMode contentMode;
    private double? left;
    private double? top;
    private double? width;
    private double? height;
    private bool isDetected;

    public CharacterPreviewProfileViewModel(CharacterPreviewProfileSnapshot snapshot)
    {
        CharacterName = snapshot.CharacterName.Trim();
        customLabel = snapshot.CustomLabel.Trim();
        contentMode = snapshot.ContentMode;
        left = snapshot.Left;
        top = snapshot.Top;
        width = snapshot.Width;
        height = snapshot.Height;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CharacterName { get; }

    public string CustomLabel
    {
        get => customLabel;
        set => SetField(ref customLabel, value.Trim());
    }

    public PreviewContentMode ContentMode
    {
        get => contentMode;
        set => SetField(ref contentMode, value);
    }

    public double? Left => left;

    public double? Top => top;

    public double? Width => width;

    public double? Height => height;

    public bool HasSavedBounds =>
        Left.HasValue &&
        Top.HasValue &&
        Width is > 0 &&
        Height is > 0;

    public bool IsDetected
    {
        get => isDetected;
        internal set => SetField(ref isDetected, value);
    }

    public void UpdateLayout(double nextLeft, double nextTop, double nextWidth, double nextHeight)
    {
        bool changed = false;
        changed |= SetField(ref left, nextLeft, nameof(Left), notify: false);
        changed |= SetField(ref top, nextTop, nameof(Top), notify: false);
        changed |= SetField(ref width, nextWidth, nameof(Width), notify: false);
        changed |= SetField(ref height, nextHeight, nameof(Height), notify: false);

        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(HasSavedBounds));
    }

    public CharacterPreviewProfileSnapshot CreateSnapshot() =>
        new(
            CharacterName,
            CustomLabel,
            ContentMode,
            Left,
            Top,
            Width,
            Height);

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null,
        bool notify = true)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        if (notify)
        {
            OnPropertyChanged(propertyName);
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
