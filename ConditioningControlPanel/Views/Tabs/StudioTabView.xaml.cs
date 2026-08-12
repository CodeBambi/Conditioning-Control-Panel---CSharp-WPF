using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Views.Tabs
{
    /// <summary>
    /// The Studio door (tab key <c>studio</c>): a master-detail effects rack.
    ///
    /// <para><b>This file is the host only.</b> It owns the rack list, the detail pane and
    /// <see cref="FocusRackEntry"/>. It owns no feature logic whatsoever: every module is an
    /// existing <c>Features/*FeatureControl</c> (or a new panel under
    /// <c>Views/Controls/Studio/</c> built by another agent), and each one still talks to the
    /// MainWindow partial that always owned it.</para>
    ///
    /// <para><b>Passthroughs go in partial files, not here.</b> Each module agent contributes
    /// <c>Views/Tabs/StudioTabView.&lt;Area&gt;.cs</c> — a partial of this class holding nothing
    /// but <c>internal &lt;Type&gt; Name =&gt; PanelX.Name;</c> style compat properties for the
    /// x:Names its MainWindow partials read and write. Same convention as
    /// <see cref="AppSettingsTabView"/> in Phase 2, for the same reason: parallel agents must
    /// never share one file.</para>
    ///
    /// <para><b>Three contracts this host is responsible for.</b>
    /// (1) <c>NotifyFeatureOpened</c> fires with the SAME locale-independent key the
    /// FeaturePopupWindow path fired (14 voiced rules per built-in mod match on it), whenever a
    /// module's panel is shown — see <see cref="StudioRackEntry.BarkFeature"/>.
    /// (2) <see cref="HostedFeaturePanels"/> exists so
    /// <c>MainWindow.RefreshSessionFeatureLock</c> can paint the session feature lock onto every
    /// hosted panel: three panels (Flash, Spiral, PinkFilter) leave their master
    /// <c>ChkEnable</c> UNMARKED and rely on <c>ApplySessionLockToFeaturePopup</c>'s
    /// belt-and-braces <c>FindName("ChkEnable")</c>, so an attached-property sweep alone would
    /// leave three master toggles live mid-session.
    /// (3) <see cref="HapticsPanel"/> is the new home of <c>MainWindow.HapticsTab</c>.</para>
    ///
    /// <para><b>Quiet surface.</b> No ambient loop is registered for this view and none may be
    /// (PLAN §2.7). The rack's selection visual is trigger-driven; the only clock is a 120ms
    /// detail crossfade, gated on <see cref="MotionFx.AllowTransitions"/> and self-terminating,
    /// so there is nothing for the motion kill-switch or the ShowTab teardown to reach.</para>
    /// </summary>
    public partial class StudioTabView : UserControl
    {
        /// <summary>What <c>ShowTab("studio")</c> lands on before the user has picked anything.</summary>
        internal const string DefaultRackKey = "flash";

        private const double DetailFadeMs = 120;

        // =====================================================================================
        //  the rack table — one row per module, the single source of truth
        // =====================================================================================

        private sealed class StudioRackEntry
        {
            /// <summary>Stable rack key. API for <see cref="FocusRackEntry"/>; never renamed.</summary>
            public string Key = string.Empty;
            public string Glyph = string.Empty;

            /// <summary>
            /// Filename under <c>Resources/features/</c> for this module's feature art, or null
            /// for the three modules that have never had any (Visuals, Scheduler, Ramp).
            /// <para>CASE MATTERS: pack URIs into a resource assembly are matched
            /// case-insensitively at runtime today, but the on-disk names are genuinely mixed
            /// (<c>flash.png</c> vs <c>Pink_filter.png</c> vs <c>Mind_Wipers.png</c>) and these
            /// strings are verbatim copies of them. Keep it that way.</para>
            /// </summary>
            public string? Art;
            /// <summary>English fallback fed to <c>MainWindow.ModAwareLabel</c>.</summary>
            public string English = string.Empty;
            public string LocKey = string.Empty;

            /// <summary>The element toggled by Visibility (a ScrollViewer wrapper, or the panel itself).</summary>
            public UIElement? Host;

            /// <summary>The raw UserControl, for the session-lock sweep. Null = not swept here.</summary>
            public UserControl? Panel;

            /// <summary>
            /// <c>App.Bark.NotifyFeatureOpened</c> key. Deliberately explicit rather than derived
            /// from the type name: the popup path derived it (type name minus "FeatureControl"),
            /// but Scheduler and Ramp both have to keep firing the popup's single
            /// <c>"SchedulerRamp"</c> key, and the new panels are not named *FeatureControl.
            /// Null = this module fires nothing (Haptics announces itself through
            /// NotifyTabNavigated("haptics") instead).
            /// </summary>
            public string? BarkFeature;

            /// <summary>Live enabled-state for the row's dot, or null for "this module has no
            /// single on/off and must not pretend to" (Visuals had no dashboard dot either).</summary>
            public Func<bool?>? Dot;

            /// <summary>True when the panel draws its own page header, so the shared detail
            /// header must hide rather than double up (Haptics).</summary>
            public bool OwnHeader;

            // Built by BuildRack.
            public RadioButton? Row;
            public TextBlock? Label;
            public Ellipse? DotShape;

            /// <summary>The resting visual: art/emoji chip, caption, state dot. Hidden while this
            /// row is the checked one AND it has art (<see cref="Tile"/> takes over).</summary>
            public Grid? Strip;

            /// <summary>The checked visual for a module WITH art: a 56px full-bleed art tile.
            /// Null for the three art-less modules, whose checked state is the strip as before.
            /// Both states are built once and swapped by Visibility - see
            /// <see cref="ApplyRowState"/> for why this is not a template trigger.</summary>
            public Grid? Tile;
            public TextBlock? TileLabel;
            public Ellipse? TileDot;
        }

        private readonly List<StudioRackEntry> _entries = new();

        /// <summary>Rack list contents in visual order: <see cref="StudioRackEntry"/> rows and
        /// bare loc-key strings for the group captions between them.</summary>
        private readonly List<object> _layout = new();

        private string _selected = DefaultRackKey;
        private bool _settingsHooked;

        /// <summary>The AppSettings instance the dot listener is attached to. See
        /// <see cref="BindDotListener"/> - a cloud restore swaps the instance out.</summary>
        private Models.AppSettings? _hookedSettings;

        /// <summary>The rack key currently showing. Survives leaving and re-entering the door.</summary>
        internal string SelectedRackKey => _selected;

        public StudioTabView()
        {
            InitializeComponent();
            BuildRack();
            // Land on the default without announcing it: nothing is on screen yet, and the
            // FeatureOpened bark belongs to the moment the user can actually see the panel.
            SelectEntry(DefaultRackKey, announce: false, animate: false);
            Loaded += OnLoaded;
        }

        // =====================================================================================
        //  public surface — what MainWindow calls
        // =====================================================================================

        /// <summary>
        /// Selects a rack entry and shows its panel. An unknown key is a quiet no-op rather than
        /// a throw: every caller (ShowTab's <c>haptics</c> re-route, the Home mosaic tiles from
        /// Phase 3, the future Ctrl+K palette) is a navigation, and none of them should be able
        /// to break one.
        /// </summary>
        internal void FocusRackEntry(string? rackKey)
        {
            try { SelectEntry(rackKey, announce: true, animate: true); }
            catch (Exception ex) { App.Logger?.Debug("FocusRackEntry({Key}): {E}", rackKey, ex.Message); }
        }

        /// <summary>
        /// Same selection, SILENT: no bark, no crossfade. For callers that pick the module before
        /// the rack is on screen (the tutorial's Studio steps, which run on
        /// <c>TutorialStep.OnBeforeTab</c>) - <see cref="OnTabShown"/> then announces the incoming
        /// selection exactly once, and it is the right one. Announcing here as well would say the
        /// same thing twice; announcing only here would say it while the page is still hidden.
        /// </summary>
        internal void PreselectRackEntry(string? rackKey)
        {
            try { SelectEntry(rackKey, announce: false, animate: false); }
            catch (Exception ex) { App.Logger?.Debug("PreselectRackEntry({Key}): {E}", rackKey, ex.Message); }
        }

        /// <summary>
        /// Per-open refresh, called from ShowTab's <c>studio</c> case. Repaints the mod-aware row
        /// captions and the state dots, re-asserts the current selection's visibility (ShowTab's
        /// teardown collapses the Haptics panel on every navigation) and re-announces the visible
        /// module, which is what the popup did on every open.
        /// </summary>
        internal void OnTabShown()
        {
            try
            {
                RefreshRackLabels();
                RefreshDots();
                SelectEntry(_selected, announce: true, animate: false);
            }
            catch (Exception ex) { App.Logger?.Debug("StudioTabView.OnTabShown: {E}", ex.Message); }
        }

        /// <summary>
        /// Mod-switch repaint: captions, dots and the detail pane's big header. Deliberately does
        /// NOT re-announce the visible module the way <see cref="OnTabShown"/> does — a mid-session
        /// mod switch on a different tab must not fire a spurious feature-opened bark.
        /// </summary>
        internal void RepaintModAwareChrome()
        {
            try { RefreshRackLabels(); RefreshDots(); RefreshDetailHeader(); }
            catch (Exception ex) { App.Logger?.Debug("StudioTabView.RepaintModAwareChrome: {E}", ex.Message); }
        }

        /// <summary>
        /// Every raw feature panel the rack hosts, for
        /// <c>MainWindow.ApplySessionLockToFeaturePopup</c>. Haptics is excluded on purpose: it
        /// is already covered by name in <c>ApplySessionLockToTabs</c>, and its Content is a
        /// ScrollViewer rather than a Panel so it could never take the lock banner anyway.
        /// </summary>
        internal IReadOnlyList<UserControl> HostedFeaturePanels =>
            _entries.Select(e => e.Panel).Where(p => p != null).Select(p => p!).ToList();

        /// <summary>
        /// The Haptics page, re-hosted as a rack module in Phase 4. <c>MainWindow.HapticsTab</c>
        /// forwards here, so all ~71 <c>HapticsTab.&lt;x:Name&gt;</c> dereferences across the
        /// MainWindow partials, the two <c>features/vibe.png</c> repaint rows, the
        /// IsVisibleChanged live-status hook and the SessionLock sweep keep working verbatim.
        /// </summary>
        internal HapticsTabView HapticsPanel => PanelHaptics;

        // =====================================================================================
        //  rack construction
        // =====================================================================================

        /// <summary>
        /// Order is the Phase-4 contract's, with group captions inserted so nothing moves.
        ///
        /// <para><b>The art column (Velvet Kit 2).</b> Every entry names its file under
        /// <c>Resources/features/</c> - the same square tile the dashboard mosaic and the
        /// FeaturePopupWindow chrome have always used, finally reaching the rack. Three modules
        /// pass null because no such art has ever existed for them (Visuals is a bundle of
        /// unrelated toggles, Scheduler and Ramp are timing, not effects); those rows keep the
        /// emoji chip on a hue-wash, and their checked state is the plain strip.</para>
        ///
        /// <para><b>Every entry now sets OwnHeader.</b> Velvet Kit 2 gives every feature page its
        /// own 72px in-page hero, so the shared <c>DetailHeader</c> bar would name the module a
        /// second time directly above it. The mechanism is deliberately still per-entry rather
        /// than a hard-collapse in <see cref="RefreshDetailHeader"/>: the flag keeps meaning
        /// exactly what it always meant ("this panel draws its own page header"), it is now simply
        /// true of all of them, and a page that ever loses its hero passes <c>ownHeader: false</c>
        /// and gets the shared bar back with nothing else to change.</para>
        /// </summary>
        private void BuildRack()
        {
            _layout.Add("st4_studio_group_effects");
            Add("flash", "⚡", "flash.png", "Flash Images", "section_flash_images", HostFlash, PanelFlash, "Flash",
                () => App.Settings?.Current?.FlashEnabled);
            Add("video", "🎬", "mandatory_videos.png", "Mandatory Video", "section_mandatory_video", HostVideo, PanelVideo, "Video",
                () => App.Settings?.Current?.MandatoryVideosEnabled);
            Add("subliminal", "💭", "subliminal.png", "Subliminals", "section_subliminals_2", HostSubliminal, PanelSubliminal, "Subliminal",
                () => App.Settings?.Current?.SubliminalEnabled);
            Add("spiral", "🌀", "spiral_overlay.png", "Spiral Overlay", "label_spiral_overlay", HostSpiral, PanelSpiral, "Spiral",
                () => App.Settings?.Current?.SpiralEnabled);
            Add("pinkfilter", "💗", "Pink_filter.png", "Pink Filter", "label_pink_filter", HostPinkFilter, PanelPinkFilter, "PinkFilter",
                () => App.Settings?.Current?.PinkFilterEnabled);
            // Visuals has no single master toggle - the dashboard card is deliberately neutral
            // too (MainWindow.Presets.cs:800). A dot that cannot be wired honestly is omitted.
            Add("visuals", "👁", null, "Visuals", "section_visuals", HostVisuals, PanelVisuals, "Visuals", null);

            _layout.Add("st4_studio_group_games");
            Add("bubbles", "🫧", "Bubble_pop.png", "Bubble Pop", "label_bubble_pop", HostBubblePop, PanelBubblePop, "BubblePop",
                () => App.Settings?.Current?.BubblesEnabled);
            Add("bubblecount", "🔢", "Bubble_count.png", "Bubble Count", "label_bubble_count", HostBubbleCount, PanelBubbleCount, "BubbleCount",
                () => App.Settings?.Current?.BubbleCountEnabled);
            Add("lockcard", "📐", "Phrase_Lock.png", "Lock Card", "label_lock_card", HostLockCard, PanelLockCard, "LockCard",
                () => App.Settings?.Current?.LockCardEnabled);
            Add("bouncingtext", "📺", "bouncing_text.png", "Bouncing Text", "label_bouncing_text", HostBouncingText, PanelBouncingText, "BouncingText",
                () => App.Settings?.Current?.BouncingTextEnabled);

            _layout.Add("st4_studio_group_immersion");
            Add("mindwipe", "🧠", "Mind_Wipers.png", "Mind Wipe", "label_mind_wipe", HostMindWipe, PanelMindWipe, "MindWipe",
                () => App.Settings?.Current?.MindWipeEnabled);
            // New in Phase 4 (G2 rescue). "BrainDrain" is a NEW feature_eq value; all three
            // built-in mods now carry a matching feat_braindrain FeatureOpened rule.
            Add("braindrain", "💧", "brain_drain.png", "Brain Drain", "section_brain_drain", HostBrainDrain, PanelBrainDrain, "BrainDrain",
                () => App.Settings?.Current?.BrainDrainEnabled);
            // Haptics: no FeatureOpened key. ShowTab("haptics") still fires
            // NotifyTabNavigated("haptics"), which is what its 3 rules per mod match on.
            // The dot reads a nested settings object with no INPC, so it refreshes on every
            // Studio show and on every selection rather than live.
            Add("haptics", "📳", "vibe.png", "Haptics", "tab_haptics", PanelHaptics, null, null,
                () => App.Settings?.Current?.Haptics?.Enabled);

            _layout.Add("st4_studio_group_timing");
            // Both fire the popup's single "SchedulerRamp" key so the existing rules keep firing.
            Add("scheduler", "📅", null, "Scheduler", "section_scheduler", HostScheduler, PanelScheduler, "SchedulerRamp",
                () => App.Settings?.Current?.SchedulerEnabled);
            Add("ramp", "📈", null, "Intensity Ramp", "section_intensity_ramp", HostRamp, PanelRamp, "SchedulerRamp",
                () => App.Settings?.Current?.IntensityRampEnabled);

            RenderRackRows();
            RefreshRackLabels();
            RefreshDots();

            void Add(string key, string glyph, string? art, string english, string locKey, UIElement? host,
                     UserControl? panel, string? bark, Func<bool?>? dot, bool ownHeader = true)
            {
                var entry = new StudioRackEntry
                {
                    Key = key,
                    Glyph = glyph,
                    Art = art,
                    English = english,
                    LocKey = locKey,
                    Host = host,
                    Panel = panel,
                    BarkFeature = bark,
                    Dot = dot,
                    OwnHeader = ownHeader,
                };
                _entries.Add(entry);
                _layout.Add(entry);
            }
        }

        private void RenderRackRows()
        {
            if (RackList == null) return;
            RackList.Children.Clear();

            foreach (var item in _layout)
            {
                if (item is string headerKey)
                {
                    // caption | hairline rule fading right - the rule is what keeps the group
                    // breaks legible now that the rows carry icon chips of their own.
                    var capRow = new Grid { Margin = new Thickness(13, 9, 10, 4) };
                    capRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    capRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var caption = new TextBlock
                    {
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Opacity = 0.85,
                        Foreground = (Brush?)TryFindResource("TextDimBrush") ?? Brushes.Gray,
                    };
                    // BOUND, not assigned. RenderRackRows runs exactly once (BuildRack, from the
                    // ctor) and the group captions are not _entries, so RefreshRackLabels never
                    // revisits them - a language switch left them frozen in the old language while
                    // the page title beside them changed. This is the same binding {loc:Str}
                    // produces (LocExtension.cs), so they now track SetLanguage's "Item[]"
                    // notification with no repaint path at all.
                    caption.SetBinding(TextBlock.TextProperty,
                        new System.Windows.Data.Binding($"[{headerKey}]")
                        {
                            Source = LocalizationManager.Instance,
                            Mode = System.Windows.Data.BindingMode.OneWay,
                        });
                    capRow.Children.Add(caption);

                    var rule = new Border
                    {
                        Height = 1,
                        Margin = new Thickness(8, 1, 2, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = new LinearGradientBrush(
                            Color.FromArgb(0x55, 0xFF, 0x69, 0xB4),
                            Color.FromArgb(0x00, 0xFF, 0x69, 0xB4),
                            0),
                    };
                    Grid.SetColumn(rule, 1);
                    capRow.Children.Add(rule);

                    RackList.Children.Add(capRow);
                    continue;
                }

                if (item is not StudioRackEntry e) continue;

                // Two states, both built now, swapped by Visibility. See ApplyRowState.
                var content = new Grid();

                e.Strip = BuildRestingStrip(e);
                content.Children.Add(e.Strip);

                e.Tile = BuildArtTile(e);              // null when this module has no art
                if (e.Tile != null) content.Children.Add(e.Tile);

                e.Row = new RadioButton
                {
                    Style = (Style?)TryFindResource("RackEntryStyle"),
                    Tag = e.Key,
                    Content = content,
                };
                e.Row.Click += RackEntry_Click;
                // Instant, storyboard-free state swap. Wired to Checked/Unchecked rather than
                // driven from SelectEntry so the swap can never drift out of step with
                // IsChecked - the RadioButton group also unchecks the outgoing row by itself.
                e.Row.Checked += (_, __) => ApplyRowState(e);
                e.Row.Unchecked += (_, __) => ApplyRowState(e);
                ApplyRowState(e);

                RackList.Children.Add(e.Row);
            }
        }

        // =====================================================================================
        //  row visuals — resting strip, active art tile
        // =====================================================================================

        /// <summary>Height of a checked row that has art. The RESTING height (38) deliberately
        /// lives only in RackEntryStyle's Height setter, so there is one owner of it.</summary>
        private const double ActiveTileHeight = 56;

        /// <summary>
        /// The resting row: art (or emoji) chip | caption | state dot.
        ///
        /// <para>The chip is 28px now rather than 20. Raw emoji floating beside text is the
        /// stock-toolkit look; contained AND filled with the module's own feature art, every row
        /// takes the same visual weight and the column reads as a designed rack rather than a
        /// bullet list. The three art-less modules keep their emoji, on a hue-wash that matches
        /// the art rows' warmth so they do not read as broken.</para>
        /// </summary>
        private Grid BuildRestingStrip(StudioRackEntry e)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chip = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                SnapsToDevicePixels = true,
            };
            chip.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush");
            RenderOptions.SetBitmapScalingMode(chip, BitmapScalingMode.HighQuality);

            var chipArt = LoadFeatureArt(e.Art, 56);
            if (chipArt != null)
            {
                var brush = new ImageBrush(chipArt)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                };
                brush.Freeze();
                chip.Background = brush;
            }
            else
            {
                // No art on disk for this module (or it failed to decode): emoji on a hue-wash.
                chip.Background = ChipHueWashBrush;
                chip.Child = new EmojiTextBlock
                {
                    Text = e.Glyph,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            Grid.SetColumn(chip, 0);
            grid.Children.Add(chip);

            e.Label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(e.Label, 1);
            grid.Children.Add(e.Label);

            if (e.Dot != null)
            {
                e.DotShape = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 2, 0),
                };
                Grid.SetColumn(e.DotShape, 2);
                grid.Children.Add(e.DotShape);
            }

            return grid;
        }

        /// <summary>
        /// The checked visual for a module WITH art: a 56px full-bleed tile. The art is
        /// right-anchored and masked away toward the left, with a scrim gradient over it, so the
        /// caption sits on flat panel colour and the art is the thing your eye lands on.
        ///
        /// <para>Returns null when the module has no art - those rows keep the strip when checked
        /// and only get the RackEntryStyle trigger fill, exactly as before.</para>
        ///
        /// <para>NEGATIVE MARGIN IS DELIBERATE. RackEntryStyle's ContentPresenter is inset
        /// 12,0,10,0; "full-bleed" means undoing that. The left inset is undone to 3 rather than
        /// 0 so the template's 3px pink RowBar still shows beside the tile instead of being
        /// painted over by it (the ContentPresenter renders ON TOP of RowBar), which is also why
        /// the left corners are 3 and the right corners 8.</para>
        /// </summary>
        private Grid? BuildArtTile(StudioRackEntry e)
        {
            var art = LoadFeatureArt(e.Art, 256);
            if (art == null) return null;

            var corners = new CornerRadius(3, 8, 8, 3);

            // Height is EXPLICIT, not inherited from the row. RackEntryStyle's ContentPresenter
            // is VerticalAlignment=Center, so content is measured to its own desired size and
            // then centred - a stretch-height tile would collapse to the height of its caption.
            var tile = new Grid
            {
                Height = ActiveTileHeight,
                Margin = new Thickness(-9, 0, -10, 0),
                Visibility = Visibility.Collapsed,
            };

            var artBrush = new ImageBrush(art)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Right,
                AlignmentY = AlignmentY.Center,
            };
            artBrush.Freeze();

            // Painted as a Border BACKGROUND, not an Image child: a Border clips its own
            // background to CornerRadius, but does not clip child elements to it.
            var artLayer = new Border
            {
                CornerRadius = corners,
                Background = artBrush,
                OpacityMask = TileArtFadeMask,
            };
            RenderOptions.SetBitmapScalingMode(artLayer, BitmapScalingMode.HighQuality);
            tile.Children.Add(artLayer);

            tile.Children.Add(new Border
            {
                CornerRadius = corners,
                Background = TileScrimBrush,
            });

            e.TileLabel = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(12, 0, 26, 0),
            };
            tile.Children.Add(e.TileLabel);

            if (e.Dot != null)
            {
                e.TileDot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 11, 0),
                };
                tile.Children.Add(e.TileDot);
            }

            return tile;
        }

        /// <summary>
        /// Swaps a row between its resting strip and its 56px art tile, and grows/shrinks the row
        /// to match. Instant by design: the rack is a quiet surface (PLAN §2.7), so this is a
        /// Visibility flip and a Height write, with no storyboard for the motion kill-switch to
        /// have to reach.
        /// <para>Height is CLEARED rather than set back to 38 so RackEntryStyle's own Height
        /// setter takes the row again - one place owns the resting height.</para>
        /// </summary>
        private static void ApplyRowState(StudioRackEntry e)
        {
            if (e.Row == null || e.Tile == null) return;   // art-less rows: nothing to swap

            bool on = e.Row.IsChecked == true;
            e.Tile.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (e.Strip != null) e.Strip.Visibility = on ? Visibility.Collapsed : Visibility.Visible;

            if (on) e.Row.Height = ActiveTileHeight;
            else e.Row.ClearValue(FrameworkElement.HeightProperty);
        }

        /// <summary>
        /// Decodes a <c>Resources/features/</c> PNG at the size it will actually be drawn at.
        /// The source tiles are ~1024px square; handing WPF twelve of those undecimated for a
        /// 28px chip is the classic way to spend 50MB on a sidebar. Frozen so the brushes built
        /// from them are freezable too, and so nothing pins them to this thread.
        /// <para>Null in, null out - and a decode failure is null as well rather than a throw:
        /// a missing art file must cost the row its picture, not the whole rack.</para>
        /// </summary>
        private static BitmapImage? LoadFeatureArt(string? file, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri("pack://application:,,,/Resources/features/" + file, UriKind.Absolute);
                bmp.DecodePixelWidth = decodePixelWidth;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Studio rack art '{File}': {E}", file, ex.Message);
                return null;
            }
        }

        /// <summary>Hue-wash behind the emoji of an art-less module's chip: 135°, warm to pink.</summary>
        private static readonly LinearGradientBrush ChipHueWashBrush = Frozen(new LinearGradientBrush(
            Color.FromArgb(0x40, 0xFF, 0x7E, 0x6B),
            Color.FromArgb(0x1F, 0xFF, 0x69, 0xB4),
            new Point(0, 0), new Point(1, 1)));

        /// <summary>
        /// Fades the active tile's art out toward the left so it dissolves into the panel instead
        /// of ending on a hard edge under the caption. Alpha is all that matters in an
        /// OpacityMask; the black is arbitrary.
        /// </summary>
        private static readonly LinearGradientBrush TileArtFadeMask = Frozen(new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.00),
                new GradientStop(Color.FromArgb(0x8C, 0, 0, 0), 0.40),
                new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.75),
            },
            new Point(0, 0.5), new Point(1, 0.5)));

        /// <summary>
        /// Readability scrim over the tile art: near-solid panel colour under the caption,
        /// gone by 80% so the right-hand art stays clean.
        /// </summary>
        private static readonly LinearGradientBrush TileScrimBrush = Frozen(new LinearGradientBrush(
            new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x38), 0.00),
                new GradientStop(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x38), 0.22),
                new GradientStop(Color.FromArgb(0x00, 0x1E, 0x1E, 0x38), 0.80),
            },
            new Point(0, 0.5), new Point(1, 0.5)));

        private static T Frozen<T>(T brush) where T : Freezable
        {
            brush.Freeze();
            return brush;
        }

        // =====================================================================================
        //  selection
        // =====================================================================================

        private StudioRackEntry? EntryFor(string? key) =>
            key == null ? null
                        : _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        private void RackEntry_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string key)
                SelectEntry(key, announce: true, animate: true);
        }

        /// <summary>
        /// Shows exactly one module. Idempotent, and safe to call for the already-selected key —
        /// re-selecting deliberately re-announces, because opening a feature popup twice used to
        /// fire its bark twice too.
        /// </summary>
        private void SelectEntry(string? key, bool announce, bool animate)
        {
            var target = EntryFor(key);
            if (target == null) return;   // quiet no-op on an unknown key, by contract

            _selected = target.Key;

            foreach (var e in _entries)
            {
                bool on = ReferenceEquals(e, target);
                if (e.Row != null) e.Row.IsChecked = on;
                if (e.Host == null) continue;
                e.Host.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }

            RefreshDetailHeader();

            if (animate) FadeInDetail(target.Host);

            RefreshDots();

            if (announce) Announce(target);
        }

        /// <summary>
        /// Repaints the detail pane's big header for the current selection. Split out of
        /// <see cref="SelectEntry"/> so a mod switch can repaint it too: <c>LabelFor</c> routes
        /// through <c>MainWindow.ModAwareLabel</c>, so the title is mod-renamable and would
        /// otherwise keep the previous mod's feature name. Deliberately does not re-announce.
        ///
        /// <para><b>As of Velvet Kit 2 this collapses the header every time</b>, because every
        /// entry sets <see cref="StudioRackEntry.OwnHeader"/> - each feature page draws its own
        /// in-page hero now. The branch is left intact rather than hard-collapsed: it is the same
        /// one Haptics has always taken, it still reads as what it means, and it is what brings
        /// the shared bar back for any page that loses its hero. The icon and title are still
        /// written so that bar is correct the moment it is shown again.</para>
        /// </summary>
        private void RefreshDetailHeader()
        {
            var target = EntryFor(_selected);
            if (target == null) return;

            if (DetailHeader != null)
                DetailHeader.Visibility = target.OwnHeader ? Visibility.Collapsed : Visibility.Visible;
            if (TxtDetailIcon != null) TxtDetailIcon.Text = target.Glyph;
            if (TxtDetailTitle != null) TxtDetailTitle.Text = LabelFor(target);
        }

        /// <summary>
        /// The FeatureOpened bark, on exactly the keys the FeaturePopupWindow path used. Losing
        /// this silently kills 14 voiced rules per built-in mod, which is why it lives on the one
        /// path every reveal goes through instead of on the click handler.
        /// </summary>
        private static void Announce(StudioRackEntry entry)
        {
            if (string.IsNullOrEmpty(entry.BarkFeature)) return;
            try { App.Bark?.NotifyFeatureOpened(entry.BarkFeature!); }
            catch { /* a bark must never break a navigation */ }
        }

        /// <summary>
        /// 120ms crossfade on the incoming panel. FillBehavior.Stop plus an explicit clear on
        /// completion so no animation clock is left holding Opacity hostage — the panel must be
        /// a plain opaque element again the moment this is over.
        /// </summary>
        private static void FadeInDetail(UIElement? host)
        {
            if (host == null) return;
            try
            {
                host.BeginAnimation(UIElement.OpacityProperty, null);
                if (!MotionFx.AllowTransitions)
                {
                    host.Opacity = 1;
                    return;
                }
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(DetailFadeMs))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                };
                fade.Completed += (_, __) =>
                {
                    try
                    {
                        host.BeginAnimation(UIElement.OpacityProperty, null);
                        host.Opacity = 1;
                    }
                    catch { }
                };
                host.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            catch (Exception ex) { App.Logger?.Debug("Studio detail fade: {E}", ex.Message); }
        }

        // =====================================================================================
        //  labels + state dots
        // =====================================================================================

        /// <summary>
        /// Row captions go through <c>MainWindow.ModAwareLabel</c> exactly like the mosaic tiles
        /// and the popup titles, so a mod that renames "Flash Images" renames the rack row too.
        /// The shared section keys all carry a leading emoji and the rows draw their own icon, so
        /// the glyph is stripped — same rule as the dashboard cards.
        /// </summary>
        private void RefreshRackLabels()
        {
            foreach (var e in _entries)
            {
                var text = LabelFor(e);
                // Both states carry their own caption, so both get repainted. Missing the tile
                // one would leave the ACTIVE row - the only one anybody is reading - frozen in
                // the previous language or the previous mod's feature name.
                if (e.Label != null) e.Label.Text = text;
                if (e.TileLabel != null) e.TileLabel.Text = text;
            }
        }

        private static string LabelFor(StudioRackEntry e) =>
            StripLeadingGlyph(MainWindow.ModAwareLabel(e.English, e.LocKey));

        /// <summary>
        /// Local twin of <c>MainWindow.StripLeadingGlyph</c> (private over there, and a
        /// UserControl cannot reach it). Keep the two in step if either changes.
        /// </summary>
        private static string StripLeadingGlyph(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var i = 0;
            while (i < text.Length && !char.IsLetterOrDigit(text, i))
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
            return i > 0 && i < text.Length ? text.Substring(i) : text;
        }

        /// <summary>The AppSettings properties whose changes move a rack dot. Filtered rather
        /// than repainting on every PropertyChanged, because SessionEngine rewrites the ramped
        /// dials about once a second.</summary>
        private static readonly HashSet<string> DotProperties = new(StringComparer.Ordinal)
        {
            nameof(Models.AppSettings.FlashEnabled),
            nameof(Models.AppSettings.MandatoryVideosEnabled),
            nameof(Models.AppSettings.SubliminalEnabled),
            nameof(Models.AppSettings.SpiralEnabled),
            nameof(Models.AppSettings.PinkFilterEnabled),
            nameof(Models.AppSettings.BubblesEnabled),
            nameof(Models.AppSettings.BubbleCountEnabled),
            nameof(Models.AppSettings.LockCardEnabled),
            nameof(Models.AppSettings.BouncingTextEnabled),
            nameof(Models.AppSettings.MindWipeEnabled),
            nameof(Models.AppSettings.BrainDrainEnabled),
            nameof(Models.AppSettings.SchedulerEnabled),
            nameof(Models.AppSettings.IntensityRampEnabled),
        };

        /// <summary>
        /// The lit dot's halo. Hoisted to one frozen instance rather than allocated per dot per
        /// repaint (RefreshDots runs on every settings write that moves a dot, on every selection
        /// and on every Studio show). Still not an animation, so there is nothing here for the
        /// motion kill-switch to reach.
        /// </summary>
        private static readonly System.Windows.Media.Effects.DropShadowEffect DotGlow =
            Frozen(new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                BlurRadius = 7,
                ShadowDepth = 0,
                Opacity = 0.85,
            });

        private void RefreshDots()
        {
            var on = (Brush?)TryFindResource("PinkBrush") ?? Brushes.HotPink;
            var off = (Brush?)TryFindResource("TextDimBrush") ?? Brushes.DimGray;

            foreach (var e in _entries)
            {
                if (e.Dot == null) continue;
                bool? state;
                try { state = e.Dot(); } catch { state = null; }

                // Unknown state reads as off rather than vanishing: the row's geometry must not
                // jump around because a settings object happened to be null for a tick.
                bool lit = state == true;
                var tip = Loc.Get(lit ? "st4_studio_dot_on" : "st4_studio_dot_off");

                // Both states own a dot; the checked row is showing the tile's one.
                Paint(e.DotShape);
                Paint(e.TileDot);

                void Paint(Ellipse? dot)
                {
                    if (dot == null) return;
                    dot.Fill = lit ? on : off;
                    dot.Opacity = lit ? 1.0 : 0.35;
                    dot.Effect = lit ? DotGlow : null;
                    dot.ToolTip = tip;
                }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_settingsHooked)
            {
                BindDotListener();
                // The rack and its panels live for the whole session, so they outlive the
                // AppSettings instance they hooked: a cloud restore or a factory-default Reset
                // SWAPS App.Settings.Current (SettingsService.RestoreFrom/Reset) and raises this.
                if (App.Settings != null) App.Settings.CurrentReplaced += OnSettingsCurrentReplaced;
                _settingsHooked = true;
            }
            // Per-module hosting wiring contributed by the module partials
            // (StudioTabView.<Area>.cs). Each is idempotent and self-guarding.
            HookHapticsModule();
            RefreshRackLabels();
            RefreshDots();
        }

        /// <summary>
        /// Points the dot listener at the CURRENT AppSettings instance, detaching from whichever
        /// one it was on. The instance is tracked rather than re-read from
        /// <c>App.Settings.Current</c>, which after a swap is already a different object - detaching
        /// from that one would leave the old subscription live and the new one absent.
        /// </summary>
        private void BindDotListener()
        {
            if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _hookedSettings = App.Settings?.Current;
            if (_hookedSettings != null) _hookedSettings.PropertyChanged += OnSettingsPropertyChanged;
        }

        /// <summary>
        /// Cloud restore / Reset swapped the settings object. Re-point this host's dot listener and
        /// every hosted panel's own hook, then repaint: without it the entire rack would show - and
        /// write back from - the discarded instance for the rest of the session.
        /// <para>
        /// Normal priority, not Loaded: Loaded-priority work is starved in this app (the note at
        /// PerformanceSettingsSection.xaml.cs:64 is the same lesson).
        /// </para>
        /// </summary>
        private void OnSettingsCurrentReplaced()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    BindDotListener();
                    foreach (var panel in _entries.Select(x => x.Panel).OfType<Features.ISettingsRebindable>())
                        panel.RebindToCurrentSettings();
                    RefreshDots();
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Studio rack rebind after a settings restore failed");
                }
            }));
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null || !DotProperties.Contains(e.PropertyName)) return;
            // Marshalled because the writer may be the session engine's timer thread.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                                   new Action(RefreshDots));
        }
    }
}
