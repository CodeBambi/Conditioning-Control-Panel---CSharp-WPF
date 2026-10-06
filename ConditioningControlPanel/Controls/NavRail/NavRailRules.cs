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
        /// <summary>The idle ring (about 45%), its hover (about 80%), the active ring (solid).</summary>
        // Idle ring 72%: at 45% the door art's own pink rims drowned the hue (desk shot
        // chrome-rail-2x.png, polish wave 2), and the owner asked for the hue AROUND the icons.
        internal const byte RingIdleAlpha = 0xB8, RingHoverAlpha = 0xE6, RingActiveAlpha = 0xFF;

        /// <summary>The ring alpha for a row: solid when active, brighter on hover, else 45%.</summary>
        internal static byte RingAlpha(bool active, bool hover) =>
            active ? RingActiveAlpha : hover ? RingHoverAlpha : RingIdleAlpha;

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
