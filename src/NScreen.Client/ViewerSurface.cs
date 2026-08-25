using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NScreen.Client;

/// <summary>
/// The remote screen as a <see cref="WriteableBitmap"/>, drawn letterboxed. The wire format is
/// <see cref="PixelFormat.Bgra8888"/> exactly, so a frame lands with a row copy and no conversion.
/// <para>
/// This is a plain <see cref="Control"/> rather than an override of <see cref="Visual.Render"/> on
/// the window itself: on macOS (Avalonia 12.1.1) a window's own drawing never reaches the screen,
/// so that arrangement showed white while frames were arriving fine. A child control renders on
/// both platforms, and still needs no XAML and no Avalonia theme.
/// </para>
/// </summary>
internal sealed class ViewerSurface : Control, IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private readonly WriteableBitmap _bitmap;
    private readonly Rect _source;
    private readonly Action _invalidate;

    public ViewerSurface(int width, int height)
    {
        _width = width;
        _height = height;
        _source = new Rect(0, 0, width, height);

        // Opaque: the duplicated desktop is the final composited image, so its alpha carries no
        // meaning and must not be blended against anything.
        _bitmap = new WriteableBitmap(
            new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        // Scaling a screen capture resamples already-final pixels; the cheap filter is both faster
        // and sharper on text than a smooth one.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.LowQuality);

        // Cached so the per-frame repaint request does not allocate a closure.
        _invalidate = InvalidateVisual;
    }

    /// <summary>
    /// Patches the bitmap rectangle by rectangle, in packet order. Runs on the receive thread -
    /// that is what <see cref="WriteableBitmap"/> is for - and posts only the repaint request.
    /// Every rectangle is expected to lie inside the bitmap; FrameReceiver rejects the frame
    /// before this is called if one does not.
    /// </summary>
    public unsafe void Apply(RECT[] rects, int count, ReadOnlySpan<byte> payload)
    {
        using (var frame = _bitmap.Lock())
        {
            var offset = 0;
            for (var i = 0; i < count; i++)
            {
                var r = rects[i];
                var rowBytes = r.Width * 4;
                for (var y = r.Top; y < r.Bottom; y++)
                {
                    if (offset + rowBytes > payload.Length)
                    {
                        // Short payload: draw what arrived rather than tearing the whole frame.
                        Dispatcher.UIThread.Post(_invalidate, DispatcherPriority.Render);
                        return;
                    }

                    var row = new Span<byte>(
                        (byte*)frame.Address + ((long)y * frame.RowBytes) + ((long)r.Left * 4), rowBytes);
                    payload.Slice(offset, rowBytes).CopyTo(row);
                    offset += rowBytes;
                }
            }
        }

        // Render priority lets the dispatcher coalesce bursts into one paint instead of queueing
        // one per frame.
        Dispatcher.UIThread.Post(_invalidate, DispatcherPriority.Render);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Letterbox: preserve the remote aspect ratio and paint the bars ourselves, so the window
        // never shows stale pixels around the image.
        var scale = Math.Min(bounds.Width / _width, bounds.Height / _height);
        var drawWidth = Math.Max(1, _width * scale);
        var drawHeight = Math.Max(1, _height * scale);

        context.FillRectangle(Brushes.Black, bounds);
        context.DrawImage(
            _bitmap,
            _source,
            new Rect((bounds.Width - drawWidth) / 2, (bounds.Height - drawHeight) / 2, drawWidth, drawHeight));
    }

    public void Dispose() => _bitmap.Dispose();
}
