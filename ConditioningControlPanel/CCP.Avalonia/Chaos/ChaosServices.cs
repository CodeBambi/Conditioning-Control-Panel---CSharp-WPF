using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>DI-backed singleton state for the Avalonia Chaos environment seam.</summary>
public interface IChaosEnvironment
{
    string? EffectiveAssetsPath { get; set; }
    IAvaloniaBubbleService? Bubbles { get; set; }
    bool VideoIsPlaying { get; }
}

/// <summary>Singleton implementation of <see cref="IChaosEnvironment"/>.</summary>
public sealed class ChaosEnvironment : IChaosEnvironment
{
    private readonly IServiceProvider _services;

    public ChaosEnvironment(IServiceProvider services)
    {
        _services = services;
    }

    public string? EffectiveAssetsPath { get; set; }

    public IAvaloniaBubbleService? Bubbles { get; set; }

    public bool VideoIsPlaying => _services.GetService<IVideoService>()?.IsRunning ?? false;
}

/// <summary>DI-backed singleton state for the Avalonia Chaos play-mode seam.</summary>
public interface IChaosModeState
{
    ChaosPlayMode ActiveMode { get; set; }
    bool DesktopMode { get; }

    /// <summary>
    /// Whether chaos windows are pinned into the topmost band. WPF parity
    /// (ChaosModeService.cs:320, ChaosWindowZ.PinTopmost): driven by the user's
    /// AppSettings.ChaosPinOnTop (snapshot at run start, default true) "regardless of mode" —
    /// NOT by DesktopMode. The old DesktopMode-driven inversion (every Free Desktop run born
    /// non-topmost and actively demoted) is the exact behavior WPF classified as a bug and
    /// deliberately abandoned; do not couple this back to <see cref="DesktopMode"/>.
    /// </summary>
    bool PinTopmost { get; set; }

    bool BornTopmost { get; }
    bool NarrativeActive { get; }
}

/// <summary>Singleton implementation of <see cref="IChaosModeState"/>.</summary>
public sealed class ChaosModeState : IChaosModeState
{
    private readonly IServiceProvider _services;

    public ChaosModeState(IServiceProvider services)
    {
        _services = services;
    }

    public ChaosPlayMode ActiveMode { get; set; } = ChaosPlayMode.Story;

    public bool DesktopMode => ActiveMode == ChaosPlayMode.FreeDesktop;

    /// <summary>
    /// Snapshot of AppSettings.ChaosPinOnTop taken at StartRun and reset to true after the
    /// run (WPF ChaosModeService.cs:3266 leak guard: dashboard trigger overlays must never
    /// inherit a stale demote). Default true = pinned.
    /// </summary>
    public bool PinTopmost { get; set; } = true;

    // WPF parity: topmost is decoupled from DesktopMode (ChaosWindowZ.cs:35 BornTopmost =>
    // PinTopmost; ChaosWindowZ.DesktopMode is write-only in WPF). DesktopMode keeps gating
    // its non-topmost traits (backdrop/narrative/ambient) only.
    public bool BornTopmost => PinTopmost;

    public bool NarrativeActive =>
        _services.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current?.NarrativeModeEnabled == true
        && ActiveMode == ChaosPlayMode.Story;
}

/// <summary>DI-backed singleton service for Chaos meta-progression.</summary>
public interface IChaosMetaService
{
    ChaosMetaState State { get; set; }
    string Rank { get; }
    ChaosRank CurrentRank { get; }
    int RankIndex { get; }

