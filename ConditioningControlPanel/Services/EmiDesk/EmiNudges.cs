using System;
using System.Collections.Generic;
using System.Windows;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// THE NUDGE MACHINE: teach the two gestures, then shut up forever.
///
/// <para>Why it exists. EMI Desk has exactly two gestures on her body and neither is discoverable:
/// a left click pats her, and the right button (or the little glyph on her shoulder) fans out her
/// cards. On the owner's third live run the pat was invisible because nothing on screen ever says
/// it is there. A tooltip cannot say it - she has no chrome - so she says it herself, a handful of
/// times, in her own voice, and then never again.</para>
///
/// <para><b>The whole design is the stopping, not the nudging.</b> Every track has FOUR
/// independent brakes and any one of them ends it:</para>
/// <list type="number">
/// <item>the <b>gist</b>: three pats, two ring opens, one pin. Latched in
/// <see cref="EmiState.PetGistGot"/> / <see cref="EmiState.RingGistGot"/> /
/// <see cref="EmiState.PinGistGot"/> and never un-latched by anything but the QA reset;</item>
/// <item>the <b>lifetime cap</b>: <see cref="LifetimeCap"/> fires per track across every launch
/// there will ever be, counted in <see cref="EmiState.NudgeFires"/>;</item>
/// <item>the lines file's own <c>limit: {per:"ever", max:6}</c>, which is the same ceiling written
/// down a second time on the content side;</item>
/// <item>the <b>spacing</b>: <see cref="MinGapMs"/> between any two nudges of any track, on top of
/// each track's own repeat interval and the engine's ordinary 45 s global floor.</item>
/// </list>
///
/// <para><b>It is not gated on spice.</b> The three pools are spice 0 - "pat me. it's allowed." is
/// not a lewd line by any reading - and a user on Innocent needs the tutorial exactly as much as
/// anyone else. <c>EmiDeskSpice</c> filters what she says, never whether the app is explainable.</para>
///
/// <para><b>This class is pure.</b> No timers, no dispatcher, no <c>App</c>, no clock of its own
/// beyond an injected one. Everything it needs about the world arrives through
/// <see cref="IEmiNudgeWorld"/>, which is what lets the caps, the ordering and the gist-stop be
/// tested headlessly. Keep it that way: the moment it reads a window it stops being testable and
/// the nagging comes back as a regression nobody can reproduce.</para>
/// </summary>
public sealed class EmiNudgeMachine
{
    // ---------------------------------------------------------------- the track ids

    /// <summary>"you can pat me." Moment id AND pool id in <c>desk-lines.json</c>.</summary>
    public const string PetTrack = "petNudge";

    /// <summary>"the other button fans my cards out."</summary>
    public const string RingTrack = "ringNudge";

    /// <summary>"the other button on a card keeps it there."</summary>
    public const string PinTrack = "pinNudge";

    /// <summary>The three, in the order they are offered. Pet first: it is the one gesture that
    /// costs nothing and the one the owner asked for by name.</summary>
    public static readonly IReadOnlyList<string> Tracks = new[] { PetTrack, RingTrack, PinTrack };

    // ---------------------------------------------------------------- the dials

    /// <summary>Hard ceiling on how many times ONE track may ever speak. Nobody is nagged forever.</summary>
    public const int LifetimeCap = 6;

    /// <summary>Never two nudges inside this window, whichever tracks they belong to.</summary>
    public const int MinGapMs = 90_000;

    /// <summary>How long after a summon the first pet nudge may land. Well past the greeting.</summary>
    public const int PetFirstMs = 25_000;

    /// <summary>...and how long between pet nudges after that.</summary>
    public const int PetRepeatMs = 240_000;

    /// <summary>The ring nudge's first delay on the summon that earns it.</summary>
    public const int RingFirstMs = 40_000;

    /// <summary>...and how long between ring nudges after that.</summary>
    public const int RingRepeatMs = 360_000;

    /// <summary>Pats after which the pet nudge is retired for good.</summary>
    public const int PetGistCount = 3;

    /// <summary>Ring opens after which the ring nudge is retired for good.</summary>
    public const int RingGistCount = 2;

    /// <summary>
    /// The ring nudge does not exist on a user's very first summon: that one already carries the
    /// first-boot greeting, and a tutorial stacked on a hello is two strangers talking at once.
    /// </summary>
    public const int RingFirstSummon = 2;

