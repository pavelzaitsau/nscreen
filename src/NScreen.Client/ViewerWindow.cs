using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;

namespace NScreen.Client;

/// <summary>
/// The viewer window: sizing, title, keys and the copy mode. The remote screen and the selection
/// rectangle are drawn by <see cref="ViewerSurface"/>, which sits in
/// <see cref="ContentControl.Content"/> and is replaced whenever the server's geometry changes.
/// </summary>
internal sealed class ViewerWindow : Window, IDisposable
{
    /// <summary>Leave room for the dock, taskbar and title bar rather than filling the screen exactly.</summary>
    private const double ScreenFillRatio = 0.85;

    private readonly RegionCopier _copier = new();

    private ViewerSurface _surface;
    private PixelSize _screen;
    private bool _closed;
    private SelectionMode _mode;
    private string _streamTitle;
    private string _stats = string.Empty;

    public ViewerWindow(int width, int height, string title, SelectionMode mode)
    {
        _screen = new PixelSize(width, height);
        _surface = new ViewerSurface(width, height);
        _mode = mode;
        _streamTitle = title;
        Content = _surface;
        Adopt(_surface);

        // The surface paints its own letterbox bars; this only covers the moment between the window
        // appearing and the first frame arriving, where the platform would otherwise show white.
        Background = Brushes.Black;

        RefreshTitle();
        Fit(width, height);
    }

    /// <summary>
    /// Swaps in a surface for a server geometry that changed under it - a monitor unplugged, or a
    /// different one taking over. Called between connections, when no frame can be in flight, and
    /// marshalled to the UI thread so the swap cannot land inside a paint, which is what makes
    /// disposing the surface it replaces safe.
    /// </summary>
    public void Resize(int width, int height)
    {
        var wanted = new PixelSize(width, height);

        Dispatcher.UIThread.Invoke(() =>
        {
            if (wanted == _screen)
            {
                return;
            }

            var previous = _surface;
            _screen = wanted;
            _surface = new ViewerSurface(width, height);
            Content = _surface;
            Adopt(_surface);

            // A drag in progress on the old surface ends here: its bitmaps are about to go, and a
            // release arriving afterwards would crop from a disposed one.
            previous.CancelSelection();
            previous.Dispose();
            Fit(width, height);
        });
    }

    /// <summary>The part of the title that names the connection. UI thread.</summary>
    public void SetStreamTitle(string title)
    {
        _streamTitle = title;
        RefreshTitle();
    }

    /// <summary>The live fps and bitrate, once a second. UI thread.</summary>
    public void SetStats(string stats)
    {
        _stats = stats;
        RefreshTitle();
    }

    /// <summary>Hands a decoded frame to the surface. Runs on the receive thread.</summary>
    public void Apply(RECT[] rects, int count, ReadOnlySpan<byte> payload)
        => _surface.Apply(rects, count, payload);

    /// <summary>Safe to call once the window has closed - nothing renders after that.</summary>
    public void Dispose()
    {
        _closed = true;
        _surface.Dispose();
        _copier.Dispose();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // A selection in progress is what Esc cancels; with none open it closes the window.
                if (!_surface.CancelSelection())
                {
                    Close();
                }

                break;

            case Key.F11:
                WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
                break;

            // Guarded, or Cmd+T from browser habit would silently change what a drag copies.
            case Key.I when e.KeyModifiers == KeyModifiers.None:
                SetMode(SelectionMode.Image);
                break;

            case Key.T when e.KeyModifiers == KeyModifiers.None:
                SetMode(SelectionMode.Text);
                break;

            default:
                break;
        }

        base.OnKeyDown(e);
    }

    private static string Describe(SelectionMode mode)
        => mode == SelectionMode.Image ? "image" : "text (en)";

    /// <summary>Points a freshly built surface at this window's mode and copy handler.</summary>
    private void Adopt(ViewerSurface surface)
    {
        surface.ModeLabel = Describe(_mode);
        surface.RegionSelected = Copy;
    }

    private void SetMode(SelectionMode mode)
    {
        if (mode == _mode)
        {
            return;
        }

        _mode = mode;
        _surface.ModeLabel = Describe(mode);
        _surface.ShowToast(Describe(mode));
        RefreshTitle();
    }

    private void RefreshTitle()
        => Title = $"{_streamTitle} [{Describe(_mode)}]   {_stats}".TrimEnd();

    /// <summary>
    /// Fires on the UI thread when a selection is released. The work behind it - OCR above all -
    /// is slow enough to matter, so the copy runs as a task and reports back through the toast.
    /// </summary>
    private void Copy(PixelRegion region, byte[] pixels)
    {
        var clipboard = Clipboard;
        if (clipboard is null)
        {
            _surface.ShowToast("no clipboard on this platform");
            return;
        }

        _ = CopyAsync(clipboard, _mode, region, pixels);
    }

    private async Task CopyAsync(IClipboard clipboard, SelectionMode mode, PixelRegion region, byte[] pixels)
    {
        string message;
        try
        {
            message = await _copier.CopyAsync(clipboard, mode, region, pixels).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            message = $"copy failed: {ex.Message}";
        }

        // Back on the UI thread: Avalonia's synchronization context is what the await returns to.
        // A copy that outlived the window still says what happened, but the surface it would draw
        // on is gone.
        Console.WriteLine(message);
        if (!_closed)
        {
            _surface.ShowToast(message);
        }
    }

    /// <summary>Sizes the window to the remote screen, capped to a share of the local one.</summary>
    private void Fit(int width, int height)
    {
        // WorkingArea is in physical pixels, Width/Height are logical units.
        var screen = Screens.Primary;
        var scaling = screen?.Scaling is > 0 ? screen.Scaling : 1.0;
        var maxWidth = (screen?.WorkingArea.Width ?? 1600) / scaling * ScreenFillRatio;
        var maxHeight = (screen?.WorkingArea.Height ?? 1000) / scaling * ScreenFillRatio;
        var fit = Math.Min(1.0, Math.Min(maxWidth / width, maxHeight / height));
        Width = Math.Round(width * fit, MidpointRounding.AwayFromZero);
        Height = Math.Round(height * fit, MidpointRounding.AwayFromZero);
    }
}
