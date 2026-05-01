using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;
using Microsoft.Win32;

namespace ConditioningControlPanel.Views.Deeper
{
    /// <summary>
    /// End-user runtime UI for Deeper enhancements.
    ///
    /// Pick an audio file → optionally pick (or auto-discover) a matching
    /// .ccpenh.json → press play. The host service binds the engine to the
    /// audio player's time source while playing, unbinds on stop. Any failure
    /// path falls back to plain audio playback (the engine just doesn't run);
    /// the user always gets to hear their file.
    /// </summary>
    public partial class EnhancementPlayerWindow : Window
    {
        private readonly EnhancementAudioPlayer _player;
        private readonly EnhancementHostService _host;
        private EnhancementAudioPlayerTimeSource? _timeSource;
        private DispatcherTimer? _uiTimer;
        private float[]? _peaks;
        private bool _isScrubbing;
        private bool _suppressVolumeSync;

        public EnhancementPlayerWindow(EnhancementAudioPlayer player, EnhancementHostService host)
        {
            InitializeComponent();
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _host = host ?? throw new ArgumentNullException(nameof(host));

            _player.Loaded += OnPlayerLoaded;
            _player.Ended += OnPlayerEnded;
            _host.Loaded += OnHostLoaded;
            _host.LoadFailed += OnHostLoadFailed;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            UpdateVolumeFromPlayer();
        }

        // -- File pickers ------------------------------------------------------

        private void BtnPickAudio_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = Loc.Get("deeper_player_pick_audio"),
                Filter = "Audio (*.mp3;*.wav;*.m4a;*.aac)|*.mp3;*.wav;*.m4a;*.aac|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            LoadAudio(dlg.FileName);
            TryAutoLoadEnhancement(dlg.FileName);
        }

