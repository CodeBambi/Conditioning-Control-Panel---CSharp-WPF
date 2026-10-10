using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE TUTORIAL THAT HAS TO SHUT UP.
///
/// <para>Wave 3 gave EMI three onboarding nudges: pat me, my cards are over there, that card can be
/// kept. Every one of them is a mascot repeating herself on a timer, which is the single easiest
/// thing in this app to get wrong: a teaching line that outlives the lesson stops being help and
/// becomes the reason she gets turned off. The owner's word for the requirement was "till the user
/// gets the gist, then no more".</para>
///
/// <para><see cref="EmiNudgeMachine"/> is pure for exactly this reason - no timers, no dispatcher,
/// no <c>App</c>, an injectable clock and a world behind an interface - so the stopping conditions
/// can be walked in a millisecond instead of being play-tested over twenty minutes. What is checked
/// here is that she starts, and much more importantly that she stops: at the gist, at the lifetime
/// cap, while anything else owns the screen, and within 90 s of her own last nudge.</para>
/// </summary>
public class EmiNudgeMachineTests
{
    /// <summary>The world, as a bag of fields a test can set. Mirrors EmiState without touching disk.</summary>
    private sealed class FakeWorld : IEmiNudgeWorld
    {
        public int PetsTotal { get; set; }
        public bool PetGistGot { get; set; }
        public int RingOpens { get; set; }
        public bool RingGistGot { get; set; }
        public bool PinGistGot { get; set; }
        public bool Quiet { get; set; } = true;

        public readonly Dictionary<string, int> Fires = new(StringComparer.Ordinal);

        public int FiresOf(string track) => Fires.TryGetValue(track, out var n) ? n : 0;

        public void NoteFire(string track) =>
            Fires[track] = FiresOf(track) + 1;
    }

    /// <summary>A clock a test drives by hand.</summary>
    private sealed class Clock
    {
        public DateTime Utc = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
        public DateTime Now() => Utc;
        public void Advance(double ms) => Utc = Utc.AddMilliseconds(ms);
    }

    private static (EmiNudgeMachine m, FakeWorld w, Clock c) Fresh(int summon = 1)
    {
        var c = new Clock();
        var m = new EmiNudgeMachine(c.Now);
        var w = new FakeWorld();
        m.NoteSummon(summon);
        return (m, w, c);
    }

    /// <summary>Ask, and if she answers, tell her it landed. The service's own loop, minus WPF.</summary>
    private static string? Poll(EmiNudgeMachine m, FakeWorld w, bool spoke = true)
    {
        var track = m.Tick(w);
        if (track != null) m.Attempted(w, track, spoke);
        return track;
    }

    // ---------------------------------------------------------------- she starts

    [Fact]
    public void SaysNothingBeforeTheFirstInterval()
    {
        var (m, w, c) = Fresh();

        c.Advance(EmiNudgeMachine.PetFirstMs - 1000);
        Assert.Null(m.Tick(w));
    }

    [Fact]
    public void PetNudgeArrivesAfterTheFirstInterval()
    {
        var (m, w, c) = Fresh();

        c.Advance(EmiNudgeMachine.PetFirstMs);
        Assert.Equal(EmiNudgeMachine.PetTrack, Poll(m, w));
    }

