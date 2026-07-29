using System;
using System.Globalization;

namespace ConditioningControlPanel.Services;

/// <summary>
/// What the Graded Intake door looks like to the current user. The Exclusives page and the
/// Dashboard card both paint straight off this, so there is exactly one place that decides
/// who gets in and why.
/// </summary>
public enum IntakePassState
{
    /// <summary>Patron. The pass system does not apply - unlimited runs, no week, no door.</summary>
    Premium,
    /// <summary>Free and signed out. The pass is per-account, so there is nothing to hand out yet.</summary>
    NeedsLogin,
    /// <summary>Free, signed in, this week's run is still unspent.</summary>
    Available,
    /// <summary>Free, signed in, already ran the intake this week.</summary>
    Spent,
}

/// <summary>
/// The weekly free-tier pass for the Graded Intake.
///
/// The intake is a premium Exclusive, but it is also the best onboarding the app has: a run
/// drafts a personalised session, and that session is the first genuinely tailored thing a
/// new user ever sees. So free accounts get ONE run per week - enough to show the feature
/// off and to keep a reason to come back - while retakes stay a reason to subscribe.
///
/// Design notes worth keeping:
///
/// * <b>Weeks are stored as weeks.</b> The authority is an ISO week key ("2026-W31") in
///   <see cref="Models.AppSettings.IntakePassSpentWeek"/>, not a timestamp delta. A rolling
///   "7 days since last run" window drifts later every week (run it Sunday night, the next
///   one is Monday-after), which makes the shared weekly beat - the nudge, the Discord post -
///   impossible to schedule. Monday 00:00 local, same for everybody.
///
/// * <b>The pass is spent on COMPLETION, never on launch.</b> <see cref="ConsumeForCompletedIntake"/>
///   is called from the quiz-result path only. A crash, an abort, or a WebView2 failure has to
///   be free, or the cost of the app glitching is a user's entire week.
///
/// * <b>Authority is local, and that is a deliberate ceiling.</b> A rolled-back clock can buy
///   extra intakes. That is acceptable while the payout is XP and a session file. It stops
///   being acceptable the moment the punch card's prize is anything paid - see
///   <see cref="IntakePunchCardService"/> - which is why the prize is not granted from here.
/// </summary>
public class IntakePassService : IDisposable
{
    /// <summary>Tolerance for a spend stamped in the future before we call it clock tampering.
    /// Generous enough to absorb NTP corrections and timezone travel, tight enough that a
    /// deliberate roll-back does not quietly mint a pass.</summary>
    private static readonly TimeSpan FutureSkewTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Raised whenever the door may have changed state, so the gate and the Dashboard
    /// card can repaint. Fired on consume and on login/subscription changes.</summary>
    public event EventHandler? PassStateChanged;

    // ============================ week arithmetic ============================

