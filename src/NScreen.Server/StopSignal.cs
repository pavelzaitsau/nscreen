namespace NScreen.Server;

/// <summary>
/// Ctrl+C for a server that has no console to press it in. A named event, set by <c>--stop</c> and
/// waited on by one background thread, so a headless server tears the socket and the duplication
/// object down exactly the way an interactive one does.
/// </summary>
internal static class StopSignal
{
    /// <summary>
    /// Session-scoped on purpose. A <c>Global\</c> name needs SeCreateGlobalPrivilege, which an
    /// ordinary user does not have, and a server is started from the session whose screen it shares.
    /// </summary>
    private const string Name = "nscreen-server-stop";

    /// <summary>
    /// Starts watching for a stop. The returned handle is the event itself: dispose it to release
    /// the name, which also releases the thread.
    /// </summary>
    public static EventWaitHandle Start(CancellationTokenSource shutdown)
    {
        var stop = new EventWaitHandle(false, EventResetMode.ManualReset, Name);

        new Thread(() => Wait(stop, shutdown))
        {
            IsBackground = true,
            Name = "nscreen-stop",
        }.Start();

        return stop;
    }

    /// <summary>Sets the event another process is waiting on. This is the whole of <c>--stop</c>.</summary>
    public static int Send()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(Name, out var stop))
            {
                Console.Error.WriteLine("No nscreen-server is running in this session.");
                return 1;
            }

            using (stop)
            {
                stop.Set();
            }

            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            // Mandatory integrity control, not a permission that can be granted: a medium process
            // cannot signal an object a high one created, whatever the object's own ACL says.
            Console.Error.WriteLine(
                "That server runs elevated (--system), so --stop needs an elevated prompt too.");
            Console.Error.WriteLine("  or:  taskkill /IM nscreen-server.exe");
            return 1;
        }
    }

    private static void Wait(EventWaitHandle stop, CancellationTokenSource shutdown)
    {
        try
        {
            // The token's own handle is in the set so that a Ctrl+C shutdown takes this thread with
            // it, rather than leaving it parked on a handle the caller is about to dispose.
            if (WaitHandle.WaitAny([stop, shutdown.Token.WaitHandle]) == 0)
            {
                shutdown.Cancel();
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            // Shutdown won the race. Nothing here needs doing, but an unhandled throw on a
            // background thread would take the process down mid-teardown.
        }
    }
}
