using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The Mantra Lab: a full-screen typing drill where every correctly typed character lights up
    /// and the whole scene warms from cold purple to hot pink as the streak climbs.
    ///
    /// PORTED from ConditioningControlPanel/Windows/MantraWindow.xaml.cs over the Core
    /// <see cref="MantraService"/> (<c>App.Mantra</c>), which the opener must have started
    /// (<c>MainShellWindow.StartMantraSession</c>, as WPF). Deviations:
    ///  - Audio: NAudio's SignalGenerator/WaveOutEvent become synthesised WAVs (<see cref="Platform.ToneWav"/>):
    ///    tones through <see cref="CoreAudio.PlayOneShot"/>, the drone as a looping LibVLC layer whose
    ///    volume follows WPF's gain ramp.
    ///  - The five WPF Storyboards (pulse, shake, letter-pulse, wrong-shake, glow) are tweens stepped
    ///    off the float timer on <see cref="Clock"/> (<see cref="Animate"/>): Avalonia's
    ///    <c>TransformAnimator</c> seizes the target's <c>RenderTransform</c> and throws on a
    ///    code-held <c>Transform</c>. Same targets, values and key times as MantraWindow.xaml:14-58;
    ///    a Begin on a running storyboard restarts it, a shake starts from the current X (WPF snapshot).
    ///  - Opened with no running session (the render proof), it starts one with MantraDefaultCount;
    ///    WPF assumes the opener did. A failed StartMantraSession is logged, not shown in a MessageBox.
    ///  - Timers and the drone are also stopped in <c>OnClosed</c>, because --render-all closes the
    ///    window externally.
    ///  - <c>DataObject.AddPastingHandler</c> -> <c>TextBox.PastingFromClipboardEvent</c>;
    ///    <c>Visibility</c> -> <c>IsVisible</c>.
    /// </summary>
    public partial class MantraWindow : Window
    {
        private readonly MantraService _service = App.Mantra;
        private DispatcherTimer? _floatTimer;
        private DispatcherTimer? _idleTimer;
        private long _startTime;

        /// <summary>The animation clock (float drift, storyboards). Tests step it.</summary>
        internal static TimeProvider Clock = TimeProvider.System;

        // Storyboard state: one slot per animated property, as WPF's SnapshotAndReplace hand-off.
        private static readonly (double T, double X)[] ShakeKeys = { (0.05, 8), (0.1, -8), (0.15, 6), (0.2, -6), (0.25, 3), (0.3, 0) };
        private static readonly (double T, double X)[] WrongShakeKeys = { (0.03, 4), (0.06, -4), (0.09, 3), (0.12, -3), (0.15, 0) };
        private long? _pulseAt, _shakeAt;
        private double _pulseTo, _pulseHalf, _shakeFrom;
        private (double T, double X)[] _shakeKeys = ShakeKeys;
        private bool _sessionComplete;
        private bool _updatingInput;

        // Drone audio (WPF: two SignalGenerators; here one looping synthesised layer)
        /// <summary>Opens the looping drone file. Tests swap it; null when libvlc did not load.</summary>
        internal static Func<string, Platform.LayeredAudio.ILayerPlayer?> DroneOpener =
            path => Platform.LibVlcAudio.Shared is { } vlc ? new Platform.LayeredAudio.VlcLayerPlayer(vlc, path, startMuted: true) : null;
        private Platform.LayeredAudio.ILayerPlayer? _drone;
        private float _droneTargetGain = 0.05f;
        private float _droneCurrentGain = 0.05f;
        private bool _droneRamped;   // WPF applies MantraDroneVolume only from the first ramp step

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
        private readonly Border _colorWashOverlay, _completionOverlay, _glowOverlay;
        private readonly TextBlock _txtCompletionStats;
        private readonly DropShadowEffect? _mantraGlow;
        private readonly GradientStop? _washCenter, _baseCenter;
        private readonly SolidColorBrush? _inputBorderBrush;
        private readonly TranslateTransform? _mantraTranslate;
        private readonly ScaleTransform? _mantraScale;

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
            _glowOverlay = this.FindControl<Border>("GlowOverlay")!;
            _txtCompletionStats = this.FindControl<TextBlock>("TxtCompletionStats")!;

            // WPF named the brushes, the effect and the transform directly. Avalonia only
            // name-scopes StyledElements, so each is reached through the control that owns it.
            _mantraGlow = _txtMantra.Effect as DropShadowEffect;
            _washCenter = (_colorWashOverlay.Background as GradientBrush)?.GradientStops[0];
            _baseCenter = (this.FindControl<Border>("BaseLayer")!.Background as GradientBrush)?.GradientStops[0];
            _inputBorderBrush = this.FindControl<Border>("InputBorder")!.BorderBrush as SolidColorBrush;
            _mantraTranslate = (_txtMantra.RenderTransform as TransformGroup)?.Children[1] as TranslateTransform;
            _mantraScale = (_txtMantra.RenderTransform as TransformGroup)?.Children[0] as ScaleTransform;

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
            _startTime = Clock.GetTimestamp();

            // Deviation: WPF assumes the opener started a session; opened bare (the render proof), start
            // one with the user's default count rather than draw an empty line and "/0".
            if (!_service.IsActive) _service.StartSession(CoreSettings.Current.MantraDefaultCount);

            // Subscribe to service events
            _service.StreakChanged += OnStreakChanged;
            _service.StreakBroken += OnStreakBroken;
            _service.MantraCompleted += OnMantraCompleted;
            _service.SessionComplete += OnSessionComplete;

            // Build initial letter display
            BuildMantraRuns(_service.CurrentMantra ?? "");
            _txtTarget.Text = $"/{_service.TargetCount}";
            _txtCompletions.Text = "0";
            _txtStreak.Text = "0";
            _txtBestStreak.Text = "0";

            // Start float animation (gentle sine-wave drift)
            _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _floatTimer.Tick += FloatTimer_Tick;
            _floatTimer.Start();

            // Start idle timer (5s inactivity breaks streak)
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _idleTimer.Tick += IdleTimer_Tick;
            _idleTimer.Start();

            StartDrone();
            _open = this;

            _txtInput.Focus();
        }

        protected override void OnClosed(EventArgs e)
        {
            Cleanup();
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
            var mantra = _service.CurrentMantra;
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

                // Subtle pulse on the whole text
                Pulse(1.02, 0.08);   // LetterPulseStoryboard
            }

            // Wrong char typed → shake
            if (newCharTyped && hasError && matchCount == _prevMatchCount)
                Shake(WrongShakeKeys);   // WrongShakeStoryboard

            _prevMatchCount = matchCount;
            _prevInputLength = input.Length;

            return matchCount;
        }

        #endregion

        private void Pulse(double to, double half) { _pulseAt = Clock.GetTimestamp(); _pulseTo = to; _pulseHalf = half; }

        private void Shake((double T, double X)[] keys) { _shakeAt = Clock.GetTimestamp(); _shakeKeys = keys; _shakeFrom = _mantraTranslate?.X ?? 0; }

        /// <summary>One frame of the float drift, GlowPulseStoryboard (0 -> 0.3 over 2 s, reversed,
        /// forever) and whichever scale pulse / X shake is running.</summary>
        private void Animate()
        {
            var now = Clock.GetTimestamp();
            var elapsed = Clock.GetElapsedTime(_startTime, now).TotalSeconds;
            var g = elapsed % 4.0 / 2.0;
            _glowOverlay.Opacity = 0.3 * (g <= 1 ? g : 2 - g);

            if (_mantraScale != null && _pulseAt is { } p)
            {
                var s = Clock.GetElapsedTime(p, now).TotalSeconds / _pulseHalf;   // From 1 To x, AutoReverse
                if (s >= 2) _pulseAt = null;
                _mantraScale.ScaleX = _mantraScale.ScaleY = 1 + (_pulseTo - 1) * (s >= 2 ? 0 : s <= 1 ? s : 2 - s);
            }

            if (_mantraTranslate == null) return;
            _mantraTranslate.Y = Math.Sin(elapsed * 0.5) * 6;
            if (_shakeAt is not { } sh) return;
            var t = Clock.GetElapsedTime(sh, now).TotalSeconds;
            double prevT = 0, prevX = _shakeFrom;
            foreach (var (kt, kx) in _shakeKeys)
            {
                if (t < kt) { _mantraTranslate.X = prevX + (kx - prevX) * (t - prevT) / (kt - prevT); return; }
                (prevT, prevX) = (kt, kx);
            }
            _mantraTranslate.X = prevX;
            _shakeAt = null;
        }

        private void FloatTimer_Tick(object? sender, EventArgs e)
        {
            Animate();

            // Smoothly ramp drone gain
            if (_drone != null && Math.Abs(_droneCurrentGain - _droneTargetGain) > 0.001f)
            {
                _droneCurrentGain += (_droneTargetGain - _droneCurrentGain) * 0.02f;
                _droneRamped = true;
                ApplyDroneVolume();
            }
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            if (_service.IsActive && _service.Streak > 0)
                _service.BreakStreak();
        }

        private void TxtInput_TextChanged()
        {
            if (_updatingInput || _sessionComplete || !_service.IsActive) return;

            // Reset idle timer
            _idleTimer?.Stop();
            _idleTimer?.Start();

            var input = _txtInput.Text ?? "";
            var target = _service.CurrentMantra;
            if (target == null) return;

            int matchCount = UpdateHighlights(input);

            // Check completion: all characters match and input length equals mantra length
            if (matchCount == target.Length && input.Length == target.Length)
            {
                if (_service.TryCompleteMantra())
                {
                    _updatingInput = true;
                    _txtInput.Text = "";
                    _updatingInput = false;
                }
            }
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

        private void OnMantraCompleted()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(OnMantraCompleted); return; }

            // Rebuild runs for the new mantra (CurrentMantra already updated before event fires)
            BuildMantraRuns(_service.CurrentMantra ?? "");
            _txtCompletions.Text = _service.Completions.ToString();

            // Full pulse animation on completion
            Pulse(1.06, 0.15);   // PulseStoryboard

            // Play streak-up tone
            PlayTone(400 + _service.Streak * 20, 150);
        }

        private void OnStreakChanged(int streak)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(() => OnStreakChanged(streak)); return; }

            _txtStreak.Text = streak.ToString();
            _txtBestStreak.Text = _service.BestStreak.ToString();

            UpdateVisualIntensity(streak);
        }

        private void OnStreakBroken()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(OnStreakBroken); return; }

            // Shake animation
            Shake(ShakeKeys);   // ShakeStoryboard

            // Play streak-break tone
            PlayTone(200, 300);

            // Cool down visuals
            UpdateVisualIntensity(0);
        }

        /// <summary>The stats line is hardcoded English in the WPF original too; no loc key exists for it.</summary>
        private void OnSessionComplete(int totalReps, int bestStreak)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Invoke(() => OnSessionComplete(totalReps, bestStreak)); return; }

            _sessionComplete = true;
            _idleTimer?.Stop();

            _txtCompletionStats.Text = $"{totalReps} repetitions  |  Best streak: {bestStreak}";
            _completionOverlay.IsVisible = true;
            _txtInput.IsEnabled = false;

            // Play completion tone
            PlayTone(523, 400);

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
            var mantra = _service.CurrentMantra;
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

            // Drone gain: 0.05 idle → 0.4 max
            _droneTargetGain = 0.05f + (float)t * 0.35f;
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

        /// <summary>WPF StartDrone: 90 Hz fundamental + 180 Hz harmonic at 0.4 of its gain.</summary>
        private void StartDrone()
        {
            try
            {
                _drone = DroneOpener(Platform.ToneWav.Drone());
                ApplyDroneVolume();
            }
            catch (Exception ex)
            {
                _drone = null;
                Serilog.Log.Warning(ex, "Failed to start mantra drone audio");
            }
        }

        /// <summary>WPF: the drone opens at a raw 0.05 (StartDrone) and only the ramp multiplies in
        /// MantraDroneVolume% (FloatTimer_Tick). The file's peak is 1.4 x the fundamental's.</summary>
        private void ApplyDroneVolume()
        {
            if (_drone == null) return;
            var masterVol = _droneRamped ? CoreSettings.Current.MantraDroneVolume / 100.0 : 1.0;
            _drone.Volume = Platform.ToneWav.VlcVolume(_droneCurrentGain * masterVol * Platform.ToneWav.DronePeak);
        }

        /// <summary>Panic (PanicSurfaces "mantra"): WPF KillAllAudio's "Stop mantra lab audio" ->
        /// Mantra.Dispose(), which leaves the open window inert (typing no longer counts; Esc closes).
        /// WPF's drone kept humming because Dispose never reached the window; here it stops too.</summary>
        internal static void StopForPanic()
        {
            _open?.StopDrone();
            App.Mantra.Dispose();
        }

        private static MantraWindow? _open;   // the opener refuses a second window, so one is enough

        private void StopDrone()
        {
            try { _drone?.Dispose(); } catch { }
            _drone = null;
        }

        private static void PlayTone(double frequency, int durationMs)
        {
            try { CoreAudio.PlayOneShot(Platform.ToneWav.Tone(frequency, durationMs), 0.15f, "mantra"); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to play mantra tone"); }
        }

        private void Cleanup()
        {
            if (_open == this) _open = null;
            _floatTimer?.Stop();
            _idleTimer?.Stop();
            StopDrone();

            _service.StreakChanged -= OnStreakChanged;
            _service.StreakBroken -= OnStreakBroken;
            _service.MantraCompleted -= OnMantraCompleted;
            _service.SessionComplete -= OnSessionComplete;

            if (_service.IsActive)
                _service.EndSession();
        }

        private void CleanupAndClose()
        {
            Cleanup();
            Close();
        }
    }
}
