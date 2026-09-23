using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Homework;

/// <summary>
/// <c>App.Homework</c>. Holds the account's homework state from the proxy and keeps it fresh: on
/// launch, every 30 minutes, and after an opt-in change or a hand-in. Offline mode, no account or
/// a proxy that says <c>enabled:false</c> all read as <see cref="HomeworkToday.Idle"/>, which draws
/// nothing anywhere.
///
/// <para>Leaving is local first: the card and the Settings row let go at once, and the POST that
/// tells the server is retried on every refresh until it lands, so a dropped request can never
/// keep someone in. A hand-in the server did not answer is kept and retried the same way; watching
/// once is enough. A hand-in it REFUSED (not watched, wrong video, a new day) is dropped.</para>
/// </summary>
public sealed class HomeworkService : IDisposable
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);

    /// <summary>Gap before asking again after <c>busy</c> or no answer. Tests set it to zero.</summary>
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1.5);

    private readonly HomeworkApi _api;
    private readonly Func<string?> _account;
    private readonly Action<Action> _post;
    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly Timer _timer;

    private string? _stateAccount;
    private bool _pendingLeave;
    private (string Account, string Day, string Url, double Watched, double Duration)? _pendingHandIn;

    public HomeworkToday State { get; private set; } = HomeworkToday.Idle;

    /// <summary>State as it stands for the account signed in right now. A sign-out, an account
    /// switch or offline mode reads as idle at once, before any refresh.</summary>
    public HomeworkToday Current =>
        _stateAccount != null && string.Equals(_account(), _stateAccount, StringComparison.Ordinal) ? State : HomeworkToday.Idle;

    /// <summary>Due, and not already watched: a hand-in still waiting for the server counts as
    /// handed in here, so nobody is sent back to a video they finished.</summary>
    public bool IsDue => Current.Due && !(_pendingHandIn is { } p && p.Day == Current.Current?.Day);

    /// <summary>Raised on the UI thread when <see cref="State"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Raised on the UI thread when the server accepted a watch.</summary>
    public event Action? HandedIn;

    /// <param name="account">The signed-in unified id, or null (none, or offline mode).</param>
    /// <param name="post">Runs an action on the UI thread. Null = the app dispatcher.</param>
    public HomeworkService(HomeworkApi api, Func<string?> account, Action<Action>? post = null)
    {
        _api = api;
        _account = account;
        _post = post ?? (a => System.Windows.Application.Current?.Dispatcher?.BeginInvoke(a));
        _timer = new Timer(_ => _ = RefreshAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        _ = RefreshAsync();
        _timer.Change(RefreshEvery, RefreshEvery);
    }

    public async Task RefreshAsync()
    {
        await _one.WaitAsync().ConfigureAwait(false);
        try
        {
            var account = _account();
            if (account == null) { Apply(null, null); return; }

            if (_pendingLeave && (await _api.SetOptInAsync(false).ConfigureAwait(false)).Ok) _pendingLeave = false;
            if (_pendingHandIn is { } p && p.Account == account)
                await SendHandInAsync(account, p.Day, p.Url, p.Watched, p.Duration).ConfigureAwait(false);

            var reply = await _api.TodayAsync().ConfigureAwait(false);
            Apply(account, reply.Ok ? reply.Today : null);
        }
        catch (Exception ex) { App.Logger?.Debug("Homework refresh failed: {E}", ex.Message); }
        finally { _one.Release(); }
    }

    /// <summary>Join (true) or leave (false). Leaving always succeeds locally; joining is true only
    /// when the server said so. A <c>busy</c> or unanswered press is asked again twice.</summary>
    public async Task<bool> SetOptInAsync(bool on)
    {
        var account = _account();
        if (account == null) return false;
        if (!on)
        {
            _pendingLeave = true;
            if (string.Equals(account, _stateAccount, StringComparison.Ordinal))
                Apply(account, State with { OptedIn = false });
        }

        await _one.WaitAsync().ConfigureAwait(false);
        try
        {
            var reply = await _api.SetOptInAsync(on).ConfigureAwait(false);
            for (var retry = 0; retry < 2 && reply.Transient; retry++)
            {
                await Task.Delay(RetryDelay).ConfigureAwait(false);
                reply = await _api.SetOptInAsync(on).ConfigureAwait(false);
            }
            if (!reply.Ok)
            {
                if (reply.Today != null) Apply(account, reply.Today);
                return !on;
            }
            // The server heard this press. A join also cancels a leave still waiting to be
            // retried: the newest press wins.
            _pendingLeave = false;
            Apply(account, reply.Today);
            return (reply.Today?.OptedIn ?? false) == on;
        }
        finally { _one.Release(); }
    }

    /// <summary>Tells the server the homework was watched. True when it was accepted. An unanswered
    /// or busy send is kept and sent again on the next refresh; a refusal is final.</summary>
    public async Task<bool> HandInAsync(HomeworkCurrent hw, double watchedSeconds, double durationSeconds)
    {
        var account = _account();
        if (account == null) return false;
        await _one.WaitAsync().ConfigureAwait(false);
        try { return await SendHandInAsync(account, hw.Day, hw.Url, watchedSeconds, durationSeconds).ConfigureAwait(false); }
        finally { _one.Release(); }
    }

    private async Task<bool> SendHandInAsync(string account, string day, string url, double watched, double duration)
    {
        var reply = await _api.WatchedAsync(day, url, watched, duration).ConfigureAwait(false);
        if (reply.Transient)
        {
            await Task.Delay(RetryDelay).ConfigureAwait(false);
            reply = await _api.WatchedAsync(day, url, watched, duration).ConfigureAwait(false);
        }
        if (reply.Transient)
        {
            _pendingHandIn = (account, day, url, watched, duration);
            return false;
        }
        _pendingHandIn = null;
        if (!reply.Ok) App.Logger?.Information("Homework hand-in refused: {Reason}", reply.Reason);
        if (reply.Today != null) Apply(account, reply.Today);
        if (!reply.Ok || reply.Today?.Done != true) return false;
        _post(() => HandedIn?.Invoke());
        return true;
    }

    private void Apply(string? account, HomeworkToday? today)
    {
        // The reply is for the account the request was sent for. If somebody else is signed in by
        // now, it describes nobody on screen.
        if (!string.Equals(account, _account(), StringComparison.Ordinal)) return;
        var next = today ?? HomeworkToday.Idle;
        if (_pendingLeave && next.OptedIn) next = next with { OptedIn = false };
        var changed = next != State || account != _stateAccount;
        State = next;
        _stateAccount = account;
        if (changed) _post(() => Changed?.Invoke());
    }

    public void Dispose()
    {
        _timer.Dispose();
        _one.Dispose();
    }
}
