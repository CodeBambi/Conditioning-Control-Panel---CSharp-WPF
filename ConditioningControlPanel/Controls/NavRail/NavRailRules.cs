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

        /// <summary>The hue wash over the medallion art (Tag "navtint"): about 18% lit, 8% idle.
        /// WPF has no multiply blend without a shader, so the icon leans toward the hue by a wash.</summary>
        internal const byte ArtTintActiveAlpha = 0x2E, ArtTintIdleAlpha = 0x14;

        /// <summary>The spur that bridges the window edge to the lit medallion: 20 x 8 px, its top
        /// at 25 px so its centre sits on the tile centre (ContentPresenter top 1 + 56 / 2 = 29).
        /// The gradient runs from the hue at 90% at the window edge to 60% against the ring.</summary>
        internal const double SpurWidth = 20, SpurHeight = 8, SpurTop = 25;
        internal const byte SpurEdgeAlpha = 0xE6, SpurRingAlpha = 0x99;

        /// <summary>The ring alpha for a row: solid when active, brighter on hover, else 80%.</summary>
        internal static byte RingAlpha(bool active, bool hover) =>
            active ? RingActiveAlpha : hover ? RingHoverAlpha : RingIdleAlpha;

        /// <summary>The ring colour: the plain hue at <see cref="RingAlpha"/> idle and on hover,
        /// the hue lifted 25% toward white, solid, when active.</summary>
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
    }
}
