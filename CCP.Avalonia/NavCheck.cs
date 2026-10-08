using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Nav;

namespace ConditioningControlPanel.Avalonia
{
    /// <summary>
    /// The smallest thing that fails if shell navigation breaks. Constructs the shell headlessly,
    /// calls ShowTab the way the rail, the strip and every deep link do, and asserts what the user
    /// would see. Nav rework (WPF 7.1.5): the section rail, the on-page strip, the registry and the
    /// old keys that must keep resolving.
    ///
    /// <para>Set CCP_NAV_SHOTS to a directory to also save d1-home.png, d1-studio.png and
    /// d1-social.png of the shell (the lane's visual proof).</para>
    /// </summary>
    internal static class NavCheck
    {
        public static int Run()
        {
            RenderProof.EnsureSetUp();
            var w = new MainShellWindow();
            // Shown, not merely constructed: a TopLevel that was never opened has no popup host,
            // so the tooltip sweep below could not be probed at all.
            w.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            bool Vis(string n) => w.FindControl<Control>(n)?.IsVisible == true;
            var strip = w.NavStrip!;

            var fails = 0;
            void Check(bool ok, string what) { if (!ok) { fails++; Console.Error.WriteLine("FAIL " + what); } }

            Check(Vis("SettingsTab") && !Vis("QuestsTab"), "startup shows Settings only");

            // ---- the rail ----------------------------------------------------------------
            var expected = NavRailRules.RailSections.Select(s => s.Key).Append(NavSections.Settings).ToArray();
            Check(w.NavRailSectionOrder.SequenceEqual(expected),
                  $"the rail paints the seven sections + the gear in table order (saw {string.Join(",", w.NavRailSectionOrder)})");
            Check(w.NavRailHooked, "the rail's setup ran");
            Check(w.LitNavSection == NavSections.Home, $"startup lights Home (saw {w.LitNavSection})");
            Check(Math.Abs((w.FindControl<Control>("NavSidebar")?.Bounds.Width ?? 0) - 96) < 0.5,
                  $"the rail fills the 96px column (saw {w.FindControl<Control>("NavSidebar")?.Bounds.Width})");
            Check(!strip.IsVisible, "Home draws no strip");
            Check(Math.Abs(w.Width - WindowFitRule.DefaultWidthDip) < 0.5 && Math.Abs(w.Height - WindowFitRule.DefaultHeightDip) < 0.5,
                  $"the window opens at WindowFitRule's default size (saw {w.Width}x{w.Height})");

            w.ShowTab("quests");
            Check(Vis("QuestsTab") && !Vis("SettingsTab"), "quests shows QuestsTab and hides Settings");
            Check(w.LitNavSection == NavSections.You && w.ExpandedDoor == "you", "quests lights the You row");
            Check(strip.IsVisible && strip.Section == NavSections.You && strip.ActivePillKey == "quests",
                  $"the You strip shows with Quests lit (saw {strip.Section}/{strip.ActivePillKey})");
            Check(strip.PillKeys.SequenceEqual(NavStripRules.Pills(NavSections.You).Select(p => p.Key)),
                  "the strip draws the section's pills in table order, hidden tabs skipped");

            w.ShowTab("Haptics");                       // module alias + case-insensitive
            Check(Vis("StudioTab") && !Vis("QuestsTab"), "haptics lands on StudioTab");
            Check(w.LitNavSection == NavSections.Studio && strip.ActivePillKey == "haptics", "haptics lights Studio and its pill");

            w.ShowTab("no-such-tab");
            Check(Vis("StudioTab"), "unknown key keeps the current tab, never a blank page");

            w.ShowTab("fyp");
            Check(Vis("StudioTab") && w.CurrentTab == "haptics", "a window key leaves the tab alone");

            // ---- Ctrl+1..7 and last-tab memory ----------------------------------------------
            Check(w.TryOpenSectionShortcut(3) && w.CurrentTab == "companion" && Vis("CompanionTab"),
                  $"Ctrl+3 opens Companion (saw {w.CurrentTab})");
            Check(!w.TryOpenSectionShortcut(8), "Ctrl+8 has no row");
            w.ShowTab("leaderboard");
            w.ShowTab("settings");
            w.OpenNavSection(NavSections.Social);
            Check(w.CurrentTab == "leaderboard", $"a section row returns to its last tab (saw {w.CurrentTab})");
            w.OpenNavSection(NavSections.Home);
            Check(w.CurrentTab == "settings", "the Home row always opens the dashboard");

            // ---- the strip ------------------------------------------------------------------
            w.ShowTab("availablesubjects");
            strip.ChooseForTests("leaderboard");
            Check(w.CurrentTab == "leaderboard" && strip.ActivePillKey == "leaderboard", "a strip pill navigates and lights");
            Check(strip.PillLockedFor("remotecontrol") && !strip.PillLockedFor("leaderboard"),
                  "a tier-1 pill wears its lock with no account service, a free pill never does");
            Check(strip.CrumbText.Length > 0, "the breadcrumb names the section and page");

            // ---- old keys and zones ---------------------------------------------------------
            w.ShowTab("exclusives");
            Check(w.CurrentTab == "premium" && Vis("ExclusivesTab") && w.LitNavSection == NavSections.Home,
                  $"'exclusives' lands on Home > Premium silently (saw {w.CurrentTab})");
            w.ShowTab("together");
            Check(w.CurrentTab == "availablesubjects" && w.LitNavSection == NavSections.Social,
                  $"'together' lands on Social > Lobby (saw {w.CurrentTab})");
            w.ShowTab("lab");
            Check(Vis("PlayTab") && strip.ActivePillKey == "play", "'lab' is the Play wall, Games lit");
            w.ShowTab("playsessions");
            Check(Vis("PlayTab") && strip.ActivePillKey == "playsessions", "the Sessions zone is the Play wall, its pill lit");
            w.ShowTab("ramp");
            Check(Vis("StudioTab") && w.StudioRack!.SelectedRackKey == "scheduler",
                  $"the Ramp zone selects the rack's scheduler (saw {w.StudioRack!.SelectedRackKey})");
            w.ShowTab("folders");
            Check(Vis("AssetsTab") && w.LitNavSection == NavSections.Library, "Folders is the Assets page in Library");

            // ---- the registry ---------------------------------------------------------------
            var page = new Border { Name = "NavCheckLanePage" };
            w.RegisterNavTab(new MainShellWindow.NavTabHost("leash", () => page));
            w.ShowTab("leash");
            Check(page.IsVisible && !Vis("AssetsTab") && w.LitNavSection == NavSections.Social,
                  "a registered page shows in the page cell and lights its section");
            w.ShowTab("studio");
            Check(!page.IsVisible && Vis("StudioTab"), "leaving a registered page hides it");

            // ---- badges ---------------------------------------------------------------------
            NavBadges.Set(NavSections.Social, 3);
            var b1 = w.NavBadgeFor(NavSections.Social);
            Check(b1.Text == "3" && b1.Opacity == 1.0, $"a fresh count shows bright (saw {b1.Text}@{b1.Opacity})");
            w.ShowTab("availablesubjects");
            var b2 = w.NavBadgeFor(NavSections.Social);
            Check(b2.Text == "3" && b2.Opacity < 1.0, $"visiting the section dims the seen count (saw {b2.Text}@{b2.Opacity})");
            NavBadges.Set(NavSections.Social, 12);
            Check(w.NavBadgeFor(NavSections.Social).Text == "9+", "past nine reads 9+");
            NavBadges.Set(NavSections.Social, 0);
            Check(w.NavBadgeFor(NavSections.Social).Text == "", "a count of 0 clears the badge");

            // ---- the entry points every "Configure in Settings" button calls -------------------
            string Section() =>
                w.AppSettingsPage!.FindControl<RadioButton>("SectionPillGeneral")?.IsChecked == true ? "general"
                : w.AppSettingsPage!.FindControl<RadioButton>("SectionPillDevices")?.IsChecked == true ? "devices"
                : w.AppSettingsPage!.FindControl<RadioButton>("SectionPillData")?.IsChecked == true ? "data"
                : "?";

            w.OpenAppSettingsSection("data");
            Check(Vis("AppSettingsTab") && !Vis("StudioTab"), "OpenAppSettingsSection lands on the Settings door");
            Check(Section() == "data", $"OpenAppSettingsSection('data') reveals Data (saw {Section()})");
            Check(w.LitNavSection == NavSections.Settings && strip.IsVisible, "Settings lights the gear and shows its header");

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

            w.OpenStudioModule("haptics");
            Check(Vis("StudioTab") && w.CurrentTab == "haptics", "OpenStudioModule('haptics') routes through ShowTab");
            Check(w.StudioRack!.SelectedRackKey == "haptics",
                  $"the haptics route still selects the Haptics module (saw {w.StudioRack!.SelectedRackKey})");

            w.SetTutorialOverlay(true);
            Check(Vis("MainTutorialOverlay"), "the ? panel opens");
            w.SetTutorialOverlay(false);
            Check(!Vis("MainTutorialOverlay"), "the ? panel closes");

            // 1. ShowTab closes a stale tooltip (MainShellWindow.ToolTipHygiene.cs).
            ToolTip.SetTip(w, "nav-check probe");
            ToolTip.SetIsOpen(w, true);
            Check(ToolTip.GetIsOpen(w), "the tooltip probe is actually open before the sweep");
            w.ShowTab("presets");
            Check(!ToolTip.GetIsOpen(w), "ShowTab closes a tooltip that was still open");
            ToolTip.SetTip(w, null);

            // 2. The Back row keeps its room: toggling it never moves the section rows.
            var back = w.Named<Button>("BtnNavBack")!;
            var host = back.Parent as Control;
            Check(back.IsVisible && host?.Height == 24, "Back shows inside its fixed 24px host once there is history");

            // 3. Landing on the Profile tab repaints the sharing footer (OnTabShown).
            w.ShowTab("discord");
            var sharing = w.ProfilePage?.FindControl<TextBlock>("TxtProfileSharingSummary")?.Text;
            Check(!string.IsNullOrEmpty(sharing) && sharing!.Contains('·'),
                  $"the Profile tab repaints its sharing footer (saw \"{sharing}\")");

            // 4. DiscordTabView's generated x:Name fields are assigned (InitializeComponent).
            var profile = w.ProfilePage;
            Check(profile is not null && ReferenceEquals(profile.TxtProfileSharingSummary,
                                                     profile.FindControl<TextBlock>("TxtProfileSharingSummary")),
                  "DiscordTabView's generated x:Name fields are assigned (InitializeComponent, not the loader)");

            w.ShowTab("settings");
            var shown = 0;
            foreach (var n in new[] { "SettingsTab","PresetsTab","QuestsTab","ProgramsTab","EnhancementsTab","DeeperTab","AchievementsTab","CompanionTab","PlayTab","LeaderboardTab","AssetsTab","DiscordTab","AwarenessTab","RemoteControlTab","AvailableSubjectsTab","BambiTakeoverTab","StudioTab","LockdownTab","BlinkTrainerTab","SheListeningTab","GradedIntakeTab","AppSettingsTab","SpiralTab","ExclusivesTab" })
                if (Vis(n)) shown++;
            Check(shown == 1, $"exactly one tab visible (saw {shown})");

            var shots = Environment.GetEnvironmentVariable("CCP_NAV_SHOTS");
            if (!string.IsNullOrWhiteSpace(shots)) fails += Shoot(w, shots!);

            Console.WriteLine(fails == 0 ? "nav-check: shell navigation holds." : $"nav-check: {fails} failure(s).");
            return fails == 0 ? 0 : 1;
        }

