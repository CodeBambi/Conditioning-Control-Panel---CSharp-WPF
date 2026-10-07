using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Views.Tabs;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Controls.NavRail;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary><c>App.Patreon</c> is process-wide; the page render swaps it, so it runs alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PremiumPageCollection
{
    public const string Name = "PremiumPage";
}

/// <summary>
/// Polish 12 (owner, 2026-10-07): the Premium page is back. Home > Premium, tab key "premium",
/// the full vault grouped Basic then Prime (then the free doors), open doors first in each group,
/// every roster entry exactly once. "exclusives" lands there without the "Moved" note, and
/// Account &amp; Plans keeps the plates and the invites with a link here.
/// </summary>
[Collection(PremiumPageCollection.Name)]
public class PremiumPageTests
{
    // ---- the table ----------------------------------------------------------------------------

    [Fact]
    public void Premium_is_Homes_second_page_and_draws_no_pill()
    {
        // Round 2 (owner): no pills on Home. Premium is still Home's page (the rail row lights,
        // the palette lands it, the Vault tile and the header spark open it), just never a pill.
        var home = NavSections.Find(NavSections.Home)!;
        Assert.Equal(new[] { "settings", "premium" }, home.Tabs.Select(t => t.Key).ToArray());
        Assert.Equal(new[] { "settings" }, home.Tabs.Where(t => !t.Hidden).Select(t => t.Key).ToArray());
        Assert.Equal("nav_tab_premium", home.Tabs[1].LabelKey);
        Assert.Equal(NavTabKind.Tab, home.Tabs[1].Kind);
        Assert.Equal(NavSections.Home, NavSections.SectionForTab("premium"));
        Assert.Equal("settings", home.DefaultTab);
    }

    [Fact]
    public void Exclusives_lands_on_Premium_without_the_moved_note()
    {
        Assert.Equal((NavSections.Home, "premium"), NavSections.Redirects["exclusives"]);
        Assert.Contains("exclusives", MainWindow.SilentRedirectKeys);
        Assert.DoesNotContain("exclusives", MainWindow.MovedRedirectKeys);
        // "patreon" still means the account section.
        Assert.Equal((NavSections.Settings, "account"), NavSections.Redirects["patreon"]);
    }

    [Fact]
    public void The_vault_names_search_to_Premium_and_Plans_stays_with_the_account()
    {
        Assert.Equal(new[] { "Exclusives", "Premium", "Velvet Vault", "The Vault" }, NavSections.Aliases["premium"]);
        Assert.Equal(new[] { "Plans" }, NavSections.Aliases["account"]);
        foreach (var q in new[] { "velvet vault", "exclusives", "the vault" })
            Assert.Contains(SettingsPaletteIndex.Search(q), e => e.Id == "tab.premium");
    }

    [Fact]
    public void Premium_is_a_hidden_Home_page_with_no_header_row()
    {
        // Polish 12 round 2 (owner): no breadcrumb, no strip, no pills on Premium; the rail's
        // Home row is the way back and stays lit while Premium shows.
        Assert.False(NavStripRules.ShowsHeader(NavSections.Home));
        Assert.False(NavStripRules.ShowsPills(NavSections.Home));
        Assert.Empty(NavStripRules.Pills(NavSections.Home));
        Assert.True(NavSections.Find(NavSections.Home)!.Tabs.Single(t => t.Key == "premium").Hidden);
        Assert.Equal(NavSections.Home, NavSections.SectionForTab("premium"));
        Assert.Null(NavStripRules.ActivePill("premium"));
        Assert.Equal("nav_tab_premium", NavStripRules.PageLabelKey("premium"));
        Assert.NotNull(NavStripRules.Glyph("premium"));
        // Section hue = Home's lilac.
        Assert.Equal(NavStripRules.Accent(NavSections.Home), NavStripRules.Accent(NavSections.SectionForTab("premium")));
    }

    // ---- the shelf order ----------------------------------------------------------------------

