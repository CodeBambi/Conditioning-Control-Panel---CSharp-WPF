using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

            /// <summary>
            /// RIGHT-click quick-toggle for this row's feature — the dashboard wall's gesture,
            /// same grammar (left-click selects/opens, right-click flips). Null for a module with
            /// no single on/off (Visuals), where the gesture must do nothing rather than guess.
            ///
            /// <para>Defaulted in <see cref="BuildRack"/> from the rack key itself for every
            /// module the wall already knows: <c>MainWindow.ToggleWallFeature</c> is keyed on THESE
            /// keys, so the two surfaces cannot drift. The three that predate it (Haptics,
            /// Scheduler, Ramp) pass an explicit toggle that flips their panel's own master
            /// checkbox, which is the premium rail's idiom - the checkbox's real handler runs, so
            /// its premium gate and its Save run with it.</para>
            /// </summary>
            public Action? Toggle;

            /// <summary>True when the panel draws its own page header, so the shared detail
            /// header must hide rather than double up (Haptics).</summary>
            public bool OwnHeader;

            // Built by BuildRack.
            public RadioButton? Row;
            public TextBlock? Label;
            public Ellipse? DotShape;
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

        private void BuildRack()
        {
            // Order is the Phase-4 contract's, with group captions inserted so nothing moves.
            _layout.Add("st4_studio_group_effects");
            Add("flash", "⚡", "Flash Images", "section_flash_images", HostFlash, PanelFlash, "Flash",
                () => App.Settings?.Current?.FlashEnabled);
            Add("video", "🎬", "Mandatory Video", "section_mandatory_video", HostVideo, PanelVideo, "Video",
                () => App.Settings?.Current?.MandatoryVideosEnabled);
            Add("subliminal", "💭", "Subliminals", "section_subliminals_2", HostSubliminal, PanelSubliminal, "Subliminal",
                () => App.Settings?.Current?.SubliminalEnabled);
            Add("spiral", "🌀", "Spiral Overlay", "label_spiral_overlay", HostSpiral, PanelSpiral, "Spiral",
                () => App.Settings?.Current?.SpiralEnabled);
            Add("pinkfilter", "💗", "Pink Filter", "label_pink_filter", HostPinkFilter, PanelPinkFilter, "PinkFilter",
                () => App.Settings?.Current?.PinkFilterEnabled);
            // Visuals has no single master toggle - the dashboard card is deliberately neutral
            // too (MainWindow.Presets.cs:800). A dot that cannot be wired honestly is omitted.
            Add("visuals", "👁", "Visuals", "section_visuals", HostVisuals, PanelVisuals, "Visuals", null);

            _layout.Add("st4_studio_group_games");
            Add("bubbles", "🫧", "Bubble Pop", "label_bubble_pop", HostBubblePop, PanelBubblePop, "BubblePop",
                () => App.Settings?.Current?.BubblesEnabled);
            Add("bubblecount", "🔢", "Bubble Count", "label_bubble_count", HostBubbleCount, PanelBubbleCount, "BubbleCount",
                () => App.Settings?.Current?.BubbleCountEnabled);
            Add("lockcard", "📐", "Lock Card", "label_lock_card", HostLockCard, PanelLockCard, "LockCard",
                () => App.Settings?.Current?.LockCardEnabled);
            Add("bouncingtext", "📺", "Bouncing Text", "label_bouncing_text", HostBouncingText, PanelBouncingText, "BouncingText",
                () => App.Settings?.Current?.BouncingTextEnabled);

            _layout.Add("st4_studio_group_immersion");
            Add("mindwipe", "🧠", "Mind Wipe", "label_mind_wipe", HostMindWipe, PanelMindWipe, "MindWipe",
                () => App.Settings?.Current?.MindWipeEnabled);
            // New in Phase 4 (G2 rescue). "BrainDrain" is a NEW feature_eq value; all three
            // built-in mods now carry a matching feat_braindrain FeatureOpened rule.
            Add("braindrain", "💧", "Brain Drain", "section_brain_drain", HostBrainDrain, PanelBrainDrain, "BrainDrain",
                () => App.Settings?.Current?.BrainDrainEnabled);
            // Haptics: no FeatureOpened key. ShowTab("haptics") still fires
            // NotifyTabNavigated("haptics"), which is what its 3 rules per mod match on.
            // The dot reads a nested settings object with no INPC, so it refreshes on every
            // Studio show and on every selection rather than live.
            Add("haptics", "📳", "Haptics", "tab_haptics", PanelHaptics, null, null,
                () => App.Settings?.Current?.Haptics?.Enabled, ownHeader: true,
                // No wall key: the mosaic never carried Haptics (the premium rail chip does), and
                // its enable lives on the nested Haptics settings object. Flip the page's own
                // master box so MainWindow.ChkHapticsEnabled_Changed runs - including the
                // premium gate that reverts the box for a free account.
                toggle: () => FlipMasterCheckBox(PanelHaptics?.ChkHapticsEnabled));

            _layout.Add("st4_studio_group_timing");
            // Both fire the popup's single "SchedulerRamp" key so the existing rules keep firing.
            // Neither is a wall tile either, and neither drives a service directly (the 30s
            // SchedulerTimer_Tick and the session ramp read the flags), so the honest quick-toggle
            // is the panel's own enable box - it writes the flag and Saves in one place.
            Add("scheduler", "📅", "Scheduler", "section_scheduler", HostScheduler, PanelScheduler, "SchedulerRamp",
                () => App.Settings?.Current?.SchedulerEnabled,
                toggle: () => FlipMasterCheckBox(PanelScheduler?.Inner.ChkEnabled));
            Add("ramp", "📈", "Intensity Ramp", "section_intensity_ramp", HostRamp, PanelRamp, "SchedulerRamp",
                () => App.Settings?.Current?.IntensityRampEnabled,
                toggle: () => FlipMasterCheckBox(PanelRamp?.Inner.ChkEnabled));

            RenderRackRows();
            RefreshRackLabels();
            RefreshDots();

            void Add(string key, string glyph, string english, string locKey, UIElement? host,
                     UserControl? panel, string? bark, Func<bool?>? dot, bool ownHeader = false,
                     Action? toggle = null)
            {
                var entry = new StudioRackEntry
                {
                    Key = key,
                    Glyph = glyph,
                    English = english,
                    LocKey = locKey,
                    Host = host,
                    Panel = panel,
                    BarkFeature = bark,
                    Dot = dot,
                    OwnHeader = ownHeader,
                    // Default: route the rack key straight into the dashboard's own quick-toggle.
                    // Derived rather than hand-listed per row so adding a wall tile for a module
                    // (or a module for a wall tile) cannot leave the rack behind.
                    Toggle = toggle ?? (WallToggleKeys.Contains(key) ? () => ToggleWallFeature(key) : null),
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

                // icon | caption | state dot
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // The glyph sits inside a 20x20 rounded chip: raw emoji floating beside text is
                // the stock-toolkit look; contained, every icon takes the same visual weight and
                // the column reads as a designed rack instead of a bullet list.
                var iconChip = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x4A)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x39, 0x39, 0x63)),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Child = new EmojiTextBlock
                    {
                        Text = e.Glyph,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Grid.SetColumn(iconChip, 0);
                grid.Children.Add(iconChip);

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

                e.Row = new RadioButton
                {
                    Style = (Style?)TryFindResource("RackEntryStyle"),
                    Tag = e.Key,
                    Content = grid,
                };
                e.Row.Click += RackEntry_Click;
                // Right-click = quick-toggle, the same second gesture the dashboard tiles carry.
                // On the ROW, not on the dot: the dot is 7px, and the gesture belongs to the
                // whole entry. Rows with no Toggle fall through unhandled (Visuals).
                e.Row.MouseRightButtonUp += RackEntry_RightClick;
                RackList.Children.Add(e.Row);
            }
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

        // =====================================================================================
        //  right-click quick-toggle — the dashboard's gesture, on the rack
        // =====================================================================================

        /// <summary>
        /// The rack keys <c>MainWindow.ToggleWallFeature</c> handles. Its cases were written
        /// against THESE keys ("Keys are the Studio rack's", MainWindow.Presets.cs), so the wall
        /// tile and the rack row flip one flag through one method: there is no second state store
        /// and no second set of service start/stop calls to fall out of step.
        ///
        /// <para>An unknown key is a quiet no-op over there, so a typo here costs a dead gesture,
        /// never a wrong write.</para>
        /// </summary>
        private static readonly HashSet<string> WallToggleKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "flash", "video", "subliminal", "spiral", "pinkfilter", "bubbles",
            "bubblecount", "lockcard", "bouncingtext", "mindwipe", "braindrain",
        };

        /// <summary>
        /// Right-click on a rack row flips that module on/off without selecting it — left-click
        /// still owns selection, exactly as left-click still owns "open" on the wall.
        ///
        /// <para>Selection is deliberately untouched: a quick-toggle that also yanked the detail
        /// pane to a different module would make the gesture unusable for turning three things on
        /// in a row, and it would fire a FeatureOpened bark for a panel nobody asked to see.</para>
        /// </summary>
        private void RackEntry_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is not string key) return;
            var entry = EntryFor(key);
            if (entry?.Toggle == null) return;   // Visuals: no single on/off, so no gesture

            e.Handled = true;

            bool? before = SafeDotState(entry);
            try { entry.Toggle(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "Studio rack quick-toggle failed for {Key}", key); }

            // One beat late, at Normal priority (never Loaded — it starves here, see BindDotListener):
            // a refusal can undo the write (the haptics premium gate flips IsChecked back) or never
            // make it (the session-lock refusal writes nothing at all). Reading AFTER lets the row
            // react to what actually happened rather than to what was asked for.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    RefreshDots();
                    if (SafeDotState(entry) != before) PingDot(entry.DotShape);
                }
                catch (Exception ex) { App.Logger?.Debug("StudioTabView rack toggle repaint: {E}", ex.Message); }
            }));
        }

        private static bool? SafeDotState(StudioRackEntry entry)
        {
            if (entry.Dot == null) return null;
            try { return entry.Dot(); } catch { return null; }
        }

        /// <summary>Routes to the dashboard wall's own quick-toggle, which owns the session-lock
        /// refusal, the per-feature service start/stop and the Save.</summary>
        private void ToggleWallFeature(string key)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.ToggleWallFeature(key);
        }

        /// <summary>
        /// Flips a module's own master enable box so the panel's real handler runs — the premium
        /// rail's <c>ToggleTabCheckBox</c> idiom, with one addition: a DISABLED box is left alone.
        /// <c>IsChecked</c> ignores <c>IsEnabled</c>, and a greyed master toggle is how the session
        /// feature lock says "the session owns this dial" — so without the guard the rack would be
        /// the one surface that could flip a locked switch.
        /// </summary>
        private static void FlipMasterCheckBox(CheckBox? cb)
        {
            if (cb == null || !cb.IsEnabled) return;
            cb.IsChecked = !(cb.IsChecked ?? false);
        }

        /// <summary>
        /// A 260ms pop on the row's state dot, fired only when the flag REALLY moved. The dot
        /// already carries the on/off language; this just makes a change land on a row the user is
        /// not looking straight at, since right-click never moves the selection.
        ///
        /// <para>Quiet-surface safe (PLAN §2.7): one-shot, gated on <see cref="MotionFx"/>,
        /// <c>FillBehavior.Stop</c> plus an explicit clear, so no clock survives it.</para>
        /// </summary>
        private static void PingDot(Ellipse? dot)
        {
            if (dot == null || !MotionFx.AllowTransitions) return;
            try
            {
                if (dot.RenderTransform is not ScaleTransform scale)
                {
                    scale = new ScaleTransform(1, 1);
                    dot.RenderTransformOrigin = new Point(0.5, 0.5);
                    dot.RenderTransform = scale;
                }

                var pop = new DoubleAnimation(2.0, 1.0, TimeSpan.FromMilliseconds(260))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop,
                };
                pop.Completed += (_, __) =>
                {
                    try
                    {
                        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                        scale.ScaleX = 1;
                        scale.ScaleY = 1;
                    }
                    catch { }
                };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
            catch (Exception ex) { App.Logger?.Debug("StudioTabView.PingDot: {E}", ex.Message); }
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
                if (e.Label != null)
                    e.Label.Text = LabelFor(e);
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

        private void RefreshDots()
        {
            var on = (Brush?)TryFindResource("PinkBrush") ?? Brushes.HotPink;
            var off = (Brush?)TryFindResource("TextDimBrush") ?? Brushes.DimGray;

            foreach (var e in _entries)
            {
                if (e.DotShape == null || e.Dot == null) continue;
                bool? state;
                try { state = e.Dot(); } catch { state = null; }

                // Unknown state reads as off rather than vanishing: the row's geometry must not
                // jump around because a settings object happened to be null for a tick.
                bool lit = state == true;
                e.DotShape.Fill = lit ? on : off;
                e.DotShape.Opacity = lit ? 1.0 : 0.35;
                // Static halo on a lit dot - an Effect swap is not an animation, so there is
                // still nothing here for the motion kill-switch to reach.
                e.DotShape.Effect = lit
                    ? new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                        BlurRadius = 7,
                        ShadowDepth = 0,
                        Opacity = 0.85,
                    }
                    : null;
                e.DotShape.ToolTip = Loc.Get(lit ? "st4_studio_dot_on" : "st4_studio_dot_off");

                // Same sentence on the whole row for the rows the right-click gesture can flip:
                // the dot is 7px of hover target, and after a quick-toggle the row the cursor is
                // already sitting on should be able to answer "did that take?" itself. Existing
                // keys only - no new string enters the nine language files for this.
                if (e.Row != null && e.Toggle != null)
                    e.Row.ToolTip = Loc.Get(lit ? "st4_studio_dot_on" : "st4_studio_dot_off");
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
