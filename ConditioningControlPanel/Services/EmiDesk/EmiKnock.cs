using System;
using System.Windows;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// Which onboarding population is at the keyboard, decided from evidence rather than a flag.
/// </summary>
public enum EmiKnockPopulation
{
    /// <summary>Nobody the knock is for: a returning user on the version they already ran.</summary>
    None = 0,

    /// <summary>A brand new install that skipped the wizard's tour. The whole point of the knock.</summary>
    Fresh = 1,

    /// <summary>A new install that already took the walk in the wizard. She has nothing to offer them.</summary>
    Walked = 2,

    /// <summary>They ran an older version. The offer is the upgrade tour, not the short walk.</summary>
    Upgrader = 3
}

/// <summary>
/// THE KNOCK: the dock chip flashes ONCE, ever, and then the feature is over.
///
/// <para>Why it exists. EMI Desk ships switched on and completely silent: the chip is a 40 px ring
/// at the bottom of a rail full of doors, and nothing in the app ever says that clicking it does
/// anything. A first-run wizard that someone pressed "explore on my own" through has, by
/// definition, told them nothing either. So she knocks: three pink pulses, six seconds, and if
/// they click she introduces herself and offers to walk them round. If they do not click, that was
/// the whole feature.</para>
///
/// <para><b>The whole design is the stopping, not the knocking</b> - the same law
/// <see cref="EmiNudgeMachine"/> is built on, and this class is deliberately modelled on it line
/// for line. FOUR independent brakes, any one of which ends the knock forever
/// (docs/emi-desk/WAVE1-CONTRACT.md):</para>
/// <list type="number">
/// <item>the <b>latch</b>: <see cref="EmiState.KnockState"/> reaches
/// <see cref="Spent"/> the moment they say yes, and nothing here fires again;</item>
/// <item>the <b>lifetime cap</b>: <see cref="OfferCap"/> knocks across every launch there will
/// ever be, counted in <see cref="EmiState.KnockOffers"/> - the knock itself, and one shrugged
/// re-offer on a later launch;</item>
/// <item>the lines file's own <c>limit: {per:"ever", max:1}</c> on the contact moments, which is
/// the same ceiling written down a second time on the content side;</item>
/// <item>the <b>tour latch</b>: <see cref="EmiState.ToursDone"/> already holding the tour she
/// would offer. Offering a walk somebody has taken is the definition of not listening.</item>
/// </list>
///
/// <para><b>Reading brake 1 against brake 2.</b> The contract calls state 2 "they answered, either
/// way", which read literally would make the re-offer in brake 2 unreachable. The reading that
/// makes both brakes real, and the one implemented here: a <b>yes</b> is answered and latches
/// state 2 immediately (<c>EmiState.NoteKnockAnswered</c>); a <b>no</b> spends one of the two offers and
/// leaves the state at <see cref="Knocked"/>, so exactly one quieter re-offer may follow on a
/// later launch, and the cap ends it after that. "Never a third" is the promise, and both counters
/// enforce it independently.</para>
///
/// <para><b>The offer is counted at the FLASH, not at the click.</b> A user who never touches the
/// chip has answered too - by ignoring it - and if the counter waited for a click, the chip would
/// re-flash on every launch until the end of time. Counting the pulse is what makes "once, ever"
/// literally true.</para>
///
/// <para><b>This class is pure.</b> No timers, no dispatcher, no <c>App</c>, no clock of its own
/// beyond an injected one. Everything it needs about the world arrives through
/// <see cref="IEmiKnockWorld"/>, which is what lets the brakes, the gates and the population
/// branch be tested headlessly in a millisecond instead of across three fresh installs. Keep it
/// that way: the moment it reads a window it stops being testable, and an onboarding prompt that
/// comes back is the single fastest way to get the whole widget switched off.</para>
/// </summary>
public sealed class EmiKnockMachine
{
    // ---------------------------------------------------------------- the moment ids

    /// <summary>Fresh install, walk not yet taken. Moment id AND pool id in <c>desk-lines.json</c>.</summary>
    public const string FreshMoment = "firstContact";

    /// <summary>Same beat, but they ran an older build: the offer is the upgrade tour.</summary>
    public const string UpgradeMoment = "firstContactUpgrade";

    /// <summary>
    /// The re-offer, on a later launch, after a shrug. A POOL ONLY - it carries no ask (the
    /// contract's shape column is load-bearing). She mentions it once more and then never again.
    /// </summary>
    public const string LaterMoment = "firstContactLater";

    // ---------------------------------------------------------------- the tour names

    /// <summary>
    /// <c>TutorialType.ShortWalk</c> by name. A STRING on purpose: this class must stay free of
    /// the tutorial enum so it can be exercised without the WPF half of the app, and
    /// <see cref="EmiState.ToursDone"/> persists names rather than ordinals for the same reason
    /// (an ordinal shifts the day somebody inserts a value into the middle of the enum).
    /// </summary>
    public const string ShortWalkTour = "ShortWalk";

