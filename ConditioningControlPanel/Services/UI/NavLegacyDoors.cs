using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// Nav rework BASE bridge (2026-10-06): the old rail's doors, DERIVED from
    /// <see cref="NavSections"/> so the table is the one truth, but projected back onto the six
    /// doors + Settings the current rail still draws. Same door keys, same default tabs and the
    /// same tab membership as the hand-written NavDoorMap it replaced, so nothing moves for the
    /// user yet. MainWindow.TabNavigation.cs's NavDoorMap reads <see cref="Build"/>; the RAIL lane
    /// deletes this file when the labelled rail lands.
    ///
    /// <para>Projection rules: the "social" section has no door on the old rail, so it folds into
    /// "play" (Lobby and Remote sat there); the Settings gear is the "appsettings" door with that
    /// one key; only keys the old rail actually owned are listed (<see cref="LegacyRailTabs"/>), so
    /// new tab keys the other lanes have not wired yet claim no row; <see cref="Overrides"/> keeps
    /// the two keys whose old door differs from their new section.</para>
    /// </summary>
    internal static class NavLegacyDoors
    {
        /// <summary>Every key the old rail owned. "lab" is deliberately absent: it is a legacy
        /// alias resolved by CanonicalTabKey, not a row. "chaster" is absent too: Circe's tab
        /// lives in the rail foot as a chip, not a door row. "justdrop" is a window key filed with
        /// Studio so its row resolves; ShowTab intercepts it before any door would expand.</summary>
        internal static readonly string[] LegacyRailTabs =
        {
            "settings", "progression",
            "studio", "presets", "haptics", "justdrop",
            "companion", "bambitakeover", "shelistening", "awareness",
            "play", "deeper", "exclusives", "gradedintake", "lockdown", "blinktrainer",
            "remotecontrol", "availablesubjects",
            "discord", "spiral", "quests", "achievements", "enhancements", "programs", "leaderboard",
            "assets",
            "appsettings",
        };

        /// <summary>Old-rail keys whose old door is not their new section's door.</summary>
        internal static readonly IReadOnlyDictionary<string, string> Overrides =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["leaderboard"] = "you",   // new home: Social > Leaderboard; the row still sits in You
                ["exclusives"] = "play",   // new home: Settings > Account & Plans; the row still sits in Play
            };

        /// <summary>The old rail door a section maps onto.</summary>
        internal static string DoorOf(string section) => section switch
        {
            NavSections.Social => "play",
            NavSections.Settings => "appsettings",
            _ => section,
        };

        /// <summary>(door, default tab, tabs) in rail order, top to bottom, Settings last.</summary>
        internal static (string Door, string DefaultTab, string[] Tabs)[] Build()
        {
            var doors = new List<(string Door, string DefaultTab, List<string> Tabs)>();
            foreach (var s in NavSections.Order)
            {
                var door = DoorOf(s.Key);
                if (doors.Any(d => d.Door == door)) continue;   // social folds into play
                doors.Add((door, s.DefaultTab, new List<string>()));
            }

            foreach (var key in LegacyRailTabs)
            {
                var door = Overrides.TryGetValue(key, out var o)
                    ? o
                    : DoorOf(NavSections.SectionForTab(key) ?? string.Empty);
                var row = doors.FindIndex(d => d.Door == door);
                if (row < 0) throw new InvalidOperationException($"NavLegacyDoors: no door for '{key}'");
                doors[row].Tabs.Add(key);
            }

            return doors.Select(d => (d.Door, d.DefaultTab, d.Tabs.ToArray())).ToArray();
        }
    }
}
