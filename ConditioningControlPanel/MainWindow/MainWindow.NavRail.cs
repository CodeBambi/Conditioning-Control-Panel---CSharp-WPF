using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The nav rail's collapsed/expanded behaviour (owner ask, 2026-08-11: "the sidebar takes too
    /// much space, can we collapse the names and keep only the icons? on hover we can bring it up
    /// and uncollapse it - keep it there for 1 sec before collapsing it back, or on any click
    /// elsewhere").
    ///
    /// <para><b>2026-08-13:</b> the "1 sec" half of that is gone - see
    /// <see cref="NavRailCollapseAnimMs"/>. The rail now starts shutting on the frame the pointer
    /// leaves it; only the click-elsewhere and hover-to-open halves are unchanged.</para>
    ///
    /// <para><b>Why the expanded rail OVERLAYS the page instead of widening the layout.</b> The
    /// whole UI lives in a <c>Viewbox Stretch="Fill"</c> over a fixed <c>DesignCanvas</c>, and the
    /// content column is deliberately pinned at the width every tab was authored against (Phase 1
    /// grew the canvas rather than shrinking that column, because the layouts clip silently
    /// otherwise - fx-inventory §12.8). If the rail widened the canvas on hover, the Viewbox would
    /// re-scale EVERY tab by 176/56 on each mouse-over: the whole app would visibly breathe. So the
    /// rail is sized for the collapsed 56px in the grid and spans both columns, painting over the
    /// page when it opens. Nothing below it ever relayouts.</para>
    ///
    /// <para><b>Why the page still is not buried.</b> Overlaying is mandatory, but it does not have
    /// to be free of charge to the page. Canvas column 0 reserves 96px - the 56px rail plus a 40px
    /// permanent gutter of bare canvas that the left-aligned rail Border refuses to fill - so the
    /// flyout spends its first 40px on dead space before it touches a pixel of live UI.
    /// <see cref="NavRailExpandedWidth"/> carries the arithmetic and the label floor.</para>
    ///
    /// <para><b>2026-08-12, the medallion rail</b> (UI-polish-2 CONTRACT, "Nav rail interaction
    /// spec"). Shut, the rail is a column of 44px hue tiles spread evenly down its height; open,
    /// each tile grows to 56px with its 44px art and its name rises into place beside it, row
    /// after row. Everything that moves is animated from this file - <see cref="ApplyNavDoorRows"/>
    /// next to the width tween that drives it - because a stagger is per row and a Style cannot
    /// hold seven different BeginTimes. MainWindow.xaml owns the shape and the hues; this file
    /// owns the motion; ChromeFx keeps owning which door is active.</para>
    ///
    /// <para>Collapsing also shuts the accordion. Seven door icons read as a rail; seven door icons
    /// with four unlabelled child icons wedged among them reads as noise, and the child rows are
    /// exactly the ones whose meaning lives in the label. <see cref="_expandedDoor"/> is left
    /// untouched, so opening the rail restores the door the user was in.</para>
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Icon-only width. Also the width the rail rows centre their icons in - the
        /// door rows with a 56px first grid column, the entry rows with a 21+14+21 padding
        /// (MainWindow.xaml) - so it cannot move without re-centring both. DesignCanvas column 0
        /// is WIDER than this on purpose (see below); what must stay in step is that the column
        /// is never NARROWER, or the page slides under the collapsed rail.</summary>
        private const double NavRailCollapsedWidth = 56;

        /// <summary>
        /// Open width. History: 190 (the width the rail rows were authored against) -> 176 when
        /// the owner flagged that the flyout buried the left edge of the page it opens over, half
        /// the fix being canvas column 0 growing to 96 so the first 40px of the flyout land on a
        /// permanent empty gutter (MainWindow.xaml, DesignCanvas) -> 236 on 2026-08-12, which is
        /// the number the UI-polish-2 CONTRACT froze for the medallion rail. True overlay over
        /// live UI is back up to 236-96 = 140px, bought deliberately: the door rows carry a 16px
        /// ExtraBold name in a 56px medallion column now, and 176 could not hold one.
        ///
        /// <para>The floor is still set by the longest ENTRY label in the nine shipped languages,
        /// not by the doors: an entry row spends 21 (left padding) + 14 (icon) + 6 (icon margin)
        /// + 8 (right padding) = 49px before its text starts, and the worst string is es
        /// "Entrenador de Parpadeo" at 122px (fr and ru sit at 120) - 171px. The door labels get
        /// a Viewbox with StretchDirection=DownOnly instead, so a long locale shrinks the name
        /// rather than running into the Border's ClipToBounds: the labels live in a horizontal
        /// layout that measures at infinite width, where TextTrimming cannot save a row - it just
        /// gets cut with no ellipsis.</para>
        /// </summary>
        private const double NavRailExpandedWidth = 236;

        /// <summary>Width tween, and the tween every medallion part rides (CONTRACT: 190ms
        /// QuadOut). The door labels get their own, longer, staggered pair below.</summary>
        private const int NavRailAnimMs = 190;

        /// <summary>
        /// The way OUT, 2026-08-13 (owner: the rail "stays open way too long after you move off
        /// it"). Opening is a reveal and keeps its 190ms; closing is the rail getting out of the
        /// way, so it runs a little quicker.
        ///
        /// <para><b>What actually changed is that there is no longer a WAIT in front of it.</b>
        /// Until now MouseLeave started a 1000ms DispatcherTimer and the collapse happened on its
        /// Tick, so the rail sat over the page for a full second after the pointer had gone. The
        /// timer is deleted, not shortened: MouseLeave collapses on the spot and this constant is
        /// the whole of the delay the user can perceive.</para>
        ///
        /// <para><b>Why that does not flicker.</b> The leave is measured on <c>NavSidebar</c>,
        /// the rail container, and WPF raises MouseLeave only when the pointer leaves an
        /// element's entire subtree - crossing from a door to its accordion entries, or between
        /// two doors, never fires it. A pointer wobbling across the rail's edge alternates
        /// MouseLeave/MouseEnter, and <see cref="SetNavRailExpanded"/> early-outs whenever the
        /// state asked for is the state it is already in, so each real crossing starts at most one
        /// tween and a re-entry mid-collapse simply re-targets the same Width clock.</para>
        /// </summary>
        private const int NavRailCollapseAnimMs = 150;

        // ===================== medallion door rows (CONTRACT, 2026-08-12) =====================
        // Sizes are animated between these two states; MainWindow.xaml authors the COLLAPSED
        // value on every element, so a rail whose Loaded hook never ran still renders correctly
        // shut. Nothing here changes the row PITCH: door rows are a fixed 64px tall in both
        // states (see the NavDoorButton style), only what sits inside them grows.

        /// <summary>The rounded (r12) tile behind the door art. Open was 56 - the full collapsed
        /// strip, flush with the window's left edge - until the 3px accent window frame landed
        /// (2026-08-13, GlassWindowEdge in MainWindow.xaml): a 56px tile sat underneath the frame
        /// and read as crowding it. 50 keeps the tile centred on the frozen 56px strip (moving
        /// the centre sideways on expand is a jitter nobody asked for) with 3px clear each side,
        /// so its left edge meets the frame's inner edge instead of disappearing under it. It
        /// still reads as the "64px medallion zone", because the door name starts at 64 either
        /// way.</summary>
        private const double NavDoorTileCollapsed = 44;
        private const double NavDoorTileExpanded = 50;

        /// <summary>The art itself - a Viewbox over the native 64px medallion, so its rounded
        /// clip scales with it and its RenderTransform stays free for ChromeFx's hover nudge.
        ///
        /// <para>2026-08-13 (owner: the door images are too small and ringed in grey). Was 28 shut
        /// / 44 open, i.e. 8px of grey plate plus a 1px grey hairline all the way round a 28px
        /// thumbnail. The tile did not grow - the art did, to 2px inside it in both states, and
        /// the plate's idle outline went Transparent in MainWindow.xaml so the only ring left is
        /// the hue one RefreshNavDoorActive paints on the door you are in. Row pitch, tile size,
        /// hit target and rail width are all untouched.</para>
        ///
        /// <para>Sized as tile minus 2px inset per side: 44 shut -> 40, and 50 open (the tile
        /// shrank 56 -> 50 the same day for the GlassWindowEdge accent frame, see the tile note
        /// above) -> 46. The inset is what keeps the tile's r12 corners (and the active hue ring,
        /// painted on the tile's own 1px edge) from being covered by the art sitting on top of
        /// them; the art is 64px native, so 46 DIP stays comfortably under upscale.</para></summary>
        private const double NavDoorIconCollapsed = 40;
        private const double NavDoorIconExpanded = 46;

        /// <summary>Radial hue halo behind the tile. 64 shut is exactly the 56px strip plus the
        /// 4px each side where the gradient has already reached zero alpha, so the rail's
        /// ClipToBounds cuts nothing visible; 84 open does get clipped at the window's left edge,
        /// which is the one edge a glow is allowed to run off.</summary>
        private const double NavDoorGlowCollapsed = 64;
        private const double NavDoorGlowExpanded = 84;

        /// <summary>Halo opacity. Idle rows show a hint once the rail is open, the active row
        /// wears it properly, and a shut rail shows none on the idle rows at all (six haloes in
        /// a 56px strip is soup, not a rail).</summary>
        private const double NavDoorGlowActive = 0.50;
        private const double NavDoorGlowOpen = 0.20;
        private const int NavDoorGlowFadeMs = 160;

        /// <summary>CONTRACT: "inactive tiles Opacity .85". On the TILE, deliberately not on the
        /// Button - StartNavDoorHeaderPulse animates Button.Opacity and then writes a local 1.0
        /// when it ends, which would outrank any setter of ours for the rest of the session.</summary>
        private const double NavDoorTileIdleOpacity = 0.85;

        /// <summary>Big door name: rises this far, one row after another.</summary>
        private const double NavDoorLabelRise = 14;
        private const int NavDoorLabelFadeMs = 220;
        private const int NavDoorLabelSlideMs = 260;
        private const int NavDoorLabelStaggerMs = 30;

        /// <summary>Marks the label host grid in MainWindow.xaml (set by the NavDoorLabelHost
        /// style). Two jobs: it is how <see cref="CacheNavDoorRows"/> finds the host, and how
        /// <see cref="CacheNavRailParts"/> knows to leave the door names OUT of the global label
        /// fade - they are driven per row, with a stagger, and two clocks on one Opacity is a
        /// race whose winner is whichever call happened to be last.</summary>
        private const string NavDoorLabelHostTag = "navdoorlabel";

        private bool _navRailExpanded;
        private bool _navRailReady;

        /// <summary>Outstanding <see cref="HoldNavRailOpen"/> claims. While this is above zero the
        /// rail ignores every collapse trigger - the pointer leaving and the
        /// click-elsewhere - because the caller is showing the user something IN the rail and a
        /// rail that shuts underneath a spotlight is worse than no spotlight at all. Counted, not
        /// a bool: a tutorial step and the palette can be up at once, and the first one to finish
        /// must not release the other's hold.</summary>
        private int _navRailHoldCount;

        /// <summary>Every label in the rail, cached once. Faded rather than collapsed: a
        /// Visibility flip would re-measure the door panels mid-tween and fight the accordion's
        /// own Height animation.</summary>
        private readonly List<TextBlock> _navRailLabels = new();

        /// <summary>Rail buttons, so icons can centre themselves when the labels are gone.</summary>
        private readonly List<ButtonBase> _navRailButtons = new();

        /// <summary>The seven door rows in rail order - the stagger index IS this order.</summary>
        private readonly List<NavDoorRow> _navDoorRows = new();

        /// <summary>Door names, excluded from <see cref="_navRailLabels"/>. See
        /// <see cref="NavDoorLabelHostTag"/>.</summary>
        private readonly HashSet<TextBlock> _navDoorLabelTexts = new();

        /// <summary>
        /// One door row's animatable parts, resolved once. The parts live in the Button's
        /// CONTENT (MainWindow.xaml authors them per door, because the art and the loc key are
        /// per door) except <see cref="ActiveBar"/>, which is the shared template's
        /// <c>NavActiveBar</c> - the part ChromeFx's ApplyNavActiveGlow drives BY NAME.
        /// </summary>
        private sealed class NavDoorRow
        {
            internal Ellipse Glow = null!;
            internal Border Tile = null!;
            internal Viewbox Icon = null!;
            internal FrameworkElement LabelHost = null!;
            internal TranslateTransform LabelSlide = null!;
            internal TextBlock? Label;

            /// <summary>The door's hue, read straight back off the Button's BorderBrush - the
            /// one place MainWindow.xaml states it, and already what NavActiveBar paints.</summary>
            internal Brush Hue = Brushes.Transparent;

            internal FrameworkElement? ActiveBar;
            internal bool Active;
        }

        /// <summary>
        /// Called once from the Loaded handler, after templates are applied - the label/button
        /// caches are a visual-tree walk and find nothing before that.
        /// </summary>
        private void InitializeNavRail()
        {
            try
            {
                if (_navRailReady || NavSidebar == null) return;

                // Doors first: it seeds _navDoorLabelTexts, which the walk below reads.
                CacheNavDoorRows();
                CacheNavRailParts(NavSidebar);

                // The medallion art is mod-aware from the first frame; MainWindow.xaml's literal
                // pack Sources are the fallback, not the answer. Repaints on ModChanged too - see
                // the subscription block in MainWindow.xaml.cs.
                ApplyDoorArt();

                NavSidebar.MouseEnter += (_, __) => SetNavRailExpanded(true);

                // No timer, by owner call (NavRailCollapseAnimMs): leaving the rail IS the
                // collapse trigger. NavSidebar is the whole rail, so this does not fire while the
                // pointer is travelling between doors or down into an open accordion.
                NavSidebar.MouseLeave += (_, __) =>
                {
                    if (_navRailHoldCount > 0) return;
                    SetNavRailExpanded(false);
                };

                // "or on any click elsewhere". Preview, so it lands even when the click is
                // handled by whatever it hit. A click INSIDE the rail is a navigation and keeps
                // the rail open - MouseLeave takes over the moment the pointer goes.
                PreviewMouseDown += (_, __) =>
                {
                    if (NavSidebar.IsMouseOver) return;
                    if (_navRailHoldCount > 0) return;
                    SetNavRailExpanded(false);
                };

                _navRailReady = true;
                SetNavRailExpanded(false, animate: false);
            }
            catch (Exception ex)
            {
                // A rail that fails to initialise stays exactly as MainWindow.xaml authored it:
                // 56px wide, 44px tiles, no door names and no hover flyout. Degraded, not
                // broken - every door still navigates and still has its tooltip.
                App.Logger?.Warning(ex, "InitializeNavRail failed; rail stays as authored (shut, icon-only)");
            }
        }

        /// <summary>
        /// Decode cap for a door medallion. The art is authored at 64px native and the tile never
        /// exceeds 56 DIP, so 128 is 2x headroom for a high-DPI display - and it is the reason
        /// this goes through the DECODED resolver: a mod is free to ship a 2048px door_play.png,
        /// and ResolveImage would hand back every pixel of it for a 44px tile.
        /// </summary>
        private const int NavDoorArtDecodeWidth = 128;

        /// <summary>
        /// Points the seven door medallions at the active mod's art. The paths are the mod
        /// compatibility surface (nav/door_*.png) and are never renamed; a mod that ships none of
        /// them resolves straight back to the embedded copies, which is byte-identical to what
        /// MainWindow.xaml already authored.
        ///
        /// <para><b>A null resolve leaves the existing Source alone.</b> An empty nav rail is the
        /// worst failure this file can produce (missing nav art has crashed this app before), and
        /// the authored pack URI is always a valid fallback - so "could not resolve" means "keep
        /// what is on screen", never "blank it".</para>
        ///
        /// <para>Safe to call before <see cref="InitializeNavRail"/> has cached anything: it only
        /// touches x:Named Images, which exist from InitializeComponent onward.</para>
        /// </summary>
        private void ApplyDoorArt()
        {
            try
            {
                var doors = new (Image? Img, string Path)[]
                {
                    (ImgDoorHome,      "nav/door_home.png"),
                    (ImgDoorStudio,    "nav/door_studio.png"),
                    (ImgDoorCompanion, "nav/door_companion.png"),
                    (ImgDoorPlay,      "nav/door_play.png"),
                    (ImgDoorYou,       "nav/door_you.png"),
                    (ImgDoorLibrary,   "nav/door_library.png"),
                    (ImgDoorSettings,  "nav/door_settings.png"),
                };

                foreach (var (img, path) in doors)
                {
                    if (img == null) continue;
                    var art = ModResourceResolver.ResolveImageDecoded(path, NavDoorArtDecodeWidth);
                    if (art != null) img.Source = art;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "ApplyDoorArt failed; nav rail keeps its embedded medallions");
            }
        }

        private void CacheNavRailParts(DependencyObject root)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                // The big door names are driven per row with a stagger (SetNavRailExpanded), so
                // they must NOT also be in the global label fade - see NavDoorLabelHostTag.
                if (child is TextBlock door && _navDoorLabelTexts.Contains(door)) continue;
                if (child is TextBlock tb) _navRailLabels.Add(tb);
                else if (child is ButtonBase b) _navRailButtons.Add(b);
                CacheNavRailParts(child);
            }
        }

        /// <summary>
        /// Resolves the seven medallion rows. The parts are found BY TYPE among the door
        /// Button's direct content children (glow = the Ellipse, tile = the Border, icon = the
        /// Viewbox, label host = the one tagged <see cref="NavDoorLabelHostTag"/>), which is the
        /// same shape ChromeFx's NudgeNavIcon relies on - it takes the first Image-or-Viewbox
        /// among those same children as the icon to nudge. Keep both contracts in mind before
        /// adding a fifth child to a door in MainWindow.xaml.
        ///
        /// <para>A door whose content does not match is skipped rather than fixed up: the rail
        /// then renders that row exactly as MainWindow.xaml authored it (collapsed sizes, no
        /// name), which is legible, instead of throwing on the Loaded path.</para>
        /// </summary>
        private void CacheNavDoorRows()
        {
            foreach (var d in NavDoorMap)
            {
                var btn = NavDoorParts(d.Door).Header;
                if (btn?.Content is not Panel content) continue;

                var kids = content.Children.OfType<FrameworkElement>().ToList();
                var glow = kids.OfType<Ellipse>().FirstOrDefault();
                var tile = kids.OfType<Border>().FirstOrDefault();
                // The label's own Viewbox is nested inside the host, so this is the icon's.
                var icon = kids.OfType<Viewbox>().FirstOrDefault();
                var host = kids.FirstOrDefault(k => (k.Tag as string) == NavDoorLabelHostTag);
                if (glow == null || tile == null || icon == null || host is not Panel hostPanel)
                {
                    App.Logger?.Debug("CacheNavDoorRows: door {Door} has no medallion content", d.Door);
                    continue;
                }

                // Built here, not in XAML: a RenderTransform declared in a Style setter is one
                // shared (and frozen) instance across all seven rows, which is the one thing a
                // per-row stagger cannot survive.
                var slide = new TranslateTransform(0, NavDoorLabelRise);
                hostPanel.RenderTransform = slide;

                var row = new NavDoorRow
                {
                    Glow = glow,
                    Tile = tile,
                    Icon = icon,
                    LabelHost = hostPanel,
                    LabelSlide = slide,
                    Label = hostPanel.Children.OfType<Viewbox>().FirstOrDefault()?.Child as TextBlock,
                    Hue = btn.BorderBrush ?? Brushes.Transparent,
                };
                if (row.Label != null) _navDoorLabelTexts.Add(row.Label);
                glow.Fill = BuildNavDoorGlow(btn.BorderBrush as SolidColorBrush);

                // The active bar is a template part; ChromeFx flips its Visibility and nothing
                // else, so that flip is the only signal this file has for "you are here". Watch
                // it rather than duplicating ChromeFx's notion of the active tab.
                btn.ApplyTemplate();
                row.ActiveBar = btn.Template?.FindName("NavActiveBar", btn) as FrameworkElement;
                if (row.ActiveBar != null)
                    row.ActiveBar.IsVisibleChanged += (_, __) => RefreshNavDoorActive(row);

                _navDoorRows.Add(row);
                RefreshNavDoorActive(row);
            }
        }

        /// <summary>The medallion's halo: the door's hue fading to fully transparent, so the
        /// ellipse can overhang the 56px strip without a visible edge. Frozen - one brush per
        /// door for the life of the window.</summary>
        private static Brush? BuildNavDoorGlow(SolidColorBrush? hue)
        {
            if (hue == null) return null;
            var c = hue.Color;
            var b = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xC0, c.R, c.G, c.B), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, c.R, c.G, c.B), 0.42));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1.0));
            b.Freeze();
            return b;
        }

        /// <summary>
        /// Repaints one row's "you are here" state from the active bar ChromeFx owns: hue ring
        /// on the tile, hue on the name, full opacity instead of .85, and a stronger halo.
        /// ClearValue rather than writing the idle colours back, so the label's own
        /// DynamicResource (TextLight) stays live for mod repaints.
        ///
        /// <para>The tile's ring is the ONLY ring on a door row since 2026-08-13 - the permanent
        /// grey hairline is authored Transparent now and the art fills the tile to within 2px -
        /// so this is what tells the user which door they are in at a glance. Idle rows clear
        /// back to no ring at all (ClearValue on a locally-set Transparent brush leaves null,
        /// which paints nothing - same result, one less brush).</para>
        /// </summary>
        private void RefreshNavDoorActive(NavDoorRow row)
        {
            try
            {
                row.Active = row.ActiveBar?.Visibility == Visibility.Visible;
                row.Tile.Opacity = row.Active ? 1.0 : NavDoorTileIdleOpacity;

                if (row.Active)
                {
                    row.Tile.BorderBrush = row.Hue;
                    if (row.Label != null) row.Label.Foreground = row.Hue;
                }
                else
                {
                    row.Tile.ClearValue(Border.BorderBrushProperty);
                    row.Label?.ClearValue(TextBlock.ForegroundProperty);
                }

                SetNavDoorGlow(row, MotionFx.AllowTransitions);
            }
            catch (Exception ex) { App.Logger?.Debug("RefreshNavDoorActive: {E}", ex.Message); }
        }

        private void SetNavDoorGlow(NavDoorRow row, bool animate)
        {
            double to = row.Active ? NavDoorGlowActive : (_navRailExpanded ? NavDoorGlowOpen : 0);
            if (!animate)
            {
                row.Glow.BeginAnimation(UIElement.OpacityProperty, null);
                row.Glow.Opacity = to;
                return;
            }
            row.Glow.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(NavDoorGlowFadeMs)));
        }

        /// <summary>
        /// The medallion half of the flyout: tiles and art grow, haloes come up, and the door
        /// names rise 14px into place one after the other, 30ms apart, so the rail unfolds
        /// instead of popping. Collapsing runs the same animations backwards at half speed with
        /// no stagger - the names have to be gone BEFORE the width tween takes their room away,
        /// or they paint over the page for a frame.
        /// </summary>
        private void ApplyNavDoorRows(bool expand, bool animate)
        {
            int ms = expand ? NavRailAnimMs : NavRailCollapseAnimMs;
            double tileTo = expand ? NavDoorTileExpanded : NavDoorTileCollapsed;
            double iconTo = expand ? NavDoorIconExpanded : NavDoorIconCollapsed;
            double glowTo = expand ? NavDoorGlowExpanded : NavDoorGlowCollapsed;
            double labelTo = expand ? 1 : 0;
            double riseTo = expand ? 0 : NavDoorLabelRise;

            for (int i = 0; i < _navDoorRows.Count; i++)
            {
                var row = _navDoorRows[i];
                SetNavRailSize(row.Tile, tileTo, animate, ms);
                SetNavRailSize(row.Icon, iconTo, animate, ms);
                SetNavRailSize(row.Glow, glowTo, animate, ms);
                SetNavDoorGlow(row, animate);

                if (!animate)
                {
                    row.LabelHost.BeginAnimation(UIElement.OpacityProperty, null);
                    row.LabelHost.Opacity = labelTo;
                    row.LabelSlide.BeginAnimation(TranslateTransform.YProperty, null);
                    row.LabelSlide.Y = riseTo;
                    continue;
                }

                var begin = TimeSpan.FromMilliseconds(expand ? i * NavDoorLabelStaggerMs : 0);
                var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

                row.LabelHost.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(labelTo, TimeSpan.FromMilliseconds(
                        expand ? NavDoorLabelFadeMs : ms / 2))
                    { BeginTime = begin, EasingFunction = ease });

                row.LabelSlide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(riseTo, TimeSpan.FromMilliseconds(
                        expand ? NavDoorLabelSlideMs : ms / 2))
                    { BeginTime = begin, EasingFunction = ease });
            }
        }

        /// <summary>Square-grows one part. Width and Height take the same clock on purpose: two
        /// halves of one tween drifting apart is how a tile ends up a rectangle mid-flight.</summary>
        private static void SetNavRailSize(FrameworkElement el, double to, bool animate, int ms)
        {
            if (!animate)
            {
                el.BeginAnimation(FrameworkElement.WidthProperty, null);
                el.BeginAnimation(FrameworkElement.HeightProperty, null);
                el.Width = to;
                el.Height = to;
                return;
            }

            var size = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            el.BeginAnimation(FrameworkElement.WidthProperty, size);
            el.BeginAnimation(FrameworkElement.HeightProperty, size);
        }

        private void SetNavRailExpanded(bool expand, bool animate = true)
        {
            if (NavSidebar == null) return;
            if (_navRailExpanded == expand && _navRailReady && animate) return;
            _navRailExpanded = expand;

            double to = expand ? NavRailExpandedWidth : NavRailCollapsedWidth;
            int ms = expand ? NavRailAnimMs : NavRailCollapseAnimMs;
            animate &= MotionFx.AllowTransitions;

            if (animate)
            {
                var width = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                NavSidebar.BeginAnimation(FrameworkElement.WidthProperty, width);

                // Labels trail the width slightly on the way out and lead it on the way in, so
                // text never paints outside the rail's clip.
                var fade = new DoubleAnimation(expand ? 1 : 0, TimeSpan.FromMilliseconds(
                    expand ? ms : ms / 2));
                foreach (var label in _navRailLabels)
                    label.BeginAnimation(UIElement.OpacityProperty, fade);
            }
            else
            {
                NavSidebar.BeginAnimation(FrameworkElement.WidthProperty, null);
                NavSidebar.Width = to;
                foreach (var label in _navRailLabels)
                {
                    label.BeginAnimation(UIElement.OpacityProperty, null);
                    label.Opacity = expand ? 1 : 0;
                }
            }

            // Icons centre themselves once the label is gone. Inert for the rail's own two
            // templates as it stands - both hard-set the ContentPresenter's alignment (doors
            // Stretch, entries Left) and centre their icons with a fixed 56px column and a 21px
            // padding respectively - but it is left standing for any rail button that does honour
            // it, and it costs one property write per button.
            foreach (var b in _navRailButtons)
                b.HorizontalContentAlignment = expand ? HorizontalAlignment.Left : HorizontalAlignment.Center;

            ApplyNavDoorRows(expand, animate);
            ApplyNavRailDoorState(animate);
        }

        /// <summary>
        /// Shuts every door panel while the rail is collapsed and restores
        /// <see cref="_expandedDoor"/> when it opens. Deliberately does NOT write
        /// <see cref="_expandedDoor"/>: the accordion's own state is what the user chose, and
        /// hovering the rail must not silently re-home them.
        /// </summary>
        private void ApplyNavRailDoorState(bool animate)
        {
            foreach (var d in NavDoorMap)
            {
                var parts = NavDoorParts(d.Door);
                if (parts.Panel == null) continue;   // pinned Settings door has no panel

                bool open = _navRailExpanded &&
                            string.Equals(d.Door, _expandedDoor, StringComparison.Ordinal);
                SetDoorPanelExpanded(d.Door, parts.Panel, parts.Entries, open, animate);
            }
        }

        /// <summary>
        /// Opens the rail and holds it, for code-driven navigation that needs the user to SEE
        /// where they landed (the tutorial's spotlights, the Ctrl+K palette). Without this the
        /// spotlight would point at a 56px icon strip with the target row shut inside it.
        ///
        /// <para>The hold is a real suspension: while <see cref="_navRailHoldCount"/> is up,
        /// MouseLeave and the click-elsewhere both bail out instead of collapsing. Every caller
        /// MUST pair this with <see cref="ReleaseNavRailOpen"/>, or the rail stays open for the
        /// session.</para>
        /// </summary>
        internal void HoldNavRailOpen()
        {
            try
            {
                _navRailHoldCount++;
                SetNavRailExpanded(true);
            }
            catch (Exception ex) { App.Logger?.Debug("HoldNavRailOpen: {E}", ex.Message); }
        }

        /// <summary>
        /// Drops one <see cref="HoldNavRailOpen"/> claim. The last one out hands the rail back to
        /// the pointer: a spotlight usually ends with a click on the row it was pointing at, so a
        /// release with the mouse still ON the rail leaves it open and lets the normal MouseLeave
        /// shut it. Anywhere else, the hold was the only thing keeping it up - collapse now,
        /// because with the delay timer gone there is nothing else that ever would.
        /// </summary>
        internal void ReleaseNavRailOpen()
        {
            try
            {
                if (_navRailHoldCount > 0) _navRailHoldCount--;
                if (_navRailHoldCount > 0) return;
                if (NavSidebar?.IsMouseOver != true) SetNavRailExpanded(false);
            }
            catch (Exception ex) { App.Logger?.Debug("ReleaseNavRailOpen: {E}", ex.Message); }
        }
    }
}
