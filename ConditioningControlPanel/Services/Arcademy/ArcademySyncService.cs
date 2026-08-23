using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// THE PUNCH-CARD MIRROR, host side (PUNCHCARD.md §5, wire contract
/// <c>proxy/docs/arcademy-cards-api.md</c>). Pulls the account's cards once when the Arcademy
/// opens and pushes after a mint, debounced.
///
/// <para>THE HOST IS THE AUTHORITY AND THIS IS BEST-EFFORT. The Arcademy runs entirely offline
/// inside the WebView2 and the page never talks to any server; every punch is minted into
/// <c>arcademy_meta.json</c> whether or not this class ever gets a packet out. What the mirror
/// buys is restore-on-reinstall (which also suppresses repeat enrollment tutorials for free,
/// because "already enrolled" is derived from <c>enrolledAt</c> rather than stored as a flag) and
/// fuel for a future Honor Roll. Offline forever is a fully working feature, so NOTHING here may
/// cost the player anything: no blocking, no dialog, no toast, no retry storm. A failure is one
/// log line and a flag that makes us try again later.</para>
///
/// <para>MONOTONIC AT BOTH ENDS. The server merges a push into what it holds and answers with the
/// merged state; that reply is folded back in through the same self-healing path a local mint
/// takes (<see cref="ArcademyMetaStore.ApplyServerCards"/> →
/// <see cref="ArcademyPunchCards.ApplyServer"/> → <c>Normalize</c>), so the numbers on a card are
/// always re-derived here from the two fields that are actually earned. A mirror cannot talk a
/// card down, and a mirror that had been handed nonsense cannot spend it.</para>
///
/// <para>Deliberately NOT routed through <c>/v2/user/sync</c>: that endpoint's empty-profile
/// clamp-reset is intended behaviour there and is exactly what would eat a card pushed by a fresh
/// install. Its own endpoints, its own Redis key, no shared write path.</para>
///
/// <para>Dev-door runs (<c>--arcademy</c>) sync like any other: their stamps are ordinary graded
/// play (PUNCHCARD §3) and the switch never reaches a player build.</para>
/// </summary>
internal static class ArcademySyncService
{
    /// <summary>The proxy, spelled the way every other service here spells it (there is no shared
    /// constant in this codebase - V2AuthService, ProfileSyncService, RemoteControlService and
    /// AvailableSubjectsService each carry their own copy).</summary>
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    private const string CardsPath = "/v2/arcademy/cards";

    /// <summary>How long a mint waits before its push goes out. Long enough that an enrollment's
    /// two mints (the stamp on <c>class-ended</c>, then <c>enrollment-done</c> at the end of the
    /// ceremony ~2s later) leave as ONE request, short enough that closing the window straight
    /// after a class still normally flushes before the user walks away. The server's own limiter
    /// is 60/min per caller; this keeps us orders of magnitude under it.</summary>
    private static readonly TimeSpan PushDebounce = TimeSpan.FromSeconds(6);

    /// <summary>Retry delay after a soft refusal (the account lock, a rate limit, a 5xx). One
    /// gentle re-arm, not a ladder: if it fails again the flag simply rides to the next mint or
    /// the next launch, which is what the spec asks for.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(45);

    /// <summary>Bodies are truncated in the log the way <c>V2AuthService.TruncateForLog</c> does -
    /// an error body from a route we call on a schedule must not fatten the log file.</summary>
    private const int MaxLoggedBody = 200;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static ArcademySyncService()
    {
        try
        {
            Http.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }
        catch { /* a header we could not set is not worth failing a static ctor over */ }
    }

    private static readonly object Gate = new();
    private static ArcademyMetaStore? _store;
    private static Action? _onCardsChanged;
    private static Timer? _debounce;

