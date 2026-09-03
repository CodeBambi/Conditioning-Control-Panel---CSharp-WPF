// PORTED from ConditioningControlPanel/MainWindow/MainWindow.TabNavigation.cs (1,174 lines),
// the part of it that is navigation.
//
// What is REAL here: ShowTab (hide every tab panel, show one), the door accordion (one door's
// entry panel open at a time, the door that owns the tab), and every rail/door click handler
// the XAML names, each of which is now one ShowTab call - exactly what it is in WPF.
//
// What is NOT, on purpose, each named so it is not lost silently:
//   - The transition choreography (AnimateTabIn, SwitchTabFx, the Stop*Shimmer/Pulse/Motion
//     calls) and the door open/close height animation. ponytail: panels snap open (Height =
//     NaN) and shut (0); the WPF MeasureDoorPanel + NavDoorExpandMs tween returns with the FX
//     partials.
//   - Per-tab side effects on the way in (RefreshPresetsList, spending HasSeenProgramsTab and
//     the first-run explainer, StopPolling on leaving Available Subjects, SelectRack for the
//     studio rack, ShowSettingsSection for "progression"). All reach App.* or a service.
//   - Bark (App.Bark.NotifyTabNavigated) and EmiDesk (EmiTargets.NoteTabOpened) hooks.
//   - The three keys that are WINDOWS, not tabs, and the one launcher door: "patreon" (opens
//     Settings · Account via ShowAppInfoPopup), "fyp" (OpenFypFeed), "justdrop" (the shop host)
//     and the "webapp" door. Each is a documented no-op below until its service exists here.
//   - BtnNavMediaLog: WPF raises AssetsTab.BtnMediaLog's Click. That button is inside the
//     ported AssetsTabView and its handler is a stub, so this lands on the Assets tab instead.
//   - An "active" state on the rail. NavDoorButton has no :checked/.active selector on this
//     head, so nothing is highlighted yet.
//
// Every panel and door is resolved by x:Name through FindControl, never by generated field, so
// a panel this head does not carry is skipped rather than a compile error.

