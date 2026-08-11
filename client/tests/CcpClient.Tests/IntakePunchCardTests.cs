using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-054: the punch card (IntakePunchCardService.cs port): first-hole-free, pending-draft
/// queueing + the 8/90-day eviction (the honest bound on "punches pend silently"), the
/// ≥50%/natural-end redemption seams, 8th-hole completion, prize-claim-once, the load
/// REPAIRS, and the SP-005 round-trip.
/// </summary>
public sealed class IntakePunchCardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-sp054-punch-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];
    private DateTimeOffset _now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() { try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ } }

    private sealed class SinkAdapter(List<string> lines) : ILogSink
    {
        public void Log(string message) => lines.Add(message);
    }

    private (PersistenceStore<IntakePunchCardDocument> Store, IntakePunchCard Card) NewCard(string? dir = null)
    {
        var store = new PersistenceStore<IntakePunchCardDocument>(
            new OperationRegistry().OwnerFor("IntakePunchCard"),
            new SinkAdapter(_log),
            Path.Combine(dir ?? Path.Combine(_root, Guid.NewGuid().ToString("N")), "intake_punchcard.json"),
            IntakePunchCardDocument.CurrentSchemaVersion);
        store.StartAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        return (store, new IntakePunchCard(store, () => _now, _log.Add));
    }

    [Fact]
    public void First_Hole_Free_And_Idempotent()
    {
        var (store, card) = NewCard();
        card.EnsureCardStarted();
        card.EnsureCardStarted();
        Assert.False(card.IsComplete);
        Assert.False(card.PrizeAwaitingClaim);
        Assert.Equal(1, store.Current.PunchedCount);
        Assert.Single(store.Current.PunchedUtc);
        Assert.NotNull(store.Current.CardStartedUtc);
    }

    [Fact]
    public void Notify_Queues_Pending_Dedupes_And_Evicts_Oldest_Beyond_8()
    {
        var (store, card) = NewCard();
        card.EnsureCardStarted();
        card.NotifyIntakeCompleted("s1");
        card.NotifyIntakeCompleted("s1"); // duplicate Ordinal — no second stamp
        card.NotifyIntakeCompleted("");   // blank — no-op
        card.NotifyIntakeCompleted(null);
        Assert.Single(store.Current.PendingDrafts);

        for (var i = 2; i <= 10; i++)
        {
            card.NotifyIntakeCompleted($"s{i}");
        }

        // 9 distinct ids queued; the cap keeps the NEWEST 8 (s1 evicted — "the newest
        // draft is the one the user is most likely to run").
        Assert.Equal(IntakePunchCard.MaxPendingDrafts, store.Current.PendingDrafts.Count);
        Assert.DoesNotContain(store.Current.PendingDrafts, d => d.SessionId == "s1");
        Assert.Contains(store.Current.PendingDrafts, d => d.SessionId == "s10");
    }

    [Fact]
    public void Redemption_Seam_Progress_Threshold_And_Natural_End()
    {
        var (store, card) = NewCard();
        card.EnsureCardStarted();
        card.NotifyIntakeCompleted("draft-a");
        card.NotifySessionProgress("draft-a", 49.9); // below the 50% credit line
        Assert.Single(store.Current.PendingDrafts);
        Assert.Equal(1, store.Current.PunchedCount);

        card.NotifySessionProgress("draft-a", 50.0); // at the line — redeems
        Assert.Empty(store.Current.PendingDrafts);
        Assert.Equal(2, store.Current.PunchedCount);

        card.NotifyIntakeCompleted("draft-b");
        card.NotifySessionCompleted("draft-b"); // natural end — regardless of percent
        Assert.Equal(3, store.Current.PunchedCount);

        card.NotifySessionProgress("nobody", 100.0); // unknown id — no-op
        Assert.Equal(3, store.Current.PunchedCount);
    }

    [Fact]
    public void Eighth_Hole_Completes_Once_And_Prize_Claims_Once()
    {
        var (store, card) = NewCard();
        var completed = 0;
        card.PunchCardCompleted += () => completed++;
        card.EnsureCardStarted(); // hole 1 (free)
        for (var i = 0; i < 7; i++)
        {
            card.NotifyIntakeCompleted($"d{i}");
            card.NotifySessionCompleted($"d{i}"); // holes 2..8
        }

        Assert.True(card.IsComplete);
        Assert.NotNull(store.Current.CompletedUtc);
        Assert.Equal(8, store.Current.PunchedCount);
        Assert.Equal(1, completed);
        Assert.True(card.PrizeAwaitingClaim);

        // Complete card: further intakes queue nothing.
        card.NotifyIntakeCompleted("late");
        Assert.Empty(store.Current.PendingDrafts);

        card.MarkPrizeClaimed();
        Assert.NotNull(store.Current.PrizeClaimedUtc);
        Assert.False(card.PrizeAwaitingClaim);
        var claimed = store.Current.PrizeClaimedUtc;
        card.MarkPrizeClaimed(); // once only
        Assert.Equal(claimed, store.Current.PrizeClaimedUtc);
    }

    [Fact]
    public async Task Card_Survives_A_Store_Reload()
    {
        var dir = Path.Combine(_root, "reload");
        var (store, card) = NewCard(dir);
        card.EnsureCardStarted();
        card.NotifyIntakeCompleted("keepme");
        await store.SaveImmediate();
        await store.StopAsync();

        var (reloaded, card2) = NewCard(dir);
        Assert.Equal(LoadOutcome.Loaded.Instance, reloaded.LastLoadOutcome);
        Assert.Equal(1, reloaded.Current.PunchedCount);
        Assert.Single(reloaded.Current.PendingDrafts);
        Assert.Equal("keepme", reloaded.Current.PendingDrafts[0].SessionId);
    }

    // ---------- the load repairs (pure) ----------

    [Fact]
    public void Repair_Clamps_Backfills_CompletedUtc_And_Heals_Null_Lists()
    {
        var doc = new IntakePunchCardDocument
        {
            PunchedCount = 99,
            PunchedUtc = null!,
            PendingDrafts = null!,
        };
        Assert.True(IntakePunchCard.Repair(doc, _now));
        Assert.Equal(IntakePunchCard.TotalHoles, doc.PunchedCount);
        Assert.Equal(_now, doc.CompletedUtc); // full card without a stamp — backfilled
        Assert.NotNull(doc.PunchedUtc);
        Assert.NotNull(doc.PendingDrafts);
    }

    [Fact]
    public void Repair_Backfills_CompletedUtc_From_The_Last_Punch()
    {
        var last = _now.AddDays(-2);
        var doc = new IntakePunchCardDocument { PunchedCount = 8, PunchedUtc = [_now.AddDays(-5), last] };
        Assert.True(IntakePunchCard.Repair(doc, _now));
        Assert.Equal(last, doc.CompletedUtc);
    }

    [Fact]
    public void Repair_Evicts_Blank_And_90Day_Drafts_And_Front_Trims()
    {
        var doc = new IntakePunchCardDocument { PunchedCount = 2 };
        for (var i = 0; i < 12; i++)
        {
            doc.PendingDrafts.Add(new IntakePendingDraft { SessionId = $"d{i}", DraftedUtc = _now.AddDays(-1) });
        }

        doc.PendingDrafts.Add(new IntakePendingDraft { SessionId = " ", DraftedUtc = _now });
        doc.PendingDrafts.Add(new IntakePendingDraft { SessionId = "ancient", DraftedUtc = _now.AddDays(-91) });
        Assert.True(IntakePunchCard.Repair(doc, _now));
        Assert.Equal(IntakePunchCard.MaxPendingDrafts, doc.PendingDrafts.Count);
        Assert.DoesNotContain(doc.PendingDrafts, d => d.SessionId == "ancient");
        Assert.DoesNotContain(doc.PendingDrafts, d => string.IsNullOrWhiteSpace(d.SessionId));
        // Front-trimmed: the oldest queued ids are gone, the newest survive.
        Assert.DoesNotContain(doc.PendingDrafts, d => d.SessionId == "d0");
        Assert.Contains(doc.PendingDrafts, d => d.SessionId == "d11");
    }

    [Fact]
    public void Repair_Clean_Document_Is_A_No_Op()
    {
        var doc = new IntakePunchCardDocument { PunchedCount = 3 };
        doc.PendingDrafts.Add(new IntakePendingDraft { SessionId = "fresh", DraftedUtc = _now.AddDays(-1) });
        Assert.False(IntakePunchCard.Repair(doc, _now));
    }

    [Fact]
    public void Eviction_Caps_Are_The_WPF_Constants()
    {
        Assert.Equal(8, IntakePunchCard.TotalHoles);
        Assert.Equal(50.0, IntakePunchCard.SessionCreditPercent);
        Assert.Equal(8, IntakePunchCard.MaxPendingDrafts);
        Assert.Equal(TimeSpan.FromDays(90), IntakePunchCard.PendingDraftMaxAge);
    }
}
