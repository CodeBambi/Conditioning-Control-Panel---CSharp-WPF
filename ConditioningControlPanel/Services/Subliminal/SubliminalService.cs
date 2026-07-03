using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NAudio.Wave;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Service for displaying subliminal text flashes across all monitors.
    /// Ported from Python engine.py _flash_subliminal / _show_subliminal_visuals
    /// </summary>
    public class SubliminalService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Random _random = new();

        // KEEP-ALIVE windows, one per screen: a subliminal "show" swaps the text content and
        // animates opacity on a persistent full-screen click-through window. Creating and
        // closing a layered window per subliminal (the old behavior) is render-thread churn
        // that can wedge WPF's shared render thread while other layered surfaces animate
        // (Application Hang 1002 — same fix as FlashService pooling / chaos overlays).
        // Keyed by screen DeviceName; bounds are re-asserted per show. Closed only in Dispose.
        private readonly Dictionary<string, Window> _screenWindows = new();
        // Per-window show generation: a new show on the same window invalidates the previous
        // storyboard's Completed (which would otherwise blank the new text early).
        private readonly Dictionary<Window, int> _showGeneration = new();

        // SOLID MODE (#461): subliminal cards rendered as children of the ONE shared
        // click-through host (ChaosBubbleHostOverlay) instead of the per-screen keep-alive
        // layered windows above — one less full-screen layered surface contending on the
        // shared WPF render thread. One card per screen, replaced outright by the next show.
        private sealed class HostedCard
        {
            public string ScreenKey = "";
            public Grid Root = null!;
            public Canvas TextCanvas = null!;
            public double OriginPxX, OriginPxY;   // screen's physical top-left
            public double Scale = 1.0;            // physical px per card-local DIP
        }
        private readonly List<HostedCard> _hostedCards = new();
        // One ref-counted hold on the shared host while any card could be up — NOT per show
        // (host churn is exactly what solid mode exists to remove). Released on Stop/Dispose,
        // or when a one-shot's card fades out with the service not running.
        private bool _hostRefHeld;
        private readonly string _audioPath;
        private string[]? _audioFilesCache;
        private DateTime _audioFilesCacheTime;
        private string[]? _modAudioFilesCache;
        private DateTime _modAudioFilesCacheTime;
        private string? _modAudioCacheModId;

        private WaveOutEvent? _audioPlayer;
        private AudioFileReader? _audioFile;

        private bool _isRunning;
        private bool _oneShotActive; // Allow one-shot display when service not running (remote control)
        private bool _disposed;
        private int _subliminalCount;

        public bool IsRunning => _isRunning;

        /// <summary>
        /// Fired when a subliminal is displayed
        /// </summary>
        public event EventHandler? SubliminalDisplayed;

        public SubliminalService()
        {
            _audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sub_audio");
            Directory.CreateDirectory(_audioPath);
            
            _timer = new DispatcherTimer();
            _timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// Start the subliminal service
        /// </summary>
        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            ScheduleNext();
            
            App.Logger?.Information("SubliminalService started");
        }

        /// <summary>
        /// Stop the subliminal service
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _timer.Stop();

            // Blank + hide the keep-alive windows (don't close them — a Stop can land
            // mid-chaos-run, and closing layered windows then is the deadlock trigger).
            foreach (var win in _screenWindows.Values)
            {
                try
                {
                    win.BeginAnimation(Window.OpacityProperty, null);
                    win.Opacity = 0;
                    win.Content = null;
                    win.Hide();
                }
                catch { }
            }

            // Solid mode: pull any hosted cards off the shared canvas and release the host ref
            // (removing canvas children is safe mid-run — only window churn deadlocks).
            RemoveHostedCard(_ => true);
            ReleaseHostRef();

            StopAudio();

            App.Logger?.Information("SubliminalService stopped");
        }

        /// <summary>
        /// Single authority for toggling the subliminal feature from any UI entry point
        /// (the Settings-tab checkbox and the feature popup both route here). Persists the
        /// flag and, when the engine is running, starts/stops the service — but only on an
        /// actual state transition, so a checkbox and popup that mirror each other can't
        /// churn Start()/Stop() between them.
        /// </summary>
        public void SetEnabled(bool on)
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            if (s.SubliminalEnabled != on)
                s.SubliminalEnabled = on;

            if (App.IsEngineRunning)
            {
                if (on && !_isRunning) Start();
                else if (!on && _isRunning) Stop();
            }

            App.Settings?.Save();
            App.Logger?.Information("Subliminals toggled: {Enabled}", on);
        }

        private void ScheduleNext()
        {
            if (!_isRunning || !App.Settings.Current.SubliminalEnabled) return;
            
            // Calculate interval based on frequency (messages per minute)
            var freq = Math.Max(1, App.Settings.Current.SubliminalFrequency);
            var baseInterval = 60.0 / freq; // seconds between messages
            
            // Add some randomness (±30%)
            var variance = baseInterval * 0.3;
            var interval = baseInterval + (_random.NextDouble() * variance * 2 - variance);
            interval = Math.Max(1, interval); // At least 1 second
            
            _timer.Interval = TimeSpan.FromSeconds(interval);
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _timer.Stop();

            if (!_isRunning || !App.Settings.Current.SubliminalEnabled)
                return;

            FlashSubliminal();
            ScheduleNext();
        }

        /// <summary>
        /// Display a subliminal flash
        /// </summary>
        public void FlashSubliminal()
        {
            if (!_isRunning) _oneShotActive = true; // Allow display from remote control
            var pool = App.Settings.Current.SubliminalPool;
            var activeTexts = pool.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();
            
            if (activeTexts.Count == 0)
            {
                App.Logger?.Debug("No active subliminal texts");
                return;
            }
            
            var text = activeTexts[_random.Next(activeTexts.Count)];
            
            // Check for linked audio
            string? audioPath = FindLinkedAudio(text);
            
            if (audioPath != null && App.Settings.Current.SubAudioEnabled)
            {
                // Duck other audio, play whisper, then show visual
                if (App.Settings.Current.AudioDuckingEnabled)
                    App.Audio.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(audioPath);
                
                // Haptic triggers 250ms before visual appears
                Task.Delay(50).ContinueWith(_ =>
                {
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(text);
                    Task.Delay(250).ContinueWith(__ =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(text));
                    });
                });

                // If "Bambi Freeze" was played, follow up with "Bambi Reset"
                if (text.Equals("Bambi Freeze", StringComparison.OrdinalIgnoreCase))
                {
                    ScheduleBambiReset();
                }
                App.Progression?.AddXP(20, XPSource.Subliminal);
            }
            else
            {
                TriggerSubliminalWithHapticPattern(text);
                App.Progression?.AddXP(10, XPSource.Subliminal);
            }
        }

        /// <summary>
        /// Flash a custom subliminal text (from remote control or Deeper engine).
        /// Sanitizes input and caps length. <paramref name="overrideDurationMs"/>
        /// (when supplied) overrides the global SubliminalDuration setting for
        /// this single flash — used by Deeper effects so the segment width on
        /// the timeline drives how long the text stays on screen.
        /// </summary>
        public void FlashSubliminalCustom(string text, int? opacity = null, int? overrideDurationMs = null, bool suppressHaptic = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > 200) text = text.Substring(0, 200);
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
            _oneShotActive = true; // Allow display even when service not running (remote control)
            TriggerSubliminalWithHapticPattern(text, opacity, overrideDurationMs, suppressHaptic);
            App.Progression?.AddXP(10, XPSource.Subliminal);
        }

        // Track if a deferred reset is pending (for when video ends) — accessed from timer/event callbacks
        private int _deferredResetPending; // 0 = false, 1 = true (for Interlocked)

        /// <summary>
        /// Trigger a Bambi Freeze subliminal with audio - used before videos and bubble count games
        /// </summary>
        /// <param name="deferReset">If true, don't schedule reset immediately (call TriggerDeferredBambiReset later)</param>
        public void TriggerBambiFreeze(bool deferReset = false)
        {
            if (!_isRunning && !App.Settings.Current.SubliminalEnabled)
            {
                // Still allow Bambi Freeze even if subliminals are disabled - it's a special trigger
                App.Logger?.Debug("Triggering Bambi Freeze (subliminals disabled but special trigger allowed)");
            }

            var text = App.Mods?.GetFreezeTriggerText() ?? "Freeze";
            string? audioPath = FindLinkedAudio(text);

            if (audioPath != null)
            {
                // Duck other audio, play whisper, then show visual
                if (App.Settings.Current.AudioDuckingEnabled)
                    App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(audioPath);

                // Haptic triggers 250ms before visual appears
                Task.Delay(50).ContinueWith(_ =>
                {
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(text);
                    Task.Delay(250).ContinueWith(__ =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(text));
                    });
                });

                // Handle reset scheduling
                if (deferReset)
                {
                    // Mark that we should trigger reset when video ends
                    Interlocked.Exchange(ref _deferredResetPending, 1);
                    App.Logger?.Information("Bambi Freeze triggered with audio (reset deferred until video ends)");
                }
                else
                {
                    // Schedule Bambi Reset after freeze with delay
                    ScheduleBambiReset();
                    App.Logger?.Information("Bambi Freeze triggered with audio");
                }
            }
            else
            {
                // No audio file, just show visual with haptic
                TriggerSubliminalWithHapticPattern(text);
                App.Logger?.Information("Bambi Freeze triggered (no audio file found)");
            }
        }

        /// <summary>
        /// Trigger the deferred Bambi Reset (called when video ends)
        /// </summary>
        public void TriggerDeferredBambiReset()
        {
            if (Interlocked.CompareExchange(ref _deferredResetPending, 0, 1) != 1)
            {
                return;
            }

            // 90% chance to trigger reset
            if (_random.NextDouble() > 0.90)
            {
                App.Logger?.Debug("Bambi Reset skipped (10% chance roll)");
                return;
            }

            // Trigger reset after a short delay (1-2 seconds after video ends)
            var delay = _random.Next(1000, 2000);
            Task.Delay(delay).ContinueWith(_ =>
            {
                Application.Current?.Dispatcher?.Invoke(() => PlayBambiReset());
            });
        }

        /// <summary>
        /// Schedule Bambi Reset to follow Bambi Freeze after a delay
        /// </summary>
        private void ScheduleBambiReset()
        {
            // 90% chance to trigger reset
            if (_random.NextDouble() > 0.90)
            {
                App.Logger?.Debug("Bambi Reset skipped (10% chance roll)");
                return;
            }

            // Wait 4-8 seconds then show Bambi Reset (longer delay than before)
            var delay = _random.Next(4000, 8000);
            Task.Delay(delay).ContinueWith(_ =>
            {
                Application.Current?.Dispatcher?.Invoke(() => PlayBambiReset());
            });
        }

        /// <summary>
        /// Play the Bambi Reset audio and visual
        /// </summary>
        private void PlayBambiReset()
        {
            var resetText = App.Mods?.GetResetTriggerText() ?? "Reset";
            string? resetAudio = FindLinkedAudio(resetText);

            if (resetAudio != null && App.Settings.Current.SubAudioEnabled)
            {
                if (App.Settings.Current.AudioDuckingEnabled)
                    App.Audio?.Duck(App.Settings.Current.DuckingLevel);
                PlayWhisperAudio(resetAudio);
                // Haptic triggers 250ms before visual appears
                Task.Delay(50).ContinueWith(_ =>
                {
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(resetText);
                    Task.Delay(250).ContinueWith(__ =>
                    {
                        Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(resetText));
                    });
                });
            }
            else
            {
                TriggerSubliminalWithHapticPattern(resetText);
            }

            App.Logger?.Debug("Bambi Reset triggered after Bambi Freeze");
        }

        /// <summary>
        /// Play the audio clip for a trigger phrase (if whispers enabled and audio exists)
        /// Used by Trigger Mode to play matching audio when showing trigger bubbles
        /// </summary>
        public void PlayTriggerAudio(string trigger)
        {
            // Check if whispers are enabled
            if (App.Settings?.Current?.SubAudioEnabled != true)
            {
                return;
            }

            var audioPath = FindLinkedAudio(trigger);
            if (audioPath != null)
            {
                // Duck other audio briefly
                if (App.Settings?.Current?.AudioDuckingEnabled == true)
                    App.Audio?.Duck(App.Settings?.Current?.DuckingLevel ?? 80);
                PlayWhisperAudio(audioPath);
                App.Logger?.Debug("TriggerMode: Playing audio for '{Trigger}'", trigger);
            }
        }

        private string? FindLinkedAudio(string text)
        {
            var cleanText = text.Trim();
            var extensions = new[] { ".mp3", ".wav", ".ogg", ".MP3", ".WAV", ".OGG" };

            // Try various case combinations
            var textVariants = new[]
            {
                cleanText,                          // As-is
                cleanText.ToUpper(),                // UPPERCASE
                cleanText.ToLower(),                // lowercase
                cleanText.Replace("\u2019", "'"),    // Normalize curly apostrophe to straight
                cleanText.Replace("'", "\u2019"),    // Normalize straight apostrophe to curly
                cleanText.ToUpper().Replace("\u2019", "'"),
            };

            // Check active mod's audio directory first
            var modAudioPath = GetModAudioPath();
            if (modAudioPath != null)
            {
                var result = SearchAudioDirectory(modAudioPath, cleanText, textVariants, extensions, isModCache: true);
                if (result != null) return result;
            }

            // Fall back to default sub_audio directory
            return SearchAudioDirectory(_audioPath, cleanText, textVariants, extensions, isModCache: false);
        }

        private string? GetModAudioPath()
        {
            var modPath = App.Mods?.ActiveMod?.InstalledPath;
            if (modPath == null) return null;

            var modAudioDir = Path.Combine(modPath, "resources", "sounds", "flashes_audio");
            return Directory.Exists(modAudioDir) ? modAudioDir : null;
        }

        private string? SearchAudioDirectory(string directory, string cleanText, string[] textVariants, string[] extensions, bool isModCache)
        {
            // Try exact filename match with case variants
            foreach (var textVar in textVariants)
            {
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(directory, textVar + ext);
                    if (File.Exists(path)) return path;
                }
            }

            // Fallback: case-insensitive directory search (cached to avoid per-subliminal disk scan)
            try
            {
                if (Directory.Exists(directory))
                {
                    string[]? files;
                    if (isModCache)
                    {
                        var currentModId = App.Mods?.ActiveMod?.Id;
                        if (_modAudioFilesCache == null || _modAudioCacheModId != currentModId ||
                            (DateTime.UtcNow - _modAudioFilesCacheTime).TotalSeconds > 60)
                        {
                            _modAudioFilesCache = Directory.GetFiles(directory);
                            _modAudioFilesCacheTime = DateTime.UtcNow;
                            _modAudioCacheModId = currentModId;
                        }
                        files = _modAudioFilesCache;
                    }
                    else
                    {
                        if (_audioFilesCache == null || (DateTime.UtcNow - _audioFilesCacheTime).TotalSeconds > 60)
                        {
                            _audioFilesCache = Directory.GetFiles(directory);
                            _audioFilesCacheTime = DateTime.UtcNow;
                        }
                        files = _audioFilesCache;
                    }

                    var normalizedText = cleanText.ToUpperInvariant().Replace("\u2019", "'");
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant().Replace("\u2019", "'");
                        if (fileName == normalizedText)
                        {
                            return file;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Error searching audio directory {Dir}: {Error}", directory, ex.Message);
            }

            return null;
        }

        private void PlayWhisperAudio(string path)
        {
            try
            {
                StopAudio();

                _audioFile = new AudioFileReader(path);
                _audioPlayer = new WaveOutEvent();
                App.Audio?.ApplyPreferredDevice(_audioPlayer);

                // Apply volume with curve, including master volume
                var masterVol = App.Settings.Current.MasterVolume / 100.0f;
                var subVol = App.Settings.Current.SubAudioVolume / 100.0f;
                var curvedVol = (float)Math.Pow(subVol * masterVol, 1.5);
                _audioFile.Volume = curvedVol;
                
                _audioPlayer.Init(_audioFile);
                // Capture duck generation so stale callbacks after ForceUnduck are ignored
                var duckGen = App.Audio?.DuckGeneration ?? -1;
                _audioPlayer.PlaybackStopped += (s, e) =>
                {
                    // Unduck after playback + small delay
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        try { App.Audio?.Unduck(duckGen); }
                        catch (Exception ex) { App.Logger?.Debug("Unduck failed in PlaybackStopped: {Error}", ex.Message); }
                    });
                };
                _audioPlayer.Play();
                // Tell the bark system a whisper is now audible so the companion won't talk over it.
                App.Audio?.MarkWhisperAudio(_audioFile.TotalTime.TotalSeconds);

                App.Logger?.Debug("Playing subliminal audio: {Path}", Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Could not play subliminal audio: {Error}", ex.Message);
                App.Audio.Unduck();
            }
        }

        private void StopAudio()
        {
            try
            {
                _audioPlayer?.Stop();
                _audioPlayer?.Dispose();
                _audioFile?.Dispose();
            }
            catch { }
            
            _audioPlayer = null;
            _audioFile = null;
        }

        /// <summary>
        /// Trigger haptic pattern before showing subliminal, then show visuals
        /// Pattern depends on the trigger text (Cum/Collapse = long, Freeze = short sharp, Sleep = decay, etc.)
        /// Buttplug.io has ~1.3s latency so we trigger haptics earlier for that provider
        /// </summary>
        private async void TriggerSubliminalWithHapticPattern(string text, int? opacity = null, int? overrideDurationMs = null, bool suppressHaptic = false)
        {
            try
            {
                // Get anticipation delay from haptic service (Buttplug needs ~1.3s, Lovense ~250ms).
                // When the haptic is suppressed (per-effect opt-out), there's nothing to anticipate,
                // so show the visual immediately.
                var anticipationMs = suppressHaptic ? 0 : (App.Haptics?.SubliminalAnticipationMs ?? 250);

                // Trigger haptic pattern first (pattern depends on text), unless suppressed.
                if (!suppressHaptic)
                    _ = App.Haptics?.TriggerSubliminalPatternAsync(text);

                // Wait for anticipation delay before showing visual
                if (anticipationMs > 0)
                    await Task.Delay(anticipationMs);

                // Now show on UI thread
                Application.Current?.Dispatcher?.Invoke(() => ShowSubliminalVisuals(text, opacity, overrideDurationMs));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("SubliminalService: TriggerSubliminalWithHapticPattern failed: {Error}", ex.Message);
            }
        }

        private void ShowSubliminalVisuals(string text, int? opacity = null, int? overrideDurationMs = null)
        {
            // Guard against delayed callbacks firing after Stop() — prevents orphaned windows
            // Allow one-shot from remote control even when service not running
            if (!_isRunning && !_oneShotActive) return;
            _oneShotActive = false;

            // Increment counter and fire event
            _subliminalCount++;
            SubliminalDisplayed?.Invoke(this, EventArgs.Empty);

            // Duration in frames * ~16.6ms per frame, minimum 100ms.
            // Caller may override (Deeper engine passes the timeline segment width).
            var durationMs = overrideDurationMs.HasValue
                ? Math.Max(100, overrideDurationMs.Value)
                : Math.Max(100, App.Settings.Current.SubliminalDuration * 17);
            var targetOpacity = (opacity ?? App.Settings.Current.SubliminalOpacity) / 100.0;

            // Colors from settings
            var bgColor = ParseColor(App.Settings.Current.SubBackgroundColor, Colors.Black);
            var textColor = ParseColor(App.Settings.Current.SubTextColor, Color.FromRgb(255, 0, 255)); // Magenta
            var borderColor = ParseColor(App.Settings.Current.SubBorderColor, Colors.White);
            var bgTransparent = App.Settings.Current.SubBackgroundTransparent;

            // Get all monitors and reuse (or lazily create) the keep-alive window per screen
            var screens = App.Settings.Current.DualMonitorEnabled
                ? App.GetAllScreensCached()
                : new[] { System.Windows.Forms.Screen.PrimaryScreen! };
            var stealsFocus = App.Settings.Current.SubliminalStealsFocus;

            // Solid mode can't steal focus (the shared host is NOACTIVATE by contract), so the
            // StealsFocus opt-in falls back to the classic per-screen windows.
            var useHost = App.Settings.Current.SubliminalSolidMode && !stealsFocus;

            foreach (var screen in screens)
            {
                if (screen == null) continue;
                // ShowHostedSubliminal reports whether the shared host could take this screen's
                // card; when it can't (host created primary-only before DualMonitorEnabled
                // flipped, or the host failed to come up), fall through to the classic window
                // so the subliminal is never silently invisible.
                if (useHost && ShowHostedSubliminal(screen, text, bgColor, textColor, borderColor,
                        bgTransparent, targetOpacity, durationMs))
                {
                    continue;
                }
                var win = GetOrCreateScreenWindow(screen);
                BuildSubliminalContent(win, text, bgColor, textColor, borderColor, bgTransparent);
                if (!win.IsVisible) win.Show();   // stays shown (transparent) between flashes; only
                                                  // hidden by Stop(), so this re-shows after a stop
                ApplyWindowStyles(win, screen.Bounds, stealsFocus);
                if (stealsFocus) win.Activate();
                PositionSubliminalText(win);
                AnimateSubliminal(win, targetOpacity, durationMs);
            }

            // Subliminal cards now record (capture-exclusion dropped), so the awareness OCR
            // relies on the text rect from GetActiveTextScreenRects to skip them. Force the OCR
            // rect cache to rebuild now instead of waiting out its 250ms window — a flash can be
            // shorter than that, and we don't want OCR reading our own text mid-flash.
            App.InvalidateCcpWindowRectsCache();
        }

        private const int WS_EX_LAYERED = 0x00080000;

        /// <summary>
        /// SOLID MODE (#461): render one subliminal card for <paramref name="screen"/> as a child
        /// of the shared click-through host instead of the keep-alive layered window. Same fade
        /// envelope; the card removes itself from the host canvas when the storyboard completes.
        /// Returns false when the host can't display this screen (caller falls back to classic).
        /// </summary>
        private bool ShowHostedSubliminal(System.Windows.Forms.Screen screen, string text,
            Color bgColor, Color textColor, Color borderColor, bool bgTransparent,
            double targetOpacity, int durationMs)
        {
            HostedCard? card = null;
            try
            {
                var key = screen.DeviceName ?? "primary";

                // One card per screen: a new show replaces the previous outright, matching the
                // classic path's content swap on the reused window.
                RemoveHostedCard(c => c.ScreenKey == key);

                var scale = GetMonitorDpi(screen) / 96.0;
                if (scale <= 0) scale = 1.0;
                var bounds = screen.Bounds;              // physical px
                double dipW = bounds.Width / scale;
                double dipH = bounds.Height / scale;

                var grid = new Grid
                {
                    Width = dipW,
                    Height = dipH,
                    Background = Brushes.Transparent,
                    IsHitTestVisible = false,
                    Opacity = 0
                };
                if (!bgTransparent)
                {
                    grid.Children.Add(new System.Windows.Shapes.Rectangle
                    {
                        Fill = new SolidColorBrush(bgColor),
                        IsHitTestVisible = false
                    });
                }

                // Same outlined-text construction as BuildSubliminalContent, but positioned now:
                // the card's size is known up front, no ActualWidth round-trip needed.
                var textCanvas = new Canvas { IsHitTestVisible = false };
                var fontSize = 120;
                var offsets = new (double x, double y)[]
                {
                    (-3, -3), (3, -3), (-3, 3), (3, 3),
                    (0, -4), (0, 4), (-4, 0), (4, 0)
                };
                var centerX = dipW / 2.0;
                var centerY = dipH / 2.0;
                foreach (var (ox, oy) in offsets)
                {
                    var borderText = CreateTextBlock(text, fontSize, borderColor);
                    borderText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(borderText, centerX - borderText.DesiredSize.Width / 2 + ox);
                    Canvas.SetTop(borderText, centerY - borderText.DesiredSize.Height / 2 + oy);
                    textCanvas.Children.Add(borderText);
                }
                var mainText = CreateTextBlock(text, fontSize, textColor);
                mainText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(mainText, centerX - mainText.DesiredSize.Width / 2);
                Canvas.SetTop(mainText, centerY - mainText.DesiredSize.Height / 2);
                textCanvas.Children.Add(mainText);
                grid.Children.Add(textCanvas);

                EnsureHostRef();

                // The host's stage bounds are fixed at creation. If it can't cover this screen
                // (created primary-only before DualMonitorEnabled flipped, or Show() failed and
                // _active is null), placing here would draw off-canvas — invisible with no error.
                if (!ChaosBubbleHostOverlay.CoversPoint(bounds.X + 8, bounds.Y + 8))
                {
                    if (!_isRunning && _hostedCards.Count == 0)
                        ReleaseHostRef();   // one-shot fallback must not strand the host hold
                    return false;
                }

                // Above hosted flashes (ZIndex 1000): the text must stay readable over an image.
                System.Windows.Controls.Panel.SetZIndex(grid, 1200);
                // Mixed-DPI: the host renders at ONE scale; a card on a differently-scaled screen
                // compensates with a LayoutTransform (same fix as the bubble/flash host visuals).
                var hostScale = ChaosBubbleHostOverlay.RenderScale;
                if (hostScale > 0 && Math.Abs(hostScale - scale) > 0.001)
                    grid.LayoutTransform = new ScaleTransform(scale / hostScale, scale / hostScale);
                ChaosBubbleHostOverlay.Add(grid);
                ChaosBubbleHostOverlay.Place(grid, bounds.X, bounds.Y);   // physical px
                ChaosBubbleHostOverlay.RaiseActive();   // classic path re-asserts HWND_TOPMOST per show

                card = new HostedCard
                {
                    ScreenKey = key,
                    Root = grid,
                    TextCanvas = textCanvas,
                    OriginPxX = bounds.X,
                    OriginPxY = bounds.Y,
                    Scale = scale
                };
                _hostedCards.Add(card);

                // Same envelope as AnimateSubliminal, driving the element's opacity.
                var fadeMs = TimeSpan.FromMilliseconds(50);
                var storyboard = new Storyboard();
                var fadeIn = new DoubleAnimation(0, targetOpacity, fadeMs);
                Storyboard.SetTarget(fadeIn, grid);
                Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
                storyboard.Children.Add(fadeIn);
                var hold = new DoubleAnimation(targetOpacity, targetOpacity, TimeSpan.FromMilliseconds(durationMs))
                {
                    BeginTime = fadeMs
                };
                Storyboard.SetTarget(hold, grid);
                Storyboard.SetTargetProperty(hold, new PropertyPath(UIElement.OpacityProperty));
                storyboard.Children.Add(hold);
                var fadeOut = new DoubleAnimation(targetOpacity, 0, fadeMs)
                {
                    BeginTime = fadeMs + TimeSpan.FromMilliseconds(durationMs)
                };
                Storyboard.SetTarget(fadeOut, grid);
                Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
                storyboard.Children.Add(fadeOut);

                storyboard.Completed += (s, e) =>
                {
                    // May already be gone (replaced by a newer show, or Stop) — removal is idempotent.
                    RemoveHostedCard(c => ReferenceEquals(c, card));
                    // A one-shot with the service not running must not hold the host forever.
                    if (!_isRunning && _hostedCards.Count == 0)
                        ReleaseHostRef();
                };
                storyboard.Begin();
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Subliminal ShowHostedSubliminal: {E}", ex.Message);
                // A throw after EnsureHostRef but before Begin() means no Completed callback
                // will ever run — pull the half-built card off the canvas (it would sit there
                // invisible forever) and don't strand a one-shot's host hold.
                if (card != null)
                    RemoveHostedCard(c => ReferenceEquals(c, card));
                if (!_isRunning && _hostedCards.Count == 0)
                    ReleaseHostRef();
                // Report failure so the caller renders this screen classically instead.
                return false;
            }
        }

        /// <summary>Detach matching hosted cards from the shared canvas (idempotent).</summary>
        private void RemoveHostedCard(Func<HostedCard, bool> match)
        {
            for (int i = _hostedCards.Count - 1; i >= 0; i--)
            {
                var card = _hostedCards[i];
                if (!match(card)) continue;
                _hostedCards.RemoveAt(i);
                try
                {
                    card.Root.BeginAnimation(UIElement.OpacityProperty, null);
                    ChaosBubbleHostOverlay.Remove(card.Root);
                }
                catch { }
            }
        }

        /// <summary>Take this service's single reference on the shared host.</summary>
        private void EnsureHostRef()
        {
            if (_hostRefHeld) return;
            _hostRefHeld = true;
            ChaosBubbleHostOverlay.EnsureCreated();
        }

        /// <summary>Release the host reference (the host closes only at its LAST owner's release).</summary>
        private void ReleaseHostRef()
        {
            if (!_hostRefHeld) return;
            _hostRefHeld = false;
            ChaosBubbleHostOverlay.CloseActive();
        }

        /// <summary>
        /// Get the keep-alive subliminal window for a screen, creating (and showing, at
        /// opacity 0) it on first use. The window shell is permanent — only its content
        /// and opacity change per subliminal. Bounds/styles are re-asserted per show by
        /// <see cref="ApplyWindowStyles"/> so resolution changes and the StealsFocus
        /// setting are picked up.
        /// </summary>
        private Window GetOrCreateScreenWindow(System.Windows.Forms.Screen screen)
        {
            var key = screen.DeviceName ?? "primary";
            if (_screenWindows.TryGetValue(key, out var cached))
            {
                if (cached.IsLoaded)
                    return cached;
                // Stale entry (the window was closed, or a display topology change left it unloaded).
                // Close it before replacing the slot so we don't orphan a live layered window that then
                // lingers until Dispose — accumulates across monitor plug/unplug cycles.
                try { cached.Close(); } catch { }
                _screenWindows.Remove(key);
            }

            // Use EXACTLY the same approach as OverlayService.GetWpfScreenBounds
            // which works correctly for multi-monitor DPI setups
            var bounds = screen.Bounds;
            double primaryScale = GetPrimaryMonitorDpi() / 96.0;

            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent, // Always transparent for click-through
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = bounds.X / primaryScale,
                Top = bounds.Y / primaryScale,
                Width = bounds.Width / primaryScale,
                Height = bounds.Height / primaryScale,
                Opacity = 0
            };

            win.Show();   // hwnd exists from here on; the window idles invisible between shows
            _screenWindows[key] = win;
            App.Logger?.Debug("Subliminal keep-alive window created for {Screen}", key);
            return win;
        }

        /// <summary>Swap in the text/background visuals for one show.</summary>
        private void BuildSubliminalContent(Window win, string text,
            Color bgColor, Color textColor, Color borderColor, bool bgTransparent)
        {
            // Use a Grid that stretches to fill the window (unlike Canvas with explicit size)
            var grid = new Grid
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            // Add colored background as child element if not transparent
            if (!bgTransparent)
            {
                var bgRect = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(bgColor),
                    IsHitTestVisible = false,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch
                };
                grid.Children.Add(bgRect);
            }

            // Create text container that centers content
            var textCanvas = new Canvas
            {
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            var fontSize = 120;

            // Border/outline offsets
            var offsets = new (double x, double y)[]
            {
                (-3, -3), (3, -3), (-3, 3), (3, 3),
                (0, -4), (0, 4), (-4, 0), (4, 0)
            };

            // Draw border text (positioned by PositionSubliminalText once sizes are known)
            foreach (var (ox, oy) in offsets)
            {
                var borderText = CreateTextBlock(text, fontSize, borderColor);
                borderText.Tag = (ox, oy, true); // Store offset and isBorder flag
                textCanvas.Children.Add(borderText);
            }

            // Draw main text
            var mainText = CreateTextBlock(text, fontSize, textColor);
            mainText.Tag = (0.0, 0.0, false); // No offset, not border
            textCanvas.Children.Add(mainText);

            grid.Children.Add(textCanvas);
            win.Content = grid;
        }

        /// <summary>
        /// Re-assert ex-styles, physical-pixel bounds, topmost band and capture-exclusion on
        /// the live hwnd. Runs per show: StealsFocus can change between shows, and bounds
        /// can change with display layout.
        /// </summary>
        private void ApplyWindowStyles(Window win, System.Drawing.Rectangle targetBounds, bool stealsFocus)
        {
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                if (hwnd == IntPtr.Zero) return;

                var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE)
                              | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_LAYERED;
                if (stealsFocus)
                    exStyle &= ~WS_EX_NOACTIVATE;   // mouse click-through, but allow focus steal
                else
                    exStyle |= WS_EX_NOACTIVATE;    // full click-through, no focus stealing
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

                // Position window using SetWindowPos with SWP_NOACTIVATE for robust no-focus behavior
                // Use physical pixel coordinates to bypass WPF DPI virtualization
                SetWindowPos(hwnd, HWND_TOPMOST,
                    targetBounds.X, targetBounds.Y, targetBounds.Width, targetBounds.Height,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);

                // Subliminal cards are intentionally LEFT in screen capture so they show up in the
                // user's screen recordings. The awareness OCR no longer relies on capture exclusion
                // to skip our own text — App.GetCcpWindowRectsCached lists this sized popup's rect and
                // ScreenOcrService drops any word inside it, so there's no OCR feedback loop. WDA_NONE
                // also clears any stale EXCLUDEFROMCAPTURE left on a pooled/reused window.
                SetWindowDisplayAffinity(hwnd, WDA_NONE);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Subliminal ApplyWindowStyles: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Physical virtual-desktop pixel rects of any subliminal text that is CURRENTLY visible
        /// (the keep-alive window is shown and mid-flash, opacity &gt; 0), padded. Subliminal cards
        /// are intentionally left in screen capture so they show up in the user's recordings, but the
        /// avatar's awareness OCR must still skip them. The full-screen keep-alive window is dropped
        /// from the exclusion set by the per-monitor span filter, so — mirroring BouncingText (#287) —
        /// only the small centered text region is excluded here. Returns empty when nothing is flashing.
        /// Must be called on the UI thread (reads live WPF visual state); consumed by
        /// <see cref="App.GetCcpWindowRectsCached"/>.
        /// </summary>
        public System.Drawing.Rectangle[] GetActiveTextScreenRects()
        {
            var rects = new List<System.Drawing.Rectangle>();
            try
            {
                foreach (var win in _screenWindows.Values)
                {
                    // Only while a flash is actually on screen — between flashes the window stays
                    // shown but fades to 0, and we must NOT permanently blind OCR to the centre.
                    if (win == null || !win.IsVisible || win.Opacity <= 0.01) continue;
                    if (win.Content is not Grid grid) continue;

                    Canvas? canvas = null;
                    foreach (var child in grid.Children)
                        if (child is Canvas c) { canvas = c; break; }
                    if (canvas == null) continue;

                    var hwnd = new System.Windows.Interop.WindowInteropHelper(win).Handle;
                    if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var wr)) continue;
                    double winDipW = win.ActualWidth;
                    if (winDipW <= 0) continue;
                    double scale = (wr.Right - wr.Left) / winDipW; // physical px per DIP

                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    bool any = false;
                    foreach (var child in canvas.Children)
                    {
                        if (child is not TextBlock tb || string.IsNullOrEmpty(tb.Text)) continue;
                        double l = Canvas.GetLeft(tb), t = Canvas.GetTop(tb);
                        if (double.IsNaN(l) || double.IsNaN(t)) continue;
                        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        double w = tb.DesiredSize.Width, h = tb.DesiredSize.Height;
                        if (w <= 0 || h <= 0) continue;
                        minX = Math.Min(minX, l); minY = Math.Min(minY, t);
                        maxX = Math.Max(maxX, l + w); maxY = Math.Max(maxY, t + h);
                        any = true;
                    }
                    if (!any) continue;

                    // Pad to absorb the OCR rect-cache staleness and fade frames.
                    const double pad = 40;
                    int left = wr.Left + (int)Math.Floor((minX - pad) * scale);
                    int top = wr.Top + (int)Math.Floor((minY - pad) * scale);
                    int right = wr.Left + (int)Math.Ceiling((maxX + pad) * scale);
                    int bottom = wr.Top + (int)Math.Ceiling((maxY + pad) * scale);
                    if (right > left && bottom > top)
                        rects.Add(new System.Drawing.Rectangle(left, top, right - left, bottom - top));
                }

                // Solid mode: hosted cards live on the shared host canvas, not in _screenWindows.
                // Card-local DIPs → physical via the screen origin + per-screen scale captured at
                // show time (the LayoutTransform makes one card DIP render as Scale physical px).
                foreach (var card in _hostedCards)
                {
                    if (card.Root.Opacity <= 0.01) continue;

                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    bool any = false;
                    foreach (var child in card.TextCanvas.Children)
                    {
                        if (child is not TextBlock tb || string.IsNullOrEmpty(tb.Text)) continue;
                        double l = Canvas.GetLeft(tb), t = Canvas.GetTop(tb);
                        if (double.IsNaN(l) || double.IsNaN(t)) continue;
                        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        double w = tb.DesiredSize.Width, h = tb.DesiredSize.Height;
                        if (w <= 0 || h <= 0) continue;
                        minX = Math.Min(minX, l); minY = Math.Min(minY, t);
                        maxX = Math.Max(maxX, l + w); maxY = Math.Max(maxY, t + h);
                        any = true;
                    }
                    if (!any) continue;

                    const double hostPad = 40;
                    int left = (int)Math.Floor(card.OriginPxX + (minX - hostPad) * card.Scale);
                    int top = (int)Math.Floor(card.OriginPxY + (minY - hostPad) * card.Scale);
                    int right = (int)Math.Ceiling(card.OriginPxX + (maxX + hostPad) * card.Scale);
                    int bottom = (int)Math.Ceiling(card.OriginPxY + (maxY + hostPad) * card.Scale);
                    if (right > left && bottom > top)
                        rects.Add(new System.Drawing.Rectangle(left, top, right - left, bottom - top));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Subliminal GetActiveTextScreenRects: {E}", ex.Message);
            }
            return rects.Count == 0 ? Array.Empty<System.Drawing.Rectangle>() : rects.ToArray();
        }

        /// <summary>Center the border/main text blocks for the current content + window size.</summary>
        private static void PositionSubliminalText(Window win)
        {
            try
            {
                if (win.Content is not Grid grid) return;
                Canvas? textCanvas = null;
                foreach (var child in grid.Children)
                    if (child is Canvas c) { textCanvas = c; break; }
                if (textCanvas == null) return;

                var centerX = win.ActualWidth / 2.0;
                var centerY = win.ActualHeight / 2.0;

                foreach (var child in textCanvas.Children)
                {
                    if (child is TextBlock tb && tb.Tag is (double ox, double oy, bool _))
                    {
                        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        Canvas.SetLeft(tb, centerX - tb.DesiredSize.Width / 2 + ox);
                        Canvas.SetTop(tb, centerY - tb.DesiredSize.Height / 2 + oy);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Subliminal PositionText: {E}", ex.Message);
            }
        }

        private double GetPrimaryMonitorDpi()
        {
            try
            {
                var primary = System.Windows.Forms.Screen.PrimaryScreen;
                if (primary != null)
                {
                    var hMonitor = MonitorFromPoint(new POINT { X = primary.Bounds.X + 1, Y = primary.Bounds.Y + 1 }, 2);
                    if (hMonitor != IntPtr.Zero)
                    {
                        var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                        if (result == 0)
                        {
                            return dpiX;
                        }
                    }
                }
            }
            catch { }
            return 96.0;
        }

        private double GetMonitorDpi(System.Windows.Forms.Screen screen)
        {
            try
            {
                // Get a point inside this monitor
                var hMonitor = MonitorFromPoint(new POINT { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
                if (hMonitor != IntPtr.Zero)
                {
                    var result = GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
                    if (result == 0)
                    {
                        return dpiX;
                    }
                }
            }
            catch { }
            return 96.0;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        private TextBlock CreateTextBlock(string text, double fontSize, Color color)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Arial"),
                Foreground = new SolidColorBrush(color),
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false
            };
        }

        private void AnimateSubliminal(Window win, double targetOpacity, int holdMs)
        {
            var fadeInDuration = TimeSpan.FromMilliseconds(50);
            var holdDuration = TimeSpan.FromMilliseconds(holdMs);
            var fadeOutDuration = TimeSpan.FromMilliseconds(50);

            var storyboard = new Storyboard();

            // Fade in
            var fadeIn = new DoubleAnimation(0, targetOpacity, fadeInDuration);
            Storyboard.SetTarget(fadeIn, win);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(fadeIn);

            // Hold (stay at target opacity)
            var hold = new DoubleAnimation(targetOpacity, targetOpacity, holdDuration)
            {
                BeginTime = fadeInDuration
            };
            Storyboard.SetTarget(hold, win);
            Storyboard.SetTargetProperty(hold, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(hold);

            // Fade out
            var fadeOut = new DoubleAnimation(targetOpacity, 0, fadeOutDuration)
            {
                BeginTime = fadeInDuration + holdDuration
            };
            Storyboard.SetTarget(fadeOut, win);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(Window.OpacityProperty));
            storyboard.Children.Add(fadeOut);

            // A newer show on this window invalidates this storyboard's cleanup — otherwise
            // the old Completed would blank the new text early.
            _showGeneration.TryGetValue(win, out var g);
            var myGeneration = g + 1;
            _showGeneration[win] = myGeneration;

            storyboard.Completed += (s, e) =>
            {
                if (_showGeneration.TryGetValue(win, out var current) && current != myGeneration)
                    return;
                // Detach animation clocks (Storyboard.SetTarget clocks pin the Window) and blank
                // the window — but DO NOT Hide() it between flashes. A hidden AllowsTransparency
                // window keeps its last layered bitmap, and the next Show() re-presents that stale
                // frame (the PREVIOUS phrase) for a frame or two before WPF repaints — that's the
                // "previous-then-next" double. Staying shown at Opacity 0 with null content keeps
                // the surface live and blank so the next flash swaps in cleanly. The window is only
                // ever closed at Stop()/Dispose() (closing mid-run can deadlock the render thread).
                win.BeginAnimation(Window.OpacityProperty, null);
                win.Opacity = 0;
                win.Content = null;
            };

            // Detach any still-running previous clocks before starting the new pass.
            win.BeginAnimation(Window.OpacityProperty, null);
            storyboard.Begin();
        }

        private Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return fallback;
                if (!hex.StartsWith("#")) hex = "#" + hex;
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                return fallback;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();
            // App shutdown: the only place the keep-alive windows actually close.
            foreach (var win in _screenWindows.Values)
            {
                try { win.Close(); } catch { }
            }
            _screenWindows.Clear();
            _showGeneration.Clear();
            StopAudio();
        }

        #region Win32

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private const uint WDA_NONE = 0x0;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

        #endregion
    }
}
