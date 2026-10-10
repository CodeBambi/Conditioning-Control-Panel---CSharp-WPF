// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs.
// The client is Core RemoteRelay; the tab (Views/Tabs/RemoteControlTabView.axaml.cs) owns toggle, waiver,
// QR, status, command log and the emote picker. This partial is the SHELL half: the full-window
// "someone is controlling you" overlay (fade in/out, session code + PIN, idle subtitle, session info card,
// big emote picker, End Session), the 2 s command toast, the controller-joined notice and the Start
// button lock while a controller drives.
// The verbs that need a window (overlays, Melt, lock card, Takeover, session verbs) are in
// MainShellWindow.RemoteVerbs.cs. The tray restore on a remote stop and the taskbar flash on join
// (Platform/TaskbarFlash.cs, Windows) are below; WPF's MinimizeToTrayForRemote has no caller there and
// is not ported. The browser blindfold (WPF :903-973) is here too; its play_hypnotube caller is not. The RemoteHud pill is in
// MainShellWindow.RemoteHud.cs + RemoteHudWindow.cs.

using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The Play door's Available Subjects entry. One ShowTab, as in WPF. The roster
        /// on that page is still filled by App.AvailableSubjects, so this opens an empty tab.</summary>
        private void BtnAvailableSubjects_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("availablesubjects");

        /// <summary>Clock for the toast hold and the fade-out collapse; tests step it.</summary>
        internal static TimeProvider RemoteOverlayTime = TimeProvider.System;
        internal static readonly TimeSpan RemoteToastHold = TimeSpan.FromSeconds(2), RemoteOverlayFadeOut = TimeSpan.FromMilliseconds(200);
        private DispatcherTimer? _remoteOverlayTimer;
        private long? _remoteToastShownAt, _remoteOverlayHidingAt;

        private static bool RemoteControllerConnected => RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected;

        /// <summary>WPF wires these in ChkRemoteControlEnabled_Changed; the relay here is one per process, so
        /// the shell listens from startup and lets go on close.</summary>
        private void InitializeRemoteControlOverlay()
        {
            var r = RemoteControlTabView.Relay.Value;
            EventHandler connected = (_, _) => Dispatcher.UIThread.Post(OnRemoteControllerChanged);
            EventHandler idle = (_, _) => Dispatcher.UIThread.Post(OnRemoteControllerIdleChanged);
            EventHandler ended = (_, _) => Dispatcher.UIThread.Post(OnRemoteSessionEnded);
            EventHandler<string> command = (_, a) => Dispatcher.UIThread.Post(() => OnRemoteCommandReceived(a));
            r.ControllerConnectedChanged += connected;
            r.ControllerIdleChanged += idle;
            r.SessionEnded += ended;
            r.CommandReceived += command;
            RemoteCommands.Head = this;   // the verbs that need a window (MainShellWindow.RemoteVerbs.cs)
            Action<string, bool> feedback = (text, pending) => _avatarTubeWindow?.ShowEmoteFeedback(text, pending);
            RemoteControlTabView.EmoteFeedback = feedback;
            Closed += (_, _) =>
            {
                r.ControllerConnectedChanged -= connected;
                r.ControllerIdleChanged -= idle;
                r.SessionEnded -= ended;
                r.CommandReceived -= command;
                if (ReferenceEquals(RemoteCommands.Head, this)) RemoteCommands.Head = null;
                if (ReferenceEquals(RemoteControlTabView.EmoteFeedback, feedback)) RemoteControlTabView.EmoteFeedback = null;
                _remoteOverlayTimer?.Stop();
                DisposeRemoteHud();
            };
            // WPF fades the overlay in 300 ms and the toast in 200 ms (out: 200 / 300 ms).
            if (Named<Border>("RemoteControlOverlay") is { } o)
                o.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) } };
            if (Named<Border>("RemoteCommandNotification") is { } n)
                n.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(250) } };
        }

        /// <summary>WPF OnRemoteControllerChanged (:784).</summary>
        internal void OnRemoteControllerChanged()
        {
            var connected = RemoteControllerConnected;
            UpdateStartButtonForRemoteControl(connected);
            if (connected) { ShowRemoteControlOverlay(); NotifyRemoteControllerJoined(); EnsureRemoteHud(); }
            else { HideRemoteControlOverlay(); RefreshRemoteHud(); }
        }

        /// <summary>WPF OnRemoteControllerIdleChanged (:818): orange "may be idle", else the grey default.</summary>
        internal void OnRemoteControllerIdleChanged()
        {
            if (Named<TextBlock>("TxtRemoteOverlaySubtitle") is not { } t) return;
            var idle = RemoteControlTabView.Relay.Value.ControllerIdle;
            t.Bind(TextBlock.TextProperty, new Binding($"[{(idle ? "label_controller_may_be_idle" : "label_someone_else_is_controlling_your_app")}]")
            { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            t.Foreground = new SolidColorBrush(idle ? Color.FromRgb(0xFF, 0xA5, 0x00) : Color.FromRgb(0xA0, 0xA0, 0xA0));
        }

        /// <summary>WPF OnRemoteSessionEnded (:832), shell half; the tab unticks itself.</summary>
        internal void OnRemoteSessionEnded()
        {
            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
            RefreshRemoteHud();
        }

        /// <summary>WPF OnRemoteCommandReceived (:1091): quiet verbs make no toast.</summary>
        internal void OnRemoteCommandReceived(string action)
        {
            if (RemoteCommands.Quiet.Contains(action)) return;
            ShowCommandNotification(action);
        }

        /// <summary>WPF ShowRemoteControlOverlay (:891).</summary>
        private void ShowRemoteControlOverlay()
        {
            var r = RemoteControlTabView.Relay.Value;
            var text = string.IsNullOrEmpty(r.SessionCode) ? "" : $"Session: {string.Join(" ", r.SessionCode.ToCharArray())}";
            if (!string.IsNullOrEmpty(r.ConnectPin)) text += $"  PIN: {r.ConnectPin}";
            if (Named<TextBlock>("TxtOverlaySessionCode") is { } code) code.Text = text;
            // The big picker: the first three slots above End Session, the last two below (WPF LoadSettings).
            var presets = CoreSettings.Current.RemoteEmotePresets;
            if (Named<ItemsControl>("LstEmotePresetsBigTop") is { } top) top.ItemsSource = presets.Take(3).ToList();
            if (Named<ItemsControl>("LstEmotePresetsBigBottom") is { } bottom) bottom.ItemsSource = presets.Skip(3).ToList();
            // WPF :903: the embedded browser is a native surface and paints over the overlay (airspace), so it
            // is blindfolded while the card is up. NOT while the controller's own video plays in it
            // (RevealBrowserForRemoteVideo): a reconnect mid-video must not blindfold it again.
            if (!_remoteOverlayBrowserRevealed) SetRemoteBrowserBlindfold(true);
            if (Named<Border>("RemoteControlOverlay") is { } o) { o.IsVisible = true; o.Opacity = 1; }
            _remoteOverlayHidingAt = null;
            _remoteOverlayTimer ??= new DispatcherTimer(RemoteOverlaySlowTick, DispatcherPriority.Background, (_, _) => RemoteOverlayTick());
            _remoteOverlayTimer.Start();
            RemoteOverlayTick();
        }

        // True while a controller-started browser video has lifted the overlay's browser blindfold.
        private bool _remoteOverlayBrowserRevealed;

        /// <summary>The Dashboard's browser container (SettingsTabView), hidden or shown. Null-safe: the
        /// page may not be built yet.</summary>
        private void SetRemoteBrowserBlindfold(bool blind)
        {
            try
            {
                if (Named<SettingsTabView>("SettingsTab")?.BrowserContainer is { } browser) browser.IsVisible = !blind;
            }
            catch (Exception ex) { Serilog.Log.Debug("Remote browser blindfold failed: {Error}", ex.Message); }
        }

        /// <summary>True while the remote overlay holds the browser hidden.</summary>
        internal bool RemoteBrowserBlindfolded =>
            Named<SettingsTabView>("SettingsTab")?.BrowserContainer is { IsVisible: false }
            && Named<Border>("RemoteControlOverlay") is { IsVisible: true };

        /// <summary>WPF RevealBrowserForRemoteVideo (ccp-bugs#1138): the one command that deliberately puts a
        /// video in that browser lifts the blindfold while it plays and puts it back when it stops, if the
        /// controller is still connected. play_hypnotube is not on this head yet; this is its seam.</summary>
        internal void RevealBrowserForRemoteVideo(bool reveal)
        {
            if (_remoteOverlayBrowserRevealed == reveal) return;
            _remoteOverlayBrowserRevealed = reveal;
            if (reveal)
            {
                SetRemoteBrowserBlindfold(false);
                Serilog.Log.Information("[RemoteControl] Browser un-hidden for a controller video (the remote overlay stays behind it)");
            }
            else if (Named<Border>("RemoteControlOverlay") is { IsVisible: true } && _remoteOverlayHidingAt == null)
                SetRemoteBrowserBlindfold(true);   // still connected: the card needs its airspace back
        }

        /// <summary>WPF's 1 s session-info cadence; 100 ms only while a toast or fade-out is pending (P07).</summary>
        internal static readonly TimeSpan RemoteOverlaySlowTick = TimeSpan.FromSeconds(1), RemoteOverlayFastTick = TimeSpan.FromMilliseconds(100);
        internal TimeSpan? RemoteOverlayInterval => _remoteOverlayTimer?.Interval;

        private void SetRemoteOverlayCadence()
        {
            if (_remoteOverlayTimer is not { } timer) return;
            var want = _remoteToastShownAt != null || _remoteOverlayHidingAt != null ? RemoteOverlayFastTick : RemoteOverlaySlowTick;
            if (timer.Interval != want) timer.Interval = want;
        }

        /// <summary>WPF HideRemoteControlOverlay (:915): fade 200 ms, then collapse; the toast stops too.</summary>
        private void HideRemoteControlOverlay()
        {
            if (Named<Border>("RemoteControlOverlay") is not { IsVisible: true } o) return;
            o.Opacity = 0;
            _remoteToastShownAt = null;
            _remoteOverlayHidingAt = RemoteOverlayTime.GetTimestamp();
            SetRemoteOverlayCadence();
        }

        /// <summary>The overlay's one timer (runs only while the overlay is up): WPF's 1 s session info
        /// refresh (:1057), the 2 s toast hold (:1240) and the fade-out collapse.</summary>
        internal void RemoteOverlayTick()
        {
            if (_remoteOverlayHidingAt is { } h && RemoteOverlayTime.GetElapsedTime(h) >= RemoteOverlayFadeOut)
            {
                _remoteOverlayHidingAt = null;
                _remoteOverlayTimer?.Stop();
                if (Named<Border>("RemoteControlOverlay") is { } o) o.IsVisible = false;
                if (Named<Border>("RemoteCommandNotification") is { } n0) n0.Opacity = 0;
                // WPF :923: the browser comes back now that the overlay is gone.
                SetRemoteBrowserBlindfold(false);
                _remoteOverlayBrowserRevealed = false;
                return;
            }
            if (_remoteToastShownAt is { } t && RemoteOverlayTime.GetElapsedTime(t) >= RemoteToastHold)
            {
                _remoteToastShownAt = null;
                if (Named<Border>("RemoteCommandNotification") is { } n) n.Opacity = 0;
            }
            SetRemoteOverlayCadence();
            UpdateRemoteSessionInfo();
        }

        /// <summary>WPF UpdateRemoteSessionInfo (:1057): the idle card, or name / mm:ss of mm:ss (+ PAUSED) / phase.</summary>
        private void UpdateRemoteSessionInfo()
        {
            var running = App.Sessions is { IsRunning: true, CurrentSession: not null };
            if (Named<Control>("RemoteSessionIdle") is { } idle) idle.IsVisible = !running;
            if (Named<Control>("RemoteSessionActive") is { } active) active.IsVisible = running;
            if (!running) return;
            var s = App.Sessions!;
            var session = s.CurrentSession!;
            if (Named<TextBlock>("TxtRemoteSessionName") is { } name) name.Text = $"{session.Icon} {session.GetModeAwareName()}";
            var total = TimeSpan.FromMinutes(session.DurationMinutes);
            if (Named<TextBlock>("TxtRemoteSessionTime") is { } time) time.Text = $"{s.Elapsed:mm\\:ss} / {total:mm\\:ss}{(s.IsPaused ? "  ⏸ PAUSED" : "")}";
            var i = s.CurrentPhaseIndex;
            if (Named<TextBlock>("TxtRemoteSessionPhase") is { } phase)
                phase.Text = session.Phases != null && i >= 0 && i < session.Phases.Count ? session.GetModeAwarePhaseName(session.Phases[i]) : "";
        }

        /// <summary>WPF ShowCommandNotification (:1231): the label for 2 s.</summary>
        private void ShowCommandNotification(string action)
        {
            if (Named<TextBlock>("TxtRemoteCommand") is { } t)
                t.Text = RemoteCommands.LabelKeys.TryGetValue(action, out var k) ? Loc.Get(k) : action.Replace("_", " ");
            if (Named<Border>("RemoteCommandNotification") is { } n) n.Opacity = 1;
            _remoteToastShownAt = RemoteOverlayTime.GetTimestamp();
            SetRemoteOverlayCadence();
        }

        /// <summary>WPF NotifyRemoteControllerJoined (:1481): the tray balloon (here the OS notification).</summary>
        private void NotifyRemoteControllerJoined()
        {
            try { Platform.OsNotifications.Show(Loc.Get("title_remote_controller_joined"), Loc.Get("msg_remote_controller_joined")); }
            catch (Exception ex) { Serilog.Log.Debug("Remote controller notice failed: {Error}", ex.Message); }
            // WPF :1493: flash the taskbar button so the host notices even with notifications off. It does
            // NOT restore the window: the host stays in control of window state.
            if (Platform.TaskbarFlash.ShouldFlash(WindowState == WindowState.Minimized, IsVisible))
            {
                try { Platform.TaskbarFlash.Flash(this); } catch { }
            }
        }

        /// <summary>WPF RestoreFromTrayForRemote (TrayIconService.ShowWindow) + ShowAvatarTube, called by both
        /// remote stop paths: a panel the session left in the tray or minimised comes back. A panel that is
        /// already on screen is left where it is (WPF re-activated it; here a stop never pulls the panel
        /// over the app the player is in).</summary>
        internal void RestoreFromTrayForRemote()
        {
            if (!IsVisible || WindowState == WindowState.Minimized) ShowFromTray();
        }

        void RemoteCommands.IRemoteHead.RestoreWindow() => RestoreFromTrayForRemote();

        /// <summary>WPF UpdateStartButtonForRemoteControl (StartStop.cs:992): disabled, green, 🎮 REMOTE CONNECTED.</summary>
        internal void UpdateStartButtonForRemoteControl(bool connected)
        {
            if (Named<Button>("BtnStart") is not { } b) return;
            b.IsEnabled = !connected;
            if (!connected) { UpdateStartButton(force: true); return; }
            b.Bind(BackgroundProperty, new Binding { Source = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)) });
            if (Named<TextBlock>("TxtStartIcon") is { } icon) icon.Text = "\U0001F3AE";
            Named<TextBlock>("TxtStartLabel")?.Bind(TextBlock.TextProperty,
                (Binding)new Localization.StrExtension("label_remote_connected").ProvideValue(null!));
        }

        /// <summary>WPF BtnEndRemoteSession_Click (:1255): stop the relay; the tab unticks on SessionEnded.</summary>
        private async void BtnEndRemoteSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await RemoteControlTabView.Relay.Value.StopAsync();
            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
        }

        private async void BtnEmotePresetBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button { Tag: Models.EmotePreset p } && !string.IsNullOrWhiteSpace(p.Text))
                await RemoteControlTabView.SendEmoteAndReportAsync(p.Text, p.Icon ?? "", "preset", Named<TextBlock>("TxtEmoteStatusBig"));
        }

        private async void BtnEmoteCustomSendBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => await SendBigCustomEmote();

        private async void TxtEmoteCustomBig_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await SendBigCustomEmote();
        }

        private System.Threading.Tasks.Task SendBigCustomEmote() =>
            Named<TextBox>("TxtEmoteCustomBig") is { } box && Named<TextBlock>("TxtEmoteStatusBig") is { } status
                ? RemoteControlTabView.SendCustomEmoteAsync(box, status) : System.Threading.Tasks.Task.CompletedTask;
    }
}
