using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>The rank spine (ChaosRanks.cs:10-29): thresholds over lifetime completed
/// descents; the index doubles as the enum value (Curious 0 … Claimed 5).</summary>
public static class DtrhRanks
{
    public static readonly int[] Thresholds = [0, 3, 10, 25, 50, 100];

    private static readonly string[] Names = ["Curious", "Tempted", "Slipping", "Entranced", "Devoted", "Claimed"];

    public static int For(int runsCompleted)
    {
        var rank = 0;
        for (var i = Thresholds.Length - 1; i >= 0; i--)
        {
            if (runsCompleted >= Thresholds[i])
            {
                rank = i;
                break;
            }
        }

        return rank;
    }

    /// <summary>The enum-name string WPF ships in payout-result (rankUp?.ToString()).</summary>
    public static string Name(int rank) => Names[Math.Clamp(rank, 0, Names.Length - 1)];
}

/// <summary>
/// The meta-progression engine (b4; dtrh-admission.md §7) — the greenfield port of WPF
/// DtrhMetaBridge + the payout/request-run halves of DtrhHostService, riding the b2 slot
/// documents on SP-005 machinery (NO parallel save file, NO schema bump — packet honesty
/// framing a). The page holds a read snapshot (rev-numbered) and sends COMMANDS; every op
/// is shape-validated here, applied through the slot store (atomic, journaled,
/// quarantine-honest), debounce-saved, and answered with a fresh snapshot.
///
/// Validation is integrity, not anti-cheat (WPF DtrhMetaBridge.cs:13-21): unknown ops are
/// logged + ignored, economy ops can't drive a balance negative, seen-flags are one-way.
/// Test mode (--dtrh-m2test) operates on a state held in memory — WPF persists the
/// clone to chaos_meta.test.json but NOTHING ever reads it back (DtrhMetaBridge.cs:427-444),
/// so the greenfield keeps the clone memory-only (recorded in SP-026 record Step 1).
/// SP-057: the in-memory state starts from the DECLARED fixture
/// (<see cref="DtrhM2TestFixture"/>) — never a clone of the live slot document.
/// </summary>
public sealed class DtrhMeta
{
    /// <summary>MAX_CONSUMABLE_SLOTS (ChaosUpgrades.cs:381 — must match catalog.js).</summary>
    public const int MaxConsumableSlots = 5;

    /// <summary>FIRST_FALL_BONUS (ChaosUpgrades.cs:106): +25 drops the first time
    /// RunsCompleted goes 0→1 (real mode only — the test mirror omits it, DtrhMetaBridge.cs:446-456).</summary>
    public const int FirstFallBonus = 25;

    // ---- whitelists (ChaosCraftingIds.cs:18-37, BenchIds ChaosRevealService.cs:31-40,
    // ChaosFirstTimes.Amounts ChaosLessons.cs:152-163) ----
    private static readonly HashSet<string> MaterialIds = ["silicone", "makeup", "pills", "lace", "lube", "chrome"];

    private static readonly HashSet<string> RecipeIds =
    [
        "slippery_slope", "rubber_up", "skeleton_key", "locked_collar", "the_cage",
        "ragdoll", "porcelain", "the_ballerina", "the_compact", "the_loom",
        "extensions", "lipstick", "the_hourglass", "the_timepiece", "the_ring_box",
        "the_corset", "sugar_cube", "the_shot", "headphones", "the_padlock",
        "the_pink_heart",
    ];

    private static readonly HashSet<string> ConsumableIds = ["slippery_slope", "rubber_up", "sugar_cube"];

    private static readonly HashSet<string> BenchIds = ["start_mantra", "diary", "stats_panel"];

    private static readonly Dictionary<string, int> FirstTimeAmounts = new()
    {
        ["first_taste"] = 5, ["first_snap"] = 10, ["first_whisper"] = 10,
        ["first_yes"] = 15, ["first_play"] = 15,
    };

    /// <summary>Seen-once bools re-armed by reset-onboarding (DtrhMetaBridge.cs:466-482,
    /// verbatim — teaching/guide state only, never currency or progression).</summary>
    private static readonly string[] OnboardingFlagNames =
    [
        "SeenWarrenWelcome", "SeenFirstReturn", "SeenIntroGuide", "SeenDollhouse",
        "SeenDefuseTutorial", "SeenFocusTip", "SeenRippleTeach", "SeenHeatTeach", "SeenGoldFirst",
        "SeenBarkDefuseFirst", "SeenBarkDefuseNoFocus", "SeenBarkDefuseRelease", "SeenBarkClickDetonate",
        "SeenEcho", "SeenChaperone", "SeenTease", "SeenBound", "SeenBrittle", "SeenBraindrain",
        "SeenFirstSin", "SeenDuoDemo", "SeenSkipDebut",
        "SeenBoudoirIntro",
    ];

    // WPF resolves set-flag/add-to-set by REFLECTION over any public bool / HashSet<string>
    // member (DtrhMetaBridge.cs:484-498). The greenfield ports the same surface as explicit
    // maps — identical semantics, no reflection.
    private static readonly Dictionary<string, Func<DtrhSlotDocument, bool>> FlagGet = new();
    private static readonly Dictionary<string, Action<DtrhSlotDocument, bool>> FlagSet = new();
    private static readonly Dictionary<string, Func<DtrhSlotDocument, HashSet<string>>> SetMap = new();

