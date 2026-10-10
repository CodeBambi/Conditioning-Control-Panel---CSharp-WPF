using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ConditioningControlPanel.Fx;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Nav
{
    /// <summary>A key the section strip reacts to (WPF Key.Left / Right / Home / End).</summary>
    public enum StripKey { Other, Left, Right, Home, End }

    /// <summary>
    /// The pure half of the section page header, ported from WPF 7.1.5
    /// (Controls/NavRail/SectionTabStrip.xaml.cs, class NavStripRules) with colours as ARGB
    /// <see cref="uint"/>: which pills a section draws, which pill a tab lights, the section hue
    /// table, the pill paint and its contrast rule, keyboard wrap and the last-tab memory.
    /// Numbers and byte rounding are the WPF ones; the WPF brushes come back as stop arrays
    /// (<c>(Color, Offset)</c>, vertical, StartPoint 0,0 to EndPoint 0,1).
    /// </summary>
    public static class NavStripRules
    {
        /// <summary>Home is the dashboard (no strip); Settings keeps its own left pill column.</summary>
        public static bool ShowsPills(string? section) =>
            section != null && section != NavSections.Home && section != NavSections.Settings;

        /// <summary>The header (breadcrumb row) shows everywhere but Home.</summary>
        public static bool ShowsHeader(string? section) =>
            section != null && section != NavSections.Home;

        /// <summary>The loc key of a pill's "what is this" line, shown on its "?" badge.</summary>
        public static string HelpKey(NavTab tab) => "nav_help_" + tab.Key;

        /// <summary>Pills that open their own window ask first, so the click is never a surprise.</summary>
        public static bool AsksBeforeOpening(NavTab tab) => tab.Kind == NavTabKind.Window;

        /// <summary>
        /// Is a pill of this tier locked for the account on this machine? A paying account sees no
        /// tier sign on the pages it already owns. WPF read App.Patreon; here the head passes the two
        /// canonical gates, null when there is no account service yet (tests, early startup), which
        /// reads as locked so the sign is never hidden by mistake.
        /// </summary>
        public static bool PillLocked(int tier, bool? hasPremiumAccess, bool? hasLabAccess)
        {
            if (tier <= 0) return false;
            var gate = tier == 1 ? hasPremiumAccess : hasLabAccess;
            return gate != true;
        }

        /// <summary>The pills a section draws, in table order, hidden tabs skipped.</summary>
        public static IReadOnlyList<NavTab> Pills(string? section)
        {
            if (!ShowsPills(section)) return Array.Empty<NavTab>();
            return NavSections.Find(section)?.Tabs.Where(t => !t.Hidden).ToArray() ?? Array.Empty<NavTab>();
        }

        /// <summary>
        /// The pill a tab key lights. A pill key lights itself; the permanent alias "lab" lights
        /// Games; pages that live inside a Play zone light that zone (Graded Intake and Lockdown
        /// sit in Sessions, Blink Trainer in Eyes). Null when no pill owns the page (Spiral Room).
        /// </summary>
        public static string? ActivePill(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return null;
            var key = tab.ToLowerInvariant();
            switch (key)
            {
                case "lab": return "play";
                case "gradedintake":
                case "lockdown": return "playsessions";
                case "blinktrainer": return "playeyes";
            }
            var section = NavSections.SectionForTab(key);
            return Pills(section).Any(p => p.Key == key) ? key : null;
        }

        /// <summary>The label key a breadcrumb shows for a tab (its own row in the table).</summary>
        public static string? PageLabelKey(string? tab)
        {
            if (string.IsNullOrEmpty(tab)) return null;
            var key = tab.ToLowerInvariant() == "lab" ? "play" : tab.ToLowerInvariant();
            foreach (var t in NavSections.AllTabs)
                if (t.Key == key) return t.LabelKey;
            return null;
        }

        /// <summary>Keyboard move inside the strip: Left/Right wrap, Home/End jump. -1 = not a strip key.</summary>
        public static int MoveIndex(int current, int count, StripKey key)
        {
            if (count <= 0) return -1;
            return key switch
            {
                StripKey.Left => current <= 0 ? count - 1 : current - 1,
                StripKey.Right => current < 0 || current >= count - 1 ? 0 : current + 1,
                StripKey.Home => 0,
                StripKey.End => count - 1,
                _ => -1,
            };
        }

        // Section hues (polish wave 2): one hue PER SECTION, never gold (T1), cyan (T2), red
        // (Circe / danger) or mint (credit), every pair at least 18 degrees apart. The gear shares
        // Home's lilac.
        public static readonly uint Lilac = Argb.FromRgb(0xB7, 0x9C, 0xFF);       // Home, Settings
        public static readonly uint Pink = Argb.FromRgb(0xFF, 0x69, 0xB4);        // Studio
        public static readonly uint Orchid = Argb.FromRgb(0xE0, 0x70, 0xFF);      // Companion
        public static readonly uint VioletBlue = Argb.FromRgb(0x7A, 0x86, 0xFF);  // Play
        public static readonly uint Sky = Argb.FromRgb(0x5F, 0xB0, 0xFF);         // Social
        public static readonly uint Coral = Argb.FromRgb(0xFF, 0x9A, 0x6B);       // You
        public static readonly uint Sage = Argb.FromRgb(0xA8, 0xD8, 0xA0);        // Library

        public static uint Accent(string? section) => section switch
        {
            NavSections.Studio => Pink,
            NavSections.Companion => Orchid,
            NavSections.Play => VioletBlue,
            NavSections.Social => Sky,
            NavSections.You => Coral,
            NavSections.Library => Sage,
            _ => Lilac,   // Home, Settings
        };

        // ---- Section ink (polish wave 8, readability pass) ----------------------------------

        /// <summary>The app's lightest text token (Colors.xaml TextLight).</summary>
        public static readonly uint TextLight = Argb.FromRgb(0xF0, 0xF0, 0xF5);

        /// <summary>How far the ink moves from the hue toward TextLight.</summary>
        public const double InkLift = 0.35;
        /// <summary>How far the optional title tint moves from TextLight toward the hue.</summary>
        public const double TintPull = 0.15;
        /// <summary>The rule under an eyebrow (35%) and a card outline (25%), as bytes.</summary>
        public const byte RuleAlpha = 0x59, OutlineAlpha = 0x40;

        /// <summary>Eyebrow / label / link ink: the hue mixed 35% toward TextLight.</summary>
        public static uint Ink(string? section) => Mix(Accent(section), TextLight, InkLift);

        /// <summary>Optional title tint: TextLight mixed 15% toward the hue.</summary>
        public static uint Tint(string? section) => Mix(TextLight, Accent(section), TintPull);

        /// <summary>The 1 px rule under an eyebrow: the hue at 35% alpha.</summary>
        public static uint Rule(string? section) => Argb.WithAlpha(Accent(section), RuleAlpha);

        /// <summary>A card outline: the hue at 25% alpha.</summary>
        public static uint Outline(string? section) => Argb.WithAlpha(Accent(section), OutlineAlpha);

        /// <summary>Last-tab memory: section -> tab, stored as JSON in AppSettings.NavLastTabBySection.</summary>
        public static Dictionary<string, string> ParseLastTabs(string? json)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json)) return map;
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json!);
                if (raw != null)
                    foreach (var (k, v) in raw)
                        if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v)) map[k] = v;
            }
            catch (JsonException) { }
            return map;
        }

        /// <summary>The JSON with one section's last tab set. Unchanged JSON when nothing moved.</summary>
        public static string WithLastTab(string? json, string section, string tab)
        {
            var map = ParseLastTabs(json);
            if (map.TryGetValue(section, out var had) && string.Equals(had, tab, StringComparison.OrdinalIgnoreCase))
                return json ?? string.Empty;
            map[section] = tab;
            return JsonSerializer.Serialize(map.OrderBy(p => p.Key, StringComparer.Ordinal)
                                               .ToDictionary(p => p.Key, p => p.Value));
        }

        /// <summary>The tab a section returns to: its remembered tab when the table still owns it,
        /// otherwise its default.</summary>
        public static string? LastTabFor(string? json, string section)
        {
            var def = NavSections.DefaultTab(section);
            if (ParseLastTabs(json).TryGetValue(section, out var tab)
                && string.Equals(NavSections.SectionForTab(tab), section, StringComparison.OrdinalIgnoreCase)
                && !NavSections.Redirects.ContainsKey(tab))
                return tab;
            return def;
        }

        /// <summary>Slide duration for the active fill: 180 ms, halved at Reduced, 0 at Off.</summary>
        public static int SlideMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => 90,
            _ => 180,
        };

        // ---- Pill paint ------------------------------------------------------------------------

        /// <summary>Dark ink for text on a light fill (the app's deepest plum).</summary>
        public static readonly uint DarkInk = Argb.FromRgb(0x15, 0x12, 0x1F);

        /// <summary>Inactive pill text: the hue at 95%.</summary>
        public const double RestTextAlpha = 0.95;
        /// <summary>Inactive pill plate: the tab's tint at 24% (34% across the top, 14% at the foot).</summary>
        public const double RestFillAlpha = 0.24;
        /// <summary>How far a plate's top and foot sit from its middle alpha.</summary>
        public const double PlateLift = 0.10;
        /// <summary>Inactive pill border: the tint at 95%, lit along the top and shaded along the foot.</summary>
        public const double RestOutlineAlpha = 0.95;
        /// <summary>The outline's top edge mixes this far toward white, its foot this far toward ink.</summary>
        public const double BevelLight = 0.70;
        public const double BevelShade = 0.75;
        /// <summary>Where the outline's shaded foot starts.</summary>
        public const double OutlineFootOffset = 0.85;
        /// <summary>Pill face border: 1.5 px at rest, 2 px on the lit tab, inside the 36 px face.</summary>
        public const double RestFaceThickness = 1.5;
        public const double ActiveFaceThickness = 2.0;
        /// <summary>Hovered inactive pill: the plate in the tint at 38%.</summary>
        public const double HoverFillAlpha = 0.38;
        /// <summary>The pill track is a tray: deep ink at 40% under the hue at 10%; 1.5 px border
        /// shaded along the top (70% ink) and the hue at 40% below.</summary>
        public const double TrackInkAlpha = 0.40;
        public const double TrackFillAlpha = 0.10;
        public const double TrackBorderAlpha = 0.40;
        public const double TrackInsetAlpha = 0.70;
        /// <summary>The active pill's soft outer glow in the hue (static, 0 offset).</summary>
        public const double ActiveGlowBlur = 16;
        public const double ActiveGlowOpacity = 0.75;
        /// <summary>The active pill's 2 px inner ring: near-white in the tab's tint.</summary>
        public const double ActiveRingAlpha = 0.55;
        public const double ActiveRingTopAlpha = 0.80;
        public const double ActiveRingFootAlpha = 0.30;
        public const double ActiveRingWhite = 0.75;
        /// <summary>The active pill's glyph: the label's ink mixed this far toward the tab's tint.</summary>
        public const double ActiveGlyphTint = 0.30;
        /// <summary>Pill size: 38 px tall, 14.5 px SemiBold label, 16 px padding, 6 px between pills.</summary>
        public const double PillHeight = 38;
        public const double PillFontSize = 14.5;
        public const double PillPadding = 16;
        public const double PillGap = 6;
        /// <summary>The tier sign on a gated pill, its plate, and the room it keeps from the label.</summary>
        public const double BadgeMaxWidth = 50;
        public const double BadgePlateWidth = 52;
        public const double BadgePlateHeight = 24;
        public const double BadgeGap = 8;
        /// <summary>Extra right padding on a pill that carries a tier sign.</summary>
        public const double BadgePadExtra = 4;
        /// <summary>Hue step between neighbouring pills on one bar, in degrees.</summary>
        public const double TabHueStep = 9;
        /// <summary>The leading glyph: 16 px in Segoe MDL2 Assets, wearing the label's colour.
        /// Linux has no Segoe MDL2: the head maps these code points to its own icon font.</summary>
        public const double GlyphSize = 16;
        public const string GlyphFont = "Segoe MDL2 Assets";

        /// <summary>One leading glyph per tab key (Segoe MDL2 Assets code points).</summary>
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
            ["personality"] = '\uE76E',      // a face
            ["permissions"] = '\uEA18',      // a shield
            ["companionlinks"] = '\uE71B',   // a link
            ["companionai"] = '\uE701',      // a signal
            ["bambitakeover"] = '\uE7AD',    // a swirl
            ["shelistening"] = '\uE720',     // a microphone
            ["awareness"] = '\uEA80',        // a light bulb
            // Play
            ["play"] = '\uE7FC',             // Games: a controller
            ["playeyes"] = '\uE7B3',         // an eye
            ["playsessions"] = '\uE768',     // play
            ["deeper"] = '\uE81E',           // layers
            // Social
            ["availablesubjects"] = '\uE7EE', // Lobby
            ["friends"] = '\uE716',          // two people
            ["leaderboard"] = '\uE9F9',      // a bar chart
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
            ["phrases"] = '\uE8D2',          // letters
            ["medialog"] = '\uE8FD',         // a list
        };

        /// <summary>The glyph a tab key wears, or null (no glyph).</summary>
        public static string? Glyph(string? key) =>
            key != null && Glyphs.TryGetValue(key.ToLowerInvariant(), out var c) ? c.ToString() : null;

        /// <summary>The "Moved" note's text: the hue at 90%.</summary>
        public const double NoteTextAlpha = 0.90;

        /// <summary>The hue at an alpha (0..1).</summary>
        public static uint WithAlpha(uint c, double alpha) => Argb.WithAlpha(c, alpha);

        /// <summary>WCAG relative luminance of an opaque colour.</summary>
        public static double Luminance(uint c)
        {
            static double Lin(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(Argb.R(c)) + 0.7152 * Lin(Argb.G(c)) + 0.0722 * Lin(Argb.B(c));
        }

        /// <summary>WCAG contrast ratio between two opaque colours (1..21).</summary>
        public static double Contrast(uint a, uint b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>The active pill's text on its solid hue fill: dark ink or white, whichever reads better.</summary>
        public static uint ActiveTextOn(uint fill) =>
            Contrast(DarkInk, fill) >= Contrast(Argb.White, fill) ? DarkInk : Argb.White;

        /// <summary>The app's dark page ground (the strip sits on it).</summary>
        public static readonly uint PageGround = Argb.FromRgb(0x1A, 0x12, 0x30);

        /// <summary>Paints <paramref name="top"/> (with its alpha) over an opaque <paramref name="under"/>.</summary>
        public static uint Over(uint top, uint under)
        {
            double a = Argb.A(top) / 255.0;
            byte M(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
            return Argb.FromRgb(M(Argb.R(top), Argb.R(under)), M(Argb.G(top), Argb.G(under)), M(Argb.B(top), Argb.B(under)));
        }

        /// <summary>Porter-Duff "over" for two colours that may both be translucent.</summary>
        public static uint Composite(uint top, uint under)
        {
            double at = Argb.A(top) / 255.0, au = Argb.A(under) / 255.0;
            double a = at + au * (1 - at);
            if (a <= 0) return Argb.FromArgb(0, 0, 0, 0);
            byte M(byte t, byte u) => (byte)Math.Round((t * at + u * au * (1 - at)) / a);
            return Argb.FromArgb((byte)Math.Round(a * 255),
                M(Argb.R(top), Argb.R(under)), M(Argb.G(top), Argb.G(under)), M(Argb.B(top), Argb.B(under)));
        }

        /// <summary>A straight mix of two colours (t = 0 gives a, 1 gives b), opaque.</summary>
        public static uint Mix(uint a, uint b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            byte M(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Argb.FromRgb(M(Argb.R(a), Argb.R(b)), M(Argb.G(a), Argb.G(b)), M(Argb.B(a), Argb.B(b)));
        }

        /// <summary>The tray's fill: the hue at 10% over deep ink at 40% (translucent).</summary>
        public static uint TrackFill(uint hue) =>
            Composite(WithAlpha(hue, TrackFillAlpha), WithAlpha(DarkInk, TrackInkAlpha));

        // ---- HSL ---------------------------------------------------------------------------------

        /// <summary>Hue (0..360), saturation and lightness (0..1) of a colour.</summary>
        public static (double H, double S, double L) ToHsl(uint c)
        {
            double r = Argb.R(c) / 255.0, g = Argb.G(c) / 255.0, b = Argb.B(c) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2, d = max - min;
            if (d < 1e-9) return (0, 0, l);
            double sat = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            double h = max == r ? (g - b) / d + (g < b ? 6 : 0)
                     : max == g ? (b - r) / d + 2
                     : (r - g) / d + 4;
            return (h * 60, sat, l);
        }

        /// <summary>An opaque colour from hue (degrees, any range), saturation and lightness.</summary>
        public static uint FromHsl(double h, double sat, double l)
        {
            h = ((h % 360) + 360) % 360 / 360.0;
            if (sat <= 0) { var v = (byte)Math.Round(l * 255); return Argb.FromRgb(v, v, v); }
            double q = l < 0.5 ? l * (1 + sat) : l + sat - l * sat, p = 2 * l - q;
            static double Ch(double p, double q, double t)
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1 / 6.0) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
                return p;
            }
            byte B(double x) => (byte)Math.Round(Math.Clamp(x, 0, 1) * 255);
            return Argb.FromRgb(B(Ch(p, q, h + 1 / 3.0)), B(Ch(p, q, h)), B(Ch(p, q, h - 1 / 3.0)));
        }

        /// <summary>The colour turned round the hue wheel, saturation and lightness kept.</summary>
        public static uint RotateHue(uint c, double degrees)
        {
            var (h, sat, l) = ToHsl(c);
            return FromHsl(h + degrees, sat, l);
        }

        /// <summary>Shortest distance between two hues, in degrees (0..180).</summary>
        public static double HueDistance(double a, double b)
        {
            var d = Math.Abs(a - b) % 360;
            return d > 180 ? 360 - d : d;
        }

        /// <summary>A tab's own tint: the section hue turned by (index - middle) x 9 degrees.</summary>
        public static uint TabTint(string? section, string? key, int index, int count)
        {
            var hue = Accent(section);
            if (count <= 1) return hue;
            return RotateHue(hue, (index - (count - 1) / 2.0) * TabHueStep);
        }

        /// <summary>The worst ground a rest pill's text sits on, in the hue.</summary>
        public static uint RestGround(uint hue) => RestGround(hue, hue);

        /// <summary>The worst ground on a pill wearing <paramref name="tint"/>: page, wash (14%) and
        /// tray in the section hue, the plate's lit top in the tint.</summary>
        public static uint RestGround(uint hue, uint tint) =>
            Over(WithAlpha(tint, RestFillAlpha + PlateLift),
                 Over(TrackFill(hue), Over(WithAlpha(hue, 0.14), PageGround)));

        /// <summary>A rest pill's text: the hue at 95%, lifted toward white until 4.5:1 on its ground.</summary>
        public static uint RestTextOn(uint hue) => RestTextOn(hue, hue);

        /// <summary>The rest label on a tinted pill: the section hue, lifted until it reads at 4.5:1.</summary>
        public static uint RestTextOn(uint hue, uint tint) => Lift(hue, RestGround(hue, tint));

        /// <summary>The rest glyph: the tab's own tint, lifted until it reads at 4.5:1 on its ground.</summary>
        public static uint RestGlyphOn(uint hue, uint tint) => Lift(tint, RestGround(hue, tint));

        /// <summary>The active glyph: the label's ink mixed toward the tab's tint, never under 3:1.</summary>
        public static uint ActiveGlyphOn(uint hue, uint tint)
        {
            var ink = ActiveTextOn(hue);
            for (double t = ActiveGlyphTint; t > 0; t -= 0.05)
            {
                var c = Mix(ink, tint, t);
                if (Contrast(c, hue) >= 3.0) return c;
            }
            return ink;
        }

        /// <summary>The active pill's inner ring colour at an alpha: near-white in the tint.</summary>
        public static uint ActiveRingColor(uint tint, double alpha) => WithAlpha(Mix(tint, Argb.White, ActiveRingWhite), alpha);

        private static uint Lift(uint start, uint ground)
        {
            var c = start;
            for (int i = 0; i < 20 && Contrast(Over(WithAlpha(c, RestTextAlpha), ground), ground) < 4.5; i++)
                c = Argb.FromRgb((byte)Math.Round(Argb.R(c) + (255 - Argb.R(c)) * 0.08),
                                 (byte)Math.Round(Argb.G(c) + (255 - Argb.G(c)) * 0.08),
                                 (byte)Math.Round(Argb.B(c) + (255 - Argb.B(c)) * 0.08));
            return WithAlpha(c, RestTextAlpha);
        }

        // ---- brushes as stops (vertical: StartPoint 0,0, EndPoint 0,1) ---------------------------

        /// <summary>A raised plate in the tint: lit across the top, shaded at the foot, centred on <paramref name="alpha"/>.</summary>
        public static (uint Color, double Offset)[] PlateStops(uint tint, double alpha) => new[]
        {
            (WithAlpha(tint, alpha + PlateLift), 0.0),
            (WithAlpha(tint, alpha - PlateLift), 1.0),
        };

        /// <summary>The rest outline: a lighter top edge, the tint in the middle, a darker foot.</summary>
        public static (uint Color, double Offset)[] OutlineStops(uint tint) => new[]
        {
            (WithAlpha(Mix(tint, Argb.White, BevelLight), RestOutlineAlpha), 0.0),
            (WithAlpha(tint, RestOutlineAlpha), 0.35),
            (WithAlpha(tint, RestOutlineAlpha), 0.65),
            (WithAlpha(Mix(tint, DarkInk, BevelShade), RestOutlineAlpha), OutlineFootOffset),
        };

        /// <summary>The active pill's inner ring: a near-white gloss, strong at the top.</summary>
        public static (uint Color, double Offset)[] ActiveRingStops(uint tint) => new[]
        {
            (ActiveRingColor(tint, ActiveRingTopAlpha), 0.0),
            (ActiveRingColor(tint, ActiveRingFootAlpha), 1.0),
        };

        /// <summary>The tray's border: shaded along the top (an inset), the hue at 40% below.</summary>
        public static (uint Color, double Offset)[] TrackBorderStops(uint hue) => new[]
        {
            (WithAlpha(DarkInk, TrackInsetAlpha), 0.0),
            (WithAlpha(hue, TrackBorderAlpha), 0.45),
            (WithAlpha(hue, TrackBorderAlpha), 1.0),
        };
    }
}
