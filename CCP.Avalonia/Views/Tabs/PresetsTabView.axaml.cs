using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/PresetsTabView.xaml.cs.
    ///
    /// The WPF code-behind holds NO view logic: all 21 handlers are two-line forwards into
    /// <c>MainWindow</c> (<c>Window.GetWindow(this) is MainWindow mw</c> -> <c>mw.Whatever(...)</c>)
    /// and the page's real behaviour lives in MainWindow.Presets.cs, .SessionIO.cs, .PresetIO.cs,
    /// .Takeaway.cs and .TabFxPresetsQuestsAchievements.cs. So no handler is wired in the XAML.
    ///
    /// ponytail: needs MainWindow (preset CRUD, SessionManager, JustDropOrdersService, the
    /// catalogue and the tab FX clock), wired when they move to Core. The wiring points, all
    /// named in the XAML:
    ///   BtnCreateSession / BtnSessionHistory / BtnStartSession / BtnRevealSpoilers /
    ///   BtnLoadPreset / BtnSaveOverPreset / BtnDeletePreset / BtnExportPreset / BtnSharePreset /
    ///   BtnExportSession / BtnSelectCornerGif / ChkCornerGifEnabled / RbCornerTL..BR /
    ///   SliderCornerGifSize + SliderCornerGifOpacity / CmbRackSort.SelectionChanged /
    ///   TxtRackSearch.TextChanged / the "+ New" preset chip / SessionDropZone (catalogue) /
    ///   every rack row and preset chip click, and IsVisibleChanged ->
    ///   OnPresetsTabVisibilityChanged (the card-sheen clock, started on show, dropped on hide).
    ///
    /// Two handlers that look view-only are NOT wired on purpose. SliderCornerGif*_ValueChanged
    /// stamps "{n}px" / "{n}%" into TxtCornerGifSize / TxtCornerGifOpacity, but it also writes
    /// AppSettings, and those two labels carry {loc:Str} - assigning .Text over a live loc binding
    /// is the documented trap that loses the value on the next language change. They come back
    /// with the settings service.
    /// </summary>
    public partial class PresetsTabView : UserControl
    {
        public PresetsTabView()
        {
            AvaloniaXamlLoader.Load(this);
            SeedPlaceholders();
        }

        // ---- PLACEHOLDER CONTENT ---------------------------------------------------
        //
        // The rail chips, the toolbar chips, the rack rows and the Takeaway strip are ALL built
        // in code on WPF too (MainWindow.Presets.CreatePresetCard, EnsureSessionRackToolbar,
        // BuildSessionRackRow, PaintTakeawayShelf), reaching the styles in this view's own
        // dictionary through TryFindTabStyle. The same shapes are built here against the same
        // keys, with sample data, so the ControlThemes actually draw in the render proof - four
        // empty panels would compile clean and prove nothing (CLAUDE.md traps 4 and 6).
        //
        // ponytail: replace with the real builders when SessionManager / the preset store /
        // JustDropOrdersService move to Core.

        private void SeedPlaceholders()
        {
            SeedPresetRail();
            SeedRackToolbar();
            SeedSessionRack();
            SeedTakeaway();
        }

        /// <summary>Three chips ahead of the fixed "+ New" one, as CreatePresetCard inserts them.</summary>
        private void SeedPresetRail()
        {
            var panel = this.FindControl<WrapPanel>("PresetCardsPanel");
            if (panel == null) return;

            int at = 0;
            panel.Children.Insert(at++, PresetChip("Morning Drift", "⚡🌀", isDefault: true, selected: false));
            panel.Children.Insert(at++, PresetChip("Deep Soak", "⚡🎬💭🌀", isDefault: false, selected: true));
            panel.Children.Insert(at, PresetChip("Quiet Hours", "💭🔒", isDefault: false, selected: false));
        }

        private Border PresetChip(string name, string glyphs, bool isDefault, bool selected)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // Feature glyphs ahead of the name - the same five the detail pane uses.
            line.Children.Add(new TextBlock
            {
                Text = glyphs,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });

            var nameText = new TextBlock { Text = name, MaxWidth = 150, Theme = TabTheme("SdPresetChipName") };
            line.Children.Add(nameText);

            // DEF / CUSTOM in the RACK's provenance colours: a built-in preset and a built-in
            // session are the same kind of thing, so they wear the same cyan.
            var (tagText, tagSolid, tagWash) = isDefault
                ? (Loc.Get("preset_tag_default"), "SessionSrcBuiltInBrush", "SessionSrcBuiltInWashBrush")
                : (Loc.Get("preset_tag_custom"), "SessionSrcCustomBrush", "SessionSrcCustomWashBrush");
            line.Children.Add(Pill(tagText, tagWash, tagSolid, "SdRackBadgeText", "SdChipTag"));

            return new Border
            {
                Theme = TabTheme(selected ? "SdPresetChipSelected" : "SdPresetChip"),
                Child = line,
            };
        }

        /// <summary>Four source chips (single-select) and four difficulty dots (independent).</summary>
        private void SeedRackToolbar()
        {
            var sources = this.FindControl<StackPanel>("RackSourceChips");
            var dots = this.FindControl<StackPanel>("RackDifficultyChips");

            if (sources != null)
            {
                // RackSourceChipLabel: "<label>  <count>".
                sources.Children.Add(SourceChip("rack_source_all", 4, "all", isOn: true));
                sources.Children.Add(SourceChip("rack_source_builtin", 2, "builtin", isOn: false));
                sources.Children.Add(SourceChip("rack_source_yours", 1, "yours", isOn: false));
                sources.Children.Add(SourceChip("rack_source_catalogue", 1, "catalogue", isOn: false));
            }

            if (dots != null)
            {
                dots.Children.Add(Dot("SessionDiffEasyBrush", Loc.Get("rack_diff_easy"), on: true));
                dots.Children.Add(Dot("SessionDiffMediumBrush", Loc.Get("rack_diff_medium"), on: true));
                dots.Children.Add(Dot("SessionDiffHardBrush", Loc.Get("rack_diff_hard"), on: true));
                dots.Children.Add(Dot("SessionDiffExtremeBrush", Loc.Get("rack_diff_extreme"), on: false));
            }

            // The zone hint. UpdateRackToolbarCounts picks rack_count_all when nothing is
            // filtered out and rack_count_filtered otherwise.
            var count = this.FindControl<TextBlock>("TxtRackCount");
            if (count != null) count.Text = Loc.GetF("rack_count_all", 4);
        }

        private ToggleButton SourceChip(string labelKey, int count, string tag, bool isOn) => new()
        {
            Theme = TabTheme("SdRackChip"),
            Tag = tag,
            IsChecked = isOn,
            // A TextBlock rather than a string Content: the labels are localized words today, but
            // every other button on this page had to opt out of Avalonia's access-key parse and a
            // chip is not the place to discover that a translation gained an underscore.
            Content = new TextBlock { Text = $"{Loc.Get(labelKey)}  {count}" },
        };

        private ToggleButton Dot(string solidKey, string tip, bool on)
        {
            var dot = new ToggleButton
            {
                Theme = TabTheme("SdRackDot"),
                IsChecked = on,
                Content = new TextBlock { Text = "●" },
                Foreground = Brush(solidKey),
            };
            ToolTip.SetTip(dot, tip);
            return dot;
        }

        /// <summary>Four rack rows, one of them selected - built-in, custom and catalogue all
        /// present so the three provenance colours and two row themes are all exercised.</summary>
        private void SeedSessionRack()
        {
            var panel = this.FindControl<StackPanel>("SessionRackPanel");
            if (panel == null) return;

            panel.Children.Add(RackRow("🌅", "Morning Drift", "Ease into the day.", "Easy", 15, 45, "rack_src_builtin", false, false));
            panel.Children.Add(RackRow("🎮", "Gamer Girl", "Play while she watches.", "Medium", 30, 90, "rack_src_builtin", true, false));
            panel.Children.Add(RackRow("🪆", "Distant Doll", "Long, quiet, and very far away.", "Hard", 60, 180, "rack_src_yours", false, true));
            panel.Children.Add(RackRow("💀", "Good Girls", "Nothing left to decide.", "Extreme", 90, 320, "rack_src_catalogue", false, true));
        }

        private Border RackRow(string icon, string name, string blurb, string difficulty,
                               int minutes, int xp, string srcKey, bool selected, bool deletable)
        {
            var (diffSolid, diffWash) = difficulty switch
            {
                "Medium" => ("SessionDiffMediumBrush", "SessionDiffMediumWashBrush"),
                "Hard" => ("SessionDiffHardBrush", "SessionDiffHardWashBrush"),
                "Extreme" => ("SessionDiffExtremeBrush", "SessionDiffExtremeWashBrush"),
                _ => ("SessionDiffEasyBrush", "SessionDiffEasyWashBrush"),
            };
            var (srcSolid, srcWash) = srcKey switch
            {
                "rack_src_yours" => ("SessionSrcCustomBrush", "SessionSrcCustomWashBrush"),
                "rack_src_catalogue" => ("SessionSrcImportedBrush", "SessionSrcImportedWashBrush"),
                _ => ("SessionSrcBuiltInBrush", "SessionSrcBuiltInWashBrush"),
            };

            var grid = new Grid
            {
                // stripe | icon | name | blurb* | difficulty | duration | xp | badges | actions
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto"),
                ClipToBounds = true,
            };

            // 0. The stripe: full height, full bleed, 4px - the one part of the row you can read
            // at a glance while scrolling.
            var stripe = new Border { Width = 4, VerticalAlignment = VerticalAlignment.Stretch, Background = Brush(diffSolid) };
            Grid.SetColumn(stripe, 0);
            grid.Children.Add(stripe);

            var glyph = new TextBlock { Text = icon, Theme = TabTheme("SdRackIcon"), Margin = new Thickness(7, 0, 0, 0) };
            Grid.SetColumn(glyph, 1);
            grid.Children.Add(glyph);

            // MaxWidth rather than a star column: a long custom name must not push the blurb off
            // the row, and an Auto column will not trim without one.
            var title = new TextBlock { Text = name, MaxWidth = 210, Theme = TabTheme("SdRowTitle"), Margin = new Thickness(7, 0, 0, 0) };
            Grid.SetColumn(title, 2);
            grid.Children.Add(title);

            var desc = new TextBlock { Text = blurb, Theme = TabTheme("SdRowBlurb") };
            ToolTip.SetTip(desc, blurb);
            Grid.SetColumn(desc, 3);
            grid.Children.Add(desc);

            var diffPill = Pill(difficulty, diffWash, diffSolid);
            Grid.SetColumn(diffPill, 4);
            grid.Children.Add(diffPill);

            var duration = Meta(Loc.GetF("rack_duration", minutes), 56);
            Grid.SetColumn(duration, 5);
            grid.Children.Add(duration);

            var reward = Meta(Loc.GetF("rack_xp", xp), 66);
            Grid.SetColumn(reward, 6);
            grid.Children.Add(reward);

            var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            badges.Children.Add(Pill(Loc.Get(srcKey), srcWash, srcSolid, "SdRackBadgeText"));
            Grid.SetColumn(badges, 7);
            grid.Children.Add(badges);

            // Edit + export on everything; share + delete only where they could ever succeed -
            // DeleteSession refuses a built-in outright. WPF reveals these on hover; see the note
            // on SdRackActions in the XAML for why they are always shown here.
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(RowAction("✎", Loc.Get("tooltip_edit_session"), danger: false));
            actions.Children.Add(RowAction("↗", Loc.Get("tooltip_export_session"), danger: false));
            if (deletable)
            {
                actions.Children.Add(RowAction("☁", Loc.Get("tooltip_share_to_catalogue"), danger: false));
                actions.Children.Add(RowAction("\U0001F5D1", Loc.Get("tooltip_delete_session"), danger: true));
            }
            // Pad out to four buttons' worth (28px wide, 3px margin) so a two-button built-in row
            // reserves the same width as a four-button custom one - without it the meta columns
            // step sideways every time the list crosses from one kind of row to the other.
            actions.Margin = new Thickness(8 + (4 - actions.Children.Count) * 31, 0, 4, 0);
            Grid.SetColumn(actions, 8);
            grid.Children.Add(actions);

            return new Border
            {
                Theme = TabTheme(selected ? "SdSessionRowSelected" : "SdSessionRow"),
                Child = grid,
            };
        }

        private Button RowAction(string glyph, string tip, bool danger)
        {
            var btn = new Button
            {
                Theme = TabTheme(danger ? "SdRowActionDanger" : "SdRowAction"),
                Content = new TextBlock { Text = glyph },
            };
            ToolTip.SetTip(btn, tip);
            return btn;
        }

        /// <summary>Three pinned receipts, the "+n more" toggle, the shop door, and three tray
        /// rows behind it. The tray host stays collapsed, as PaintTakeawayShelf leaves it.</summary>
        private void SeedTakeaway()
        {
            var shelf = this.FindControl<StackPanel>("TakeawayShelf");
            if (shelf != null)
            {
                // PaintTakeawayShelf pins up to three receipts, then the "+n more" toggle, then
                // the door. ONE receipt here: the strip never wraps and never scrolls sideways,
                // and at the render proof's 1100px the fill is ~330px, so a second receipt would
                // push the door off the clip and leave its ControlTheme unproven. With three or
                // fewer orders there is no overflow, so the toggle (SdTakeawayChipAccent, a
                // two-setter Border variant of the chip below it) is correctly absent too.
                shelf.Children.Add(TakeawayChip("Slow Sink", 30, "AUG 09"));
                shelf.Children.Add(DoorChip());
            }

            var tray = this.FindControl<StackPanel>("TakeawayTray");
            if (tray != null)
            {
                // The tray renders EVERY order the drawer returned, not just the pinned ones.
                tray.Children.Add(TrayRow("Velvet Hour", 45, "AUG 12", Loc.Get("takeaway_today")));
                tray.Children.Add(TrayRow("Slow Sink", 30, "AUG 09", Loc.GetF("takeaway_days_ago", 3)));
                tray.Children.Add(TrayRow("Static Bloom", 20, "JUL 28", Loc.GetF("takeaway_days_ago", 15)));
            }

            var count = this.FindControl<TextBlock>("TxtTakeawayCount");
            if (count != null) count.Text = Loc.GetF("sd_takeaway_kept", 3);
        }

        private Border TakeawayChip(string name, int minutes, string date)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new TextBlock
            {
                Text = "📦",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            line.Children.Add(new TextBlock { Text = name, MaxWidth = 120, Theme = TabTheme("SdTakeawayChipTitle") });
            line.Children.Add(new TextBlock { Text = Loc.GetF("sd_takeaway_meta", minutes, date), Theme = TabTheme("SdTakeawayChipMeta") });

            // The copy element sits INSIDE the chip; its handler marks the click handled, or
            // copying a link would also start playing the drop.
            var copy = new Border
            {
                Theme = TabTheme("SdTakeawayCopy"),
                Child = new TextBlock
                {
                    Text = "🔗",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            ToolTip.SetTip(copy, Loc.Get("tooltip_takeaway_copy_link"));
            line.Children.Add(copy);

            var chip = new Border { Theme = TabTheme("SdTakeawayChip"), Child = line };
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_replay"));
            return chip;
        }

        private Border DoorChip()
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            line.Children.Add(new TextBlock
            {
                Text = "+",
                Foreground = Brush("PinkBrush"),
                FontSize = 15,
                FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            });
            line.Children.Add(new TextBlock
            {
                Text = Loc.Get("sd_takeaway_order"),
                Foreground = Brush("PinkBrush"),
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var chip = new Border { Theme = TabTheme("SdTakeawayChipDoor"), Child = line };
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_order_drop"));
            return chip;
        }

        private Border TrayRow(string name, int minutes, string date, string age)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto") };

            var box = new TextBlock
            {
                Text = "📦",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 0),
            };
            Grid.SetColumn(box, 0);
            grid.Children.Add(box);

            var title = new TextBlock { Text = name, Theme = TabTheme("SdTakeawayRowTitle") };
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);

            var mins = new TextBlock { Text = Loc.GetF("takeaway_row_min", minutes), MinWidth = 58, Theme = TabTheme("SdTakeawayRowMeta") };
            Grid.SetColumn(mins, 2);
            grid.Children.Add(mins);

            var when = new TextBlock { Text = date, MinWidth = 62, Theme = TabTheme("SdTakeawayRowMeta") };
            Grid.SetColumn(when, 3);
            grid.Children.Add(when);

            var howLong = new TextBlock { Text = age, MinWidth = 78, Theme = TabTheme("SdTakeawayRowAge") };
            Grid.SetColumn(howLong, 4);
            grid.Children.Add(howLong);

            return new Border { Theme = TabTheme("SdTakeawayRow"), Child = grid };
        }

        // ---- shared shapes (MakeRackPill / MakeRackMeta) ---------------------------

        /// <summary>Solid foreground on its 13% wash sibling. Background is a local value on
        /// purpose: SdPill deliberately sets none, so every pill can carry its own meaning colour.</summary>
        private Border Pill(string text, string washKey, string solidKey,
                            string textThemeKey = "SdPillText", string pillThemeKey = "SdPill") => new()
        {
            Theme = TabTheme(pillThemeKey),
            Background = Brush(washKey),
            Child = new TextBlock { Text = text, Theme = TabTheme(textThemeKey), Foreground = Brush(solidKey) },
        };

        /// <summary>Duration / reward cell. MinWidth is what turns them into columns.</summary>
        private TextBlock Meta(string text, double minWidth) =>
            new() { Text = text, MinWidth = minWidth, Theme = TabTheme("SdRackMeta") };

        // ---- resource lookup ------------------------------------------------------

        /// <summary>
        /// The twin of MainWindow.TryFindTabStyle: this page's card vocabulary lives in the view's
        /// OWN dictionary, not in Theme/, so it is reached from the view rather than from above.
        /// </summary>
        private ControlTheme? TabTheme(string key) =>
            Resources.TryGetResource(key, null, out var value) ? value as ControlTheme : null;

        /// <summary>
        /// The provenance and difficulty families live in Theme/Brushes.xaml, so these come from
        /// the APP dictionary the way SetResourceReference reaches them on WPF.
        ///
        /// Application, not <c>this</c>: resource lookup on a StyledElement walks its logical
        /// parents, and these are built from the constructor, before the view is attached to a
        /// tree - so `this.TryFindResource` finds nothing and every pill, badge and difficulty
        /// stripe renders with a null brush, which draws as invisible rather than as an error.
        /// </summary>
        private static IBrush? Brush(string key) =>
            Application.Current is { } app && app.TryFindResource(key, out var value) ? value as IBrush : null;
    }
}
