using System;
using System.Net.Http;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The push clocks. The app's own construction lives in the head (ChasterServiceApp).</summary>
public sealed partial class ChasterService
{
    private Timer? _settleTimer;
    private Timer? _lockTimer;
    private string? _lastSettleDay;
    private Timer? _pushTimer;
    private bool _pushArmed;
    private volatile bool _live;

    /// <summary>How long a booking waits before it goes out, so a burst of slip-ups lands on
    /// the lock as one line in its history instead of a line per typo.</summary>
    public static readonly TimeSpan PushDelay = TimeSpan.FromSeconds(30);

    /// <summary>The retry clock for a push that could not go out (Chaster down, no lock picked
    /// yet), and the once-a-day "misses you" check. A minute after launch, then every ten.</summary>
    public static readonly TimeSpan TickEvery = TimeSpan.FromMinutes(10);

    /// <summary>Live from here on: bookings schedule their own push, and the tick retries.</summary>
    public void StartSettle()
    {
        _live = true;
        _settleTimer ??= new Timer(_ => _ = TickAsync(), null, TimeSpan.FromMinutes(1), TickEvery);
        StartLockRefresh();
    }

    /// <summary>A booking that added time. The first one arms the push; the ones inside the
    /// window ride along with it.</summary>
    private void SchedulePush()
    {
        if (!_live) return;
        lock (_gate)
        {
            if (_pushArmed) return;
            _pushArmed = true;
            _pushTimer ??= new Timer(_ => _ = PushNowAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _pushTimer.Change(PushDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task PushNowAsync()
    {
        lock (_gate) _pushArmed = false;
        try
        {
            if (await SettleAsync().ConfigureAwait(false) == SettleOutcome.Pushed)
                await RefreshLockAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { Diag.Swallowed(ex, "chaster push"); }
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

    /// <summary>Push whatever is waiting, then, once per local day, book what being away cost.
    /// Never throws: it runs on a timer thread.</summary>
    public async Task<SettleOutcome> TickAsync()
    {
        try
        {
            var outcome = await SettleAsync().ConfigureAwait(false);
            var today = CircesTab.DayKey(_localNow());
            // AFTER the push, on purpose: what being away cost is booked after the old balance
            // went out, so the player has the rest of the day to do the session that forgives half.
            if (_lastSettleDay != today)
            {
                _lastSettleDay = today;
                NoteSeen();
            }
            if (outcome == SettleOutcome.Pushed) await RefreshLockAsync().ConfigureAwait(false);
            return outcome;
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "chaster tick");
            return SettleOutcome.TryLater;
        }
    }
}
