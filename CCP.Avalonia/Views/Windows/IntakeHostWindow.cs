using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Quiz;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Graded Intake window (WPF Services/Quiz/IntakeHostService.cs). The run's rules are Core
    /// <see cref="IntakeRun"/>; this owns the <see cref="WebHost"/> and the carrier:
    ///   page -&gt; host: <c>window.invokeCSharpAction(json)</c> (engine-injected; web-shim.js treats it
    ///                  as its string carrier) -&gt; <see cref="WebHost.WebMessage"/> -&gt; <see cref="HandleMessage"/>
    ///   host -&gt; page: <c>window.__ccpRnPush(json)</c> through <see cref="WebHost.InvokeScriptAsync"/>.
    ///
    /// ponytail: not opened by Begin Intake yet - nothing serves the page on this head (no ccp.game
    /// virtual host, Assets/web/intake not shipped), so a window would be dead UI. Slice 2
    /// (~/ccp-port/briefs/intake-plan.md, docs/avalonia-decisions.md) serves it and then also ports
    /// the init payload, heartbeat watchdog/relaunch, fullscreen-set, duck/restore, loom-save,
    /// intake-save-image, need-remote and the speech bridge; the QuizCompleted/QuizAbandoned
    /// achievement bridge, the drafted-session toast + Sessions refresh and the punch card follow.
    /// </summary>
    internal sealed class IntakeHostWindow : Window
    {
        private static readonly TimeSpan ExitWatchdog = TimeSpan.FromMilliseconds(1200);   // WPF ArmExitWatchdog

        internal IntakeRun Run { get; } = new();
        internal WebHost Web { get; } = new();
        internal string SessionsFolder { get; init; } = SessionFileService.CustomSessionsFolder;

        public IntakeHostWindow()
        {
            Title = "Graded Intake";   // WPF IntakeHostService.ProductName
            Width = 1280;
            Height = 800;
            Content = Web;
            Web.WebMessage += HandleMessage;
        }

        /// <summary>One page message (WPF IntakeHostService.OnPageMessage). Only a parsed
        /// <c>quiz-result</c> reaches <see cref="IntakeRun.Complete"/>, so only it spends the pass.</summary>
        internal void HandleMessage(string json)
        {
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception ex) { Log.Debug("IntakeHost: unreadable page message: {E}", ex.Message); return; }

            switch ((string?)o["type"])
            {
                case "ready":
                case "heartbeat":
                case "pong":
                    Run.Beat();
                    break;
                case "quiz-result":
                    OnQuizResult(o);
                    break;
                case "boot-error":
                    Log.Warning("IntakeHost: page boot-error: {Msg}", (string?)o["msg"]);
                    Close();
                    break;
                case "exit":   // page-initiated wind-down: its own exit-done, or the watchdog
                    Run.TakeWalkOut();
                    Run.Exiting = true;
                    DispatcherTimer.RunOnce(Close, ExitWatchdog);
                    break;
                case "intake-close":   // "are you sure? -> Yes": an abort, nothing is earned
                    Run.TakeWalkOut();
                    Run.Exiting = true;
                    Close();
                    break;
                case "exit-done":
                    Close();
                    break;
            }
        }

        private void OnQuizResult(JObject o)
        {
            var run = Run.AcceptResult(o);
            if (run == null) return;
            // quizCompleted: null - the achievement bridge (WPF QuizService.RaiseQuizCompleted ->
            // GamificationBridge) is head-side and not on this head yet (see QuizWindow).
            var (session, path) = IntakeRun.Complete(run, SessionsFolder,
                () => App.IntakePass.ConsumeForCompletedIntake(), null);
            Post(new { type = "session-drafted", ok = session != null, name = session?.Name, path });
        }

        /// <summary>Host -&gt; page (WPF ChaosWebViewHost.Post): the page's string carrier entry point.</summary>
        internal void Post(object message)
        {
            var literal = JsonConvert.SerializeObject(JsonConvert.SerializeObject(message));
            _ = Web.InvokeScriptAsync($"window.__ccpRnPush && window.__ccpRnPush({literal})");
        }
    }
}
