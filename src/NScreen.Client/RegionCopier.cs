using Avalonia;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NScreen.Client.Native;

namespace NScreen.Client;

/// <summary>
/// Puts a selected region on the clipboard, as pixels or as the text they contain. Returns the line
/// to show the user, because every outcome here - copied, nothing recognised, wrong platform - is
/// something they need told rather than an exception.
/// </summary>
internal sealed class RegionCopier : IDisposable
{
    private const string Superseded = "dropped: a newer selection took the clipboard";

    /// <summary>Orders the copies, so the clipboard ends up holding the newest selection.</summary>
    private readonly CopyQueue _queue = new();

    /// <summary>
    /// The bitmap the clipboard was handed last. Pastes are served from the object itself - on
    /// Windows through an OLE data object that stays live - so it has to outlive the call that put
    /// it there. Keeping one and releasing it when the next copy replaces it holds that open
    /// without leaking a frame per selection. UI thread only.
    /// </summary>
    private WriteableBitmap? _copied;

    private bool _disposed;

    /// <summary>Copies one selection, and says what happened.</summary>
    /// <param name="clipboard">The window's clipboard.</param>
    /// <param name="mode">What to put on it.</param>
    /// <param name="region">The selection, for its size.</param>
    /// <param name="pixels">Tightly packed BGRA, <paramref name="region"/> sized.</param>
    /// <returns>A message for the overlay and the console.</returns>
    public Task<string> CopyAsync(
        IClipboard clipboard, SelectionMode mode, PixelRegion region, byte[] pixels)
        => _queue.RunAsync(() => CopyOneAsync(clipboard, mode, region, pixels), Superseded);

    /// <summary>Releases the bitmap the clipboard still holds, once the window is gone.</summary>
    public void Dispose()
    {
        _disposed = true;
        _copied?.Dispose();
    }

    /// <summary>One copy, with the queue holding every other copy off until it returns.</summary>
    private async Task<string> CopyOneAsync(
        IClipboard clipboard, SelectionMode mode, PixelRegion region, byte[] pixels)
    {
        if (_disposed)
        {
            return "the window closed before the copy finished";
        }

        if (mode == SelectionMode.Image)
        {
            var bitmap = ToBitmap(region, pixels);
            await clipboard.SetBitmapAsync(bitmap).ConfigureAwait(true);

            // Windows serves a paste from the process that copied; without this the picture is gone
            // the moment the viewer closes. The call does nothing on macOS.
            await clipboard.FlushAsync().ConfigureAwait(true);

            _copied?.Dispose();
            _copied = bitmap;
            return $"copied {region.Width}x{region.Height}";
        }

        if (!OperatingSystem.IsMacOS())
        {
            // Windows has an OCR engine of its own, behind WinRT activation. See docs/ROADMAP.md.
            return "text mode reads the screen on macOS only";
        }

        // Recognition takes a few hundred milliseconds, which is far too long for the UI thread.
        var text = await Task.Run(() => MacTextRecognizer.Recognize(pixels, region.Width, region.Height))
            .ConfigureAwait(true);

        if (text is null)
        {
            return "text recognition did not start";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return "no text found in the selection";
        }

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
        await clipboard.FlushAsync().ConfigureAwait(true);
        return $"copied {text.Length} characters";
    }

    /// <summary>Wraps the cropped pixels in the bitmap the clipboard takes.</summary>
    private static unsafe WriteableBitmap ToBitmap(PixelRegion region, byte[] pixels)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(region.Width, region.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var frame = bitmap.Lock();
        var rowBytes = region.Width * 4;
        for (var y = 0; y < region.Height; y++)
        {
            pixels.AsSpan(y * rowBytes, rowBytes)
                .CopyTo(new Span<byte>((byte*)frame.Address + ((long)y * frame.RowBytes), rowBytes));
        }

        return bitmap;
    }
}
