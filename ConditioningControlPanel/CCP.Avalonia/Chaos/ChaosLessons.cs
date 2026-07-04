using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Chaos;
using ConditioningControlPanel.Models;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Chaos;

/// <summary>One lesson: gameplay proof required before an item's Unlock (level 1) is buyable.</summary>
public sealed class ChaosLessonDef
{
    public string Id = "";
    public string Text = "";
    public string Detail = "";
    public long Target = 1;
    public bool HighWater = false;
}

/// <summary>
/// Lessons engine. Avalonia port of the WPF class. Progress ticks live in memory during a run
/// and persist with the run-end save; completion persists immediately and fires reveal sync.
/// </summary>
public static class ChaosLessons
{
    public const long T_VIBE_POPPING = 10;
    public const long T_FREEZE_TRIGGER = 15;
    public const long T_PORN_DVD = 10;
    public const long T_SNAP_FIELD = 5;
    public const long T_RABBIT_CALLER = 25;
    public const long T_CHAIN_REACTION = 50;
    public const long T_BLINDFOLD = 10;
    public const long T_LAST_BREATH = 10;
    public const long T_TAKING_CHANCES = 8;
    public const long T_THE_PULL = 15;
    public const long T_INTRUSIVE = 50;
    public const long T_SURRENDER = 5;
    public const long T_SLOW_FUSES = 60;
    public const long T_SILK_TOUCH = 1;
    public const long T_POPUP_NOTIF = 3;
    public const long T_DRAFT4 = 15;
    public const long T_EXTREME_TIER = 10;

    public static readonly HashSet<string> Lessonless = new() { "e_stim", "the_spanker" };

    private static readonly HashSet<string> CurseBound = new() { "taking_chances", "surrender" };

    public static readonly IReadOnlyList<ChaosLessonDef> All = new List<ChaosLessonDef>
    {
        new() { Id = "vibe_popping", Text = "pop 10 treats inside 5 seconds", Target = T_VIBE_POPPING, HighWater = true,
                Detail = "pop 10 treats inside one rolling 5-second window. flash and whisper treats count." },
        new() { Id = "freeze_trigger", Text = "catch 15 freeze pickups", Target = T_FREEZE_TRIGGER,
                Detail = "catch 15 freeze pickups (the ❄ bubble), lifetime." },
        new() { Id = "porn_dvd", Text = "endure 10 videos to the end", Target = T_PORN_DVD,
                Detail = "sit through 10 video payloads to their natural end." },
        new() { Id = "snap_field", Text = "defuse 5 threats inside a single loop", Target = T_SNAP_FIELD, HighWater = true,
                Detail = "snap 5 trances inside one loop." },
        new() { Id = "rabbit_caller", Text = "catch 25 rabbits", Target = T_RABBIT_CALLER,
                Detail = "catch 25 white rabbits, lifetime." },
        new() { Id = "chain_reaction", Text = "land 50 interlaced pops", Target = T_CHAIN_REACTION,
                Detail = "50 interlaced pops, lifetime." },
        new() { Id = "blindfold", Text = "defuse 10 threats while the screen is busy", Target = T_BLINDFOLD,
                Detail = "snap 10 trances while the screen is busy with an effect." },
        new() { Id = "last_breath", Text = "start 10 defuses with under a second left", Target = T_LAST_BREATH,
                Detail = "complete 10 holds that started with 0.8 seconds or less left." },
        new() { Id = "taking_chances", Text = "pop 8 prisms", Target = T_TAKING_CHANCES,
                Detail = "pop 8 mimic prisms, lifetime." },
        new() { Id = "the_pull", Text = "pop 15 bubbles without moving", Target = T_THE_PULL,
                Detail = "pop 15 treats with your cursor at rest." },
        new() { Id = "intrusive_thoughts", Text = "pop 50 whispering treats", Target = T_INTRUSIVE,
                Detail = "pop 50 whisper treats, lifetime." },
        new() { Id = "surrender", Text = "accept 5 sins", Target = T_SURRENDER,
                Detail = "accept 5 sins at draft tables, lifetime." },
        new() { Id = "slow_fuses", Text = "spend a minute holding on", Target = T_SLOW_FUSES,
                Detail = "hold defuse channels for 60 seconds in total, lifetime." },
        new() { Id = "silk_touch", Text = "finish a loop without a single detonation", Target = T_SILK_TOUCH,
                Detail = "finish one whole loop with zero detonations." },
        new() { Id = "popup_notification", Text = "end 3 descents with resistance still held", Target = T_POPUP_NOTIF,
                Detail = "finish 3 descents to the buzzer with at least 1 resistance still held." },
        new() { Id = "draft4", Text = "take 15 mantras", Target = T_DRAFT4,
                Detail = "take 15 cards at draft tables, lifetime." },
        new() { Id = "extreme_tier", Text = "finish 10 relentless descents", Target = T_EXTREME_TIER,
                Detail = "finish 10 descents on Relentless or harder, full course to the buzzer." },
    };