    /// <inheritdoc cref="ShortWalkTour"/>
    public const string UpgradeTour = "UpgradeTour";

    // ---------------------------------------------------------------- the states

    /// <summary>She has never flashed the chip.</summary>
    public const int Never = 0;

    /// <summary>The chip has knocked; the offer is still live.</summary>
    public const int Knocked = 1;

    /// <summary>Spent. They said yes, and nothing in here ever fires again.</summary>
    public const int Spent = 2;

    // ---------------------------------------------------------------- the dials

    /// <summary>
    /// Hard ceiling on how many times the chip may EVER knock: the knock, plus one shrugged
    /// re-offer on a later launch. A third is nagging and the machine refuses it.
    /// </summary>
    public const int OfferCap = 2;

    private readonly Func<DateTime> _now;

    /// <summary>Builds a machine. <paramref name="clock"/> is for tests; production passes null.</summary>
    public EmiKnockMachine(Func<DateTime>? clock = null)
    {
        _now = clock ?? (() => DateTime.UtcNow);
    }

    // ---------------------------------------------------------------- the population

    /// <summary>
    /// Who is at the keyboard, decided from evidence: an empty <c>LastSeenVersion</c>, a tour
    /// already latched in <see cref="EmiState.ToursDone"/>, or a stamped version older than this
    /// build.
    ///
    /// <para><b>Never gate on a bare seen-flag.</b> That is the bug that showed every fresh install
    /// a migration notice for a move it never witnessed: "has a version stamp" is not the same
    /// question as "ran an older build". An empty stamp is a fresh install; a stamp EQUAL to this
    /// build is somebody who already ran it and is owed nothing.</para>
    ///
    /// <para>Order matters. The walk is checked first, because somebody who took it in the wizard
    /// is a fresh install by every other measure and she still has nothing to offer them.</para>
    /// </summary>
    public EmiKnockPopulation Population(IEmiKnockWorld w)
    {
        if (w == null) return EmiKnockPopulation.None;

        bool fresh = string.IsNullOrWhiteSpace(w.LastSeenVersion);

        if (fresh)
        {
            // Took the walk inside the wizard: a greeting is all she has, and the knock's brake 4
            // stops the chip flashing at all.
            return w.TourDone(ShortWalkTour) ? EmiKnockPopulation.Walked : EmiKnockPopulation.Fresh;
        }

        if (IsOlder(w.LastSeenVersion, w.CurrentVersion))
            return w.TourDone(UpgradeTour) ? EmiKnockPopulation.Walked : EmiKnockPopulation.Upgrader;

        return EmiKnockPopulation.None;
    }

    /// <summary>The tour this population would be offered, or null when there is nothing to offer.</summary>
    public static string? TourFor(EmiKnockPopulation p) => p switch
    {
        EmiKnockPopulation.Fresh => ShortWalkTour,
        EmiKnockPopulation.Upgrader => UpgradeTour,
        _ => null
    };

    // ---------------------------------------------------------------- the brakes

    /// <summary>
    /// Is an offer still owed - the FOUR BRAKES and nothing else? Deliberately separate from
    /// <see cref="MayKnock"/>: the brakes are permanent and are the thing worth testing on its own,
    /// while the gates below are about this exact instant and go away by themselves.
    /// </summary>
    public bool OfferOwed(IEmiKnockWorld w)
    {
        if (w == null) return false;

        // Brake 1. They said yes. Latched, and nothing un-latches it but the QA reset.
        if (w.KnockState >= Spent) return false;

        // Brake 2. The knock, plus one shrugged re-offer, and never again.
        if (w.KnockOffers >= OfferCap) return false;

        // Brake 4. She has nothing to offer this population, or they have already walked it.
        var pop = Population(w);
        var tour = TourFor(pop);
        if (tour == null) return false;
        if (w.TourDone(tour)) return false;

        // Brake 3 (the lines file's own limit: {per:"ever", max:1}) is enforced by the engine at
        // draw time and deliberately NOT restated here: it is the content side's copy of the same
        // ceiling, and a machine that second-guessed it would make one of the two dead code.
        return true;
    }

    /// <summary>
    /// The re-offer is a LATER-LAUNCH beat, never a second flash in the same sitting. Without
    /// this, a dismiss and a re-summon inside one minute would read as two separate launches and
    /// she would knock twice in five seconds.
    /// </summary>
    private bool SameLaunchAsLastKnock(IEmiKnockWorld w)
    {
        if (w.KnockAtUtc <= 0) return false;
        try
        {
            var knocked = new DateTime(w.KnockAtUtc, DateTimeKind.Utc);
            return knocked >= w.LaunchStartedUtc;
        }
        catch
        {
            // A corrupt tick count is a reason to stay quiet, never a reason to knock again.
            return true;
        }
    }

