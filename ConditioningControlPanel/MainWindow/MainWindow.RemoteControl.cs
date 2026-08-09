using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Remote control tab: remote command handlers and pairing UI.
    public partial class MainWindow
    {
        #region Remote Control Handlers

        internal async void ChkRemoteControlEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var isEnabled = RemoteControlTab.ChkRemoteControlEnabled.IsChecked ?? false;

            if (isEnabled)
            {
                // Must have a cloud identity (unified ID from Patreon or Discord login)
                if (string.IsNullOrEmpty(App.UnifiedUserId))
                {
                    _isLoading = true;
                    RemoteControlTab.ChkRemoteControlEnabled.IsChecked = false;
                    _isLoading = false;
                    ShowStyledDialog(Loc.Get("title_login_required"), Loc.Get("msg_login_required_remote"), Loc.Get("btn_ok"), "");
                    return;
                }

                var tier = GetSelectedRemoteTier();

                // Show consent waiver
                if (!ShowRemoteControlWaiver(tier))
                {
                    // Defer revert so it runs after the dialog's event stack fully unwinds,
                    // preventing WPF toggle animation from getting stuck in the ON position.
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        RemoteControlTab.ChkRemoteControlEnabled.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }

                // Start session
                RemoteControlTab.RemoteControlPanel.Visibility = System.Windows.Visibility.Visible;
                var code = await App.RemoteControl.StartSessionAsync(tier);
                if (code == null)
                {
                    _isLoading = true;
                    RemoteControlTab.ChkRemoteControlEnabled.IsChecked = false;
                    _isLoading = false;
                    RemoteControlTab.RemoteControlPanel.Visibility = System.Windows.Visibility.Collapsed;
                    ShowStyledDialog(Loc.Get("title_connection_error"), Loc.Get("msg_remote_connection_error"), Loc.Get("btn_ok"), "");
                    return;
                }

                RemoteControlTab.TxtRemoteCode.Text = string.Join(" ", code.ToCharArray());
                var pin = App.RemoteControl?.ConnectPin;
                RemoteControlTab.TxtRemotePin.Text = !string.IsNullOrEmpty(pin) ? $"PIN: {pin}" : "";
                RemoteControlTab.TxtRemotePin.Visibility = !string.IsNullOrEmpty(pin)
                    ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                RemoteControlTab.RemoteLinkPanel.Visibility = System.Windows.Visibility.Visible;
                RemoteControlTab.RemoteCodePanel.Visibility = System.Windows.Visibility.Visible;
                RemoteControlTab.RemoteStatusPanel.Visibility = System.Windows.Visibility.Visible;
                RemoteControlTab.BtnStopRemote.Visibility = System.Windows.Visibility.Visible;
                UpdateRemoteStatus(false);
                RefreshRemoteQrCode(BuildRemotePairingUrl(code));

                // SP5 layer 3 / community feedback: instead of hiding the opt-in
                // ("Show Me") section once a session is active, keep it visible but
                // grey it out + disable it. This gives the user a clear "you're
                // listed" confirmation rather than the controls silently vanishing.
                // Then chain the directory opt-in call if the user ticked the
                // checkbox. Best-effort — the session is already running.
                if (RemoteControlTab.OptInSectionPanel != null)
                {
                    RemoteControlTab.OptInSectionPanel.IsEnabled = false;
                    RemoteControlTab.OptInSectionPanel.Opacity = 0.5;
                }
                UpdateDirectoryListingStatus();
                _ = RunOptInChainAsync();

                // Listen for controller connection changes
                App.RemoteControl.ControllerConnectedChanged += OnRemoteControllerChanged;
                App.RemoteControl.ControllerIdleChanged += OnRemoteControllerIdleChanged;
                App.RemoteControl.CommandReceived += OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded += OnRemoteSessionEnded;

                // Wire up session callbacks for remote status
                WireRemoteSessionCallbacks();
            }
            else
            {
                await StopRemoteControl();
            }

            // Keep the dashboard Remote chip's dot honest no matter where the toggle was flipped
            // (Remote tab or dashboard chip) — previously enabling from the tab left the dot stale (#440).
            try { RefreshPremiumRail(); } catch { }
        }

        private string GetSelectedRemoteTier()
        {
            return (RemoteControlTab.CmbRemoteTier.SelectedIndex) switch
            {
                0 => "light",
                1 => "standard",
                2 => "full",
                _ => "light"
            };
        }

        private bool ShowRemoteControlWaiver(string tier)
        {
            var actions = new System.Text.StringBuilder();
            actions.AppendLine("  - Trigger flash images (from YOUR image folder)");
            actions.AppendLine("  - Trigger subliminal messages (from YOUR subliminal pool)");
            actions.AppendLine("  - Toggle overlays (pink filter, spiral)");
            actions.AppendLine("  - Start/stop bubbles");

            if (tier is "standard" or "full")
            {
                actions.AppendLine("  - Trigger mandatory videos (from YOUR video folder)");
                actions.AppendLine("  - Trigger haptic device patterns");
                actions.AppendLine("  - Duck/unduck audio");
            }

            if (tier == "full")
            {
                actions.AppendLine("  - Start/stop autonomy mode");
                actions.AppendLine("  - Start/pause/stop sessions");
                actions.AppendLine("  - Enable strict lock (videos cannot be skipped)");
                actions.AppendLine("  - Disable panic button (ESC key won't work)");
            }

            var message = $"You are about to allow another person to remotely control parts of your app.\n\n" +
                          $"The Controller will be able to:\n{actions}\n" +
                          $"All media content shown comes from YOUR local files and settings.\n" +
                          $"You assume full responsibility for this interaction.\n" +
                          $"You can stop the session at ANY time by clicking \"Stop Session\" or closing the app.\n" +
                          $"The session stays active as long as the app is running. If the app closes without stopping the session, it expires within 4 hours.";

            var confirmed = WarningDialog.ShowDoubleWarning(this,
                "Remote Control",
                message);

            return confirmed;
        }

        internal async void CmbRemoteTier_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_isLoading) return;
            if (!App.RemoteControl?.IsActive == true) return;

            // If session is active and tier changed, restart with new tier
            var newTier = GetSelectedRemoteTier();
            if (newTier != App.RemoteControl.Tier)
            {
                if (!ShowRemoteControlWaiver(newTier))
                    return;

                // Unsubscribe before stopping so OnRemoteSessionEnded doesn't collapse the panel.
                // ControllerIdleChanged has to come off too or the re-subscribe below stacks a
                // second copy on the multicast list every time the tier is changed.
                App.RemoteControl.ControllerConnectedChanged -= OnRemoteControllerChanged;
                App.RemoteControl.ControllerIdleChanged -= OnRemoteControllerIdleChanged;
                App.RemoteControl.CommandReceived -= OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded -= OnRemoteSessionEnded;

                await App.RemoteControl.StopSessionAsync();
                var code = await App.RemoteControl.StartSessionAsync(newTier);
                if (code != null)
                {
                    RemoteControlTab.TxtRemoteCode.Text = string.Join(" ", code.ToCharArray());
                    var reconnectPin = App.RemoteControl?.ConnectPin;
                    RemoteControlTab.TxtRemotePin.Text = !string.IsNullOrEmpty(reconnectPin) ? $"PIN: {reconnectPin}" : "";
                    RemoteControlTab.TxtRemotePin.Visibility = !string.IsNullOrEmpty(reconnectPin)
                        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
                    UpdateRemoteStatus(false);
                }

                // Re-subscribe after restart
                App.RemoteControl.ControllerConnectedChanged += OnRemoteControllerChanged;
                App.RemoteControl.ControllerIdleChanged += OnRemoteControllerIdleChanged;
                App.RemoteControl.CommandReceived += OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded += OnRemoteSessionEnded;
            }
        }

        internal void BtnCopyRemoteCode_Click(object sender, RoutedEventArgs e)
        {
            var code = App.RemoteControl?.SessionCode;
            if (!string.IsNullOrEmpty(code))
            {
                try
                {
                    var pin = App.RemoteControl?.ConnectPin;
                    var copyText = !string.IsNullOrEmpty(pin) ? $"{code} (PIN: {pin})" : code;
                    System.Windows.Clipboard.SetText(copyText);
                    RemoteControlTab.BtnCopyRemoteCode.Content = Loc.Get("btn_copied");
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Failed to copy remote code to clipboard");
                    RemoteControlTab.BtnCopyRemoteCode.Content = Loc.Get("label_failed");
                }
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, _) => { RemoteControlTab.BtnCopyRemoteCode.Content = Loc.Get("btn_copy"); timer.Stop(); };
                timer.Start();
            }
        }

        internal void BtnCopyRemoteLink_Click(object sender, RoutedEventArgs e)
        {
            var code = App.RemoteControl?.SessionCode;
            var url = !string.IsNullOrEmpty(code)
                ? BuildRemotePairingUrl(code)
                : "https://cclabs.app/remote/";
            try
            {
                System.Windows.Clipboard.SetText(url);
                RemoteControlTab.BtnCopyRemoteLink.Content = Loc.Get("btn_copied");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to copy remote link to clipboard");
                RemoteControlTab.BtnCopyRemoteLink.Content = Loc.Get("label_failed");
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (s, _) => { RemoteControlTab.BtnCopyRemoteLink.Content = Loc.Get("btn_copy_link"); timer.Stop(); };
            timer.Start();
        }

        internal async void BtnStopRemote_Click(object sender, RoutedEventArgs e)
        {
            await StopRemoteControl();
            _isLoading = true;
            RemoteControlTab.ChkRemoteControlEnabled.IsChecked = false;
            _isLoading = false;
        }

        internal void ChkStopEffectsOnRemoteDisconnect_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (App.Settings?.Current == null) return;
            App.Settings.Current.StopEffectsOnRemoteDisconnect = RemoteControlTab.ChkStopEffectsOnRemoteDisconnect.IsChecked ?? false;
            App.Settings.Save();
        }

        // Privacy toggle: share linked avatar with controllers. Persists to settings,
        // and if a remote session is currently active pushes status immediately so the
        // controller's pinned strip flips within their next poll (~3s) rather than
        // waiting up to ~15s for the next scheduled status push.
        internal async void ChkRemoteShareAvatar_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (App.Settings?.Current == null) return;
            App.Settings.Current.RemoteShareAvatar = RemoteControlTab.ChkRemoteShareAvatar.IsChecked ?? false;
            App.Settings.Save();
            if (App.RemoteControl?.IsActive == true)
            {
                try { await App.RemoteControl.PushStatusNowAsync(); }
                catch (Exception ex) { App.Logger?.Warning(ex, "[RemoteShareAvatar] immediate status push failed"); }
            }
        }

        // Emote picker — preset click, custom send, edit popup. The preset list
        // is bound to App.Settings.Current.RemoteEmotePresets in LoadSettings().
        // Watermark on RemoteControlTab.TxtEmoteCustom is driven by EmoteHelper.LastSentEmoteHint
        // (session-only state, reset on app restart) with EmoteHelper.PlaceholderText
        // as the localized fallback.
        private Models.EmotePreset? _editingPreset;

        internal async void BtnEmotePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Models.EmotePreset preset) return;
            if (string.IsNullOrWhiteSpace(preset.Text)) return; // can't send a label-less slot
            await SendEmoteAndReportAsync(preset.Text, preset.Icon ?? "", "preset", RemoteControlTab.TxtEmoteStatus);
        }

        internal async void BtnEmoteCustomSend_Click(object sender, RoutedEventArgs e)
        {
            await SendCustomEmoteAsync(RemoteControlTab.TxtEmoteCustom, RemoteControlTab.TxtEmoteStatus);
        }

        internal async void TxtEmoteCustom_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendCustomEmoteAsync(RemoteControlTab.TxtEmoteCustom, RemoteControlTab.TxtEmoteStatus);
            }
        }

        // Splash-overlay (big) picker. Shares the same source list, same service,
        // same debounce (per-RemoteControlService instance), different UI targets.
        private async void BtnEmotePresetBig_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Models.EmotePreset preset) return;
            if (string.IsNullOrWhiteSpace(preset.Text)) return;
            await SendEmoteAndReportAsync(preset.Text, preset.Icon ?? "", "preset", TxtEmoteStatusBig);
        }

        private async void BtnEmoteCustomSendBig_Click(object sender, RoutedEventArgs e)
        {
            await SendCustomEmoteAsync(TxtEmoteCustomBig, TxtEmoteStatusBig);
        }

        private async void TxtEmoteCustomBig_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SendCustomEmoteAsync(TxtEmoteCustomBig, TxtEmoteStatusBig);
            }
        }

        private async Task SendCustomEmoteAsync(TextBox? textbox, TextBlock? statusTarget)
        {
            var raw = textbox?.Text ?? "";
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) return; // whitespace-only / empty: silent no-op
            var sent = await SendEmoteAndReportAsync(trimmed, "", "custom", statusTarget);
            if (sent && textbox != null)
            {
                // Clear the box; the watermark switches to the ghost-of-last-sent
                // on THIS textbox only — the small and big custom boxes maintain
                // independent ghost state because each has its own attached prop.
                textbox.Text = "";
                Helpers.EmoteHelper.SetLastSentEmoteHint(textbox, trimmed);
            }
        }

        // Returns true on successful send (so the custom path knows to clear the box).
        // Internal so the avatar context-menu surface (AvatarTubeWindow.MenuItemEmote_Click)
        // can route through the same centralized helper for step 3.6 speech-bubble feedback.
        internal async Task<bool> SendEmoteAndReportAsync(string text, string icon, string kind, TextBlock? statusTarget)
        {
            if (App.RemoteControl == null) return false;

            // Step 3.6: flash the avatar speech bubble before the await so the user gets
            // instant feedback regardless of which surface they fired from. Skip when the
            // call would immediately bounce (no active session or still in debounce window)
            // to avoid a "Sending..." flicker that would never resolve to "Sent: ...".
            var willActuallySend = App.RemoteControl.IsActive && !App.RemoteControl.IsWithinDebounceWindow;
            if (willActuallySend)
            {
                App.AvatarWindow?.ShowEmoteFeedback(text, isPending: true);
            }

            var (ok, error, retry) = await App.RemoteControl.SendEmoteAsync(text, icon, kind);

            if (ok)
            {
                // Update the bubble to "Sent: ..." once the server confirms 200.
                App.AvatarWindow?.ShowEmoteFeedback(text, isPending: false);
                if (statusTarget != null)
                {
                    statusTarget.Foreground = System.Windows.Media.Brushes.LightGreen;
                    statusTarget.Text = Localization.Loc.Get("status_emote_sent");
                }
                return true;
            }
            if (error == "debounced")
            {
                // Silent — debounce is a UX guard, not a user-facing condition.
                return false;
            }
            if (statusTarget != null)
            {
                statusTarget.Foreground = System.Windows.Media.Brushes.Salmon;
                if (error == "rate_limited" && retry.HasValue)
                {
                    statusTarget.Text = Localization.Loc.GetF("status_emote_rate_limited", retry.Value);
                }
                else if (error == "session not active")
                {
                    statusTarget.Text = Localization.Loc.Get("status_emote_no_session");
                }
                else
                {
                    statusTarget.Text = Localization.Loc.Get("status_emote_failed");
                }
            }
            return false;
        }

        internal void BtnEmoteEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not Models.EmotePreset preset) return;
            _editingPreset = preset;
            if (RemoteControlTab.TxtEditEmoteIcon != null) RemoteControlTab.TxtEditEmoteIcon.Text = preset.Icon ?? "";
            if (RemoteControlTab.TxtEditEmoteText != null) RemoteControlTab.TxtEditEmoteText.Text = preset.Text ?? "";
            if (RemoteControlTab.BtnEditEmoteSave != null) RemoteControlTab.BtnEditEmoteSave.IsEnabled = !string.IsNullOrWhiteSpace(preset.Text);
            if (RemoteControlTab.EmoteEditPopup != null)
            {
                RemoteControlTab.EmoteEditPopup.PlacementTarget = btn;
                RemoteControlTab.EmoteEditPopup.IsOpen = true;
            }
            RemoteControlTab.TxtEditEmoteText?.Focus();
        }

        internal void TxtEditEmoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RemoteControlTab.BtnEditEmoteSave == null || RemoteControlTab.TxtEditEmoteText == null) return;
            RemoteControlTab.BtnEditEmoteSave.IsEnabled = !string.IsNullOrWhiteSpace(RemoteControlTab.TxtEditEmoteText.Text);
        }

        internal void BtnEditEmoteSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingPreset == null) return;
            var newText = (RemoteControlTab.TxtEditEmoteText?.Text ?? "").Trim();
            if (newText.Length == 0) return; // defense in depth — Save button should already be disabled
            _editingPreset.Icon = (RemoteControlTab.TxtEditEmoteIcon?.Text ?? "");
            _editingPreset.Text = newText;
            App.Settings?.Save();
            if (RemoteControlTab.EmoteEditPopup != null) RemoteControlTab.EmoteEditPopup.IsOpen = false;
            _editingPreset = null;
        }

        internal void BtnEditEmoteCancel_Click(object sender, RoutedEventArgs e)
        {
            if (RemoteControlTab.EmoteEditPopup != null) RemoteControlTab.EmoteEditPopup.IsOpen = false;
            _editingPreset = null;
        }

        // =====================================================================
        // SP5 layer 3 — Available Subjects tab (controller side)
        // =====================================================================

        private bool _availableSubjectsBound;

        private void BtnAvailableSubjects_Click(object sender, RoutedEventArgs e)
        {
            ShowTab("availablesubjects");
        }

        internal void BtnBecomeASubject_Click(object sender, RoutedEventArgs e)
        {
            // Premium → take them straight to the Remote Control tab so they
            // can opt into the directory. Free → open the Patreon page.
            if (App.Patreon?.HasPremiumAccess == true)
            {
                ShowTab("remotecontrol");
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.patreon.com/CodeBambi",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Subjects] failed to open Patreon URL");
            }
        }

        /// <summary>
        /// Shows the italic "support the project" subtitle only to free users.
        /// Premium users see just the button (which opens the Remote Control tab).
        /// </summary>
        private void RefreshBecomeASubjectCta()
        {
            if (AvailableSubjectsTab.TxtBecomeASubjectSubtitle == null) return;
            var hasPremium = App.Patreon?.HasPremiumAccess == true;
            AvailableSubjectsTab.TxtBecomeASubjectSubtitle.Visibility = hasPremium ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// One-time binding: hook the service's ObservableCollection to the
        /// ItemsControl ItemsSource and the IsEmpty/HasError flags to the
        /// empty/error panels. Called from ShowTab on first navigation.
        /// </summary>
        private void EnsureAvailableSubjectsBound()
        {
            if (_availableSubjectsBound) return;
            if (App.AvailableSubjects == null) return;
            if (AvailableSubjectsTab.AvailableSubjectsList == null) return;

            AvailableSubjectsTab.AvailableSubjectsList.ItemsSource = App.AvailableSubjects.Entries;
            App.AvailableSubjects.PropertyChanged += OnAvailableSubjectsServicePropertyChanged;
            UpdateAvailableSubjectsEmptyAndError();
            RefreshBecomeASubjectCta();
            _availableSubjectsBound = true;
        }

        private void OnAvailableSubjectsServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // The service raises these from a background task — marshal to UI.
            Dispatcher.Invoke(UpdateAvailableSubjectsEmptyAndError);
        }

        internal void AvailableSubjectsScroller_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is not System.Windows.Controls.ScrollViewer sv) return;
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void UpdateAvailableSubjectsEmptyAndError()
        {
            var svc = App.AvailableSubjects;
            if (svc == null) return;
            // Show error panel if last refresh failed; show empty panel if
            // last refresh was clean but the roster is empty. Otherwise both
            // hidden (cards visible).
            if (AvailableSubjectsTab.AvailableSubjectsErrorPanel != null)
                AvailableSubjectsTab.AvailableSubjectsErrorPanel.Visibility = svc.HasError
                    ? Visibility.Visible : Visibility.Collapsed;
            if (AvailableSubjectsTab.AvailableSubjectsEmptyPanel != null)
                AvailableSubjectsTab.AvailableSubjectsEmptyPanel.Visibility = (!svc.HasError && svc.IsEmpty)
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Connect button on a subject card. Reads the entry from the button's
        /// DataContext, calls the service to claim, and on success opens the
        /// returned session_url in the user's default browser via Process.Start.
        ///
        /// Privacy: the session_url string lives in this method's stack only —
        /// referenced once for Process.Start, never logged, never assigned to
        /// any field. The hash fragment carries the PIN; the cclabs.app/remote/
        /// page strips it from the URL after parsing.
        ///
        /// 409 → service handles silently (re-fetches, card flips to TAKEN).
        /// other failures → no toast in v1; user can re-click. Audit-log
        /// coverage for the failure mode is filed as the SP6 followup.
        /// </summary>
        internal async void BtnConnectSubject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            if (btn.DataContext is not ConditioningControlPanel.Services.DirectoryEntry entry) return;
            if (entry.Claimed) return; // belt-and-braces; IsEnabled binding already guards
            if (App.AvailableSubjects == null) return;

            btn.IsEnabled = false;
            try
            {
                var url = await App.AvailableSubjects.TryClaimAsync(entry.UnifiedId);
                if (string.IsNullOrEmpty(url)) return; // 409 handled silently or transient error

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    // Don't echo the URL into the log line — exception message
                    // typically only carries the OS error code anyway.
                    App.Logger?.Warning(ex, "[AvailableSubjects] failed to open browser for claimed session");
                }
            }
            finally
            {
                // Restore the button — IsEnabled binding will recompute on the
                // next refresh based on entry.Claimed.
                btn.IsEnabled = entry.IsConnectEnabled;
            }
        }

        // =====================================================================
        // SP5 layer 3 — Available Subjects directory opt-in
        // =====================================================================
        // The opt-in checkbox itself NEVER persists across sessions. The user
        // re-opts every time. Tags + status_text persist to AppSettings only
        // when RemoteControlTab.ChkRememberOptInDetails is ticked at the moment of session
        // start. The chained opt-in API call happens after StartSessionAsync
        // returns a code; failure is best-effort (logged + non-blocking inline
        // status), the session itself is unaffected.

        // Per locked decisions: 10 fixed tags, cap selection at 5.
        private const int OptInMaxTags = 5;

        private System.Windows.Controls.CheckBox[] OptInTagCheckBoxes() => new[]
        {
            RemoteControlTab.ChkTagBimbo, RemoteControlTab.ChkTagDrone, RemoteControlTab.ChkTagTrance, RemoteControlTab.ChkTagFeminization,
            RemoteControlTab.ChkTagSubmission, RemoteControlTab.ChkTagDegradation, RemoteControlTab.ChkTagAudioOk, RemoteControlTab.ChkTagSoftOnly,
            RemoteControlTab.ChkTagLockdownOk, RemoteControlTab.ChkTagChastity
        };

        internal void ChkOptIntoDirectory_Changed(object sender, RoutedEventArgs e)
        {
            if (RemoteControlTab.OptInFormPanel == null) return;
            var checkedNow = RemoteControlTab.ChkOptIntoDirectory?.IsChecked == true;
            RemoteControlTab.OptInFormPanel.Visibility = checkedNow
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;

            // First time user opens the form this session → pre-populate from
            // saved settings (only if Remember was previously ticked).
            if (checkedNow) PopulateOptInFormFromSavedSettings();
        }

        private void PopulateOptInFormFromSavedSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            // Saved tags → check matching boxes.
            var saved = new HashSet<string>(s.SavedDirectoryTags ?? new List<string>());
            foreach (var cb in OptInTagCheckBoxes())
            {
                var tag = cb.Tag as string ?? "";
                cb.IsChecked = saved.Contains(tag);
            }

            // Saved status text + char count.
            if (RemoteControlTab.TxtOptInStatus != null) RemoteControlTab.TxtOptInStatus.Text = s.SavedDirectoryStatusText ?? "";
            UpdateOptInStatusCharCount();

            // Remember toggle reflects saved preference.
            if (RemoteControlTab.ChkRememberOptInDetails != null)
                RemoteControlTab.ChkRememberOptInDetails.IsChecked = s.RememberDirectoryDetails;
        }

        internal void TxtOptInStatus_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateOptInStatusCharCount();
        }

        private void UpdateOptInStatusCharCount()
        {
            if (RemoteControlTab.TxtOptInStatusCount == null || RemoteControlTab.TxtOptInStatus == null) return;
            var len = (RemoteControlTab.TxtOptInStatus.Text ?? "").Length;
            RemoteControlTab.TxtOptInStatusCount.Text = $"{len}/80";
        }

        internal void ChkOptInTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox cb) return;
            if (cb.IsChecked != true) return; // unchecking is always fine; only cap on check

            var checkedCount = OptInTagCheckBoxes().Count(c => c.IsChecked == true);
            if (checkedCount > OptInMaxTags)
            {
                // Soft cap: undo the just-clicked check and surface a brief inline
                // hint via the feedback TextBlock.
                cb.IsChecked = false;
                ShowOptInFeedback(Loc.Get("msg_optin_directory_max_tags"), persistMs: 2500);
            }
        }

        private List<string> GetSelectedDirectoryTags()
        {
            var list = new List<string>();
            foreach (var cb in OptInTagCheckBoxes())
            {
                if (cb.IsChecked == true && cb.Tag is string tag && !string.IsNullOrEmpty(tag))
                    list.Add(tag);
            }
            return list;
        }

        private System.Windows.Threading.DispatcherTimer? _optInFeedbackTimer;
        private void ShowOptInFeedback(string message, int persistMs)
        {
            if (RemoteControlTab.TxtOptInFeedback == null) return;
            RemoteControlTab.TxtOptInFeedback.Text = message;
            RemoteControlTab.TxtOptInFeedback.Visibility = System.Windows.Visibility.Visible;
            _optInFeedbackTimer?.Stop();
            _optInFeedbackTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(persistMs)
            };
            _optInFeedbackTimer.Tick += (_, _) =>
            {
                _optInFeedbackTimer?.Stop();
                if (RemoteControlTab.TxtOptInFeedback != null)
                {
                    RemoteControlTab.TxtOptInFeedback.Text = "";
                    RemoteControlTab.TxtOptInFeedback.Visibility = System.Windows.Visibility.Collapsed;
                }
            };
            _optInFeedbackTimer.Start();
        }

        /// <summary>
        /// Runs after a successful StartSessionAsync. If the user opted in,
        /// chains the proxy /v2/directory/opt-in call and persists the
        /// tags+status on success when "Remember" is ticked. All best-effort:
        /// the session is already running and is not affected by failures here.
        /// </summary>
        private async Task RunOptInChainAsync()
        {
            if (App.RemoteControl == null) return;
            if (RemoteControlTab.ChkOptIntoDirectory?.IsChecked != true) return;

            var tags = GetSelectedDirectoryTags();
            var statusText = RemoteControlTab.TxtOptInStatus?.Text ?? "";
            // Defensive: cap to OptInMaxTags + 80c even if UI somehow let more
            // through (paste, accessibility, etc.). The proxy validates again,
            // but failing client-side here is faster + clearer.
            if (tags.Count > OptInMaxTags) tags = tags.Take(OptInMaxTags).ToList();
            if (statusText.Length > 80) statusText = statusText.Substring(0, 80);

            var ok = await App.RemoteControl.OptInToDirectoryAsync(tags, statusText);
            if (!ok)
            {
                ShowOptInFeedback(Loc.Get("msg_optin_directory_failed"), persistMs: 4000);
                return;
            }

            // Listed successfully → confirm it in the Session Code section and
            // the header status pill so the user doesn't have to tab to Subjects.
            _directoryOptedIn = true;
            UpdateDirectoryListingStatus();

            // Persist tags+status only on success AND only when Remember is on.
            if (RemoteControlTab.ChkRememberOptInDetails?.IsChecked == true && App.Settings?.Current is { } s)
            {
                s.RememberDirectoryDetails = true;
                s.SavedDirectoryTags = tags;
                s.SavedDirectoryStatusText = statusText;
                App.Settings.Save();
            }
            else if (RemoteControlTab.ChkRememberOptInDetails?.IsChecked != true && App.Settings?.Current is { } s2 && s2.RememberDirectoryDetails)
            {
                // Remember was previously on, now off → clear saved state.
                s2.RememberDirectoryDetails = false;
                s2.SavedDirectoryTags = new List<string>();
                s2.SavedDirectoryStatusText = "";
                App.Settings.Save();
            }
        }

        // True once the directory opt-in for the active remote session has
        // succeeded. Drives the "Listed/Claimed" header pill + Session Code
        // confirmation. Reset when the remote session stops.
        private bool _directoryOptedIn;

        /// <summary>
        /// Refreshes the header listing pill and the Session Code confirmation
        /// banner from the current remote-session state:
        ///   • no active session            → pill hidden, banner hidden
        ///   • active, not opted in         → "Private only"
        ///   • active, opted in, no claimer → "Listed" + confirmation banner
        ///   • active, opted in, claimed    → "Claimed" + confirmation banner
        /// </summary>
        private void UpdateDirectoryListingStatus()
        {
            var active = App.RemoteControl?.IsActive ?? false;
            var claimed = App.RemoteControl?.ControllerConnected ?? false;

            // Session Code confirmation banner: only once listed.
            if (RemoteControlTab.ListedConfirmationPanel != null)
                RemoteControlTab.ListedConfirmationPanel.Visibility = (active && _directoryOptedIn)
                    ? System.Windows.Visibility.Visible
                    : System.Windows.Visibility.Collapsed;

            if (DirectoryStatusPill == null) return;

            if (!active)
            {
                DirectoryStatusPill.Visibility = System.Windows.Visibility.Collapsed;
                return;
            }

            string text;
            System.Windows.Media.Color dot;
            string tip;
            if (!_directoryOptedIn)
            {
                text = "Private only";
                dot = System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0xA0); // muted grey
                tip = "Your session is private — you are not listed in the Available Subjects Directory.";
            }
            else if (claimed)
            {
                text = "Claimed";
                dot = System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88); // green
                tip = "A controller has claimed your directory listing.";
            }
            else
            {
                text = "Listed";
                dot = System.Windows.Media.Color.FromRgb(0xB4, 0x7B, 0xFF); // neon purple (directory accent)
                tip = "You're listed in the Available Subjects Directory and waiting to be claimed.";
            }

            if (TxtDirectoryStatus != null) TxtDirectoryStatus.Text = text;
            if (DirectoryStatusDot != null) DirectoryStatusDot.Fill = new System.Windows.Media.SolidColorBrush(dot);
            DirectoryStatusPill.ToolTip = tip;
            DirectoryStatusPill.Visibility = System.Windows.Visibility.Visible;
        }

        private async Task StopRemoteControl()
        {
            if (App.RemoteControl != null)
            {
                App.RemoteControl.ControllerConnectedChanged -= OnRemoteControllerChanged;
                // Was never detached, so every enable/disable cycle left another live copy
                // subscribed to the service and firing into this (now torn-down) tab UI.
                App.RemoteControl.ControllerIdleChanged -= OnRemoteControllerIdleChanged;
                App.RemoteControl.CommandReceived -= OnRemoteCommandReceived;
                App.RemoteControl.SessionEnded -= OnRemoteSessionEnded;
                await App.RemoteControl.StopSessionAsync();
            }

            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
            RemoteControlTab.RemoteControlPanel.Visibility = System.Windows.Visibility.Collapsed;
            RemoteControlTab.RemoteLinkPanel.Visibility = System.Windows.Visibility.Collapsed;
            RemoteControlTab.RemoteCodePanel.Visibility = System.Windows.Visibility.Collapsed;
            RemoteControlTab.RemoteStatusPanel.Visibility = System.Windows.Visibility.Collapsed;
            RemoteControlTab.BtnStopRemote.Visibility = System.Windows.Visibility.Collapsed;
            if (RemoteControlTab.ImgRemoteQrCode != null) RemoteControlTab.ImgRemoteQrCode.Source = null;
            if (RemoteControlTab.LstRemoteCommandLog != null) RemoteControlTab.LstRemoteCommandLog.Items.Clear();

            // SP5 layer 3: restore the opt-in section so the user can
            // configure it for the next session (or untick if they're done).
            // The opt-in checkbox itself stays unchecked — re-opt every time.
            // Re-enable + un-grey it (it stays visible during the session now).
            if (RemoteControlTab.OptInSectionPanel != null)
            {
                RemoteControlTab.OptInSectionPanel.Visibility = System.Windows.Visibility.Visible;
                RemoteControlTab.OptInSectionPanel.IsEnabled = true;
                RemoteControlTab.OptInSectionPanel.Opacity = 1.0;
            }
            if (RemoteControlTab.ChkOptIntoDirectory != null)
                RemoteControlTab.ChkOptIntoDirectory.IsChecked = false;
            if (RemoteControlTab.OptInFormPanel != null)
                RemoteControlTab.OptInFormPanel.Visibility = System.Windows.Visibility.Collapsed;

            // Clear the directory listing state + header pill.
            _directoryOptedIn = false;
            UpdateDirectoryListingStatus();
        }

        private void OnRemoteControllerChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var connected = App.RemoteControl?.ControllerConnected ?? false;
                UpdateRemoteStatus(connected);
                UpdateStartButtonForRemoteControl(connected);
                // Header listing pill flips Listed ↔ Claimed as a controller
                // connects/disconnects.
                UpdateDirectoryListingStatus();

                if (connected)
                {
                    // A controller joining must NOT kill the subject's scripted session
                    // (#878). It used to call _sessionEngine.StopSession(completed: false)
                    // here, which threw away the session's elapsed time and its bonus XP
                    // the instant somebody connected — and the 120 s idle auto-disconnect
                    // meant an idle controller could do it again on every reconnect.
                    // The controller sees live session progress in the status push and has
                    // explicit verbs (pause_session / stop_session / start_session /
                    // trigger_panic) when it genuinely wants the floor. See the matching
                    // comment in RemoteControlService's connect path.
                    ShowRemoteControlOverlay();
                    NotifyRemoteControllerJoined();
                }
                else
                {
                    HideRemoteControlOverlay();
                }
            });
        }

        private void OnRemoteControllerIdleChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                var idle = App.RemoteControl?.ControllerIdle ?? false;
                TxtRemoteOverlaySubtitle.Text = idle
                    ? "Controller may be idle..."
                    : "Someone else is controlling your app";
                TxtRemoteOverlaySubtitle.Foreground = new System.Windows.Media.SolidColorBrush(
                    idle ? System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00)  // orange
                         : System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0)); // gray
            });
        }

        private void OnRemoteSessionEnded(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                HideRemoteControlOverlay();
                UpdateStartButtonForRemoteControl(false);
                _isLoading = true;
                RemoteControlTab.ChkRemoteControlEnabled.IsChecked = false;
                _isLoading = false;
                RemoteControlTab.RemoteControlPanel.Visibility = System.Windows.Visibility.Collapsed;
                RemoteControlTab.RemoteCodePanel.Visibility = System.Windows.Visibility.Collapsed;
                RemoteControlTab.RemoteStatusPanel.Visibility = System.Windows.Visibility.Collapsed;
                RemoteControlTab.BtnStopRemote.Visibility = System.Windows.Visibility.Collapsed;

                // Re-enable + un-grey the opt-in section and clear the listing pill.
                if (RemoteControlTab.OptInSectionPanel != null)
                {
                    RemoteControlTab.OptInSectionPanel.IsEnabled = true;
                    RemoteControlTab.OptInSectionPanel.Opacity = 1.0;
                }
                _directoryOptedIn = false;
                UpdateDirectoryListingStatus();
            });
        }

        private void UpdateRemoteStatus(bool controllerConnected)
        {
            if (controllerConnected)
            {
                RemoteControlTab.RemoteStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88));
                RemoteControlTab.TxtRemoteStatus.Text = Loc.Get("label_controller_connected");
                RemoteControlTab.TxtRemoteStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88));
            }
            else
            {
                RemoteControlTab.RemoteStatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xA5, 0x00));
                RemoteControlTab.TxtRemoteStatus.Text = Loc.Get("label_waiting_for_controller");
                RemoteControlTab.TxtRemoteStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xA0));
            }
        }

        private void ShowRemoteControlOverlay()
        {
            var code = App.RemoteControl?.SessionCode;
            var overlayPin = App.RemoteControl?.ConnectPin;
            var sessionText = !string.IsNullOrEmpty(code)
                ? $"Session: {string.Join(" ", code.ToCharArray())}"
                : "";
            if (!string.IsNullOrEmpty(overlayPin))
                sessionText += $"  PIN: {overlayPin}";
            TxtOverlaySessionCode.Text = sessionText;

            // Hide browser to avoid WebView2 airspace issue (renders on top of WPF overlays)
            SettingsTab.BrowserContainer.Visibility = System.Windows.Visibility.Hidden;
            RemoteControlOverlay.Visibility = System.Windows.Visibility.Visible;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            RemoteControlOverlay.BeginAnimation(OpacityProperty, fadeIn);

            StartRemoteSessionInfoTimer();
        }

        private void HideRemoteControlOverlay()
        {
            if (RemoteControlOverlay.Visibility != System.Windows.Visibility.Visible) return;

            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, _) =>
            {
                RemoteControlOverlay.Visibility = System.Windows.Visibility.Collapsed;
                // Restore browser visibility now that overlay is gone
                SettingsTab.BrowserContainer.Visibility = System.Windows.Visibility.Visible;
            };
            RemoteControlOverlay.BeginAnimation(OpacityProperty, fadeOut);
            _remoteNotificationTimer?.Stop();
            _remoteSessionInfoTimer?.Stop();
        }

        private void WireRemoteSessionCallbacks()
        {
            if (App.RemoteControl == null) return;

            App.RemoteControl.GetAvailableSessionsCallback = () =>
            {
                var sessions = new List<object>();
                try
                {
                    // Include built-in sessions
                    foreach (var s in Models.Session.GetAllSessions().Where(s => s.IsAvailable))
                    {
                        sessions.Add(new { id = s.Id, name = s.GetModeAwareName(), icon = s.Icon, duration_minutes = s.DurationMinutes, difficulty = s.Difficulty.ToString() });
                    }
                    // Include custom sessions from SessionManager
                    if (_sessionManager != null)
                    {
                        foreach (var s in _sessionManager.CustomSessions.Where(s => s.IsAvailable))
                        {
                            sessions.Add(new { id = s.Id, name = s.GetModeAwareName(), icon = s.Icon, duration_minutes = s.DurationMinutes, difficulty = s.Difficulty.ToString() });
                        }
                    }
                }
                catch { }
                return sessions;
            };

            App.RemoteControl.GetSessionProgressCallback = () =>
            {
                try
                {
                    if (_sessionEngine?.IsRunning != true || _sessionEngine.CurrentSession == null)
                        return null;

                    var session = _sessionEngine.CurrentSession;
                    var phaseIndex = _sessionEngine.CurrentPhaseIndex;
                    var phaseName = session.Phases != null && phaseIndex >= 0 && phaseIndex < session.Phases.Count
                        ? session.Phases[phaseIndex].Name : "";

                    return new Services.SessionProgressInfo
                    {
                        Name = session.GetModeAwareName(),
                        Icon = session.Icon,
                        ElapsedSeconds = (int)_sessionEngine.ElapsedTime.TotalSeconds,
                        TotalSeconds = session.DurationMinutes * 60,
                        IsPaused = _sessionEngine.IsPaused,
                        CurrentPhase = phaseName
                    };
                }
                catch { return null; }
            };

            App.RemoteControl.FindSessionByIdCallback = (sessionId) =>
            {
                try
                {
                    // Check built-in sessions first
                    var session = Models.Session.GetAllSessions()
                        .FirstOrDefault(s => s.Id == sessionId && s.IsAvailable);
                    // Then check custom sessions
                    if (session == null && _sessionManager != null)
                    {
                        session = _sessionManager.CustomSessions
                            .FirstOrDefault(s => s.Id == sessionId && s.IsAvailable);
                    }
                    return session;
                }
                catch { return null; }
            };
        }

        private void StartRemoteSessionInfoTimer()
        {
            _remoteSessionInfoTimer?.Stop();
            _remoteSessionInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _remoteSessionInfoTimer.Tick += (s, _) => UpdateRemoteSessionInfo();
            _remoteSessionInfoTimer.Start();
            UpdateRemoteSessionInfo();
        }

        private void UpdateRemoteSessionInfo()
        {
            try
            {
                if (_sessionEngine?.IsRunning == true && _sessionEngine.CurrentSession != null)
                {
                    var session = _sessionEngine.CurrentSession;
                    // Show active state, hide idle state
                    RemoteSessionIdle.Visibility = Visibility.Collapsed;
                    RemoteSessionActive.Visibility = Visibility.Visible;

                    TxtRemoteSessionName.Text = $"{session.Icon} {session.GetModeAwareName()}";

                    var elapsed = _sessionEngine.ElapsedTime;
                    var total = TimeSpan.FromMinutes(session.DurationMinutes);
                    var pauseLabel = _sessionEngine.IsPaused ? "  ⏸ PAUSED" : "";
                    TxtRemoteSessionTime.Text = $"{elapsed:mm\\:ss} / {total:mm\\:ss}{pauseLabel}";

                    var phaseIndex = _sessionEngine.CurrentPhaseIndex;
                    if (session.Phases != null && phaseIndex >= 0 && phaseIndex < session.Phases.Count)
                        TxtRemoteSessionPhase.Text = session.Phases[phaseIndex].Name;
                    else
                        TxtRemoteSessionPhase.Text = "";
                }
                else
                {
                    // Show idle state, hide active state
                    RemoteSessionIdle.Visibility = Visibility.Visible;
                    RemoteSessionActive.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void OnRemoteCommandReceived(object? sender, string action)
        {
            if (SuppressedCommands.Contains(action)) return;

            Dispatcher.Invoke(() =>
            {
                ShowCommandNotification(action);
                AppendRemoteCommandLog(action);
            });
        }

        /// <summary>
        /// Appends a command to the Remote Control tab's command log.
        /// Caps the log at 50 entries (oldest dropped).
        /// </summary>
        private void AppendRemoteCommandLog(string action)
        {
            if (RemoteControlTab.LstRemoteCommandLog == null) return;
            try
            {
                var label = CommandLabels.TryGetValue(action, out var l) ? Loc.Get(l) : action.Replace("_", " ");
                var entry = $"{DateTime.Now:HH:mm:ss}  {label}";
                RemoteControlTab.LstRemoteCommandLog.Items.Insert(0, entry);
                while (RemoteControlTab.LstRemoteCommandLog.Items.Count > 50)
                    RemoteControlTab.LstRemoteCommandLog.Items.RemoveAt(RemoteControlTab.LstRemoteCommandLog.Items.Count - 1);
            }
            catch { }
        }

        /// <summary>
        /// Refreshes the Remote Control tab UI: gating overlay, QR code (if a session
        /// is active), tier card highlight. Called whenever the tab is shown.
        /// </summary>
        private void UpdateRemoteControlUI()
        {
            RefreshPremiumGate(RemoteControlTab.RemoteControlGate);
            RefreshTierCardHighlight();
            // If a session is already running, refresh the QR code with the current code.
            var code = App.RemoteControl?.SessionCode;
            if (!string.IsNullOrEmpty(code))
                RefreshRemoteQrCode(BuildRemotePairingUrl(code));
            else if (RemoteControlTab.ImgRemoteQrCode != null)
                RemoteControlTab.ImgRemoteQrCode.Source = null;
        }

        /// <summary>
        /// Generates the pairing URL for the QR code from the current session code.
        /// Uses a hash fragment so the PIN never appears in server access logs or
        /// Referer headers. The web page parses the fragment and auto-connects.
        /// </summary>
        private string BuildRemotePairingUrl(string code)
        {
            var pin = App.RemoteControl?.ConnectPin;
            if (!string.IsNullOrEmpty(pin))
                return $"https://cclabs.app/remote/#code={code}&pin={pin}";
            return $"https://cclabs.app/remote/#code={code}";
        }

        /// <summary>
        /// Renders a QR code image into RemoteControlTab.ImgRemoteQrCode for the given pairing URL.
        /// </summary>
        private void RefreshRemoteQrCode(string url)
        {
            if (RemoteControlTab.ImgRemoteQrCode == null) return;
            try
            {
                // Pull mod-themed colors. Use AccentDarkColor for foreground (max contrast on white).
                byte[] fgRgb = new byte[] { 0xFF, 0x14, 0x93 };
                byte[] bgRgb = new byte[] { 0xFF, 0xFF, 0xFF };
                try
                {
                    var accentDarkHex = App.Mods?.GetAccentDarkColorHex();
                    if (!string.IsNullOrEmpty(accentDarkHex))
                    {
                        var fgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(accentDarkHex);
                        fgRgb = new byte[] { fgColor.R, fgColor.G, fgColor.B };
                    }
                }
                catch { /* fall back to default pink */ }

                using var generator = new QRCoder.QRCodeGenerator();
                using var data = generator.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
                using var qr = new QRCoder.PngByteQRCode(data);
                var bytes = qr.GetGraphic(10, fgRgb, bgRgb);
                using var ms = new MemoryStream(bytes);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = ms;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                RemoteControlTab.ImgRemoteQrCode.Source = bmp;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to render remote QR code");
            }
        }

        /// <summary>
        /// Highlights the active tier card based on RemoteControlTab.CmbRemoteTier.SelectedIndex.
        /// </summary>
        private void RefreshTierCardHighlight()
        {
            if (RemoteControlTab.TierCardLight == null || RemoteControlTab.TierCardStandard == null || RemoteControlTab.TierCardFull == null) return;
            var dim = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x48));
            var active = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));
            RemoteControlTab.TierCardLight.BorderBrush = dim;
            RemoteControlTab.TierCardLight.BorderThickness = new Thickness(1);
            RemoteControlTab.TierCardStandard.BorderBrush = dim;
            RemoteControlTab.TierCardStandard.BorderThickness = new Thickness(1);
            RemoteControlTab.TierCardFull.BorderBrush = dim;
            RemoteControlTab.TierCardFull.BorderThickness = new Thickness(1);

            var idx = RemoteControlTab.CmbRemoteTier?.SelectedIndex ?? 0;
            Border? activeCard = idx switch
            {
                1 => RemoteControlTab.TierCardStandard,
                2 => RemoteControlTab.TierCardFull,
                _ => RemoteControlTab.TierCardLight,
            };
            if (activeCard != null)
            {
                activeCard.BorderBrush = active;
                activeCard.BorderThickness = new Thickness(2);
            }
        }

        /// <summary>
        /// Routes a tier card click to the legacy RemoteControlTab.CmbRemoteTier handler so the
        /// existing tier-change logic still fires.
        /// </summary>
        internal void TierCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tagStr && int.TryParse(tagStr, out var idx))
            {
                if (RemoteControlTab.CmbRemoteTier != null && RemoteControlTab.CmbRemoteTier.SelectedIndex != idx)
                    RemoteControlTab.CmbRemoteTier.SelectedIndex = idx;
                RefreshTierCardHighlight();
            }
        }

        private void ShowCommandNotification(string action)
        {
            var label = CommandLabels.TryGetValue(action, out var l) ? Loc.Get(l) : action.Replace("_", " ");
            TxtRemoteCommand.Text = label;

            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            RemoteCommandNotification.BeginAnimation(OpacityProperty, fadeIn);

            _remoteNotificationTimer?.Stop();
            _remoteNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _remoteNotificationTimer.Tick += (s, _) =>
            {
                _remoteNotificationTimer.Stop();
                HideCommandNotification();
            };
            _remoteNotificationTimer.Start();
        }

        private void HideCommandNotification()
        {
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            RemoteCommandNotification.BeginAnimation(OpacityProperty, fadeOut);
        }

        private async void BtnEndRemoteSession_Click(object sender, RoutedEventArgs e)
        {
            await StopRemoteControl();
            _isLoading = true;
            RemoteControlTab.ChkRemoteControlEnabled.IsChecked = false;
            _isLoading = false;
        }

        // Methods called by RemoteControlService for session commands
        internal async void StartSessionFromRemote(Models.Session session)
        {
            try
            {
                App.Logger?.Information("[RemoteControl] StartSessionFromRemote called for: {Name} (id: {Id})", session.Name, session.Id);

                // Stop any existing running session first
                if (_sessionEngine?.IsRunning == true)
                {
                    App.Logger?.Information("[RemoteControl] Stopping existing session engine before starting new one");
                    _sessionEngine.StopSession(completed: false);
                }

                if (_sessionEngine == null)
                {
                    _sessionEngine = new Services.SessionEngine(this);
                    _sessionEngine.SessionCompleted += OnSessionCompleted;
                    _sessionEngine.ProgressUpdated += OnSessionProgressUpdated;
                    _sessionEngine.PhaseChanged += OnSessionPhaseChanged;
                    _sessionEngine.SessionStarted += OnSessionStarted;
                    _sessionEngine.SessionStopped += OnSessionStopped;
                    // Attach the bark system to this session engine (it's MainWindow-owned
                    // and created lazily, so BarkService can't subscribe at its own Start()).
                    App.Bark?.AttachSessionEngine(_sessionEngine);
                }

                // Call StartEngine directly — BtnStart_Click returns early
                // when remote controlled due to its guard check
                if (!_isRunning)
                {
                    App.Logger?.Information("[RemoteControl] Starting main engine for remote session");
                    StartEngine();

                    // Kill overlays that StartEngine activated from saved settings —
                    // the session engine will control them based on session segments
                    App.Overlay?.StopPinkFilter();
                    App.Overlay?.StopSpiral();
                    App.Logger?.Information("[RemoteControl] Cleared overlays — session engine will control them");
                }

                App.IsSessionRunning = true;
                await _sessionEngine.StartSessionAsync(session);
                App.Logger?.Information("[RemoteControl] Session engine started successfully for: {Name}", session.Name);
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to start session from remote: {Name}", session?.Name);
            }
        }

        internal void PauseSessionFromRemote()
        {
            try
            {
                if (_sessionEngine?.IsRunning == true && !_sessionEngine.IsPaused)
                    _sessionEngine.PauseSession();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to pause session from remote");
            }
        }

        internal void ResumeSessionFromRemote()
        {
            try
            {
                if (_sessionEngine?.IsRunning == true && _sessionEngine.IsPaused)
                    _sessionEngine.ResumeSession();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to resume session from remote");
            }
        }

        internal void StopSessionFromRemote() => StopEngineAndSession("RemoteControl");

        /// <summary>Stop the running session + main engine from an external trigger (remote control,
        /// or diving into a Chaos run). Safe to call when nothing is running — it self-guards.</summary>
        internal void StopEngineAndSession(string source)
        {
            try
            {
                App.Logger?.Information("[{Source}] StopEngineAndSession called", source);
                if (_sessionEngine?.IsRunning == true)
                    _sessionEngine.StopSession();

                App.IsSessionRunning = false;

                // Also stop the main engine to reset services and _isRunning state
                if (_isRunning)
                {
                    App.Logger?.Information("[{Source}] Also stopping main engine", source);
                    StopEngine();
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[{Source}] Failed to stop engine/session", source);
            }
        }

        internal void TriggerPanicFromRemote()
        {
            try
            {
                App.Logger?.Information("[RemoteControl] Panic triggered from remote");

                // Track panic press for Relapse achievement
                App.Achievements?.TrackPanicPressed();

                // Kill all audio immediately
                App.KillAllAudio();
                App.Autonomy?.CancelActivePulses();

                if (_sessionEngine != null && _sessionEngine.IsRunning && !_sessionEngine.IsPaused)
                {
                    _sessionEngine.PauseSession();
                }

                // Stop video explicitly (closes all video windows)
                App.Video?.Stop();

                // Stop other active effects
                App.Flash?.Stop();
                App.Subliminal?.Stop();
                App.Bubbles?.Stop();
                App.BouncingText?.Stop();
                App.BubbleCount?.Stop();
                App.MindWipe?.Stop();
                App.BrainDrain?.Stop();
                App.LockCard?.Stop();

                // Turn off overlays but keep the overlay service alive
                // so the controller can turn them back on. Clear the settings flags first so a
                // running reconcile loop won't recreate them, then stop the windows directly:
                // voice/Deeper start spiral & pink ad-hoc (no reconcile loop), so RefreshOverlays()
                // — gated on the service's IsRunning — can't see those windows to tear them down.
                EnablePinkFilter(false);
                EnableSpiral(false);
                App.Overlay?.RefreshOverlays();
                App.Overlay?.StopPinkFilter();
                App.Overlay?.StopSpiral();

                App.InteractionQueue?.ForceReset();

                Show();
                WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
                App.Overlay?.NotifyTopWindowClosed();
                ShowAvatarTube();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "[RemoteControl] Failed to trigger panic from remote");
            }
        }

        internal void MinimizeToTrayForRemote()
        {
            _trayIcon?.MinimizeToTray();
            _trayIcon?.ShowNotification("Remote Control", "Session active — minimized to tray.", System.Windows.Forms.ToolTipIcon.Info);
        }

        /// <summary>Tuck the main window into the tray while a Chaos/DtRH descent owns the screen.
        /// Restored via <see cref="ShowFromTray"/> when the game closes. No notification (the game
        /// window is the focus); does NOT go through the close handler, so the run keeps running.</summary>
        public void MinimizeToTrayForChaos() => _trayIcon?.MinimizeToTray();

        /// <summary>
        /// Alerts the host that a remote controller just joined. Pops a tray
        /// balloon and flashes the taskbar icon if minimized — does NOT restore
        /// the window so the host stays in control of window state.
        /// </summary>
        private void NotifyRemoteControllerJoined()
        {
            // Always show a tray balloon — it's a useful cue even when visible.
            try
            {
                _trayIcon?.ShowNotification(
                    Loc.Get("title_remote_controller_joined"),
                    Loc.Get("msg_remote_controller_joined"),
                    System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Failed to show remote controller tray balloon: {Error}", ex.Message);
            }

            // Flash the taskbar button so the host notices even with notifications off.
            if (this.WindowState == WindowState.Minimized || !this.IsVisible)
            {
                try { Helpers.FlashWindowHelper.Flash(this); } catch { }
            }
        }

        internal void RestoreFromTrayForRemote()
        {
            _trayIcon?.ShowWindow();
        }

        /// <summary>
        /// Called when a second instance signals this instance to show itself.
        /// </summary>
        public void ShowFromTray()
        {
            _trayIcon?.ShowWindow();
        }

        #endregion
    }
}
