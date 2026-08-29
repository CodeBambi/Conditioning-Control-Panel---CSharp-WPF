using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Event args for quest completion
/// </summary>
public class QuestCompletedEventArgs : EventArgs
{
    public QuestDefinition QuestDefinition { get; }
    public int XPAwarded { get; }
    public QuestType QuestType { get; }

    public QuestCompletedEventArgs(QuestDefinition def, int xp, QuestType type)
    {
        QuestDefinition = def;
        XPAwarded = xp;
        QuestType = type;
    }
}

/// <summary>
/// Event args for quest progress updates
/// </summary>
public class QuestProgressEventArgs : EventArgs
{
    public QuestType QuestType { get; }
    public int CurrentProgress { get; }
    public int TargetValue { get; }

    public QuestProgressEventArgs(QuestType type, int current, int target)
    {
        QuestType = type;
        CurrentProgress = current;
        TargetValue = target;
    }
}

/// <summary>
/// Service for managing daily and weekly quests
/// </summary>
public class QuestService : IDisposable
{
    private readonly string _progressPath;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Random _random = new();
    private bool _isDirty;

    // #889: Patreon validation is asynchronous, so at launch the entitlement gate can still read
    // "no access" for a paying patron — and the premium-loss rerolls below then destroyed their
    // premium quest on every single launch. A deferred decision costs at most one refresh tick.
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private static readonly TimeSpan EntitlementSettleWindow = TimeSpan.FromSeconds(90);
    private bool _premiumRecheckPending;

    // Accumulators for fractional minutes (time-based quests are called with small increments)
    private double _spiralMinutesAccumulator;
    private double _pinkFilterMinutesAccumulator;
    private double _brainDrainMinutesAccumulator;
    private double _videoMinutesAccumulator;
    private double _combinedMinutesAccumulator;
    private double _autonomyMinutesAccumulator;

    public QuestProgress Progress { get; private set; }

    // The other half of the same problem: a slot ROLLED while the entitlement was unresolved
    // drew from the free-only pool (IsQuestAvailableForTier reads the same "no access"), and a
    // patron then wore a free quest for the whole day. Remembered per slot so the recheck can
    // re-roll it — and only it — once the answer lands. These are pure proxies onto the
    // persisted QuestProgress fields: a quit inside the settle window must not lose the flag,
    // and a single source of truth keeps the file and the session in step.
    private bool DailyRolledUnresolved
    {
        get => Progress.DailyRolledUnresolved;
        set
        {
            if (Progress.DailyRolledUnresolved == value) return;
            Progress.DailyRolledUnresolved = value;
            _isDirty = true;
        }
    }

    private bool WeeklyRolledUnresolved
    {
        get => Progress.WeeklyRolledUnresolved;
        set
        {
            if (Progress.WeeklyRolledUnresolved == value) return;
            Progress.WeeklyRolledUnresolved = value;
            _isDirty = true;
        }
    }

    public event EventHandler<QuestCompletedEventArgs>? QuestCompleted;
    public event EventHandler<QuestProgressEventArgs>? QuestProgressChanged;
    public event EventHandler? QuestsRefreshed;

    public QuestService()
    {
        _progressPath = Path.Combine(
            App.UserDataPath,
            "quests.json");

        Progress = LoadProgress();

        // Check for expired quests and generate new ones
        CheckAndGenerateQuests();

        // Auto-save every 30 seconds if dirty (off UI thread)
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _saveTimer.Tick += (s, e) =>
        {
            if (_isDirty)
            {
                _isDirty = false;
                var json = JsonSerializer.Serialize(Progress, new JsonSerializerOptions { WriteIndented = true });
                var path = _progressPath;
                var tmpPath = path + ".tmp";
                _ = Task.Run(() =>
                {
                    try
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        // Atomic write: write to .tmp first, then rename
                        File.WriteAllText(tmpPath, json);
                        File.Move(tmpPath, path, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.Error(ex, "Failed to save quest progress");
                    }
                });
            }
        };
        _saveTimer.Start();

        // Quest refresh timer — detect day/week rollover while app is running
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _refreshTimer.Tick += (s, e) =>
        {
            var dailyExpired = Progress.IsDailyExpired();
            var weeklyExpired = Progress.IsWeeklyExpired();
            // A premium-loss decision deferred at launch (#889) is retried here, once the
            // entitlement answer has landed.
            var premiumRecheck = _premiumRecheckPending && IsEntitlementResolved();
            if (dailyExpired || weeklyExpired || premiumRecheck)
            {
                if (premiumRecheck)
                {
                    // NOT cleared here: CheckAndGenerateQuests recomputes the flag from what is
                    // still deferred. Clearing it up front dropped the re-roll for good if the
                    // entitlement flapped back to unresolved before the inner check ran.
                    App.Logger?.Information("Quest premium recheck: entitlement resolved, reconciling deferred quests");
                }
                else
                {
                    App.Logger?.Information("Quest rollover detected (daily={Daily}, weekly={Weekly})", dailyExpired, weeklyExpired);
                }
                CheckAndGenerateQuests();
                QuestsRefreshed?.Invoke(this, EventArgs.Empty);
            }
        };
        _refreshTimer.Start();

        App.Logger?.Information("QuestService initialized. Daily board: [{Daily}], Weekly: {Weekly}",
            string.Join(", ", Progress.DailyQuests.Select(q => q?.DefinitionId ?? "empty")),
            Progress.WeeklyQuest?.DefinitionId ?? "none");
    }

    #region Persistence