    [Fact]
    public void Every_roster_entry_stands_on_exactly_one_shelf()
    {
        var groups = PremiumShelfOrder.Arrange(ExclusiveFeature.All, f => f.Key, f => f.Tier, _ => false);
        var keys = groups.SelectMany(g => g.Items.Select(f => f.Key)).ToList();
        Assert.Equal(ExclusiveFeature.All.Count, keys.Count);
        Assert.Equal(ExclusiveFeature.All.Select(f => f.Key).OrderBy(k => k), keys.OrderBy(k => k));
        Assert.Equal(new[] { PremiumGroup.Basic, PremiumGroup.Prime, PremiumGroup.Free }, groups.Select(g => g.Group));
    }

    [Fact]
    public void The_groups_follow_the_price_tag_and_Graded_Intake_is_Prime()
    {
        foreach (var f in ExclusiveFeature.All)
        {
            var g = PremiumShelfOrder.GroupOf(f.Key, f.Tier);
            if (f.Tier == 1) Assert.Equal(PremiumGroup.Basic, g);
            else if (f.Tier == 2) Assert.Equal(PremiumGroup.Prime, g);
        }
        Assert.Equal(PremiumGroup.Prime, PremiumShelfOrder.GroupOf("gradedintake", 0));
        Assert.Equal(PremiumGroup.Free, PremiumShelfOrder.GroupOf("backroom", 0));
        Assert.Equal(PremiumGroup.Free, PremiumShelfOrder.GroupOf("justdrop", 0));
    }

    [Fact]
    public void Open_doors_lead_their_group_and_roster_order_holds_in_each_half()
    {
        var roster = new[] { ("a", 1), ("b", 1), ("c", 1), ("d", 2), ("e", 2), ("f", 0) };
        var open = new HashSet<string> { "c", "e" };
        var groups = PremiumShelfOrder.Arrange(roster, r => r.Item1, r => r.Item2, r => open.Contains(r.Item1));
        Assert.Equal(new[] { "c", "a", "b" }, groups[0].Items.Select(r => r.Item1));
        Assert.Equal(new[] { "e", "d" }, groups[1].Items.Select(r => r.Item1));
        Assert.Equal(new[] { "f" }, groups[2].Items.Select(r => r.Item1));
    }

    [Fact]
    public void An_empty_group_is_left_out()
    {
        var groups = PremiumShelfOrder.Arrange(new[] { ("x", 2) }, r => r.Item1, r => r.Item2, _ => true);
        Assert.Single(groups);
        Assert.Equal(PremiumGroup.Prime, groups[0].Group);
    }

    // ---- the page, painted by the real builder ------------------------------------------------

    [Fact]
    public void The_page_paints_grouped_for_a_free_and_a_Basic_account() => WpfRenderHarness.OnStaThread(() =>
    {
        var dir = Environment.GetEnvironmentVariable("PREMIUM_PAGE_SHOTS");
        foreach (var (name, tier) in new[] { ("free", PatreonTier.None), ("basic", PatreonTier.Level1) })
        {
            WithTier(tier, () =>
            {
                var (mw, view) = PaintPage();
                var shelf = view.ExclusivesShelf;
                var heads = shelf.Children.OfType<FrameworkElement>().Where(e => Equals(e.Tag, "vault-group")).ToList();
                Assert.Equal(3, heads.Count);
                var cards = shelf.Children.OfType<Border>().Where(b => !Equals(b.Tag, "vault-group")
                    && b.Visibility == Visibility.Visible).ToList();
                Assert.True(cards.Count >= ExclusiveFeature.All.Count(f => f.Shown()));

                // The first card under BASIC is open for a Basic account (no veil), and the shelf
                // starts with the BASIC header.
                Assert.Same(heads[0], shelf.Children[0]);
                Assert.Equal(Visibility.Collapsed, view.InvitesHost.Visibility);
                Assert.Equal(Visibility.Visible, view.SpotlightCard.Visibility);
                if (!string.IsNullOrEmpty(dir)) Shot(view, dir!, $"page-{name}.png");
            });
        }
    });

    // ---- round 2 flair (owner: "add more particles and flair to the Premium section") ------------

