using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// The Arcademy's persisted meta blob (<c>arcademy_meta.json</c> in <see cref="App.UserDataPath"/>):
/// attendance streak, per-class grade history, per-game progression (grade tiers, baselines) and
/// the shell's seen-once flags.
///
/// Same ownership model as <see cref="Chaos.DtrhMetaBridge"/> — C# owns the file, the page holds a
/// rev-numbered read snapshot and sends COMMANDS (<c>meta-command {op,key,value}</c>) which are
/// shape-validated here, applied, debounce-saved and answered. Unlike the DtRH bridge this state is
/// a free-form JSON object rather than a typed model: V1 per-game meta is authored page-side
/// (SYNTHESIS-NOTES #15) and pinning a C# class to it would make every game a C# change.
///
/// Validation is integrity, not anti-cheat (offline, the user's own save): unknown ops are logged
/// and ignored, keys and values are size-capped, and the two host-owned regions
/// (<see cref="AttendanceKey"/> / <see cref="StreakKey"/>) are written only by
/// <see cref="RecordAttendance"/> so a stale page cannot mint a streak.
/// </summary>
internal sealed class ArcademyMetaStore
{
    /// <summary>Host-owned: the local date (yyyy-MM-dd) attendance was last credited for.</summary>
    public const string AttendanceKey = "lastAttendanceLocalDate";

    /// <summary>Host-owned: consecutive local days with at least one completed class.</summary>
    public const string StreakKey = "streak";

    /// <summary>Host-owned: lifetime count of local days where all 4 classes were completed
    /// (<see cref="ClassesPerDay"/> - the timetable deals four since 2026-08-23).</summary>
    public const string PerfectKey = "perfectAttendance";

    /// <summary>Host-owned: game keys completed on <see cref="AttendanceKey"/>'s day.</summary>
    public const string TodayClassesKey = "todayClasses";

    /// <summary>Host-owned: the XP ledger, <c>{ "&lt;utcDay&gt;": ["&lt;gameKey&gt;", …] }</c>. A class pays
    /// its XP once per UTC day; every later run of the same class that day is a free replay.</summary>
    public const string XpPaidKey = "xpPaidDays";

    /// <summary>Host-owned: the punch cards, <c>{ "&lt;gameKey&gt;": {punches, dates, enrolledAt,
    /// sDates, house, complete, unlockedAt} }</c> — ten holes per class, the tenth a permanent unlock
    /// (PUNCHCARD.md §2.1). Minted here for the same reason the streak is: a card is worth a
    /// standing Begin on a room the seed did not deal, so a stale page must not be able to write
    /// one. The math itself lives in <see cref="ArcademyPunchCards"/>.</summary>
    public const string PunchCardsKey = "punchCards";

    /// <summary>Host-owned: the two-currency wallet — balances, the one-a-day token ledger, the
    /// replay counters the decay ladder reads, what the player has bought and the two lever rungs.
    /// Minted here for the plainest possible reason: it is MONEY, and a page that could write it
    /// could print it. Shape and every rule about it live in <see cref="ArcademyEconomy"/>.</summary>
    public const string WalletKey = "wallet";

    /// <summary>Host-owned: this install's stable id, minted once and never changed. The server
    /// keys its once-per-device wallet import on it (wallet contract §2).</summary>
    public const string WalletDeviceKey = "walletDeviceId";

    /// <summary>Host-owned: when this machine carried its wallet up to the account, absent until it
    /// has. Present means the local <see cref="WalletKey"/> is a CACHE of the account's copy rather
    /// than the record, and it is what decides whether an unpaid mint may be parked at all.</summary>
    public const string WalletImportedKey = "walletImported";

    /// <summary>Host-owned: class-ended frames the server never took, oldest first. Money the
    /// player has earned and nobody has banked yet, so it is the one region here that is worth more
    /// than the file it sits in.</summary>
    public const string PendingMintsKey = "pendingMints";

    /// <summary>How many unpaid frames are kept. See <see cref="QueueMint"/> for why the cap drops
    /// the oldest rather than refusing the newest.</summary>
    private const int PendingMintCap = 60;

    /// <summary>The shape the wallet routes demand of <c>deviceId</c>, spelled here so an id this
    /// machine hands over can never be one the server will only ever answer with a 400.</summary>
    private static readonly Regex DeviceIdShape = new("^[A-Za-z0-9_-]{8,64}$", RegexOptions.Compiled);

    /// <summary>Classes in a day — the timetable's fixed size (GROUND-RULES §4).</summary>
    private const int ClassesPerDay = 4;

    /// <summary>UTC days of XP ledger kept. Older days can no longer be replayed into (the page
    /// only ever ends a class on today's seed), so keeping them would just grow the blob.</summary>
    private const int XpPaidDayHistory = 14;

    private const int MaxKeyLength = 128;
    private const int MaxBlobChars = 512 * 1024;   // a meta file this big is a bug, not a save

    /// <summary>Serialized ceiling for ONE key's value. The page authors per-game meta freely
    /// (SYNTHESIS-NOTES #15), so nothing page-side bounds what it stores; without a write-side cap a
    /// single runaway game could inflate the blob past <see cref="MaxBlobChars"/> and the NEXT launch
    /// would read it as corrupt. Refusing one write is recoverable; losing the file is not.</summary>
    private const int MaxValueChars = 32 * 1024;

