namespace NScreen.Server;

internal static class Program
{
    private const string Usage = """
        nscreen-server - share this machine's primary display on the LAN

          nscreen-server [--port N] [--compress] [--headless] [--system]
          nscreen-server --stop

            --port N     TCP port to listen on (default 7000)
            --compress   Brotli the payloads; worth it on Wi-Fi, not on a gigabit LAN
            --headless   Serve in the background: no console, no window, and diagnostics go to
                         nscreen-server.log next to the executable
            --system     Run at High priority, asking UAC for administrator rights if it has none
            --stop       Stop the server running in this session, the way Ctrl+C would
        """;

    private static int Main(string[] args)
    {
        var port = Protocol.DefaultPort;
        var compress = false;
        var headless = false;
        var system = false;
        var relaunched = false;

        var next = 0;
        while (next < args.Length)
        {
            var option = args[next++];
            switch (option)
            {
                case "--compress" or "-c":
                    compress = true;
                    break;

                case "--headless":
                    headless = true;
                    break;

                case "--system":
                    system = true;
                    break;

                case "--stop":
                    return StopSignal.Send();

                case Launcher.RelaunchedMarker:
                    relaunched = true;
                    break;

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
                    return Fail($"Unknown option '{option}'.");
            }
        }

        // Arguments are checked before this point on purpose: a relaunched process has no console to
        // complain to, and the one that starts it does.
        var elevate = system && !Launcher.IsElevated;
        if (!relaunched && (headless || elevate))
        {
            return Launcher.Relaunch(args, elevate, headless);
        }

        if (headless)
        {
            Log.ToFile();
        }

        if (system)
        {
            Launcher.RaisePriority();
        }

        try
        {
            ScreenServer.Run(port, compress);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.WriteLine();
        Console.WriteLine(Usage);
        return 1;
    }
}
