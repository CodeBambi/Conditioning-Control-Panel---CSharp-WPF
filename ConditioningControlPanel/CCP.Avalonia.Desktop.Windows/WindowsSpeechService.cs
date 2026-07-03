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
using Newtonsoft.Json.Linq;
using Vosk;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows;

/// <summary>
/// Windows offline speech recognition — the real engine behind <see cref="ISpeechRecognitionService"/>.
/// Ported verbatim from the WPF <c>Services/Speech/SpeechService.cs</c> for 1:1 parity; the only
/// changes are dependency injection (<see cref="ISettingsService"/> + <see cref="ILogger"/>) in place
/// of the WPF <c>App.Settings</c> / <c>App.Logger</c> statics, and the shared Core models/matching.
///
/// Design contract: OFFLINE ONLY (Vosk closed-grammar STT + NAudio capture, no network ever);
/// single capture session at a time; <see cref="IsListening"/> runtime-only; audio buffers stay in
/// memory and are never written to disk or transmitted; graceful no-op when no model / no mic.
/// </summary>
public sealed class WindowsSpeechService : ISpeechRecognitionService, IDisposable
{
    private const int SampleRate = 16000; // Vosk small models are 16 kHz mono.

    private readonly ISettingsService _settings;
    private readonly ILogger<WindowsSpeechService>? _logger;

    private readonly object _gate = new();
    private Model? _model;
    private bool _modelLoadAttempted;
    private bool _disposed;

    // Only one capture session may run at a time.
    private int _sessionActive; // 0/1 via Interlocked.
    private CancellationTokenSource? _activeCts;

    public event EventHandler<string>? PartialTranscript;
    public event EventHandler<double>? LevelChanged;
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

    public WindowsSpeechService(ISettingsService settings, ILogger<WindowsSpeechService>? logger = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger;
        // Silence Vosk's native chatter on stdout; never throw from the ctor.
        try { Vosk.Vosk.SetLogLevel(-1); } catch { /* native lib may be absent in odd builds */ }
    }

    public bool IsAvailable
    {
        get
        {
            if (_disposed) return false;
            if (!HasCaptureDevice) return false;
            EnsureModel();
            return _model != null;
        }
    }

    public bool HasCaptureDevice
    {
        get
        {
            try { return WaveInEvent.DeviceCount > 0; }
            catch { return false; }
        }
    }

    public void StopListening()
    {
        try { _activeCts?.Cancel(); } catch { }
    }

    public IReadOnlyList<SpeechInputDevice> EnumerateInputDevices()
    {
        var list = new List<SpeechInputDevice> { new(-1, "System default") };
        try
        {
            int count = WaveInEvent.DeviceCount;
            for (int i = 0; i < count; i++)
            {
                string name;
                try { name = WaveInEvent.GetCapabilities(i).ProductName; }
                catch { name = ""; }
                if (string.IsNullOrWhiteSpace(name)) name = $"Device {i}";
                list.Add(new SpeechInputDevice(i, name));
            }
        }
        catch { }
        return list;
    }