    /// <summary>Top-level keys the page may own. Four host-owned regions plus the page's own
    /// <c>days</c>/<c>games</c>/flags - 64 is roomy and still a bound.</summary>
    private const int MaxTopLevelKeys = 64;

    /// <summary>Graded day rows kept. `days` is the only unbounded-by-construction region (one row
    /// per calendar day, forever), and the shell only ever reads today plus a short recent window.
    /// Trimmed on every write that touches it - same posture as the XP ledger's SkipLast.</summary>
    private const int DayHistory = 40;

    /// <summary>The page's graded day rows, keyed by LOCAL date (regression #978).</summary>
    private const string DaysKey = "days";

    /// <summary>Per-game tier/state, the second-largest region and the salvage step after `days`.</summary>
    private const string GamesKey = "games";

    /// <summary>Keys the page may never write: the host mints attendance and the streak from
    /// <c>class-ended</c>, so a set/merge naming one is dropped (and logged) rather than applied.</summary>
    private static readonly HashSet<string> HostOwnedKeys = new(StringComparer.Ordinal)
    {
        AttendanceKey, StreakKey, PerfectKey, TodayClassesKey, XpPaidKey, PunchCardsKey, WalletKey,
        WalletDeviceKey, WalletImportedKey, PendingMintsKey,
    };

    private readonly object _lock = new();
    private readonly Action<object> _broadcast;
    private readonly string _path;
    private JObject _state;
    private DispatcherTimer? _saveDebounce;
    private bool _dirty;

    public ArcademyMetaStore(Action<object> broadcast)
    {
        _broadcast = broadcast;
        _path = Path.Combine(App.UserDataPath, "arcademy_meta.json");
        _state = Load(_path);
    }

    public int Rev { get; private set; }

    /// <summary>The full blob for the <c>init</c> projection. A deep clone: the page's snapshot must
    /// not be a live handle onto the state a later command mutates.</summary>
    public JObject Snapshot()
    {
        lock (_lock) return (JObject)_state.DeepClone();
    }

    /// <summary>The <c>{type:"meta"}</c> broadcast body (whole-blob push, used after a host-side
    /// write such as an attendance credit).</summary>
    public object SnapshotMessage()
    {
        lock (_lock)
        {
            return new { type = "meta", rev = Rev, state = (JObject)_state.DeepClone() };
        }
    }

    /// <summary>
    /// Handle a <c>meta-command {op:"get"|"set"|"merge", key, value?}</c>. Always answers with
    /// <c>{type:"meta", key, value}</c> carrying the POST-write value, so the page's cache converges
    /// on what C# actually stored even when a write was clamped or refused.
    /// </summary>
    public void Handle(JObject o)
    {
        var op = (string?)o["op"];
        var key = NormalizeKey((string?)o["key"]);
        if (key == null)
        {
            App.Logger?.Debug("ArcademyMetaStore: command '{Op}' with a missing/oversized key - ignored", op);
            return;
        }

        try
        {
            switch (op)
            {
                case "get":
                    break;   // the reply below IS the get
                case "set":
                    if (Guard(key) && SizeGuard(key, o["value"])) Set(key, o["value"]);
                    break;
                case "merge":
                    if (Guard(key) && SizeGuard(key, o["value"])) Merge(key, o["value"]);
                    break;
                default:
                    App.Logger?.Debug("ArcademyMetaStore: unknown op '{Op}' - ignored", op);
                    return;
            }

            JToken? value;
            lock (_lock) value = _state[key]?.DeepClone();
            _broadcast(new { type = "meta", key, value });
        }
        catch (Exception ex) { App.Logger?.Warning("ArcademyMetaStore.Handle({Op}): {E}", op, ex.Message); }
    }

    /// <summary>Replace one key outright.</summary>
    public void Set(string key, JToken? value)
    {
        lock (_lock)
        {
            if (!AcceptNewKey(key)) return;
            _state[key] = value?.DeepClone() ?? JValue.CreateNull();
            TrimDays();
            Touch();
        }
    }

    /// <summary>Shallow-merge an object into one key (creating it when absent). A non-object patch
    /// is a set — the page uses merge for per-game bags, and refusing would just lose the write.</summary>
    public void Merge(string key, JToken? patch)
    {
        lock (_lock)
        {
            if (!AcceptNewKey(key)) return;
            if (patch is JObject po)
            {
                if (_state[key] is JObject existing)
                {
                    foreach (var p in po.Properties()) existing[p.Name] = p.Value.DeepClone();
                }
                else _state[key] = po.DeepClone();
            }
            else _state[key] = patch?.DeepClone() ?? JValue.CreateNull();
            TrimDays();
            Touch();
        }
    }

    public JToken? Get(string key)
    {
        lock (_lock) return _state[key]?.DeepClone();
    }