    // ---------------------------------------------------------------- the decision

    /// <summary>
    /// May the chip flash RIGHT NOW? Asked once, at the far side of the first-run flow, and the
    /// answer is no almost every time.
    ///
    /// <para>The brakes above, then the gates from the contract's "Gates the knock must pass"
    /// section: no knock while the first-run wizard is up, while an update dialog is up, while a
    /// session is running, while a tutorial overlay is open, while the window is minimised or
    /// hidden, while EMI Desk is switched off, or while she is already out (a chip flashing to
    /// summon somebody who is standing right there is nonsense).</para>
    /// </summary>
    public bool MayKnock(IEmiKnockWorld w)
    {
        if (w == null) return false;

        if (!OfferOwed(w)) return false;
        if (SameLaunchAsLastKnock(w)) return false;

        if (!w.DeskEnabled) return false;
        if (w.AlreadyOut) return false;
        if (w.WizardUp) return false;
        if (w.UpdateDialogUp) return false;
        if (w.SessionRunning) return false;
        if (w.TutorialOpen) return false;
        if (!w.WindowUsable) return false;

        return true;
    }

    /// <summary>
    /// She has been summoned off the back of a knock: which moment does she open with?
    ///
    /// <para>The FIRST contact carries the ask (<c>firstContact</c> / <c>firstContactUpgrade</c>);
    /// the second is the quieter <c>firstContactLater</c>, a pool with no offer attached, because
    /// somebody who shrugged once does not need the same two chips shown to them again.</para>
    ///
    /// <para>Null means she has nothing scripted to say and the ordinary greeting stands.</para>
    /// </summary>
    public string? ContactMoment(IEmiKnockWorld w)
    {
        if (w == null) return null;
        if (w.KnockState >= Spent) return null;

        // The flash has already spent its offer by the time she is summoned, so offer 1 is the
        // first contact and anything above it is the re-offer.
        if (w.KnockOffers >= OfferCap) return LaterMoment;

        return Population(w) switch
        {
            EmiKnockPopulation.Fresh => FreshMoment,
            EmiKnockPopulation.Upgrader => UpgradeMoment,
            _ => null
        };
    }

    /// <summary>
    /// The effect verb a YES on this population's ask should carry. Used by the tests and by
    /// diagnostics; the lines file states it per ask, which is where the writers can see it.
    /// </summary>
    public static string? EffectFor(EmiKnockPopulation p) => p switch
    {
        EmiKnockPopulation.Fresh => "tour:shortwalk",
        EmiKnockPopulation.Upgrader => "tour:upgrade",
        _ => null
    };

    // ---------------------------------------------------------------- the arithmetic

    /// <summary>
    /// Is <paramref name="seen"/> strictly older than <paramref name="current"/>? Parsed as a
    /// version when both parse, because "6.10.0" sorts before "6.9.0" as a string and that is
    /// exactly the kind of bug nobody notices until the tenth minor release. An unparseable stamp
    /// falls back to "different means older", which is what the app's own What's New gate does.
    /// </summary>
    public static bool IsOlder(string? seen, string? current)
    {
        if (string.IsNullOrWhiteSpace(seen)) return false;
        if (string.IsNullOrWhiteSpace(current)) return false;
        var a = seen!.Trim().TrimStart('v', 'V');
        var b = current!.Trim().TrimStart('v', 'V');
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        if (Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)) return va < vb;
        return true;
    }

    /// <summary>Now, through the injected clock. Exposed so the service can stamp with the same one.</summary>
    public DateTime UtcNow() => _now();
}

/// <summary>
/// Everything <see cref="EmiKnockMachine"/> needs to know about the world, behind an interface so
/// the machine can be driven by a fake in a headless test. The live implementation is
/// <see cref="EmiKnockWorld"/>; it reads <see cref="EmiState"/>, <c>AppSettings</c> and the
/// handful of app-wide flags that say whether anything already owns the screen.
/// </summary>
public interface IEmiKnockWorld
{
    // ---- the ledger ----

    /// <summary>0 never knocked, 1 knocked, 2 spent. <see cref="EmiState.KnockState"/>.</summary>
    int KnockState { get; }

    /// <summary>When the chip last knocked, in UTC ticks. 0 = never.</summary>
    long KnockAtUtc { get; }

    /// <summary>Knocks so far, ever. Capped at <see cref="EmiKnockMachine.OfferCap"/>.</summary>
    int KnockOffers { get; }

    /// <summary>Has this <c>TutorialType</c> name been finished end to end?</summary>
    bool TourDone(string tour);

    // ---- the population ----

