using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The DtRH browser game's window onto <see cref="ChaosMetaState"/>. chaos_meta.json stays
/// exclusively C#-owned: the page holds a read snapshot (rev-numbered) and sends COMMANDS,
/// which are shape-validated here, applied, debounce-saved, and answered with a fresh
/// snapshot. Commands compose with the C#-side mutations (run awards, reveal sync) that a
/// JS-pushed state blob would silently clobber - and the vocabulary becomes the v2 online
/// API verbatim.
///
/// Validation is integrity, not anti-cheat (offline, user's own save): unknown ops are
/// logged + ignored, economy ops can't drive a balance negative, seen-flags are one-way.
///
/// Test mode (--dtrh-m2test) operates on a CLONE persisted to chaos_meta.test.json so
/// automated bridge exercises never touch the real save.
/// </summary>
internal sealed class DtrhMetaBridge
{
    private readonly bool _testMode;
    private readonly ChaosMetaState? _testState;
    private DispatcherTimer? _saveDebounce;
    private readonly Action<object> _broadcast;   // posts a {type:'meta'} message to the page

    private static readonly JsonSerializerSettings SnapshotJson = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    public int Rev { get; private set; }

    private ChaosMetaState S => _testMode ? _testState! : ChaosMeta.State;

    public DtrhMetaBridge(bool testMode, Action<object> broadcast)
    {
        _testMode = testMode;
        _broadcast = broadcast;
        if (testMode)
        {
            // Deep clone via serialize/deserialize - the real state is never handed out.
            var json = JsonConvert.SerializeObject(ChaosMeta.State);
            _testState = JsonConvert.DeserializeObject<ChaosMetaState>(json) ?? new ChaosMetaState();
        }
    }

    public bool TestMode => _testMode;

    /// <summary>The {type:'meta'} message body: camelCase state snapshot + revision.</summary>
    public object SnapshotMessage() => new
    {
        type = "meta",
        rev = Rev,
        state = JObject.Parse(JsonConvert.SerializeObject(S, SnapshotJson)),
        testMode = _testMode,
    };

    /// <summary>Bank a completed web run through the SAME formula/banking as the WPF game
    /// (ChaosMeta.AwardRunRewards overload). Returns sparks earned.</summary>
    public int AwardRun(in ChaosMeta.ChaosRunRewardInput input)
    {
        int sparks;
        if (_testMode)
        {
            // Mirror the formula against the clone so the real save never moves.
            sparks = ComputeSparksOnly(input);
            _testState!.Sparks += sparks;
            _testState.RunsCompleted += 1;
            _testState.BestScore = Math.Max(_testState.BestScore, (long)input.Score);
            SaveNow();
        }
        else
        {
            sparks = ChaosMeta.AwardRunRewards(input);   // persists internally
        }
        Bump();
        return sparks;
    }

