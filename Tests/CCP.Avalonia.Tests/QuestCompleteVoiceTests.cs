using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The quest-complete voice line (WPF App.Flash.PlayRandomSound, MainWindow.Quests.cs:52): a RECORDED
/// clip from the flash voice pool or nothing. An empty pool, a missing file and a muted voice are all silent.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class QuestCompleteVoiceTests
{
    [Fact]
    public void PlaysARecordedClipOrStaysSilent()
    {
        var oldProvider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var service = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var s = ConditioningControlPanel.CoreSettings.Current;
        var old = (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted);
        var oldBuild = FlashVoicePool.Build;
        var oldPlay = QuestsTabView.PlayQuestVoice;
        var clip = Path.Combine(Path.GetTempPath(), "k2-quest-voice-" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(clip, new byte[] { 0 });
        try
        {
            var played = new List<(string Path, float Volume)>();
            QuestsTabView.PlayQuestVoice = (path, volume) => played.Add((path, volume));
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = (100, false, false);

            // No recorded clip: silent, never a synthetic stand-in.
            FlashVoicePool.Build = _ => new List<string>();
            FlashVoicePool.Reset();
            QuestsTabView.PlayQuestCompleteVoice();
            Assert.Empty(played);

            // A clip that is not on disk: silent.
            FlashVoicePool.Build = _ => new List<string> { clip + ".gone" };
            FlashVoicePool.Reset();
            QuestsTabView.PlayQuestCompleteVoice();
            Assert.Empty(played);

            // A recorded clip plays at the flash curve.
            FlashVoicePool.Build = _ => new List<string> { clip };
            FlashVoicePool.Reset();
            QuestsTabView.PlayQuestCompleteVoice();
            var one = Assert.Single(played);
            Assert.Equal(clip, one.Path);
            Assert.Equal(FlashVoicePool.Volume(100), one.Volume, 3);

            // Muted voice lines and master mute: silent.
            s.CompanionVoiceLinesMuted = true;
            QuestsTabView.PlayQuestCompleteVoice();
            s.CompanionVoiceLinesMuted = false;
            s.MasterVolume = 0;
            QuestsTabView.PlayQuestCompleteVoice();
            Assert.Single(played);
        }
        finally
        {
            (s.MasterVolume, s.AvatarMuted, s.CompanionVoiceLinesMuted) = old;
            FlashVoicePool.Build = oldBuild;
            FlashVoicePool.Reset();
            QuestsTabView.PlayQuestVoice = oldPlay;
            service.SaveImmediate();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldProvider;
            try { File.Delete(clip); } catch { }
        }
    }
}
