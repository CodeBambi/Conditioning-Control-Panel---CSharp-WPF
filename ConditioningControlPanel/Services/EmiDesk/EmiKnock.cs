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

    /// <summary>
    /// They ran an older version. NO LONGER PRODUCED by <see cref="EmiKnockMachine.Population"/>:
    /// the upgrade tour is offered by What's New / the Welcome-back sheet, which is the surface an
    /// upgrader is already looking at. The value and its mappings survive so the upgrade moment,
    /// its pool and its asks stay addressable from that surface and from QA.
    /// </summary>
    Upgrader = 3
}

/// <summary>
/// THE KNOCK: she comes out ONCE, ever, asks one question, and then the feature is over.
///
/// <para>Why it exists. EMI Desk ships switched on and completely silent: the chip is a 40 px ring
/// at the bottom of a rail full of doors, and nothing in the app ever says that clicking it does
/// anything. A first-run wizard that someone pressed "explore on my own" through has, by
/// definition, told them nothing either. So she knocks.</para>
///
/// <para><b>The knock is now the offer, not an invitation to fetch the offer</b> (first-run
/// redesign, Sep 2026). It used to be three pink pulses and nothing else: if the user happened to
/// click the chip in those six seconds she introduced herself and offered the walk, and if they
/// did not - which is almost everybody, because nothing on screen said the ring meant anything -
/// the offer was spent on a pulse nobody read as a question. So a shrug bought a second flash on a
/// later launch, and the whole thing still mostly landed as noise.
/// Now: when <see cref="MayKnock"/> says yes the chip pulses AND she is summoned in the same beat,
/// opening with <see cref="FreshMoment"/>, whose ask IS the walk offer. Yes runs the walk. No, or
/// a dismissed desk, is a no - and it is the last word, because there is no second offer to spend.
/// The pulse survives as the reason she appeared: it points at the chip she came out of.</para>
///
/// <para><b>The whole design is the stopping, not the knocking</b> - the same law
/// <see cref="EmiNudgeMachine"/> is built on, and this class is deliberately modelled on it line
/// for line. FOUR independent brakes, any one of which ends the knock forever
/// (docs/emi-desk/WAVE1-CONTRACT.md):</para>
/// <list type="number">
/// <item>the <b>latch</b>: <see cref="EmiState.KnockState"/> reaches
/// <see cref="Spent"/> the moment they say yes, and nothing here fires again;</item>
/// <item>the <b>lifetime cap</b>: <see cref="OfferCap"/> - ONE offer across every launch there
/// will ever be, counted in <see cref="EmiState.KnockOffers"/>;</item>
/// <item>the lines file's own <c>limit: {per:"ever", max:1}</c> on the contact moments, which is
/// the same ceiling written down a second time on the content side;</item>
/// <item>the <b>tour latch</b>: <see cref="EmiState.ToursDone"/> already holding the tour she
/// would offer. Offering a walk somebody has taken is the definition of not listening.</item>
/// </list>
///
/// <para><b>Brake 1 against brake 2, now that the cap is one.</b> A <b>yes</b> latches
/// <see cref="Spent"/> immediately (<c>EmiState.NoteKnockAnswered</c>) so the answer survives even a
/// QA counter reset. A <b>no</b> needs no latch of its own: the single offer was already spent, so
/// brake 2 alone means she is never owed another. The two brakes agree instead of contradicting
/// each other, which is what the old two-offer reading never quite managed.</para>
///
/// <para><b>The offer is counted at the FLASH, not at the answer.</b> A user who closes the app
/// mid-bubble has been asked, and if the counter waited for a chip press she would come out again
/// on every launch until the end of time. Counting the moment she appears is what makes "once,
/// ever" literally true.</para>
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

    /// <summary>
    /// Same beat, but they ran an older build: the offer is the upgrade tour. The knock no longer
    /// reaches it (see <see cref="EmiKnockPopulation.Upgrader"/>); it is kept as the id of a
    /// written, shipped moment for the surface that does offer that tour.
    /// </summary>
    public const string UpgradeMoment = "firstContactUpgrade";

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
    /// Hard ceiling on how many times she may EVER knock: ONE. She comes out, she asks, and
    /// whatever the answer is, that was the feature. A second ask is nagging and the machine
    /// refuses it.
    ///
    /// <para>It was 2 while the knock was only a pulse and the offer needed a click to reach: a
    /// shrug bought one quieter re-offer on a later launch. Now the ask arrives with her, so the
    /// user has genuinely been asked the first time and there is nothing left to re-offer.</para>
    /// </summary>
    public const int OfferCap = 1;

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
    ///
    /// <para><b>THE KNOCK IS FOR FRESH INSTALLS ONLY</b> (first-run redesign, Sep 2026). An
    /// upgrader is already being handed the upgrade tour by What's New / the Welcome-back sheet on
    /// the very same launch, and a companion who materialises to offer a second tour on top of that
    /// sheet is the exact pile-up this redesign exists to end. So an older stamp now answers
    /// <see cref="EmiKnockPopulation.None"/>: not "owed nothing ever", just "not owed it by
    /// me".</para>
    /// </summary>
    public EmiKnockPopulation Population(IEmiKnockWorld w)
    {
        if (w == null) return EmiKnockPopulation.None;

        bool fresh = string.IsNullOrWhiteSpace(w.LastSeenVersion);
        if (!fresh) return EmiKnockPopulation.None;

        // Took the walk inside the wizard: a greeting is all she has, and the knock's brake 4
        // stops her coming out at all.
        return w.TourDone(ShortWalkTour) ? EmiKnockPopulation.Walked : EmiKnockPopulation.Fresh;
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

        // Brake 2. One offer, ever. Spent at the flash, so this is what makes a no permanent
        // without needing a latch of its own.
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
    /// NEVER TWICE IN ONE SITTING. Brake 2 is the real ceiling now that the cap is one, and this
    /// looks like belt and braces on top of it - it is not. It is the guard that holds when the
    /// counter does not: a QA replay, a settings file rolled back under a running app, a corrupt
    /// ledger. Without it any of those would put her back on screen seconds after she was sent
    /// away, which is the single worst thing this feature can do.
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
    /// hidden, while EMI Desk is switched off, or while she is already out (summoning somebody who
    /// is standing right there is nonsense).</para>
    ///
    /// <para><b>The startup quiet window is deliberately NOT a gate.</b> Every other first-run
    /// surface is held or sent to the Inbox for the first ten minutes; this one offer is the single
    /// thing allowed through it, because it IS the onboarding the quiet window is protecting. It
    /// arrives non-modally, in a bubble, from a companion the user can dismiss with one click - and
    /// gating it on quiet would push the app's only tour offer past the point where anybody is
    /// still wondering what the app does.</para>
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
    /// <para>There is only one, and it carries the ask: <see cref="FreshMoment"/>, whose two chips
    /// are the walk offer itself. Asked by the summon that the knock itself triggered, so it must
    /// NOT consult <see cref="EmiState.KnockOffers"/> the way it used to - the flash spends the one
    /// offer a beat before this is read, and a cap check here would answer "nothing to say" to the
    /// very summon it just caused.</para>
    ///
    /// <para>Null means she has nothing scripted to say and the ordinary greeting stands - which is
    /// what a second summon this launch, a summon by an upgrader, or a summon after a yes all
    /// get.</para>
    /// </summary>
    public string? ContactMoment(IEmiKnockWorld w)
    {
        if (w == null) return null;
        if (w.KnockState >= Spent) return null;

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
    /// Is <paramref name="seen"/> strictly older than <paramref name="current"/>? No longer read by
    /// <see cref="Population"/> - the knock is fresh-installs-only - but kept as this feature's
    /// version arithmetic, pinned by its own tests, for the upgrade-tour surface that took the
    /// upgrader branch over. Parsed as a
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
