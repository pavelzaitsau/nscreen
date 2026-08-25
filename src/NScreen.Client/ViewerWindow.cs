using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace NScreen.Client;

/// <summary>
/// The viewer window: sizing, title and keys. The remote screen is drawn by <see cref="ViewerSurface"/>,
/// which sits in <see cref="ContentControl.Content"/> and is replaced whenever the server's geometry
/// changes.
/// </summary>
internal sealed class ViewerWindow : Window, IDisposable
{
    /// <summary>Leave room for the dock, taskbar and title bar rather than filling the screen exactly.</summary>
    private const double ScreenFillRatio = 0.85;

    private ViewerSurface _surface;
    private PixelSize _screen;

    public ViewerWindow(int width, int height, string title)
    {
        _screen = new PixelSize(width, height);
        _surface = new ViewerSurface(width, height);
        Content = _surface;

        // The surface paints its own letterbox bars; this only covers the moment between the window
        // appearing and the first frame arriving, where the platform would otherwise show white.
        Background = Brushes.Black;

        Title = title;
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
            previous.Dispose();
            Fit(width, height);
        });
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

    /// <summary>Hands a decoded frame to the surface. Runs on the receive thread.</summary>
    public void Apply(RECT[] rects, int count, ReadOnlySpan<byte> payload)
        => _surface.Apply(rects, count, payload);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
        else if (e.Key == Key.F11)
        {
            WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Safe to call once the window has closed - nothing renders after that.</summary>
    public void Dispose() => _surface.Dispose();
}
