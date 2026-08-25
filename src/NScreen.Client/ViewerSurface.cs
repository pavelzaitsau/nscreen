using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NScreen.Client;

/// <summary>
/// The remote screen as a <see cref="WriteableBitmap"/>, drawn letterboxed, plus the region
/// selection drawn over it. The wire format is <see cref="PixelFormat.Bgra8888"/> exactly, so a
/// frame lands with a row copy and no conversion.
/// <para>
/// This is a plain <see cref="Control"/> rather than an override of <see cref="Visual.Render"/> on
/// the window itself: on macOS (Avalonia 12.1.1) a window's own drawing never reaches the screen,
/// so that arrangement showed white while frames were arriving fine. A child control renders on
/// both platforms, and still needs no XAML and no Avalonia theme.
/// </para>
/// </summary>
internal sealed class ViewerSurface : Control, IDisposable
{
    /// <summary>How long a copy message stays on screen.</summary>
    private static readonly TimeSpan ToastLife = TimeSpan.FromSeconds(1.6);

    /// <summary>Immutable so one instance is shared by every surface and every render thread.</summary>
    private static readonly IBrush Shade = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)).ToImmutable();
    private static readonly IBrush Plate = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)).ToImmutable();
    private static readonly IPen Edge = new ImmutablePen(Brushes.White, 1);

    private readonly int _width;
    private readonly int _height;
    private readonly WriteableBitmap _bitmap;
    private readonly Rect _source;
    private readonly Action _invalidate;
    private readonly DispatcherTimer _toastTimer;
    private readonly Cursor _crosshair;

    /// <summary>
    /// Guards the bitmap's pixels. The receive thread writes them and the UI thread copies them out
    /// when a selection starts; without this the frozen frame could catch a half-applied one.
    /// </summary>
    private readonly Lock _sync = new();

    private WriteableBitmap? _frozen;
    private IPointer? _captured;

    /// <summary>Read by the receive thread, which skips the repaint request while it is set.</summary>
    private volatile bool _selecting;

    /// <summary>
    /// The two corners of the drag, in remote pixels rather than in this control's coordinates.
    /// Holding them in the remote frame is what makes a window resized mid-drag harmless.
    /// </summary>
    private Point _anchor;
    private Point _cursor;

    private string? _toast;
    private string _modeLabel = string.Empty;

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

        _toastTimer = new DispatcherTimer { Interval = ToastLife };
        _toastTimer.Tick += (_, _) => ShowToast(null);

        _crosshair = new Cursor(StandardCursorType.Cross);
        Cursor = _crosshair;
    }

    /// <summary>Called on the UI thread with the pixels of a released selection.</summary>
    public Action<PixelRegion, byte[]>? RegionSelected { get; set; }

    /// <summary>The current copy mode, drawn in the corner so it is visible without the title bar.</summary>
    public string ModeLabel
    {
        get => _modeLabel;
        set
        {
            _modeLabel = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Patches the bitmap rectangle by rectangle, in packet order. Runs on the receive thread -
    /// that is what <see cref="WriteableBitmap"/> is for - and posts only the repaint request.
    /// Every rectangle is expected to lie inside the bitmap; FrameReceiver rejects the frame
    /// before this is called if one does not.
    /// </summary>
    public unsafe void Apply(RECT[] rects, int count, ReadOnlySpan<byte> payload)
    {
        lock (_sync)
        {
            using var frame = _bitmap.Lock();
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
                        Repaint();
                        return;
                    }

                    var row = new Span<byte>(
                        (byte*)frame.Address + ((long)y * frame.RowBytes) + ((long)r.Left * 4), rowBytes);
                    payload.Slice(offset, rowBytes).CopyTo(row);
                    offset += rowBytes;
                }
            }
        }

        Repaint();
    }

    /// <summary>
    /// Asks for one paint, unless a selection is open - the frozen frame is what gets drawn then,
    /// and repainting it at the stream's frame rate produces the same picture over and over. The
    /// end of the selection paints once by itself.
    /// <para>
    /// Render priority lets the dispatcher coalesce bursts into one paint instead of queueing one
    /// per frame.
    /// </para>
    /// </summary>
    private void Repaint()
    {
        if (!_selecting)
        {
            Dispatcher.UIThread.Post(_invalidate, DispatcherPriority.Render);
        }
    }

    /// <summary>Shows a message under the image for a moment; null clears it. UI thread.</summary>
    public void ShowToast(string? message)
    {
        _toast = message;
        _toastTimer.Stop();
        if (message is not null)
        {
            _toastTimer.Start();
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Drops a selection in progress and gives the pointer back. True means there was one, which is
    /// how Esc knows to keep the window open instead of closing it.
    /// </summary>
    public bool CancelSelection()
    {
        if (!_selecting)
        {
            return false;
        }

        Release();
        InvalidateVisual();
        return true;
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
        var box = RegionSelection.Fit(bounds.Width, bounds.Height, _width, _height);

        // While a selection is open the frozen copy is what gets drawn, so the picture cannot move
        // out from under the drag. Frames keep arriving into _bitmap the whole time.
        var image = _selecting && _frozen is not null ? _frozen : _bitmap;

        context.FillRectangle(Brushes.Black, bounds);
        context.DrawImage(image, _source, ToRect(box));

        if (_selecting)
        {
            DrawSelection(context, bounds, box, image);
        }

        DrawOverlay(context, bounds);
    }

    /// <summary>Safe to call once the window has closed - nothing renders after that.</summary>
    public void Dispose()
    {
        _toastTimer.Stop();
        _bitmap.Dispose();
        _frozen?.Dispose();
        _crosshair.Dispose();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // One drag at a time: a second pointer - a second finger, a pen - would otherwise move the
        // anchor and abandon the capture the first one took.
        var point = e.GetCurrentPoint(this);
        if (_selecting || !point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Freeze();
        _selecting = true;
        _anchor = ToPixels(point.Position);
        _cursor = _anchor;

        // Capture, so a drag that leaves the window still ends in OnPointerReleased.
        _captured = e.Pointer;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_selecting)
        {
            _cursor = ToPixels(e.GetPosition(this));
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // A right or middle button going up during a left-button drag does not end that drag, and
        // must not take the capture away from it either.
        if (e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        var selecting = _selecting;
        _cursor = ToPixels(e.GetPosition(this));
        Release();
        InvalidateVisual();

        if (!selecting)
        {
            return;
        }

        var region = Selected();
        if (region is null)
        {
            // A plain click is not a failed selection. A drag that stayed under the minimum is, and
            // saying so beats leaving the user to wonder why the clipboard did not change.
            if (_cursor != _anchor)
            {
                ShowToast($"selection under {RegionSelection.MinimumPixels} px, nothing copied");
            }

            return;
        }

        // The crop comes from the frozen frame, so what was under the rectangle is what is copied.
        var pixels = Crop(region.Value);
        if (pixels is not null)
        {
            RegionSelected?.Invoke(region.Value, pixels);
        }
    }

    /// <summary>
    /// The platform can take the capture back - an app switch, a system gesture, a touch cancelled
    /// under the palm. No release event follows, so without this the viewer would sit on the frozen
    /// frame with no way out but Esc.
    /// </summary>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_selecting)
        {
            Release();
            InvalidateVisual();
        }
    }

    private static Rect ToRect(RegionSelection.Letterbox box)
        => new(box.Left, box.Top, box.Width, box.Height);

    private static FormattedText Label(string value)
        => new(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            12,
            Brushes.White);

    /// <summary>Draws text on a plate, so it stays readable over any part of a screen capture.</summary>
    private static void DrawLabel(DrawingContext context, FormattedText text, double left, double top)
    {
        var plate = new Rect(left, top, text.Width + 10, text.Height + 6);
        context.DrawRectangle(Plate, null, new RoundedRect(plate, 3));
        context.DrawText(text, new Point(left + 5, top + 3));
    }

    /// <summary>A point of this control, in remote pixels, through the letterbox in force now.</summary>
    private Point ToPixels(Point position)
    {
        var box = RegionSelection.Fit(Bounds.Width, Bounds.Height, _width, _height);
        var (x, y) = RegionSelection.ToPixels(position.X, position.Y, box, _width, _height);
        return new Point(x, y);
    }

    /// <summary>The pixels the current drag covers, or null while it is still too small.</summary>
    private PixelRegion? Selected()
        => RegionSelection.ToRegion(_anchor.X, _anchor.Y, _cursor.X, _cursor.Y, _width, _height);

    /// <summary>Ends a selection, released or cancelled, and hands the pointer back.</summary>
    private void Release()
    {
        _selecting = false;
        _captured?.Capture(null);
        _captured = null;
    }

    /// <summary>
    /// Copies the live frame into the bitmap drawn while the selection is open. One full-frame copy
    /// per drag, on the UI thread: at 1080p that is 8 MB and invisible next to the drag itself. The
    /// frozen bitmap is also what the crop comes from, so the pixels under the rectangle are the
    /// pixels that reach the clipboard.
    /// </summary>
    private unsafe void Freeze()
    {
        _frozen ??= new WriteableBitmap(
            new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

        lock (_sync)
        {
            using var live = _bitmap.Lock();
            using var frozen = _frozen.Lock();
            var rowBytes = _width * 4;
            for (var y = 0; y < _height; y++)
            {
                new ReadOnlySpan<byte>((byte*)live.Address + ((long)y * live.RowBytes), rowBytes)
                    .CopyTo(new Span<byte>((byte*)frozen.Address + ((long)y * frozen.RowBytes), rowBytes));
            }
        }
    }

    /// <summary>
    /// Cuts the selected pixels out of the frozen frame. UI thread; nothing writes it. Null means
    /// there is no frozen frame to cut from, which is a selection that never started.
    /// </summary>
    private unsafe byte[]? Crop(PixelRegion region)
    {
        if (_frozen is null)
        {
            return null;
        }

        using var frozen = _frozen.Lock();
        var frame = new ReadOnlySpan<byte>((byte*)frozen.Address, frozen.RowBytes * _height);
        return RegionSelection.Crop(frame, frozen.RowBytes, region);
    }

    /// <summary>
    /// Shades everything, then repaints the selected pixels at full brightness and outlines them.
    /// The bright part is derived from the pixel region rather than from the cursor, so it shows
    /// the rounding that the copy will use.
    /// </summary>
    private void DrawSelection(
        DrawingContext context, Rect bounds, RegionSelection.Letterbox box, IImage image)
    {
        context.FillRectangle(Shade, bounds);

        var region = Selected();
        if (region is null)
        {
            return;
        }

        var target = ToRect(RegionSelection.ToBounds(region.Value, box, _width, _height));
        var source = new Rect(region.Value.Left, region.Value.Top, region.Value.Width, region.Value.Height);

        context.DrawImage(image, source, target);
        context.DrawRectangle(null, Edge, target);

        // Above the rectangle where there is room, inside it when the drag reaches the top edge.
        var top = target.Y > 24 ? target.Y - 22 : target.Y + 4;
        DrawLabel(context, Label($"{region.Value.Width}x{region.Value.Height}"), target.X, top);
    }

    /// <summary>The mode badge, always on, and the copy message when there is one.</summary>
    private void DrawOverlay(DrawingContext context, Rect bounds)
    {
        if (_modeLabel.Length > 0)
        {
            DrawLabel(context, Label(_modeLabel), 8, 8);
        }

        if (_toast is null)
        {
            return;
        }

        // Under the picture, but never above the badge: a window squeezed to a sliver would
        // otherwise put the message off the top edge, where the user never sees what was copied.
        var text = Label(_toast);
        var floor = 8 + text.Height + 10;
        var top = Math.Max(floor, bounds.Height - text.Height - 22);
        DrawLabel(context, text, (bounds.Width - text.Width - 10) / 2, top);
    }
}
