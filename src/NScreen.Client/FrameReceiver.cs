using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;

namespace NScreen.Client;

/// <summary>
/// The socket read loop, straight into the window's bitmap. Blocks the calling thread until the
/// stream ends, which is how the caller learns to reconnect.
/// </summary>
/// <param name="stream">Connected frame stream, already past the hello.</param>
/// <param name="window">Where decoded pixels go.</param>
/// <param name="width">Server screen width, from the hello.</param>
/// <param name="height">Server screen height, from the hello.</param>
/// <param name="onStats">Fires on the calling thread, once a second.</param>
internal sealed class FrameReceiver(
    NetworkStream stream,
    ViewerWindow window,
    int width,
    int height,
    Action<string> onStats)
{
    private readonly int _frameBytes = width * height * 4;

    public void Receive()
    {
        var rects = new RECT[Protocol.MaxRects];
        var rectBytes = new byte[Protocol.MaxRects * Protocol.RectBytes];
        var wire = new byte[_frameBytes];
        var raw = new byte[_frameBytes];
        var head = new byte[3];
        var lengths = new byte[8];

        var clock = Stopwatch.StartNew();
        var frames = 0;
        long bytesIn = 0;

        try
        {
            while (true)
            {
                var first = stream.ReadByte();
                if (first < 0)
                {
                    break;
                }

                head[0] = (byte)first;
                stream.ReadExactly(head.AsSpan(1, 2));
                var compressed = (head[0] & Protocol.FlagCompressed) != 0;

                var rectCount = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(1));
                if (rectCount is 0 or > Protocol.MaxRects)
                {
                    throw new InvalidDataException($"Rect count {rectCount} out of range.");
                }

                stream.ReadExactly(rectBytes.AsSpan(0, rectCount * Protocol.RectBytes));
                for (var i = 0; i < rectCount; i++)
                {
                    ReadOnlySpan<byte> s = rectBytes.AsSpan(i * Protocol.RectBytes, Protocol.RectBytes);
                    var rect = new RECT
                    {
                        Left = BinaryPrimitives.ReadInt32LittleEndian(s),
                        Top = BinaryPrimitives.ReadInt32LittleEndian(s[4..]),
                        Right = BinaryPrimitives.ReadInt32LittleEndian(s[8..]),
                        Bottom = BinaryPrimitives.ReadInt32LittleEndian(s[12..]),
                    };

                    // Checked here, once, so the pixel copy can index the bitmap without asking
                    // again. A rectangle reaching outside it would either walk off the buffer or
                    // silently misalign every rectangle behind it in the payload.
                    if (rect.Left < 0 || rect.Top < 0 || rect.Right > width || rect.Bottom > height
                        || rect.Width < 0 || rect.Height < 0)
                    {
                        throw new InvalidDataException(
                            $"Rectangle {rect.Left},{rect.Top},{rect.Right},{rect.Bottom} lies " +
                            $"outside the {width}x{height} screen.");
                    }

                    rects[i] = rect;
                }

                stream.ReadExactly(lengths);
                var wireBytes = BinaryPrimitives.ReadInt32LittleEndian(lengths);
                var rawBytes = BinaryPrimitives.ReadInt32LittleEndian(lengths.AsSpan(4));

                // A payload never exceeds one whole screen: the rectangles cover at most that, and
                // the server keeps a compressed payload only when it came out smaller. Bounding
                // both lengths is what stops a corrupt header from sizing an allocation.
                if (wireBytes < 0 || wireBytes > _frameBytes || rawBytes < 0 || rawBytes > _frameBytes)
                {
                    throw new InvalidDataException(
                        $"Frame length {wireBytes}/{rawBytes} is outside one {_frameBytes}-byte screen.");
                }

                stream.ReadExactly(wire.AsSpan(0, wireBytes));

                window.Apply(rects, rectCount, compressed
                    ? raw.AsSpan(0, Protocol.Decompress(wire.AsSpan(0, wireBytes), raw.AsSpan(0, rawBytes)))
                    : wire.AsSpan(0, wireBytes));

                frames++;
                bytesIn += wireBytes;

                if (clock.ElapsedTicks >= Stopwatch.Frequency)
                {
                    var seconds = clock.ElapsedTicks / (double)Stopwatch.Frequency;
                    onStats($"{frames / seconds:0.0} fps   {bytesIn * 8 / seconds / 1e6:0.00} Mbit/s");
                    frames = 0;
                    bytesIn = 0;
                    clock.Restart();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or InvalidDataException)
        {
            Console.WriteLine($"Stream ended: {ex.Message}");
        }
    }
}
