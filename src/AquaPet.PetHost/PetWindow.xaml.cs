using System.Diagnostics;
using System.IO.Pipes;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;
using WinUIEx;
using AquaPet.PetHost.Action;

namespace AquaPet.PetHost;

public sealed partial class PetWindow : Window
{
    private const int SpriteWidth = 300;
    private const int SpriteHeight = 400;
    private const int HostWidth = SpriteWidth + DialogOverlay.LeftSpace;
    private const float Gravity = 2500f;
    private const int FallKeyFrameCount = 96;
    private const int DragThreshold = 8;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const int RgnOr = 2;
    private static readonly nint HwndTopmost = new(-1);

    private readonly int _parentPid;
    private readonly bool _mouseFollowEnabled;
    private readonly string _dialogPipeName;
    private readonly CancellationTokenSource _dialogPipeCancellation = new();
    private readonly Rise _rise;
    private readonly Fall _fall;
    private readonly Walk _walk;
    private readonly IReadOnlyList<Frame> _startupFrames;
    private readonly IReadOnlyList<Frame> _shutdownFrames;
    private readonly IReadOnlyList<Frame> _idleFrames;
    private readonly IReadOnlyList<Frame> _raiseEnterFrames;
    private readonly IReadOnlyList<Frame> _raiseHoldFrames;
    private readonly FallFrames _fallLeftFrames;
    private readonly FallFrames _fallRightFrames;
    private readonly WalkFrames _walkLeftFrames;
    private readonly WalkFrames _walkRightFrames;
    private readonly IReadOnlyList<Frame> _allFrames;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _frameTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _parentTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _landingTimer;
    private readonly EventRepository _events;
    private readonly MouseGridMonitor _mouseGridMonitor;
    private readonly ScreenLayout _screens;
    private readonly ScreenGrid _screenGrid = new();
    private readonly Stopwatch _fallClock = new();
    private readonly nint _hwnd;
    private ContainerVisual? _petVisual;
    private ContainerVisual? _pixelVisual;
    private SpriteVisual? _sprite;
    private DialogOverlay? _dialogOverlay;
    private float _currentY;
    private float _groundY;
    private RectInt32 _workArea;
    private int _frameIndex;
    private IReadOnlyList<Frame> _activeFrames = [];
    private bool _loopActiveAnimation;
    private IReadOnlyList<Frame>? _nextAnimationFrames;
    private bool _nextAnimationLoops;
    private IReadOnlyList<Frame>? _pendingFallExitFrames;
    private IReadOnlyList<Frame>? _pendingWalkExitFrames;
    private bool _walkEventPlaying;
    private bool _eventAnimationInterruptedExternally;
    private MouseFollowState? _activeMouseFollow;
    // The grid cell the active or queued follow animation is travelling toward.
    private MouseGridCell? _animationTargetMouseCell;
    private bool _dragPending;
    private bool _dragging;
    private PointInt32 _dragOrigin;
    private int _windowStartX;
    private int _windowStartY;
    private float _dragStartY;
    private int _previousCursorX;
    private WalkDirection _releaseDirection = WalkDirection.Right;
    private float _fallStartY;
    private TimeSpan _fallDuration;
    private bool _falling;
    private bool _shutdownInProgress;
    private bool _boundsSyncQueued;

