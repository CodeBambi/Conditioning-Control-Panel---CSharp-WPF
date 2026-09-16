using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/PresetsTabView.xaml.cs.
    ///
    /// The WPF code-behind holds the full preset/session store and feature wiring. This head keeps
    /// those head-owned dependencies out of the view; the built-in Core session rack is the one
    /// honest read-only slice restored here, including pointer and keyboard selection.
    ///
    /// ponytail: needs MainWindow (preset CRUD, SessionManager, JustDropOrdersService, and the
    /// tab FX clock), wired when those services move to Core. The remaining wiring points, all
    /// named in the XAML, are:
    ///   BtnCreateSession / BtnSessionHistory / BtnStartSession / BtnRevealSpoilers /
    ///   BtnLoadPreset / BtnSaveOverPreset / BtnDeletePreset / BtnExportPreset / BtnSharePreset /
    ///   BtnExportSession / BtnSelectCornerGif / ChkCornerGifEnabled / RbCornerTL..BR /
    ///   SliderCornerGifSize + SliderCornerGifOpacity / CmbRackSort.SelectionChanged /
    ///   TxtRackSearch.TextChanged / the "+ New" preset chip / SessionDropZone (catalogue) /
    ///   preset chip clicks and IsVisibleChanged -> OnPresetsTabVisibilityChanged (the card-sheen
    ///   clock, started on show, dropped on hide).
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
            // InitializeComponent, not AvaloniaXamlLoader.Load: only the generated one assigns the
            // x:Name fields, and Load leaves every one of them permanently null - a silent no-op
            // that compiles, renders and reviews clean.
            InitializeComponent();
            TxtDetailTitle.Text = Loc.Get("label_select_a_preset");
            TxtDetailSubtitle.Text = Loc.Get("label_click_on_a_preset_or_session_to_see_details");
            TxtSessionDuration.Text = Loc.Get("label_30_minutes");
            TxtSessionXP.Text = Loc.Get("label_50_xp");
            TxtSessionDifficulty.Text = Loc.Get("label_easy_2");
            SeedPlaceholders();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
            RefreshLocalizedDetails();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnLanguageChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (VisualRoot is null) return;
                RefreshLocalizedDetails();
            });

        private void RefreshLocalizedDetails()
        {
            RefreshLocalizedRack();

            if (_selectedSession is Session selected)
            {
                SelectSession(selected);
                return;
            }

            TxtDetailTitle.Text = Loc.Get("label_select_a_preset");
            TxtDetailSubtitle.Text = Loc.Get("label_click_on_a_preset_or_session_to_see_details");
            TxtSessionDuration.Text = Loc.Get("label_30_minutes");
            TxtSessionXP.Text = Loc.Get("label_50_xp");
            TxtSessionDifficulty.Text = Loc.Get("label_easy_2");
        }

        /// <summary>
        /// Refreshes the code-built catalogue in place. Language changes must not rebuild rows:
        /// their Session tags, focused row and either ScrollViewer's offset belong to the live view,
        /// not to a newly loaded catalogue.
        /// </summary>
        private void RefreshLocalizedRack()
        {
            foreach (var row in SessionRackPanel.Children.OfType<Border>())
            {
                if (row.Tag is not Session session || row.Child is not Grid grid) continue;

                foreach (var child in grid.Children)
                {
                    switch (Grid.GetColumn(child))
                    {
                        case 2 when child is TextBlock title:
                            title.Text = SessionName(session);
                            break;
                        case 3 when child is TextBlock description:
                        {
                            var blurb = SessionDescription(session);
                            description.Text = string.IsNullOrWhiteSpace(blurb)
                                ? Loc.Get("label_custom_session")
                                : blurb.Split('\n')[0].Trim();
                            ToolTip.SetTip(description, blurb);
                            break;
                        }
                        case 4 when child is Border difficulty:
                            SetPillText(difficulty, session.GetDifficultyText());
                            break;
                        case 5 when child is TextBlock duration:
                            duration.Text = Loc.GetF("rack_duration", session.DurationMinutes);
                            break;
                        case 6 when child is TextBlock reward:
                            reward.Text = Loc.GetF("rack_xp", session.BonusXP);
                            break;
                        case 7 when child is StackPanel badges:
                            if (badges.Children.OfType<Border>().FirstOrDefault() is Border source)
                                SetPillText(source, Loc.Get(RackSourceKeys(session.Source).labelKey));
                            break;
                        case 8 when child is StackPanel actions:
                            RefreshRowActionTooltips(actions);
                            break;
                    }
                }
            }

            for (var i = 0; i < RackSourceChips.Children.Count; i++)
            {
                if (RackSourceChips.Children[i] is not ToggleButton chip) continue;
                var key = chip.Tag as string ?? "all";
                var count = key switch
                {
                    "builtin" => _availableSessions.Count(session => session.Source == SessionSource.BuiltIn),
                    "yours" => _availableSessions.Count(session => session.Source == SessionSource.Custom),
                    "catalogue" => _availableSessions.Count(session => session.Source == SessionSource.Imported),
                    _ => _availableSessions.Count
                };
                SetTextContent(chip, $"{Loc.Get(key switch
                {
                    "builtin" => "rack_source_builtin",
                    "yours" => "rack_source_yours",
                    "catalogue" => "rack_source_catalogue",
                    _ => "rack_source_all"
                })}  {count}");
            }

            var difficulties = new[]
            {
                SessionDifficulty.Easy,
                SessionDifficulty.Medium,
                SessionDifficulty.Hard,
                SessionDifficulty.Extreme
            };
            for (var i = 0; i < Math.Min(difficulties.Length, RackDifficultyChips.Children.Count); i++)
                if (RackDifficultyChips.Children[i] is ToggleButton dot)
                    ToolTip.SetTip(dot, Loc.Get($"rack_diff_{difficulties[i].ToString().ToLowerInvariant()}"));

            TxtRackCount.Text = Loc.GetF("rack_count_all", _availableSessions.Count);
        }

        private static void SetTextContent(ToggleButton control, string text)
        {
            if (control.Content is TextBlock block)
                block.Text = text;
            else
                control.Content = new TextBlock { Text = text };
        }

        private static void SetPillText(Border pill, string text)
        {
            if (pill.Child is TextBlock block)
                block.Text = text;
        }

        private static void RefreshRowActionTooltips(StackPanel actions)
        {
            var keys = new[] { "tooltip_edit_session", "tooltip_export_session" };
            var buttons = actions.Children.OfType<Button>().ToArray();
            for (var i = 0; i < Math.Min(keys.Length, buttons.Length); i++)
                ToolTip.SetTip(buttons[i], Loc.Get(keys[i]));
        }

        private IReadOnlyList<Session> _availableSessions =
            Session.GetAllSessions().Where(session => session.IsAvailable).ToArray();
        private Session? _selectedSession;

        /// <summary>Replaces the offline built-in source with an already-loaded manager. Loading
        /// stays in App's desktop composition so constructing a view for render/nav never touches
        /// the user's session folders.</summary>
        internal void UseSessionManager(SessionManager manager)
        {
            ArgumentNullException.ThrowIfNull(manager);
            _availableSessions = manager.AllSessions.Where(session => session.IsAvailable).ToArray();
            _selectedSession = null;
            RackSourceChips.Children.Clear();
            RackDifficultyChips.Children.Clear();
            SessionRackPanel.Children.Clear();
            SeedRackToolbar();
            SeedSessionRack();
            RefreshLocalizedDetails();
        }

        // ---- placeholder furniture + Core-backed session rack --------------------
        //
        // The preset rail, toolbar chrome and Takeaway strip remain render furniture until their
        // own Core stores move. The session rack is different: the built-in catalogue already
        // lives in Core, so it must not keep presenting sample rows.

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
            int at = 0;
            PresetCardsPanel.Children.Insert(at++, PresetChip("Morning Drift", "⚡🌀", isDefault: true, selected: false));
            PresetCardsPanel.Children.Insert(at++, PresetChip("Deep Soak", "⚡🎬💭🌀", isDefault: false, selected: true));
            PresetCardsPanel.Children.Insert(at, PresetChip("Quiet Hours", "💭🔒", isDefault: false, selected: false));
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
            // Counts come from the same available Core catalogue as the rows; unavailable
            // placeholders must never make the rack claim that they can be selected.
            var builtIn = _availableSessions.Count(session => session.Source == SessionSource.BuiltIn);
            var custom = _availableSessions.Count(session => session.Source == SessionSource.Custom);
            var imported = _availableSessions.Count(session => session.Source == SessionSource.Imported);

            RackSourceChips.Children.Add(SourceChip("rack_source_all", _availableSessions.Count, "all", isOn: true));
            RackSourceChips.Children.Add(SourceChip("rack_source_builtin", builtIn, "builtin", isOn: false));
            RackSourceChips.Children.Add(SourceChip("rack_source_yours", custom, "yours", isOn: false));
            RackSourceChips.Children.Add(SourceChip("rack_source_catalogue", imported, "catalogue", isOn: false));

            RackDifficultyChips.Children.Add(Dot("SessionDiffEasyBrush", Loc.Get("rack_diff_easy"), on: true));
            RackDifficultyChips.Children.Add(Dot("SessionDiffMediumBrush", Loc.Get("rack_diff_medium"), on: true));
            RackDifficultyChips.Children.Add(Dot("SessionDiffHardBrush", Loc.Get("rack_diff_hard"), on: true));
            RackDifficultyChips.Children.Add(Dot("SessionDiffExtremeBrush", Loc.Get("rack_diff_extreme"), on: false));

            TxtRackCount.Text = Loc.GetF("rack_count_all", _availableSessions.Count);
        }

        private ToggleButton SourceChip(string labelKey, int count, string tag, bool isOn) => new()
        {
            Theme = TabTheme("SdRackChip"),
            Tag = tag,
            IsChecked = isOn,
            IsEnabled = false,
            Opacity = 0.5,
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
                IsEnabled = false,
                Opacity = 0.5,
                Content = new TextBlock { Text = "●" },
                Foreground = Brush(solidKey),
            };
            ToolTip.SetTip(dot, tip);
            return dot;
        }

        /// <summary>Build the selectable rows from the available Core built-ins only.</summary>
        private void SeedSessionRack()
        {
            foreach (var session in _availableSessions)
                SessionRackPanel.Children.Add(RackRow(session));
        }

        private Border RackRow(Session session)
        {
            var (diffSolid, diffWash) = session.Difficulty switch
            {
                SessionDifficulty.Medium => ("SessionDiffMediumBrush", "SessionDiffMediumWashBrush"),
                SessionDifficulty.Hard => ("SessionDiffHardBrush", "SessionDiffHardWashBrush"),
                SessionDifficulty.Extreme => ("SessionDiffExtremeBrush", "SessionDiffExtremeWashBrush"),
                _ => ("SessionDiffEasyBrush", "SessionDiffEasyWashBrush"),
            };
            var (srcKey, srcSolid, srcWash) = RackSourceKeys(session.Source);
            var icon = string.IsNullOrWhiteSpace(session.Icon) ? "🎬" : session.Icon;
            var name = SessionName(session);
            var blurb = SessionDescription(session);
            var difficulty = session.GetDifficultyText();

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

            var rowBlurb = string.IsNullOrWhiteSpace(blurb) ? Loc.Get("label_custom_session") : blurb.Split('\n')[0].Trim();
            var desc = new TextBlock { Text = rowBlurb, Theme = TabTheme("SdRowBlurb") };
            ToolTip.SetTip(desc, blurb);
            Grid.SetColumn(desc, 3);
            grid.Children.Add(desc);

            var diffPill = Pill(difficulty, diffWash, diffSolid);
            Grid.SetColumn(diffPill, 4);
            grid.Children.Add(diffPill);

            var duration = Meta(Loc.GetF("rack_duration", session.DurationMinutes), 56);
            Grid.SetColumn(duration, 5);
            grid.Children.Add(duration);

            var reward = Meta(Loc.GetF("rack_xp", session.BonusXP), 66);
            Grid.SetColumn(reward, 6);
            grid.Children.Add(reward);

            var badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            badges.Children.Add(Pill(Loc.Get(srcKey), srcWash, srcSolid, "SdRackBadgeText"));
            Grid.SetColumn(badges, 7);
            grid.Children.Add(badges);

            // Session CRUD/import is not on this head yet. Keep the existing action geometry, but
            // do not offer controls that would silently do nothing in this read-only slice.
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(RowAction("✎", Loc.Get("tooltip_edit_session"), danger: false));
            actions.Children.Add(RowAction("↗", Loc.Get("tooltip_export_session"), danger: false));
            // Pad out to four buttons' worth (28px wide, 3px margin) so the existing row columns
            // keep their layout if custom rows are restored later.
            actions.Margin = new Thickness(8 + (4 - actions.Children.Count) * 31, 0, 4, 0);
            Grid.SetColumn(actions, 8);
            grid.Children.Add(actions);

            var row = new Border
            {
                Name = $"SessionRow_{session.Id}",
                Tag = session,
                Theme = TabTheme(_selectedSession?.Id == session.Id ? "SdSessionRowSelected" : "SdSessionRow"),
                Focusable = true,
                IsTabStop = true,
                Child = grid,
            };
            row.PointerPressed += SessionRow_PointerPressed;
            row.KeyDown += SessionRow_KeyDown;
            return row;
        }

        private Button RowAction(string glyph, string tip, bool danger)
        {
            var btn = new Button
            {
                Theme = TabTheme(danger ? "SdRowActionDanger" : "SdRowAction"),
                Content = new TextBlock { Text = glyph },
                IsEnabled = false,
            };
            ToolTip.SetTip(btn, tip);
            return btn;
        }

        private static (string labelKey, string solidKey, string washKey) RackSourceKeys(SessionSource source) =>
            source switch
            {
                SessionSource.Custom => ("rack_src_yours", "SessionSrcCustomBrush", "SessionSrcCustomWashBrush"),
                SessionSource.Imported => ("rack_src_catalogue", "SessionSrcImportedBrush", "SessionSrcImportedWashBrush"),
                _ => ("rack_src_builtin", "SessionSrcBuiltInBrush", "SessionSrcBuiltInWashBrush")
            };

        private static string SessionName(Session session) =>
            LocalizedOrFallback(session.LocalizedName, $"session_{session.Id}_name", session.GetModeAwareName());

        private static string SessionDescription(Session session) =>
            LocalizedOrFallback(session.LocalizedDescription, $"session_{session.Id}_desc", session.GetModeAwareDescription());

        private static string LocalizedOrFallback(string localized, string key, string fallback) =>
            string.IsNullOrWhiteSpace(localized) || string.Equals(localized, key, StringComparison.Ordinal)
                ? fallback
                : CoreMods.MakeModAware(localized);

        private void SessionRow_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border row || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                return;
            if (row.Tag is not Session session || !session.IsAvailable) return;

            row.Focus();
            SelectSession(session);
            e.Handled = true;
        }

        private void SessionRow_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Space) ||
                sender is not Border { Tag: Session session } || !session.IsAvailable)
                return;

            SelectSession(session);
            e.Handled = true;
        }

        private void SelectSession(Session session)
        {
            if (!session.IsAvailable) return;

            _selectedSession = session;
            PresetDetailScroller.IsVisible = false;
            PresetButtonsPanel.IsVisible = false;
            SessionDetailScroller.IsVisible = true;
            SessionButtonsPanel.IsVisible = false;
            BtnStartSession.IsEnabled = false;
            BtnExportSession.IsEnabled = false;
            SessionSpoilerPanel.IsVisible = false;
            CornerGifOptionPanel.IsVisible = false;

            TxtDetailTitle.Text = $"{(string.IsNullOrWhiteSpace(session.Icon) ? "🎬" : session.Icon)} {SessionName(session)}";
            TxtDetailSubtitle.Text = session.GenerateFeatureDescription();
            TxtSessionDuration.Text = Loc.GetF("rack_duration", session.DurationMinutes);
            TxtSessionXP.Text = Loc.GetF("rack_xp", session.BonusXP);
            TxtSessionDifficulty.Text = session.GetDifficultyText();
            TxtSessionDescription.Text = SessionDescription(session);

            RefreshSessionRackSelection();
        }

        private void RefreshSessionRackSelection()
        {
            var selectedStyle = TabTheme("SdSessionRowSelected");
            var normalStyle = TabTheme("SdSessionRow");
            if (selectedStyle is null || normalStyle is null) return;

            foreach (var row in SessionRackPanel.Children.OfType<Border>())
            {
                var selected = row.Tag is Session session && session.Id == _selectedSession?.Id;
                row.Theme = selected ? selectedStyle : normalStyle;
            }
        }

        /// <summary>Three pinned receipts, the "+n more" toggle, the shop door, and three tray
        /// rows behind it. The tray host stays collapsed, as PaintTakeawayShelf leaves it.</summary>
        private void SeedTakeaway()
        {
            // PaintTakeawayShelf pins up to three receipts, then the "+n more" toggle, then
            // the door. ONE receipt here: the strip never wraps and never scrolls sideways,
            // and at the render proof's 1100px the fill is ~330px, so a second receipt would
            // push the door off the clip and leave its ControlTheme unproven. With three or
            // fewer orders there is no overflow, so the toggle (SdTakeawayChipAccent, a
            // two-setter Border variant of the chip below it) is correctly absent too.
            TakeawayShelf.Children.Add(TakeawayChip("Slow Sink", 30, "AUG 09"));
            TakeawayShelf.Children.Add(DoorChip());

            // The tray renders EVERY order the drawer returned, not just the pinned ones.
            TakeawayTray.Children.Add(TrayRow("Velvet Hour", 45, "AUG 12", Loc.Get("takeaway_today")));
            TakeawayTray.Children.Add(TrayRow("Slow Sink", 30, "AUG 09", Loc.GetF("takeaway_days_ago", 3)));
            TakeawayTray.Children.Add(TrayRow("Static Bloom", 20, "JUL 28", Loc.GetF("takeaway_days_ago", 15)));

            TxtTakeawayCount.Text = Loc.GetF("sd_takeaway_kept", 3);
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
