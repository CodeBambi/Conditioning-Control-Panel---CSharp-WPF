using System;
using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The dashboard billboard (owner ask 2026-09-12): the roster and the walk. Both are pure, so the
/// caps, the wrap and the pause are tested without a window; the card, the clock and the click are
/// MainWindow.DashboardBillboard.cs.
///
/// The roster tests are the ones that earn their keep on release day: a card whose poster is not
/// an embedded Resource renders as a black box, and a card whose strings are not localisation keys
/// ships English to nine languages. Neither failure is visible until somebody folds the browser.
/// </summary>
public class DashboardBillboardTests
{
    // Every folder a roster poster may live in is declared in ConditioningControlPanel.csproj as
    // <Resource>: Resources\features\*.png, Resources\nav\*.png, plus the two loose files.
    private static readonly string[] AllowedPosterRoots = { "features/", "nav/" };
    private static readonly string[] AllowedLoosePosters = { "discord.png", "logo.png" };

    [Fact]
    public void TheRosterIsFourToSixHouseCards()
    {
        Assert.InRange(DashboardBillboard.Roster.Count, 4, 6);
    }

    [Fact]
    public void EveryCardHasItsOwnId()
    {
        var ids = DashboardBillboard.Roster.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void EveryStringIsALocalisationKey()
    {
        // Keys, never sentences: a space or a capital means somebody typed the copy in here and it
        // will never be translated.
        foreach (var card in DashboardBillboard.Roster)
        {
            foreach (var key in new[] { card.EyebrowKey, card.TitleKey, card.LineKey })
            {
                Assert.StartsWith("billboard_", key, StringComparison.Ordinal);
                Assert.DoesNotContain(" ", key, StringComparison.Ordinal);
                Assert.Equal(key.ToLowerInvariant(), key);
            }
        }
    }

    [Fact]
    public void EveryPosterIsArtTheAppAlreadyShips()
    {
        foreach (var card in DashboardBillboard.Roster)
        {
            bool declared = AllowedPosterRoots.Any(r => card.Poster.StartsWith(r, StringComparison.Ordinal))
                            || AllowedLoosePosters.Contains(card.Poster, StringComparer.Ordinal);
            Assert.True(declared, card.Id + " points at " + card.Poster + ", which is not a declared <Resource>");
            Assert.EndsWith(".png", card.Poster, StringComparison.Ordinal);
            Assert.DoesNotContain("\\", card.Poster, StringComparison.Ordinal);
            Assert.DoesNotContain(" ", card.Poster, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void APosterResolvesToAPackUri()
    {
        var card = DashboardBillboard.Roster.First(c => c.Id == "loom");
        Assert.Equal("pack://application:,,,/Resources/features/loom.png", DashboardBillboard.PosterUri(card));
    }

    [Fact]
    public void EveryLinkIsAbsoluteHttps()
    {
        foreach (var card in DashboardBillboard.Roster.Where(c => c.Kind == BillboardTargetKind.Link))
        {
            Assert.True(Uri.TryCreate(card.Target, UriKind.Absolute, out var uri), card.Id);
            Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
        }
    }

    [Fact]
    public void EveryTabTargetIsALowerCaseShowTabKey()
    {
        foreach (var card in DashboardBillboard.Roster.Where(c => c.Kind == BillboardTargetKind.Tab))
        {
            Assert.Equal(card.Target.ToLowerInvariant(), card.Target);
            Assert.DoesNotContain("/", card.Target, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(card.Target));
        }
    }

    [Fact]
    public void TheRosterCarriesTheHouseCards()
    {
        var ids = DashboardBillboard.Roster.Select(c => c.Id).ToArray();
        Assert.Contains("webapp", ids);
        Assert.Contains("remix", ids);
        Assert.Contains("loom", ids);
        Assert.Contains("discord", ids);
        Assert.Contains("support", ids);
    }

    // ---- the walk ----

    [Fact]
    public void NextStepsForwardAndWrapsAtTheEnd()
    {
        Assert.Equal(1, DashboardBillboard.NextIndex(0, 3));
        Assert.Equal(2, DashboardBillboard.NextIndex(1, 3));
        Assert.Equal(0, DashboardBillboard.NextIndex(2, 3));
    }

    [Fact]
    public void AWalkOfTheWholeRosterComesBackToTheStart()
    {
        int count = DashboardBillboard.Roster.Count;
        int i = 0;
        for (int step = 0; step < count; step++) i = DashboardBillboard.NextIndex(i, count);
        Assert.Equal(0, i);
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(9, 3)]
    [InlineData(0, 0)]
    [InlineData(4, -2)]
    public void ADriftedIndexComesBackToTheFirstCard(int current, int count)
        => Assert.Equal(0, DashboardBillboard.NextIndex(current, count));

    [Theory]
    [InlineData(0, 3, 0)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 3, 0)]
    [InlineData(-1, 3, 0)]
    [InlineData(1, 0, 0)]
    public void JumpLandsOnTheDotOrOnTheFirstCard(int requested, int count, int expected)
        => Assert.Equal(expected, DashboardBillboard.JumpTo(requested, count));

    [Fact]
    public void CardAtNeverThrows()
    {
        Assert.Equal(DashboardBillboard.Roster[0], DashboardBillboard.CardAt(-4));
        Assert.Equal(DashboardBillboard.Roster[0], DashboardBillboard.CardAt(DashboardBillboard.Roster.Count));
        Assert.Equal(DashboardBillboard.Roster[1], DashboardBillboard.CardAt(1));
    }

    [Theory]
    [InlineData(false, true, true)]   // on screen, pointer away: the clock runs
    [InlineData(true, true, false)]   // the pointer on the card holds it
    [InlineData(false, false, false)] // another tab is up: nothing to advance
    [InlineData(true, false, false)]
    public void ThePointerAndAHiddenTabBothStopTheClock(bool pointerOver, bool onScreen, bool expected)
        => Assert.Equal(expected, DashboardBillboard.ShouldAdvance(pointerOver, onScreen));

    [Fact]
    public void TheCardHoldsLongEnoughToRead()
    {
        // An ambient loop by MotionFx's own definition, so it belongs in the 8-60s band.
        Assert.InRange(DashboardBillboard.RotateSeconds, 8, 60);
    }

    // ---- the copy ----

    [Fact]
    public void EveryCardStringReachedAllNineLanguages()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var card in DashboardBillboard.Roster)
            {
                foreach (var key in new[] { card.EyebrowKey, card.TitleKey, card.LineKey })
                {
                    Assert.True(file.ContainsKey(key), key + " is missing from " + lang + ".json");
                    Assert.False(string.IsNullOrWhiteSpace(file[key]), key + " is blank in " + lang + ".json");
                }
            }
        }
    }

    [Fact]
    public void TheCopyKeepsTheHouseVoice()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var card in DashboardBillboard.Roster)
            {
                foreach (var key in new[] { card.EyebrowKey, card.TitleKey, card.LineKey })
                {
                    var value = file[key];
                    Assert.DoesNotContain("—", value, StringComparison.Ordinal);
                    Assert.DoesNotContain("–", value, StringComparison.Ordinal);
                    Assert.DoesNotContain("!", value, StringComparison.Ordinal);
                    Assert.DoesNotContain("\n", value, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public void TheFoldTooltipsReachedAllNineLanguages()
    {
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            Assert.True(file.ContainsKey("tooltip_browser_fold"), lang);
            Assert.True(file.ContainsKey("tooltip_browser_unfold"), lang);
        }
    }
}
