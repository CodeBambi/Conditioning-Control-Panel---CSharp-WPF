using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Controls;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The wardrobe's coordinate maths, and the one WPF fact it depends on.
///
/// <para>Both profile-card placement bugs came from the same place: the Wardrobe editor drew its
/// preview on a surface that was not the card. The decoration was clipped on the card but not in
/// the editor, and the charm/banner geometry was computed against a 640x240 mock of a strip that is
/// really ~1394x249. These tests pin both halves - the layout-clip fix on the card, and the pure
/// fraction-to-pixel maths both surfaces now share.</para>
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class WardrobeGeometryTests
{
    private static void OnStaThread(Action body)
    {
        Exception? escaped = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { escaped = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA render thread did not finish in time");
        if (escaped != null) throw new Xunit.Sdk.XunitException(escaped.ToString());
    }

    // ===================== the card: no layout clip on the decoration =====================

    /// <summary>
    /// The regression this whole branch exists for. A child whose unclipped desired size exceeds
    /// its arrange slot gets a layout clip from FrameworkElement.ArrangeCore - so the 148px
    /// decoration canvas inside the 104px avatar Grid was silently cut to Rect(22.3, 22.3, 104,
    /// 104), throwing away the outer 30% of every decoration (headband arcs, scarf fringes) on the
    /// real card while the editor's Canvas-hosted preview showed it whole.
    /// </summary>
    [Theory]
    [InlineData(104d)]
    [InlineData(28d)]     // the leaderboard/friends-rail size this control is built to drop into
    public void Decoration_IsNeverLayoutClipped(double avatarSize)
    {
        OnStaThread(() =>
        {
            var avatar = new AdornedAvatar { AvatarSize = avatarSize, DecorationId = "bambi_trance_headset" };
            avatar.Measure(new Size(avatarSize, avatarSize));
            avatar.Arrange(new Rect(0, 0, avatarSize, avatarSize));
            avatar.UpdateLayout();

            var deco = (FrameworkElement)avatar.FindName("DecoLayer")!;
            var expected = avatarSize / WardrobeCatalog.AvatarCircleRatio;

            Assert.Equal(expected, deco.RenderSize.Width, 3);
            Assert.Equal(expected, deco.RenderSize.Height, 3);

            // Nothing between the art and the control may clip either - a clip on the Root Grid or
            // on the UserControl would crop the decoration just as effectively.
            for (DependencyObject? node = deco; node != null; node = VisualTreeHelper.GetParent(node))
            {
                Assert.Null(VisualTreeHelper.GetClip((Visual)node));
                if (ReferenceEquals(node, avatar)) break;
            }

            // ...and the avatar still measures as its own diameter, so equipping a decoration
            // cannot push the hero's identity column sideways.
            Assert.Equal(avatarSize, avatar.DesiredSize.Width, 3);
            Assert.Equal(avatarSize, avatar.DesiredSize.Height, 3);
        });
    }

    /// <summary>The decoration stays centred on the avatar after the negative-margin fix.</summary>
    [Fact]
    public void Decoration_StaysCentredOnTheAvatar()
    {
        OnStaThread(() =>
        {
            var avatar = new AdornedAvatar { AvatarSize = 104, DecorationId = "bambi_trance_headset" };
            avatar.Measure(new Size(104, 104));
            avatar.Arrange(new Rect(0, 0, 104, 104));
            avatar.UpdateLayout();

            var deco = (FrameworkElement)avatar.FindName("DecoLayer")!;
            var offset = deco.TransformToAncestor(avatar).Transform(new Point(0, 0));
            var bleed = (104 / WardrobeCatalog.AvatarCircleRatio - 104) / 2;

            Assert.Equal(-bleed, offset.X, 3);
            Assert.Equal(-bleed, offset.Y, 3);
        });
    }

    // ===================== the stage: a to-scale mock of the live card =====================

    [Fact]
    public void Stage_KeepsTheCardsAspectRatio()
    {
        // The measured hero on a 1440-wide window, against the old fixed 640x240 mock.
        var stage = WardrobeStageGeometry.ForCard(1394, 249, 660, 250);

        Assert.Equal(1394d / 249d, stage.Width / stage.Height, 4);
        Assert.True(stage.Width <= 660.0001 && stage.Height <= 250.0001,
            $"stage {stage.Width}x{stage.Height} escaped its box");
        Assert.Equal(660d, stage.Width, 3);   // width-bound at this aspect
    }

    [Fact]
    public void Stage_ScalesTheAvatarWithTheCard()
    {
        var stage = WardrobeStageGeometry.ForCard(1394, 249, 660, 250);

        Assert.Equal(660d / 1394d, stage.Scale, 6);
        Assert.Equal(WardrobeStageGeometry.CardAvatarSize * stage.Scale, stage.AvatarSize, 6);
        Assert.Equal(WardrobeStageGeometry.CardAvatarLeft * stage.Scale, stage.AvatarLeft, 6);
        Assert.Equal(WardrobeStageGeometry.CardAvatarTop * stage.Scale, stage.AvatarTop, 6);

        // The avatar occupies the same share of the stage as it does of the card.
        Assert.Equal(WardrobeStageGeometry.CardAvatarSize / 1394d, stage.AvatarSize / stage.Width, 6);
        Assert.Equal(WardrobeStageGeometry.CardAvatarSize / 249d, stage.AvatarSize / stage.Height, 6);
    }

    [Fact]
    public void Stage_IsHeightBoundOnATallCard()
    {
        var stage = WardrobeStageGeometry.ForCard(600, 400, 660, 250);

        Assert.Equal(250d, stage.Height, 3);
        Assert.Equal(375d, stage.Width, 3);
        Assert.Equal(600d / 400d, stage.Width / stage.Height, 4);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 200)]
    [InlineData(double.NaN, 200)]
    [InlineData(double.PositiveInfinity, 200)]
    [InlineData(1000, 0)]
    public void Stage_FallsBackWhenTheCardHasNotBeenMeasured(double width, double height)
    {
        var stage = WardrobeStageGeometry.ForCard(width, height, 660, 250);

        Assert.True(stage.Width > 0 && stage.Height > 0);
        Assert.Equal(WardrobeStageGeometry.FallbackCardWidth / WardrobeStageGeometry.FallbackCardHeight,
                     stage.Width / stage.Height, 4);
    }

    /// <summary>
    /// End to end: the editor really does size its stage from the card it was handed, rather than
    /// from the 640x240 constant it used to carry.
    /// </summary>
    [Fact]
    public void Editor_SizesItsStageFromTheLiveCard()
    {
        OnStaThread(() =>
        {
            var draft = new ConditioningControlPanel.Models.ProfileCosmetics();
            var editor = new ConditioningControlPanel.WardrobeEditorDialog(draft, null, 1394, 249);
            var frame = (FrameworkElement)editor.FindName("StageFrame")!;

            Assert.Equal(1394d / 249d, frame.Width / frame.Height, 3);
            Assert.NotEqual(240d, frame.Height);
            editor.Close();
        });
    }

    // ===================== charm fractions =====================

    /// <summary>
    /// The point of the whole exercise: a charm placed on the stage lands on the SAME relative spot
    /// on the card, at the same relative size - which only holds because the stage now shares the
    /// card's aspect ratio.
    /// </summary>
    [Theory]
    [InlineData(0.6387, 0.6633, 2.1)]     // the arrangement from the bug report
    [InlineData(0.90, 0.76, 0.8)]         // slot-1 default anchor
    [InlineData(0.0, 0.0, 0.3)]
    [InlineData(1.0, 1.0, 3.0)]
    public void Charm_LandsOnTheSameRelativeSpotOnStageAndCard(double x, double y, double scale)
    {
        const double cardW = 1394, cardH = 249;
        var stage = WardrobeStageGeometry.ForCard(cardW, cardH, 660, 250);

        var onCard = WardrobeStageGeometry.CharmRect(cardW, cardH, x, y, scale);
        var onStage = WardrobeStageGeometry.CharmRect(stage.Width, stage.Height, x, y, scale);

        Assert.Equal(onCard.Left / cardW, onStage.Left / stage.Width, 6);
        Assert.Equal(onCard.Top / cardH, onStage.Top / stage.Height, 6);
        Assert.Equal(onCard.Size / cardH, onStage.Size / stage.Height, 6);
        Assert.Equal(onCard.Size / cardW, onStage.Size / stage.Width, 6);
        Assert.Equal(stage.Scale, onStage.Size / onCard.Size, 6);
    }

    /// <summary>
    /// The old 640x240 mock, kept as a witness: at the same fractions the charm sat 27.5% of the
    /// way across the preview's width and 13.1% across the card - a 2.1x error in apparent size,
    /// and ~480 DIP of horizontal drift. This is the bug, expressed as a number.
    /// </summary>
    [Fact]
    public void Charm_TheOldFixedMockDisagreedWithTheCard()
    {
        const double cardW = 1394, cardH = 249;
        var old = WardrobeStageGeometry.CharmRect(640, 240, 0.6387, 0.6633, 2.1);
        var card = WardrobeStageGeometry.CharmRect(cardW, cardH, 0.6387, 0.6633, 2.1);

        var oldWidthShare = old.Size / 640d;
        var cardWidthShare = card.Size / cardW;
        Assert.True(oldWidthShare / cardWidthShare > 2.0,
            $"expected the old mock to over-state charm size; got {oldWidthShare:F3} vs {cardWidthShare:F3}");
    }

    [Fact]
    public void Charm_IsCentredOnItsFraction()
    {
        var (left, top, size) = WardrobeStageGeometry.CharmRect(1000, 200, 0.5, 0.5, 1.0);

        Assert.Equal(500d, left + size / 2, 6);
        Assert.Equal(100d, top + size / 2, 6);
        Assert.Equal(WardrobeCatalog.CharmBaseHeightFraction * 200d, size, 6);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    public void Charm_SurvivesAnUnmeasuredSurface(double bad)
    {
        var (left, top, size) = WardrobeStageGeometry.CharmRect(bad, bad, 0.5, 0.5, 1.0);

        Assert.True(size > 0);
        Assert.False(double.IsNaN(left) || double.IsNaN(top));
    }

    // ===================== decoration canvas =====================

    [Fact]
    public void DecorationRect_CentresTheCanvasOnTheAvatar()
    {
        var (left, top, canvas) = WardrobeStageGeometry.DecorationRect(104, 24, 22);

        Assert.Equal(104 / WardrobeCatalog.AvatarCircleRatio, canvas, 6);
        Assert.Equal(24 + 52, left + canvas / 2, 6);
        Assert.Equal(22 + 52, top + canvas / 2, 6);
    }

    /// <summary>
    /// The stage's decoration is the card's decoration times the stage scale - the same composition,
    /// smaller. (This is what makes the deco preview trustworthy once the card stopped clipping it.)
    /// </summary>
    [Fact]
    public void DecorationRect_IsTheCardsCanvasAtStageScale()
    {
        var stage = WardrobeStageGeometry.ForCard(1394, 249, 660, 250);

        var card = WardrobeStageGeometry.DecorationRect(
            WardrobeStageGeometry.CardAvatarSize,
            WardrobeStageGeometry.CardAvatarLeft,
            WardrobeStageGeometry.CardAvatarTop);
        var mock = WardrobeStageGeometry.DecorationRect(stage.AvatarSize, stage.AvatarLeft, stage.AvatarTop);

        Assert.Equal(card.Canvas * stage.Scale, mock.Canvas, 6);
        Assert.Equal(card.Left * stage.Scale, mock.Left, 6);
        Assert.Equal(card.Top * stage.Scale, mock.Top, 6);
    }

    [Fact]
    public void DecorationRect_SurvivesAnUnmeasuredAvatar()
    {
        var (left, top, canvas) = WardrobeStageGeometry.DecorationRect(double.NaN, 0, 0);

        Assert.Equal(WardrobeStageGeometry.CardAvatarSize / WardrobeCatalog.AvatarCircleRatio, canvas, 6);
        Assert.False(double.IsNaN(left) || double.IsNaN(top));
    }
}
