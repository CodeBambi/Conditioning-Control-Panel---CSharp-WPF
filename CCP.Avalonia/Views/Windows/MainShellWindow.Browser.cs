// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Browser.cs (3,084 lines).
//
// Two things are LIVE here, and one of them has a caller:
//   * BtnDiscordTab_Click - the header's Profile door. MainShellWindow.axaml:1123 names it, and
//     on WPF it is one ShowTab("discord") call. It is that here too. CALLED.
//   * the site navigation, the offline gate and the profile-viewer clear. Restored in full and
//     each is complete, but NOTHING CALLS THEM YET - see "Callers still missing" below.
//
// WHY NAVIGATION IS PORTABLE AT ALL. The blanket "this whole file is WebView2" header that used
// to sit here was wrong. SettingsTabView.axaml already carries <controls:WebHost x:Name=
// "BrowserWebHost"/>, which wraps Avalonia's NativeWebView and draws a legible fallback panel
// naming the page where no engine is installed. Setting its Source IS the navigation, and its
// NavigationCompleted carries the live URL (OnBrowserNavigationCompleted). What WebHost does NOT
// expose is a document-script channel, a mute or a fullscreen signal - and
// every member that needed one of those is still a note, not a half-port.
//
// TWO DELIBERATE DEVIATIONS, both because WebHost is narrower than CoreWebView2:
//   1. autoPlayFullscreen is ACCEPTED AND IGNORED by NavigateToUrlInBrowser, and logged. The WPF
//      path injects JS from a NavigationCompleted hook; returning false for that flag alone would
//      break the plain-navigation half of the same call for every caller.
//   2. (Resolved) the offline-block toast now shows through App.Notifications, as WPF
//      NotifyBrowserBlockedOffline does (MainWindow.Browser.cs:489-494).
//
// REFUSED, not "not done yet": the mute pair. BtnMuteBrowser_Click flips
// AppSettings.BrowserVideoMuted and applies it live through CoreWebView2.IsMuted;
// SyncBrowserMuteIcon paints the glyph from the saved flag. WebHost has no mute, so restoring the
// pair would move the glyph to "muted" over a web view still playing at full volume - a control
// that lies about state, and the one control whose entire job is to say whether sound is coming
// out. A stub until WebHost exposes IsAudioMuted.
//
// CALLERS STILL MISSING, each one line in a file this layer does not own:
//   * (Resolved) SettingsTabView forwards RbBambiCloud / RbHypnoTube / BtnReloadBrowser and the
//     WebHost's NavigationCompleted to this partial.
//   * CCP.Avalonia/Views/Tabs/DiscordTabView.axaml.cs:BtnClearProfile_Click now forwards
//     (`Host?.BtnClearProfile_Click`), which wires
//     ClearProfileViewer -> SetProfileViewingSelf. NavigateToUrlInBrowser stays public for
//     AvatarTubeWindow's speech-bubble links and the remote controller, neither on this head.
// STILL BLOCKED, grouped by what is actually missing (60 members):
//   * WebView2 itself (36): the init/teardown chain (InitializeBrowserAsync, InitAndNavigateAsync,
//     TearDownBrowserForReinit, NavigateWhenBrowserReadyAsync, BrowserLoadingText_Click,
//     OpenUrlExternallyAfterBrowserFailure, _browserInitializing, _browserCorePending); the
//     script channel (AutoPlayAndFullscreenVideoAsync,
//     AutoPlayBambiCloudPlaylistAsync, EndWebVideoTakeover, OnBrowserWebMessageReceived,
//     HandleBrowserMediaMessage, the four HandleAudioSync* members, HookHapticAudioSyncRearm,
//     OnHapticConnectionChangedForAudioSync, _hapticAudioSyncConnHooked); the pop-out and
//     fullscreen rig (BtnPopOutBrowser_Click, FocusBrowserSurface's pop-out half,
//     HandleBrowserFullscreenChanged, Arm/DisarmFullscreenEscapes, Enter/ExitBrowserFullscreen,
//     ExitBrowserFullscreenForTeardown, the four _browserFs* fields); the remote video pair
//     (PlayHypnotubeFromRemote, StopBrowserVideoFromRemote, _remoteBrowserVideoActive); the
//     refused mute pair above.
//   * the Trainer Card read (search, own card, lookup, badges, achievements, FormatNumber) is
//     PORTED onto CCP.Avalonia/Views/Tabs/DiscordTabView.axaml.cs, text rules in Core
//     TrainerCardText. Still missing: BtnDiscordTabLogin_Click, UpdateDiscordTabUI,
//     ResolveProfilePictureUnavailable, LoadPatreonBadgeImage, ProfileDiscordHandle_Click,
//     BtnProfileDiscord_Click (art). Change name / delete profile are ported below.
//   * CoreMods (1): SyncSiteRadiosToActiveMod needs ShowBambiCloudOption() and
//     GetDefaultBrowserUrl(); CCP.Core/CoreMods.cs carries neither yet. Its IsBrowserShowingKnownSite
//     helper is NOT restored either: WebHost.CurrentUrl now answers it, but its only caller is
//     SyncSiteRadiosToActiveMod, so write it with that caller.

