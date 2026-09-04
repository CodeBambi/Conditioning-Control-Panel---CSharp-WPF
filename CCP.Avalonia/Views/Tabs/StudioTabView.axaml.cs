using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Studio door (tab key <c>studio</c>): a master-detail effects rack. PORTED from
    /// <c>ConditioningControlPanel/Views/Tabs/StudioTabView.xaml.cs</c> (+ its
    /// <c>.Haptics.cs</c> partial).
    ///
    /// <para><b>This file is the host only.</b> It owns the rack list and the detail pane, and no
    /// feature logic whatsoever: every module is an existing <c>Views/Features/*FeatureControl</c>
    /// or a <c>Views/Controls/Studio/</c> panel, each already ported on its own.</para>
    ///
    /// <para><b>What is real here.</b> The rack table and every row built from it, the group
    /// captions (bound to the localization manager's indexer, so a language switch repaints them),
    /// the row captions and their glyph-stripping, the resting-strip / active-tile swap and the
    /// row height that grows with it, the tier livery on the row rim and the chip, the state dots
    /// with their tooltips and accent halo, the accent-tint replay (<see cref="RetintChrome"/>),
    /// the selection contract (<see cref="FocusRackEntry"/> / <see cref="PreselectRackEntry"/> /
    /// <see cref="OnTabShown"/>) and the detail header's OwnHeader collapse.</para>
    ///
    /// <para><b>Wired against Core.</b> The thirteen state dots read their own
    /// <c>CoreSettings.Current.&lt;Feature&gt;Enabled</c> flag (Haptics the nested
    /// <c>Haptics.Enabled</c>) and repaint from a filtered <c>PropertyChanged</c> listener plus a
    /// <c>CurrentReplaced</c> rebind, so a cloud restore cannot leave the rack reading - and
    /// writing back to - a discarded settings instance. Row captions go through
    /// <see cref="CoreMods.MakeModAware"/>, the accent through
    /// <see cref="CoreMods.GetFilterColorRgb"/>, and <see cref="CoreMods.ModChanged"/> drives
    /// <see cref="RepaintModAwareChrome"/>. The Scheduler and Ramp quick-toggles flip their
    /// panel's own master checkbox, which is what WPF does and what keeps the write and the Save
    /// in one place.</para>
    ///
    /// <para><b>What is stubbed, and why.</b> Everything the WPF code-behind reaches into the app
    /// head for: <c>MainWindow.ToggleWallFeature</c> (the eleven wall modules' quick-toggle, which
    /// owns the session-lock refusal and the per-feature service start/stop),
    /// <c>BarkService.NotifyFeatureOpened</c>, <c>PerimeterCometAdorner</c> (the active tile's
    /// comet) and the detail crossfade. Each is marked <c>ponytail:</c> at its site.</para>
    ///
    /// <para><b>The art is real.</b> The rack's feature plates and the door medallion resolve
    /// through <see cref="ModArt.TryLoad"/> over <see cref="CoreModArt"/> against the
    /// <c>Assets/features</c> and <c>Assets/nav</c> the csproj links, repainting on
    /// <see cref="CoreMods.ModChanged"/>. Every authored wash, glyph and scrim stays underneath as
    /// the no-art fallback, which is the WPF null path.</para>
    ///
    /// <para><b>Quiet surface (PLAN §2.7).</b> No ambient loop is registered for this view and
    /// none may be. On WPF the one exception is the checked tile's perimeter comet, gated twice on
    /// <c>MotionFx</c>; the gate has a twin here now
    /// (<c>AmbientFxCanvas.Env.AllowAmbientLoops</c>), but the adorner does not, so the port
    /// carries no clock at all - which is the safe direction, not a new one.</para>
    /// </summary>
    public partial class StudioTabView : UserControl
    {
        /// <summary>What <c>ShowTab("studio")</c> lands on before the user has picked anything.</summary>
        internal const string DefaultRackKey = "flash";

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
            /// <para>ponytail: both halves of the lookup work now - the csproj links
            /// <c>Assets/features/*.png</c> as <c>avares://CCP.Avalonia/Resources/features/</c>,
            /// and <c>Helpers.ModArt.TryLoad</c> picks a mod's override over it. Nothing decodes
            /// art here only because there is nowhere to put it (see <c>RefreshRackArt</c>). The
            /// column is kept verbatim (case and all - the on-disk names really are mixed) because
            /// it is what decides which rows get the 56px active TILE and which keep the plain
            /// strip, and it is what <c>RefreshRackArt</c> will read.</para>
            /// </summary>
            public string? Art;

            /// <summary>English fallback fed to <c>MainWindow.ModAwareLabel</c>.</summary>
            public string English = string.Empty;
            public string LocKey = string.Empty;

            /// <summary>The element toggled by IsVisible (a ScrollViewer wrapper, or the panel itself).</summary>
            public Control? Host;

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
            /// RIGHT-click quick-toggle for this row's feature — the dashboard wall's gesture, same
            /// grammar (left-click selects/opens, right-click flips). Null for a module with no
            /// single on/off (Visuals), where the gesture must do nothing rather than guess. It
            /// also decides which rows carry the "Currently on/off" tooltip.
            /// </summary>
            public Action? Toggle;

            /// <summary>True when the panel draws its own page header, so the shared detail
            /// header must hide rather than double up.</summary>
            public bool OwnHeader;

            /// <summary>
            /// The tier this module is SOLD at: 0 = free, 1 = premium, 2 = Lab. Presentation only
            /// — the row wears the tier livery (gold/diamond, Theme/Brushes.xaml) permanently,
            /// whoever is looking; the refusal belongs to the panel's own premium gate.
            /// </summary>
            public int Tier;

            // Built by RenderRackRows.
            public RadioButton? Row;
            public TextBlock? Label;
            public Ellipse? DotShape;

            /// <summary>The resting visual: chip, caption, state dot. Hidden while this row is the
            /// checked one AND it has a tile (<see cref="Tile"/> takes over).</summary>
            public Grid? Strip;

            /// <summary>The checked visual for a module with feature art: a 56px full-bleed tile.
            /// Null for the three art-less modules, whose checked state is the strip as before.
            /// Both states are built once and swapped by IsVisible — see
            /// <see cref="ApplyRowState"/>.</summary>
            public Grid? Tile;
            public TextBlock? TileLabel;
            public Ellipse? TileDot;

            /// <summary>The tile's art layer. Its Background is the shared accent gradient until
            /// <see cref="RefreshRackArt"/> resolves this module's plate, then an ImageBrush of
            /// it. Null for the three art-less modules.</summary>
            public Border? Plate;

            /// <summary>The resting strip's 28px chip. Same swap, same fallback - except on a
            /// tiered row, where the livery well owns the background and art must not take it.</summary>
            public Border? Chip;
        }

        private readonly List<StudioRackEntry> _entries = new();

        /// <summary>Rack list contents in visual order: <see cref="StudioRackEntry"/> rows and
        /// bare loc-key strings for the group captions between them.</summary>
        private readonly List<object> _layout = new();

        private string _selected = DefaultRackKey;

        /// <summary>The rack key currently showing. Survives leaving and re-entering the door.</summary>
        internal string SelectedRackKey => _selected;

        /// <summary>
        /// Every code-built brush on this page whose colour is the mod accent rather than a fixed
        /// house colour. Each build site registers a closure that re-writes its own colours from a
        /// Color; <see cref="RetintChrome"/> replays the whole list on a mod switch. A list of
        /// closures rather than a list of brushes because the sites disagree about WHAT to do with
        /// the accent (an alpha ramp, a hue-rotated partner, a lightened foreground) and that
        /// knowledge belongs next to the brush that needs it.
        /// <para>Two things are deliberately absent on WPF and stay absent here: the comet's coral
        /// is a house constant, and the tier livery is a commercial mark, not a theme colour.</para>
        /// </summary>
        private readonly List<Action<Color>> _accentTints = new();

        /// <summary>
        /// The accent this page paints with: the active mod's colour, re-read on every
        /// <see cref="RetintChrome"/>.
        /// <para>Deviation from WPF, which reads <c>FxTheme.GlowColor</c>. That resolves
        /// glowColor -&gt; filterColor -&gt; accentColor; Core carries the last two links as
        /// <see cref="CoreMods.GetFilterColorRgb"/>, so a mod that sets a DISTINCT
        /// <c>fxPalette.glowColor</c> paints its filter colour on this rack instead. Unseeded that
        /// is the built-in manifest's #FF69B4 - what the WPF head paints today.</para>
        /// </summary>
        private static Color Accent
        {
            get
            {
                var (r, g, b) = CoreMods.GetFilterColorRgb();
                return Color.FromRgb(r, g, b);
            }
        }

        public StudioTabView()
        {
            // InitializeComponent, not AvaloniaXamlLoader.Load: the generated method ALSO fills the
            // x:Name fields, and this host is nothing but those fields - Load alone leaves RackList
            // and every Host*/Panel* null, which renders an empty rack and an empty detail pane
            // with no error at all.
            InitializeComponent();
            // Before BuildRack: the per-row closures it registers are appended to these.
            RegisterChromeTints();
            BuildRack();
            // Seeds the art for whichever mod is ALREADY active at construction, the same way
            // RetintChrome below seeds the accent; RepaintModAwareChrome handles every switch after.
            try { RefreshRackArt(); ApplyDoorIcon(); }
            catch (Exception ex) { Log.Debug(ex, "[Studio] the first art pass failed"); }
            // Land on the default without announcing it: nothing is on screen yet, and the
            // FeatureOpened bark belongs to the moment the user can actually see the panel.
            SelectEntry(DefaultRackKey, announce: false);
            // Seeds the accent for whichever mod is ALREADY active at construction; the same call
            // from RepaintModAwareChrome handles every switch after that.
            RetintChrome();
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Registers the page-level chrome objects that carry the accent. Row-level ones (the
        /// group rules, the NEW pill) register themselves as they are built.
        /// </summary>
        private void RegisterChromeTints()
        {
            // The hue-wash: 0x40 warm -> 0x1F accent, where "warm" is the accent hue-rotated +38°.
            // See HueRotate for why that single number reproduces the original #FF7E6B/#FF69B4
            // pair - the wash was always one colour and a rotation, written out as two literals,
            // which is what froze it pink.
            _accentTints.Add(c =>
            {
                ChipHueWashBrush.GradientStops[0].Color = WithAlpha(HueRotate(c, 38), 0x40);
                ChipHueWashBrush.GradientStops[1].Color = WithAlpha(c, 0x1F);
            });

            // The active tile's plate, for a module whose art does not resolve. On WPF this layer
            // is the module's feature art behind a fade mask; RefreshRackArt paints that art per
            // row, and this accent wash is what a row without any wears instead, so the checked
            // row still reads as a tile rather than as a taller strip.
            _accentTints.Add(c =>
            {
                TilePlateBrush.GradientStops[0].Color = WithAlpha(HueRotate(c, 38), 0x66);
                TilePlateBrush.GradientStops[1].Color = WithAlpha(c, 0x22);
            });

            _accentTints.Add(c => _dotGlowColor = c);
        }

        // =====================================================================================
        //  public surface — what MainWindow calls
        // =====================================================================================

        /// <summary>
        /// Selects a rack entry and shows its panel. An unknown key is a quiet no-op rather than a
        /// throw: every caller (ShowTab's <c>haptics</c> re-route, the Home mosaic tiles, the
        /// future Ctrl+K palette) is a navigation, and none of them should be able to break one.
        /// </summary>
        internal void FocusRackEntry(string? rackKey)
        {
            try { SelectEntry(rackKey, announce: true); }
            catch { /* a navigation must never throw */ }
        }

        /// <summary>
        /// Same selection, SILENT: no bark. For callers that pick the module before the rack is on
        /// screen (the tutorial's Studio steps, which run on <c>TutorialStep.OnBeforeTab</c>) —
        /// <see cref="OnTabShown"/> then announces the incoming selection exactly once, and it is
        /// the right one.
        /// </summary>
        internal void PreselectRackEntry(string? rackKey)
        {
            try { SelectEntry(rackKey, announce: false); }
            catch { /* as above */ }
        }

        /// <summary>
        /// Per-open refresh, called from ShowTab's <c>studio</c> case. Repaints the mod-aware row
        /// captions and the state dots, re-asserts the current selection's visibility and
        /// re-announces the visible module, which is what the popup did on every open.
        /// </summary>
        internal void OnTabShown()
        {
            try
            {
                RefreshRackLabels();
                RefreshDots();
                SelectEntry(_selected, announce: true);
            }
            catch { /* a door open must never throw */ }
        }

        /// <summary>
        /// Mod-switch repaint: captions, art, door medallion, accent and the detail pane's header.
        /// Deliberately does NOT re-announce the visible module the way <see cref="OnTabShown"/>
        /// does — a mid-session mod switch on a different tab must not fire a spurious
        /// feature-opened bark. Each step is independently guarded: a throw painting the art must
        /// not cost the labels.
        /// </summary>
        internal void RepaintModAwareChrome()
        {
            Try(RefreshRackLabels);
            Try(RefreshRackArt);
            Try(ApplyDoorIcon);
            Try(RetintChrome);
            Try(RefreshDots);           // after RetintChrome: it reads the accent-tinted dot halo
            Try(RefreshDetailHeader);

            static void Try(Action a)
            {
                try { a(); }
                catch (Exception ex) { Log.Debug(ex, "[Studio] a mod-aware repaint step failed"); }
            }
        }

        /// <summary>
        /// Re-resolves every row's feature art and writes it into the brushes the rows are already
        /// painted with. A null resolve KEEPS the current art: "the new mod ships no override and
        /// the embedded file failed to decode this time" must cost nothing, and blanking the row
        /// would turn a transient decode failure into a permanently empty rack.
        /// <para>The tile plate and the resting chip are per-row Borders built in
        /// <see cref="BuildArtTile"/> and <see cref="BuildRestingStrip"/> - only the accent
        /// gradients behind them are shared - so this writes an ImageBrush into each row's own
        /// Background and leaves the shared brushes alone.</para>
        /// <para>A TIERED row keeps its livery well: the gold/diamond fill under the glyph is how
        /// the tier reads on a small chip, and art over it would delete the mark. WPF paints the
        /// tier livery after the art for the same reason.</para>
        /// </summary>
        private void RefreshRackArt()
        {
            foreach (var e in _entries)
            {
                if (e.Art == null) continue;                 // art-less module: nothing to resolve

                Bitmap? bmp;
                try { bmp = ModArt.TryLoad("features/" + e.Art); }
                catch (Exception ex)
                {
                    Log.Debug(ex, "[Studio] feature art {Art} would not load", e.Art);
                    continue;
                }
                if (bmp == null) continue;                   // keep what is already painted

                // UniformToFill + right alignment: the tile is 56px tall and much wider, and the
                // fade mask eats the left edge, so the interesting half of a feature plate is the
                // right one.
                if (e.Plate != null)
                    e.Plate.Background = new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Right,
                    };

                // The chip takes the art WHATEVER its tier - WPF's order is art first, then the
                // tier well only `if (chipArt == null)`, so a tiered module with art (Haptics)
                // wears its plate under the gold frame rather than the amber well. The glyph goes
                // with it: over there an art chip is built with no child at all.
                if (e.Chip != null)
                {
                    e.Chip.Background = new ImageBrush(bmp)
                    {
                        Stretch = Stretch.UniformToFill,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center,
                    };
                    if (e.Chip.Child != null) e.Chip.Child.IsVisible = false;
                }
            }
        }

        /// <summary>
        /// The page-header door medallion. Same file the nav rail's Studio door uses, so a mod that
        /// reskins the rail reskins this too instead of leaving the header on stock art.
        /// <para>A null resolve leaves the wash chip and its glyph exactly as they are, which is
        /// the WPF null path - the medallion is decoration and a mod that ships none must cost
        /// nothing. The glyph is only hidden once there is a picture to hide it behind.</para>
        /// </summary>
        private void ApplyDoorIcon()
        {
            if (ImgStudioDoorArt == null) return;

            Bitmap? bmp;
            try { bmp = ModArt.TryLoad("nav/door_studio.png"); }
            catch (Exception ex)
            {
                Log.Debug(ex, "[Studio] the door medallion would not load");
                return;
            }
            if (bmp == null) return;

            ImgStudioDoorArt.Source = bmp;
            ImgStudioDoorArt.IsVisible = true;
            if (TxtStudioDoorGlyph != null) TxtStudioDoorGlyph.IsVisible = false;
        }

        /// <summary>
        /// Replays every registered accent closure with the mod's current accent. Cheap and
        /// idempotent — the closures only write colours — so it runs from the constructor as well,
        /// which is what seeds the rack for the mod that is already active at startup.
        /// </summary>
        private void RetintChrome()
        {
            var accent = Accent;
            foreach (var tint in _accentTints)
            {
                try { tint(accent); }
                catch { /* one bad closure must not cost the rest of the page its colour */ }
            }
        }

        // ---- accent maths --------------------------------------------------------------------

        /// <summary>The same colour at a different alpha.</summary>
        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>
        /// Blend toward white. <c>Lighten(#FF69B4, 0.25)</c> is <c>#FF8FC7</c> to the byte — which
        /// is exactly the relationship the NEW pill's two literals encoded, now expressed as the
        /// transform instead of as a second hard-coded pink.
        /// </summary>
        private static Color Lighten(Color c, double t)
        {
            static byte Mix(byte v, double f) => (byte)Math.Clamp(Math.Round(v + (255 - v) * f), 0, 255);
            return Color.FromArgb(c.A, Mix(c.R, t), Mix(c.G, t), Mix(c.B, t));
        }

        /// <summary>
        /// Rotate a colour's hue, keeping saturation and value. The chip hue-wash used to run coral
        /// <c>#FF7E6B</c> → pink <c>#FF69B4</c>; measured in HSV those two differ ONLY in hue, by
        /// +38° (S 0.580 vs 0.588, V 1.0 both). So the wash is not two colours, it is one accent
        /// and a hue rotation — and written that way it follows the mod instead of staying pink
        /// under a purple one, while still reproducing the original pair (to within a byte) for the
        /// default accent.
        /// </summary>
        private static Color HueRotate(Color c, double degrees)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double d = max - min;

            double h;
            if (d <= 0) h = 0;
            else if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);

            h = ((h + degrees) % 360 + 360) % 360;
            double s = max <= 0 ? 0 : d / max, v = max;

            double cc = v * s;
            double x = cc * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - cc;
            (double R, double G, double B) t = h switch
            {
                < 60 => (cc, x, 0),
                < 120 => (x, cc, 0),
                < 180 => (0, cc, x),
                < 240 => (0, x, cc),
                < 300 => (x, 0, cc),
                _ => (cc, 0, x),
            };
            static byte B255(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
            return Color.FromArgb(c.A, B255(t.R + m), B255(t.G + m), B255(t.B + m));
        }

        /// <summary>
        /// Every raw feature panel the rack hosts, for
        /// <c>MainWindow.ApplySessionLockToFeaturePopup</c>. Haptics is excluded on purpose: it is
        /// already covered by name in <c>ApplySessionLockToTabs</c>, and its content is a
        /// ScrollViewer rather than a Panel so it could never take the lock banner anyway.
        /// </summary>
        internal IReadOnlyList<UserControl> HostedFeaturePanels =>
            _entries.Select(e => e.Panel).Where(p => p != null).Select(p => p!).ToList();

        /// <summary>
        /// The Haptics page, re-hosted as a rack module. <c>MainWindow.HapticsTab</c> forwards
        /// here, so all ~71 <c>HapticsTab.&lt;x:Name&gt;</c> dereferences across the MainWindow
        /// partials keep working verbatim.
        /// <para>The page IS the module now, hosted raw. Two shell partials reach through this
        /// property by name - MainShellWindow.Haptics.cs's 34 forwards and
        /// MainShellWindow.TabFxTakeoverLabStatus.cs's SetHapticsStatusPulse, whose lookup is
        /// deliberately two hops (StudioRack -&gt; PanelHaptics -&gt; HapticStatusDot) because the
        /// dot lives in this page's own namescope.</para>
        /// </summary>
        internal HapticsTabView HapticsPanel => PanelHaptics;

        // =====================================================================================
        //  rack construction
        // =====================================================================================

        /// <summary>
        /// Order is the Phase-4 contract's, with group captions inserted so nothing moves.
        ///
        /// <para><b>Every entry sets OwnHeader.</b> Every feature page draws its own in-page hero,
        /// so the shared <c>DetailHeader</c> bar would name the module a second time directly above
        /// it. The mechanism is deliberately still per-entry rather than a hard-collapse in
        /// <see cref="RefreshDetailHeader"/>: the flag keeps meaning exactly what it always meant
        /// ("this panel draws its own page header"), it is now simply true of all of them, and a
        /// page that ever loses its hero passes <c>ownHeader: false</c> and gets the shared bar
        /// back with nothing else to change.</para>
        /// </summary>
        private void BuildRack()
        {
            _layout.Add("st4_studio_group_effects");
            Add("flash", "⚡", "flash.png", "Flash Images", "section_flash_images", HostFlash, PanelFlash, "Flash",
                () => CoreSettings.Current.FlashEnabled);
            Add("video", "🎬", "mandatory_videos.png", "Mandatory Video", "section_mandatory_video", HostVideo, PanelVideo, "Video",
                () => CoreSettings.Current.MandatoryVideosEnabled);
            Add("subliminal", "💭", "subliminal.png", "Subliminals", "section_subliminals_2", HostSubliminal, PanelSubliminal, "Subliminal",
                () => CoreSettings.Current.SubliminalEnabled);
            Add("spiral", "🌀", "spiral_overlay.png", "Spiral Overlay", "label_spiral_overlay", HostSpiral, PanelSpiral, "Spiral",
                () => CoreSettings.Current.SpiralEnabled);
            Add("pinkfilter", "💗", "Pink_filter.png", "Pink Filter", "label_pink_filter", HostPinkFilter, PanelPinkFilter, "PinkFilter",
                () => CoreSettings.Current.PinkFilterEnabled);
            // Visuals has no single master toggle - the dashboard card is deliberately neutral too.
            // A dot that cannot be wired honestly is omitted, and with it the right-click gesture.
            Add("visuals", "👁", null, "Visuals", "section_visuals", HostVisuals, PanelVisuals, "Visuals", null);

            _layout.Add("st4_studio_group_games");
            Add("bubbles", "🫧", "Bubble_pop.png", "Bubble Pop", "label_bubble_pop", HostBubblePop, PanelBubblePop, "BubblePop",
                () => CoreSettings.Current.BubblesEnabled);
            Add("bubblecount", "🔢", "Bubble_count.png", "Bubble Count", "label_bubble_count", HostBubbleCount, PanelBubbleCount, "BubbleCount",
                () => CoreSettings.Current.BubbleCountEnabled);
            Add("lockcard", "📐", "Phrase_Lock.png", "Lock Card", "label_lock_card", HostLockCard, PanelLockCard, "LockCard",
                () => CoreSettings.Current.LockCardEnabled);
            Add("bouncingtext", "📺", "bouncing_text.png", "Bouncing Text", "label_bouncing_text", HostBouncingText, PanelBouncingText, "BouncingText",
                () => CoreSettings.Current.BouncingTextEnabled);

            _layout.Add("st4_studio_group_immersion");
            Add("mindwipe", "🧠", "Mind_Wipers.png", "Mind Wipe", "label_mind_wipe", HostMindWipe, PanelMindWipe, "MindWipe",
                () => CoreSettings.Current.MindWipeEnabled);
            // "BrainDrain" is a NEW feature_eq value; all three built-in mods carry a matching
            // feat_braindrain FeatureOpened rule.
            Add("braindrain", "💧", "brain_drain.png", "Brain Drain", "section_brain_drain", HostBrainDrain, PanelBrainDrain, "BrainDrain",
                () => CoreSettings.Current.BrainDrainEnabled);
            // Haptics: no FeatureOpened key. ShowTab("haptics") still fires
            // NotifyTabNavigated("haptics"), which is what its 3 rules per mod match on. Panel is
            // null because the placard is not a UserControl the session-lock sweep can paint, and
            // on WPF the real page is excluded from that sweep for its own reasons anyway.
            // The dot reads a nested settings object with no INPC of its own, so it repaints on
            // every Studio show and every selection rather than live - exactly as on WPF.
            // Tier 1: the rack's one paid module, same bar as the premium rail's chip.
            Add("haptics", "📳", "vibe.png", "Haptics", "tab_haptics", PanelHaptics, null, null,
                () => CoreSettings.Current.Haptics?.Enabled,
                // REFUSED, and the page being hosted now does not change it. WPF flips
                // PanelHaptics.ChkHapticsEnabled so the page's own handler AND its premium gate
                // run with the write; HapticsTabView.ChkHapticsEnabled_Changed is itself an empty
                // stub on this head (all 34 of its forwards are - see the header of
                // CCP.Avalonia/Views/Windows/MainShellWindow.Haptics.cs), so the flip would move a
                // checkbox and nothing else. Writing CoreSettings.Current.Haptics.Enabled straight
                // from here instead would skip the gate AND start no device, which is worse than
                // an inert gesture on a page whose subject is hardware that touches the user.
                toggle: null,
                tier: 1);

            _layout.Add("st4_studio_group_timing");
            // Both fire the popup's single "SchedulerRamp" key so the existing rules keep firing.
            // Neither is a wall tile and neither drives a service directly (the scheduler tick and
            // the session ramp read the flags), so the honest quick-toggle is the panel's own
            // enable box - it writes the flag and Saves in one place.
            Add("scheduler", "📅", null, "Scheduler", "section_scheduler", HostScheduler, PanelScheduler, "SchedulerRamp",
                () => CoreSettings.Current.SchedulerEnabled,
                toggle: () => FlipMasterCheckBox(PanelScheduler?.Inner.FindControl<CheckBox>("ChkEnabled")));
            Add("ramp", "📈", null, "Intensity Ramp", "section_intensity_ramp", HostRamp, PanelRamp, "SchedulerRamp",
                () => CoreSettings.Current.IntensityRampEnabled,
                toggle: () => FlipMasterCheckBox(PanelRamp?.Inner.FindControl<CheckBox>("ChkEnabled")));

            RenderRackRows();
            RefreshRackLabels();
            RefreshDots();

            void Add(string key, string glyph, string? art, string english, string locKey, Control? host,
                     UserControl? panel, string? bark, Func<bool?>? dot, bool ownHeader = true,
                     Action? toggle = null, int tier = 0)
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
                    Tier = tier,
                    // Default: route the rack key into the dashboard's own quick-toggle. Derived
                    // rather than hand-listed per row so adding a wall tile for a module (or a
                    // module for a wall tile) cannot leave the rack behind.
                    Toggle = toggle ?? (WallToggleKeys.Contains(key) ? () => QuickToggle(key) : null),
                };
                _entries.Add(entry);
                _layout.Add(entry);
            }
        }

        /// <summary>
        /// Flips a panel's own master checkbox. Going through the box rather than the settings flag
        /// is what makes the panel's real handler - and any premium or session gate on it - run
        /// with the write instead of behind its back. A disabled box has refused already.
        /// </summary>
        private static void FlipMasterCheckBox(CheckBox? cb)
        {
            if (cb == null || !cb.IsEnabled) return;
            cb.IsChecked = !(cb.IsChecked ?? false);
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
                    capRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    capRow.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

                    var caption = new TextBlock
                    {
                        FontSize = 10,
                        FontWeight = FontWeight.Bold,
                        Opacity = 0.85,
                    };
                    caption[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextDimBrush");
                    // BOUND, not assigned. RenderRackRows runs exactly once (BuildRack, from the
                    // ctor) and the group captions are not _entries, so RefreshRackLabels never
                    // revisits them - a language switch left them frozen in the old language while
                    // the page title beside them changed. This is the same binding {loc:Str}
                    // produces (Localization/StrExtension.cs), so they track SetLanguage's "Item[]"
                    // notification with no repaint path at all.
                    caption.Bind(TextBlock.TextProperty, new Binding($"[{headerKey}]")
                    {
                        Source = LocalizationManager.Instance,
                        Mode = BindingMode.OneWay,
                    });
                    capRow.Children.Add(caption);

                    // The rule is an ACCENT ramp, not a pink one: same alphas (0x55 -> 0x00), the
                    // hue taken from the active mod and re-taken on every switch.
                    var ruleBrush = Wash(Color.FromArgb(0x55, 0xFF, 0x69, 0xB4),
                                         Color.FromArgb(0x00, 0xFF, 0x69, 0xB4),
                                         0, 0.5, 1, 0.5);
                    _accentTints.Add(c =>
                    {
                        ruleBrush.GradientStops[0].Color = WithAlpha(c, 0x55);
                        ruleBrush.GradientStops[1].Color = WithAlpha(c, 0x00);
                    });

                    var rule = new Border
                    {
                        Height = 1,
                        Margin = new Thickness(8, 1, 2, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = ruleBrush,
                    };
                    Grid.SetColumn(rule, 1);
                    capRow.Children.Add(rule);

                    RackList.Children.Add(capRow);
                    continue;
                }

                if (item is not StudioRackEntry e) continue;

                // Two states, both built now, swapped by IsVisible. See ApplyRowState.
                var content = new Grid();

                e.Strip = BuildRestingStrip(e);
                content.Children.Add(e.Strip);

                e.Tile = BuildArtTile(e);              // null when this module has no feature art
                if (e.Tile != null) content.Children.Add(e.Tile);

                e.Row = new RadioButton
                {
                    Tag = e.Key,
                    Content = content,
                };
                // DynamicResource, not TryFindResource: the rows are built in the constructor,
                // where this control is not yet attached and cannot resolve a keyed resource at
                // all. Without the theme the RadioButton keeps the Fluent default and the whole
                // rack renders as a column of radio circles.
                e.Row[!StyledElement.ThemeProperty] = new DynamicResourceExtension("RackEntryStyle");
                // BorderBrush is the template's TierRim (RackEntryStyle nulls it for free rows),
                // so a tiered row wears a permanent gold/diamond outline.
                if (e.Tier > 0) e.Row.BorderBrush = TierLiveryBrush(e.Tier);
                e.Row.Click += RackEntry_Click;
                // Right-click = quick-toggle, the same second gesture the dashboard tiles carry.
                // On the ROW, not on the dot: the dot is 7px, and the gesture belongs to the whole
                // entry. Rows with no Toggle fall through unhandled (Visuals, and Haptics until
                // its page lands with the master box the gesture has to go through).
                e.Row.PointerReleased += RackEntry_RightClick;
                // Instant, animation-free state swap. Wired to the checked change rather than
                // driven from SelectEntry so the swap can never drift out of step with IsChecked -
                // the RadioButton group also unchecks the outgoing row by itself.
                var entry = e;
                e.Row.IsCheckedChanged += (_, _) => ApplyRowState(entry);
                ApplyRowState(e);

                RackList.Children.Add(e.Row);
            }
        }

        // =====================================================================================
        //  row visuals — resting strip, active tile
        // =====================================================================================

        /// <summary>Height of a checked row that has a tile. The RESTING height (38) deliberately
        /// lives only in RackEntryStyle's Height setter, so there is one owner of it.</summary>
        private const double ActiveTileHeight = 56;

        /// <summary>
        /// The resting row: chip | caption | state dot.
        ///
        /// <para>The chip is 28px. Raw emoji floating beside text is the stock-toolkit look;
        /// contained, every row takes the same visual weight and the column reads as a designed
        /// rack rather than a bullet list.</para>
        /// </summary>
        private Grid BuildRestingStrip(StudioRackEntry e)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var chip = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                // The hue-wash under the emoji is the no-art fallback; RefreshRackArt overwrites
                // it with the module's feature plate when one resolves, which is exactly what WPF
                // shows for a module with art and without.
                Background = ChipHueWashBrush,
                Child = new TextBlock
                {
                    Text = e.Glyph,
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            chip[!Border.BorderBrushProperty] = new DynamicResourceExtension("GlassBorderBrush");

            // Tier livery on the chip: the metal on the chip's own frame, plus a tinted well
            // behind the glyph, so the mark survives even where the row rim is faint.
            // Presentation only — see StudioRackEntry.Tier.
            if (e.Tier > 0)
            {
                chip.BorderBrush = TierLiveryBrush(e.Tier);
                chip.Background = new SolidColorBrush(e.Tier >= 2
                    ? Color.FromRgb(0x1E, 0x33, 0x40)    // deep ice under diamond
                    : Color.FromRgb(0x3D, 0x33, 0x1E));  // dark amber under gold
            }
            e.Chip = chip;
            Grid.SetColumn(chip, 0);
            grid.Children.Add(chip);

            e.Label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(e.Label, 1);
            grid.Children.Add(e.Label);

            // v6.8.0 NEW pill: remove in 6.9.
            // Brain Drain came back from the dead in that release, so the rack row that hosts it
            // says so for one cycle. No keyed resource - the original note's point was that this
            // pill must not depend on a theme key that might be missing - but the three colours are
            // MIXED FROM THE MOD ACCENT rather than hard-coded pink, because a pink pill on a
            // purple mod's rack is the exact bug this lane is for.
            // The extra column is added to THIS row's grid only (the grid is per-row), and the dot
            // below takes the LAST column rather than a hard-coded 2, so a badged row keeps
            // icon | label | pill | dot in that order.
            if (string.Equals(e.Key, "braindrain", StringComparison.OrdinalIgnoreCase))
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var pillBack = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x69, 0xB4));
                var pillRim = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
                var pillText = new SolidColorBrush(Color.FromRgb(0xFF, 0x8F, 0xC7));
                _accentTints.Add(c =>
                {
                    pillBack.Color = WithAlpha(c, 0x33);
                    pillRim.Color = c;
                    pillText.Color = Lighten(c, 0.25);
                });

                var newPill = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = pillBack,
                    BorderBrush = pillRim,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "NEW",
                        FontFamily = new FontFamily("Consolas, Courier New"),
                        FontSize = 9,
                        FontWeight = FontWeight.Bold,
                        Foreground = pillText,
                    },
                };
                Grid.SetColumn(newPill, 2);
                grid.Children.Add(newPill);
            }

            if (e.Dot != null)
            {
                e.DotShape = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 2, 0),
                };
                // Last column, not a fixed 2: the NEW pill above inserts a column on the row it
                // badges. Unbadged rows still resolve to 2.
                Grid.SetColumn(e.DotShape, grid.ColumnDefinitions.Count - 1);
                grid.Children.Add(e.DotShape);
            }

            return grid;
        }

        /// <summary>
        /// The checked visual for a module with feature art: a 56px full-bleed tile. The plate is
        /// masked away toward the left with a scrim gradient over it, so the caption sits on flat
        /// panel colour and the plate is the thing your eye lands on.
        ///
        /// <para>Returns null when the module has no art — those rows keep the strip when checked
        /// and only get the RackEntryStyle fill, exactly as before.</para>
        ///
        /// <para>NEGATIVE MARGIN IS DELIBERATE. RackEntryStyle's ContentPresenter is inset
        /// 12,0,10,0; "full-bleed" means undoing that. The left inset is undone to 3 rather than 0
        /// so the template's 3px pink RowBar still shows beside the tile instead of being painted
        /// over by it (the ContentPresenter renders ON TOP of RowBar), which is also why the left
        /// corners are 3 and the right corners 8.</para>
        /// </summary>
        private Grid? BuildArtTile(StudioRackEntry e)
        {
            if (e.Art == null) return null;

            var corners = new CornerRadius(3, 8, 8, 3);

            // Height is EXPLICIT, not inherited from the row. RackEntryStyle's ContentPresenter is
            // VerticalAlignment=Center, so content is measured to its own desired size and then
            // centred - a stretch-height tile would collapse to the height of its caption.
            var tile = new Grid
            {
                Height = ActiveTileHeight,
                Margin = new Thickness(-9, 0, -10, 0),
                IsVisible = false,
            };

            // Painted as a Border BACKGROUND, not an Image child: a Border clips its own background
            // to CornerRadius, but does not clip child elements to it.
            var plate = new Border
            {
                CornerRadius = corners,
                Background = TilePlateBrush,
                OpacityMask = TileArtFadeMask,
            };
            e.Plate = plate;
            tile.Children.Add(plate);

            tile.Children.Add(new Border
            {
                CornerRadius = corners,
                Background = TileScrimBrush,
            });

            e.TileLabel = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
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
        /// Swaps a row between its resting strip and its 56px tile, and grows/shrinks the row to
        /// match. Instant by design: the rack is a quiet surface (PLAN §2.7), so this is an
        /// IsVisible flip and a Height write, with no clock for a motion kill-switch to reach.
        /// <para>Height is CLEARED rather than set back to 38 so RackEntryStyle's own Height setter
        /// takes the row again — one place owns the resting height.</para>
        /// <para>ponytail: WPF also lights a <c>PerimeterCometAdorner</c> on the checked tile here,
        /// gated twice on <c>MotionFx</c>. The gate has a twin now
        /// (<c>AmbientFxCanvas.Env.AllowAmbientLoops</c>); what is missing is
        /// ConditioningControlPanel/Controls/PerimeterCometAdorner.cs, a WPF Adorner with no
        /// Avalonia twin - the port carries no comet, the safe direction for a quiet surface.</para>
        /// </summary>
        private static void ApplyRowState(StudioRackEntry e)
        {
            if (e.Row == null || e.Tile == null) return;   // art-less rows: nothing to swap

            bool on = e.Row.IsChecked == true;
            e.Tile.IsVisible = on;
            if (e.Strip != null) e.Strip.IsVisible = !on;

            if (on) e.Row.Height = ActiveTileHeight;
            else e.Row.ClearValue(HeightProperty);
        }

        /// <summary>
        /// Hue-wash behind the emoji of a chip: 135°, warm to accent. Instance, because
        /// <see cref="RetintChrome"/> re-mixes it from the mod accent — see
        /// <see cref="RegisterChromeTints"/> for the warm end's derivation. Shared by every chip,
        /// which is the same multi-element sharing any resource brush does.
        /// </summary>
        private readonly LinearGradientBrush ChipHueWashBrush =
            Wash(Color.FromArgb(0x40, 0xFF, 0x7E, 0x6B), Color.FromArgb(0x1F, 0xFF, 0x69, 0xB4),
                 0, 0, 1, 1);

        /// <summary>The active tile's plate — the accent stand-in for the module's feature art.
        /// Shared and re-mixed by <see cref="RetintChrome"/>, like the chip wash.</summary>
        private readonly LinearGradientBrush TilePlateBrush =
            Wash(Color.FromArgb(0x66, 0xFF, 0x7E, 0x6B), Color.FromArgb(0x22, 0xFF, 0x69, 0xB4),
                 0, 0, 1, 1);

        /// <summary>
        /// Fades the active tile's plate out toward the left so it dissolves into the panel instead
        /// of ending on a hard edge under the caption. Alpha is all that matters in an OpacityMask;
        /// the black is arbitrary.
        /// </summary>
        private static readonly LinearGradientBrush TileArtFadeMask = new()
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.00),
                new GradientStop(Color.FromArgb(0x8C, 0, 0, 0), 0.40),
                new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), 0.75),
            },
        };

        /// <summary>
        /// Readability scrim over the tile: near-solid panel colour under the caption, gone by 80%
        /// so the right-hand plate stays clean.
        /// </summary>
        private static readonly LinearGradientBrush TileScrimBrush = new()
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x38), 0.00),
                new GradientStop(Color.FromArgb(0xF2, 0x1E, 0x1E, 0x38), 0.22),
                new GradientStop(Color.FromArgb(0x00, 0x1E, 0x1E, 0x38), 0.80),
            },
        };

        /// <summary>Two-stop gradient in relative coordinates. Avalonia points are ABSOLUTE unless
        /// declared relative, which is the quiet way a ported WPF gradient ends up painting one
        /// device pixel of colour and nothing else.</summary>
        private static LinearGradientBrush Wash(Color from, Color to, double x0, double y0, double x1, double y1) =>
            new()
            {
                StartPoint = new RelativePoint(x0, y0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(x1, y1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) },
            };

        // =====================================================================================
        //  selection
        // =====================================================================================

        private StudioRackEntry? EntryFor(string? key) =>
            key == null ? null
                        : _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        private void RackEntry_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string key)
                SelectEntry(key, announce: true);
        }

        // =====================================================================================
        //  right-click quick-toggle — the dashboard's gesture, on the rack
        // =====================================================================================

        /// <summary>
        /// Right-click on a rack row flips that module on/off without selecting it — left-click
        /// still owns selection, exactly as left-click still owns "open" on the wall.
        ///
        /// <para>Selection is deliberately untouched: a quick-toggle that also yanked the detail
        /// pane to a different module would make the gesture unusable for turning three things on
        /// in a row, and it would fire a FeatureOpened bark for a panel nobody asked to see.</para>
        /// </summary>
        private void RackEntry_RightClick(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton != MouseButton.Right) return;
            if (sender is not RadioButton rb || rb.Tag is not string key) return;
            var entry = EntryFor(key);
            if (entry?.Toggle == null) return;   // Visuals: no single on/off, so no gesture

            e.Handled = true;
            try { entry.Toggle(); }
            catch { /* a quick-toggle must never break the rack */ }

            // One beat late, at Normal priority: a refusal can undo the write (the haptics premium
            // gate flips IsChecked back) or never make it at all (the session-lock refusal writes
            // nothing). Reading AFTER lets the row react to what actually happened rather than to
            // what was asked for.
            Dispatcher.UIThread.Post(() =>
            {
                try { RefreshDots(); }
                catch { /* a state dot must never break a toggle */ }
            }, DispatcherPriority.Normal);
        }

        /// <summary>
        /// The rack keys <c>MainWindow.ToggleWallFeature</c> handles. Its cases were written
        /// against THESE keys ("Keys are the Studio rack's", MainWindow.Presets.cs), so the wall
        /// tile and the rack row flip one flag through one method: there is no second state store
        /// and no second set of service start/stop calls to fall out of step.
        /// </summary>
        private static readonly HashSet<string> WallToggleKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "flash", "video", "subliminal", "spiral", "pinkfilter", "bubbles",
            "bubblecount", "lockcard", "bouncingtext", "mindwipe", "braindrain",
        };

        /// <summary>
        /// ponytail: needs <c>MainWindow.ToggleWallFeature(key)</c> for the eleven wall modules —
        /// it owns the session-lock refusal, the per-feature service start/stop and the Save — and,
        /// for the three that predate the wall (Haptics, Scheduler, Ramp), a flip of the panel's
        /// own master checkbox so the panel's real handler and its premium gate run with it.
        /// ToggleWallFeature is a WPF-head path with no Core seam; the Haptics half is now blocked
        /// one level nearer, on <c>HapticsTabView.ChkHapticsEnabled_Changed</c> being a stub. An
        /// unknown key is a quiet no-op over there, so a typo costs a dead gesture, never a wrong
        /// write.
        /// </summary>
        private static void QuickToggle(string key) => _ = WallToggleKeys.Contains(key);

        /// <summary>
        /// Shows exactly one module. Idempotent, and safe to call for the already-selected key —
        /// re-selecting deliberately re-announces, because opening a feature popup twice used to
        /// fire its bark twice too.
        /// <para>ponytail: WPF also crossfades the incoming panel over 120ms, gated on
        /// <c>MotionFx.AllowTransitions</c>. The gate is reachable now -
        /// <c>CoreSettings.Current.MotionLevel != MotionLevel.Off</c>, which is MotionFx's own
        /// definition and what <c>AmbientFxCanvas.Env</c> reads for the ambient half - so what is
        /// left is only the 120ms fade itself. Still dropped here deliberately: an instant swap on
        /// a quiet surface is not a defect, and adding a clock is the change that needs a reason.</para>
        /// </summary>
        private void SelectEntry(string? key, bool announce)
        {
            var target = EntryFor(key);
            if (target == null) return;   // quiet no-op on an unknown key, by contract

            _selected = target.Key;

            foreach (var e in _entries)
            {
                bool on = ReferenceEquals(e, target);
                if (e.Row != null) e.Row.IsChecked = on;
                if (e.Host == null) continue;
                e.Host.IsVisible = on;
            }

            RefreshDetailHeader();
            RefreshDots();

            if (announce) Announce(target);
        }

        /// <summary>
        /// Repaints the detail pane's header for the current selection. Split out of
        /// <see cref="SelectEntry"/> so a mod switch can repaint it too: the title is mod-renamable
        /// and would otherwise keep the previous mod's feature name. Deliberately does not
        /// re-announce.
        ///
        /// <para><b>This collapses the header every time</b>, because every entry sets
        /// <see cref="StudioRackEntry.OwnHeader"/> — each feature page draws its own in-page hero.
        /// The branch is left intact rather than hard-collapsed: it is the same one Haptics has
        /// always taken, it still reads as what it means, and it is what brings the shared bar back
        /// for any page that loses its hero. The icon and title are still written so that bar is
        /// correct the moment it is shown again.</para>
        /// </summary>
        private void RefreshDetailHeader()
        {
            var target = EntryFor(_selected);
            if (target == null) return;

            if (DetailHeader != null)
            {
                DetailHeader.IsVisible = !target.OwnHeader;
                // The header echoes the row's tier livery, so the detail pane says "this is the
                // paid one" in the same metal the rack does. #2A2A46 = the header's own XAML
                // literal, restored verbatim for free modules.
                DetailHeader.BorderBrush = target.Tier > 0
                    ? TierLiveryBrush(target.Tier)
                    : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x46));
            }
            if (TxtDetailIcon != null) TxtDetailIcon.Text = target.Glyph;
            if (TxtDetailTitle != null) TxtDetailTitle.Text = LabelFor(target);
        }

        /// <summary>
        /// The tier livery for a paid module's chrome: gold = Tier 1, diamond = Tier 2, from the
        /// shared theme (Theme/Brushes.xaml) with a flat literal fallback in the established tier
        /// tones, because a missing brush must degrade to a duller rim, never to a throw inside row
        /// construction.
        /// </summary>
        private IBrush TierLiveryBrush(int tier)
        {
            var key = tier >= 2 ? "Tier2DiamondBorderBrush" : "Tier1GoldBorderBrush";
            return Res(key) as IBrush
                   ?? new SolidColorBrush(tier >= 2
                       ? Color.FromRgb(0x8F, 0xD4, 0xEF)
                       : Color.FromRgb(0xF0, 0xC2, 0x4B));
        }

        /// <summary>Keyed lookup that tolerates being called before this control is attached, which
        /// the constructor's rack build is.</summary>
        private object? Res(string key) => this.TryFindResource(key, out var v) ? v : null;

        /// <summary>
        /// The FeatureOpened bark, on exactly the keys the FeaturePopupWindow path used. Losing
        /// this silently kills 14 voiced rules per built-in mod, which is why it lives on the one
        /// path every reveal goes through instead of on the click handler.
        /// <para>ponytail: needs <c>App.Bark.NotifyFeatureOpened</c>, wired when BarkService moves
        /// to Core. The KEYS are already correct and are the load-bearing half — deriving them from
        /// a type name is what would fire into silence for Scheduler and Ramp.</para>
        /// </summary>
        private static void Announce(StudioRackEntry entry)
        {
            if (string.IsNullOrEmpty(entry.BarkFeature)) return;
        }

        // =====================================================================================
        //  labels + state dots
        // =====================================================================================

        /// <summary>
        /// Row captions go through <c>MainWindow.ModAwareLabel</c> exactly like the mosaic tiles
        /// and the popup titles, so a mod that renames "Flash Images" renames the rack row too. The
        /// shared section keys all carry a leading emoji and the rows draw their own icon, so the
        /// glyph is stripped — same rule as the dashboard cards.
        /// </summary>
        private void RefreshRackLabels()
        {
            foreach (var e in _entries)
            {
                var text = LabelFor(e);
                // Both states carry their own caption, so both get repainted. Missing the tile one
                // would leave the ACTIVE row - the only one anybody is reading - frozen in the
                // previous language or the previous mod's feature name.
                if (e.Label != null) e.Label.Text = text;
                if (e.TileLabel != null) e.TileLabel.Text = text;
            }
        }

        /// <summary>
        /// WPF's <c>MainWindow.ModAwareLabel(english, locKey)</c>, inlined: a <c>.ccpmod</c> that
        /// renames a feature renames the rack row too, and with no override the localized string
        /// wins. <see cref="CoreMods.MakeModAware"/> returns its argument unchanged when no mod
        /// layer is up, which is what makes the first branch fall through.
        /// </summary>
        private static string LabelFor(StudioRackEntry e)
        {
            var modText = CoreMods.MakeModAware(e.English);
            if (!string.IsNullOrEmpty(modText) && !string.Equals(modText, e.English, StringComparison.Ordinal))
                return StripLeadingGlyph(modText);

            var text = Loc.Get(e.LocKey);
            // LocalizationManager returns the key itself when it has no string for it; showing a
            // raw snake_case key on the rack is a failed render, so fall back to the English column.
            if (string.IsNullOrEmpty(text) || string.Equals(text, e.LocKey, StringComparison.Ordinal))
                text = e.English;
            return StripLeadingGlyph(text);
        }

        /// <summary>
        /// Local twin of <c>MainWindow.StripLeadingGlyph</c> (private over there, and a UserControl
        /// cannot reach it). Keep the two in step if either changes.
        /// </summary>
        private static string StripLeadingGlyph(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var i = 0;
            while (i < text.Length && !char.IsLetterOrDigit(text, i))
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
            return i > 0 && i < text.Length ? text.Substring(i) : text;
        }

        /// <summary>
        /// The lit dot's halo colour, re-mixed from the mod accent by <see cref="RetintChrome"/>.
        /// A fixed pink here is exactly the shape that goes on glowing pink forever under a purple
        /// mod.
        /// <para>Deviation from WPF: the effect is built per lit dot instead of hoisted to one
        /// shared <c>DropShadowEffect</c>. Avalonia effects are attached to a visual, and a handful
        /// of 7px shadows on a repaint is not a cost worth sharing an instance over.</para>
        /// </summary>
        private Color _dotGlowColor = Color.FromRgb(0xFF, 0x69, 0xB4);

        private void RefreshDots()
        {
            var on = Res("PinkBrush") as IBrush ?? Brushes.HotPink;
            var off = Res("TextDimBrush") as IBrush ?? Brushes.DimGray;

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

                // Same sentence on the whole row for the rows the right-click gesture can flip: the
                // dot is 7px of hover target, and after a quick-toggle the row the cursor is
                // already sitting on should be able to answer "did that take?" itself. Existing
                // keys only - no new string enters the nine language files for this.
                if (e.Row != null && e.Toggle != null)
                    ToolTip.SetTip(e.Row, tip);

                void Paint(Ellipse? dot)
                {
                    if (dot == null) return;
                    dot.Fill = lit ? on : off;
                    dot.Opacity = lit ? 1.0 : 0.35;
                    dot.Effect = lit
                        ? new DropShadowEffect
                        {
                            Color = _dotGlowColor,
                            BlurRadius = 7,
                            OffsetX = 0,
                            OffsetY = 0,
                            Opacity = 0.85,
                        }
                        : null;
                    ToolTip.SetTip(dot, tip);
                }
            }
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            RefreshRackLabels();
            ApplyDoorIcon();
            RefreshDots();
        }

        // =====================================================================================
        //  live state — the settings listener and the mod hook
        // =====================================================================================

        /// <summary>The AppSettings properties whose changes move a rack dot. Filtered rather than
        /// repainting on every PropertyChanged, because the session engine rewrites the ramped
        /// dials about once a second.</summary>
        private static readonly HashSet<string> DotProperties = new(StringComparer.Ordinal)
        {
            nameof(AppSettings.FlashEnabled),
            nameof(AppSettings.MandatoryVideosEnabled),
            nameof(AppSettings.SubliminalEnabled),
            nameof(AppSettings.SpiralEnabled),
            nameof(AppSettings.PinkFilterEnabled),
            nameof(AppSettings.BubblesEnabled),
            nameof(AppSettings.BubbleCountEnabled),
            nameof(AppSettings.LockCardEnabled),
            nameof(AppSettings.BouncingTextEnabled),
            nameof(AppSettings.MindWipeEnabled),
            nameof(AppSettings.BrainDrainEnabled),
            nameof(AppSettings.SchedulerEnabled),
            nameof(AppSettings.IntensityRampEnabled),
        };

        /// <summary>The instance the dot listener is currently on. Tracked rather than re-read
        /// from <see cref="CoreSettings.Current"/>, which after a restore is already a DIFFERENT
        /// object - detaching from that one leaves the old subscription live and the new one
        /// absent.</summary>
        private AppSettings? _hookedSettings;
        private bool _modHooked;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);

            // A cloud restore or a factory Reset SWAPS the settings instance out from under a rack
            // that lives for the whole session; without this the rack shows - and writes back to -
            // the discarded one for the rest of it.
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += OnSettingsCurrentReplaced;
            BindDotListener();

            if (!_modHooked)
            {
                CoreMods.ModChanged += OnModChanged;
                _modHooked = true;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= OnSettingsCurrentReplaced;
            UnbindDotListener();

            if (_modHooked) CoreMods.ModChanged -= OnModChanged;
            _modHooked = false;

            base.OnDetachedFromVisualTree(e);
        }

        private void BindDotListener()
        {
            UnbindDotListener();
            _hookedSettings = CoreSettings.Current;
            _hookedSettings.PropertyChanged += OnSettingsPropertyChanged;
        }

        private void UnbindDotListener()
        {
            if (_hookedSettings != null) _hookedSettings.PropertyChanged -= OnSettingsPropertyChanged;
            _hookedSettings = null;
        }

        /// <summary>Re-point the listener at the live instance and repaint. Posted rather than run
        /// inline: CurrentReplaced can be raised off the UI thread by the restore that swapped
        /// it.</summary>
        private void OnSettingsCurrentReplaced() => Dispatcher.UIThread.Post(() =>
        {
            try { BindDotListener(); RefreshDots(); }
            catch { /* a rebind must never take the rack down */ }
        });

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == null || !DotProperties.Contains(e.PropertyName)) return;
            // Marshalled because the writer may be the session engine's timer thread.
            Dispatcher.UIThread.Post(RefreshDots, DispatcherPriority.Normal);
        }

        /// <summary>ModChanged can be raised off the UI thread; marshal before touching brushes.
        /// This is the authoritative "the mod answers differently now" signal and the only one the
        /// rack gets on this head, so the accent and the captions ride on it.</summary>
        private void OnModChanged(object? sender, ModPackage mod) =>
            Dispatcher.UIThread.Post(RepaintModAwareChrome);
    }
}
