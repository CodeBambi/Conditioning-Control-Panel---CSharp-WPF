using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE HANDOFF (CONTRACT-FUSE-0816 §2.3) — what the fuse window does between its bloom and getting
/// off the screen.
///
/// <para>This is the part of the night with a network in it, which is why it is a state machine
/// rather than a few timers scattered through a render loop: the ceremony may arrive in two
/// seconds, in forty, or not at all, and all three have to end with the fullscreen window gone and
/// nothing written. The machine is driven at fifty frames a second by the window, so the tests
/// below drive it the same way — an action that fired twice here would fire fifty times there.</para>
/// </summary>
public class DescentFuseHandoffTests
{
    /// <summary>Drive the machine from t0 to t1 at the window's real cadence, collecting actions.</summary>
    private static System.Collections.Generic.List<DescentHandoffAction> Run(
        DescentFuseHandoff handoff, double from, double to, bool ceremonyOpen, double step = 0.02)
    {
        var seen = new System.Collections.Generic.List<DescentHandoffAction>();
        // Index-derived rather than accumulated: two thousand additions of 0.02 drift far enough to
        // land on the wrong side of an 8-second slot boundary, and a test that flakes on the timing
        // it is meant to pin is worse than no test.
        var frames = (int)System.Math.Round((to - from) / step);
        for (var i = 0; i <= frames; i++)
        {
            var action = handoff.Advance(from + i * step, ceremonyOpen);
            if (action != DescentHandoffAction.Wait) seen.Add(action);
        }
        return seen;
    }

    // ------------------------------------------------------------ before the bloom

    /// <summary>
    /// Nothing happens before the bloom. The window drives this machine from its very first frame
    /// so it does not need a guard of its own, which means the machine needs one.
    /// </summary>
    [Fact]
    public void BeforeTheBloom_DoesNothing()
    {
        var handoff = new DescentFuseHandoff();
        Assert.Equal(DescentHandoffAction.Wait, handoff.Advance(-5.2, ceremonyOpen: false));
        Assert.Equal(DescentHandoffAction.Wait, handoff.Advance(-0.01, ceremonyOpen: true));
        Assert.False(handoff.IsHandingOff);
        Assert.False(handoff.IsDone);
    }

    // ------------------------------------------------------------ the sync cadence

    /// <summary>
    /// A sync is asked for on the FIRST frame of the bloom. §2.3's "force an immediate sync" — the
    /// ceremony cannot be offered on a conversation that never happens.
    /// </summary>
    [Fact]
    public void FirstBloomFrame_AsksForASync()
    {
        var handoff = new DescentFuseHandoff();
        Assert.Equal(DescentHandoffAction.Resync, handoff.Advance(0.0, ceremonyOpen: false));
    }

    /// <summary>
    /// And then again on a cadence, because ProfileSyncService enforces a thirty-second client-side
    /// cooldown: the first ask is quite likely to be refused outright on a healthy install that
    /// synced during the countdown's last minute. Asking once and waiting in silence would MISS the
    /// happy path on a perfectly good machine — the retries are what make it work.
    /// </summary>
    [Fact]
    public void WhileWaiting_RetriesOnACadenceThatOutlastsTheSyncCooldown()
    {
        var handoff = new DescentFuseHandoff();
        var actions = Run(handoff, 0, 40, ceremonyOpen: false);

        Assert.All(actions, a => Assert.Equal(DescentHandoffAction.Resync, a));

        // 0, 8, 16, 24, 32, 40 — six asks inside forty seconds, so at least one lands outside a
        // thirty-second cooldown no matter when the last sync happened.
        Assert.Equal(6, actions.Count);
    }

    /// <summary>
    /// The cadence is scheduled from the SLOT, not from the frame that noticed. A late frame (the
    /// app was busy) must not push every later attempt back with it.
    /// </summary>
    [Fact]
    public void RetryCadence_DoesNotDriftWithLateFrames()
    {
        var handoff = new DescentFuseHandoff();
        Assert.Equal(DescentHandoffAction.Resync, handoff.Advance(0.0, false));

        // A three-second stall, so the second slot is noticed late.
        Assert.Equal(DescentHandoffAction.Resync, handoff.Advance(11.0, false));

        // The third slot is still 16, not 19.
        Assert.Equal(DescentHandoffAction.Wait, handoff.Advance(15.9, false));
        Assert.Equal(DescentHandoffAction.Resync, handoff.Advance(16.0, false));
    }

    // ------------------------------------------------------------ the happy path

