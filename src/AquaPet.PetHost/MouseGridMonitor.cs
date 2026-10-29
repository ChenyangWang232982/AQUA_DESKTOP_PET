using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.Graphics;

namespace AquaPet.PetHost;

/// <summary>Continuously exposes the mouse X coordinate in the screen grid.</summary>
internal sealed class MouseGridMonitor : IDisposable
{
    // The active display's work area is split into three horizontal follow zones.
    public const int ColumnCount = 3;
    private readonly DispatcherQueueTimer _timer;
    private readonly ScreenLayout _screens;
    private bool _hasPosition;
    private RectInt32 _workArea;

    public MouseGridMonitor(DispatcherQueue dispatcherQueue, ScreenLayout screens)
    {
        _screens = screens;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => UpdatePosition();
    }

    public int X { get; private set; }
    public int CellX { get; private set; }
    public RectInt32 WorkArea => _workArea;

    /// <summary>Raised every sampling frame with the mouse's current screen grid cell.</summary>
    public event System.Action<MouseGridCell>? PositionObserved;

    public void Start()
    {
        UpdatePosition();
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();

    private void UpdatePosition()
    {
        if (!GetCursorPos(out var cursor)) return;
        var workArea = _screens.AtPoint(cursor).WorkArea;
        var cellX = GetCellX(cursor.X, workArea);
        if (!_hasPosition)
        {
            _hasPosition = true;
        }

        X = cursor.X;
        CellX = cellX;
        _workArea = workArea;
        PositionObserved?.Invoke(new MouseGridCell(workArea, cellX));
    }

    private static int GetCellX(int x, RectInt32 workArea)
    {
        var localX = Math.Clamp(x - workArea.X, 0, Math.Max(0, workArea.Width - 1));
        return Math.Min(ColumnCount - 1, localX * ColumnCount / Math.Max(1, workArea.Width));
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointInt32 point);
}

internal readonly record struct MouseGridCell(RectInt32 WorkArea, int Column);
