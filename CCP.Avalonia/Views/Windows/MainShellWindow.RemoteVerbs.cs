// The head half of the Remote Control verbs Core cannot run by itself (Core RemoteCommands.IRemoteHead).
// PORTED from ConditioningControlPanel/Services/RemoteControlService.cs ExecuteCommand (the overlay verbs
// with EnsureOverlayRunning, Melt, trigger_lock_card, autonomy, the four session verbs) and
// MainWindow/MainWindow.RemoteControl.cs StartSessionFromRemote / Pause / Resume / StopEngineAndSession.
// Every member runs on the UI thread (RemoteRelay dispatches).
// play_hypnotube is in MainShellWindow.RemoteVideoLink.cs. not ported: Easy scaling of opacity (no
// RemoteHud), the online wallpaper pool (WallpaperService), start_session's strict_lock flag (dropped on purpose:
// no new path by which a remote participant switches Strict Lock on).

using System;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow : RemoteCommands.IRemoteHead
    {
        internal const string NoHazeHere = "no screen haze on this system";

        // Held by reference (never by id), as WPF _remoteStartedSession: it describes THIS run.
        private Session? _remoteStartedSession;

        bool RemoteCommands.IRemoteHead.RefreshOverlay(string which)
        {
            if (which == "pink")
            {
                PinkFilterOverlay.Refresh(this);
                return PinkFilterOverlay.IsShowing;
            }
            SpiralOverlay.Refresh(this);
            return SpiralOverlay.IsShowing || SpiralOverlay.Decoding is { IsCompleted: false };
        }

        string? RemoteCommands.IRemoteHead.RefreshBrainDrain()
        {
            BrainDrainOverlay.Refresh(this);
            return BrainDrainOverlay.IsSupported ? null : NoHazeHere;
        }

        /// <summary>WPF App.LockCard.ShowLockCard(): the next phrase from the subject's own list, with the
        /// subject's own repeats / strict / voice settings. Never stacks.</summary>
        string? RemoteCommands.IRemoteHead.ShowLockCard()
        {
            if (LockCardWindow.IsAnyOpen()) return "a lock card is already up";
            if (LockCardScheduler.EnabledPhrases().Count == 0) return "no phrases";
            LockCardWindow.ShowNext(isTest: false);
            return LockCardWindow.IsAnyOpen() ? null : "could not show a card";
        }

        void RemoteCommands.IRemoteHead.CloseCards()
        {
            LockCardWindow.ForceCloseAll();
            BubbleCountWindow.ForceCloseAll();
        }

        /// <summary>WPF App.Autonomy.Start() / Stop(): the scheduler's own gate decides (the subject's switch,
        /// consent and access); the saved switch is never written from here.</summary>
        string? RemoteCommands.IRemoteHead.SetAutonomy(bool on)
        {
            if (!on)
            {
                Autonomy.Stop();
                UpdateAutonomyButtonState(false);
                return null;
            }
            var started = Autonomy.Start();
            UpdateAutonomyButtonState(Autonomy.IsEnabled);
            return started ? null : "Takeover is off on this side";
        }

        void RemoteCommands.IRemoteHead.CancelAutonomyPulses(bool restart)
        {
            CancelAutonomyPulses();
            if (!restart) return;
            // WPF StopAllRemoteEffects: Stop, then re-arm when the subject's own switch is on (#299).
            Autonomy.Stop();
            if (CoreSettings.Current.AutonomyModeEnabled) Autonomy.Start();
            UpdateAutonomyButtonState(Autonomy.IsEnabled);
        }

        bool RemoteCommands.IRemoteHead.SessionIsRemoteStarted => IsSessionRemoteStarted;

        internal const string NoWallpaperHere = "no wallpaper control on this system";
        internal const string NoWallpapers = "no wallpapers in the folder";

        string? RemoteCommands.IRemoteHead.Wallpaper(bool on) => RemoteWallpaper(on);

        /// <summary>
        /// WPF trigger_wallpaper (App.Wallpaper Shuffle / Activate) and stop_wallpaper (Deactivate).
        /// The picture always comes from the subject's own wallpaper folder (WallpaperService.SourceFolder);
        /// a controller never sends one. Off also runs on every remote stop path (the controller's stop,
        /// session end, the controller leaving, panic), and puts the subject's own desktop back.
        /// </summary>
        internal static string? RemoteWallpaper(bool on)
        {
            if (!on)
            {
                Platform.WallpaperHead.Restore();
                return null;
            }
            if (!Platform.WallpaperHead.Supported) return NoWallpaperHere;
            return Platform.WallpaperHead.Service.Shuffle() ? null : NoWallpapers;
        }

        /// <summary>WPF IsSessionRemoteStarted.</summary>
        internal bool IsSessionRemoteStarted =>
            _remoteStartedSession != null && App.Sessions is { IsRunning: true } r && ReferenceEquals(r.CurrentSession, _remoteStartedSession);

        /// <summary>The rule's second lock: true when a start_session still names strict_lock here.</summary>
        internal static bool RemoteStartAsksStrictLock(JObject? p) => RemoteCommandGate.DropsStrictLockFlag("start_session", p);

        string? RemoteCommands.IRemoteHead.Session(string verb, JObject? p)
        {
            if (App.Sessions is not { } runner) return RemoteCommands.NotOnThisBuild;
            switch (verb)
            {
                case "start_session":
                    var session = RemoteSessionFor(p?["session_id"]?.ToString());
                    // Owner, 2026-10-10: a remote session start never turns Strict Lock on, for any controller
                    // on any tier. RemoteCommandGate removes the flag before this runs; StartSession takes no
                    // strict argument at all, so a flag that slipped through still does nothing here.
                    if (RemoteStartAsksStrictLock(p))
                        Log.Warning("[RemoteControl] start_session reached the head with strict_lock; ignored");
                    if (runner.IsRunning) runner.Stop(completed: false);   // WPF: stop the running one first
                    _remoteStartedSession = session;                       // before the start, as WPF
                    StartSession(session);
                    return null;
                case "pause_session":
                    if (!runner.IsRunning || runner.IsPaused) return null;   // WPF: silent no-op
                    runner.Pause();
                    RefreshRemoteSessionChrome(paused: true);
                    return null;
                case "resume_session":
                    if (!runner.IsRunning || !runner.IsPaused) return null;
                    StartEffect(() =>   // effects come back, so the portal panic bind comes first
                    {
                        if (App.Sessions is not { IsPaused: true } r) return;
                        r.Resume();
                        RefreshRemoteSessionChrome(paused: false);
                    });
                    return null;
                case "stop_session":
                    // WPF StopEngineAndSession: the session ends (not completed) and the main engine stops.
                    if (runner.IsRunning) runner.Stop(completed: false);
                    _remoteStartedSession = null;
                    if (CoreEngine.IsRunning) StopEngine();
                    return null;
                default: return RemoteCommands.NotOnThisBuild;
            }
        }

        private void RefreshRemoteSessionChrome(bool paused)
        {
            PinkFilterOverlay.Refresh(this);
            SpiralOverlay.Refresh(this);
            BrainDrainOverlay.Refresh(this);
            SetPauseButton(paused);
            OnSessionTick();
        }

        /// <summary>WPF start_session: the session with that id from the subject's own list, else a generic
        /// 30-minute run that keeps the subject's current settings.</summary>
        internal static Session RemoteSessionFor(string? id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                try
                {
                    var found = Session.GetAllSessions().FirstOrDefault(x => x != null && x.IsAvailable && x.Id == id);
                    if (found != null) return found;
                }
                catch (Exception ex) { Log.Warning(ex, "[RemoteControl] session lookup failed"); }
            }
            var cur = CoreSettings.Current;
            return new Session
            {
                Id = "remote_session",
                Name = "Remote Session",
                Icon = "\U0001F3AE",
                DurationMinutes = 30,
                Difficulty = SessionDifficulty.Medium,
                BonusXP = 200,
                Settings = new SessionSettings
                {
                    FlashEnabled = cur.FlashEnabled,
                    FlashPerHour = cur.FlashFrequency,
                    FlashOpacity = cur.FlashOpacity,
                    FlashImages = cur.SimultaneousImages,
                    FlashClickable = cur.FlashClickable,
                    FlashAudioEnabled = cur.FlashAudioEnabled,
                    SubliminalEnabled = cur.SubliminalEnabled,
                    SubliminalPerMin = cur.SubliminalFrequency,
                    SubliminalOpacity = cur.SubliminalOpacity,
                    SubliminalFrames = cur.SubliminalDuration,
                    MandatoryVideosEnabled = cur.MandatoryVideosEnabled,
                    BubblesEnabled = cur.BubblesEnabled,
                },
            };
        }
    }
}
