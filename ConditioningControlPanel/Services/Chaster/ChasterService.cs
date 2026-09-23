using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>What the player chose. Read fresh on every call, so a switch flipped in Settings
/// takes hold on the next event with no restart. <paramref name="RemoteOpen"/> is a Remote
/// session running right now; <paramref name="PanicArmed"/> is the panic key switched on.</summary>
public sealed record ChasterOptions(bool TabEnabled, string? LockId, ISet<string> Prices, TabLimits? Limits = null,
    bool RemoteOpen = false, bool PanicArmed = true)
{
    public TabLimits Caps => Limits ?? TabLimits.Default;

    public static readonly ChasterOptions Off = new(false, null, new HashSet<string>());
}

public sealed record ChasterStoredTokens(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);

/// <summary>Where the link's tokens live. The app's one is DPAPI on disk; tests use memory.</summary>
public interface IChasterTokenStore
{
    ChasterStoredTokens? Read();
    void Write(ChasterStoredTokens tokens);
    void Clear();
}

public enum SettleOutcome
{
    /// <summary>Nothing to send: tab off, not linked, already pushed today, or no positive balance.</summary>
    Nothing,
    Pushed,
    /// <summary>More than one active lock and the player has not picked one, or the one they
    /// picked has ended. Either way: ask, never guess.</summary>
    NoLockChosen,
    LinkExpired,
    /// <summary>Chaster did not take it this time. The balance waits; nothing is lost.</summary>
    TryLater,
}

/// <summary>
/// The Chaster link and Circe's tab, as one service: who is linked, what is on the tab, and the
/// once-a-day settle that turns a positive balance into lock time.
///
/// <para>Three rules sit here rather than in <see cref="CircesTab"/> because they need the
/// world: nothing books unless the tab is ON and an account is LINKED; a panic press or an
/// emergency exit opens a safety hold during which nothing adds; and a settle only ever calls
/// <see cref="ChasterClient.AddTimeAsync"/>, never anything that could take a lock's time down
/// or open it.</para>
///
/// <para>The link flow (browser + loopback listener) is in ChasterService.Link.cs.</para>
/// </summary>
public sealed partial class ChasterService : IDisposable
{
    /// <summary>After a panic press or an emergency exit, nothing adds for this long. Long
    /// enough that leaving is never priced, short enough that one press is not a free day.</summary>
    public static readonly TimeSpan SafetyHold = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The most a local day can add while a Remote session is open, whatever the player's own
    /// daily limit (2026-09-23 security pass). A controller drives flashes, lock cards, bubbles and
    /// sessions, and every one of those can carry a price, so this counts EVERY add booked while
    /// the session is open, not only the two remote rows. Per day, not per session, so reconnecting
    /// does not hand out a fresh share. And with the panic key switched off (a controller can send
    /// disable_panic) nothing adds at all: the safety hold hangs off that key.
    /// </summary>
    public const int RemoteDailySeconds = 30 * 60;

    private readonly ChasterClient _client;
    private readonly IChasterTokenStore _tokens;
    private readonly string _tabPath;
    private readonly Func<ChasterOptions> _options;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<DateTime> _localNow;
    private readonly DateTime _runStartUtc;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _settleGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private TabState _tab;
    private DateTime _safetyUntilUtc = DateTime.MinValue;
    private int _pushedThisRun;

    /// <summary>A price landed (or a credit, or the jackpot wipe). The flashing "+0:30".</summary>
    public event Action<string, TabBooking>? Booked;

    /// <summary>Linked, unlinked, or the link died. Raised on whatever thread found out.</summary>
    public event Action? LinkChanged;

    public ChasterService(ChasterClient client, IChasterTokenStore tokens, string tabPath,
        Func<ChasterOptions> options, Func<DateTime>? utcNow = null, Func<DateTime>? localNow = null)
    {
        _client = client;
        _tokens = tokens;
        _tabPath = tabPath;
        _options = options;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _localNow = localNow ?? (() => DateTime.Now);
        _runStartUtc = _utcNow();
        _tab = LoadTab();
    }

    public bool IsLinked => _tokens.Read() is { RefreshToken.Length: > 0 };

    /// <summary>The player's two limits, as the next booking will read them.</summary>
    public TabLimits Caps => (_options() ?? ChasterOptions.Off).Caps;

    /// <summary>Seconds on the tab and not on the lock yet. Negative is credit.</summary>
    public int BalanceSeconds { get { lock (_gate) return _tab.BalanceSeconds; } }

    /// <summary>Gross adds booked today, for "Today 12:30 of 60:00". A day nothing was booked
    /// on yet reads 0, whatever yesterday left behind.</summary>
    public int TodayAddedSeconds
    {
        get { lock (_gate) return _tab.Day == CircesTab.DayKey(_localNow()) ? _tab.DayAddedSeconds : 0; }
    }