    static DtrhMeta()
    {
        void Flag(string camel, Func<DtrhSlotDocument, bool> get, Action<DtrhSlotDocument, bool> set)
        {
            FlagGet[camel] = get;
            FlagSet[camel] = set;
        }

        // Every public writable bool member (WPF FlagProp surface).
        Flag("extremeUnlocked", d => d.ExtremeUnlocked, (d, v) => d.ExtremeUnlocked = v);
        Flag("giftGiven", d => d.GiftGiven, (d, v) => d.GiftGiven = v);
        Flag("forceScriptedRun", d => d.ForceScriptedRun, (d, v) => d.ForceScriptedRun = v);
        Flag("denialArmed", d => d.DenialArmed, (d, v) => d.DenialArmed = v);
        Flag("seenDefuseTutorial", d => d.SeenDefuseTutorial, (d, v) => d.SeenDefuseTutorial = v);
        Flag("seenBarkDefuseFirst", d => d.SeenBarkDefuseFirst, (d, v) => d.SeenBarkDefuseFirst = v);
        Flag("seenBarkDefuseNoFocus", d => d.SeenBarkDefuseNoFocus, (d, v) => d.SeenBarkDefuseNoFocus = v);
        Flag("seenBarkDefuseRelease", d => d.SeenBarkDefuseRelease, (d, v) => d.SeenBarkDefuseRelease = v);
        Flag("seenBarkClickDetonate", d => d.SeenBarkClickDetonate, (d, v) => d.SeenBarkClickDetonate = v);
        Flag("seenEcho", d => d.SeenEcho, (d, v) => d.SeenEcho = v);
        Flag("seenChaperone", d => d.SeenChaperone, (d, v) => d.SeenChaperone = v);
        Flag("seenTease", d => d.SeenTease, (d, v) => d.SeenTease = v);
        Flag("seenBound", d => d.SeenBound, (d, v) => d.SeenBound = v);
        Flag("seenBrittle", d => d.SeenBrittle, (d, v) => d.SeenBrittle = v);
        Flag("seenBraindrain", d => d.SeenBraindrain, (d, v) => d.SeenBraindrain = v);
        Flag("seenIntroGuide", d => d.SeenIntroGuide, (d, v) => d.SeenIntroGuide = v);
        Flag("seenDuoDemo", d => d.SeenDuoDemo, (d, v) => d.SeenDuoDemo = v);
        Flag("seenSkipDebut", d => d.SeenSkipDebut, (d, v) => d.SeenSkipDebut = v);
        Flag("seenGoldFirst", d => d.SeenGoldFirst, (d, v) => d.SeenGoldFirst = v);
        Flag("seenDollhouse", d => d.SeenDollhouse, (d, v) => d.SeenDollhouse = v);
        Flag("seenFirstSin", d => d.SeenFirstSin, (d, v) => d.SeenFirstSin = v);
        Flag("seenFocusTip", d => d.SeenFocusTip, (d, v) => d.SeenFocusTip = v);
        Flag("seenRippleTeach", d => d.SeenRippleTeach, (d, v) => d.SeenRippleTeach = v);
        Flag("seenHeatTeach", d => d.SeenHeatTeach, (d, v) => d.SeenHeatTeach = v);
        Flag("seenWarrenWelcome", d => d.SeenWarrenWelcome, (d, v) => d.SeenWarrenWelcome = v);
        Flag("seenFirstReturn", d => d.SeenFirstReturn, (d, v) => d.SeenFirstReturn = v);
        Flag("seenBoudoirIntro", d => d.SeenBoudoirIntro, (d, v) => d.SeenBoudoirIntro = v);

        void Set(string camel, Func<DtrhSlotDocument, HashSet<string>> get) => SetMap[camel] = get;

        // Every public HashSet<string> member (WPF SetProp surface).
        Set("purchasedUpgrades", d => d.PurchasedUpgrades);
        Set("purchasedDials", d => d.PurchasedDials);
        Set("disabledUpgrades", d => d.DisabledUpgrades);
        Set("discoveredCodexIds", d => d.DiscoveredCodexIds);
        Set("activeLifetimeBoons", d => d.ActiveLifetimeBoons);
        Set("benchPurchases", d => d.BenchPurchases);
        Set("lessonsComplete", d => d.LessonsComplete);
        Set("pendingReveals", d => d.PendingReveals);
        Set("seenReveals", d => d.SeenReveals);
        Set("firstTimesAwarded", d => d.FirstTimesAwarded);
        Set("bubbleHintsLearned", d => d.BubbleHintsLearned);
        Set("seenNarrativeLines", d => d.SeenNarrativeLines);
        Set("paperwallSketches", d => d.PaperwallSketches);
    }

