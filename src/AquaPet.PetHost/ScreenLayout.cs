using System.Runtime.InteropServices;
using Windows.Graphics;

namespace AquaPet.PetHost;

/// <summary>A snapshot of monitor geometry captured once when the pet starts.</summary>
internal sealed class ScreenLayout
{
    private readonly IReadOnlyList<ScreenInfo> _screens;

    private ScreenLayout(IReadOnlyList<ScreenInfo> screens) => _screens = screens;

    public static ScreenLayout Capture()
    {
        var screens = new List<ScreenInfo>();
        MonitorEnumCallback callback = (nint monitor, nint dc, nint rectangle, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;

            var bounds = ToRect(info.Bounds);
            var workArea = ToRect(info.WorkArea);
            screens.Add(new ScreenInfo(bounds, workArea, (info.Flags & 1) != 0));
            return true;
        };

        if (!EnumDisplayMonitors(0, 0, callback, 0) || screens.Count == 0)
        {
            throw new InvalidOperationException("Unable to enumerate desktop displays.");
        }
        GC.KeepAlive(callback);
        return new ScreenLayout(screens);
    }

    public ScreenInfo Primary => _screens.FirstOrDefault(screen => screen.IsPrimary) ?? _screens[0];

    public ScreenInfo AtPoint(PointInt32 point) => _screens.MinBy(screen => DistanceSquared(screen.Bounds, point))!;

    private static long DistanceSquared(RectInt32 bounds, PointInt32 point)
    {
        var closestX = Math.Clamp((long)point.X, bounds.X, (long)bounds.X + bounds.Width - 1);
        var closestY = Math.Clamp((long)point.Y, bounds.Y, (long)bounds.Y + bounds.Height - 1);
        var dx = point.X - closestX;
        var dy = point.Y - closestY;
        return dx * dx + dy * dy;
    }

    private static RectInt32 ToRect(NativeRect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Bounds;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private delegate bool MonitorEnumCallback(nint monitor, nint dc, nint rectangle, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumCallback callback, nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}

internal sealed record ScreenInfo(RectInt32 Bounds, RectInt32 WorkArea, bool IsPrimary);
