using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The companion picker after the CCP Default pivot: single-look mods preview the animated avatar
/// they really wear (never the retired neon-girl pose art), and "More personality options" sits
/// under the personality choice instead of below the whole card.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class CompanionPickerCardTests
{
    [Theory]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData(BuiltInMods.BambiSleepId)]
    [InlineData(BuiltInMods.SissyHypnoId)]
    public void SingleLookMods_PreviewTheSharedAnimatedAvatar(string modId)
    {
        var uri = AvatarTubeWindow.EmoteIdleClipUri(modId, 1, installedPath: null);
        Assert.NotNull(uri);
        Assert.EndsWith("/Resources/avatar0_emotes/idle.gif", uri!.AbsoluteUri);
    }

    [Fact]
    public void NonAnimatedSet_HasNoEmotePreview()
    {
        Assert.Null(AvatarTubeWindow.EmoteIdleClipUri(BuiltInMods.CCPDefaultId, 3, installedPath: null));
        Assert.Null(AvatarTubeWindow.EmoteIdleClipUri(null, 1, installedPath: null));
    }

    [Fact]
    public void EmoteStill_DecodesTheIdleFrame()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var still = CompanionPickerCard.EmoteStill(
                AvatarTubeWindow.EmoteIdleClipUri(BuiltInMods.CCPDefaultId, 1, null));
            Assert.NotNull(still);
            Assert.True(still!.Width > 0 && still.Height > 0);
        });

    [Fact]
    public void MoreOptions_IsHiddenUntilAHostOffersIt()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var card = Layout(new CompanionPickerCard(), 540);
            Assert.Equal(Visibility.Collapsed, card.BtnMorePersonality.Visibility);
        });

    [Theory]
    [InlineData(540)]   // the Companion tab's "who" sheet (580 wide, 18 px padding)
    [InlineData(700)]   // the Customise window's detail column
    public void MoreOptions_SitsUnderThePersonalityChoice_InViewWithoutScrolling(double width)
        => WpfRenderHarness.OnStaThread(() =>
        {
            var card = new CompanionPickerCard { MorePersonalityOptions = _ => { } };
            Layout(card, width);

            Assert.Equal(Visibility.Visible, card.BtnMorePersonality.Visibility);
            var link = card.BtnMorePersonality.TranslatePoint(new Point(0, 0), card);
            var combo = card.CmbPersonality.TranslatePoint(new Point(0, 0), card);
            var sample = card.SamplePanel.TranslatePoint(new Point(0, 0), card);

            Assert.True(link.Y > combo.Y, "link must sit under the personality combo");
            Assert.True(link.Y < sample.Y, "link must come before the sample lines, not below the card");
            Assert.True(Math.Abs(link.X - combo.X) < 1, "link shares the personality column");
            Assert.True(link.Y + card.BtnMorePersonality.ActualHeight < 160,
                $"link must be in the first screenful of the card, was at {link.Y}");
        });

    /// <summary>
    /// Writes a staged picture of the card for a human to look at, only when
    /// CCP_PICKER_SHOT_DIR is set. The services (App.Mods, the tube) do not exist headless, so the
    /// combos and name are filled by hand; the portrait is the real CCP Default emote still.
    /// </summary>
    [Fact]
    public void Shot_WhenAsked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_PICKER_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        WpfRenderHarness.OnStaThread(() =>
        {
            Directory.CreateDirectory(dir);
            var card = new CompanionPickerCard { MorePersonalityOptions = _ => { } };
            card.TxtLiveName.Text = "Companion";
            card.CmbAvatar.Items.Add(new ComboBoxItem { Content = "Companion" });
            card.CmbAvatar.SelectedIndex = 0;
            card.CmbAvatar.IsEnabled = false;
            card.TxtAvatarHint.Text = "This companion has one look.";
            card.TxtAvatarHint.Visibility = Visibility.Visible;
            card.CmbPersonality.Items.Add(new ComboBoxItem { Content = "Pushy Charm" });
            card.CmbPersonality.SelectedIndex = 0;
            card.SamplePanel.Children.Add(new TextBlock
            {
                Text = "“Sit down, relax, and let me do the thinking for a while.”",
                TextWrapping = TextWrapping.Wrap, FontStyle = FontStyles.Italic, Foreground = Brushes.White
            });
            card.TxtPerk.Text = "Pink filter time earns bonus XP.";
            card.TxtPreviewGlyph.Visibility = Visibility.Collapsed;
            card.ImgPreview.Source = CompanionPickerCard.EmoteStill(
                AvatarTubeWindow.EmoteIdleClipUri(BuiltInMods.CCPDefaultId, 1, null));

            var host = new Border { Background = new SolidColorBrush(Color.FromRgb(0x20, 0x17, 0x2B)), Padding = new Thickness(18), Child = card };
            Layout(host, 580);
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(dir, "picker-ccp-default-580.png"));
            enc.Save(fs);
        });
    }

    private static T Layout<T>(T element, double width) where T : FrameworkElement
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), new Size(width, Math.Max(1, element.DesiredSize.Height))));
        element.UpdateLayout();
        return element;
    }
}
