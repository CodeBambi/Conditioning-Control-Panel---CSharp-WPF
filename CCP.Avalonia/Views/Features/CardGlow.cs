using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// The on glow of a mosaic tile (perf, 2026-10-09). WPF put a DropShadowEffect on the tile's
    /// root border and breathed its Opacity; in Avalonia an Effect makes the whole tile (art, text,
    /// chrome) an offscreen layer that is re-rendered and blurred on every animation frame, which
    /// was most of the dashboard lag with the engine on. The glow is now a sibling Border behind
    /// the tile wearing a BoxShadow in FxGlowColor; its Opacity breathes, nothing else moves.
    /// </summary>
    internal static class CardGlow
    {
        /// <summary>Default blur, the WPF DropShadowEffect BlurRadius.</summary>
        public const double BlurRadius = 18;

        public static BoxShadows Shadow(object? colour, double blur = BlurRadius) =>
            new(new BoxShadow { Blur = blur, Color = colour is Color c ? c : Colors.HotPink });

        /// <summary>Keeps <paramref name="layer"/>'s shadow in the current mod's FxGlowColor.</summary>
        public static void Bind(Border layer, Func<double> blur)
        {
            layer.Bind(Border.BoxShadowProperty,
                layer.GetResourceObservable("FxGlowColor", c => Shadow(c, blur())));
        }

        /// <summary>Re-applies the shadow after a blur change (performance tier).</summary>
        public static void SetBlur(Border layer, double blur) => Bind(layer, () => blur);   // a new binding replaces the old
    }

    /// <summary>
    /// The tile breath on the window's shared 30 fps beat (owner, 2026-10-09: "still something
    /// stealing frames"). An Avalonia Animation ticks at render rate, so one breathing tile kept
    /// the whole window composing 60 frames a second, each paying a full-window blit; on the
    /// FrameClock it ticks with the fog and the logo dial. Same curve as the old Animation
    /// (Alternate + SineEaseInOut over the period): min + (max - min) * (1 - cos(pi t / period)) / 2.
    /// </summary>
    internal sealed class BreathClock
    {
        private readonly global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock _clock;
        private readonly System.Diagnostics.Stopwatch _watch = new();
        private readonly double _period;
        private (Visual Target, double Min, double Max)[] _targets = Array.Empty<(Visual, double, double)>();

        public BreathClock(Visual owner, double periodSeconds)
        {
            _period = periodSeconds;
            _clock = new global::ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock(owner) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            _clock.Tick += (_, _) => Step();
        }

        public bool IsRunning => _clock.IsEnabled;

        public void Start(params (Visual Target, double Min, double Max)[] targets)
        {
            _targets = targets;
            _watch.Restart();
            Step();
            _clock.Start();
        }

        public void Stop()
        {
            _clock.Stop();
            _watch.Reset();
            _targets = Array.Empty<(Visual, double, double)>();
        }

        private void Step()
        {
            double k = (1 - Math.Cos(Math.PI * _watch.Elapsed.TotalSeconds / _period)) / 2;
            foreach (var (t, min, max) in _targets) t.Opacity = min + (max - min) * k;
        }
    }
}
