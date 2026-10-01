using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
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
    /// The page is served by <see cref="WebAssetServer"/> (WPF's https://ccp.game virtual host) and the
    /// user's asset library under its <see cref="WebAssetServer.AssetsPrefix"/> (WPF's ccp.assets).
    ///
    /// ponytail: not ported yet - heartbeat watchdog/relaunch, fullscreen-set, duck/restore main,
    /// loom-save, intake-save-image, need-remote (remoteMedia=false), the speech bridge, the
    /// bubble sprite / subliminal pool in init, ccp.content packs (no audio ships on this head), the
    /// punch card. Next slices in ~/ccp-port/briefs/intake-plan.md.
    /// </summary>
    internal sealed class IntakeHostWindow : Window
    {
        private static readonly TimeSpan ExitWatchdog = TimeSpan.FromMilliseconds(1200);   // WPF ArmExitWatchdog
        private const int Protocol = 1;                                                     // WPF IntakeHostService.Protocol
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";          // WPF IntakeHostService.ProxyBaseUrl

        internal IntakeRun Run { get; } = new();
        internal WebHost Web { get; } = new();
        internal string SessionsFolder { get; init; } = SessionFileService.CustomSessionsFolder;

        /// <summary>The page this host loaded; messages from any other document are dropped.</summary>
        internal Uri? PageUrl { get; set; }

        public IntakeHostWindow()
        {
            Title = "Graded Intake";   // WPF IntakeHostService.ProductName
            Width = 1280;
            Height = 800;
            Content = Web;
            Web.WebMessage += OnPageMessage;
            // The page never leaves the served origin; anything else is refused before the engine loads it.
            Web.AllowNavigation = url => PageUrl != null && SameOrigin(url, PageUrl);
        }

        /// <summary>WPF StartUrl https://ccp.game/intake/index.html, here on the loopback server.</summary>
        internal void Load(WebAssetServer server)
        {
            PageUrl = new Uri(server.Url("intake/index.html"));
            AssetsBase = $"{PageUrl.GetLeftPart(UriPartial.Authority)}/{WebAssetServer.AssetsPrefix}";
            Web.Navigate(PageUrl);
        }

        /// <summary>Where the media manifest points (WPF https://ccp.assets/).</summary>
        internal string AssetsBase { get; private set; } = "";

        internal static bool SameOrigin(Uri a, Uri b) =>
            a.IsAbsoluteUri && b.IsAbsoluteUri && a.Scheme == b.Scheme && a.Authority == b.Authority;

        /// <summary>WPF ChaosWebViewHost.OnWebMessage's SameDocument guard. The engine's message args
        /// carry no source, so the source is the page the web view last finished navigating to.</summary>
        private void OnPageMessage(string json)
        {
            if (PageUrl == null || Web.CurrentUrl == null || !IntakeRun.SameDocument(Web.CurrentUrl, PageUrl)) return;
            HandleMessage(json);
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
                case "ready":   // WPF ChaosWebViewHost "ready" -> IntakeHostService.OnPageReady
                    Run.Beat();
                    SendInit();
                    break;
                case "log":
                    Log.Debug("IntakeHost page: {Msg}", (string?)o["msg"]);
                    break;
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
                    ReportWalkOutIfUnfinished();
                    Run.Exiting = true;
                    DispatcherTimer.RunOnce(Close, ExitWatchdog);
                    break;
                case "intake-close":   // "are you sure? -> Yes": an abort, nothing is earned
                    ReportWalkOutIfUnfinished();
                    Run.Exiting = true;
                    Close();
                    break;
                case "exit-done":
                    Close();
                    break;
            }
        }

        /// <summary>WPF ReportWalkOutIfUnfinished: QuizService.RaiseQuizAbandoned -> GamificationBridge.</summary>
        private void ReportWalkOutIfUnfinished()
        {
            if (Run.TakeWalkOut()) App.Achievements?.TrackQuizAbandoned();
        }

        /// <summary>WPF IntakeHostService.OnQuizResult: achievements, pass, draft, then the app and the page are told.</summary>
        private void OnQuizResult(JObject o)
        {
            var run = Run.AcceptResult(o);
            if (run == null) return;
            var (session, path) = IntakeRun.Complete(run, SessionsFolder,
                () => App.IntakePass.ConsumeForCompletedIntake(),
                (_, passed, perfect, category) => App.Achievements?.TrackQuizCompleted(passed, perfect, category));
            if (session != null && path != null) Drafted?.Invoke(session, path);
            Post(new { type = "session-drafted", ok = session != null, name = session?.Name, path });
        }

        /// <summary>A session was drafted and saved; the opener refreshes Sessions and toasts it.</summary>
        internal event Action<Session, string>? Drafted;

        /// <summary>WPF IntakeHostService.OnPageReady: web-shim.fromHostInit wants { type:'init', config, ai }.</summary>
        private void SendInit()
        {
            Post(InitMessage());
            Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
        }

        internal object InitMessage()
        {
            var settings = CoreSettings.Current;
            var want = IntakeRun.ResolveNiche(App.Mods?.ActiveModId, App.Mods?.ActiveMod?.Manifest?.Tags,
                settings.ContentMode == ContentMode.SissyHypno);
            // WPF SafeNiche: a niche with no prompt bank serves the default one.
            var niche = File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "web", "intake", "banks", want + ".json"))
                ? want : IntakeRun.FallbackNiche;
            object? media = null;
            try
            {
                var (gifs, images) = IntakeRun.SampleMedia(CorePaths.EffectiveAssets, settings.DisabledAssetPaths);
                if (gifs.Length + images.Length > 0)
                    media = new { gifs = gifs.Select(r => AssetsBase + r).ToArray(), images = images.Select(r => AssetsBase + r).ToArray() };
            }
            catch (Exception ex) { Log.Debug("IntakeHost: media manifest: {E}", ex.Message); }

            // A sandbox never reaches the real AI server: no serverBase -> the page's local stub.
            var ai = SandboxNet.Allows(new Uri(ProxyBaseUrl))
                ? new { serverBase = ProxyBaseUrl, authToken = AccountSeed.Patreon?.GetAccessToken() ?? "" }
                : null;
            Log.Information("IntakeHost: sending init (niche={N})", niche);
            return new
            {
                type = "init",
                protocol = Protocol,
                config = new
                {
                    niche,
                    caps = new { flashRate = 1.0, flashOpacity = 1.0, subDensity = 1.0, duckDepth = 1.0,
                                 bubbleRate = 1.0, binauralDepth = 1.0, bgIntensity = 1.0, masterIntensity = 1.0 },
                    endless = false,
                    steerValve = 1.0,
                    priorRun = (object?)null,
                    m2Test = false,
                    micEnabled = settings.MicConsentGiven,
                    media,
                    remoteMedia = false,
                    subjectId = IntakeRun.SubjectId(CorePaths.UserData),
                    subliminals = (object?)null,
                },
                ai,
            };
        }

        /// <summary>Host -&gt; page (WPF ChaosWebViewHost.Post): the page's string carrier entry point.</summary>
        internal void Post(object message)
        {
            var literal = JsonConvert.SerializeObject(JsonConvert.SerializeObject(message));
            _ = Web.InvokeScriptAsync($"window.__ccpRnPush && window.__ccpRnPush({literal})");
        }
    }
}
