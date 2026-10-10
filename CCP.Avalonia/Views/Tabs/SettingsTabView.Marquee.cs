// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Marquee.cs, region "Marquee Animation"
// (StartMarqueeAnimation) and ResolveDefaultMarqueeMessage. On WPF that code lived on MainWindow and
// reached into SettingsTab.MarqueeText / MarqueeCanvas; here the page that carries both controls owns
// the strip, and MainShellWindow.MarqueeStrip.cs forwards the window-level entry points to it.
//
// What is the same as WPF: the saved message (blank -> the active mod's banner, else
// AppSettings.DefaultMarqueeMessage), upper-cased, doubled with a 10-space separator into one segment,
// measured with the TextBlock's own font, repeated ceil(canvas / segment) + 2 times, and scrolled from 0
// to -segment at 80 px/s forever, which loops seamlessly because the next segment is identical. Under
// the ambient gate (MotionFx.AllowAmbientLoops false: Performance tier, Reduced or Off motion) it parks
// ONE segment at X = 0, exactly as WPF does. It restarts on canvas resize, on attach and on a motion gate
// change (WPF CmbMotionLevel_SelectionChanged re-arms every ambient loop).
//
// What differs: WPF ran a controllable Storyboard (DoubleAnimation at AmbientFrameRate). Avalonia has no
// Storyboard twin, so a FrameClock at 30 fps (the house rule for effect clocks, never 24) writes X from
// the wall-clock time since the strip started. Position comes from elapsed time, not summed ticks, so a
// dropped frame never slows the scroll.
//
// NOT PORTED: the interlude acts (WPF MainWindow.MarqueeReads.cs: CancelMarqueeInterlude,
// ArmMarqueeReadClock, the paused-storyboard handoff). Nothing on this head arms them yet.

using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        private MarqueeScroller? _marquee;

        /// <summary>The scrolling strip behind MarqueeText (tests read its state).</summary>
        internal MarqueeScroller Marquee => _marquee ??= new MarqueeScroller(MarqueeCanvas, MarqueeText);

        /// <summary>WPF MainWindow.StartMarqueeAnimation: rebuild the strip from the saved message.</summary>
        public void StartMarqueeAnimation() => Marquee.Start();

        /// <summary>Called once from the constructor.</summary>
        private void InitializeMarqueeStrip()
        {
            var strip = Marquee;
            EventHandler onLanguage = (_, _) => strip.Start();
            Action onGate = () => strip.Start();

            // WPF: MarqueeText.Loaded + MarqueeCanvas.SizeChanged both restart the loop.
            MarqueeText.AttachedToVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged += onLanguage;
                AmbientFxCanvas.Env.MotionGateChanged += onGate;
                strip.Start();
            };
            MarqueeText.DetachedFromVisualTree += (_, _) =>
            {
                LocalizationManager.Instance.LanguageChanged -= onLanguage;
                AmbientFxCanvas.Env.MotionGateChanged -= onGate;
                strip.Stop();
            };
            MarqueeCanvas.SizeChanged += (_, e) =>
            {
                if (e.WidthChanged) strip.Start();
            };
        }
    }

    /// <summary>
    /// The marquee strip itself: WPF StartMarqueeAnimation over a Canvas + TextBlock pair, with a
    /// FrameClock in place of the Storyboard. See the file header.
    /// </summary>
    internal sealed class MarqueeScroller
    {
        /// <summary>WPF: "Speed: 80 pixels per second".</summary>
        internal const double PixelsPerSecond = 80;

        /// <summary>WPF: 10 spaces between repetitions.</summary>
        internal const string Separator = "          ";

        private readonly Canvas _canvas;
        private readonly TextBlock _text;
        private readonly FrameClock _clock;
        private readonly Stopwatch _elapsed = new();
        private double _segmentWidth;

        /// <summary>Gate override for tests; null reads MotionFx.AllowAmbientLoops.</summary>
        internal static Func<bool>? AmbientOverride { get; set; }

        private static bool AllowAmbientLoops => AmbientOverride?.Invoke() ?? AmbientFxCanvas.Env.AllowAmbientLoops;

        public MarqueeScroller(Canvas canvas, TextBlock text)
        {
            _canvas = canvas;
            _text = text;
            _clock = new FrameClock(text) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
            _clock.Tick += (_, _) => Advance();
        }

        /// <summary>True while the strip scrolls (false when parked or not built).</summary>
        internal bool IsScrolling => _clock.IsEnabled;

        /// <summary>The upper-cased message the strip was last built from.</summary>
        internal string CurrentMessage { get; private set; } = "";

        internal double SegmentWidth => _segmentWidth;

        internal TranslateTransform Transform
        {
            get
            {
                if (_text.RenderTransform is TranslateTransform t) return t;
                var fresh = new TranslateTransform();
                _text.RenderTransform = fresh;
                return fresh;
            }
        }

        /// <summary>
        /// WPF ResolveDefaultMarqueeMessage: the active mod's banner (which walks to CCP Default's
        /// neutral line), else the neutral const. Only for a banner nobody typed.
        /// </summary>
        internal static string ResolveDefaultMarqueeMessage()
        {
            try
            {
                var banner = App.Mods?.GetMarqueeBannerMessage();
                if (!string.IsNullOrWhiteSpace(banner)) return banner!;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Marquee: mod banner lookup failed; using the neutral default");
            }
            return AppSettings.DefaultMarqueeMessage;
        }

        public void Stop()
        {
            _clock.Stop();
            _elapsed.Reset();
        }

        public void Start()
        {
            try
            {
                Stop();

                var canvasWidth = _canvas.Bounds.Width;
                if (canvasWidth <= 0) return;

                var message = CoreSettings.Current.MarqueeMessage;
                if (string.IsNullOrWhiteSpace(message)) message = ResolveDefaultMarqueeMessage();
                message = message.ToUpperInvariant();
                CurrentMessage = message;

                var singleSegment = message + Separator + message + Separator;

                var probe = new TextBlock
                {
                    Text = singleSegment,
                    FontFamily = _text.FontFamily,
                    FontSize = _text.FontSize,
                    FontWeight = _text.FontWeight,
                };
                probe.Measure(Size.Infinity);
                var segmentWidth = probe.DesiredSize.Width;
                if (segmentWidth <= 0) return;
                _segmentWidth = segmentWidth;

                var segmentsNeeded = (int)Math.Ceiling(canvasWidth / segmentWidth) + 2;
                _text.Text = string.Concat(Enumerable.Repeat(singleSegment, segmentsNeeded));

                var transform = Transform;
                transform.X = 0;

                // Ambient loop: Performance tier or reduced / off motion parks one segment at 0.
                if (!AllowAmbientLoops)
                {
                    _text.Text = singleSegment;
                    return;
                }

                _elapsed.Restart();
                _clock.Start();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to start marquee animation");
            }
        }

        private void Advance()
        {
            if (_segmentWidth <= 0) return;
            var travelled = _elapsed.Elapsed.TotalSeconds * PixelsPerSecond;
            Transform.X = -(travelled % _segmentWidth);
        }
    }
}
