using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Speech;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using SherpaOnnx;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Speech;

/// <summary>
/// Offline keyword wake service ("hey bambi") backed by a streaming sherpa-onnx transducer KWS model.
/// Avalonia port of the WPF SherpaWakeService. The mic runs through the shared <see cref="HighPassFilter"/>/
/// <see cref="NoiseGate"/>/<see cref="PreRollBuffer"/> front-end (Core) plus a <see cref="SileroVadGate"/>
/// when its model is installed, so steady room tone never reaches the keyword scorer while the low-energy
/// "hey" onset still does (the gate flips on AFTER speech begins; the pre-roll flushes the clipped onset).
///
/// OFFLINE ONLY — audio never leaves the process. Falls back to the grammar wake when the model drop-in is
/// absent or init fails. Includes the per-user calibration sweep (CalibrateAsync) that records a few
/// spoken wake words + room tone and sweeps the threshold to fit the user's voice + mic.
/// </summary>
public sealed class SherpaWakeService : ISpeechWakeService, IDisposable
{
    private const int SampleRate = 16000; // KWS zipformer models are 16 kHz mono.
    private const int FeatureDim = 80;

    private readonly ISettingsService _settings;
    private readonly ILogger<SherpaWakeService> _logger;

    private KeywordSpotter? _spotter;
    private string? _initFingerprint;
    private string? _failedFingerprint;
    private readonly object _gate = new();
    private bool _disposed;
    private int _sessionActive; // 0/1 via Interlocked.

    public SherpaWakeService(ISettingsService settings, ILogger<SherpaWakeService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler<bool>? ListeningChanged;

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        private set
        {
            if (_isListening == value) return;
            _isListening = value;
            try { ListeningChanged?.Invoke(this, value); } catch { /* UI handler hygiene */ }
        }
    }

