using CcpClient.Desktop.Features.Dtrh;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// §3.3 inbox contract (dtrh-admission.md): monotonic seq, retained-until-ack,
/// long-poll with bounded timeout, replay-equivalence for a lost response.
/// </summary>
public class DtrhInboxTests
{
    [Fact]
    public void Enqueue_AssignsMonotonicSeq_StartingAtOne()
    {
        var inbox = new Inbox();
        Assert.Equal(1, inbox.Enqueue("{\"type\":\"a\"}"));
        Assert.Equal(2, inbox.Enqueue("{\"type\":\"b\"}"));
        Assert.Equal(3, inbox.Enqueue("{\"type\":\"c\"}"));
    }

    [Fact]
    public async Task Poll_ReturnsAllMessagesAfterSeq_InOrder()
    {
        var inbox = new Inbox();
        inbox.Enqueue("{\"type\":\"init\"}");
        inbox.Enqueue("{\"type\":\"manifest\"}");
        var messages = await inbox.PollAsync(0, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Equal(new long[] { 1, 2 }, messages.Select(m => m.Seq).ToArray());
        Assert.Equal("{\"type\":\"init\"}", messages[0].Body);
    }

    [Fact]
    public async Task Poll_AcknowledgesByAfter_PurgesAcked()
    {
        var inbox = new Inbox();
        inbox.Enqueue("{\"type\":\"a\"}");
        inbox.Enqueue("{\"type\":\"b\"}");
        var first = await inbox.PollAsync(0, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Equal(2, first.Count);
        // Ack seq<=1: the next poll returns only seq 2.
        var second = await inbox.PollAsync(1, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Equal(2, second.Single().Seq);
        // Ack seq<=2: nothing retained.
        var third = await inbox.PollAsync(2, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Empty(third);
    }

    [Fact]
    public async Task LostResponse_Immunity_UnackedMessagesAreRetained()
    {
        // The page polled, the response was lost (renderer stall) — the page re-polls with
        // the SAME after and must receive the same messages again (exactly-once at the page).
        var inbox = new Inbox();
        inbox.Enqueue("{\"type\":\"init\"}");
        var first = await inbox.PollAsync(0, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        var replay = await inbox.PollAsync(0, TimeSpan.FromMilliseconds(50), CancellationToken.None);
        Assert.Equal(first.Select(m => m.Seq), replay.Select(m => m.Seq));
    }

    [Fact]
    public async Task LongPoll_HangsUntilMessageArrives()
    {
        var inbox = new Inbox();
        var poll = inbox.PollAsync(0, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(poll.IsCompleted);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        inbox.Enqueue("{\"type\":\"init\"}");
        var messages = await poll.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Single(messages);
    }

    [Fact]
    public async Task LongPoll_BoundedTimeout_ReturnsEmpty()
    {
        var inbox = new Inbox();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var messages = await inbox.PollAsync(0, TimeSpan.FromMilliseconds(150), CancellationToken.None);
        elapsed.Stop();
        Assert.Empty(messages);
        Assert.True(elapsed.ElapsedMilliseconds >= 100, $"long-poll returned too early: {elapsed.ElapsedMilliseconds}ms");
        Assert.True(elapsed.ElapsedMilliseconds < 2000, $"long-poll blew its bound: {elapsed.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ReleaseAll_CompletesHangingPollers_Empty()
    {
        var inbox = new Inbox();
        var poll = inbox.PollAsync(0, TimeSpan.FromSeconds(30), CancellationToken.None);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        inbox.ReleaseAll();
        var messages = await poll.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Empty(messages);
    }
}
