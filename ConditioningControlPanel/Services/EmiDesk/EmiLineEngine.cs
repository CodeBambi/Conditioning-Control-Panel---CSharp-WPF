using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

// ============================================================================================
// THE LINE ENGINE. docs/emi-desk/LINES-SCHEMA.md is the contract; this file implements it and
// nothing else. Everything that decides WHETHER she speaks lives here; everything that decides
// HOW it looks lives in the window. The file is data (Resources/emi/desk-lines.json, shipped as
// Content beside the exe) and a missing or corrupt file leaves the engine INERT rather than
// throwing: EMI with no lines is a widget, EMI with a stack trace is a bug report.
// ============================================================================================

/// <summary>One line, resolved and ready to speak. Wordless holds carry an empty <see cref="Text"/>.</summary>
/// <param name="Id">The line id, for <see cref="EmiLineEngine.Ack"/> and the recent ring.</param>
/// <param name="Pool">The pool it came from (diagnostics only).</param>
/// <param name="Text">The line with its tokens already substituted. Empty on a hold row.</param>
/// <param name="Face">The reaction kaomoji.</param>
/// <param name="Chain">An optional chains.js chain id to play before the bubble lands.</param>
/// <param name="Priority">1 filler, 2 normal, 3 ceremony.</param>
/// <param name="Hold">True when this is a wordless face hold rather than a line.</param>
/// <param name="HoldMs">How long a hold sits, in ms. 0 for a normal line.</param>
public sealed record LineDraw(
    string Id, string Pool, string Text, string Face, string? Chain,
    int Priority, bool Hold, int HoldMs);

/// <summary>One offer, resolved and ready to put on the glass.</summary>
/// <param name="Id">The ask id.</param>
/// <param name="Moment">The moment that raised it.</param>
/// <param name="Question">The question, tokens substituted. Chip 1 is YES.</param>
/// <param name="Face">The face she wears while she waits.</param>
/// <param name="Chips">Exactly two chip labels; index 0 is YES.</param>
/// <param name="Yes">Her reaction to chip 1.</param>
/// <param name="No">Her reaction to chip 2.</param>
/// <param name="Effect">What a YES does (see LINES-SCHEMA 4), tokens already substituted.</param>
/// <param name="EffectNo">What chip 2 does when it is a second action instead of a decline.</param>
public sealed record AskDraw(
    string Id, string Moment, string Question, string Face, IReadOnlyList<string> Chips,
    LineDraw Yes, LineDraw No, string Effect, string? EffectNo);

/// <summary>
/// Loads <c>Resources/emi/desk-lines.json</c> once and answers "does she say anything, and what".
///
/// The owner-locked rules this implements, in the order LINES-SCHEMA 5 states them: holds and the
/// panic silence beat everything, then per-moment limits, then the per-moment cooldown, then the
/// 45 s global floor, then the odds roll, then the ask branch, then the dork channel, then the
/// pool choice and the shuffle-bag deal. Priority 3 bypasses the odds and the floor and NOTHING
/// else.
///
/// Rotation (BRIEF 8): per-pool shuffle bags dealt without replacement, persisted in
/// <see cref="EmiState.SeenByPool"/> so a relaunch does not re-propose last night's lines; on
/// exhaustion the pool reshuffles with the last three dealt kept out of the first three slots. A
/// global 40-id recent ring sits on top, which common pools avoid while they still can.
/// </summary>
public sealed class EmiLineEngine
{
    /// <summary>The process-wide engine. Cheap to touch: the file loads on the first draw.</summary>
    public static EmiLineEngine Instance { get; } = new();

    /// <summary>Milliseconds between any two spontaneous lines (BRIEF 8; priority 3 is exempt).</summary>
    public const int GlobalFloorMs = 45_000;

    /// <summary>Milliseconds between two offers (BRIEF 7).</summary>
    public const int AskGapMs = 600_000;

    /// <summary>She never offers before this many summons (BRIEF 7).</summary>
    public const int AskMinSummons = 3;

    /// <summary>Unanswered offers in a row before she stops offering for the launch (BRIEF 7).</summary>
    public const int AskIgnoreLimit = 3;

    private const int RecentRingSize = 40;

    private readonly object _gate = new();
    private readonly Random _rng = new();

    private LinesFile? _file;
    private bool _loadAttempted;

    // Per-pool shuffle bags. Runtime only: the PERSISTED half is EmiState.SeenByPool, and a bag is
    // rebuilt from it on the first touch of that pool this launch.
    private readonly Dictionary<string, List<string>> _bags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, LineDef>> _byId = new(StringComparer.Ordinal);

    // Cooldown clocks, keyed by cooldownKey ?? momentId, stamped only when she actually SPOKE.
    private readonly Dictionary<string, DateTime> _spokeAt = new(StringComparer.Ordinal);

