using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Mantra Lab: a full-screen typing drill where every correctly typed character lights up
    /// and the whole scene warms from cold purple to hot pink as the streak climbs.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MantraWindow.xaml.cs. Deviations:
    ///  - <c>App.Mantra</c> (MantraService) is pinned to the WPF head by App.Progression /
    ///    App.Quests, so the SESSION half is still stubbed: no completion counting, no streak
    ///    events, no NAudio drone or tones, no session-complete trigger. The VIEW half is ported
    ///    verbatim — the per-character highlight system, the streak intensity ramp, the colour
    ///    lerp, the float drift and the #734 anti-cheat guard — and its text and rep target now
    ///    come from <c>CoreSettings.Current</c> (MantraPool, MantraDefaultCount) rather than an
    ///    invented English line; <see cref="SampleStreak"/> still drives the warm palette so the
    ///    render shows a live scene rather than a cold empty one.
    ///  - Five WPF Storyboards (pulse, shake, letter-pulse, wrong-shake, glow) are dropped:
    ///    Avalonia has no Storyboard and every one of them is begun from a service event that is
    ///    stubbed above. ponytail: re-add as Avalonia Animations when MantraService reaches Core.
    ///  - <c>DispatcherTimer</c> exists in Avalonia; the float timer is stopped in
    ///    <c>OnClosed</c> as well as <c>CleanupAndClose</c>, because --render-all closes the
    ///    window externally and a 16ms tick would outlive the view in that shared process.
    ///  - The idle timer is dropped entirely: it only calls <c>MantraService.BreakStreak</c>.
    ///  - <c>DataObject.AddPastingHandler</c> -> <c>TextBox.PastingFromClipboardEvent</c>;
    ///    <c>Visibility</c> -> <c>IsVisible</c>.
    /// </summary>
    public partial class MantraWindow : Window
    {
        /// <summary>Last-resort line for the letter highlighting when the user's pool is empty.
        /// ponytail: the live line needs MantraService.CurrentMantra from
        /// ConditioningControlPanel/Services/MantraService.cs — it is pinned to the head by
        /// App.Progression / App.Quests, so no Core seam exists for it yet.</summary>
        private const string SampleMantra = "good girls sink deeper every time";
        private const int SampleStreak = 7;

        private DispatcherTimer? _floatTimer;
        private DateTime _startTime;
        private bool _sessionComplete;

        // Per-character highlight state
        private readonly List<Run> _mantraRuns = new();
        private int _prevMatchCount;
        private int _prevInputLength;
        private Color _highlightColor = Color.FromRgb(0x99, 0x88, 0xDD);
        private static readonly Color DimColor = Color.FromRgb(0x35, 0x35, 0x50);
        private static readonly Color ErrorColor = Color.FromRgb(0xFF, 0x44, 0x44);
        private static readonly Color FlashColor = Colors.White;

        private readonly TextBlock _txtMantra, _txtCompletions, _txtTarget, _txtStreak, _txtBestStreak;
        private readonly TextBox _txtInput;
        private readonly Border _colorWashOverlay, _completionOverlay;
        private readonly TextBlock _txtCompletionStats;
        private readonly DropShadowEffect? _mantraGlow;
        private readonly GradientStop? _washCenter, _baseCenter;
        private readonly SolidColorBrush? _inputBorderBrush;
        private readonly TranslateTransform? _mantraTranslate;

        public MantraWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _txtMantra = this.FindControl<TextBlock>("TxtMantra")!;
            _txtCompletions = this.FindControl<TextBlock>("TxtCompletions")!;
            _txtTarget = this.FindControl<TextBlock>("TxtTarget")!;
            _txtStreak = this.FindControl<TextBlock>("TxtStreak")!;
            _txtBestStreak = this.FindControl<TextBlock>("TxtBestStreak")!;
            _txtInput = this.FindControl<TextBox>("TxtInput")!;
            _colorWashOverlay = this.FindControl<Border>("ColorWashOverlay")!;
            _completionOverlay = this.FindControl<Border>("CompletionOverlay")!;
            _txtCompletionStats = this.FindControl<TextBlock>("TxtCompletionStats")!;

            // WPF named the brushes, the effect and the transform directly. Avalonia only
            // name-scopes StyledElements, so each is reached through the control that owns it.
            _mantraGlow = _txtMantra.Effect as DropShadowEffect;
            _washCenter = (_colorWashOverlay.Background as GradientBrush)?.GradientStops[0];
            _baseCenter = (this.FindControl<Border>("BaseLayer")!.Background as GradientBrush)?.GradientStops[0];
            _inputBorderBrush = this.FindControl<Border>("InputBorder")!.BorderBrush as SolidColorBrush;
            _mantraTranslate = (_txtMantra.RenderTransform as TransformGroup)?.Children[1] as TranslateTransform;

            // Same anti-cheat hardening as the lock card (#734). Key blocking alone isn't enough:
            // the pasting handler also covers the context menu and drag-drop, and undo has to be
            // off because completing a mantra clears the box - Ctrl+Z would put the finished
            // mantra straight back and every Ctrl+Z/Ctrl+Y pair counted as another repetition.
            _txtInput.AddHandler(TextBox.PastingFromClipboardEvent, (_, e) => e.Handled = true);
            _txtInput.IsUndoEnabled = false;

            _txtInput.TextChanged += (_, _) => TxtInput_TextChanged();
            _txtInput.AddHandler(KeyDownEvent, TxtInput_PreviewKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            KeyDown += (_, e) => Window_KeyDown(e);
            Loaded += (_, _) => Window_Loaded();
        }

        private void Window_Loaded()
        {
            _startTime = DateTime.UtcNow;

            // ponytail: subscribing StreakChanged / StreakBroken / MantraCompleted /
            // SessionComplete needs ConditioningControlPanel/Services/MantraService.cs; the drone
            // and the streak tones need NAudio's SignalGenerator + WaveOutEvent, which CoreAudio
            // deliberately does not cover (PlayOneShot takes a file path, not a generator).

            // Build initial letter display
            var mantra = CurrentMantra ?? "";
            BuildMantraRuns(mantra);
            // ponytail: the live figure is MantraService.TargetCount, which the caller sets via
            // StartSession(reps). MantraDefaultCount is the settings-backed default that caller
            // reads, so it is the honest stand-in rather than an invented number.
            _txtTarget.Text = $"/{CoreSettings.Current.MantraDefaultCount}";
            _txtCompletions.Text = "0";
            _txtStreak.Text = "0";
            _txtBestStreak.Text = "0";

            // Start float animation (gentle sine-wave drift)
            _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _floatTimer.Tick += FloatTimer_Tick;
            _floatTimer.Start();

            // Placeholder session state, so the highlight ramp and the warm palette both draw.
            OnStreakChanged(SampleStreak);
            UpdateHighlights(mantra[..Math.Min(11, mantra.Length)]);

            _txtInput.Focus();
        }

        protected override void OnClosed(EventArgs e)
        {
            _floatTimer?.Stop();
            base.OnClosed(e);
        }

        #region Per-character highlight system

        private void BuildMantraRuns(string mantra)
        {
            _txtMantra.Inlines ??= new InlineCollection();
            _txtMantra.Inlines.Clear();
            _mantraRuns.Clear();
            _prevMatchCount = 0;
            _prevInputLength = 0;

            foreach (char c in mantra)
            {
                var run = new Run(c.ToString())
                {
                    Foreground = new SolidColorBrush(DimColor)
                };
                _mantraRuns.Add(run);
                _txtMantra.Inlines.Add(run);
            }
        }

        private int UpdateHighlights(string input)
        {
            var mantra = CurrentMantra;
            if (mantra == null || _mantraRuns.Count == 0) return 0;

            int matchCount = 0;
            bool hasError = false;

            for (int i = 0; i < mantra.Length && i < input.Length; i++)
            {
                if (char.ToLowerInvariant(input[i]) == char.ToLowerInvariant(mantra[i]))
                    matchCount = i + 1;
                else
                {
                    hasError = true;
                    break;
                }
            }

            // Color each Run
            for (int i = 0; i < _mantraRuns.Count; i++)
            {
                Color color;
                if (i < matchCount)
                    color = _highlightColor;
                else if (hasError && i == matchCount)
                    color = ErrorColor;
                else
                    color = DimColor;

                _mantraRuns[i].Foreground = new SolidColorBrush(color);
            }

            // Flash the latest correct char white briefly
            bool newCharTyped = input.Length > _prevInputLength;
            if (newCharTyped && matchCount > _prevMatchCount && matchCount > 0)
            {
                int flashIdx = matchCount - 1;
                _mantraRuns[flashIdx].Foreground = new SolidColorBrush(FlashColor);

                // Fade back to highlight color after a short delay
                var idx = flashIdx;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (idx < _mantraRuns.Count)
                        _mantraRuns[idx].Foreground = new SolidColorBrush(_highlightColor);
                };
                timer.Start();

                // ponytail: the WPF LetterPulseStoryboard fired here; re-add as an Avalonia
                // Animation alongside the other four.
            }

            _prevMatchCount = matchCount;
            _prevInputLength = input.Length;

            return matchCount;
        }

        #endregion

        /// <summary>
        /// The line on screen. The user's own <c>MantraPool</c> is in Core, so the window shows a
        /// real mantra rather than an invented English one; the pool's FIRST entry is deliberate —
        /// picking at random here would be a second copy of <c>MantraService.NextMantra</c>'s
        /// no-immediate-repeat rotation, which is the drift the service exists to prevent.
        /// ponytail: needs MantraService.CurrentMantra for the rotating line.
        /// </summary>
        private static string? CurrentMantra
        {
            get
            {
                var pool = CoreSettings.Current.MantraPool;
                return pool is { Count: > 0 } && !string.IsNullOrWhiteSpace(pool[0])
                    ? pool[0]
                    : SampleMantra;
            }
        }

        private void FloatTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
            if (_mantraTranslate != null)
                _mantraTranslate.Y = Math.Sin(elapsed * 0.5) * 6;

            // ponytail: the drone gain ramp lived here. Needs NAudio's SignalGenerator (the tone is
            // synthesised, not a file), which CoreAudio.PlayOneShot cannot express; its volume knob
            // CoreSettings.Current.MantraDroneVolume is already in Core and waits on the generator.
        }

        private void TxtInput_TextChanged()
        {
            if (_sessionComplete) return;

            var input = _txtInput.Text ?? "";
            var target = CurrentMantra;
            if (target == null) return;

            int matchCount = UpdateHighlights(input);

            // Check completion: all characters match and input length equals mantra length.
            // ponytail: needs MantraService.TryCompleteMantra from
            // ConditioningControlPanel/Services/MantraService.cs to count the rep, clear the box
            // and roll the next mantra; until then a finished line just stays lit. The
            // _updatingInput re-entry guard belongs with it — it only exists to cover that clear.
            _ = matchCount;
        }

        private void TxtInput_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            // Same gesture set as the lock card (paste/copy/cut/select-all/undo/redo plus the
            // legacy Shift+Insert / Ctrl+Insert / Shift+Delete clipboard gestures) — shared with
            // LockCardWindow deliberately so the two anti-cheat surfaces can't drift apart
            // again (#734). WPF read Keyboard.Modifiers; Avalonia carries them on the args.
            if (LockCardWindow.IsBlockedInputGesture(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
            }
        }

        private void OnStreakChanged(int streak)
        {
            _txtStreak.Text = streak.ToString();
            _txtBestStreak.Text = streak.ToString();

            UpdateVisualIntensity(streak);
        }

        /// <summary>
        /// The MantraService.SessionComplete handler, ported view-side. Nothing calls it yet — the
        /// event it hangs off is stubbed — so the overlay only draws once the service reaches Core.
        /// The stats line is hardcoded English in the WPF original too; no loc key exists for it.
        /// </summary>
        private void OnSessionComplete(int totalReps, int bestStreak)
        {
            _sessionComplete = true;

            _txtCompletionStats.Text = $"{totalReps} repetitions  |  Best streak: {bestStreak}";
            _completionOverlay.IsVisible = true;
            _txtInput.IsEnabled = false;

            // ponytail: the completion tone needs NAudio, wired with the rest of the audio.

            // Auto-close after 5 seconds
            var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                CleanupAndClose();
            };
            closeTimer.Start();
        }

        private void UpdateVisualIntensity(int streak)
        {
            // Normalize streak 0-15 → 0-1
            double t = Math.Min(streak / 15.0, 1.0);

            // Update highlight color: cold purple → hot pink
            _highlightColor = LerpColor(Color.FromRgb(0x99, 0x88, 0xDD), Color.FromRgb(0xFF, 0x69, 0xB4), t);

            // Re-color already highlighted runs with new color
            var input = _txtInput.Text ?? "";
            var mantra = CurrentMantra;
            if (mantra != null)
            {
                int matchLen = 0;
                for (int i = 0; i < mantra.Length && i < input.Length; i++)
                {
                    if (char.ToLowerInvariant(input[i]) == char.ToLowerInvariant(mantra[i]))
                        matchLen = i + 1;
                    else break;
                }
                for (int i = 0; i < matchLen && i < _mantraRuns.Count; i++)
                    _mantraRuns[i].Foreground = new SolidColorBrush(_highlightColor);
            }

            // Color wash: cold purples → hot pinks, opacity 0→0.8
            _colorWashOverlay.Opacity = t * 0.8;
            if (_washCenter != null)
                _washCenter.Color = LerpColor(Color.FromRgb(0x66, 0x33, 0xAA), Color.FromRgb(0xFF, 0x69, 0xB4), t);

            // Glow intensity
            if (_mantraGlow != null)
            {
                _mantraGlow.BlurRadius = 20 + t * 30;
                _mantraGlow.Opacity = 0.6 + t * 0.4;
                _mantraGlow.Color = LerpColor(Color.FromRgb(0x99, 0x66, 0xCC), Color.FromRgb(0xFF, 0x69, 0xB4), t);
            }

            // Input border glow
            if (_inputBorderBrush != null)
                _inputBorderBrush.Color = LerpColor(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4), Color.FromArgb(0xFF, 0xFF, 0x69, 0xB4), t);

            // Base gradient warm up
            if (_baseCenter != null)
                _baseCenter.Color = LerpColor(Color.FromRgb(0x1A, 0x0A, 0x2E), Color.FromRgb(0x2E, 0x0A, 0x2E), t);

            // ponytail: the drone target gain rode this same t; needs NAudio.
        }

        private static Color LerpColor(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        private void Window_KeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CleanupAndClose();
                e.Handled = true;
                return;
            }

            if (_sessionComplete)
            {
                CleanupAndClose();
                e.Handled = true;
            }
        }

        private void CleanupAndClose()
        {
            _floatTimer?.Stop();

            // ponytail: unsubscribing the four MantraService events and MantraService.EndSession()
            // go here, plus StopDrone(); all need ConditioningControlPanel/Services/MantraService.cs
            // and NAudio respectively.

            Close();
        }
    }
}
