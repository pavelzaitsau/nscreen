using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace NScreen.Client;

internal static class Program
{
    /// <summary>How long the handshake may take before the peer is declared not to be a server.</summary>
    private const int HelloTimeoutMs = 5000;

    private const string Usage = """
        nscreen-client - watch a screen shared by nscreen-server

          nscreen-client                  find a server on the LAN and connect to it
          nscreen-client <host>[:port]    connect directly, skipping discovery
          nscreen-client [fe80::1]:7000   an IPv6 literal only carries a port in brackets

            --port N   TCP port (default 7000)

        Esc closes the window, F11 toggles fullscreen.
        """;

    /// <summary>How long to wait before asking the server again after a stream ended.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

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
    /// Handshakes once, then runs the window around a stream that reconnects. Asking the server for
    /// its geometry before any UI exists gives the window its real size, and turns a server that is
    /// not there into a message instead of a dead window.
    /// </summary>
    private static void Watch(string host, int port)
    {
        var address = Target.Describe(host, port);
        Console.WriteLine($"Connecting to {address} ...");

        var (width, height) = Handshake(host, port, address);
        Console.WriteLine($"Connected: {width}x{height}. Esc closes, F11 toggles fullscreen.");

        AppBuilder.Configure<Application>()
            .UsePlatformDetect()
            .Start(
                (app, _) =>
                {
                    using var window = new ViewerWindow(width, height, $"nscreen - {address}");

                    new Thread(() => Follow(window, host, port, address))
                    {
                        IsBackground = true,
                        Name = "nscreen-receive",
                    }.Start();

                    // Blocks until the window closes; the receive thread is a background thread, so
                    // it dies with the process.
                    app.Run(window);
                },
                []);
    }

    /// <summary>
    /// One connection after another, for as long as the window lives. The server drops the
    /// connection whenever the shared screen changes size, which is the whole of its geometry
    /// renegotiation, so reconnecting is also how the viewer follows a monitor unplugged or a
    /// different one taking over. A server with no monitor at all closes before the hello, and this
    /// loop waits that out rather than showing an invented screen.
    /// </summary>
    private static void Follow(ViewerWindow window, string host, int port, string address)
    {
        var announced = false;

        try
        {
            while (true)
            {
                if (Pump(window, host, port, address))
                {
                    announced = false;
                }
                else if (!announced)
                {
                    Console.WriteLine($"Waiting for {address} ...");
                    announced = true;
                }

                // Unconditional: a server that hangs up the instant it accepts would otherwise spin
                // this loop, and a second between connections is invisible next to unplugging a
                // monitor.
                Thread.Sleep(RetryDelay);
            }
        }
        catch (InvalidDataException ex)
        {
            // Whatever is on that port does not speak this protocol, and retrying will not change
            // that. Say so and take the window down rather than sit on a frozen picture.
            Console.Error.WriteLine($"error: {ex.Message}");
            Dispatcher.UIThread.Post(window.Close);
        }
    }

    /// <summary>
    /// One connection, from the hello to the end of the stream. False means the server was not there
    /// or had no monitor to show - both states to wait out rather than errors.
    /// </summary>
    private static bool Pump(ViewerWindow window, string host, int port, string address)
    {
        using var client = new TcpClient();
        try
        {
            client.Connect(host, port);
            client.NoDelay = true;
            client.ReceiveBufferSize = 1 << 20;

            using var stream = client.GetStream();
            var (width, height) = ReadHello(client, stream, address);

            window.Resize(width, height);
            var title = $"nscreen - {address} - {width}x{height}";
            Console.WriteLine($"Streaming {width}x{height}.");

            // The callback fires on this thread, so window access is posted.
            new FrameReceiver(
                stream,
                window,
                width,
                height,
                onStats: stats => Dispatcher.UIThread.Post(() => window.Title = $"{title}   {stats}"))
                .Receive();

            return true;
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            return false;
        }
    }

    /// <summary>Connects, reads the hello and hangs up. Used once, to size the window.</summary>
    private static (int Width, int Height) Handshake(string host, int port, string address)
    {
        using var client = new TcpClient();
        client.Connect(host, port);

        using var stream = client.GetStream();
        return ReadHello(client, stream, address);
    }

    /// <summary>
    /// An nscreen server sends the hello the moment it accepts. Anything else listening on the port
    /// - macOS gives 7000 to the AirPlay receiver, for one - accepts and then says nothing, so this
    /// one read is bounded. The frame loop that follows is not: an idle screen produces no traffic
    /// at all, and a timeout there would kill a healthy connection.
    /// </summary>
    private static (int Width, int Height) ReadHello(TcpClient client, NetworkStream stream, string address)
    {
        client.ReceiveTimeout = HelloTimeoutMs;
        var hello = new byte[Protocol.HelloBytes];
        try
        {
            stream.ReadExactly(hello);
        }
        catch (IOException ex) when (ex.InnerException is SocketException
        {
            SocketErrorCode: SocketError.TimedOut,
        })
        {
            throw new InvalidDataException(
                $"{address} accepted the connection and then sent nothing for " +
                $"{HelloTimeoutMs / 1000} s. Check that nscreen-server is what listens there.");
        }

        client.ReceiveTimeout = 0;
        return Protocol.ReadHello(hello);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.WriteLine();
        Console.WriteLine(Usage);
        return 1;
    }
}