    /// <summary>
    /// Credit one completed class to today's attendance and roll the streak. LOCAL date, always
    /// (regression #978: local midnight rolls the streak, the UTC date only seeds content).
    ///
    /// <para>Idempotent per game key: replaying the same class on the same day does not re-count it
    /// toward perfect attendance. A gap of exactly one day continues the streak, a larger gap
    /// restarts it at 1, and a day already credited leaves the streak alone.</para>
    /// </summary>
    /// <para>THE TARDY SLIP (economy 2026-08-26, STACKED 2026-09-04). A gap of N+1 days is N missed
    /// nights, and the slips in the bag cover one night each: two slips carry a player over two
    /// consecutive missed nights, a THIRD missed night breaks the streak exactly as it always did,
    /// and nothing is spent unless the whole gap is covered (half a cover is not a cover — see
    /// <see cref="ArcademyEconomy.ConsumeLateSlips"/>). The slip is spent HERE, automatically, with
    /// nothing for the player to press, and the caller is told so the debrief can say so.</para>
    /// <returns>(streak, perfectAttendance, classesToday, slipsSpent) after the write. The last is
    /// a COUNT, not a flag: the server holds the authoritative bag and has to be told how many came
    /// off it, and "a slip was used" is simply <c>&gt; 0</c>.</returns>
    public (int Streak, int Perfect, int ClassesToday, int SlipsSpent) RecordAttendance(
        string localDate, string? gameKey)
    {
        lock (_lock)
        {
            var last = (string?)_state[AttendanceKey];
            int streak = (int?)_state[StreakKey] ?? 0;
            int perfect = (int?)_state[PerfectKey] ?? 0;
            int slipsSpent = 0;

            if (!string.Equals(last, localDate, StringComparison.Ordinal))
            {
                // A gap of G days is G-1 missed nights. One slip per missed night, all or nothing,
                // and never more than the desk will hold — so 2 covers two nights and a third
                // night is the break it has always been. `spent` is 0 whenever the gap is not
                // covered, which is the same "no" the no-slips player already got.
                var gap = ArcademyEconomy.DayGap(last, localDate);
                var missed = gap > 1 ? gap - 1 : 0;
                int spent = 0;
                if (!IsPreviousDay(last, localDate)
                    && missed is > 0 and <= ArcademyEconomy.SlipStackMax)
                {
                    spent = ArcademyEconomy.ConsumeLateSlips(WalletUnlocked(), missed);
                }

                if (IsPreviousDay(last, localDate)) streak += 1;
                else if (spent > 0)
                {
                    streak += 1;
                    slipsSpent = spent;
                    App.Logger?.Information(
                        "ArcademyMetaStore: {Spent} tardy slip(s) covered {Missed} missed night(s) after {Last} - streak carries on at {N}",
                        spent, missed, last, streak);
                }
                else streak = 1;
                _state[AttendanceKey] = localDate;
                _state[StreakKey] = streak;
                _state[TodayClassesKey] = new JArray();
            }

            var today = _state[TodayClassesKey] as JArray;
            if (today == null) { today = new JArray(); _state[TodayClassesKey] = today; }
            if (!string.IsNullOrWhiteSpace(gameKey)
                && !today.Any(t => string.Equals((string?)t, gameKey, StringComparison.Ordinal)))
            {
                today.Add(gameKey);
                // The perfect-attendance credit fires on the transition INTO a full day, so a
                // FIFTH completion (a retake, now that a day is four classes) can never award it
                // twice. ClassesPerDay is the timetable's CLASSES_PER_DAY - the two are the same
                // ruling and a mismatch would either strand perfect attendance forever (too high)
                // or hand it out early (too low).
                if (today.Count == ClassesPerDay)
                {
                    perfect += 1;
                    _state[PerfectKey] = perfect;
                }
            }

            Touch();
            return (streak, perfect, today.Count, slipsSpent);
        }
    }

    /// <summary>
    /// Stamp today's hole on <paramref name="gameKey"/>'s punch card. Rides the SAME
    /// <c>class-ended</c> credit as <see cref="RecordAttendance"/> and on the same LOCAL date
    /// (#978), which is what makes the rule "any graded finish stamps, once a day" true for free:
    /// an Esc-leave sends <c>class-left</c> and never gets here, and a Free Swim ends without a
    /// <c>class-ended</c> frame at all (<c>shell.js finishClass</c> returns first).
    ///
    /// <para>Idempotent per (local day, game), and a no-op once the card is full. See
    /// <see cref="ArcademyPunchCards.Stamp"/> for the day-one interlock.</para>
    ///
    /// <para>AN S DAY IS WORTH TWO HOLES (owner ruling 2026-08-23). <paramref name="gradedS"/> is
    /// the host's own read of the grade off the same <c>class-ended</c> frame; the page never gets
    /// to say. Idempotence is unchanged, so a retake that grades S mints nothing at all.</para>
    /// </summary>
    /// <returns>Null when there is no game key to stamp; otherwise the outcome, carrying a CLONE
    /// of the card so the caller can put it straight on a frame.</returns>
    public ArcademyPunchCards.PunchMint? StampPunchCard(string? gameKey, string localDate,
        bool gradedS = false)
    {
        if (string.IsNullOrWhiteSpace(gameKey)) return null;
        lock (_lock)
        {
            var mint = ArcademyPunchCards.Stamp(Cards(), gameKey, localDate, gradedS);
            if (mint.Minted)
            {
                Touch();
                App.Logger?.Information("ArcademyMetaStore: punch card '{Key}' stamped for {Date} (+{Got}, {N}/{Holes}){Unlock}",
                    gameKey, localDate, mint.Punches,
                    (int?)mint.Card[ArcademyPunchCards.PunchesField] ?? 0,
                    ArcademyPunchCards.Holes, mint.JustUnlocked ? " - ROOM UNLOCKED" : "");
            }
            return new ArcademyPunchCards.PunchMint(
                mint.Minted, mint.Punches, mint.JustUnlocked, (JObject)mint.Card.DeepClone());
        }
    }