    /// <summary>Directory we expect the Vosk model to live in (drop the unpacked model here).</summary>
    public static string ModelRoot =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "vosk");

    private void EnsureModel()
    {
        if (_model != null || _modelLoadAttempted) return;
        lock (_gate)
        {
            if (_model != null || _modelLoadAttempted) return;
            _modelLoadAttempted = true;
            try
            {
                var dir = ResolveModelDir();
                if (dir == null)
                {
                    _logger?.LogInformation("SpeechService: no Vosk model found under {Root} — speech disabled", ModelRoot);
                    return;
                }
                _model = new Model(dir);
                _logger?.LogInformation("SpeechService: Vosk model loaded from {Dir}", dir);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SpeechService: failed to load Vosk model — speech disabled");
            }
        }
    }

    private static string? ResolveModelDir()
    {
        if (!Directory.Exists(ModelRoot)) return null;

        // P3 S-5: collect every valid model dir (the root itself or a nested folder, the way the
        // official zips unpack). When more than one is present (e.g. the old small model left alongside
        // a freshly-dropped lgraph one), prefer the more accurate model — lgraph over a generic name
        // over the small model — so simply dropping the upgrade in picks it up without deleting the old
        // folder first. Mirrors WPF SpeechService.ResolveModelDir (SpeechService.cs:215-238).
        var candidates = new List<string>();
        if (LooksLikeModel(ModelRoot)) candidates.Add(ModelRoot);
        foreach (var sub in Directory.EnumerateDirectories(ModelRoot))
            if (LooksLikeModel(sub)) candidates.Add(sub);
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        static int Rank(string dir)
        {
            var name = Path.GetFileName(dir).ToLowerInvariant();
            if (name.Contains("lgraph")) return 2;
            if (!name.Contains("small")) return 1;
            return 0;
        }
        return candidates.OrderByDescending(Rank).First();
    }

    private static bool LooksLikeModel(string dir) =>
        Directory.Exists(Path.Combine(dir, "am")) && Directory.Exists(Path.Combine(dir, "conf"));

    public Task<PhraseResult> RecognizePhraseAsync(string target, RecognizeOptions? options = null, CancellationToken ct = default)
        => RunGrammarSessionAsync(target, new[] { target }, options, isWakeWord: false, ct);

    public Task<PhraseResult> RecognizeOneOfAsync(IReadOnlyList<string> phrases, RecognizeOptions? options = null, CancellationToken ct = default)
        => RunGrammarSessionAsync(phrases.Count > 0 ? phrases[0] : "", phrases, options, isWakeWord: false, ct);

    public async Task<string?> WaitForWakeWordAsync(IEnumerable<string> wakeWords, CancellationToken ct = default)
    {
        var words = wakeWords?.Where(w => !string.IsNullOrWhiteSpace(w)).Select(SpeechMatching.Normalize).Distinct().ToList()
                    ?? new List<string>();
        if (words.Count == 0) return null;
        var res = await RunGrammarSessionAsync(words[0], words,
                new RecognizeOptions { Timeout = Timeout.InfiniteTimeSpan, MatchThreshold = SettingDouble("SpeechWakeMatchThreshold", 0.6) },
                isWakeWord: true, ct)
            .ConfigureAwait(false);
        return res.Matched ? (string.IsNullOrEmpty(res.Transcript) ? words[0] : res.Transcript) : null;
    }

    private async Task<PhraseResult> RunGrammarSessionAsync(
        string target, IReadOnlyList<string> grammarPhrases, RecognizeOptions? options, bool isWakeWord, CancellationToken ct)
    {
        options ??= new RecognizeOptions();
        if (!IsAvailable) return PhraseResult.NotAvailable;

        if (Interlocked.CompareExchange(ref _sessionActive, 1, 0) != 0)
        {
            _logger?.LogWarning("SpeechService: recognize requested while a session is already active — skipping");
            return PhraseResult.NotAvailable;
        }

        try
        {
            var matchThreshold = options.MatchThreshold ?? SettingDouble("SpeechMatchThreshold", 0.62);
            var loudnessThreshold = options.LoudnessThreshold ?? SettingDouble("SpeechLoudnessThreshold", 0.015);

            // Mic noise front-end (opt-out via settings): high-pass out AC/fan/hum rumble and gate
            // onset on an adaptive noise floor instead of the fixed loudness threshold alone, so a hot
            // day's AC hum self-raises the trigger point rather than tripping it. DSP lives in CCP.Core
            // (HighPassFilter / NoiseGate). Mirrors the WPF SpeechService front-end.
            var nsEnabled = SettingBool("SpeechNoiseSuppression", true);
            var gateFactor = SettingDouble("SpeechNoiseGateFactor", 4.0);
            var hpf = nsEnabled ? new HighPassFilter(SampleRate) : null;
            var gate = nsEnabled ? new NoiseGate(gateFactor) : null;

            var normalizedTarget = SpeechMatching.Normalize(target);

            var tcs = new TaskCompletionSource<PhraseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            WaveInEvent? mic = null;
            VoskRecognizer? rec = null;
            var recLock = new object();
            double peakRms = 0;
            var done = 0;
            var speechStarted = 0;
            CancellationTokenSource? timeoutCts = null;
            CancellationTokenSource? onsetCts = null;

            void Finish(PhraseResult r)
            {
                if (Interlocked.Exchange(ref done, 1) != 0) return;
                tcs.TrySetResult(r);
            }

            PhraseResult Evaluate(string transcript, double confidence, bool timedOut)
            {
                var heard = SpeechMatching.Normalize(transcript);
                var score = SpeechMatching.Similarity(normalizedTarget, heard);
                if (isWakeWord && !string.IsNullOrEmpty(normalizedTarget))
                {
                    var tn = normalizedTarget.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    var hh = heard.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (hh.Length > tn)
                        score = Math.Max(score, SpeechMatching.Similarity(normalizedTarget, string.Join(' ', hh.Take(tn))));
                }
                // With the adaptive gate on, a phrase must clear whichever is higher: the fixed floor
                // or the room-tone-scaled onset gate — so hum can't be mistaken for "said out loud".
                var loudGate = gate != null ? Math.Max(loudnessThreshold, gate.EnterGate) : loudnessThreshold;
                var loud = peakRms >= loudGate;
                return new PhraseResult
                {
                    Transcript = transcript?.Trim() ?? "",
                    Score = score,
                    Confidence = confidence,
                    LoudEnough = loud,
                    Matched = !timedOut && score >= matchThreshold && loud,
                    TimedOut = timedOut
                };
            }

            try
            {
                rec = BuildRecognizer(grammarPhrases);
                rec.SetWords(true);

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
                        // Strip low-frequency rumble in place BEFORE measuring RMS and before Vosk sees it.
                        hpf?.ProcessPcm16(e.Buffer, e.BytesRecorded);

                        double rms = Rms(e.Buffer, e.BytesRecorded);
                        peakRms = Math.Max(peakRms, rms);
                        RaiseLevel(rms);

                        // Speech onset: the adaptive gate tracks the room floor; falls back to the fixed threshold.
                        bool voiced = gate != null ? gate.Update(rms) : rms >= loudnessThreshold;
                        if (voiced) Volatile.Write(ref speechStarted, 1);

                        lock (recLock)
                        {
                            if (Volatile.Read(ref done) != 0 || rec == null) return;
                            if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                            {
                                var (text, conf) = ParseResult(rec.Result());
                                if (!string.IsNullOrWhiteSpace(text))
                                {
                                    var result = Evaluate(text, conf, timedOut: false);
                                    if (!isWakeWord || result.Matched)
                                        Finish(result);
                                }
                            }
                            else
                            {
                                var partial = ParsePartial(rec.PartialResult());
                                if (!string.IsNullOrWhiteSpace(partial)) RaisePartial(partial);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "SpeechService: capture callback failed");
                        Finish(Evaluate("", 0, timedOut: true));
                    }
                };
                mic.RecordingStopped += (_, _) => { /* surfaced via Finish paths */ };

                _activeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var ctReg = _activeCts.Token.Register(() => Finish(Evaluate("", 0, timedOut: true)));

                if (options.Timeout != Timeout.InfiniteTimeSpan)
                {
                    timeoutCts = new CancellationTokenSource(options.Timeout);
                    timeoutCts.Token.Register(() =>
                    {
                        try
                        {
                            lock (recLock)
                            {
                                if (Volatile.Read(ref done) != 0 || rec == null) { Finish(Evaluate("", 0, timedOut: true)); return; }
                                var (text, conf) = ParseResult(rec.FinalResult());
                                Finish(Evaluate(text, conf, timedOut: string.IsNullOrWhiteSpace(text)));
                            }
                        }
                        catch { Finish(Evaluate("", 0, timedOut: true)); }
                    });
                }

                if (options.OnsetTimeout is { } onset && onset != Timeout.InfiniteTimeSpan)
                {
                    onsetCts = new CancellationTokenSource(onset);
                    onsetCts.Token.Register(() =>
                    {
                        if (Volatile.Read(ref speechStarted) != 0) return;
                        Finish(Evaluate("", 0, timedOut: true));
                    });
                }

                IsListening = true;
                mic.StartRecording();

                return await tcs.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SpeechService: session setup failed");
                return PhraseResult.NotAvailable;
            }
            finally
            {
                IsListening = false;
                Interlocked.Exchange(ref done, 1);
                try { mic?.StopRecording(); } catch { }
                try { mic?.Dispose(); } catch { }
                lock (recLock)
                {
                    try { rec?.Dispose(); } catch { }
                    rec = null;
                }
                try { timeoutCts?.Dispose(); } catch { }
                try { onsetCts?.Dispose(); } catch { }
                try { _activeCts?.Dispose(); } catch { }
                _activeCts = null;
                RaiseLevel(0);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sessionActive, 0);
        }
    }

    private VoskRecognizer BuildRecognizer(IReadOnlyList<string> phrases)
    {
        var cleaned = phrases.Select(SpeechMatching.Normalize).Where(p => p.Length > 0).Distinct().ToList();
        if (cleaned.Count > 0)
        {
            try
            {
                var grammar = new JArray();
                foreach (var p in cleaned) grammar.Add(p);
                grammar.Add("[unk]"); // lets Vosk emit "unknown" instead of forcing a wrong match
                return new VoskRecognizer(_model, SampleRate, grammar.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex)
            {
                _logger?.LogInformation(ex, "SpeechService: grammar recognizer failed, using free recognizer");
            }
        }
        return new VoskRecognizer(_model, SampleRate);
    }

    private int ResolveDeviceNumber()
    {
        var savedIndex = SettingInt("SpeechInputDeviceIndex", -1);
        var savedName = SettingString("SpeechInputDeviceName", null);
        try
        {
            // P2 S-4: prefer a match on the saved device NAME (robust to NAudio ordinal reshuffling when
            // virtual audio devices appear/disappear — the "voice worked yesterday, not today" failure,
            // #441b), then the saved ordinal if still valid, else 0. Mirrors WPF
            // SpeechService.ResolveDeviceNumber(int, string) at SpeechService.cs:524-545.
            if (!string.IsNullOrWhiteSpace(savedName))
            {
                int count = WaveInEvent.DeviceCount;
                for (int i = 0; i < count; i++)
                {
                    string name;
                    try { name = WaveInEvent.GetCapabilities(i).ProductName; }
                    catch { name = ""; }
                    if (string.Equals(name, savedName, StringComparison.OrdinalIgnoreCase))
                        return i;
                }
            }
            if (savedIndex >= 0 && savedIndex < WaveInEvent.DeviceCount) return savedIndex;
        }
        catch { }
        return 0; // WaveIn device 0 == Windows default capture device.
    }

    private void RaisePartial(string text)
    {
        try { PartialTranscript?.Invoke(this, text); } catch { }
    }

    private void RaiseLevel(double level)
    {
        try { LevelChanged?.Invoke(this, Math.Clamp(level, 0, 1)); } catch { }
    }

    private static (string text, double conf) ParseResult(string json)
    {
        try
        {
            var o = JObject.Parse(json);
            var text = (string?)o["text"] ?? "";
            double conf = 0;
            if (o["result"] is JArray words && words.Count > 0)
            {
                double sum = 0; int n = 0;
                foreach (var w in words)
                {
                    var c = (double?)w["conf"];
                    if (c.HasValue) { sum += c.Value; n++; }
                }
                if (n > 0) conf = sum / n;
            }
            return (text, conf);
        }
        catch { return ("", 0); }
    }

    private static string ParsePartial(string json)
    {
        try { return (string?)JObject.Parse(json)["partial"] ?? ""; }
        catch { return ""; }
    }

    private static double Rms(byte[] buffer, int bytes)
    {
        if (bytes < 2) return 0;
        long samples = bytes / 2;
        double sumSq = 0;
        for (int i = 0; i + 1 < bytes; i += 2)
        {
            short s = (short)(buffer[i] | (buffer[i + 1] << 8));
            double v = s / 32768.0;
            sumSq += v * v;
        }
        return Math.Sqrt(sumSq / samples);
    }

    // --- settings access (kept defensive; mirrors the WPF reflection so behavior is identical) ---

    private double SettingDouble(string name, double fallback)
    {
        try
        {
            var current = _settings.Current;
            var p = current?.GetType().GetProperty(name);
            if (p?.GetValue(current) is double d && d > 0) return d;
        }
        catch { }
        return fallback;
    }

    private int SettingInt(string name, int fallback)
    {
        try
        {
            var current = _settings.Current;
            var p = current?.GetType().GetProperty(name);
            if (p?.GetValue(current) is int i) return i;
        }
        catch { }
        return fallback;
    }

    private bool SettingBool(string name, bool fallback)
    {
        try
        {
            var current = _settings.Current;
            var p = current?.GetType().GetProperty(name);
            if (p?.GetValue(current) is bool b) return b;
        }
        catch { }
        return fallback;
    }

    private string? SettingString(string name, string? fallback)
    {
        try
        {
            var current = _settings.Current;
            var p = current?.GetType().GetProperty(name);
            if (p?.GetValue(current) is string s) return s;
        }
        catch { }
        return fallback;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _model?.Dispose(); } catch { }
        _model = null;
    }
}
