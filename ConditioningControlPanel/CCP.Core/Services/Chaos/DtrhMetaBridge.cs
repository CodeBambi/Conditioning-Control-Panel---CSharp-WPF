using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

// Ported (byte-for-byte semantics) from ConditioningControlPanel/Services/Chaos/DtrhMetaBridge.cs
// in the WPF head. The WPF file is an internal helper bound to ChaosMeta.State / ChaosMeta.Save() /
// ChaosMeta.RankIndex / App.Bark / App.Logger / App.UserDataPath; here it is a public sealed
// INSTANCE service injecting the Core seams (IChaosMetaStore, IAppEnvironment,
// ILogger<DtrhMetaBridge>, IBarkService). The observable behavior — every op, clamp, formula,
// one-way-flag guard, and whitelist — is unchanged. Cited WPF line numbers (branch
// feat/crossplatform) are preserved in comments where the original carried them. The completed-run
// banking (AwardRun real path) is inlined from the frozen WPF ChaosUpgrades.cs:522-548 using the
// Core ChaosEconomy.SparkReward formula.

namespace ConditioningControlPanel.Core.Services.Chaos;

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
public sealed class DtrhMetaBridge
{
    private readonly IChaosMetaStore _store;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<DtrhMetaBridge> _logger;
    private readonly IBarkService? _bark;
    private readonly bool _testMode;
    private readonly ChaosMetaState? _testState;
    private readonly Action<object> _broadcast;   // posts a {type:'meta'} message to the page

    private Timer? _saveDebounce;                 // WPF: DispatcherTimer (UI-thread, 500ms); here a restartable one-shot Timer
    private readonly object _debounceLock = new();

    private static readonly JsonSerializerSettings SnapshotJson = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    public int Rev { get; private set; }

    private ChaosMetaState S => _testMode ? _testState! : _store.State;   // WPF: ChaosMeta.State

