using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EveCommandCenter.Application.Diagnostics;

namespace EveCommandCenter.Presentation;

public static class PreviewGroupingBehavior
{
    private const double SnapDistance = 16.0;
    private const double MinimumOverlap = 24.0;
    private static readonly TimeSpan SnapHoldDuration = TimeSpan.FromSeconds(1);
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Dictionary<FloatingPreviewWindow, WindowEntry> Entries = [];
    private static readonly HashSet<FloatingPreviewWindow> SuppressedLocationEvents = [];
    private static readonly Dictionary<string, PersistedWindowState> Persisted =
        new(NameComparer);
    private static readonly DispatcherTimer HoldTimer = new(DispatcherPriority.Input)
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };

    private static MoveSession? activeMove;
    private static bool storeLoaded;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(PreviewGroupingBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    static PreviewGroupingBehavior()
    {
        HoldTimer.Tick += OnHoldTimerTick;
    }

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
            window.LocationChanged += OnWindowLocationChanged;
            window.AddHandler(
                UIElement.PreviewMouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnWindowMouseLeftButtonDown),
                handledEventsToo: true);
            window.AddHandler(
                UIElement.PreviewMouseLeftButtonUpEvent,
                new MouseButtonEventHandler(OnWindowMouseLeftButtonUp),
                handledEventsToo: true);
        }
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window || Entries.ContainsKey(window))
        {
            return;
        }

        EnsureStoreLoaded();
        string characterName = window.DataContext is DetectedClientViewModel client
            ? client.DisplayName.Trim()
            : window.Title.Trim();

        var entry = new WindowEntry(window, characterName);
        Entries.Add(window, entry);
        InstallContextMenu(entry);

        if (Persisted.TryGetValue(characterName, out PersistedWindowState? saved) &&
            saved.Width >= window.MinWidth && saved.Height >= window.MinHeight)
        {
            window.ApplyBounds(saved.Left, saved.Top, saved.Width, saved.Height);
        }

        UpdateContextMenus();
        if (!HoldTimer.IsEnabled)
        {
            HoldTimer.Start();
        }

        AppLog.Information("Grouping", $"Preview grouping registered for {characterName}.");
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FloatingPreviewWindow window)
        {
            return;
        }

        Entries.Remove(window);
        SuppressedLocationEvents.Remove(window);
        if (activeMove?.Primary.Window == window)
        {
            activeMove = null;
        }

        if (Entries.Count == 0)
        {
            HoldTimer.Stop();
        }
    }

    private static void OnWindowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            e.ChangedButton != MouseButton.Left ||
            !Entries.TryGetValue(window, out WindowEntry? primary))
        {
            return;
        }

        string groupId = GetGroupId(primary.CharacterName);
        WindowEntry[] members = string.IsNullOrWhiteSpace(groupId)
            ? [primary]
            : Entries.Values
                .Where(entry => NameComparer.Equals(GetGroupId(entry.CharacterName), groupId))
                .ToArray();

        if (members.Length == 0)
        {
            members = [primary];
        }

        var startingBounds = members.ToDictionary(
            entry => entry,
            entry => CaptureBounds(entry.Window));

        activeMove = new MoveSession(
            primary,
            members,
            startingBounds,
            startingBounds[primary]);
    }

    private static void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            SuppressedLocationEvents.Contains(window) ||
            activeMove is null ||
            activeMove.Primary.Window != window ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Rect current = CaptureBounds(window);
        if (Math.Abs(current.Width - activeMove.PrimaryStart.Width) > 0.5 ||
            Math.Abs(current.Height - activeMove.PrimaryStart.Height) > 0.5)
        {
            activeMove.IsResize = true;
            return;
        }

        activeMove.IsMoving = true;
        double deltaX = current.Left - activeMove.PrimaryStart.Left;
        double deltaY = current.Top - activeMove.PrimaryStart.Top;

        foreach (WindowEntry member in activeMove.Members)
        {
            if (ReferenceEquals(member, activeMove.Primary))
            {
                continue;
            }

            Rect start = activeMove.StartingBounds[member];
            ApplyBoundsSuppressed(member.Window, new Rect(
                start.Left + deltaX,
                start.Top + deltaY,
                start.Width,
                start.Height));
        }

        UpdateSnapCandidate(activeMove);
    }

    private static void OnWindowMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            e.ChangedButton != MouseButton.Left ||
            activeMove is null ||
            activeMove.Primary.Window != window)
        {
            return;
        }

        MoveSession session = activeMove;
        activeMove = null;

        if (session.IsResize || !session.IsMoving)
        {
            return;
        }

        UpdateSnapCandidate(session);
        if (session.ArmedCandidate is not null &&
            session.CurrentCandidate is not null &&
            session.ArmedCandidate.Key == session.CurrentCandidate.Key)
        {
            ApplySnapAndMerge(session, session.CurrentCandidate);
        }

        PersistOpenPositions(session.Members);
        SaveStore();
        UpdateContextMenus();
    }

    private static void OnHoldTimerTick(object? sender, EventArgs e)
    {
        if (activeMove is null ||
            !activeMove.IsMoving ||
            activeMove.IsResize ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateSnapCandidate(activeMove);
        if (activeMove.CurrentCandidate is not null &&
            DateTimeOffset.UtcNow - activeMove.CandidateSince >= SnapHoldDuration &&
            (activeMove.ArmedCandidate is null ||
             activeMove.ArmedCandidate.Key != activeMove.CurrentCandidate.Key))
        {
            activeMove.ArmedCandidate = activeMove.CurrentCandidate;
            AppLog.Information(
                "Grouping",
                $"Snap armed for {activeMove.Primary.CharacterName} next to " +
                $"{activeMove.CurrentCandidate.Target.CharacterName}.");
        }
    }

    private static void UpdateSnapCandidate(MoveSession session)
    {
        SnapCandidate? candidate = FindBestCandidate(session);
        if (candidate is null)
        {
            session.CurrentCandidate = null;
            session.ArmedCandidate = null;
            session.CandidateSince = DateTimeOffset.MinValue;
            return;
        }

        if (session.CurrentCandidate is null || session.CurrentCandidate.Key != candidate.Key)
        {
            session.CurrentCandidate = candidate;
            session.ArmedCandidate = null;
            session.CandidateSince = DateTimeOffset.UtcNow;
            return;
        }

        session.CurrentCandidate = candidate;
    }

    private static SnapCandidate? FindBestCandidate(MoveSession session)
    {
        Rect movingBounds = UnionBounds(session.Members.Select(member => CaptureBounds(member.Window)));
        HashSet<FloatingPreviewWindow> movingWindows = session.Members
            .Select(member => member.Window)
            .ToHashSet();
        SnapCandidate? best = null;

        foreach (WindowEntry target in Entries.Values)
        {
            if (movingWindows.Contains(target.Window) || !target.Window.IsVisible)
            {
                continue;
            }

            Rect targetBounds = CaptureBounds(target.Window);
            double verticalOverlap = Overlap(
                movingBounds.Top,
                movingBounds.Bottom,
                targetBounds.Top,
                targetBounds.Bottom);
            double horizontalOverlap = Overlap(
                movingBounds.Left,
                movingBounds.Right,
                targetBounds.Left,
                targetBounds.Right);

            if (verticalOverlap >= MinimumOverlap)
            {
                ConsiderCandidate(ref best, target, "right-to-left", targetBounds.Left - movingBounds.Right, 0);
                ConsiderCandidate(ref best, target, "left-to-right", targetBounds.Right - movingBounds.Left, 0);
            }

            if (horizontalOverlap >= MinimumOverlap)
            {
                ConsiderCandidate(ref best, target, "bottom-to-top", 0, targetBounds.Top - movingBounds.Bottom);
                ConsiderCandidate(ref best, target, "top-to-bottom", 0, targetBounds.Bottom - movingBounds.Top);
            }
        }

        return best;
    }

    private static void ConsiderCandidate(
        ref SnapCandidate? best,
        WindowEntry target,
        string side,
        double deltaX,
        double deltaY)
    {
        double distance = Math.Abs(deltaX) + Math.Abs(deltaY);
        if (distance > SnapDistance || (best is not null && distance >= best.Distance))
        {
            return;
        }

        best = new SnapCandidate(
            $"{target.CharacterName}|{side}",
            target,
            deltaX,
            deltaY,
            distance);
    }

    private static void ApplySnapAndMerge(MoveSession session, SnapCandidate candidate)
    {
        foreach (WindowEntry member in session.Members)
        {
            Rect current = CaptureBounds(member.Window);
            ApplyBoundsSuppressed(member.Window, new Rect(
                current.Left + candidate.DeltaX,
                current.Top + candidate.DeltaY,
                current.Width,
                current.Height));
        }

        string movingGroup = GetGroupId(session.Primary.CharacterName);
        string targetGroup = GetGroupId(candidate.Target.CharacterName);
        string newGroup = !string.IsNullOrWhiteSpace(targetGroup)
            ? targetGroup
            : !string.IsNullOrWhiteSpace(movingGroup)
                ? movingGroup
                : Guid.NewGuid().ToString("N");

        if (!string.IsNullOrWhiteSpace(movingGroup) && !NameComparer.Equals(movingGroup, newGroup))
        {
            foreach (PersistedWindowState state in Persisted.Values.Where(state =>
                         NameComparer.Equals(state.GroupId, movingGroup)))
            {
                state.GroupId = newGroup;
            }
        }

        foreach (WindowEntry member in session.Members)
        {
            GetOrCreateState(member.CharacterName).GroupId = newGroup;
        }

        string candidateGroup = GetGroupId(candidate.Target.CharacterName);
        if (string.IsNullOrWhiteSpace(candidateGroup))
        {
            GetOrCreateState(candidate.Target.CharacterName).GroupId = newGroup;
        }
        else
        {
            foreach (PersistedWindowState state in Persisted.Values.Where(state =>
                         NameComparer.Equals(state.GroupId, candidateGroup)))
            {
                state.GroupId = newGroup;
            }
        }

        PersistOpenPositions(session.Members);
        PersistOpenPositions(Entries.Values.Where(entry =>
            NameComparer.Equals(GetGroupId(entry.CharacterName), newGroup)));

        AppLog.Information(
            "Grouping",
            $"Preview group created/merged: {session.Primary.CharacterName} + " +
            $"{candidate.Target.CharacterName}; group={newGroup}.");
    }

    private static void InstallContextMenu(WindowEntry entry)
    {
        var detach = new MenuItem { Header = "Detach this preview" };
        var ungroup = new MenuItem { Header = "Ungroup entire group" };

        detach.Click += (_, _) => Detach(entry);
        ungroup.Click += (_, _) => Ungroup(entry);

        var menu = new ContextMenu();
        menu.Items.Add(detach);
        menu.Items.Add(ungroup);
        menu.Opened += (_, _) =>
        {
            bool grouped = IsGrouped(entry.CharacterName);
            detach.IsEnabled = grouped;
            ungroup.IsEnabled = grouped;
        };
        entry.Window.ContextMenu = menu;
    }

    private static void Detach(WindowEntry entry)
    {
        string groupId = GetGroupId(entry.CharacterName);
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return;
        }

        GetOrCreateState(entry.CharacterName).GroupId = string.Empty;
        PersistedWindowState[] remaining = Persisted.Values
            .Where(state => NameComparer.Equals(state.GroupId, groupId))
            .ToArray();
        if (remaining.Length <= 1)
        {
            foreach (PersistedWindowState state in remaining)
            {
                state.GroupId = string.Empty;
            }
        }

        SaveStore();
        UpdateContextMenus();
        AppLog.Information("Grouping", $"{entry.CharacterName} detached from its preview group.");
    }

    private static void Ungroup(WindowEntry entry)
    {
        string groupId = GetGroupId(entry.CharacterName);
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return;
        }

        foreach (PersistedWindowState state in Persisted.Values.Where(state =>
                     NameComparer.Equals(state.GroupId, groupId)))
        {
            state.GroupId = string.Empty;
        }

        SaveStore();
        UpdateContextMenus();
        AppLog.Information("Grouping", $"Preview group {groupId} was dissolved.");
    }

    private static void UpdateContextMenus()
    {
        foreach (WindowEntry entry in Entries.Values)
        {
            if (entry.Window.ContextMenu is null)
            {
                continue;
            }

            bool grouped = IsGrouped(entry.CharacterName);
            foreach (MenuItem item in entry.Window.ContextMenu.Items.OfType<MenuItem>())
            {
                item.IsEnabled = grouped;
            }
        }
    }

    private static bool IsGrouped(string characterName)
    {
        string groupId = GetGroupId(characterName);
        return !string.IsNullOrWhiteSpace(groupId) &&
               Persisted.Values.Count(state => NameComparer.Equals(state.GroupId, groupId)) >= 2;
    }

    private static string GetGroupId(string characterName) =>
        Persisted.TryGetValue(characterName, out PersistedWindowState? state)
            ? state.GroupId
            : string.Empty;

    private static void PersistOpenPositions(IEnumerable<WindowEntry> entries)
    {
        foreach (WindowEntry entry in entries.Distinct())
        {
            Rect bounds = CaptureBounds(entry.Window);
            PersistedWindowState state = GetOrCreateState(entry.CharacterName);
            state.Left = bounds.Left;
            state.Top = bounds.Top;
            state.Width = bounds.Width;
            state.Height = bounds.Height;
        }
    }

    private static PersistedWindowState GetOrCreateState(string characterName)
    {
        if (Persisted.TryGetValue(characterName, out PersistedWindowState? state))
        {
            return state;
        }

        state = new PersistedWindowState { CharacterName = characterName };
        Persisted[characterName] = state;
        return state;
    }

    private static void EnsureStoreLoaded()
    {
        if (storeLoaded)
        {
            return;
        }

        storeLoaded = true;
        try
        {
            if (!File.Exists(StorePath))
            {
                return;
            }

            PersistedFile? file = JsonSerializer.Deserialize<PersistedFile>(
                File.ReadAllText(StorePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (file?.Windows is null)
            {
                return;
            }

            foreach (PersistedWindowState state in file.Windows.Where(state =>
                         !string.IsNullOrWhiteSpace(state.CharacterName)))
            {
                Persisted[state.CharacterName.Trim()] = state;
            }
        }
        catch (Exception exception)
        {
            AppLog.Warning("Grouping", "Could not load preview-groups.json; defaults will be used.", exception);
        }
    }

    private static void SaveStore()
    {
        try
        {
            var file = new PersistedFile
            {
                Windows = Persisted.Values
                    .OrderBy(state => state.CharacterName, NameComparer)
                    .ToArray(),
            };
            string temporaryPath = StorePath + ".tmp";
            string json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, StorePath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Warning("Grouping", "Could not save preview-groups.json.", exception);
        }
    }

    private static void ApplyBoundsSuppressed(FloatingPreviewWindow window, Rect bounds)
    {
        SuppressedLocationEvents.Add(window);
        try
        {
            window.ApplyBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
        finally
        {
            SuppressedLocationEvents.Remove(window);
        }
    }

    private static Rect CaptureBounds(FloatingPreviewWindow window) =>
        new(
            window.Left,
            window.Top,
            window.ActualWidth > 0 ? window.ActualWidth : window.Width,
            window.ActualHeight > 0 ? window.ActualHeight : window.Height);

    private static Rect UnionBounds(IEnumerable<Rect> rectangles)
    {
        Rect result = Rect.Empty;
        foreach (Rect rectangle in rectangles)
        {
            result = result.IsEmpty ? rectangle : Rect.Union(result, rectangle);
        }

        return result;
    }

    private static double Overlap(double firstStart, double firstEnd, double secondStart, double secondEnd) =>
        Math.Max(0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));

    private sealed record WindowEntry(FloatingPreviewWindow Window, string CharacterName);

    private sealed class MoveSession(
        WindowEntry primary,
        WindowEntry[] members,
        Dictionary<WindowEntry, Rect> startingBounds,
        Rect primaryStart)
    {
        public WindowEntry Primary { get; } = primary;
        public WindowEntry[] Members { get; } = members;
        public Dictionary<WindowEntry, Rect> StartingBounds { get; } = startingBounds;
        public Rect PrimaryStart { get; } = primaryStart;
        public bool IsMoving { get; set; }
        public bool IsResize { get; set; }
        public SnapCandidate? CurrentCandidate { get; set; }
        public SnapCandidate? ArmedCandidate { get; set; }
        public DateTimeOffset CandidateSince { get; set; }
    }

    private sealed record SnapCandidate(
        string Key,
        WindowEntry Target,
        double DeltaX,
        double DeltaY,
        double Distance);

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
