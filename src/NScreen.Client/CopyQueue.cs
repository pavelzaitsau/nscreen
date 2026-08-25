namespace NScreen.Client;

/// <summary>
/// Runs one copy at a time, in the order the copies were started, and drops the ones a later copy
/// has already replaced. Without the ordering, two copies race on the clipboard and the one that
/// happens to finish last wins - which is the older selection whenever recognition is involved,
/// because OCR takes a few hundred milliseconds and a picture takes single-digit ones.
/// <para>
/// UI thread only: the counter and the chain are plain fields, and every await returns to the
/// thread that started the copy.
/// </para>
/// </summary>
internal sealed class CopyQueue
{
    private Task _previous = Task.CompletedTask;
    private int _started;

    /// <summary>
    /// Waits for the copies before this one, then runs <paramref name="copy"/> - unless another
    /// copy has started in the meantime, in which case this one is not worth doing and
    /// <paramref name="superseded"/> comes back instead.
    /// </summary>
    /// <param name="copy">The work, run at most once.</param>
    /// <param name="superseded">What to return when a later copy took over.</param>
    public async Task<string> RunAsync(Func<Task<string>> copy, string superseded)
    {
        var ticket = ++_started;
        var queued = _previous;

        // The chain is handed on before the first await, so the next caller queues behind this copy
        // rather than beside it. It completes rather than faults, so one failed copy does not take
        // the queue down with it.
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _previous = finished.Task;

        try
        {
            await queued.ConfigureAwait(true);
            return ticket == _started ? await copy().ConfigureAwait(true) : superseded;
        }
        finally
        {
            finished.SetResult();
        }
    }
}