    void Init(IAppEnvironment env);
    void Save();
    bool AtLeast(ChaosRank rank);
    void AddGold(int amount);
    bool TrySpendGold(int amount);
    void EquipStartBoon(string? boonId);
    void ApplyTo(ChaosRunConfig config);
    void MarkDiscovered(string codexId);
    bool IsDiscovered(string codexId);
    bool IsOwned(string id);
    bool IsUpgradeActive(string id);
    void SetUpgradeActive(string id, bool active);
    bool CanAfford(string id);
    bool CanAffordUnlock(string id);
    bool CanAffordUpgrade(string id);
    bool IsPurchaseRankLocked(string id);
    bool IsBoonRankLocked(string id);
    bool IsAccessoryScriptLocked(string id);
    bool IsBoonUnlocked(string id);
    bool IsBoonActive(string id);
    void SetBoonActive(string id, bool active);
    int BoonLevel(string id);
    bool TryUnlockBoon(string id);
    bool TryUpgradeBoon(string id);
    bool TryPurchase(string id);
    bool HasFreePocket(ChaosBoonCategory cat);
    int SlotsFor(ChaosBoonCategory cat);
    int EquippedCountIn(ChaosBoonCategory cat);
    (string Name, bool Affordable, string? LessonId, int Cost)? NextGoal();
    void DebugResetState();
}

/// <summary>Singleton implementation of <see cref="IChaosMetaService"/>.</summary>
public sealed class ChaosMetaService : IChaosMetaService
{
    private readonly JsonSerializerOptions _loadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly JsonSerializerOptions _saveOptions = new() { WriteIndented = true };

    private string? _filePath;

    public ChaosMetaState State { get; set; } = new();

    public string Rank => ChaosRanks.Name(CurrentRank);

    public ChaosRank CurrentRank => ChaosRanks.For(State.RunsCompleted);

    public int RankIndex => (int)CurrentRank;

    public void Init(IAppEnvironment env)
    {
        _filePath = Path.Combine(env.UserDataPath, "chaos_meta.json");
        State = LoadState();
        RefundRetiredBoons();
        SanitizePockets();
    }

    private ChaosMetaState LoadState()
    {
        try
        {
            var path = _filePath;
            if (string.IsNullOrEmpty(path)) return new ChaosMetaState();
            var tempPath = path + ".tmp";

            if (File.Exists(tempPath) && !File.Exists(path))
            {
                try { File.Move(tempPath, path); } catch { }
            }
            else if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            if (!File.Exists(path)) return new ChaosMetaState();

            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<ChaosMetaState>(json, _loadOptions);
            if (state == null)
            {
                LogWarning("ChaosMeta: chaos_meta.json parsed to null; using fresh meta state");
                return new ChaosMetaState();
            }

            state.PurchasedUpgrades ??= new();
            state.DisabledUpgrades ??= new();
            state.DiscoveredCodexIds ??= new();
            state.LifetimeBoonLevels ??= new();
            state.ActiveLifetimeBoons ??= new();
            state.BenchPurchases ??= new();
            state.LessonProgress ??= new();
            state.LessonsComplete ??= new();
            state.PendingReveals ??= new();
            state.SeenReveals ??= new();
            state.FirstTimesAwarded ??= new();
            state.BubbleHintsLearned ??= new();
            state.SeenNarrativeLines ??= new();
            state.NarrativeCooldownEnds ??= new();
            return state;
        }
        catch (Exception ex)
        {
            LogWarning("ChaosMeta.Load failed ({Error}); using fresh meta state", ex.Message);
            return new ChaosMetaState();
        }
    }

    public void Save()
    {
        try
        {
            var path = _filePath;
            if (string.IsNullOrEmpty(path)) return;
            var tempPath = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var json = JsonSerializer.Serialize(State, _saveOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            LogWarning("ChaosMeta.Save failed: {Error}", ex.Message);
        }
    }

    public bool AtLeast(ChaosRank rank) => CurrentRank >= rank;

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        State.Gold += amount;
        Save();
    }

    public bool TrySpendGold(int amount)
    {
        if (amount < 0 || State.Gold < amount) return false;
        State.Gold -= amount;
        Save();
        return true;
    }

    public void EquipStartBoon(string? boonId)
    {
        State.EquippedStartBoon = boonId;
        Save();
    }

    /// <summary>Apply every owned-and-switched-on upgrade's effect to a freshly-built run
    /// config (verbatim WPF ChaosUpgrades.cs:312-318).</summary>
    public void ApplyTo(ChaosRunConfig config)
    {
        if (config == null) return;
        foreach (var id in State.PurchasedUpgrades)
            if (IsUpgradeActive(id))
                ChaosUpgrades.ById(id)?.Apply(config);
    }

