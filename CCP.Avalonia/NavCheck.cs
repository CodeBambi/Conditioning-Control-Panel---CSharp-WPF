using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// The smallest thing that fails if shell navigation breaks. Constructs the shell headlessly,
    /// calls ShowTab the way every rail handler does, and asserts what the user would see.
    /// </summary>
    internal static class NavCheck
    {
        public static int Run()
        {
            RenderProof.EnsureSetUp();
            var w = new MainShellWindow();
            bool Vis(string n) => w.FindControl<Control>(n)?.IsVisible == true;
            double H(string n) => w.FindControl<Control>(n)?.Height ?? -1;

            var fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; Console.Error.WriteLine("FAIL " + what); } }

            Check(Vis("SettingsTab") && !Vis("QuestsTab"), "startup shows Settings only");

            w.ShowTab("quests");
            Check(Vis("QuestsTab") && !Vis("SettingsTab"), "quests shows QuestsTab and hides Settings");
            Check(double.IsNaN(H("DoorPanelYou")) && H("DoorPanelStudio") == 0, "quests unfolds the You door only");

            w.ShowTab("Haptics");                       // alias + case-insensitive
            Check(Vis("StudioTab") && !Vis("QuestsTab"), "haptics lands on StudioTab");
            Check(double.IsNaN(H("DoorPanelStudio")) && H("DoorPanelYou") == 0, "haptics unfolds the Studio door only");

            w.ShowTab("no-such-tab");
            Check(Vis("StudioTab"), "unknown key keeps the current tab, never a blank page");

            w.ShowTab("fyp");
            Check(Vis("StudioTab") && w.CurrentTab == "haptics", "a window key leaves the tab alone");

            var shown = 0;
            foreach (var n in new[] { "SettingsTab","PresetsTab","QuestsTab","ProgramsTab","EnhancementsTab","DeeperTab","AchievementsTab","CompanionTab","PlayTab","LeaderboardTab","AssetsTab","DiscordTab","AwarenessTab","RemoteControlTab","AvailableSubjectsTab","BambiTakeoverTab","StudioTab","LockdownTab","BlinkTrainerTab","SheListeningTab","GradedIntakeTab","AppSettingsTab","SpiralTab","ExclusivesTab" })
                if (Vis(n)) shown++;
            Check(shown == 1, $"exactly one tab visible (saw {shown})");

            Console.WriteLine(fails == 0 ? "nav-check: shell navigation holds." : $"nav-check: {fails} failure(s).");
            return fails == 0 ? 0 : 1;
        }
    }
}