    // ---------------------------------------------------------------- per-run state

    private readonly Func<DateTime> _now;

    private DateTime _summonAtUtc = DateTime.MinValue;
    private DateTime _lastAnyUtc = DateTime.MinValue;
    private DateTime _lastPetUtc = DateTime.MinValue;
    private DateTime _lastRingUtc = DateTime.MinValue;
    private bool _pinThisSummon;
    private int _summonCount;

    /// <summary>Builds a machine. <paramref name="clock"/> is for tests; production passes null.</summary>
    public EmiNudgeMachine(Func<DateTime>? clock = null)
    {
        _now = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>True while she is out and the machine has a summon to measure against.</summary>
    public bool Armed => _summonAtUtc != DateTime.MinValue;

    // ---------------------------------------------------------------- lifecycle

    /// <summary>
    /// She came out. Starts the clock every "how long since" in here is measured from, and clears
    /// the once-per-summon pin latch.
    /// </summary>
    public void NoteSummon(int summonCount)
    {
        _summonAtUtc = _now();
        _summonCount = summonCount;
        _pinThisSummon = false;
        _lastPetUtc = DateTime.MinValue;
        _lastRingUtc = DateTime.MinValue;
    }

    /// <summary>She left. Nothing may fire until the next summon.</summary>
    public void NoteDismiss()
    {
        _summonAtUtc = DateTime.MinValue;
        _pinThisSummon = false;
    }

    // ---------------------------------------------------------------- the decisions

    /// <summary>
    /// The ambient question, asked on a short poll: is there a nudge owed right now? Returns the
    /// track id, or null - which is the answer almost every time it is asked.
    ///
    /// <para>Order is pet, then ring. The pet is the cheaper gesture and the one the owner asked
    /// for; the ring nudge waits for the second summon anyway, so on a first run there is only one
    /// candidate at all.</para>
    /// </summary>
    public string? Tick(IEmiNudgeWorld w)
    {
        if (w == null) return null;
        if (!Armed) return null;
        if (!w.Quiet) return null;

        var now = _now();
        if (Since(now, _lastAnyUtc) < MinGapMs) return null;

        if (Wants(w, PetTrack, w.PetGistGot, w.PetsTotal, PetGistCount)
            && Due(now, _lastPetUtc, PetFirstMs, PetRepeatMs))
        {
            return PetTrack;
        }

        if (_summonCount >= RingFirstSummon
            && Wants(w, RingTrack, w.RingGistGot, w.RingOpens, RingGistCount)
            && Due(now, _lastRingUtc, RingFirstMs, RingRepeatMs))
        {
            return RingTrack;
        }

        return null;
    }

    /// <summary>
    /// The ring just opened. The pin nudge is the one track with an event rather than a clock
    /// behind it: "right-click a card to keep it" is only useful while the cards are on screen.
    /// At most once per summon, and never once anything has been pinned.
    /// </summary>
    public string? OnRingOpened(IEmiNudgeWorld w)
    {
        if (w == null) return null;
        if (!Armed) return null;
        if (_pinThisSummon) return null;
        if (w.PinGistGot) return null;
        if (w.FiresOf(PinTrack) >= LifetimeCap) return null;
        if (!w.Quiet) return null;
        if (Since(_now(), _lastAnyUtc) < MinGapMs) return null;
        return PinTrack;
    }

    /// <summary>
    /// A track was offered to the line engine. Call it whether or not a line actually reached the
    /// screen: the repeat clock and the 90 s spacing move on the ATTEMPT, so an engine that
    /// refuses (a hold, a cooldown, the global floor) costs a retry interval rather than turning
    /// the poll into a loop that asks every five seconds forever. Only a line that really landed
    /// spends one of the six lifetime fires.
    /// </summary>
    public void Attempted(IEmiNudgeWorld w, string track, bool spoke)
    {
        if (string.IsNullOrEmpty(track)) return;
        var now = _now();
        _lastAnyUtc = now;

        switch (track)
        {
            case PetTrack: _lastPetUtc = now; break;
            case RingTrack: _lastRingUtc = now; break;
            case PinTrack: _pinThisSummon = true; break;
        }

        if (spoke) w?.NoteFire(track);
    }

    // ---------------------------------------------------------------- the arithmetic

    private bool Wants(IEmiNudgeWorld w, string track, bool gistGot, int count, int gistAt)
    {
        if (gistGot) return false;
        if (count >= gistAt) return false;
        return w.FiresOf(track) < LifetimeCap;
    }

    /// <summary>
    /// Is this track's clock up? The FIRST nudge of a summon is measured from the summon itself
    /// (so she does not open her mouth the instant she lands); every one after it is measured from
    /// the previous attempt.
    /// </summary>
    private bool Due(DateTime now, DateTime lastUtc, double firstMs, double repeatMs)
    {
        if (lastUtc == DateTime.MinValue) return Since(now, _summonAtUtc) >= firstMs;
        return Since(now, lastUtc) >= repeatMs;
    }

    private static double Since(DateTime now, DateTime then)
    {
        if (then == DateTime.MinValue) return double.MaxValue;
        double ms = (now - then).TotalMilliseconds;
        return ms < 0 ? 0 : ms;
    }
}

/// <summary>
/// Everything <see cref="EmiNudgeMachine"/> needs to know about the world, behind an interface so
/// the machine can be driven by a fake in a headless test. The live implementation is
/// <see cref="EmiNudgeWorld"/>; it reads <see cref="EmiState"/> and asks the service whether the
/// desktop is calm enough to speak into.
/// </summary>
public interface IEmiNudgeWorld
{
    /// <summary>Pats so far, ever.</summary>
    int PetsTotal { get; }

