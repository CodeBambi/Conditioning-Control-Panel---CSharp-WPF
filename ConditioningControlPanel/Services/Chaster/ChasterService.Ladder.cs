using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The Locktober raffle on Circe's Tab. The "time CCP added" totals shown on the page are
/// this machine's own count (display only); the raffle's days and total are the ones the server
/// reads off the lock's Chaster history after a push lands and when the page opens
/// (<see cref="ChasterRaffle"/>). Every failure is quiet: no raffle is a page with no card.</summary>
public sealed partial class ChasterService
{
    private readonly SemaphoreSlim _ladderGate = new(1, 1);
    private DateTime? _ladderVerifiedAtUtc;
    private Timer? _ladderRetry;

    /// <summary>Null = no ladder (tests, the DEBUG demo).</summary>
    public IChasterLadderApi? LadderApi { get; init; }

    /// <summary>The player's "post my days in Discord" switch, read at each refresh.</summary>
    public Func<bool>? RafflePostDays { get; init; }

    /// <summary>The player's "show my name on the ladder" switch, read at each board read.</summary>
    public Func<bool>? LadderShowName { get; init; }

    /// <summary>What the server last read off Chaster for this month, or null when it has not.</summary>
    public LadderVerify? LastLadderVerify { get; private set; }

    /// <summary>Lifetime seconds CCP added to the lock. Added only: nothing taken off counts.</summary>
    public long AddedLifetimeSeconds { get { lock (_gate) return ChasterLadder.Lifetime(_tab); } }

    /// <summary>This UTC month's adds, as this machine counted them.</summary>
    public int AddedThisMonthSeconds { get { lock (_gate) return ChasterLadder.ThisMonth(_tab, _utcNow()); } }

    private void LadderPushLanded() => _ = VerifyLadderAsync(CancellationToken.None);

    /// <summary>Have the server read this month off the chosen lock, at most every 15 minutes.
    /// Only while the link is live and a lock is picked.</summary>
    public async Task VerifyLadderAsync(CancellationToken ct)
    {
        if (LadderApi == null || !IsLinked) return;
        var lockId = (_options() ?? ChasterOptions.Off).LockId;
        if (string.IsNullOrEmpty(lockId)) return;
        await _ladderGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = _utcNow();
            if (!ChasterLadder.VerifyDue(_ladderVerifiedAtUtc, now))
            {
                ArmLadderRetry(ChasterLadder.VerifyAt(_ladderVerifiedAtUtc, now), now);
                return;
            }
            var access = await AccessTokenAsync(ct).ConfigureAwait(false);
            if (access == null) return;
            _ladderVerifiedAtUtc = now;
            var verdict = await LadderApi.VerifyAsync(lockId!, access, ct).ConfigureAwait(false);
            if (verdict != null) LastLadderVerify = verdict;
        }
        catch (Exception ex) { Diag.Swallowed(ex, "ladder verify skipped"); }
        finally { _ladderGate.Release(); }
    }

    /// <summary>The player's raffle card for the page, after a due verify. The server's copy of
    /// the "post my days" switch is brought back in line with the player's when they differ.</summary>
    public async Task<RaffleCard?> RaffleAsync(CancellationToken ct = default)
    {
        if (LadderApi == null) return null;
        await VerifyLadderAsync(ct).ConfigureAwait(false);
        var card = await LadderApi.MeAsync(ct).ConfigureAwait(false);
        var want = RafflePostDays?.Invoke() == true;
        if (card != null && card.PostDays != want && await LadderApi.OptInAsync(want, ct).ConfigureAwait(false))
            card = card with { PostDays = want };
        return card;
    }

    /// <summary>The month's top ten for the pinned scrap. The raffle read verifies first, so this
    /// one does not. The server's copy of the name switch is brought back in line with the
    /// player's when they differ.</summary>
    public async Task<LadderBoard?> LadderAsync(CancellationToken ct = default)
    {
        if (LadderApi == null) return null;
        var board = await LadderApi.TopAsync(ct).ConfigureAwait(false);
        var want = LadderShowName?.Invoke() == true;
        if (board != null && board.ShowName != want && await LadderApi.ShowNameAsync(want, ct).ConfigureAwait(false))
            board = await LadderApi.TopAsync(ct).ConfigureAwait(false) ?? board;
        return board;
    }

    /// <summary>The "show my name" toggle. True when the server took it.</summary>
    public Task<bool> SetLadderShowNameAsync(bool show, CancellationToken ct = default) =>
        LadderApi == null ? Task.FromResult(false) : LadderApi.ShowNameAsync(show, ct);

    /// <summary>The page's toggle. True when the server took it.</summary>
    public Task<bool> SetRafflePostDaysAsync(bool post, CancellationToken ct = default) =>
        LadderApi == null ? Task.FromResult(false) : LadderApi.OptInAsync(post, ct);

    // A push that landed inside the 15 minutes is verified when they are up, not dropped.
    private void ArmLadderRetry(DateTime? at, DateTime now)
    {
        if (at is not { } when) return;
        var delay = when - now;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        var timer = new Timer(_ => _ = VerifyLadderAsync(CancellationToken.None), null, delay + TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        Interlocked.Exchange(ref _ladderRetry, timer)?.Dispose();
    }

    private void DisposeLadder() => Interlocked.Exchange(ref _ladderRetry, null)?.Dispose();
}