    /// <summary>
    /// The first-run mint for <paramref name="gameKey"/>: the enrollment punch, the on-the-house
    /// punch and the sign-on punch — three holes, once ever (PUNCHCARD §4). Driven by the page's
    /// <c>enrollment-done</c> frame, which the shell posts at the end of the enrollment ceremony.
    ///
    /// <para>Validated here rather than trusted: the frame carries only a game key, and every
    /// number on the card is derived from what this store already holds. Repeat frames are
    /// no-ops, and today's daily stamp is superseded so the day can never net four.</para>
    /// </summary>
    public ArcademyPunchCards.PunchMint? EnrollPunchCard(string? gameKey, string localDate)
    {
        if (string.IsNullOrWhiteSpace(gameKey)) return null;
        lock (_lock)
        {
            var mint = ArcademyPunchCards.Enroll(Cards(), gameKey, localDate);
            if (mint.Minted)
            {
                Touch();
                App.Logger?.Information("ArcademyMetaStore: enrolled in '{Key}' on {Date} - {N} punches",
                    gameKey, localDate, mint.Punches);
            }
            return new ArcademyPunchCards.PunchMint(
                mint.Minted, mint.Punches, mint.JustUnlocked, (JObject)mint.Card.DeepClone());
        }
    }

    /// <summary>Game keys whose card is full — the rooms that offer Begin every night, board or
    /// no board (PUNCHCARD §2.3). The page derives the same list off the snapshot; this is the
    /// host's own read, for the log line that tells the owner what a dark build has unlocked.</summary>
    public IReadOnlyList<string> UnlockedGameKeys()
    {
        lock (_lock) return ArcademyPunchCards.UnlockedKeys(_state[PunchCardsKey] as JObject);
    }

    /// <summary>
    /// The push payload for the server mirror (PUNCHCARD §5): a normalized clone of every card
    /// with something earned on it, or an empty object when there is nothing to mirror yet.
    /// Taken under the lock like every other read, so it can never catch a card mid-mint.
    /// </summary>
    public JObject ExportCards()
    {
        lock (_lock) return ArcademyPunchCards.Export(_state[PunchCardsKey] as JObject);
    }

    /// <summary>
    /// Fold the mirror's merged reply into the cards, monotonically and through the same
    /// self-healing path a mint takes (<see cref="ArcademyPunchCards.ApplyServer"/>): dates
    /// unioned, enrollment earliest-wins, every derived number re-counted here rather than
    /// believed. Nothing local is ever dropped, so a cold or stale mirror costs nothing.
    ///
    /// <para>A restored <c>enrolledAt</c> is also what suppresses a repeat enrollment tutorial:
    /// the shell derives "already enrolled" from the card itself (spec §2.2), so there is no
    /// separate flag here to restore and none to forget.</para>
    /// </summary>
    /// <returns>The game keys the reply changed - empty when the mirror knew nothing new, which
    /// is the ordinary case and deliberately does not <see cref="Touch"/> (no rev bump, no save,
    /// no repaint for a no-op sync).</returns>
    public IReadOnlyList<string> ApplyServerCards(JObject? serverCards)
    {
        lock (_lock)
        {
            var changed = ArcademyPunchCards.ApplyServer(Cards(), serverCards);
            if (changed.Count > 0)
            {
                Touch();
                App.Logger?.Information(
                    "ArcademyMetaStore: mirror restored/updated {N} punch card(s): {Keys}",
                    changed.Count, string.Join(", ", changed));
            }
            return changed;
        }
    }

    /// <summary>The punch-card bag, created when absent. Called under the lock.</summary>
    private JObject Cards()
    {
        if (_state[PunchCardsKey] is not JObject cards)
        {
            cards = new JObject();
            _state[PunchCardsKey] = cards;
        }
        return cards;
    }

    /// <summary>
    /// Claim the one XP payment a <paramref name="gameKey"/> is worth on <paramref name="dayUtc"/>.
    /// True the first time that pair is seen, false forever after.
    ///
    /// <para>THE FARM GUARD. A class is replayable by design (the seed is the day's, so a retake is
    /// the same script) and nothing stops a player finishing Daily Trigger ten times before lunch.
    /// The ledger lives here rather than page-side for the same reason the streak does: a stale or
    /// edited page must not be able to mint a payout. Attendance is deliberately NOT keyed off this
    /// — <see cref="RecordAttendance"/> is idempotent on its own terms and runs on the LOCAL date
    /// (#978), so a retake still cannot double-count a class but a genuine new local day still
    /// credits one.</para>
    /// </summary>
    public bool TryClaimXpDay(string? gameKey, string? dayUtc)
    {
        if (string.IsNullOrWhiteSpace(gameKey) || string.IsNullOrWhiteSpace(dayUtc)) return true;
        lock (_lock)
        {
            if (_state[XpPaidKey] is not JObject paid)
            {
                paid = new JObject();
                _state[XpPaidKey] = paid;
            }
            if (paid[dayUtc] is not JArray day)
            {
                day = new JArray();
                paid[dayUtc] = day;
            }
            if (day.Any(t => string.Equals((string?)t, gameKey, StringComparison.Ordinal))) return false;

            day.Add(gameKey);
            foreach (var stale in paid.Properties().Select(p => p.Name)
                         .OrderBy(n => n, StringComparer.Ordinal)
                         .SkipLast(XpPaidDayHistory).ToList())
            {
                paid.Remove(stale);
            }
            Touch();
            return true;
        }
    }

