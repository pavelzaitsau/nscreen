using System.Runtime.InteropServices;

namespace NScreen.Client;

/// <summary>
/// A rectangle of the remote screen, in that screen's own pixels rather than in the window's
/// coordinates. Width and height are counts, so a region never carries an exclusive edge that has
/// to be remembered.
/// </summary>
/// <param name="Left">Distance from the left edge of the remote screen.</param>
/// <param name="Top">Distance from the top edge of the remote screen.</param>
/// <param name="Width">Pixels across.</param>
/// <param name="Height">Pixels down.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PixelRegion(int Left, int Top, int Width, int Height);