    /// <summary>Handle a {type:'meta-command'} message. Returns true if a mutation was applied.</summary>
    public bool Handle(JObject o)
    {
        var op = (string?)o["op"];
        bool applied = false;
        try
        {
            switch (op)
            {
                case "purchase-upgrade":
                {
                    var id = RequireId(o); int cost = Cost(o);
                    if (id != null && S.Sparks >= cost && !S.PurchasedUpgrades.Contains(id))
                    {
                        S.Sparks -= cost;
                        S.PurchasedUpgrades.Add(id);
                        // Mirrors ChaosMeta.TryPurchase: the Inescapable door opens at purchase time.
                        if (id == "extreme_tier") S.ExtremeUnlocked = true;
                        applied = true;
                    }
                    break;
                }
                case "toggle-upgrade":
                {
                    var id = RequireId(o); bool on = (bool?)o["on"] ?? true;
                    if (id != null) { applied = on ? S.DisabledUpgrades.Remove(id) : S.DisabledUpgrades.Add(id); }
                    break;
                }
                case "purchase-dial":
                {
                    // Buy back an options-panel dial (UNLOCK_LADDER in engine/settings.js).
                    // Cost is client-supplied (same trust model as purchase-upgrade); the
                    // two feral dials are rank-gated here so the page can't skip the floor.
                    var id = RequireId(o); int cost = Cost(o);
                    if (id == null || S.PurchasedDials.Contains(id)) break;
                    bool rankOk = (id != "hydra" && id != "glitchTimer") || ChaosMeta.RankIndex >= ChaosRank.Entranced;
                    if (rankOk && S.Sparks >= cost)
                    {
                        S.Sparks -= cost;
                        S.PurchasedDials.Add(id);
                        applied = true;
                    }
                    break;
                }
                case "set-lifetime-boon":
                {
                    var id = RequireId(o);
                    if (id == null) break;
                    if (o["level"] != null)
                    {
                        int level = (int?)o["level"] ?? 0;
                        int cost = Cost(o);
                        int cur = S.LifetimeBoonLevels.TryGetValue(id, out var l) ? l : 0;
                        // levels only climb, one paid step at a time
                        if (level == cur + 1 && S.Sparks >= cost)
                        { S.Sparks -= cost; S.LifetimeBoonLevels[id] = level; applied = true; }
                    }
                    if (o["active"] != null)
                    {
                        bool active = (bool?)o["active"] ?? false;
                        applied |= active ? S.ActiveLifetimeBoons.Add(id) : S.ActiveLifetimeBoons.Remove(id);
                    }
                    break;
                }
                case "equip-boon":
                {
                    S.EquippedStartBoon = string.IsNullOrEmpty((string?)o["id"]) ? null : (string?)o["id"];
                    applied = true;
                    break;
                }
                case "add-gold":
                {
                    int amount = (int?)o["amount"] ?? 0;
                    if (amount > 0 && amount <= 100000) { S.Gold += amount; applied = true; }
                    break;
                }
                case "spend-gold":
                {
                    int amount = (int?)o["amount"] ?? 0;
                    if (amount > 0 && S.Gold >= amount) { S.Gold -= amount; applied = true; }
                    break;
                }
                case "buy-pocket":
                {
                    var kind = (string?)o["kind"]; int cost = Cost(o);
                    if (S.Gold < cost) break;
                    if (kind == "toy" && S.ToyPockets < ChaosMeta.MAX_POCKETS_PER_CATEGORY)
                    { S.Gold -= cost; S.ToyPockets++; applied = true; }
                    else if (kind == "accessory" && S.AccessoryPockets < ChaosMeta.MAX_POCKETS_PER_CATEGORY)
                    { S.Gold -= cost; S.AccessoryPockets++; applied = true; }
                    break;
                }
                case "bench-purchase":
                {
                    var id = RequireId(o); int cost = Cost(o);
                    if (id != null && S.Gold >= cost && !S.BenchPurchases.Contains(id))
                    { S.Gold -= cost; S.BenchPurchases.Add(id); applied = true; }
                    break;
                }
                case "bench-buy":
                {
                    // The full WPF BenchBuy_Click semantics in one op: spend gold (or HER GIFT -
                    // the one-time cover of a short balance on the first toy pocket), record the
                    // purchase, and apply the pocket effect for the pocket rows. The page owns the
                    // rank/reveal gating UI; affordability + one-time-ness are enforced here.
                    var id = RequireId(o); int cost = Cost(o);
                    if (id == null || S.BenchPurchases.Contains(id)) break;
                    bool paid = false, gift = false;
                    if (S.Gold >= cost) { S.Gold -= cost; paid = true; }
                    else if (id == BenchIds.ToyPocket1 && !S.GiftGiven)
                    { S.GiftGiven = true; S.Gold = 0; paid = true; gift = true; }
                    if (!paid) break;
                    S.BenchPurchases.Add(id);
                    if (id is BenchIds.ToyPocket1 or BenchIds.ToyPocket2)
                        S.ToyPockets = Math.Min(S.ToyPockets + 1, ChaosMeta.MAX_POCKETS_PER_CATEGORY);
                    else if (id is BenchIds.AccPocket1 or BenchIds.AccPocket2)
                        S.AccessoryPockets = Math.Min(S.AccessoryPockets + 1, ChaosMeta.MAX_POCKETS_PER_CATEGORY);
                    if (gift && !_testMode) { try { App.Bark?.NotifyChaosGiftGiven(); } catch { } }
                    applied = true;
                    break;
                }
                case "first-time":
                {
                    // ChaosFirstTimes.TryAward over the bridge: a one-time drops bonus, the
                    // amount table C#-owned so the page can't invent values. Bark included
                    // (mirrors TryAward), skipped in test mode.
                    var id = RequireId(o);
                    if (id == null || !ChaosFirstTimes.Amounts.TryGetValue(id, out int amount)) break;
                    S.FirstTimesAwarded ??= new HashSet<string>();
                    if (!S.FirstTimesAwarded.Add(id)) break;
                    S.Sparks += amount;
                    if (!_testMode) { try { App.Bark?.NotifyChaosFirstTime(id); } catch { } }
                    App.Logger?.Information("DtrhMetaBridge: first-time {Id} +{Amount} drops", id, amount);
                    applied = true;
                    break;
                }
                case "lesson-progress":
                {
                    var id = RequireId(o);
                    if (id == null) break;
                    long value = (long?)o["value"] ?? 0;
                    long cur = S.LessonProgress.TryGetValue(id, out var v) ? v : 0;
                    if (value > cur) { S.LessonProgress[id] = value; applied = true; }
                    if ((bool?)o["complete"] == true) applied |= S.LessonsComplete.Add(id);
                    break;
                }
                case "set-flag":
                {
                    // One-way: any bool property may only ever go false -> true (they are all
                    // seen-once / earned-once semantics in ChaosMetaState).
                    var key = (string?)o["key"];
                    var prop = FlagProp(key);
                    if (prop != null && prop.GetValue(S) is false) { prop.SetValue(S, true); applied = true; }
                    break;
                }
                case "add-to-set":
                {
                    var prop = SetProp((string?)o["set"]);
                    var id = RequireId(o);
                    if (prop?.GetValue(S) is HashSet<string> set && id != null) applied = set.Add(id);
                    break;
                }
                case "remove-from-set":
                {
                    // Only reveal-pending is legitimately removable (pending -> seen transition).
                    var setName = (string?)o["set"];
                    var id = RequireId(o);
                    if (setName == "pendingReveals" && id != null) applied = S.PendingReveals.Remove(id);
                    break;
                }
                case "set-num":
                {
                    var key = (string?)o["key"];
                    long value = (long?)o["value"] ?? 0;
                    if (key == "lastRankSeen" && value > S.LastRankSeen && value < 64)
                    { S.LastRankSeen = (int)value; applied = true; }
                    break;
                }
                case "map-set":
                {
                    var id = RequireId(o);
                    long value = (long?)o["value"] ?? 0;
                    if ((string?)o["map"] == "narrativeCooldownEnds" && id != null)
                    { S.NarrativeCooldownEnds[id] = value; applied = true; }
                    break;
                }
                case "add-channel-seconds":
                {
                    double sec = (double?)o["seconds"] ?? 0;
                    if (sec > 0 && sec < 36000) { S.TotalChannelSeconds += sec; applied = true; }
                    break;
                }
                default:
                    App.Logger?.Warning("DtrhMetaBridge: unknown op '{Op}' ignored", op);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhMetaBridge: op '{Op}' failed: {E}", op, ex.Message);
        }

        if (applied) { Bump(); DebounceSave(); }
        else if (op != null) App.Logger?.Debug("DtrhMetaBridge: op '{Op}' rejected (shape/affordability)", op);
        return applied;
    }

    /// <summary>Push a fresh snapshot to the page after C#-side mutations that bypass
    /// <see cref="Handle"/> (run-end reveal sync, award banking).</summary>
    public void Rebroadcast() => Bump();

    /// <summary>Flush any pending debounced save immediately (run end / exit).</summary>
    public void FlushSave()
    {
        if (_saveDebounce == null) return;
        _saveDebounce.Stop();
        _saveDebounce = null;
        SaveNow();
    }

    // -------- internals --------

    private void Bump()
    {
        Rev++;
        try { _broadcast(SnapshotMessage()); } catch { }
    }

    private void DebounceSave()
    {
        if (_saveDebounce == null)
        {
            _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveDebounce.Tick += (_, _) => FlushSave();
        }
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    private void SaveNow()
    {
        try
        {
            if (_testMode)
            {
                var path = Path.Combine(App.UserDataPath, "chaos_meta.test.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(_testState, Formatting.Indented));
            }
            else
            {
                ChaosMeta.Save();
            }
        }
        catch (Exception ex) { App.Logger?.Warning("DtrhMetaBridge.SaveNow: {E}", ex.Message); }
    }

    private int ComputeSparksOnly(in ChaosMeta.ChaosRunRewardInput run)
    {
        double durationMin = Math.Max(0, run.RunDurationSec) / 60.0;
        double completionBonus = 35.0 * run.DifficultyMult * Math.Min(1.0, durationMin / 3.0);
        double scorePart = 1.5 * Math.Sqrt(Math.Max(0, run.Score));
        int sparks = (int)Math.Round((scorePart + completionBonus) * run.SparkGainMult);
        sparks += (int)Math.Max(0, run.TrickleDrops);
        if (run.DripFeedMaxed) sparks = (int)Math.Round(sparks * 1.10);
        return Math.Max(0, sparks);
    }

    private static string? RequireId(JObject o)
    {
        var id = (string?)o["id"];
        return string.IsNullOrWhiteSpace(id) || id.Length > 128 ? null : id;
    }

    private static int Cost(JObject o) => Math.Max(0, (int?)o["cost"] ?? 0);

    private static PropertyInfo? FlagProp(string? camelKey)
    {
        if (string.IsNullOrWhiteSpace(camelKey)) return null;
        var pascal = char.ToUpperInvariant(camelKey[0]) + camelKey[1..];
        var prop = typeof(ChaosMetaState).GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance);
        return prop != null && prop.PropertyType == typeof(bool) && prop.CanWrite ? prop : null;
    }

    private static PropertyInfo? SetProp(string? camelKey)
    {
        if (string.IsNullOrWhiteSpace(camelKey)) return null;
        var pascal = char.ToUpperInvariant(camelKey[0]) + camelKey[1..];
        var prop = typeof(ChaosMetaState).GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance);
        return prop != null && prop.PropertyType == typeof(HashSet<string>) ? prop : null;
    }
}
