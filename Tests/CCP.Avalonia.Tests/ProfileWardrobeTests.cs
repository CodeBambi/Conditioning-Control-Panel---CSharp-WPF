using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The profile wardrobe on this head (WPF MainWindow.ProfileWardrobe + the two dialogs):
/// real registry art on the hero, the charms placed by Core WardrobeStageGeometry, and the
/// Customize/Wardrobe dialogs offering and arranging real items.</summary>
public sealed class ProfileWardrobeTests
{
    private const string Deco = "bambi_silk_bow", Charm1 = "bambi_plush_bunny", Charm2 = "bambi_bubble_wand";

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public async Task OwnCard_WearsDecoAndCharms_ViewedNullStripsThem()
    {
        var s = CoreSettings.Current;
        var old = (s.ProfileCosmetics, s.UserDisplayName, s.OfflineMode);
        try
        {
            s.UserDisplayName = null;
            s.OfflineMode = true;
            s.ProfileCosmetics = new ProfileCosmetics
            {
                AvatarDeco = Deco,
                Charms = { Charm1, Charm2 },
                CharmTransforms = new() { [Charm2] = new CosmeticTransform { X = 0.5, Y = 0.5, Scale = 1.0, Rotation = 30 } },
            };
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                Setup();
                var shell = new MainShellWindow();
                shell.Show();
                try
                {
                    // The user path: open the Profile tab, which renders your own card first.
                    shell.ShowTab("discord");
                    for (var i = 0; i < 20 && shell.ProfilePage!.ProfileCharmSlot1.Source == null; i++)
                    {
                        await Task.Delay(20);
                        Dispatcher.UIThread.RunJobs();
                    }
                    var page = shell.ProfilePage!;
                    var deco = page.ProfileHeroAvatar.FindControl<Image>("DecoLayer")!;
                    Assert.True(deco.IsVisible);
                    Assert.NotNull(deco.Source);
                    Assert.True(page.ProfileCharmSlot1.IsVisible);
                    Assert.NotNull(page.ProfileCharmSlot2.Source);
                    Assert.Equal("Plush Bunny", ToolTip.GetTip(page.ProfileCharmSlot1));

                    // Placement is Core WardrobeStageGeometry against the measured card.
                    shell.UpdateLayout();
                    shell.ApplyOwnProfileWardrobe();
                    var card = page.ProfileHeroCard.Bounds;
                    Assert.True(card.Width > 0);
                    var (left, top, size) = WardrobeStageGeometry.CharmRect(card.Width, card.Height, 0.5, 0.5, 1.0);
                    Assert.Equal(left, Canvas.GetLeft(page.ProfileCharmSlot2), 3);
                    Assert.Equal(top, Canvas.GetTop(page.ProfileCharmSlot2), 3);
                    Assert.Equal(size, page.ProfileCharmSlot2.Width, 3);
                    Assert.NotNull(page.ProfileCharmSlot2.RenderTransform);

                    // A card resize re-places the charms on its own (WPF SizeChanged hook), no re-apply.
                    page.ProfileHeroCard.Width = card.Width - 400;
                    shell.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    var wider = page.ProfileHeroCard.Bounds;
                    Assert.True(wider.Width < card.Width, $"{wider.Width} vs {card.Width}");
                    var moved = WardrobeStageGeometry.CharmRect(wider.Width, wider.Height, 0.5, 0.5, 1.0);
                    Assert.Equal(moved.Left, Canvas.GetLeft(page.ProfileCharmSlot2), 3);

                    // Someone else's card with no cosmetics: nothing of yours is left on it.
                    shell.ApplyViewedProfileWardrobe(null);
                    Assert.False(deco.IsVisible);
                    Assert.False(page.ProfileCharmSlot1.IsVisible);
                    Assert.False(page.ProfileCharmSlot2.IsVisible);

                    // Per-slot validation: a charm id in the decoration slot is not worn.
                    shell.ApplyViewedProfileWardrobe(new ProfileCosmetics { AvatarDeco = Charm1 });
                    Assert.False(deco.IsVisible);
                }
                finally { shell.Close(); }
            });
        }
        finally { (s.ProfileCosmetics, s.UserDisplayName, s.OfflineMode) = old; }
    }

    [Fact]
    public async Task Customize_OffersRealItems_TogglesAndCaps_EditorDrawsArt()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var dialog = new ProfileCustomizeDialog(new ProfileCosmetics(), null);
            var tabs = dialog.FindControl<WrapPanel>("WardrobeModTabs")!;
            var decoHost = dialog.FindControl<WrapPanel>("WardrobeDecoHost")!;
            var charmHost = dialog.FindControl<WrapPanel>("WardrobeCharmHost")!;
            Assert.True(tabs.IsVisible);
            Assert.True(tabs.Children.Count >= 2);
            Assert.NotEmpty(decoHost.Children);
            Assert.NotEmpty(charmHost.Children);
            Assert.False(dialog.FindControl<TextBlock>("TxtWardrobeEmpty")!.IsVisible);

            // Equip, re-click to take off, and the charm cap - the WPF ToggleWardrobeItem rules.
            dialog.ToggleWardrobeItem(WardrobeCatalog.Find(Deco)!);
            Assert.Equal(Deco, dialog.Result.AvatarDeco);
            dialog.ToggleWardrobeItem(WardrobeCatalog.Find(Deco)!);
            Assert.Null(dialog.Result.AvatarDeco);
            var charms = WardrobeCatalog.Items.Where(i => i.IsCharm).Take(3).ToList();
            foreach (var c in charms) dialog.ToggleWardrobeItem(c);
            Assert.Equal(charms.Take(ProfileCosmetics.MaxCharms).Select(c => c.Id), dialog.Result.Charms);
            Assert.Equal(Loc.GetF("profile_customize_wardrobe_charms_full", ProfileCosmetics.MaxCharms),
                dialog.FindControl<TextBlock>("TxtWardrobeSlots")!.Text);

            // The editor stage draws the registry art and names, not placeholders.
            var editor = new WardrobeEditorDialog(new ProfileCosmetics { AvatarDeco = Deco, Charms = { Charm1 } }, null);
            var sprites = editor.FindControl<Canvas>("Stage")!.Children.OfType<Image>().ToList();
            Assert.Equal(2, sprites.Count);
            Assert.All(sprites, i => Assert.NotNull(i.Source));
            var chips = editor.FindControl<WrapPanel>("ItemChips")!.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Plush Bunny", chips);
            return Task.CompletedTask;
        });
    }

    /// <summary>A real press/release on the middle of <paramref name="c"/> (scrolled into view first).</summary>
    private static void Click(Window w, Control c)
    {
        c.BringIntoView();
        w.UpdateLayout();
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task Customize_OffersSceneBannersPresetsAndPinArt_ClickEquips_EditorPaintsBanner()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var dialog = new ProfileCustomizeDialog(new ProfileCosmetics(),
                new[] { ("plastic_initiation", "Plastic Initiation"), ("no_such_achievement", "Ghost") });
            Border Tile(string host, string tip) => dialog.FindControl<WrapPanel>(host)!.Children.OfType<Border>()
                .Single(b => (ToolTip.GetTip(b) as string) == tip);

            // Every pool entry whose art loads is offered (WPF BuildBanners/BuildAvatars), plus "none".
            Assert.Equal(1 + CosmeticsPool.Banners.Count, dialog.FindControl<WrapPanel>("BannerHost")!.Children.Count);
            Assert.Equal(1 + CosmeticsPool.AvatarPresets.Count, dialog.FindControl<WrapPanel>("AvatarHost")!.Children.Count);
            Assert.IsType<ImageBrush>(((Border)Tile("AvatarHost", "Twin Tails").Child!).Background);

            // Pins draw the achievement art; an achievement without art is not offered (WPF BuildPinTile).
            var pins = dialog.FindControl<WrapPanel>("PinHost")!.Children;
            Assert.Single(pins);
            Assert.NotNull(((Grid)((Border)pins[0]).Child!).Children.OfType<Image>().Single().Source);

            dialog.Show();
            try
            {
                Click(dialog, Tile("BannerHost", "Neon Den"));
                Assert.Equal("bambi_neon_den", dialog.Result.BannerId);
                Click(dialog, Tile("AvatarHost", "Twin Tails"));
                Assert.Equal("avatar_bambi_2", dialog.Result.AvatarId);
                // Keyboard reach: a tabbed-to tile answers Enter.
                Assert.True(((Border)pins[0]).Focus(NavigationMethod.Tab));
                dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.Equal(new[] { "plastic_initiation" }, dialog.Result.PinnedAchievements);
            }
            finally { dialog.Close(); }

            // The editor stage paints the chosen banner, cropped from the top like the hero.
            var editor = new WardrobeEditorDialog(new ProfileCosmetics { BannerId = "bambi_neon_den", AvatarDeco = Deco }, null);
            var stage = Assert.IsType<ImageBrush>(editor.FindControl<Border>("StageBanner")!.Background);
            Assert.NotNull(stage.Source);
            Assert.Equal(AlignmentY.Top, stage.AlignmentY);
            return Task.CompletedTask;
        });
    }
}