    /// <summary>Bumped by <see cref="Detach"/>. Every continuation captures the value it started
    /// with and drops itself when the two disagree, so a reply that lands after the window closed
    /// cannot write into the NEXT session's store or repaint a page that no longer exists (the
    /// same generation guard <see cref="ArcademyHostService"/> uses for remote batches).</summary>
    private static int _generation;

    /// <summary>Set when a push did not land. Cleared by the next successful one. Not persisted:
    /// the launch pull answers the same question better (it asks the mirror what it is actually
    /// missing) and a flag on disk could only be wrong.</summary>
    private static bool _owed;

    /// <summary>One request at a time. A pull and a push can both be wanted at once (launch), and
    /// two writes racing the account's 5s server lock would just 409 each other.</summary>
    private static readonly SemaphoreSlim InFlight = new(1, 1);

    // ============================ lifecycle ============================

    /// <summary>
    /// Bind to the store the Arcademy just opened and pull the mirror. Called from
    /// <see cref="ArcademyHostService"/>'s launch, right after the store is constructed and before
    /// <c>init</c> goes out - the reply lands whenever it lands, and if that is after the page has
    /// booted, <paramref name="onCardsChanged"/> repaints it with the ordinary whole-blob push.
    /// </summary>
    /// <param name="store">The live store. Held only until <see cref="Detach"/>.</param>
    /// <param name="onCardsChanged">Raised (on a background thread) when a reply actually changed
    /// a card. The caller owns marshalling it to the dispatcher.</param>
    public static void Attach(ArcademyMetaStore store, Action onCardsChanged)
    {
        lock (Gate)
        {
            _store = store;
            _onCardsChanged = onCardsChanged;
        }
        int generation = Volatile.Read(ref _generation);
        Run(() => PullAsync(generation), "pull");
    }

