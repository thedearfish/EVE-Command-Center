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
    private const int StoreVersion = 2;
    private const double SnapDistance = 28.0;
    private const double MinimumOverlap = 8.0;
    private const double VisualJoinOverlap = 1.0;
    private const double GeometryTolerance = 0.75;

    private static readonly TimeSpan SnapHoldDuration = TimeSpan.FromSeconds(1);
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Dictionary<FloatingPreviewWindow, WindowEntry> Entries = [];
    private static readonly HashSet<FloatingPreviewWindow> SuppressedWindowEvents = [];
    private static readonly DispatcherTimer HoldTimer = new(DispatcherPriority.Input)
    {
        Interval = TimeSpan.FromMilliseconds(100),
    };

    private static PersistedFile store = new();
    private static InteractionSession? activeSession;
    private static bool storeLoaded;
    private static bool restoreQueued;

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
        if (dependencyObject is not FloatingPreviewWindow window || !(bool)e.NewValue)
        {
            return;
        }

        window.Loaded += OnWindowLoaded;
        window.Closed += OnWindowClosed;
        window.LocationChanged += OnWindowLocationChanged;
        window.SizeChanged += OnWindowSizeChanged;
        window.LayoutCommitted += OnWindowLayoutCommitted;
        window.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnWindowMouseLeftButtonDown),
            handledEventsToo: true);
        window.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnWindowMouseLeftButtonUp),
            handledEventsToo: true);
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

        PersistedWindowState? saved = FindState(characterName);
        if (saved is not null && IsUsableBounds(saved.ToRect(), window))
        {
            ApplyBoundsSuppressed(window, saved.ToRect());
        }

        UpdateContextMenus();
        QueueRestoreGroups();

        if (!HoldTimer.IsEnabled)
        {
            HoldTimer.Start();
        }

        AppLog.Information("Grouping", $"Dock workspace registered for {characterName}.");
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not FloatingPreviewWindow window)
        {
            return;
        }

        window.Loaded -= OnWindowLoaded;
        window.Closed -= OnWindowClosed;
        window.LocationChanged -= OnWindowLocationChanged;
        window.SizeChanged -= OnWindowSizeChanged;
        window.LayoutCommitted -= OnWindowLayoutCommitted;

        Entries.Remove(window);
        SuppressedWindowEvents.Remove(window);
        if (activeSession?.Primary.Window == window)
        {
            activeSession = null;
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

        activeSession = CreateSession(primary);
        AppLog.Debug(
            "Grouping",
            $"Dock interaction started for {primary.CharacterName}; members={activeSession.Members.Length}.");
    }

    private static InteractionSession CreateSession(WindowEntry primary, Size? previousPrimarySize = null)
    {
        WindowEntry[] members = GetOpenComponent(primary);
        var startingBounds = members.ToDictionary(
            entry => entry,
            entry => CaptureBounds(entry.Window));

        Rect primaryStart = startingBounds[primary];
        if (previousPrimarySize is { Width: > 0, Height: > 0 } previous)
        {
            primaryStart = new Rect(primaryStart.Left, primaryStart.Top, previous.Width, previous.Height);
            startingBounds[primary] = primaryStart;
        }

        Dictionary<string, GridOffset> offsets = BuildGridOffsets(primary.CharacterName);
        foreach (WindowEntry member in members)
        {
            offsets.TryAdd(member.CharacterName, EstimateOffset(primaryStart, startingBounds[member]));
        }

        return new InteractionSession(primary, members, startingBounds, offsets, primaryStart);
    }

    private static void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            SuppressedWindowEvents.Contains(window) ||
            activeSession is null ||
            activeSession.Primary.Window != window ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Rect current = CaptureBounds(window);
        if (SizeChanged(activeSession.PrimaryStart, current))
        {
            activeSession.IsResize = true;
            ApplyUniformGroupLayout(activeSession, current);
            return;
        }

        if (!PositionChanged(activeSession.PrimaryStart, current))
        {
            return;
        }

        activeSession.IsMoving = true;
        MoveComponent(activeSession, current.Left - activeSession.PrimaryStart.Left, current.Top - activeSession.PrimaryStart.Top);
        UpdateSnapCandidate(activeSession);
    }

    private static void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            SuppressedWindowEvents.Contains(window) ||
            !Entries.TryGetValue(window, out WindowEntry? primary) ||
            GetComponentNames(primary.CharacterName).Count < 2)
        {
            return;
        }

        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) <= GeometryTolerance &&
            Math.Abs(e.NewSize.Height - e.PreviousSize.Height) <= GeometryTolerance)
        {
            return;
        }

        if (activeSession is null || activeSession.Primary.Window != window)
        {
            activeSession = CreateSession(primary, e.PreviousSize);
        }

        activeSession.IsResize = true;
        ApplyUniformGroupLayout(activeSession, CaptureBounds(window));
    }

    private static void OnWindowLayoutCommitted(object? sender, PreviewWindowLayoutChangedEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            SuppressedWindowEvents.Contains(window) ||
            !Entries.TryGetValue(window, out WindowEntry? primary))
        {
            return;
        }

        InteractionSession? session = activeSession?.Primary.Window == window
            ? activeSession
            : null;

        if (GetComponentNames(primary.CharacterName).Count >= 2)
        {
            session ??= CreateSession(primary);
            if (session.IsResize ||
                Math.Abs(e.Width - session.PrimaryStart.Width) > GeometryTolerance ||
                Math.Abs(e.Height - session.PrimaryStart.Height) > GeometryTolerance)
            {
                ApplyUniformGroupLayout(session, new Rect(e.Left, e.Top, e.Width, e.Height));
            }

            PersistOpenPositions(session.Members);
        }
        else
        {
            PersistOpenPositions([primary]);
        }

        SaveStore();
    }

    private static void OnWindowMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FloatingPreviewWindow window ||
            e.ChangedButton != MouseButton.Left ||
            activeSession is null ||
            activeSession.Primary.Window != window)
        {
            return;
        }

        InteractionSession session = activeSession;
        activeSession = null;
        Rect finalBounds = CaptureBounds(window);
        bool resized = session.IsResize || SizeChanged(session.PrimaryStart, finalBounds);
        bool moved = session.IsMoving || PositionChanged(session.PrimaryStart, finalBounds);

        if (resized)
        {
            ApplyUniformGroupLayout(session, finalBounds);
            PersistOpenPositions(session.Members);
            SaveStore();
            AppLog.Information(
                "Grouping",
                $"Dock group resized from {session.Primary.CharacterName}; members={session.Members.Length}.");
            return;
        }

        if (!moved)
        {
            return;
        }

        UpdateSnapCandidate(session);
        if (session.ArmedCandidate is not null &&
            session.CurrentCandidate is not null &&
            string.Equals(session.ArmedCandidate.Key, session.CurrentCandidate.Key, StringComparison.Ordinal))
        {
            ApplySnapAndLink(session, session.CurrentCandidate);
        }

        WindowEntry[] finalMembers = GetOpenComponent(session.Primary);
        PersistOpenPositions(finalMembers);
        SaveStore();
        UpdateContextMenus();
    }

    private static void OnHoldTimerTick(object? sender, EventArgs e)
    {
        if (activeSession is null)
        {
            return;
        }

        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            activeSession = null;
            return;
        }

        if (!activeSession.IsMoving || activeSession.IsResize)
        {
            return;
        }

        UpdateSnapCandidate(activeSession);
        if (activeSession.CurrentCandidate is not null &&
            DateTimeOffset.UtcNow - activeSession.CandidateSince >= SnapHoldDuration &&
            (activeSession.ArmedCandidate is null ||
             activeSession.ArmedCandidate.Key != activeSession.CurrentCandidate.Key))
        {
            activeSession.ArmedCandidate = activeSession.CurrentCandidate;
            AppLog.Information(
                "Grouping",
                $"Dock armed: {activeSession.CurrentCandidate.Moving.CharacterName} " +
                $"{activeSession.CurrentCandidate.Side} of {activeSession.CurrentCandidate.Target.CharacterName}.");
        }
    }

    private static void MoveComponent(InteractionSession session, double deltaX, double deltaY)
    {
        var proposed = session.Members.ToDictionary(
            member => member,
            member =>
            {
                Rect start = session.StartingBounds[member];
                return new Rect(start.Left + deltaX, start.Top + deltaY, start.Width, start.Height);
            });

        ShiftBoundsIntoVirtualScreen(proposed);
        ApplyBounds(proposed);
    }

    private static void ApplyUniformGroupLayout(InteractionSession session, Rect requestedPrimaryBounds)
    {
        if (session.Members.Length < 2)
        {
            return;
        }

        Size constrainedSize = ConstrainUniformSizeToVirtualScreen(
            session,
            new Size(requestedPrimaryBounds.Width, requestedPrimaryBounds.Height));
        double stepX = Math.Max(1.0, constrainedSize.Width - VisualJoinOverlap);
        double stepY = Math.Max(1.0, constrainedSize.Height - VisualJoinOverlap);

        var proposed = new Dictionary<WindowEntry, Rect>();
        foreach (WindowEntry member in session.Members)
        {
            GridOffset offset = session.GridOffsets.GetValueOrDefault(member.CharacterName);
            proposed[member] = new Rect(
                requestedPrimaryBounds.Left + (offset.Column * stepX),
                requestedPrimaryBounds.Top + (offset.Row * stepY),
                constrainedSize.Width,
                constrainedSize.Height);
        }

        ShiftBoundsIntoVirtualScreen(proposed);
        ApplyBounds(proposed);
    }

    private static Size ConstrainUniformSizeToVirtualScreen(InteractionSession session, Size requested)
    {
        Rect screen = GetVirtualScreen();
        int minColumn = session.GridOffsets.Values.Min(offset => offset.Column);
        int maxColumn = session.GridOffsets.Values.Max(offset => offset.Column);
        int minRow = session.GridOffsets.Values.Min(offset => offset.Row);
        int maxRow = session.GridOffsets.Values.Max(offset => offset.Row);
        int columnSpan = Math.Max(0, maxColumn - minColumn);
        int rowSpan = Math.Max(0, maxRow - minRow);

        double maximumWidth = (screen.Width + (columnSpan * VisualJoinOverlap)) / (columnSpan + 1.0);
        double maximumHeight = (screen.Height + (rowSpan * VisualJoinOverlap)) / (rowSpan + 1.0);
        double minimumWidth = session.Members.Max(member => member.Window.MinWidth);
        double minimumHeight = session.Members.Max(member => member.Window.MinHeight);

        return new Size(
            Math.Max(minimumWidth, Math.Min(requested.Width, maximumWidth)),
            Math.Max(minimumHeight, Math.Min(requested.Height, maximumHeight)));
    }

    private static void UpdateSnapCandidate(InteractionSession session)
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

    private static SnapCandidate? FindBestCandidate(InteractionSession session)
    {
        HashSet<string> movingNames = session.Members
            .Select(member => member.CharacterName)
            .ToHashSet(NameComparer);
        SnapCandidate? best = null;

        foreach (WindowEntry moving in session.Members)
        {
            Rect movingBounds = CaptureBounds(moving.Window);
            foreach (WindowEntry target in Entries.Values)
            {
                if (movingNames.Contains(target.CharacterName) || !target.Window.IsVisible)
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
                    ConsiderCandidate(
                        ref best,
                        moving,
                        target,
                        DockSide.Left,
                        targetBounds.Left - movingBounds.Right + VisualJoinOverlap,
                        0);
                    ConsiderCandidate(
                        ref best,
                        moving,
                        target,
                        DockSide.Right,
                        targetBounds.Right - movingBounds.Left - VisualJoinOverlap,
                        0);
                }

                if (horizontalOverlap >= MinimumOverlap)
                {
                    ConsiderCandidate(
                        ref best,
                        moving,
                        target,
                        DockSide.Top,
                        0,
                        targetBounds.Top - movingBounds.Bottom + VisualJoinOverlap);
                    ConsiderCandidate(
                        ref best,
                        moving,
                        target,
                        DockSide.Bottom,
                        0,
                        targetBounds.Bottom - movingBounds.Top - VisualJoinOverlap);
                }
            }
        }

        return best;
    }

    private static void ConsiderCandidate(
        ref SnapCandidate? best,
        WindowEntry moving,
        WindowEntry target,
        DockSide side,
        double deltaX,
        double deltaY)
    {
        double distance = Math.Abs(deltaX) + Math.Abs(deltaY);
        if (distance > SnapDistance || (best is not null && distance >= best.Distance))
        {
            return;
        }

        best = new SnapCandidate(
            $"{moving.CharacterName}|{target.CharacterName}|{side}",
            moving,
            target,
            side,
            deltaX,
            deltaY,
            distance);
    }

    private static void ApplySnapAndLink(InteractionSession session, SnapCandidate candidate)
    {
        var proposed = session.Members.ToDictionary(
            member => member,
            member =>
            {
                Rect current = CaptureBounds(member.Window);
                return new Rect(
                    current.Left + candidate.DeltaX,
                    current.Top + candidate.DeltaY,
                    current.Width,
                    current.Height);
            });

        ShiftBoundsIntoVirtualScreen(proposed);
        ApplyBounds(proposed);

        RemoveDuplicateLink(candidate.Moving.CharacterName, candidate.Target.CharacterName);
        store.Links.Add(new PersistedDockLink
        {
            FirstCharacter = candidate.Target.CharacterName,
            SecondCharacter = candidate.Moving.CharacterName,
            SecondSide = candidate.Side.ToString(),
        });

        WindowEntry[] component = GetOpenComponent(candidate.Target);
        ArrangeLinkedComponent(component, candidate.Target, resizeUniformly: false);
        PersistOpenPositions(component);

        AppLog.Information(
            "Grouping",
            $"Dock link created: {candidate.Moving.CharacterName} {candidate.Side} of " +
            $"{candidate.Target.CharacterName}; componentMembers={component.Length}.");
    }

    private static void ArrangeLinkedComponent(
        WindowEntry[] members,
        WindowEntry anchor,
        bool resizeUniformly)
    {
        if (members.Length < 2)
        {
            return;
        }

        HashSet<string> memberNames = members.Select(member => member.CharacterName).ToHashSet(NameComparer);
        var proposedByName = new Dictionary<string, Rect>(NameComparer)
        {
            [anchor.CharacterName] = CaptureBounds(anchor.Window),
        };
        var queue = new Queue<string>();
        queue.Enqueue(anchor.CharacterName);

        while (queue.Count > 0)
        {
            string currentName = queue.Dequeue();
            Rect currentBounds = proposedByName[currentName];
            foreach ((PersistedDockLink link, string neighborName, DockSide side) in EnumerateNeighbors(currentName))
            {
                if (!memberNames.Contains(neighborName) || proposedByName.ContainsKey(neighborName))
                {
                    continue;
                }

                WindowEntry? neighbor = FindEntry(neighborName);
                if (neighbor is null)
                {
                    continue;
                }

                Rect neighborCurrent = CaptureBounds(neighbor.Window);
                double width = resizeUniformly ? currentBounds.Width : neighborCurrent.Width;
                double height = resizeUniformly ? currentBounds.Height : neighborCurrent.Height;
                proposedByName[neighborName] = PlaceBeside(currentBounds, width, height, side);
                queue.Enqueue(neighborName);
            }
        }

        var proposed = new Dictionary<WindowEntry, Rect>();
        foreach (WindowEntry member in members)
        {
            if (proposedByName.TryGetValue(member.CharacterName, out Rect bounds))
            {
                proposed[member] = bounds;
            }
        }

        ShiftBoundsIntoVirtualScreen(proposed);
        ApplyBounds(proposed);
    }

    private static Rect PlaceBeside(Rect anchor, double width, double height, DockSide side) =>
        side switch
        {
            DockSide.Left => new Rect(anchor.Left - width + VisualJoinOverlap, anchor.Top, width, height),
            DockSide.Right => new Rect(anchor.Right - VisualJoinOverlap, anchor.Top, width, height),
            DockSide.Top => new Rect(anchor.Left, anchor.Top - height + VisualJoinOverlap, width, height),
            DockSide.Bottom => new Rect(anchor.Left, anchor.Bottom - VisualJoinOverlap, width, height),
            _ => new Rect(anchor.Left, anchor.Top, width, height),
        };

    private static IEnumerable<(PersistedDockLink Link, string NeighborName, DockSide Side)> EnumerateNeighbors(
        string characterName)
    {
        foreach (PersistedDockLink link in store.Links)
        {
            if (!TryParseSide(link.SecondSide, out DockSide side))
            {
                continue;
            }

            if (NameComparer.Equals(link.FirstCharacter, characterName))
            {
                yield return (link, link.SecondCharacter, side);
            }
            else if (NameComparer.Equals(link.SecondCharacter, characterName))
            {
                yield return (link, link.FirstCharacter, Opposite(side));
            }
        }
    }

    private static Dictionary<string, GridOffset> BuildGridOffsets(string primaryName)
    {
        var offsets = new Dictionary<string, GridOffset>(NameComparer)
        {
            [primaryName] = new GridOffset(0, 0),
        };
        var queue = new Queue<string>();
        queue.Enqueue(primaryName);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            GridOffset currentOffset = offsets[current];
            foreach ((PersistedDockLink link, string neighborName, DockSide side) in EnumerateNeighbors(current))
            {
                if (offsets.ContainsKey(neighborName))
                {
                    continue;
                }

                GridOffset delta = SideOffset(side);
                offsets[neighborName] = new GridOffset(
                    currentOffset.Column + delta.Column,
                    currentOffset.Row + delta.Row);
                queue.Enqueue(neighborName);
            }
        }

        return offsets;
    }

    private static GridOffset EstimateOffset(Rect primary, Rect member)
    {
        double stepX = Math.Max(1.0, primary.Width - VisualJoinOverlap);
        double stepY = Math.Max(1.0, primary.Height - VisualJoinOverlap);
        return new GridOffset(
            (int)Math.Round((member.Left - primary.Left) / stepX, MidpointRounding.AwayFromZero),
            (int)Math.Round((member.Top - primary.Top) / stepY, MidpointRounding.AwayFromZero));
    }

    private static GridOffset SideOffset(DockSide side) =>
        side switch
        {
            DockSide.Left => new GridOffset(-1, 0),
            DockSide.Right => new GridOffset(1, 0),
            DockSide.Top => new GridOffset(0, -1),
            DockSide.Bottom => new GridOffset(0, 1),
            _ => new GridOffset(0, 0),
        };

    private static DockSide Opposite(DockSide side) =>
        side switch
        {
            DockSide.Left => DockSide.Right,
            DockSide.Right => DockSide.Left,
            DockSide.Top => DockSide.Bottom,
            DockSide.Bottom => DockSide.Top,
            _ => side,
        };

    private static void QueueRestoreGroups()
    {
        if (restoreQueued || Entries.Count < 2)
        {
            return;
        }

        restoreQueued = true;
        Dispatcher dispatcher = Entries.Keys.First().Dispatcher;
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() =>
            {
                restoreQueued = false;
                RestoreOpenGroups();
            }));
    }

    private static void RestoreOpenGroups()
    {
        var processed = new HashSet<string>(NameComparer);
        foreach (WindowEntry entry in Entries.Values.OrderBy(entry => entry.CharacterName, NameComparer))
        {
            if (processed.Contains(entry.CharacterName))
            {
                continue;
            }

            WindowEntry[] component = GetOpenComponent(entry);
            foreach (WindowEntry member in component)
            {
                processed.Add(member.CharacterName);
            }

            if (component.Length < 2)
            {
                continue;
            }

            WindowEntry anchor = component
                .OrderBy(member => FindState(member.CharacterName)?.Top ?? member.Window.Top)
                .ThenBy(member => FindState(member.CharacterName)?.Left ?? member.Window.Left)
                .First();
            ArrangeLinkedComponent(component, anchor, resizeUniformly: false);
            PersistOpenPositions(component);
        }

        SaveStore();
        UpdateContextMenus();
    }

    private static WindowEntry[] GetOpenComponent(WindowEntry entry)
    {
        HashSet<string> names = GetComponentNames(entry.CharacterName);
        return Entries.Values
            .Where(candidate => names.Contains(candidate.CharacterName))
            .ToArray();
    }

    private static HashSet<string> GetComponentNames(string characterName)
    {
        var result = new HashSet<string>(NameComparer) { characterName };
        var queue = new Queue<string>();
        queue.Enqueue(characterName);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            foreach ((PersistedDockLink link, string neighborName, DockSide side) in EnumerateNeighbors(current))
            {
                if (result.Add(neighborName))
                {
                    queue.Enqueue(neighborName);
                }
            }
        }

        return result;
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
            bool grouped = GetComponentNames(entry.CharacterName).Count >= 2;
            detach.IsEnabled = grouped;
            ungroup.IsEnabled = grouped;
        };
        entry.Window.ContextMenu = menu;
    }

    private static void Detach(WindowEntry entry)
    {
        int removed = store.Links.RemoveAll(link =>
            NameComparer.Equals(link.FirstCharacter, entry.CharacterName) ||
            NameComparer.Equals(link.SecondCharacter, entry.CharacterName));
        if (removed == 0)
        {
            return;
        }

        PersistOpenPositions(Entries.Values);
        SaveStore();
        UpdateContextMenus();
        AppLog.Information(
            "Grouping",
            $"{entry.CharacterName} detached from its dock group; linksRemoved={removed}.");
    }

    private static void Ungroup(WindowEntry entry)
    {
        HashSet<string> component = GetComponentNames(entry.CharacterName);
        if (component.Count < 2)
        {
            return;
        }

        int removed = store.Links.RemoveAll(link =>
            component.Contains(link.FirstCharacter) && component.Contains(link.SecondCharacter));
        PersistOpenPositions(Entries.Values.Where(candidate => component.Contains(candidate.CharacterName)));
        SaveStore();
        UpdateContextMenus();
        AppLog.Information(
            "Grouping",
            $"Dock group containing {entry.CharacterName} dissolved; linksRemoved={removed}.");
    }

    private static void UpdateContextMenus()
    {
        foreach (WindowEntry entry in Entries.Values)
        {
            bool grouped = GetComponentNames(entry.CharacterName).Count >= 2;
            if (entry.Window.ContextMenu is null)
            {
                continue;
            }

            foreach (MenuItem item in entry.Window.ContextMenu.Items.OfType<MenuItem>())
            {
                item.IsEnabled = grouped;
            }
        }
    }

    private static void RemoveDuplicateLink(string first, string second) =>
        store.Links.RemoveAll(link =>
            (NameComparer.Equals(link.FirstCharacter, first) && NameComparer.Equals(link.SecondCharacter, second)) ||
            (NameComparer.Equals(link.FirstCharacter, second) && NameComparer.Equals(link.SecondCharacter, first)));

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
            state.GroupId = string.Empty;
        }
    }

    private static PersistedWindowState GetOrCreateState(string characterName)
    {
        PersistedWindowState? existing = FindState(characterName);
        if (existing is not null)
        {
            return existing;
        }

        var created = new PersistedWindowState { CharacterName = characterName };
        store.Windows.Add(created);
        return created;
    }

    private static PersistedWindowState? FindState(string characterName) =>
        store.Windows.FirstOrDefault(state => NameComparer.Equals(state.CharacterName, characterName));

    private static WindowEntry? FindEntry(string characterName) =>
        Entries.Values.FirstOrDefault(entry => NameComparer.Equals(entry.CharacterName, characterName));

    private static void EnsureStoreLoaded()
    {
        if (storeLoaded)
        {
            return;
        }

        storeLoaded = true;
        try
        {
            if (File.Exists(StorePath))
            {
                store = JsonSerializer.Deserialize<PersistedFile>(
                            File.ReadAllText(StorePath),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
                        new PersistedFile();
            }

            store.Windows ??= [];
            store.Links ??= [];
            RemoveInvalidLinks();
            MigrateLegacyGroups();
            store.Version = StoreVersion;
        }
        catch (Exception exception)
        {
            store = new PersistedFile { Version = StoreVersion };
            AppLog.Warning("Grouping", "Could not load preview-groups.json; a new dock workspace will be used.", exception);
        }
    }

    private static void RemoveInvalidLinks()
    {
        store.Links.RemoveAll(link =>
            string.IsNullOrWhiteSpace(link.FirstCharacter) ||
            string.IsNullOrWhiteSpace(link.SecondCharacter) ||
            NameComparer.Equals(link.FirstCharacter, link.SecondCharacter) ||
            !TryParseSide(link.SecondSide, out _));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        store.Links.RemoveAll(link => !seen.Add(NormalizedLinkKey(link.FirstCharacter, link.SecondCharacter)));
    }

    private static void MigrateLegacyGroups()
    {
        if (store.Links.Count > 0)
        {
            return;
        }

        bool migrated = false;
        foreach (PersistedWindowState[] group in store.Windows
                     .Where(state => !string.IsNullOrWhiteSpace(state.GroupId))
                     .GroupBy(state => state.GroupId, NameComparer)
                     .Select(group => group.OrderBy(state => state.Top).ThenBy(state => state.Left).ToArray())
                     .Where(group => group.Length >= 2))
        {
            for (int index = 1; index < group.Length; index++)
            {
                PersistedWindowState previous = group[index - 1];
                PersistedWindowState current = group[index];
                store.Links.Add(new PersistedDockLink
                {
                    FirstCharacter = previous.CharacterName,
                    SecondCharacter = current.CharacterName,
                    SecondSide = DetermineSide(previous.ToRect(), current.ToRect()).ToString(),
                });
            }

            migrated = true;
        }

        foreach (PersistedWindowState state in store.Windows)
        {
            state.GroupId = string.Empty;
        }

        if (migrated)
        {
            AppLog.Information("Grouping", "Legacy coordinate groups migrated to persistent dock links.");
            SaveStore();
        }
    }

    private static DockSide DetermineSide(Rect first, Rect second)
    {
        double deltaX = second.Left + (second.Width / 2) - first.Left - (first.Width / 2);
        double deltaY = second.Top + (second.Height / 2) - first.Top - (first.Height / 2);
        if (Math.Abs(deltaX) >= Math.Abs(deltaY))
        {
            return deltaX >= 0 ? DockSide.Right : DockSide.Left;
        }

        return deltaY >= 0 ? DockSide.Bottom : DockSide.Top;
    }

    private static void SaveStore()
    {
        try
        {
            store.Version = StoreVersion;
            store.Windows = store.Windows
                .Where(state => !string.IsNullOrWhiteSpace(state.CharacterName))
                .GroupBy(state => state.CharacterName, NameComparer)
                .Select(group => group.First())
                .OrderBy(state => state.CharacterName, NameComparer)
                .ToList();
            store.Links = store.Links
                .OrderBy(link => link.FirstCharacter, NameComparer)
                .ThenBy(link => link.SecondCharacter, NameComparer)
                .ToList();

            string temporaryPath = StorePath + ".tmp";
            string json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, StorePath, overwrite: true);
        }
        catch (Exception exception)
        {
            AppLog.Warning("Grouping", "Could not save preview-groups.json.", exception);
        }
    }

    private static void ApplyBounds(IReadOnlyDictionary<WindowEntry, Rect> proposed)
    {
        foreach ((WindowEntry entry, Rect bounds) in proposed)
        {
            Rect current = CaptureBounds(entry.Window);
            if (!PositionChanged(current, bounds) && !SizeChanged(current, bounds))
            {
                continue;
            }

            ApplyBoundsSuppressed(entry.Window, bounds);
        }
    }

    private static void ApplyBoundsSuppressed(FloatingPreviewWindow window, Rect bounds)
    {
        SuppressedWindowEvents.Add(window);
        try
        {
            window.ApplyBounds(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
        finally
        {
            SuppressedWindowEvents.Remove(window);
        }
    }

    private static void ShiftBoundsIntoVirtualScreen(IDictionary<WindowEntry, Rect> bounds)
    {
        if (bounds.Count == 0)
        {
            return;
        }

        Rect screen = GetVirtualScreen();
        Rect union = UnionBounds(bounds.Values);
        double shiftX = 0;
        double shiftY = 0;

        if (union.Width <= screen.Width)
        {
            if (union.Left < screen.Left)
            {
                shiftX = screen.Left - union.Left;
            }
            else if (union.Right > screen.Right)
            {
                shiftX = screen.Right - union.Right;
            }
        }
        else
        {
            shiftX = screen.Left - union.Left;
        }

        if (union.Height <= screen.Height)
        {
            if (union.Top < screen.Top)
            {
                shiftY = screen.Top - union.Top;
            }
            else if (union.Bottom > screen.Bottom)
            {
                shiftY = screen.Bottom - union.Bottom;
            }
        }
        else
        {
            shiftY = screen.Top - union.Top;
        }

        if (Math.Abs(shiftX) <= GeometryTolerance && Math.Abs(shiftY) <= GeometryTolerance)
        {
            return;
        }

        foreach (WindowEntry entry in bounds.Keys.ToArray())
        {
            Rect current = bounds[entry];
            bounds[entry] = new Rect(
                current.Left + shiftX,
                current.Top + shiftY,
                current.Width,
                current.Height);
        }
    }

    private static Rect CaptureBounds(FloatingPreviewWindow window) =>
        new(
            window.Left,
            window.Top,
            window.ActualWidth > 0 ? window.ActualWidth : window.Width,
            window.ActualHeight > 0 ? window.ActualHeight : window.Height);

    private static Rect GetVirtualScreen() =>
        new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

    private static bool IsUsableBounds(Rect bounds, FloatingPreviewWindow window)
    {
        if (bounds.IsEmpty || bounds.Width < window.MinWidth || bounds.Height < window.MinHeight)
        {
            return false;
        }

        Rect visible = Rect.Intersect(bounds, GetVirtualScreen());
        return !visible.IsEmpty && visible.Width >= 48 && visible.Height >= 32;
    }

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

    private static bool PositionChanged(Rect before, Rect after) =>
        Math.Abs(before.Left - after.Left) > GeometryTolerance ||
        Math.Abs(before.Top - after.Top) > GeometryTolerance;

    private static bool SizeChanged(Rect before, Rect after) =>
        Math.Abs(before.Width - after.Width) > GeometryTolerance ||
        Math.Abs(before.Height - after.Height) > GeometryTolerance;

    private static bool TryParseSide(string? value, out DockSide side) =>
        Enum.TryParse(value, ignoreCase: true, out side);

    private static string NormalizedLinkKey(string first, string second) =>
        NameComparer.Compare(first, second) <= 0
            ? $"{first}\u001f{second}"
            : $"{second}\u001f{first}";

    private sealed record WindowEntry(FloatingPreviewWindow Window, string CharacterName);

    private sealed class InteractionSession(
        WindowEntry primary,
        WindowEntry[] members,
        Dictionary<WindowEntry, Rect> startingBounds,
        Dictionary<string, GridOffset> gridOffsets,
        Rect primaryStart)
    {
        public WindowEntry Primary { get; } = primary;
        public WindowEntry[] Members { get; } = members;
        public Dictionary<WindowEntry, Rect> StartingBounds { get; } = startingBounds;
        public Dictionary<string, GridOffset> GridOffsets { get; } = gridOffsets;
        public Rect PrimaryStart { get; } = primaryStart;
        public bool IsMoving { get; set; }
        public bool IsResize { get; set; }
        public SnapCandidate? CurrentCandidate { get; set; }
        public SnapCandidate? ArmedCandidate { get; set; }
        public DateTimeOffset CandidateSince { get; set; }
    }

    private sealed record SnapCandidate(
        string Key,
        WindowEntry Moving,
        WindowEntry Target,
        DockSide Side,
        double DeltaX,
        double DeltaY,
        double Distance);

    private readonly record struct GridOffset(int Column, int Row);

    private enum DockSide
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    private sealed class PersistedFile
    {
        public int Version { get; set; } = StoreVersion;
        public List<PersistedWindowState> Windows { get; set; } = [];
        public List<PersistedDockLink> Links { get; set; } = [];
    }

    private sealed class PersistedDockLink
    {
        public string FirstCharacter { get; set; } = string.Empty;
        public string SecondCharacter { get; set; } = string.Empty;
        public string SecondSide { get; set; } = DockSide.Bottom.ToString();
    }

    private sealed class PersistedWindowState
    {
        public string CharacterName { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public Rect ToRect() => new(Left, Top, Width, Height);
    }
}
