using System.IO;
using ConditioningControlPanel.Models;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>"Use only Afterglow": off by default, and the ambient card tick is the one place it acts.</summary>
public class AfterglowOnlyTests
{
    [Fact]
    public void Use_only_afterglow_is_off_by_default_and_round_trips()
    {
        var s = new AppSettings();
        Assert.False(s.AfterglowOnly);
        s.AfterglowOnly = true;
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(s);
        var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AppSettings>(json)!;
        Assert.True(back.AfterglowOnly);
    }

    [Fact]
    public void The_ambient_timer_skips_the_card_only_when_the_setting_and_the_effect_are_on()
    {
        var src = File.ReadAllText(Path.Combine(FindRoot(), "ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs"));
        var tick = src.Substring(src.IndexOf("private void Timer_Tick", System.StringComparison.Ordinal));
        tick = tick.Substring(0, tick.IndexOf("public void FlashSubliminal", System.StringComparison.Ordinal));
        Assert.Contains("AfterglowOnly", tick);
        Assert.Contains("SuperAccess.IsOn(Super.SuperEffect.Afterglow)", tick);
        Assert.Contains("ScheduleNext();", tick);   // the scheduler keeps ticking either way
        // A one-shot (remote) card is not the ambient scheduler and is never suppressed.
        var flash = src.Substring(src.IndexOf("public void FlashSubliminal", System.StringComparison.Ordinal), 600);
        Assert.DoesNotContain("AfterglowOnly", flash);
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