    public PetWindow(
        int parentPid,
        string animationDirectory,
        string behaviorMode,
        string dialogAssetPath,
        string dialogPipeName)
    {
        _parentPid = parentPid;
        _mouseFollowEnabled = string.Equals(behaviorMode, "mouseFollow", StringComparison.OrdinalIgnoreCase);
        _dialogPipeName = dialogPipeName;
        _rise = Rise.FromDefaultAnimationDirectory(animationDirectory);
        _fall = Fall.FromDefaultAnimationDirectory(animationDirectory);
        _walk = Walk.FromDefaultAnimationDirectory(animationDirectory);
        var assetRoot = Path.GetFullPath(Path.Combine(animationDirectory, "..", "..", ".."));
        _startupFrames = LoadFrames(Path.Combine(assetRoot, "StartUP", "Nomal"));
        _shutdownFrames = LoadFrames(Path.Combine(assetRoot, "Shutdown", "Nomal_1"));
        _idleFrames = LoadFrames(animationDirectory);
        _raiseEnterFrames = LoadFrames(_rise.EnterAnimationDirectory);
        _raiseHoldFrames = LoadFrames(_rise.HoldAnimationDirectory);
        _fallLeftFrames = LoadFallFrames(_fall.Left);
        _fallRightFrames = LoadFallFrames(_fall.Right);
        _walkLeftFrames = LoadWalkFrames(_walk.Left);
        _walkRightFrames = LoadWalkFrames(_walk.Right);
        _allFrames = _startupFrames.Concat(_shutdownFrames).Concat(_idleFrames).Concat(_raiseEnterFrames).Concat(_raiseHoldFrames)
            .Concat(_fallLeftFrames.All).Concat(_fallRightFrames.All)
            .Concat(_walkLeftFrames.All).Concat(_walkRightFrames.All).ToArray();
        if (_startupFrames.Count == 0 || _shutdownFrames.Count == 0 || _idleFrames.Count == 0 || _raiseEnterFrames.Count == 0 ||
            _raiseHoldFrames.Count == 0 || !_fallLeftFrames.IsComplete ||
            !_fallRightFrames.IsComplete || !_walkLeftFrames.IsComplete ||
            !_walkRightFrames.IsComplete)
        {
            throw new InvalidOperationException("Required pet animation frames were not found.");
        }
        _activeFrames = _startupFrames;
        _loopActiveAnimation = false;
        _nextAnimationFrames = _idleFrames;
        _nextAnimationLoops = true;
        InitializeComponent();
        _screens = ScreenLayout.Capture();
        _mouseGridMonitor = new MouseGridMonitor(DispatcherQueue, _screens);
        _events = new EventRepository(
            PlayWalkEventAsync,
            FollowMouseXAsync,
            TryRetargetMouseFollow,
            InterruptEventAnimation);
        _mouseGridMonitor.PositionObserved += QueueMouseFollow;
        _hwnd = WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
        }

        this.SetWindowStyle(WinUIEx.WindowStyle.Tiled);
        AppWindow.IsShownInSwitchers = false;
        SystemBackdrop = new TransparentTintBackdrop();
        ConfigureBounds();
        AppWindow.Closing += AppWindow_Closing;

