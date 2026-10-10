// PORTED from ConditioningControlPanel/Services/Safety/GameSurfaces.cs and MainWindow.xaml.cs
// PanicStopEverySurface (:1923): one list of everything a panic stops, so a new surface is one line here
// instead of a call remembered in each route (P06). The panic key, the tray's Stop everything and the
// spoken safe word all call StopAll; Lockdown refusal, the lock-card/palette/grace-pause rungs, the
// window restore and the double-press exit ladder stay in the routes. Guarded by PanicSurfacesTests.

using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    internal static class PanicSurfaces
    {
        /// <summary>A thing a panic must stop. <paramref name="OwnsTheScreen"/> marks a game-like host:
        /// a press that ends it does not advance the double-press exit ladder (P30, WPF
        /// AnyGameSurfaceOwnsTheScreen). <paramref name="Stop"/> gets the shell the route ran on
        /// (null off the desktop path) and must be idempotent and safe when idle.</summary>
        internal sealed record Surface(string Id, Action<MainShellWindow?> Stop, Func<bool>? OwnsTheScreen = null);

        /// <summary>In stop order; order matters where noted. Add a surface with one line. Tests swap it.</summary>
        internal static IReadOnlyList<Surface> All { get; set; } = new Surface[]
        {
            // Mic first (decisions "Panic ↔ mic"): the capture and command chain in flight end before
            // anything can react to them; the wake loop and push-to-talk stay armed.
            // The intake's say-it loops end before the capture abort below reads as silence; WPF GameSurfaces
            // 'intake' -> CloseActive. A game surface: closing it never arms the exit ladder.
            new("intake", _ => IntakeHostWindow.CloseAllForPanic(), IntakeHostWindow.IsAnyOpen),
            // WPF GameSurfaces backroom/breakout/goon/piecebypiece/dtrh/arcademy: every web game window.
            new("games", _ => Games.GameWindow.CloseAllForPanic(), Games.GameWindow.IsAnyOpen),
            new("friends-landing", _ => Friends.FriendsLanding.CloseAllForPanic()),   // knock cards, corner notices, floating words: down at once, unanswered ones wait in the Inbox
            new("voice-capture", sh => { sh?.CancelVoicePrompt(); sh?.DropVoiceHolds(); }),   // and a spoken spiral or tint
            new("ai-followups", _ => MainShellWindow.CancelPendingAi()),
            // WPF MainWindow.xaml.cs:1726: standalone Lab minigames first; the engine stop never reaches them.
            new("blink-trainer", _ => Overlays.BlinkTrainerSession.Stop()),
            new("gaze-minigame", _ => Lab.GazeMinigame.GazeMinigameWindow.CloseAllForPanic(), Lab.GazeMinigame.GazeMinigameWindow.IsAnyRunning),   // ends before the camera stop below
            new("mantra", _ => MantraWindow.StopForPanic()),                // WPF KillAllAudio -> Mantra?.Dispose()
            new("chaos", _ => Chaos.ChaosRunHost.ForceShutdown(), () => Chaos.ChaosRunHost.IsDescending),
            // WPF PanicStopEverySurface (:1992): the toys go to zero, bypassing throttles and gates.
            new("haptics", _ => CoreHaptics.Service?.PanicStop()),
            new("remote-haptics", _ => RemoteCommands.StopHaptics()),   // decisions 2026-10-08
            new("remote-overlays", sh => { RemoteCommands.PanicDropOverlays(); sh?.StopBrowserVideoFromRemote(); }),   // a controller-held pink filter, spiral or haze never outlives a panic
            // Lockdown's haunt: every possessed control back at once, the edge pulse and its shake gone.
            // The lockdown itself is LockdownPauseRule's business, never this line's.
            new("possession", sh => MainShellWindow.StopPossessionForPanic(sh)),
            new("takeover", sh => sh?.StopAutonomyForPanic()),           // WPF KillAllAudio -> Autonomy.Stop
            // WPF RunPanicStopTail StopEngine/StopAdHocEffects: pauses a running session; OnEngineStopped
            // then ends Takeover pulses, desktop overlays (subliminal/whisper, mind wipe, spiral, video,
            // bubbles, pink filter), pop quizzes and open lock cards.
            // programs 3a (P06, docs/avalonia-decisions.md): today's program session ENDS, not pauses, so
            // a panic never leaves a program run holding the screen; foreign sessions stay paused below.
            new("program-session", sh => { if (sh != null) sh.EndProgramSessionQuietly("panic"); else App.Programs?.StopProgramSessionIfRunning("panic"); }),
            new("engine", _ => MainShellWindow.StopEngine()),
            new("pink-rush", _ => PinkRushHost.Stop()),                 // WPF StopEngine -> SkillTree.Stop: the 3x ends
            // WPF :1967 "corner GIFs": the standalone Spiral-card slots close (settings untouched) and queued ones cancel.
            new("corner-gif", _ => Overlays.CornerGifOverlay.StopAll()),
            // WPF :2005 "tube speech": voice line, bubble, thinking + listening dots (tube#T2).
            new("tube", _ => AvatarTube.AvatarTubeWindow.PanicSilenceLive()),
            new("lock-cards", _ => MainShellWindow.StopLockCards()),      // WPF LockCardService.Stop(dismissOpenCards: true)
            new("attention-test", _ => Overlays.AttentionTestTarget.CloseAll()),   // the style editor's Test target (P06; WPF left it up)
            new("deeper-editor-audio", _ => Deeper.DeeperEditorWindow.PauseAllForPanic()),   // P06; WPF left it playing
            new("deeper-player", _ => Deeper.EnhancementPlayerWindow.StopAllForPanic()),    // media, engine and its effects
            new("camera", _ => MainShellWindow.StopCameraForPanic()),     // decision C: last, fire-and-forget
        };

        /// <summary>WPF AnyGameSurfaceOwnsTheScreen. Sample BEFORE <see cref="StopAll"/>.</summary>
        internal static bool AnyOwnsTheScreen()
        {
            foreach (var s in All)
            {
                try { if (s.OwnsTheScreen?.Invoke() == true) return true; }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: {Id} screen probe failed", s.Id); }
            }
            return false;
        }

        /// <summary>WPF MainWindow.xaml.cs:1589 (App.Chaster?.NoteSafetyExit): the way out never costs. Every
        /// panic route arms Circe's ten-minute hold: the key (first lines of the press, so a press a lock
        /// card or the palette consumes arms it too), and through <see cref="StopAll"/> the tray, the safe
        /// word and the leash gate's Panic; the 6-blink stop calls it itself. Never throws.</summary>
        internal static Action SafetyHold { get; set; } = () => Platform.ChasterHead.Service?.NoteSafetyExit();   // tests swap it

        internal static void ArmSafetyHold()
        {
            ConditioningControlPanel.Services.EmiDesk.EmiDeskBus.Fire("panicPressed");   // WPF MainWindow.xaml.cs:1585: a hold with a five minute silence tail, armed first
            try { SafetyHold(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Panic: Chaster safety hold failed"); }
        }

        /// <summary>Stops every surface in order. One failing stop never skips the rest. Never throws.</summary>
        internal static void StopAll(string reason, MainShellWindow? shell = null)
        {
            Serilog.Log.Information("Panic: stopping every surface ({Reason})", reason);
            ArmSafetyHold();
            shell ??= MainShellWindow.Current;
            foreach (var s in All)
            {
                try { s.Stop(shell); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: {Id} stop failed", s.Id); }
            }
        }
    }
}
