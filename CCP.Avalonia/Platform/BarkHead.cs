using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of WPF 7.1.5 <c>Services/Companion/BarkService.cs</c> (ai#1): one Core
/// <see cref="BarkEngine"/> (rules, gate, rotation - same numbers as WPF), its mouth (the tube's public
/// Giggle / GigglePriority), the CoreBark doorbell seams, the brain's bark echo, the Phrase Manager's bark
/// rows, the mod-switch reload, and the subset of WPF's WireSubscriptions block this head has sources
/// for: flash / subliminal / bubble pops, video start, player level-up, companion level-up, achievement,
/// quests, mantra, lockdown, mod switch, user chat, numeric setting changes.
/// ponytail: no source here yet for webcam/gaze/blink trainer, attention checks, bubble count, bouncing
/// text, lock card (+ its coin-flip pool bark), brain drain / mind wipe, keyword triggers, window
/// awareness ({0} falls back to "that"), skill tree, roadmap, quiz, remote control, Patreon tier,
/// Discord, tutorial, update, session engine phases, the SessionSetupReady debounce, possession,
/// emergency exit, Descent, the Chaos run events beyond the three CoreBark already carries, Panic (the
/// port's panic path silences the tube by contract), and TabNavigated (the shell's ShowTab has no call).
/// The tube's idle beat and launch greeting call <see cref="CoreBark.TryDispatchIdle"/> /
/// <see cref="CoreBark.TryAppOpened"/> - lane tube owns that one-line hookup in AvatarTubeWindow.Speech.cs.
/// </summary>
internal static class BarkHead
{
    internal static BarkEngine? Engine { get; private set; }

    internal static void Start()
    {
        if (Engine != null) return;
        var e = new BarkEngine();
        Engine = e;
        Seed(e);
        e.Start();
        Wire(e);
    }

    /// <summary>Test-only: forget the engine and every seam it seeded.</summary>
    internal static void Reset()
    {
        Engine = null;
        CoreBark.UiAction = null; CoreBark.TabNavigated = null; CoreBark.FeatureOpened = null;
        CoreBark.AvatarClicked = null; CoreBark.ChaosDraftAutopick = null; CoreBark.ChaosResultsShown = null;
        CoreBark.ChaosRankUp = null; CoreBark.AllLinesProvider = null; CoreBark.IdleDispatcher = null;
        CoreBark.AppOpenedDispatcher = null; CoreBark.RaiseProvider = null;
        CoreModsHooks.ReloadBarkRules = null;
        CoreSettingsHooks.SettingChangedSink = null;
    }

    /// <summary>The mouth and the doorbell. Separate from <see cref="Wire"/> so a test can seed without events.</summary>
    internal static void Seed(BarkEngine e)
    {
        e.Speak = Speak;
        e.IsSpeaking = () => Views.AvatarTube.AvatarTubeWindow.Live?.IsSpeaking == true;
        e.IsCompanionBusy = ms => Views.AvatarTube.AvatarTubeWindow.Live?.IsCompanionBusy(ms) == true;
        e.LiveField = field => field switch
        {
            "video_playing" => CoreEngine.Video?.IsPlaying == true,
            _ => null,
        };

        CoreBark.UiAction = a => { if (!string.IsNullOrEmpty(a)) e.Raise("UiAction", c => c.Set("action", a!)); };
        CoreBark.TabNavigated = t => { if (!string.IsNullOrEmpty(t)) e.Raise("TabNavigated", c => c.Set("tab", t!)); };
        CoreBark.FeatureOpened = f => { if (!string.IsNullOrEmpty(f)) e.Raise("FeatureOpened", c => c.Set("feature", f!)); };
        CoreBark.AvatarClicked = () =>
        {
            e.State.RegisterAvatarClick();
            e.Raise("AvatarClicked", c => c.Set("clicks_60s", (double)e.State.AvatarClicksWithin(BarkEngine.RapidClickWindow)));
        };
        CoreBark.ChaosDraftAutopick = () => e.Raise("ChaosDraftAutopick");
        CoreBark.ChaosRankUp = rank => e.Raise("ChaosRankUp", c => c.Set("rank", rank));
        CoreBark.ChaosResultsShown = (score, best, pbDelta, isPb, defused, detonated, bestCombo, difficulty) =>
            e.Raise("ChaosResultsShown", c => c
                .Set("score", score).Set("best_score", best).Set("pb_delta", pbDelta).Set("is_pb", isPb)
                .Set("defused", defused).Set("detonated", detonated).Set("best_combo", (double)bestCombo)
                .Set("difficulty", difficulty));
        CoreBark.AllLinesProvider = e.GetAllBarkLines;
        CoreBark.IdleDispatcher = e.DispatchIdle;
        CoreBark.AppOpenedDispatcher = bucket => e.Raise("AppOpened", c => c.Set("away_bucket", bucket ?? "first"));
        CoreBark.RaiseProvider = (trigger, values, guaranteed) => e.Raise(trigger, c =>
        {
            if (values == null) return;
            foreach (var kv in values) c.Set(kv.Key, kv.Value);
        }, guaranteed);
        CoreModsHooks.ReloadBarkRules = e.ReloadRules;
        CoreSettingsHooks.SettingChangedSink = name => OnSettingChanged(e, name);
    }

    /// <summary>WPF Speak's routing: preempting barks via GigglePriority (the clip plays as the bubble's
    /// voice, no giggle on top); ordinary text-only barks via Giggle. An ordinary VOICED bark also goes
    /// through GigglePriority because Giggle has no clip parameter on this tube; the gate already dropped
    /// it if she was mid-bubble ("speaking"), so it never cuts a line off.</summary>
    internal static void Speak(BarkSpeech s)
    {
        var tube = Views.AvatarTube.AvatarTubeWindow.Live;
        if (tube == null) return;
        if (s.SilentEgg || (!s.Priority && s.AudioPath == null)) { tube.Giggle(s.Text); return; }
        tube.GigglePriority(s.Text, playSound: s.AudioPath == null, aiGenerated: false,
            phraseAudioPath: s.AudioPath, barkVoice: s.AudioPath != null, mood: s.Mood);
    }

    private static readonly Dictionary<string, System.Reflection.PropertyInfo?> PropCache = new(StringComparer.Ordinal);

    /// <summary>WPF NotifySettingChanged: every change counts as a setup action; numeric/bool/enum ones
    /// raise SettingChanged with their value.</summary>
    internal static void OnSettingChanged(BarkEngine e, string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        e.State.MarkSettingChanged();
        System.Reflection.PropertyInfo? pi;
        lock (PropCache)
        {
            if (!PropCache.TryGetValue(name!, out pi)) PropCache[name!] = pi = typeof(global::ConditioningControlPanel.Models.AppSettings).GetProperty(name!);
        }
        if (pi == null) return;
        object? raw;
        try { raw = pi.GetValue(CoreSettings.Current); } catch { return; }
        double value;
        switch (raw)
        {
            case bool b: value = b ? 1 : 0; break;
            case int i: value = i; break;
            case long l: value = l; break;
            case double d: value = d; break;
            case float f: value = f; break;
            case Enum en: value = Convert.ToDouble(en); break;
            default: return;
        }
        e.Raise("SettingChanged", c => c.Set("setting", name!).Set("value", value));
    }

    private static void Wire(BarkEngine e)
    {
        try
        {
            App.FeatureUsed += f =>
            {
                if (f == global::ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureFlash) e.Raise("FlashDisplayed");
                else if (f == global::ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureSubliminal) e.Raise("SubliminalDisplayed");
                else if (f == global::ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureBubbles) e.Raise("BubblePopped");
            };
            if (CoreEngine.Video is { } video) video.VideoStarted += () => e.Raise("VideoStarted");
            ProgressionBank.LevelUp += lvl => e.Raise("LevelUp", c => c.Set("level", lvl));
            CompanionCore.LevelUp += (_, lvl) => e.Raise("CompanionLevelUp", c => c.Set("level", lvl));
            App.UserMessageSent += () => { e.NoteUserMessage(); e.Raise("UserMessageSent"); };
            if (App.Achievements is { } ach) ach.Unlocked += (_, a) => e.Raise("AchievementUnlocked", c => c.Set("achievement", a?.Id ?? ""));
            if (App.Quests is { } q)
            {
                q.QuestCompleted += (_, _) => e.Raise("QuestCompleted");
                q.QuestProgressChanged += (_, _) => e.Raise("QuestProgressChanged");
                q.QuestsRefreshed += (_, _) => e.Raise("QuestsRefreshed");
            }
            App.Mantra.MantraCompleted += () => e.Raise("MantraCompleted");
            App.Mantra.StreakChanged += s => e.Raise("MantraStreakChanged", c => c.Set("streak", s));
            App.Mantra.StreakBroken += () => e.Raise("MantraStreakBroken");
            if (global::ConditioningControlPanel.Services.LockdownService.Current is { } ld)
            {
                ld.LockdownActivated += () => e.Raise("LockdownActivated");
                ld.LockdownDeactivated += () => e.Raise("LockdownDeactivated");
                ld.CountdownTick += ts => e.Raise("LockdownCountdownTick", c => c.Set("remaining_sec", ts.TotalSeconds));
            }
            if (App.Mods is { } mods)
                mods.ModChanged += (_, mod) =>
                {
                    e.State.RegisterModSwitch();
                    e.Raise("ModChanged", c => c.Set("mod", mod?.Id ?? "")
                        .Set("mod_switches_60s", e.State.ModSwitchesWithin(BarkEngine.RapidModSwitchWindow)));
                    e.ReloadRules();
                };
            // CompanionBrain.AttachBarkSource: the LLM learns what her recorded voice just said.
            App.Brain?.AttachBarkSource(h => e.BarkSpoken += h, h => e.BarkSpoken -= h);
        }
        catch (Exception ex) { Log.Warning(ex, "BarkHead: wiring failed"); }
    }
}
