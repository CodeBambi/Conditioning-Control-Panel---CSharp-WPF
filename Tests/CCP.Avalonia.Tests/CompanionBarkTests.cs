using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Wave B lane companion: ai#1 (bark engine), ai#5 (companion switch), progression#47 (companion XP).
/// Same numbers as WPF 7.1.5 BarkService / CompanionService.
/// </summary>
public sealed class CompanionBarkTests
{
    private static Task WithSettings(Action body)
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try { body(); return Task.CompletedTask; }
        finally { service.SealForReset(); CoreSettings.ServiceProvider = null; }
    }

    private static BarkRule Rule(string id, string trigger, string text, bool repeatable = true, string cls = "normal",
        int priority = 0, Dictionary<string, object>? cond = null) => new()
    {
        Id = id, Trigger = trigger, Repeatable = repeatable, ClassRaw = cls, Priority = priority, Conditions = cond,
        VariantPool = new List<BarkVariant> { new(text) },
    };

    private static (BarkEngine Engine, List<BarkSpeech> Said) Engine(params BarkRule[] rules)
    {
        var said = new List<BarkSpeech>();
        var e = new BarkEngine(new Random(1)) { LoadRules = () => new BarkRuleSet(rules), ResolveAudio = _ => null };
        e.Speak = s => said.Add(s);
        e.Start();
        return (e, said);
    }

    [Fact]
    public void ShippedBaseManifestLoads()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "companion_audio", "bark_rules.json");
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets", "sounds", "companion_audio", "bark_rules.json"));
        Assert.True(File.Exists(path), path);
        var mod = Path.Combine(Path.GetDirectoryName(path)!, "mods", "builtin-ccp-default", "bark_rules.json");
        var set = BarkRuleManifest.Load(basePath: path, modPath: mod);
        Assert.True(set.Count > 200, $"only {set.Count} rules");   // base (Chaos) + the CCP Default overlay (154 Idle)
        Assert.NotEmpty(set.ForTrigger("Idle"));
        Assert.NotEmpty(set.ForTrigger("AppOpened"));
    }

    [Fact]
    public Task RaiseSpeaksThenTheSixtySecondFloorHolds() => AvaloniaTestDispatcher.RunAsync(() => WithSettings(() =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        var (e, said) = Engine(Rule("a", "FlashDisplayed", "hi {who}"), Rule("b", "VideoStarted", "video"));
        Assert.True(e.Raise("FlashDisplayed", c => c.Set("who", "you")));
        Assert.Equal("hi you", said[0].Text);
        Assert.False(said[0].Priority);
        Assert.False(e.Raise("VideoStarted"));   // global 60 s min-gap
        Assert.Single(said);
    }));

    [Fact]
    public Task CompanionOffSilencesAllButSafety() => AvaloniaTestDispatcher.RunAsync(() => WithSettings(() =>
    {
        CoreSettings.Current.AvatarEnabled = false;
        var (e, said) = Engine(Rule("a", "FlashDisplayed", "x"), Rule("p", "Panic", "breathe", cls: "safety"));
        Assert.False(e.Raise("FlashDisplayed"));
        Assert.True(e.Raise("Panic"));
        Assert.True(said[0].Priority);
    }));

    [Fact]
    public Task ConditionsPickTheHighestPriorityMatch() => AvaloniaTestDispatcher.RunAsync(() => WithSettings(() =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        var (e, said) = Engine(
            Rule("big", "LevelUp", "big", priority: 5, cond: new() { ["level_gte"] = 10L }),
            Rule("small", "LevelUp", "small", priority: 1));
        e.Raise("LevelUp", c => c.Set("level", 12));
        Assert.Equal("big", said[0].Text);
    }));

    [Fact]
    public Task IdleRotatesWithoutRepeatAndPersists() => AvaloniaTestDispatcher.RunAsync(() => WithSettings(() =>
    {
        var s = CoreSettings.Current;
        s.AvatarEnabled = true;
        s.MasterVolume = 50;
        var (e, said) = Engine(Rule("i1", "Idle", "one"), Rule("i2", "Idle", "two"));
        Assert.True(e.DispatchIdle());
        Assert.Contains(said[0].Text == "one" ? "i1" : "i2", s.BarkIdleRotation);
    }));

    [Fact]
    public Task NoMouthNeverSpendsAOneShot() => AvaloniaTestDispatcher.RunAsync(() => WithSettings(() =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        var (e, said) = Engine(Rule("once", "AppOpened", "welcome", repeatable: false));
        e.Speak = null;
        Assert.False(e.Raise("AppOpened"));
        e.Speak = sp => said.Add(sp);
        Assert.True(e.Raise("AppOpened"));
    }));

    [Fact]
    public void AwayBucketsMatchWpf()
    {
        var now = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("first", CoreBark.GreetingAwayBucket(null, now));
        Assert.Equal("soon", CoreBark.GreetingAwayBucket(now.AddHours(-5), now));
        Assert.Equal("back", CoreBark.GreetingAwayBucket(now.AddHours(-17), now));
        Assert.Equal("while", CoreBark.GreetingAwayBucket(now.AddDays(-2), now));
        Assert.Equal("long", CoreBark.GreetingAwayBucket(now.AddDays(-4), now));
    }

    [Fact]
    public void XpModifiersMatchWpf()
    {
        Assert.Equal(1.4, CompanionCore.CalculateXPModifier(CompanionBonusType.PinkFilterBonus, false, false, false, false, 40), 6);
        Assert.Equal(1.5, CompanionCore.CalculateXPModifier(CompanionBonusType.AutonomyBonus, true, false, false, false, 0));
        Assert.Equal(0.5, CompanionCore.CalculateXPModifier(CompanionBonusType.StrictModeBonus, false, false, false, false, 0));
        Assert.Equal(2.0, CompanionCore.CalculateXPModifier(CompanionBonusType.StrictModeBonus, false, true, true, true, 0));
        Assert.Equal(1.0, CompanionCore.CalculateXPModifier(CompanionBonusType.XPDrain, true, true, true, true, 50));
    }

    [Fact]
    public Task CompanionXpLevelsUpAndSwitchKeepsProgress() => AvaloniaTestDispatcher.RunAsync(() => WithSettings(() =>
    {
        var s = CoreSettings.Current;
        s.ActiveCompanionId = (int)CompanionId.OGBambiSprite;
        var levels = new List<int>();
        Action<CompanionId, int> onLevel = (_, l) => levels.Add(l);
        CompanionCore.LevelUp += onLevel;
        CompanionId? switched = null;
        Action<CompanionId> onSwitch = id => switched = id;
        CompanionCore.Switched += onSwitch;
        try
        {
            var need = CompanionCore.ActiveProgress.XPForNextLevel;
            var startLevel = CompanionCore.ActiveProgress.Level;
            CompanionCore.AddCompanionXP(need / CompanionCore.CurrentModifier() + 1, "Flash");
            Assert.Equal(startLevel + 1, CompanionCore.ActiveProgress.Level);
            Assert.Equal(new[] { startLevel + 1 }, levels);

            var other = CompanionId.CultBunny;
            Assert.True(CompanionCore.SwitchCompanion(other));
            Assert.Equal((int)other, s.ActiveCompanionId);
            Assert.Equal(other, switched);
            Assert.Equal(startLevel + 1, CompanionCore.GetProgress(CompanionId.OGBambiSprite).Level);
        }
        finally { CompanionCore.LevelUp -= onLevel; CompanionCore.Switched -= onSwitch; }
    }));

    [Fact]
    public Task HeadSeedsTheDoorbellAndThePhraseManagerRows() => AvaloniaTestDispatcher.RunAsync(() => WithSettings(() =>
    {
        CoreSettings.Current.AvatarEnabled = true;
        var (e, _) = Engine(Rule("ui", "UiAction", "opened {action}"));
        try
        {
            BarkHead.Seed(e);
            var said = new List<BarkSpeech>();
            e.Speak = sp => said.Add(sp);
            CoreBark.NotifyUiAction("open_assets");
            Assert.Equal("opened open_assets", said[0].Text);
            Assert.Contains(CoreBark.AllLines, l => l.RuleId == "ui");
            Assert.NotNull(CoreModsHooks.ReloadBarkRules);
        }
        finally { BarkHead.Reset(); }
    }));
}
