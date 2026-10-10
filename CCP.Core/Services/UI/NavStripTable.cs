using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The pure half of the section page header (nav rework 2026-10-06): which pills a section
    /// draws, which pill a tab lights, the section hue, keyboard wrap and the last-tab memory.
    /// Moved out of WPF's NavStripRules (Controls/NavRail/SectionTabStrip.xaml.cs) so both heads'
    /// strips read one truth; NavStripRules delegates here.
    /// </summary>
    public static class NavStripTable
    {
        /// <summary>Home is the dashboard (no strip); Settings keeps its own left pill column.</summary>
        public static bool ShowsPills(string? section) =>
            section != null && section != NavSections.Home && section != NavSections.Settings;

        /// <summary>The header (breadcrumb row) shows everywhere but Home.</summary>
        public static bool ShowsHeader(string? section) =>
            section != null && section != NavSections.Home;

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

        /// <summary>Keyboard move inside the strip, by key NAME (both heads' Key enums print
        /// "Left"/"Right"/"Home"/"End"): Left/Right wrap, Home/End jump. -1 = not a strip key.</summary>
        public static int MoveIndex(int current, int count, string key)
        {
            if (count <= 0) return -1;
            return key switch
            {
                "Left" => current <= 0 ? count - 1 : current - 1,
                "Right" => current < 0 || current >= count - 1 ? 0 : current + 1,
                "Home" => 0,
                "End" => count - 1,
                _ => -1,
            };
        }

        // Section hues (polish wave 2): one hue per section, 0xRRGGBB. The gear shares Home's lilac.
        public const uint Lilac = 0xB79CFF, Pink = 0xFF69B4, Orchid = 0xE070FF, VioletBlue = 0x7A86FF,
                          Sky = 0x5FB0FF, Coral = 0xFF9A6B, Sage = 0xA8D8A0;

        public static uint AccentRgb(string? section) => section switch
        {
            NavSections.Studio => Pink,
            NavSections.Companion => Orchid,
            NavSections.Play => VioletBlue,
            NavSections.Social => Sky,
            NavSections.You => Coral,
            NavSections.Library => Sage,
            _ => Lilac,   // Home, Settings
        };

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
    }
}