    /// <summary>The pet nudge has been retired.</summary>
    bool PetGistGot { get; }

    /// <summary>Ring opens so far, ever.</summary>
    int RingOpens { get; }

    /// <summary>The ring nudge has been retired.</summary>
    bool RingGistGot { get; }

    /// <summary>Something has been pinned at least once, so the pin nudge is retired.</summary>
    bool PinGistGot { get; }

    /// <summary>How many times this track has ever spoken.</summary>
    int FiresOf(string track);

    /// <summary>One more fire on the board.</summary>
    void NoteFire(string track);

    /// <summary>
    /// Is the desktop calm enough for a tutorial line? False while an ask is waiting for an
    /// answer, while a full-screen feature owns the screen, while a chain is playing, and any time
    /// she is not actually on screen.
    /// </summary>
    bool Quiet { get; }
}

/// <summary>
/// The live world: <see cref="EmiState"/> for the counters and <c>App.EmiDesk</c> for the
/// situation. Every property is wrapped, because a nudge is the least important thing in the app
/// and must never be the thing that throws.
/// </summary>
public sealed class EmiNudgeWorld : IEmiNudgeWorld
{
    /// <inheritdoc/>
    public int PetsTotal { get { try { return EmiState.Current.PetsTotal; } catch { return 0; } } }

    /// <inheritdoc/>
    public bool PetGistGot { get { try { return EmiState.Current.PetGistGot; } catch { return true; } } }

    /// <inheritdoc/>
    public int RingOpens { get { try { return EmiState.Current.RingOpens; } catch { return 0; } } }

    /// <inheritdoc/>
    public bool RingGistGot { get { try { return EmiState.Current.RingGistGot; } catch { return true; } } }

    /// <inheritdoc/>
    public bool PinGistGot { get { try { return EmiState.Current.PinGistGot; } catch { return true; } } }

    /// <inheritdoc/>
    public int FiresOf(string track)
    {
        try { return EmiState.Current.NudgeFires.TryGetValue(track, out int n) ? n : 0; }
        catch { return int.MaxValue; }   // unreadable state means "stop", never "nag"
    }

    /// <inheritdoc/>
    public void NoteFire(string track) => EmiState.NoteNudgeFired(track);

    /// <inheritdoc/>
    public bool Quiet
    {
        get
        {
            try
            {
                var desk = App.EmiDesk;
                if (desk == null || !desk.IsOut) return false;

                // AskSituationOk is already the app's answer to "is anything owning the screen or
                // the user's attention right now": a live ask, a mandatory video, a running
                // session, a tube bubble, a minimised app. Reusing it means the nudge cannot
                // develop its own idea of quiet and drift away from the offers'.
                if (!desk.AskSituationOk()) return false;

                var win = desk.Window;
                if (win == null || win.Visibility != Visibility.Visible) return false;
                if (win.ChainLive) return false;

                // A hold is the safety silence: panic pressed, attention check up, lockdown
                // counting down. Nothing tutorial-shaped speaks through one.
                if (EmiLineEngine.Instance.HoldActive) return false;

                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] nudge quiet probe failed");
                return false;
            }
        }
    }
}
