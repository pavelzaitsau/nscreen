using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace NScreen.Client;

internal static class Program
{
    private const string Usage = """
        nscreen-client - watch a screen shared by nscreen-server

          nscreen-client                  find a server on the LAN and connect to it
          nscreen-client <host>[:port]    connect directly, skipping discovery
          nscreen-client [fe80::1]:7000   an IPv6 literal only carries a port in brackets

            --port N   TCP port (default 7000)

        Esc closes the window, F11 toggles fullscreen.
        """;

    private static int Main(string[] args)
    {
        var port = Protocol.DefaultPort;
        string? host = null;

        var next = 0;
        while (next < args.Length)
        {
            var token = args[next++];
            switch (token)
            {
                case "--port" or "-p":
                    if (next >= args.Length || !int.TryParse(args[next++], out port))
                    {
                        return Fail("--port needs a number.");
                    }

                    break;

                case "--help" or "-h":
                    Console.WriteLine(Usage);
                    return 0;

                default:
                    if (token.StartsWith('-'))
                    {
                        return Fail($"Unknown option '{token}'.");
                    }

                    (host, port) = Target.Split(token, port);
                    break;
            }
        }

        if (port is < 1 or > 65535)
        {
            return Fail($"Port {port} is outside 1-65535.");
        }

        try
        {
            if (host is null)
            {
                Console.WriteLine("Looking for a server on the local network...");
                var found = DiscoveryProbe.Find();
                if (found.Count == 0)
                {
                    Console.Error.WriteLine(
                        "No server found. Start nscreen-server on the other machine, check that both " +
                        "are on the same network and that UDP 7001 is not blocked, or pass the " +
                        "address directly: nscreen-client 192.168.1.42");
                    return 1;
                }

                // Replies arrive fastest-first, so the head of the list is the nearest server.
                // With more than one, say which was picked instead of asking a question.
                if (found.Count > 1)
                {
                    Console.WriteLine($"Found {found.Count} servers, using the first:");
                    foreach (var candidate in found)
                    {
                        Console.WriteLine($"  {candidate}");
                    }
                }

                host = found[0].Address.ToString();
                port = found[0].TcpPort;
                Console.WriteLine($"Using {found[0]}");
            }

            Watch(host, port);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Connects, then runs the window around the stream. Connecting before any UI exists gives the
    /// window its real size, and turns a failed connection into a message instead of a dead window.
    /// </summary>
    private static void Watch(string host, int port)
    {
        using var client = new TcpClient();
        var address = Target.Describe(host, port);
        Console.WriteLine($"Connecting to {address} ...");
        client.Connect(host, port);
        client.NoDelay = true;
        client.ReceiveBufferSize = 1 << 20;

        using var stream = client.GetStream();

        var hello = new byte[Protocol.HelloBytes];
        stream.ReadExactly(hello);
        var (width, height) = Protocol.ReadHello(hello);
        Console.WriteLine($"Connected: {width}x{height}. Esc closes, F11 toggles fullscreen.");

        var title = $"nscreen - {address} - {width}x{height}";

        AppBuilder.Configure<Application>()
            .UsePlatformDetect()
            .Start(
                (app, _) =>
                {
                    using var window = new ViewerWindow(width, height, title);

                    // Both callbacks fire on the receive thread, so window access is posted.
                    new FrameReceiver(
                        stream,
                        window,
                        width,
                        height,
                        onStats: stats => Dispatcher.UIThread.Post(() => window.Title = $"{title}   {stats}"),
                        onEnded: () => Dispatcher.UIThread.Post(window.Close)).Start();

                    // Blocks until the window closes; the receive thread is a background thread, so
                    // it dies with the process.
                    app.Run(window);
                },
                []);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.WriteLine();
        Console.WriteLine(Usage);
        return 1;
    }
}