    public void MarkDiscovered(string codexId)
    {
        if (string.IsNullOrEmpty(codexId)) return;
        if (State.DiscoveredCodexIds.Add(codexId)) Save();
    }

    public bool IsDiscovered(string codexId) =>
        !string.IsNullOrEmpty(codexId) && State.DiscoveredCodexIds.Contains(codexId);

    public bool IsOwned(string id) => State.PurchasedUpgrades.Contains(id);

    public bool IsUpgradeActive(string id) =>
        IsOwned(id) && !State.DisabledUpgrades.Contains(id);

    public void SetUpgradeActive(string id, bool active)
    {
        if (!IsOwned(id)) return;
        bool changed = active ? State.DisabledUpgrades.Remove(id) : State.DisabledUpgrades.Add(id);
        if (changed) Save();
    }

    public bool CanAfford(string id)
    {
        var u = ChaosUpgrades.ById(id);
        return u != null && !IsOwned(id) && State.Sparks >= u.Cost;
    }

    public bool CanAffordUnlock(string id)
    {
        var c = UnlockCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    public bool CanAffordUpgrade(string id)
    {
        var c = NextUpgradeCostOf(id);
        return c.HasValue && State.Sparks >= c.Value;
    }

    public bool IsPurchaseRankLocked(string id) =>
        id == "extreme_tier" && !IsOwned(id) && !AtLeast(ChaosRank.Devoted);

    public bool IsBoonRankLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return b != null && !IsBoonUnlocked(id) && !AtLeast(b.RankFloor);
    }

