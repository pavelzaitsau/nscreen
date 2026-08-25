namespace NScreen.Server;

/// <summary>
/// Where a diagnostic goes. With a console it goes there and reads exactly as it always has; under
/// <c>--headless</c> there is no console to write to, so events land in a log file next to the exe
/// and the per-second fps line is dropped - it overwrites itself for a human watching, and means
/// nothing in a file.
/// </summary>
internal static class Log
{
    /// <summary>A log here is a diagnostic, not a record: past this the file restarts rather than grows.</summary>
    private const long MaxBytes = 1 << 20;

    private static readonly Lock Gate = new();

    /// <summary>Null while the console is the sink, which is also the fallback if the file fails.</summary>
    private static string? Sink;

    /// <summary><c>nscreen-server.log</c> beside the executable, wherever it was copied to.</summary>
    public static string FilePath { get; } =
        Path.ChangeExtension(Environment.ProcessPath ?? "nscreen-server", ".log");

    /// <summary>True once the sink is the file, which is also what says there is nobody watching.</summary>
    public static bool IsFile => Sink is not null;

    /// <summary>Sends everything from here on to <see cref="FilePath"/> instead of a console.</summary>
    public static void ToFile() => Sink = FilePath;

    /// <summary>A line that stands on its own, such as the startup banner.</summary>
    public static void Line(string message)
    {
        var file = Sink;
        if (file is null)
        {
            Console.WriteLine(message);
            return;
        }

        Append(file, message);
    }

    /// <summary>Something that happened at a moment worth recording.</summary>
    public static void Event(string message)
    {
        var file = Sink;
        if (file is null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            return;
        }

        Append(file, message);
    }

    public static void Error(string message)
    {
        var file = Sink;
        if (file is null)
        {
            Console.Error.WriteLine(message);
            return;
        }

        Append(file, message);
    }

    /// <summary>
    /// The live fps/bitrate line, which owns its own carriage return because each one replaces the
    /// last. Console-only by nature, so <c>--headless</c> drops it entirely.
    /// </summary>
    public static void Status(string message)
    {
        if (Sink is null)
        {
            Console.Write($"\r  {message}   ");
        }
    }

    private static void Append(string file, string message)
    {
        lock (Gate)
        {
            // Re-read the field: a failed write below turns the file sink off for good, and the
            // discovery thread logs through here too.
            if (Sink is null)
            {
                return;
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}";
            try
            {
                // Exists first: FileInfo.Length throws where the file is not there yet, and that
                // throw is an IOException - it would disable the log on its very first line.
                var existing = new FileInfo(file);
                if (existing.Exists && existing.Length > MaxBytes)
                {
                    File.WriteAllText(file, line);
                }
                else
                {
                    File.AppendAllText(file, line);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // A log is a convenience and must never take the screen stream down with it. One
                // failure is enough to stop trying: a folder the exe cannot write to - Program
                // Files, most likely - is not going to become writable while the server runs.
                Sink = null;
            }
        }
    }
}