    [Fact]
    public void NothingFiresBeforeSheIsSummoned()
    {
        var c = new Clock();
        var m = new EmiNudgeMachine(c.Now);
        var w = new FakeWorld();

        Assert.False(m.Armed);
        c.Advance(TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.Null(m.Tick(w));
    }

    [Fact]
    public void DismissDisarmsHer()
    {
        var (m, w, c) = Fresh();
        c.Advance(EmiNudgeMachine.PetFirstMs);

        m.NoteDismiss();

        Assert.False(m.Armed);
        Assert.Null(m.Tick(w));
    }

    // ---------------------------------------------------------------- she stops

    [Fact]
    public void ThePetNudgeStopsForeverOnceTheGistIsGot()
    {
        var (m, w, c) = Fresh();

        w.PetsTotal = EmiNudgeMachine.PetGistCount;

        c.Advance(TimeSpan.FromHours(4).TotalMilliseconds);
        Assert.Null(m.Tick(w));
    }

    [Fact]
    public void ThePetNudgeStopsOnTheLatchedFlagEvenIfTheCountIsLost()
    {
        // EmiState latches PetGistGot the moment the count is reached; the count itself could be
        // reset by a ledger repair. The latch is the one that must hold.
        var (m, w, c) = Fresh();

        w.PetsTotal = 0;
        w.PetGistGot = true;

        c.Advance(TimeSpan.FromHours(4).TotalMilliseconds);
        Assert.Null(m.Tick(w));
    }

    [Fact]
    public void ThePetNudgeStopsAtTheLifetimeCap()
    {
        var (m, w, c) = Fresh();

        int fired = 0;
        for (int i = 0; i < 60; i++)
        {
            c.Advance(EmiNudgeMachine.PetRepeatMs);
            if (Poll(m, w) == EmiNudgeMachine.PetTrack) fired++;
        }

        Assert.Equal(EmiNudgeMachine.LifetimeCap, fired);
        Assert.Equal(EmiNudgeMachine.LifetimeCap, w.FiresOf(EmiNudgeMachine.PetTrack));
    }

    [Fact]
    public void TheCapSurvivesARestart()
    {
        // A new machine every launch; the six is remembered by the world, not by the object.
        var c = new Clock();
        var w = new FakeWorld();
        w.Fires[EmiNudgeMachine.PetTrack] = EmiNudgeMachine.LifetimeCap;

        var m = new EmiNudgeMachine(c.Now);
        m.NoteSummon(1);

        c.Advance(TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.Null(m.Tick(w));
    }

    [Fact]
    public void NothingSpeaksWhileTheDesktopIsBusy()
    {
        var (m, w, c) = Fresh();

        w.Quiet = false;
        c.Advance(TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.Null(m.Tick(w));

        // ...and she picks it straight back up when the screen is hers again.
        w.Quiet = true;
        Assert.Equal(EmiNudgeMachine.PetTrack, m.Tick(w));
    }

    [Fact]
    public void ARefusedNudgeCostsARetryIntervalRatherThanLoopingThePoll()
    {
        var (m, w, c) = Fresh();
        c.Advance(EmiNudgeMachine.PetFirstMs);

        // The engine said no (a hold, a cooldown, the 45 s floor). The attempt is still booked.
        Assert.Equal(EmiNudgeMachine.PetTrack, Poll(m, w, spoke: false));

        // No lifetime fire was spent...
        Assert.Equal(0, w.FiresOf(EmiNudgeMachine.PetTrack));

        // ...and the next poll five seconds later is silent, not a second attempt.
        c.Advance(5_000);
        Assert.Null(m.Tick(w));
    }

    // ---------------------------------------------------------------- spacing

    [Fact]
    public void TwoNudgesNeverLandWithinNinetySeconds()
    {
        var (m, w, c) = Fresh(summon: EmiNudgeMachine.RingFirstSummon);

        c.Advance(EmiNudgeMachine.PetFirstMs);
        Assert.Equal(EmiNudgeMachine.PetTrack, Poll(m, w));

        // The ring track's own first interval is up too, but the shared floor holds it back.
        c.Advance(EmiNudgeMachine.RingFirstMs - EmiNudgeMachine.PetFirstMs);
        Assert.Null(m.Tick(w));

        c.Advance(EmiNudgeMachine.MinGapMs);
        Assert.Equal(EmiNudgeMachine.RingTrack, Poll(m, w));
    }

    [Fact]
    public void ThePetTrackGoesFirstWhenBothAreDue()
    {
        var (m, w, c) = Fresh(summon: EmiNudgeMachine.RingFirstSummon);

        c.Advance(TimeSpan.FromMinutes(30).TotalMilliseconds);
        Assert.Equal(EmiNudgeMachine.PetTrack, Poll(m, w));
    }

    // ---------------------------------------------------------------- the ring track

    [Fact]
    public void TheRingNudgeWaitsForTheSecondSummon()
    {
        var (m, w, c) = Fresh(summon: 1);

        // Take the pet track out of the running so only the ring track can answer.
        w.PetGistGot = true;

        c.Advance(TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.Null(m.Tick(w));

        m.NoteSummon(EmiNudgeMachine.RingFirstSummon);
        c.Advance(EmiNudgeMachine.RingFirstMs);
        Assert.Equal(EmiNudgeMachine.RingTrack, Poll(m, w));
    }

    [Fact]
    public void TheRingNudgeStopsAfterTwoOpens()
    {
        var (m, w, c) = Fresh(summon: EmiNudgeMachine.RingFirstSummon);
        w.PetGistGot = true;
        w.RingOpens = EmiNudgeMachine.RingGistCount;

        c.Advance(TimeSpan.FromHours(2).TotalMilliseconds);
        Assert.Null(m.Tick(w));
    }

    // ---------------------------------------------------------------- the pin track

    [Fact]
    public void ThePinNudgeRidesTheRingOpening()
    {
        var (m, w, _) = Fresh();
        Assert.Equal(EmiNudgeMachine.PinTrack, m.OnRingOpened(w));
    }

    [Fact]
    public void ThePinNudgeIsAtMostOncePerSummon()
    {
        var (m, w, c) = Fresh();

        Assert.Equal(EmiNudgeMachine.PinTrack, m.OnRingOpened(w));
        m.Attempted(w, EmiNudgeMachine.PinTrack, spoke: true);

        c.Advance(TimeSpan.FromHours(1).TotalMilliseconds);
        Assert.Null(m.OnRingOpened(w));

        // A fresh summon gets one more.
        m.NoteSummon(3);
        Assert.Equal(EmiNudgeMachine.PinTrack, m.OnRingOpened(w));
    }

    [Fact]
    public void ThePinNudgeStopsForeverAfterTheFirstPin()
    {
        var (m, w, _) = Fresh();
        w.PinGistGot = true;

        Assert.Null(m.OnRingOpened(w));
    }

    [Fact]
    public void ThePinNudgeObeysTheSharedNinetySecondFloor()
    {
        var (m, w, c) = Fresh();

        c.Advance(EmiNudgeMachine.PetFirstMs);
        Assert.Equal(EmiNudgeMachine.PetTrack, Poll(m, w));

        // The ring is opened right after she said something. She does not talk over herself.
        Assert.Null(m.OnRingOpened(w));

        c.Advance(EmiNudgeMachine.MinGapMs);
        Assert.Equal(EmiNudgeMachine.PinTrack, m.OnRingOpened(w));
    }

    [Fact]
    public void ThePinNudgeStopsAtTheLifetimeCap()
    {
        var (m, w, _) = Fresh();
        w.Fires[EmiNudgeMachine.PinTrack] = EmiNudgeMachine.LifetimeCap;

        Assert.Null(m.OnRingOpened(w));
    }

    // ---------------------------------------------------------------- the whole shape

    [Fact]
    public void OverALongLifeEveryTrackIsBoundedBySixAndStopsAtItsGist()
    {
        var c = new Clock();
        var m = new EmiNudgeMachine(c.Now);
        var w = new FakeWorld();

        // Twenty summons, twenty minutes each, a ring opened once per summon. She never learns
        // anything, which is the WORST case: the caps are the only thing stopping her.
        for (int summon = 1; summon <= 20; summon++)
        {
            m.NoteSummon(summon);

            var pin = m.OnRingOpened(w);
            if (pin != null) m.Attempted(w, pin, spoke: true);

            for (int t = 0; t < 40; t++)
            {
                c.Advance(30_000);
                Poll(m, w);
            }

            m.NoteDismiss();
        }

        foreach (var track in EmiNudgeMachine.Tracks)
        {
            Assert.True(w.FiresOf(track) <= EmiNudgeMachine.LifetimeCap,
                $"{track} fired {w.FiresOf(track)} times, over the lifetime cap of {EmiNudgeMachine.LifetimeCap}");
        }

        // And a user who DOES get it hears nothing more, ever.
        w.PetGistGot = true;
        w.RingGistGot = true;
        w.PinGistGot = true;
        m.NoteSummon(21);
        c.Advance(TimeSpan.FromHours(6).TotalMilliseconds);

        Assert.Null(m.Tick(w));
        Assert.Null(m.OnRingOpened(w));
    }
}