    public bool IsAccessoryScriptLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return b != null
            && b.Category == ChaosBoonCategory.Accessory
            && b.Id != "the_spanker"
            && !IsBoonUnlocked("the_spanker");
    }

    public bool IsBoonUnlocked(string id) => BoonLevel(id) >= 1;

    public bool IsBoonActive(string id) =>
        State.ActiveLifetimeBoons.Contains(id) && IsBoonUnlocked(id);

    public void SetBoonActive(string id, bool active)
    {
        if (active)
        {
            if (!IsBoonUnlocked(id)) return;
            var cat = ChaosLifetimeBoons.ById(id)?.Category ?? ChaosBoonCategory.Utility;
            if (!IsBoonActive(id) && !HasFreePocket(cat)) return;
        }
        bool changed = active ? State.ActiveLifetimeBoons.Add(id) : State.ActiveLifetimeBoons.Remove(id);
        if (changed) Save();
    }

    public int BoonLevel(string id) =>
        State.LifetimeBoonLevels.TryGetValue(id, out var l) ? l : 0;

    public bool TryUnlockBoon(string id)
    {
        if (IsBoonRankLocked(id)) return false;
        if (ChaosLessons.IsLessonBlocked(id)) return false;
        if (IsAccessoryScriptLocked(id)) return false;
        var c = UnlockCostOf(id);
        if (!c.HasValue || State.Sparks < c.Value) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels[id] = 1;
        var cat = ChaosLifetimeBoons.ById(id)?.Category ?? ChaosBoonCategory.Utility;
        if (HasFreePocket(cat)) State.ActiveLifetimeBoons.Add(id);
        Save();
        return true;
    }

    public bool TryUpgradeBoon(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        var c = NextUpgradeCostOf(id);
        if (b == null || !c.HasValue || State.Sparks < c.Value) return false;
        if (BoonLevel(id) + 1 >= b.MaxLevel && !AtLeast(ChaosRank.Devoted)) return false;
        State.Sparks -= c.Value;
        State.LifetimeBoonLevels[id] = Math.Min(BoonLevel(id) + 1, b.MaxLevel);
        Save();
        return true;
    }

    public bool TryPurchase(string id)
    {
        var u = ChaosUpgrades.ById(id);
        if (u == null) return false;
        if (ChaosLessons.IsLessonBlocked(id)) return false;
        if (State.PurchasedUpgrades.Contains(id)) return false;
        if (State.Sparks < u.Cost) return false;
        if (IsPurchaseRankLocked(id)) return false;

        State.Sparks -= u.Cost;
        State.PurchasedUpgrades.Add(id);
        if (id == "extreme_tier") State.ExtremeUnlocked = true;
        Save();
        return true;
    }

    public bool HasFreePocket(ChaosBoonCategory cat) => EquippedCountIn(cat) < SlotsFor(cat);

    public const int MAX_POCKETS_PER_CATEGORY = 2;

    public int SlotsFor(ChaosBoonCategory cat) => cat switch
    {
        ChaosBoonCategory.Utility => int.MaxValue,
        ChaosBoonCategory.Skill => Math.Min(State.ToyPockets, MAX_POCKETS_PER_CATEGORY),
        ChaosBoonCategory.Accessory => Math.Min(State.AccessoryPockets, MAX_POCKETS_PER_CATEGORY),
        _ => 0,
    };

    public int EquippedCountIn(ChaosBoonCategory cat) =>
        State.ActiveLifetimeBoons.Count(id => ChaosLifetimeBoons.ById(id)?.Category == cat && IsBoonUnlocked(id));

    public (string Name, bool Affordable, string? LessonId, int Cost)? NextGoal()
    {
        ChaosNextGoal? bestAffordable = null, bestAny = null;
        void Consider(string id, string name, int cost, string? lessonId)
        {
            bool affordable = lessonId == null && cost <= State.Sparks;
            var g = new ChaosNextGoal(id, name, cost, lessonId, affordable);
            if (bestAny == null || cost < bestAny.Value.Cost) bestAny = g;
            if (g.Affordable && (bestAffordable == null || cost < bestAffordable.Value.Cost)) bestAffordable = g;
        }

        foreach (var u in ChaosUpgrades.All)
        {
            if (IsOwned(u.Id) || IsPurchaseRankLocked(u.Id)) continue;
            Consider(u.Id, u.Name, u.Cost,
                ChaosLessons.IsLessonBlocked(u.Id) ? u.Id : null);
        }
        foreach (var b in ChaosLifetimeBoons.All)
        {
            int level = BoonLevel(b.Id);
            if (level <= 0)
            {
                if (IsAccessoryScriptLocked(b.Id)) continue;
                Consider(b.Id, b.Name, b.UnlockCost,
                    ChaosLessons.IsLessonBlocked(b.Id) ? b.Id : null);
            }
            else if (level < b.MaxLevel && !IsCapstonePurchaseRankLocked(b.Id))
            {
                int cost = NextUpgradeCostOf(b.Id) ?? 0;
                if (cost > 0) Consider(b.Id, b.Name, cost, null);
            }
        }
        var result = bestAffordable ?? bestAny;
        return result.HasValue ? (result.Value.Name, result.Value.Affordable, result.Value.LessonId, result.Value.Cost) : null;
    }

    private int? UnlockCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return (b == null || IsBoonUnlocked(id)) ? null : b.UnlockCost;
    }

    private int? NextUpgradeCostOf(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return null;
        int lvl = BoonLevel(id);
        if (lvl < 1 || lvl >= b.MaxLevel) return null;
        return (lvl - 1) < b.UpgradeCosts.Length ? b.UpgradeCosts[lvl - 1] : null;
    }

    private bool IsCapstonePurchaseRankLocked(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return false;
        int lvl = BoonLevel(id);
        return lvl >= 1 && lvl < b.MaxLevel && lvl + 1 >= b.MaxLevel && !AtLeast(ChaosRank.Devoted);
    }

    private void SanitizePockets()
    {
        try
        {
            bool changed = false;
            if (State.ToyPockets > MAX_POCKETS_PER_CATEGORY) { State.ToyPockets = MAX_POCKETS_PER_CATEGORY; changed = true; }
            if (State.AccessoryPockets > MAX_POCKETS_PER_CATEGORY) { State.AccessoryPockets = MAX_POCKETS_PER_CATEGORY; changed = true; }
            foreach (var cat in new[] { ChaosBoonCategory.Skill, ChaosBoonCategory.Accessory })
            {
                int keep = SlotsFor(cat);
                foreach (var b in ChaosLifetimeBoons.InCategory(cat))
                {
                    if (!IsBoonActive(b.Id)) continue;
                    if (keep > 0) { keep--; continue; }
                    State.ActiveLifetimeBoons.Remove(b.Id);
                    changed = true;
                    LogInformation("Chaos: unequipped {Id} (over the {Cat} pocket cap)", b.Id, cat);
                }
            }
            if (changed) Save();
        }
        catch (Exception ex) { LogWarning("Chaos: pocket sanitize failed ({E})", ex.Message); }
    }

    private static readonly Dictionary<string, int[]> RetiredBoonRefunds = new()
    {
        ["muscle_memory"] = new[] { 200, 400, 700, 1150, 1800 },
        ["magic_wand"] = new[] { 150, 300, 550, 950, 1550 },
    };

    private void RefundRetiredBoons()
    {
        try
        {
            bool changed = false;
            foreach (var (id, costs) in RetiredBoonRefunds)
            {
                if (State.LifetimeBoonLevels.TryGetValue(id, out int lvl) && lvl >= 1)
                {
                    int refund = costs[Math.Clamp(lvl, 1, costs.Length) - 1];
                    State.Sparks += refund;
                    State.LifetimeBoonLevels.Remove(id);
                    changed = true;
                    LogInformation("Chaos: retired boon {Id} (L{Lvl}) refunded ✦{Refund}", id, lvl, refund);
                }
                if (State.ActiveLifetimeBoons.Remove(id)) changed = true;
            }
            if (State.PurchasedUpgrades.Remove("spark_gain")) changed = true;
            if (State.DisabledUpgrades.Remove("spark_gain")) changed = true;
            var retiredHabitRefunds = new Dictionary<string, int>
            {
                ["bigger_hitboxes"] = 80, ["magnet"] = 150, ["shield_recharge"] = 200,
                ["start_shield"] = 100, ["collar"] = 200, ["pendulum"] = 220,
                ["base_mult"] = 90, ["golden_touch"] = 130, ["take_more"] = 400,
                ["tunnel_vision"] = 140, ["max_bubbles"] = 110,
            };
            foreach (var (id, refund) in retiredHabitRefunds)
            {
                if (State.PurchasedUpgrades.Remove(id))
                {
                    State.Sparks += refund;
                    changed = true;
                    LogInformation("Chaos: retired habit {Id} refunded ✦{Refund}", id, refund);
                }
                if (State.DisabledUpgrades.Remove(id)) changed = true;
            }
            foreach (var id in new[] { "tunnel_vision", "pendulum" })
            {
                if (State.LifetimeBoonLevels.Remove(id)) changed = true;
                if (State.ActiveLifetimeBoons.Remove(id)) changed = true;
            }
            if (changed) Save();
        }
        catch (Exception ex) { LogWarning("Chaos: retired-boon refund failed ({E})", ex.Message); }
    }

    public void DebugResetState()
    {
        State = new ChaosMetaState();
        Save();
        LogWarning("ChaosMeta: state RESET via debug strip");
    }

    private void LogWarning(string message, params object?[] args)
    {
        try { global::ConditioningControlPanel.CoreApp.Logger?.LogWarning(message, args); } catch { }
    }

    private void LogInformation(string message, params object?[] args)
    {
        try { global::ConditioningControlPanel.CoreApp.Logger?.LogInformation(message, args); } catch { }
    }

    private readonly record struct ChaosNextGoal(string Id, string Name, int Cost, string? LessonId, bool Affordable);
}

