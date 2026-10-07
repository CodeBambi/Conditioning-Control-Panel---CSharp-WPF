using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Invites;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The inviter's reward ladder: which badges a converted count earns, and that every rung is
/// wired end to end (a visible badge with art and words in all nine languages, gating one
/// wardrobe item that has art of its own).
/// </summary>
public class InviteRewardsTests
{
    private static readonly string[] Languages = { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" };

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    public static IEnumerable<object[]> Rungs() => InviteRewards.Ladder.Select(r => new object[] { r.AchievementId });

    [Theory]
    [InlineData(0, new string[0])]
    [InlineData(1, new[] { "invite_first" })]
    [InlineData(2, new[] { "invite_first" })]
    [InlineData(5, new[] { "invite_first", "invite_hostess", "invite_pied_piper" })]
    [InlineData(40, new[] { "invite_first", "invite_hostess", "invite_pied_piper", "invite_recruiter_chief" })]
    public void Due_EarnsEveryRungAtOrBelowTheCount(int converted, string[] expected)
        => Assert.Equal(expected, InviteRewards.Due(converted, null));

    [Fact]
    public void Due_SkipsWhatIsAlreadyUnlocked()
        => Assert.Equal(new[] { "invite_hostess" },
            InviteRewards.Due(4, new HashSet<string> { "invite_first" }));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 3)]
    [InlineData(4, 5)]
    [InlineData(9, 10)]
    public void Next_IsTheFirstRungNotReached(int converted, int expected)
        => Assert.Equal(expected, InviteRewards.Next(converted)!.Converted);

    [Fact]
    public void Next_IsNullAtTheTop() => Assert.Null(InviteRewards.Next(10));

    [Fact]
    public void LadderClimbsStrictlyWithUniqueIds()
    {
        var ladder = InviteRewards.Ladder;
        for (var i = 1; i < ladder.Count; i++) Assert.True(ladder[i].Converted > ladder[i - 1].Converted);
        Assert.True(ladder[0].Converted >= 1, "a rung at zero would reward handing out codes");
        Assert.Equal(ladder.Count, ladder.Select(r => r.AchievementId).Distinct().Count());
        Assert.Equal(ladder.Count, ladder.Select(r => r.WardrobeItemId).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Rungs))]
    public void EveryRungIsAVisibleCommunityBadgeWithArt(string id)
    {
        var rung = InviteRewards.Ladder.Single(r => r.AchievementId == id);
        Assert.True(Achievement.All.TryGetValue(id, out var a), $"Missing achievement '{id}'.");
        Assert.Equal(AchievementCategory.Community, a!.Category);
        Assert.False(a.IsHidden);
        Assert.False(a.IsExclusive);
        // Only subscribers hand out codes: out of a free account's denominator until earned.
        Assert.True(a.IsPremiumFeature);
        Assert.Equal($"{id}.png", a.ImageName);
        Assert.Contains(rung.Converted.ToString(), a.Requirement);
        Assert.True(File.Exists(Path.Combine(SourceRoots.RepoRoot, "Assets", "achievements", a.ImageName)), $"no badge art for {id}");
    }

    [Theory]
    [MemberData(nameof(Rungs))]
    public void EveryRungGatesOneWardrobeItemWithArt(string id)
    {
        var rung = InviteRewards.Ladder.Single(r => r.AchievementId == id);
        var registry = JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.RepoRoot, "Assets", "cosmetics", "registry.json")));
        var item = ((JArray)registry["items"]!).OfType<JObject>().SingleOrDefault(i => i.Value<string>("id") == rung.WardrobeItemId);
        Assert.True(item != null, $"registry has no '{rung.WardrobeItemId}'");
        Assert.Equal("invites", item!.Value<string>("mod"));
        Assert.Equal($"achievement:{id}", item.Value<string>("unlock"));
        var file = item.Value<string>("file")!;
        Assert.True(File.Exists(Path.Combine(SourceRoots.RepoRoot, "Assets", "cosmetics", file)), $"no wardrobe art at {file}");
    }

    [Fact]
    public void EveryRungHasItsWordsInEveryLanguage()
    {
        foreach (var lang in Languages)
        {
            var json = JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json")));
            foreach (var rung in InviteRewards.Ladder)
                foreach (var part in new[] { "name", "req", "flavor" })
                {
                    var key = $"achievement_{rung.AchievementId}_{part}";
                    Assert.False(string.IsNullOrWhiteSpace(json.Value<string>(key)), $"{lang}.json is missing {key}");
                }
        }
    }

    [Fact]
    public void ApplyWithNothingConvertedIsANoOp() => Assert.Equal(0, InviteRewards.Apply(0));
}
