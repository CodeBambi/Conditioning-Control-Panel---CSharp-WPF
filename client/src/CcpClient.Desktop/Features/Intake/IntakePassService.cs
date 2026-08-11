using System.Globalization;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Intake;

/// <summary>
/// SP-054: the Weekly Intake Pass state machine (WPF Services/Progression/IntakePassService.cs,
/// 320 lines — read for this port), placeholder-shaped per the packet's honesty framing (b):
/// the entitlement hooks are TYPED SEAMS; completion-spend semantics land tested against
/// fixtures.
///
/// Ported semantics (File.cs:line against the WPF evidence):
/// <list type="bullet">
/// <item>ISO week key <c>"2026-W31"</c> via <see cref="ISOWeek"/> (:71-72) — Jan 1 can key
/// to the prior year's W52/W53 (the New-Year guard); the stored STRING is the authority,
/// never a 7-day delta; the reset is effectively Monday 00:00 local.</item>
/// <item>StartOfWeek / NextPassLocal / DaysUntilNextPass (:80-93).</item>
/// <item>Completion-spend ONLY (:166-185; the sole WPF spend site is the quiz-result path,
/// IntakeHostService.cs:406) — crash/abort/process-fail cost nothing.</item>
/// <item>&gt;5min future-stamped spend = clock rollback → Spent (:58, :104-110).</item>
/// <item>Fail-closed to Spent on ANY exception during state evaluation (:130-136).</item>
/// <item>Dual-provider late-premium refund (:232-251 hooks, :278-302) — ported against the
/// seam's TierChanged; fires only when the spend happened THIS session, the week still
/// matches, and the seam now reports premium.</item>
/// </list>
///
/// Typed seam (pre-approach consult ruling 5): greenfield has no login/entitlement
/// providers (the BLOCKED inventory's item). The default seam reports
/// <c>IsPremium=false</c>, <c>IsLoggedIn=null</c> (not-applicable) — the state lands
/// <see cref="IntakePassState.Available"/> with reason
/// <see cref="IntakePassReason.AvailableNoEntitlementProvider"/> so the intake RUNS per the
/// degraded-delivery contract. The NeedsLogin branch is UNREACHABLE this build — exercised
/// only through fake-seam unit tests; never silently collapsed.
/// </summary>
public sealed class IntakePassService
{
    /// <summary>The four WPF states (:11-29).</summary>
    public enum IntakePassState
    {
        Premium,
        NeedsLogin,
        Available,
        Spent,
    }

    /// <summary>WHY the state landed where it did (typed — never a bare enum).</summary>
    public enum IntakePassReason
    {
        /// <summary>An entitlement provider reports premium (passes don't spend for patrons).</summary>
        PremiumEntitled,

        /// <summary>A login provider exists and reports logged-out (UNREACHABLE this build —
        /// no login provider; fake-seam tests only).</summary>
        LoginRequired,

        /// <summary>The stored week key equals the current ISO week.</summary>
        SpentThisWeek,

        /// <summary>The stored spend stamp is >5min in the future — clock rolled back.</summary>
        SpentClockRollback,

        /// <summary>State evaluation threw — fail-closed (:130-136).</summary>
        SpentFailClosed,

        /// <summary>Week key differs and a login provider reports logged-in.</summary>
        AvailableThisWeek,

        /// <summary>Week key differs; NO entitlement provider exists (the greenfield default
        /// seam) — the intake runs per the degraded-delivery contract.</summary>
        AvailableNoEntitlementProvider,
    }

    /// <summary>The entitlement seam. <see cref="IsLoggedIn"/> is null when NO login
    /// provider exists (not-applicable — distinct from logged-out).</summary>
    public interface IIntakeEntitlementSource
    {
        bool IsPremium { get; }

        bool? IsLoggedIn { get; }

        /// <summary>Dual-provider TierChanged parity (:232-251) — the refund hook.</summary>
        event Action? TierChanged;
    }

    /// <summary>The greenfield default: no providers. IsPremium=false, IsLoggedIn=null
    /// (not-applicable); TierChanged never fires. The entitlement rows own a real source.</summary>
    public sealed class NoEntitlementSource : IIntakeEntitlementSource
    {
        public bool IsPremium => false;

        public bool? IsLoggedIn => null;

#pragma warning disable CS0067 // never raised — the typed seam's null default
        public event Action? TierChanged;
#pragma warning restore CS0067
    }

    /// <summary>The rollback tolerance (:58).</summary>
    public static readonly TimeSpan FutureSkewTolerance = TimeSpan.FromMinutes(5);

    private readonly PersistenceStore<IntakeSettingsDocument> _store;
    private readonly IIntakeEntitlementSource _entitlement;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<string> _log;
    private bool _spentThisSession;

    public IntakePassService(
        PersistenceStore<IntakeSettingsDocument> store,
        IIntakeEntitlementSource? entitlement,
        Func<DateTimeOffset>? utcNow,
        Action<string> log)
    {
        _store = store;
        _entitlement = entitlement ?? new NoEntitlementSource();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _log = log;
        _entitlement.TierChanged += RefundLateResolvedPremium;
    }

