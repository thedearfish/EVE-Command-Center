using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public static class PreviewGroupGapBehavior
{
    private const double VisualJoinOverlap = 1.0;
    private const double GeometryTolerance = 0.75;
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Dictionary<FloatingPreviewWindow, WindowEntry> Entries = [];
    private static readonly Dictionary<FloatingPreviewWindow, GroupTopology> ActiveTopologies = [];
    private static readonly HashSet<FloatingPreviewWindow> PendingRealignments = [];
    private static readonly HashSet<FloatingPreviewWindow> InternalUpdates = [];

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(PreviewGroupGapBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static string StorePath => Path.Combine(AppContext.BaseDirectory, "preview-groups.json");

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FloatingPreviewWindow window)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            window.Loaded += OnWindowLoaded;
            window.Closed += OnWindowClosed;
            window.SizeChanged += OnWindowSizeChanged;
            window.LayoutCommitted += OnWindowLayoutCommitted;
            window.AddHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown),
                handledEventsToo: true);
            window.AddHandler(
                UIElement.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(OnPreviewMouseLeftButtonUp),
                handledEventsToo: true);
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window || Entries.ContainsKey(window))
        {
            return;
        }

        string characterName = window.DataContext is DetectedClientViewModel client
            ? client.DisplayName.Trim()
            : window.Title.Trim();
        Entries[window] = new WindowEntry(window, characterName);
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FloatingPreviewWindow window)
        {
            return;
        }

        window.Loaded -= OnWindowLoaded;
        window.Closed -= OnWindowClosed;
        window.SizeChanged -= OnWindowSizeChanged;
        window.LayoutCommitted -= OnWindowLayoutCommitted;
        Entries.Remove(window);
        ActiveTopologies.Remove(window);
        PendingRealignments.Remove(window);
        InternalUpdates.Remove(window);
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            e.ChangedButton != MouseButton.Left ||
            !Entries.TryGetValue(window, out WindowEntry? primary))
        {
            return;
        }

        GroupTopology? topology = CaptureTopology(primary);
        if (topology is not null)
        {
            ActiveTopologies[window] = topology;
        }
    }

    private static void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            InternalUpdates.Contains(window) ||
            !Entries.TryGetValue(window, out WindowEntry? primary))
        {
            return;
        }

        if (!ActiveTopologies.TryGetValue(window, out GroupTopology? topology))
        {
            topology = CaptureTopology(primary);
            if (topology is null)
            {
                return;
            }

            ActiveTopologies[window] = topology;
        }

        QueueRealignment(window, topology, persist: false);
    }

    private static void OnWindowLayoutCommitted(object? sender, PreviewWindowLayoutChangedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            !Entries.TryGetValue(window, out WindowEntry? primary))
        {
            return;
        }

        if (!ActiveTopologies.TryGetValue(window, out GroupTopology? topology))
        {
            topology = CaptureTopology(primary);
        }

        if (topology is not null)
        {
            QueueRealignment(window, topology, persist: true);
        }
    }

    private static void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            e.ChangedButton != MouseButton.Left ||
            !Entries.TryGetValue(window, out WindowEntry? primary))
        {
            return;
        }

        GroupTopology? topology = ActiveTopologies.TryGetValue(window, out GroupTopology? existing)
            ? existing
            : CaptureTopology(primary);

        if (topology is not null)
        {
            QueueRealignment(window, topology, persist: true);
        }

        ActiveTopologies.Remove(window);
    }

    private static void QueueRealignment(
        FloatingPreviewWindow primaryWindow,
        GroupTopology topology,
        bool persist)
    {
        if (!PendingRealignments.Add(primaryWindow))
        {
            if (persist)
            {
                topology.PersistAfterRealignment = true;
            }

            return;
        }

        if (persist)
        {
            topology.PersistAfterRealignment = true;
        }

        _ = primaryWindow.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                PendingRealignments.Remove(primaryWindow);
                if (!primaryWindow.IsLoaded || !Entries.ContainsKey(primaryWindow))
                {
                    return;
                }

                RealignGroup(topology, topology.PersistAfterRealignment);
                topology.PersistAfterRealignment = false;
            }));
    }

    private static GroupTopology? CaptureTopology(WindowEntry primary)
    {
        PersistedFile? store = LoadStore();
        PersistedWindowState? primaryState = store?.Windows.FirstOrDefault(state =>
            NameComparer.Equals(state.CharacterName, primary.CharacterName));
        if (primaryState is null || string.IsNullOrWhiteSpace(primaryState.GroupId))
        {
            return null;
        }

        WindowEntry[] members = Entries.Values
            .Where(entry =>
            {
                PersistedWindowState? state = store!.Windows.FirstOrDefault(candidate =>
                    NameComparer.Equals(candidate.CharacterName, entry.CharacterName));
                return state is not null && NameComparer.Equals(state.GroupId, primaryState.GroupId);
            })
            .ToArray();
        if (members.Length < 2)
        {
            return null;
        }

        Rect primaryBounds = CaptureBounds(primary.Window);
        double stepX = Math.Max(1.0, primaryBounds.Width - VisualJoinOverlap);
        double stepY = Math.Max(1.0, primaryBounds.Height - VisualJoinOverlap);
        var offsets = new Dictionary<string, GridOffset>(NameComparer);

        foreach (WindowEntry member in members)
        {
            Rect memberBounds = CaptureBounds(member.Window);
            double deltaX = memberBounds.Left - primaryBounds.Left;
            double deltaY = memberBounds.Top - primaryBounds.Top;
            int column = QuantizeOffset(deltaX, stepX);
            int row = QuantizeOffset(deltaY, stepY);

            if (!ReferenceEquals(member, primary) && column == 0 && row == 0)
            {
                if (Math.Abs(deltaX) >= Math.Abs(deltaY))
                {
                    column = deltaX >= 0 ? 1 : -1;
                }
                else
                {
                    row = deltaY >= 0 ? 1 : -1;
                }
            }

            offsets[member.CharacterName] = new GridOffset(column, row);
        }

        return new GroupTopology(primary, primaryState.GroupId, offsets);
    }

    private static void RealignGroup(GroupTopology topology, bool persist)
    {
        if (!Entries.ContainsKey(topology.Primary.Window))
        {
            return;
        }

        Rect primaryBounds = CaptureBounds(topology.Primary.Window);
        double stepX = Math.Max(1.0, primaryBounds.Width - VisualJoinOverlap);
        double stepY = Math.Max(1.0, primaryBounds.Height - VisualJoinOverlap);
        var touched = new List<WindowEntry>();

        foreach ((string characterName, GridOffset offset) in topology.Offsets)
        {
            WindowEntry? member = Entries.Values.FirstOrDefault(entry =>
                NameComparer.Equals(entry.CharacterName, characterName));
            if (member is null)
            {
                continue;
            }

            double targetLeft = primaryBounds.Left + (offset.Column * stepX);
            double targetTop = primaryBounds.Top + (offset.Row * stepY);
            Rect current = CaptureBounds(member.Window);
            bool needsPosition =
                Math.Abs(current.Left - targetLeft) > GeometryTolerance ||
                Math.Abs(current.Top - targetTop) > GeometryTolerance;
            bool needsSize =
                Math.Abs(current.Width - primaryBounds.Width) > GeometryTolerance ||
                Math.Abs(current.Height - primaryBounds.Height) > GeometryTolerance;

            if (!needsPosition && !needsSize)
            {
                touched.Add(member);
                continue;
            }

            InternalUpdates.Add(member.Window);
            try
            {
                member.Window.ApplyBounds(
                    targetLeft,
                    targetTop,
                    primaryBounds.Width,
                    primaryBounds.Height);
            }
            finally
            {
                InternalUpdates.Remove(member.Window);
            }

            touched.Add(member);
        }

        if (persist)
        {
            PersistCorrectedBounds(topology.GroupId, touched);
        }

        AppLog.Debug(
            "Grouping",
            $"Group {topology.GroupId} realigned to a fixed 1 px join after resize/move.");
    }

    private static void PersistCorrectedBounds(string groupId, IReadOnlyCollection<WindowEntry> entries)
    {
        PersistedFile store = LoadStore() ?? new PersistedFile();
        var states = store.Windows.ToDictionary(
            state => state.CharacterName,
            state => state,
            NameComparer);

        foreach (WindowEntry entry in entries)
        {
            Rect bounds = CaptureBounds(entry.Window);
            if (!states.TryGetValue(entry.CharacterName, out PersistedWindowState? state))
            {
                state = new PersistedWindowState
                {
                    CharacterName = entry.CharacterName,
                    GroupId = groupId,
                };
                states[entry.CharacterName] = state;
            }

            state.GroupId = groupId;
            state.Left = bounds.Left;
            state.Top = bounds.Top;
            state.Width = bounds.Width;
            state.Height = bounds.Height;
        }

        store.Windows = states.Values
            .OrderBy(state => state.CharacterName, NameComparer)
            .ToArray();
        SaveStore(store);
    }

    private static PersistedFile? LoadStore()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PersistedFile>(
                File.ReadAllText(StorePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception exception)
        {
            AppLog.Warning("Grouping", "Could not read preview-groups.json for gap correction.", exception);
            return null;
        }
    }

    private static void SaveStore(PersistedFile store)
    {
        try
        {
            string temporaryPath = StorePath + ".gap.tmp";
            string json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, StorePath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Warning("Grouping", "Could not persist corrected grouped-preview geometry.", exception);
        }
    }

    private static int QuantizeOffset(double delta, double step)
    {
        if (Math.Abs(delta) <= GeometryTolerance)
        {
            return 0;
        }

        int value = (int)Math.Round(delta / step, MidpointRounding.AwayFromZero);
        return value == 0 ? (delta > 0 ? 1 : -1) : value;
    }

    private static Rect CaptureBounds(FloatingPreviewWindow window) =>
        new(
            window.Left,
            window.Top,
            window.ActualWidth > 0 ? window.ActualWidth : window.Width,
            window.ActualHeight > 0 ? window.ActualHeight : window.Height);

    private sealed record WindowEntry(FloatingPreviewWindow Window, string CharacterName);

    private sealed class GroupTopology(
        WindowEntry primary,
        string groupId,
        Dictionary<string, GridOffset> offsets)
    {
        public WindowEntry Primary { get; } = primary;
        public string GroupId { get; } = groupId;
        public Dictionary<string, GridOffset> Offsets { get; } = offsets;
        public bool PersistAfterRealignment { get; set; }
    }

    private readonly record struct GridOffset(int Column, int Row);

    private sealed class PersistedFile
    {
        public PersistedWindowState[] Windows { get; set; } = [];
    }

    private sealed class PersistedWindowState
    {
        public string CharacterName { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
