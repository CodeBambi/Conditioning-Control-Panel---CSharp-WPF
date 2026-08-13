using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls;
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
    /// <para><b>Quiet surface, with exactly one exception.</b> No ambient loop is REGISTERED for
    /// this view (PLAN §2.7) and none may be: the rack's selection visual is trigger-driven and
    /// the detail crossfade is a 120ms self-terminating clock gated on
    /// <see cref="MotionFx.AllowTransitions"/>. Velvet Kit 2 adds one perimeter comet on the
    /// CHECKED row's art tile — one adorner, one clock, capped at 24fps, moved by
    /// <see cref="SetTileComet"/> and torn off the moment the selection leaves. It is gated
    /// twice (transitions here, ambient loops inside the adorner) and degrades to a static lit
    /// outline rather than vanishing, so the motion kill-switch still has exactly one thing to
    /// reach and it is reachable from <see cref="RefreshTileComets"/>.</para>
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

            /// <summary>
            /// The tier this module is SOLD at: 0 = free, 1 = premium, 2 = Lab. Presentation
            /// only — the row wears the tier livery (gold/diamond, Resources\Theme\Brushes.xaml)
            /// permanently, whoever is looking; the refusal still belongs to the panel's own
            /// premium gate and <see cref="Services.TierGate"/>. Hand-maintained, like
            /// <see cref="BarkFeature"/>: there is no per-module verdict to derive it from
            /// without asking PatreonService a question ("is this ALLOWED?") that answers a
            /// different question than "what does it COST?".
            /// </summary>
            public int Tier;

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

            /// <summary>The comet lapping this row's tile outline while it is the checked row,
            /// or null. Exactly one entry can hold one at a time — see
            /// <see cref="SetTileComet"/>.</summary>
            public PerimeterCometAdorner? Comet;

            /// <summary>
            /// The two brushes that paint this row's feature art: the 28px resting chip and the
            /// 56px active tile. Held so <see cref="RefreshRackArt"/> can write a new
            /// <c>ImageSource</c> into the SAME brush on a mod switch.
            ///
            /// <para><b>Deliberately not frozen.</b> They used to be, which is precisely why a
            /// <c>.ccpmod</c> that overrides <c>features/flash.png</c> was invisible on the rack:
            /// a frozen brush cannot take the new bitmap, and swapping the brush object out would
            /// mean re-finding every Border that holds one. One decode at
            /// <see cref="RackArtDecodeWidth"/> feeds both (the chip centre-crops it), so the
            /// resolver's width-keyed cache serves this row once, not twice.</para>
            /// </summary>
            public ImageBrush? ChipArtBrush;
            public ImageBrush? TileArtBrush;
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

        /// <summary>
        /// One decode width for both rack art states. The chip is 28px and centre-crops what it
        /// is given; the active tile is 56px tall and right-anchored across ~230px. 256 covers
        /// the tile on a 2x display and is the ONLY width either state asks for, so
        /// <see cref="ModResourceResolver.ResolveImageDecoded"/>'s width-keyed cache holds one
        /// entry per module rather than two.
        /// </summary>
        private const int RackArtDecodeWidth = 256;

        /// <summary>
        /// Every code-built brush/effect on this page whose colour is the mod accent rather than
        /// a fixed house colour. Each build site registers a closure that re-writes its own
        /// colours from a Color; <see cref="RetintChrome"/> replays the whole list on a mod
        /// switch. A list of closures rather than a list of brushes because the sites disagree
        /// about WHAT to do with the accent (an alpha ramp, a hue-rotated partner, a
        /// lightened foreground) and that knowledge belongs next to the brush that needs it.
        ///
        /// <para>Registering also means the brush must NOT be frozen. That is the whole trade:
        /// a frozen brush is cheaper and silently never repaints.</para>
        ///
        /// <para>Two things are deliberately absent. <c>PerimeterCometAdorner</c>'s coral is a
        /// house constant owned by the adorner, and the tier livery (gold/diamond) is a
        /// commercial mark, not a theme colour.</para>
        /// </summary>
        private readonly List<Action<Color>> _accentTints = new();

        /// <summary>
        /// The accent this page paints with. <see cref="FxTheme.GlowColor"/> rather than a
        /// <c>TryFindResource</c> because it is a plain static read that already falls back to
        /// #FF69B4 on a missing key, so nothing here can be broken by a theme dictionary — which
        /// is the property the NEW pill's original literal was protecting.
        /// </summary>
        private static Color Accent
        {
            get { try { return FxTheme.GlowColor; } catch { return Color.FromRgb(0xFF, 0x69, 0xB4); } }
        }

        public StudioTabView()
        {
            InitializeComponent();
            // Before BuildRack: the per-row closures it registers are appended to these.
            RegisterChromeTints();
            BuildRack();
            // Land on the default without announcing it: nothing is on screen yet, and the
            // FeatureOpened bark belongs to the moment the user can actually see the panel.
            SelectEntry(DefaultRackKey, announce: false, animate: false);
            // Seeds the accent for whichever mod is ALREADY active at construction; the same
            // call from RepaintModAwareChrome handles every switch after that.
            RetintChrome();
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Registers the two page-level chrome objects that carry the accent. Row-level ones
        /// (the group rules, the NEW pill) register themselves as they are built.
        /// </summary>
        private void RegisterChromeTints()
        {
            // The hue-wash: 0x40 warm -> 0x1F accent, where "warm" is the accent hue-rotated
            // +38°. See HueRotate for why that single number reproduces the original
            // #FF7E6B/#FF69B4 pair - the wash was always one colour and a rotation, it was just
            // written out as two literals, which is what froze it pink.
            _accentTints.Add(c =>
            {
                ChipHueWashBrush.GradientStops[0].Color = WithAlpha(HueRotate(c, 38), 0x40);
                ChipHueWashBrush.GradientStops[1].Color = WithAlpha(c, 0x1F);
            });

            _accentTints.Add(c => DotGlow.Color = c);
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
                RefreshTileComets();
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
            // Art and colour as well as text. A mod ships THREE kinds of chrome for this page -
            // the feature names, the features/*.png tiles behind the rows and the accent the
            // code-built gradients are mixed from - and until the rack art pass was added here a
            // .ccpmod override of features/flash.png was invisible on the rack under any mod.
            // Each is independently guarded: a throw painting the art must not cost the labels.
            Try(RefreshRackLabels, nameof(RefreshRackLabels));
            Try(RefreshRackArt, nameof(RefreshRackArt));
            Try(ApplyDoorIcon, nameof(ApplyDoorIcon));
            Try(RetintChrome, nameof(RetintChrome));
            Try(RefreshDots, nameof(RefreshDots));       // after RetintChrome: it reads DotGlow
            Try(RefreshDetailHeader, nameof(RefreshDetailHeader));

            static void Try(Action a, string what)
            {
                try { a(); }
                catch (Exception ex) { App.Logger?.Debug("StudioTabView.RepaintModAwareChrome/{What}: {E}", what, ex.Message); }
            }
        }

        /// <summary>
        /// Re-resolves every row's feature art and writes it into the brushes the rows are
        /// already painted with. Mutates <c>ImageSource</c> in place rather than swapping brush
        /// objects: the chip Border and the tile's art layer were built in the constructor and
        /// nothing else holds a handle to them.
        ///
        /// <para>A null resolve KEEPS the current art. "The new mod ships no override and the
        /// embedded file failed to decode this time" must cost nothing; blanking the row would
        /// turn a transient decode failure into a permanently empty rack.</para>
        ///
        /// <para>Rows whose art was null at build time (the three art-less modules, or a module
        /// whose file was missing) have no brush to write into and stay on their emoji chip -
        /// a mod cannot conjure a tile that was never constructed. That is the one thing this
        /// pass does not fix, and it is a build-time shape, not a colour.</para>
        /// </summary>
        private void RefreshRackArt()
        {
            foreach (var e in _entries)
            {
                if (e.Art == null) continue;
                var art = LoadFeatureArt(e.Art, RackArtDecodeWidth);
                if (art == null) continue;
                if (e.ChipArtBrush is { IsFrozen: false } chip) chip.ImageSource = art;
                if (e.TileArtBrush is { IsFrozen: false } tile) tile.ImageSource = art;
            }
        }

        /// <summary>
        /// The page-header door medallion. Same file the nav rail's Studio door uses, so a mod
        /// that reskins the rail reskins this too instead of leaving the header on stock art.
        /// Null resolve leaves the XAML-declared pack:// default standing.
        /// </summary>
        private void ApplyDoorIcon()
        {
            var art = ModResourceResolver.ResolveImageDecoded("nav/door_studio.png", 128);
            if (art != null && ImgStudioDoorIcon != null) ImgStudioDoorIcon.Source = art;
        }

        /// <summary>
        /// Replays every registered accent closure with the mod's current accent. Cheap and
        /// idempotent - the closures only write colours - so it runs from the constructor as
        /// well, which is what seeds the rack for the mod that is already active at startup.
        /// </summary>
        private void RetintChrome()
        {
            var accent = Accent;
            foreach (var tint in _accentTints)
            {
                try { tint(accent); }
                catch (Exception ex) { App.Logger?.Debug("Studio accent tint: {E}", ex.Message); }
            }
        }

        // ---- accent maths --------------------------------------------------------------------

        /// <summary>The same colour at a different alpha.</summary>
        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>
        /// Blend toward white. <c>Lighten(#FF69B4, 0.25)</c> is <c>#FF8FC7</c> to the byte - which
        /// is exactly the relationship the NEW pill's two literals encoded, now expressed as the
        /// transform instead of as a second hard-coded pink.
        /// </summary>
        private static Color Lighten(Color c, double t)
        {
            static byte Mix(byte v, double f) => (byte)Math.Clamp(Math.Round(v + (255 - v) * f), 0, 255);
            return Color.FromArgb(c.A, Mix(c.R, t), Mix(c.G, t), Mix(c.B, t));
        }

        /// <summary>
        /// Rotate a colour's hue, keeping saturation and value. The chip hue-wash used to run
        /// coral <c>#FF7E6B</c> → pink <c>#FF69B4</c>; measured in HSV those two differ ONLY in
        /// hue, by +38° (S 0.580 vs 0.588, V 1.0 both). So the wash is not two colours, it is one
        /// accent and a hue rotation — and written that way it follows the mod instead of staying
        /// pink under a purple one, while still reproducing the original pair (to within a byte)
        /// for the default accent.
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
                () => App.Settings?.Current?.Haptics?.Enabled,
                // No wall key: the mosaic never carried Haptics (the premium rail chip does), and
                // its enable lives on the nested Haptics settings object. Flip the page's own
                // master box so MainWindow.ChkHapticsEnabled_Changed runs - including the
                // premium gate that reverts the box for a free account.
                toggle: () => FlipMasterCheckBox(PanelHaptics?.ChkHapticsEnabled),
                // The rack's one paid module (the premium gate above is the enforcement this
                // livery advertises). Same bar as the rail chip's RailLockBand: Tier 1.
                tier: 1);

            _layout.Add("st4_studio_group_timing");
            // Both fire the popup's single "SchedulerRamp" key so the existing rules keep firing.
            // Neither is a wall tile either, and neither drives a service directly (the 30s
            // SchedulerTimer_Tick and the session ramp read the flags), so the honest quick-toggle
            // is the panel's own enable box - it writes the flag and Saves in one place.
            Add("scheduler", "📅", null, "Scheduler", "section_scheduler", HostScheduler, PanelScheduler, "SchedulerRamp",
                () => App.Settings?.Current?.SchedulerEnabled,
                toggle: () => FlipMasterCheckBox(PanelScheduler?.Inner.ChkEnabled));
            Add("ramp", "📈", null, "Intensity Ramp", "section_intensity_ramp", HostRamp, PanelRamp, "SchedulerRamp",
                () => App.Settings?.Current?.IntensityRampEnabled,
                toggle: () => FlipMasterCheckBox(PanelRamp?.Inner.ChkEnabled));

            RenderRackRows();
            RefreshRackLabels();
            RefreshDots();

            void Add(string key, string glyph, string? art, string english, string locKey, UIElement? host,
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

                    // The rule is an ACCENT ramp, not a pink one: same alphas (0x55 -> 0x00), the
                    // hue taken from the active mod and re-taken on every switch. Registered
                    // unfrozen with _accentTints; see that field for why nothing here freezes.
                    var ruleBrush = new LinearGradientBrush(
                        Color.FromArgb(0x55, 0xFF, 0x69, 0xB4),
                        Color.FromArgb(0x00, 0xFF, 0x69, 0xB4),
                        0);
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
                // BorderBrush is the template's TierRim (RackEntryStyle nulls it for free
                // rows), so a tiered row wears a permanent gold/diamond outline.
                if (e.Tier > 0) e.Row.BorderBrush = TierLiveryBrush(e.Tier);
                e.Row.Click += RackEntry_Click;
                // Right-click = quick-toggle, the same second gesture the dashboard tiles carry.
                // On the ROW, not on the dot: the dot is 7px, and the gesture belongs to the
                // whole entry. Rows with no Toggle fall through unhandled (Visuals).
                e.Row.MouseRightButtonUp += RackEntry_RightClick;
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

            // RackArtDecodeWidth, not 56: the tile below asks for the same file at the same width,
            // so the two states share one resolver cache entry and one decode.
            var chipArt = LoadFeatureArt(e.Art, RackArtDecodeWidth);
            if (chipArt != null)
            {
                // NOT frozen - RefreshRackArt writes the mod's override into this exact brush.
                var brush = new ImageBrush(chipArt)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                };
                e.ChipArtBrush = brush;
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
            // Tier livery on the chip: the metal on the chip's own frame (and, for the art-less
            // chips, a tinted well behind the glyph), so the mark survives even where the row
            // rim is faint. Presentation only — see StudioRackEntry.Tier.
            if (e.Tier > 0)
            {
                chip.BorderBrush = TierLiveryBrush(e.Tier);
                if (chipArt == null)
                    chip.Background = new SolidColorBrush(e.Tier >= 2
                        ? Color.FromRgb(0x1E, 0x33, 0x40)    // deep ice under diamond
                        : Color.FromRgb(0x3D, 0x33, 0x1E));  // dark amber under gold
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

            // v6.8.0 NEW pill: remove in 6.9
            // Brain Drain came back from the dead in this release, so the rack row that
            // hosts it says so for one cycle. Still no TryFindResource - the original note's
            // point was that this pill must not depend on a theme key that might be missing -
            // but the three colours are now MIXED FROM THE MOD ACCENT rather than hard-coded
            // pink, because a pink pill on a purple mod's rack is the exact bug this lane is
            // for. Accent comes from FxTheme, which is a static read with its own #FF69B4
            // fallback, so a missing key still cannot break it. The lighten factor reproduces
            // the old #FF8FC7 from the old #FF69B4 to the byte.
            // The extra column is added to THIS row's grid only (the grid is per-row), and
            // the dot below takes the last column rather than a hard-coded 2, so a
            // badged row keeps icon | label | pill | dot in that order.
            if (string.Equals(e.Key, "braindrain", StringComparison.OrdinalIgnoreCase))
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
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
                // Last column, not a fixed 2: the NEW pill above inserts a column on the row
                // it badges (v6.8.0). Unbadged rows still resolve to 2.
                Grid.SetColumn(e.DotShape, grid.ColumnDefinitions.Count - 1);
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
            var art = LoadFeatureArt(e.Art, RackArtDecodeWidth);
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

            // NOT frozen - RefreshRackArt writes the mod's override into this exact brush on a
            // mod switch. The mask and the scrim below stay frozen: neither is mod-aware.
            var artBrush = new ImageBrush(art)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Right,
                AlignmentY = AlignmentY.Center,
            };
            e.TileArtBrush = artBrush;

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

            SetTileComet(e, on);
        }

        // =====================================================================================
        //  active-tile comet (Velvet Kit 2 · FX lane A)
        // =====================================================================================

        /// <summary>Corner radius the comet follows. Mirrors <see cref="BuildArtTile"/>'s
        /// <c>CornerRadius(3, 8, 8, 3)</c> — the adorner takes one radius, and 8 is the one the
        /// eye reads (the 3s exist only to leave the template's RowBar showing).</summary>
        private const double ActiveTileCornerRadius = 8;

        /// <summary>Lap time for the active tile. Faster than the adorner's 9s default on
        /// purpose: this is a small tile and the ONE thing on the rack that is meant to look
        /// live, not a large card breathing in the background.</summary>
        private const double ActiveTileLapSeconds = 4.0;

        /// <summary>
        /// Puts the perimeter comet on the checked row's art tile and takes it off everything
        /// else — exactly one comet exists on this surface at a time, which is the whole reason
        /// the rack can carry an ambient FX at all (PLAN §2.7's "quiet surface" rule is about not
        /// having eighty of them, and the tiering in the Motion Lab spec reserves animated borders
        /// for the active/hero surface).
        ///
        /// <para>Gated on <see cref="MotionFx.AllowTransitions"/> here; the adorner ALSO checks
        /// <see cref="MotionFx.AllowAmbientLoops"/> itself and degrades to a static lit outline
        /// rather than vanishing when the tier or the motion level says no loops.</para>
        ///
        /// <para>A null return from <c>Attach</c> is expected, not a failure: rows are built in
        /// the constructor, long before this control is in a visual tree with an adorner layer.
        /// <see cref="RefreshTileComets"/> from <c>Loaded</c>/<c>OnTabShown</c> is what picks the
        /// default selection up.</para>
        /// </summary>
        private static void SetTileComet(StudioRackEntry e, bool on)
        {
            try
            {
                bool allowed;
                try { allowed = MotionFx.AllowTransitions; } catch { allowed = false; }

                if (!on || e.Tile == null || !allowed)
                {
                    PerimeterCometAdorner.Detach(e.Comet);
                    e.Comet = null;
                    return;
                }
                if (e.Comet != null) return;                  // already lit — leave its clock alone
                e.Comet = PerimeterCometAdorner.Attach(e.Tile, ActiveTileCornerRadius,
                                                       ActiveTileLapSeconds);
            }
            catch (Exception ex) { App.Logger?.Debug("Studio tile comet: {E}", ex.Message); }
        }

        /// <summary>
        /// Re-asserts the comet for every row. Idempotent, and cheap when nothing has changed.
        /// Called on Loaded (the adorner layer only exists from then on) and on every door open
        /// (so a motion-level change made in Settings takes effect on the way back in).
        /// </summary>
        private void RefreshTileComets()
        {
            foreach (var entry in _entries)
                SetTileComet(entry, entry.Row?.IsChecked == true);
        }

        /// <summary>
        /// Resolves a <c>Resources/features/</c> PNG at the size it will actually be drawn at.
        /// The source tiles are ~1024px square; handing WPF twelve of those undecimated for a
        /// 28px chip is the classic way to spend 50MB on a sidebar.
        ///
        /// <para><b>Through the resolver, not through a hand-built pack:// URI.</b> This method
        /// used to concatenate <c>pack://application:,,,/Resources/features/</c> itself, which
        /// meant the rack was the ONE surface that ignored mod art: a <c>.ccpmod</c> shipping
        /// <c>resources/features/flash.png</c> repainted the dashboard tile, the feature page and
        /// the popup chrome, and left the rack row on the built-in picture under every mod.
        /// <see cref="ModResourceResolver.ResolveImageDecoded"/> is the same event-skin → mod →
        /// embedded chain every other art call site already uses, and it caches per (skin, mod,
        /// path, width) so repeat resolves on a mod switch cost nothing.</para>
        ///
        /// <para>Null in, null out - and a resolve failure is null as well rather than a throw:
        /// a missing art file must cost the row its picture, not the whole rack. Callers treat
        /// null on a REPAINT as "keep what is on screen" - see <see cref="RefreshRackArt"/>.</para>
        /// </summary>
        private static ImageSource? LoadFeatureArt(string? file, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            try
            {
                return ModResourceResolver.ResolveImageDecoded("features/" + file, decodePixelWidth);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Studio rack art '{File}': {E}", file, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Hue-wash behind the emoji of an art-less module's chip: 135°, warm to accent.
        /// Instance, and NOT frozen, because <see cref="RetintChrome"/> re-mixes it from the mod
        /// accent - see <see cref="RegisterChromeTints"/> for the warm end's derivation.
        /// Shared by the three art-less chips, which is the same multi-element sharing any
        /// resource brush does.
        /// </summary>
        private readonly LinearGradientBrush ChipHueWashBrush = new(
            Color.FromArgb(0x40, 0xFF, 0x7E, 0x6B),
            Color.FromArgb(0x1F, 0xFF, 0x69, 0xB4),
            new Point(0, 0), new Point(1, 1));

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
            {
                DetailHeader.Visibility = target.OwnHeader ? Visibility.Collapsed : Visibility.Visible;
                // The header echoes the row's tier livery, so the detail pane says "this is
                // the paid one" in the same metal the rack does. #2A2A46 = the header's own
                // XAML literal, restored verbatim for free modules.
                DetailHeader.BorderBrush = target.Tier > 0
                    ? TierLiveryBrush(target.Tier)
                    : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x46));
            }
            if (TxtDetailIcon != null) TxtDetailIcon.Text = target.Glyph;
            if (TxtDetailTitle != null) TxtDetailTitle.Text = LabelFor(target);
        }

        /// <summary>
        /// The tier livery for a paid module's chrome: gold = Tier 1, diamond = Tier 2, from
        /// the shared theme (Resources\Theme\Brushes.xaml) with a flat literal fallback in the
        /// established tier tones, because a missing brush must degrade to a duller rim, never
        /// to a throw inside row construction.
        /// </summary>
        private Brush TierLiveryBrush(int tier)
        {
            var key = tier >= 2 ? "Tier2DiamondBorderBrush" : "Tier1GoldBorderBrush";
            return (Brush?)TryFindResource(key)
                   ?? new SolidColorBrush(tier >= 2
                       ? Color.FromRgb(0x8F, 0xD4, 0xEF)
                       : Color.FromRgb(0xF0, 0xC2, 0x4B));
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
        /// The lit dot's halo. Hoisted to one instance rather than allocated per dot per repaint
        /// (RefreshDots runs on every settings write that moves a dot, on every selection and on
        /// every Studio show). Still not an animation, so there is nothing here for the motion
        /// kill-switch to reach.
        /// <para>Instance and unfrozen now: the halo is the mod accent, and a frozen effect is
        /// exactly the shape that goes on glowing pink forever under a purple mod.</para>
        /// </summary>
        private readonly System.Windows.Media.Effects.DropShadowEffect DotGlow =
            new()
            {
                Color = Color.FromRgb(0xFF, 0x69, 0xB4),
                BlurRadius = 7,
                ShadowDepth = 0,
                Opacity = 0.85,
            };

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

                // Same sentence on the whole row for the rows the right-click gesture can flip:
                // the dot is 7px of hover target, and after a quick-toggle the row the cursor is
                // already sitting on should be able to answer "did that take?" itself. Existing
                // keys only - no new string enters the nine language files for this.
                if (e.Row != null && e.Toggle != null)
                    e.Row.ToolTip = tip;

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
            // The header medallion is mod art like the nav rail's. Done here rather than in the
            // ctor for symmetry with the repaint path; the XAML default covers the gap.
            try { ApplyDoorIcon(); }
            catch (Exception ex) { App.Logger?.Debug("StudioTabView door icon: {E}", ex.Message); }
            RefreshDots();
            // Only now does an adorner layer exist for the rows built in the constructor.
            RefreshTileComets();
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