    private static readonly Dictionary<string, ChaosLessonDef> _byId = All.ToDictionary(l => l.Id);

    public static ChaosLessonDef? ById(string id) => _byId.TryGetValue(id, out var l) ? l : null;

    public static bool IsLessonBlocked(string id)
    {
        if (!_byId.ContainsKey(id) || Lessonless.Contains(id)) return false;
        if (IsComplete(id)) return false;
        if (CurseBound.Contains(id))
        {
            var settings = App.Services?.GetService<global::ConditioningControlPanel.Core.Services.Settings.ISettingsService>()?.Current;
            if (settings?.ChaosAllowCurses == false) return false;
        }
        return true;
    }

    public static bool IsComplete(string id) =>
        !_byId.ContainsKey(id) || Lessonless.Contains(id) ||
        (ChaosMeta.State.LessonsComplete?.Contains(id) ?? false);

    public static long Progress(string id) =>
        ChaosMeta.State.LessonProgress != null &&
        ChaosMeta.State.LessonProgress.TryGetValue(id, out var p) ? p : 0;

    public static event Action<string>? LessonCompleted;

    public static void Tick(string id, long amount = 1)
    {
        var def = ById(id);
        if (def == null || amount <= 0 || IsComplete(id)) return;
        SetProgress(def, Math.Min(def.Target, Progress(id) + amount));
    }

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
        App.Services?.GetRequiredService<ILogger<object>>().LogInformation("Chaos lesson complete: {Id}", def.Id);
        RevealService.Sync("lesson:" + def.Id);
        try { LessonCompleted?.Invoke(def.Id); } catch { }
    }
}

public static class ChaosFirstTimes
{
    public const string Taste = "first_taste";
    public const string Snap = "first_snap";
    public const string Whisper = "first_whisper";
    public const string Yes = "first_yes";
    public const string Play = "first_play";

    public static readonly IReadOnlyDictionary<string, int> Amounts = new Dictionary<string, int>
    {
        [Taste] = 5, [Snap] = 10, [Whisper] = 10, [Yes] = 15, [Play] = 15,
    };

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Taste] = "first taste", [Snap] = "first snap", [Whisper] = "first whisper",
        [Yes] = "first yes", [Play] = "first play",
    };

    public static event Action<string, int>? Awarded;

    public static bool IsAwarded(string bonusId) =>
        ChaosMeta.State.FirstTimesAwarded?.Contains(bonusId) ?? false;

    public static bool TryAward(string bonusId)
    {
        if (!Amounts.TryGetValue(bonusId, out int amount)) return false;
        ChaosMeta.State.FirstTimesAwarded ??= new();
        if (!ChaosMeta.State.FirstTimesAwarded.Add(bonusId)) return false;
        ChaosMeta.State.Sparks += amount;
        ChaosMeta.Save();
        App.Services?.GetRequiredService<ILogger<object>>().LogInformation("Chaos first-time: {Id} +{Amount} drops", bonusId, amount);
        try { Awarded?.Invoke(bonusId, amount); } catch { }
        return true;
    }
}

/// <summary>
/// Gameplay-to-lessons glue. Avalonia port: simplified from WPF because many payload/cursor
/// services are not yet ported. Advanced lessons (chain_reaction, the_pull, blindfold) are
/// tracked conservatively; revisit once the underlying services land.
/// </summary>
public static class ChaosLessonHooks
{
    private const double VIBE_WINDOW_SEC = 5.0;
    private const double LAST_BREATH_BRINK_SEC = 0.8;
    private const double CHANNEL_MAX_SEC = 1.5;