/// <summary>DI-backed singleton service for Chaos feature reveals.</summary>
public interface IRevealService
{
    event Action<string>? Pending;

    bool IsUnlocked(string id);
    bool IsPending(string id);
    bool IsSeen(string id);
    bool Clamp(string id, bool userSetting);
    void Sync(string reason);
    IReadOnlyList<string> PendingIds();
    void MarkSeen(string id);
}

/// <summary>Singleton implementation of <see cref="IRevealService"/>.</summary>
public sealed class RevealServiceImpl : IRevealService
{
    private readonly IChaosMetaService _meta;
    private readonly Dictionary<string, Func<bool>> _registry;

    public RevealServiceImpl(IChaosMetaService meta)
    {
        _meta = meta;
        _registry = new()
        {
            [RevealIds.Dollhouse] = () => meta.State.RunsCompleted >= 1,
            [RevealIds.TabLookingGlass] = () => meta.RankIndex >= (int)ChaosRank.Slipping,
            [RevealIds.SectionToys] = () => meta.State.ToyPockets >= 1,
            [RevealIds.SectionAccessories] = () => meta.State.AccessoryPockets >= 1,
            [RevealIds.HerCorner] = () => meta.State.RunsCompleted >= 2 && meta.RankIndex < (int)ChaosRank.Slipping,
            [RevealIds.PillTeasing] = () => meta.RankIndex >= (int)ChaosRank.Tempted,
            [RevealIds.PillRelentless] = () => meta.RankIndex >= (int)ChaosRank.Entranced,
            [RevealIds.PillInescapable] = () => meta.State.ExtremeUnlocked,
            [RevealIds.DraftSkip] = () => meta.State.RunsCompleted >= 2,
            [RevealIds.StartPicker] = () => meta.State.BenchPurchases.Contains(BenchIds.StartMantra),
            [RevealIds.Diary] = () => meta.State.BenchPurchases.Contains(BenchIds.Diary),
            [RevealIds.StatsPanel] = () => meta.State.BenchPurchases.Contains(BenchIds.StatsPanel),
            [RevealIds.BenchToyPocket2] = () => meta.RankIndex >= (int)ChaosRank.Devoted,
            [RevealIds.BenchAccPocket2] = () => meta.RankIndex >= (int)ChaosRank.Devoted,
            [RevealIds.VariantVideo] = () => meta.RankIndex >= (int)ChaosRank.Entranced,
            [RevealIds.VariantHtlink] = () => meta.RankIndex >= (int)ChaosRank.Entranced,
            [RevealIds.Capstones] = () => meta.RankIndex >= (int)ChaosRank.Devoted,
            [RevealIds.ExtremeTierRow] = () => meta.RankIndex >= (int)ChaosRank.Devoted,
        };
    }

