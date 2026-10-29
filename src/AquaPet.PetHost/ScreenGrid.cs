using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace AquaPet.PetHost;

/// <summary>
/// A logical, horizontal-only grid whose coordinates match the current
/// display's work-area pixels. The pet's left edge is the grid coordinate.
/// </summary>
internal sealed class ScreenGrid
{
    public int ClampX(RectInt32 workArea, int x, int petWidth)
    {
        var minimumX = workArea.X;
        var maximumX = Math.Max(minimumX, workArea.X + workArea.Width - petWidth);
        return Math.Clamp(x, minimumX, maximumX);
    }

    public int GetTargetX(RectInt32 workArea, int currentX, int signedGridSpan, int petWidth)
    {
        return ClampX(workArea, currentX + signedGridSpan, petWidth);
    }
}
