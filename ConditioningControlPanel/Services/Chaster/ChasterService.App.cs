using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The app's own construction and the once-a-day settle clock. Kept apart so the
/// testable service never reaches for App.</summary>
public sealed partial class ChasterService
{
    private Timer? _settleTimer;
    private Timer? _lockTimer;
    private string? _lastSettleDay;

    public static ChasterService CreateForApp() => new(
        new ChasterClient(userAgent: $"ConditioningControlPanel/{UpdateService.AppVersion}"),
        new DpapiChasterTokenStore(),
        Path.Combine(App.UserDataPath, "chaster_tab.json"),
        () =>
        {
            var s = App.Settings?.Current;
            return s == null
                ? ChasterOptions.Off
                : new ChasterOptions(s.ChasterTabEnabled, s.ChasterLockId, new HashSet<string>(s.ChasterPrices ?? new List<string>(), StringComparer.Ordinal));
        });

    /// <summary>A minute after launch, then hourly. The hourly tick only acts when the local day
    /// has changed since the last attempt, so a day's bookings always get the rest of that day to
    /// be earned back before anything reaches the lock.</summary>
    public void StartDailySettle()
    {
        _settleTimer ??= new Timer(_ => _ = SettleIfNewDayAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));
        StartLockRefresh();
    }

    /// <summary>How often the rail chip's clock is allowed to be wrong. Fifteen minutes on a
    /// countdown that prints minutes means the digits are only ever stale by a rounding error,
    /// and the chip counts the rest of the way down locally between fetches.</summary>
    public static readonly TimeSpan LockRefreshEvery = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The lock's own clock. Five seconds after launch (late enough to be out of the startup
    /// crush, early enough that the rail chip is not showing an empty padlock while the player
    /// looks at it), then every <see cref="LockRefreshEvery"/>.
    ///
    /// <para>Its own timer rather than a share of the settle's: the settle is hourly and must stay
    /// hourly, and the two have nothing to say to each other. Costs no extra token refresh - the
    /// fetch takes whatever access token is already valid, and only mints one when that token has
    /// aged out, which it would have to do for the settle anyway.</para>
    /// </summary>
    public void StartLockRefresh()
    {
        _lockTimer ??= new Timer(_ => _ = RefreshLockAsync(), null, TimeSpan.FromSeconds(5), LockRefreshEvery);
    }

    /// <summary>Settle at most once per local day per run. Never throws: it runs on a timer thread.</summary>
    public async Task<SettleOutcome> SettleIfNewDayAsync()
    {
        try
        {
            var today = CircesTab.DayKey(_localNow());
            if (_lastSettleDay == today) return SettleOutcome.Nothing;
            var outcome = await SettleAsync().ConfigureAwait(false);
            // AFTER the settle, on purpose: what being away cost lands on the tab once today's
            // push has gone (or been found empty), so the player has the rest of the day to do
            // the session that forgives half of it.
            NoteSeen();
            // A try-later keeps the day open, so the next hourly tick goes again. So does a lock
            // nobody picked yet: once the player picks one, the push follows within the hour.
            if (outcome is not (SettleOutcome.TryLater or SettleOutcome.NoLockChosen)) _lastSettleDay = today;
            // A push is the one moment the lock is known to have moved, so the snapshot every
            // surface reads is re-read here rather than waiting up to a quarter of an hour to show
            // the time this app just added. A settle that pushed nothing moved nothing, and pays
            // for no call.
            if (outcome == SettleOutcome.Pushed) await RefreshLockAsync().ConfigureAwait(false);
            return outcome;
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "daily chaster settle");
            return SettleOutcome.TryLater;
        }
    }
}
