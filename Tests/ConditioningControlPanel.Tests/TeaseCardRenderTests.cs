using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Features;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The dashboard's nameless TEASE tile and its teaser card (owner, 2026-08-13).
///
/// <para><b>What can rot here, silently.</b> The tease is a costume made of four separate things -
/// a blur, a veil, a livery rim and a badge - and every one of them survives a clean compile if it
/// stops being applied. The specific failure this suite exists to catch is the tease leaking the
/// feature's identity: a blur that stops being set leaves the app shipping a legible picture of an
/// unannounced feature on the front page, and nothing else in the build would say so.</para>
///
/// <para>The livery assertions check the tier is a real dial rather than a hardcoded diamond,
/// because "swapping the teased feature is one line" is a promise this suite is the only thing
/// keeping honest.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class TeaseCardRenderTests
{
    private static void OnStaThread(Action body) => WpfRenderHarness.OnStaThread(body);

    /// <summary>Every string the loc keys must never contain, in any of the nine languages.</summary>
    private static readonly string[] ForbiddenNames = { "just drop", "justdrop", "just-drop" };

    private static readonly string[] TeaseKeys =
    {
        "tease_card_tooltip", "tease_popup_title", "tease_popup_body", "tease_popup_dismiss",
    };

    private static void Realize(FrameworkElement element, double width = 240, double height = 180)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    // =====================================================================================
    //  the tile
    // =====================================================================================

    [Fact]
    public void ATeasedCardBlursItsArtAndVeilsIt()
    {
        OnStaThread(() =>
        {
            var card = new FeatureCard { Title = "???", TeaseTier = 2 };
            Realize(card);

            // The blur is the whole promise: without it the card is a legible picture of an
            // unannounced feature, sitting on the front page.
            var blur = Assert.IsType<BlurEffect>(card.ImgIconHost.Effect);
            Assert.True(blur.Radius >= 20, $"tease blur is only {blur.Radius}px - the art would still read");
            Assert.IsType<BlurEffect>(card.GlyphHost.Effect);

            Assert.Equal(Visibility.Visible, card.TeaseHost.Visibility);
            Assert.Equal("?", card.TxtTeaseGlyph.Text);
        });
    }

    [Fact]
    public void TheTeaseTierPicksTheLiveryRatherThanHardcodingDiamond()
    {
        OnStaThread(() =>
        {
            var gold = new FeatureCard { TeaseTier = 1 };
            var diamond = new FeatureCard { TeaseTier = 2 };
            Realize(gold);
            Realize(diamond);

            // Reference equality with the shared theme brushes, not a colour comparison: the whole
            // point of the tier livery is that these tiles wear the SAME metal the Studio rack and
            // the Play wall wear, and a hand-mixed lookalike would pass a colour check.
            Assert.Same(Application.Current!.TryFindResource("Tier1GoldBorderBrush"), gold.RootBorder.BorderBrush);
            Assert.Same(Application.Current!.TryFindResource("Tier2DiamondBorderBrush"), diamond.RootBorder.BorderBrush);
            Assert.True(diamond.RootBorder.BorderThickness.Left > 1, "the livery rim must be heavier than the glass one");
        });
    }

    [Fact]
    public void TheTeaseBadgeIsWornInTheLiveryAndNotInPink()
    {
        OnStaThread(() =>
        {
            var card = new FeatureCard { TeaseTier = 2 };
            Realize(card);
            // Badge written AFTER the tease, which is the order MainWindow.ApplyTeaseCard uses and
            // the order that would silently repaint it pink if OnTierBadgeChanged forgot to
            // re-apply the costume.
            card.TierBadge = "◆";

            Assert.Equal(Visibility.Visible, card.TierBadgeHost.Visibility);
            Assert.Same(Application.Current!.TryFindResource("Tier2DiamondBorderBrush"), card.TierBadgeHost.BorderBrush);
            Assert.Same(Application.Current!.TryFindResource("Tier2DiamondBorderBrush"), card.TxtTierBadge.Foreground);
        });
    }

    [Fact]
    public void ClearingTheTeaseGivesTheCardBackExactlyWhatItShippedAs()
    {
        OnStaThread(() =>
        {
            var card = new FeatureCard { TeaseTier = 2 };
            Realize(card);
            card.TeaseTier = 0;

            Assert.Null(card.ImgIconHost.Effect);
            Assert.Null(card.GlyphHost.Effect);
            Assert.Equal(Visibility.Collapsed, card.TeaseHost.Visibility);
            Assert.Equal(new Thickness(1), card.RootBorder.BorderThickness);
            // Back on the theme, not frozen at whatever the theme was when the tease came off -
            // this is what SetResourceReference buys and a ClearValue would not.
            Assert.Same(Application.Current!.TryFindResource("GlassBorderBrush"), card.RootBorder.BorderBrush);
        });
    }

    [Fact]
    public void AnUnteasedCardIsUntouchedByAnyOfThis()
    {
        OnStaThread(() =>
        {
            var card = new FeatureCard { Title = "Flash Images" };
            Realize(card);

            Assert.Null(card.ImgIconHost.Effect);
            Assert.Equal(Visibility.Collapsed, card.TeaseHost.Visibility);
            Assert.Equal(new Thickness(1), card.RootBorder.BorderThickness);
        });
    }

    // =====================================================================================
    //  the teaser card
    // =====================================================================================

    [Fact]
    public void TheTeaserPopupBuildsInBothLiveries()
    {
        OnStaThread(() =>
        {
            foreach (var tier in new[] { 1, 2 })
            {
                // Constructing it is the assertion: a chromeless AllowsTransparency window with a
                // templated button and a keyframed shimmer is exactly the shape that compiles and
                // then throws on its first BAML load.
                var popup = new TeaseRevealPopup(tier);
                try
                {
                    var key = tier >= 2 ? "Tier2DiamondBorderBrush" : "Tier1GoldBorderBrush";
                    Assert.Same(Application.Current!.TryFindResource(key), popup.CardBorder.BorderBrush);
                    Assert.Same(Application.Current!.TryFindResource(key), popup.TxtGlyph.Foreground);
                    Assert.Same(Application.Current!.TryFindResource(key), popup.BtnDismiss.Background);

                    // The shimmer is armed from Loaded, and the card is never shown here - so its
                    // resting state is what a reduced-motion or Performance-tier user sees, and it
                    // must be invisible rather than a static white bar across the copy.
                    Assert.Equal(Visibility.Collapsed, popup.ShimmerHost.Visibility);
                }
                finally { CloseQuietly(popup); }
            }
        });
    }

    /// <summary>A window that was built but never shown has nothing to tear down; closing it is
    /// tidiness, not a contract, so it must never be the reason a test fails.</summary>
    private static void CloseQuietly(Window window)
    {
        try { window.Close(); } catch { /* never shown */ }
    }

    [Fact]
    public void NothingTheTeaserSaysNamesTheFeature()
    {
        OnStaThread(() =>
        {
            var popup = new TeaseRevealPopup(2);
            try
            {
                var said = new[]
                {
                    popup.Title ?? "", popup.TxtGlyph.Text ?? "", popup.TxtTitle.Text ?? "",
                    popup.TxtBody.Text ?? "", popup.BtnDismiss.Content as string ?? "",
                };

                foreach (var line in said)
                foreach (var name in ForbiddenNames)
                    Assert.False(line.Contains(name, StringComparison.OrdinalIgnoreCase),
                        $"the teaser said \"{line}\", which names the feature");
            }
            finally { CloseQuietly(popup); }
        });
    }

    // =====================================================================================
    //  the copy
    // =====================================================================================

    [Fact]
    public void TheTeaseCopyReachedAllNineLanguages()
    {
        var missing = new List<string>();
        foreach (var lang in CompanionLocMasters.Languages)
        {
            // For() parses with System.Text.Json - strict, so a literal newline inside any value
            // in any of the nine files fails right here rather than at the user's first launch.
            var file = CompanionLocMasters.For(lang);
            foreach (var key in TeaseKeys)
            {
                if (!file.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                    missing.Add($"{lang}.json:{key}");
            }
        }

        Assert.True(missing.Count == 0, "tease copy missing from: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoTranslationOfTheTeaseLeaksTheFeaturesName()
    {
        // The one way nine language files can undo a tease: a translator (or a future editor
        // working from a name-bearing source string) writing the feature in plainly.
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var file = CompanionLocMasters.For(lang);
            foreach (var key in TeaseKeys)
            {
                if (!file.TryGetValue(key, out var value)) continue;
                foreach (var name in ForbiddenNames)
                    Assert.DoesNotContain(name, value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void TheTeaseBodyUsesEscapedBreaksAndNotRealOnes()
    {
        // The house rule (CLAUDE.md, Localization #11). The strict parse above already rejects a
        // raw newline in the FILE; this asserts the decoded value still has the two breaks the
        // three-line card was written for, so a translation cannot quietly collapse to one line.
        foreach (var lang in CompanionLocMasters.Languages)
        {
            var body = CompanionLocMasters.For(lang)["tease_popup_body"];
            Assert.Equal(2, body.Count(c => c == '\n'));
            Assert.DoesNotContain("\r", body);
        }
    }
}