    // Limit buckets that die with the process (launch, video, run, lockdown, rush, per-target).
    private readonly Dictionary<string, int> _volatileLimits = new(StringComparer.Ordinal);

    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);

    private DateTime _lastSpokeUtc = DateTime.MinValue;
    private DateTime _lastAskUtc = DateTime.MinValue;
    private bool _doubleSpent;
    private bool _dorkSpent;

    // The ask the last Draw() chose instead of a line. See DrawAsk().
    private AskDraw? _pendingAsk;
    private string? _pendingAskMoment;

    // Holds. _heldBy is a holdUntilReleased moment; _holdUntilUtc covers fixed holds and tails.
    private string? _heldBy;
    private DateTime _holdUntilUtc = DateTime.MinValue;

    private int _ignoredAsksThisLaunch;

    // ---- the rotation ledger ---------------------------------------------------------------
    // Normally EmiState (%LOCALAPPDATA%\ConditioningControlPanel\emi-desk.json). An engine built
    // by FromJson keeps the whole ledger in memory instead, so a test can exercise the owner's
    // rotation rule a thousand times without touching the user's own state file. Every read and
    // write below goes through the four accessors, so there is exactly one place this can diverge.
    private readonly Dictionary<string, List<string>>? _isoSeen;
    private readonly List<string>? _isoRecent;
    private readonly Dictionary<string, int>? _isoLimits;

    /// <summary>True when this engine keeps its rotation ledger in memory (a test instance).</summary>
    private bool Isolated => _isoSeen != null;

    /// <summary>Summons the offer cadence counts against. In-memory engines never offer.</summary>
    private int SummonCount
    {
        get
        {
            if (Isolated) return 0;
            try { return EmiState.Current.SummonCount; }
            catch { return 0; }
        }
    }

    /// <summary>The process-wide engine, reading and writing the real state file.</summary>
    private EmiLineEngine() { }

    private EmiLineEngine(string json)
    {
        _isoSeen = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        _isoRecent = new List<string>();
        _isoLimits = new Dictionary<string, int>(StringComparer.Ordinal);
        LoadJson(json);
    }

    /// <summary>
    /// Test seam: an engine over an in-memory lines file, with an in-memory ledger. Nothing it does
    /// can reach <see cref="EmiState"/>, the user's <c>emi-desk.json</c> or the shipped lines file.
    /// </summary>
    internal static EmiLineEngine FromJson(string json) => new(json);

    // ------------------------------------------------------------------ loading

    /// <summary>True once the file has been read and parsed. False leaves every draw null.</summary>
    public bool Ready
    {
        get { lock (_gate) { EnsureLoaded(); return _file != null; } }
    }

    /// <summary>Moment ids the file carries (for tests and diagnostics).</summary>
    public IReadOnlyCollection<string> MomentIds
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _file?.Moments?.Keys.ToArray() ?? Array.Empty<string>();
            }
        }
    }

    private void EnsureLoaded()
    {
        if (_loadAttempted) return;
        _loadAttempted = true;
        try
        {
            var path = FindLinesFile();
            if (path == null)
            {
                Log.Warning("[EmiDesk] no desk-lines.json found, the line engine is inert");
                return;
            }
            LoadJson(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _file = null;
            Log.Warning(ex, "[EmiDesk] desk-lines.json unreadable, the line engine is inert");
        }
    }

    /// <summary>Parse one lines file. Shared by the disk load and the in-memory test engine.</summary>
    private void LoadJson(string json)
    {
        try
        {
            _loadAttempted = true;
            var file = JsonConvert.DeserializeObject<LinesFile>(json);
            if (file?.Moments == null || file.Pools == null)
            {
                Log.Warning("[EmiDesk] lines file parsed to nothing usable, the line engine is inert");
                return;
            }
            file.Asks ??= new List<AskDef>();
            file.Deferred ??= new List<string>();
            _byId.Clear();
            foreach (var kv in file.Pools)
            {
                var map = new Dictionary<string, LineDef>(StringComparer.Ordinal);
                foreach (var l in kv.Value)
                {
                    if (l?.Id == null) continue;
                    map[l.Id] = l;
                }
                _byId[kv.Key] = map;
            }
            _file = file;
            PruneStaleLimits();
            Log.Information("[EmiDesk] lines file v{Version}: {Moments} moments, {Pools} pools, {Asks} asks",
                file.Version, file.Moments.Count, file.Pools.Count, file.Asks.Count);
        }
        catch (Exception ex)
        {
            _file = null;
            Log.Warning(ex, "[EmiDesk] lines file unreadable, the line engine is inert");
        }
    }

    /// <summary>
    /// The shipped file sits beside the exe (csproj Content item). The walk-up probe is for the
    /// test host, whose base directory is the test project's bin folder.
    /// </summary>
    private static string? FindLinesFile()
    {
        try
        {
            var direct = Path.Combine(AppContext.BaseDirectory, "Resources", "emi", "desk-lines.json");
            if (File.Exists(direct)) return direct;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var a = Path.Combine(dir.FullName, "Resources", "emi", "desk-lines.json");
                if (File.Exists(a)) return a;
                var b = Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources", "emi", "desk-lines.json");
                if (File.Exists(b)) return b;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] lines file probe failed");
        }
        return null;
    }

    // ------------------------------------------------------------------ public API

    /// <summary>
    /// Evaluate one moment. Returns the line to speak, or null when she stays quiet.
    ///
    /// When the fire chose an OFFER instead of a line this returns null and parks the offer, which
    /// <see cref="DrawAsk"/> then hands over: one evaluation, one set of limit increments, one
    /// odds roll, whichever branch wins. Call <see cref="Draw"/> first, then
    /// <see cref="DrawAsk"/>, and speak whichever came back.
    ///
    /// Nothing here is committed to state until <see cref="Ack"/>: the caller may still drop the
    /// draw (a hold landed in the same tick, the window went away) without burning the cooldown.
    /// </summary>
    public LineDraw? Draw(string momentId, IReadOnlyDictionary<string, object?>? ctx = null)
    {
        lock (_gate)
        {
            _pendingAsk = null;
            _pendingAskMoment = null;
            try { return DrawCore(momentId, ctx ?? EmptyCtx); }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] Draw({Moment}) failed", momentId);
                return null;
            }
        }
    }

    /// <summary>
    /// The offer the last <see cref="Draw"/> of this moment chose, or null. Consumes it, so a
    /// second call returns null. <paramref name="ctx"/> is accepted for symmetry and to keep the
    /// call site honest; the offer was already resolved against it.
    /// </summary>
    public AskDraw? DrawAsk(string momentId, IReadOnlyDictionary<string, object?>? ctx = null)
    {
        lock (_gate)
        {
            if (_pendingAsk == null) return null;
            if (!string.Equals(_pendingAskMoment, momentId, StringComparison.Ordinal)) return null;
            var ask = _pendingAsk;
            _pendingAsk = null;
            _pendingAskMoment = null;
            return ask;
        }
    }

    /// <summary>
    /// Confirm that a draw actually reached the screen. Stamps the global floor, the moment's
    /// cooldown clock, the recent ring, the ask gap and the launch's one double. Call it with the
    /// line's or the ask's id the instant the bubble goes up, and again for an ask's yes/no
    /// reaction line.
    /// </summary>
    public void Ack(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (_gate)
        {
            try
            {
                var now = DateTime.UtcNow;
                _lastSpokeUtc = now;
                if (id.StartsWith("ask.", StringComparison.Ordinal)) _lastAskUtc = now;

                if (_ackMoment.TryGetValue(id, out var key))
                {
                    _spokeAt[key] = now;
                    _ackMoment.Remove(id);
                }
                if (_ackDouble.Remove(id)) _doubleSpent = true;

                NoteRecent(id);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] Ack({Id}) failed", id);
            }
        }
    }

    // What Ack has to stamp for a draw it has not seen yet. Populated by DrawCore, drained by Ack.
    private readonly Dictionary<string, string> _ackMoment = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ackDouble = new(StringComparer.Ordinal);

    /// <summary>True while any hold, hold tail or the panic silence is running. Nothing speaks.</summary>
    public bool HoldActive
    {
        get { lock (_gate) { return _heldBy != null || DateTime.UtcNow < _holdUntilUtc; } }
    }

    /// <summary>The moment currently holding her, or null.</summary>
    public string? HeldBy { get { lock (_gate) { return _heldBy; } } }

    /// <summary>
    /// Release a <c>holdUntilReleased</c> hold (the avatar stopped talking, the attention check
    /// resolved, intake closed). Applies the moment's <c>tailMs</c> silence afterwards. Releasing
    /// a hold that is not held is a no-op.
    /// </summary>
    public void ReleaseHold(string momentId)
    {
        lock (_gate)
        {
            try
            {
                if (!string.Equals(_heldBy, momentId, StringComparison.Ordinal)) return;
                _heldBy = null;
                EnsureLoaded();
                int tail = 0;
                if (_file?.Moments != null && _file.Moments.TryGetValue(momentId, out var m)) tail = m.TailMs;
                var until = DateTime.UtcNow.AddMilliseconds(tail);
                if (until > _holdUntilUtc) _holdUntilUtc = until;
                Log.Debug("[EmiDesk] hold released by {Moment}, tail {Tail} ms", momentId, tail);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ReleaseHold({Moment}) failed", momentId);
            }
        }
    }

    /// <summary>
    /// Reset a volatile limit bucket. The hook agent calls this on the events LINES-SCHEMA 2.1
    /// names: <c>video</c> per VideoStarted, <c>run</c> per SessionStarted, <c>lockdown</c> per
    /// arm, <c>rush</c> per Pink Rush.
    /// </summary>
    public void ResetBucket(string per)
    {
        if (string.IsNullOrWhiteSpace(per)) return;
        lock (_gate)
        {
            try
            {
                var suffix = "|" + per;
                var dead = _volatileLimits.Keys.Where(k => k.EndsWith(suffix, StringComparison.Ordinal)).ToList();
                foreach (var k in dead) _volatileLimits.Remove(k);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ResetBucket({Per}) failed", per);
            }
        }
    }

    /// <summary>Note that an offer went unanswered. Three in a row and she stops offering this launch.</summary>
    public void NoteAskIgnored()
    {
        lock (_gate)
        {
            _ignoredAsksThisLaunch++;
            if (Isolated) return;
            try
            {
                var st = EmiState.Current;
                st.IgnoreStreak++;
                EmiState.SaveSoon();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ignore streak bump failed"); }
        }
    }

    /// <summary>Note that an offer was answered. Clears the ignore streak.</summary>
    public void NoteAskAnswered()
    {
        lock (_gate)
        {
            _ignoredAsksThisLaunch = 0;
            if (Isolated) return;
            try
            {
                var st = EmiState.Current;
                if (st.IgnoreStreak != 0) { st.IgnoreStreak = 0; EmiState.SaveSoon(); }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ignore streak clear failed"); }
        }
    }

    /// <summary>True while she has given up on offering for this launch (three ignored in a row).</summary>
    public bool AsksExhausted
    {
        get { lock (_gate) { return _ignoredAsksThisLaunch >= AskIgnoreLimit; } }
    }

    /// <summary>True while bedtime is set: offers and glass channels are muted until 06:00.</summary>
    public static bool BedtimeSet
    {
        get
        {
            try { return DateTime.UtcNow < EmiState.Current.BedtimeUntil; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Turn an anonymous ctx object (<c>new { target = "fyp", n = 3 }</c>) into the dictionary the
    /// gates and tokens read. Already-a-dictionary passes through; null yields an empty one.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> ToCtx(object? ctx)
    {
        if (ctx == null) return EmptyCtx;
        if (ctx is IReadOnlyDictionary<string, object?> ro) return ro;
        if (ctx is IDictionary<string, object?> rw) return new Dictionary<string, object?>(rw, StringComparer.OrdinalIgnoreCase);
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in ctx.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                d[p.Name] = p.GetValue(ctx);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ctx conversion failed");
        }
        return d;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyCtx =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ the pipeline

    private LineDraw? DrawCore(string momentId, IReadOnlyDictionary<string, object?> ctx)
    {
        EnsureLoaded();
        var file = _file;
        if (file?.Moments == null) return null;

        // 1. unknown / deferred
        if (file.Deferred.Contains(momentId, StringComparer.Ordinal))
        {
            LogOnce("deferred:" + momentId, "[EmiDesk] moment {Moment} is deferred in the lines file, ignored", momentId);
            return null;
        }
        if (!file.Moments.TryGetValue(momentId, out var m) || m == null)
        {
            LogOnce("unknown:" + momentId, "[EmiDesk] moment {Moment} is not in the lines file, ignored", momentId);
            return null;
        }

        var now = DateTime.UtcNow;

        // 2 + 3. an active hold (or the panic tail) silences everything, ceremonies included.
        //        A hold moment itself may still arm, otherwise a second hold could never land.
        if (!m.Hold && (_heldBy != null || now < _holdUntilUtc)) return null;

        // 2.2 hold moments never speak.
        if (m.Hold) return ArmHold(momentId, m, ctx);

        // 4. limits (a fire that loses the odds roll still counts as a fire)
        if (!TakeLimit(momentId, m, ctx)) return null;

        // 5. cooldown
        var cdKey = string.IsNullOrEmpty(m.CooldownKey) ? momentId : m.CooldownKey!;
        if (m.CooldownMs > 0 && _spokeAt.TryGetValue(cdKey, out var last)
            && (now - last).TotalMilliseconds < m.CooldownMs) return null;

        bool ceremony = m.Priority >= 3;

        // 6. global floor
        if (!ceremony && (now - _lastSpokeUtc).TotalMilliseconds < GlobalFloorMs) return null;

        // 7. odds
        if (!ceremony && _rng.NextDouble() >= m.Odds) return null;

        int ceiling = Math.Min(m.SpiceCeiling, UserSpice());

        // 9. the ask branch
        if (m.AskOdds > 0 && AskGatesPass(m) && _rng.NextDouble() < m.AskOdds)
        {
            var ask = PickAsk(momentId, ctx, ceiling);
            if (ask != null)
            {
                _pendingAsk = ask;
                _pendingAskMoment = momentId;
                _ackMoment[ask.Id] = cdKey;
                return null;
            }
            // No eligible offer: fall through and let her say something instead.
        }

        // 8 + 5.6: an ask that fired because of ITS OWN effect never speaks twice.
        if (Truthy(ctx, "fromAsk")) return null;

        // 10. the dork channel. Never on a ceremony, and never on a moment whose ceiling is 0:
        //     those are the moments where something went wrong for the user (MOMENTS 3.13) and a
        //     non-sequitur there reads as not listening, whatever the dork line's own spice is.
        if (!ceremony && ceiling > 0 && !_dorkSpent && file.Dork != null
            && !string.IsNullOrEmpty(file.Dork.Pool)
            && _rng.NextDouble() < file.Dork.Odds)
        {
            var dork = Deal(file.Dork.Pool!, ctx, ceiling, avoidRecent: true);
            if (dork != null)
            {
                _dorkSpent = true;
                return Finish(dork, file.Dork.Pool!, m, cdKey, ctx);
            }
        }

        // 11 + 12. pool choice and the deal
        var chosen = ChoosePool(m, ctx, ceiling);
        if (chosen == null) return null;
        var line = Deal(chosen, ctx, ceiling, avoidRecent: IsCommon(chosen));
        if (line == null) return null;

        return Finish(line, chosen, m, cdKey, ctx);
    }

    private LineDraw Finish(LineDef line, string pool, MomentDef m, string cdKey,
                            IReadOnlyDictionary<string, object?> ctx)
    {
        var text = Substitute(line.T, ctx) ?? string.Empty;
        _ackMoment[line.Id!] = cdKey;
        if (line.Double) _ackDouble.Add(line.Id!);
        return new LineDraw(line.Id!, pool, text, line.Face ?? "^_^", line.Chain,
                            m.Priority, false, 0);
    }

    /// <summary>
    /// A hold moment: no bubble, ever. She takes a row from <c>common.hold</c> (or nothing when
    /// the moment has no pools, e.g. appClosing, whose flinch lives in code) and wears it.
    /// </summary>
    private LineDraw? ArmHold(string momentId, MomentDef m, IReadOnlyDictionary<string, object?> ctx)
    {
        var now = DateTime.UtcNow;
        if (m.HoldUntilReleased)
        {
            _heldBy = momentId;
        }
        else
        {
            var until = now.AddMilliseconds(Math.Max(0, m.HoldMs) + Math.Max(0, m.TailMs));
            if (until > _holdUntilUtc) _holdUntilUtc = until;
        }
        Log.Debug("[EmiDesk] hold armed by {Moment} (untilReleased={Held}, holdMs={Hold}, tailMs={Tail})",
            momentId, m.HoldUntilReleased, m.HoldMs, m.TailMs);

        var pool = m.Pools?.FirstOrDefault();
        if (string.IsNullOrEmpty(pool)) return null;
        var row = Deal(pool!, ctx, 2, avoidRecent: false);
        if (row == null) return null;
        int ms = m.HoldMs > 0 ? m.HoldMs : (row.HoldMs > 0 ? row.HoldMs : 1400);
        return new LineDraw(row.Id!, pool!, string.Empty, row.Face ?? "-_-", row.Chain,
                            m.Priority, true, ms);
    }

    // ------------------------------------------------------------------ pools and bags

    private static bool IsCommon(string pool) => pool.StartsWith("common.", StringComparison.Ordinal);

    private string? ChoosePool(MomentDef m, IReadOnlyDictionary<string, object?> ctx, int ceiling)
    {
        var pools = m.Pools;
        if (pools == null || pools.Count == 0) return null;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string? specific = null;
            var commons = new List<string>();
            for (int i = 0; i < pools.Count; i++)
            {
                var p = pools[i];
                if (string.IsNullOrEmpty(p)) continue;
                if (!PoolWhenPasses(m, p, ctx)) continue;   // poolWhen drops a whole pool
                if (i == 0 && !IsCommon(p)) specific = p;
                else commons.Add(p);
            }

            bool sHas = specific != null && HasEligible(specific, ctx, ceiling);
            var cHas = commons.Where(p => HasEligible(p, ctx, ceiling)).ToList();

            if (sHas && cHas.Count > 0)
                return _rng.NextDouble() < m.Mix ? specific : cHas[_rng.Next(cHas.Count)];
            if (sHas) return specific;
            if (cHas.Count > 0) return cHas[_rng.Next(cHas.Count)];

            // Neither side has an unseen eligible line: reshuffle the bags and try once more.
            if (specific != null) Reshuffle(specific);
            foreach (var p in commons) Reshuffle(p);
        }
        return null;
    }

    private bool PoolWhenPasses(MomentDef m, string pool, IReadOnlyDictionary<string, object?> ctx)
    {
        if (m.PoolWhen == null) return true;
        if (!m.PoolWhen.TryGetValue(pool, out var gates) || gates == null) return true;
        return gates.All(g => GatePasses(g, ctx));
    }

    private List<string> Bag(string pool)
    {
        if (_bags.TryGetValue(pool, out var bag)) return bag;
        bag = BuildBag(pool);
        _bags[pool] = bag;
        return bag;
    }

    /// <summary>
    /// Rebuild a pool's bag from the persisted "already dealt" list, so a relaunch resumes the
    /// rotation rather than restarting it. An empty remainder reshuffles at once.
    /// </summary>
    private List<string> BuildBag(string pool)
    {
        var all = PoolIds(pool);
        var seen = SeenList(pool);
        var rest = all.Where(id => !seen.Contains(id)).ToList();
        if (rest.Count == 0) return ShuffledWithLastThreeHeldBack(pool, all, seen, clearSeen: true);
        Shuffle(rest);
        return rest;
    }

    private void Reshuffle(string pool)
    {
        var all = PoolIds(pool);
        _bags[pool] = ShuffledWithLastThreeHeldBack(pool, all, SeenList(pool), clearSeen: true);
    }

    /// <summary>
    /// The owner's reshuffle constraint (BRIEF 8): the last three ids dealt may not land in the
    /// first three positions of the new bag, so an exhausted pool never opens with a repeat.
    /// </summary>
    private List<string> ShuffledWithLastThreeHeldBack(string pool, List<string> all, List<string> seen, bool clearSeen)
    {
        var fresh = new List<string>(all);
        Shuffle(fresh);

        var lastThree = seen.Count <= 3 ? new List<string>(seen) : seen.GetRange(seen.Count - 3, 3);
        if (lastThree.Count > 0 && fresh.Count > 3)
        {
            for (int i = 0; i < 3 && i < fresh.Count; i++)
            {
                if (!lastThree.Contains(fresh[i], StringComparer.Ordinal)) continue;
                for (int j = 3; j < fresh.Count; j++)
                {
                    if (lastThree.Contains(fresh[j], StringComparer.Ordinal)) continue;
                    (fresh[i], fresh[j]) = (fresh[j], fresh[i]);
                    break;
                }
            }
        }

        if (clearSeen) ClearSeen(pool);
        return fresh;
    }

    private List<string> PoolIds(string pool)
    {
        if (_file?.Pools != null && _file.Pools.TryGetValue(pool, out var lines) && lines != null)
            return lines.Where(l => l?.Id != null).Select(l => l.Id!).ToList();
        return new List<string>();
    }

    /// <summary>The ids already dealt out of one pool's bag. Persisted, unless this is a test engine.</summary>
    private List<string> SeenList(string pool)
    {
        try
        {
            var map = _isoSeen ?? EmiState.Current.SeenByPool;
            if (map.TryGetValue(pool, out var list) && list != null) return list;
            var fresh = new List<string>();
            map[pool] = fresh;
            return fresh;
        }
        catch { return new List<string>(); }
    }

    /// <summary>Clear one pool's dealt list. Called when its bag reshuffles.</summary>
    private void ClearSeen(string pool)
    {
        try
        {
            if (_isoSeen != null) { _isoSeen[pool] = new List<string>(); return; }
            EmiState.Current.SeenByPool[pool] = new List<string>();
            EmiState.SaveSoon();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] bag reset failed for {Pool}", pool); }
    }

    /// <summary>Push an id onto the global recent ring (capped at 40, newest last).</summary>
    private void NoteRecent(string id)
    {
        if (_isoRecent == null) { EmiState.NoteLine(id); return; }
        _isoRecent.Remove(id);
        _isoRecent.Add(id);
        while (_isoRecent.Count > RecentRingSize) _isoRecent.RemoveAt(0);
    }

    private LineDef? Line(string pool, string id)
        => _byId.TryGetValue(pool, out var map) && map.TryGetValue(id, out var l) ? l : null;

    private bool HasEligible(string pool, IReadOnlyDictionary<string, object?> ctx, int ceiling)
    {
        var bag = Bag(pool);
        for (int i = 0; i < bag.Count; i++)
        {
            var l = Line(pool, bag[i]);
            if (l != null && Eligible(l, ctx, ceiling)) return true;
        }
        return false;
    }

    /// <summary>
    /// Deal one line out of a pool's bag. A line filtered out at draw time (spice, a failed gate,
    /// a missing token) is SKIPPED, not consumed: it stays in the bag for a fire whose ctx suits
    /// it. Common pools take a second pass so they can dodge the global recent ring while they
    /// still have a line outside it.
    /// </summary>
    private LineDef? Deal(string pool, IReadOnlyDictionary<string, object?> ctx, int ceiling, bool avoidRecent)
    {
        var bag = Bag(pool);
        if (bag.Count == 0) { Reshuffle(pool); bag = Bag(pool); }

        var hit = TakeFromBag(pool, bag, ctx, ceiling, avoidRecent);
        if (hit != null) return hit;

        // Nothing LEFT in the bag fits this context, which is an exhausted bag and not an empty
        // pool: the rows that would fit are exactly the ones already dealt. Reshuffle once and look
        // again. Without this, a pool whose tail is all gated rows goes permanently silent the
        // first time it drains, and the symptom (she just stops mentioning one thing) is invisible.
        Reshuffle(pool);
        return TakeFromBag(pool, Bag(pool), ctx, ceiling, avoidRecent);
    }

    private LineDef? TakeFromBag(string pool, List<string> bag,
        IReadOnlyDictionary<string, object?> ctx, int ceiling, bool avoidRecent)
    {
        for (int pass = avoidRecent ? 0 : 1; pass < 2; pass++)
        {
            for (int i = 0; i < bag.Count; i++)
            {
                var l = Line(pool, bag[i]);
                if (l == null || !Eligible(l, ctx, ceiling)) continue;
                if (pass == 0 && Recent().Contains(l.Id!, StringComparer.Ordinal)) continue;

                bag.RemoveAt(i);
                MarkDealt(pool, l.Id!);
                return l;
            }
        }
        return null;
    }

    private void MarkDealt(string pool, string id)
    {
        try
        {
            SeenList(pool).Add(id);
            if (!Isolated) EmiState.SaveSoon();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] seen bookkeeping failed for {Pool}", pool); }

        if (_bags.TryGetValue(pool, out var bag) && bag.Count == 0) Reshuffle(pool);
    }

    private List<string> Recent()
    {
        if (_isoRecent != null) return _isoRecent;
        try { return EmiState.Current.RecentIds; }
        catch { return new List<string>(); }
    }

    // ------------------------------------------------------------------ filtering

    private bool Eligible(LineDef l, IReadOnlyDictionary<string, object?> ctx, int ceiling)
    {
        if (l.Spice > ceiling) return false;
        if (l.Double && _doubleSpent) return false;
        if (l.When != null && !l.When.All(g => GatePasses(g, ctx))) return false;
        if (!string.IsNullOrEmpty(l.T) && Substitute(l.T, ctx) == null) return false;
        return true;
    }

    /// <summary>
    /// LINES-SCHEMA 5.4: <c>flag</c> (ctx truthy), <c>!flag</c> (missing or falsy),
    /// <c>key:value</c> (case-insensitive string equality).
    /// </summary>
    private static bool GatePasses(string? gate, IReadOnlyDictionary<string, object?> ctx)
    {
        if (string.IsNullOrWhiteSpace(gate)) return true;
        var g = gate.Trim();
        if (g[0] == '!') return !Truthy(ctx, g.Substring(1));
        int c = g.IndexOf(':');
        if (c > 0)
        {
            var key = g.Substring(0, c);
            var want = g.Substring(c + 1);
            var have = Str(ctx, key);
            return have != null && string.Equals(have, want, StringComparison.OrdinalIgnoreCase);
        }
        return Truthy(ctx, g);
    }

    private static bool Truthy(IReadOnlyDictionary<string, object?> ctx, string key)
    {
        if (!ctx.TryGetValue(key, out var v) || v == null) return false;
        return v switch
        {
            bool b => b,
            string s => s.Length > 0 && !string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) && s != "0",
            sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0,
            float or double or decimal => Convert.ToDouble(v, CultureInfo.InvariantCulture) != 0.0,
            _ => true
        };
    }

    private static string? Str(IReadOnlyDictionary<string, object?> ctx, string key)
    {
        if (!ctx.TryGetValue(key, out var v) || v == null) return null;
        var s = v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        return string.IsNullOrEmpty(s) ? null : s;
    }

    /// <summary>
    /// Token substitution (LINES-SCHEMA 5.3). Returns null when ANY token is missing or empty:
    /// every token pool carries plain siblings, so a skipped line is never a silent EMI. She never
    /// claims a number that is not real (MOMENTS 3.14), which is why there is no default value
    /// anywhere in here.
    /// </summary>
    public static string? Substitute(string? text, IReadOnlyDictionary<string, object?> ctx)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text!.IndexOf('{') < 0) return text;

        var sb = new System.Text.StringBuilder(text.Length + 16);
        int i = 0;
        while (i < text.Length)
        {
            char ch = text[i];
            if (ch != '{') { sb.Append(ch); i++; continue; }
            int close = text.IndexOf('}', i + 1);
            if (close < 0) { sb.Append(ch); i++; continue; }
            var key = text.Substring(i + 1, close - i - 1);
            var val = Str(ctx, key);
            if (val == null) return null;
            sb.Append(val);
            i = close + 1;
        }
        return sb.ToString();
    }

    private static int UserSpice()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return 2;
            return Math.Max(0, Math.Min(2, s.EmiDeskSpice));
        }
        catch { return 2; }
    }

    // ------------------------------------------------------------------ asks

    /// <summary>
    /// The cadence gates from BRIEF 7 that do not need the ctx: the 10 minute gap, the third
    /// summon, the ignore streak and bedtime. The situational half (no session, no video, not
    /// minimised) is the caller's, because only the caller can see the app.
    /// </summary>
    private bool AskGatesPass(MomentDef m)
    {
        try
        {
            if (m.AskOdds <= 0) return false;
            var s = App.Settings?.Current;
            if (s != null && !s.EmiDeskOffers) return false;
            if (BedtimeSet) return false;
            if (_ignoredAsksThisLaunch >= AskIgnoreLimit) return false;
            if ((DateTime.UtcNow - _lastAskUtc).TotalMilliseconds < AskGapMs) return false;
            if (SummonCount < AskMinSummons) return false;

            // The half only the app can see (LINES-SCHEMA 5.6): no session, no video, the avatar is
            // quiet, the app is not minimised, no offer already on the glass. No service, no offer.
            if (App.EmiDesk?.AskSituationOk() != true) return false;
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] ask gate probe failed");
            return false;
        }
    }

    private string? _lastAskId;

    private AskDraw? PickAsk(string momentId, IReadOnlyDictionary<string, object?> ctx, int ceiling)
    {
        var file = _file;
        if (file?.Asks == null) return null;

        var pool = new List<AskDef>();
        foreach (var a in file.Asks)
        {
            if (a?.Id == null || a.Moment == null) continue;
            if (!string.Equals(a.Moment, momentId, StringComparison.Ordinal)) continue;
            if (file.Deferred.Contains(a.Moment, StringComparer.Ordinal)) continue;
            if (a.Spice > ceiling) continue;
            if (a.Chips == null || a.Chips.Count != 2) continue;
            if (a.When != null && !a.When.All(g => GatePasses(g, ctx))) continue;
            if (string.Equals(a.Id, _lastAskId, StringComparison.Ordinal)) continue;
            if (Substitute(a.Q, ctx) == null) continue;

            var effect = Substitute(a.Effect ?? "none", ctx);
            if (effect == null) continue;
            if (!EmiOffers.EffectFeasible(effect)) continue;
            pool.Add(a);
        }
        if (pool.Count == 0) return null;

        var pick = pool[_rng.Next(pool.Count)];
        var q = Substitute(pick.Q, ctx) ?? string.Empty;
        var eff = Substitute(pick.Effect ?? "none", ctx) ?? "none";
        var effNo = pick.EffectNo == null ? null : Substitute(pick.EffectNo, ctx);

        var yes = Reaction(pick.Id! + ".yes", pick.Yes, ctx, "^_^");
        var no = Reaction(pick.Id! + ".no", pick.No, ctx, "._.");

        _lastAskId = pick.Id;
        return new AskDraw(pick.Id!, momentId, q, pick.Face ?? "0_0",
            new[] { pick.Chips![0], pick.Chips[1] }, yes, no, eff, effNo);
    }

    private LineDraw Reaction(string id, ReactionDef? r, IReadOnlyDictionary<string, object?> ctx, string fallbackFace)
    {
        if (r == null) return new LineDraw(id, "ask", string.Empty, fallbackFace, null, 2, false, 0);
        var t = Substitute(r.T, ctx) ?? r.T ?? string.Empty;
        if (r.Double) _ackDouble.Add(id);
        return new LineDraw(id, "ask", t, r.Face ?? fallbackFace, r.Chain, 2, false, 0);
    }

    // ------------------------------------------------------------------ limits

    /// <summary>
    /// LINES-SCHEMA 2.1. Increments the bucket on the way through: a fire that later loses the
    /// odds roll still spends its chance, which is what "1 per launch" is meant to mean.
    /// </summary>
    private bool TakeLimit(string momentId, MomentDef m, IReadOnlyDictionary<string, object?> ctx)
    {
        var lim = m.Limit;
        if (lim == null || lim.Max <= 0) return true;

        var target = Str(ctx, "target");

        // The extra persisted per-target once (suggestionIgnored3x), on top of the normal bucket.
        if (string.Equals(lim.PerTarget, "ever", StringComparison.OrdinalIgnoreCase) && target != null)
        {
            var pk = momentId + "|ptarget:" + target.ToLowerInvariant();
            if (Persisted(pk) >= 1) return false;
            BumpPersisted(pk);
        }

        var per = (lim.Per ?? "launch").ToLowerInvariant();
        string key;
        bool persisted;
        switch (per)
        {
            case "ever": key = momentId + "|ever"; persisted = true; break;
            case "day": key = momentId + "|day:" + DayKey(); persisted = true; break;
            case "night": key = momentId + "|night:" + DayKey(); persisted = true; break;
            case "featureday": key = momentId + "|featureDay:" + (target ?? "-").ToLowerInvariant() + ":" + DayKey(); persisted = true; break;
            case "version": key = momentId + "|version:" + AppVersionString(); persisted = true; break;
            case "target": key = momentId + "|target:" + (target ?? "-").ToLowerInvariant() + "|launch"; persisted = false; break;
            case "video": key = momentId + "|video"; persisted = false; break;
            case "run": key = momentId + "|run"; persisted = false; break;
            case "lockdown": key = momentId + "|lockdown"; persisted = false; break;
            case "rush": key = momentId + "|rush"; persisted = false; break;
            default: key = momentId + "|launch"; persisted = false; break;
        }

        if (persisted)
        {
            if (Persisted(key) >= lim.Max) return false;
            BumpPersisted(key);
            return true;
        }

        _volatileLimits.TryGetValue(key, out int n);
        if (n >= lim.Max) return false;
        _volatileLimits[key] = n + 1;
        return true;
    }

    private int Persisted(string key)
    {
        try
        {
            var map = _isoLimits ?? EmiState.Current.Limits;
            return map.TryGetValue(key, out int n) ? n : 0;
        }
        catch { return 0; }
    }

    private void BumpPersisted(string key)
    {
        try
        {
            var map = _isoLimits ?? EmiState.Current.Limits;
            map.TryGetValue(key, out int n);
            map[key] = n + 1;
            if (!Isolated) EmiState.SaveSoon();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] limit bump failed for {Key}", key); }
    }

    /// <summary>The local date, turning over at 06:00 (a "night" is 22:00..06:00 of one date).</summary>
    private static string DayKey()
    {
        var now = DateTime.Now;
        var d = now.Hour < 6 ? now.Date.AddDays(-1) : now.Date;
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string AppVersionString()
    {
        try { return ConditioningControlPanel.Services.UpdateService.AppVersion; }
        catch { return "0"; }
    }

    /// <summary>Drop yesterday's day / night / featureDay counters so the file cannot grow forever.</summary>
    private void PruneStaleLimits()
    {
        if (Isolated) return;
        try
        {
            var st = EmiState.Current;
            var today = DayKey();
            var dead = st.Limits.Keys.Where(k =>
                (k.Contains("|day:") || k.Contains("|night:") || k.Contains("|featureDay:"))
                && !k.EndsWith(today, StringComparison.Ordinal)).ToList();
            if (dead.Count == 0) return;
            foreach (var k in dead) st.Limits.Remove(k);
            EmiState.SaveSoon();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] limit prune failed"); }
    }

    // ------------------------------------------------------------------ small bits

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void LogOnce(string key, string template, params object[] args)
    {
        if (!_logged.Add(key)) return;
        Log.Debug(template, args);
    }

    /// <summary>Test seam: forget every bag, clock and ration and re-read the file.</summary>
    internal void ResetForTests()
    {
        lock (_gate)
        {
            _loadAttempted = false;
            _file = null;
            _byId.Clear();
            _bags.Clear();
            _spokeAt.Clear();
            _volatileLimits.Clear();
            _ackMoment.Clear();
            _ackDouble.Clear();
            _logged.Clear();
            _lastSpokeUtc = DateTime.MinValue;
            _lastAskUtc = DateTime.MinValue;
            _doubleSpent = false;
            _dorkSpent = false;
            _pendingAsk = null;
            _pendingAskMoment = null;
            _heldBy = null;
            _holdUntilUtc = DateTime.MinValue;
            _ignoredAsksThisLaunch = 0;
            _lastAskId = null;
        }
    }

    /// <summary>Test seam: deal straight out of one pool's bag, bypassing the moment pipeline.</summary>
    internal string? DealForTests(string pool, IReadOnlyDictionary<string, object?>? ctx = null, int ceiling = 2)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return Deal(pool, ctx ?? EmptyCtx, ceiling, avoidRecent: false)?.Id;
        }
    }

    /// <summary>Test seam: how many ids a pool holds.</summary>
    internal int PoolSizeForTests(string pool)
    {
        lock (_gate) { EnsureLoaded(); return PoolIds(pool).Count; }
    }

    /// <summary>Test seam: forget the launch's one double, so a second draw can spend it again.</summary>
    internal void ClearDoubleForTests()
    {
        lock (_gate) { _doubleSpent = false; }
    }

    /// <summary>Test seam: pretend nothing has been said, so the 45 s floor is open again.</summary>
    internal void ClearFloorForTests()
    {
        lock (_gate) { _lastSpokeUtc = DateTime.MinValue; _spokeAt.Clear(); }
    }

    // ============================================================================================
    // the file's shape. Unknown keys are ignored on purpose (LINES-SCHEMA 1, forward compatibility).
    // ============================================================================================

    private sealed class LinesFile
    {
        [JsonProperty("version")] public int Version { get; set; }
        [JsonProperty("moments")] public Dictionary<string, MomentDef>? Moments { get; set; }
        [JsonProperty("pools")] public Dictionary<string, List<LineDef>>? Pools { get; set; }
        [JsonProperty("asks")] public List<AskDef>? Asks { get; set; }
        [JsonProperty("dork")] public DorkDef? Dork { get; set; }
        [JsonProperty("deferred")] public List<string>? Deferred { get; set; }
    }

    private sealed class MomentDef
    {
        [JsonProperty("pools")] public List<string>? Pools { get; set; }
        [JsonProperty("odds")] public double Odds { get; set; }
        [JsonProperty("cooldownMs")] public int CooldownMs { get; set; }
        [JsonProperty("priority")] public int Priority { get; set; } = 2;
        [JsonProperty("mix")] public double Mix { get; set; } = 0.65;
        [JsonProperty("spiceCeiling")] public int SpiceCeiling { get; set; } = 2;
        [JsonProperty("hold")] public bool Hold { get; set; }
        [JsonProperty("askOdds")] public double AskOdds { get; set; }
        [JsonProperty("limit")] public LimitDef? Limit { get; set; }
        [JsonProperty("cooldownKey")] public string? CooldownKey { get; set; }
        [JsonProperty("poolWhen")] public Dictionary<string, List<string>>? PoolWhen { get; set; }
        [JsonProperty("holdMs")] public int HoldMs { get; set; }
        [JsonProperty("tailMs")] public int TailMs { get; set; }
        [JsonProperty("holdUntilReleased")] public bool HoldUntilReleased { get; set; }
    }

    private sealed class LimitDef
    {
        [JsonProperty("per")] public string? Per { get; set; }
        [JsonProperty("max")] public int Max { get; set; }
        [JsonProperty("perTarget")] public string? PerTarget { get; set; }
    }

    private sealed class LineDef
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("t")] public string? T { get; set; }
        [JsonProperty("face")] public string? Face { get; set; }
        [JsonProperty("spice")] public int Spice { get; set; }
        [JsonProperty("chain")] public string? Chain { get; set; }
        [JsonProperty("double")] public bool Double { get; set; }
        [JsonProperty("when")] public List<string>? When { get; set; }
        [JsonProperty("holdMs")] public int HoldMs { get; set; }
    }

    private sealed class AskDef
    {
        [JsonProperty("id")] public string? Id { get; set; }
        [JsonProperty("moment")] public string? Moment { get; set; }
        [JsonProperty("when")] public List<string>? When { get; set; }
        [JsonProperty("q")] public string? Q { get; set; }
        [JsonProperty("face")] public string? Face { get; set; }
        [JsonProperty("chips")] public List<string>? Chips { get; set; }
        [JsonProperty("yes")] public ReactionDef? Yes { get; set; }
        [JsonProperty("no")] public ReactionDef? No { get; set; }
        [JsonProperty("effect")] public string? Effect { get; set; }
        [JsonProperty("effectNo")] public string? EffectNo { get; set; }
        [JsonProperty("spice")] public int Spice { get; set; }
    }

    private sealed class ReactionDef
    {
        [JsonProperty("t")] public string? T { get; set; }
        [JsonProperty("face")] public string? Face { get; set; }
        [JsonProperty("chain")] public string? Chain { get; set; }
        [JsonProperty("double")] public bool Double { get; set; }
    }

    private sealed class DorkDef
    {
        [JsonProperty("pool")] public string? Pool { get; set; }
        [JsonProperty("odds")] public double Odds { get; set; }
        [JsonProperty("limit")] public LimitDef? Limit { get; set; }
    }
}
