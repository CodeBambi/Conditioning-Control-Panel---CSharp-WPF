using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// CAMPUS PRESENCE, host side ("the Student Body", <c>planning/arcademy/PRESENCE.md</c> §3; wire
/// contract <c>proxy/docs/arcademy-presence-api.md</c>). Two halves that share nothing but an
/// HttpClient, and the split is the whole privacy story:
///
/// <list type="number">
/// <item><description>THE EMITTER pushes four state transitions - <c>campus_enter</c>,
/// <c>room_enter</c>, <c>class_end</c>, <c>campus_leave</c> - and runs ONLY while
/// <see cref="Models.AppSettings.ArcademyPresenceShare"/> is something other than <c>off</c>,
/// with exactly one exception: the REVOCATION (see <see cref="OnSettingChanged"/>), which is the
/// one payload that has to leave BECAUSE the player turned sharing off. A
/// transition is a room key and a letter grade; there is no coordinate in this file, no field for
/// one, and none is ever computed here. Where a ghost walks is <c>shell/ghosts.js</c>'s invention
/// from the event list.</description></item>
/// <item><description>THE SNAPSHOT PUSHER pulls the public feed and hands it to the page. It is
/// gated on the window being open and OfflineMode being off, and on NOTHING ELSE: watching is not
/// consenting, so a player at <c>off</c> still gets a populated campus. The endpoint is
/// unauthenticated for exactly that reason.</description></item>
/// </list>
///
/// <para>BEST-EFFORT, ArcademySyncService's house rules verbatim: no dialog, no toast, no blocking,
/// no retry storm. One log line per failure, bodies truncated, and a failure costs the player some
/// company - never a class, never a grade, never a punch. The Arcademy is fully playable with this
/// class doing nothing at all, which is what lets the setting default to off and stay there.</para>
///
/// <para>THE PLAYER'S OWN GHOST IS NEVER DRAWN. Each POST answers with this account's opaque id
/// (<c>self</c>), which rides every frame we push so the page can subtract itself from the crowd.
/// Until a POST lands - which at <c>off</c> is forever - it is null, and the page simply draws
/// everybody.</para>
/// </summary>
internal static class ArcademyPresenceService
{
    /// <summary>The proxy, spelled the way every other service here spells it (there is no shared
    /// constant in this codebase - ArcademySyncService, V2AuthService, ProfileSyncService and
    /// RemoteControlService each carry their own copy).</summary>
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    private const string EventPath = "/v2/arcademy/presence";
    private const string SnapshotPath = "/v2/arcademy/presence/snapshot";

    /// <summary>Snapshot cadence. The CDN holds the feed for 30s with a 25s Redis memo underneath,
    /// so polling faster than this buys a cached body and nothing else. Jittered so a thousand
    /// clients that all opened the campus at 9pm do not arrive as one wave.</summary>
    private static readonly TimeSpan SnapshotPeriod = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SnapshotJitter = TimeSpan.FromSeconds(10);

    /// <summary>Bodies are truncated in the log the way <c>V2AuthService.TruncateForLog</c> does.</summary>
    private const int MaxLoggedBody = 200;

