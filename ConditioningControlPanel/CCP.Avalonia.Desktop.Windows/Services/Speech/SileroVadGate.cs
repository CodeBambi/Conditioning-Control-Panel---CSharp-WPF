using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace ConditioningControlPanel.Avalonia.Desktop.Windows.Services.Speech;

/// <summary>
/// Streaming voiced/silent classifier backed by Silero VAD (via the sherpa-onnx runtime the wake
/// engine already ships). Replaces the energy-based <see cref="NoiseGate"/> in front of the wake
/// spotter whenever the model file is present: an RMS gate is fundamentally blind to WHAT is loud —
/// broadband fan/AC noise can carry as much energy as quiet speech, so in exactly the noisy rooms the
/// gate exists for it either passes everything or blocks the user. Silero is a tiny (~2 MB) network
/// trained to tell speech from noise regardless of level, so the fan stays gated while a soft "hey
/// bambi" opens the stream.
///
/// Contract mirrors the other speech drop-ins:
///  - OFFLINE ONLY; audio never leaves the process.
///  - Model absent / init failure =&gt; <see cref="TryCreate"/> returns null and the caller falls back to
///    the energy gate. Never throws out of the public surface.
///  - One instance per capture session (holds decoder state); confine calls to the capture callback.
/// </summary>
internal sealed class SileroVadGate : IDisposable
{
    private readonly VoiceActivityDetector _vad;
    private readonly ILogger? _logger;
    private bool _disposed;

    /// <summary>Folder where the Silero VAD model is dropped.</summary>
    public static string ModelRoot =>
        Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "silero-vad");

    private SileroVadGate(VoiceActivityDetector vad, ILogger? logger)
    {
        _vad = vad;
        _logger = logger;
    }

    /// <summary>Locate the VAD model file, or null if the drop-in is absent.</summary>
    public static string? FindModel()
    {
        try
        {
            if (!Directory.Exists(ModelRoot)) return null;
            return Directory.EnumerateFiles(ModelRoot, "*.onnx", SearchOption.AllDirectories)
                .OrderByDescending(f => Path.GetFileName(f).Contains("silero", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>Build a gate for one capture session, or null when the model is missing or init fails
    /// (caller falls back to the energy gate).</summary>
    public static SileroVadGate? TryCreate(int sampleRate, ILogger? logger)
    {
        var model = FindModel();
        if (model == null) return null;
        try
        {
            var config = new VadModelConfig();
            config.SileroVad.Model = model;
            // Recall-biased: as a GATE a false "voiced" costs nothing (the KWS keyword threshold still
            // guards false wakes) while a false "silent" swallows the wake outright.
            config.SileroVad.Threshold = 0.25f;
            // MinSilenceDuration doubles as the gate's hangover: long enough that the natural gap
            // between "hey" and "bambi" (or a soft talker's pauses) never closes the stream mid-phrase.
            config.SileroVad.MinSilenceDuration = 0.6f;
            // Flip to "speech" fast — the PreRollBuffer covers whatever elapses before the flip.
            config.SileroVad.MinSpeechDuration = 0.1f;
            config.SileroVad.WindowSize = 512; // fixed by the silero model at 16 kHz
            config.SileroVad.MaxSpeechDuration = 10.0f;
            config.SampleRate = sampleRate;
            config.NumThreads = 1;
            config.Provider = "cpu";
            config.Debug = 0;
            var vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 10.0f);
            return new SileroVadGate(vad, logger);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "SileroVadGate: init failed — falling back to the energy gate");
            return null;
        }
    }

    /// <summary>
    /// Feed one capture chunk and report whether the stream is currently voiced. Completed speech
    /// segments are drained and discarded on the spot — we only use the live speech/no-speech state,
    /// and popping keeps the detector's internal queue from growing over a long armed session.
    /// </summary>
    public bool Update(float[] samples)
    {
        if (_disposed) return true; // never swallow audio after teardown
        try
        {
            _vad.AcceptWaveform(samples);
            while (!_vad.IsEmpty()) _vad.Pop();
            return _vad.IsSpeechDetected();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SileroVadGate: update failed — passing audio through");
            return true; // fail-open: worse to drop the user's speech than to skip gating
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _vad.Dispose(); } catch { }
    }
}
