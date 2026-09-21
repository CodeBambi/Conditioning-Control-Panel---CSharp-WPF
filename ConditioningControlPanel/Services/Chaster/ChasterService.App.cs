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
    }

    /// <summary>Settle at most once per local day per run. Never throws: it runs on a timer thread.</summary>
    public async Task<SettleOutcome> SettleIfNewDayAsync()
    {
        try
        {
            var today = CircesTab.DayKey(_localNow());
            if (_lastSettleDay == today) return SettleOutcome.Nothing;
            var outcome = await SettleAsync().ConfigureAwait(false);
            // A try-later keeps the day open, so the next hourly tick goes again.
            if (outcome != SettleOutcome.TryLater) _lastSettleDay = today;
            return outcome;
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "daily chaster settle");
            return SettleOutcome.TryLater;
        }
    }
}
