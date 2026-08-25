namespace NScreen.Server;

internal static class Program
{
    private const string Usage = """
        nscreen-server - share this machine's primary display on the LAN

          nscreen-server [--port N] [--compress]

            --port N     TCP port to listen on (default 7000)
            --compress   Brotli the payloads; worth it on Wi-Fi, not on a gigabit LAN
        """;

    private static int Main(string[] args)
    {
        var port = Protocol.DefaultPort;
        var compress = false;

        var next = 0;
        while (next < args.Length)
        {
            var option = args[next++];
            switch (option)
            {
                case "--compress" or "-c":
                    compress = true;
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

        try
        {
            ScreenServer.Run(port, compress);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
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
