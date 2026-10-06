using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Commands;

namespace ConditioningControlPanel
{
    public partial class App
    {
        /// <summary>
        /// The AI effect commands and their control gate moved to Core (CCP.Core/Services/Commands).
        /// These are the WPF halves they used to call inline, unchanged: each runs on the dispatcher
        /// and reports success exactly as the original command did.
        /// </summary>
        private static void SeedAiEffectSurfaces()
        {
            AiCommandService.LiveActionSink = line =>
            {
                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null) return;

                void Apply()
                {
                    var list = AiLiveActions;
                    list.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
                    while (list.Count > AiCommandService.MaxLiveActions) list.RemoveAt(0);
                }

                if (dispatcher.CheckAccess()) Apply();
                else dispatcher.BeginInvoke((Action)Apply);
            };

            FlashImageCommand.Surface = (amount, durationMs, size) =>
            {
                Current.Dispatcher.Invoke(() => Flash?.TriggerFlashOnce(amount, durationMs, size));
                return true;
            };
            BubbleCommand.Surface = (start, frequency) =>
            {
                Current.Dispatcher.Invoke(() => { if (start) Bubbles?.Start(true, frequency); else Bubbles?.Stop(); });
                return true;
            };
            SubliminalCommand.Surface = (text, opacity) =>
            {
                Current.Dispatcher.Invoke(() => Subliminal?.FlashSubliminalCustom(text, opacity));
                return true;
            };
            MantraLockScreenCommand.Surface = (phrase, repeats) =>
            {
                Current.Dispatcher.Invoke(() => LockCard?.ShowLockCard(phrase, repeats, customStrict: true));
                return true;
            };
            BounceCommand.Surface = (on, words) =>
            {
                Current.Dispatcher.Invoke(() => { if (on) BouncingText?.Start(true, words); else BouncingText?.Stop(); });
                return true;
            };
            SpiralCommand.Surface = (on, intensity) => ApplyOverlay(s => { s.SpiralOpacity = intensity; s.SpiralEnabled = on; });
            PinkCommand.Surface = (on, intensity) => ApplyOverlay(s => { s.PinkFilterOpacity = intensity; s.PinkFilterEnabled = on; });

            MediaCommand.VideoSurface = path => Current.Dispatcher.Invoke(() =>
            {
                if (Video == null) return false;
                if (Video.IsPlaying)
                {
                    if (path == null) Logger?.Information("MediaCommand: random video requested but a video is already playing — skipping");
                    else Logger?.Information("MediaCommand: video already playing — skipping {Path}", path);
                    return false;
                }
                if (path == null) Video.TriggerVideo();
                else Video.PlaySpecificVideo(path, false);
                return true;
            });
            MediaCommand.AudioSurface = path =>
                Task.FromResult(Current.Dispatcher.Invoke(() => Audio?.PlaySound(path, 100) ?? 0) > 0);

            MediaCommand.HypnoTubeSurface = url => Current.Dispatcher.Invoke(() =>
            {
                if (RemoteControl?.ControllerConnected == true)
                {
                    Logger?.Information("MediaCommand: AI HypnoTube video skipped, a remote controller is connected");
                    return false;
                }
                if (Video?.IsPlaying == true || BrowserMedia?.ShouldDeferNewVideo == true)
                {
                    Logger?.Information("MediaCommand: AI HypnoTube video skipped, a video is already playing");
                    return false;
                }

                var mainWindow = MainWindowRef ?? Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWindow == null) return false;

                if (BrowserMedia?.BeginTakeover(Services.Browser.BrowserMediaService.MediaOwner.Autonomy) == false)
                    return false;

                // userInitiated: false - the AI decided this, so an offline block stays silent.
                if (mainWindow.NavigateToUrlInBrowser(url, autoPlayFullscreen: true, userInitiated: false))
                {
                    Logger?.Information("MediaCommand: AI HypnoTube video opened on {Host}", Services.Logging.UrlLog.Host(url));
                    return true;
                }

                BrowserMedia?.OnMediaStopped("navigation-failed");
                Logger?.Warning("MediaCommand: AI HypnoTube video not opened, browser not available");
                return false;
            });

            GetBackToMeCommand.AiProvider = () => Ai;
            GetBackToMeCommand.SaySurface = (text, aiGenerated) =>
            {
                var dispatcher = Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(new Action(() =>
                {
                    try { AvatarWindow?.GigglePriority(text, playSound: true, aiGenerated: aiGenerated); }
                    catch (Exception ex) { Logger?.Debug("GetBackToMeCommand: avatar speak failed: {Error}", ex.Message); }
                }));
            };
        }

        /// <summary>SpiralCommand / PinkCommand's WPF body: write the setting, bring the overlay up
        /// past the level check, refresh and save.</summary>
        private static bool ApplyOverlay(Action<Models.AppSettings> write)
        {
            Current.Dispatcher.Invoke(() =>
            {
                var settings = Settings?.Current;
                if (settings == null || Overlay == null) return;

                write(settings);

                if (!Overlay.IsRunning)
                {
                    Overlay.BypassLevelCheck = true;
                    Overlay.Start();
                }
                else if (!Overlay.BypassLevelCheck)
                {
                    Overlay.BypassLevelCheck = true;
                }

                Overlay.RefreshOverlays();
                Settings?.Save();
            });
            return true;
        }
    }
}
