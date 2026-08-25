using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace NScreen.Server;

/// <summary>
/// The two things <c>--headless</c> and <c>--system</c> ask for that a running process cannot give
/// itself: no console, and an elevated token. Both are fixed at process creation, so both are
/// served by starting the exe again and letting the first one exit - which is also what hands the
/// shell its prompt back instead of parking on a server that no longer prints anything.
/// </summary>
internal static class Launcher
{
    /// <summary>
    /// Marks a process that a relaunch already produced. Without it the child would look at the
    /// same flags, reach the same conclusion, and start a third process.
    /// </summary>
    public const string RelaunchedMarker = "--relaunched";

    /// <summary>
    /// True once UAC is past. An administrator's unelevated token has the group filtered out, which
    /// is exactly the answer wanted here: not elevated yet, so ask.
    /// </summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Raises the whole process to High, which is the ceiling short of realtime.</summary>
    /// <remarks>
    /// Realtime is deliberately not offered: the capture loop would outrank the compositor and the
    /// input stack of the very machine whose screen is being shared. The capture thread keeps the
    /// default priority within the process - there is only one thread doing work, so a thread-level
    /// bump would buy nothing that the process class has not already bought.
    /// </remarks>
    public static void RaisePriority()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            self.PriorityClass = ProcessPriorityClass.High;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            Log.Line($"Could not raise the priority ({ex.Message}). Serving at the default instead.");
        }
    }

    /// <summary>
    /// Starts this exe again with the same arguments and exits. Returns the code for the process
    /// that is going away, not for the server.
    /// </summary>
    /// <param name="args">The original command line, forwarded verbatim.</param>
    /// <param name="elevate">Ask UAC for an administrator token.</param>
    /// <param name="hidden">Give the child no console window of its own.</param>
    public static int Relaunch(string[] args, bool elevate, bool hidden)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            Console.Error.WriteLine("Cannot find this executable's own path, so it cannot restart itself.");
            return 1;
        }

        var start = new ProcessStartInfo(exe)
        {
            // ShellExecute is the only way to ask for the elevated token, and it is also the one
            // that ignores CreateNoWindow - so hiding goes through the window style, which it does
            // honour, and CreateNoWindow covers the plain case where there is no shell involved.
            UseShellExecute = elevate,
            Verb = elevate ? "runas" : string.Empty,
            CreateNoWindow = !elevate && hidden,
            WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
        };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.ArgumentList.Add(RelaunchedMarker);

        try
        {
            using var child = Process.Start(start);
            if (child is null)
            {
                Console.Error.WriteLine("Windows reused an existing process instead of starting the server.");
                return 1;
            }

            Announce(child.Id, elevate, hidden);
            return 0;
        }
        catch (Win32Exception ex)
        {
            // Declining the UAC prompt lands here as ERROR_CANCELLED, and reads plainly enough.
            Console.Error.WriteLine($"Could not start the server: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Says where the server went. The parent is the only part of a headless start that has a
    /// console at all, so this line is the one chance to name the log file and the way to stop it.
    /// </summary>
    private static void Announce(int pid, bool elevate, bool hidden)
    {
        if (!hidden)
        {
            Console.WriteLine($"nscreen-server restarted with administrator rights in its own window (pid {pid}).");
            return;
        }

        Console.WriteLine($"nscreen-server is running in the background{(elevate ? " as administrator" : string.Empty)}, pid {pid}.");
        Console.WriteLine($"  log:   {Log.FilePath}");
        Console.WriteLine("  stop:  nscreen-server --stop");
    }
}
