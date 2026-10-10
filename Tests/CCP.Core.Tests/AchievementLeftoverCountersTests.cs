using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The lifetime counters lane k23 left (k25): enhancements built off the editor's save signal, the
/// once-ever companion chat backfill, the combination badges checked when their settings change, and
/// the Deeper play-time credit.
/// </summary>
[Collection(SessionStatics.Name)]   // swaps AchievementEngine.Current and its UI post
public sealed class AchievementLeftoverCountersTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-ach-left-").FullName;
    private readonly AchievementEngine? _before = AchievementEngine.Current;
    private readonly Action<Action> _post = AchievementEngine.UiPost;
    private readonly string? _savedPath = CoreTutorialEvents.LastSavedEnhancementPath;

    private int _n;
    private AchievementEngine Engine() => new(new AchievementStore(Path.Combine(_dir, $"achievements{_n++}.json")));

    public void Dispose()
    {
        AchievementEngine.Attach(_before);
        AchievementEngine.UiPost = _post;
        CoreTutorialEvents.LastSavedEnhancementPath = _savedPath;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string SaveEnhancement(int rules)
    {
        var enh = new Enhancement();
        for (var i = 0; i < rules; i++) enh.Rules.Add(new EnhancementRule());
        var path = Path.Combine(_dir, $"e{rules}.ccpenh");
        File.WriteAllText(path, EnhancementSerializer.Save(enh));
        return path;
    }

    [Fact]
    public void TheEditorsSaveSignal_CountsABuild_AndFiveRulesIsMadScientist()
    {
        var e = Engine();
        AchievementEngine.Attach(e);

        CoreTutorialEvents.Emit("RuleAdded");                       // not a save
        Assert.Equal(0, e.Progress.EnhancementsBuilt);

        CoreTutorialEvents.LastSavedEnhancementPath = SaveEnhancement(2);
        CoreTutorialEvents.Emit("FileSaved");
        Assert.Equal(1, e.Progress.EnhancementsBuilt);
        Assert.True(e.Progress.IsUnlocked("not_a_video_editor"));
        Assert.False(e.Progress.IsUnlocked("mad_scientist"));

        CoreTutorialEvents.LastSavedEnhancementPath = SaveEnhancement(5);
        CoreTutorialEvents.Emit("FileSaved");
        Assert.Equal(2, e.Progress.EnhancementsBuilt);
        Assert.True(e.Progress.IsUnlocked("mad_scientist"));

        // A file that can not be read back still counts the build.
        CoreTutorialEvents.LastSavedEnhancementPath = Path.Combine(_dir, "gone.ccpenh");
        CoreTutorialEvents.Emit("FileSaved");
        Assert.Equal(3, e.Progress.EnhancementsBuilt);

        AchievementEngine.Attach(null);
        CoreTutorialEvents.Emit("FileSaved");                       // detached: nothing counts
        Assert.Equal(3, e.Progress.EnhancementsBuilt);
    }

    [Fact]
    public void ChatBackfill_RunsOnce_OnlyRaises_AndWaitsForAReadableSource()
    {
        var e = Engine();
        e.Progress.CompanionMessages = 4;

        Assert.False(e.BackfillCompanionChat(null, null));          // both reads failed: latch stays open
        Assert.False(e.Progress.CompanionChatBackfilled);
        Assert.False(e.BackfillCompanionChat((ConditioningControlPanel.Services.Companion.Brain.CompanionBrain?)null));
        Assert.False(e.Progress.CompanionChatBackfilled);

        Assert.True(e.BackfillCompanionChat(12, null));
        Assert.True(e.Progress.CompanionChatBackfilled);
        Assert.Equal(12, e.Progress.CompanionMessages);
        Assert.True(e.Progress.IsUnlocked("pleased_to_meet_you"));
        Assert.False(e.Progress.IsUnlocked("pillow_talk"));

        Assert.False(e.BackfillCompanionChat(900, 900));            // once ever
        Assert.Equal(12, e.Progress.CompanionMessages);

        var thin = Engine();
        thin.Progress.CompanionMessages = 30;
        Assert.True(thin.BackfillCompanionChat(3, 7));              // a thinner record never lowers the count
        Assert.Equal(30, thin.Progress.CompanionMessages);

        var none = Engine();
        Assert.True(none.BackfillCompanionChat(0, 0));              // looked, found nothing: latched, no badge
        Assert.False(none.Progress.IsUnlocked("pleased_to_meet_you"));
    }

    [Fact]
    public void ComboBadges_AreCheckedWhenTheirSettingsChange_NotOnATimer()
    {
        var s = CoreSettings.Current;
        var old = (s.BubblesEnabled, s.BouncingTextEnabled, s.SpiralEnabled, s.StrictLockEnabled, s.PanicKeyEnabled, s.PinkFilterEnabled);
        var posts = 0;
        try
        {
            (s.BubblesEnabled, s.BouncingTextEnabled, s.SpiralEnabled) = (false, false, false);
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.PinkFilterEnabled) = (false, true, false);
            var e = Engine();
            AchievementEngine.UiPost = a => { posts++; a(); };
            AchievementEngine.Attach(e);
            Assert.False(e.Progress.HasSystemOverload);

            s.BubblesEnabled = true;
            s.BouncingTextEnabled = true;
            Assert.False(e.Progress.HasSystemOverload);
            var before = posts;
            s.FlashFrequency = s.FlashFrequency;                    // an unrelated setting asks for no check
            Assert.Equal(before, posts);
            s.SpiralEnabled = true;                                 // the third one lands: the badge, at once
            Assert.True(e.Progress.HasSystemOverload);
            Assert.True(e.Progress.IsUnlocked("system_overload"));

            s.StrictLockEnabled = true;
            s.PinkFilterEnabled = true;
            Assert.False(e.Progress.HasTotalLockdown);              // panic still on
            s.PanicKeyEnabled = false;
            Assert.True(e.Progress.HasTotalLockdown);

            // Detached: the settings object keeps no handler of ours.
            AchievementEngine.Attach(null);
            (s.BubblesEnabled, s.SpiralEnabled) = (false, true);
            before = posts;
            s.BubblesEnabled = true;
            Assert.Equal(before, posts);
        }
        finally
        {
            AchievementEngine.Attach(null);
            (s.BubblesEnabled, s.BouncingTextEnabled, s.SpiralEnabled, s.StrictLockEnabled, s.PanicKeyEnabled, s.PinkFilterEnabled) = old;
            CoreSettings.SaveImmediate();
        }
    }

    [Fact]
    public void ACombinationAlreadySet_IsSeenAtAttach()
    {
        var s = CoreSettings.Current;
        var old = (s.BubblesEnabled, s.BouncingTextEnabled, s.SpiralEnabled);
        try
        {
            (s.BubblesEnabled, s.BouncingTextEnabled, s.SpiralEnabled) = (true, true, true);
            var e = Engine();
            AchievementEngine.Attach(e);
            Assert.True(e.Progress.HasSystemOverload);
        }
        finally
        {
            AchievementEngine.Attach(null);
            (s.BubblesEnabled, s.BouncingTextEnabled, s.SpiralEnabled) = old;
            CoreSettings.SaveImmediate();
        }
    }

    [Fact]
    public void RemoteSessionStart_ZeroesTheCommandCount()
    {
        var e = Engine();
        e.Progress.RemoteCommandsThisSession = 9;
        e.TrackRemoteSessionStarted();
        Assert.Equal(0, e.Progress.RemoteCommandsThisSession);
        AchievementEngine.Attach(e);
        var ran = new List<string>();
        AchievementEngine.UiPost = a => { ran.Add("post"); a(); };
        e.Progress.RemoteCommandsThisSession = 3;
        AchievementEngine.OnCurrent(x => x.TrackRemoteSessionStarted(), "test");
        Assert.Equal(new[] { "post" }, ran);
        Assert.Equal(0, e.Progress.RemoteCommandsThisSession);
        AchievementEngine.OnCurrent(_ => throw new InvalidOperationException("listener"), "test");   // never throws
    }

    [Fact]
    public void DeeperMinutes_AddUpToPermanentResident()
    {
        var e = Engine();
        e.TrackDeeperMinutes(0);
        Assert.False(e.IsDirty);
        e.TrackDeeperMinutes(599.5);
        Assert.False(e.Progress.IsUnlocked("permanent_resident"));
        e.TrackDeeperMinutes(1);
        Assert.Equal(600.5, e.Progress.DeeperMinutes, 3);
        Assert.True(e.Progress.IsUnlocked("permanent_resident"));

        // The player's credit: only the time between two playing ticks, never a gap past six seconds.
        var credit = new RunningTimeCredit(TimeSpan.FromSeconds(6));
        Assert.Equal(0, credit.Sample(true, TimeSpan.FromSeconds(0)));
        Assert.Equal(0.1 / 60, credit.Sample(true, TimeSpan.FromSeconds(0.1)), 6);
        Assert.Equal(0, credit.Sample(false, TimeSpan.FromSeconds(5)));     // paused
        Assert.Equal(0, credit.Sample(true, TimeSpan.FromSeconds(50)));     // resumed: opens the stamp only
        Assert.Equal(0, credit.Sample(true, TimeSpan.FromSeconds(80)));     // a 30 s hole is not play time
    }
}
