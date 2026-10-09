using System;
using Serilog;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The app moments the companion tube reacts to (WPF 7.1.5 <c>AvatarTubeWindow.xaml.cs:261-340</c>
    /// subscribed to ~14 services one by one: App.Video, App.Flash, App.Subliminal, App.Bubbles,
    /// App.BubbleCount, App.Achievements, App.Progression, App.Companion, App.MindWipe, App.BrainDrain,
    /// MainWindow.EngineStopped). On this port those surfaces live in different heads and lanes, so
    /// each one raises its moment here and the tube listens in one place. Ledger row tube#T5.
    ///
    /// <para>Raise from any thread; a throwing listener never reaches the raiser. Lane companion
    /// raises <see cref="RaiseCompanionSwitched"/> / <see cref="RaiseCompanionLevelUp"/>.</para>
    /// </summary>
    public static class CoreTubeEvents
    {
        public static event Action? VideoAboutToStart;
        public static event Action<string?>? VideoEnded;          // the video's title, when known
        public static event Action? FlashAboutToDisplay;
        public static event Action? FlashClicked;
        public static event Action<string>? FlashAudioPlaying;    // the clip's caption
        public static event Action? SubliminalDisplayed;
        public static event Action? BubblePopped;
        public static event Action? BubbleMissed;
        public static event Action? BubbleGameCompleted;
        public static event Action? BubbleGameFailed;
        public static event Action<string>? AchievementUnlocked;  // the achievement's display name
        public static event Action<int>? LevelUp;
        public static event Action<string, int, bool>? CompanionLevelUp;   // name, new level, is max level
        public static event Action<string>? CompanionSwitched;    // the new companion's display name
        public static event Action? MindWipeTriggered;
        public static event Action? BrainDrainTriggered;
        public static event Action? EngineStopped;

        public static void RaiseVideoAboutToStart() => Fire(VideoAboutToStart, nameof(VideoAboutToStart));
        public static void RaiseVideoEnded(string? title) => Fire(VideoEnded, title, nameof(VideoEnded));
        public static void RaiseFlashAboutToDisplay() => Fire(FlashAboutToDisplay, nameof(FlashAboutToDisplay));
        public static void RaiseFlashClicked() => Fire(FlashClicked, nameof(FlashClicked));
        public static void RaiseFlashAudioPlaying(string caption) => Fire(FlashAudioPlaying, caption, nameof(FlashAudioPlaying));
        public static void RaiseSubliminalDisplayed() => Fire(SubliminalDisplayed, nameof(SubliminalDisplayed));
        public static void RaiseBubblePopped() => Fire(BubblePopped, nameof(BubblePopped));
        public static void RaiseBubbleMissed() => Fire(BubbleMissed, nameof(BubbleMissed));
        public static void RaiseBubbleGameCompleted() => Fire(BubbleGameCompleted, nameof(BubbleGameCompleted));
        public static void RaiseBubbleGameFailed() => Fire(BubbleGameFailed, nameof(BubbleGameFailed));
        public static void RaiseAchievementUnlocked(string name) => Fire(AchievementUnlocked, name, nameof(AchievementUnlocked));
        public static void RaiseLevelUp(int level) => Fire(LevelUp, level, nameof(LevelUp));
        public static void RaiseCompanionSwitched(string name) => Fire(CompanionSwitched, name, nameof(CompanionSwitched));
        public static void RaiseMindWipeTriggered() => Fire(MindWipeTriggered, nameof(MindWipeTriggered));
        public static void RaiseBrainDrainTriggered() => Fire(BrainDrainTriggered, nameof(BrainDrainTriggered));
        public static void RaiseEngineStopped() => Fire(EngineStopped, nameof(EngineStopped));

        public static void RaiseCompanionLevelUp(string name, int level, bool isMax)
        {
            try { CompanionLevelUp?.Invoke(name, level, isMax); }
            catch (Exception ex) { Log.Debug(ex, "CoreTubeEvents CompanionLevelUp listener failed"); }
        }

        private static void Fire(Action? h, string what)
        {
            try { h?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "CoreTubeEvents {What} listener failed", what); }
        }

        private static void Fire<T>(Action<T>? h, T arg, string what)
        {
            try { h?.Invoke(arg); } catch (Exception ex) { Log.Debug(ex, "CoreTubeEvents {What} listener failed", what); }
        }
    }
}
