using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>One lesson: gameplay proof required before an item's Unlock (level 1) is buyable.</summary>
public sealed class ChaosLessonDef
{
    public string Id = "";       // == purchasable id (toy / accessory / habit)
    public string Text = "";     // shown on the locked shelf row (the mystery line)
    /// <summary>The hover SPECIFICS: exactly what counts, the precise window/threshold, and
    /// what doesn't count. The row keeps its mystery; the tooltip gives the math.</summary>
    public string Detail = "";
    public long Target = 1;
    /// <summary>High-water lessons track a best-within-window value via <see cref="ChaosLessons.RaiseTo"/>
    /// instead of accumulating ticks.</summary>
    public bool HighWater = false;
}

/// <summary>
/// Lessons engine. Rule: Unlock (level 1) of a Toy/Accessory/Habit needs drops AND its
/// lesson complete. Deeper levels: drops only. Capstone levels: drops + Devoted rank.
/// Items absent from the table have no lesson; e_stim and the_spanker NEVER have one
/// (scripted firsts). Progress ticks live in memory during a run and persist with the
/// run-end save; completion persists immediately and fires the bark + reveal sync.
/// </summary>
public static class ChaosLessons
{
    /// <summary>Head bark hooks (WPF: App.Bark.NotifyChaosLessonComplete / NotifyChaosFirstTime),
    /// fired at the exact point the WPF head barked. Null on a head without the companion bark.</summary>
    public static Action<string>? LessonBark, FirstTimeBark;

    // ---- thresholds (tunable) ----
    public const long T_VIBE_POPPING   = 10;  // treats popped inside a 5s rolling window (high-water)
    public const long T_FREEZE_TRIGGER = 15;  // freeze pickups caught
    public const long T_PORN_DVD       = 10;  // video payloads endured to completion
    public const long T_SNAP_FIELD     = 5;   // threats defused inside a single loop (high-water)
    public const long T_RABBIT_CALLER  = 25;  // rabbits caught
    public const long T_CHAIN_REACTION = 50;  // interlaced pops (overlapping pop bursts)
    public const long T_BLINDFOLD      = 10;  // defuses while any screen effect active
    public const long T_LAST_BREATH    = 10;  // brink defuses (channel started <= 0.8s left)
    public const long T_TAKING_CHANCES = 8;   // prisms popped
    public const long T_THE_PULL       = 15;  // pops with the cursor at rest
    public const long T_INTRUSIVE      = 50;  // subliminal treats popped
    public const long T_SURRENDER      = 5;   // sins accepted
    public const long T_SLOW_FUSES     = 60;  // cumulative seconds holding channels
    public const long T_SILK_TOUCH     = 1;   // full loop with zero detonations
    public const long T_POPUP_NOTIF    = 3;   // descents ended with resistance still held
    public const long T_DRAFT4         = 15;  // mantras taken lifetime
    public const long T_EXTREME_TIER   = 10;  // Relentless descents finished

    /// <summary>Ids that never get a lesson (scripted first purchases).</summary>
    public static readonly HashSet<string> Lessonless = new() { "e_stim", "the_spanker" };

    /// <summary>Lessons only provable with sins enabled (prisms ride the bright_colors sin;
    /// surrender needs sins accepted). Waived while the user's curse toggle is off — the
    /// proof is impossible, not failed, and a permanent brick punishes a comfort setting.</summary>
    private static readonly HashSet<string> CurseBound = new() { "taking_chances", "surrender" };