    /// <summary>Folder where the sherpa-onnx KWS model + keywords.txt are dropped.</summary>
    public static string ModelRoot =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "sherpa-kws");

    /// <summary>Whether the OS reports at least one audio capture device.</summary>
    public static bool HasCaptureDevice
    {
        get { try { return WaveIn.DeviceCount > 0; } catch { return false; } }
    }

    /// <summary>The resolved model files, or null if the drop-in isn't complete.</summary>
    public readonly record struct ModelFiles(string Encoder, string Decoder, string Joiner, string Tokens, string Keywords);

    /// <summary>Locate the streaming-transducer KWS files under <see cref="ModelRoot"/>: an encoder/
    /// decoder/joiner ONNX trio plus tokens.txt and keywords.txt. Returns null if any piece is missing.
    /// Prefers int8 ONNX variants when both are present (smaller/faster, negligible accuracy loss).</summary>
    public static ModelFiles? FindModel()
    {
        try
        {
            if (!Directory.Exists(ModelRoot)) return null;
            var onnx = Directory.EnumerateFiles(ModelRoot, "*.onnx", SearchOption.AllDirectories).ToList();
            string? Pick(string part) =>
                onnx.Where(f => Path.GetFileName(f).Contains(part, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileName(f).Contains("int8", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

            var enc = Pick("encoder");
            var dec = Pick("decoder");
            var join = Pick("joiner");
            var tokens = Directory.EnumerateFiles(ModelRoot, "tokens.txt", SearchOption.AllDirectories).FirstOrDefault();
            var keywords = Directory.EnumerateFiles(ModelRoot, "keywords.txt", SearchOption.AllDirectories).FirstOrDefault();
            if (enc == null || dec == null || join == null || tokens == null || keywords == null) return null;
            return new ModelFiles(enc, dec, join, tokens, keywords);
        }
        catch { return null; }
    }

    /// <summary>Configured = the full KWS model drop-in is present (cheap check, no engine init).</summary>
    public bool IsConfigured => !_disposed && FindModel() != null;

    /// <summary>True when the wake spotter can actually run: model present, a mic exists, and the engine
    /// initialises. Lazily inits on first query and caches the result; re-inits if the model files change,
    /// and remembers a failing model set so it isn't re-initialised on every poll.</summary>
    public bool IsAvailable
    {
        get
        {
            if (_disposed || !HasCaptureDevice || !IsConfigured) return false;
            EnsureEngine();
            return _spotter != null;
        }
    }

    private float WakeThreshold() => (float)Math.Clamp(_settings.Current?.SpeechWakeThreshold ?? 0.15, 0.02, 0.6);
    private float WakeBoost() => (float)Math.Clamp(_settings.Current?.SpeechWakeBoost ?? 2.0, 0.0, 5.0);

    private string Fingerprint(ModelFiles m)
    {
        // Include threshold/boost + the keywords file's mtime so calibration (or a manual keyword edit)
        // changes the fingerprint and forces a clean re-init with the new tuning.
        string kwStamp = "";
        try { kwStamp = File.GetLastWriteTimeUtc(m.Keywords).Ticks.ToString(); } catch { }
        return string.Join("|", m.Encoder, m.Decoder, m.Joiner, m.Tokens, m.Keywords,
                            WakeThreshold().ToString("0.000"), WakeBoost().ToString("0.0"), kwStamp);
    }

    private void EnsureEngine()
    {
        var model = FindModel();
        if (model is not { } m) return;
        var fp = Fingerprint(m);

        if (_spotter != null && _initFingerprint == fp) return;
        if (_failedFingerprint == fp) return; // this exact model set already threw; wait for a change/reset

        lock (_gate)
        {
            if (_spotter != null && _initFingerprint == fp) return;
            if (_failedFingerprint == fp) return;
            if (_spotter != null) { try { _spotter.Dispose(); } catch { } _spotter = null; }
            try
            {
                _spotter = BuildSpotter(m, WakeThreshold(), WakeBoost());
                _initFingerprint = fp;
                _failedFingerprint = null;
                _logger.LogInformation("SherpaWakeService: KWS engine initialised (keywords {Kw})", Path.GetFileName(m.Keywords));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SherpaWakeService: failed to initialise — wake falls back to grammar");
                _spotter = null;
                _failedFingerprint = fp;
            }
        }
    }

    /// <summary>Forget any cached failure so the next idle IsAvailable rebuilds from current files.</summary>
    public void ResetInitState() => _failedFingerprint = null;

    public async Task<bool> WaitForWakeAsync(CancellationToken ct)
    {
        if (!IsAvailable) return false;

        // Re-entrancy guard: never feed two capture streams into the one engine.
        if (Interlocked.CompareExchange(ref _sessionActive, 1, 0) != 0)
        {
            _logger.LogWarning("SherpaWakeService: wake requested while a session is already active — skipping");
            return false;
        }

        try
        {
            var spotter = _spotter;
            if (spotter == null) return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            WaveInEvent? mic = null;
            OnlineStream? stream = null;
            SileroVadGate? vadGate = null;
            var engineLock = new object(); // serialize engine/stream access with teardown
            var done = 0;

            bool diag = _settings.Current?.SpeechWakeDiagnostics == true;
            long totalFrames = 0; double winPeak = 0; int winFrames = 0;

            void Finish(bool detected)
            {
                if (Interlocked.Exchange(ref done, 1) != 0) return;
                if (diag) _logger.LogInformation("SherpaWakeService: capture stopped (detected={Detected}, frames={Frames})", detected, totalFrames);
                tcs.TrySetResult(detected);
            }

            try
            {
                stream = spotter.CreateStream();

                // Mic noise front-end (opt-out via settings): high-pass out AC/fan/hum rumble, then a
                // voiced/silent gate so steady room tone never reaches the keyword scorer. The gate is
                // Silero VAD when its model is installed (understands speech-vs-noise, so a fan can't mask
                // a soft voice) with the adaptive energy gate as fallback. Silent chunks are NOT dropped —
                // they roll through a pre-roll buffer flushed into the decoder the moment the gate opens,
                // so the low-energy "hey" onset (which every gate flips on only AFTER it has begun) still
                // reaches the model instead of being clipped off.
                var nsEnabled = _settings.Current?.SpeechNoiseSuppression ?? true;
                var gateFactor = _settings.Current?.SpeechNoiseGateFactor ?? 4.0;
                var hpf = nsEnabled ? new HighPassFilter(SampleRate) : null;
                vadGate = nsEnabled ? SileroVadGate.TryCreate(SampleRate, _logger) : null;
                var gate = nsEnabled && vadGate == null ? new NoiseGate(gateFactor, hangoverFrames: 12) : null;
                var preRoll = nsEnabled ? new PreRollBuffer(SampleRate) : null;

                mic = new WaveInEvent
                {
                    DeviceNumber = ResolveDeviceNumber(),
                    WaveFormat = new WaveFormat(SampleRate, 16, 1),
                    BufferMilliseconds = 50
                };

                mic.DataAvailable += (_, e) =>
                {
                    if (Volatile.Read(ref done) != 0) return;
                    try
                    {
                        // 16-bit LE PCM -> float[-1,1].
                        int n = e.BytesRecorded / 2;
                        if (n <= 0) return;
                        var samples = new float[n];
                        for (int i = 0, j = 0; i + 1 < e.BytesRecorded; i += 2, j++)
                            samples[j] = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)) / 32768f;

                        // Rumble filter first, so both the level meter and the gate see clean audio.
                        hpf?.ProcessInPlace(samples, n);

                        double rms = 0;
                        if (gate != null || diag)
                        {
                            double sumSq = 0;
                            for (int k = 0; k < n; k++) sumSq += samples[k] * samples[k];
                            rms = Math.Sqrt(sumSq / n);
                        }

                        if (diag)
                        {
                            winPeak = Math.Max(winPeak, rms);
                            if (++winFrames >= 40) // ~2s at 50ms buffers
                            {
                                _logger.LogInformation("SherpaWakeService: listening (peakRms={Peak:0.0000}, floor={Floor:0.0000}, frames={Frames})", winPeak, gate?.NoiseFloor ?? 0, totalFrames + winFrames);
                                winPeak = 0; winFrames = 0;
                            }
                        }
                        totalFrames++;

                        bool voiced = vadGate?.Update(samples) ?? gate?.Update(rms) ?? true;
                        if (!voiced && preRoll != null)
                        {
                            preRoll.Push(samples);
                            return;
                        }

                        lock (engineLock)
                        {
                            if (Volatile.Read(ref done) != 0 || stream == null) return;
                            if (preRoll != null)
                                foreach (var held in preRoll.Drain())
                                    stream.AcceptWaveform(SampleRate, held);
                            stream.AcceptWaveform(SampleRate, samples);
                            while (spotter.IsReady(stream))
                            {
                                spotter.Decode(stream);
                                var result = spotter.GetResult(stream);
                                if (!string.IsNullOrEmpty(result.Keyword))
                                {
                                    spotter.Reset(stream);
                                    Finish(true);
                                    return;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SherpaWakeService: capture callback failed");
                        Finish(false);
                    }
                };

                using var ctReg = ct.Register(() => Finish(false));

                IsListening = true; // mic is now physically open — light the privacy pill
                mic.StartRecording();
                if (diag) _logger.LogInformation("SherpaWakeService: capture started (device={Dev}, gate={Gate})",
                    mic.DeviceNumber, vadGate != null ? "silero-vad" : gate != null ? "energy" : "off");
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SherpaWakeService: wake session failed");
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref done, 1);
                IsListening = false;
                // Stop the mic (ends DataAvailable), then take engineLock so any in-flight callback has
                // finished touching the handles before we dispose the per-wait stream. The shared spotter
                // is reused across waits and torn down only in Dispose().
                try { mic?.StopRecording(); } catch { }
                try { mic?.Dispose(); } catch { }
                lock (engineLock)
                {
                    try { stream?.Dispose(); } catch { }
                    stream = null;
                }
                try { vadGate?.Dispose(); } catch { }
                vadGate = null;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sessionActive, 0);
        }
    }

    /// <summary>
    /// Tune SpeechWakeThreshold to THIS user's voice + mic. Records <paramref name="target"/> spoken wake
    /// utterances (endpointed on silence) plus the room tone between them, then sweeps the trigger threshold
    /// to find the strictest value that still catches the user reliably without the ambient firing — and
    /// stores it. Uses the wake loop's own capture device. The caller MUST stop the wake loop first (the
    /// recognizer is single-session); re-arm after. Audio stays in memory, never written to disk.
    /// </summary>
    public async Task<WakeCalibrationResult> CalibrateAsync(int target = 5, IProgress<WakeCalibrationProgress>? progress = null, CancellationToken ct = default)
    {
        if (!IsConfigured) return new WakeCalibrationResult { Message = "The wake-word model isn't installed." };
        if (!HasCaptureDevice) return new WakeCalibrationResult { Message = "No microphone detected." };
        var model = FindModel();
        if (model is not { } m) return new WakeCalibrationResult { Message = "The wake-word model isn't installed." };

        if (Interlocked.CompareExchange(ref _sessionActive, 1, 0) != 0)
            return new WakeCalibrationResult { Message = "The microphone is busy — stop listening, then calibrate." };

        try
        {
            var utterances = new List<float[]>();
            var ambient = new List<float>(SampleRate * 3); // up to ~3s of room tone
            var cur = new List<float>();
            double noiseFloor = 0.01;     // adaptive room-tone estimate
            int trailingSilenceMs = 0;
            bool inSpeech = false;
            var captureLock = new object();
            var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            const double MinUttMs = 250, MaxUttMs = 2500, EndSilenceMs = 480;

            WaveInEvent? mic = null;
            // Same rumble filter as the live wake path, so the utterances we sweep against are the audio
            // the decoder will actually see (calibrating on raw audio picks a threshold for a pipeline the
            // user never runs).
            var hpfCal = (_settings.Current?.SpeechNoiseSuppression ?? true) ? new HighPassFilter(SampleRate) : null;
            try
            {
                mic = new WaveInEvent
                {
                    DeviceNumber = ResolveDeviceNumber(),
                    WaveFormat = new WaveFormat(SampleRate, 16, 1),
                    BufferMilliseconds = 50
                };

                mic.DataAvailable += (_, e) =>
                {
                    try
                    {
                        int n = e.BytesRecorded / 2;
                        if (n <= 0) return;
                        var buf = new float[n];
                        for (int i = 0, j = 0; i + 1 < e.BytesRecorded; i += 2, j++)
                            buf[j] = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)) / 32768f;
                        hpfCal?.ProcessInPlace(buf, n);
                        double sumSq = 0;
                        for (int k = 0; k < n; k++) sumSq += buf[k] * buf[k];
                        double rms = Math.Sqrt(sumSq / n);
                        double bufMs = 1000.0 * n / SampleRate;

                        lock (captureLock)
                        {
                            if (doneTcs.Task.IsCompleted) return;
                            // Track a slow noise floor from quiet frames; gate scales off it so it adapts
                            // to mic gain. Onset must clearly exceed room tone.
                            if (!inSpeech) noiseFloor = Math.Min(noiseFloor * 1.02 + 1e-5, Math.Max(noiseFloor, rms));
                            if (rms < noiseFloor * 1.5) noiseFloor = 0.9 * noiseFloor + 0.1 * rms;
                            double onsetGate = Math.Clamp(noiseFloor * 4.0, 0.02, 0.08);
                            double endGate = onsetGate * 0.6;

                            if (!inSpeech)
                            {
                                // Collect room tone for the false-wake guard, but ONLY clearly-quiet frames
                                // (below the end gate) so a near-onset word fragment never bleeds into the
                                // ambient and makes it spuriously "fire" during the sweep.
                                if (rms < endGate && ambient.Count < SampleRate * 2) ambient.AddRange(buf);
                                if (rms >= onsetGate) { inSpeech = true; trailingSilenceMs = 0; cur.Clear(); cur.AddRange(buf); }
                            }
                            else
                            {
                                cur.AddRange(buf);
                                trailingSilenceMs = rms < endGate ? trailingSilenceMs + (int)bufMs : 0;
                                double uttMs = 1000.0 * cur.Count / SampleRate;
                                if (trailingSilenceMs >= EndSilenceMs || uttMs >= MaxUttMs)
                                {
                                    if (uttMs - trailingSilenceMs >= MinUttMs && uttMs <= MaxUttMs + 200)
                                    {
                                        utterances.Add(cur.ToArray());
                                        progress?.Report(new WakeCalibrationProgress { Phase = "listen", Captured = utterances.Count, Target = target, Level = rms });
                                    }
                                    inSpeech = false; cur.Clear(); trailingSilenceMs = 0;
                                    if (utterances.Count >= target) doneTcs.TrySetResult(true);
                                }
                            }
                        }
                        progress?.Report(new WakeCalibrationProgress { Phase = "listen", Captured = utterances.Count, Target = target, Level = rms });
                    }
                    catch { /* never let a capture frame throw out */ }
                };

                using var ctReg = ct.Register(() => doneTcs.TrySetResult(false));
                // Overall safety cap so a silent user doesn't hang the flow.
                using var capTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                using var toReg = capTimeout.Token.Register(() => doneTcs.TrySetResult(false));

                IsListening = true;
                mic.StartRecording();
                await doneTcs.Task.ConfigureAwait(false);
            }
            finally
            {
                IsListening = false;
                try { mic?.StopRecording(); } catch { }
                try { mic?.Dispose(); } catch { }
            }

            if (ct.IsCancellationRequested)
                return new WakeCalibrationResult { Message = "Calibration cancelled." };
            if (utterances.Count < 2)
                return new WakeCalibrationResult { Message = $"Only heard {utterances.Count} clear say(s). Try again — say \"Hey Bambi\" clearly, with a pause between each." };

            progress?.Report(new WakeCalibrationProgress { Phase = "analyze", Captured = utterances.Count, Target = target });

            // Sweep: strictest threshold that still catches enough says with the room tone silent.
            var ambientArr = ambient.ToArray();
            float boost = WakeBoost();
            double[] candidates = { 0.28, 0.24, 0.20, 0.16, 0.13, 0.10, 0.08, 0.06 };
            int needed = Math.Max(2, (int)Math.Ceiling(utterances.Count * 0.7));
            int n = utterances.Count;

            // Score every threshold independently: how many of YOUR says it catches, and whether the room
            // tone would false-fire. Decoupled so a firing ambient never hides that a threshold catches you.
            var caughtAt = new int[candidates.Length];
            var ambientAt = new bool[candidates.Length];
            bool ambientUsable = ambientArr.Length > SampleRate / 2; // need ~0.5s+ of room tone to judge
            await Task.Run(() =>
            {
                for (int k = 0; k < candidates.Length; k++)
                {
                    using var spk = BuildSpotter(m, (float)candidates[k], boost);
                    ambientAt[k] = ambientUsable && SpotFires(spk, ambientArr);
                    int c = 0; foreach (var u in utterances) if (SpotFires(spk, u)) c++;
                    caughtAt[k] = c;
                }
            }, ct).ConfigureAwait(false);

            if (_settings.Current?.SpeechWakeDiagnostics == true)
            {
                var tbl = string.Join("  ", candidates.Select((t, k) => $"{t:0.00}:{caughtAt[k]}/{n}{(ambientAt[k] ? "!amb" : "")}"));
                _logger?.LogInformation("SherpaWakeService: calibration sweep utts={Utt} ambientMs={Amb} needed={Need} | {Table}",
                    n, ambientUsable ? ambientArr.Length * 1000 / SampleRate : 0, needed, tbl);
            }

            // Recall-first selection (the user's complaint is MISSES, not false wakes):
            //  1) strictest threshold that catches >= needed AND keeps the room tone silent — ideal.
            //  2) else strictest that catches >= needed (accept some false-wake risk; they prefer catching).
            //  3) else the threshold with the most catches (best effort).
            int pick = -1;
            for (int k = 0; k < candidates.Length; k++) if (caughtAt[k] >= needed && !ambientAt[k]) { pick = k; break; }
            bool ambientRisk = false;
            if (pick < 0)
            {
                for (int k = 0; k < candidates.Length; k++) if (caughtAt[k] >= needed) { pick = k; ambientRisk = true; break; }
            }
            if (pick < 0)
            {
                int best = 0; for (int k = 1; k < candidates.Length; k++) if (caughtAt[k] > caughtAt[best]) best = k;
                pick = best; ambientRisk = ambientAt[best];
            }

            double chosen = candidates[pick];
            int caught = caughtAt[pick];

            if (caught < 2)
                return new WakeCalibrationResult { Message = $"Only caught {caught}/{n} clearly — didn't change anything. Try again: say \"Hey Bambi\" a bit louder, with a clear pause between each." };

            string msg = caught >= needed && !ambientRisk
                ? $"Calibrated to your voice — caught {caught}/{n} at sensitivity {chosen:0.00}."
                : ambientRisk
                    ? $"Calibrated (recall-biased) — caught {caught}/{n} at sensitivity {chosen:0.00}. It may occasionally wake on background noise; re-run somewhere quieter to tighten it."
                    : $"Set sensitivity {chosen:0.00} — caught {caught}/{n}. Re-run for a better fit (say it clearly, pausing between each).";

            // Persist + force a rebuild with the new threshold on next IsAvailable.
            try
            {
                if (_settings.Current != null)
                {
                    _settings.Current.SpeechWakeThreshold = chosen;
                    _settings.Save();
                }
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "SherpaWakeService: failed to persist calibrated threshold"); }
            ResetInitState();
            _logger?.LogInformation("SherpaWakeService: calibrated threshold={Thr:0.000} (caught {Caught}/{Utt})", chosen, caught, n);
            return new WakeCalibrationResult { Success = true, ChosenThreshold = chosen, Utterances = n, CaughtAtChosen = caught, Message = msg };
        }
        finally
        {
            Interlocked.Exchange(ref _sessionActive, 0);
        }
    }

    /// <summary>Feed a whole buffer through a spotter (batch) and report whether the keyword fired.</summary>
    private static bool SpotFires(KeywordSpotter spk, float[] audio)
    {
        var s = spk.CreateStream();
        try
        {
            s.AcceptWaveform(SampleRate, audio);
            s.AcceptWaveform(SampleRate, new float[SampleRate / 3]); // trailing silence to flush
            s.InputFinished();
            while (spk.IsReady(s))
            {
                spk.Decode(s);
                if (!string.IsNullOrEmpty(spk.GetResult(s).Keyword)) return true;
            }
            return false;
        }
        finally { try { s.Dispose(); } catch { } }
    }

    private KeywordSpotter BuildSpotter(ModelFiles m, float threshold, float boost)
    {
        var config = new KeywordSpotterConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = FeatureDim;
        config.ModelConfig.Transducer.Encoder = m.Encoder;
        config.ModelConfig.Transducer.Decoder = m.Decoder;
        config.ModelConfig.Transducer.Joiner = m.Joiner;
        config.ModelConfig.Tokens = m.Tokens;
        config.ModelConfig.Provider = "cpu";
        config.ModelConfig.NumThreads = 1;
        config.KeywordsFile = m.Keywords;
        config.KeywordsThreshold = threshold;
        config.KeywordsScore = boost;
        return new KeywordSpotter(config);
    }

    private int ResolveDeviceNumber()
    {
        var idx = _settings.Current?.SpeechInputDeviceIndex ?? -1;
        try
        {
            if (idx >= 0 && idx < WaveInEvent.DeviceCount) return idx;
        }
        catch { }
        return 0; // WaveIn device 0 == Windows default capture device.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            try { _spotter?.Dispose(); } catch { }
            _spotter = null;
        }
    }
}