    // ============================ the wallet ============================

    /// <summary>The wallet bag, shape-ensured and created when absent. Called under the lock, and
    /// deliberately NOT public: every mutation below goes through a named method so it can
    /// <see cref="Touch"/>, exactly the way the punch cards do.</summary>
    private JObject WalletUnlocked()
    {
        var wallet = ArcademyEconomy.EnsureShape(_state[WalletKey] as JObject);
        if (!ReferenceEquals(_state[WalletKey], wallet)) _state[WalletKey] = wallet;
        return wallet;
    }

    /// <summary>A CLONE of the wallet, for a projection or a reply. Never a live handle.</summary>
    public JObject WalletSnapshot()
    {
        lock (_lock) return (JObject)WalletUnlocked().DeepClone();
    }

    /// <summary>Read one thing off the wallet without cloning the whole bag.</summary>
    public bool WalletOwns(string sku)
    {
        lock (_lock) return ArcademyEconomy.Owns(WalletUnlocked(), sku);
    }

    /// <summary>(extraUnlocked, honorsUnlocked) — what the lever will actually accept tonight.</summary>
    public (bool Extra, bool Honors) LeverUnlocks()
    {
        lock (_lock)
        {
            var w = WalletUnlocked();
            return (ArcademyEconomy.ExtraUnlocked(w), ArcademyEconomy.HonorsUnlocked(w));
        }
    }

    /// <summary>
    /// Count one paid finish for (local day, game) and answer how many came BEFORE it — the index
    /// the replay-decay ladder reads. The counters live here rather than page-side for the same
    /// reason the XP ledger does: they are the only thing standing between a retake and a mint.
    /// </summary>
    public int NoteWalletPlay(string localDate, string? gameKey)
    {
        lock (_lock)
        {
            var prior = ArcademyEconomy.NotePlay(WalletUnlocked(), localDate, gameKey);
            Touch();
            return prior;
        }
    }

    /// <summary>Credit tickets. Zero and negatives are no-ops and do not even bump the rev.</summary>
    public void EarnTickets(int amount)
    {
        if (amount <= 0) return;
        lock (_lock)
        {
            ArcademyEconomy.EarnTickets(WalletUnlocked(), amount);
            Touch();
        }
    }

    /// <summary>
    /// Claim the one token <paramref name="localDate"/> is worth. True the first time that date is
    /// seen and false forever after — the same shape as <see cref="TryClaimXpDay"/>, on its own
    /// ledger inside the wallet rather than on the XP one, because the two roll on different clocks
    /// (the token is LOCAL, #978; XP is seeded by the UTC day).
    /// </summary>
    public bool TryClaimTokenDay(string? localDate)
    {
        if (string.IsNullOrWhiteSpace(localDate)) return false;
        lock (_lock)
        {
            if (!ArcademyEconomy.TryMintToken(WalletUnlocked(), localDate)) return false;
            Touch();
            App.Logger?.Information("ArcademyMetaStore: first S of {Date} - a token in the case", localDate);
            return true;
        }
    }

    /// <summary>Open the Extra Credit notch on a first A-or-better. Idempotent.</summary>
    public bool TryUnlockExtraCredit(string? grade)
    {
        lock (_lock)
        {
            if (!ArcademyEconomy.TryUnlockExtraCredit(WalletUnlocked(), grade)) return false;
            Touch();
            App.Logger?.Information("ArcademyMetaStore: Extra Credit unlocked (first {Grade})", grade);
            return true;
        }
    }

    /// <summary>Spend at the Prize Counter. Every rule is <see cref="ArcademyEconomy.Buy"/>'s;
    /// this only owns the lock, the rev bump and the log line.</summary>
    public ArcademyEconomy.BuyResult Buy(string? sku, string localDate)
    {
        lock (_lock)
        {
            var result = ArcademyEconomy.Buy(WalletUnlocked(), sku, localDate);
            if (result.Ok)
            {
                Touch();
                App.Logger?.Information("ArcademyMetaStore: bought '{Sku}' for {Cost}{Cur}",
                    result.Item?.Sku, result.Item?.Cost, result.Item?.Cur);
            }
            return result;
        }
    }

    // ============================ the wallet, banked on the server ============================

