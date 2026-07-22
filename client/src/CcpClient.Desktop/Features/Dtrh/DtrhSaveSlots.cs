using System.Text.Json.Serialization;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Features.Dtrh;

/// <summary>
/// The b2 slot document (schema v1): the fields the picker/quarantine/lifecycle semantics
/// need now. b4 (progression) ADDS members without a schema bump — unknown-member
/// tolerance + <see cref="ExtensionData"/> preserve them both directions (persistence
/// contract §6, SP-007 precedent). <see cref="CraftedItems"/> keeps the WPF key names
/// verbatim ("ragdoll" / "porcelain" — ChaosMetaStore.cs:105-108) because the stitch-lock
/// reads them.
///
/// b4 (SP-026): the FULL WPF ChaosMetaState progression surface (ChaosMetaState.cs —
/// additive-only, neutral defaults :8-9) rides this document. NOT ported: SchemaVersion
/// (the SP-005 store owns schema) and the retired ToyPockets/AccessoryPockets (WPF
/// do-not-read, migration-zeroed, ChaosMetaState.cs:62-63). A b2-era file lacking these
/// members loads clean — the initializers ARE the WPF defaults (ConsumableSlots=1 mirrors
/// the ChaosMetaStore.cs:71 load clamp); the meta engine flags absent members once at
/// bind, never silently.
/// </summary>
public sealed class DtrhSlotDocument
{
    public const int CurrentSchemaVersion = 1;

    // ---- b2 (picker/payout spine) ----
    public int Sparks { get; set; }

    public int Gold { get; set; }

    public int RunsCompleted { get; set; }

    public long BestScore { get; set; }

    public Dictionary<string, int> CraftedItems { get; set; } = new();

    // ---- b4: economy/unlocks (ChaosMetaState.cs:17-37,58-74) ----
    public HashSet<string> PurchasedUpgrades { get; set; } = new();

    public HashSet<string> PurchasedDials { get; set; } = new();

    public HashSet<string> DisabledUpgrades { get; set; } = new();

    public bool ExtremeUnlocked { get; set; }

    public string? EquippedStartBoon { get; set; }

    public HashSet<string> DiscoveredCodexIds { get; set; } = new();

    public Dictionary<string, int> LifetimeBoonLevels { get; set; } = new();

    public HashSet<string> ActiveLifetimeBoons { get; set; } = new();

    public int ConsumableSlots { get; set; } = 1;

    public HashSet<string> BenchPurchases { get; set; } = new();

    public bool GiftGiven { get; set; }

    // ---- b4: lessons/reveals/first-times (ChaosMetaState.cs:77-85,116) ----
    public Dictionary<string, long> LessonProgress { get; set; } = new();

    public HashSet<string> LessonsComplete { get; set; } = new();

    public HashSet<string> PendingReveals { get; set; } = new();

    public HashSet<string> SeenReveals { get; set; } = new();

    public HashSet<string> FirstTimesAwarded { get; set; } = new();

    public HashSet<string> BubbleHintsLearned { get; set; } = new();

    // ---- b4: seen-once bools (ChaosMetaState.cs:40-53,88-109,151) ----
    public bool SeenDefuseTutorial { get; set; }
    public bool SeenBarkDefuseFirst { get; set; }
    public bool SeenBarkDefuseNoFocus { get; set; }
    public bool SeenBarkDefuseRelease { get; set; }
    public bool SeenBarkClickDetonate { get; set; }
    public bool SeenEcho { get; set; }
    public bool SeenChaperone { get; set; }
    public bool SeenTease { get; set; }
    public bool SeenBound { get; set; }
    public bool SeenBrittle { get; set; }
    public bool SeenBraindrain { get; set; }
    public bool SeenIntroGuide { get; set; }
    public bool SeenDuoDemo { get; set; }
    public bool SeenSkipDebut { get; set; }
    public bool SeenGoldFirst { get; set; }
    public bool SeenDollhouse { get; set; }
    public bool SeenFirstSin { get; set; }
    public bool SeenFocusTip { get; set; }
    public bool SeenRippleTeach { get; set; }
    public bool SeenHeatTeach { get; set; }
    public bool SeenWarrenWelcome { get; set; }
    public bool SeenFirstReturn { get; set; }
    public bool SeenBoudoirIntro { get; set; }