    /// <summary>ISO-8601 week key for a local date, e.g. "2026-W31". Uses
    /// <see cref="ISOWeek"/> rather than hand-rolled day maths because the year boundary is
    /// where naive week numbering breaks: Jan 1 frequently belongs to the previous year's
    /// week 52/53, and a home-made key would hand out a bonus pass every New Year.</summary>
    public static string WeekKey(DateTime local) =>
        string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(local):D4}-W{ISOWeek.GetWeekOfYear(local):D2}");

    /// <summary>This week's key, in local time.</summary>
    public static string CurrentWeekKey() => WeekKey(DateTime.Now);

    /// <summary>Local midnight at the start of the ISO week containing <paramref name="local"/>
    /// (Monday). <see cref="DayOfWeek"/> counts Sunday as 0, so the +6 %7 rotation re-bases it
    /// onto Monday.</summary>
    public static DateTime StartOfWeek(DateTime local) =>
        local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));

    /// <summary>Local midnight when the next pass unlocks.</summary>
    public static DateTime NextPassLocal => StartOfWeek(DateTime.Now).AddDays(7);

    /// <summary>Whole days until the next pass, floored at 1 so the UI never says "in 0 days".</summary>
    public static int DaysUntilNextPass
    {
        get
        {
            var span = NextPassLocal - DateTime.Now;
            return Math.Max(1, (int)Math.Ceiling(span.TotalDays));
        }
    }

    // ============================ state ============================

    private static bool IsPremium => App.Patreon?.HasPremiumAccess == true;

    /// <summary>True when the spend stamp sits in the future by more than
    /// <see cref="FutureSkewTolerance"/> - i.e. the machine clock moved backwards after a run.
    /// Treated as "still spent": we would rather a genuinely time-travelling user waits than
    /// hand out an unbounded supply of passes to anyone who can change the date.</summary>
    private static bool ClockLooksRolledBack
    {
        get
        {
            var spent = App.Settings?.Current?.IntakePassSpentUtc;
            return spent.HasValue && spent.Value > DateTime.UtcNow.Add(FutureSkewTolerance);
        }
    }

    /// <summary>The single source of truth for who may open the intake and what the door says.</summary>
    public IntakePassState State
    {
        get
        {
            try
            {
                if (IsPremium) return IntakePassState.Premium;
                if (!App.IsLoggedIn) return IntakePassState.NeedsLogin;
                if (ClockLooksRolledBack) return IntakePassState.Spent;

                var spentWeek = App.Settings?.Current?.IntakePassSpentWeek ?? "";
                return string.Equals(spentWeek, CurrentWeekKey(), StringComparison.Ordinal)
                    ? IntakePassState.Spent
                    : IntakePassState.Available;
            }
            catch (Exception ex)
            {
                // Never let a settings hiccup wedge the Exclusives page. Closed is the safe
                // default: premium users are unaffected, and a free user sees the normal
                // "come back next week" copy rather than a broken panel.
                App.Logger?.Debug("IntakePassService.State: {E}", ex.Message);
                return IntakePassState.Spent;
            }
        }
    }

    /// <summary>An unspent free run is waiting. False for patrons - they have no pass, they
    /// have the feature - so callers wanting "may this person start an intake?" want
    /// <see cref="CanStartIntake"/> instead.</summary>
    public bool IsPassAvailable => State == IntakePassState.Available;

    /// <summary>May this user open the Graded Intake right now?</summary>
    public bool CanStartIntake
    {
        get
        {
            var s = State;
            return s == IntakePassState.Premium || s == IntakePassState.Available;
        }
    }

    /// <summary>Should the Dashboard play the flip ceremony? Only when a pass is actually
    /// waiting AND this week's spin has not already run. The card stays on the Dashboard
    /// either way - this gates the animation, not the affordance, so someone who was on
    /// another tab when the week rolled has not missed anything.</summary>
    public bool ShouldPlayCeremony
    {
        get
        {
            if (!IsPassAvailable) return false;
            var seen = App.Settings?.Current?.IntakePassCeremonyWeek ?? "";
            return !string.Equals(seen, CurrentWeekKey(), StringComparison.Ordinal);
        }
    }

    // ============================ transitions ============================

    /// <summary>Remember that this week's spin has played, so returning to the Dashboard does
    /// not replay it.</summary>
    public void MarkCeremonyPlayed()
    {
        try
        {
            var settings = App.Settings?.Current;
            if (settings == null) return;
            var week = CurrentWeekKey();
            if (string.Equals(settings.IntakePassCeremonyWeek, week, StringComparison.Ordinal)) return;
            settings.IntakePassCeremonyWeek = week;
            App.Settings?.Save();
        }
        catch (Exception ex) { App.Logger?.Debug("IntakePassService.MarkCeremonyPlayed: {E}", ex.Message); }
    }

    /// <summary>
    /// Spend this week's pass. Called ONLY from the completed-intake path (a quiz-result
    /// actually arrived from the page), never from the launch path - see the class remarks.
    ///
    /// A no-op for patrons: they never consumed a pass to get in, so there is nothing to
    /// spend, and writing the week key for them would leave stale state behind if their
    /// subscription later lapsed.
    /// </summary>
    public void ConsumeForCompletedIntake()
    {
        try
        {
            if (IsPremium) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            settings.IntakePassSpentWeek = CurrentWeekKey();
            settings.IntakePassSpentUtc = DateTime.UtcNow;
            _spentThisSession = true;
            App.Settings?.Save();

            App.Logger?.Information("IntakePassService: weekly pass spent for {Week}", settings.IntakePassSpentWeek);
            RaiseChanged();
        }
        catch (Exception ex) { App.Logger?.Warning("IntakePassService.ConsumeForCompletedIntake: {E}", ex.Message); }
    }

    /// <summary>Tell listeners the door may look different now - login, logout, or a
    /// subscription refresh. Cheap enough to call speculatively.</summary>
    public void RaiseChanged()
    {
        try { PassStateChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { App.Logger?.Debug("IntakePassService.RaiseChanged: {E}", ex.Message); }
    }

    // ============================ entitlement plumbing ============================
    //
    // The pass is a FREE-TIER amenity, so <see cref="State"/> is a function of entitlement -
    // and entitlement is not known when MainWindow first paints. PatreonService validates
    // asynchronously (step 4 of App.OnStartup) and the window is already up by the time the
    // answer lands, so a genuine patron on a fresh install (or one whose 2-week cached-premium
    // grace has expired) renders as free for the first second or two and would be shown pass UI
    // they must never see. The tier events below are the moment the truth arrives; re-raising
    // PassStateChanged from them makes every listener - the Dashboard tile, the Exclusives gate -
    // repaint off the corrected answer.
    //
    // Both providers are hooked because HasPremiumAccess OR's SubscribeStar in (see
    // PatreonService.HasPremiumAccess): a SubscribeStar-only patron never moves PatreonService's
    // tier, so listening to Patreon alone would leave them looking free forever.

    /// <summary>True once the pass has been spent by THIS process. Bounds the late-premium
    /// refund in <see cref="OnEntitlementChanged"/> to a spend we can be sure we charged while
    /// entitlement was still unresolved - a stamp inherited from a previous session is left
    /// alone, so subscribing later never retroactively refunds an old week.</summary>
    private bool _spentThisSession;

    /// <summary>One-shot latch + the handler instance we registered, kept so
    /// <see cref="Dispose"/> can detach exactly what it attached.</summary>
    private bool _entitlementHooked;
    private EventHandler<ConditioningControlPanel.Models.PatreonTier>? _entitlementHandler;

    /// <summary>
    /// Subscribe to the subscription providers so the door re-evaluates whenever entitlement
    /// resolves, changes, or is torn down by a logout. Idempotent and safe to call once the
    /// providers exist - which is LATER in App.OnStartup than this service is constructed, hence
    /// a separate call rather than constructor work.
    /// </summary>
    public void AttachEntitlementSources()
    {
        if (_entitlementHooked) return;
        _entitlementHooked = true;
        _entitlementHandler = OnEntitlementChanged;

        try
        {
            var patreon = App.Patreon;
            if (patreon != null) patreon.TierChanged += _entitlementHandler;
        }
        catch (Exception ex) { App.Logger?.Debug("IntakePassService: Patreon hook failed: {E}", ex.Message); }

        try
        {
            var subscribeStar = App.SubscribeStar;
            if (subscribeStar != null) subscribeStar.TierChanged += _entitlementHandler;
        }
        catch (Exception ex) { App.Logger?.Debug("IntakePassService: SubscribeStar hook failed: {E}", ex.Message); }
    }

    /// <summary>
    /// Entitlement moved. Fires from an async validation continuation on a background thread, so
    /// this deliberately does NO UI work: it only re-raises <see cref="PassStateChanged"/>, and
    /// every listener is responsible for marshalling itself (both current ones do).
    /// </summary>
    private void OnEntitlementChanged(object? sender, ConditioningControlPanel.Models.PatreonTier tier)
    {
        try
        {
            RefundLateResolvedPremium();
            App.Logger?.Debug("IntakePassService: entitlement changed (tier {Tier}) -> pass state {State}", tier, State);
            RaiseChanged();
        }
        catch (Exception ex) { App.Logger?.Debug("IntakePassService.OnEntitlementChanged: {E}", ex.Message); }
    }

    /// <summary>
    /// Hands back a pass we charged before we knew the user was a patron.
    ///
    /// <see cref="ConsumeForCompletedIntake"/> is already a no-op for patrons, but it reads
    /// entitlement at the moment of the spend. If validation had not landed yet - offline start,
    /// slow proxy, fresh install with no cached grace - a patron's completed run writes the week
    /// key against them. Nothing is visibly wrong while they stay subscribed (Premium outranks
    /// Spent), but the stale stamp becomes real the moment their subscription lapses inside the
    /// same week, which is exactly the "leave stale state behind" case the class remarks warn about.
    ///
    /// Narrow on purpose: only a spend made in this process, only for the current week, and only
    /// when entitlement has actually resolved to premium. A stamp from a previous session is never
    /// touched, so this cannot be used to reset an old week by subscribing.
    /// </summary>
    private void RefundLateResolvedPremium()
    {
        try
        {
            if (!_spentThisSession || !IsPremium) return;

            var settings = App.Settings?.Current;
            if (settings == null) return;

            // Only the week we just charged. Anything else is not ours to give back.
            if (!string.Equals(settings.IntakePassSpentWeek, CurrentWeekKey(), StringComparison.Ordinal))
            {
                _spentThisSession = false;
                return;
            }

            settings.IntakePassSpentWeek = "";
            settings.IntakePassSpentUtc = null;
            _spentThisSession = false;
            App.Settings?.Save();

            App.Logger?.Information(
                "IntakePassService: premium resolved after a run this session - weekly pass refunded (it was never theirs to spend)");
        }
        catch (Exception ex) { App.Logger?.Debug("IntakePassService.RefundLateResolvedPremium: {E}", ex.Message); }
    }

    /// <summary>Detach the tier hooks. The service outlives every window, but the providers
    /// outlive IT (they are App singletons torn down last), so leaving the handlers attached
    /// across an app shutdown keeps this object - and anything still subscribed to
    /// <see cref="PassStateChanged"/>, i.e. MainWindow - reachable during teardown.</summary>
    public void Dispose()
    {
        try
        {
            if (_entitlementHandler != null)
            {
                try { var p = App.Patreon; if (p != null) p.TierChanged -= _entitlementHandler; } catch { }
                try { var s = App.SubscribeStar; if (s != null) s.TierChanged -= _entitlementHandler; } catch { }
                _entitlementHandler = null;
            }
            _entitlementHooked = false;
            PassStateChanged = null;
        }
        catch (Exception ex) { App.Logger?.Debug("IntakePassService.Dispose: {E}", ex.Message); }
    }
}