    /// <summary>
    /// The ceremony comes up: crossfade ONCE, wait exactly a second, close. The one-second number
    /// is §2.3's, and the "once" is what stops a 50fps loop restarting the fade fifty times.
    /// </summary>
    [Fact]
    public void CeremonyArrives_CrossfadesOnceThenCloses()
    {
        var handoff = new DescentFuseHandoff();

        Assert.Equal(DescentHandoffAction.Resync, handoff.Advance(0.0, false));
        Assert.Equal(DescentHandoffAction.CrossfadeToCeremony, handoff.Advance(3.0, ceremonyOpen: true));
        Assert.True(handoff.IsHandingOff);

        // Not a second time, however many frames arrive.
        var during = Run(handoff, 3.02, 3.98, ceremonyOpen: true);
        Assert.Empty(during);

        Assert.Equal(DescentHandoffAction.Close, handoff.Advance(4.0, ceremonyOpen: true));
        Assert.True(handoff.IsDone);

        // And nothing after Done, ever.
        Assert.Empty(Run(handoff, 4.02, 60.0, ceremonyOpen: true));
    }

    /// <summary>The crossfade's progress runs 0..1 across exactly its one second, and only while
    /// the machine is actually handing off.</summary>
    [Fact]
    public void CrossfadeProgress_TracksTheFadeAndNothingElse()
    {
        var handoff = new DescentFuseHandoff();
        Assert.Equal(0.0, handoff.CrossfadeProgress(2.0), 4);

        handoff.Advance(2.0, ceremonyOpen: true);
        Assert.Equal(0.0, handoff.CrossfadeProgress(2.0), 4);
        Assert.Equal(0.5, handoff.CrossfadeProgress(2.5), 4);
        Assert.Equal(1.0, handoff.CrossfadeProgress(3.5), 4);
    }

    /// <summary>
    /// The ceremony BEATS the clock on a tie. A subject who gets both an offer and a timeout on the
    /// same frame should get the ceremony — the line is the consolation, not the outcome.
    /// </summary>
    [Fact]
    public void OnATie_TheCeremonyWins()
    {
        var handoff = new DescentFuseHandoff();
        Assert.Equal(DescentHandoffAction.CrossfadeToCeremony,
            handoff.Advance(DescentFuseHandoff.TimeoutSeconds, ceremonyOpen: true));
        Assert.True(handoff.IsHandingOff);
        Assert.False(handoff.IsAnnouncing);
    }

    // ------------------------------------------------------------ the timeout

    /// <summary>
    /// The timeout with no offer: say the standing line, hold it four seconds, close. §2.3's
    /// shape — and note what does NOT happen, which is anything being written or retried forever.
    /// The re-offer contract (and, since 0825, the director's bounded post-zero retry) takes it
    /// from there.
    /// </summary>
    [Fact]
    public void NoOfferByTheTimeout_SaysTheLineThenCloses()
    {
        var t = DescentFuseHandoff.TimeoutSeconds;
        var handoff = new DescentFuseHandoff();
        var actions = Run(handoff, 0, t - 0.1, ceremonyOpen: false);
        Assert.All(actions, a => Assert.Equal(DescentHandoffAction.Resync, a));

        Assert.Equal(DescentHandoffAction.SpeakAwaits, handoff.Advance(t, false));
        Assert.True(handoff.IsAnnouncing);

        // Held, silently, for the four seconds. No more syncs — the machine has stopped asking.
        Assert.Empty(Run(handoff, t + 0.02, t + 3.9, ceremonyOpen: false));

        Assert.Equal(DescentHandoffAction.Close, handoff.Advance(t + 4.0, false));
        Assert.True(handoff.IsDone);
    }

    /// <summary>
    /// 0825 D1: the timeout must clear TWO client-side sync cooldowns (30s each) plus the bloom,
    /// or a client that synced in the last half-minute before zero, and then ate one 429, misses
    /// the happy path on a perfectly healthy night.
    /// </summary>
    [Fact]
    public void Timeout_ClearsTwoSyncCooldowns()
    {
        Assert.True(DescentFuseHandoff.TimeoutSeconds >= 2 * 30 + 8);
    }

    /// <summary>
    /// An offer that lands DURING the four-second hold does not rescue the window. It is already
    /// leaving; the ceremony simply opens over the top, which is the right outcome and needs no
    /// branch. What matters is that the window still closes rather than getting stuck announcing.
    /// </summary>
    [Fact]
    public void LateOffer_DoesNotStrandTheWindow()
    {
        var t = DescentFuseHandoff.TimeoutSeconds;
        var handoff = new DescentFuseHandoff();
        Run(handoff, 0, t - 0.1, ceremonyOpen: false);
        Assert.Equal(DescentHandoffAction.SpeakAwaits, handoff.Advance(t, false));

        var late = Run(handoff, t + 0.02, t + 3.9, ceremonyOpen: true);
        Assert.Empty(late);

        Assert.Equal(DescentHandoffAction.Close, handoff.Advance(t + 4.0, ceremonyOpen: true));
        Assert.True(handoff.IsDone);
    }

    /// <summary>
    /// EVERY path ends closed. This is the one property that must hold: a fullscreen topmost window
    /// that never closes is only escapable by the panic key, and the whole feature would be
    /// remembered for that instead of for the show.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EveryPathEndsClosed(bool ceremonyArrives)
    {
        var handoff = new DescentFuseHandoff();
        Run(handoff, 0, 120, ceremonyArrives, step: 0.05);
        Assert.True(handoff.IsDone);
    }
}