    // ---- b4: arc/rank/narrative (ChaosMetaState.cs:112-130) ----
    public bool ForceScriptedRun { get; set; }

    public int LastRankSeen { get; set; }

    public int TutorialStage { get; set; }

    public HashSet<string> SeenNarrativeLines { get; set; } = new();

    public Dictionary<string, long> NarrativeCooldownEnds { get; set; } = new();

    // ---- b4: crafting (ChaosMetaState.cs:137-155) ----
    public Dictionary<string, int> Materials { get; set; } = new();

    public HashSet<string> DiscoveredRecipes { get; set; } = new();

    public string? PinnedBoon { get; set; }

    public bool DenialArmed { get; set; }

    public HashSet<string> PaperwallSketches { get; set; } = new();

    // ---- b4: lifetime stats (ChaosMetaState.cs:158-166) ----
    public int BestCombo { get; set; }

    public long TotalDefused { get; set; }

    public double TotalRunSeconds { get; set; }

    public double TotalChannelSeconds { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}

/// <summary>The DTRH-owned index document: which slot is live. Lives in its own store
/// because Persistence/DemoSettings.cs is outside this packet's File Scope (SP-024
/// record Step 1 — a 4th PersistenceStore on the SAME SP-005 machinery, not a second
/// persistence path; WPF parity: ChaosMetaStore.cs:38 keeps the active slot in app
/// settings).</summary>
public sealed class DtrhSlotIndex
{
    public const int CurrentSchemaVersion = 1;

    public int ActiveSlot { get; set; } = 1;

    // ---- b4 (SP-026): the saved Descent-tab choices (request-run persistence, WPF
    // PersistRunSetup :421-444 writes AppSettings — app-global, shared across slots, so
    // the INDEX document carries them; same File Scope constraint class as ActiveSlot).
    // init's runSetup reads these (BuildRunSetup :447-478 raw values, NOT clamped run
    // values); request-run clamps at deal time. Defaults = the WPF fallbacks. ----
    public string Difficulty { get; set; } = "Easy";

    public int DurationSec { get; set; } = 180;

    public int WaveCount { get; set; } = 5;

    public string Motion { get; set; } = "Mixed";

    /// <summary>Enabled variant ids; null = all enabled (WPF ChaosEnabledVariants).</summary>
    public List<string>? EnabledVariants { get; set; }

    public double EffectIntensity { get; set; } = 0.85;

    public bool ColorFlashes { get; set; } = true;

    public bool BoonDraftEnabled { get; set; } = true;

    public bool AllowCurses { get; set; } = true;

    public bool DartersEnabled { get; set; } = true;

    public string Key1 { get; set; } = "Q";

