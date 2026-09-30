using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class RemoteControlTabView : UserControl
    {
        /// <summary>True while the seed writes the controls, so no handler mistakes the echo for a
        /// user edit. Starts TRUE, the directory's established pattern (AwarenessTabView,
        /// BambiTakeoverTabView): the .axaml wires IsCheckedChanged itself, so the moment any box
        /// here is given IsChecked="True" in markup the handler fires from inside
        /// InitializeComponent, before the ctor has read settings - and would save the markup
        /// default over the user's file.</summary>
        private bool _isLoading = true;

        public RemoteControlTabView()
        {
            InitializeComponent(); // generated: loads the XAML AND fills the x:Name fields

            // The emote slots are AppSettings.RemoteEmotePresets, which is in Core - the same five
            // EmotePreset instances the WPF picker binds (MainWindow.Settings.cs:93). OnDeserialized
            // has already padded/truncated to exactly 5, so the ItemsControl never sees an odd count,
            // and an unseeded head gets DefaultRemoteEmotePresets().
            var s = CoreSettings.Current;
            LstEmotePresets.ItemsSource = s.RemoteEmotePresets;
            ChkStopEffectsOnRemoteDisconnect.IsChecked = s.StopEffectsOnRemoteDisconnect;
            ChkRemoteShareAvatar.IsChecked = s.RemoteShareAvatar;
            _isLoading = false;

            LstRemoteCommandLog.ItemsSource = _log;
            var r = Relay.Value;
            r.ControllerConnectedChanged += (_, _) => Dispatcher.UIThread.Post(() => UpdateRemoteStatus(r.ControllerConnected));
            r.CommandReceived += (_, a) => Dispatcher.UIThread.Post(() => AppendRemoteCommandLog(a));
            r.SessionEnded += (_, _) => Dispatcher.UIThread.Post(() => ShowSession(null));
            RefreshTierCardHighlight();
        }

        /// <summary>The one relay client (WPF App.RemoteControl). Commands run through Core's gate and table.</summary>
        internal static readonly Lazy<RemoteRelay> Relay = new(() => new RemoteRelay(
            () => CoreSettings.Current.AuthToken, () => CoreAccount.UnifiedUserId, CoreReleaseContent.AppVersion,
            RemoteCommands.Execute, RemoteCommands.StopEffects));

        private readonly ObservableCollection<string> _log = new();

        private static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)),
            Orange = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)), Grey = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
            TierDim = new SolidColorBrush(Color.FromArgb(0x40, 0x5E, 0xD4, 0xE8)), TierActive = new SolidColorBrush(Color.FromRgb(0x5E, 0xD4, 0xE8));

        // A code-set string over a {loc:Str} binding is undone on the next language change; bind the key instead.
        private static void BindKey(TextBlock t, string key) =>
            t.Bind(TextBlock.TextProperty, new Binding($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });

        private void SetChecked(bool on) { _isLoading = true; ChkRemoteControlEnabled.IsChecked = on; _isLoading = false; }

        private string SelectedTier => CmbRemoteTier.SelectedIndex switch { 1 => "standard", 2 => "full", _ => "light" };

        /// <summary>WPF ShowRemoteControlWaiver, minus "Disable panic button": on this head a controller cannot
        /// (RemoteCommandGate), so the waiver does not ask the subject to agree to it.</summary>
        internal static string Waiver(string tier)
        {
            var a = new System.Text.StringBuilder();
            a.AppendLine("  - Trigger flash images (from YOUR image folder)");
            a.AppendLine("  - Trigger subliminal messages (from YOUR subliminal pool)");
            a.AppendLine("  - Toggle overlays (pink filter, spiral)");
            a.AppendLine("  - Start/stop bubbles");
            if (tier is "standard" or "full")
            {
                a.AppendLine("  - Trigger mandatory videos (from YOUR video folder)");
                a.AppendLine("  - Trigger haptic device patterns");
                a.AppendLine("  - Duck/unduck audio");
            }
            if (tier == "full")
            {
                a.AppendLine("  - Start/stop autonomy mode");
                a.AppendLine("  - Start/pause/stop sessions");
                a.AppendLine("  - Enable strict lock (videos cannot be skipped)");
            }
            return "You are about to allow another person to remotely control parts of your app.\n\n" +
                   $"The Controller will be able to:\n{a}\n" +
                   "All media content shown comes from YOUR local files and settings.\n" +
                   "You assume full responsibility for this interaction.\n" +
                   "You can stop the session at ANY time by clicking \"Stop Session\" or closing the app.\n" +
                   "Your panic key always stays on, and a controller can never override Lockdown.\n" +
                   "The session stays active as long as the app is running. If the app closes without stopping the session, it expires within 4 hours.";
        }

        /// <summary>WPF MainWindow.RemoteControl.cs:35: premium gate, then login, then the double waiver, then start.
        /// Every refusal unticks the toggle first.</summary>
        private async void ChkRemoteControlEnabled_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            if (ChkRemoteControlEnabled.IsChecked != true) { await StopRemoteControl(); return; }
            if (TopLevel.GetTopLevel(this) is not Window owner) { SetChecked(false); return; }

            var gate = TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote");
            if (!gate.Allowed) { SetChecked(false); TierGate.ShowDenied(gate); return; }
            if (string.IsNullOrEmpty(CoreAccount.UnifiedUserId))
            {
                SetChecked(false);
                await MessageDialog.ShowAsync(owner, Loc.Get("title_login_required"), Loc.Get("msg_login_required_remote"));
                return;
            }
            var tier = SelectedTier;
            if (!await WarningDialog.ShowDoubleWarningAsync(owner, "Remote Control", Waiver(tier))) { SetChecked(false); return; }
            await StartAsync(owner, tier);
        }

        private async Task StartAsync(Window owner, string tier)
        {
            var r = Relay.Value;
            var code = await r.StartAsync(tier);
            if (code == null)
            {
                SetChecked(false);
                ShowSession(null);
                // BUG-NV4FF6TPA7: an auth rejection is not a connectivity problem.
                if (r.LastStartFailedAuth) await MessageDialog.ShowAsync(owner, Loc.Get("title_login_required"), Loc.Get("msg_remote_auth_error"));
                else await MessageDialog.ShowAsync(owner, Loc.Get("title_connection_error"), Loc.Get("msg_remote_connection_error"));
                return;
            }
            ShowSession(code);
        }

        /// <summary>The session panels on (a code) or off (null): WPF's enable path and StopRemoteControl/OnRemoteSessionEnded.</summary>
        private void ShowSession(string? code)
        {
            var on = code != null;
            if (!on) SetChecked(false);
            TxtRemoteCode.Text = on ? string.Join(" ", code!.ToCharArray()) : "- - - - - - - -";
            var pin = on ? Relay.Value.ConnectPin : null;
            TxtRemotePin.Text = string.IsNullOrEmpty(pin) ? "" : $"PIN: {pin}";
            TxtRemotePin.IsVisible = !string.IsNullOrEmpty(pin);
            RemoteControlPanel.IsVisible = RemoteLinkPanel.IsVisible = RemoteCodePanel.IsVisible = BtnStopRemote.IsVisible = on;
            // SP5: the opt-in section stays visible but greyed while a session runs.
            OptInSectionPanel.IsEnabled = !on;
            OptInSectionPanel.Opacity = on ? 0.5 : 1.0;
            if (!on) { ChkOptIntoDirectory.IsChecked = false; OptInFormPanel.IsVisible = false; _log.Clear(); }
            UpdateRemoteStatus(false, idle: !on);
            ImgRemoteQrCode.Source = on ? QrCode(RemoteRelay.PairingUrl(code!, pin)) : null;
        }

        /// <summary>WPF RefreshRemoteQrCode: ECC M, 10 px modules, the mod's dark accent on white.</summary>
        internal static global::Avalonia.Media.Imaging.Bitmap? QrCode(string url)
        {
            try
            {
                var fg = Color.Parse("#FF1493");
                try { if (App.Mods?.GetAccentDarkColorHex() is { Length: > 0 } hex) fg = Color.Parse(hex); } catch { }
                using var gen = new QRCoder.QRCodeGenerator();
                using var data = gen.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
                var png = new QRCoder.PngByteQRCode(data).GetGraphic(10, new[] { fg.R, fg.G, fg.B }, new byte[] { 0xFF, 0xFF, 0xFF });
                return new global::Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(png));
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to render remote QR code"); return null; }
        }

        /// <summary>WPF UpdateRemoteStatus; <paramref name="idle"/> is the no-session line the markup starts with.</summary>
        private void UpdateRemoteStatus(bool connected, bool idle = false)
        {
            RemoteStatusDot.Fill = idle ? Grey : connected ? Green : Orange;
            BindKey(TxtRemoteStatus, idle ? "label_remote_idle" : connected ? "label_controller_connected" : "label_waiting_for_controller");
            TxtRemoteStatus.Foreground = connected ? Green : Grey;
        }

        /// <summary>WPF AppendRemoteCommandLog: newest first, 50 entries, quiet verbs skipped.</summary>
        private void AppendRemoteCommandLog(string action)
        {
            if (RemoteCommands.Quiet.Contains(action)) return;
            var label = RemoteCommands.LabelKeys.TryGetValue(action, out var k) ? Loc.Get(k) : action.Replace("_", " ");
            _log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {label}");
            while (_log.Count > 50) _log.RemoveAt(_log.Count - 1);
        }

        private async Task StopRemoteControl()
        {
            await Relay.Value.StopAsync();
            ShowSession(null);
        }

        private async void BtnStopRemote_Click(object? sender, RoutedEventArgs e) => await StopRemoteControl();

        /// <summary>WPF CmbRemoteTier_SelectionChanged: a live session restarts on the new tier after a fresh waiver.</summary>
        private async void CmbRemoteTier_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!IsInitialized) return;
            RefreshTierCardHighlight();
            var r = Relay.Value;
            if (_isLoading || !r.IsActive || SelectedTier == r.Tier || TopLevel.GetTopLevel(this) is not Window owner) return;
            if (!await WarningDialog.ShowDoubleWarningAsync(owner, "Remote Control", Waiver(SelectedTier))) return;
            await r.StopAsync();
            SetChecked(true);
            await StartAsync(owner, SelectedTier);
        }

        private void TierCard_Click(object? sender, PointerReleasedEventArgs e)
        {
            if (sender is Control { Tag: string t } && int.TryParse(t, out var idx) && CmbRemoteTier.SelectedIndex != idx)
                CmbRemoteTier.SelectedIndex = idx;
        }

        private void RefreshTierCardHighlight()
        {
            foreach (var (card, i) in new[] { (TierCardLight, 0), (TierCardStandard, 1), (TierCardFull, 2) })
                card.BorderBrush = CmbRemoteTier.SelectedIndex == i ? TierActive : TierDim;
        }

        private async void BtnCopyRemoteCode_Click(object? sender, RoutedEventArgs e)
        {
            var r = Relay.Value;
            if (string.IsNullOrEmpty(r.SessionCode)) return;
            await Copy(BtnCopyRemoteCode, string.IsNullOrEmpty(r.ConnectPin) ? r.SessionCode : $"{r.SessionCode} (PIN: {r.ConnectPin})", "btn_copy");
        }

        private async void BtnCopyRemoteLink_Click(object? sender, RoutedEventArgs e)
        {
            var r = Relay.Value;
            await Copy(BtnCopyRemoteLink, string.IsNullOrEmpty(r.SessionCode) ? "https://cclabs.app/remote/" : RemoteRelay.PairingUrl(r.SessionCode, r.ConnectPin), "btn_copy_link");
        }

        /// <summary>WPF: "Copied!" (or "Failed") for 2 s, then the button's own label.</summary>
        private async Task Copy(Button b, string text, string restoreKey)
        {
            var ok = false;
            try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } c) { await c.SetTextAsync(text); ok = true; } }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to copy remote text to clipboard"); }
            if (b.Content is not TextBlock t) return;
            BindKey(t, ok ? "btn_copied" : "label_failed");
            await Task.Delay(2000);
            BindKey(t, restoreKey);
        }

        // Pure settings round-trip, as MainWindow.RemoteControl.cs:297 does it.
        private void ChkStopEffectsOnRemoteDisconnect_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var want = ChkStopEffectsOnRemoteDisconnect.IsChecked ?? false;
            if (CoreSettings.Current.StopEffectsOnRemoteDisconnect == want) return;
            CoreSettings.Current.StopEffectsOnRemoteDisconnect = want;
            CoreSettings.Save();
        }

        // Settings half of MainWindow.RemoteControl.cs:309. The other half pushes the new value to a
        // LIVE controller within one poll instead of ~15s; that needs App.RemoteControl
        // (ConditioningControlPanel/Services/RemoteControlService.cs), and no session can exist on
        // this head, so the privacy setting is stored honestly and no push is being skipped.
        private void ChkRemoteShareAvatar_Changed(object? sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var want = ChkRemoteShareAvatar.IsChecked ?? false;
            if (CoreSettings.Current.RemoteShareAvatar == want) return;
            CoreSettings.Current.RemoteShareAvatar = want;
            CoreSettings.Save();
        }

        // View half of MainWindow.RemoteControl.cs:647 - reveal the opt-in form, then pre-populate.
        private void ChkOptIntoDirectory_Changed(object? sender, RoutedEventArgs e)
        {
            var checkedNow = ChkOptIntoDirectory.IsChecked == true;
            OptInFormPanel.IsVisible = checkedNow;
            if (checkedNow) PopulateOptInFormFromSavedSettings();
        }

        // Mirrors MainWindow.RemoteControl.cs:660. SavedDirectoryTags is on AppSettings, in Core.
        private void PopulateOptInFormFromSavedSettings()
        {
            var saved = CoreSettings.Current.SavedDirectoryTags;
            if (saved == null) return;
            foreach (var cb in new[]
            {
                ChkTagBimbo, ChkTagDrone, ChkTagTrance, ChkTagFeminization, ChkTagSubmission,
                ChkTagDegradation, ChkTagAudioOk, ChkTagSoftOnly, ChkTagLockdownOk, ChkTagChastity,
            })
            {
                cb.IsChecked = cb.Tag is string tag && saved.Contains(tag);
            }
        }

        // ponytail: emotes (Core RemoteRelay has no /v2/remote/emote yet) and the directory opt-in chain
        // (/v2/directory/opt-in) are not ported; the opt-in form stays a local form that publishes nothing.
        private void BtnEditEmoteCancel_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEditEmoteSave_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmoteCustomSend_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmoteEdit_Click(object? sender, RoutedEventArgs e) { }
        private void BtnEmotePreset_Click(object? sender, RoutedEventArgs e) { }
        private void BtnGateUnlock_Click(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.BtnGateUnlock_Click(sender, e);
        private void ChkOptInTag_Click(object? sender, RoutedEventArgs e) { }
        private void TxtEditEmoteText_TextChanged(object? sender, TextChangedEventArgs e) { }
        private void TxtEmoteCustom_KeyDown(object? sender, KeyEventArgs e) { }
        private void TxtOptInStatus_TextChanged(object? sender, TextChangedEventArgs e) { }
    }
}