    public static readonly IReadOnlyList<ChaosLessonDef> All = new List<ChaosLessonDef>
    {
        new() { Id = "vibe_popping",        Text = "pop 10 treats inside 5 seconds",                Target = T_VIBE_POPPING, HighWater = true,
                Detail = "pop 10 treats inside one rolling 5-second window. flash and whisper treats count (specials don't). the bar keeps your best burst, so it never goes down." },
        new() { Id = "freeze_trigger",      Text = "catch 15 freeze pickups",                       Target = T_FREEZE_TRIGGER,
                Detail = "catch 15 freeze pickups (the ❄ bubble), lifetime. click it before it drifts off the screen." },
        new() { Id = "porn_dvd",            Text = "endure 10 videos to the end",                   Target = T_PORN_DVD,
                Detail = "sit through 10 video payloads to their natural end (or the 15 second cap). a tape cut short by waking up doesn't count." },
        new() { Id = "snap_field",          Text = "defuse 5 threats inside a single loop",         Target = T_SNAP_FIELD, HighWater = true,
                Detail = "snap 5 trances inside one loop. your own holds, toys, chains and zones all count. the bar keeps your best loop." },
        new() { Id = "rabbit_caller",       Text = "catch 25 rabbits",                              Target = T_RABBIT_CALLER,
                Detail = "catch 25 white rabbits, lifetime. with the spanker on, getting your hands on one — the first smack — counts too." },
        new() { Id = "chain_reaction",      Text = "land 50 interlaced pops",                       Target = T_CHAIN_REACTION,
                Detail = "50 interlaced pops, lifetime: pop a bubble while another burst from the last 0.4 seconds still glows within reach (about 170px). a tight cluster pays several at once." },
        new() { Id = "blindfold",           Text = "defuse 10 threats while the screen is busy",    Target = T_BLINDFOLD,
                Detail = "snap 10 trances while the screen is busy with an effect: a fresh flash or whisper, an overlay, a video, gif rain. lifetime." },
        new() { Id = "last_breath",         Text = "start 10 defuses with under a second left",     Target = T_LAST_BREATH,
                Detail = "complete 10 holds that STARTED with 0.8 seconds or less left on the trance. starting late is the skill; finishing the hold is the proof." },
        new() { Id = "taking_chances",      Text = "pop 8 prisms",                                  Target = T_TAKING_CHANCES,
                Detail = "pop 8 mimic prisms (the swirling ❂), lifetime. yes, the effect sealed inside fires every time." },
        new() { Id = "the_pull",            Text = "pop 15 bubbles without moving",                 Target = T_THE_PULL,
                Detail = "pop 15 treats with your cursor at rest (under ~3px of drift in the last half second). park the hand. let them come to you." },
        new() { Id = "intrusive_thoughts",  Text = "pop 50 whispering treats",                      Target = T_INTRUSIVE,
                Detail = "pop 50 whisper treats (the ♥ subliminal bubble), lifetime." },
        new() { Id = "surrender",           Text = "accept 5 sins",                                 Target = T_SURRENDER,
                Detail = "accept 5 sins at draft tables, lifetime. the first free taste counts too." },
        new() { Id = "slow_fuses",          Text = "spend a minute holding on",                     Target = T_SLOW_FUSES,
                Detail = "hold defuse channels for 60 seconds in total, lifetime. the clock only runs while your button is down on a live bubble; a full hold is about 1 second." },
        new() { Id = "silk_touch",          Text = "finish a loop without a single detonation",     Target = T_SILK_TOUCH,
                Detail = "finish one whole loop with zero detonations. a touched tease dirties it too. the run's final loop counts when the clock runs out." },
        new() { Id = "popup_notification",  Text = "end 3 descents with resistance still held",     Target = T_POPUP_NOTIF,
                Detail = "finish 3 descents to the buzzer with at least 1 resistance still held. waking up early doesn't count." },
        new() { Id = "draft4",              Text = "take 15 mantras",                               Target = T_DRAFT4,
                Detail = "take 15 cards at draft tables, lifetime. mantras and sins both count; skips don't." },
        new() { Id = "extreme_tier",        Text = "finish 10 relentless descents",                 Target = T_EXTREME_TIER,
                Detail = "finish 10 descents on Relentless or harder, full course to the buzzer. (the deeper door also wants the Devoted rank.)" },
    };

    private static readonly Dictionary<string, ChaosLessonDef> _byId = All.ToDictionary(l => l.Id);

    public static ChaosLessonDef? ById(string id) => _byId.TryGetValue(id, out var l) ? l : null;

    /// <summary>True when this id's Unlock is still lesson-blocked.</summary>
    public static bool IsLessonBlocked(string id) =>
        _byId.ContainsKey(id) && !Lessonless.Contains(id) && !IsComplete(id)
        && !(CurseBound.Contains(id) && CoreSettings.Current.ChaosAllowCurses == false);