    /// <summary>The snapshot is public and modest (under 30 KB served at the 200-student cap), but
    /// a body this class never asked for must not be read into memory unbounded.</summary>
    private const int MaxSnapshotChars = 4_000_000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static ArcademyPresenceService()
    {
        try
        {
            Http.DefaultRequestHeaders.Add("X-Client-Version", UpdateService.AppVersion);
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}");
        }
        catch { /* a header we could not set is not worth failing a static ctor over */ }
    }

    /// <summary>
    /// THE ROOM VOCABULARY, and it is deliberately its OWN table (the server says so in the same
    /// words): the wire is kebab-case, the page's game keys are snake_case, and the punch-card
    /// mirror next door uses a third list. Two features, two vocabularies, no shared table to
    /// drift. A key that is not in here is not sent at all - a `room_enter` with an unknown room
    /// is a 400, and losing one event is cheaper than teaching the server a new room by accident.
    ///
    /// <para><c>misdirection</c> stays listed although the room is dark in the client
    /// (<c>RETIRED_GAMES</c>): a 24h window can still be replaying a ghost who was in it, and a
    /// retake of a retired class through the dev door is still a real transition.</para>
    /// </summary>
    private static readonly HashSet<string> Rooms = new(StringComparer.Ordinal)
    {
        "anomaly", "composure", "daily-trigger", "deja-vu", "echo",
        "impulse-control", "instant-recall", "lost-and-found", "misdirection",
        "sort", "the-deep-end",
    };

    /// <summary>The four letters the wire knows. A zen finish reports <c>pass</c>, which is not a
    /// grade and rides as null - "was here, no grade" is a fact the renderer draws.</summary>
    private static readonly HashSet<string> Grades = new(StringComparer.Ordinal) { "S", "A", "B", "C" };

    /// <summary>The absence of a rung. Not consent, and on the wire it is legal on exactly one
    /// event kind - a <c>campus_leave</c>, the revocation. Spelled once, here.</summary>
    private const string PresenceOff = "off";

    /// <summary>Rung ordering, the server's <c>SHARE_RANK</c> verbatim. Used for ONE decision: did
    /// this change reduce what the account shows, which is the change that owes a POST.</summary>
    private static int Rank(string? share) => share switch
    {
        "anon" => 1,
        "username" => 2,
        "discord" => 3,
        _ => 0,       // "off", and anything unreadable
    };

    private static readonly object Gate = new();

    /// <summary>Raised on a background thread with (self, snapshot) when a snapshot arrives whole.
    /// The caller owns marshalling it to the dispatcher and posting it - this class never touches
    /// the WebView2 and never touches the dispatcher.</summary>
    private static Action<string?, JObject?>? _onSnapshot;

    /// <summary>The poller. A <see cref="Timer"/> rather than a DispatcherTimer on purpose: it must
    /// not ride the UI thread, and it must be disposable from <see cref="Detach"/> without caring
    /// which thread is doing the disposing.</summary>
    private static Timer? _poll;

    /// <summary>Bumped by <see cref="Detach"/>. Every continuation captures the value it started
    /// with and drops itself when the two disagree, so a snapshot that lands after the window
    /// closed can never be pushed into the NEXT window's page (the same generation guard
    /// <see cref="ArcademySyncService"/> and the host's remote batches use).</summary>
    private static int _generation;

    /// <summary>True between <see cref="Attach"/> and <see cref="Detach"/>. The snapshot half reads
    /// it as "is there a campus to draw on".</summary>
    private static bool _open;

    /// <summary>This account's opaque id, as last answered by a POST. Null until one lands - and at
    /// the <c>off</c> rung that is forever, which is correct: a player with no ghost has no id to
    /// subtract.</summary>
    private static string? _self;

    /// <summary>Did this session put ANYTHING on the wire? A lone <c>campus_leave</c> from a
    /// session that never announced itself would draw a student who only ever left.</summary>
    private static bool _emitted;

    /// <summary>The rung the last change was measured against. Seeded on the first hook so the
    /// first change we see is compared to the truth, not to <c>off</c>.</summary>
    private static string _lastShare = "off";

    /// <summary>The settings instance we are watching. Hooked at the first <see cref="Attach"/> and
    /// deliberately NEVER unhooked: the consent obligation below ("post one campus_leave when the
    /// rung drops") is at its most useful when the window is already shut, and a static handler on
    /// the settings singleton costs nothing. Re-pointed if <c>App.Settings.Current</c> is
    /// replaced (a cloud restore), the way the host's own watch is.</summary>
    private static Models.AppSettings? _hookedSettings;

    // ============================ lifecycle ============================

    /// <summary>
    /// The campus opened. Emits <c>campus_enter</c> (if the player consents), pulls the first
    /// snapshot immediately and arms the poller. Called from <see cref="ArcademyHostService"/>'s
    /// launch beside <see cref="ArcademySyncService.Attach"/> - nothing here is awaited and the
    /// launch does not depend on any of it.
    /// </summary>
    /// <param name="onSnapshot">Raised (on a background thread) with the account's opaque id and
    /// the server snapshot VERBATIM. The caller marshals and posts it.</param>
    public static void Attach(Action<string?, JObject?> onSnapshot)
    {
        lock (Gate)
        {
            _onSnapshot = onSnapshot;
            _open = true;
            _emitted = false;
        }
        HookSettings();
        int generation = Volatile.Read(ref _generation);
        Emit("campus_enter", null, null, generation);
        Run(() => PullSnapshotAsync(generation), "snapshot");
        ArmPoll(Delay());
    }

    /// <summary>
    /// The campus closed. Stops the poller (a timer must never outlive the window that armed it),
    /// retires the generation so anything still on the network drops itself, and sends one
    /// best-effort <c>campus_leave</c> - only if this session ever announced itself. The server
    /// auto-expires a silent account anyway, so a leave that never lands costs a ghost a fade
    /// instead of a walk-out.
    /// </summary>
    public static void Detach()
    {
        bool announce;
        lock (Gate)
        {
            _open = false;
            _onSnapshot = null;
            announce = _emitted;
            _emitted = false;
            try { _poll?.Dispose(); } catch { }
            _poll = null;
        }
        Interlocked.Increment(ref _generation);
        // Detached: no page to push into and no generation to belong to, so this is send-and-forget
        // (generation -1, the same escape ArcademySyncService.Detach's flush takes).
        if (announce) Emit("campus_leave", null, null, -1);
    }

    // ============================ the four events ============================

    /// <summary>A class started: the player walked into a room. <paramref name="gameKey"/> is the
    /// page's own snake_case key and is translated here, once.</summary>
    public static void NoteRoomEnter(string? gameKey)
    {
        var room = RoomFor(gameKey);
        if (room == null) return;
        Emit("room_enter", room, null, Volatile.Read(ref _generation));
    }

    /// <summary>A class ended. <paramref name="grade"/> is the HOST's clamped letter (the same
    /// value that decided the XP multiplier and the S double), so a junk field from the page cannot
    /// reach the wire; anything that is not S/A/B/C - a zen <c>pass</c> included - rides as null,
    /// which the renderer draws as "was here, finished, no letter".</summary>
    public static void NoteClassEnd(string? gameKey, string? grade)
    {
        var room = RoomFor(gameKey);
        if (room == null) return;
        var g = (grade ?? "").Trim().ToUpperInvariant();
        Emit("class_end", room, Grades.Contains(g) ? g : null, Volatile.Read(ref _generation));
    }

    /// <summary>The page's game key -> the wire's room key, or null for anything not on the
    /// server's allowlist.</summary>
    private static string? RoomFor(string? gameKey)
    {
        var k = (gameKey ?? "").Trim().ToLowerInvariant().Replace('_', '-');
        return Rooms.Contains(k) ? k : null;
    }

    // ============================ consent ============================

    /// <summary>
    /// Watch the share rung. Hooked once, at the first campus open, and never unhooked - see
    /// <see cref="_hookedSettings"/>. Re-points at a replaced settings instance.
    /// </summary>
    private static void HookSettings()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null || ReferenceEquals(s, _hookedSettings)) return;
            if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingChanged;
            s.PropertyChanged += OnSettingChanged;
            _hookedSettings = s;
            _lastShare = Share();
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyPresence.HookSettings: {E}", ex.Message); }
    }

    /// <summary>
    /// THE CLIENT OBLIGATION (api doc, "The one gap, stated plainly"). The server's consent row is
    /// only ever moved by a POST, and the snapshot filters on the CURRENT row - so a player who
    /// lowers their rung and then never plays again keeps their prior 24 hours visible at the OLD
    /// rung. One <c>campus_leave</c> carrying the new rung closes that, retroactively, for every
    /// row already on disk. It is sent even outside a campus session, which is exactly the case it
    /// exists for.
    ///
    /// <para>ONLY A DOWNGRADE OWES A POST. Moving UP shows more, and the account has not shown it
    /// yet: the next real event carries the new rung and that is soon enough. Sending on the way up
    /// would spend one of the twelve hourly writes to say nothing.</para>
    ///
    /// <para>AND <c>off</c> IS A POST OF ITS OWN. It is the case the obligation exists for: a
    /// player who turns sharing off is asking for their name to come off the map NOW, not in
    /// twenty-four hours' time, and the current rung only ever moves on a POST. So the move to
    /// <c>off</c> sends one <c>campus_leave</c> carrying <c>share: "off"</c> - the server's
    /// revocation shape, accepted on that event kind and on no other (api doc, "Revoking - the
    /// shape"). It upserts the consent row to <c>off</c>, deletes the avatar reverse index and
    /// stores a row with no name and no picture, and the next snapshot reduces the account's whole
    /// prior window to a head count. What is NOT sent is the nearest consenting rung: that would be
    /// this client asserting a consent the player just withdrew, which is the one thing it must
    /// never do.</para>
    ///
    /// <para>Best-effort like everything else here: if it does not land - offline, no identity, a
    /// 429 - the account's prior window ages out on its own inside 24 hours, and the next real
    /// event (which cannot happen at <c>off</c>) is not needed to close it.</para>
    /// </summary>
    private static void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Models.AppSettings.ArcademyPresenceShare)) return;
        try
        {
            var now = Share();
            var was = _lastShare;
            _lastShare = now;
            if (Rank(now) >= Rank(was)) return;      // unchanged, or the player showing MORE

            if (Rank(now) == 0)
            {
                App.Logger?.Information(
                    "[ArcademyPresence] presence turned OFF - posting the revocation, which takes the"
                    + " account's prior 24h off the map at once");
                // The one payload that leaves at the off rung, and it says so explicitly rather
                // than reading the setting: Emit's own gate would (rightly) drop anything else.
                Emit("campus_leave", null, null, -1, PresenceOff);
                return;
            }

            App.Logger?.Information("[ArcademyPresence] rung dropped {Was} -> {Now} - posting a leave to move consent",
                was, now);
            // Deliberately generation -1: this can fire long after the window closed, and it has
            // nothing to push back into the page.
            Emit("campus_leave", null, null, -1);
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyPresence.OnSettingChanged: {E}", ex.Message); }
    }

    /// <summary>The rung as stored, lowercased, with anything unreadable reading as <c>off</c>.</summary>
    private static string Share()
    {
        try { return (App.Settings?.Current?.ArcademyPresenceShare ?? "off").Trim().ToLowerInvariant(); }
        catch { return "off"; }
    }

    // ============================ the emitter ============================

    /// <summary>
    /// One transition, fire-and-forget. Gated here rather than at each call site so there is
    /// exactly one place that can decide to put something on the wire: no consent, no identity,
    /// offline - nothing leaves, silently.
    /// </summary>
    /// <param name="shareOverride">THE ONE WAY PAST THE CONSENT GATE, and it has exactly one
    /// caller: <see cref="OnSettingChanged"/> sending <see cref="PresenceOff"/> to WITHDRAW. The
    /// rung named here is asserted on the wire, so nothing but <c>off</c> may ever be passed - an
    /// override that RAISED the rung would be this client consenting on the player's behalf, and
    /// the server would believe it. Null everywhere else, which is the ordinary gated path.</param>
    private static void Emit(string kind, string? room, string? grade, int generation,
        string? shareOverride = null)
    {
        var revoking = shareOverride == PresenceOff;
        var share = revoking ? PresenceOff : Share();
        if (!revoking && Rank(share) == 0) return;          // off: the whole feature is absent
        if (!Identity(out var unifiedId, out var token)) return;
        // A revocation is not an announcement: it must not arm Detach's own campus_leave, which
        // exists to walk a ghost out that this session walked in.
        if (!revoking) lock (Gate) { _emitted = true; }
        Run(() => PostEventAsync(kind, room, grade, share, unifiedId, token, generation), kind);
    }

    private static async Task PostEventAsync(string kind, string? room, string? grade, string share,
        string unifiedId, string token, int generation)
    {
        try
        {
            // THE BODY IS THE WHOLE CONTRACT and it is five fields. There is no name here, no
            // picture, no timestamp and no position: `display` is read off the account server-side,
            // `avatar` is resolved there, and `ts` is the server clock. A field this client added
            // would be dropped, but the point is that it is never built.
            var body = JsonConvert.SerializeObject(new
            {
                @event = kind,
                room,
                grade,
                share,
                unified_id = unifiedId,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{EventPath}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("X-Auth-Token", token);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // No retry ladder, and deliberately not even the one gentle re-arm the card mirror
                // takes: a transition is a MOMENT. Re-sending it a minute later would write a
                // ghost walking into a room it has already left, and the renderer would believe it.
                App.Logger?.Information("[ArcademyPresence] {Kind} refused: {Status} {Body}",
                    kind, (int)response.StatusCode, Truncate(text));
                return;
            }

            var self = (string?)Parse(text)?["self"];
            if (string.IsNullOrWhiteSpace(self)) return;
            var known = Volatile.Read(ref _self);
            Volatile.Write(ref _self, self);
            App.Logger?.Debug("ArcademyPresence: {Kind} logged", kind);
            // The FIRST id is worth a frame of its own: the page has probably already drawn a
            // snapshot that this account is standing in, and `self` is what removes it.
            if (known != self && !Retired(generation)) PushSelfOnly(generation);
        }
        catch (Exception ex)
        {
            App.Logger?.Information("[ArcademyPresence] {Kind} failed: {E}", kind, ex.Message);
        }
    }

    // ============================ the snapshot pusher ============================

    /// <summary>The next poll delay: the period plus or minus the jitter band.</summary>
    private static TimeSpan Delay()
    {
        var span = SnapshotJitter.TotalMilliseconds;
        var offset = (Random.Shared.NextDouble() * 2 - 1) * span;
        return TimeSpan.FromMilliseconds(Math.Max(1000, SnapshotPeriod.TotalMilliseconds + offset));
    }

    /// <summary>Arm (or re-arm) the one-shot poll timer. Never armed while closed, and every tick
    /// re-arms itself from inside the tick, so there is no repeating timer to leak.</summary>
    private static void ArmPoll(TimeSpan delay)
    {
        lock (Gate)
        {
            if (!_open) return;
            try
            {
                if (_poll == null)
                {
                    _poll = new Timer(_ => OnPollElapsed(), null, delay, Timeout.InfiniteTimeSpan);
                    return;
                }
                _poll.Change(delay, Timeout.InfiniteTimeSpan);
            }
            catch (Exception ex) { App.Logger?.Debug("ArcademyPresence.ArmPoll: {E}", ex.Message); }
        }
    }

    private static void OnPollElapsed()
    {
        int generation = Volatile.Read(ref _generation);
        lock (Gate) { if (!_open) return; }
        Run(async () =>
        {
            await PullSnapshotAsync(generation).ConfigureAwait(false);
            // Re-armed whatever happened: a failed poll keeps the cadence, because the campus is
            // still open and the next one may well land.
            if (!Retired(generation)) ArmPoll(Delay());
        }, "snapshot");
    }

    /// <summary>
    /// GET the public feed and hand it to the page WHOLE. Unauthenticated by design (a head count
    /// and things people explicitly turned on), so this half runs at every rung including
    /// <c>off</c> - watching is not consenting.
    ///
    /// <para>NOTHING IS RESHAPED. The snapshot's own <c>now</c> is what makes the page's ages the
    /// SERVER's ages, so a skewed client clock cannot invent a live ghost; touching a timestamp
    /// here would be the one way to break that. We validate the envelope and pass the object
    /// through untouched.</para>
    /// </summary>
    private static async Task PullSnapshotAsync(int generation)
    {
        if (!Watchable()) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ProxyBaseUrl}{SnapshotPath}");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                App.Logger?.Debug("ArcademyPresence: snapshot {Status} {Body}",
                    (int)response.StatusCode, Truncate(text));
                return;
            }
            if (text.Length > MaxSnapshotChars)
            {
                App.Logger?.Information("[ArcademyPresence] snapshot is {N} chars - ignored", text.Length);
                return;
            }

            // A PARTIAL SNAPSHOT IS WORSE THAN NONE. The page replaces its whole plan on every
            // frame it accepts, so half a feed would empty the campus and then refill it on the
            // next poll - a flicker that reads as a bug. Refuse anything that is not the shape we
            // know: v exactly 1 (a newer wire is not ours to guess at) and students an array.
            var snapshot = Parse(text);
            if (snapshot == null) return;
            if ((int?)snapshot["v"] != 1 || snapshot["students"] is not JArray students)
            {
                App.Logger?.Information("[ArcademyPresence] snapshot shape refused (v={V})",
                    (string?)snapshot["v"] ?? "?");
                return;
            }
            if (Retired(generation)) return;

            Action<string?, JObject?>? cb;
            lock (Gate) cb = _onSnapshot;
            if (cb == null) return;
            App.Logger?.Debug("ArcademyPresence: snapshot with {N} student(s)", students.Count);
            try { cb(Volatile.Read(ref _self), snapshot); }
            catch (Exception ex) { App.Logger?.Debug("ArcademyPresence.push: {E}", ex.Message); }
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyPresence: snapshot failed: {E}", ex.Message);
        }
    }

    /// <summary>Push a frame that carries the id and NO snapshot. `ghosts.js` tests `if (m.snapshot)`
    /// before it replaces anything, so a null there leaves the crowd it is already drawing exactly
    /// where it is - which is the whole point: this frame says "you are that one", not "here is a
    /// new campus". An EMPTY object would not do: it normalises to a snapshot with no students and
    /// would blank the map until the next poll.</summary>
    private static void PushSelfOnly(int generation)
    {
        Action<string?, JObject?>? cb;
        lock (Gate) cb = _onSnapshot;
        if (cb == null || Retired(generation)) return;
        try { cb(Volatile.Read(ref _self), null); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyPresence.PushSelfOnly: {E}", ex.Message); }
    }

    // ============================ small guards ============================

    /// <summary>The snapshot half's only two gates: a campus to draw on, and an app that is allowed
    /// on the network at all. Note what is NOT here - the share rung and the identity.</summary>
    private static bool Watchable()
    {
        try
        {
            lock (Gate) { if (!_open) return false; }
            return App.Settings?.Current?.OfflineMode != true;
        }
        catch { return false; }
    }

    /// <summary>
    /// The token door: <c>X-Auth-Token</c> plus this account's own <c>unified_id</c> in the body
    /// (the door the desktop holds; the Bearer door is the web's). No identity, no traffic - and
    /// silently so: an account that has never logged in is not a problem to report.
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
            App.Logger?.Debug("ArcademyPresence.Identity: {E}", ex.Message);
            return false;
        }
    }

    private static bool Retired(int generation) =>
        generation >= 0 && Volatile.Read(ref _generation) != generation;

    /// <summary>Fire-and-forget onto the thread pool with a catch-all: every entry point here is
    /// called from a UI-thread handler (a launch, a class end, a teardown, a settings write) and
    /// none of them may pay for the network or die of it (CLAUDE.md async rules 6-8).</summary>
    private static void Run(Func<Task> work, string what)
    {
        try
        {
            _ = Task.Run(async () =>
            {
                try { await work().ConfigureAwait(false); }
                catch (Exception ex) { App.Logger?.Information("[ArcademyPresence] {What} failed: {E}", what, ex.Message); }
            });
        }
        catch (Exception ex) { App.Logger?.Debug("ArcademyPresence.Run({What}): {E}", what, ex.Message); }
    }

    private static JObject? Parse(string body)
    {
        try { return string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body); }
        catch (Exception ex)
        {
            App.Logger?.Debug("ArcademyPresence: unreadable reply: {E}", ex.Message);
            return null;
        }
    }

    private static string Truncate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= MaxLoggedBody ? s : s[..MaxLoggedBody] + "...";
    }
}
