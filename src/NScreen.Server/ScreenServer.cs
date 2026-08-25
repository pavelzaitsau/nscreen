using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NScreen.Server.Capture;

namespace NScreen.Server;

/// <summary>
/// Duplicates the primary output onto a TCP socket. One client at a time; when it drops the server
/// goes back to listening.
/// </summary>
internal static class ScreenServer
{
    /// <summary>How long the loop blocks in AcquireNextFrame before checking for shutdown.</summary>
    private const int GrabTimeoutMs = 250;

    /// <summary>Small on purpose: TCP backpressure is the flow control, so it must appear quickly.</summary>
    private const int SendBufferBytes = 256 * 1024;

    public static void Run(int port, bool compress)
    {
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            // Take the Ctrl+C ourselves so the socket and the duplication object get torn down
            // properly instead of the process being shot mid-frame.
            e.Cancel = true;
            shutdown.Cancel();
        };

        // The second way in: --headless leaves no console for a Ctrl+C to arrive through.
        using var stopSignal = StopSignal.Start(shutdown);

        using var duplicator = new DesktopDuplicator();

        using var listener = new TcpListener(IPAddress.Any, port);
        listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Start();

        // Stopping the listener is what unblocks AcceptTcpClient below. This keeps the whole server
        // synchronous - no sync-over-async, no task scheduler anywhere near the capture loop.
        using var stopListener = shutdown.Token.Register(listener.Stop);

        DiscoveryResponder.Start(port, shutdown.Token);

        Log.Line($"nscreen-server  {Geometry(duplicator)}  port {port}  {(compress ? "brotli" : "raw")}");
        Log.Line(Log.IsFile
            ? "Run nscreen-client with no arguments on the other machine. (nscreen-server --stop to stop)"
            : "Run nscreen-client with no arguments on the other machine. (Ctrl+C to stop)");

        while (!shutdown.IsCancellationRequested)
        {
            using var client = Accept(listener, shutdown.Token);
            if (client is null)
            {
                break;
            }

            var peer = client.Client.RemoteEndPoint?.ToString() ?? "?";
            Log.Event($"client connected: {peer}");
            try
            {
                Serve(duplicator, client, compress, shutdown.Token);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                // Client went away mid-frame. Nothing to do but wait for the next one.
            }

            Log.Event($"client disconnected: {peer}");
        }