    public DtrhMetaBridge(IChaosMetaStore store, IAppEnvironment environment,
                          ILogger<DtrhMetaBridge> logger, Action<object> broadcast,
                          bool testMode = false, IBarkService? bark = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _broadcast = broadcast ?? throw new ArgumentNullException(nameof(broadcast));
        _testMode = testMode;
        _bark = bark;
        if (testMode)
        {
            // Deep clone via serialize/deserialize - the real state is never handed out.
            var json = JsonConvert.SerializeObject(_store.State);
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
    /// (the frozen ChaosUpgrades.cs:522-548 AwardRunRewards banking, via the Core
    /// <see cref="ChaosEconomy.SparkReward"/> formula). Returns sparks earned.</summary>
    public int AwardRun(in ChaosRunRewardInput input)
    {
        int sparks;
        if (_testMode)
        {
            // Mirror the formula against the clone so the real save never moves. The test path
            // deliberately OMITS the first-fall bonus (and the BestCombo/TotalDefused/TotalRunSeconds
            // bumps) so a deterministic smoke run can't perturb lifetime stats.
            sparks = ComputeSparksOnly(input);
            _testState!.Sparks += sparks;
            _testState.RunsCompleted += 1;
            _testState.BestScore = Math.Max(_testState.BestScore, (long)input.Score);
            SaveNow();
        }
        else
        {
            // Frozen banking from WPF ChaosUpgrades.cs:522-548 (AwardRunRewards), Core formula:
            bool firstFall = S.RunsCompleted == 0;   // WPF ChaosUpgrades.cs:513 (guarded so it applies once ever)
            sparks = ChaosEconomy.SparkReward(input.Score, input.DifficultyMult, input.RunDurationSec,
                            input.SparkGainMult, (long)Math.Max(0, input.TrickleDrops), input.DripFeedMaxed, firstFall);
            S.Sparks += Math.Max(0, sparks);          // WPF ChaosUpgrades.cs:540
            S.RunsCompleted += 1;                      // WPF ChaosUpgrades.cs:541
            S.BestScore = Math.Max(S.BestScore, (long)input.Score);   // WPF ChaosUpgrades.cs:542
            S.BestCombo = Math.Max(S.BestCombo, input.BestCombo);     // WPF ChaosUpgrades.cs:543
            S.TotalDefused += input.Defused;           // WPF ChaosUpgrades.cs:544
            S.TotalRunSeconds += Math.Max(0, input.ElapsedSec);       // WPF ChaosUpgrades.cs:545
            _store.Save();                             // WPF ChaosUpgrades.cs:546 (ChaosMetaStore.Save)
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
                    // Gold cutover: dials cost GOLD (gold unlocks, drops level). Cost is
                    // client-supplied (same trust model as purchase-upgrade); the two feral
                    // dials are rank-gated here so the page can't skip the floor. HER GIFT
                    // (the one-time short-balance cover, repointed from the retired first
                    // toy pocket) lands on your very first dial.
                    var id = RequireId(o); int cost = Cost(o);
                    if (id == null || S.PurchasedDials.Contains(id)) break;
                    bool rankOk = (id != "hydra" && id != "glitchTimer") || _store.RankIndex >= (int)ChaosRank.Entranced;
                    if (!rankOk) break;
                    if (S.Gold >= cost)
                    {
                        S.Gold -= cost;
                        S.PurchasedDials.Add(id);
                        applied = true;
                    }
                    else if (!S.GiftGiven && S.PurchasedDials.Count == 0)
                    {
                        S.GiftGiven = true;
                        S.Gold = 0;
                        S.PurchasedDials.Add(id);
                        if (!_testMode) { try { _bark?.NotifyChaosGiftGiven(); } catch { } }
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
                case "buy-consumable-slot":
                {
                    // Grab-in-the-tube rework: sew another consumable HUD slot with Sparks (the
                    // dollhouse meta-purchase). Starts at 1, capped at MAX_CONSUMABLE_SLOTS.
                    int cost = Cost(o);
                    if (S.Sparks >= cost && S.ConsumableSlots < ChaosMetaState.MaxConsumableSlots)
                    { S.Sparks -= cost; S.ConsumableSlots++; applied = true; }
                    break;
                }
                case "bench-buy":
                {
                    // Gold cutover: the bench is gone; this op survives only for the three
                    // console extras (stats / diary / starting mantra) sold at the DIALS
                    // console. Whitelisted so a stale page can't resurrect pocket ids.
                    // Her gift moved to purchase-dial (the first dial), so gold-or-nothing here.
                    var id = RequireId(o); int cost = Cost(o);
                    if (id is not (BenchIds.StartMantra or BenchIds.Diary or BenchIds.StatsPanel)) break;
                    if (S.BenchPurchases.Contains(id) || S.Gold < cost) break;
                    S.Gold -= cost;
                    S.BenchPurchases.Add(id);
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
                    if (!_testMode) { try { _bark?.NotifyChaosFirstTime(id); } catch { } }
                    _logger.LogInformation("DtrhMetaBridge: first-time {Id} +{Amount} drops", id, amount);
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
                    // the Cheshire tutorial arc: climbs only (0..16), reset-onboarding rewinds it
                    else if (key == "tutorialStage" && value > S.TutorialStage && value <= 16)   // WPF DtrhMetaBridge.cs:260-262
                    { S.TutorialStage = (int)value; applied = true; }
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
                case "reset-onboarding":
                {
                    // Full FTUE replay WITHOUT touching progression. Re-arms every seen-once
                    // teach/debut/guide flag, the non-boon discovery ledger (lesson cards), and
                    // the dollhouse station flash; arms the one-shot scripted-classroom deal.
                    // Rewards stay: FirstTimesAwarded/LessonProgress/GiftGiven are grants, not lessons.
                    foreach (var name in OnboardingFlagNames)
                        typeof(ChaosMetaState).GetProperty(name)?.SetValue(S, false);
                    S.DiscoveredCodexIds.RemoveWhere(id => !id.StartsWith("boon:", StringComparison.Ordinal));
                    S.BubbleHintsLearned.Clear();
                    S.SeenReveals.Remove("dollhouse");
                    S.PendingReveals.Remove("dollhouse");
                    // the Cheshire arc: rewind the stage + purge her namespaced once-lines
                    // and pooled cooldowns (other narrative rows stay untouched)
                    S.TutorialStage = 0;   // WPF DtrhMetaBridge.cs:291-296
                    S.SeenNarrativeLines.RemoveWhere(id => id.StartsWith("cheshire:", StringComparison.Ordinal));
                    foreach (var k in S.NarrativeCooldownEnds.Keys.Where(k => k.StartsWith("cheshire:", StringComparison.Ordinal)).ToList())
                        S.NarrativeCooldownEnds.Remove(k);
                    S.ForceScriptedRun = true;
                    applied = true;
                    break;
                }
                default:
                    _logger.LogWarning("DtrhMetaBridge: unknown op '{Op}' ignored", op);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("DtrhMetaBridge: op '{Op}' failed: {E}", op, ex.Message);
        }

        if (applied) { Bump(); DebounceSave(); }
        else if (op != null) _logger.LogDebug("DtrhMetaBridge: op '{Op}' rejected (shape/affordability)", op);
        return applied;
    }

    /// <summary>Push a fresh snapshot to the page after C#-side mutations that bypass
    /// <see cref="Handle"/> (run-end reveal sync, award banking).</summary>
    public void Rebroadcast() => Bump();

    /// <summary>Flush any pending debounced save immediately (run end / exit).</summary>
    public void FlushSave()
    {
        Timer? timer;
        lock (_debounceLock)
        {
            timer = _saveDebounce;
            _saveDebounce = null;
        }
        if (timer == null) return;
        try { timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); } catch { }
        try { timer.Dispose(); } catch { }
        SaveNow();
    }

    /// <summary>Force any pending debounced write synchronously (deterministic test hook).</summary>
    internal void FlushForTest() => FlushSave();

    // -------- internals --------

    private void Bump()
    {
        Rev++;
        try { _broadcast(SnapshotMessage()); } catch { }
    }

    private void DebounceSave()
    {
        // WPF: a 500ms DispatcherTimer restarted on each mutation; here a restartable one-shot
        // System.Threading.Timer (Core has no DispatcherTimer). Same observable semantics: each
        // applied mutation (re)arms a 500ms one-shot; on fire it calls FlushSave(). Off-thread
        // best-effort save matches the S1 telemetry-store background-write model.
        const int debounceMs = 500;
        lock (_debounceLock)
        {
            if (_saveDebounce == null)
                _saveDebounce = new Timer(_ => FlushSave(), null, TimeSpan.FromMilliseconds(debounceMs), Timeout.InfiniteTimeSpan);
            else
                _saveDebounce.Change(TimeSpan.FromMilliseconds(debounceMs), Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveNow()
    {
        try
        {
            if (_testMode)
            {
                var path = Path.Combine(_environment.UserDataPath, "chaos_meta.test.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(_testState, Formatting.Indented));
            }
            else
            {
                _store.Save();
            }
        }
        catch (Exception ex) { _logger.LogWarning("DtrhMetaBridge.SaveNow: {E}", ex.Message); }
    }

    private int ComputeSparksOnly(in ChaosRunRewardInput run)
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

    /// <summary>Seen-once bools re-armed by reset-onboarding. Teaching/guide state only —
    /// never anything that grants currency or records progression.</summary>
    private static readonly string[] OnboardingFlagNames =
    {
        // hub / FTUE guides
        "SeenWarrenWelcome", "SeenFirstReturn", "SeenIntroGuide", "SeenDollhouse",
        // verb / system teaches
        "SeenDefuseTutorial", "SeenFocusTip", "SeenRippleTeach", "SeenHeatTeach", "SeenGoldFirst",
        // once-ever teaching barks
        "SeenBarkDefuseFirst", "SeenBarkDefuseNoFocus", "SeenBarkDefuseRelease", "SeenBarkClickDetonate",
        // behavioral-bubble debuts (extended-trance solo introductions)
        "SeenEcho", "SeenChaperone", "SeenTease", "SeenBound", "SeenBrittle", "SeenBraindrain",
        // happy-path one-shots (defanged sin demo, duo gold card, skip debut)
        "SeenFirstSin", "SeenDuoDemo", "SeenSkipDebut",
    };

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
