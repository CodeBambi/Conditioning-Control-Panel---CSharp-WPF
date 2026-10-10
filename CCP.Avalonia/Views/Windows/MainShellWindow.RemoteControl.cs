// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs.
// The client is Core RemoteRelay; the tab (Views/Tabs/RemoteControlTabView.axaml.cs) owns toggle, waiver,
// QR, status, command log and the emote picker. This partial is the SHELL half: the full-window
// "someone is controlling you" overlay (fade in/out, session code + PIN, idle subtitle, session info card,
// big emote picker, End Session), the 2 s command toast, the controller-joined notice and the Start
// button lock while a controller drives.
// ponytail: still missing here - remote-driven session verbs (StartSessionFromRemote & co; Core
// RemoteCommands refuses them "not on this build"), the directory opt-in chain, tray minimise/restore
// for remote, the taskbar flash on join (no Avalonia API), the RemoteHud pill and the browser
// blindfold (no embedded browser under the overlay on this head).

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
            Closed += (_, _) =>
            {
                r.ControllerConnectedChanged -= connected;
                r.ControllerIdleChanged -= idle;
                r.SessionEnded -= ended;
                r.CommandReceived -= command;
                _remoteOverlayTimer?.Stop();
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
            if (connected) { ShowRemoteControlOverlay(); NotifyRemoteControllerJoined(); }
            else HideRemoteControlOverlay();
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
            if (Named<Border>("RemoteControlOverlay") is { } o) { o.IsVisible = true; o.Opacity = 1; }
            _remoteOverlayHidingAt = null;
            _remoteOverlayTimer ??= new DispatcherTimer(RemoteOverlaySlowTick, DispatcherPriority.Background, (_, _) => RemoteOverlayTick());
            _remoteOverlayTimer.Start();
            RemoteOverlayTick();
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
        private static void NotifyRemoteControllerJoined()
        {
            try { Platform.OsNotifications.Show(Loc.Get("title_remote_controller_joined"), Loc.Get("msg_remote_controller_joined")); }
            catch (Exception ex) { Serilog.Log.Debug("Remote controller notice failed: {Error}", ex.Message); }
        }

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