        // Worth a line even interactively, but the reason it is here is the log file: without it
        // nothing in there separates a server that stopped cleanly from one that was killed.
        Log.Event("stopped");
    }

    /// <summary>
    /// Blocks for the next client. Null means shutdown ended the wait: the cancellation callback
    /// calls <see cref="TcpListener.Stop"/>, which surfaces as a socket error, not a cancellation.
    /// </summary>
    private static TcpClient? Accept(TcpListener listener, CancellationToken shutdown)
    {
        try
        {
            return listener.AcceptTcpClient();
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
        {
            if (shutdown.IsCancellationRequested)
            {
                return null;
            }

            throw;
        }
    }

    private static void Serve(DesktopDuplicator duplicator, TcpClient client, bool compress, CancellationToken shutdown)
    {
        client.NoDelay = true;
        client.SendBufferSize = SendBufferBytes;

        using var stream = client.GetStream();

        var packet = new FramePacket();

        // Nothing calls Reset while the server sits idle, so the size the duplicator reports here
        // can predate a display change - and a hello cannot be taken back once written, because the
        // client sizes its bitmap from it. Settling first is what keeps a stale one off the wire.
        var settled = Settle(duplicator, packet, shutdown);
        if (!duplicator.HasScreen)
        {
            // Every monitor is gone. Closing without a hello beats promising a size that does not
            // exist: the client retries, and gets a picture the moment a monitor carries one again.
            Log.Event("no display attached, nothing to serve");
            return;
        }

        int width = duplicator.Width, height = duplicator.Height;

        Span<byte> hello = stackalloc byte[Protocol.HelloBytes];
        Protocol.WriteHello(hello, width, height);
        stream.Write(hello);

        var header = new byte[Protocol.MaxHeaderBytes];
        var compressed = compress ? new byte[Protocol.MaxCompressedBytes(duplicator.FrameBytes)] : [];

        var wholeScreen = true;
        var clock = Stopwatch.StartNew();
        var statsAt = Stopwatch.Frequency;
        var frames = 0;
        long bytesOut = 0;

        // Settling already paid for a whole-screen frame where the screen was changing; sending it
        // here is what makes that grab count instead of throwing it away.
        if (settled == GrabStatus.Frame)
        {
            bytesOut += WriteFrame(stream, packet, header, compressed, compress);
            frames++;
            wholeScreen = false;
        }

        // Grab's timeout bounds how long a Ctrl+C goes unnoticed, so no extra wakeup is needed.
        // Pacing is TCP backpressure: a blocked Write stops the loop, and DXGI coalesces meanwhile.
        while (client.Connected && !shutdown.IsCancellationRequested)
        {
            var status = duplicator.Grab(GrabTimeoutMs, packet, wholeScreen);

            if (status == GrabStatus.Lost)
            {
                duplicator.Reset();

                // Reset re-picks the monitor, so this is also where a screen unplugged or a new
                // primary shows up. Either way the hello the client already has is wrong: drop it
                // and let it reconnect against the new geometry.
                if (duplicator.Width != width || duplicator.Height != height)
                {
                    Log.Event($"screen changed to {Geometry(duplicator)}");
                    return;
                }

                wholeScreen = true;
                continue;
            }

            if (status == GrabStatus.Timeout)
            {
                continue;
            }

            bytesOut += WriteFrame(stream, packet, header, compressed, compress);
            frames++;
            wholeScreen = false;

            if (clock.ElapsedTicks >= statsAt)
            {
                var seconds = statsAt / (double)Stopwatch.Frequency;
                Log.Status($"{frames / seconds,5:0.0} fps   {bytesOut * 8 / seconds / 1e6,7:0.00} Mbit/s");
                frames = 0;
                bytesOut = 0;
                statsAt = clock.ElapsedTicks + Stopwatch.Frequency;
            }
        }
    }

    /// <summary>What to print for the shared screen, including the case where there is not one.</summary>
    private static string Geometry(DesktopDuplicator duplicator)
        => duplicator.HasScreen ? $"{duplicator.Width}x{duplicator.Height}" : "no display attached";

    /// <summary>
    /// Brings duplication up to date before anything is promised to the client.
    /// <see cref="GrabStatus.Frame"/> means <paramref name="packet"/> holds a whole-screen frame
    /// ready to send; <see cref="GrabStatus.Timeout"/> means the screen is merely static, which is
    /// itself proof that the geometry is current.
    /// </summary>
    private static GrabStatus Settle(DesktopDuplicator duplicator, FramePacket packet, CancellationToken shutdown)
    {
        // A display change invalidates the duplication object, and DXGI reports that no other way
        // than as Lost on the next grab - so asking is the only way to find out. One rebuild covers
        // one change, and the serve loop handles anything still wrong after that.
        for (var attempt = 0; attempt < 2 && !shutdown.IsCancellationRequested; attempt++)
        {
            var status = duplicator.Grab(GrabTimeoutMs, packet, wholeScreen: true);
            if (status != GrabStatus.Lost)
            {
                return status;
            }

            duplicator.Reset();
        }

        return GrabStatus.Timeout;
    }

    private static int WriteFrame(
        NetworkStream stream, FramePacket packet, byte[] header, byte[] compressed, bool compress)
    {
        ReadOnlySpan<byte> payload = packet.Payload.AsSpan(0, packet.PayloadLength);
        var rawBytes = payload.Length;
        byte flags = 0;

        if (compress)
        {
            var n = Protocol.Compress(payload, compressed);
            if (n > 0 && n < rawBytes)
            {
                payload = compressed.AsSpan(0, n);
                flags = Protocol.FlagCompressed;
            }
        }

        header[0] = flags;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(1), (ushort)packet.RectCount);

        var pos = 3;
        for (var i = 0; i < packet.RectCount; i++)
        {
            var r = packet.Rects[i];
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(pos), r.Left);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(pos + 4), r.Top);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(pos + 8), r.Right);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(pos + 12), r.Bottom);
            pos += Protocol.RectBytes;
        }

        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(pos), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(pos + 4), rawBytes);
        pos += 8;

        stream.Write(header, 0, pos);
        stream.Write(payload);
        return pos + payload.Length;
    }
}