    public string Key2 { get; set; } = "E";

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}

/// <summary>Headline stats for one save slot, read for the pre-descent picker
/// (WPF: ChaosMetaStore.SlotSummary, ChaosMetaStore.cs:236-253).</summary>
public sealed record DtrhSlotSummary(
    int Slot,
    bool Exists,
    int Sparks,
    int Gold,
    int RunsCompleted,
    long BestScore,
    DateTime? LastPlayedUtc,
    bool HasRagdoll,
    bool HasPorcelain,
    /// <summary>Typed Degraded (SP-005 §5): this slot's document was quarantined or is
    /// newer-schema. Greenfield divergence from WPF (which shows corrupt slots as
    /// zeroed-but-deletable, ChaosMetaStore.cs:84-85): the packet mandates SP-005
    /// quarantine, so the card surfaces the flag instead of silent zeros.</summary>
    bool Degraded);

/// <summary>
/// The three local DTRH save slots (slice b2; dtrh-admission.md §7) on SP-005 machinery:
/// one <see cref="PersistenceStore{TModel}"/> per slot file (<c>dtrh_slot1..3.json</c>
/// beside settings.json — WPF one-file-per-slot identity, ChaosMetaStore.cs:24,30) plus
/// one for the active-slot index (<c>dtrh_slots.json</c>). Every store gets its OWN named
/// owner: <see cref="AsyncOperationOwner.Begin"/> cancels the previous generation
/// (OperationRegistry.cs:148-159), so N stores sharing one owner would cancel each
/// other's in-flight writes.
///
/// Semantics ported from WPF (archaeology cites in SP-024 record Step 1): missing slot =
/// empty ("New Journey"), never auto-created at load (SP-005 Missing outcome is not
/// dirty); corrupt slot = quarantine + typed Degraded, never silent defaults; descending
/// into an empty slot persists a fresh document immediately (defines lastPlayed);
/// delete removes file + stray temp and reloads the slot store to flagged-empty; the
/// stitch-lock keeps slots 2/3 shut until any save crafts the Ragdoll / Porcelain doll.
/// </summary>
public sealed class DtrhSaveSlots : IBackgroundParticipant
{
    public const int SlotCount = 3;

    private readonly ParticipantInfrastructure _infra;
    private readonly ILogSink _log;
    private readonly string _dataDirectory;
    private readonly PersistenceStore<DtrhSlotIndex> _index;
    private readonly PersistenceStore<DtrhSlotDocument>[] _slots = new PersistenceStore<DtrhSlotDocument>[SlotCount];
    private readonly PersistenceStore<DtrhAssetStatsDocument> _assetStats;
    private int _started;

    public DtrhSaveSlots(ParticipantInfrastructure infra, string dataDirectory)
    {
        _infra = infra;
        _log = infra.Log;
        _dataDirectory = dataDirectory;
        _index = new PersistenceStore<DtrhSlotIndex>(
            infra.OwnerFor("DtrhSlotIndex"), infra.Log, IndexFilePath, DtrhSlotIndex.CurrentSchemaVersion);
        for (var i = 0; i < SlotCount; i++)
        {
            _slots[i] = NewSlotStore(i + 1);
        }

        // b4 (SP-026): cumulative per-asset engagement (asset-stats) — app-global in WPF
        // (dtrh_asset_stats.json, DtrhAssetStatsStore.cs:41), so it lives beside the index,
        // not inside a slot. Same SP-005 machinery, its OWN named owner.
        _assetStats = new PersistenceStore<DtrhAssetStatsDocument>(
            infra.OwnerFor("DtrhAssetStats"), infra.Log, AssetStatsFilePath, DtrhAssetStatsDocument.CurrentSchemaVersion);
    }

    private PersistenceStore<DtrhSlotDocument> NewSlotStore(int slot) =>
        new(_infra.OwnerFor($"DtrhSlot{slot}"), _log, SlotFilePath(slot), DtrhSlotDocument.CurrentSchemaVersion);

    public string Name => "DtrhSaveSlots";

    public bool Running { get; private set; }

    /// <summary>The folder every save lives in — shown in the picker footer
    /// (WPF: ChaosMetaStore.SaveFolder, ChaosMetaStore.cs:35).</summary>
    public string SaveFolder => _dataDirectory;

    public string IndexFilePath => Path.Combine(_dataDirectory, "dtrh_slots.json");

    /// <summary>Absolute path to the cumulative asset-engagement document (WPF:
    /// dtrh_asset_stats.json, DtrhAssetStatsStore.cs:41).</summary>
    public string AssetStatsFilePath => Path.Combine(_dataDirectory, "dtrh_asset_stats.json");

    /// <summary>The app-global asset-engagement store (b4). Mutations via the
    /// <see cref="DtrhAssetStats"/> wrapper (merge clamps + case-insensitive names).</summary>
    public PersistenceStore<DtrhAssetStatsDocument> AssetStatsStore => _assetStats;

    /// <summary>The index store (b4: request-run persists the Descent-tab choices here).</summary>
    public PersistenceStore<DtrhSlotIndex> IndexStore => _index;