    /// <summary>Would a Note for this row book right now: tab on, account linked, row switched
    /// on, not a way-out id, and no safety hold running. For the cues that are dealt BEFORE the
    /// event (Natasha's red bubble), so nothing wears a price it cannot charge.</summary>
    public bool CanBook(string eventId)
    {
        if (string.IsNullOrEmpty(eventId) || TabPrices.NeverPriced.Contains(eventId)) return false;
        if (!Active(out var options) || !options.Prices.Contains(eventId) || TabPrices.Find(eventId) == null) return false;
        lock (_gate) return _utcNow() >= _safetyUntilUtc && RemoteRoom(options) > 0;
    }

    private bool Active(out ChasterOptions options)
    {
        options = _options() ?? ChasterOptions.Off;
        return options.TabEnabled && IsLinked;
    }

    /// <summary>A priced event happened. Books nothing unless the tab is on, an account is linked
    /// and the player switched this row on. Safe to call from anywhere, on any thread.</summary>
    public TabBooking Note(string eventId, int units = 1)
    {
        if (!Active(out var options)) return new(0, TabRefusal.Nothing);
        // The first finished session after coming back forgives half of what being away cost,
        // whether or not the session row itself is switched on.
        if (eventId == "session") ForgiveMisses();
        // "misses" has a row so it can be switched on, but only NoteSeen ever books it.
        if (eventId == CircesMisses.EventId) return new(0, TabRefusal.Nothing);
        return BookSeconds(eventId, TabPrices.Resolve(eventId, options.Prices, units));
    }

    /// <summary>CCP is running today. Call at launch and when the local day turns over. With
    /// the "misses" row on, every full day since the last linked run is booked on its own date,
    /// so the 60:00 day cap and the backlog cap both still hold. Returns the seconds booked.</summary>
    public int NoteSeen()
    {
        if (!IsLinked) return 0;
        var booked = 0;
        lock (_gate)
        {
            var local = _localNow();
            var today = CircesTab.DayKey(local);
            var last = _tab.LastSeenDay;
            if (last == today) return 0;
            _tab.LastSeenDay = today;
            if (Active(out var options) && options.Prices.Contains(CircesMisses.EventId)
                && CircesMisses.TryDay(last, out var lastDay))
            {
                var charges = CircesMisses.Charges(CircesMisses.DaysAway(last, local));
                for (var i = 0; i < charges.Count; i++)
                    booked += CircesTab.Book(_tab, CircesMisses.EventId, charges[i], _utcNow(),
                        lastDay.AddDays(i + 1).AddHours(12), _runStartUtc, safetyExit: false, options.Caps).AppliedSeconds;
                if (booked > 0) _tab.ForgivableSeconds = CircesMisses.Forgivable(booked);
            }
            SaveTab();
        }
        if (booked > 0)
        {
            Booked?.Invoke(CircesMisses.EventId, new TabBooking(booked, TabRefusal.None));
            SchedulePush();
        }
        return booked;
    }

    private void ForgiveMisses()
    {
        TabBooking booking;
        lock (_gate)
        {
            if (_tab.ForgivableSeconds <= 0) return;
            booking = CircesTab.Book(_tab, CircesMisses.ForgivenEventId, -_tab.ForgivableSeconds, _utcNow(), _localNow(), _runStartUtc, safetyExit: false);
            _tab.ForgivableSeconds = 0;
            SaveTab();
        }
        if (booking.Booked) Booked?.Invoke(CircesMisses.ForgivenEventId, booking);
    }

    /// <summary>An event that names its own price (an Awareness trigger carries its minutes in
    /// the preset). Same tab, same cap, same safety hold, and the row still has to be switched
    /// on: a toggle on the page that reads off must mean off. Only the AMOUNT skips the table.</summary>
    public TabBooking NoteSeconds(string eventId, int seconds)
    {
        if (!Active(out var options) || TabPrices.NeverPriced.Contains(eventId ?? "")) return new(0, TabRefusal.Nothing);
        if (!options.Prices.Contains(eventId!)) return new(0, TabRefusal.Nothing);
        return BookSeconds(eventId!, Math.Clamp(seconds, -TabLimits.MaxDailySeconds, TabLimits.MaxDailySeconds));
    }

    /// <summary>The jackpot. Wipes the tab, never the lock.</summary>
    public TabBooking Wipe()
    {
        if (!Active(out _)) return new(0, TabRefusal.Nothing);
        TabBooking booking;
        lock (_gate)
        {
            booking = CircesTab.Wipe(_tab, _utcNow(), _runStartUtc);
            if (booking.Booked) SaveTab();
        }
        if (booking.Booked) Booked?.Invoke(CircesTab.JackpotEventId, booking);
        return booking;
    }