        private void BtnPickEnhancement_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = Loc.Get("deeper_player_pick_enh"),
                Filter = "Deeper Enhancement (*.ccpenh.json)|*.ccpenh.json|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) != true) return;
            _host.LoadFromFile(dlg.FileName);
        }

        private void BtnUnloadEnhancement_Click(object sender, RoutedEventArgs e) => _host.Unload();

        private async void BtnLoadUrl_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new UrlPromptDialog { Owner = this };
            if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.Result)) return;

            TxtStatus.Text = Loc.Get("deeper_player_status_fetching_url");
            try
            {
                var enh = await App.DeeperFetcher.FetchAsync(dlg.Result).ConfigureAwait(true);
                if (enh == null)
                {
                    TxtStatus.Text = Loc.Get("deeper_player_status_url_failed");
                    return;
                }
                _host.LoadFromMemory(enh, dlg.Result);
                TxtStatus.Text = Loc.Get("deeper_player_status_url_loaded");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Player URL load error: {Error}", ex.Message);
                TxtStatus.Text = Loc.Get("deeper_player_status_url_failed");
            }
        }

        private void TryAutoLoadEnhancement(string audioPath)
        {
            // 1) Side-by-side: foo.mp3 → foo.ccpenh.json next to it.
            // 2) Library lookup by media_source pattern (Phase 10).
            try
            {
                var dir = Path.GetDirectoryName(audioPath);
                var baseName = Path.GetFileNameWithoutExtension(audioPath);
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(baseName))
                {
                    var candidate = Path.Combine(dir, baseName + ".ccpenh.json");
                    if (File.Exists(candidate))
                    {
                        _host.LoadFromFile(candidate);
                        return;
                    }
                }

                var match = App.EnhancementLibrary?.FindMatch(audioPath, Models.Deeper.MediaTypes.Audio);
                if (match != null) _host.LoadFromFile(match.FilePath);
            }
            catch { }
        }

        // -- Audio loading -----------------------------------------------------

        private async void LoadAudio(string path)
        {
            // Stop any in-flight playback so the new file replaces it cleanly.
            UnbindEngineIfRunning();
            _player.Stop();

            TxtAudioPath.Text = path;
            TxtStatus.Text = Loc.Get("deeper_player_status_loading_audio");
            await LoadWaveformAsync(path);

            if (!_player.Play(path))
            {
                TxtStatus.Text = Loc.Get("deeper_player_status_audio_failed");
                return;
            }

            TxtTotal.Text = FormatTime(_player.DurationMs / 1000.0);
            BtnPlayPause.Content = "⏸";
            BindEngineIfReady();
            TxtStatus.Text = Loc.Get("deeper_player_status_playing");
        }

        private async Task LoadWaveformAsync(string path)
        {
            _peaks = null;
            try
            {
                var data = await AudioWaveformCache.LoadAsync(path);
                _peaks = data.Peaks;
                RenderWaveform();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("EnhancementPlayer: waveform decode failed: {Error}", ex.Message);
                _peaks = null;
                WaveformPath.Data = null;
            }
        }

        // -- Transport ---------------------------------------------------------

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
                BtnPlayPause.Content = "▶";
            }
            else if (_player.IsPaused)
            {
                _player.Resume();
                BtnPlayPause.Content = "⏸";
            }
            else if (!string.IsNullOrEmpty(_player.CurrentPath))
            {
                _player.Play(_player.CurrentPath);
                BtnPlayPause.Content = "⏸";
                BindEngineIfReady();
            }
            else
            {
                BtnPickAudio_Click(sender, e);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            UnbindEngineIfRunning();
            _player.Stop();
            BtnPlayPause.Content = "▶";
            TxtCurrent.Text = "0:00";
            UpdatePlayhead(0);
            TxtStatus.Text = Loc.Get("deeper_player_status_stopped");
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressVolumeSync) return;
            _player.Volume = (int)e.NewValue;
        }

        private void UpdateVolumeFromPlayer()
        {
            try
            {
                _suppressVolumeSync = true;
                SliderVolume.Value = Math.Clamp(_player.Volume, 0, 100);
            }
            finally { _suppressVolumeSync = false; }
        }

        // -- UI tick (transport label, playhead) -------------------------------

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_isScrubbing) return;
            var ms = _player.CurrentTimeMs;
            TxtCurrent.Text = FormatTime(ms / 1000.0);
            UpdatePlayhead(_player.DurationMs > 0 ? (double)ms / _player.DurationMs : 0);
        }

        // -- Waveform render + scrub ------------------------------------------

        private void RenderWaveform()
        {
            if (_peaks == null || _peaks.Length == 0)
            {
                WaveformPath.Data = null;
                return;
            }

            var w = WaveformCanvas.ActualWidth;
            var h = WaveformCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var midY = h / 2.0;
            var amp = (h - 4) / 2.0;
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                int samples = Math.Min(_peaks.Length, Math.Max(64, (int)w));
                for (int i = 0; i < samples; i++)
                {
                    var x = (double)i / (samples - 1) * w;
                    var idx = (int)Math.Round((double)i / (samples - 1) * (_peaks.Length - 1));
                    var v = Math.Clamp(_peaks[idx], 0f, 1f);
                    ctx.BeginFigure(new Point(x, midY - v * amp), false, false);
                    ctx.LineTo(new Point(x, midY + v * amp), true, false);
                }
            }
            geom.Freeze();
            WaveformPath.Data = geom;
        }

        private void UpdatePlayhead(double frac)
        {
            var w = WaveformCanvas.ActualWidth;
            var h = WaveformCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var x = Math.Clamp(frac, 0, 1) * w;
            PlayheadLine.X1 = PlayheadLine.X2 = x;
            PlayheadLine.Y1 = 0;
            PlayheadLine.Y2 = h;
        }

        private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var w = WaveformCanvas.ActualWidth;
            if (w <= 0 || _player.DurationMs <= 0) return;
            var frac = Math.Clamp(e.GetPosition(WaveformCanvas).X / w, 0, 1);
            _player.Seek(frac * _player.DurationMs / 1000.0);
            UpdatePlayhead(frac);
        }

        // -- Engine binding ---------------------------------------------------

        private void BindEngineIfReady()
        {
            if (_host.LoadedEnhancement == null) return;
            // Audio host: build a fresh time source and let the host own its
            // attach/detach lifetime.
            _timeSource = new EnhancementAudioPlayerTimeSource(_player);
            _host.Bind(_timeSource,
                attach: () => _timeSource?.Attach(),
                detach: () => { _timeSource?.Detach(); _timeSource = null; });
        }

        private void UnbindEngineIfRunning()
        {
            if (!_host.IsRunning) return;
            _host.UnbindEngine();
        }

        // -- Player + host event handlers -------------------------------------

        private void OnPlayerLoaded(string path) { /* status updated in LoadAudio */ }

        private void OnPlayerEnded()
        {
            try
            {
                if (Dispatcher.CheckAccess()) HandlePlayerEnded();
                else Dispatcher.BeginInvoke(HandlePlayerEnded);
            }
            catch { }
        }

        private void HandlePlayerEnded()
        {
            UnbindEngineIfRunning();
            BtnPlayPause.Content = "▶";
            TxtStatus.Text = Loc.Get("deeper_player_status_ended");
        }

        private void OnHostLoaded(Models.Deeper.Enhancement? enh, string? path)
        {
            try
            {
                if (Dispatcher.CheckAccess()) UpdateHostUi(enh, path);
                else Dispatcher.BeginInvoke(() => UpdateHostUi(enh, path));
            }
            catch { }
        }

        private void UpdateHostUi(Models.Deeper.Enhancement? enh, string? path)
        {
            if (enh == null)
            {
                TxtEnhPath.Text = Loc.Get("deeper_player_no_enh");
                TxtEnhMetadata.Text = "";
                BtnUnloadEnhancement.Visibility = Visibility.Collapsed;
                UnbindEngineIfRunning();
                return;
            }
            TxtEnhPath.Text = path ?? "";
            var creator = string.IsNullOrEmpty(enh.Metadata?.Creator) ? "" : $" — {enh.Metadata.Creator}";
            var name = string.IsNullOrEmpty(enh.Metadata?.Name) ? "(untitled)" : enh.Metadata!.Name;
            var counts = $"{enh.Regions.Count} regions, {enh.HapticTracks.Sum(t => t?.Events?.Count ?? 0)} haptic events, {enh.Rules.Count} rules";
            TxtEnhMetadata.Text = $"{name}{creator}  ·  {counts}";
            BtnUnloadEnhancement.Visibility = Visibility.Visible;
            // If audio is already playing, attach the engine now.
            if (_player.IsPlaying) BindEngineIfReady();
        }

        private void OnHostLoadFailed(string reason)
        {
            try
            {
                if (Dispatcher.CheckAccess()) TxtStatus.Text = string.Format(Loc.Get("deeper_player_status_enh_failed_fmt"), reason);
                else Dispatcher.BeginInvoke(() => TxtStatus.Text = string.Format(Loc.Get("deeper_player_status_enh_failed_fmt"), reason));
            }
            catch { }
        }

        // -- Cleanup -----------------------------------------------------------

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _uiTimer?.Stop();
                _uiTimer = null;
                _player.Loaded -= OnPlayerLoaded;
                _player.Ended -= OnPlayerEnded;
                _host.Loaded -= OnHostLoaded;
                _host.LoadFailed -= OnHostLoadFailed;
                UnbindEngineIfRunning();
                _player.Stop();
            }
            catch { }
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds)) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}"
                : $"{ts.Minutes}:{ts.Seconds:00}";
        }
    }
}
