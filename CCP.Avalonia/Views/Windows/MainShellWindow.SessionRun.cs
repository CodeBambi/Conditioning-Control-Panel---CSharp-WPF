// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Presets.cs: BtnStartSession_Click (:1560),
// StartSession (:1586), OnSessionLogReady + ShowSessionSummaryWhenClear (:1687, :1743),
// OnSessionProgressUpdated (:1838), OnSessionStarted (:1891), OnSessionStopped (:1936) and
// BtnStopSession_Click (:1983); plus the stop-session branch of BtnStart_Click (MainWindow.StartStop.cs:58).
// The head owns one Core SessionRunner (App.Sessions), which runs only the ported subset - flash,
// subliminal, bouncing text, lock cards (docs/avalonia-decisions.md).
// ponytail: dropped, each with no service on this head: corner-GIF options, pause button + penalty (U4),
// lockdown/remote gates, program/punch-card/Bark/profile-sync hooks, the takeaway-shelf refresh, the
// Withdraw summary suppression and the video-teardown wait before the recap (no video service yet).
// ponytail: both confirms use MessageDialog, so the buttons read OK/Cancel rather than WPF's
// "▶ Start Session"/"Not yet" and "Yes, stop"/"Keep going"; the text above them is WPF's.

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF BtnStartSession_Click: Stop while a session runs, else confirm and start.</summary>
        internal async void BtnStartSession_Click(Session? selected)
        {
            if (App.Sessions is not { } runner) return;
            if (runner.IsRunning) { await ConfirmStopSession("title_stop_session_confirm", "msg_stop_session_body"); return; }
            if (selected is null || !selected.IsAvailable) return;

            // WPF's text here is not localised (MainWindow.Presets.cs:1572); kept verbatim.
            var confirmed = await MessageDialog.ConfirmAsync(this, $"🌅 Start {selected.Name}?",
                $"Duration: {selected.DurationMinutes} minutes\n\n" +
                "Your current settings will be temporarily replaced.\n" +
                "They will be restored when the session ends." +
                "\n\nReady to begin?");
            if (confirmed) StartSession(selected);
        }

        /// <summary>WPF StartSession: the engine start (through the portal wrapper, so the panic key is
        /// bound first) and the runner start in one step - SessionRunner.Start starts CoreEngine itself.</summary>
        internal void StartSession(Session session)
        {
            var gen = ++_engineGen;
            StartEffect(() =>
            {
                if (gen != _engineGen || App.Sessions is not { IsRunning: false } runner) return;
                try
                {
                    runner.Start(session);
                    PinkFilterOverlay.Refresh(this);
                    Log.Information("Started session: {Name} ({Difficulty}, +{XP} XP)", session.Name, session.Difficulty, session.BonusXP);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to start session");
                    _ = MessageDialog.ShowAsync(this, Loc.Get("title_error"), Loc.GetF("msg_failed_to_start_session", ex.Message));
                }
                OnSessionTick();   // WPF OnSessionStarted: labels + the feature lock at once, not a second later
            });
        }

        /// <summary>WPF BtnStopSession_Click / the BtnStart_Click session branch: same body, own keys.</summary>
        internal async Task ConfirmStopSession(string titleKey, string bodyKey)
        {
            if (App.Sessions is not { IsRunning: true, CurrentSession: { } session } runner) return;
            var elapsed = runner.Elapsed;
            var remaining = runner.Remaining;
            var potentialXP = (int)Math.Round(session.BonusXP * SessionXp.Multiplier(CoreSettings.Current.PlayerLevel));
            var confirmed = await MessageDialog.ConfirmAsync(this, Loc.Get(titleKey),
                Loc.GetF(bodyKey, session.Icon ?? "", session.Name ?? "",
                    $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}",
                    $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}",
                    potentialXP, ""));   // penalty text: no pauses before U4
            if (confirmed && ReferenceEquals(runner.CurrentSession, session)) runner.Stop(completed: false);
        }

        /// <summary>WPF OnSessionProgressUpdated (+ OnSessionStarted's red button): the lock heartbeat
        /// and both clock labels. Runs on the UI thread (the runner ticks through CoreDispatch).</summary>
        internal void OnSessionTick()
        {
            RefreshSessionFeatureLock(force: false);
            if (App.Sessions is not { IsRunning: true, CurrentSession: { } session } runner) return;
            var remaining = runner.Remaining;
            var showCountdown = CoreSettings.Current.ShowSessionCountdown != false;
            Named<Tabs.PresetsTabView>("PresetsTab")?.SetSessionButtonLabel(SessionClockLabel.StopButton(
                remaining, showCountdown, Loc.Get("btn_stop_session_0_1"), Loc.Get("btn_stop_session_2")));

            var mName = session.GetModeAwareName();
            var nameMax = showCountdown ? 14 : 22;
            var name = mName.Length > nameMax ? mName.Substring(0, nameMax - 3) + "..." : mName;
            if (Named<Button>("BtnStart") is { } b)
                b.Bind(BackgroundProperty, new Binding { Source = new SolidColorBrush(Color.FromRgb(220, 53, 69)) });
            if (Named<TextBlock>("TxtStartIcon") is { } icon) icon.Text = "⏹";
            Named<TextBlock>("TxtStartLabel")?.Bind(TextBlock.TextProperty, new Binding
            {
                Source = SessionClockLabel.StartButton(name, remaining, showCountdown, "", Loc.Get("label_0_1_2_3"))
            });
        }

        /// <summary>WPF OnSessionLogReady + ShowSessionSummaryWhenClear: the recap opens by itself,
        /// completed or ended early. Raised on the runner's thread; hops to the UI thread.</summary>
        internal void OnSessionLogReady(object? sender, SessionLogReadyEventArgs e)
        {
            var log = e.Log;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var dialog = new SessionCompleteWindow(log);
                    if (IsVisible) _ = dialog.ShowDialog(this);
                    else dialog.Show();   // shell in the tray: an owned modal needs a visible owner
                }
                catch (Exception ex) { Log.Error(ex, "Failed to show post-session log dialog"); }
            }, DispatcherPriority.Background);
        }
    }
}