using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The header's Profile door. One ShowTab call on WPF, one here.</summary>
        private void BtnDiscordTab_Click(object? sender, RoutedEventArgs e) => ShowTab("discord");

        // ============================== the embedded browser ==============================

        private const string BambiCloudHome = "https://bambicloud.com/";
        private const string HypnoTubeHome = "https://hypnotube.com/";

        /// <summary>The Settings tab, which hosts the browser card. Resolved on every read, and
        /// its controls reached with FindControl: this window loads with AvaloniaXamlLoader.Load,
        /// so its generated fields are never assigned (MainShellWindow.TabNavigation.cs).</summary>
        private Tabs.SettingsTabView? BrowserPage => Named<Tabs.SettingsTabView>("SettingsTab");

        private WebHost? BrowserView => BrowserPage?.FindControl<WebHost>("BrowserWebHost");

        /// <summary>Homepage of whichever site radio is selected. BambiCloud is the default, as on
        /// WPF, including the "both deselected after an external link" case.</summary>
        private string SiteHomeUrl() =>
            BrowserPage?.FindControl<RadioButton>("RbHypnoTube")?.IsChecked == true
                ? HypnoTubeHome : BambiCloudHome;

        /// <summary>The offline gate, checked FIRST by every entry point below - #867 moved it
        /// above the lazy-init branch on WPF because underneath it the first click still built a
        /// browser and loaded a page, the one thing the block exists to prevent.</summary>
        /// <param name="userInitiated">WPF toasts only for navigation the user asked for; so does
        /// this, and it picks the log level.</param>
        private static bool BrowserBlockedOffline(bool userInitiated = true)
        {
            if (!CoreSettings.Current.OfflineMode) return false;
            if (userInitiated)
            {
                Log.Information("Browser action blocked by offline mode");
                App.Notifications.Show(Loc.Get("browser_toast_offline_blocked"),
                    Helpers.NotificationType.Warning, TimeSpan.FromSeconds(6));
            }
            else Log.Debug("Browser navigation blocked by offline mode");
            return true;
        }

        /// <summary>Points the web view at a URL. A missing host (the tab is not in the tree, as in
        /// a single-view render) is a no-op that returns false, never a throw.</summary>
        private bool NavigateBrowser(string url)
        {
            try
            {
                var host = BrowserView;
                if (host == null) return false;
                host.Navigate(new Uri(url));   // even when equal: WPF #867, re-click goes home
                Log.Information("Browser navigated to {Url}", url);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Browser navigation failed for {Url}", url);
                return false;
            }
        }

        /// <summary>WPF's <c>_browser.NavigationCompleted</c> handler (MainWindow.Browser.cs:132-160),
        /// minus the audio-sync injection (no document script channel on WebHost). Forwarded by
        /// SettingsTabView from its WebHost with the LIVE url, so a link clicked inside the page
        /// is looked up too, not just the URL we asked for.</summary>
        internal void OnBrowserNavigationCompleted(Uri url)
        {
            if (BrowserPage?.FindControl<TextBlock>("TxtBrowserStatus") is { } status)
            {
                // Bound, not assigned: a local Text under {loc:Str} is undone on a language change.
                status.Bind(TextBlock.TextProperty, new global::Avalonia.Data.Binding("[label_connected_2]")
                    { Source = LocalizationManager.Instance, Mode = global::Avalonia.Data.BindingMode.OneWay });
                status.Foreground = new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.FromRgb(0, 230, 118));
            }
            TriggerCatalogueLookupForNavigation(url.AbsoluteUri);   // escaped, like CoreWebView2.Source
        }

        /// <summary>Back to the selected site's homepage. Shared by the reload button and the
        /// remote video-stop path, exactly as on WPF.</summary>
        private void NavigateBrowserToCurrentSiteHome()
        {
            RevealDashboardBrowser("reload");   // WPF Browser.cs:3153 / :603 (site-toggle)
            NavigateBrowser(SiteHomeUrl());
        }

        /// <summary>The site radios' one entry point. Click, not IsCheckedChanged, for the WPF
        /// reason (#867) and an Avalonia one: clicking the site you are already on must still take
        /// you home, and a programmatic IsChecked write raises IsCheckedChanged here, so a change
        /// handler would navigate behind the user's back on every sync.</summary>
        internal void BrowserSiteToggle_Click(object? sender, RoutedEventArgs e)
        {
            if (BrowserBlockedOffline()) return;
            NavigateBrowserToCurrentSiteHome();
        }

        /// <summary>Toolbar reload: back onto the selected site's homepage. The way out of a stuck
        /// page, since re-clicking an already-checked radio raises no change event.</summary>
        internal void BtnReloadBrowser_Click(object? sender, RoutedEventArgs e)
        {
            if (BrowserBlockedOffline()) return;
            NavigateBrowserToCurrentSiteHome();
        }

        /// <summary>Navigates the embedded browser and brings its tab forward. Public because
        /// the callers live elsewhere on WPF (speech-bubble links, the remote controller).</summary>
        /// <param name="autoPlayFullscreen">ACCEPTED AND IGNORED - see deviation 2 in the header.
        /// WebHost has no script channel, so the video takeover cannot be requested. The plain
        /// navigation still happens, which is what every caller needs first.</param>
        public bool NavigateToUrlInBrowser(string url, bool autoPlayFullscreen = false, bool userInitiated = true)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (BrowserBlockedOffline(userInitiated)) return false;

            // WPF's FocusBrowserSurface, minus the pop-out branch: there is no browser pop-out
            // window on this head, so the embedded surface is always the one to bring forward.
            ShowTab("settings");
            RevealDashboardBrowser("focus-surface");   // WPF Browser.cs:349
            Activate();

            // Move the radios to match the URL, deselecting both for an external link so that
            // clicking either one afterwards navigates back to it. Safe to write: the handlers
            // above are Click-driven, so nothing here starts a second navigation.
            var lower = url.ToLowerInvariant();
            var bambi = BrowserPage?.FindControl<RadioButton>("RbBambiCloud");
            var hypno = BrowserPage?.FindControl<RadioButton>("RbHypnoTube");
            if (bambi != null) bambi.IsChecked = lower.Contains("bambicloud.com");
            if (hypno != null) hypno.IsChecked = lower.Contains("hypnotube.com");

            if (autoPlayFullscreen)
                Log.Warning("Auto-play/fullscreen requested but WebHost has no script channel - takeover skipped: {Url}", url);

            return NavigateBrowser(url);
        }

        // ============================== the profile viewer ==============================

        /// <summary>Clears the search box and puts the "search for a user" plate back up.</summary>
        internal void BtnClearProfile_Click(object? sender, RoutedEventArgs e)
        {
            var search = ProfilePage?.FindControl<TextBox>("TxtProfileSearch");
            if (search != null) search.Text = "";
            ClearProfileViewer();
        }

        /// <summary>Takes whoever's card is up off the screen. Every line of the WPF body is
        /// presentation over controls this head carries. WPF also stops the OgBorderAnimation
        /// storyboard; here collapsing the container re-gates the loop (MainShellWindow.ProfileFx.cs
        /// listens to its IsVisible). Ends in
        /// SetProfileViewingSelf(true): nothing on screen belongs to anyone else any more, so the
        /// "back to me" chip retires and Customize and Privacy come back.</summary>
        internal void ClearProfileViewer()
        {
            try
            {
                var page = ProfilePage;
                if (page == null) return;

                var wrapper = page.FindControl<Grid>("ProfileCardWrapper");
                if (wrapper != null) wrapper.IsVisible = false;
                var empty = page.FindControl<Border>("NoProfileSelected");
                if (empty != null) empty.IsVisible = true;
                var grid = page.FindControl<ItemsControl>("ProfileAchievementGrid");
                if (grid != null) grid.ItemsSource = null;
                var ogBorder = page.FindControl<Border>("OgBorderContainer");
                if (ogBorder != null) ogBorder.IsVisible = false;
                var ogBadge = page.FindControl<Border>("OgBannerBadge");
                if (ogBadge != null) ogBadge.IsVisible = false;
                var patreon = page.FindControl<Image>("ProfilePatreonTierBadge");
                if (patreon != null) patreon.IsVisible = false;

                SetProfileViewingSelf(true);
            }
            catch (Exception ex) { Log.Debug("ClearProfileViewer: {E}", ex.Message); }
        }

        /// <summary>WPF MainWindow.Browser.cs:1677 BtnChangeDisplayName_Click; the request goes through CoreAccount.</summary>
        internal async System.Threading.Tasks.Task ChangeDisplayNameAsync()
        {
            var button = ProfilePage?.FindControl<Button>("BtnChangeDisplayName");
            try
            {
                var s = CoreSettings.Current;
                var currentName = s.UserDisplayName ?? "";
                var dialog = new Dialogs.DisplayNameDialog(isChangeName: true, currentName: currentName);
                if (!await dialog.ShowDialog<bool>(this)) return;

                var newName = dialog.DisplayName;
                if (string.Equals(newName, currentName, StringComparison.Ordinal)) return;

                if (button != null) button.IsEnabled = false;
                var (success, error, resultName) = await CoreAccount.ChangeDisplayNameAsync(newName);
                if (success && resultName != null)
                {
                    s.UserDisplayName = resultName;
                    CoreSettings.Save();
                    var name = ProfilePage?.FindControl<TextBlock>("TxtProfileViewerName");
                    if (name != null) name.Text = resultName;
                    UpdateQuickLoginUI();
                }
                else
                    await MessageDialog.ShowAsync(this, Loc.Get("title_name_change_failed"), error ?? Loc.Get("msg_failed_to_change_display_name"));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error changing display name");
                await MessageDialog.ShowAsync(this, Loc.Get("label_error"), Loc.Get("msg_error_changing_name"));
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }

        /// <summary>WPF MainWindow.Browser.cs:1730 BtnDeleteProfile_Click: type DELETE, delete server-side, then a
        /// full sign-out that pushes nothing and wipes the quest file (AccountSeed.Logout(accountDeleted: true)).</summary>
        internal async System.Threading.Tasks.Task DeleteProfileAsync()
        {
            var button = ProfilePage?.FindControl<Button>("BtnDeleteProfile");
            try
            {
                if (!await new Dialogs.DisplayNameDialog("delete").ShowDialog<bool>(this)) return;

                if (button != null) button.IsEnabled = false;
                var (success, error) = await CoreAccount.DeleteAccountAsync();
                if (success)
                {
                    await Platform.AccountSeed.Logout(accountDeleted: true);
                    UpdateQuickLoginUI(accountChanged: true);
                    ClearProfileViewer();
                    await MessageDialog.ShowAsync(this, Loc.Get("title_profile_deleted"), Loc.Get("msg_profile_deleted"));
                }
                else
                    await MessageDialog.ShowAsync(this, Loc.Get("title_deletion_failed"), error ?? Loc.Get("msg_failed_to_delete_profile"));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error deleting profile");
                await MessageDialog.ShowAsync(this, Loc.Get("label_error"), Loc.Get("msg_error_deleting_profile"));
            }
            finally
            {
                if (button != null) button.IsEnabled = true;
            }
        }
    }
}
