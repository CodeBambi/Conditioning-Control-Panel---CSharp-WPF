using System.IO;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Afterglow "Super only": the ambient card tick is the one place the classic cards step aside.</summary>
public class AfterglowOnlyTests
{
    [Fact]
    public void Legacy_use_only_afterglow_setting_still_round_trips()
    {
        // Kept so old settings files load; nothing reads it any more (the Super only pick replaced it).
        var s = new AppSettings();
        Assert.False(s.AfterglowOnly);
        s.AfterglowOnly = true;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(s);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.True(back.AfterglowOnly);
    }

    [Fact]
    public void The_ambient_timer_skips_the_card_only_when_afterglow_replaces_the_base()
    {
        var src = File.ReadAllText(Path.Combine(FindRoot(), "ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs"));
        var tick = src.Substring(src.IndexOf("private void Timer_Tick", System.StringComparison.Ordinal));
        tick = tick.Substring(0, tick.IndexOf("public void FlashSubliminal", System.StringComparison.Ordinal));
        Assert.Contains("SuperAccess.ReplacesBase(Super.SuperEffect.Afterglow)", tick);
        Assert.DoesNotContain("AfterglowOnly", tick);
        Assert.Contains("ScheduleNext();", tick);   // the scheduler keeps ticking either way
        // A one-shot (remote) card is not the ambient scheduler and is never suppressed.
        var flash = src.Substring(src.IndexOf("public void FlashSubliminal", System.StringComparison.Ordinal), 600);
        Assert.DoesNotContain("AfterglowOnly", flash);
        Assert.DoesNotContain("ReplacesBase", flash);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