    /// <summary>Panic was pressed or an emergency exit opened. Call it from those paths and from
    /// nowhere else; it never books anything itself.</summary>
    public void NoteSafetyExit()
    {
        lock (_gate) _safetyUntilUtc = _utcNow() + SafetyHold;
    }

    // Caller holds _gate. What an add may still book today with a Remote session open: all of it
    // when none is open, nothing with the panic key off.
    private int RemoteRoom(ChasterOptions options)
    {
        if (!options.RemoteOpen) return int.MaxValue;
        if (!options.PanicArmed) return 0;
        var used = _tab.RemoteDay == CircesTab.DayKey(_localNow()) ? Math.Max(0, _tab.RemoteDaySeconds) : 0;
        return Math.Max(0, RemoteDailySeconds - used);
    }

    private TabBooking BookSeconds(string eventId, int seconds)
    {
        if (seconds == 0) return new(0, TabRefusal.Nothing);
        TabBooking booking;
        lock (_gate)
        {
            var now = _utcNow();
            var options = _options() ?? ChasterOptions.Off;
            var remote = seconds > 0 && options.RemoteOpen;
            if (remote)
            {
                var room = RemoteRoom(options);
                if (room == 0) return new(0, TabRefusal.Remote);
                seconds = Math.Min(seconds, room);
            }
            booking = CircesTab.Book(_tab, eventId, seconds, now, _localNow(), _runStartUtc, safetyExit: now < _safetyUntilUtc, options.Caps);
            if (remote && booking.AppliedSeconds > 0)
            {
                var today = CircesTab.DayKey(_localNow());
                if (_tab.RemoteDay != today) { _tab.RemoteDay = today; _tab.RemoteDaySeconds = 0; }
                _tab.RemoteDaySeconds += booking.AppliedSeconds;
                if (booking.Refusal == TabRefusal.None && _tab.RemoteDaySeconds >= RemoteDailySeconds)
                    booking = booking with { Refusal = TabRefusal.Remote };
            }
            if (booking.Booked) SaveTab();
        }
        if (booking.Booked) Booked?.Invoke(eventId, booking);
        if (booking.AppliedSeconds > 0) SchedulePush();
        return booking;
    }

    /// <summary>This run's receipt.</summary>
    public TabBill Bill()
    {
        lock (_gate) return TabBill.Build(_tab.Entries.ToList(), _runStartUtc, _pushedThisRun);
    }

    /// <summary>The wearer's active locks, or null when the link cannot be used right now.</summary>
    public async Task<IReadOnlyList<ChasterLock>?> GetLocksAsync(CancellationToken ct = default)
    {
        var access = await AccessTokenAsync(ct).ConfigureAwait(false);
        if (access == null) return null;
        var locks = await _client.GetLocksAsync(access, ct).ConfigureAwait(false);
        if (locks.Status == ChasterStatus.LinkExpired) DropLink();
        return locks.Ok ? locks.Value : null;
    }

