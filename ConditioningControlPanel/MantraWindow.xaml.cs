using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ConditioningControlPanel
{
    public partial class MantraWindow : Window
    {
        private readonly Services.MantraService _service;
        private DispatcherTimer? _floatTimer;
        private DispatcherTimer? _idleTimer;
        private DateTime _startTime;
        private bool _sessionComplete;
        private bool _updatingInput;

        // Per-character highlight state
        private readonly List<Run> _mantraRuns = new();
        private int _prevMatchCount;
        private int _prevInputLength;
        private Color _highlightColor = Color.FromRgb(0x99, 0x88, 0xDD);
        private static readonly Color DimColor = Color.FromRgb(0x35, 0x35, 0x50);
        private static readonly Color ErrorColor = Color.FromRgb(0xFF, 0x44, 0x44);
        private static readonly Color FlashColor = Colors.White;

        // Drone audio
        private WaveOutEvent? _droneOutput;
        private MixingSampleProvider? _droneMixer;
        private SignalGenerator? _droneFundamental;
        private SignalGenerator? _droneHarmonic;
        private float _droneTargetGain = 0.05f;
        private float _droneCurrentGain = 0.05f;

        public MantraWindow()
        {
            InitializeComponent();
            _service = App.Mantra;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _startTime = DateTime.UtcNow;

            // Subscribe to service events
            _service.StreakChanged += OnStreakChanged;
            _service.StreakBroken += OnStreakBroken;
            _service.MantraCompleted += OnMantraCompleted;
            _service.SessionComplete += OnSessionComplete;

            // Build initial letter display
            BuildMantraRuns(_service.CurrentMantra ?? "");
            TxtTarget.Text = $"/{_service.TargetCount}";
            TxtCompletions.Text = "0";
            TxtStreak.Text = "0";
            TxtBestStreak.Text = "0";

            // Start float animation (gentle sine-wave drift)
            _floatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _floatTimer.Tick += FloatTimer_Tick;
            _floatTimer.Start();

            // Start idle timer (5s inactivity breaks streak)
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _idleTimer.Tick += IdleTimer_Tick;
            _idleTimer.Start();

            // Start glow pulse
            var glowPulse = (Storyboard)FindResource("GlowPulseStoryboard");
            glowPulse.Begin();

            // Start drone audio
            StartDrone();

            // Focus input
            TxtInput.Focus();
        }

        #region Per-character highlight system

        private void BuildMantraRuns(string mantra)
        {
            TxtMantra.Inlines.Clear();
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
                TxtMantra.Inlines.Add(run);
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
                var pulse = (Storyboard)FindResource("LetterPulseStoryboard");
                pulse.Begin();
            }

            // Wrong char typed → shake
            if (newCharTyped && hasError && matchCount == _prevMatchCount)
            {
                var shake = (Storyboard)FindResource("WrongShakeStoryboard");
                shake.Begin();
            }

            _prevMatchCount = matchCount;
            _prevInputLength = input.Length;

            return matchCount;
        }

        #endregion

        private void FloatTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
            MantraTranslate.Y = Math.Sin(elapsed * 0.5) * 6;

            // Smoothly ramp drone gain
            if (_droneFundamental != null && Math.Abs(_droneCurrentGain - _droneTargetGain) > 0.001f)
            {
                _droneCurrentGain += (_droneTargetGain - _droneCurrentGain) * 0.02f;
                var masterVol = (float)((App.Settings?.Current?.MantraDroneVolume ?? 30) / 100.0);
                _droneFundamental.Gain = _droneCurrentGain * masterVol;
                if (_droneHarmonic != null)
                    _droneHarmonic.Gain = _droneCurrentGain * 0.4f * masterVol;
            }
        }

        private void IdleTimer_Tick(object? sender, EventArgs e)
        {
            if (_service.IsActive && _service.Streak > 0)
            {
                _service.BreakStreak();
            }
        }

        private void TxtInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_updatingInput || _sessionComplete || !_service.IsActive) return;

            // Reset idle timer
            _idleTimer?.Stop();
            _idleTimer?.Start();

            var input = TxtInput.Text;
            var target = _service.CurrentMantra;
            if (target == null) return;

            int matchCount = UpdateHighlights(input);

            // Check completion: all characters match and input length equals mantra length
            if (matchCount == target.Length && input.Length == target.Length)
            {
                if (_service.TryCompleteMantra())
                {
                    _updatingInput = true;
                    TxtInput.Text = "";
                    _updatingInput = false;
                }
            }
        }

        private void TxtInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Block paste (Ctrl+V), copy (Ctrl+C), select all (Ctrl+A)
            if (Keyboard.Modifiers == ModifierKeys.Control &&
                (e.Key == Key.V || e.Key == Key.C || e.Key == Key.A))
            {
                e.Handled = true;
            }
        }

        private void OnMantraCompleted()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnMantraCompleted); return; }

            // Rebuild runs for the new mantra (CurrentMantra already updated before event fires)
            BuildMantraRuns(_service.CurrentMantra ?? "");
            TxtCompletions.Text = _service.Completions.ToString();

            // Full pulse animation on completion
            var pulse = (Storyboard)FindResource("PulseStoryboard");
            pulse.Begin();

            // Play streak-up tone
            PlayTone(400 + _service.Streak * 20, 150);
        }

        private void OnStreakChanged(int streak)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => OnStreakChanged(streak)); return; }

            TxtStreak.Text = streak.ToString();
            TxtBestStreak.Text = _service.BestStreak.ToString();

            UpdateVisualIntensity(streak);
        }

        private void OnStreakBroken()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(OnStreakBroken); return; }

            // Shake animation
            var shake = (Storyboard)FindResource("ShakeStoryboard");
            shake.Begin();

            // Play streak-break tone
            PlayTone(200, 300);

            // Cool down visuals
            UpdateVisualIntensity(0);
        }

        private void OnSessionComplete(int totalReps, int bestStreak)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(() => OnSessionComplete(totalReps, bestStreak)); return; }

            _sessionComplete = true;
            _idleTimer?.Stop();

            TxtCompletionStats.Text = $"{totalReps} repetitions  |  Best streak: {bestStreak}";
            CompletionOverlay.Visibility = Visibility.Visible;
            TxtInput.IsEnabled = false;

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
            var input = TxtInput.Text;
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
            ColorWashOverlay.Opacity = t * 0.8;
            WashCenter.Color = LerpColor(Color.FromRgb(0x66, 0x33, 0xAA), Color.FromRgb(0xFF, 0x69, 0xB4), t);

            // Glow intensity
            MantraGlow.BlurRadius = 20 + t * 30;
            MantraGlow.Opacity = 0.6 + t * 0.4;
            MantraGlow.Color = LerpColor(Color.FromRgb(0x99, 0x66, 0xCC), Color.FromRgb(0xFF, 0x69, 0xB4), t);

            // Input border glow
            InputBorderBrush.Color = LerpColor(Color.FromArgb(0x40, 0xFF, 0x69, 0xB4), Color.FromArgb(0xFF, 0xFF, 0x69, 0xB4), t);

            // Base gradient warm up
            BaseCenter.Color = LerpColor(Color.FromRgb(0x1A, 0x0A, 0x2E), Color.FromRgb(0x2E, 0x0A, 0x2E), t);

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

        private void StartDrone()
        {
            try
            {
                _droneFundamental = new SignalGenerator(44100, 1)
                {
                    Type = SignalGeneratorType.Sin,
                    Frequency = 90,
                    Gain = 0.05f
                };

                _droneHarmonic = new SignalGenerator(44100, 1)
                {
                    Type = SignalGeneratorType.Sin,
                    Frequency = 180,
                    Gain = 0.02f
                };

                _droneMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
                _droneMixer.AddMixerInput(_droneFundamental);
                _droneMixer.AddMixerInput(_droneHarmonic);

                _droneOutput = new WaveOutEvent();
                _droneOutput.Init(_droneMixer);
                _droneOutput.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to start mantra drone audio");
            }
        }

        private void StopDrone()
        {
            try
            {
                _droneOutput?.Stop();
                _droneOutput?.Dispose();
                _droneOutput = null;
                _droneMixer = null;
                _droneFundamental = null;
                _droneHarmonic = null;
            }
            catch { }
        }

        private void PlayTone(double frequency, int durationMs)
        {
            try
            {
                var gen = new SignalGenerator(44100, 1)
                {
                    Type = SignalGeneratorType.Sin,
                    Frequency = frequency,
                    Gain = 0.15f
                };

                var take = new OffsetSampleProvider(gen)
                {
                    Take = TimeSpan.FromMilliseconds(durationMs)
                };

                var output = new WaveOutEvent();
                output.Init(take);
                output.PlaybackStopped += (_, _) =>
                {
                    try { output.Dispose(); } catch { }
                };
                output.Play();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Failed to play mantra tone");
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
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
            _idleTimer?.Stop();
            StopDrone();

            _service.StreakChanged -= OnStreakChanged;
            _service.StreakBroken -= OnStreakBroken;
            _service.MantraCompleted -= OnMantraCompleted;
            _service.SessionComplete -= OnSessionComplete;

            if (_service.IsActive)
                _service.EndSession();

            Close();
        }
    }
}
