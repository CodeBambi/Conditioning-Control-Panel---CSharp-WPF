using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The nudge machine's head half: WPF <c>EmiDeskService.cs</c> :1161-1360 (StartNudges, the
    /// 5 s poll, NoteRingOpened, SpeakNudge, DrawNudge) and AskSituationOk :207. The machine
    /// itself, its brakes and its world are Core <c>EmiNudges.cs</c>: teach the two gestures, then
    /// shut up forever. A nudge is text in her bubble, like every other line.
    /// </summary>
    internal sealed partial class EmiDeskService
    {
        private readonly EmiNudgeMachine _nudges = new();
        private readonly EmiNudgeWorld _nudgeWorld = new();
        private DispatcherTimer? _nudgeTimer;

        internal const int NudgePollMs = 5_000;

        // Used only when the track is NOT in the lines file's vocabulary (WPF :1178-1182).
        private const string PetNudgeFallbackText = "pat me. it's allowed.";
        private const string RingNudgeFallbackText = "my other side has stuff in it. the other button.";
        private const string PinNudgeFallbackText = "like one? squeeze it. the other button. it stays.";

        /// <summary>True while the tube has anything in its bubble slot (WPF TubeBubbleLive).</summary>
        public bool TubeBubbleLive
        {
            get
            {
                try { return AvatarTube.AvatarTubeWindow.Live?.HasBubbleUp == true; }
                catch { return false; }
            }
        }

        /// <summary>
        /// The situational half of the quiet gate (WPF AskSituationOk :207): she is out and visible,
        /// nothing owns the screen (a video, a session, a tube bubble) and the app is not minimised.
        /// </summary>
        internal bool AskSituationOk()
        {
            try
            {
                if (!IsOut) return false;
                var win = _window;
                if (win == null || !win.IsVisible) return false;
                if (win.PresentationActive || win.InputLocked || win.Transiting) return false;
                if (win.AskLive) return false;

                if (CoreEngine.Video?.IsPlaying == true) return false;
                if (CoreSession.IsSessionRunning) return false;
                if (TubeBubbleLive) return false;

                var main = (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (main != null && main.WindowState == WindowState.Minimized) return false;
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] ask situation probe failed");
                return false;
            }
        }

        /// <summary>The nudge world's Quiet (WPF EmiNudgeWorld.Quiet): the situation, no live chain, no hold.</summary>
        private bool NudgeQuiet()
        {
            if (!AskSituationOk()) return false;
            if (_window?.ChainLive == true) return false;
            if (EmiLineEngine.Instance.HoldActive) return false;
            return true;
        }

        private void StartNudges(int summonCount)
        {
            try
            {
                EmiNudgeWorld.QuietProbe = NudgeQuiet;
                _nudges.NoteSummon(summonCount);
                StopNudgeTimer();
                _nudgeTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    Interval = TimeSpan.FromMilliseconds(NudgePollMs)
                };
                _nudgeTimer.Tick += OnNudgeTick;
                _nudgeTimer.Start();
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge machine failed to arm"); }
        }

        private void StopNudges()
        {
            try { _nudges.NoteDismiss(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge disarm failed"); }
            StopNudgeTimer();
        }

        private void StopNudgeTimer()
        {
            var t = _nudgeTimer;
            _nudgeTimer = null;
            if (t == null) return;
            try { t.Stop(); t.Tick -= OnNudgeTick; }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge timer stop failed"); }
        }

        /// <summary>True while the 5 s nudge poll runs (test seam).</summary>
        internal bool NudgesArmed => _nudgeTimer?.IsEnabled == true;

        private void OnNudgeTick(object? sender, EventArgs e)
        {
            try
            {
                if (!IsOut) { StopNudges(); return; }
                var track = _nudges.Tick(_nudgeWorld);
                if (track == null) return;
                SpeakNudge(track);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge tick failed"); }
        }

        /// <summary>The ring opened: count it, and maybe teach the pin once the fan has dealt (WPF :1250).</summary>
        internal void NoteRingOpened()
        {
            try
            {
                EmiState.NoteRingOpen();
                var track = _nudges.OnRingOpened(_nudgeWorld);
                if (track == null) return;

                // Let the fan finish dealing before she narrates it: 650 ms is the last card at rest.
                DispatcherTimer.RunOnce(() =>
                {
                    try { SpeakNudge(track); }
                    catch (Exception ex) { Log.Debug(ex, "[EmiDesk] pin nudge failed"); }
                }, TimeSpan.FromMilliseconds(900), DispatcherPriority.Background);
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ring-open bookkeeping failed"); }
        }

        private void SpeakNudge(string track)
        {
            bool spoke = false;
            try
            {
                if (!IsOut) return;
                var win = _window;
                if (win == null || win.PresentationActive || !win.IsVisible) return;

                var line = DrawNudge(track);
                if (line != null)
                {
                    win.SpeakLine(line);
                    spoke = true;
                    Log.Information("[EmiDesk] nudge {Track}: {Line}", track, line.Id);
                }
            }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge {Track} failed", track); }
            finally
            {
                try { _nudges.Attempted(_nudgeWorld, track, spoke); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge bookkeeping failed"); }
            }
        }

        /// <summary>WPF DrawNudge :1326: the file's own line when the track is in its vocabulary, else the fallback.</summary>
        public static LineDraw? DrawNudge(string track, EmiLineEngine? engine = null)
        {
            try
            {
                engine ??= EmiLineEngine.Instance;

                bool known = false;
                try { known = engine.MomentIds.Contains(track); }
                catch (Exception ex) { Log.Debug(ex, "[EmiDesk] nudge vocabulary probe failed"); }

                if (known)
                {
                    var drawn = engine.Draw(track);
                    if (drawn != null && !drawn.Hold && !string.IsNullOrWhiteSpace(drawn.Text)) return drawn;
                    return null;
                }

                if (engine.HoldActive) return null;

                var (text, face) = track switch
                {
                    EmiNudgeMachine.PetTrack => (PetNudgeFallbackText, "^_^"),
                    EmiNudgeMachine.RingTrack => (RingNudgeFallbackText, "^_~"),
                    EmiNudgeMachine.PinTrack => (PinNudgeFallbackText, "^_^"),
                    _ => ((string?)null, (string?)null)
                };
                if (text == null || face == null) return null;
                return new LineDraw(track + ".fallback", track, text, face, null, 2, false, 0);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] nudge draw failed for {Track}", track);
                return null;
            }
        }
    }
}
