using System;
using System.Net.Http;
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

    public static ChasterService CreateForApp()
    {
        Func<ChasterOptions> options = () =>
        {
            var s = App.Settings?.Current;
            return s == null
                ? ChasterOptions.Off
                : new ChasterOptions(s.ChasterTabEnabled, s.ChasterLockId, new HashSet<string>(s.ChasterPrices ?? new List<string>(), StringComparer.Ordinal));
        };
#if DEBUG
        if (DemoService(options) is { } demo) return demo;
#endif
        return new(
            new ChasterClient(userAgent: $"ConditioningControlPanel/{UpdateService.AppVersion}"),
            new DpapiChasterTokenStore(),
            Path.Combine(App.UserDataPath, "chaster_tab.json"),
            options);
    }

#if DEBUG
    /// <summary>The demo switch, the same shape as <c>CCP_PRIZE_GRANTS</c>: with the environment
    /// variable set, the service is linked to a fake Chaster that owns one test lock twelve days
    /// out and answers every call locally. The tab lives in its own file and starts with a few
    /// bookings on it, so the page has something to show. Compiled only under DEBUG; a Release
    /// build never reads the variable and never talks to anything but the real proxy.</summary>
    public const string DemoEnvVar = "CCP_CHASTER_DEMO";

    private static ChasterService? DemoService(Func<ChasterOptions> options)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DemoEnvVar))) return null;
        var tabPath = Path.Combine(App.UserDataPath, "chaster_tab.demo.json");
        try { File.Delete(tabPath); } catch (Exception ex) { Diag.Swallowed(ex); }
        var svc = new ChasterService(new ChasterClient(new DemoChaster()), new DemoTokens(), tabPath,
            () => options() with { LockId = "demo-lock" });
        svc.Note("typo", 3);
        svc.Note("attention");
        svc.Note("escape");
        svc.Note("session");
        svc.Note("quest");
        App.Logger?.Warning("[Chaster] DEMO mode: fake account, fake lock, nothing reaches Chaster");
        return svc;
    }

    private sealed class DemoTokens : IChasterTokenStore
    {
        private ChasterStoredTokens? _tokens = new("demo", "demo", DateTime.UtcNow.AddMinutes(4));
        public ChasterStoredTokens? Read() => _tokens;
        public void Write(ChasterStoredTokens tokens) => _tokens = tokens;
        public void Clear() => _tokens = null;
    }

    private sealed class DemoChaster : HttpMessageHandler
    {
        private static readonly DateTime Ends = DateTime.UtcNow.AddDays(12).AddHours(4);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/chaster/refresh")
                return Task.FromResult(Json("{\"access_token\":\"demo\",\"expires_in\":300}"));
            if (path == "/locks")
                return Task.FromResult(Json("[{\"_id\":\"demo-lock\",\"title\":\"Locktober\",\"status\":\"locked\",\"role\":\"wearer\",\"endDate\":\""
                    + Ends.ToString("o") + "\",\"isFrozen\":false,\"displayRemainingTime\":true,\"isAllowedToViewTime\":true,\"isTestLock\":true}]"));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NoContent));
        }

        private static HttpResponseMessage Json(string body) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    }
#endif

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