    /// <summary>chain_reaction: two treat pops this close in time count as one interlaced pair.
    /// The Avalonia head does not expose per-pop screen coordinates (WPF's burst-overlap test),
    /// so this is the time-only proxy for WPF ChaosLessonHooks.cs:112-121 — dense popping (where
    /// Poppers chains actually happen) still ticks it.</summary>
    private const double CHAIN_WINDOW_SEC = 0.40;
    /// <summary>the_pull: the cursor counts as at rest under this much pop-to-pop travel (px).
    /// Slightly looser than WPF's 3px sampled-rest test to absorb pop-cadence jitter.</summary>
    private const double PULL_REST_MAX_PX = 8.0;

    private static readonly Queue<DateTime> _vibePops = new();
    private static int _loopDefuses;
    private static bool _loopDirty;
    private static DateTime? _channelStartUtc;
    private static double _channelFraction;
    private static DateTime? _lastTreatPopUtc;                       // chain_reaction time proxy
    private static (double X, double Y)? _pullSample;                // the_pull cursor-at-rest
    private static DateTime _busyUntilUtc = DateTime.MinValue;       // blindfold screen-busy window

    public static void OnRunStarted()
    {
        _vibePops.Clear();
        _loopDefuses = 0;
        _loopDirty = false;
        _channelStartUtc = null;
        _channelFraction = 0;
        _lastTreatPopUtc = null;
        _pullSample = null;
        _busyUntilUtc = DateTime.MinValue;
    }

    public static void OnTreatPopped(string variantId)
    {
        Safe(() =>
        {
            var now = DateTime.UtcNow;
            RegisterScreenBusy(variantId);
            if (variantId == "subliminal") ChaosLessons.Tick("intrusive_thoughts");
            if (!ChaosLessons.IsComplete("vibe_popping"))
            {
                _vibePops.Enqueue(now);
                while (_vibePops.Count > 0 && (now - _vibePops.Peek()).TotalSeconds > VIBE_WINDOW_SEC)
                    _vibePops.Dequeue();
                ChaosLessons.RaiseTo("vibe_popping", _vibePops.Count);
            }

            // chain_reaction: this pop landed while the previous pop's burst still glowed.
            if (!ChaosLessons.IsComplete("chain_reaction"))
            {
                if (_lastTreatPopUtc.HasValue && (now - _lastTreatPopUtc.Value).TotalSeconds <= CHAIN_WINDOW_SEC)
                    ChaosLessons.Tick("chain_reaction");
                _lastTreatPopUtc = now;
            }

            // the_pull: the bubble came to the hand — popped with the cursor at rest.
            if (!ChaosLessons.IsComplete("the_pull"))
            {
                var cur = GetCursor();
                if (cur.HasValue)
                {
                    if (_pullSample.HasValue)
                    {
                        double dx = cur.Value.X - _pullSample.Value.X, dy = cur.Value.Y - _pullSample.Value.Y;
                        if (dx * dx + dy * dy <= PULL_REST_MAX_PX * PULL_REST_MAX_PX) ChaosLessons.Tick("the_pull");
                    }
                    _pullSample = (cur.Value.X, cur.Value.Y);
                }
            }

            ChaosFirstTimes.TryAward(ChaosFirstTimes.Taste);
        });
    }

    public static void OnPrismPopped() => Safe(() => ChaosLessons.Tick("taking_chances"));
    public static void OnRabbitCaught() => Safe(() => ChaosLessons.Tick("rabbit_caller"));
    /// <summary>A rabbit took its FIRST smack (the Spanker equipped). Counts toward rabbit_caller:
    /// with the Spanker on, rabbits can't be caught at all, so the first smack is the substitute
    /// tick (WPF ChaosLessonHooks.OnRabbitSpanked, BubbleService.cs:3789).</summary>
    public static void OnRabbitSpanked() => Safe(() => ChaosLessons.Tick("rabbit_caller"));
    public static void OnFreezeCaught() => Safe(() => ChaosLessons.Tick("freeze_trigger"));

    public static void OnChannelStarted() => Safe(() => _channelStartUtc = DateTime.UtcNow);
    public static void OnChannelBroken() => Safe(() => EndChannel());

