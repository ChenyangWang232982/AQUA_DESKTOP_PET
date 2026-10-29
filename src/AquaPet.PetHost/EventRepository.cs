using AquaPet.PetHost.Action;

namespace AquaPet.PetHost;

/// <summary>
/// Serializes autonomous pet events. Events added here run one at a time;
/// external interactions can take ownership and suspend repository execution.
/// </summary>
internal sealed class EventRepository : IDisposable
{
    private readonly object _gate = new();
    private readonly List<QueuedEvent> _eventQueue = [];
    private readonly SemaphoreSlim _eventLock = new(1, 1);
    private readonly Func<WalkEvent, CancellationToken, Task> _runWalk;
    private readonly Func<MouseFollowEvent, CancellationToken, Task> _runMouseFollow;
    private readonly Func<MouseFollowEvent, bool> _tryRetargetMouseFollow;
    private readonly System.Action _interruptActiveEvent;
    private CancellationTokenSource? _activeCancellation;
    private EventPriority? _activePriority;
    private bool _isDraining;
    private int _externalEventDepth;
    private bool _isDisposed;

    public EventRepository(
        Func<WalkEvent, CancellationToken, Task> runWalk,
        Func<MouseFollowEvent, CancellationToken, Task> runMouseFollow,
        Func<MouseFollowEvent, bool> tryRetargetMouseFollow,
        System.Action interruptActiveEvent)
    {
        _runWalk = runWalk;
        _runMouseFollow = runMouseFollow;
        _tryRetargetMouseFollow = tryRetargetMouseFollow;
        _interruptActiveEvent = interruptActiveEvent;
    }

    /// <summary>Speed used for subsequently queued walk events, in grid units per second.</summary>
    public float WalkSpeedGridUnitsPerSecond { get; set; } = 120f;

    public bool IsExternalEventActive
    {
        get
        {
            lock (_gate)
            {
                return _externalEventDepth > 0;
            }
        }
    }

    public void WalkLeft(int gridSpan, EventPriority priority = EventPriority.Normal) =>
        EnqueueWalk(WalkDirection.Left, gridSpan, priority);

    public void WalkRight(int gridSpan, EventPriority priority = EventPriority.Normal) =>
        EnqueueWalk(WalkDirection.Right, gridSpan, priority);

    /// <summary>
    /// Registers one mouse-cell follow action. A newer cell uses the same Low
    /// priority and therefore replaces the previous pending mouse target.
    /// </summary>
    public void FollowMouseX(MouseFollowEvent mouseFollowEvent)
    {
        if (IsExternalEventActive) return;
        if (_tryRetargetMouseFollow(mouseFollowEvent)) return;

        EnqueueEvent(
            cancellationToken => _runMouseFollow(mouseFollowEvent, cancellationToken),
            EventPriority.Low,
            EventKind.MouseFollow);
    }

    /// <summary>
    /// Gives an event outside the repository exclusive ownership. The active
    /// repository event is cancelled and queued events are held until EndExternalEvent.
    /// </summary>
    public void BeginExternalEvent()
    {
        CancellationTokenSource? activeCancellation;
        lock (_gate)
        {
            _externalEventDepth++;
            _eventQueue.Clear();
            activeCancellation = _activeCancellation;
        }

        _interruptActiveEvent();
        activeCancellation?.Cancel();
    }

    /// <summary>Returns event execution to the repository after an external action ends.</summary>
    public void EndExternalEvent()
    {
        lock (_gate)
        {
            if (_isDisposed) return;
            if (_externalEventDepth == 0) return;
            _externalEventDepth--;
            if (_externalEventDepth > 0) return;
        }
        StartDrain();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        BeginExternalEvent();
    }

    private void EnqueueWalk(WalkDirection direction, int gridSpan, EventPriority priority)
    {
        if (gridSpan <= 0) return;
        EnqueueEvent(
            cancellationToken => _runWalk(new WalkEvent(
                direction,
                gridSpan,
                Math.Max(1f, WalkSpeedGridUnitsPerSecond)), cancellationToken),
            priority,
            EventKind.Generic);
    }

    private void EnqueueEvent(
        Func<CancellationToken, Task> eventAction,
        EventPriority priority,
        EventKind kind)
    {
        CancellationTokenSource? interruptedCancellation = null;
        lock (_gate)
        {
            if (_isDisposed) return;
            // External actions own the pet completely. Requests observed while
            // they are active are deliberately discarded, never deferred.
            if (_externalEventDepth > 0) return;
            if (kind == EventKind.MouseFollow)
            {
                // Mouse movement is continuous input: only the latest target
                // matters after the currently active event has finished.
                _eventQueue.RemoveAll(item => item.Kind == EventKind.MouseFollow);
            }

            var queuedEvent = new QueuedEvent(priority, kind, eventAction);
            var insertIndex = _eventQueue.FindIndex(item => item.Priority < priority);
            if (insertIndex < 0)
            {
                _eventQueue.Add(queuedEvent);
            }
            else
            {
                _eventQueue.Insert(insertIndex, queuedEvent);
            }

            if (_externalEventDepth == 0 && _activePriority is not null && priority >= _activePriority)
            {
                interruptedCancellation = _activeCancellation;
            }
        }

        // Cancel outside the lock so a continuation can safely re-enter the repository.
        interruptedCancellation?.Cancel();
        StartDrain();
    }

    private void StartDrain()
    {
        lock (_gate)
        {
            if (_isDraining || _isDisposed || _externalEventDepth > 0) return;
            _isDraining = true;
        }
        _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        await _eventLock.WaitAsync();
        try
        {
            while (true)
            {
                QueuedEvent queuedEvent;
                CancellationTokenSource cancellation;
                lock (_gate)
                {
                    if (_eventQueue.Count == 0 || _externalEventDepth > 0)
                    {
                        _isDraining = false;
                        return;
                    }

                    queuedEvent = _eventQueue[0];
                    _eventQueue.RemoveAt(0);
                    cancellation = new CancellationTokenSource();
                    _activeCancellation = cancellation;
                    _activePriority = queuedEvent.Priority;
                }

                try
                {
                    await queuedEvent.Action(cancellation.Token);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // External interactions intentionally cancel repository work.
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_activeCancellation, cancellation))
                        {
                            _activeCancellation = null;
                            _activePriority = null;
                        }
                    }
                    cancellation.Dispose();
                }
            }
        }
        finally
        {
            _eventLock.Release();
        }
    }
}

internal enum EventPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

internal enum EventKind
{
    Generic,
    MouseFollow
}

internal readonly record struct QueuedEvent(
    EventPriority Priority,
    EventKind Kind,
    Func<CancellationToken, Task> Action);

internal readonly record struct MouseFollowEvent(
    int TargetX,
    WalkDirection Direction,
    MouseGridCell TargetGrid);

internal readonly record struct WalkEvent(
    WalkDirection Direction,
    int GridSpan,
    float GridUnitsPerSecond);
