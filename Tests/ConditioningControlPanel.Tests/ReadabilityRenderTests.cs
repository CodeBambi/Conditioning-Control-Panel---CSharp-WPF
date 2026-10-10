using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Readability pass (polish 8): the two pages the owner screenshotted ride the shared type
/// scale. Play > Games: the zone eyebrow wears the section ink, every card has a visible
/// outline. Companion > Personality: page title > card title > field label, by size.
/// Set CCP_NAV_PNG_DIR for play-games.png and companion-personality.png.
/// </summary>
public class ReadabilityRenderTests
{
    private static void Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit) yield return hit;
            foreach (var deeper in Descendants<T>(child)) yield return deeper;
        }
    }

    private static Style AppStyle(string key) => (Style)Application.Current.Resources[key];

    private static void Shot(FrameworkElement element, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_NAV_PNG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var w = (int)Math.Ceiling(element.ActualWidth);
        var h = (int)Math.Ceiling(element.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // PanelBg under the Play / Companion 14% wash, near enough for a desk look.
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x29, 0x2B, 0x51)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }

    [Fact]
    public void PlayGames_EyebrowWearsTheSectionInk_AndEveryCardHasAnOutline() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new PlayTabView();
        Realize(page, 1469, 2600);

        var ink = (SolidColorBrush)Application.Current.Resources["SectionInkBrush"];
        var eyebrow = Assert.IsType<SolidColorBrush>(page.ZoneGames.Foreground);
        Assert.Equal(ink.Color, eyebrow.Color);
        Assert.Equal(AppStyle("Type.SectionHeader").Setters.OfType<Setter>()
            .Single(s => s.Property == TextBlock.FontSizeProperty).Value, page.ZoneGames.FontSize);

        var cardStyles = new[] { "PlayCard", "PlayCardT1", "PlayCardT2" }
            .Select(k => (Style)page.ZoneGames.FindResource(k)).ToHashSet();
        var cards = Descendants<Border>(page).Where(b => b.Style != null && cardStyles.Contains(b.Style)).ToList();
        Assert.True(cards.Count >= 9, $"expected the whole wall of cards, found {cards.Count}");
        foreach (var card in cards)
        {
            Assert.NotNull(card.BorderBrush);
            Assert.True(card.BorderThickness.Left >= 1, "a Play card lost its outline");
            if (card.BorderBrush is SolidColorBrush solid)
                Assert.True(solid.Color.A > 0, "a Play card outline is fully transparent");
        }

        Shot(page, "play-games.png");
    });

    [Fact]
    public void Personality_TitleOutranksCardTitleOutranksLabel() => WpfRenderHarness.OnStaThread(() =>
    {
        var page = new PersonalityPage();
        var card = new CompanionPickerCard();
        page.PickerHost.Content = card;
        page.PresetsHost.Content = new MakeHerYoursView { DataContext = new MockMakeHerYoursVm() };
        page.CommunityHost.Content = new WorkshopCommunityCell();
        Realize(page, 1150, 1800);

        var texts = Descendants<TextBlock>(page).ToList();
        var pageTitle = texts.First(t => t.Style == AppStyle("Type.PageTitle"));
        var label = texts.First(t => t.Style == AppStyle("Type.Label"));
        var cardTitle = card.TxtLiveName;

        Assert.Equal(AppStyle("Type.CardTitle"), cardTitle.Style);
        Assert.True(pageTitle.FontSize > cardTitle.FontSize,
            $"page title {pageTitle.FontSize} must outrank the card title {cardTitle.FontSize}");
        Assert.True(cardTitle.FontSize > label.FontSize,
            $"card title {cardTitle.FontSize} must outrank the field label {label.FontSize}");

        // The chip group header in the presets card is an eyebrow now, not a second card title.
        var ink = (SolidColorBrush)Application.Current.Resources["SectionInkBrush"];
        var eyebrows = texts.Where(t => t.FontFamily.Source.StartsWith("Consolas") && t.FontWeight == FontWeights.Bold
                                        && t.Foreground is SolidColorBrush b && b.Color == ink.Color).ToList();
        Assert.NotEmpty(eyebrows);
        Assert.All(eyebrows, e => Assert.True(e.FontSize < cardTitle.FontSize));

        Shot(page, "companion-personality.png");
    });
}