using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The tab key currently shown, lower-case. "settings" until the first switch,
        /// which is the panel the XAML leaves visible.</summary>
        internal string CurrentTab { get; private set; } = "settings";

        /// <summary>Tab key -> the x:Name of the panel it shows. Aliases point at one panel:
        /// "lab" is the old name for the Play card wall, "haptics" is a module inside Studio,
        /// "progression" is a section of Settings. Same table as the WPF switch.</summary>
        private static readonly Dictionary<string, string> TabPanels = new(StringComparer.Ordinal)
        {
            ["settings"] = "SettingsTab",        ["progression"] = "SettingsTab",
            ["presets"] = "PresetsTab",          ["quests"] = "QuestsTab",
            ["programs"] = "ProgramsTab",        ["enhancements"] = "EnhancementsTab",
            ["deeper"] = "DeeperTab",            ["achievements"] = "AchievementsTab",
            ["companion"] = "CompanionTab",      ["play"] = "PlayTab",  ["lab"] = "PlayTab",
            ["leaderboard"] = "LeaderboardTab",  ["assets"] = "AssetsTab",
            ["discord"] = "DiscordTab",          ["awareness"] = "AwarenessTab",
            ["remotecontrol"] = "RemoteControlTab", ["availablesubjects"] = "AvailableSubjectsTab",
            ["bambitakeover"] = "BambiTakeoverTab", ["studio"] = "StudioTab", ["haptics"] = "StudioTab",
            ["lockdown"] = "LockdownTab",        ["blinktrainer"] = "BlinkTrainerTab",
            ["shelistening"] = "SheListeningTab", ["gradedintake"] = "GradedIntakeTab",
            ["appsettings"] = "AppSettingsTab",  ["spiral"] = "SpiralTab",
            ["exclusives"] = "ExclusivesTab",
        };

        /// <summary>Keys that open a window or a service rather than a tab. ShowTab leaves the
        /// current tab alone for them, as WPF does; the launch itself is not on this head yet.</summary>
        private static readonly HashSet<string> WindowKeys = new(StringComparer.Ordinal)
            { "patreon", "fyp", "justdrop", "webapp" };

        /// <summary>The rail's doors: Tag on the door button, the tab it opens, the tabs it owns,
        /// and the entry panel that unfolds under it (null for the two doors that have none).
        /// Copied from WPF's NavDoorMap.</summary>
        private static readonly (string Door, string DefaultTab, string[] Tabs, string? Panel)[] NavDoorMap =
        {
            ("home",        "settings",    new[] { "settings", "progression" },                                   null),
            ("studio",      "studio",      new[] { "studio", "presets", "haptics" },                              "DoorPanelStudio"),
            ("companion",   "companion",   new[] { "companion", "bambitakeover", "shelistening", "awareness" },   "DoorPanelCompanion"),
            ("play",        "play",        new[] { "play", "lab", "deeper", "exclusives", "gradedintake", "lockdown", "blinktrainer", "remotecontrol", "availablesubjects" }, "DoorPanelPlay"),
            ("you",         "discord",     new[] { "discord", "spiral", "quests", "achievements", "enhancements", "programs", "leaderboard" }, "DoorPanelYou"),
            ("library",     "assets",      new[] { "assets" },                                                    "DoorPanelLibrary"),
            ("appsettings", "appsettings", new[] { "appsettings" },                                               null),
        };

        /// <summary>
        /// Shows one tab and hides the rest, and unfolds the door that owns it. Case-insensitive
        /// at the door for the same reason WPF is: a deep link or a mod that says "Settings" must
        /// not land on a blank page. An unknown key is a no-op that keeps the current tab, never a
        /// page with nothing on it.
        /// </summary>
        internal void ShowTab(string? tab)
        {
            tab = (tab ?? string.Empty).ToLowerInvariant();
            if (WindowKeys.Contains(tab)) return;                 // a window, not a tab - see header
            if (!TabPanels.TryGetValue(tab, out var target)) return;

            foreach (var name in new HashSet<string>(TabPanels.Values))
            {
                var panel = this.FindControl<Control>(name);
                if (panel is not null) panel.IsVisible = name == target;
            }
            CurrentTab = tab;
            SetExpandedDoor(NavDoorForTab(tab));
        }

        private static string? NavDoorForTab(string tab)
        {
            foreach (var d in NavDoorMap)
                if (Array.IndexOf(d.Tabs, tab) >= 0) return d.Door;
            return null;
        }

        /// <summary>One door open at a time. ponytail: snaps, no height tween.</summary>
        private void SetExpandedDoor(string? door)
        {
            foreach (var d in NavDoorMap)
            {
                if (d.Panel is null) continue;
                var panel = this.FindControl<Control>(d.Panel);
                if (panel is not null) panel.Height = d.Door == door ? double.NaN : 0;
            }
        }

        private void NavDoor_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string door) return;
            foreach (var d in NavDoorMap)
                if (string.Equals(d.Door, door, StringComparison.Ordinal)) { ShowTab(d.DefaultTab); return; }
            // "webapp" and any unmapped door: a launcher, not a tab. No-op here, as in WPF.
        }

        private void BtnSettings_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("settings");
        private void BtnPresets_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("presets");
        private void BtnQuests_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("quests");
        private void BtnPrograms_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("programs");
        private void BtnEnhancements_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("enhancements");
        private void BtnNavStudio_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("studio");
        private void BtnNavHaptics_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("haptics");
        private void BtnNavPlay_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("play");
        private void BtnNavBambiTakeover_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("bambitakeover");
        private void BtnNavSheListening_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("shelistening");
        private void BtnNavGradedIntake_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("gradedintake");
        private void BtnNavLockdown_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("lockdown");
        private void BtnNavBlinkTrainer_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("blinktrainer");
        private void BtnNavRemoteControl_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("remotecontrol");

        // Still a stub: the web-app door launches a browser, which is a service on this head.
        private void DoorWebApp_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }
        private void BtnNavMediaLog_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => ShowTab("assets");
    }
}