        _frameTimer = DispatcherQueue.CreateTimer();
        _frameTimer.Tick += (_, _) => AdvanceFrame();
        _parentTimer = DispatcherQueue.CreateTimer();
        _parentTimer.Interval = TimeSpan.FromSeconds(1);
        _parentTimer.Tick += (_, _) => CheckParent();
        _landingTimer = DispatcherQueue.CreateTimer();
        _landingTimer.IsRepeating = false;
        _landingTimer.Tick += (_, _) =>
        {
            CompleteFall();
        };
        Closed += (_, _) =>
        {
            _frameTimer.Stop();
            _parentTimer.Stop();
            _landingTimer.Stop();
            _events.Dispose();
            _mouseGridMonitor.Dispose();
            _dialogPipeCancellation.Cancel();
            _dialogOverlay?.Dispose();
            if (_sprite is not null)
            {
                _sprite.Brush = null;
            }
            foreach (var frame in _allFrames)
            {
                frame.Dispose();
            }
        };
        Root.Loaded += Root_Loaded;
        Root.PointerPressed += Root_PointerPressed;
        Root.PointerMoved += Root_PointerMoved;
        Root.PointerExited += Root_PointerExited;
        Root.PointerReleased += Root_PointerReleased;
        Root.PointerCanceled += Root_PointerReleased;
    }

    private void ConfigureBounds()
    {
        var screen = _screens.Primary;
        var workArea = screen.WorkArea;
        ApplyWorkArea(workArea, workArea.X + workArea.Width - HostWidth - 24);
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void ApplyWorkArea(RectInt32 workArea, int windowX)
    {
        _workArea = workArea;
        _groundY = Math.Max(0, workArea.Height - SpriteHeight);
        AppWindow.MoveAndResize(new RectInt32(
            windowX,
            workArea.Y,
            HostWidth,
            workArea.Height));
    }

    private bool MoveToCursorDisplay(PointInt32 cursor, float proposedY, int desiredWindowX)
    {
        var targetWorkArea = _screens.AtPoint(cursor).WorkArea;
        if (targetWorkArea.X == _workArea.X && targetWorkArea.Y == _workArea.Y &&
            targetWorkArea.Width == _workArea.Width && targetWorkArea.Height == _workArea.Height)
        {
            return false;
        }

        var globalPetTop = AppWindow.Position.Y + proposedY;
        ApplyWorkArea(targetWorkArea, desiredWindowX);
        _currentY = Math.Clamp(globalPetTop - targetWorkArea.Y, 0, _groundY);
        if (_petVisual is not null)
        {
            _petVisual.Offset = new Vector3(DialogOverlay.LeftSpace, _currentY, 0);
            _dialogOverlay?.SetPosition(_currentY);
        }
        _dragStartY = _currentY;
        _dragOrigin = cursor;
        _windowStartX = desiredWindowX;
        _windowStartY = targetWorkArea.Y;
        SetInteractiveRegion(0, _currentY);
        return true;
    }

    private async void Root_Loaded(object sender, RoutedEventArgs args)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
        _pixelVisual = compositor.CreateContainerVisual();
        _petVisual = compositor.CreateContainerVisual();
        _sprite = compositor.CreateSpriteVisual();
        _sprite.Size = new Vector2(SpriteWidth, SpriteHeight);
        _petVisual.Children.InsertAtTop(_sprite);
        try
        {
            _dialogOverlay = new DialogOverlay(compositor);
            _dialogOverlay.Hidden += (_, _) => SetInteractiveRegion(_falling ? GetFallingY() : _currentY);
            _petVisual.Children.InsertAtTop(_dialogOverlay.Visual);
        }
        catch (Exception error)
        {
            App.LogError(error);
        }
        _pixelVisual.Children.InsertAtTop(_petVisual);
        ElementCompositionPreview.SetElementChildVisual(Root, _pixelVisual);
        Root.XamlRoot.Changed += XamlRoot_Changed;
        UpdatePixelScale();
        PlaceAt(_groundY);
        SetInteractiveRegion(_groundY);

        await Task.WhenAll(_allFrames.Select(frame => frame.LoadAsync(compositor)));

        ShowFrame();
        _frameTimer.Start();
        _parentTimer.Start();

        if (_mouseFollowEnabled)
        {
            _mouseGridMonitor.Start();
        }

        if (!string.IsNullOrWhiteSpace(_dialogPipeName))
        {
            _ = ListenForDialogsAsync();
        }
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        UpdatePixelScale();
        if (_boundsSyncQueued) return;
        _boundsSyncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _boundsSyncQueued = false;
            // WinUI may resize the HWND when its monitor DPI changes. Restore the
            // pixel-sized host before rebuilding the matching interactive region.
            var size = AppWindow.Size;
            var position = AppWindow.Position;
            if (size.Width != HostWidth || size.Height != _workArea.Height || position.Y != _workArea.Y)
            {
                AppWindow.MoveAndResize(new RectInt32(position.X, _workArea.Y, HostWidth, _workArea.Height));
            }
            SetInteractiveRegion(_dragging ? 0 : (_falling ? GetFallingY() : _currentY),
                _dragging ? _currentY : null);
        });
    }

    private void UpdatePixelScale()
    {
        if (_pixelVisual is null || Root.XamlRoot is null) return;
        var scale = (float)Root.XamlRoot.RasterizationScale;
        if (scale > 0) _pixelVisual.Scale = new Vector3(1 / scale, 1 / scale, 1);
    }

    private async Task ListenForDialogsAsync()
    {
        while (!_dialogPipeCancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _dialogPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_dialogPipeCancellation.Token);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: false);
                var content = await reader.ReadToEndAsync(_dialogPipeCancellation.Token);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    DispatcherQueue.TryEnqueue(() => ShowDialog(content));
                }
            }
            catch (OperationCanceledException) when (_dialogPipeCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // A single malformed local message must not stop the pet renderer.
            }
        }
    }

    private void ShowDialog(string content)
    {
        try
        {
            _dialogOverlay?.Show(content, _falling ? GetFallingY() : _currentY);
            SetInteractiveRegion(_falling ? GetFallingY() : _currentY);
        }
        catch (Exception error)
        {
            App.LogError(error);
        }
    }

    private void ShowFrame()
    {
        var frame = _activeFrames[_frameIndex];
        _sprite!.Brush = frame.Brush;
        _frameTimer.Interval = frame.Duration;
    }

    private void AdvanceFrame()
    {
        if (_loopActiveAnimation)
        {
            _frameIndex = (_frameIndex + 1) % _activeFrames.Count;
        }
        else if (_frameIndex < _activeFrames.Count - 1)
        {
            _frameIndex++;
        }
        else if (_nextAnimationFrames is not null)
        {
            var nextFrames = _nextAnimationFrames;
            var nextLoops = _nextAnimationLoops;
            SetActiveAnimation(nextFrames, nextLoops);
            return;
        }
        ShowFrame();
    }

    private void SetActiveAnimation(
        IReadOnlyList<Frame> frames,
        bool loop,
        IReadOnlyList<Frame>? nextFrames = null,
        bool nextLoops = false)
    {
        _activeFrames = frames;
        _loopActiveAnimation = loop;
        _nextAnimationFrames = nextFrames;
        _nextAnimationLoops = nextLoops;
        _frameIndex = 0;
        ShowFrame();
    }

    private void PlaceAt(float y)
    {
        if (_petVisual is null) return;
        _petVisual.StopAnimation("Offset.Y");
        _currentY = Math.Clamp(y, 0, _groundY);
        _petVisual.Offset = new Vector3(DialogOverlay.LeftSpace, _currentY, 0);
        _dialogOverlay?.SetPosition(_currentY);
    }

    private void FallToGround()
    {
        if (_petVisual is null) return;
        if (_currentY >= _groundY)
        {
            CompleteFall();
            return;
        }

        var distance = _groundY - _currentY;
        var duration = TimeSpan.FromSeconds(Math.Sqrt(2 * distance / Gravity));
        var animation = _petVisual.Compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(0f, _currentY);
        for (var i = 1; i <= FallKeyFrameCount; i++)
        {
            var progress = i / (float)FallKeyFrameCount;
            animation.InsertKeyFrame(progress, _currentY + distance * progress * progress);
        }

        _fallStartY = _currentY;
        _fallDuration = duration;
        _falling = true;
        SetInteractiveRegion(_currentY);
        _fallClock.Restart();
        _petVisual.StartAnimation("Offset.Y", animation);
        _landingTimer.Interval = duration;
        _landingTimer.Start();
    }

    private void CompleteFall()
    {
        _falling = false;
        PlaceAt(_groundY);
        SetInteractiveRegion(_groundY);
        if (_pendingFallExitFrames is null)
        {
            _events.EndExternalEvent();
            return;
        }

        var exitFrames = _pendingFallExitFrames;
        _pendingFallExitFrames = null;
        _pendingWalkExitFrames = null;
        SetActiveAnimation(
            exitFrames,
            loop: false,
            nextFrames: _idleFrames,
            nextLoops: true);
        EndExternalEventAfterFallExit(exitFrames);
    }

    private async void EndExternalEventAfterFallExit(IReadOnlyList<Frame> exitFrames)
    {
        await Task.Delay(GetTotalDuration(exitFrames) + TimeSpan.FromMilliseconds(20));
        DispatcherQueue.TryEnqueue(_events.EndExternalEvent);
    }

    private void SetInteractiveRegion(float top, float? dialogPetTop = null)
    {
        var regionTop = Math.Max(0, (int)top);
        var region = CreateRectRgn(DialogOverlay.LeftSpace, regionTop,
            DialogOverlay.LeftSpace + SpriteWidth, (int)_groundY + SpriteHeight);
        if (_dialogOverlay is { IsVisible: true })
        {
            var dialogTop = Math.Max(0, (int)((dialogPetTop ?? top) + _dialogOverlay.Visual.Offset.Y) - 8);
            var dialogBottom = _falling
                ? Math.Min(_workArea.Height, (int)(_groundY + _dialogOverlay.Visual.Offset.Y + _dialogOverlay.Height))
                : Math.Min(_workArea.Height, dialogTop + (int)_dialogOverlay.Height + 16);
            var dialogRegion = CreateRectRgn(
                0, dialogTop, DialogOverlay.Width, Math.Max(dialogTop, dialogBottom));
            CombineRgn(region, region, dialogRegion, RgnOr);
            DeleteObject(dialogRegion);
        }
        if (SetWindowRgn(_hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void Root_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        var pointerX = point.Position.X * scale;
        var pointerY = point.Position.Y * scale;
        var currentY = _falling ? GetFallingY() : _currentY;
        if (point.Properties.IsLeftButtonPressed && _dialogOverlay?.Contains(pointerX, pointerY, currentY) == true)
        {
            _dialogOverlay.FadeOut();
            e.Handled = true;
            return;
        }
        if (!point.Properties.IsLeftButtonPressed ||
            pointerX < DialogOverlay.LeftSpace ||
            pointerX > DialogOverlay.LeftSpace + SpriteWidth ||
            pointerY < currentY ||
            pointerY > currentY + SpriteHeight)
        {
            return;
        }

        _events.BeginExternalEvent();
        _dragPending = true;
        GetCursorPos(out _dragOrigin);
        _previousCursorX = _dragOrigin.X;
        _dragStartY = currentY;
        var position = AppWindow.Position;
        _windowStartX = position.X;
        _windowStartY = position.Y;
        Root.CapturePointer(e.Pointer);
    }

    private void BeginDrag()
    {
        var currentY = _falling ? GetFallingY() : _currentY;
        _dragPending = false;
        _dragging = true;
        _landingTimer.Stop();
        _falling = false;
        _pendingFallExitFrames = null;
        _pendingWalkExitFrames = null;
        _petVisual?.StopAnimation("Offset.Y");
        PlaceAt(currentY);
        SetActiveAnimation(
            _raiseEnterFrames,
            loop: false,
            nextFrames: _raiseHoldFrames,
            nextLoops: true);
        _dragStartY = _currentY;
        SetInteractiveRegion(0, _currentY);
    }

    private float GetFallingY()
    {
        var progress = Math.Clamp(
            _fallClock.Elapsed.TotalSeconds / _fallDuration.TotalSeconds, 0d, 1d);
        return _fallStartY + (_groundY - _fallStartY) * (float)(progress * progress);
    }

    private void Root_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_dragging && !_dragPending && _dialogOverlay is not null)
        {
            var point = e.GetCurrentPoint(Root).Position;
            var scale = Root.XamlRoot?.RasterizationScale ?? 1;
            var petY = _falling ? GetFallingY() : _currentY;
            _dialogOverlay.SetHovered(_dialogOverlay.Contains(point.X * scale, point.Y * scale, petY), petY);
        }
        if (!_dragging && !_dragPending) return;
        GetCursorPos(out var cursor);
        if (_dragPending)
        {
            var horizontalDistance = cursor.X - _dragOrigin.X;
            var verticalDistance = cursor.Y - _dragOrigin.Y;
            if (horizontalDistance * horizontalDistance + verticalDistance * verticalDistance <
                DragThreshold * DragThreshold)
            {
                return;
            }

            BeginDrag();
        }

        _previousCursorX = cursor.X;
        var proposedY = Math.Clamp(_dragStartY + cursor.Y - _dragOrigin.Y, 0, _groundY);
        var desiredWindowX = _windowStartX + cursor.X - _dragOrigin.X;
        if (!MoveToCursorDisplay(cursor, proposedY, desiredWindowX))
        {
            _currentY = proposedY;
            if (_petVisual is not null)
            {
                _petVisual.Offset = new Vector3(DialogOverlay.LeftSpace, _currentY, 0);
                _dialogOverlay?.SetPosition(_currentY);
            }
            AppWindow.Move(new PointInt32(desiredWindowX, _windowStartY));
        }
        if (_dialogOverlay is { IsVisible: true })
        {
            // The sprite region covers the drag path; the bubble follows the
            // pet's actual position, not that path's top edge.
            SetInteractiveRegion(0, _currentY);
        }
    }

    private void Root_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_dragging && !_dragPending)
        {
            _dialogOverlay?.SetHovered(false, _falling ? GetFallingY() : _currentY);
        }
    }

    private void Root_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_dragPending)
        {
            _dragPending = false;
            _events.EndExternalEvent();
            Root.ReleasePointerCapture(e.Pointer);
            return;
        }
        if (!_dragging) return;
        GetCursorPos(out var cursor);
        var horizontalDelta = cursor.X - _previousCursorX;
        if (horizontalDelta == 0)
        {
            horizontalDelta = cursor.X - _dragOrigin.X;
        }
        _releaseDirection = horizontalDelta < 0
            ? WalkDirection.Left
            : WalkDirection.Right;
        _dragging = false;
        Root.ReleasePointerCapture(e.Pointer);
        var fallFrames = _releaseDirection == WalkDirection.Left
            ? _fallLeftFrames
            : _fallRightFrames;
        _pendingFallExitFrames = fallFrames.Exit;
        SetActiveAnimation(
            fallFrames.Enter,
            loop: false,
            nextFrames: fallFrames.Hold,
            nextLoops: true);
        FallToGround();
    }

    private void CheckParent()
    {
        try
        {
            using var parent = Process.GetProcessById(_parentPid);
            if (!parent.HasExited) return;
        }
        catch (ArgumentException)
        {
        }

        Close();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownInProgress) return;

        args.Cancel = true;
        _shutdownInProgress = true;
        _mouseGridMonitor.Dispose();
        _events.BeginExternalEvent();
        _dragPending = false;
        _dragging = false;
        _falling = false;
        _landingTimer.Stop();
        _petVisual?.StopAnimation("Offset.Y");
        SetActiveAnimation(_shutdownFrames, loop: false);
        FinishShutdownAsync();
    }

    private async void FinishShutdownAsync()
    {
        await Task.Delay(GetTotalDuration(_shutdownFrames) + TimeSpan.FromMilliseconds(20));
        DispatcherQueue.TryEnqueue(Close);
    }

    /// <summary>
    /// Exposes the serialized autonomous event queue to future triggers.
    /// </summary>
    internal EventRepository Events => _events;

    private async Task PlayWalkEventAsync(WalkEvent walkEvent, CancellationToken cancellationToken)
    {
        var walkFrames = walkEvent.Direction == WalkDirection.Left
            ? _walkLeftFrames
            : _walkRightFrames;
        _eventAnimationInterruptedExternally = false;
        _walkEventPlaying = true;
        var exitStarted = false;
        try
        {
            PlayWalk(walkEvent.Direction);
            await Task.Delay(GetTotalDuration(walkFrames.Enter), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            await MoveAcrossGridAsync(
                walkEvent.Direction,
                walkEvent.GridSpan,
                walkEvent.GridUnitsPerSecond,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            StopWalk();
            exitStarted = true;
            await WaitForWalkExitAsync(walkFrames);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!exitStarted && !_eventAnimationInterruptedExternally &&
                _pendingWalkExitFrames is not null)
            {
                StopWalk();
                await WaitForWalkExitAsync(walkFrames);
            }

            throw;
        }
        finally
        {
            _walkEventPlaying = false;
        }
    }

    private async Task FollowMouseXAsync(
        MouseFollowEvent mouseFollowEvent,
        CancellationToken cancellationToken)
    {
        _eventAnimationInterruptedExternally = false;
        _walkEventPlaying = true;
        var exitStarted = false;
        var state = new MouseFollowState(
            mouseFollowEvent.TargetX,
            mouseFollowEvent.Direction,
            mouseFollowEvent.TargetGrid);
        _activeMouseFollow = state;
        var walkFrames = mouseFollowEvent.Direction == WalkDirection.Left
            ? _walkLeftFrames
            : _walkRightFrames;
        try
        {
            PlayWalk(state.Direction);
            await Task.Delay(GetTotalDuration(walkFrames.Enter), cancellationToken);
            await MoveToMouseFollowTargetAsync(
                state,
                _events.WalkSpeedGridUnitsPerSecond,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            state.IsMoving = false;
            StopWalk();
            exitStarted = true;
            await WaitForWalkExitAsync(walkFrames);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!exitStarted && !_eventAnimationInterruptedExternally &&
                _pendingWalkExitFrames is not null)
            {
                state.IsMoving = false;
                StopWalk();
                await WaitForWalkExitAsync(walkFrames);
            }

            throw;
        }
        finally
        {
            if (ReferenceEquals(_activeMouseFollow, state))
            {
                _activeMouseFollow = null;
            }
            _walkEventPlaying = false;
        }
    }

    private static Task WaitForWalkExitAsync(WalkFrames walkFrames) =>
        Task.Delay(GetTotalDuration(walkFrames.Exit) + TimeSpan.FromMilliseconds(20));

    private void QueueMouseFollow(MouseGridCell mouseCell)
    {
        if (_events.IsExternalEventActive)
        {
            _animationTargetMouseCell = null;
            return;
        }

        var petCenterX = AppWindow.Position.X + DialogOverlay.LeftSpace + SpriteWidth / 2;
        // The monitor reports every frame. A follow animation owns a target cell;
        // only a different mouse cell is allowed to replace that target.
        if (_animationTargetMouseCell == mouseCell) return;
        _animationTargetMouseCell = mouseCell;

        var cellStart = mouseCell.WorkArea.X +
            mouseCell.Column * mouseCell.WorkArea.Width / MouseGridMonitor.ColumnCount;
        var cellEnd = mouseCell.WorkArea.X +
            (mouseCell.Column + 1) * mouseCell.WorkArea.Width / MouseGridMonitor.ColumnCount;
        var cellCoordinate = Random.Shared.Next(cellStart, Math.Max(cellStart + 1, cellEnd));
        var targetX = _screenGrid.ClampX(_workArea, cellCoordinate - SpriteWidth / 2, SpriteWidth)
            - DialogOverlay.LeftSpace;

        _events.FollowMouseX(new MouseFollowEvent(
            targetX,
            cellCoordinate < petCenterX ? WalkDirection.Left : WalkDirection.Right,
            mouseCell));
    }

    private bool TryRetargetMouseFollow(MouseFollowEvent mouseFollowEvent)
    {
        if (_activeMouseFollow is not { IsMoving: true } state ||
            state.Direction != mouseFollowEvent.Direction)
        {
            return false;
        }

        state.TargetX = mouseFollowEvent.TargetX;
        state.TargetGrid = mouseFollowEvent.TargetGrid;
        return true;
    }

    private Task MoveToMouseFollowTargetAsync(
        MouseFollowState state,
        float gridUnitsPerSecond,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        var clock = Stopwatch.StartNew();
        var lastElapsed = TimeSpan.Zero;
        var finished = false;
        CancellationTokenRegistration cancellationRegistration = default;

        void Finish(bool canceled)
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            cancellationRegistration.Dispose();
            if (canceled)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                completion.TrySetResult();
            }
        }

        timer.Tick += (_, _) =>
        {
            var elapsed = clock.Elapsed;
            var elapsedSeconds = Math.Max(0d, (elapsed - lastElapsed).TotalSeconds);
            lastElapsed = elapsed;
            var currentX = AppWindow.Position.X;
            var remaining = state.TargetX - currentX;
            var requestedDirection = remaining < 0 ? WalkDirection.Left : WalkDirection.Right;
            if (Math.Abs(remaining) <= 2 || requestedDirection != state.Direction)
            {
                Finish(canceled: false);
                return;
            }

            var step = Math.Max(1, (int)Math.Round(gridUnitsPerSecond * elapsedSeconds));
            var nextX = currentX + Math.Sign(remaining) * Math.Min(Math.Abs(remaining), step);
            AppWindow.Move(new PointInt32(nextX, AppWindow.Position.Y));
        };
        cancellationRegistration = cancellationToken.Register(() => Finish(canceled: true));
        timer.Start();
        return completion.Task;
    }

    private Task MoveAcrossGridAsync(
        WalkDirection direction,
        int gridSpan,
        float gridUnitsPerSecond,
        CancellationToken cancellationToken)
    {
        var start = AppWindow.Position;
        var display = _screens.AtPoint(
            new PointInt32(start.X + DialogOverlay.LeftSpace + SpriteWidth / 2, _workArea.Y + SpriteHeight / 2));
        var targetX = _screenGrid.GetTargetX(
            display.WorkArea,
            start.X + DialogOverlay.LeftSpace,
            direction == WalkDirection.Left ? -gridSpan : gridSpan,
            SpriteWidth) - DialogOverlay.LeftSpace;
        var distance = Math.Abs(targetX - start.X);
        if (distance == 0) return Task.CompletedTask;

        var duration = TimeSpan.FromSeconds(distance / gridUnitsPerSecond);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        var clock = Stopwatch.StartNew();
        var finished = false;
        CancellationTokenRegistration cancellationRegistration = default;

        void Finish(bool canceled)
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            cancellationRegistration.Dispose();
            if (canceled)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            AppWindow.Move(new PointInt32(targetX, start.Y));
            completion.TrySetResult();
        }

        timer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0d, 1d);
            var x = start.X + (int)Math.Round((targetX - start.X) * progress);
            AppWindow.Move(new PointInt32(x, start.Y));
            if (progress >= 1d)
            {
                Finish(canceled: false);
            }
        };
        cancellationRegistration = cancellationToken.Register(() => Finish(canceled: true));
        timer.Start();
        return completion.Task;
    }

    private void InterruptEventAnimation()
    {
        _animationTargetMouseCell = null;
        if (!_walkEventPlaying) return;

        _eventAnimationInterruptedExternally = true;
        if (_activeMouseFollow is not null)
        {
            _activeMouseFollow.IsMoving = false;
        }
        _pendingWalkExitFrames = null;
        _nextAnimationFrames = null;
        SetActiveAnimation(_idleFrames, loop: true);
    }

    /// <summary>
    /// Starts a normal walk chain in the requested direction. This is prepared
    /// for a later movement event; it does not create movement by itself.
    /// </summary>
    internal void PlayWalk(WalkDirection direction)
    {
        var walkFrames = direction == WalkDirection.Left
            ? _walkLeftFrames
            : _walkRightFrames;
        _pendingWalkExitFrames = walkFrames.Exit;
        SetActiveAnimation(
            walkFrames.Enter,
            loop: false,
            nextFrames: walkFrames.Hold,
            nextLoops: true);
    }

    /// <summary>Stops the active walk chain by playing its C segment once.</summary>
    internal void StopWalk()
    {
        if (_pendingWalkExitFrames is null) return;

        var exitFrames = _pendingWalkExitFrames;
        _pendingWalkExitFrames = null;
        SetActiveAnimation(
            exitFrames,
            loop: false,
            nextFrames: _idleFrames,
            nextLoops: true);
    }

    private sealed class MouseFollowState(
        int targetX,
        WalkDirection direction,
        MouseGridCell targetGrid)
    {
        public int TargetX { get; set; } = targetX;
        public WalkDirection Direction { get; } = direction;
        public MouseGridCell TargetGrid { get; set; } = targetGrid;
        public bool IsMoving { get; set; } = true;
    }

    private sealed class Frame : IDisposable
    {
        private Frame(string path, TimeSpan duration)
        {
            Path = path;
            Duration = duration;
        }

        public string Path { get; }
        public TimeSpan Duration { get; }
        public LoadedImageSurface? Surface { get; private set; }
        public CompositionSurfaceBrush? Brush { get; private set; }

        public async Task LoadAsync(Compositor compositor)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var surface = LoadedImageSurface.StartLoadFromUri(
                new Uri(Path, UriKind.Absolute),
                new Windows.Foundation.Size(SpriteWidth, SpriteWidth));
            surface.LoadCompleted += (_, args) =>
            {
                if (args.Status == LoadedImageSourceLoadStatus.Success)
                {
                    ready.TrySetResult();
                }
                else
                {
                    ready.TrySetException(new InvalidOperationException(
                        $"桌宠图片加载失败：{Path}（{args.Status}）"));
                }
            };

            Surface = surface;
            await ready.Task;
            var brush = compositor.CreateSurfaceBrush();
            brush.Surface = surface;
            brush.Stretch = CompositionStretch.Uniform;
            brush.HorizontalAlignmentRatio = 0.5f;
            brush.VerticalAlignmentRatio = 0.5f;
            Brush = brush;
        }

        public void Dispose()
        {
            Brush?.Dispose();
            Surface?.Dispose();
        }

        public static Frame FromPath(string path)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path).Split('_');
            return new Frame(path, int.TryParse(name.LastOrDefault(), out var ms)
                ? TimeSpan.FromMilliseconds(ms)
                : TimeSpan.FromMilliseconds(125));
        }
    }

    private static IReadOnlyList<Frame> LoadFrames(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(Frame.FromPath)
                .ToArray()
            : [];

    private static FallFrames LoadFallFrames(FallChain chain) => new(
        LoadFrames(chain.EnterAnimationDirectory),
        LoadFrames(chain.HoldAnimationDirectory),
        LoadFrames(chain.ExitAnimationDirectory));

    private static WalkFrames LoadWalkFrames(WalkChain chain) => new(
        LoadFrames(chain.EnterAnimationDirectory),
        LoadFrames(chain.HoldAnimationDirectory),
        LoadFrames(chain.ExitAnimationDirectory));

    private static TimeSpan GetTotalDuration(IEnumerable<Frame> frames) =>
        TimeSpan.FromMilliseconds(frames.Sum(frame => frame.Duration.TotalMilliseconds));

    private sealed record FallFrames(
        IReadOnlyList<Frame> Enter,
        IReadOnlyList<Frame> Hold,
        IReadOnlyList<Frame> Exit)
    {
        public bool IsComplete => Enter.Count > 0 && Hold.Count > 0 && Exit.Count > 0;
        public IEnumerable<Frame> All => Enter.Concat(Hold).Concat(Exit);
    }

    private sealed record WalkFrames(
        IReadOnlyList<Frame> Enter,
        IReadOnlyList<Frame> Hold,
        IReadOnlyList<Frame> Exit)
    {
        public bool IsComplete => Enter.Count > 0 && Hold.Count > 0 && Exit.Count > 0;
        public IEnumerable<Frame> All => Enter.Concat(Hold).Concat(Exit);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointInt32 point);

    [DllImport("gdi32.dll")]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(nint destination, nint source1, nint source2, int combineMode);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(nint hwnd, nint region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
}