    /// <summary>
    /// This install's stable id, minted once and kept forever. The server records which devices
    /// have handed their wallet over (<c>imports[deviceId]</c>), so a second import from the same
    /// machine is a no-op there as well as here - two independent guards on the one operation that
    /// could ever add money twice.
    /// </summary>
    public string WalletDeviceId()
    {
        lock (_lock)
        {
            var existing = (string?)_state[WalletDeviceKey];
            // THE WIRE'S SHAPE, NOT JUST A LENGTH. The server refuses anything outside
            // `^[A-Za-z0-9_-]{8,64}$` with a 400, so an id that is the right length but the wrong
            // alphabet - a hand-edited file, a braced GUID somebody pasted in - would fail on EVERY
            // launch, for ever, with no way out. The import is the one call that cannot simply be
            // skipped and retried later, because the balance it carries is real. Minting a fresh one
            // costs nothing: the local `walletImported` flag is what stops a second import, and it
            // is still absent on a machine that has never managed a first.
            if (!string.IsNullOrWhiteSpace(existing) && DeviceIdShape.IsMatch(existing)) return existing;
            var minted = Guid.NewGuid().ToString("N");
            _state[WalletDeviceKey] = minted;
            Touch();
            App.Logger?.Information("ArcademyMetaStore: minted this install's wallet device id");
            return minted;
        }
    }

    /// <summary>When this machine carried its wallet up to the account, or null if it never has.
    /// The flag lives out here rather than inside the wallet object because it is a fact about this
    /// INSTALL, and the wallet object is about to become a cache of the account's copy.</summary>
    public string? WalletImportedAt()
    {
        lock (_lock) return (string?)_state[WalletImportedKey];
    }

    /// <summary>Record the import. Written on any answer at all, the server's own no-op included:
    /// what it records is "this device has been offered", not "this device paid in".</summary>
    public void MarkWalletImported()
    {
        lock (_lock)
        {
            _state[WalletImportedKey] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            Touch();
        }
    }

    /// <summary>Is there anything on this machine worth carrying up? Balances, what was ever
    /// earned, the shelf and the two lever rungs - the same regions the contract's import rule
    /// names.</summary>
    public bool WalletHasEarnings()
    {
        lock (_lock)
        {
            var w = WalletUnlocked();
            if (((int?)w["t"] ?? 0) > 0 || ((int?)w["k"] ?? 0) > 0) return true;
            if (((int?)w["earnedT"] ?? 0) > 0 || ((int?)w["earnedK"] ?? 0) > 0) return true;
            if (w["inv"] is JObject inv && inv.Count > 0) return true;
            return ArcademyEconomy.ExtraUnlocked(w) || ArcademyEconomy.HonorsUnlocked(w);
        }
    }

    /// <summary>
    /// Take the account's wallet whole. REPLACE, never merge: the server is the authority once the
    /// import has run, and a local merge here would be this machine quietly arguing with it - which
    /// is exactly how a balance ends up different on two desks. Anything the server carries that
    /// this build does not know about rides along untouched.
    /// </summary>
    public void AdoptServerWallet(JObject? wallet)
    {
        if (wallet == null) return;
        lock (_lock)
        {
            _state[WalletKey] = ArcademyEconomy.EnsureShape((JObject)wallet.DeepClone());
            Touch();
        }
    }

    /// <summary>The parked mint frames, oldest first - a clone, and only the rows still carrying a
    /// <c>mintId</c> (a frame without one cannot be paid idempotently, so it is not a frame).</summary>
    public JArray PendingMints()
    {
        lock (_lock)
        {
            var queue = new JArray();
            if (_state[PendingMintsKey] is not JArray parked) return queue;
            foreach (var row in parked)
            {
                if (row is JObject f && !string.IsNullOrWhiteSpace((string?)f["mintId"]))
                    queue.Add(f.DeepClone());
            }
            return queue;
        }
    }

    /// <summary>How many frames are still waiting - the number the log line quotes.</summary>
    public int PendingMintCount()
    {
        lock (_lock) return (_state[PendingMintsKey] as JArray)?.Count ?? 0;
    }

    /// <summary>
    /// Park one frame the server never took. Idempotent on <c>mintId</c>, so a retry that queues
    /// the same night twice still only owes one mint.
    ///
    /// <para>Capped, and the cap drops the OLDEST. A queue this long means months offline with an
    /// account attached, and the server's own replay wall is fourteen days anyway - so the frames
    /// falling off the front were already past being payable, and the alternative (refusing new
    /// ones) would lose tonight instead of a night nobody can bank any more.</para>
    /// </summary>
    public void QueueMint(JObject frame)
    {
        var mintId = (string?)frame["mintId"];
        if (string.IsNullOrWhiteSpace(mintId)) return;
        lock (_lock)
        {
            if (_state[PendingMintsKey] is not JArray parked)
            {
                parked = new JArray();
                _state[PendingMintsKey] = parked;
            }
            if (parked.Any(r => r is JObject f
                                && string.Equals((string?)f["mintId"], mintId, StringComparison.Ordinal))) return;

            parked.Add(frame.DeepClone());
            while (parked.Count > PendingMintCap) parked.RemoveAt(0);
            Touch();
            App.Logger?.Information("ArcademyMetaStore: parked a mint for {Game} on {Day} ({N} waiting)",
                (string?)frame["game"], (string?)frame["localDay"], parked.Count);
        }
    }

    /// <summary>Drop one parked frame once the server has answered for it, one way or the other.
    /// Anything malformed sharing the ride is swept out with it.</summary>
    public void DropMint(string mintId)
    {
        lock (_lock)
        {
            if (_state[PendingMintsKey] is not JArray parked || parked.Count == 0) return;
            var keep = new JArray();
            foreach (var row in parked)
            {
                if (row is not JObject f) continue;
                var id = (string?)f["mintId"];
                if (string.IsNullOrWhiteSpace(id) || string.Equals(id, mintId, StringComparison.Ordinal)) continue;
                keep.Add(f);
            }
            if (keep.Count == parked.Count) return;
            _state[PendingMintsKey] = keep;
            Touch();
        }
    }

