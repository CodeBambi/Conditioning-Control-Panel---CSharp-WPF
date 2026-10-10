using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// The pure half of the section page header (nav rework 2026-10-06): which pills a section
    /// draws, which pill a tab lights, the section hue, keyboard wrap and the last-tab memory.
    /// No WPF state, so SectionTabStripTests can pin it without a window.
    /// </summary>
    public static class NavStripRules
    {
        /// <summary>Home is the dashboard (no strip); Settings keeps its own left pill column.
        /// Home's other page, Premium, is a hidden tab (polish 12 round 2, owner: "remove the whole
        /// row, people click Home on the side rail to go back"), so Home never draws pills.</summary>
        public static bool ShowsPills(string? section) => NavStripTable.ShowsPills(section);

        /// <summary>The header (breadcrumb row) shows everywhere but Home (NavStripTable).</summary>
        public static bool ShowsHeader(string? section) => NavStripTable.ShowsHeader(section);

        /// <summary>The loc key of a pill's "what is this" line, shown on its "?" badge (2026-10-07).</summary>
        public static string HelpKey(NavTab tab) => "nav_help_" + tab.Key;

        /// <summary>Pills that open their own window ask first, so the click is never a surprise.</summary>
        public static bool AsksBeforeOpening(NavTab tab) => tab.Kind == NavTabKind.Window;

        /// <summary>The pills a section draws, in table order, hidden tabs skipped.</summary>
        /// <summary>Is a pill of this tier locked for the account on this machine? A paying
        /// account sees no tier sign on the pages it already owns (review fix, 2026-10-06: every
        /// Basic pill wore a BASIC plate even for subscribers). No account service (tests, early
        /// startup) reads as locked, so the sign is never hidden by mistake.</summary>
        public static bool PillLocked(int tier)
        {
            if (tier <= 0) return false;
            try
            {
                var patreon = App.Patreon;
                if (patreon == null) return true;
                return tier == 1 ? !patreon.HasPremiumAccess : !patreon.HasLabAccess;
            }
            catch { return true; }
        }

        public static IReadOnlyList<NavTab> Pills(string? section) => NavStripTable.Pills(section);

        /// <summary>The pill a tab key lights (NavStripTable.ActivePill).</summary>
        public static string? ActivePill(string? tab) => NavStripTable.ActivePill(tab);

        /// <summary>The label key a breadcrumb shows for a tab (its own row in the table).</summary>
        public static string? PageLabelKey(string? tab) => NavStripTable.PageLabelKey(tab);

        /// <summary>Keyboard move inside the strip: Left/Right wrap, Home/End jump. -1 = not a strip key.</summary>
        public static int MoveIndex(int current, int count, Key key) => NavStripTable.MoveIndex(current, count, key.ToString());

        // Section hues (polish wave 2, owner 2026-10-06: one hue PER SECTION so a page, its pills
        // and its rail medallion read as one place). Static (commerce-neutral) tokens, never gold
        // (T1), cyan (T2), red (Circe / danger) or mint (credit), and every pair at least 18
        // degrees apart: SectionTabStripTests pins the gaps. The gear shares Home's lilac.
        public static readonly Color Lilac = FromRgb(NavStripTable.Lilac);       // Home, Settings
        public static readonly Color Pink = FromRgb(NavStripTable.Pink);         // Studio (SectionHueGeneral)
        public static readonly Color Orchid = FromRgb(NavStripTable.Orchid);     // Companion
        public static readonly Color VioletBlue = FromRgb(NavStripTable.VioletBlue); // Play
        public static readonly Color Sky = FromRgb(NavStripTable.Sky);           // Social
        public static readonly Color Coral = FromRgb(NavStripTable.Coral);       // You
        public static readonly Color Sage = FromRgb(NavStripTable.Sage);         // Library

        public static Color Accent(string? section) => C(NavStripPaint.Accent(section));

        private static Color FromRgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        // The colour math is Core NavStripPaint (0xAARRGGBB), shared with the Avalonia head.
        internal static Color C(uint c) => Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
        internal static uint U(Color c) => NavStripPaint.Argb(c.A, c.R, c.G, c.B);

        // ---- Section ink (polish wave 8, readability pass) ----------------------------------
        // Saturation goes into the headers, not the body: the eyebrow, its rule, the card
        // outline and links wear the section, body text stays neutral. PaintSectionWash writes
        // these four into the SectionInk / SectionTint / SectionRule / SectionOutline Color
        // resources; TypeScaleContrastTests pins Ink at 4.5:1 and Tint at 9:1 or more.

        /// <summary>The app's lightest text token (Colors.xaml TextLight).</summary>
        public static readonly Color TextLight = Color.FromRgb(0xF0, 0xF0, 0xF5);

        /// <summary>How far the ink moves from the hue toward TextLight.</summary>
        public const double InkLift = NavStripPaint.InkLift;
        /// <summary>How far the optional title tint moves from TextLight toward the hue.</summary>
        public const double TintPull = NavStripPaint.TintPull;
        /// <summary>The rule under an eyebrow (35%) and a card outline (25%), as bytes.</summary>
        public const byte RuleAlpha = NavStripPaint.RuleAlpha, OutlineAlpha = NavStripPaint.OutlineAlpha;

        /// <summary>Eyebrow / label / link ink: the hue mixed 35% toward TextLight.</summary>
        public static Color Ink(string? section) => C(NavStripPaint.Ink(section));

        /// <summary>Optional title tint: TextLight mixed 15% toward the hue.</summary>
        public static Color Tint(string? section) => C(NavStripPaint.Tint(section));

        /// <summary>The 1 px rule under an eyebrow: the hue at 35% alpha.</summary>
        public static Color Rule(string? section) => C(NavStripPaint.Rule(section));

        /// <summary>A card outline: the hue at 25% alpha (the SectionHueXBorder convention).</summary>
        public static Color Outline(string? section) => C(NavStripPaint.Outline(section));


        /// <summary>Last-tab memory: section -> tab, stored as JSON in AppSettings.NavLastTabBySection.</summary>
        public static Dictionary<string, string> ParseLastTabs(string? json) => NavStripTable.ParseLastTabs(json);

        /// <summary>The JSON with one section's last tab set. Unchanged JSON when nothing moved.</summary>
        public static string WithLastTab(string? json, string section, string tab) => NavStripTable.WithLastTab(json, section, tab);

        /// <summary>The tab a section returns to: its remembered tab, else its default.</summary>
        public static string? LastTabFor(string? json, string section) => NavStripTable.LastTabFor(json, section);

        /// <summary>Slide duration for the active fill: 180 ms, halved at Reduced, 0 at Off.</summary>
        public static int SlideMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => 90,
            _ => 180,
        };

        // ---- Pill paint (polish wave 2, lane PILLS): the strip wears its section's hue ----------

        /// <summary>Dark ink for text on a light fill (the app's deepest plum).</summary>
        public static readonly Color DarkInk = Color.FromRgb(0x15, 0x12, 0x1F);

        // Polish wave 3 (owner: inactive pills were "barely distinguishable as a standalone
        // item"): every pill is a FILLED plate, so the strip reads as a tab bar at one glance.

        /// <summary>Inactive pill text: the hue at 95%.</summary>
        public const double RestTextAlpha = NavStripPaint.RestTextAlpha;

        // Polish wave 7 (owner: the pills "should POP more and be easily distinguishable from the
        // rest of the UI"): a raised plate sitting in a darker tray. The plate is a vertical
        // gradient around RestFillAlpha (lit top, shaded foot) inside a bevelled outline, and each
        // pill wears its own near-hue of the section (TabTint), so the bar still reads as one place.

        /// <summary>Inactive pill plate: the tab's tint at 24% (34% across the top, 14% at the foot).</summary>
        public const double RestFillAlpha = NavStripPaint.RestFillAlpha;
        /// <summary>How far a plate's top and foot sit from its middle alpha (polish wave 9: 0.06 -> 0.10,
        /// owner: the pills "seem flat").</summary>
        public const double PlateLift = NavStripPaint.PlateLift;
        /// <summary>Inactive pill border: the tint at 95%, lit along the top and shaded along the foot.</summary>
        public const double RestOutlineAlpha = 0.95;
        /// <summary>The outline's top edge mixes this far toward white, its foot this far toward ink
        /// (polish wave 9: strong enough that the bevel reads, not merely orders).</summary>
        public const double BevelLight = 0.70;
        public const double BevelShade = 0.75;
        /// <summary>Where the outline's shaded foot starts: a solid line along the bottom rather than
        /// a hairline fade into the last pixel.</summary>
        public const double OutlineFootOffset = 0.85;
        /// <summary>Pill face border: 1.5 px at rest, 2 px on the lit tab. Both sit inside the 36 px
        /// face, so a pill stays 38 tall; the lit face gives the extra half pixel back from its
        /// padding, so the label and the pill width never move.</summary>
        public const double RestFaceThickness = 1.5;
        public const double ActiveFaceThickness = 2.0;
        /// <summary>Hovered inactive pill: the plate in the tint at 38%.</summary>
        public const double HoverFillAlpha = 0.38;
        /// <summary>The pill track is a tray: deep ink at 40% under the hue at 10%, so the pills sit in
        /// something darker than the page wash. Its 1.5 px border is shaded along the top (an inset)
        /// and the hue at 40% below.</summary>
        public const double TrackInkAlpha = NavStripPaint.TrackInkAlpha;
        public const double TrackFillAlpha = NavStripPaint.TrackFillAlpha;
        public const double TrackBorderAlpha = 0.40;
        public const double TrackInsetAlpha = 0.70;
        /// <summary>The active pill's soft outer glow in the hue (static, 0 offset).</summary>
        public const double ActiveGlowBlur = 16;
        public const double ActiveGlowOpacity = 0.75;
        /// <summary>The active pill's 2 px inner ring: near-white in the tab's tint, 55% on average
        /// (80% across the top, 30% at the foot, so it reads as a gloss rather than a frame).</summary>
        public const double ActiveRingAlpha = 0.55;
        public const double ActiveRingTopAlpha = 0.80;
        public const double ActiveRingFootAlpha = 0.30;
        public const double ActiveRingWhite = NavStripPaint.ActiveRingWhite;
        /// <summary>The active pill's glyph: the label's ink mixed this far toward the tab's tint
        /// (less when the mix would drop under 3:1 on the solid hue).</summary>
        public const double ActiveGlyphTint = NavStripPaint.ActiveGlyphTint;
        /// <summary>Pill size: 38 px tall, 14.5 px SemiBold label, 16 px padding, 6 px between pills.</summary>
        public const double PillHeight = 38;
        public const double PillFontSize = 14.5;
        public const double PillPadding = 16;
        public const double PillGap = 6;
        /// <summary>The tier sign on a gated pill (polish wave 7: about 30% bigger than the old
        /// 38 px sign on a 40 x 20 plate), its plate, and the room it keeps from the label.</summary>
        public const double BadgeMaxWidth = 50;
        public const double BadgePlateWidth = 52;
        public const double BadgePlateHeight = 24;
        public const double BadgeGap = 8;
        /// <summary>Extra right padding on a pill that carries a tier sign.</summary>
        public const double BadgePadExtra = 4;
        /// <summary>Hue step between neighbouring pills on one bar, in degrees.</summary>
        public const double TabHueStep = NavStripPaint.TabHueStep;
        /// <summary>The leading glyph: 16 px in Segoe MDL2 Assets, wearing the label's colour.</summary>
        public const double GlyphSize = 16;
        public const string GlyphFont = "Segoe MDL2 Assets";

        /// <summary>
        /// One leading glyph per tab key (Segoe MDL2 Assets code points). A key not in the table
        /// draws no glyph; a glyph the font lacks also draws none (never a box), and
        /// NavStripPolishGlyphTests checks every entry against the font on the machine.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, char> Glyphs = new Dictionary<string, char>
        {
            // Home
            ["settings"] = '\uE80F',         // Dashboard: a home
            ["premium"] = '\uE735',          // Premium: a filled star
            // Studio
            ["studio"] = '\uE790',           // Effects: a palette
            ["presets"] = '\uE9E9',          // sliders
            ["haptics"] = '\uE877',          // a vibrating phone
            ["justdrop"] = '\uEB42',         // a droplet
            ["ramp"] = '\uE9D2',             // a rising line
            // Companion
            ["companion"] = '\uE8BD',        // Chat: a speech balloon
            ["personality"] = '\uE76E',      // a face (the font has no mask)
            ["permissions"] = '\uEA18',      // a shield
            ["companionlinks"] = '\uE71B',   // a link
            ["companionai"] = '\uE701',      // a signal: which AI it talks to
            ["bambitakeover"] = '\uE7AD',    // a swirl
            ["shelistening"] = '\uE720',     // a microphone
            ["awareness"] = '\uEA80',        // a light bulb
            // Play
            ["play"] = '\uE7FC',             // Games: a controller
            ["playeyes"] = '\uE7B3',         // an eye
            ["playsessions"] = '\uE768',     // play
            ["deeper"] = '\uE81E',           // layers
            // Social
            ["availablesubjects"] = '\uE7EE', // Lobby: a host at a table (the font has no door)
            ["friends"] = '\uE716',          // two people
            ["leaderboard"] = '\uE9F9',      // a bar chart (the font has no trophy)
            ["remotecontrol"] = '\uE703',    // two linked screens
            ["leash"] = '\uE71B',            // a chain link
            // You
            ["discord"] = '\uE77B',          // Profile: a person
            ["quests"] = '\uE9D5',           // a checklist
            ["achievements"] = '\uE734',     // a star
            ["enhancements"] = '\uE945',     // a bolt
            ["programs"] = '\uE787',         // a calendar
            ["chaster"] = '\uE72E',          // a padlock
            // Library
            ["assets"] = '\uE8B7',           // a folder
            ["folders"] = '\uE838',          // an open folder
            ["mods"] = '\uEA86',             // a puzzle piece
            ["catalogue"] = '\uE736',        // a book
            ["phrases"] = '\uE8D2',          // letters (the font has no quote mark)
            ["medialog"] = '\uE8FD',         // a list
        };

        /// <summary>The glyph a tab key wears, or null (no glyph).</summary>
        public static string? Glyph(string? key) =>
            key != null && Glyphs.TryGetValue(key.ToLowerInvariant(), out var c) ? c.ToString() : null;
        /// <summary>The "Moved" note's text: the hue at 90%.</summary>
        public const double NoteTextAlpha = 0.90;

        /// <summary>The hue at an alpha (0..1).</summary>
        public static Color WithAlpha(Color c, double alpha) => C(NavStripPaint.WithAlpha(U(c), alpha));

        /// <summary>WCAG relative luminance of an opaque colour.</summary>
        public static double Luminance(Color c) => NavStripPaint.Luminance(U(c));

        /// <summary>WCAG contrast ratio between two opaque colours (1..21).</summary>
        public static double Contrast(Color a, Color b) => NavStripPaint.Contrast(U(a), U(b));

        /// <summary>The active pill's text on its solid hue fill: dark ink or white, whichever
        /// reads better. SectionTabStripTests pins it at 4.5:1 or more for every section.</summary>
        public static Color ActiveTextOn(Color fill) => C(NavStripPaint.ActiveTextOn(U(fill)));

        /// <summary>The app's dark page ground (the strip sits on it).</summary>
        public static readonly Color PageGround = Color.FromRgb(0x1A, 0x12, 0x30);

        /// <summary>Paints <paramref name="top"/> (with its alpha) over an opaque <paramref name="under"/>.</summary>
        public static Color Over(Color top, Color under) => C(NavStripPaint.Over(U(top), U(under)));

        /// <summary>Porter-Duff "over" for two colours that may both be translucent.</summary>
        public static Color Composite(Color top, Color under) => C(NavStripPaint.Composite(U(top), U(under)));

        /// <summary>A straight mix of two colours (t = 0 gives a, 1 gives b), opaque.</summary>
        public static Color Mix(Color a, Color b, double t) => C(NavStripPaint.Mix(U(a), U(b), t));

        /// <summary>The tray's fill: the hue at 10% over deep ink at 40% (translucent).</summary>
        public static Color TrackFill(Color hue) => C(NavStripPaint.TrackFill(U(hue)));

        // ---- HSL (polish wave 7: per-tab tints) ----------------------------------------------

        /// <summary>Hue (0..360), saturation and lightness (0..1) of a colour.</summary>
        public static (double H, double S, double L) ToHsl(Color c) => NavStripPaint.ToHsl(U(c));

        /// <summary>An opaque colour from hue (degrees, any range), saturation and lightness.</summary>
        public static Color FromHsl(double h, double sat, double l) => C(NavStripPaint.FromHsl(h, sat, l));

        /// <summary>The colour turned round the hue wheel, saturation and lightness kept.</summary>
        public static Color RotateHue(Color c, double degrees) => C(NavStripPaint.RotateHue(U(c), degrees));

        /// <summary>Shortest distance between two hues, in degrees (0..180).</summary>
        public static double HueDistance(double a, double b)
        {
            var d = Math.Abs(a - b) % 360;
            return d > 180 ? 360 - d : d;
        }

        /// <summary>
        /// A tab's own tint (polish wave 7, owner: "subtle identity to the subtabs, maybe a colour
        /// coding"): the section hue turned by (index - middle) x 9 degrees, so every pill on a bar
        /// has its own near-hue and the bar still reads as one section. The middle pill wears the
        /// section hue itself. The key is part of the signature so a tab can be pinned later.
        /// </summary>
        public static Color TabTint(string? section, string? key, int index, int count) => C(NavStripPaint.TabTint(section, index, count));

        /// <summary>The worst ground a rest pill's text sits on: the page with the section wash
        /// (lane CHROME, up to 14%), the tray and the pill's own plate at its lit top, in the hue.</summary>
        public static Color RestGround(Color hue) => RestGround(hue, hue);

        /// <summary>The worst ground on a pill wearing <paramref name="tint"/>: page, wash and tray
        /// in the section hue, the plate's lit top in the tint.</summary>
        public static Color RestGround(Color hue, Color tint) => C(NavStripPaint.RestGround(U(hue), U(tint)));

        /// <summary>
        /// A rest pill's text: the hue at 95%. A dark hue (Play's violet-blue) reads muddy at 95%
        /// on the dark track, so it is lifted toward white in small steps until the text reads at
        /// 4.5:1 on <see cref="RestGround(Color)"/>. Light hues come back unchanged.
        /// </summary>
        public static Color RestTextOn(Color hue) => RestTextOn(hue, hue);

        /// <summary>The rest label on a tinted pill: the section hue, lifted until it reads at 4.5:1
        /// on that pill's ground (the label rule stays the section's; only the ground moves).</summary>
        public static Color RestTextOn(Color hue, Color tint) => C(NavStripPaint.RestTextOn(U(hue), U(tint)));

        /// <summary>The rest glyph: the tab's own tint, lifted until it reads at 4.5:1 on its ground.</summary>
        public static Color RestGlyphOn(Color hue, Color tint) => C(NavStripPaint.RestGlyphOn(U(hue), U(tint)));

        /// <summary>The active glyph: the label's ink mixed toward the tab's tint, never under 3:1
        /// on the solid section hue (an icon, so the WCAG graphics floor).</summary>
        public static Color ActiveGlyphOn(Color hue, Color tint) => C(NavStripPaint.ActiveGlyphOn(U(hue), U(tint)));

        /// <summary>The active pill's inner ring colour at an alpha: near-white in the tint.</summary>
        public static Color ActiveRingColor(Color tint, double alpha) => C(NavStripPaint.ActiveRingColor(U(tint), alpha));

        private static Color Lift(Color start, Color ground) => C(NavStripPaint.Lift(U(start), U(ground)));

        // ---- brushes ------------------------------------------------------------------------

        private static LinearGradientBrush Vertical(params (Color C, double At)[] stops)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
            b.Freeze();
            return b;
        }

        /// <summary>A raised plate in the tint: lit across the top, shaded at the foot, centred on <paramref name="alpha"/>.</summary>
        public static LinearGradientBrush PlateBrush(Color tint, double alpha) =>
            Vertical((WithAlpha(tint, alpha + PlateLift), 0), (WithAlpha(tint, alpha - PlateLift), 1));

        /// <summary>The rest outline: the tint at 75%, a lighter 1 px top edge and a darker foot.</summary>
        public static LinearGradientBrush OutlineBrush(Color tint) =>
            Vertical((WithAlpha(Mix(tint, Colors.White, BevelLight), RestOutlineAlpha), 0),
                     (WithAlpha(tint, RestOutlineAlpha), 0.35),
                     (WithAlpha(tint, RestOutlineAlpha), 0.65),
                     (WithAlpha(Mix(tint, DarkInk, BevelShade), RestOutlineAlpha), OutlineFootOffset));

        /// <summary>The active pill's inner ring: a near-white gloss, strong at the top.</summary>
        public static LinearGradientBrush ActiveRingBrush(Color tint) =>
            Vertical((ActiveRingColor(tint, ActiveRingTopAlpha), 0), (ActiveRingColor(tint, ActiveRingFootAlpha), 1));

        /// <summary>The tray's border: shaded along the top (an inset), the hue at 40% below.</summary>
        public static LinearGradientBrush TrackBorderBrush(Color hue) =>
            Vertical((WithAlpha(DarkInk, TrackInsetAlpha), 0), (WithAlpha(hue, TrackBorderAlpha), 0.45),
                     (WithAlpha(hue, TrackBorderAlpha), 1));
    }

    /// <summary>
    /// The section page header: breadcrumb, pill strip, "Moved" note and accent line. One
    /// instance, mounted by MainWindow above the page views and synced from ShowTab.
    /// </summary>
    public partial class SectionTabStrip : UserControl
    {
        private static readonly SolidColorBrush FocusRing = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)));
        private static readonly SolidColorBrush BadgePlate = Freeze(new SolidColorBrush(NavStripRules.WithAlpha(NavStripRules.DarkInk, 0.85)));

        // The section's pill paint, rebuilt when the section changes (polish wave 2).
        private Brush _activeText = Brushes.Black;
        private Color _hue = NavStripRules.Lilac;

        /// <summary>One pill and its own paint (polish wave 7: every pill wears its tab's tint).</summary>
        private sealed class PillParts
        {
            public NavTab Tab = null!;
            public Button Pill = null!;
            public Border Face = null!;
            public TextBlock Label = null!;
            public TextBlock? Glyph;
            public Color Tint;
            public Brush RestText = Brushes.Gainsboro;
            public Brush RestGlyph = Brushes.Gainsboro;
            public Brush ActiveGlyph = Brushes.Black;
            public Brush Outline = Brushes.Transparent;
            public Brush ActiveRing = Brushes.Transparent;
            public Brush Plate = Brushes.Transparent;
            public Brush Hover = Brushes.Transparent;
            public Thickness RestPadding;
            // Polish wave 10 (depth): the face's travel, the drop band under it, and the state.
            public TranslateTransform? FaceShift;
            public System.Windows.Shapes.Rectangle? Drop;
            public bool Pressed;
            public bool Hovered;
        }

        private readonly List<PillParts> _pills = new();
        private string? _section;
        private string? _activePill;
        private string? _crumbKey;
        private DispatcherTimer? _noteTimer;

        /// <summary>A pill was chosen (click, Enter/Space, or a Left/Right/Home/End move).</summary>
        public event Action<NavTab>? TabRequested;

        /// <summary>The breadcrumb's section word was clicked.</summary>
        public event Action<string>? SectionRequested;

        /// <summary>A pill was built (the host attaches its pin menu here).</summary>
        public event Action<NavTab, FrameworkElement>? PillCreated;

        /// <summary>Test seam: forces a motion level instead of asking MotionFx.</summary>
        internal MotionLevel? MotionOverride { get; set; }

        public SectionTabStrip()
        {
            InitializeComponent();
            CrumbSep.Text = SafeLoc("nav_crumb_sep", "›");
            PillRow.PreviewKeyDown += PillRow_PreviewKeyDown;
            SizeChanged += (_, _) => PositionFill(animate: false);
        }

        internal string? Section => _section;
        internal string? ActivePillKey => _activePill;
        internal IReadOnlyList<string> PillKeys => _pills.Select(p => p.Tab.Key).ToArray();
        internal Button? PillFor(string key) => Part(key)?.Pill;
        private PillParts? Part(string key) => _pills.FirstOrDefault(p => p.Tab.Key == key);
        internal string CrumbText => $"{CrumbSectionText.Text} {CrumbSep.Text} {CrumbPage.Text}".Trim();

        private MotionLevel Level => MotionOverride ?? MotionFx.Level;

        /// <summary>Show the header for a tab. <paramref name="pageLabel"/> overrides the
        /// breadcrumb's page word (Settings passes its current section's label).</summary>
        public void Show(string? section, string? tab, string? pageLabel = null)
        {
            if (!NavStripRules.ShowsHeader(section))
            {
                Visibility = Visibility.Collapsed;
                _section = section;
                return;
            }
            Visibility = Visibility.Visible;

            bool sectionChanged = !string.Equals(section, _section, StringComparison.OrdinalIgnoreCase);
            _section = section;
            var accent = new SolidColorBrush(NavStripRules.Accent(section));
            accent.Freeze();

            if (sectionChanged)
            {
                PaintFor(NavStripRules.Accent(section));
                FixCrumbWidth(section!);
                BuildPills(section!, accent);
                AccentLine.Background = new LinearGradientBrush(
                    NavStripRules.Accent(section), Color.FromArgb(0, 0, 0, 0), 0);
                ActiveFill.Background = accent;
                CrumbSectionText.Foreground = accent;
            }
            // Hidden, not Collapsed: the track keeps the row's height, so Settings (no pills) has the
            // same header height as every other section.
            PillTrack.Visibility = _pills.Count > 0 ? Visibility.Visible : Visibility.Hidden;
            TrayHost.Visibility = PillTrack.Visibility;

            // Breadcrumb: two levels only; the section word is a link back to its last tab.
            var sectionLabel = SafeLoc(NavSections.Find(section)?.LabelKey, section ?? string.Empty);
            var page = pageLabel ?? SafeLoc(NavStripRules.PageLabelKey(tab), string.Empty);
            var crumbKey = sectionLabel + "|" + page;
            if (crumbKey != _crumbKey)
            {
                CrumbSectionText.Text = sectionLabel;
                CrumbPage.Text = page;
                CrumbSep.Visibility = string.IsNullOrEmpty(page) ? Visibility.Collapsed : Visibility.Visible;
                if (_crumbKey != null) Crossfade(CrumbPanel);
                _crumbKey = crumbKey;
            }
            CrumbSection.ToolTip = sectionLabel;

            SetActive(NavStripRules.ActivePill(tab), animate: !sectionChanged);
        }

        /// <summary>The one-line "Moved: Social › Lobby" note, about three seconds, non-modal.</summary>
        public void ShowMovedNote(string text)
        {
            MovedNoteText.Text = text;
            var hue = NavStripRules.Accent(_section);
            MovedNote.BorderBrush = new SolidColorBrush(hue);
            MovedNoteText.Foreground = new SolidColorBrush(NavStripRules.WithAlpha(hue, NavStripRules.NoteTextAlpha));
            MovedNote.Visibility = Visibility.Visible;
            var level = Level;
            if (level == MotionLevel.Off) MovedNote.Opacity = 1;
            else MovedNote.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(level == MotionLevel.Reduced ? 75 : 150)));

            _noteTimer?.Stop();
            _noteTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(3.2) };
            _noteTimer.Tick += (_, _) =>
            {
                _noteTimer?.Stop();
                if (Level == MotionLevel.Off) { HideNote(); return; }
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
                fade.Completed += (_, _) => HideNote();
                MovedNote.BeginAnimation(OpacityProperty, fade);
            };
            _noteTimer.Start();
        }

        /// <summary>The section's pill paint: track, rest text, outline, hover and active text, all
        /// from the one hue table (NavStripRules.Accent).</summary>
        private void PaintFor(Color hue)
        {
            _activeText = Freeze(new SolidColorBrush(NavStripRules.ActiveTextOn(hue)));
            _hue = hue;
            // The tray: darker than the page wash, its top edge shaded so the pills sit IN it.
            PillTrack.Background = Freeze(new SolidColorBrush(NavStripRules.TrackFill(hue)));
            PillTrack.BorderBrush = NavStripRules.TrackBorderBrush(hue);
            PaintDepthPages(hue);
        }

        /// <summary>Test seam: the brushes a pill wears now (label, face outline).</summary>
        internal (Brush Text, Brush Outline) PillPaint(string key)
        {
            var p = Part(key);
            return (p?.Label.Foreground ?? Brushes.Transparent, p?.Face.BorderBrush ?? Brushes.Transparent);
        }

        /// <summary>Test seam: the tint a pill wears (polish wave 7).</summary>
        internal Color PillTint(string key) => Part(key)?.Tint ?? Colors.Transparent;

        /// <summary>Test seam: the brush a pill's glyph wears now, or null (no glyph).</summary>
        internal Brush? PillGlyphBrush(string key) => Part(key)?.Glyph?.Foreground;

        /// <summary>Test seam: a pill's plate fill (transparent on the active pill: the ActiveFill shows).</summary>
        internal Brush PillFill(string key) => Part(key)?.Face.Background ?? Brushes.Transparent;

        /// <summary>Test seam: a pill face's border thickness (polish wave 9).</summary>
        internal double PillFaceThickness(string key) => Part(key)?.Face.BorderThickness.Top ?? 0;

        /// <summary>Test seam: the glyph text a pill shows, or null.</summary>
        internal string? PillGlyph(string key) => Part(key)?.Glyph?.Text;

        /// <summary>Test seam: the active fill's glow (null when Motion is Off).</summary>
        internal System.Windows.Media.Effects.DropShadowEffect? ActiveGlow => ActiveFill.Effect as System.Windows.Media.Effects.DropShadowEffect;

        internal Brush TrackFill => PillTrack.Background;
        internal Brush TrackBorder => PillTrack.BorderBrush;
        internal Brush CrumbWordBrush => CrumbSectionText.Foreground;
        internal Brush MovedNoteBrush => MovedNoteText.Foreground;
        internal double PillHeight(string key) => PillFor(key)?.ActualHeight ?? 0;
        internal double TrackHeightForTests => PillTrack.ActualHeight;

        private void HideNote()
        {
            MovedNote.BeginAnimation(OpacityProperty, null);
            MovedNote.Opacity = 0;
            MovedNote.Visibility = Visibility.Collapsed;
        }

        // =====================================================================================
        //  pills
        // =====================================================================================

        private void BuildPills(string section, Brush accent)
        {
            PillRow.Children.Clear();
            _pills.Clear();
            _helpBadges.Clear();
            CloseConfirm();
            _activePill = null;
            ActiveFill.Width = 0;
            ActiveFill.Visibility = Visibility.Collapsed;

            var tabs = NavStripRules.Pills(section);
            var hue = NavStripRules.Accent(section);
            for (int index = 0; index < tabs.Count; index++)
            {
                var tab = tabs[index];
                // Polish wave 7: the tab's own near-hue paints its glyph, outline, hover and the
                // active ring; the active FILL and the label rule stay the section's.
                var tint = NavStripRules.TabTint(section, tab.Key, index, tabs.Count);
                var parts = new PillParts
                {
                    Tab = tab,
                    Tint = tint,
                    RestText = Freeze(new SolidColorBrush(NavStripRules.RestTextOn(hue, tint))),
                    RestGlyph = Freeze(new SolidColorBrush(NavStripRules.RestGlyphOn(hue, tint))),
                    ActiveGlyph = Freeze(new SolidColorBrush(NavStripRules.ActiveGlyphOn(hue, tint))),
                    Outline = NavStripRules.OutlineBrush(tint),
                    ActiveRing = NavStripRules.ActiveRingBrush(tint),
                    Plate = NavStripRules.PlateBrush(tint, NavStripRules.RestFillAlpha),
                    Hover = NavStripRules.PlateBrush(tint, NavStripRules.HoverFillAlpha),
                };
                var label = new TextBlock
                {
                    Text = SafeLoc(tab.LabelKey, tab.Key),
                    FontSize = NavStripRules.PillFontSize,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = parts.RestText,
                };
                // The active pill turns ExtraBold: reserve that width now, or lighting a pill
                // would widen it and push every pill after it sideways.
                label.MinWidth = BoldWidth(label);
                label.TextAlignment = TextAlignment.Center;
                var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                // The leading glyph (polish wave 3): one per tab key, in the label's colour. A
                // glyph the font lacks draws nothing, never a box.
                TextBlock? glyph = null;
                var glyphText = NavStripRules.Glyph(tab.Key);
                if (glyphText != null && GlyphRenders(glyphText))
                {
                    glyph = new TextBlock
                    {
                        Text = glyphText,
                        FontFamily = GlyphFamily,
                        FontSize = NavStripRules.GlyphSize,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0),
                        Foreground = parts.RestGlyph,
                    };
                    content.Children.Add(glyph);
                }
                content.Children.Add(label);
                bool locked = NavStripRules.PillLocked(tab.Tier);
                if (locked)
                {
                    // Locked stays visible: the tier sign sits in the pill, the click still
                    // navigates and the page's own gate explains the lock. Static on chrome.
                    var badge = new TierBadge
                    {
                        MotionOverride = false,   // first: Tier would start the hum
                        Tier = tab.Tier,
                        MaxWidthOverride = NavStripRules.BadgeMaxWidth,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    // A dark plate under the sign, so a neon tier sign reads the same on the
                    // solid hue of the active pill as on the dark track.
                    content.Children.Add(new Border
                    {
                        Width = NavStripRules.BadgePlateWidth,
                        Height = NavStripRules.BadgePlateHeight,
                        Margin = new Thickness(NavStripRules.BadgeGap, 0, -6, 0),
                        CornerRadius = new CornerRadius(7),
                        Background = BadgePlate,
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new Grid
                        {
                            Width = NavStripRules.BadgeMaxWidth,
                            Height = NavStripRules.BadgePlateHeight - 2,
                            ClipToBounds = false,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Children = { badge },
                        },
                    });
                }

                // The face: a raised plate in the tab's tint at rest (a lit-top gradient around
                // 24%, a bevelled 75% border). Its 1 px outer ring is the focus ring, so border +
                // ring never grow the pill.
                double rightPad = NavStripRules.PillPadding + (locked ? NavStripRules.BadgePadExtra : 0);
                var face = new Border
                {
                    CornerRadius = new CornerRadius(NavStripRules.PillHeight / 2 - 1),
                    Padding = new Thickness(NavStripRules.PillPadding, 0, rightPad, 0),
                    MinHeight = NavStripRules.PillHeight - 2,   // + the 1 px ring = a 38 px pill
                    Background = parts.Plate,
                    BorderThickness = new Thickness(NavStripRules.RestFaceThickness),
                    BorderBrush = parts.Outline,
                    Child = content,
                    // Polish wave 10: the face travels (lit = sunk, pressed = down); the ring
                    // around it keeps the hover lift and the squish.
                    RenderTransform = parts.FaceShift = new TranslateTransform(),
                };
                var ring = new Border
                {
                    CornerRadius = new CornerRadius(NavStripRules.PillHeight / 2),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Transparent,
                    Child = face,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                };
                // A real Button (UIA: a named, invokable control) wearing the pill face. The
                // button captures the mouse on press, so the release always reaches it: a press
                // that squished the face away from the pointer, a focus hand-back after a dialog
                // or a window activation no longer swallows the first click. The transparent
                // backing keeps the whole unsquished rectangle hit-testable.
                var pill = new Button
                {
                    Template = PillTemplate,
                    Content = ring,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Focusable = true,
                    FocusVisualStyle = null,
                    Tag = tab.Key,
                    ToolTip = PillToolTip(section, tab, label.Text),
                };
                ToolTipService.SetInitialShowDelay(pill, 500);
                KeyboardNavigation.SetIsTabStop(pill, false);
                System.Windows.Automation.AutomationProperties.SetName(pill, label.Text);
                System.Windows.Automation.AutomationProperties.SetHelpText(pill, CrumbFor(section, label.Text));
                System.Windows.Automation.AutomationProperties.SetAutomationId(pill, "NavPill_" + tab.Key);

                pill.MouseEnter += (_, _) => { if (!IsActive(tab.Key)) face.Background = parts.Hover; DepthHover(parts, true); };
                pill.MouseLeave += (_, _) => { face.Background = IsActive(tab.Key) ? Brushes.Transparent : parts.Plate; if (!pill.IsPressed) MotionFx.PressSquish(ring, false); DepthHover(parts, false); };
                pill.PreviewMouseLeftButtonDown += (_, _) => { MotionFx.PressSquish(ring, true); DepthPress(parts, true); };
                pill.LostMouseCapture += (_, _) => { MotionFx.PressSquish(ring, false); DepthPress(parts, false); };
                pill.Click += (_, e) =>
                {
                    e.Handled = true;
                    MotionFx.PressSquish(ring, false);
                    parts.Pressed = false;
                    Choose(tab, focus: false);
                };
                pill.GotKeyboardFocus += (_, _) => ring.BorderBrush = FocusRing;
                pill.LostKeyboardFocus += (_, _) => ring.BorderBrush = Brushes.Transparent;

                // The pill rides in a host with its "?" badge (2026-10-07), so the gap and the
                // badge live outside the button: hovering the badge never presses the pill.
                PillRow.Children.Add(HostWithHelp(section, tab, pill, label.Text, parts.Tint,
                    new Thickness(PillRow.Children.Count == 0 ? 0 : NavStripRules.PillGap, 0, 0, 0)));
                parts.Pill = pill;
                parts.Face = face;
                parts.RestPadding = face.Padding;
                parts.Label = label;
                parts.Glyph = glyph;
                parts.Drop = DropOf(pill);
                _pills.Add(parts);
                DepthSettle(parts, animate: false);
                PillCreated?.Invoke(tab, pill);
            }

            // One tab stop for the whole strip (ARIA tabs): the active pill, else the first.
            if (_pills.Count > 0) KeyboardNavigation.SetIsTabStop(_pills[0].Pill, true);
        }

        private static readonly FontFamily GlyphFamily = new(NavStripRules.GlyphFont);
        private static readonly Dictionary<string, bool> GlyphCache = new();

        /// <summary>True when the glyph font on this machine has the glyph (else the pill shows
        /// no glyph rather than a box). Cached per glyph.</summary>
        internal static bool GlyphRenders(string glyph)
        {
            lock (GlyphCache)
            {
                if (GlyphCache.TryGetValue(glyph, out var known)) return known;
                bool ok = false;
                try
                {
                    var typeface = new Typeface(GlyphFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                    if (typeface.TryGetGlyphTypeface(out var gt) && gt.FamilyNames.Values.Contains(NavStripRules.GlyphFont))
                        ok = glyph.All(ch => gt.CharacterToGlyphMap.TryGetValue(ch, out var index) && index != 0);
                }
                catch { ok = false; }
                GlyphCache[glyph] = ok;
                return ok;
            }
        }

        /// <summary>Template for a pill button: a transparent hit backing and the face.</summary>
        private static readonly ControlTemplate PillTemplate = BuildPillTemplate();

        private static ControlTemplate BuildPillTemplate()
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            // Polish wave 10: the raised pill's drop band, under the face, on the sheet (it does
            // not travel with the face). Length and paint are set per state in DepthSettle; the
            // negative foot margin keeps the pill 38 px tall.
            var drop = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle), "DepthDrop");
            drop.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
            drop.SetValue(UIElement.IsHitTestVisibleProperty, false);
            drop.SetValue(System.Windows.Shapes.Rectangle.RadiusXProperty, 4.0);
            drop.SetValue(System.Windows.Shapes.Rectangle.RadiusYProperty, 4.0);
            drop.SetValue(FrameworkElement.HeightProperty, 0.0);
            grid.AppendChild(drop);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            grid.AppendChild(presenter);
            var t = new ControlTemplate(typeof(Button)) { VisualTree = grid };
            t.Seal();
            return t;
        }

        /// <summary>"Play > Games": the section and page a pill leads to.</summary>
        internal static string CrumbFor(string section, string page)
        {
            var sectionLabel = SafeLoc(NavSections.Find(section)?.LabelKey, section);
            return $"{sectionLabel} {SafeLoc("nav_crumb_sep", ">")} {page}";
        }

        /// <summary>The pill tooltip says more than its label: where it leads, an optional
        /// "label key + _tip" line, and the tier line for a locked page.</summary>
        internal static string PillToolTip(string section, NavTab tab, string label)
        {
            var lines = new List<string> { CrumbFor(section, label) };
            var tip = HelpText(tab);
            if (!string.IsNullOrEmpty(tip)) lines.Add(tip);
            if (!NavStripRules.PillLocked(tab.Tier)) { }
            else if (tab.Tier == 1) lines.Add(SafeLoc("nav_tag_premium_tip", string.Empty));
            else if (tab.Tier >= 2) lines.Add(SafeLoc("nav_tag_lab_tip", string.Empty));
            return string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrEmpty(l)));
        }

        /// <summary>
        /// The page word gets one fixed width per section: the longest page name the section can
        /// show (hidden pages and Settings' sections included). The crumb then never pushes the
        /// pills sideways when the page changes, so a second click at the same spot hits the
        /// same pill.
        /// </summary>
        private void FixCrumbWidth(string section)
        {
            double widest = 0;
            try
            {
                var typeface = new Typeface(CrumbPage.FontFamily, CrumbPage.FontStyle, FontWeights.SemiBold, CrumbPage.FontStretch);
                double dpi = 1.0;
                try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                foreach (var t in NavSections.Find(section)?.Tabs ?? Array.Empty<NavTab>())
                {
                    var text = SafeLoc(t.LabelKey, t.Key);
                    var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, typeface, CrumbPage.FontSize, Brushes.White, dpi);
                    widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
                }
            }
            catch (Exception ex) { App.Logger?.Debug("FixCrumbWidth({Section}): {E}", section, ex.Message); }
            CrumbPage.Width = widest > 0 ? Math.Ceiling(widest) + 2 : double.NaN;
        }

        /// <summary>A pill label's width at the active (ExtraBold) weight.</summary>
        private double BoldWidth(TextBlock label)
        {
            try
            {
                double dpi = 1.0;
                try { dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
                var typeface = new Typeface(label.FontFamily, label.FontStyle, FontWeights.ExtraBold, label.FontStretch);
                var ft = new FormattedText(label.Text ?? string.Empty, System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, typeface, label.FontSize, Brushes.White, dpi);
                return Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + 1;
            }
            catch { return 0; }
        }

        /// <summary>Test seam: the fixed width of the breadcrumb's page word.</summary>
        internal double CrumbPageWidth => CrumbPage.Width;

        private bool IsActive(string key) => string.Equals(_activePill, key, StringComparison.OrdinalIgnoreCase);

        private void Choose(NavTab tab, bool focus)
        {
            // Launchers and windows open something else; the page on screen keeps its pill.
            if (tab.Kind is NavTabKind.Tab or NavTabKind.Zone) SetActive(tab.Key, animate: true);
            if (focus) PillFor(tab.Key)?.Focus();
            // A pill that opens its own window asks first (owner, 2026-10-07: Just Drop).
            if (NavStripRules.AsksBeforeOpening(tab)) { AskBeforeOpening(tab); return; }
            TabRequested?.Invoke(tab);
        }

        private void SetActive(string? key, bool animate)
        {
            _activePill = key;
            foreach (var p in _pills)
            {
                bool on = IsActive(p.Tab.Key);
                // Active: dark ink (or white) on the solid hue the ActiveFill slides under; the
                // plate steps aside so the fill reads as one shape, and a near-white gloss ring in
                // the tab's tint sits just inside it. Rest: the label on a raised plate in the tab's
                // tint inside a bevelled border. The ExtraBold width stays reserved, so nothing
                // moves either way. The glyph wears the tab's own tint in both states.
                p.Label.Foreground = on ? _activeText : p.RestText;
                p.Label.FontWeight = FontWeights.SemiBold;
                if (p.Glyph != null) p.Glyph.Foreground = on ? p.ActiveGlyph : p.RestGlyph;
                p.Face.BorderBrush = on ? p.ActiveRing : p.Outline;
                // The lit ring is half a pixel heavier; the padding gives it back so the label
                // and the pill's width stay exactly where they were (PillsStayPutWhenThePageChanges).
                double grow = on ? NavStripRules.ActiveFaceThickness - NavStripRules.RestFaceThickness : 0;
                p.Face.BorderThickness = new Thickness(on ? NavStripRules.ActiveFaceThickness : NavStripRules.RestFaceThickness);
                var pad = p.RestPadding;
                p.Face.Padding = new Thickness(Math.Max(0, pad.Left - grow), pad.Top, Math.Max(0, pad.Right - grow), pad.Bottom);
                p.Face.Background = on ? Brushes.Transparent : (p.Pill.IsMouseOver ? p.Hover : p.Plate);
                KeyboardNavigation.SetIsTabStop(p.Pill, on);
                System.Windows.Automation.AutomationProperties.SetItemStatus(p.Pill, on ? "selected" : string.Empty);
                DepthSettle(p, animate);
            }
            if (key == null && _pills.Count > 0) KeyboardNavigation.SetIsTabStop(_pills[0].Pill, true);
            PositionFill(animate);
        }

        private void PositionFill(bool animate)
        {
            var pill = _activePill == null ? null : PillFor(_activePill);
            if (pill == null)
            {
                ActiveFill.Visibility = Visibility.Collapsed;
                return;
            }
            if (!pill.IsMeasureValid || pill.ActualWidth <= 0)
            {
                // First show: the row has not been laid out yet. Place it once layout lands.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (pill.ActualWidth > 0 && PillFor(_activePill ?? string.Empty) == pill) PlaceFill(pill, false);
                }));
                return;
            }
            PlaceFill(pill, animate);
        }

        private void PlaceFill(FrameworkElement pill, bool animate)
        {
            double x;
            try { x = pill.TranslatePoint(new Point(0, 0), PillRow).X; }
            catch (InvalidOperationException) { return; }
            ActiveFill.Visibility = Visibility.Visible;
            ActiveFill.Height = pill.ActualHeight;
            // The lit tab floats: a soft static glow in the hue (0 offset), none at Motion Off.
            if (Level == MotionLevel.Off) ActiveFill.Effect = null;
            else if (ActiveFill.Effect is not System.Windows.Media.Effects.DropShadowEffect glow || glow.Color != _hue)
                ActiveFill.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = _hue,
                    ShadowDepth = 0,
                    BlurRadius = NavStripRules.ActiveGlowBlur,
                    Opacity = NavStripRules.ActiveGlowOpacity,
                };

            int ms = animate && ActiveFill.Width > 0 ? NavStripRules.SlideMs(Level) : 0;
            if (ms <= 0)
            {
                ActiveFillShift.BeginAnimation(TranslateTransform.XProperty, null);
                ActiveFill.BeginAnimation(WidthProperty, null);
                ActiveFillShift.X = x;
                ActiveFill.Width = pill.ActualWidth;
                return;
            }
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(ms);
            ActiveFillShift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(x, dur) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
            ActiveFill.BeginAnimation(WidthProperty,
                new DoubleAnimation(pill.ActualWidth, dur) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        }

        private void PillRow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_pills.Count == 0) return;
            int current = _pills.FindIndex(p => p.Pill.IsKeyboardFocused);
            if (current < 0) current = _pills.FindIndex(p => IsActive(p.Tab.Key));

            if (e.Key is Key.Enter or Key.Space)
            {
                if (current >= 0) Choose(_pills[current].Tab, focus: true);
                e.Handled = true;
                return;
            }
            int next = NavStripRules.MoveIndex(current, _pills.Count, e.Key);
            if (next < 0) return;
            e.Handled = true;
            var target = _pills[next];
            // Automatic activation for pages (they are built); launchers and windows only take
            // focus, so arrowing past "Mods" never opens a dialog.
            if (target.Tab.Kind is NavTabKind.Tab or NavTabKind.Zone) Choose(target.Tab, focus: true);
            else target.Pill.Focus();
        }

        // =====================================================================================
        //  breadcrumb
        // =====================================================================================

        private void CrumbSection_Click(object sender, RoutedEventArgs e)
        {
            if (_section != null) SectionRequested?.Invoke(_section);
        }

        private void Crossfade(UIElement element)
        {
            var level = Level;
            if (level == MotionLevel.Off) { element.BeginAnimation(OpacityProperty, null); element.Opacity = 1; return; }
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1,
                TimeSpan.FromMilliseconds(level == MotionLevel.Reduced ? 75 : 150)));
        }

        private static string SafeLoc(string? key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            try
            {
                var s = Loc.Get(key!);
                return string.IsNullOrEmpty(s) || s == key ? fallback : s;
            }
            catch { return fallback; }
        }

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