    /// <summary>Send the tab to the lock, if today's push has not gone and the balance is
    /// positive. Calling it more often is harmless. Never call it on the way out of the app: a
    /// call cut off mid-flight is exactly the doubt the pending mark exists for.</summary>
    public async Task<SettleOutcome> SettleAsync(CancellationToken ct = default)
    {
        if (!Active(out var options)) return SettleOutcome.Nothing;
        await _settleGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            TabPush plan;
            lock (_gate)
            {
                // The file on disk is not trusted: clamp it before anything is planned from it.
                if (CircesTab.Sanitise(_tab, options.Caps))
                {
                    SaveTab();
                    App.Logger?.Warning("[Chaster] the tab file held numbers outside its limits; clamped");
                }
                // An add from last time that was never answered: counted as landed, never resent.
                var doubted = CircesTab.ResolvePending(_tab);
                if (doubted > 0)
                {
                    SaveTab();
                    App.Logger?.Information("[Chaster] an unanswered push of {Seconds}s is counted as landed", doubted);
                }
                // A wearer link can only add. canRemove stays false until a link exists that can.
                // Never more than the daily limit reaches the lock in one local day, however big
                // the balance is.
                plan = CircesTab.PlanPush(_tab, canRemove: false, options.Caps, _localNow());
            }
            if (plan.Kind != TabPushKind.Add) return SettleOutcome.Nothing;
            // A backlog from days Chaster was unreachable still lands an hour at a time.
            plan = plan with { Seconds = Math.Min(plan.Seconds, ChasterClient.MaxAddSeconds) };

            var access = await AccessTokenAsync(ct).ConfigureAwait(false);
            if (access == null) return IsLinked ? SettleOutcome.TryLater : SettleOutcome.LinkExpired;

            var lockId = options.LockId;
            if (string.IsNullOrEmpty(lockId))
            {
                var locks = await _client.GetLocksAsync(access, ct).ConfigureAwait(false);
                if (!locks.Ok) return Failed(locks.Status);
                if (locks.Value!.Count != 1) return locks.Value.Count == 0 ? SettleOutcome.Nothing : SettleOutcome.NoLockChosen;
                lockId = locks.Value[0].Id;
            }

            lock (_gate)
            {
                CircesTab.MarkPending(_tab, plan, _localNow());
                SaveTab();
            }
            var added = await _client.AddTimeAsync(access, lockId!, plan.Seconds, ct).ConfigureAwait(false);
            // No answer at all: the mark stays, and the next settle counts the push as landed.
            if (added.Status == ChasterStatus.TimedOut) return SettleOutcome.TryLater;

            lock (_gate)
            {
                CircesTab.ClearPending(_tab);
                if (added.Ok)
                {
                    CircesTab.ApplyPush(_tab, plan, _localNow());
                    _pushedThisRun += plan.Seconds;
                }
                SaveTab();
            }
            if (!added.Ok) return Failed(added.Status);
            App.Logger?.Information("[Chaster] settled {Seconds}s to the lock", plan.Seconds);
            return SettleOutcome.Pushed;
        }
        finally { _settleGate.Release(); }
    }

    private SettleOutcome Failed(ChasterStatus status)
    {
        // The chosen lock ended or was never this wearer's. Picking another is the player's call.
        if (status == ChasterStatus.NotFound) return SettleOutcome.NoLockChosen;
        if (status != ChasterStatus.LinkExpired) return SettleOutcome.TryLater;
        DropLink();
        return SettleOutcome.LinkExpired;
    }

    /// <summary>Forget the link on this machine and tell Chaster to forget it too. The tab
    /// stays: unlinking is a way out, not a way to clear what was already owed or earned.</summary>
    public async Task UnlinkAsync()
    {
        var tokens = _tokens.Read();
        _tokens.Clear();
        ForgetProfile();
        LinkChanged?.Invoke();
        if (tokens is { RefreshToken.Length: > 0 }) await _client.RevokeAsync(tokens.RefreshToken).ConfigureAwait(false);
    }

    private void StoreTokens(ChasterTokens fresh, string? previousRefresh)
    {
        var refresh = string.IsNullOrEmpty(fresh.RefreshToken) ? previousRefresh : fresh.RefreshToken;
        _tokens.Write(new ChasterStoredTokens(fresh.AccessToken, refresh ?? "", _utcNow().AddSeconds(Math.Max(0, fresh.ExpiresIn))));
    }

    // One refresh at a time. If Chaster rotates refresh tokens, two at once means the second
    // presents a spent token, gets a 401, and a perfectly good link is dropped.
    private async Task<string?> AccessTokenAsync(CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tokens = _tokens.Read();
            if (tokens == null || string.IsNullOrEmpty(tokens.RefreshToken)) return null;
            if (!ChasterClient.NeedsRefresh(tokens.ExpiresAtUtc, _utcNow())) return tokens.AccessToken;

            var fresh = await _client.RefreshAsync(tokens.RefreshToken, ct).ConfigureAwait(false);
            if (fresh.Status == ChasterStatus.LinkExpired) { DropLink(); return null; }
            if (!fresh.Ok) return null;
            StoreTokens(fresh.Value!, tokens.RefreshToken);
            return fresh.Value!.AccessToken;
        }
        finally { _refreshGate.Release(); }
    }

    private void DropLink()
    {
        _tokens.Clear();
        ForgetProfile();
        App.Logger?.Information("[Chaster] the link expired; the tab is kept");
        LinkChanged?.Invoke();
    }

    private TabState LoadTab()
    {
        try
        {
            if (File.Exists(_tabPath))
                return JsonConvert.DeserializeObject<TabState>(File.ReadAllText(_tabPath)) ?? new TabState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Diag.Swallowed(ex, "unreadable tab file starts a clean tab");
        }
        return new TabState();
    }

    // Caller holds _gate. Temp file then move, so a crash mid-write never leaves half a tab.
    private void SaveTab()
    {
        try
        {
            var tmp = _tabPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_tab));
            File.Move(tmp, _tabPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Swallowed(ex, "tab not saved this time; the next booking tries again");
        }
    }

    public void Dispose()
    {
        CancelLink();
        _settleTimer?.Dispose();
        _pushTimer?.Dispose();
        _lockTimer?.Dispose();   // ChasterService.App.cs
        _settleGate.Dispose();
        _refreshGate.Dispose();
    }
}
