// PORTED from ConditioningControlPanel/Services/Safety/GameSurfaces.cs and MainWindow.xaml.cs
// PanicStopEverySurface (:1923): one list of everything a panic stops, so a new surface is one line here
// instead of a call remembered in each route (P06). The panic key, the tray's Stop everything and the
// spoken safe word all call StopAll; Lockdown refusal, the lock-card/palette/grace-pause rungs, the
// window restore and the double-press exit ladder stay in the routes. Guarded by PanicSurfacesTests.
// Owner, 2026-10-10 ("stop until re-enabled"): every accepted panic route also switches keyword triggers
// off (SwitchOffKeywordTriggers); the user switches them back on in the Awareness tab.

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
            // No "chaos" line: the native Chaos run is retired on this head (owner, 2026-10-10). Chaos is the
            // web descent, a game window, closed by "games" above.
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

        /// <summary>EMI's gif rain has no surface of its own (the cascade belongs to the Back Room's):
        /// every panic route takes HER rain down here. Her spiral hold goes with the stop pass
        /// (SpiralOverlay.ReleaseAllHolds). Tests swap it.</summary>
        internal static Action StopEmiRain { get; set; } = EmiDesk.EmiDeskService.StopRain;

        /// <summary>EMI's own spiral hold (owner "emi") lets go on every panic route too, the ones a lock
        /// card or the palette consumes included (no stop pass runs there). Nobody else's hold is touched.
        /// Tests swap it.</summary>
        internal static Action ReleaseEmiSpiral { get; set; } =
            () => Overlays.SpiralOverlay.Release(EmiDesk.EmiDeskService.EmiOwner, MainShellWindow.Current);

        internal static void ArmSafetyHold()
        {
            try { StopEmiRain(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Panic: EMI rain stop failed"); }
            try { ReleaseEmiSpiral(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Panic: EMI spiral release failed"); }
            ConditioningControlPanel.Services.EmiDesk.EmiDeskBus.Fire("panicPressed");   // WPF MainWindow.xaml.cs:1585: a hold with a five minute silence tail, armed first
            try { App.Achievements?.TrackPanicPressed(); } catch { /* WPF MainWindow.xaml.cs:1849: the relapse window opens */ }
            try { SafetyHold(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Panic: Chaster safety hold failed"); }
        }

        /// <summary>Raised on the UI thread after a panic switched keyword triggers off (the Awareness tab
        /// repaints its switches and its notice).</summary>
        internal static event Action? KeywordTriggersSwitchedOff;

        /// <summary>True from a panic that switched the master off until the user switches it back on.
        /// This run only: the master is a per-session switch (AppSettings.KeywordTriggersEnabled is not
        /// saved), so the next launch starts with it off anyway.</summary>
        internal static bool KeywordMasterOffByPanic { get; set; }

        /// <summary>Test seam: stopping what reads the screen and the keys and what it drew.</summary>
        internal static Action StopKeywordSources { get; set; } = () =>
        {
            Platform.KeywordTriggerHead.SyncSources();   // the X11 key listener follows the master
            Platform.ScreenOcrService.Stop();            // the screen reader's timer, whatever the switches say
            Overlays.KeywordHighlightOverlay.CloseAll(); // any highlight box still on screen
        };

        /// <summary>Owner, 2026-10-10: "Stop until re-enabled". A panic press switches keyword triggers
        /// off and they stay off until the user turns them back on: the master (typed and screen) goes
        /// off, the screen read goes off AS A SAVED SETTING (the master is per-session, so it is the
        /// saved switch that would otherwise read the screen again next time), the reader stops and any
        /// highlight comes down. Nothing here or in PanicWatchdog switches either back on.
        /// Called by every accepted route: <see cref="StopAll"/> (tray, safe word, leash gate, the key's
        /// stop pass), the 6-blink stop, and the key's lock-card and grace-pause rungs. A refused press
        /// never gets here, and neither does an Escape the settings palette claimed (not a panic).
        /// UI thread. Idempotent. Never throws.</summary>
        internal static void SwitchOffKeywordTriggers()
        {
            var changed = false;
            try
            {
                var s = CoreSettings.Current;
                if (s != null)
                {
                    if (s.KeywordTriggersEnabled) { s.KeywordTriggersEnabled = false; KeywordMasterOffByPanic = true; changed = true; }
                    if (s.ScreenOcrEnabled)
                    {
                        s.ScreenOcrEnabled = false;
                        s.KeywordTriggersOffByPanic = true;
                        changed = true;
                        CoreSettings.Save();
                    }
                }
                if (changed) Serilog.Log.Information("Panic: keyword triggers switched off until the user turns them back on");
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: keyword trigger switch-off failed"); }
            try { StopKeywordSources(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: keyword source stop failed"); }
            if (!changed) return;
            try { KeywordTriggersSwitchedOff?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Panic: keyword switch-off repaint failed"); }
        }

        /// <summary>Stops every surface in order. One failing stop never skips the rest. Never throws.</summary>
        /// <param name="holdArmed">The caller already ran <see cref="ArmSafetyHold"/> for this press (the
        /// panic key arms it in its first lines, before the rungs that return early): arm once, not twice
        /// (hunt3 IC8: EMI heard "panicPressed" twice a press).</param>
        internal static void StopAll(string reason, MainShellWindow? shell = null, bool holdArmed = false)
        {
            Serilog.Log.Information("Panic: stopping every surface ({Reason})", reason);
            if (!holdArmed) ArmSafetyHold();
            SwitchOffKeywordTriggers();
            shell ??= MainShellWindow.Current;
            foreach (var s in All)
            {
                try { s.Stop(shell); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Panic: {Id} stop failed", s.Id); }
            }
        }
    }
}