    /// <summary>The rooms the player is enrolled in, ordinally sorted — the roster the nightly
    /// payday draw runs over. Enrollment is the punch card's own <c>enrolledAt</c>, so there is no
    /// second list here to keep in step with it.</summary>
    public IReadOnlyList<string> EnrolledGameKeys()
    {
        lock (_lock)
        {
            if (_state[PunchCardsKey] is not JObject cards) return Array.Empty<string>();
            return cards.Properties()
                .Where(p => p.Value is JObject c
                            && c[ArcademyPunchCards.EnrolledAtField] is JValue { Type: JTokenType.String })
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>Flush any debounced write now (teardown / app exit).</summary>
    public void FlushSave()
    {
        try { _saveDebounce?.Stop(); } catch { }
        JObject snapshot;
        lock (_lock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = (JObject)_state.DeepClone();
        }
        WriteAtomic(snapshot);
    }

    // ============================ internals ============================

    /// <summary>Bump the revision and arm the debounced write. Callers own the broadcast (the page
    /// gets a per-key reply; a host-side write gets a whole-blob push). Called under the lock.</summary>
    private void Touch()
    {
        Rev++;
        _dirty = true;
        DebounceSave();
    }

    private void DebounceSave()
    {
        var disp = Application.Current?.Dispatcher;
        if (disp == null || disp.HasShutdownStarted)
        {
            // No dispatcher to debounce on (unit host / shutting down): write through instead of
            // silently dropping the mutation.
            _dirty = true;
            var snapshot = (JObject)_state.DeepClone();
            if (WriteAtomic(snapshot)) _dirty = false;
            return;
        }
        disp.BeginInvoke(() =>
        {
            if (_saveDebounce == null)
            {
                _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _saveDebounce.Tick += (_, _) => { try { _saveDebounce?.Stop(); } catch { } FlushSave(); };
            }
            _saveDebounce.Stop();
            _saveDebounce.Start();
        });
    }

    /// <summary>False (and logged) for a key the host owns. The page gets a reply either way, so a
    /// refused write shows up page-side as "the value did not change".</summary>
    private static bool Guard(string key)
    {
        if (!HostOwnedKeys.Contains(key)) return true;
        App.Logger?.Debug("ArcademyMetaStore: page tried to write host-owned key '{Key}' - refused", key);
        return false;
    }

    /// <summary>False (and logged) for a value whose serialized form exceeds
    /// <see cref="MaxValueChars"/>. The page still gets its reply, carrying the value that is
    /// actually stored, so a refused write shows up as "it did not change" rather than as silence.
    /// Bounded HERE rather than at load time because a blob that has already grown past
    /// <see cref="MaxBlobChars"/> is a save the player has to lose.</summary>
    private static bool SizeGuard(string key, JToken? value)
    {
        if (value == null) return true;
        int len;
        try { len = value.ToString(Formatting.None).Length; }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyMetaStore: could not size '{Key}' ({E}) - refused", key, ex.Message);
            return false;
        }
        if (len <= MaxValueChars) return true;
        App.Logger?.Warning("ArcademyMetaStore: '{Key}' is {N} chars (cap {Cap}) - refused", key, len, MaxValueChars);
        return false;
    }

    /// <summary>Cap the number of top-level keys. Existing keys always pass (an update must never be
    /// refused because the blob is wide); only a NEW key can be turned away. Called under the lock.</summary>
    private bool AcceptNewKey(string key)
    {
        if (_state[key] != null || _state.Count < MaxTopLevelKeys) return true;
        App.Logger?.Warning("ArcademyMetaStore: {N} top-level keys already - new key '{Key}' dropped",
            _state.Count, key);
        return false;
    }

    /// <summary>Keep the newest <see cref="DayHistory"/> day rows. `days` is keyed by yyyy-MM-dd, so
    /// ordinal order IS chronological order and the same SkipLast shape the XP ledger uses works
    /// here. Runs on every write that could have touched the region rather than on a schedule: the
    /// store has no clock of its own, and an unbounded region only bites at the next load.
    /// Called under the lock.</summary>
    private void TrimDays()
    {
        if (_state[DaysKey] is not JObject days || days.Count <= DayHistory) return;
        foreach (var stale in days.Properties().Select(p => p.Name)
                     .OrderBy(n => n, StringComparer.Ordinal)
                     .SkipLast(DayHistory).ToList())
        {
            days.Remove(stale);
        }
    }

    private static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        key = key.Trim();
        return key.Length > MaxKeyLength ? null : key;
    }

