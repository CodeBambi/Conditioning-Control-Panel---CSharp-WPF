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
            // Shown, not merely constructed: a TopLevel that was never opened has no popup host,
            // so ToolTip.SetIsOpen throws from inside its own property-changed handler and the
            // tooltip sweep below could not be probed at all. Show() is what RenderProof does too.
            w.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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

            // ---- the entry points every "Configure in Settings" button calls -------------------
            // Each is ShowTab + a focus call on the landed tab, so the assertion is both halves:
            // the right panel visible AND the right thing selected inside it.

            // The lit mini-rail pill is the section the user sees revealed; the scroll itself needs
            // a measured layout, which a headless construct does not have.
            string Section() =>
                w.AppSettingsPage!.FindControl<RadioButton>("SectionPillGeneral")?.IsChecked == true ? "general"
                : w.AppSettingsPage!.FindControl<RadioButton>("SectionPillDevices")?.IsChecked == true ? "devices"
                : w.AppSettingsPage!.FindControl<RadioButton>("SectionPillData")?.IsChecked == true ? "data"
                : "?";

            w.OpenAppSettingsSection("data");
            Check(Vis("AppSettingsTab") && !Vis("StudioTab"), "OpenAppSettingsSection lands on the Settings door");
            Check(Section() == "data", $"OpenAppSettingsSection('data') reveals Data (saw {Section()})");

            w.OpenDeviceSettings();
            Check(Vis("AppSettingsTab"), "OpenDeviceSettings lands on the Settings door");
            Check(Section() == "devices", $"OpenDeviceSettings reveals Devices (saw {Section()})");

            w.OpenAppSettingsSection("no-such-section");
            Check(Vis("AppSettingsTab") && Section() == "devices",
                  "an unknown section still opens the door and changes nothing");

            w.OpenStudioModule("flash");
            Check(Vis("StudioTab") && !Vis("AppSettingsTab"), "OpenStudioModule lands on Studio");
            Check(w.StudioRack!.SelectedRackKey == "flash",
                  $"OpenStudioModule('flash') selects the Flash module (saw {w.StudioRack!.SelectedRackKey})");

            // Haptics is the one rack key that must route as a TAB, not as a rack row - the bark
            // and the first-visit card hang off the tab key. Same landing, different announcement.
            w.OpenStudioModule("haptics");
            Check(Vis("StudioTab") && w.CurrentTab == "haptics", "OpenStudioModule('haptics') routes through ShowTab");
            Check(w.StudioRack!.SelectedRackKey == "haptics",
                  $"the haptics route still selects the Haptics module (saw {w.StudioRack!.SelectedRackKey})");

            // The ? panel. Its rows still cannot START a tour (App.Tutorial is not on this head),
            // but the panel itself now opens and closes - which is what every one of those rows
            // did first, and what none of them did while this file's handlers were empty.
            w.SetTutorialOverlay(true);
            Check(Vis("MainTutorialOverlay"), "the ? panel opens");
            w.SetTutorialOverlay(false);
            Check(!Vis("MainTutorialOverlay"), "the ? panel closes");

            // ---- the shell members that WERE restored but had no caller ------------------------
            // Each assertion drives the CALL SITE, not the member, and checks something the user
            // would see - a control found, a pill's state written, a footer with a number in it.
            // Break the one line each adds and the corresponding check fails.

            // 1. ShowTab closes a stale tooltip (MainShellWindow.ToolTipHygiene.cs). The window
            //    itself stands in for the owner: FindOpenToolTipOwner descends the pointer-over
            //    chain and tests the ROOT first, and headless there is no pointer to be over
            //    anything, so the root is the only reachable owner. It needs a Tip - Avalonia's
            //    IsOpenChanged puts IsOpen straight back to false on a control that has none, so
            //    a tipless probe would pass whether or not ShowTab swept anything.
            ToolTip.SetTip(w, "nav-check probe");
            ToolTip.SetIsOpen(w, true);
            Check(ToolTip.GetIsOpen(w), "the tooltip probe is actually open before the sweep");
            w.ShowTab("presets");
            Check(!ToolTip.GetIsOpen(w), "ShowTab closes a tooltip that was still open");
            ToolTip.SetTip(w, null);

            // 2. The rail's one-time setup paints the premium pills
            //    (MainShellWindow.NavPremiumTags.RefreshNavPremiumTags, called by
            //    MainShellWindow.NavRail.InitializeNavRail). Forced ON first, so a no-op wiring
            //    leaves it on and fails; the answer for every key is "not locked" on a head with no
            //    entitlement service, which is WPF's own documented fallback.
            var pillNames = new[]
            {
                "TagPremiumHaptics", "TagPremiumTakeover", "TagPremiumSheListening",
                "TagPremiumAwareness", "TagPremiumGradedIntake", "TagPremiumLockdown",
                "TagPremiumBlinkTrainer", "TagPremiumRemoteControl",
            };
            var pillsFound = 0;
            foreach (var n in pillNames)
                if (w.FindControl<Border>(n) is { } pill) { pillsFound++; pill.IsVisible = true; }
            Check(pillsFound == pillNames.Length,
                  $"every rail premium pill resolves by name (saw {pillsFound} of {pillNames.Length})");

            w.InitializeNavRail();
            var pillsLit = 0;
            foreach (var n in pillNames)
                if (w.FindControl<Border>(n)?.IsVisible == true) pillsLit++;
            Check(pillsLit == 0, $"the rail setup repaints every pill from the roster (saw {pillsLit} still lit)");

            // 3. Landing on the Profile tab repaints the sharing footer
            //    (MainShellWindow.ProfileCard.UpdateProfileSharingSummary, called from OnTabShown).
            //    The string is "{0} on · {1} private", so the separator proves it was FORMATTED -
            //    a raw key or an unresolved lookup carries no interpunct.
            w.ShowTab("discord");
            var sharing = w.ProfilePage?.FindControl<TextBlock>("TxtProfileSharingSummary")?.Text;
            Check(!string.IsNullOrEmpty(sharing) && sharing!.Contains('\u00b7'),
                  $"the Profile tab repaints its sharing footer (saw \"{sharing}\")");

            // 4. DiscordTabView's ctor calls InitializeComponent, not AvaloniaXamlLoader.Load, so
            //    its generated x:Name fields are ASSIGNED. Under the loader every one of them was
            //    permanently null - which compiles, renders and reviews clean. Reading the field and
            //    demanding it be the same object FindControl returns is the only thing that tells
            //    the two ctors apart, and it fails the moment anyone puts the loader back.
            var page = w.ProfilePage;
            Check(page is not null && ReferenceEquals(page.TxtProfileSharingSummary,
                                                     page.FindControl<TextBlock>("TxtProfileSharingSummary")),
                  "DiscordTabView's generated x:Name fields are assigned (InitializeComponent, not the loader)");

            w.ShowTab("settings");

            var shown = 0;
            foreach (var n in new[] { "SettingsTab","PresetsTab","QuestsTab","ProgramsTab","EnhancementsTab","DeeperTab","AchievementsTab","CompanionTab","PlayTab","LeaderboardTab","AssetsTab","DiscordTab","AwarenessTab","RemoteControlTab","AvailableSubjectsTab","BambiTakeoverTab","StudioTab","LockdownTab","BlinkTrainerTab","SheListeningTab","GradedIntakeTab","AppSettingsTab","SpiralTab","ExclusivesTab" })
                if (Vis(n)) shown++;
            Check(shown == 1, $"exactly one tab visible (saw {shown})");

            Console.WriteLine(fails == 0 ? "nav-check: shell navigation holds." : $"nav-check: {fails} failure(s).");
            return fails == 0 ? 0 : 1;
        }
    }
}
