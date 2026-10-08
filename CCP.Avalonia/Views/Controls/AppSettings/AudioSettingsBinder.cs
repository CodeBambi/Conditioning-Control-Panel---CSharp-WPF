using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.AppSettings
{
    /// <summary>
    /// The global audio dials, bound once for each surface that shows them: Settings · Audio and the
    /// dashboard's audio card. WPF keeps one canonical set of controls in Settings and mirrors the
    /// dashboard through it (MainWindow.HomeAudio.cs) so there is one code path that changes master
    /// volume. Here that one path is this class: every surface writes settings through it, and every
    /// attached surface repaints from settings when any of them changes - the same single owner,
    /// without the echo guards. Handlers are WPF's MainWindow.UiUpdates.cs:1022-1176.
    /// </summary>
    internal sealed class AudioSettingsBinder
    {
        /// <summary>Raised on the UI thread after any surface writes an audio setting.</summary>
        internal static event Action? Changed;

        internal static Func<IReadOnlyList<LibVlcAudio.OutputDevice>> Enumerate = () => LibVlcAudio.EnumerateOutputDevices();
        internal static Func<Window?, string, Task> ShowDiagnostics = (owner, text) =>
            owner is null ? Task.CompletedTask : Dialogs.MessageDialog.ShowAsync(owner, "Audio Diagnostics", text);

        private static IReadOnlyList<LibVlcAudio.OutputDevice>? _devices;
        private static bool _testingAudio; // WPF: a second click while the probe runs is ignored

        private readonly Control _owner;
        private readonly Slider _master, _video, _duck;
        private readonly TextBlock _txtMaster, _txtVideo, _txtDuck;
        private readonly CheckBox _duckOn, _exclude;
        private readonly ComboBox _output;
        private SettingsService? _service;
        private bool _syncing;

        internal AudioSettingsBinder(Control owner, Slider master, TextBlock txtMaster, Slider video, TextBlock txtVideo,
            CheckBox duckOn, Slider duck, TextBlock txtDuck, CheckBox exclude, ComboBox output, Button refresh, Button test)
        {
            (_owner, _master, _txtMaster, _video, _txtVideo, _duckOn, _duck, _txtDuck, _exclude, _output) =
                (owner, master, txtMaster, video, txtVideo, duckOn, duck, txtDuck, exclude, output);

            master.ValueChanged += (_, e) => Edit(s => s.MasterVolume = (int)e.NewValue, () =>
            {
                LayeredAudio.Instance?.SetMasterVolumeLive();
                MandatoryVideoOverlay.Instance.UpdateVolume();
            });
            video.ValueChanged += (_, e) => Edit(s => s.VideoVolume = (int)e.NewValue, MandatoryVideoOverlay.Instance.UpdateVolume);
            duck.ValueChanged += (_, e) => Edit(s => s.DuckingLevel = (int)e.NewValue);
            duckOn.IsCheckedChanged += (_, _) => Edit(s => s.AudioDuckingEnabled = duckOn.IsChecked ?? true, () =>
            {
                // WPF: turning ducking off restores every app we ducked, now.
                if (duckOn.IsChecked == false) LibVlcAudio.Instance?.ForceUnduck();
            });
            exclude.IsCheckedChanged += (_, _) => Edit(s => s.ExcludeBambiCloudFromDucking = exclude.IsChecked ?? true);
            output.SelectionChanged += (_, _) => SelectDevice();
            refresh.Click += (_, _) => RefreshDevices();
            test.Click += async (_, _) => await TestAudioAsync(TopLevel.GetTopLevel(_owner) as Window);

            owner.AttachedToVisualTree += (_, _) =>
            {
                Changed += Sync;
                _service = CoreSettings.Service;
                if (_service != null) _service.CurrentReplaced += OnReplaced;
                if (_devices is null) RefreshDevices(); else Sync();
            };
            owner.DetachedFromVisualTree += (_, _) =>
            {
                Changed -= Sync;
                if (_service != null) _service.CurrentReplaced -= OnReplaced;
                _service = null;
            };
        }

        private void OnReplaced() => Dispatcher.UIThread.Post(Sync);

        /// <summary>Paints every control from settings without raising an edit.</summary>
        internal void Sync()
        {
            var s = CoreSettings.Current;
            _syncing = true;
            try
            {
                // Only move a slider that disagrees, so a drag in progress is never yanked back.
                if ((int)_master.Value != s.MasterVolume) _master.Value = s.MasterVolume;
                if ((int)_video.Value != s.VideoVolume) _video.Value = s.VideoVolume;
                if ((int)_duck.Value != s.DuckingLevel) _duck.Value = s.DuckingLevel;
                _txtMaster.Text = $"{s.MasterVolume}%";
                _txtVideo.Text = $"{s.VideoVolume}%";
                _txtDuck.Text = $"{s.DuckingLevel}%";
                _duckOn.IsChecked = s.AudioDuckingEnabled;
                _exclude.IsChecked = s.ExcludeBambiCloudFromDucking;

                var devices = _devices ?? Array.Empty<LibVlcAudio.OutputDevice>();
                if (!ReferenceEquals(_output.ItemsSource, devices)) _output.ItemsSource = devices;
                // WPF: prefer the id, fall back to the name (ids change after a driver reinstall).
                _output.SelectedItem =
                    devices.FirstOrDefault(d => s.AudioOutputDeviceId.Length > 0 && d.Id == s.AudioOutputDeviceId)
                    ?? devices.FirstOrDefault(d => s.AudioOutputDeviceName.Length > 0
                                                   && string.Equals(d.Name, s.AudioOutputDeviceName, StringComparison.OrdinalIgnoreCase))
                    ?? devices.FirstOrDefault();
            }
            finally { _syncing = false; }
        }

        private void Edit(Action<Models.AppSettings> write, Action? live = null)
        {
            if (_syncing) return;
            write(CoreSettings.Current);
            CoreSettings.Save(); // debounced, so per slider tick is fine (WPF ApplySettingsLive)
            try { live?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "[Audio] live apply"); }
            Changed?.Invoke();
        }

        private void SelectDevice()
        {
            if (_syncing || _output.SelectedItem is not LibVlcAudio.OutputDevice dev) return;
            var s = CoreSettings.Current;
            if (s.AudioOutputDeviceId == dev.Id && s.AudioOutputDeviceName == dev.Name) return;
            s.AudioOutputDeviceId = dev.Id;
            s.AudioOutputDeviceName = dev.Name;
            CoreSettings.Save();
            Log.Information("Audio output device set to '{Name}' (id={Id})", dev.Name, dev.Id.Length == 0 ? "(default)" : dev.Id);
            Changed?.Invoke();
        }

        /// <summary>The ↻ button, and the first time any surface is shown. Every surface shares the list.</summary>
        private static void RefreshDevices()
        {
            _devices = Enumerate();
            Changed?.Invoke();
        }

        internal static async Task TestAudioAsync(Window? owner)
        {
            if (_testingAudio) return;
            _testingAudio = true;
            try
            {
                // Off the UI thread, as WPF (#686): the probe walks folders and asks the sound server.
                var result = await Task.Run(BuildDiagnostics);
                Log.Information("[AudioDiag] Test requested:\n{Result}", result);
                await ShowDiagnostics(owner, result);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[AudioDiag] Test failed");
                await ShowDiagnostics(owner, $"Audio diagnostics failed: {ex.Message}");
            }
            finally { _testingAudio = false; }
        }

        /// <summary>WPF AudioService.TestAudioPlayback, with LibVLC in place of WaveOut.</summary>
        internal static string BuildDiagnostics()
        {
            var baseDir = AppContext.BaseDirectory;
            var soundsDir = Path.Combine(baseDir, "Resources", "sounds");
            var sb = new StringBuilder("=== Audio Diagnostics ===\n");
            CountLine(sb, soundsDir, "Resources/sounds/", SearchOption.AllDirectories);
            CountLine(sb, Path.Combine(baseDir, "Resources", "sub_audio"), "Resources/sub_audio/", SearchOption.TopDirectoryOnly);

            if (CoreAudio.PlayOneShotProvider is null)
            {
                sb.AppendLine("Audio device: FAILED (LibVLC is not loaded - install libvlc / VLC)");
                return sb.ToString();
            }
            var s = CoreSettings.Current;
            sb.AppendLine($"Audio device: OK ({(s.AudioOutputDeviceName.Length > 0 ? s.AudioOutputDeviceName : "system default")})");

            var playFile = new[] { "chime1.mp3", "lvup.mp3", Path.Combine("bubbles", "Pop.mp3") }
                .Select(f => Path.Combine(soundsDir, f)).FirstOrDefault(File.Exists);
            if (playFile is null)
            {
                sb.AppendLine("WARNING: No test sound files found to play");
                return sb.ToString();
            }
            CoreAudio.PlayOneShot(playFile, 0.5f, "audio-test"); // fixed 50%, bypasses the master curve
            sb.AppendLine($"Playing: {Path.GetFileName(playFile)} at 50% volume");
            sb.AppendLine("If you can't hear this, check your system volume mixer.");

            sb.AppendLine($"\nMaster Volume: {s.MasterVolume}%");
            sb.AppendLine($"Whispers Enabled: {s.SubAudioEnabled}");
            sb.AppendLine($"Whispers Muted: {s.SubAudioMuted}");
            sb.AppendLine($"Whisper Volume: {s.SubAudioVolume}%");
            sb.AppendLine($"Flash Audio Enabled: {s.FlashAudioEnabled}");
            var effectiveWhisperVol = Math.Pow((s.SubAudioVolume / 100.0) * (s.MasterVolume / 100.0), 1.5) * 100;
            sb.AppendLine($"Effective Whisper Volume: {effectiveWhisperVol:F1}%");
            return sb.ToString();
        }

        private const int FileCountProbeCap = 5000; // diagnostic only: never walk a whole tree (#686)

        private static void CountLine(StringBuilder sb, string dir, string label, SearchOption option)
        {
            if (!Directory.Exists(dir)) { sb.AppendLine($"WARNING: {label} directory MISSING"); return; }
            var n = Directory.EnumerateFiles(dir, "*", option).Take(FileCountProbeCap + 1).Count();
            sb.AppendLine($"{label}: {(n > FileCountProbeCap ? $"{FileCountProbeCap}+" : n.ToString())} files");
        }
    }
}