    /// <summary>Is <paramref name="last"/> exactly the calendar day before <paramref name="today"/>?
    /// Anything unparseable answers false, which restarts the streak rather than extending one from
    /// a corrupt date.</summary>
    private static bool IsPreviousDay(string? last, string today)
    {
        if (string.IsNullOrWhiteSpace(last)) return false;
        if (!DateTime.TryParseExact(last, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var prev)) return false;
        if (!DateTime.TryParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var now)) return false;
        return (now.Date - prev.Date).TotalDays == 1;
    }

    /// <summary>
    /// ATOMIC WRITE. A bare <c>WriteAllText</c> truncates the file first, so a crash, a power cut or
    /// an OnExit TerminateProcess landing in that window leaves a HALF-WRITTEN meta blob — and the
    /// next launch parses it, fails, and starts fresh: the streak, every grade and the XP ledger,
    /// gone. Temp file → flush → <see cref="File.Replace(string,string,string)"/>, which also leaves
    /// the previous good copy behind as <c>.bak</c> for <see cref="Load"/> to fall back on.
    /// </summary>
    /// <returns>True when the bytes are on disk.</returns>
    private bool WriteAtomic(JObject snapshot)
    {
        try
        {
            Directory.CreateDirectory(App.UserDataPath);
            var text = snapshot.ToString(Formatting.Indented);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(_path))
            {
                // One .bak generation, kept deliberately: the fallback is only useful for the write
                // that just failed, and a deeper history of a save file nobody reads is just clutter.
                File.Replace(tmp, _path, _path + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, _path);
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyMetaStore.WriteAtomic: {E}", ex.Message);
            try { if (File.Exists(_path + ".tmp")) File.Delete(_path + ".tmp"); } catch { }
            return false;
        }
    }

    /// <summary>Read the blob, or an empty object once every recovery has been tried. A corrupt meta
    /// file must not stop the Arcademy opening — but "start fresh" is the LAST resort, not the first:
    /// it throws away the player's whole transcript, and the two failures we actually see (a blob
    /// that outgrew the cap, and a truncated write) are both recoverable.
    ///
    /// <para>The ladder: parse the main file → parse the <c>.bak</c> the previous
    /// <see cref="File.Replace(string,string,string)"/> left → empty. An over-cap blob is SALVAGED
    /// rather than dropped (shed <c>days</c>, then the oldest <c>games</c> entries), and anything
    /// destructive copies the original to a <c>.corrupt</c> sidecar first so the save is still
    /// there to look at.</para></summary>
    private static JObject Load(string path)
    {
        var main = TryLoadOne(path, salvage: true);
        if (main != null) return main;

        var bak = TryLoadOne(path + ".bak", salvage: true);
        if (bak != null)
        {
            App.Logger?.Warning("ArcademyMetaStore: recovered from {Path}.bak - the main file was unreadable", path);
            return bak;
        }

        App.Logger?.Warning("ArcademyMetaStore: no readable meta blob - starting fresh");
        return new JObject();
    }

    private static JObject? TryLoadOne(string path, bool salvage)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var raw = File.ReadAllText(path);
            if (raw.Length > MaxBlobChars)
            {
                App.Logger?.Warning("ArcademyMetaStore: {Path} is {N} chars - attempting salvage", path, raw.Length);
                PreserveCorrupt(path);
                if (!salvage) return null;
                JObject over;
                try { over = JObject.Parse(raw); }
                catch (Exception ex)
                {
                    App.Logger?.Warning("ArcademyMetaStore: over-cap blob does not parse either ({E})", ex.Message);
                    return null;
                }
                return Salvage(over);
            }
            return JObject.Parse(raw);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("ArcademyMetaStore: load of {Path} failed ({E})", path, ex.Message);
            return null;
        }
    }

    /// <summary>Shed the big page-owned regions until the blob is back under the cap, newest-first.
    /// Order matters: <c>days</c> is the graded transcript (nice to have), <c>games</c> carries tier
    /// progression (worse to lose), and the host-owned streak/ledger keys are never touched — they
    /// are tiny and they are the ones a player would actually mourn.</summary>
    private static JObject Salvage(JObject over)
    {
        if (over[DaysKey] is JObject days && days.Count > 0)
        {
            days.RemoveAll();
            App.Logger?.Warning("ArcademyMetaStore: salvage dropped the 'days' transcript");
            if (over.ToString(Formatting.None).Length <= MaxBlobChars) return over;
        }
        if (over[GamesKey] is JObject games)
        {
            // Oldest first by key order - there is no timestamp in a per-game bag, and ordinal order
            // is at least stable, so the same entries go each time rather than a random half.
            foreach (var name in games.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList())
            {
                if (over.ToString(Formatting.None).Length <= MaxBlobChars) break;
                games.Remove(name);
                App.Logger?.Warning("ArcademyMetaStore: salvage dropped games['{Key}']", name);
            }
            if (over.ToString(Formatting.None).Length <= MaxBlobChars) return over;
        }
        // Still over: keep only what the host owns, which is all that cannot be re-earned.
        var kept = new JObject();
        foreach (var k in HostOwnedKeys)
        {
            if (over[k] != null) kept[k] = over[k]!.DeepClone();
        }
        App.Logger?.Warning("ArcademyMetaStore: salvage kept the host-owned keys only");
        return kept;
    }

    /// <summary>Copy a file we are about to recover from (destructively) to a <c>.corrupt</c> sidecar.
    /// One generation, overwritten: the point is that the bytes still exist to look at, not an
    /// archive.</summary>
    private static void PreserveCorrupt(string path)
    {
        try { File.Copy(path, path + ".corrupt", overwrite: true); }
        catch (Exception ex) { App.Logger?.Debug("ArcademyMetaStore: .corrupt sidecar failed: {E}", ex.Message); }
    }
}