    [Fact]
    public void Cards_that_are_yours_wear_a_rim_glow_in_their_tier_colour_and_locked_ones_do_not() => WpfRenderHarness.OnStaThread(() =>
    {
        WithTier(PatreonTier.Level1, () =>
        {
            var (mw, view) = PaintPage();
            var cards = (IEnumerable)typeof(MainWindow).GetField("_exclusiveCards", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mw)!;
            int auras = 0, bare = 0;
            foreach (var ui in cards)
            {
                var t = ui.GetType();
                var feature = (ExclusiveFeature)t.GetField("Feature")!.GetValue(ui)!;
                var aura = (VaultCardAura?)t.GetField("Aura")!.GetValue(ui);
                if (!feature.Shown()) continue;
                var group = PremiumShelfOrder.GroupOf(feature.Key, feature.Tier);
                if (feature.Tier == 1)
                {
                    // A Basic account owns every Basic door: each wears a gold rim.
                    Assert.NotNull(aura);
                    Assert.Equal(Color.FromRgb(0xFF, 0xC8, 0x5A), aura!.Hue);
                    auras++;
                }
                else if (group == PremiumGroup.Prime && aura == null) bare++;
            }
            Assert.True(auras > 0);
            Assert.True(bare > 0, "a locked Prime card should carry no rim glow for a Basic account");
        });
    });