    /// <summary>The ISO week key (:71-72). Local time — the reset is Monday 00:00 LOCAL.</summary>
    public static string WeekKey(DateTime local) =>
        $"{ISOWeek.GetYear(local):D4}-W{ISOWeek.GetWeekOfYear(local):D2}";

    /// <summary>Monday 00:00 of the argument's ISO week (:80-81).</summary>
    public static DateTime StartOfWeek(DateTime local) =>
        local.Date.AddDays(-(((int)local.DayOfWeek + 6) % 7));

    /// <summary>The next pass opens at the next ISO week boundary (:84).</summary>
    public static DateTime NextPassLocal(DateTime local) => StartOfWeek(local).AddDays(7);

    /// <summary>Whole days until the next pass, at least 1 (:87-93).</summary>
    public static int DaysUntilNextPass(DateTime local) =>
        Math.Max(1, (int)Math.Ceiling((NextPassLocal(local) - local).TotalDays));

    /// <summary>&gt;5min future-stamped spend = clock rollback (:104-110).</summary>
    public bool ClockLooksRolledBack()
    {
        var spent = _store.Current.PassSpentUtc;
        return spent is { } s && s > _utcNow() + FutureSkewTolerance;
    }

    /// <summary>Evaluate the pass state (:114-138) — fail-CLOSED to Spent on any exception
    /// (:130-136). The reason is always typed.</summary>
    public (IntakePassState State, IntakePassReason Reason) Evaluate()
    {
        try
        {
            if (_entitlement.IsPremium)
            {
                return (IntakePassState.Premium, IntakePassReason.PremiumEntitled);
            }

            if (_entitlement.IsLoggedIn == false)
            {
                return (IntakePassState.NeedsLogin, IntakePassReason.LoginRequired);
            }

            if (ClockLooksRolledBack())
            {
                return (IntakePassState.Spent, IntakePassReason.SpentClockRollback);
            }

            var currentWeek = WeekKey(_utcNow().LocalDateTime);
            if (string.Equals(_store.Current.PassSpentWeek, currentWeek, StringComparison.Ordinal))
            {
                return (IntakePassState.Spent, IntakePassReason.SpentThisWeek);
            }

            return _entitlement.IsLoggedIn == true
                ? (IntakePassState.Available, IntakePassReason.AvailableThisWeek)
                : (IntakePassState.Available, IntakePassReason.AvailableNoEntitlementProvider);
        }
        catch (Exception ex)
        {
            _log($"intake-pass: state evaluation threw ({ex.GetType().Name}) — fail-CLOSED to Spent (WPF :130-136)");
            return (IntakePassState.Spent, IntakePassReason.SpentFailClosed);
        }
    }

    /// <summary>CanStartIntake = Premium || Available (:144-151).</summary>
    public bool CanStartIntake()
    {
        var (state, _) = Evaluate();
        return state is IntakePassState.Premium or IntakePassState.Available;
    }

    /// <summary>Completion-spend ONLY (:166-185): called from the quiz-result path and
    /// nowhere else. Patrons never spend. Writes the week key + stamp and saves.</summary>
    public void ConsumeForCompletedIntake()
    {
        if (_entitlement.IsPremium)
        {
            _log("intake-pass: premium — completion does not spend the pass (:166-170)");
            return;
        }

        var now = _utcNow();
        var week = WeekKey(now.LocalDateTime);
        _store.Mutate(d =>
        {
            d.PassSpentWeek = week;
            d.PassSpentUtc = now;
        });
        _spentThisSession = true;
        _ = _store.Save();
        _log($"intake-pass: Spent for this ISO week (completion-spend, :172-181)");
        PassStateChanged?.Invoke();
    }

    /// <summary>Raised after a spend (WPF PassStateChanged, :181).</summary>
    public event Action? PassStateChanged;

    /// <summary>Dual-provider late-premium refund (:278-302): ONLY when the spend happened
    /// THIS session, the stored week still equals the current week, and the seam now reports
    /// premium. Prior-session stamps are never refunded.</summary>
    private void RefundLateResolvedPremium()
    {
        try
        {
            if (!_spentThisSession || !_entitlement.IsPremium)
            {
                return;
            }

            var currentWeek = WeekKey(_utcNow().LocalDateTime);
            if (!string.Equals(_store.Current.PassSpentWeek, currentWeek, StringComparison.Ordinal))
            {
                return;
            }

            _store.Mutate(d =>
            {
                d.PassSpentWeek = string.Empty;
                d.PassSpentUtc = null;
            });
            _spentThisSession = false;
            _ = _store.Save();
            _log("intake-pass: late-resolved premium — this-session spend REFUNDED (:284-296)");
            PassStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _log($"intake-pass: refund path threw ({ex.GetType().Name}) — spend stands (typed, never silent)");
        }
    }
}