    /// <summary>The snapshot serializer: the slot document WITHOUT its extension-data
    /// members (consult item 1 — unknown members stay file-only, never pushed to the page;
    /// WPF's ChaosMetaState has no extension data to leak).</summary>
    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver().WithAddedModifier(ti =>
        {
            if (ti.Type == typeof(DtrhSlotDocument))
            {
                var ext = ti.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, "extensionData", StringComparison.Ordinal));
                if (ext is not null)
                {
                    ti.Properties.Remove(ext);
                }
            }
        }),
    };

    private readonly PersistenceStore<DtrhSlotDocument> _slotStore;
    private readonly PersistenceStore<DtrhSlotIndex> _indexStore;
    private readonly DtrhAssetStats _stats;
    private readonly Action<object> _broadcast;
    private readonly Action<string> _log;
    private readonly bool _testMode;
    private readonly DtrhSlotDocument? _testState;
    private Timer? _saveDebounce;
    private readonly object _timerGate = new();

    /// <summary>The snapshot revision (WPF DtrhMetaBridge.Rev). Bumps ONLY on applied ops
    /// and explicit rebroadcasts — m2test's pass/fail counts these deltas.</summary>
    public int Rev { get; private set; }

    public bool TestMode => _testMode;

    /// <summary>True between run-started and run-ended (WPF _runActive, DtrhHostService.cs:257).</summary>
    public bool RunActive { get; private set; }

    /// <summary>The live state: the clone in test mode, the slot document otherwise
    /// (WPF DtrhMetaBridge.S, :36).</summary>
    private DtrhSlotDocument S => _testMode ? _testState! : _slotStore.Current;

    public DtrhMeta(
        PersistenceStore<DtrhSlotDocument> slotStore,
        PersistenceStore<DtrhSlotIndex> indexStore,
        DtrhAssetStats stats,
        Action<object> broadcast,
        Action<string> log,
        bool testMode,
        string? slotFilePath = null,
        DtrhSlotDocument? testFixture = null)
    {
        _slotStore = slotStore;
        _indexStore = indexStore;
        _stats = stats;
        _broadcast = broadcast;
        _log = log;
        _testMode = testMode;
        if (testMode)
        {
            // SP-057: the test clone starts from the DECLARED fixture (the committed
            // sentinel document, or an explicit test-supplied document) — NEVER the live
            // slot store. The old deep-clone of slotStore.Current let evidence inherit
            // the owner's real profile (SP-052 Run B's confidently-wrong dealt 7200/True).
            // Memory-only: WPF's test file is write-only. A malformed committed fixture
            // throws typed (DtrhM2TestFixtureException) — no live-doc fallback exists.
            _testState = testFixture ?? DtrhM2TestFixture.Load();
        }

        FlagAbsentProgressionMembers(slotFilePath);
    }

    /// <summary>Absent-member honesty (packet framing a): a b2-era slot file carries no
    /// progression members — the additive-only defaults apply, and that is FLAGGED once at
    /// bind, never silent. Read-only check of the on-disk JSON; a missing file (fresh
    /// slot) is the normal case, not flagged.</summary>
    private void FlagAbsentProgressionMembers(string? slotFilePath)
    {
        try
        {
            if (slotFilePath is null || !File.Exists(slotFilePath))
            {
                return;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(slotFilePath));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && !doc.RootElement.TryGetProperty("consumableSlots", out _)
                && !doc.RootElement.TryGetProperty("purchasedUpgrades", out _))
            {
                _log("dtrh-meta: slot file predates progression members — neutral defaults applied (additive-only rule, ChaosMetaState.cs:8-9)");
            }
        }
        catch (Exception ex)
        {
            // Never let the flagging read break the bind — the store's own load outcome
            // already typed any corruption.
            _log($"dtrh-meta: absent-member check failed ({ex.GetType().Name}) — bind continues");
        }
    }

    // ============================ meta snapshot ============================

    /// <summary>The {type:'meta'} message (DtrhMetaBridge.SnapshotMessage :58-64):
    /// rev + camelCase state snapshot (typed members only) + testMode.</summary>
    public object SnapshotMessage()
    {
        var state = JsonSerializer.SerializeToElement(S, SnapshotOptions);
        return new { type = "meta", rev = Rev, state, testMode = _testMode };
    }

    /// <summary>Push a fresh snapshot after engine-side mutations that bypass the op
    /// switch (DtrhMetaBridge.Rebroadcast :399).</summary>
    public void Rebroadcast() => Bump();

    private void Bump()
    {
        Rev++;
        try { _broadcast(SnapshotMessage()); }
        catch (Exception ex) { _log($"dtrh-meta: snapshot broadcast failed ({ex.GetType().Name})"); }
    }

    // ============================ meta-command ops ============================

    /// <summary>Handle a {type:'meta-command'} message (DtrhMetaBridge.Handle :89-397).
    /// Returns true iff a mutation was applied (→ snapshot bump + debounced save).
    /// NEVER throws: unknown ops log + ignore; failures count as not-applied.</summary>
    public bool HandleMetaCommand(JsonElement raw)
    {
        var op = raw.TryGetProperty("op", out var opProp) && opProp.ValueKind == JsonValueKind.String
            ? opProp.GetString()
            : null;
        var applied = false;
        try
        {
            applied = op switch
            {
                "purchase-upgrade" => OpPurchaseUpgrade(raw),
                "toggle-upgrade" => OpToggleUpgrade(raw),
                "purchase-dial" => OpPurchaseDial(raw),
                "set-lifetime-boon" => OpSetLifetimeBoon(raw),
                "equip-boon" => OpEquipBoon(raw),
                "add-gold" => OpAddGold(raw),
                "spend-gold" => OpSpendGold(raw),
                "material-add" => OpMaterialAdd(raw),
                "craft" => OpCraft(raw),
                "consume-crafted" => OpConsumeCrafted(raw),
                "pin-boon" => OpPinBoon(raw),
                "set-denial" => OpSetDenial(raw),
                "buy-consumable-slot" => OpBuyConsumableSlot(raw),
                "bench-buy" => OpBenchBuy(raw),
                "first-time" => OpFirstTime(raw),
                "lesson-progress" => OpLessonProgress(raw),
                "set-flag" => OpSetFlag(raw),
                "add-to-set" => OpAddToSet(raw),
                "remove-from-set" => OpRemoveFromSet(raw),
                "set-num" => OpSetNum(raw),
                "map-set" => OpMapSet(raw),
                "add-channel-seconds" => OpAddChannelSeconds(raw),
                "reset-onboarding" => OpResetOnboarding(),
                _ => UnknownOp(op),
            };
        }
        catch (Exception ex)
        {
            _log($"dtrh-meta: op '{op ?? "(missing)"}' failed ({ex.GetType().Name}) — not applied");
            applied = false;
        }

        if (applied)
        {
            Bump();
            DebounceSave();
        }
        else if (op is not null && IsKnownOp(op))
        {
            _log($"dtrh-meta: op '{op}' rejected (shape/affordability)");
        }

        return applied;
    }

    private static bool IsKnownOp(string op) => op is
        "purchase-upgrade" or "toggle-upgrade" or "purchase-dial" or "set-lifetime-boon"
        or "equip-boon" or "add-gold" or "spend-gold" or "material-add" or "craft"
        or "consume-crafted" or "pin-boon" or "set-denial" or "buy-consumable-slot"
        or "bench-buy" or "first-time" or "lesson-progress" or "set-flag" or "add-to-set"
        or "remove-from-set" or "set-num" or "map-set" or "add-channel-seconds"
        or "reset-onboarding";

    private bool UnknownOp(string? op)
    {
        _log($"dtrh-meta: unknown op '{op ?? "(missing)"}' ignored");
        return false;
    }

    private void MutateState(Action<DtrhSlotDocument> mutation)
    {
        if (_testMode)
        {
            mutation(_testState!);
        }
        else
        {
            _slotStore.Mutate(mutation);
        }
    }

    private bool OpPurchaseUpgrade(JsonElement o)
    {
        var id = RequireId(o);
        var cost = Cost(o);
        if (id is null || S.Sparks < cost || S.PurchasedUpgrades.Contains(id)) return false;
        MutateState(d =>
        {
            d.Sparks -= cost;
            d.PurchasedUpgrades.Add(id);
            // Mirrors ChaosMeta.TryPurchase: the Inescapable door opens at purchase time.
            if (id == "extreme_tier") d.ExtremeUnlocked = true;
        });
        return true;
    }

    private bool OpToggleUpgrade(JsonElement o)
    {
        var id = RequireId(o);
        if (id is null) return false;
        var on = GetBool(o, "on") ?? true;
        var changed = false;
        MutateState(d => changed = on ? d.DisabledUpgrades.Remove(id) : d.DisabledUpgrades.Add(id));
        return changed;
    }

    private bool OpPurchaseDial(JsonElement o)
    {
        // Gold cutover (DtrhMetaBridge.cs:116-143): dials cost GOLD; hydra/glitchTimer are
        // rank-gated; HER GIFT covers a short balance on your very FIRST dial.
        var id = RequireId(o);
        var cost = Cost(o);
        if (id is null || S.PurchasedDials.Contains(id)) return false;
        var rankOk = (id != "hydra" && id != "glitchTimer") || DtrhRanks.For(S.RunsCompleted) >= 3; // Entranced
        if (!rankOk) return false;
        if (S.Gold >= cost)
        {
            MutateState(d =>
            {
                d.Gold -= cost;
                d.PurchasedDials.Add(id);
            });
            return true;
        }

        if (!S.GiftGiven && S.PurchasedDials.Count == 0)
        {
            MutateState(d =>
            {
                d.GiftGiven = true;
                d.Gold = 0;
                d.PurchasedDials.Add(id);
            });
            // WPF barks NotifyChaosGiftGiven here (non-test) — the greenfield has no bark
            // subsystem (quips/sound-arbitration row owns it; named limit).
            return true;
        }

        return false;
    }

    private bool OpSetLifetimeBoon(JsonElement o)
    {
        var id = RequireId(o);
        if (id is null) return false;
        var applied = false;
        if (Has(o, "level"))
        {
            // Levels only climb, one paid step at a time (DtrhMetaBridge.cs:147-156).
            var level = GetInt(o, "level") ?? 0;
            var cost = Cost(o);
            var cur = S.LifetimeBoonLevels.TryGetValue(id, out var l) ? l : 0;
            if (level == cur + 1 && S.Sparks >= cost)
            {
                MutateState(d =>
                {
                    d.Sparks -= cost;
                    d.LifetimeBoonLevels[id] = level;
                });
                applied = true;
            }
        }

        if (Has(o, "active"))
        {
            var active = GetBool(o, "active") ?? false;
            MutateState(d => applied |= active ? d.ActiveLifetimeBoons.Add(id) : d.ActiveLifetimeBoons.Remove(id));
        }

        return applied;
    }

    private bool OpEquipBoon(JsonElement o)
    {
        var id = GetString(o, "id");
        MutateState(d => d.EquippedStartBoon = string.IsNullOrEmpty(id) ? null : id);
        return true;
    }

    private bool OpAddGold(JsonElement o)
    {
        var amount = GetInt(o, "amount") ?? 0;
        if (amount <= 0 || amount > 100000) return false;
        MutateState(d => d.Gold += amount);
        return true;
    }

    private bool OpSpendGold(JsonElement o)
    {
        var amount = GetInt(o, "amount") ?? 0;
        if (amount <= 0 || S.Gold < amount) return false;
        MutateState(d => d.Gold -= amount);
        return true;
    }

    private bool OpMaterialAdd(JsonElement o)
    {
        // A material grabbed in the tube, granted LIVE per grab (DtrhMetaBridge.cs:182-194).
        var id = RequireId(o);
        var amount = GetInt(o, "amount") ?? 0;
        if (id is null || !MaterialIds.Contains(id) || amount <= 0 || amount > 30) return false;
        MutateState(d => d.Materials[id] = (d.Materials.TryGetValue(id, out var have) ? have : 0) + amount);
        return true;
    }

    private bool OpCraft(JsonElement o)
    {
        // THE BOUDOIR (DtrhMetaBridge.cs:195-228): validate the SPEND shape — whitelisted
        // ids, 1..9 cells, holdings cover — then decrement, record discovery + item.
        var id = RequireId(o);
        if (id is null || !RecipeIds.Contains(id)) return false;
        if (!o.TryGetProperty("cost", out var costEl) || costEl.ValueKind != JsonValueKind.Object) return false;
        var cost = new Dictionary<string, int>();
        var cells = 0;
        foreach (var p in costEl.EnumerateObject())
        {
            var n = p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v) ? v : 0;
            if (!MaterialIds.Contains(p.Name) || n <= 0 || n > 9) return false;
            cost[p.Name] = n;
            cells += n;
        }

        if (cells < 1 || cells > 9) return false;
        if (!cost.All(kv => S.Materials.TryGetValue(kv.Key, out var have) && have >= kv.Value)) return false;
        MutateState(d =>
        {
            foreach (var kv in cost) d.Materials[kv.Key] -= kv.Value;
            d.DiscoveredRecipes.Add(id);
            d.CraftedItems[id] = (d.CraftedItems.TryGetValue(id, out var owned) ? owned : 0) + 1;
        });
        _log($"dtrh-meta: crafted {id} (x{S.CraftedItems[id]})");
        return true;
    }

    private bool OpConsumeCrafted(JsonElement o)
    {
        // Only the three consumables decrement; permanents are never spendable
        // (DtrhMetaBridge.cs:229-240).
        var id = RequireId(o);
        if (id is null || !ConsumableIds.Contains(id)) return false;
        if (!S.CraftedItems.TryGetValue(id, out var held) || held <= 0) return false;
        MutateState(d =>
        {
            if (held == 1) d.CraftedItems.Remove(id);
            else d.CraftedItems[id] = held - 1;
        });
        return true;
    }

    private bool OpPinBoon(JsonElement o)
    {
        // THE PADLOCK (DtrhMetaBridge.cs:241-251): gated on owning the padlock; blank unpins.
        if (!S.CraftedItems.ContainsKey("the_padlock")) return false;
        var id = GetString(o, "id");
        if (id is not null && id.Length > 64) return false;
        MutateState(d => d.PinnedBoon = string.IsNullOrEmpty(id) ? null : id);
        return true;
    }

    private bool OpSetDenial(JsonElement o)
    {
        // THE CAGE's two-way toggle (DtrhMetaBridge.cs:252-262): arming needs the cage.
        var on = GetBool(o, "on") ?? false;
        if (on && !S.CraftedItems.ContainsKey("the_cage")) return false;
        if (S.DenialArmed == on) return false;
        MutateState(d => d.DenialArmed = on);
        return true;
    }

    private bool OpBuyConsumableSlot(JsonElement o)
    {
        var cost = Cost(o);
        if (S.Sparks < cost || S.ConsumableSlots >= MaxConsumableSlots) return false;
        MutateState(d =>
        {
            d.Sparks -= cost;
            d.ConsumableSlots++;
        });
        return true;
    }

    private bool OpBenchBuy(JsonElement o)
    {
        // Gold cutover (DtrhMetaBridge.cs:272-285): only the three console extras survive;
        // pocket ids are rejected (whitelist).
        var id = RequireId(o);
        var cost = Cost(o);
        if (id is null || !BenchIds.Contains(id)) return false;
        if (S.BenchPurchases.Contains(id) || S.Gold < cost) return false;
        MutateState(d =>
        {
            d.Gold -= cost;
            d.BenchPurchases.Add(id);
        });
        return true;
    }

    private bool OpFirstTime(JsonElement o)
    {
        // One-time drops bonus; the amount table is host-owned (DtrhMetaBridge.cs:286-300).
        var id = RequireId(o);
        if (id is null || !FirstTimeAmounts.TryGetValue(id, out var amount)) return false;
        if (S.FirstTimesAwarded.Contains(id)) return false;
        MutateState(d =>
        {
            d.FirstTimesAwarded.Add(id);
            d.Sparks += amount;
        });
        return true;
    }

    private bool OpLessonProgress(JsonElement o)
    {
        var id = RequireId(o);
        if (id is null) return false;
        var applied = false;
        var value = GetLong(o, "value") ?? 0;
        var cur = S.LessonProgress.TryGetValue(id, out var v) ? v : 0;
        if (value > cur)
        {
            MutateState(d => d.LessonProgress[id] = value);
            applied = true;
        }

        if (GetBool(o, "complete") == true)
        {
            MutateState(d => applied |= d.LessonsComplete.Add(id));
        }

        return applied;
    }

    private bool OpSetFlag(JsonElement o)
    {
        // One-way: any bool member may only ever go false → true (DtrhMetaBridge.cs:311-318).
        var key = GetString(o, "key");
        if (key is null || !FlagGet.TryGetValue(key, out var get)) return false;
        if (get(S)) return false;
        MutateState(d => FlagSet[key](d, true));
        return true;
    }

    private bool OpAddToSet(JsonElement o)
    {
        var setName = GetString(o, "set");
        var id = RequireId(o);
        if (setName is null || id is null || !SetMap.TryGetValue(setName, out var get)) return false;
        var added = false;
        MutateState(d => added = get(d).Add(id));
        return added;
    }

    private bool OpRemoveFromSet(JsonElement o)
    {
        // Only reveal-pending is legitimately removable (DtrhMetaBridge.cs:327-333).
        var setName = GetString(o, "set");
        var id = RequireId(o);
        if (setName != "pendingReveals" || id is null) return false;
        var removed = false;
        MutateState(d => removed = d.PendingReveals.Remove(id));
        return removed;
    }

    private bool OpSetNum(JsonElement o)
    {
        // Climb-only counters (DtrhMetaBridge.cs:335-344).
        var key = GetString(o, "key");
        var value = GetLong(o, "value") ?? 0;
        if (key == "lastRankSeen" && value > S.LastRankSeen && value < 64)
        {
            MutateState(d => d.LastRankSeen = (int)value);
            return true;
        }

        if (key == "tutorialStage" && value > S.TutorialStage && value <= 16)
        {
            MutateState(d => d.TutorialStage = (int)value);
            return true;
        }

        return false;
    }

    private bool OpMapSet(JsonElement o)
    {
        var id = RequireId(o);
        var value = GetLong(o, "value") ?? 0;
        if (GetString(o, "map") != "narrativeCooldownEnds" || id is null) return false;
        MutateState(d => d.NarrativeCooldownEnds[id] = value);
        return true;
    }

    private bool OpAddChannelSeconds(JsonElement o)
    {
        var sec = GetDouble(o, "seconds") ?? 0;
        if (sec <= 0 || sec >= 36000) return false;
        MutateState(d => d.TotalChannelSeconds += sec);
        return true;
    }

    private bool OpResetOnboarding()
    {
        // Full FTUE replay WITHOUT touching progression (DtrhMetaBridge.cs:360-378).
        MutateState(d =>
        {
            foreach (var name in OnboardingFlagNames)
            {
                FlagSet[char.ToLowerInvariant(name[0]) + name[1..]](d, false);
            }

            d.DiscoveredCodexIds.RemoveWhere(id => !id.StartsWith("boon:", StringComparison.Ordinal));
            d.BubbleHintsLearned.Clear();
            d.SeenReveals.Remove("dollhouse");
            d.PendingReveals.Remove("dollhouse");
            d.TutorialStage = 0;
            d.SeenNarrativeLines.RemoveWhere(id => id.StartsWith("cheshire:", StringComparison.Ordinal));
            foreach (var k in d.NarrativeCooldownEnds.Keys.Where(k => k.StartsWith("cheshire:", StringComparison.Ordinal)).ToList())
            {
                d.NarrativeCooldownEnds.Remove(k);
            }

            d.ForceScriptedRun = true;
        });
        return true;
    }

    // ============================ payout (OnRunEnded) ============================

    /// <summary>Plain-value award input (ChaosUpgrades.ChaosRunRewardInput, :573-576).</summary>
    public readonly record struct RunRewardInput(
        double RunDurationSec, double DifficultyMult, double SparkGainMult,
        double Score, double TrickleDrops, bool DripFeedMaxed,
        int BestCombo, int Defused, double ElapsedSec);

    /// <summary>THE SHOT drip (ChaosUpgrades.ShotSparkMult :565-569): +4% per shot, cap 10.</summary>
    public static double ShotSparkMult(DtrhSlotDocument state) =>
        1.0 + 0.04 * Math.Min(10, state.CraftedItems.TryGetValue("the_shot", out var n) ? n : 0);

    /// <summary>The spark formula (ChaosUpgrades.AwardRunRewards :578-598). Real mode adds
    /// FIRST_FALL_BONUS when RunsCompleted == 0 (:600); the test mirror omits it
    /// (DtrhMetaBridge.ComputeSparksOnly :446-456).</summary>
    public int ComputeSparks(in RunRewardInput run)
    {
        var durationMin = Math.Max(0, run.RunDurationSec) / 60.0;
        var completionBonus = 35.0 * run.DifficultyMult * Math.Min(1.0, durationMin / 3.0);
        var scorePart = 1.5 * Math.Sqrt(Math.Max(0, run.Score));
        var sparks = (int)Math.Round((scorePart + completionBonus) * run.SparkGainMult);
        sparks += (int)Math.Max(0, run.TrickleDrops);
        if (run.DripFeedMaxed) sparks = (int)Math.Round(sparks * 1.10);
        sparks = (int)Math.Round(sparks * ShotSparkMult(S));
        if (!_testMode && S.RunsCompleted == 0) sparks += FirstFallBonus;
        return Math.Max(0, sparks);
    }

    /// <summary>Bank a completed run (ChaosUpgrades.AwardRunRewards :600-609 real /
    /// DtrhMetaBridge.AwardRun :66-84 test). Returns the sparks earned; bumps the rev.</summary>
    public int AwardRun(in RunRewardInput input)
    {
        var sparks = ComputeSparks(input);
        var score = input.Score;
        var bestCombo = input.BestCombo;
        var defused = input.Defused;
        var elapsedSec = input.ElapsedSec;
        MutateState(d =>
        {
            d.Sparks += sparks;
            d.RunsCompleted += 1;
            d.BestScore = Math.Max(d.BestScore, (long)score);
            if (!_testMode)
            {
                // The test mirror banks only Sparks/RunsCompleted/BestScore
                // (DtrhMetaBridge.cs:71-78).
                d.BestCombo = Math.Max(d.BestCombo, bestCombo);
                d.TotalDefused += defused;
                d.TotalRunSeconds += Math.Max(0, elapsedSec);
            }
        });
        SaveNow(); // WPF persists inside AwardRunRewards (:608)
        Bump();
        return sparks;
    }

    /// <summary>run-started (DtrhHostService.cs:255-269): the freeze/duck hygiene has
    /// ALREADY run (the host window invokes it first, :258-259 order); this tracks the
    /// run boundary. Crash-sentinel/bark hooks are named limits (no greenfield subsystems).</summary>
    public void OnRunStarted(string? difficulty, string? mode)
    {
        RunActive = true;
        _log($"dtrh-meta: run started (diff={difficulty ?? "Gentle"}, mode={mode ?? "?"})");
    }

    /// <summary>run-ended → XP payout + meta banking (DtrhHostService.OnRunEnded :510-613).
    /// The freeze hygiene has ALREADY run (host window, :513 order). Returns the
    /// payout-result payload. skillMult is 1.0: WPF reads App.SkillTree with a `?? 1.0`
    /// fallback (:526) and the greenfield has no skill tree — the fallback IS the parity
    /// outcome. App-level hooks (AddXP/achievements/reveal-sync/bark/sentinel/session
    /// telemetry) are named limits — no greenfield subsystems exist for them.</summary>
    public DtrhProtocol.DtrhPayoutResult OnRunEnded(JsonElement raw)
    {
        RunActive = false;
        var score = GetDouble(raw, "score") ?? 0;
        var durationSec = Math.Max(1, GetDouble(raw, "durationSec") ?? 60);
        var elapsedSec = Math.Clamp(GetDouble(raw, "elapsedSec") ?? durationSec, 0, durationSec * 2);
        var diffMult = Math.Clamp(GetDouble(raw, "difficultyMult") ?? 1.0, 0.5, 5.0);
        var sparkGainMult = Math.Clamp(GetDouble(raw, "sparkGainMult") ?? 1.0, 0.5, 5.0);

        var durMin = durationSec / 60.0;
        var capBase = 250.0 * durMin * diffMult;
        var baseXp = Math.Min(score, capBase);
        const double skillMult = 1.0; // no greenfield skill tree — WPF's own ?? 1.0 fallback (:526)
        var finalXp = baseXp * skillMult;

        long previousBest = _testMode ? 0 : S.BestScore; // read BEFORE banking (:529)

        var sparksEarned = AwardRun(new RunRewardInput(
            RunDurationSec: durationSec,
            DifficultyMult: diffMult,
            SparkGainMult: sparkGainMult,
            Score: score,
            TrickleDrops: GetDouble(raw, "trickleDrops") ?? 0,
            DripFeedMaxed: GetBool(raw, "dripFeedMaxed") ?? false,
            BestCombo: GetInt(raw, "bestCombo") ?? 0,
            Defused: GetInt(raw, "defused") ?? 0,
            ElapsedSec: elapsedSec));

        string? rankUp = null;
        if (!_testMode)
        {
            // Evaluated AFTER AwardRun incremented RunsCompleted (:563-566).
            var nowRank = DtrhRanks.For(S.RunsCompleted);
            if (nowRank > S.LastRankSeen) rankUp = DtrhRanks.Name(nowRank);
            // RevealService.Sync's pending-reveal mutation is a named limit (no reveal
            // subsystem); the rebroadcast still pushes the banked snapshot (:572).
            Rebroadcast();
        }

        _log($"dtrh-meta: run complete — base {baseXp:0} x {skillMult:0.0} = {finalXp:0} XP, {sparksEarned} sparks{(rankUp is not null ? ", RANK UP" : "")}{(_testMode ? " (TEST, dry run)" : "")}");
        return new DtrhProtocol.DtrhPayoutResult(baseXp, skillMult, finalXp, sparksEarned, previousBest, rankUp, _testMode);
    }

    // ============================ request-run ============================

    /// <summary>request-run {setup?} (DtrhHostService.OnRequestRun :395-417): persist the
    /// hub's Descent-tab choices into the index document, then deal a fresh run config —
    /// the scripted first-descent classroom when RunsCompleted == 0 (or a forced replay),
    /// else FromSetup with owned-upgrade multipliers pre-applied. Returns the run-config
    /// payload (the host wraps it in {type:'run-config'}).</summary>
    public object OnRequestRun(JsonElement? setup)
    {
        try
        {
            if (setup is { ValueKind: JsonValueKind.Object } s)
            {
                PersistRunSetup(s);
            }

            var force = !_testMode && S.ForceScriptedRun;
            var scripted = !_testMode && (S.RunsCompleted == 0 || force);
            var cfg = scripted ? DtrhRunConfig.FirstRun() : DtrhRunConfig.FromSetup(_indexStore.Current, S);
            if (force)
            {
                // One-shot spent at DEAL time (DtrhHostService.cs:403-411): a run-end clear
                // has too many exit paths — any missed one would deal a second classroom.
                MutateState(d => d.ForceScriptedRun = false);
                SaveNow();
                Rebroadcast();
            }

            _log($"dtrh-meta: dealt run config (diff={cfg.Difficulty}, dur={cfg.DurationSec}s, endless={cfg.Endless}, scripted={scripted})");
            return DtrhRunConfig.BuildRunConfigPayload(cfg, S, DtrhRanks.For(S.RunsCompleted));
        }
        catch (Exception ex)
        {
            // WPF wraps the whole handler (:414-416): a bad setup must not break the deal.
            _log($"dtrh-meta: request-run failed ({ex.GetType().Name}) — dealing fallback config");
            return DtrhRunConfig.BuildRunConfigPayload(DtrhRunConfig.FromSetup(_indexStore.Current, S), S, DtrhRanks.For(S.RunsCompleted));
        }
    }

    /// <summary>The web-relevant setup persistence (DtrhHostService.PersistRunSetup
    /// :421-444): absent fields are left untouched; clamps stay host-side so a stale page
    /// can never write itself past a gate. App-global (index document) in the greenfield —
    /// WPF writes AppSettings, shared across slots.</summary>
    public void PersistRunSetup(JsonElement setup)
    {
        _indexStore.Mutate(idx =>
        {
            if (setup.TryGetProperty("difficulty", out var diff) && diff.ValueKind == JsonValueKind.String)
            {
                idx.Difficulty = diff.GetString() ?? idx.Difficulty;
            }

            if (setup.TryGetProperty("durationSec", out _))
            {
                // The Hourglass unlock lifts the ceiling to 2h; without it the preset
                // ceiling holds (DtrhHostService.cs:474-475). Ownership = the active
                // slot's PurchasedUpgrades (IsOwned, ChaosUpgrades.cs:323).
                var durMax = S.PurchasedUpgrades.Contains("custom_duration") ? 7200 : 1200;
                idx.DurationSec = Math.Clamp(GetInt(setup, "durationSec") ?? 960, 60, durMax);
            }

            if (setup.TryGetProperty("waveCount", out _))
            {
                idx.WaveCount = Math.Clamp(GetInt(setup, "waveCount") ?? 5, 1, 12);
            }

            // The Bottomless Fall: the endless toggle only sticks for owners (a stale page
            // can't arm it without the unlock, DtrhHostService.cs:478-480); FromSetup
            // re-checks ownership at deal time (ChaosModels.cs:206).
            if (setup.TryGetProperty("endless", out _))
            {
                idx.Endless = (GetBool(setup, "endless") ?? false) && S.PurchasedUpgrades.Contains("endless_mode");
            }

            if (setup.TryGetProperty("motion", out var motion) && motion.ValueKind == JsonValueKind.String)
            {
                idx.Motion = motion.GetString() ?? idx.Motion;
            }

            if (setup.TryGetProperty("enabledVariants", out var variants))
            {
                idx.EnabledVariants = variants.ValueKind == JsonValueKind.Null
                    ? null
                    : variants.ValueKind == JsonValueKind.Array
                        ? variants.EnumerateArray()
                            .Where(v => v.ValueKind == JsonValueKind.String)
                            .Select(v => v.GetString()!)
                            .ToList()
                        : idx.EnabledVariants;
            }

            if (setup.TryGetProperty("effectIntensity", out _))
            {
                idx.EffectIntensity = Math.Clamp(GetDouble(setup, "effectIntensity") ?? 0.85, 0.2, 1.5);
            }

            if (setup.TryGetProperty("colorFlashes", out _)) idx.ColorFlashes = GetBool(setup, "colorFlashes") ?? true;
            if (setup.TryGetProperty("boonDraftEnabled", out _)) idx.BoonDraftEnabled = GetBool(setup, "boonDraftEnabled") ?? true;
            if (setup.TryGetProperty("allowCurses", out _)) idx.AllowCurses = GetBool(setup, "allowCurses") ?? true;
            if (setup.TryGetProperty("dartersEnabled", out _)) idx.DartersEnabled = GetBool(setup, "dartersEnabled") ?? true;
            if (setup.TryGetProperty("key1", out var k1) && k1.ValueKind == JsonValueKind.String) idx.Key1 = k1.GetString() ?? idx.Key1;
            if (setup.TryGetProperty("key2", out var k2) && k2.ValueKind == JsonValueKind.String) idx.Key2 = k2.GetString() ?? idx.Key2;
        });
        _ = _indexStore.Save();
        _log("dtrh-meta: descent setup persisted (index document, app-global)");
    }

    /// <summary>The init-carried saved setup (BuildRunSetup :447-478 — raw values, NOT the
    /// clamped run values; the hub applies its own gating visually).</summary>
    public static DtrhProtocol.DtrhRunSetup BuildRunSetupPayload(DtrhSlotIndex idx) =>
        new(idx.Difficulty, idx.DurationSec, idx.Endless, idx.WaveCount, idx.Motion, idx.EnabledVariants,
            idx.EffectIntensity, idx.ColorFlashes, idx.BoonDraftEnabled, idx.AllowCurses,
            idx.DartersEnabled, idx.Key1, idx.Key2);

    // ============================ asset-stats ============================

    /// <summary>asset-stats (DtrhHostService.cs:280-284): merge the page's engagement
    /// deltas into the cumulative store. No reply message (fire-and-forget in WPF).</summary>
    public void OnAssetStats(JsonElement raw) => _stats.Merge(raw);

    /// <summary>The favorites seed at ready (DtrhHostService.cs:215-216): top-12 names by
    /// cumulative engagement; empty when nothing ranked (no message sent then — WPF posts
    /// only when Count > 0).</summary>
    public IReadOnlyList<string> FavoritesSeed() => _stats.TopAssets(12);

    // ============================ save discipline ============================

    /// <summary>500 ms debounce (DtrhMetaBridge.cs:418-425): op bursts coalesce into one
    /// store write.</summary>
    private void DebounceSave()
    {
        lock (_timerGate)
        {
            _saveDebounce ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
            _saveDebounce.Change(500, Timeout.Infinite);
        }
    }

    /// <summary>Flush any pending debounced save immediately (DtrhMetaBridge.FlushSave
    /// :402-409 — run end / window close).</summary>
    public void FlushSave()
    {
        lock (_timerGate)
        {
            _saveDebounce?.Dispose();
            _saveDebounce = null;
        }

        SaveNow();
    }

    private void SaveNow()
    {
        if (_testMode)
        {
            return; // clone is memory-only (WPF's chaos_meta.test.json is write-only)
        }

        try
        {
            _ = _slotStore.Save(); // SP-005 serialized enqueue — never blocks the dispatcher
        }
        catch (Exception ex)
        {
            _log($"dtrh-meta: save failed ({ex.GetType().Name})");
        }
    }

    // ============================ field helpers ============================

    private static bool Has(JsonElement o, string name) => o.TryGetProperty(name, out _);

    private static string? RequireId(JsonElement o)
    {
        var id = GetString(o, "id");
        return string.IsNullOrWhiteSpace(id) || id.Length > 128 ? null : id;
    }

    private static int Cost(JsonElement o) => Math.Max(0, GetInt(o, "cost") ?? 0);

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool? GetBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean()
            : null;

    private static int? GetInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v)
            ? v
            : null;

    private static long? GetLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v)
            ? v
            : null;

    private static double? GetDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v)
            ? v
            : null;
}
