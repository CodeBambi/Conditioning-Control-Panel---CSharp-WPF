using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Services.Chaster;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Circe's tab as it actually lays out. A missing StaticResource in a tab view is a crash the
/// first time someone opens the page and nothing at compile time, so the page is realized here:
/// with no service it shows the unlinked hero and a dead Link button; linked, the menu builds one
/// row per price on two boards, the title hangs a padlock on every o, the calendar draws a square
/// a day and the key, the paper tag says what is on the tab, and the trailer dresses itself for
/// whichever row it is aimed at.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class ChasterTabRenderTests
{
    static ChasterTabRenderTests()
    {
        // The trailer's scene is a WebView2; with no window behind the page there is no browser
        // to build, and the still picture under it is the trailer.
        ChasterTrailerView.BrowserEnabled = false;
    }

    [Fact]
    public void Every_row_maps_to_a_scene_the_trailers_page_defines_and_the_page_can_mount_one()
    {
        var page = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "web", "chaster", "trailers.html");
        Assert.True(File.Exists(page), "trailers.html is missing");
        var html = File.ReadAllText(page);
        Assert.Contains("window.__mount", html);
        Assert.Contains("CT.run=function(wrap,id)", html);
        Assert.Contains("CT.I.receipt=", html);
        foreach (var id in TabPrices.All.Select(p => p.Id).Append(TabMenuCopy.JackpotId))
        {
            var scene = TabMenuCopy.VignetteFor(id);
            Assert.Contains("V." + scene + "=", html);
        }
        Assert.Equal("typo", TabMenuCopy.VignetteFor("lockcard"));
        Assert.Equal("program", TabMenuCopy.VignetteFor("program_skipped"));
        Assert.Equal("bubbles", TabMenuCopy.VignetteFor("natasha"));
        Assert.Equal("escape", TabMenuCopy.VignetteFor("escape"));
        // the mount script never lets a quote through to the page
        Assert.Equal("window.__mount && window.__mount('typo',null,null)", ChasterTrailerView.MountScript("typo"));
        Assert.Equal("window.__mount && window.__mount('typo',30,60)", ChasterTrailerView.MountScript("typo", 30, 60));
        Assert.DoesNotContain("'", ChasterTrailerView.MountScript("a'b").Replace("__mount('", "").Replace("',null,null)", ""));
    }

    [Fact]
    public void Preview_has_a_loaded_warmup_host_and_reparents_into_the_hover_plate()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            Assert.Same(tab.TrailerWarmHost, tab.TrailerWeb.Parent);
            Assert.Equal(Visibility.Hidden, tab.TrailerWarmHost.Visibility);
            tab.OpenTrailer(new ToggleButton { Tag = "natasha" });
            Assert.Same(tab.TrailerPlate, tab.TrailerWeb.Parent);
            Assert.Equal(Visibility.Collapsed, tab.TrailerWeb.Visibility);
            Assert.Equal("natasha", tab.TrailerId);
            tab.HideTrailer();
        });
    }

    private static void Realize(FrameworkElement element, double width, double height)
    {
        var host = new Grid { Width = width, Height = height };
        host.Children.Add(element);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(new Point(0, 0), new Size(width, height)));
        host.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private static List<ToggleButton> Rows(ChasterTabView tab) =>
        Descendants(tab.CostRows).Concat(Descendants(tab.EarnRows)).OfType<ToggleButton>().ToList();

    [Fact]
    public void With_no_service_the_page_is_the_ask_and_a_link_button_that_cannot_be_pressed()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.OnTabShown();
            Realize(tab, 1000, 700);

            Assert.Equal(Visibility.Visible, tab.UnlinkedPanel.Visibility);
            Assert.Equal(Visibility.Visible, tab.FactRow.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.LinkedPanel.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.SwitchPill.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.PaperTag.Visibility);
            Assert.False(tab.BtnLink.IsEnabled);
            // The hero draws nothing it cannot know with no service behind it.
            Assert.Equal(Visibility.Collapsed, tab.HeroClockRow.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.TxtHeroEnds.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.HeroPills.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.CalendarRow.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.HeroTitle.Visibility);
            Assert.False(tab.Trailer.IsOpen);
        });
    }

    [Fact]
    public void The_menu_is_one_row_per_price_on_two_boards_and_every_row_is_a_picture_a_name_a_where_and_a_stamp()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildMenu();
            tab.BuildMenu(); // a second visit must not double the rows
            Realize(tab, 1000, 2400);

            var rows = Rows(tab);
            Assert.Equal(TabPrices.All.Count, rows.Count);
            Assert.Equal(TabPrices.All.Select(p => p.Id).OrderBy(x => x), rows.Select(t => (string)t.Tag).OrderBy(x => x));
            Assert.All(rows, t => Assert.True(t.ActualWidth > 0, "a menu row collapsed to nothing"));

            // Costs on the red board, earn-backs on the mint one, nothing on both.
            var costIds = Descendants(tab.CostRows).OfType<ToggleButton>().Select(t => (string)t.Tag).ToList();
            var earnIds = Descendants(tab.EarnRows).OfType<ToggleButton>().Select(t => (string)t.Tag).ToList();
            Assert.All(costIds, id => Assert.True(TabPrices.Find(id)!.Seconds > 0, id + " is on the wrong board"));
            Assert.All(earnIds, id => Assert.True(TabPrices.Find(id)!.Seconds < 0, id + " is on the wrong board"));

            foreach (var row in rows)
            {
                var id = (string)row.Tag;
                var texts = Descendants(row).OfType<TextBlock>().Select(t => t.Text).ToList();
                // a name, a where line and a price stamp; the tier sign is a picture, not text
                Assert.True(texts.Count >= 3, id + " lost a line");
                Assert.Contains(texts, t => t.StartsWith("+") || t.StartsWith("-"));
                Assert.Contains(texts, t => t == Localization.Loc.Get(TabMenuCopy.WhereKey(id)));
                Assert.True(Descendants(row).OfType<Image>().Any(), id + " has no picture");
                var wantsSign = TabMenuCopy.BadgeTier(TabPrices.Find(id)!.Gate) > 0;
                Assert.Equal(wantsSign, Descendants(row).OfType<TierBadge>().Any());
            }
        });
    }

    /// <summary>TAB-13: a where line longer than its column ends in "..." inside the column, with the
    /// whole line on hover. The horizontal row around it measured it at unlimited width, so the
    /// trimming never engaged and the column cut it hard (German "Mantras, pro Wiederholung").</summary>
    [Fact]
    public void A_long_where_line_trims_inside_its_column_and_shows_whole_on_hover()
    {
        var loc = Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("de");
            WpfRenderHarness.OnStaThread(() =>
            {
                var tab = new ChasterTabView();
                tab.LinkedPanel.Visibility = Visibility.Visible;
                tab.BuildMenu();
                Realize(tab, 1000, 2400);

                var squeezed = new List<string>();
                foreach (var row in Rows(tab))
                {
                    var id = (string)row.Tag;
                    var text = Localization.Loc.Get(TabMenuCopy.WhereKey(id));
                    var where = Descendants(row).OfType<TextBlock>().First(t => t.Text == text);
                    var words = (FrameworkElement)VisualTreeHelper.GetParent(VisualTreeHelper.GetParent(where));
                    // the line's own width, measured on a twin: the row's line is capped by now
                    var twin = new TextBlock { Text = text, FontSize = where.FontSize, FontFamily = where.FontFamily, FontWeight = where.FontWeight };
                    twin.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (twin.DesiredSize.Width > words.ActualWidth) squeezed.Add(id);
                    Assert.True(where.ActualWidth <= words.ActualWidth + 0.5,
                        $"{id}: the where line is {where.ActualWidth:0} wide in a {words.ActualWidth:0} column");
                    Assert.Equal(text, where.ToolTip as string);
                }
                // the row the hunt saw cut, or this proves nothing
                Assert.Contains("mantra", squeezed);
            });
        }
        finally { loc.SetLanguage(previous); }
    }

    [Fact]
    public void Every_row_with_a_picture_points_at_a_png_that_ships()
    {
        var resources = System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources");
        foreach (var id in TabMenuCopy.ArtIds)
        {
            var art = TabMenuCopy.ArtFor(id)!;
            Assert.True(File.Exists(System.IO.Path.Combine(resources, art.Replace("/", System.IO.Path.DirectorySeparatorChar.ToString()))), id + " points at a missing " + art);
        }
        // and every price row has one
        Assert.All(TabPrices.All, p => Assert.NotNull(TabMenuCopy.ArtFor(p.Id)));
    }

    [Fact]
    public void A_row_with_no_copy_still_prints_its_id_and_the_sign_follows_the_gate()
    {
        Assert.Equal("nobody_wrote_this", TabMenuCopy.ShortName("nobody_wrote_this", _ => null));
        Assert.Equal("nobody_wrote_this", TabMenuCopy.ShortName("nobody_wrote_this", key => key));
        Assert.Equal("Typo", TabMenuCopy.ShortName("typo", _ => "Typo"));
        Assert.Equal(0, TabMenuCopy.BadgeTier(TabPriceGate.Free));
        Assert.Equal(0, TabMenuCopy.BadgeTier(TabPriceGate.Sparkles));
        Assert.Equal(1, TabMenuCopy.BadgeTier(TabPriceGate.Tier1));
        Assert.Equal(2, TabMenuCopy.BadgeTier(TabPriceGate.Tier2));
    }

    [Fact]
    public void The_keys_start_dark_with_nothing_on_and_the_menu_is_in_view_without_pressing_anything()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.RefreshPresets();
            tab.BuildMenu();
            Realize(tab, 1000, 2400);

            foreach (var key in new[] { tab.BtnPresetGentle, tab.BtnPresetStrict, tab.BtnPresetCirce, tab.BtnPresetCustom })
            {
                Assert.True(key.ActualWidth > 0, "a key collapsed to nothing");
                Assert.NotEqual(true, key.IsChecked);
            }
            Assert.True(tab.CostBoard.ActualHeight > 0 && tab.EarnBoard.ActualHeight > 0, "a board did not lay out");
            Assert.True(tab.JackpotRow.ActualWidth > 0, "the jackpot row did not lay out");
        });
    }

    [Fact]
    public void The_title_hangs_a_padlock_on_every_o_and_leans_them_alternately()
    {
        var runs = LockTitle.Split("Locktober");
        Assert.Equal(new[] { ("L", false), ("o", true), ("ckt", false), ("o", true), ("ber", false) }, runs.Select(r => (r.Run, r.Padlock)));
        Assert.Empty(LockTitle.Split(""));
        Assert.Equal(new[] { ("O", true), ("O", true) }, LockTitle.Split("OO").Select(r => (r.Run, r.Padlock)));

        WpfRenderHarness.OnStaThread(() =>
        {
            var title = new LockTitle { Text = "Locktober", FontSize = 88 };
            Realize(title, 1000, 140);
            Assert.Equal(88, title.EffectiveFontSize);

            Assert.Equal(2, title.PadlockCount);
            var glyphs = title.Glyphs.ToList();
            Assert.Equal(5, glyphs.Count);
            Assert.All(glyphs, g => Assert.True(g.ActualWidth > 0 && g.ActualHeight > 0, "a glyph did not lay out"));
            var padlocks = glyphs.OfType<Image>().ToList();
            Assert.Equal(2, padlocks.Count);
            // the padlock stands on the cap line, not the full line box, and leans opposite ways
            Assert.All(padlocks, p => Assert.InRange(p.Height, 88 * 0.6, 88 * 0.95));
            var tilts = padlocks.Select(p => ((TransformGroup)p.RenderTransform).Children.OfType<RotateTransform>().First().Angle).ToList();
            Assert.Equal(-tilts[0], tilts[1]);
            Assert.NotEqual(0, tilts[0]);
            // the letters wear the candy gradient, under an ice sliver
            var texts = glyphs.OfType<Grid>().SelectMany(g => g.Children.OfType<TextBlock>()).ToList();
            Assert.Equal(6, texts.Count);
            Assert.Contains(texts, t => t.Foreground is LinearGradientBrush);
            Assert.Contains(texts, t => t.OpacityMask != null);

            title.Text = "no ring";
            title.UpdateLayout();
            Assert.Equal(1, title.PadlockCount);
            title.Text = "";
            Assert.Equal(0, title.PadlockCount);
            Assert.Empty(title.Glyphs);

            // the hero's inner width at a 1000 px card is 948: Locktober at 88 fits it whole,
            // and a long lock name shrinks to the width instead of wrapping or clipping
            var fitted = new LockTitle { Text = "Locktober", FontSize = 88, FitWidth = 948 };
            Realize(fitted, 948, 140);
            Assert.Equal(88, fitted.EffectiveFontSize);
            Assert.True(fitted.ActualWidth <= 948, "Locktober at 88 does not fit a 1000 px hero");
            var longName = new LockTitle { Text = "The Longest Lock Anyone Ever Wore", FontSize = 88, FitWidth = 600 };
            Realize(longName, 600, 140);
            Assert.True(longName.EffectiveFontSize < 88, "a long name did not shrink");
            Assert.True(longName.ActualWidth <= 601, $"the fitted word is {longName.ActualWidth} wide in 600");
            Assert.True(LockTitle.NaturalWidth("Locktober", 88) > LockTitle.NaturalWidth("Locktober", 60));
        });
    }

    [Fact]
    public void The_calendar_is_a_square_a_day_for_the_demo_lock_with_the_key_after_the_last()
    {
        // the demo lock's shape: 31 days first to last, today 19 days in
        var today = new DateTime(2026, 9, 22, 10, 0, 0);
        var span = (today.AddDays(-18), today.AddDays(12).AddHours(4));

        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildCalendar(span, today);
            Realize(tab, 1000, 1400);

            Assert.Equal(Visibility.Visible, tab.CalendarRow.Visibility);
            var squares = tab.Calendar.Children.OfType<Border>().ToList();
            Assert.Equal(32, squares.Count);
            Assert.All(squares, sq => Assert.True(sq.ActualWidth > 0 && sq.ActualHeight > 0, "a square did not lay out"));
            Assert.True(tab.Calendar.ActualWidth <= 8 * (ChasterTabView.CellSize + 1) + 1, "the sheet is wider than eight columns");
            Assert.True(tab.Calendar.ActualHeight >= 4 * ChasterTabView.CellSize, "a month and its key did not take four rows");
            Assert.True(tab.Sheet.ActualWidth < 420, $"the sheet is {tab.Sheet.ActualWidth} wide, it must leave room for the countdown");

            // 18 crossed out in marker (two strokes and a splat), tonight ringed, 12 stamped, then the sticker
            var crosses = squares.Where(sq => Descendants(sq).OfType<Canvas>().Any(c => c.Children.OfType<Ellipse>().Any())).ToList();
            Assert.Equal(18, crosses.Count);
            Assert.All(crosses, sq => Assert.Equal(2, Descendants(sq).OfType<System.Windows.Shapes.Path>().Count(p => p.Tag is double)));
            var tonight = squares[18];
            Assert.DoesNotContain(tonight, crosses);
            Assert.Equal(2, Descendants(tonight).OfType<System.Windows.Shapes.Path>().Count(p => p.Data is EllipseGeometry));
            var padlocks = squares.Where(sq => Descendants(sq).OfType<Rectangle>().Any(r => r.OpacityMask is ImageBrush)).ToList();
            Assert.Equal(12, padlocks.Count);
            var key = squares[^1];
            var sticker = Assert.Single(Descendants(key).OfType<Border>().Where(b => b.Background is LinearGradientBrush));
            Assert.Contains(Descendants(sticker), d => d is System.Windows.Shapes.Path { Fill: SolidColorBrush });
            Assert.DoesNotContain(Descendants(key), d => d is TextBlock);
            // the numerals are the day of the month
            Assert.Equal("4", Descendants(squares[0]).OfType<TextBlock>().First().Text);
            Assert.Equal("22", Descendants(tonight).OfType<TextBlock>().First().Text);

            // the tag hangs under tonight's square while there is something on the tab
            tab.RefreshTag(750);
            Assert.Equal("+12:30", tab.TxtCalendarTag.Text);
            Assert.Equal(Visibility.Visible, tab.CalendarTag.Visibility);
            tab.RefreshTag(0);
            Assert.Equal(Visibility.Collapsed, tab.CalendarTag.Visibility);

            // the same day again is not a rebuild; a new day is
            var before = squares[0];
            tab.BuildCalendar(span, today.AddHours(3));
            Assert.Same(before, tab.Calendar.Children[0]);
            tab.BuildCalendar(span, today.AddDays(1));
            Assert.NotSame(before, tab.Calendar.Children[0]);
            Assert.Equal(19, tab.Calendar.Children.OfType<Border>().Count(sq => Descendants(sq).OfType<Canvas>().Any(c => c.Children.OfType<Ellipse>().Any())));

            // a year-long lock shows its last 31 days and says so on the first
            tab.BuildCalendar((today.AddDays(-300), today.AddDays(12)), today);
            tab.UpdateLayout();
            var longSquares = tab.Calendar.Children.OfType<Border>().ToList();
            Assert.Equal(32, longSquares.Count);
            Assert.Contains(Descendants(longSquares[0]).OfType<TextBlock>(), t => t.Text == "...");

            tab.BuildCalendar(null, today);
            Assert.Equal(Visibility.Collapsed, tab.CalendarRow.Visibility);
            Assert.Empty(tab.Calendar.Children);
        });
    }

    [Fact]
    public void The_calendar_span_is_the_lock_in_local_days_and_counts_from_today_without_a_start()
    {
        var today = new DateTime(2026, 9, 22, 10, 0, 0, DateTimeKind.Local);
        var end = DateTime.UtcNow.AddDays(12);
        var known = new LockSnapshot("l", "Locktober", end, false, false, true, DateTime.UtcNow) { StartedAtUtc = DateTime.UtcNow.AddDays(-18) };
        var span = ChasterTabView.CalendarSpan(known, today);
        Assert.NotNull(span);
        Assert.Equal(DateTime.UtcNow.AddDays(-18).ToLocalTime().Date, span!.Value.Start.Date);
        Assert.Equal(end.ToLocalTime().Date, span.Value.End.Date);

        var unknown = new LockSnapshot("l", "Locktober", end, false, false, true, DateTime.UtcNow);
        Assert.Equal(today, ChasterTabView.CalendarSpan(unknown, today)!.Value.Start);

        Assert.Null(ChasterTabView.CalendarSpan(null, today));
        Assert.Null(ChasterTabView.CalendarSpan(new LockSnapshot("l", "t", null, false, false, false, DateTime.UtcNow), today));
        Assert.Null(ChasterTabView.CalendarSpan(new LockSnapshot("l", "t", end, false, TimerHidden: true, false, DateTime.UtcNow), today));
    }

    [Fact]
    public void The_paper_tag_says_what_is_on_the_tab_and_stamps_it_unpaid_clear_or_credit()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.PaperTag.Visibility = Visibility.Visible;
            var today = new DateTime(2026, 9, 22, 10, 0, 0);
            tab.BuildCalendar((today, today.AddDays(3)), today);
            Realize(tab, 1000, 1400);

            tab.RefreshTag(750);
            Assert.Equal("+12:30", tab.TxtTagAmount.Text);
            Assert.Equal("UNPAID", tab.TxtTagStamp.Text);
            Assert.Equal("+12:30", tab.TxtCalendarTag.Text);
            Assert.Equal(Visibility.Visible, tab.CalendarTag.Visibility);

            tab.RefreshTag(0);
            Assert.Equal("0:00", tab.TxtTagAmount.Text);
            Assert.Equal("CLEAR", tab.TxtTagStamp.Text);
            Assert.Equal(Visibility.Collapsed, tab.CalendarTag.Visibility);

            tab.RefreshTag(-90);
            Assert.Equal("-1:30", tab.TxtTagAmount.Text);
            Assert.Equal("CREDIT", tab.TxtTagStamp.Text);
            Assert.True(tab.PaperTag.ActualWidth > 0 && tab.PaperTag.ActualHeight > 0, "the tag did not lay out");
        });
    }

    /// <summary>Bug hunt 2026-09-29 (TAB-10): the rubber stamp sat over the end of the tag's own
    /// line ("on its way to the lo" with the rest under UNPAID; the backlog split lost "0 later").
    /// For every stamp word in every language and every line the tag can show, the stamp, tilt
    /// included, stays clear of the line's box and reaches no higher than the empty foot of the
    /// amount's line.</summary>
    [Fact]
    public void The_paper_tag_stamp_never_covers_the_tags_own_line()
    {
        var langs = new[] { "en", "de", "es", "fr", "ja", "ko", "pt-BR", "ru", "zh-CN" }
            .Select(lang => Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(System.IO.Path.Combine(
                RepoRoot(), "ConditioningControlPanel", "Localization", "Languages", lang + ".json"))))
            .ToList();
        var words = langs.SelectMany(json => new[] { "chaster_tag_unpaid", "chaster_tag_clear", "chaster_tag_credit" }
            .Select(k => (string)json[k]!)).ToList();
        var lines = langs.SelectMany(json => new[] { "chaster_tag_lands", "chaster_tag_credit_lands", "chaster_tag_paused",
                "chaster_tag_nolock", "chaster_tag_tomorrow", "chaster_tag_split" }
            .Select(k => ((string)json[k]!).Replace("{0}", "15:00").Replace("{1}", "45:00"))).ToList();
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.PaperTag.Visibility = Visibility.Visible;
            Realize(tab, 1000, 1400);
            tab.RefreshTag(750);
            foreach (var word in words)
            foreach (var line in lines)
            {
                tab.TxtTagStamp.Text = word;
                tab.TxtTagLands.Text = line;
                tab.UpdateLayout();
                var lands = tab.TxtTagLands.TransformToAncestor(tab.PaperTag).TransformBounds(new Rect(tab.TxtTagLands.RenderSize));
                var amount = tab.TxtTagAmount.TransformToAncestor(tab.PaperTag).TransformBounds(new Rect(tab.TxtTagAmount.RenderSize));
                var stamp = tab.TagStamp.TransformToAncestor(tab.PaperTag).TransformBounds(new Rect(tab.TagStamp.RenderSize));
                Assert.True(lands.Height > 0 && stamp.Width > 0, "the tag's line or stamp did not lay out");
                Assert.True(stamp.Left >= lands.Right - 0.5 || stamp.Top >= lands.Bottom - 0.5,
                    $"{word} stamp ({stamp.Left:0.0},{stamp.Top:0.0}) covers '{line}' (right {lands.Right:0.0}, bottom {lands.Bottom:0.0})");
                Assert.True(stamp.Top >= amount.Bottom - 6.5,
                    $"{word} stamp top {stamp.Top:0.0} reaches the amount (bottom {amount.Bottom:0.0}) beside '{line}'");
            }
        });
    }

    [Fact]
    public void Clicking_the_figure_opens_a_box_only_on_rows_with_one_fixed_figure()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildMenu();
            Realize(tab, 1000, 2400);

            // no account linked: no lock runs, so a figure can move
            Assert.True(tab.BeginPriceEdit("typo"));
            Assert.Equal("typo", tab.EditingPriceId);
            Assert.Equal(Visibility.Collapsed, tab.StampFor("typo")!.Visibility);
            tab.CancelPriceEdit();
            Assert.Null(tab.EditingPriceId);
            Assert.Equal(Visibility.Visible, tab.StampFor("typo")!.Visibility);
            Assert.Contains("+0:30", tab.StampFor("typo")!.Text);

            // sizes picked elsewhere never open a box; the way out has no stamp at all
            Assert.False(tab.BeginPriceEdit("leash"));
            Assert.False(tab.BeginPriceEdit(CircesMisses.EventId));
            Assert.False(tab.BeginPriceEdit(TabDayEnd.StreakEventId));
            Assert.Null(tab.StampFor("panic"));
            Assert.Null(tab.EditingPriceId);
        });
    }

    /// <summary>Bug hunt 2026-09-29 (TAB-8 / DESK-3): "Esc drops it". The panic key is Escape on a
    /// fresh install and the global hook sees every Escape, so the open box is a surface that takes
    /// its own Escape: the press drops the edit and never reaches the panic (no session pause, no
    /// safety hold, no step toward quitting). The row's stamp, outside the box, takes nothing.</summary>
    [Fact]
    public void Escape_in_the_open_price_box_belongs_to_the_box_not_the_panic_key()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildMenu();
            Realize(tab, 1000, 2400);

            var stamp = tab.StampFor("typo")!;
            Assert.False(EscapeClaim.InASurface(stamp));
            Assert.True(tab.BeginPriceEdit("typo"));
            var box = ((Grid)stamp.Parent).Children.OfType<TextBox>().Single();
            Assert.True(EscapeClaim.InASurface(box));

            var fresh = new ConditioningControlPanel.Models.AppSettings();
            Assert.True(PanicPolicy.SurfaceTakesEscape(fresh.PanicKeyEnabled, fresh.PanicKey, lockCardOpen: false,
                ccpInFront: true, surfaceHasTheKeyboard: EscapeClaim.InASurface(box), takenPressOnItsWay: false));
            // with the box gone the next Escape is a panic again
            tab.CancelPriceEdit();
            Assert.False(EscapeClaim.InASurface(stamp));
        });
    }

    [Fact]
    public void Only_an_element_inside_a_marked_surface_has_its_escape()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var inner = new TextBox();
            var surface = new Border { Child = new StackPanel { Children = { inner } } };
            var elsewhere = new TextBox();
            _ = new Grid { Children = { surface, elsewhere } };
            Assert.False(EscapeClaim.InASurface(inner));
            EscapeClaim.Mark(surface);
            Assert.True(EscapeClaim.InASurface(inner));
            Assert.True(EscapeClaim.InASurface(surface));
            Assert.False(EscapeClaim.InASurface(elsewhere));
            Assert.False(EscapeClaim.InASurface(null));
        });
    }

    [Fact]
    public void The_trailer_dresses_itself_for_the_row_it_is_aimed_at_and_closes_clean()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            tab.BuildMenu();
            Realize(tab, 1000, 2400);

            var escape = Rows(tab).Single(r => (string)r.Tag == "escape");
            tab.OpenTrailer(escape);
            // A popup with no window behind it may not open for real; the aim and the dressing must hold anyway.
            Assert.Equal("escape", tab.TrailerId);
            Assert.Equal(Localization.Loc.Get(TabMenuCopy.FlavourKey("escape")), tab.TxtTrailerFlavour.Text);
            // the words carry the figure the row books, never a figure of their own
            Assert.Equal(Localization.Loc.Get(TabMenuCopy.WhyKey("escape")).Replace("{0}", "3:00"), tab.TxtTrailerWhy.Text);
            Assert.Contains("3:00", tab.TxtTrailerWhy.Text);
            Assert.NotNull(tab.TrailerArt.Source);
            Assert.Equal(Visibility.Collapsed, tab.TrailerWeb.Visibility); // no browser in the harness: the still picture is the trailer
            Assert.IsType<TierBadge>(tab.TrailerBadgeHost.Child); // Lockdown is tier 1

            var typo = Rows(tab).Single(r => (string)r.Tag == "typo");
            tab.OpenTrailer(typo);
            Assert.Equal("typo", tab.TrailerId);
            Assert.Null(tab.TrailerBadgeHost.Child); // a free row wears no sign

            tab.HideTrailer();
            Assert.False(tab.Trailer.IsOpen);
            Assert.Null(tab.TrailerId);
            tab.HideTrailer(); // twice is fine
        });
    }

    [Fact]
    public void The_consent_card_and_the_receipt_start_out_of_the_way()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            Realize(tab, 1000, 1400);

            // Nothing about the first switch-on is on screen until the switch is pressed.
            Assert.Equal(Visibility.Collapsed, tab.ConsentCard.Visibility);
            Assert.Equal(Visibility.Collapsed, tab.ReceiptHost.Visibility);
        });
    }

    [Fact]
    public void The_cap_meter_is_a_fill_inside_a_measured_track_and_starts_empty()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var tab = new ChasterTabView();
            tab.LinkedPanel.Visibility = Visibility.Visible;
            Realize(tab, 1000, 1400);

            Assert.True(tab.CapTrack.ActualWidth > 0, "the cap track did not lay out");
            Assert.Equal(0, tab.CapFill.Width);
            Assert.True(tab.CapFill.Width <= tab.CapTrack.ActualWidth);
        });
    }

    [Fact]
    public void The_bill_realizes_as_a_receipt_with_its_lines_its_figures_and_a_net_stamp()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var start = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);
            var bill = TabBill.Build(new[]
            {
                new TabEntry { AtUtc = start.AddMinutes(1), EventId = "typo", Seconds = 45, Count = 3 },
                new TabEntry { AtUtc = start.AddMinutes(2), EventId = "session", Seconds = -600, Count = 1 },
            }, start, 120);

            var receipt = new ChasterReceiptView();
            receipt.Show(bill);
            Realize(receipt, 500, 500);

            var text = Descendants(receipt).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("+0:45", text);
            Assert.Contains("-10:00", text);
            Assert.Contains(text, t => t.Contains("NET") && t.Contains("-9:15"));
            Assert.True(receipt.ActualWidth > 0 && receipt.ActualHeight > 0, "the receipt did not lay out");
        });
    }

    [Fact]
    public void An_empty_run_prints_one_line_and_no_stamp()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var receipt = new ChasterReceiptView();
            receipt.Show(TabBill.Build(Array.Empty<TabEntry>(), DateTime.UtcNow, 0));
            receipt.Show(null); // and a missing service is the same thing, not a crash
            Realize(receipt, 500, 500);

            var text = Descendants(receipt).OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.DoesNotContain(text, t => t.Contains("NET"));
            Assert.True(receipt.ActualHeight > 0, "the empty receipt did not lay out");
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