    public static bool IsComplete(string id) =>
        !_byId.ContainsKey(id) || Lessonless.Contains(id) ||
        (ChaosMeta.State.LessonsComplete?.Contains(id) ?? false);

    public static long Progress(string id) =>
        ChaosMeta.State.LessonProgress != null &&
        ChaosMeta.State.LessonProgress.TryGetValue(id, out var p) ? p : 0;

    /// <summary>Raised on completion (lesson_id). Fired on whatever thread ticked it.</summary>
    public static event Action<string>? LessonCompleted;

    /// <summary>Accumulate progress. In-memory until the next meta save; completion saves now.</summary>
    public static void Tick(string id, long amount = 1)
    {
        var def = ById(id);
        if (def == null || amount <= 0 || IsComplete(id)) return;
        SetProgress(def, Math.Min(def.Target, Progress(id) + amount));
    }

    /// <summary>High-water progress: record a best-so-far value (e.g. pops within a window).</summary>
    public static void RaiseTo(string id, long value)
    {
        var def = ById(id);
        if (def == null || IsComplete(id)) return;
        if (value <= Progress(id)) return;
        SetProgress(def, Math.Min(def.Target, value));
    }

    private static void SetProgress(ChaosLessonDef def, long value)
    {
        ChaosMeta.State.LessonProgress ??= new();
        ChaosMeta.State.LessonProgress[def.Id] = value;
        if (value < def.Target) return;

        ChaosMeta.State.LessonsComplete ??= new();
        if (!ChaosMeta.State.LessonsComplete.Add(def.Id)) return;
        ChaosMeta.Save();
        Log.Information("Chaos lesson complete: {Id}", def.Id);
        try { LessonBark?.Invoke(def.Id); } catch { }
        RevealService.Sync("lesson:" + def.Id);
        try { LessonCompleted?.Invoke(def.Id); } catch { }
    }
}

/// <summary>
/// First-times: one-time drops bonuses for firsts down the hole. Display family name:
/// "first times". first_fall is the existing FIRST_SPARK_BONUS (renamed display only).
/// </summary>
public static class ChaosFirstTimes
{
    public const string Taste   = "first_taste";    // first pop, +5
    public const string Snap    = "first_snap";     // first defuse, +10
    public const string Whisper = "first_whisper";  // first draft pick, +10
    public const string Yes     = "first_yes";      // first sin, +15
    public const string Play    = "first_play";     // first toy fire, +15

    public static readonly IReadOnlyDictionary<string, int> Amounts = new Dictionary<string, int>
    {
        [Taste] = 5, [Snap] = 10, [Whisper] = 10, [Yes] = 15, [Play] = 15,
    };

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Taste] = "first taste", [Snap] = "first snap", [Whisper] = "first whisper",
        [Yes] = "first yes", [Play] = "first play",
    };

    /// <summary>Raised after a first-time awards (bonus_id) so the run HUD can toast it.</summary>
    public static event Action<string, int>? Awarded;

    /// <summary>Cheap pre-check so hot paths can skip the call once a bonus is banked.</summary>
    public static bool IsAwarded(string bonusId) =>
        ChaosMeta.State.FirstTimesAwarded != null && ChaosMeta.State.FirstTimesAwarded.Contains(bonusId);

    /// <summary>Award once: banks drops immediately, persists, fires bark + toast event.</summary>
    public static bool TryAward(string bonusId)
    {
        if (!Amounts.TryGetValue(bonusId, out int amount)) return false;
        ChaosMeta.State.FirstTimesAwarded ??= new();
        if (!ChaosMeta.State.FirstTimesAwarded.Add(bonusId)) return false;
        ChaosMeta.State.Sparks += amount;
        ChaosMeta.Save();
        Log.Information("Chaos first-time: {Id} +{Amount} drops", bonusId, amount);
        try { ChaosLessons.FirstTimeBark?.Invoke(bonusId); } catch { }
        try { Awarded?.Invoke(bonusId, amount); } catch { }
        return true;
    }
}
