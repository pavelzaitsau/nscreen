using System.Threading.Channels;
using NScreen.Client;

namespace NScreen.Tests;

/// <summary>
/// The rule the clipboard depends on: copies land in the order they were started, and a copy that a
/// later one replaced never runs. Recognition takes a few hundred milliseconds and a picture takes
/// single-digit ones, so without the queue the older selection wins whenever the two overlap.
/// </summary>
[TestClass]
public sealed class CopyQueueTests
{
    private const string Superseded = "superseded";

    [TestMethod]
    public async Task A_single_copy_runs_and_returns_its_own_answer()
    {
        var queue = new CopyQueue();

        var result = await queue.RunAsync(() => Task.FromResult("copied"), Superseded).ConfigureAwait(false);

        Assert.AreEqual("copied", result);
    }

    // A copy already running finishes; the one behind it starts only afterwards.
    [TestMethod]
    public async Task A_queued_copy_waits_for_the_one_in_front_of_it()
    {
        // A channel rather than a semaphore: the gate has to outlive several awaits, and a
        // disposable one would put those awaits inside its using scope.
        var gate = Channel.CreateUnbounded<bool>();
        var queue = new CopyQueue();
        var order = new List<string>();

        var first = queue.RunAsync(() => HeldAsync(gate, order, "first"), Superseded);
        var second = queue.RunAsync(() => RecordAsync(order, "second"), Superseded);

        CollectionAssert.AreEqual(Array.Empty<string>(), order);
        gate.Writer.TryWrite(item: true);

        Assert.AreEqual("first", await first.ConfigureAwait(false));
        Assert.AreEqual("second", await second.ConfigureAwait(false));
        CollectionAssert.AreEqual(new[] { "first", "second" }, order);
    }

    // Three drags in a burst: the middle one is dead before it ever reaches the front.
    [TestMethod]
    public async Task Only_the_newest_of_the_copies_queued_behind_one_runs()
    {
        var gate = Channel.CreateUnbounded<bool>();
        var queue = new CopyQueue();
        var order = new List<string>();

        var first = queue.RunAsync(() => HeldAsync(gate, order, "first"), Superseded);
        var second = queue.RunAsync(() => RecordAsync(order, "second"), Superseded);
        var third = queue.RunAsync(() => RecordAsync(order, "third"), Superseded);

        gate.Writer.TryWrite(item: true);

        Assert.AreEqual("first", await first.ConfigureAwait(false));
        Assert.AreEqual(Superseded, await second.ConfigureAwait(false));
        Assert.AreEqual("third", await third.ConfigureAwait(false));
        CollectionAssert.AreEqual(new[] { "first", "third" }, order);
    }

    [TestMethod]
    public async Task The_copies_that_run_run_in_the_order_they_started()
    {
        var queue = new CopyQueue();
        var order = new List<string>();

        await queue.RunAsync(() => RecordAsync(order, "first"), Superseded).ConfigureAwait(false);
        await queue.RunAsync(() => RecordAsync(order, "second"), Superseded).ConfigureAwait(false);

        CollectionAssert.AreEqual(new[] { "first", "second" }, order);
    }

    // A copy that threw must not leave every copy after it stuck behind a faulted chain.
    [TestMethod]
    public async Task A_failed_copy_does_not_block_the_next_one()
    {
        var queue = new CopyQueue();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => queue.RunAsync(() => throw new InvalidOperationException("clipboard"), Superseded))
            .ConfigureAwait(false);

        Assert.AreEqual(
            "copied",
            await queue.RunAsync(() => Task.FromResult("copied"), Superseded).ConfigureAwait(false));
    }

    private static async Task<string> HeldAsync(Channel<bool> gate, List<string> order, string name)
    {
        await gate.Reader.ReadAsync().ConfigureAwait(false);
        order.Add(name);
        return name;
    }

    private static Task<string> RecordAsync(List<string> order, string name)
    {
        order.Add(name);
        return Task.FromResult(name);
    }
}
