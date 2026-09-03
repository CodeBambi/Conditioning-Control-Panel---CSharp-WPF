// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Settings.cs (726 lines), plus the
// two navigation entry points that live elsewhere in the WPF window but belong with them:
// OpenDeviceSettings (MainWindow.LabTab.cs:383) and RequestPickAssetsFolder
// (MainWindow.xaml.cs:1969 -> BtnPickAssetsFolder_Click, MainWindow.UiUpdates.cs:2114).
//
// The header this file used to carry - "every member below reaches App.*" - was wrong for half of
// them. What actually resolves today, and is restored:
//
//   * OpenAppSettingsSection / OpenDeviceSettings. Both are ShowTab + AppSettingsTab.FocusSection,
//     and BOTH halves are on this head: ShowTab is real (MainShellWindow.TabNavigation.cs) and
//     AppSettingsTabView.FocusSection is a full port with the same nine SectionKeys. Nothing was
//     missing from Core; they were simply never written here.
//   * The ? panel itself. Every tutorial button's first two lines close MainTutorialOverlay and
//     put SettingsTab.BrowserContainer back, and both controls exist in this head's XAML. Only the
//     tour that follows needs App.Tutorial, so the panel now opens and closes for real instead of
//     every one of its rows being inert.
//   * RequestPickAssetsFolder. Avalonia's StorageProvider replaces FolderBrowserDialog, and
//     CoreSettings.Current.CustomAssetsPath is exactly where WPF writes the answer - CorePaths
//     .EffectiveAssets reads it straight back through the head's provider.
//
// Still blocked, each naming its symbol rather than a vague service:
//   - StartTutorial(TutorialType) and every tour behind it: ConditioningControlPanel/Services/
//     TutorialService.cs (App.Tutorial), which drives TutorialOverlay step by step.
//   - OpenBugReportWindow: ConditioningControlPanel/Windows/BugReportWindow.xaml, not ported.
//   - BtnTutorialModding_Click's second half: Windows/ModCreatorWindow.xaml, not ported.
//   - BtnSave_Click: SaveSettings() re-derives AppSettings from AppSettingsTab's controls, but on
//     this head every one of those controls is already a live editor that writes Current and
//     saves (GeneralSettingsSection, PerformanceSettingsSection, AudioSettingsSection, ...).
//     Restoring the read-back would make the shell a SECOND writer of settings the sections
//     already own - the exact hazard WPF's own Phase 8 comment spends 20 lines on. The rest of
//     the handler (_allPresets, the "save as preset" offer, FlashSaveAbsorb) is preset machinery
//     that is stubbed in MainShellWindow.Presets.cs.
//   - LoadSettings / UpdateSliderTexts: same reason - each Settings section seeds itself.
//   - The pack migration inside BtnPickAssetsFolder_Click: Services/PackEncryptionService.cs and
//     Services/ContentPacks are not on this head, so no .packs folder is discovered to move.
//   - The post-change rescan (App.Flash/Video/BubbleCount/ContentPacks RefreshImagesPath etc. and
//     RefreshAssetTree): those four services and the assets tree are not on this head.
//   - Services/Auth/SecurityHelper.IsPersonalFolderRoot: the #1053 refusal. Not in Core, so the
//     guard is re-stated privately below; delete the local copy the day it moves.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // =====================================================================================
        //  navigation entry points - what every "Configure in Settings" button calls
        // =====================================================================================

        /// <summary>
        /// Opens the Settings door and scrolls to one of its sections. Goes through
        /// <see cref="ShowTab"/> so the door expansion and the per-tab FX teardown behave exactly
        /// as a rail click would. Valid keys: <see cref="Tabs.AppSettingsTabView.SectionKeys"/>;
        /// an unknown key still opens the door and simply does not scroll.
        /// </summary>
        internal void OpenAppSettingsSection(string sectionKey)
        {
            try
            {
                ShowTab("appsettings");
                AppSettingsPage?.FocusSection(sectionKey);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "OpenAppSettingsSection({Key}) failed", sectionKey);
            }
        }

        /// <summary>
        /// "Configure in Settings" from the three webcam status chips and the System panel's
        /// no-panic row. The named case of <see cref="OpenAppSettingsSection"/>, kept as its own
        /// method because ~6 call sites say "device settings" rather than a section key.
        /// </summary>
        internal void OpenDeviceSettings() => OpenAppSettingsSection("devices");

        // =====================================================================================
        //  assets folder
        // =====================================================================================

        /// <summary>
        /// Picks the custom assets folder. Async where WPF was blocking: Avalonia's picker returns
        /// a Task, so callers fire and forget (<c>_ = RequestPickAssetsFolder();</c>) the way the
        /// WPF wrappers called the click handler.
        /// </summary>
        internal async Task RequestPickAssetsFolder()
        {
            try
            {
                var current = CoreSettings.Current.CustomAssetsPath;
                var start = !string.IsNullOrWhiteSpace(current) && Directory.Exists(current)
                    ? current
                    : CorePaths.EffectiveAssets;

                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select a folder for your custom assets (images and videos)",
                    AllowMultiple = false,
                    SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start),
                });
                if (folders.Count != 1 || folders[0].TryGetLocalPath() is not { } selected) return;

                // #1053: this folder becomes the root of every media scan in the app, and the app
                // also writes into it (images/, videos/, .packs/, .temp/). Neither belongs on the
                // Desktop or a drive root.
                if (IsPersonalFolderRoot(selected))
                {
                    await MessageDialog.ShowAsync(this, "Pick a folder of your own",
                        "That folder is one of your system's own - your Desktop, Documents, " +
                        "Pictures, Downloads, your home folder or a whole drive." +
                        Environment.NewLine + Environment.NewLine +
                        "The app both reads and writes here, so pick or make a folder that holds " +
                        "nothing but your assets.");
                    return;
                }

                Directory.CreateDirectory(Path.Combine(selected, "images"));
                Directory.CreateDirectory(Path.Combine(selected, "videos"));

                CoreSettings.Current.CustomAssetsPath = selected;
                CoreSettings.Save();
                Log.Information("Custom assets path set to: {Path}", selected);

                await MessageDialog.ShowAsync(this, Loc.Get("title_assets_folder_set"),
                    Loc.GetF("msg_custom_assets_folder_set_0", selected));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "RequestPickAssetsFolder failed");
            }
        }

        /// <summary>
        /// ponytail: a private re-statement of Services/Auth/SecurityHelper.IsPersonalFolderRoot,
        /// which is in the WPF head's assembly and so unreachable from here. Same rule minus the
        /// four Common* special folders (empty on Linux, and this head's reason to exist), same
        /// fail-closed default (an unreadable path is treated as personal). Delete this and call
        /// the shared one the day SecurityHelper moves to Core.
        /// </summary>
        private static bool IsPersonalFolderRoot(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return true;
            try
            {
                var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

                var root = Path.GetPathRoot(dir);
                if (!string.IsNullOrEmpty(root) &&
                    string.Equals(dir, Path.TrimEndingDirectorySeparator(root!), StringComparison.OrdinalIgnoreCase))
                    return true;

                var known = new List<string>();
                foreach (var folder in new[]
                {
                    Environment.SpecialFolder.Desktop,
                    Environment.SpecialFolder.DesktopDirectory,
                    Environment.SpecialFolder.MyDocuments,
                    Environment.SpecialFolder.MyPictures,
                    Environment.SpecialFolder.MyVideos,
                    Environment.SpecialFolder.MyMusic,
                    Environment.SpecialFolder.UserProfile,
                })
                {
                    var p = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(p)) known.Add(p);
                }

                // Downloads has no SpecialFolder id on any platform; the profile-relative name
                // catches the default, which is what WPF settles for too.
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile))
                    foreach (var name in new[] { "Downloads", "Desktop", "Documents", "Pictures", "Videos", "Music" })
                        known.Add(Path.Combine(profile, name));

                foreach (var p in known)
                    if (string.Equals(dir, Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)),
                                      StringComparison.OrdinalIgnoreCase))
                        return true;

                return false;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "IsPersonalFolderRoot check failed for: {Path}", directory);
                return true;
            }
        }

        // =====================================================================================
        //  the "?" panel - MainTutorialOverlay
        // =====================================================================================

        /// <summary>Opens the help panel.</summary>
        private void BtnMainHelp_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => SetTutorialOverlay(true);

        /// <summary>Closes the help panel. Bound to both the scrim's PointerPressed and the ✕
        /// button's Click, which is why it takes the base RoutedEventArgs.</summary>
        private void MainTutorial_Close(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => CloseTutorialOverlay();

        /// <summary>Clicking the card must not close the panel behind it.</summary>
        private void MainTutorial_ContentClick(object? sender, global::Avalonia.Input.PointerPressedEventArgs e)
            => e.Handled = true;

        private void CloseTutorialOverlay() => SetTutorialOverlay(false);

        /// <summary>
        /// Both controls are resolved by name, never through the generated fields - this window
        /// loads with AvaloniaXamlLoader.Load, which leaves every one of them null (see the header
        /// of MainShellWindow.TabNavigation.cs). BrowserContainer belongs to SettingsTabView, which
        /// does call InitializeComponent, so its own field is real once the view itself is found.
        /// </summary>
        internal void SetTutorialOverlay(bool open)
        {
            var overlay = Named<global::Avalonia.Controls.Control>("MainTutorialOverlay");
            if (overlay != null) overlay.IsVisible = open;
            // WPF hides the browser first because WebView2 ignores WPF z-order. This head has no
            // WebView2 yet, but it is the same control and the panel behaves identically.
            var browser = Named<Tabs.SettingsTabView>("SettingsTab")?.BrowserContainer;
            if (browser != null) browser.IsVisible = !open;
        }

        // Every row of the ? panel: close the panel, then start a tour. The close half is real;
        // the tour half is one call into App.Tutorial, which is not on this head. Named per row so
        // the day TutorialService lands, each is one line rather than a rediscovery of which
        // TutorialType a row meant.
        //   WhatMoved => UpgradeTour   GettingStarted => GettingStarted   Settings => Settings
        //   Presets => Presets         Progression => Progression         Achievements => Achievements
        //   Companion => Companion     Patreon => Patreon                 Avatar => Avatar
        //   Awareness => Awareness (plus the one-shot "open the Puppy preset editor when the tour
        //   finishes naturally" hook in MainWindow.Settings.cs:670)
        //   StartTutorial (the panel's big button) => FullTour
        private void BtnStartTutorial_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialWhatMoved_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialGettingStarted_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialSettings_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialPresets_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialProgression_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialAchievements_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialCompanion_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialPatreon_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialAvatar_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();
        private void BtnTutorialAwareness_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();

        /// <summary>ponytail: closes the panel, then WPF opens Windows/ModCreatorWindow.xaml with
        /// startWithTutorial:true. That window is not ported.</summary>
        private void BtnTutorialModding_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();

        /// <summary>ponytail: closes the panel, then WPF opens Windows/BugReportWindow.xaml.
        /// Not ported.</summary>
        private void BtnTutorialReportBug_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => CloseTutorialOverlay();

        /// <summary>ponytail: needs Windows/BugReportWindow.xaml. Deliberately NOT approximated
        /// with a message box - a bug report that goes nowhere is worse than a button that admits
        /// it does nothing.</summary>
        private void BtnReportBug_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }

        /// <summary>ponytail: see the "Still blocked" note at the top of this file - restoring
        /// SaveSettings() here would make the shell a second writer of settings each Settings
        /// section already owns and saves.</summary>
        private void BtnSave_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) { }
    }
}