    public event Action<string>? Pending;

    public bool IsUnlocked(string id) =>
        !_registry.TryGetValue(id, out var pred) || SafePred(pred);

    public bool IsPending(string id) => _meta.State.PendingReveals.Contains(id);

    public bool IsSeen(string id) => _meta.State.SeenReveals.Contains(id);

    public bool Clamp(string id, bool userSetting) => userSetting && IsUnlocked(id);

    public void Sync(string reason)
    {
        try
        {
            bool changed = false;
            foreach (var (id, pred) in _registry)
            {
                if (!SafePred(pred)) continue;
                if (_meta.State.SeenReveals.Contains(id)) continue;
                if (!_meta.State.PendingReveals.Add(id)) continue;
                changed = true;
                Log("Chaos reveal pending: {Id} ({Reason})", id, reason);
                try { Pending?.Invoke(id); } catch { }
            }
            if (changed) _meta.Save();
        }
        catch (Exception ex) { Log("RevealService.Sync failed ({E})", ex.Message); }
    }

    public IReadOnlyList<string> PendingIds() => _meta.State.PendingReveals.ToList();

    public void MarkSeen(string id)
    {
        bool changed = _meta.State.PendingReveals.Remove(id);
        changed |= _meta.State.SeenReveals.Add(id);
        if (changed) _meta.Save();
    }

    private static bool SafePred(Func<bool> pred)
    {
        try { return pred(); } catch { return false; }
    }

    private void Log(string message, params object?[] args)
    {
        try { global::ConditioningControlPanel.CoreApp.Logger?.LogInformation(message, args); } catch { }
    }
}