    /// <summary>Absolute path to a slot's save file (1-based).</summary>
    public string SlotFilePath(int slot) =>
        Path.Combine(_dataDirectory, $"dtrh_slot{Clamp(slot)}.json");

    /// <summary>The live save slot (1-3), remembered in the index document
    /// (WPF: ChaosMetaStore.ActiveSlot, ChaosMetaStore.cs:38).</summary>
    public int ActiveSlot => Clamp(_index.Current.ActiveSlot);

    private static int Clamp(int slot) => slot < 1 || slot > SlotCount ? 1 : slot;

    /// <summary>Start loads every store (SP-005 phase-3 contract): a missing slot yields
    /// flagged-empty defaults WITHOUT a file write (empty slots stay file-less); a
    /// corrupt slot is quarantined here — once, at startup, never at picker-open.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return; // idempotent (SP-003)
        }

        Running = true;
        await _index.StartAsync(cancellationToken).ConfigureAwait(false);
        foreach (var store in _slots)
        {
            await store.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        await _assetStats.StartAsync(cancellationToken).ConfigureAwait(false);
        if (_assetStats.LastLoadOutcome is { } statsOutcome && statsOutcome.IsDegraded)
        {
            _log.Log($"dtrh-slots: asset-stats document degraded ({statsOutcome.GetType().Name})");
        }

        if (_index.LastLoadOutcome is { } indexOutcome && indexOutcome.IsDegraded)
        {
            _log.Log($"dtrh-slots: index document degraded ({indexOutcome.GetType().Name})");
        }

        for (var i = 0; i < SlotCount; i++)
        {
            if (_slots[i].LastLoadOutcome is { } outcome && outcome.IsDegraded)
            {
                _log.Log($"dtrh-slots: slot {i + 1} degraded ({outcome.GetType().Name}) — flagged defaults, original preserved aside");
            }
        }
    }

