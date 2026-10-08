using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The achievement tiles' interaction FX, ported from WPF MainWindow.TabFxPresetsQuestsAchievements.cs:692-834
    /// (entrance stagger, holo-foil tilt + hover lift + badge pop on unlocked tiles) and MainWindow.EventFx.cs:276-344
    /// (unlock reveal: blur dissolve + overshoot settle, and the particle burst on the tile). No ambient loop, as WPF.
    /// One clock for all of it, running only while a tween is live and the tab is shown (P01); tests step
    /// <see cref="Time"/> and call <see cref="StepFx"/>.
    /// </summary>
    public partial class AchievementsTabView
    {
        // WPF AchievementTiltDegrees/Ms, MotionFx HoverLiftScale/HoverMs/StaggerMs/StaggerCap, EventFx Achievement*.
        private const double TiltDegrees = 0.8, LiftScale = 1.02, RevealScale = 1.08, RevealBlur = 15;
        private const int TiltMs = 160, HoverMs = 150, StaggerMs = 40, StaggerCap = 6, RevealMs = 320, BurstCount = 95;

        internal static TimeProvider Time = TimeProvider.System;

        private sealed class Tween
        {
            public object Target = null!;
            public long Start;
            public double Delay, Ms, From, To;
            public Func<double, double> Ease = QuadOut;
            public Action<double> Set = null!;
            public Action? Done;
        }

        private readonly List<Tween> _tweens = new();
        private DispatcherTimer? _fxTimer;
        private readonly EventBurstLayer _burst = new();

        /// <summary>Test hooks: the clock is live, and how many unlock bursts were emitted.</summary>
        internal bool FxRunning => _fxTimer?.IsEnabled == true;
        internal int Bursts => _burst.Count;

        private static double QuadOut(double t) => 1 - (1 - t) * (1 - t);

        // WPF BackEase EaseOut, Amplitude 0.5: 1 - f(1 - t), f(x) = x^3 - x * A * sin(pi x).
        private static double BackOut(double t) { double x = 1 - t; return 1 - (x * x * x - x * 0.5 * Math.Sin(Math.PI * x)); }

        /// <summary>Animates one property; a new tween on the same target replaces the old one from where it stands.
        /// At MotionLevel Off it snaps to the end value, as WPF's MotionFx does.</summary>
        private void Animate(object target, double from, double to, int ms, Action<double> set,
                             Func<double, double>? ease = null, double delay = 0, Action? done = null)
        {
            Cancel(target);
            if (!Env.AllowTransitions) { set(to); done?.Invoke(); return; }
            _tweens.Add(new Tween { Target = target, Start = Time.GetTimestamp(), Delay = delay, Ms = ms, From = from, To = to,
                Ease = ease ?? QuadOut, Set = set, Done = done });
            set(from);
            _fxTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => StepFx());
            _fxTimer.Start();
        }

        private void Cancel(object target) => _tweens.RemoveAll(t => ReferenceEquals(t.Target, target));

        /// <summary>One clock tick at <see cref="Time"/>'s now.</summary>
        internal void StepFx()
        {
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var tw = _tweens[i];
                double ms = Time.GetElapsedTime(tw.Start).TotalMilliseconds - tw.Delay;
                if (ms < 0) continue;
                double p = Math.Min(ms / tw.Ms, 1);
                tw.Set(tw.From + (tw.To - tw.From) * tw.Ease(p));
                if (p < 1) continue;
                _tweens.RemoveAt(i);
                tw.Done?.Invoke();
            }
            if (_tweens.Count == 0) _fxTimer?.Stop();
        }

        /// <summary>Lands every tween on its end value and stops the clock (tab hidden or detached).</summary>
        private void FinishFx()
        {
            var all = _tweens.ToArray();
            _tweens.Clear();
            _fxTimer?.Stop();
            foreach (var tw in all) { tw.Set(tw.To); tw.Done?.Invoke(); }
        }

        /// <summary>WPF StaggerAchievementTiles: free grid only (the patron grid is below the fold), fade in from a
        /// 10 px rise, 40 ms apart, capped at 6 slots so a 60-tile wall lands in ~500 ms.</summary>
        private void StaggerTiles()
        {
            if (!Env.AllowTransitions) return;
            int i = 0;
            foreach (var t in _tiles.Values.Where(t => !t.A.IsExclusive && t.Card.IsVisible))
            {
                double delay = StaggerMs * Math.Min(i++, StaggerCap);
                var card = t.Card;
                Animate(card, 0, 1, 220, v => card.Opacity = v, delay: delay);
                Animate(t.CardSlide, 10, 0, 260, v => t.CardSlide.Y = v, delay: delay);
            }
        }

        /// <summary>WPF AchievementTile_MouseEnter: unlocked tiles only (a blurred "???" that tilts invitingly is
        /// the UI flirting about something you cannot have); tilts toward the side the pointer came in on.</summary>
        private void TileEnter(Tile t, PointerEventArgs e)
        {
            if (!IsUnlocked(t.A.Id)) return;
            double sign = e.GetPosition(t.Card).X < t.Card.Bounds.Width / 2 ? -1 : 1;
            Lift(t, true);
            Animate(t.BadgeTilt, t.BadgeTilt.Angle, sign * TiltDegrees, TiltMs, v => t.BadgeTilt.Angle = v);
            HoverPop.Enter(t.Badge);
        }

        private void TileLeave(Tile t)
        {
            Lift(t, false);
            Animate(t.BadgeTilt, t.BadgeTilt.Angle, 0, TiltMs, v => t.BadgeTilt.Angle = v);
            HoverPop.Leave(t.Badge);
        }

        private void Lift(Tile t, bool on) =>
            Animate(t.BadgeLift, t.BadgeLift.ScaleX, on ? LiftScale : 1, HoverMs, v => t.BadgeLift.ScaleX = t.BadgeLift.ScaleY = v);

        /// <summary>WPF CelebrateAchievementUnlock + RevealAchievementTile, when the grid is on screen: the locked blur
        /// dissolves off (Performance tier skips the raster), the tile settles back from 1.08, sparks burst on it.
        /// ponytail: with the tab hidden WPF bursts on the Achievements nav anchor instead - that needs the shell's
        /// event-burst layer (shell-event-fx); the unlock toast still announces it.</summary>
        private void Celebrate(Tile t)
        {
            if (!IsEffectivelyVisible) return;
            Burst(t.Card); // particles gate themselves (AmbientFxCanvas.Burst -> Env.AllowParticles)
            if (!Env.AllowTransitions) return;
            if (Env.AllowGlow(Env.CurrentTier))
            {
                var blur = new BlurEffect { Radius = RevealBlur };
                t.Badge.Effect = blur;
                // Dropped at the end: a live 0-radius Effect still costs the tile its own render pass.
                Animate(blur, RevealBlur, 0, RevealMs, v => blur.Radius = v,
                    done: () => { if (ReferenceEquals(t.Badge.Effect, blur)) t.Badge.Effect = null; });
            }
            Animate(t.CardScale, RevealScale, 1, RevealMs, v => t.CardScale.ScaleX = t.CardScale.ScaleY = v, BackOut);
        }

        /// <summary>WPF FireBurstAt on the tile centre (see <see cref="EventBurstLayer"/>).</summary>
        private void Burst(Control anchor) => _burst.Fire(anchor, BurstCount);
    }
}