        /// <summary>Home, Studio and Social, as the user sees them, into the given directory.</summary>
        private static int Shoot(MainShellWindow w, string dir)
        {
            int fails = 0;
            Directory.CreateDirectory(dir);
            var pairs = new[] { ("settings", "d1-home.png"), ("studio", "d1-studio.png"), ("availablesubjects", "d1-social.png") };
            // CCP_NAV_SHOTS_ALL=1: one PNG per visible page, for the side-by-side with WPF.
            if (Environment.GetEnvironmentVariable("CCP_NAV_SHOTS_ALL") == "1")
                pairs = "settings premium studio presets companion personality permissions companionlinks companionai play deeper availablesubjects friends leaderboard remotecontrol leash discord quests achievements enhancements programs chaster assets appsettings"
                    .Split(' ').Select(k => (k, "par-" + k + ".png")).ToArray();
            foreach (var (tab, file) in pairs)
            {
                w.ShowTab(tab);
                for (int i = 0; i < 4; i++) global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                using var frame = w.CaptureRenderedFrame();
                if (frame is null) { fails++; Console.Error.WriteLine("FAIL no frame for " + tab); continue; }
                var path = Path.Combine(dir, file);
                frame.Save(path);
                Console.WriteLine($"shot -> {path} ({frame.PixelSize.Width}x{frame.PixelSize.Height})");
            }
            return fails;
        }
    }
}