    public static void OnDefuseCompleted(double fuseSecLeft, bool viaChannel)
    {
        Safe(() =>
        {
            if (viaChannel)
            {
                EndChannel();
                if (fuseSecLeft <= LAST_BREATH_BRINK_SEC) ChaosLessons.Tick("last_breath");
            }
            _loopDefuses++;
            ChaosLessons.RaiseTo("snap_field", _loopDefuses);
            // blindfold: snapped a trance while the screen was busy with an effect.
            if (!ChaosLessons.IsComplete("blindfold") && IsScreenBusy()) ChaosLessons.Tick("blindfold");
            ChaosFirstTimes.TryAward(ChaosFirstTimes.Snap);
        });
    }

    /// <summary>Mark the screen "busy" for a payload's estimated on-screen lifetime (blindfold).
    /// Mirrors WPF ChaosLessonHooks.RegisterScreenBusy per-family durations.</summary>
    private static void RegisterScreenBusy(string variantId)
    {
        if (ChaosLessons.IsComplete("blindfold")) return;
        double sec = variantId switch
        {
            "flash" => 1.5,
            "subliminal" => 1.2,
            "pink" or "spiral" or "braindrain" => 3.0,
            "bambifreeze" => 3.0,
            "video" => 15.0,
            "htlink" => 8.0,
            _ => 0,
        };
        if (sec <= 0) return;
        var until = DateTime.UtcNow.AddSeconds(sec);
        if (until > _busyUntilUtc) _busyUntilUtc = until;
    }

    /// <summary>Derived "the screen is busy" flag: a recent payload window or a video still playing.</summary>
    private static bool IsScreenBusy() =>
        DateTime.UtcNow < _busyUntilUtc || AvaloniaChaosApp.Video?.IsPlaying == true;

    private static (double X, double Y)? GetCursor()
    {
        try
        {
            var p = App.Services?.GetService<IPointerState>()?.GetCursorPosition();
            return p.HasValue ? (p.Value.X, p.Value.Y) : null;
        }
        catch { return null; }
    }

    private static void EndChannel()
    {
        if (_channelStartUtc == null) return;
        double sec = Math.Clamp((DateTime.UtcNow - _channelStartUtc.Value).TotalSeconds, 0, CHANNEL_MAX_SEC);
        _channelStartUtc = null;
        ChaosMeta.State.TotalChannelSeconds += sec;
        if (ChaosLessons.IsComplete("slow_fuses")) return;
        _channelFraction += sec;
        long whole = (long)Math.Floor(_channelFraction);
        if (whole > 0)
        {
            _channelFraction -= whole;
            ChaosLessons.Tick("slow_fuses", whole);
        }
    }

    public static void OnDetonation() => Safe(() => _loopDirty = true);

    public static void OnLoopCompleted()
    {
        Safe(() =>
        {
            if (!_loopDirty) ChaosLessons.Tick("silk_touch");
            _loopDirty = false;
            _loopDefuses = 0;
        });
    }

    public static void OnRunCompleted(int shieldsLeft, bool ranFullCourse, string difficulty)
    {
        Safe(() =>
        {
            EndChannel();
            if (!ranFullCourse) return;
            OnLoopCompleted();
            if (shieldsLeft > 0) ChaosLessons.Tick("popup_notification");
            if (difficulty is "Hard" or "Extreme" or "Relentless" or "Inescapable") ChaosLessons.Tick("extreme_tier");
        });
    }

    public static void OnDraftCardTaken(bool isSin)
    {
        Safe(() =>
        {
            ChaosLessons.Tick("draft4");
            if (isSin) ChaosLessons.Tick("surrender");
            ChaosFirstTimes.TryAward(ChaosFirstTimes.Whisper);
            if (isSin) ChaosFirstTimes.TryAward(ChaosFirstTimes.Yes);
        });
    }

    public static void OnToyUsed(string toyId) => Safe(() => ChaosFirstTimes.TryAward(ChaosFirstTimes.Play));

    public static void OnRippleCast() => Safe(() => { });

    public static void OnVideoEndured() => Safe(() => ChaosLessons.Tick("porn_dvd"));

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex)
        {
            App.Services?.GetRequiredService<ILogger<object>>().LogDebug("ChaosLessonHooks: {E}", ex.Message);
        }
    }
}
