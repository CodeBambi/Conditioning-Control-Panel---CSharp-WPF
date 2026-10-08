using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Depth;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Motion;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Nav
{
    /// <summary>
    /// The section rail's decisions, ported from WPF 7.1.5 Controls/NavRail/NavRailRules.cs with
    /// colours as ARGB <see cref="uint"/>. MainWindow.NavRail.cs (WPF) / the Avalonia shell is only
    /// the painter. Numbers verbatim.
    /// </summary>
    public static class NavRailRules
    {
        /// <summary>The badge pill caps here: anything above reads "9+".</summary>
        public const int BadgeCap = 9;

        /// <summary>The active row's fill behind the medallion (about 20%).</summary>
        public const byte FillAlpha = 0x33;
        /// <summary>The faint inner tint of every medallion tile (about 18%).</summary>
        public const byte TileTintAlpha = 0x40;
        /// <summary>The idle ring (80%), its hover (95%), the active ring (solid).</summary>
        public const byte RingIdleAlpha = 0xCC, RingHoverAlpha = 0xF2, RingActiveAlpha = 0xFF;

        /// <summary>How far the active ring is mixed toward white (25%).</summary>
        public const double RingActiveLift = 0.25;

        /// <summary>Ring thickness: 3 px at rest and on hover, 3.5 px on the lit row (drawn OVER the art).</summary>
        public const double RingIdleThickness = 3.0, RingActiveThickness = 3.5;

        /// <summary>The hue wash over the medallion art: none idle, about 5% on the lit row.</summary>
        public const byte ArtTintActiveAlpha = 0x0D, ArtTintIdleAlpha = 0x00;

        // ---- vivid ring (polish wave 11) -------------------------------------------------------

        /// <summary>The ring's lightness (HSL) and its saturation floor.</summary>
        public const double VividLightness = 0.58, VividSaturation = 0.80;

        /// <summary>How far the ring's lit corner mixes toward white, and its shaded corner toward black.</summary>
        public const double RingBevelLight = 0.35, RingBevelShade = 0.50;

        /// <summary>The section hue at neon strength: same angle, lightness pulled down to 0.58,
        /// saturation raised to at least 0.80. A deeper hue keeps its own lightness.</summary>
        public static uint Vivid(uint hue)
        {
            var (h, s, l) = ToHsl(hue);
            return FromHsl(h, Math.Max(s, VividSaturation), Math.Min(l, VividLightness), Argb.A(hue));
        }

        /// <summary>HSL of a colour: hue in degrees 0..360, saturation and lightness 0..1.</summary>
        public static (double H, double S, double L) ToHsl(uint c)
        {
            double r = Argb.R(c) / 255.0, g = Argb.G(c) / 255.0, b = Argb.B(c) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, d = max - min;
            if (d < 1e-9) return (0, 0, l);
            double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            return (h * 60, s, l);
        }

        /// <summary>The rail's own HSL -> colour (keeps alpha; no hue wrap, no grey shortcut, as WPF).</summary>
        public static uint FromHsl(double h, double s, double l, byte a = 0xFF)
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
            return Argb.FromArgb(a, B(Ch(p, q, k + 1 / 3.0)), B(Ch(p, q, k)), B(Ch(p, q, k - 1 / 3.0)));
        }

        /// <summary>
        /// The ring as a diagonal gradient (StartPoint 0,0 to 1,1). Raised (idle, hover): lit
        /// top-left, vivid middle, shaded bottom-right. ON IS PRESSED IN: a lit row's light flips
        /// and it is solid.
        /// </summary>
        public static (uint Color, double Offset)[] RingStops(uint hue, bool active, bool hover)
        {
            var v = Vivid(hue);
            byte a = RingAlpha(active, hover);
            var light = WithAlpha(NavStripRules.Mix(v, Argb.White, RingBevelLight), a);
            var mid = WithAlpha(v, a);
            var shade = WithAlpha(NavStripRules.Mix(v, Argb.Black, RingBevelShade), a);
            return active
                ? new[] { (shade, 0.0), (mid, 0.5), (light, 1.0) }
                : new[] { (light, 0.0), (mid, 0.42), (shade, 1.0) };
        }

        // ---- coin glass (polish wave 11) -------------------------------------------------------

        /// <summary>The coin's outer edge: a dark hairline just outside the 56 px ring (58 px).</summary>
        public const double CoinOuterSize = 58, CoinOuterRadius = 16;

        /// <summary>Where the art meets the dish: a 2.5 px inner shadow ring inside the lip.</summary>
        public const double CoinInnerSize = 48, CoinInnerRadius = 11, CoinInnerThickness = 2.5;

        /// <summary>The specular crescent offset and its peak alpha.</summary>
        public const double SpecularOffsetX = 2.5, SpecularOffsetY = 3.0;
        public const byte SpecularAlpha = 0xC8;

        /// <summary>The contact shadow falls down AND right of a lamp at the top-left.</summary>
        public const double CoinDiscRightPx = 1.5;

        /// <summary>A lit coin's socket is darker than the shared DepthPressedShade.</summary>
        public const byte SocketTopAlpha = 0x66;

        /// <summary>The spur that bridges the window edge to the lit medallion: 20 x 8 px at top 25,
        /// the hue at 90% at the window edge to 60% against the ring.</summary>
        public const double SpurWidth = 20, SpurHeight = 8, SpurTop = 25;
        public const byte SpurEdgeAlpha = 0xE6, SpurRingAlpha = 0x99;

        /// <summary>The ring alpha for a row: solid when active, brighter on hover, else 80%.</summary>
        public static byte RingAlpha(bool active, bool hover) =>
            active ? RingActiveAlpha : hover ? RingHoverAlpha : RingIdleAlpha;

        /// <summary>The ring's flat colour: the hue at RingAlpha idle / hover, lifted 25% toward
        /// white and solid when active.</summary>
        public static uint RingColor(uint hue, bool active, bool hover) =>
            active
                ? WithAlpha(NavStripRules.Mix(hue, Argb.White, RingActiveLift), RingActiveAlpha)
                : WithAlpha(hue, RingAlpha(false, hover));

        /// <summary>The ring thickness for a row.</summary>
        public static double RingThickness(bool active) => active ? RingActiveThickness : RingIdleThickness;

        /// <summary>The art wash alpha for a row.</summary>
        public static byte ArtTintAlpha(bool active) => active ? ArtTintActiveAlpha : ArtTintIdleAlpha;

        /// <summary>A colour with its alpha replaced.</summary>
        public static uint WithAlpha(uint c, byte a) => Argb.WithAlpha(c, a);

        /// <summary>The rail's rows, top to bottom: every section but the Settings gear.</summary>
        public static IReadOnlyList<NavSection> RailSections { get; } =
            NavSections.Order.Where(s => s.Key != NavSections.Settings).ToArray();

        /// <summary>The badge text for a count: null (no pill) at 0 or below, "9+" past the cap.</summary>
        public static string? BadgeText(int count) =>
            count <= 0 ? null : count > BadgeCap ? BadgeCap + "+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A badge the player has already seen sits back at this opacity (7.1.5).</summary>
        public const double BadgeSeenOpacity = 0.45;

        /// <summary>Full when the count is fresh, dimmed once seen.</summary>
        public static double BadgeOpacity(bool fresh) => fresh ? 1.0 : BadgeSeenOpacity;

        /// <summary>A row's Tag to its section key. The gear keeps Tag "appsettings".</summary>
        public static string SectionForDoorTag(string tag) =>
            string.Equals(tag, "appsettings", StringComparison.OrdinalIgnoreCase) ? NavSections.Settings : tag;

        /// <summary>The row Tag for a section key (the inverse of <see cref="SectionForDoorTag"/>).</summary>
        public static string DoorTagForSection(string section) =>
            section == NavSections.Settings ? "appsettings" : section;

        /// <summary>
        /// Where a section row click lands: the remembered last tab when it still belongs to the
        /// section as a page or a zone, else the default. Windows and launchers are never
        /// remembered; Home always opens the dashboard. Unreadable JSON falls back to the default.
        /// </summary>
        public static string? TargetTab(string section, string? lastTabJson)
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
                    if (t.Hidden && s.Key == NavSections.Home) break;
                    return t.Key;
                }
            }
            return s.DefaultTab;
        }

        public static string? ReadLastTab(string section, string? json)
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
        public static int ShortcutNumber(string section)
        {
            for (int i = 0; i < RailSections.Count; i++)
                if (RailSections[i].Key == section) return i + 1;
            return 0;
        }

        /// <summary>Pressed/scale timing for the current motion level: Full as authored, Reduced
        /// halves it, Off is instant (0).</summary>
        public static int Ms(int fullMs, MotionLevel level) => MotionGate.Ms(fullMs, level);

        // ---- the coin (polish wave 10, depth) -------------------------------------------------

        /// <summary>The coin's contact shadow disc under the tile's foot.</summary>
        public const double CoinDiscWidth = 54, CoinDiscHeight = 12;

        /// <summary>The coin's lip: a 1 px rim just inside the 3 px hue ring.</summary>
        public const double CoinRimSize = 50, CoinRimRadius = 12;

        /// <summary>The pointer tilt eases over the launcher tile's own 90 ms.</summary>
        public const int CoinTiltMs = 90;

        /// <summary>Where a coin's face sits, px DOWN from rest: the shared travel rule.</summary>
        public static double CoinTravel(bool pressed, bool active, bool hovered) =>
            DepthRules.TravelFor(enabled: true, pressed, active, hovered);

        /// <summary>The contact shadow's offset below the face; 0 = no shadow.</summary>
        public static double CoinShadow(bool pressed, bool active, bool hovered) =>
            DepthRules.ShadowFor(enabled: true, pressed, active, hovered);

        /// <summary>The spring's overshoot point on release: past the target by ReleaseOvershootPx
        /// in the direction of travel, then back. No travel, no overshoot.</summary>
        public static double CoinOvershoot(double from, double to) =>
            to == from ? to : to + Math.Sign(to - from) * DepthRules.ReleaseOvershootPx;

        /// <summary>The hover lean toward the pointer (nx, ny in -1..1). Never past TiltDegrees.</summary>
        public static double CoinTilt(double nx, double ny)
        {
            nx = Math.Clamp(nx, -1, 1);
            ny = Math.Clamp(ny, -1, 1);
            return nx * -ny * DepthRules.TiltDegrees;
        }

        /// <summary>A lit coin sits in its socket and never leans; an idle one leans where allowed.</summary>
        public static bool CoinTilts(bool active, MotionLevel level, PerformanceTier tier) =>
            !active && DepthRules.TiltAllowed(level, tier);

        /// <summary>The contact disc's stops (radial), tinted by the row's hue.</summary>
        public static (uint Color, double Offset)[] CoinDiscStops(uint hue) => new[]
        {
            (DepthRules.ShadowColor(hue), 0.0),
            (DepthRules.ShadowColor(hue, 0x5C / 255.0), 0.6),
            (DepthRules.ShadowColor(hue, 0), 1.0),
        };

        /// <summary>The rail's shadow on the page (horizontal), tinted by the section.</summary>
        public static (uint Color, double Offset)[] RailShadowStops(uint hue) => new[]
        {
            (DepthRules.ShadowColor(hue, 0xBF / 255.0), 0.0),
            (DepthRules.ShadowColor(hue, 0), 1.0),
        };
    }
}
