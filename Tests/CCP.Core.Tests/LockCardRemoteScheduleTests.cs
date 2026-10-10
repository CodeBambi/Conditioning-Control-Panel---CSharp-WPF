using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>SAFETY (hunt3 IC1): a lock card schedule a remote controller switched on is the
/// controller's, so its cards are never strict; a schedule the player already had running stays the
/// player's; a stop hands it back.</summary>
[Collection(SessionStatics.Name)]   // the scheduler singleton and CoreSettings.Current
public sealed class LockCardRemoteScheduleTests
{
    [Fact]
    public void ARemoteStartMarksTheSchedule_UntilTheNextStop()
    {
        var s = CoreSettings.Current;
        var (oldEnabled, sched) = (s.LockCardEnabled, LockCardScheduler.Instance);
        sched.Stop();
        try
        {
            s.LockCardEnabled = true;
            sched.StartFromRemote();
            Assert.True(sched.IsRunning);
            Assert.True(sched.StartedByRemote);
            sched.Start();                                  // the player's engine starting over it changes nothing
            Assert.True(sched.StartedByRemote);
            sched.Stop();
            Assert.False(sched.StartedByRemote);

            sched.Start();                                  // the player's own schedule
            sched.StartFromRemote();                        // a controller asking for what already runs
            Assert.False(sched.StartedByRemote);
            sched.Stop();

            s.LockCardEnabled = false;
            sched.StartFromRemote();                        // refused by the setting: nothing started, nothing marked
            Assert.False(sched.IsRunning);
            Assert.False(sched.StartedByRemote);
        }
        finally
        {
            sched.Stop();
            s.LockCardEnabled = oldEnabled;
        }
    }

    /// <summary>The remote verbs never start the schedule the plain way.</summary>
    [Fact]
    public void TheRemoteVerbsOnlyStartTheScheduleAsRemote()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "RemoteControl", "RemoteCommands.cs"));
        Assert.DoesNotMatch(new Regex(@"LockCardScheduler\s*\.\s*Instance\s*\.\s*Start\s*\("), text);
        Assert.Contains("LockCardScheduler.Instance.StartFromRemote()", text);
        Assert.DoesNotContain("CoreLockCard.Show(", text);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
