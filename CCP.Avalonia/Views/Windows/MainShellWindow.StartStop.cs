// PORTED from ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs: BtnStart_Click (:35),
// BtnStartMenu_Click, MenuStartNormal_Click, StartEngine (:294), StopEngine (:444) and
// UpdateStartButton (:944), for the features Core drives (CoreEngine). The session half of
// BtnStart_Click (stop-session dialog) is ConfirmStopSession (MainShellWindow.SessionRun.cs).
// ponytail: still missing, each with no service on this head: the remote-control gate,
// Relapse/TotalSessions achievements, the bubble/brain-drain/pop-quiz/autonomy/
// ramp starts, the scheduler, the Presets "running" label and the hero FX.
// MenuJumpRightIn_Click / RandomizeAndStart (:132) are ported.

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Views.Overlays;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Bumped by every Stop, so a Start still waiting on the portal bind (up to 30 s) is dropped
        // if the user stopped or panicked meanwhile.
        private static int _engineGen;

        private void BtnStartMenu_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button b) b.ContextMenu?.Open(b);
        }

        private async void BtnStart_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (RemoteControllerConnected) return;   // WPF StartStop.cs:43: a controller drives
            if ((CoreEngine.IsRunning || App.Sessions?.IsRunning == true) && RefuseStopUnderLockdown()) return;
            // WPF MainWindow.StartStop.cs:58: a running session asks first; declining keeps everything on.
            if (App.Sessions?.IsRunning == true)
            {
                Serilog.Log.Information("Start button: asking to stop the running session");
                await ConfirmStopSession("dialog_stop_session_title", "dialog_stop_session_body");
                if (!App.Sessions.IsRunning) { NoteSchedulerManualStop(); StopEngine(); }
                return;
            }
            if (CoreEngine.IsRunning) { Serilog.Log.Information("Start button: stopping the engine"); NoteSchedulerManualStop(); StopEngine(); }
            else { Scheduler.NoteManualStart(); StartEngine(); }   // WPF StartStop.cs:111
        }

        private void MenuJumpRightIn_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => RandomizeAndStart();

        /// <summary>WPF StartStop.cs:142 "Jump right in": a fun, non-overwhelming mix with random
        /// pacing, then Start. Refused whole mid-session (it used to re-roll a prescribed mix).
        /// Setters clamp, so the ranges are always safe.</summary>
        internal void RandomizeAndStart(System.Random? rng = null)
        {
            if (RefuseActionIfSessionLocked("jump-right-in")) return;
            rng ??= new System.Random();
            var s = CoreSettings.Current;
            s.FlashEnabled = true;
            s.FlashFrequency = rng.Next(20, 81);
            s.SimultaneousImages = rng.Next(2, 9);
            s.SubliminalEnabled = true;
            s.SubliminalFrequency = rng.Next(3, 13);
            s.SpiralEnabled = rng.Next(2) == 0;
            s.PinkFilterEnabled = rng.Next(2) == 0;
            CoreSettings.Save();
            if (!CoreEngine.IsRunning) StartEngine();
        }

        private void MenuStartNormal_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!CoreEngine.IsRunning) StartEngine();
        }

        /// <summary>WPF StartEngine, through the portal wrapper so the panic key is bound first.</summary>
        internal void StartEngine()
        {
            var gen = ++_engineGen;
            StartEffect(() =>
            {
                if (gen != _engineGen || CoreEngine.IsRunning) return;
                CoreEngine.Start();
                StartRampIfEnabled();              // WPF StartEngine :398
                PinkRushHost.Start();              // WPF StartEngine :309 App.SkillTree?.Start()
                PinkFilterOverlay.Refresh(this);   // WPF App.Overlay.Start()
                SpiralOverlay.Refresh(this);
                BrainDrainOverlay.Refresh(this);   // the haze follows the engine (WPF App.Overlay.Start / Stop)
                UpdateStartButton();
            });
        }

        /// <summary>WPF StopEngine. Saved flags are left alone; <see cref="OnEngineStopped"/> clears the screen.
        /// A running session is paused first, not ended (panic, tray Stop): WPF MainWindow.xaml.cs:1726.</summary>
        internal static void StopEngine()
        {
            _engineGen++;
            App.Sessions?.Pause();
            CoreEngine.Stop();
        }

        /// <summary><see cref="CoreEngine.StoppedHook"/>: the head half of StopEngineCore.</summary>
        internal void OnEngineStopped()
        {
            Platform.ProgramEngineBridge.RaiseSessionChanged();   // any session end repaints the Programs row
            // WPF StartStop.cs:485: hand back anything a Takeover pulse borrowed before the overlays go;
            // also retires the pulse timers so an old one cannot end the next run's bubbles.
            CancelAutonomyPulses();
            PinkRushHost.Stop();                   // WPF StartStop.cs:505 App.SkillTree?.Stop()
            StopRampTimer();                       // WPF StartStop.cs:537: reset the ramped values
            App.StopDesktopOverlays(final: false);
            PopQuizHost.Instance.CloseAll();   // first: drops a queued quiz before cards close; WPF StartStop.cs:521
            LockCardWindow.ForceCloseAll();
            PinkFilterOverlay.Refresh(this);
            UpdateStartButton();
            // A paused session keeps its labels, lock and pause button (WPF StopEngine leaves them).
            if (App.Sessions?.IsRunning == true) { SetPauseButton(App.Sessions.IsPaused); return; }
            // WPF OnSessionStopped: the session button and the dials come back on every exit path.
            Named<Tabs.PresetsTabView>("PresetsTab")?.SetSessionButtonLabel(null);
            if (Named<Button>("BtnPauseSession") is { } pause) pause.IsVisible = false;
            RefreshSessionFeatureLock();
        }

        /// <summary>WPF UpdateStartButton: red ■ Stop while running, the accent ▶ Start otherwise.</summary>
        internal void UpdateStartButton(bool force = false)
        {
            if (!force && RemoteControllerConnected) return;   // WPF :941 keeps the remote label
            var running = CoreEngine.IsRunning;
            if (Named<Button>("BtnStart") is { } b)
            {
                if (running) b.Bind(BackgroundProperty, new Binding { Source = new SolidColorBrush(Color.FromRgb(255, 107, 107)) });
                else b.Bind(BackgroundProperty, b.GetResourceObservable("AccentGradientBrush"));
            }
            if (Named<TextBlock>("TxtStartIcon") is { } icon) icon.Text = running ? "■" : "▶";
            Named<TextBlock>("TxtStartLabel")?.Bind(TextBlock.TextProperty,
                (Binding)new StrExtension(running ? "label_stop" : "label_start").ProvideValue(null!));
        }
    }
}
