using System.Runtime.InteropServices;

namespace NScreen.Client;

/// <summary>
/// Where the remote screen lands inside the viewer, which of its pixels a dragged rectangle covers,
/// and how to cut them out. Plain arithmetic over doubles and ints: <see cref="ViewerSurface"/>
/// draws with it and copies with it, and the tests exercise it without a window.
/// </summary>
internal static class RegionSelection
{
    /// <summary>A drag shorter than this in either direction is a stray click, not a selection.</summary>
    public const int MinimumPixels = 8;

    /// <summary>Where the image sits inside a control, in that control's coordinates.</summary>
    /// <param name="Left">Left edge of the image; the letterbox bar is what lies before it.</param>
    /// <param name="Top">Top edge of the image.</param>
    /// <param name="Width">Width of the image as drawn.</param>
    /// <param name="Height">Height of the image as drawn.</param>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Letterbox(double Left, double Top, double Width, double Height);

    /// <summary>
    /// Fits the remote screen into the control, preserving its aspect ratio and centring it. Both
    /// the painting and the coordinate mapping go through this, so a selection lands on the pixels
    /// it was drawn over.
    /// </summary>
    public static Letterbox Fit(double boundsWidth, double boundsHeight, int width, int height)
    {
        var scale = Math.Min(boundsWidth / width, boundsHeight / height);
        var drawWidth = Math.Max(1, width * scale);
        var drawHeight = Math.Max(1, height * scale);

        return new Letterbox(
            (boundsWidth - drawWidth) / 2,
            (boundsHeight - drawHeight) / 2,
            drawWidth,
            drawHeight);
    }

    /// <summary>
    /// One point of a drag, in remote pixels rather than in the window's coordinates. Fractional
    /// and unclamped on purpose: a drag keeps both of its corners in this form, so a window resized
    /// under it cannot move the selection.
    /// </summary>
    public static (double X, double Y) ToPixels(double x, double y, Letterbox box, int width, int height)
        => ((x - box.Left) / box.Width * width, (y - box.Top) / box.Height * height);

    /// <summary>
    /// The whole pixels between two corners, clamped to the screen so the letterbox bars cannot be
    /// selected. Null means the drag was too small to mean anything.
    /// </summary>
    public static PixelRegion? ToRegion(double x1, double y1, double x2, double y2, int width, int height)
    {
        var left = Whole(Math.Min(x1, x2), width);
        var right = Whole(Math.Max(x1, x2), width);
        var top = Whole(Math.Min(y1, y2), height);
        var bottom = Whole(Math.Max(y1, y2), height);

        var region = new PixelRegion(left, top, right - left, bottom - top);
        return region.Width >= MinimumPixels && region.Height >= MinimumPixels ? region : null;
    }

    /// <summary>
    /// The inverse of <see cref="ToPixels"/>: where a region of the remote screen is drawn. The
    /// highlight is painted from this rather than from the raw drag, so what stays bright is
    /// exactly what lands on the clipboard.
    /// </summary>
    public static Letterbox ToBounds(PixelRegion region, Letterbox box, int width, int height)
        => new(
            box.Left + (box.Width * region.Left / width),
            box.Top + (box.Height * region.Top / height),
            box.Width * region.Width / width,
            box.Height * region.Height / height);

    /// <summary>
    /// Cuts a region out of a BGRA frame, row by row. The result is tightly packed, which is what
    /// both the clipboard bitmap and the OCR handler expect.
    /// </summary>
    /// <param name="frame">The whole frame, at least <paramref name="frameStride"/> bytes per row.</param>
    /// <param name="frameStride">Bytes from one row of the frame to the next.</param>
    /// <param name="region">The pixels to cut out.</param>
    public static byte[] Crop(ReadOnlySpan<byte> frame, int frameStride, PixelRegion region)
    {
        var rowBytes = region.Width * 4;
        var crop = new byte[rowBytes * region.Height];

        for (var y = 0; y < region.Height; y++)
        {
            var start = ((region.Top + y) * frameStride) + (region.Left * 4);
            frame.Slice(start, rowBytes).CopyTo(crop.AsSpan(y * rowBytes));
        }

        // Duplication does not promise an opaque alpha channel, and the viewer never looks at it -
        // the bitmap is declared opaque. A clipboard encoder does look, so a copied region that
        // inherited a zero alpha would paste as nothing.
        for (var i = 3; i < crop.Length; i += 4)
        {
            crop[i] = 0xFF;
        }

        return crop;
    }

    private static int Whole(double position, int pixels)
        => Math.Clamp((int)Math.Round(position, MidpointRounding.AwayFromZero), 0, pixels);
}