    [Fact]
    public void Each_group_sign_carries_a_shimmer_band() => WpfRenderHarness.OnStaThread(() =>
    {
        WithTier(PatreonTier.None, () =>
        {
            var (mw, _) = PaintPage();
            var sheens = (IList)typeof(MainWindow).GetField("_vaultSignSheens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(mw)!;
            Assert.Equal(2, sheens.Count);   // BASIC and PRIME wear signs; the free group has none
        });
    });

    [Fact]
    public void Vault_motes_are_capped_and_follow_the_motion_level()
    {
        Assert.Equal(0, VaultMoteMath.Cap(MotionLevel.Off));
        Assert.Equal(0, VaultMoteMath.SpawnPerSecond(MotionLevel.Off));
        Assert.True(VaultMoteMath.Cap(MotionLevel.Reduced) < VaultMoteMath.Cap(MotionLevel.Full));
        Assert.True(VaultMoteMath.SpawnPerSecond(MotionLevel.Reduced) < VaultMoteMath.SpawnPerSecond(MotionLevel.Full));
        Assert.True(VaultMoteMath.SpeedScale(MotionLevel.Reduced) < VaultMoteMath.SpeedScale(MotionLevel.Full));
        Assert.InRange(VaultMoteMath.Cap(MotionLevel.Full), 40, 80);
        // The tier's budget still bounds the page: a starved governor starves the motes too.
        Assert.Equal(0, VaultMoteMath.Target(MotionLevel.Full, 0));
        Assert.Equal(10, VaultMoteMath.Target(MotionLevel.Full, 5));
    }

    [Fact]
    public void A_mote_is_born_on_the_ring_round_its_card()
    {
        var zone = new Rect(100, 200, 300, 180);
        var outer = zone; outer.Inflate(VaultMoteMath.RingOut + 0.01, VaultMoteMath.RingOut + 0.01);
        var inner = zone; inner.Inflate(-VaultMoteMath.RingIn - 0.01, -VaultMoteMath.RingIn - 0.01);
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var p = VaultMoteMath.SpawnOnRing(zone, rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
            Assert.True(outer.Contains(p), $"{p} is outside the ring");
            Assert.False(inner.Contains(p), $"{p} is deep inside the card, where nobody sees it");
        }
    }

    [Fact]
    public void An_aura_breathes_and_glints_at_Full_breathes_slowly_at_Reduced_and_holds_still_at_Off() => WpfRenderHarness.OnStaThread(() =>
    {
        var card = new Border { Width = 200, Height = 120 };
        var aura = new VaultCardAura(card, Colors.Gold, 12);
        aura.Start(MotionLevel.Full, 3);
        Assert.True(aura.Breathing);
        Assert.True(aura.Glinting);
        aura.Start(MotionLevel.Reduced, 3);
        Assert.True(aura.Breathing);
        Assert.False(aura.Glinting);
        aura.Start(MotionLevel.Off, 3);
        Assert.False(aura.Breathing);
        Assert.Equal(VaultCardAura.StaticOpacity, aura.Opacity);
        aura.Stop();
        Assert.False(aura.Breathing);
    });

    [Fact]
    public void Account_and_Plans_keeps_the_plates_and_the_invites_and_links_here() => WpfRenderHarness.OnStaThread(() =>
    {
        var plans = new ExclusivesTabView { PlansMode = true };
        Assert.Equal(Visibility.Visible, plans.BtnSeePremium.Visibility);
        Assert.Equal(Visibility.Visible, plans.InvitesHost.Visibility);
        Assert.Equal(Visibility.Collapsed, plans.SpotlightCard.Visibility);
        Assert.Equal(Visibility.Collapsed, plans.ExclusivesShelf.Visibility);

        var page = new ExclusivesTabView();
        Assert.Equal(Visibility.Collapsed, page.BtnSeePremium.Visibility);
        Assert.Equal(Visibility.Visible, page.ExclusivesShelf.Visibility);
    });

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>Stands an uninitialised PatreonService at <paramref name="tier"/> in for App.Patreon.</summary>
    private static void WithTier(PatreonTier tier, Action body)
    {
        var prop = typeof(App).GetProperty(nameof(App.Patreon), BindingFlags.Static | BindingFlags.Public)!;
        var before = prop.GetValue(null);
        var svc = (PatreonService)RuntimeHelpers.GetUninitializedObject(typeof(PatreonService));
        typeof(PatreonService).GetField("<CurrentTier>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(svc, tier);
        prop.SetValue(null, svc);
        try { body(); }
        finally { prop.SetValue(null, before); }
    }

    /// <summary>
    /// The real builder on a MainWindow that never ran its constructor: only the collections the
    /// vault partial keeps are made, the Premium page view is handed in, and ShowVaultPage runs
    /// the same three steps ShowTab("premium") runs.
    /// </summary>
    private static (MainWindow Mw, ExclusivesTabView View) PaintPage()
    {
        var mw = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        foreach (var f in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            var t = f.FieldType;
            if (f.GetValue(mw) != null || t.IsValueType || t.IsAbstract || t.IsInterface) continue;
            bool collection = t.IsGenericType && typeof(IEnumerable).IsAssignableFrom(t)
                && t.Namespace == "System.Collections.Generic";
            if (!collection) continue;
            try { f.SetValue(mw, Activator.CreateInstance(t)); } catch { /* not needed here */ }
        }

        var view = new ExclusivesTabView();
        typeof(MainWindow).GetField("_premiumView", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(mw, view);

        var host = new System.Windows.Documents.AdornerDecorator { Width = 1400, Child = view };
        void Layout()
        {
            host.Measure(new Size(1400, 3200));
            host.Arrange(new Rect(0, 0, 1400, 3200));
            host.UpdateLayout();
        }
        Layout();

        var paint = typeof(MainWindow).GetMethod("PaintVault", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var show = typeof(MainWindow).GetMethod("ShowVaultPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var fit = typeof(MainWindow).GetMethod("FitExclusiveShelf", BindingFlags.Instance | BindingFlags.NonPublic)!;
        paint.Invoke(mw, new object[] { view, (Action)(() => show.Invoke(mw, null)) });
        Layout();
        fit.Invoke(mw, null);
        Layout();
        return (mw, view);
    }

    private static void Shot(FrameworkElement element, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var content = (FrameworkElement)((ScrollViewer)element.FindName("ContentScroll")).Content;
        int w = (int)Math.Ceiling(element.ActualWidth);
        int h = (int)Math.Ceiling(Math.Min(3200, content.ActualHeight + 10));
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), null, new Rect(0, 0, w, h));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                Viewbox = new Rect(0, 0, w, h), ViewboxUnits = BrushMappingMode.Absolute },
                null, new Rect(0, 0, w, h));
        }
        rtb.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(dir, name));
        enc.Save(fs);
    }
}
