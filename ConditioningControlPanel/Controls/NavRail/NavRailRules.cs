using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Controls.NavRail
{
    /// <summary>
    /// The section rail's decisions, pure (no WPF) so tests pin them without a window
    /// (nav rework, 2026-10-06). MainWindow.NavRail.cs is only the painter.
    /// </summary>
    internal static class NavRailRules
    {
        /// <summary>The badge pill caps here: anything above reads "9+".</summary>
        internal const int BadgeCap = 9;

        // Polish wave 2: every rail hue comes from NavStripRules.Accent; these are only alphas.
        /// <summary>The active row's fill behind the medallion (about 20%).</summary>
        internal const byte FillAlpha = 0x33;
        /// <summary>The faint inner tint of every medallion tile (about 18%).</summary>
        internal const byte TileTintAlpha = 0x40;
        /// <summary>The idle ring (80%), its hover (95%), the active ring (solid).</summary>
        // Idle ring 80% (polish wave 9, was 72%): the owner asked for the hue around the icons to
        // be more noticeable and the ring thicker. Still under the active ring, which is solid
        // AND lifted toward white (RingActiveLift).
        internal const byte RingIdleAlpha = 0xCC, RingHoverAlpha = 0xF2, RingActiveAlpha = 0xFF;

        /// <summary>How far the active ring is mixed toward white (25%): brighter in the hue, so
        /// the lit row reads at a glance without a second colour.</summary>
        internal const double RingActiveLift = 0.25;

        /// <summary>Ring thickness: 3 px at rest and on hover, 3.5 px on the lit row. The ring is
        /// its own Border drawn OVER the art (Tag "navring"), so the thickness eats no art.</summary>
        internal const double RingIdleThickness = 3.0, RingActiveThickness = 3.5;

        /// <summary>The hue wash over the medallion art (Tag "navtint"). Polish wave 11: the owner
        /// read the rail as pastel, and a pastel hue laid over dark ink art lifts and greys it, so
        /// the idle wash is gone (0) and the lit row only breathes the hue (about 5%). The ring,
        /// the halo and the bar carry the section colour; the art reads as its author made it.</summary>
        internal const byte ArtTintActiveAlpha = 0x0D, ArtTintIdleAlpha = 0x00;

        // ============================== vivid ring (polish wave 11) ==============================
        // The section hues (NavStripRules.Accent) are pastel by design: L .69 to .81, good for text
        // on the dark chrome. A ring in them reads milky. The ring takes the same hue angle at a
        // neon lightness instead, and wears the lamp: bright top-left, deep bottom-right.

        /// <summary>The ring's lightness (HSL) and its saturation floor.</summary>
        internal const double VividLightness = 0.58, VividSaturation = 0.80;

        /// <summary>How far the ring's lit corner mixes toward white, and its shaded corner toward black.</summary>
        internal const double RingBevelLight = 0.35, RingBevelShade = 0.50;

        /// <summary>The section hue at neon strength: same angle, lightness pulled down to
        /// <see cref="VividLightness"/>, saturation raised to at least <see cref="VividSaturation"/>.
        /// A hue already deeper than that keeps its own lightness.</summary>
        internal static System.Windows.Media.Color Vivid(System.Windows.Media.Color hue)
        {
            var (h, s, l) = ToHsl(hue);
            return FromHsl(h, Math.Max(s, VividSaturation), Math.Min(l, VividLightness), hue.A);
        }

        /// <summary>HSL of a colour: hue in degrees 0..360, saturation and lightness 0..1.</summary>
        internal static (double H, double S, double L) ToHsl(System.Windows.Media.Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, d = max - min;
            if (d < 1e-9) return (0, 0, l);
            double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h * 60, s, l);
        }

        internal static System.Windows.Media.Color FromHsl(double h, double s, double l, byte a = 0xFF)
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q, k = h / 360.0;
            static double Ch(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            }
            byte B(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
            return System.Windows.Media.Color.FromArgb(a, B(Ch(p, q, k + 1 / 3.0)), B(Ch(p, q, k)), B(Ch(p, q, k - 1 / 3.0)));
        }

        /// <summary>
        /// The ring as a diagonal gradient (StartPoint 0,0 to 1,1). Raised (idle, hover): the lamp
        /// catches the top-left (vivid mixed toward white), the vivid hue runs through the middle,
        /// the bottom-right falls into shade. ON IS PRESSED IN: a lit row's ring is the lip of a
        /// socket, so the light flips (shaded top-left, lit bottom-right) and it is solid.
        /// </summary>
        internal static (System.Windows.Media.Color Color, double Offset)[] RingStops(System.Windows.Media.Color hue, bool active, bool hover)
        {
            var v = Vivid(hue);
            byte a = RingAlpha(active, hover);
            var light = WithAlpha(NavStripRules.Mix(v, System.Windows.Media.Colors.White, RingBevelLight), a);
            var mid = WithAlpha(v, a);
            var shade = WithAlpha(NavStripRules.Mix(v, System.Windows.Media.Colors.Black, RingBevelShade), a);
            return active
                ? new[] { (shade, 0.0), (mid, 0.5), (light, 1.0) }
                : new[] { (light, 0.0), (mid, 0.42), (shade, 1.0) };
        }

        // ============================== coin glass (polish wave 11) ==============================

        /// <summary>The coin's outer edge: a dark hairline just outside the 56 px ring, so the coin
        /// separates from the rail (58 px, inside the 60 px medallion budget).</summary>
        internal const double CoinOuterSize = 58, CoinOuterRadius = 16;

        /// <summary>Where the art meets the dish: a 2.5 px inner shadow ring inside the lip, strongest
        /// at the top-left (the rim overhangs the lamp side).</summary>
        internal const double CoinInnerSize = 48, CoinInnerRadius = 11, CoinInnerThickness = 2.5;

        /// <summary>The specular crescent: the sliver between the face and the face shifted by this
        /// offset, faded out past the top-left corner. Peak alpha <see cref="SpecularAlpha"/>.</summary>
        internal const double SpecularOffsetX = 2.5, SpecularOffsetY = 3.0;
        internal const byte SpecularAlpha = 0xC8;

        /// <summary>The contact shadow falls down AND right of a lamp at the top-left.</summary>
        internal const double CoinDiscRightPx = 1.5;

        /// <summary>A lit coin's socket is darker than the shared DepthPressedShade (owner: deeper socket).</summary>
        internal const byte SocketTopAlpha = 0x66;

        /// <summary>The spur that bridges the window edge to the lit medallion: 20 x 8 px, its top
        /// at 25 px so its centre sits on the tile centre (ContentPresenter top 1 + 56 / 2 = 29).
        /// The gradient runs from the hue at 90% at the window edge to 60% against the ring.</summary>
        internal const double SpurWidth = 20, SpurHeight = 8, SpurTop = 25;
        internal const byte SpurEdgeAlpha = 0xE6, SpurRingAlpha = 0x99;

        /// <summary>The ring alpha for a row: solid when active, brighter on hover, else 80%.</summary>
        internal static byte RingAlpha(bool active, bool hover) =>
            active ? RingActiveAlpha : hover ? RingHoverAlpha : RingIdleAlpha;

        /// <summary>The ring's flat colour (wave 9; polish wave 11 paints <see cref="RingStops"/>
        /// instead and keeps this as the colour a flat consumer would use): the plain hue at
        /// <see cref="RingAlpha"/> idle and on hover, the hue lifted 25% toward white, solid, when active.</summary>
        internal static System.Windows.Media.Color RingColor(System.Windows.Media.Color hue, bool active, bool hover) =>
            active
                ? WithAlpha(NavStripRules.Mix(hue, System.Windows.Media.Colors.White, RingActiveLift), RingActiveAlpha)
                : WithAlpha(hue, RingAlpha(false, hover));

        /// <summary>The ring thickness for a row.</summary>
        internal static double RingThickness(bool active) => active ? RingActiveThickness : RingIdleThickness;

        /// <summary>The art wash alpha for a row.</summary>
        internal static byte ArtTintAlpha(bool active) => active ? ArtTintActiveAlpha : ArtTintIdleAlpha;

        /// <summary>A colour with its alpha replaced.</summary>
        internal static System.Windows.Media.Color WithAlpha(System.Windows.Media.Color c, byte a) =>
            System.Windows.Media.Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>The rail's rows, top to bottom: every section but the Settings gear.</summary>
        internal static IReadOnlyList<NavSection> RailSections { get; } =
            NavSections.Order.Where(s => s.Key != NavSections.Settings).ToArray();

        /// <summary>The badge text for a count: null (no pill) at 0 or below, "9+" past the cap.</summary>
        internal static string? BadgeText(int count) =>
            count <= 0 ? null : count > BadgeCap ? BadgeCap + "+" : count.ToString();

        /// <summary>A row's Tag to its section key. The gear keeps Tag "appsettings" (the tab key
        /// "settings" is the dashboard), every other row carries the section key itself.</summary>
        internal static string SectionForDoorTag(string tag) =>
            string.Equals(tag, "appsettings", StringComparison.OrdinalIgnoreCase) ? NavSections.Settings : tag;

        /// <summary>The row Tag for a section key (the inverse of <see cref="SectionForDoorTag"/>).</summary>
        internal static string DoorTagForSection(string section) =>
            section == NavSections.Settings ? "appsettings" : section;

        /// <summary>
        /// Where a section row click lands: the section's remembered last tab when the tab strip
        /// wrote one (AppSettings.NavLastTabBySection, a JSON object section -> tab key) and that
        /// tab still belongs to the section as a page or a zone, else the section's default tab.
        /// Windows and launchers are never remembered: a row click must navigate, not launch.
        /// Unreadable JSON falls back to the default; it never throws.
        /// </summary>
        internal static string? TargetTab(string section, string? lastTabJson)
        {
            var s = NavSections.Find(section);
            if (s == null) return null;
            var last = ReadLastTab(section, lastTabJson);
            if (!string.IsNullOrEmpty(last))
            {
                foreach (var t in s.Tabs)
                {
                    if (!string.Equals(t.Key, last, StringComparison.OrdinalIgnoreCase)) continue;
                    if (t.Kind is NavTabKind.Window or NavTabKind.Launcher) break;
                    // Home's rail row is the way back from Premium (polish 12 round 2): it always
                    // opens the dashboard, never the hidden page it was last on.
                    if (t.Hidden && s.Key == NavSections.Home) break;
                    return t.Key;
                }
            }
            return s.DefaultTab;
        }

        internal static string? ReadLastTab(string section, string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                foreach (var p in doc.RootElement.EnumerateObject())
                    if (string.Equals(p.Name, section, StringComparison.OrdinalIgnoreCase)
                        && p.Value.ValueKind == JsonValueKind.String)
                        return p.Value.GetString();
            }
            catch (JsonException) { }
            return null;
        }

        /// <summary>The Ctrl+N digit for a section (1-based rail position), 0 for the gear or an unknown key.</summary>
        internal static int ShortcutNumber(string section)
        {
            for (int i = 0; i < RailSections.Count; i++)
                if (RailSections[i].Key == section) return i + 1;
            return 0;
        }

        /// <summary>Pressed/scale timing for the current motion level: Full as authored, Reduced
        /// halves it, Off is instant (0).</summary>
        internal static int Ms(int fullMs, Models.MotionLevel level) => level switch
        {
            Models.MotionLevel.Off => 0,
            Models.MotionLevel.Reduced => fullMs / 2,
            _ => fullMs,
        };

        // ============================== the coin (polish wave 10, depth) ==============================
        // Every rail medallion is a COIN in a socket (Controls/Depth/DepthRules.cs is the law and
        // owns every height, alpha and timing; these only say how the rail applies them).

        /// <summary>The coin's contact shadow: a flat disc under the tile's foot, centred on the
        /// tile's bottom edge and pushed down by <see cref="DepthRules.ShadowFor"/> (RaisedPx at
        /// rest). Wider than tall so only its lower half shows below the coin.</summary>
        internal const double CoinDiscWidth = 54, CoinDiscHeight = 12;

        /// <summary>The coin's lip: a 1 px rim just INSIDE the 3 px hue ring (56 - 2 x 3 = 50), so
        /// the ring keeps its job and the lip still catches the lamp.</summary>
        internal const double CoinRimSize = 50, CoinRimRadius = 12;

        /// <summary>The pointer tilt eases over the launcher tile's own 90 ms.</summary>
        internal const int CoinTiltMs = 90;

        /// <summary>Where a coin's face sits, px DOWN from rest: the shared travel rule.</summary>
        internal static double CoinTravel(bool pressed, bool active, bool hovered) =>
            Depth.DepthRules.TravelFor(enabled: true, pressed, active, hovered);

        /// <summary>The contact shadow's offset below the face for a state; 0 means no shadow
        /// (pressed or lit: the coin sits on the sheet).</summary>
        internal static double CoinShadow(bool pressed, bool active, bool hovered) =>
            Depth.DepthRules.ShadowFor(enabled: true, pressed, active, hovered);

        /// <summary>The spring's overshoot point on release: past the target by ReleaseOvershootPx
        /// in the direction of travel, then back. No travel, no overshoot.</summary>
        internal static double CoinOvershoot(double from, double to) =>
            to == from ? to : to + Math.Sign(to - from) * Depth.DepthRules.ReleaseOvershootPx;

        /// <summary>The hover lean toward the pointer (the launcher recipe): nx, ny are the pointer
        /// over the tile in -1..1; right side dips when the pointer is right, flipped by the
        /// vertical half. Never past TiltDegrees.</summary>
        internal static double CoinTilt(double nx, double ny)
        {
            nx = Math.Clamp(nx, -1, 1);
            ny = Math.Clamp(ny, -1, 1);
            return nx * -ny * Depth.DepthRules.TiltDegrees;
        }

        /// <summary>A lit coin sits in its socket and never leans; an idle one leans on hover
        /// where the motion level and tier allow it.</summary>
        internal static bool CoinTilts(bool active, Models.MotionLevel level, Models.PerformanceTier tier) =>
            !active && Depth.DepthRules.TiltAllowed(level, tier);

        /// <summary>The contact disc's stops, tinted by the row's hue: the DepthDropDisc shape
        /// (ShadowAlpha at the centre, 0x5C at 60%, clear at the edge) in DepthRules.ShadowColor.</summary>
        internal static (System.Windows.Media.Color Color, double Offset)[] CoinDiscStops(System.Windows.Media.Color hue) => new[]
        {
            (Depth.DepthRules.ShadowColor(hue), 0.0),
            (Depth.DepthRules.ShadowColor(hue, 0x5C / 255.0), 0.6),
            (Depth.DepthRules.ShadowColor(hue, 0), 1.0),
        };

        /// <summary>The rail's shadow on the page, tinted by the section: DepthRailShadow's 0xBF
        /// at the rail's edge, clear at RailShadowPx.</summary>
        internal static (System.Windows.Media.Color Color, double Offset)[] RailShadowStops(System.Windows.Media.Color hue) => new[]
        {
            (Depth.DepthRules.ShadowColor(hue, 0xBF / 255.0), 0.0),
            (Depth.DepthRules.ShadowColor(hue, 0), 1.0),
        };
    }
}
