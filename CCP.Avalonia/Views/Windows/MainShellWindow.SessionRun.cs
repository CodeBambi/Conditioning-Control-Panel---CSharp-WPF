// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Presets.cs: BtnStartSession_Click (:1560),
// StartSession (:1586), OnSessionLogReady + ShowSessionSummaryWhenClear (:1687, :1743),
// OnSessionProgressUpdated (:1838), OnSessionStarted (:1891), OnSessionStopped (:1936) and
// BtnStopSession_Click (:1983); plus the stop-session branch of BtnStart_Click (MainWindow.StartStop.cs:58).
// The head owns one Core SessionRunner (App.Sessions), which runs only the ported subset - flash,
// subliminal, bouncing text, lock cards (docs/avalonia-decisions.md).
// BtnPauseSession_Click (:2021) too.
// ponytail: dropped, each with no service on this head: corner-GIF options, remote gates, program/punch-card/Bark/profile-sync hooks, the takeaway-shelf refresh, the
// Withdraw summary suppression and the video-teardown wait before the recap (no video service yet).
// ponytail: both confirms use MessageDialog, so the buttons read OK/Cancel rather than WPF's
// "▶ Start Session"/"Not yet" and "Yes, stop"/"Keep going"; the text above them is WPF's.

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Localization;
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
                    PinkRushHost.Start();          // WPF: a session starts through StartEngine -> SkillTree.Start()
                    PinkFilterOverlay.Refresh(this);
                    SpiralOverlay.Refresh(this);
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
            if (LockdownActive)   // WPF MainWindow.Presets.cs:1993
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_end_a_se"));
                return;
            }
            var elapsed = runner.Elapsed;
            var remaining = runner.Remaining;
            var potentialXP = (int)Math.Round(session.BonusXP * SessionXp.Multiplier(CoreSettings.Current.PlayerLevel));
            var confirmed = await MessageDialog.ConfirmAsync(this, Loc.Get(titleKey),
                Loc.GetF(bodyKey, session.Icon ?? "", session.Name ?? "",
                    $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}",
                    $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}",
                    potentialXP, runner.PauseCount > 0 ? Loc.GetF("msg_plus_pause_penalty_0", runner.XPPenalty) : ""));
            if (!confirmed || !ReferenceEquals(runner.CurrentSession, session)) return;
            Log.Information("Session stop confirmed by the user");
            runner.Stop(completed: false);
            Platform.AccountSeed.Sync?.Nudge("session end");   // explicit end: the runner no longer counts as running
        }

        /// <summary>WPF BtnPauseSession_Click: resume at once; a pause asks first (it costs 100 XP)
        /// unless SkipPauseXpWarning. ponytail: MessageDialog has no "don't ask again" box, so the opt-out
        /// is only settable from the WPF head. Lockdown refuses the pause, never the resume (LockdownPauseRule).</summary>
        private async void BtnPauseSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (App.Sessions is not { IsRunning: true } runner) return;
            if (ConditioningControlPanel.Services.LockdownPauseRule.RefusesPauseButton(LockdownActive, runner.IsPaused))
            {
                await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_pause_du"));
                return;
            }
            if (runner.IsPaused)
            {
                StartEffect(() =>   // effects come back, so the portal panic bind comes first
                {
                    if (App.Sessions is not { IsPaused: true } r) return;
                    r.Resume();
                    PinkFilterOverlay.Refresh(this);
                    SpiralOverlay.Refresh(this);
                    SetPauseButton(false);
                    OnSessionTick();
                });
                return;
            }
            var confirmed = CoreSettings.Current.SkipPauseXpWarning || await MessageDialog.ConfirmAsync(this,
                Loc.Get("title_pause_session_confirm"),
                Loc.GetF("msg_pause_session_body", runner.XPPenalty, runner.XPPenalty + SessionXp.PausePenalty));
            if (!confirmed || !runner.IsRunning) return;
            runner.Pause();
            PinkFilterOverlay.Refresh(this);   // WPF App.Overlay.Stop()
            SpiralOverlay.Refresh(this);
            SetPauseButton(true);
        }

        /// <summary>WPF TxtPauseIcon + BtnPauseSession.ToolTip: "\u25B6" + resume while paused, else "\u23F8" + the count.</summary>
        internal void SetPauseButton(bool paused)
        {
            if (Named<Button>("BtnPauseSession") is not { } b) return;
            b.IsVisible = true;
            if (Named<TextBlock>("TxtPauseIcon") is { } icon) icon.Text = paused ? "\u25B6" : "\u23F8";
            var count = App.Sessions?.PauseCount ?? 0;
            b.Bind(ToolTip.TipProperty, paused || count == 0
                ? (Binding)new StrExtension(paused ? "tooltip_resume_session" : "tooltip_pause_session_100_xp_penalty_per_pause").ProvideValue(null!)
                : new Binding { Source = Loc.GetF("tooltip_pause_session_100_xp_penalty_per_pause_npause", count) });
        }

        /// <summary>WPF OnSessionProgressUpdated (+ OnSessionStarted's red button): the lock heartbeat
        /// and both clock labels. Runs on the UI thread (the runner ticks through CoreDispatch).</summary>
        internal void OnSessionTick()
        {
            RefreshSessionFeatureLock(force: false);
            if (App.Sessions is not { IsRunning: true, CurrentSession: { } session } runner) return;
            PinkFilterOverlay.Refresh(this);   // the delayed start and the ramp (SessionRunner.PinkOpacity)
            if (Named<Button>("BtnPauseSession") is { IsVisible: false }) SetPauseButton(false);   // WPF OnSessionStarted
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
        private SessionCompleteWindow? _liveSessionRecap;

        internal void OnSessionLogReady(object? sender, SessionLogReadyEventArgs e)
        {
            var log = e.Log;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var dialog = new SessionCompleteWindow(log);
                    // WPF MainWindow.Presets.cs:1790 (#1303): under a lock card, pop quiz or Bubble
                    // Count cover the recap opens passive - non-modal, not activated - and only one.
                    // ponytail: videoUp is false until the head has a VideoService.IsPlaying.
                    var passive = SessionSummaryPresentation.Decide(false, LockCardWindow.IsAnyOpen(),
                        PopQuizWindow.IsAnyOpen(), BubbleCountWindow.IsAnyOpen()) == SessionSummaryPresentation.Mode.Passive;
                    dialog.Topmost = !passive;   // WPF MainWindow.Presets.cs:1799
                    if (passive)
                    {
                        _liveSessionRecap?.Close();
                        _liveSessionRecap = dialog;
                        dialog.Closed += (_, _) => { if (ReferenceEquals(_liveSessionRecap, dialog)) _liveSessionRecap = null; };
                        dialog.ShowActivated = false;
                        if (IsVisible) dialog.Show(this); else dialog.Show();
                    }
                    else if (IsVisible) _ = dialog.ShowDialogSafe(this);
                    else dialog.Show();   // shell in the tray: an owned modal needs a visible owner
                }
                catch (Exception ex) { Log.Error(ex, "Failed to show post-session log dialog"); }
            }, DispatcherPriority.Background);
        }
    }
}