    public async Task StopAsync()
    {
        if (!Running)
        {
            return;
        }

        Running = false;
        await _index.StopAsync().ConfigureAwait(false);
        foreach (var store in _slots)
        {
            await store.StopAsync().ConfigureAwait(false);
        }

        await _assetStats.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Teardown flush for every store (SP-005 §11): enqueues final writes for
    /// dirty stores and awaits quiescence with a bounded wait. Never throws.</summary>
    public async Task FlushAsync(TimeSpan boundedWait)
    {
        if (!Running)
        {
            return;
        }

        await _index.FlushAsync(boundedWait).ConfigureAwait(false);
        foreach (var store in _slots)
        {
            await store.FlushAsync(boundedWait).ConfigureAwait(false);
        }

        await _assetStats.FlushAsync(boundedWait).ConfigureAwait(false);
    }

    /// <summary>The store for a slot (1-3) — progression slices mutate through it.</summary>
    public PersistenceStore<DtrhSlotDocument> StoreFor(int slot) => _slots[Clamp(slot) - 1];

    /// <summary>The typed load outcome of a slot's document (null until started).</summary>
    public LoadOutcome? SlotLoadOutcome(int slot) => _slots[Clamp(slot) - 1].LastLoadOutcome;

    /// <summary>Headline stats for every slot in FIXED slot order 1..3 (picker cards;
    /// WPF: ChaosMeta.AllSlotSummaries, ChaosUpgrades.cs:246-252). Pure read: summaries
    /// come from the already-loaded stores plus file facts — no parallel parse path.</summary>
    public IReadOnlyList<DtrhSlotSummary> Summaries()
    {
        var list = new List<DtrhSlotSummary>(SlotCount);
        for (var slot = 1; slot <= SlotCount; slot++)
        {
            var path = SlotFilePath(slot);
            var exists = File.Exists(path);
            var doc = _slots[slot - 1].Current;
            list.Add(new DtrhSlotSummary(
                slot,
                exists,
                doc.Sparks,
                doc.Gold,
                doc.RunsCompleted,
                doc.BestScore,
                exists ? File.GetLastWriteTimeUtc(path) : null,
                doc.CraftedItems.ContainsKey("ragdoll"),
                doc.CraftedItems.ContainsKey("porcelain"),
                _slots[slot - 1].LastLoadOutcome?.IsDegraded == true));
        }

        return list;
    }

    /// <summary>The stitch-lock (WPF: ChaosSlotPickerWindow.xaml.cs:60-80): slots 2/3 are
    /// stitched shut until ANY save owns the Ragdoll / Porcelain craft; a pre-existing
    /// save keeps its own slot open (back-compat). A DEGRADED slot also stays open:
    /// re-locking would hide the quarantine flag behind a Stitched-Shut card (the WPF
    /// corrupt card stayed visible + deletable).</summary>
    public bool IsSlotLocked(int slot, IReadOnlyList<DtrhSlotSummary> summaries)
    {
        if (slot is < 2 or > SlotCount)
        {
            return false;
        }

        var anyDoll = false;
        foreach (var s in summaries)
        {
            anyDoll |= slot == 2 ? s.HasRagdoll : s.HasPorcelain;
        }

        return !anyDoll && !summaries[slot - 1].Exists && !summaries[slot - 1].Degraded;
    }

    /// <summary>Commit the chosen slot (WPF: ChaosMeta.SwitchSlot, ChaosUpgrades.cs:230-243):
    /// remember it in the index document (persisted immediately, like WPF's settings save).
    /// Deliberately does NOT bank the outgoing slot — its file is already current from the
    /// last flush, and banking would resurrect a just-deleted slot.</summary>
    public Task<OperationOutcome> SelectSlot(int slot)
    {
        slot = Clamp(slot);
        _index.Mutate(m => m.ActiveSlot = slot);
        _log.Log($"dtrh-slots: live save is now slot {slot}");
        return _index.Save();
    }

    /// <summary>Descend into a slot: select it, then persist a fresh document when the slot
    /// is empty — the save now exists (defines lastPlayed) and restart persistence has a
    /// file to prove. Greenfield choice recorded in SP-024 record Step 1 (WPF creates the
    /// file on the first meta flush; the user-observable outcome is identical).</summary>
    public async Task<OperationOutcome> DescendInto(int slot)
    {
        var outcome = await SelectSlot(slot).ConfigureAwait(false);
        if (outcome is not OperationOutcome.Completed)
        {
            return outcome;
        }

        if (!File.Exists(SlotFilePath(slot)))
        {
            return await _slots[Clamp(slot) - 1].Save().ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>Erase a slot back to "New Journey" (WPF: ChaosMetaStore.Delete,
    /// ChaosMetaStore.cs:116-131): delete the file + stray temp, then reload the slot's
    /// store so the in-memory state is flagged-empty too (deleting the ACTIVE slot and
    /// then descending into it correctly starts fresh — ChaosUpgrades.cs:224-229).
    /// Returns true if something was removed.</summary>
    public bool DeleteSlot(int slot)
    {
        slot = Clamp(slot);
        var removed = false;
        try
        {
            var path = SlotFilePath(slot);
            if (File.Exists(path)) { File.Delete(path); removed = true; }
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) { File.Delete(tmp); removed = true; }
        }
        catch (Exception ex)
        {
            _log.Log($"dtrh-slots: delete slot {slot} failed: {ex.Message}");
        }

        // Fresh store = SP-005 load of the now-missing file (flagged-empty defaults, not
        // dirty — the empty slot stays file-less until a descend persists it). The OLD
        // store is stopped first so an in-flight write can't resurrect the deleted file.
        var old = _slots[slot - 1];
        old.StopAsync().GetAwaiter().GetResult();
        var fresh = NewSlotStore(slot);
        fresh.StartAsync(default).GetAwaiter().GetResult();
        _slots[slot - 1] = fresh;
        if (removed)
        {
            _log.Log($"dtrh-slots: deleted save slot {slot}");
        }

        return removed;
    }
}
