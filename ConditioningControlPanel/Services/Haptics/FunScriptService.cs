using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics.Core;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// PHASE F — FUNSCRIPT PLAYBACK. Zero-config: when a video starts, look for a script beside it
    /// (<c>&lt;video&gt;.funscript</c>, then <c>&lt;video-dir&gt;\funscripts\&lt;video&gt;.funscript</c>).
    /// If there is one, drive the toy from it for as long as that video plays; if there is not,
    /// this service costs one File.Exists per video and says nothing.
    ///
    /// RENDERING
    ///  - Position actuators (Solace Pro): absolute placement, sampled ~300 ms AHEAD of the
    ///    playhead because the hardware coasts about that long after a target arrives
    ///    (see the Lovense cheat-sheet in docs/HAPTICS_OVERHAUL_PLAN.md).
    ///  - Everything else: the standard stroke→vibration conversion — intensity tracks stroke
    ///    SPEED, so faster strokes feel stronger — pushed onto <see cref="HapticLayer.Pattern"/>.
    ///    That layer is the authored-envelope layer, so a script cannot stomp AudioSync or Manual.
    ///
    /// SYNC. <see cref="VideoService.PrimaryPlaybackTimeMsChanged"/> only ticks a few times a
    /// second, so the render loop EXTRAPOLATES from the last report with the wall clock and resyncs
    /// on every report (a seek is just a report that disagrees with the extrapolation). If reports
    /// stop for <see cref="StaleMs"/> the video is paused or gone, and the layer is zeroed.
    ///
    /// Owned and constructed by <see cref="Services.HapticService"/>: Phase F does not touch
    /// App.xaml.cs, so VideoService calls <see cref="OnVideoStarted"/> / <see cref="OnVideoStopped"/>
    /// through <c>App.Haptics.FunScript</c>.
    /// </summary>
    public sealed class FunScriptService : IDisposable
    {
        /// <summary>Position targets are sampled this far ahead of the playhead (coast + spin-up).</summary>
        public const int PositionLeadMs = 300;
        /// <summary>Render cadence. Faster than the mixer's 10 Hz output loop so quantisation, not
        /// sampling, is what limits resolution.</summary>
        public const int RenderIntervalMs = 50;
        /// <summary>No playback report for this long = paused/stopped, so stop driving the toy.</summary>
        public const int StaleMs = 900;

        private readonly Services.HapticService _haptics;
        private readonly HapticSettings _settings;
        private readonly object _gate = new();

        private FunScript? _script;
        private string? _scriptPath;
        private Timer? _timer;
        private bool _subscribed;
        private long _lastReportMs = -1;
        private long _lastReportAt;
        private double _lastPositionSent = -1;
        private bool _vibeLayerOwned;
        private bool _disposed;

        /// <summary>True while a script is loaded and following a video.</summary>
        public bool IsActive { get { lock (_gate) return _script != null; } }

        /// <summary>Full path of the loaded script, for diagnostics/UI. Null when none.</summary>
        public string? LoadedScriptPath { get { lock (_gate) return _scriptPath; } }

        public FunScriptService(Services.HapticService haptics, HapticSettings settings)
        {
            _haptics = haptics;
            _settings = settings;
        }

        // ===================================================================== discovery

        /// <summary>
        /// Candidate script paths for a video, in priority order. Public + static so the lookup
        /// rule is testable and so the Phase E UI can show "we looked here".
        /// </summary>
        public static string[] CandidatePaths(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath)) return Array.Empty<string>();
            try
            {
                var dir = Path.GetDirectoryName(videoPath);
                var stem = Path.GetFileNameWithoutExtension(videoPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem)) return Array.Empty<string>();

                return new[]
                {
                    Path.Combine(dir, stem + ".funscript"),
                    Path.Combine(dir, "funscripts", stem + ".funscript"),
                };
            }
            catch { return Array.Empty<string>(); }
        }

        // ===================================================================== lifecycle

        /// <summary>Called by VideoService the moment a video is on screen. Safe to call with any
        /// path (including one that has no script) and safe to call twice.</summary>
        public void OnVideoStarted(string? videoPath)
        {
            if (_disposed || string.IsNullOrWhiteSpace(videoPath)) return;
            if (!_settings.FunScriptEnabled) return;

            OnVideoStopped();   // never let a previous script bleed into the new clip

            // Disk + JSON off the UI thread: PlayVideo's caller is mid-teardown-sensitive.
            _ = Task.Run(() =>
            {
                try
                {
                    string? foundPath = null;
                    string? json = null;
                    foreach (var candidate in CandidatePaths(videoPath!))
                    {
                        if (!File.Exists(candidate)) continue;
                        try { json = File.ReadAllText(candidate); foundPath = candidate; }
                        catch (Exception ex)
                        {
                            App.Logger?.Debug("FunScript: could not read {Path}: {E}", candidate, ex.Message);
                        }
                        break;
                    }

                    if (json == null || foundPath == null) return;          // no script: stay silent
                    if (!FunScript.TryParse(json, out var parsed) || parsed == null)
                    {
                        App.Logger?.Warning("FunScript: {Path} has no usable actions — ignoring", foundPath);
                        return;
                    }

                    lock (_gate)
                    {
                        if (_disposed) return;
                        _script = parsed;
                        _scriptPath = foundPath;
                        _lastReportMs = -1;
                        _lastReportAt = 0;
                        _lastPositionSent = -1;
                    }

                    App.Logger?.Information(
                        "FunScript: loaded {Path} ({Actions} actions, {Seconds:F0}s) for {Video}",
                        Path.GetFileName(foundPath), parsed.Actions.Count,
                        parsed.DurationMs / 1000.0, Path.GetFileName(videoPath!));

                    Subscribe();
                    StartTimer();
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("FunScript: load failed (non-fatal): {E}", ex.Message);
                }
            });
        }

        /// <summary>Called by VideoService during teardown. Idempotent; always zeroes the layer.</summary>
        public void OnVideoStopped()
        {
            Timer? timer;
            bool hadScript;
            lock (_gate)
            {
                hadScript = _script != null;
                _script = null;
                _scriptPath = null;
                _lastReportMs = -1;
                _lastReportAt = 0;
                _lastPositionSent = -1;
                timer = _timer;
                _timer = null;
            }

            try { timer?.Dispose(); } catch { }
            Unsubscribe();
            if (hadScript) ZeroLayer();
        }

        /// <summary>Playback position report. Called from LibVLC's thread via VideoService's event;
        /// this only stores two numbers, so it never blocks that thread.</summary>
        public void OnPlaybackTimeMs(long timeMs)
        {
            if (_disposed || timeMs < 0) return;
            lock (_gate)
            {
                if (_script == null) return;
                _lastReportMs = timeMs;
                _lastReportAt = Environment.TickCount64;
            }
        }

        private void Subscribe()
        {
            try
            {
                var video = App.Video;
                if (video == null || _subscribed) return;
                video.PrimaryPlaybackTimeMsChanged += OnPlaybackTimeMs;
                _subscribed = true;
            }
            catch (Exception ex) { App.Logger?.Debug("FunScript: subscribe failed: {E}", ex.Message); }
        }

        private void Unsubscribe()
        {
            try
            {
                if (!_subscribed) return;
                _subscribed = false;
                var video = App.Video;
                if (video != null) video.PrimaryPlaybackTimeMsChanged -= OnPlaybackTimeMs;
            }
            catch { }
        }

        private void StartTimer()
        {
            lock (_gate)
            {
                if (_disposed || _script == null || _timer != null) return;
                _timer = new Timer(_ => RenderTick(), null, RenderIntervalMs, RenderIntervalMs);
            }
        }

        // ===================================================================== render

        private void RenderTick()
        {
            try
            {
                FunScript? script;
                long reportMs, reportAt;
                lock (_gate)
                {
                    script = _script;
                    reportMs = _lastReportMs;
                    reportAt = _lastReportAt;
                }
                if (script == null) return;

                var now = Environment.TickCount64;
                if (reportMs < 0 || now - reportAt > StaleMs)
                {
                    // Paused, seeking, or the video is gone: stop driving. Position is left where
                    // it is (a thruster snapping home on every pause is worse than holding).
                    ZeroLayer();
                    return;
                }

                var estimatedMs = reportMs + (now - reportAt);
                if (estimatedMs > script.DurationMs + 1000) { ZeroLayer(); return; }

                if (_haptics.HasPositionActuator)
                {
                    var pos = script.PositionAt(estimatedMs + PositionLeadMs);
                    // 1 position step (0-100) is the hardware's own resolution — don't spam below it.
                    if (_lastPositionSent < 0 || Math.Abs(pos - _lastPositionSent) >= 0.01)
                    {
                        _lastPositionSent = pos;
                        _ = SafeSetPositionAsync(pos);
                    }
                }

                if (_settings.FunScriptToVibeConversion)
                {
                    _vibeLayerOwned = true;
                    _haptics.SetLayer(HapticLayer.Pattern, script.IntensityAt(estimatedMs));
                }
                else if (_vibeLayerOwned)
                {
                    // Conversion turned off mid-clip: release the layer ONCE instead of writing 0
                    // at 20 Hz over whatever else (a Deeper envelope) may want it.
                    _vibeLayerOwned = false;
                    _haptics.SetLayer(HapticLayer.Pattern, 0);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("FunScript: render tick error (non-fatal): {E}", ex.Message);
            }
        }

        private async Task SafeSetPositionAsync(double position01)
        {
            try { await _haptics.SetPositionAsync(position01).ConfigureAwait(false); }
            catch (Exception ex) { App.Logger?.Debug("FunScript: position send failed: {E}", ex.Message); }
        }

        private void ZeroLayer()
        {
            if (!_vibeLayerOwned) return;
            _vibeLayerOwned = false;
            try { _haptics.SetLayer(HapticLayer.Pattern, 0); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnVideoStopped();
        }
    }
}