    /// <summary>
    /// Unbind at teardown. A push that was still sitting in the debounce is sent NOW rather than
    /// dropped - the payload is taken before the store reference goes, so the request can outlive
    /// the window it came from without touching anything that is being disposed.
    /// </summary>
    public static void Detach()
    {
        ArcademyMetaStore? store;
        bool owed;
        lock (Gate)
        {
            store = _store;
            owed = _owed || _debounce != null;
            try { _debounce?.Dispose(); } catch { }
            _debounce = null;
            _store = null;
            _onCardsChanged = null;
        }
        Interlocked.Increment(ref _generation);

        if (store == null || !owed) return;
        JObject payload;
        try { payload = store.ExportCards(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademySync.Detach export: {E}", ex.Message); return; }
        if (payload.Count == 0) return;

        // Detached: no store to apply the reply into and no page to repaint, so this is a
        // send-and-forget. Whatever the server merges will come back down at the next launch pull.
        Run(() => PostCardsAsync(payload, applyReply: false, generation: -1), "flush");
    }

    /// <summary>
    /// A punch was minted - schedule the push. Coalescing rather than sending: a class end can mint
    /// twice within a couple of seconds (the daily stamp, then enrollment) and a ceremony is not a
    /// reason to spend two requests.
    /// </summary>
    public static void NotifyMutation()
    {
        lock (Gate)
        {
            if (_store == null) return;
            _owed = true;
            Arm(PushDebounce);
        }
    }

    /// <summary>Arm (or re-arm) the debounce. Called under <see cref="Gate"/>.</summary>
    private static void Arm(TimeSpan delay)
    {
        try
        {
            if (_debounce == null)
            {
                _debounce = new Timer(_ => OnDebounceElapsed(), null, delay, Timeout.InfiniteTimeSpan);
                return;
            }
            _debounce.Change(delay, Timeout.InfiniteTimeSpan);
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademySync.Arm: {E}", ex.Message); }
    }

    private static void OnDebounceElapsed()
    {
        int generation = Volatile.Read(ref _generation);
        lock (Gate)
        {
            try { _debounce?.Dispose(); } catch { }
            _debounce = null;
            if (_store == null) return;
        }
        Run(() => PushAsync(generation), "push");
    }

    // ============================ the two directions ============================

    /// <summary>
    /// LAUNCH PULL. Read the mirror, fold it in monotonically, and then - using the same reply -
    /// answer "did a push fail while we were offline?" without needing a flag that survived the
    /// crash: anything this machine holds that the mirror does not is pushed straight back.
    /// </summary>
    private static async Task PullAsync(int generation)
    {
        if (!Identity(out var unifiedId, out var token)) return;
        if (!await InFlight.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            var url = $"{ProxyBaseUrl}{CardsPath}?unified_id={Uri.EscapeDataString(unifiedId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Auth-Token", token);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Information("[ArcademySync] pull failed: {Status} {Body}",
                    (int)response.StatusCode, Truncate(body));
                return;
            }

            var reply = Parse(body);
            var serverCards = reply?["cards"] as JObject;
            if (Retired(generation)) return;

            var store = Store(generation);
            if (store == null) return;

            var changed = store.ApplyServerCards(serverCards);
            App.Logger?.Information(
                "[ArcademySync] pulled {N} card(s) from the mirror (rev {Rev}); {Changed} changed locally",
                serverCards?.Count ?? 0, (int?)reply?["rev"] ?? 0, changed.Count);
            if (changed.Count > 0) RaiseChanged(generation);

            // The offline retry: the mirror itself tells us what it is missing.
            if (ArcademyPunchCards.HasUnmirrored(store.Get(ArcademyMetaStore.PunchCardsKey) as JObject, serverCards))
            {
                lock (Gate) { _owed = true; }
                App.Logger?.Information("[ArcademySync] the mirror is behind this machine - pushing");
                var payload = store.ExportCards();
                if (payload.Count > 0)
                {
                    await PostCardsAsync(payload, applyReply: true, generation).ConfigureAwait(false);
                }
            }
            else
            {
                lock (Gate) { _owed = false; }
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademySync] pull failed: {E}", ex.Message);
        }
        finally { try { InFlight.Release(); } catch { } }
    }

    /// <summary>The debounced push. The payload is taken at send time, so every mint that landed
    /// during the debounce rides along in one request.</summary>
    private static async Task PushAsync(int generation)
    {
        var store = Store(generation);
        if (store == null) return;
        JObject payload;
        try { payload = store.ExportCards(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademySync export: {E}", ex.Message); return; }
        if (payload.Count == 0) { lock (Gate) { _owed = false; } return; }

        if (!await InFlight.WaitAsync(0).ConfigureAwait(false))
        {
            // A pull is mid-flight (launch). Come back rather than fight it for the account lock.
            lock (Gate) { if (_store != null) Arm(RetryDelay); }
            return;
        }
        try { await PostCardsAsync(payload, applyReply: true, generation).ConfigureAwait(false); }
        finally { try { InFlight.Release(); } catch { } }
    }

    /// <summary>
    /// The write itself. PUT is the spec's verb (the route also answers POST, for browser origins
    /// that cannot preflight a PUT - the host has no such problem).
    ///
    /// <para>A refusal is never fatal and never surfaced: <c>_owed</c> stays set and the next mint,
    /// the retry timer or the next launch pull carries it. The one thing worth reading in the log
    /// is <c>clamps</c> - the server naming a row it would not take, which means a clock problem or
    /// an edited file on this machine, not a failure of this request (it still answers 200).</para>
    /// </summary>
    private static async Task PostCardsAsync(JObject cards, bool applyReply, int generation)
    {
        if (!Identity(out var unifiedId, out var token)) return;
        try
        {
            var body = JsonConvert.SerializeObject(new { unified_id = unifiedId, cards });
            using var request = new HttpRequestMessage(HttpMethod.Put, $"{ProxyBaseUrl}{CardsPath}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Auth-Token", token);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                lock (Gate) { _owed = true; }
                bool soft = response.StatusCode == HttpStatusCode.Conflict
                            || (int)response.StatusCode == 429
                            || (int)response.StatusCode >= 500;
                App.Logger?.Information("[ArcademySync] push refused: {Status} {Body}{Retry}",
                    (int)response.StatusCode, Truncate(text), soft ? " - retrying later" : " - queued for next launch");
                if (soft) lock (Gate) { if (_store != null) Arm(RetryDelay); }
                return;
            }

            var reply = Parse(text);
            if (reply?["clamps"] is JArray clamps && clamps.Count > 0)
            {
                // A clamp is the mirror refusing an impossible row, not a failed request: a clock
                // that ran ahead, or a hand-edited meta file. Worth a line, worth nothing else.
                App.Logger?.Information("[ArcademySync] the mirror clamped {N} row(s): {Clamps}",
                    clamps.Count, string.Join(", ", clamps.Select(c => (string?)c ?? "?")));
            }

            lock (Gate) { _owed = false; }
            App.Logger?.Information("[ArcademySync] pushed {N} card(s) (rev {Rev})",
                cards.Count, (int?)reply?["rev"] ?? 0);

            if (!applyReply || Retired(generation)) return;
            var store = Store(generation);
            if (store == null) return;
            var changed = store.ApplyServerCards(reply?["cards"] as JObject);
            if (changed.Count > 0) RaiseChanged(generation);
        }
        catch (Exception ex)
        {
            lock (Gate) { _owed = true; }
            App.Logger?.Information("[ArcademySync] push failed: {E}", ex.Message);
        }
    }

    // ============================ small guards ============================

    /// <summary>
    /// The token door: <c>X-Auth-Token</c> plus this account's own <c>unified_id</c> (the door the
    /// desktop holds; the Bearer door is the web's). No identity, no traffic - and silently so: an
    /// account that has never logged in is not a problem to report, it is a player whose cards
    /// simply live on their own machine.
    /// </summary>
    private static bool Identity(out string unifiedId, out string token)
    {
        unifiedId = string.Empty;
        token = string.Empty;
        try
        {
            if (App.Settings?.Current?.OfflineMode == true) return false;
            var id = App.Settings?.Current?.UnifiedId;
            var auth = App.Settings?.Current?.AuthToken;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(auth)) return false;
            unifiedId = id;
            token = auth;
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademySync.Identity: {E}", ex.Message);
            return false;
        }
    }

    /// <summary>The store, or null if this generation has been retired (the window closed while we
    /// were on the network).</summary>
    private static ArcademyMetaStore? Store(int generation)
    {
        if (Retired(generation)) return null;
        lock (Gate) return _store;
    }

    private static bool Retired(int generation) =>
        generation >= 0 && Volatile.Read(ref _generation) != generation;

    private static void RaiseChanged(int generation)
    {
        Action? cb;
        lock (Gate) cb = _onCardsChanged;
        if (cb == null || Retired(generation)) return;
        try { cb(); }
        catch (Exception ex) { App.Logger?.Debug("ArcademySync.RaiseChanged: {E}", ex.Message); }
    }

    /// <summary>Fire-and-forget onto the thread pool with a catch-all: every entry point here is
    /// called from a UI-thread handler (a mint, a launch, a teardown) and none of them may pay for
    /// the network or die of it (CLAUDE.md async rules 6-8).</summary>
    private static void Run(Func<Task> work, string what)
    {
        try
        {
            _ = Task.Run(async () =>
            {
                try { await work().ConfigureAwait(false); }
                catch (Exception ex) { App.Logger?.Information("[ArcademySync] {What} failed: {E}", what, ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademySync.Run({What}): {E}", what, ex.Message); }
    }

    private static JObject? Parse(string body)
    {
        try { return string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body); }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademySync] unreadable reply: {E}", ex.Message);
            return null;
        }
    }

    private static string Truncate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= MaxLoggedBody ? s : s[..MaxLoggedBody] + "...";
    }
}