    /// <summary>
    /// <c>AppSettings.LastSeenVersion</c> as it stood BEFORE this launch stamped it. The stamp
    /// happens inside <c>ShowWhatsNewIfNeeded</c>, several dispatcher passes ahead of the knock,
    /// so the live implementation snapshots it rather than reading it late.
    /// </summary>
    string? LastSeenVersion { get; }

    /// <summary>This build's version string.</summary>
    string? CurrentVersion { get; }

    /// <summary>When this process started, for the "not twice in one sitting" rule.</summary>
    DateTime LaunchStartedUtc { get; }

    // ---- the gates ----

    /// <summary>EMI Desk is switched on in settings.</summary>
    bool DeskEnabled { get; }

    /// <summary>She is already on screen, so there is nothing to knock about.</summary>
    bool AlreadyOut { get; }

    /// <summary>The first-run wizard is up.</summary>
    bool WizardUp { get; }

    /// <summary>An update dialog is up (<c>App.IsUpdateDialogActive</c>).</summary>
    bool UpdateDialogUp { get; }

    /// <summary>A session is running.</summary>
    bool SessionRunning { get; }

    /// <summary>A tutorial overlay is already on the glass.</summary>
    bool TutorialOpen { get; }

    /// <summary>The main window exists, is loaded, is visible and is not minimised.</summary>
    bool WindowUsable { get; }
}

/// <summary>
/// The live world: <see cref="EmiState"/> for the ledger, <c>AppSettings</c> for the switch, and
/// the app's own flags for the gates. Every property is wrapped, because a knock is the least
/// important thing in the app and must never be the thing that throws.
///
/// <para><see cref="LastSeenVersion"/> is a SNAPSHOT taken by the caller, not a live read.
/// <c>ShowWhatsNewIfNeeded</c> stamps the setting to the current version on the synchronous side
/// of the first-run branch, minutes before the knock's dispatcher item runs; a live read would see
/// the stamp and classify every upgrader as somebody who is owed nothing.</para>
/// </summary>
public sealed class EmiKnockWorld : IEmiKnockWorld
{
    private readonly string _seenVersion;
    private readonly DateTime _launchUtc;

    /// <summary>
    /// Builds the live world. <paramref name="seenVersionSnapshot"/> must be read BEFORE anything
    /// on this launch stamps <c>LastSeenVersion</c> - see the class remarks.
    /// </summary>
    public EmiKnockWorld(string? seenVersionSnapshot, DateTime? launchStartedUtc = null)
    {
        _seenVersion = seenVersionSnapshot ?? string.Empty;
        _launchUtc = launchStartedUtc ?? ProcessStartUtc();
    }

    private static DateTime ProcessStartUtc()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow.AddMinutes(-5); }
    }

    /// <inheritdoc/>
    public int KnockState { get { try { return EmiState.Current.KnockState; } catch { return EmiKnockMachine.Spent; } } }

    /// <inheritdoc/>
    public long KnockAtUtc { get { try { return EmiState.Current.KnockAtUtc; } catch { return 0; } } }

    /// <inheritdoc/>
    public int KnockOffers { get { try { return EmiState.Current.KnockOffers; } catch { return EmiKnockMachine.OfferCap; } } }

    /// <inheritdoc/>
    public bool TourDone(string tour) => EmiState.HasTourDone(tour);

    /// <inheritdoc/>
    public string? LastSeenVersion => _seenVersion;

    /// <inheritdoc/>
    public string? CurrentVersion
    {
        get { try { return ConditioningControlPanel.Services.UpdateService.AppVersion; } catch { return null; } }
    }

    /// <inheritdoc/>
    public DateTime LaunchStartedUtc => _launchUtc;

    /// <inheritdoc/>
    public bool DeskEnabled
    {
        get { try { return App.Settings?.Current?.EmiDeskEnabled == true; } catch { return false; } }
    }

    /// <inheritdoc/>
    public bool AlreadyOut
    {
        get { try { return App.EmiDesk?.IsOut == true; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool WizardUp
    {
        get { try { return ConditioningControlPanel.MainWindow.IsStartupDialogShowing; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool UpdateDialogUp
    {
        get { try { return App.IsUpdateDialogActive; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool SessionRunning
    {
        get { try { return SessionEngine.Active?.IsRunning == true; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool TutorialOpen
    {
        get { try { return App.Tutorial?.IsActive == true; } catch { return true; } }
    }

    /// <inheritdoc/>
    public bool WindowUsable
    {
        get
        {
            try
            {
                var main = Application.Current?.MainWindow;
                if (main == null) return false;
                if (!main.IsLoaded) return false;
                if (main.Visibility != Visibility.Visible) return false;
                if (main.WindowState == WindowState.Minimized) return false;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] knock window probe failed");
                return false;
            }
        }
    }
}
