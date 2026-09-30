using System.Collections.Generic;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>MantraService moved from the WPF head: XP, quest and Chaster calls go through the Core seams
/// with WPF's amounts (30 + min(streak*5, 50), App.Quests.TrackMantraCompleted, Chaster "mantra" x reps).</summary>
[Collection(SessionStatics.Name)] // CoreProgression is process-global
public sealed class MantraServiceTests
{
    [Fact]
    public void RepsPayXpTrackQuestsAndABrokenStreakNotesChaster()
    {
        var (xp0, quest0, chaster0) = (CoreProgression.AddXPProvider, CoreProgression.TrackMantraCompletedProvider, MantraService.ChasterNote);
        var xp = new List<(double, string)>();
        var quests = 0;
        var notes = new List<int>();
        CoreProgression.AddXPProvider = (a, s) => xp.Add((a, s));
        CoreProgression.TrackMantraCompletedProvider = () => quests++;
        MantraService.ChasterNote = notes.Add;
        try
        {
            var svc = new MantraService();
            int? done = null;
            svc.SessionComplete += (reps, _) => done = reps;
            svc.StartSession(2);
            Assert.Contains(svc.CurrentMantra, CoreSettings.Current.MantraPool);
            Assert.False(svc.TryCompleteMantra());   // anti-cheat: under 1.5 s
            Thread.Sleep(1600);
            Assert.True(svc.TryCompleteMantra());
            svc.BreakStreak();
            Thread.Sleep(1600);
            Assert.True(svc.TryCompleteMantra());

            Assert.Equal(new[] { (35.0, "Mantra"), (35.0, "Mantra") }, xp);
            Assert.Equal(2, quests);
            Assert.Equal(new[] { 1 }, notes);
            Assert.Equal(2, done);
            Assert.False(svc.IsActive);

            Assert.True(svc.CreditExternalMantra());   // voice path: base XP only
            Assert.Equal((30.0, "Mantra"), xp[^1]);
            Assert.Equal(3, quests);
        }
        finally
        {
            (CoreProgression.AddXPProvider, CoreProgression.TrackMantraCompletedProvider, MantraService.ChasterNote) = (xp0, quest0, chaster0);
        }
    }
}