    private QuestProgress LoadProgress()
    {
        var tmpPath = _progressPath + ".tmp";

        // Try loading from main file first
        if (File.Exists(_progressPath))
        {
            try
            {
                var json = File.ReadAllText(_progressPath);
                return JsonSerializer.Deserialize<QuestProgress>(json) ?? new QuestProgress();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Quest progress file corrupted, attempting recovery from .tmp");
            }
        }

        // Main file missing or corrupt — try recovering from .tmp
        if (File.Exists(tmpPath))
        {
            try
            {
                var json = File.ReadAllText(tmpPath);
                var progress = JsonSerializer.Deserialize<QuestProgress>(json);
                if (progress != null)
                {
                    App.Logger?.Warning("Recovered quest progress from .tmp file");
                    // Promote .tmp to main file so future loads succeed normally
                    try { File.Move(tmpPath, _progressPath, overwrite: true); } catch { }
                    return progress;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to recover quest progress from .tmp file");
            }
        }

        return new QuestProgress();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_progressPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(Progress, new JsonSerializerOptions { WriteIndented = true });

            // Atomic write: write to .tmp first, then rename to prevent corruption on crash
            var tmpPath = _progressPath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, _progressPath, overwrite: true);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to save quest progress");
        }
    }

    #endregion

    #region Quest Generation

    /// <summary>
    /// Check for expired quests and generate new ones if needed.
    /// Also regenerates quests whose definitions are no longer available (removed from server).
    /// </summary>
    public void CheckAndGenerateQuests()
    {
        bool changed = false;

        // The recheck flag is recomputed by this pass, not by the caller: every path below that
        // defers a decision (CanDropPremiumQuest, the generators, the re-roll blocks at the end)
        // re-arms it, so a decision can never be lost between the arming and the retry.
        _premiumRecheckPending = false;

        // THE DAILY BOARD. All three of today's quests are dealt at once and reconciled as a
        // set - rollover, migration from the old one-at-a-time file, top-up, and the per-slot
        // "this quest is no longer legal for you" rerolls all live in ReconcileDailySlots.
        if (ReconcileDailySlots()) changed = true;

        // Check weekly quest
        if (Progress.IsWeeklyExpired() || Progress.WeeklyQuest == null)
        {
            GenerateNewWeeklyQuest();
            changed = true;
        }

        // If weekly quest definition is missing (removed from server), regenerate
        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted && GetCurrentWeeklyDefinition() == null)
        {
            App.Logger?.Information("Weekly quest definition '{QuestId}' no longer available, regenerating",
                Progress.WeeklyQuest.DefinitionId);
            GenerateNewWeeklyQuest();
            changed = true;
        }

        // (The daily equivalents of the two guards below are per-slot, inside ReconcileDailySlots.)

        // If weekly quest requires premium but access was lost, regenerate a free one
        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted)
        {
            var weeklyDef = GetCurrentWeeklyDefinition();
            if (weeklyDef != null && !IsQuestAvailableForTier(weeklyDef)
                && CanDropPremiumQuest(Progress.WeeklyQuest, "weekly"))
            {
                App.Logger?.Information("Weekly quest '{QuestId}' requires premium (access lost), regenerating",
                    Progress.WeeklyQuest.DefinitionId);
                GenerateNewWeeklyQuest();
                changed = true;
            }
        }

        // If weekly quest's feature is locked at current level, regenerate
        if (Progress.WeeklyQuest != null && !Progress.WeeklyQuest.IsCompleted)
        {
            var weeklyDef = GetCurrentWeeklyDefinition();
            if (weeklyDef != null && !IsQuestAvailableForLevel(weeklyDef.Category))
            {
                App.Logger?.Information("Weekly quest '{QuestId}' requires locked feature ({Category}), regenerating",
                    Progress.WeeklyQuest.DefinitionId, weeklyDef.Category);
                GenerateNewWeeklyQuest();
                changed = true;
            }
        }

        // The premium-loss rerolls above only defend a quest that already EXISTS. A slot rolled
        // while the entitlement was unresolved (launch on a fresh day is the common case) drew
        // from the free pool with nothing to defend, and no recheck was ever armed. Now that the
        // answer has landed as premium, re-roll the slot against the blended pool — but only
        // while it is untouched, so a quest the player has already worked on is never taken away.
        // (The daily half of this is per-slot, inside ReconcileDailySlots.)

        if (WeeklyRolledUnresolved && IsEntitlementResolved())
        {
            WeeklyRolledUnresolved = false;
            if (App.Patreon?.HasPremiumAccess == true && Progress.WeeklyQuest != null
                && !Progress.WeeklyQuest.IsCompleted && Progress.WeeklyQuest.CurrentProgress == 0)
            {
                App.Logger?.Information("Weekly quest '{QuestId}' was rolled before the entitlement resolved — re-rolling with premium access",
                    Progress.WeeklyQuest.DefinitionId);
                GenerateNewWeeklyQuest();
                changed = true;
            }
        }

        // Anything still flagged is still waiting on the answer — keep the tick armed. This is
        // also what re-arms the flag after a launch: the slot flags survive in quests.json but
        // _premiumRecheckPending does not.
        if (DailyRolledUnresolved || WeeklyRolledUnresolved) _premiumRecheckPending = true;

        if (changed)
        {
            _isDirty = true;
            Save();
        }
    }

    // ============================ THE DAILY BOARD ============================
    //
    // A day deals THREE daily quests at once (Progress.DailyQuests) instead of handing them out
    // one after another. Everything a slot can need - dealing, rolling over at midnight, migrating
    // a pre-three-up save, topping the board back up to three, and dropping a quest that has
    // stopped being legal for this player - is reconciled here, as a set, from live state. The old
    // one-slot code path did the same work in four scattered ifs against Progress.DailyQuest; that
    // field is now only a legacy mirror (see SyncLegacyDailyMirror).

    /// <summary>
    /// Bring today's three daily slots into a legal state. Idempotent: safe to call on every
    /// startup, every refresh tick and after any completion.
    /// </summary>
    /// <returns>True if anything about the board changed (caller saves).</returns>
    private bool ReconcileDailySlots()
    {
        bool changed = false;
        var slots = Progress.DailyQuests;

        // ---- 1. MIDNIGHT. A new day throws the board away, but a quest FINISHED today is
        // carried onto the new board rather than dropped: a session that runs past midnight
        // used to lose the completion count that proved it happened (the old code patched the
        // counter by hand for exactly this case). Carrying the stamped card forward keeps the
        // count honest now that the count is derived from the board.
        if (Progress.IsDailyExpired())
        {
            var carried = new List<ActiveQuest>();
            var carriedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var q in EnumerateDailySlotsIncludingLegacy())
            {
                if (!q.IsCompleted || q.CompletedAt?.Date != DateTime.Today) continue;
                // One card per definition: a deserialized legacy twin must not be carried
                // alongside the slot it mirrors.
                if (!string.IsNullOrEmpty(q.DefinitionId) && !carriedIds.Add(q.DefinitionId)) continue;
                carried.Add(q);
            }

            slots.Clear();
            foreach (var q in carried.Take(MaxDailyQuestsPerDay)) slots.Add(q);
            Progress.DailyQuest = null;

            // Stamped BEFORE the pool is consulted: an empty pool must not leave the board
            // looking permanently expired, or every tick would re-clear it.
            Progress.DailyQuestGeneratedAt = DateTime.Now;
            Progress.GetDailyQuestsCompletedToday();   // resets/derives the counter for the new day
            changed = true;

            if (carried.Count > 0)
                App.Logger?.Information("Quest day rollover: carried {Count} quest(s) completed after midnight onto today's board", carried.Count);
        }

        // ---- 2. MIGRATION. A quests.json written by a pre-three-up build has one quest and a
        // completion counter. The quest the player was actually working on keeps its progress and
        // its place; the counter is honoured by stamping that many further slots as already-earned,
        // so an update landing mid-afternoon cannot hand the day's XP out twice.
        if (slots.Count == 0 && Progress.DailyQuest != null)
        {
            int alreadyEarned = Math.Max(0, Math.Min(MaxDailyQuestsPerDay, Progress.DailyQuestsCompletedToday));

            var legacy = Progress.DailyQuest;
            slots.Add(legacy);
            if (legacy.IsCompleted) alreadyEarned = Math.Max(0, alreadyEarned - 1);

            for (int i = 0; i < alreadyEarned && slots.Count < MaxDailyQuestsPerDay; i++)
            {
                var filler = RollDailyQuest(DailyBoardIds());
                if (filler == null) break;
                filler.IsCompleted = true;
                filler.CompletedAt = DateTime.Now;
                slots.Add(filler);
            }

            changed = true;
            App.Logger?.Information("Migrated single-slot daily quest to the three-up board (kept '{QuestId}', {Earned} slot(s) already earned today)",
                legacy.DefinitionId, alreadyEarned);
        }

        // ---- 3. TOP UP to three. Also the first-ever deal.
        while (slots.Count < MaxDailyQuestsPerDay)
        {
            var rolled = RollDailyQuest(DailyBoardIds());
            if (rolled == null) break;          // pool is empty - never spin
            slots.Add(rolled);
            changed = true;
        }

        // ---- 4. PER-SLOT LEGALITY. A finished slot is a record and is never touched; an
        // unfinished one is replaced if its definition vanished from the server, if it needs a
        // tier the player no longer has, or if it needs a feature their level has not unlocked.
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.IsCompleted) continue;

            var def = GetDailyDefinition(slot);
            string? reason = null;

            if (def == null)
            {
                reason = "definition no longer available";
            }
            else if (!IsQuestAvailableForTier(def) && CanDropPremiumQuest(slot, "daily slot " + (i + 1)))
            {
                reason = "requires premium (access lost)";
            }
            else if (!IsQuestAvailableForLevel(def.Category))
            {
                reason = "requires locked feature (" + def.Category + ")";
            }

            if (reason == null) continue;

            var replacement = RollDailyQuest(DailyBoardIds(skipIndex: i));
            if (replacement == null) continue;

            App.Logger?.Information("Daily slot {Slot}: '{QuestId}' {Reason}, regenerating as '{NewId}'",
                i + 1, slot.DefinitionId, reason, replacement.DefinitionId);
            slots[i] = replacement;
            changed = true;
        }

        // ---- 4b. DUPLICATE SELF-HEAL. A board written by a pre-fix build (or a rollover that
        // carried a slot and its deserialized legacy twin) can seat the same definition twice.
        // Reroll the later seat, but only when it is untouched - a finished slot is a record and
        // a quest already worked on is never taken away, so at worst a duplicate stays visible.
        // A null roll (pool drained) also leaves the seat alone: a repeated quest beats an empty
        // seat, same call RollDailyQuest itself makes.
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null) continue;
            if (string.IsNullOrEmpty(slot.DefinitionId) || seenIds.Add(slot.DefinitionId)) continue;
            if (slot.IsCompleted || slot.CurrentProgress > 0) continue;

            var replacement = RollDailyQuest(DailyBoardIds(skipIndex: i));
            if (replacement == null) continue;

            App.Logger?.Information("Daily slot {Slot}: '{QuestId}' duplicates an earlier seat, regenerating as '{NewId}'",
                i + 1, slot.DefinitionId, replacement.DefinitionId);
            slots[i] = replacement;
            if (!string.IsNullOrEmpty(replacement.DefinitionId)) seenIds.Add(replacement.DefinitionId);
            changed = true;
        }

        // ---- 5. THE DEFERRED-ENTITLEMENT RE-ROLL (#889). A slot rolled before the Patreon answer
        // landed drew from the free-only pool. Now that the answer is in and it is "premium",
        // re-roll every slot that is still untouched - a quest already worked on is never taken
        // away, and a finished one certainly is not.
        if (DailyRolledUnresolved && IsEntitlementResolved())
        {
            DailyRolledUnresolved = false;
            if (App.Patreon?.HasPremiumAccess == true)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.IsCompleted || slot.CurrentProgress > 0) continue;

                    var replacement = RollDailyQuest(DailyBoardIds(skipIndex: i));
                    if (replacement == null) continue;

                    App.Logger?.Information("Daily slot {Slot} ('{QuestId}') was rolled before the entitlement resolved - re-rolling with premium access",
                        i + 1, slot.DefinitionId);
                    slots[i] = replacement;
                    changed = true;
                }
            }
        }

        SyncLegacyDailyMirror();
        return changed;
    }

    /// <summary>Today's slots plus the legacy single quest, for the rollover carry-forward.</summary>
    private IEnumerable<ActiveQuest> EnumerateDailySlotsIncludingLegacy()
    {
        foreach (var q in Progress.DailyQuests) if (q != null) yield return q;

        // The mirror is rebound BY REFERENCE at runtime (SyncLegacyDailyMirror), but the JSON
        // round-trip deserializes it as an independent copy of one slot, so after a load the
        // reference Contains() below never matches. Dedupe by DefinitionId as well, or the
        // rollover carry sees the slot AND its twin.
        var legacy = Progress.DailyQuest;
        if (legacy == null || Progress.DailyQuests.Contains(legacy)) yield break;
        foreach (var q in Progress.DailyQuests)
        {
            if (q != null && string.Equals(q.DefinitionId, legacy.DefinitionId, StringComparison.Ordinal))
                yield break;
        }
        yield return legacy;
    }

    /// <summary>The definition ids currently on the board - what a fresh roll must avoid so the
    /// player never sees the same quest twice in one day.</summary>
    private HashSet<string> DailyBoardIds(int? skipIndex = null)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Progress.DailyQuests.Count; i++)
        {
            if (skipIndex.HasValue && i == skipIndex.Value) continue;
            var id = Progress.DailyQuests[i]?.DefinitionId;
            if (!string.IsNullOrEmpty(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Roll one daily quest, avoiding <paramref name="excludeIds"/>. Returns null only when the
    /// pool is genuinely empty - the caller must treat that as "leave the slot alone", never as a
    /// reason to retry in a loop.
    /// </summary>
    private ActiveQuest? RollDailyQuest(ICollection<string>? excludeIds)
    {
        // Rolling before the entitlement answer lands means rolling from the free-only pool.
        // Flag the board (and arm the refresh tick) so it is re-rolled if the answer is premium.
        DailyRolledUnresolved = !IsEntitlementResolved();
        if (DailyRolledUnresolved) _premiumRecheckPending = true;

        // Use remote quests from QuestDefinitionService if available, fall back to embedded
        var questPool = App.QuestDefinitions?.GetDailyQuests() ?? QuestDefinition.DailyQuests.ToList();
        var hasPremium = App.Patreon?.HasPremiumAccess == true;
        var availableQuests = FilterDailyRollPool(questPool, excludeIds, hasPremium, DateTime.Today, applyDateWindow: true);

        // THE WINDOW MUST NEVER STARVE THE PLAYER. If a date window emptied the pool,
        // fall back to the undated pool rather than leaving the day questless: an event
        // that ends at midnight UTC-somewhere must not be able to take the daily quest
        // with it. See IsQuestInDateWindow.
        if (availableQuests.Count == 0)
        {
            availableQuests = FilterDailyRollPool(questPool, excludeIds, hasPremium, DateTime.Today, applyDateWindow: false);
        }

        // LAST RESORT: three slots can drain a pool that one slot never could (a low-level
        // account with most categories still locked is the realistic case). A repeated quest is
        // a worse board than three distinct ones and a much better board than an empty seat, so
        // the no-duplicates rule is the first thing dropped, not the day itself.
        if (availableQuests.Count == 0)
        {
            // Through the same helper, with the exclusions dropped rather than the predicates.
            // Hand-rolling this filter is how a Remote quest gets onto the board: it is the one
            // path that never sees IsRollableAsDaily unless it goes through here.
            availableQuests = FilterDailyRollPool(questPool, (ICollection<string>?)null, hasPremium, DateTime.Today, applyDateWindow: false);
        }

        if (availableQuests.Count == 0) return null;

        var selectedQuest = availableQuests[_random.Next(availableQuests.Count)];
        App.Logger?.Information("Rolled daily quest: {QuestId} (from {Source})",
            selectedQuest.Id, App.QuestDefinitions != null ? "server" : "embedded");
        return new ActiveQuest(selectedQuest.Id);
    }

    /// <summary>
    /// Keep <see cref="QuestProgress.DailyQuest"/> pointing at the first unfinished slot. Nothing
    /// in this build reads it; it exists so a downgrade to a pre-three-up build finds a live quest
    /// where it expects one instead of rolling the player a fresh day.
    /// </summary>
    private void SyncLegacyDailyMirror()
    {
        ActiveQuest? mirror = null;
        foreach (var q in Progress.DailyQuests)
        {
            if (q == null) continue;
            if (!q.IsCompleted) { mirror = q; break; }
            mirror ??= q;
        }
        Progress.DailyQuest = mirror;
    }

    private void GenerateNewWeeklyQuest(string? excludeId = null)
    {
        // Same deferred-tier bookkeeping as the daily generator — see the note there.
        WeeklyRolledUnresolved = !IsEntitlementResolved();
        if (WeeklyRolledUnresolved) _premiumRecheckPending = true;

        // Use remote quests from QuestDefinitionService if available, fall back to embedded
        var questPool = App.QuestDefinitions?.GetWeeklyQuests() ?? QuestDefinition.WeeklyQuests.ToList();
        var availableQuests = questPool
            .Where(q => q.Id != excludeId)
            .Where(q => IsQuestAvailableForLevel(q.Category))
            .Where(IsQuestAvailableForTier)
            .Where(IsQuestInDateWindow)
            .ToList();

        // Same starvation guard as the daily generator — see the note there.
        if (availableQuests.Count == 0)
        {
            availableQuests = questPool
                .Where(q => q.Id != excludeId)
                .Where(q => IsQuestAvailableForLevel(q.Category))
                .Where(IsQuestAvailableForTier)
                .ToList();
        }

        if (availableQuests.Count == 0) return;

        var selectedQuest = availableQuests[_random.Next(availableQuests.Count)];

        Progress.WeeklyQuest = new ActiveQuest(selectedQuest.Id);
        Progress.WeeklyQuestGeneratedAt = DateTime.Now;

        App.Logger?.Information("Generated new weekly quest: {QuestId} (from {Source})",
            selectedQuest.Id, App.QuestDefinitions != null ? "server" : "embedded");
    }

    /// <summary>
    /// Guards the "requires premium, access lost" rerolls (#889). Throwing a premium quest away is
    /// only correct once we actually KNOW the entitlement, and a Patreon answer arrives seconds
    /// after launch — before it does, every patron looks free. Progress is also protected: a quest
    /// the player has already worked on is never taken away mid-run. A deferred decision is retried
    /// by the refresh timer, so a genuinely lapsed patron still gets a free quest a tick later.
    /// </summary>
    private bool CanDropPremiumQuest(ActiveQuest quest, string slot)
    {
        if (quest.CurrentProgress > 0)
        {
            App.Logger?.Information(
                "Premium {Slot} quest '{QuestId}' kept: it already has progress ({Progress})",
                slot, quest.DefinitionId, quest.CurrentProgress);
            return false;
        }

        if (!IsEntitlementResolved())
        {
            _premiumRecheckPending = true;
            App.Logger?.Information(
                "Premium {Slot} quest '{QuestId}' kept: entitlement not resolved yet, deferring the decision",
                slot, quest.DefinitionId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// True once the premium gate can be trusted this session: an entitled read is self-evidently
    /// resolved, and an unentitled one is only believed after validation has had time to land (and
    /// is not still in flight). With the service alive it is never permanently "unresolved" — the
    /// settle window guarantees a lapsed patron's quest is reconciled shortly after launch instead
    /// of never; only a missing PatreonService stays unresolved, which is fail-safe (premium
    /// quests are never dropped) and is the intended behaviour.
    /// </summary>
    private bool IsEntitlementResolved()
    {
        // THE SIGNED-OUT WINDOW IS NOT AN ANSWER. quests.json deliberately survives a logout
        // (BUG-BN8X9B9SZ5 / #1027), stamped with the account that owns it — but the entitlement
        // providers do not: a logout tears them down, so between the sign-out and the next
        // sign-in the premium gate reads a flat "no access" that belongs to nobody. Believing it
        // let the premium-loss rerolls below discard the departed account's untouched quest
        // purely because the user signed out, which is the same "log out and back in and your
        // quests are different" complaint the ledger fix was meant to end. Treat the whole window
        // as unresolved: the decision is deferred (never lost), the refresh tick stays quiet
        // while nobody is signed in, and the first post-login sync settles it for real.
        if (IsSignedOutWithOwnedQuests(
                App.UnifiedUserId ?? App.Settings?.Current?.UnifiedId, Progress?.OwnerUnifiedId))
            return false;

        var patreon = App.Patreon;
        if (patreon == null) return false;
        if (patreon.IsVerifying) return false;
        if (patreon.HasPremiumAccess) return true;
        // Premium access is the OR of both providers (PatreonService.HasPremiumAccess folds in
        // SubscribeStar), so an un-entitled read while a SubscribeStar validation is still in
        // flight is just as unresolved as a Patreon one — without this, a SubscribeStar-only
        // subscriber's quest was dropped at the settle mark for reading "resolved, no access".
        if (App.SubscribeStar?.IsVerifying == true) return false;
        return DateTime.UtcNow - _startedUtc >= EntitlementSettleWindow;
    }

    /// <summary>
    /// True when nobody is signed in but the local quest ledger belongs to an account that was.
    /// That is the logout window: the quests are being held for a returning owner whose
    /// entitlement is currently unknowable. Pure, so it is testable without a live App.
    /// </summary>
    internal static bool IsSignedOutWithOwnedQuests(string? currentUnifiedId, string? questOwnerUnifiedId)
        => string.IsNullOrEmpty(currentUnifiedId) && !string.IsNullOrEmpty(questOwnerUnifiedId);

    /// <summary>
    /// Feature level gating has been removed — every quest category is available from level 1.
    /// </summary>
    private static bool IsQuestAvailableForLevel(QuestCategory category)
    {
        return true;
    }

    /// <summary>
    /// Premium-gated quests (RequiresPremium) are only offered to users with Patreon
    /// premium access. Free users never roll a quest tied to an exclusive feature they
    /// can't complete. Premium users get the blended pool (free + premium).
    /// </summary>
    private static bool IsQuestAvailableForTier(QuestDefinition quest)
        => IsQuestAvailableForTier(quest, App.Patreon?.HasPremiumAccess == true);

    /// <summary>Pure form of the tier test, split out so it is testable without a live App.</summary>
    internal static bool IsQuestAvailableForTier(QuestDefinition quest, bool hasPremium)
        => !quest.RequiresPremium || hasPremium;

    /// <summary>
    /// THE SOLO-USER GATE. A quest in <see cref="QuestCategory.Remote"/> only moves when
    /// SOMEONE ELSE issues commands to this user, so a player with nobody controlling them
    /// could roll "Take 25 remote commands" and spend the whole day unable to touch it - two
    /// community threads reported exactly that. Remote-RECEIVING quests are therefore no
    /// longer offered in the DAILY roll (today: handed_over_d, remote_hands_d, plus anything
    /// the definitions channel publishes in that category).
    ///
    /// THE DEFINITIONS ARE NOT DELETED and no counter is touched: QuestCategory.Remote,
    /// TrackRemoteCommand and both quest entries all still exist, so a quest already rolled
    /// today still completes normally, a persisted quests.json naming one still resolves
    /// through GetCurrentDailyDefinition, and the weekly slot (puppet_strings_w,
    /// fully_remote_w) is untouched - a week is long enough to find a Controller.
    ///
    /// Filtering by CATEGORY rather than by id is deliberate: it also covers a daily
    /// remote-receive quest published later by the server-side definitions channel.
    /// </summary>
    internal static bool IsRollableAsDaily(QuestDefinition quest)
        // RemoteIssue is held out too until the web controller reports issued commands back
        // (see REMOTE_CONTROL_PRIMER §4a-bis) - the desktop app never issues commands itself,
        // so rolling it today would hand out a quest that can never move past 0.
        => quest.Category != QuestCategory.Remote && quest.Category != QuestCategory.RemoteIssue;

    /// <summary>
    /// Pure form of the daily roll pool filter, split out so it is testable without a live App.
    /// The two call sites in GenerateNewDailyQuest differ only in whether the date window applies
    /// (the starvation fallback drops it) - every other predicate MUST stay identical between
    /// them, which is exactly what this shared helper guarantees.
    /// </summary>
    internal static List<QuestDefinition> FilterDailyRollPool(
        IEnumerable<QuestDefinition> pool, string? excludeId, bool hasPremium,
        DateTime today, bool applyDateWindow)
        => FilterDailyRollPool(
            pool,
            string.IsNullOrEmpty(excludeId) ? null : new[] { excludeId },
            hasPremium, today, applyDateWindow);

    /// <summary>
    /// Set-excluding form, for the three-up daily board. Each seat has to roll against every id
    /// ALREADY on the board rather than against one, or the day can deal the same quest twice.
    ///
    /// The single-id overload above is kept, and kept first, because it is what the tests call
    /// by name (excludeId:) - the parameter names are what pick the overload apart at a null
    /// argument, so do not rename either one.
    /// </summary>
    internal static List<QuestDefinition> FilterDailyRollPool(
        IEnumerable<QuestDefinition> pool, ICollection<string>? excludeIds, bool hasPremium,
        DateTime today, bool applyDateWindow)
    {
        return pool
            .Where(q => excludeIds == null || !excludeIds.Contains(q.Id))
            .Where(q => IsQuestAvailableForLevel(q.Category))
            .Where(q => IsQuestAvailableForTier(q, hasPremium))
            .Where(IsRollableAsDaily)
            .Where(q => !applyDateWindow || IsQuestInDateWindow(q.ActiveFrom, q.ActiveUntil, today))
            .ToList();
    }

    /// <summary>
    /// THE DATE WINDOW, finally read. QuestDefinitionService has parsed, cached and
    /// refreshed `activeFrom`/`activeUntil` since the definitions channel shipped, and
    /// nothing has ever looked at them — a seasonal quest went live the moment it was
    /// published and stayed live forever. This predicate is that missing read, and it
    /// doubles as the channel's kill switch: publishing a quest with an `activeUntil`
    /// in the past retires it on the next roll without a client update.
    ///
    /// NO-OP BY CONSTRUCTION FOR EVERYTHING THAT SHIPS TODAY. Both fields are null on
    /// every embedded quest (QuestDefinition's ctor never sets them) and on every quest
    /// the live channel currently publishes, and a null/blank/unparseable bound is
    /// treated as "no constraint". A quest can only be hidden by a bound that is both
    /// present and a valid yyyy-MM-dd — anything else fails OPEN, because a server typo
    /// must cost a scheduling window, never the player's daily quest.
    ///
    /// LOCAL DATES, DELIBERATELY. The rest of quest scheduling turns over on
    /// DateTime.Today (QuestProgress.IsDailyExpired, GetStartOfWeek), so the window
    /// uses the same boundary — a quest that appears at "the start of the day" must
    /// appear at the start of the day the player's other quests reset on, not eight
    /// hours off it. `activeUntil` is INCLUSIVE: the last day named is a day it runs.
    /// </summary>
    private static bool IsQuestInDateWindow(QuestDefinition quest)
        => IsQuestInDateWindow(quest.ActiveFrom, quest.ActiveUntil, DateTime.Today);

    /// <summary>Pure form of the window test, split out so it is testable without a live App.</summary>
    internal static bool IsQuestInDateWindow(string? activeFrom, string? activeUntil, DateTime today)
    {
        if (TryParseQuestDate(activeFrom, out var from) && today.Date < from) return false;
        if (TryParseQuestDate(activeUntil, out var until) && today.Date > until) return false;
        return true;
    }

    /// <summary>
    /// Strict yyyy-MM-dd only. The channel is hand-authored JSON, so a loose parse would
    /// read "03/04" differently depending on the machine's culture and retire a quest on
    /// the wrong continent. False means "no usable bound" — the caller then ignores it.
    /// </summary>
    private static bool TryParseQuestDate(string? value, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);
    }

    /// <summary>
    /// Force regenerate the weekly quest (called by server-side reset flag)
    /// </summary>
    public void ForceRegenerateWeeklyQuest()
    {
        // Don't regenerate if current quest is still within this week
        if (Progress.WeeklyQuest != null && !Progress.IsWeeklyExpired())
        {
            App.Logger?.Information("Skipping weekly quest force-regeneration - quest still within current week");
            return;
        }

        var oldId = Progress.WeeklyQuest?.DefinitionId;
        GenerateNewWeeklyQuest(excludeId: oldId);
        _isDirty = true;
        Save();
        App.Logger?.Information("Force-regenerated weekly quest (old: {OldId}, new: {NewId})",
            oldId, Progress.WeeklyQuest?.DefinitionId);
    }

    /// <summary>
    /// Force regenerate the daily quest (called by server-side reset flag)
    /// </summary>
    public void ForceRegenerateDailyQuest()
    {
        // Every UNFINISHED slot is re-dealt. A finished one is left alone: the server's reset flag
        // means "give them a fresh board", not "take back what they already earned today".
        int rerolled = 0;
        for (int i = 0; i < Progress.DailyQuests.Count; i++)
        {
            var slot = Progress.DailyQuests[i];
            if (slot == null || slot.IsCompleted) continue;

            var replacement = RollDailyQuest(DailyBoardIds(skipIndex: i));
            if (replacement == null) continue;
            Progress.DailyQuests[i] = replacement;
            rerolled++;
        }

        SyncLegacyDailyMirror();
        _isDirty = true;
        Save();
        App.Logger?.Information("Force-regenerated {Count} daily quest slot(s)", rerolled);
    }

    private static DateTime GetStartOfWeek(DateTime date)
    {
        int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }

    #endregion

    #region Quest Definitions

    /// <summary>
    /// Get the definition for the current daily quest
    /// </summary>
    public QuestDefinition? GetCurrentDailyDefinition() => GetDailyDefinition(FirstUnfinishedDailySlot());

    /// <summary>
    /// The definition behind one daily slot. Remote pool first, embedded as the fallback - a
    /// server that has rotated its pool since the slot was rolled resolves to null, which
    /// ReconcileDailySlots reads as "replace this slot".
    /// </summary>
    public QuestDefinition? GetDailyDefinition(ActiveQuest? quest)
    {
        if (quest == null || string.IsNullOrEmpty(quest.DefinitionId)) return null;

        // Try remote quests first, fall back to embedded
        var remoteQuests = App.QuestDefinitions?.GetDailyQuests();
        if (remoteQuests != null)
        {
            var remoteQuest = remoteQuests.FirstOrDefault(q => q.Id == quest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.DailyQuests.FirstOrDefault(q => q.Id == quest.DefinitionId);
    }

    /// <summary>
    /// Today's board, in slot order, paired with the definition behind each seat. ALWAYS
    /// <see cref="MaxDailyQuestsPerDay"/> entries long, so the UI can index it without guarding:
    /// a seat the pool could not fill comes back as (null, null) and paints as an empty slot.
    /// </summary>
    public IReadOnlyList<(ActiveQuest? Quest, QuestDefinition? Definition)> GetDailySlots()
    {
        var board = new List<(ActiveQuest?, QuestDefinition?)>(MaxDailyQuestsPerDay);
        for (int i = 0; i < MaxDailyQuestsPerDay; i++)
        {
            var quest = i < Progress.DailyQuests.Count ? Progress.DailyQuests[i] : null;
            board.Add((quest, GetDailyDefinition(quest)));
        }
        return board;
    }

    /// <summary>The first seat still in play, or null once the board is finished.</summary>
    private ActiveQuest? FirstUnfinishedDailySlot()
    {
        foreach (var q in Progress.DailyQuests)
        {
            if (q != null && !q.IsCompleted) return q;
        }
        return null;
    }

    /// <summary>
    /// Get the definition for the current weekly quest
    /// </summary>
    public QuestDefinition? GetCurrentWeeklyDefinition()
    {
        if (Progress.WeeklyQuest == null) return null;

        // Try remote quests first, fall back to embedded
        var remoteQuests = App.QuestDefinitions?.GetWeeklyQuests();
        if (remoteQuests != null)
        {
            var remoteQuest = remoteQuests.FirstOrDefault(q => q.Id == Progress.WeeklyQuest.DefinitionId);
            if (remoteQuest != null) return remoteQuest;
        }

        return QuestDefinition.WeeklyQuests.FirstOrDefault(q => q.Id == Progress.WeeklyQuest.DefinitionId);
    }

    #endregion

    #region Reroll

    /// <summary>
    /// Check if user has Patreon premium access
    /// </summary>
    private bool HasPatreonAccess => App.Patreon?.HasPremiumAccess == true;

    /// <summary>
    /// Get remaining daily rerolls (1 base + 2 for Patreon = 3 max)
    /// </summary>
    public int GetRemainingDailyRerolls() => Progress.GetRemainingDailyRerolls(HasPatreonAccess);

    /// <summary>
    /// Get remaining weekly rerolls (1 base + 2 for Patreon = 3 max)
    /// </summary>
    public int GetRemainingWeeklyRerolls() => Progress.GetRemainingWeeklyRerolls(HasPatreonAccess);

    /// <summary>
    /// Reroll the daily quest (1 base + 2 extra for Patreon users)
    /// </summary>
    /// <returns>True if reroll succeeded, false if no rerolls remaining</returns>
    public bool RerollDailyQuest()
    {
        for (int i = 0; i < Progress.DailyQuests.Count; i++)
        {
            if (Progress.DailyQuests[i]?.IsCompleted == false) return RerollDailyQuest(i);
        }
        App.Logger?.Debug("Cannot reroll: no unfinished daily slot");
        return false;
    }

    /// <summary>
    /// Reroll ONE seat of today's board. The rerolls themselves are a shared daily pool (1 base
    /// + 2 for Patreon + skill-tree bonuses) - three seats do not mean three times the rerolls,
    /// they mean the player chooses which seat is worth spending one on.
    /// </summary>
    /// <param name="slot">Zero-based seat index.</param>
    /// <returns>True if the seat was rerolled, false if it could not be (no rerolls left, seat
    /// already finished, empty pool, or an index that is not on the board).</returns>
    public bool RerollDailyQuest(int slot)
    {
        if (slot < 0 || slot >= Progress.DailyQuests.Count)
        {
            App.Logger?.Debug("Cannot reroll daily slot {Slot}: not on the board", slot);
            return false;
        }

        var quest = Progress.DailyQuests[slot];
        if (quest == null)
        {
            App.Logger?.Debug("Cannot reroll empty daily slot {Slot}", slot);
            return false;
        }

        if (quest.IsCompleted)
        {
            App.Logger?.Debug("Cannot reroll completed daily quest");
            return false;
        }

        if (!Progress.CanRerollDaily(HasPatreonAccess))
        {
            App.Logger?.Debug("No daily rerolls remaining");
            return false;
        }

        // The whole board is excluded, not just this seat: spending a reroll to be handed a
        // duplicate of the card next to it would be the worst possible outcome of pressing it.
        var replacement = RollDailyQuest(DailyBoardIds(skipIndex: slot));
        if (replacement == null)
        {
            App.Logger?.Warning("Daily reroll found no replacement quest - the pool is empty, keeping '{QuestId}' and NOT spending the reroll",
                quest.DefinitionId);
            return false;
        }

        var oldId = quest.DefinitionId;
        Progress.DailyQuests[slot] = replacement;
        Progress.DailyRerollsUsed++;
        SyncLegacyDailyMirror();
        _isDirty = true;
        Save();

        App.Logger?.Information("Daily slot {Slot} rerolled from {OldId} to {NewId} (rerolls used: {Used})",
            slot + 1, oldId, replacement.DefinitionId, Progress.DailyRerollsUsed);
        return true;
    }

    /// <summary>
    /// Reroll the weekly quest (1 base + 2 extra for Patreon users)
    /// </summary>
    /// <returns>True if reroll succeeded, false if no rerolls remaining</returns>
    public bool RerollWeeklyQuest()
    {
        if (!Progress.CanRerollWeekly(HasPatreonAccess))
        {
            App.Logger?.Debug("No weekly rerolls remaining");
            return false;
        }

        if (Progress.WeeklyQuest?.IsCompleted == true)
        {
            App.Logger?.Debug("Cannot reroll completed weekly quest");
            return false;
        }

        var oldId = Progress.WeeklyQuest?.DefinitionId;
        GenerateNewWeeklyQuest(excludeId: oldId);
        Progress.WeeklyRerollsUsed++;
        _isDirty = true;
        Save();

        App.Logger?.Information("Weekly quest rerolled from {OldId} to {NewId} (rerolls used: {Used})",
            oldId, Progress.WeeklyQuest?.DefinitionId, Progress.WeeklyRerollsUsed);
        return true;
    }

    #endregion

    #region Progress Tracking

    /// <summary>
    /// Track flash image viewed
    /// </summary>
    public void TrackFlashImage()
    {
        UpdateQuestProgress(QuestCategory.Flash, 1);
    }

    /// <summary>
    /// Track spiral overlay time (called periodically with elapsed minutes)
    /// </summary>
    public void TrackSpiralMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _spiralMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_spiralMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_spiralMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Spiral, wholeMinutes);
            _spiralMinutesAccumulator -= wholeMinutes;
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_combinedMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Combined, wholeMinutes);
            _combinedMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track pink filter time (called periodically with elapsed minutes)
    /// </summary>
    public void TrackPinkFilterMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _pinkFilterMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_pinkFilterMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_pinkFilterMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.PinkFilter, wholeMinutes);
            _pinkFilterMinutesAccumulator -= wholeMinutes;
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_combinedMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Combined, wholeMinutes);
            _combinedMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track BrainDrain overlay time (called periodically with elapsed minutes)
    /// </summary>
    public void TrackBrainDrainMinutes(double minutes)
    {
        // BrainDrain feeds into Combined category only (no dedicated BrainDrain quest category)
        _brainDrainMinutesAccumulator += minutes;
        _combinedMinutesAccumulator += minutes;

        if (_brainDrainMinutesAccumulator >= 1.0)
        {
            _brainDrainMinutesAccumulator -= (int)Math.Floor(_brainDrainMinutesAccumulator);
        }

        if (_combinedMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_combinedMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Combined, wholeMinutes);
            _combinedMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track bubble popped
    /// </summary>
    public void TrackBubblePopped()
    {
        UpdateQuestProgress(QuestCategory.Bubbles, 1);
    }

    /// <summary>
    /// Advance bubble-pop quests by a whole batch at once (DtRH web run reporting
    /// its total on completion).
    /// </summary>
    public void TrackBubblesPopped(int count)
    {
        if (count <= 0) return;
        UpdateQuestProgress(QuestCategory.Bubbles, count);
    }

    /// <summary>
    /// Track video minutes watched
    /// </summary>
    public void TrackVideoMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _videoMinutesAccumulator += minutes;

        if (_videoMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_videoMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Video, wholeMinutes);
            _videoMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track session completed
    /// </summary>
    public void TrackSessionCompleted()
    {
        UpdateQuestProgress(QuestCategory.Session, 1);
    }

    /// <summary>
    /// Track lock card completed
    /// </summary>
    public void TrackLockCardCompleted()
    {
        UpdateQuestProgress(QuestCategory.LockCard, 1);
    }

    /// <summary>
    /// Track bubble count game completed
    /// </summary>
    public void TrackBubbleCountCompleted()
    {
        UpdateQuestProgress(QuestCategory.BubbleCount, 1);
    }

    public void TrackMantraCompleted()
    {
        UpdateQuestProgress(QuestCategory.Mantra, 1);
    }

    /// <summary>
    /// Track Bambi Takeover (autonomy) active time (called periodically with elapsed minutes).
    /// Patreon-exclusive category.
    /// </summary>
    public void TrackAutonomyMinutes(double minutes)
    {
        // Accumulate fractional minutes until we have at least 1 full minute
        _autonomyMinutesAccumulator += minutes;

        if (_autonomyMinutesAccumulator >= 1.0)
        {
            int wholeMinutes = (int)Math.Floor(_autonomyMinutesAccumulator);
            UpdateQuestProgress(QuestCategory.Autonomy, wholeMinutes);
            _autonomyMinutesAccumulator -= wholeMinutes;
        }
    }

    /// <summary>
    /// Track a completed lockdown. Patreon-exclusive category.
    /// </summary>
    public void TrackLockdownCompleted()
    {
        UpdateQuestProgress(QuestCategory.Lockdown, 1);
    }

    /// <summary>
    /// Track a remote-control command received. Patreon-exclusive category.
    /// </summary>
    public void TrackRemoteCommand()
    {
        UpdateQuestProgress(QuestCategory.Remote, 1);
    }

    /// <summary>
    /// Track a remote-control command this user ISSUED to another subject as a Controller
    /// (take_the_reins_d). Open to every tier - see QuestCategory.RemoteIssue.
    ///
    /// SELF-CONTROL NEVER COUNTS. A user can pair their own phone or browser to their own
    /// session, so the target's unified id is compared against this install's own
    /// (App.UnifiedUserId) and a match is dropped. When the caller cannot supply a target id
    /// the command is dropped too: crediting an unattributable command would reopen the
    /// self-control loophole, and a silent under-count is the safer failure.
    ///
    /// INTENSITY-BLIND BY DESIGN: one command is one tick whatever the session tier is. The
    /// quest must never be a reason to talk a subject into a heavier level.
    /// </summary>
    public void TrackRemoteCommandIssued(string? targetUnifiedId)
    {
        if (!CountsAsForeignSubject(targetUnifiedId, App.UnifiedUserId)) return;
        UpdateQuestProgress(QuestCategory.RemoteIssue, 1);
    }

    /// <summary>
    /// Pure form of the self-control guard, split out so it is testable without a live App.
    /// True only when the target is a known id that is NOT this install's own id.
    /// </summary>
    internal static bool CountsAsForeignSubject(string? targetUnifiedId, string? selfUnifiedId)
    {
        if (string.IsNullOrWhiteSpace(targetUnifiedId)) return false;
        if (string.IsNullOrWhiteSpace(selfUnifiedId)) return true;
        return !string.Equals(targetUnifiedId.Trim(), selfUnifiedId.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Track a keyword/OCR trigger firing. Patreon-exclusive category.
    /// </summary>
    public void TrackKeywordTrigger()
    {
        UpdateQuestProgress(QuestCategory.KeywordTrigger, 1);
    }

    /// <summary>
    /// Track a blink logged in the live blink trainer. Patreon-exclusive category.
    /// </summary>
    public void TrackBlinkTrainerBlink()
    {
        UpdateQuestProgress(QuestCategory.BlinkTrainer, 1);
    }

    /// <summary>
    /// Track XP earned (for "earn X XP" quests)
    /// </summary>
    public void TrackXPEarned(int xp)
    {
        // Check if there's a quest tracking XP earned specifically
        var dailyDef = GetCurrentDailyDefinition();
        var weeklyDef = GetCurrentWeeklyDefinition();

        // Only the conditioning_champion_w quest tracks XP earned
        if (weeklyDef?.Id == "conditioning_champion_w" && Progress.WeeklyQuest?.IsCompleted == false)
        {
            Progress.WeeklyQuest.CurrentProgress += xp;
            _isDirty = true;

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Weekly, Progress.WeeklyQuest.CurrentProgress, weeklyDef.TargetValue));
            }
        }
    }

    /// <summary>
    /// Track daily streak (called when streak updates)
    /// </summary>
    public void TrackStreak(int currentStreak)
    {
        var weeklyDef = GetCurrentWeeklyDefinition();

        // streak_keeper_w tracks maintaining a streak
        if (weeklyDef?.Id == "streak_keeper_w" && Progress.WeeklyQuest?.IsCompleted == false)
        {
            Progress.WeeklyQuest.CurrentProgress = Math.Max(Progress.WeeklyQuest.CurrentProgress, currentStreak);
            _isDirty = true;

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Weekly, Progress.WeeklyQuest.CurrentProgress, weeklyDef.TargetValue));
            }
        }
    }

    /// <summary>
    /// Update quest progress for a specific category
    /// </summary>
    private void UpdateQuestProgress(QuestCategory category, int amount)
    {
        if (amount <= 0) return;

        // Training Programs observe the same signals as quests, from the same choke point, so the two
        // can never disagree about what the user actually did. One tracking pass, not two.
        try { App.Programs?.TrackVerifier(category, amount); } catch { /* a program must never break quests */ }

        // Check EVERY unfinished daily seat. All three of today's quests are live at once, so one
        // spiral minute legitimately advances every seat that is asking for spiral minutes - the
        // board is three parallel asks, not a queue. Iterated over a snapshot because CompleteQuest
        // raises events that reach back into this service.
        var dailyBoard = Progress.DailyQuests.ToList();
        foreach (var dailyQuest in dailyBoard)
        {
            if (dailyQuest == null || dailyQuest.IsCompleted) continue;

            var dailyDef = GetDailyDefinition(dailyQuest);
            if (dailyDef == null || dailyDef.Category != category) continue;

            dailyQuest.CurrentProgress += amount;
            _isDirty = true;

            if (dailyQuest.CurrentProgress >= dailyDef.TargetValue)
            {
                CompleteQuest(dailyQuest, dailyDef, QuestType.Daily);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Daily, dailyQuest.CurrentProgress, dailyDef.TargetValue));
            }
        }

        // Check weekly quest
        var weeklyDef = GetCurrentWeeklyDefinition();
        // Skip quests that have dedicated tracking methods (conditioning_champion_w tracks XP via TrackXPEarned, not overlay minutes)
        var weeklyHasDedicatedTracking = weeklyDef?.Id is "conditioning_champion_w" or "streak_keeper_w";
        if (weeklyDef != null && weeklyDef.Category == category && Progress.WeeklyQuest?.IsCompleted == false && !weeklyHasDedicatedTracking)
        {
            Progress.WeeklyQuest.CurrentProgress += amount;
            _isDirty = true;

            if (Progress.WeeklyQuest.CurrentProgress >= weeklyDef.TargetValue)
            {
                CompleteQuest(Progress.WeeklyQuest, weeklyDef, QuestType.Weekly);
            }
            else
            {
                QuestProgressChanged?.Invoke(this, new QuestProgressEventArgs(
                    QuestType.Weekly, Progress.WeeklyQuest.CurrentProgress, weeklyDef.TargetValue));
            }
        }
    }

    public const int MaxDailyQuestsPerDay = 3;

    /// <summary>
    /// Get how many daily quests have been completed today
    /// </summary>
    public int GetDailyQuestsCompletedToday() => Progress.GetDailyQuestsCompletedToday();

    /// <summary>
    /// Check if all daily quests for today are done (3/3)
    /// </summary>
    public bool AreAllDailyQuestsCompleted() => Progress.AreAllDailyQuestsCompleted();

    /// <summary>
    /// The day key used by the quest completion log: the same calendar day
    /// <see cref="QuestProgress.DailyQuestCompletionDates"/> stores, as yyyy-MM-dd invariant
    /// (the format the cloud sync already sends those dates in).
    /// </summary>
    private static string DayKey(DateTime day) =>
        day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Append (day, questId) to the quest completion log, de-duped on the pair. Only ever called
    /// from <see cref="CompleteQuest"/>, which is the only path that knows a quest id: the streak
    /// shield and the manual day fix stamp the calendar with no quest behind them and must not
    /// invent one here.
    /// </summary>
    private void RecordQuestInLog(DateTime day, string questId)
    {
        if (string.IsNullOrWhiteSpace(questId)) return;
        var key = DayKey(day);
        Progress.QuestCompletionLog ??= new List<QuestLogEntry>();
        if (Progress.QuestCompletionLog.Any(e => e.D == key && e.Q == questId)) return;
        Progress.QuestCompletionLog.Add(new QuestLogEntry(key, questId));
    }

    /// <summary>
    /// Complete a quest and award rewards
    /// </summary>
    private void CompleteQuest(ActiveQuest quest, QuestDefinition def, QuestType type)
    {
        if (quest.IsCompleted) return;

        quest.IsCompleted = true;
        quest.CompletedAt = DateTime.Now;

        // Spiral W1: remember WHICH quest was finished today, not just that one was. Daily and
        // weekly both, and here at the top because this is the last place that still holds the
        // definition id. Keyed on the same calendar day the streak calendar uses.
        RecordQuestInLog(DateTime.Today, def.Id);

        // Update statistics
        if (type == QuestType.Daily)
        {
            Progress.TotalDailyQuestsCompleted++;
            // Derived, not incremented: GetDailyQuestsCompletedToday counts the stamped seats on
            // the board (and resets the date first), and the seat above has just been stamped.
            Progress.GetDailyQuestsCompletedToday();
            SyncLegacyDailyMirror();

            // Record completion date for streak calendar (only once per day)
            var today = DateTime.Today;
            bool firstCompletionToday = !Progress.DailyQuestCompletionDates.Contains(today);
            if (firstCompletionToday)
            {
                Progress.DailyQuestCompletionDates.Add(today);
            }

            // Trim entries older than 90 days (matches cloud sync window)
            var cutoff = today.AddDays(-90);
            Progress.DailyQuestCompletionDates.RemoveAll(d => d.Date < cutoff);
            var logCutoff = DayKey(cutoff);
            Progress.QuestCompletionLog.RemoveAll(e => string.CompareOrdinal(e.D, logCutoff) < 0);
            App.Settings?.Current?.StreakShieldUsedDates?.RemoveAll(d => d.Date < cutoff);

            // Apply streak shield if yesterday is missing (would break streak)
            var yesterday = today.AddDays(-1);
            if (!Progress.DailyQuestCompletionDates.Any(d => d.Date == yesterday)
                && App.Settings?.Current?.LastDailyQuestDate?.Date < yesterday)
            {
                if (App.SkillTree?.UseStreakShield() == true)
                {
                    Progress.DailyQuestCompletionDates.Add(yesterday);
                    var settings = App.Settings?.Current;
                    if (settings != null && !settings.StreakShieldUsedDates.Contains(yesterday))
                        settings.StreakShieldUsedDates.Add(yesterday);
                    App.Logger?.Information("Quest streak shield used! Filled gap at {Date}", yesterday);
                }
            }

            // Grow the streak incrementally on the first completion of a new day.
            // Tracked independently of the 90-day completion calendar so the streak
            // is NOT capped at the calendar's trim window (~91 days). The calendar
            // stays the source of truth for detecting BREAKS (via AdvanceQuestStreak's
            // yesterday check) and for repairing the streak upward after a sync.
            if (firstCompletionToday)
                AdvanceQuestStreak();

            // Repair the streak upward if the calendar proves a longer run than the
            // stored counter (e.g. after a cloud merge). Never decreases it.
            RecalculateStreak();
        }
        else
        {
            Progress.TotalWeeklyQuestsCompleted++;
        }

        // Scale XP reward based on player level. +4%/level pre-Descent, +1.2%/level once the
        // account has been through the migration ceremony (CONTRACTS-0812 §3) — the coefficient
        // moves with the XP curve, because it was the curve that made the old one a runaway.
        // ProgressionService.QuestLevelScale reads the per-user epoch, so this is a no-op for
        // every un-migrated account.
        var playerLevel = App.Settings?.Current?.PlayerLevel ?? 1;
        var betterQuestsMultiplier = App.SkillTree?.GetRerollBonusMultiplier() ?? 1.0;
        // Quest streak bonus: +3% per consecutive day
        var questStreak = App.Settings?.Current?.DailyQuestStreak ?? 0;
        var streakMultiplier = 1.0 + (questStreak * 0.03);
        var scaledXP = (int)Math.Round(def.XPReward * ProgressionService.QuestLevelScale(playerLevel) * betterQuestsMultiplier * streakMultiplier);

        Progress.TotalXPFromQuests += scaledXP;

        _isDirty = true;
        Save();

        // Award XP. XPSource.Quest, not Other: nothing branches on the source inside AddXP (the
        // old comment's recursion worry was never about which value was passed), but THE BANK's
        // flight only fires for completion-shaped awards and a quest payout is the archetype.
        App.Progression?.AddXP(scaledXP, XPSource.Quest);

        // Check for Perfect Bimbo Week bonus (7, 14, 30 day daily quest streaks).
        // CheckPerfectWeekBonus grants the XP itself (before it writes its paid-once latch, so a
        // crash between the two cannot burn the milestone) and returns what it awarded.
        if (type == QuestType.Daily)
        {
            var bonusXP = App.SkillTree?.CheckPerfectWeekBonus() ?? 0;
            if (bonusXP > 0)
            {
                App.Logger?.Information("Perfect Bimbo Week bonus granted: {XP} XP", bonusXP);
            }
        }

        // Play celebration effects
        PlayCompletionEffects();

        App.Logger?.Information("Quest completed: {QuestName} ({Type}) - Awarded {XP} XP (base: {BaseXP}, level: {Level}, streak: {Streak}x{StreakPct}%)",
            def.Name, type, scaledXP, def.XPReward, playerLevel, questStreak, questStreak * 3);

        // Fire event
        QuestCompleted?.Invoke(this, new QuestCompletedEventArgs(def, scaledXP, type));

        // NOTHING IS GENERATED HERE ANY MORE. Under the old one-at-a-time board, finishing the
        // daily quest had to roll the next one or the player was left with an empty card. All
        // three are dealt at midnight now, so a completion just stamps its own seat and leaves the
        // other two exactly as they were.
    }

    /// <summary>
    /// Play sound effect and haptic feedback on quest completion
    /// </summary>
    private void PlayCompletionEffects()
    {
        try
        {
            // Play Windows notification sound
            SystemSounds.Exclamation.Play();

            // Quest completion posts its OWN event kind, so the Haptics tab's "Quest complete"
            // routing row (enable / intensity / pattern / target toy) is what decides how this
            // feels. It used to call AchievementPatternAsync(), which reads the ACHIEVEMENT row —
            // leaving the quest row on screen doing nothing at all.
            // ONE call only: this fired twice, which stacked two overlapping copies of the
            // same pattern on the toy rather than making it play any stronger.
            _ = App.Haptics?.PostEvent(Services.Haptics.Core.HapticEventKind.QuestComplete);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Error playing quest completion effects: {Error}", ex.Message);
        }
    }

    #endregion

    /// <summary>
    /// Recalculate daily quest streak from the completion calendar (single source of truth).
    /// Replaces fragile LastDailyQuestDate-based comparison.
    /// </summary>
    public void RecalculateStreak()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        var completedDates = new HashSet<DateTime>(
            Progress.DailyQuestCompletionDates.Select(d => d.Date));

        // Also include streak-shielded dates as "completed" for streak calculation
        if (settings.StreakShieldUsedDates != null)
        {
            foreach (var shieldDate in settings.StreakShieldUsedDates)
                completedDates.Add(shieldDate.Date);
        }

        int streak = 0;
        var checkDate = DateTime.Today;

        // If today isn't completed yet, start checking from yesterday
        if (!completedDates.Contains(checkDate))
            checkDate = checkDate.AddDays(-1);

        while (completedDates.Contains(checkDate))
        {
            streak++;
            checkDate = checkDate.AddDays(-1);
        }

        // Repair upward only. Day-to-day growth is handled incrementally by
        // AdvanceQuestStreak (so the streak is not capped at the calendar's ~90-day
        // trim window). Recalculation may only RAISE the streak — e.g. when a cloud
        // sync merges in completion dates that prove a longer run than the stored
        // counter — and must never lower it (dates may have been trimmed/lost).
        if (streak > settings.DailyQuestStreak)
        {
            App.Logger?.Debug("RecalculateStreak: calendar proves {Calculated} > stored {Current} — repairing upward",
                streak, settings.DailyQuestStreak);
            settings.DailyQuestStreak = streak;
        }

        // Keep LastDailyQuestDate in sync with actual quest completions (not shield fills)
        if (Progress.DailyQuestCompletionDates.Count > 0)
            settings.LastDailyQuestDate = Progress.DailyQuestCompletionDates.Max();
    }

    /// <summary>
    /// Grow (or reset) the persisted daily quest streak on the first daily-quest
    /// completion of a new day. Growth is tracked incrementally HERE rather than
    /// recomputed from the completion calendar, because the calendar is trimmed to a
    /// rolling 90-day window (see CompleteQuest) — recomputing from it caps the streak
    /// at ~91 and freezes it there permanently. Call exactly once per day, AFTER the
    /// streak shield has had a chance to fill yesterday.
    /// </summary>
    private void AdvanceQuestStreak()
    {
        var settings = App.Settings?.Current;
        if (settings == null) return;

        var yesterday = DateTime.Today.AddDays(-1);
        bool continuesStreak =
            Progress.DailyQuestCompletionDates.Any(d => d.Date == yesterday)
            || (settings.StreakShieldUsedDates?.Any(d => d.Date == yesterday) ?? false);

        if (continuesStreak || settings.DailyQuestStreak <= 0)
        {
            // Yesterday completed/shielded (chain continues) — or first streak ever.
            settings.DailyQuestStreak++;
        }
        else
        {
            // Gap with no shield: the streak broke; today is day 1 of a new streak.
            App.Logger?.Information("Quest streak reset to 1 — gap before {Today} (was {Prev})",
                DateTime.Today.ToString("yyyy-MM-dd"), settings.DailyQuestStreak);
            settings.DailyQuestStreak = 1;
        }
    }

    /// <summary>
    /// Reset all quest progress (used on account deletion and account SWITCH to clear
    /// account-specific data — a plain logout preserves the file, see <see cref="StampOwner"/>)
    /// </summary>
    /// <param name="generateQuests">If false, skip quest generation (caller will generate after cloud sync)</param>
    public void ResetProgress(bool generateQuests = true)
    {
        Progress = new QuestProgress();
        _isDirty = false;
        Save();

        if (generateQuests)
        {
            // Generate fresh quests so the UI doesn't show "Loading..."
            CheckAndGenerateQuests();
        }

        App.Logger?.Information("QuestService progress reset (generateQuests={Generate})", generateQuests);
    }

    /// <summary>
    /// Record which unified account the current quest file belongs to, WITHOUT wiping it.
    /// Called on logout instead of <see cref="ResetProgress"/> (BUG-BN8X9B9SZ5): active quest
    /// progress exists nowhere but quests.json — the server only holds aggregate quest
    /// stats — so wiping at logout turned "log out and back in" (our standard auth-recovery
    /// advice!) into guaranteed loss of the day's quest progress. The stamp lets
    /// <see cref="EnsureOwnedBy"/> do the wipe later, exactly when a DIFFERENT account signs in.
    /// </summary>
    public void StampOwner(string? unifiedId)
    {
        if (string.IsNullOrEmpty(unifiedId)) return;
        if (Progress.OwnerUnifiedId == unifiedId) return;
        Progress.OwnerUnifiedId = unifiedId;
        Save();
        App.Logger?.Information("Quest progress stamped as owned by {UnifiedId} (preserved across logout)", unifiedId);
    }

    /// <summary>
    /// Make sure the local quest file belongs to <paramref name="unifiedId"/>. If it is stamped
    /// for a different account, wipe it (the one case the old logout-time wipe was actually
    /// protecting against — quest progress must not bleed between accounts on a shared install).
    /// If it is unstamped (pre-fix file), adopt it. Safe to call repeatedly; called from the
    /// profile-load path so every login route funnels through it, not just the login dialog.
    /// </summary>
    public void EnsureOwnedBy(string? unifiedId)
    {
        if (string.IsNullOrEmpty(unifiedId)) return;

        var owner = Progress.OwnerUnifiedId;
        if (!string.IsNullOrEmpty(owner) && owner != unifiedId)
        {
            App.Logger?.Information("Quest progress belongs to {Owner} but {UnifiedId} signed in — resetting quest file", owner, unifiedId);
            ResetProgress(generateQuests: false);
        }

        StampOwner(unifiedId);
    }

    #region IDisposable

    public void Dispose()
    {
        _saveTimer.Stop();
        _refreshTimer.Stop();
        if (_isDirty)
        {
            Save();
        }
    }

    #endregion
}
