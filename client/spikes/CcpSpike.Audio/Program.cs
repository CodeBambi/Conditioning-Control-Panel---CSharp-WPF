using System.Diagnostics;
using System.Text.Json;

namespace CcpSpike.Audio;

/// <summary>
/// SP-017 spike runner. Executes the IDENTICAL probe sequence per admitted backend and emits
/// JSONL facts (one line per observation). Completion/interruption claims come only from
/// backend events/positions cross-checked against a shared monotonic stopwatch (honesty
/// framing b) — never from call returns or sleeps. Declared pre-run tolerances (consult item 7):
/// completion window = [duration-100ms, duration+500ms]; pause freeze drift ≤ 20ms;
/// resume successor completion = duration+pauseWall ±600ms; SFX latency poll = 2ms
/// (resolution = poll interval + device period, recorded per backend).
/// </summary>
public static class Program
{
    private const int CompletionSlopEarlyMs = 100;
    private const int CompletionSlopLateMs = 500;
    private const int SfxCount = 8;
    private const int SfxSpacingMs = 30;
    private const int LatencyPollMs = 2;

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--diag")
        {
            DiagStopBlocking();
            return 0;
        }
        var backends = ArgValue(args, "--backends")?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       ?? new[] { "soundflow", "openal", "naudio" };
        var outDir = ArgValue(args, "--out")
                     ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "scratch");
        outDir = Path.GetFullPath(outDir);
        var toneDir = ToneGen.EnsureTones(Path.Combine(outDir, "tones"));
        var logPath = Path.Combine(outDir, $"spike-{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");
        Directory.CreateDirectory(outDir);

        using var log = new StreamWriter(logPath) { AutoFlush = true };
        void Emit(string backend, string probe, object facts)
        {
            var line = JsonSerializer.Serialize(new
            {
                ts = DateTime.UtcNow.ToString("o"),
                backend,
                probe,
                facts
            });
            log.WriteLine(line);
            Console.WriteLine(line);
        }

        Console.WriteLine($"ccp-spike-audio: backends=[{string.Join(',', backends)}] out={outDir}");
        Console.WriteLine($"platform: {(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other")}; " +
                          $"declared tolerances: completion [-{CompletionSlopEarlyMs},+{CompletionSlopLateMs}]ms, sfx poll {LatencyPollMs}ms");

        foreach (var name in backends)
        {
            using IAudioHarness h = name switch
            {
                "soundflow" => new SoundFlowHarness(),
                "openal" => new OpenAlHarness(),
                "naudio" => new NAudioHarness(),
                _ => throw new ArgumentException($"unknown backend {name}")
            };
            try
            {
                RunBackend(h, toneDir, Emit);
            }
            catch (Exception ex)
            {
                Emit(h.Name, "harness-fault", new { error = ex.GetType().Name + ": " + ex.Message });
            }
            try
            {
                RunTeardown(name, toneDir, Emit);
            }
            catch (Exception ex)
            {
                Emit(name, "teardown-fault", new { error = ex.GetType().Name + ": " + ex.Message });
            }
        }

        Console.WriteLine($"log: {logPath}");
        return 0;
    }

    /// <summary>
    /// Isolation diagnostic for the observed SoundFlow Stop() blocking (Step-2 finding class):
    /// times each teardown step of an actively-playing SoundFlow player.
    /// </summary>
    private static void DiagStopBlocking()
    {
        var toneDir = ToneGen.EnsureTones(Path.Combine(Path.GetTempPath(), "ccp-sp017-diag"));
        var voice = Path.Combine(toneDir, "voice-2500ms.wav");
        using var engine = new SoundFlow.Backends.MiniAudio.MiniAudioEngine();
        var format = new SoundFlow.Structs.AudioFormat { Format = SoundFlow.Enums.SampleFormat.F32, Channels = 1, SampleRate = ToneGen.SampleRate };
        var device = engine.InitializePlaybackDevice(null, format);
        device.Start();
        var provider = new SoundFlow.Providers.AssetDataProvider(engine, voice);
        var player = new SoundFlow.Components.SoundPlayer(engine, format, provider);
        device.MasterMixer.AddComponent(player);
        player.PlaybackEnded += (_, _) => Console.WriteLine($"[diag] PlaybackEnded at {NowMs():F1}ms");
        player.Play();
        Console.WriteLine($"[diag] playing, sleeping 500ms @ {NowMs():F1}");
        Thread.Sleep(500);
        var t0 = NowMs();
        player.Stop();
        Console.WriteLine($"[diag] Stop() took {NowMs() - t0:F1}ms");
        t0 = NowMs();
        device.MasterMixer.RemoveComponent(player);
        Console.WriteLine($"[diag] RemoveComponent took {NowMs() - t0:F1}ms");
        t0 = NowMs();
        player.Dispose();
        Console.WriteLine($"[diag] Dispose took {NowMs() - t0:F1}ms");
        // second cycle: natural completion then stop
        var p2 = new SoundFlow.Components.SoundPlayer(engine, format, new SoundFlow.Providers.AssetDataProvider(engine, voice));
        device.MasterMixer.AddComponent(p2);
        p2.PlaybackEnded += (_, _) => Console.WriteLine($"[diag] p2 PlaybackEnded at {NowMs():F1}ms");
        p2.Play();
        Console.WriteLine("[diag] p2 playing to natural end (2600ms)");
        Thread.Sleep(2600);
        t0 = NowMs();
        p2.Stop();
        Console.WriteLine($"[diag] p2 Stop-after-natural-end took {NowMs() - t0:F1}ms");
        t0 = NowMs();
        device.Stop();
        Console.WriteLine($"[diag] device.Stop took {NowMs() - t0:F1}ms");
    }

    private static string? ArgValue(string[] args, string key)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == key) return args[i + 1];
        return null;
    }

    private static void RunBackend(IAudioHarness h, string toneDir,
        Action<string, string, object> emit)
    {
        var voice = Path.Combine(toneDir, "voice-2500ms.wav");
        var sfx = Path.Combine(toneDir, "sfx-300ms.wav");
        var whisper = Path.Combine(toneDir, "whisper-1500ms.wav");

        if (!h.SupportedHere)
        {
            emit(h.Name, "supported", new { supported = false, mechanism = h.CompletionMechanism, note = "honest not-supported on this OS; no native calls attempted" });
            return;
        }
        emit(h.Name, "supported", new { supported = true, mechanism = h.CompletionMechanism });

        // ---- devices: enumerate / select / invalid-fallback ----
        var devices = h.EnumerateDevices();
        emit(h.Name, "devices-enumerate", new { count = devices.Count, devices });
        if (!h.TryInit(null, out var initErr))
        {
            emit(h.Name, "device-init-default", new { ok = false, error = initErr });
            return; // nothing further is meaningful
        }
        emit(h.Name, "device-init-default", new { ok = true });

        var nonDefault = devices.FirstOrDefault(d => !d.IsDefault);
        if (nonDefault != null)
        {
            var ok = h.TryInit(nonDefault.Id, out var selErr);
            emit(h.Name, "device-select-nondefault", new { ok, device = nonDefault.Name, error = selErr });
            h.TryInit(null, out _); // back to default for the rest
        }
        else
        {
            emit(h.Name, "device-select-nondefault", new { ok = false, skipped = true, note = "single-device session fact" });
        }

        var bogusOk = h.TryInit("ccp-spike-bogus-device-id", out var bogusErr);
        emit(h.Name, "device-invalid-fallback", new
        {
            invalidRejected = !bogusOk,
            invalidError = bogusErr,
            fallbackOk = h.TryInit(null, out var fbErr),
            fallbackError = fbErr
        });

        // ---- volume (mechanism + backend readback; audible level = mechanism-only named) ----
        h.VoicePlay(voice, 1.0f);
        h.SetVoiceGain(0.25f);
        var g1 = h.GetVoiceGain();
        h.SetVoiceGain(1.0f);
        var g2 = h.GetVoiceGain();
        h.VoiceStop();
        emit(h.Name, "volume", new { setLow = 0.25, readLow = g1, setHigh = 1.0, readHigh = g2, note = "gain set+backend readback verified; audible level effect is mechanism-only (no backend meter exposed at this layer)" });

        // ---- completion: end signal ≈ exact asset duration ----
        h.VoicePlay(voice, 1.0f);
        var t0 = NowMs();
        double? endAt = WaitFor(() => h.VoiceEndAtMs, ToneGen.VoiceMs + 3000);
        var inWindow = endAt != null &&
                       endAt - t0 >= ToneGen.VoiceMs - CompletionSlopEarlyMs &&
                       endAt - t0 <= ToneGen.VoiceMs + CompletionSlopLateMs;
        emit(h.Name, "voice-completion", new
        {
            durationMs = ToneGen.VoiceMs,
            endSignalAtMs = endAt != null ? Math.Round(endAt.Value - t0, 1) : (double?)null,
            withinDeclaredWindow = inWindow,
            window = $"[{ToneGen.VoiceMs - CompletionSlopEarlyMs},{ToneGen.VoiceMs + CompletionSlopLateMs}]",
            positionAtEnd = Math.Round(h.VoicePositionSec, 3)
        });

        // ---- interruption: stop mid-stream; interrupt≠completion discrimination ----
        h.ClearVoiceEnd();
        h.VoicePlay(voice, 1.0f);
        var ti = NowMs();
        SleepUntil(ti, 600);
        var posAtStop = h.VoicePositionSec;
        var rawBefore = h.VoiceRawEndCount;
        h.VoiceStop();
        var stopDoneAt = NowMs();
        // watch 2600ms for any late FILTERED end signal (natural completion would land ~2500ms after ti)
        var lateEnd = WaitFor(() => h.VoiceEndAtMs, (int)(ti + 3200 - NowMs()));
        var rawAfter = h.VoiceRawEndCount;
        emit(h.Name, "voice-interruption", new
        {
            stoppedAtMs = Math.Round(stopDoneAt - ti, 1),
            positionAtStopSec = Math.Round(posAtStop, 3),
            positionBelowDuration = posAtStop < ToneGen.VoiceMs / 1000.0,
            filteredEndAfterStopAtMs = lateEnd != null ? Math.Round(lateEnd.Value - ti, 1) : (double?)null,
            rawEndEventsAroundStop = rawAfter - rawBefore,
            note = lateEnd == null && rawAfter == rawBefore
                ? "no end signal at all after explicit stop → interrupt and completion distinguishable by signal presence"
                : lateEnd == null
                    ? "backend FIRED an end signal for the explicit stop (raw +" + (rawAfter - rawBefore) + ") but player-identity filtering rejects it → interrupt≠completion achievable via identity/generation token (recorded FINDING per consult item 6)"
                    : "end signal passed filtering after explicit stop → backend does NOT discriminate interrupt from completion at event level (recorded FINDING per consult item 6)"
        });

        // ---- pause/resume: position freeze + successor continuation ----
        h.ClearVoiceEnd();
        h.VoicePlay(voice, 1.0f);
        var tp = NowMs();
        SleepUntil(tp, 500);
        h.VoicePause();
        SleepUntil(tp, 600);
        var p1 = h.VoicePositionSec;
        SleepUntil(tp, 950);
        var p2 = h.VoicePositionSec;
        h.VoiceResume();
        var resumeAt = NowMs();
        var pauseWall = resumeAt - (tp + 500);
        var endAfterResume = WaitFor(() => h.VoiceEndAtMs, (int)(tp + ToneGen.VoiceMs + pauseWall + 2000 - NowMs()));
        var expectedEnd = ToneGen.VoiceMs + pauseWall;
        emit(h.Name, "voice-pause-resume", new
        {
            positionFrozenDriftMs = Math.Round((p2 - p1) * 1000, 1),
            freezeOk = Math.Abs(p2 - p1) * 1000 <= 20,
            pauseWallMs = Math.Round(pauseWall, 1),
            endAtMs = endAfterResume != null ? Math.Round(endAfterResume.Value - tp, 1) : (double?)null,
            expectedEndMs = Math.Round(expectedEnd, 1),
            withinDeclaredWindow = endAfterResume != null && Math.Abs(endAfterResume.Value - tp - expectedEnd) <= 600
        });

        // ---- overlapping SFX: N rapid triggers, per-trigger start latency, all complete ----
        // Polling is INTERLEAVED with the trigger loop (poll pending handles in the spacing
        // gaps, detection granularity ≈ poll interval from trigger 0) — the previous
        // poll-after-all-triggers design read 0 offsets for clips that had already finished
        // (measured artifact) and could not separate detection delay from start latency.
        var handles = new long[SfxCount];
        var trigAt = new double[SfxCount];
        var firstAdvanceAt = new double?[SfxCount];
        var ts = NowMs();
        var maxSimultaneous = 0;
        void PollPending()
        {
            int active = 0;
            for (int i = 0; i < SfxCount; i++)
            {
                if (handles[i] == 0) continue;
                if (firstAdvanceAt[i] == null && h.SfxPositionSec(handles[i]) > 0)
                    firstAdvanceAt[i] = NowMs();
                if (h.SfxActive(handles[i])) active++;
            }
            if (active > maxSimultaneous) maxSimultaneous = active;
        }
        for (int i = 0; i < SfxCount; i++)
        {
            SleepUntil(ts, i * SfxSpacingMs);
            handles[i] = h.SfxPlay(sfx, 0.5f);
            trigAt[i] = NowMs();
            // poll in the gap until the next trigger slot
            while (i < SfxCount - 1 && NowMs() < ts + (i + 1) * SfxSpacingMs - 1)
            {
                PollPending();
                Thread.Sleep(LatencyPollMs);
            }
        }
        // poll remaining advances; clip is 300ms so every trigger is still alive here
        var cap = ts + 2500;
        while (NowMs() < cap && firstAdvanceAt.Any(x => x == null))
        {
            PollPending();
            Thread.Sleep(LatencyPollMs);
        }
        var allDoneAt = WaitFor(() => handles.All(hh => !h.SfxActive(hh)) ? NowMs() : (double?)null, 3000);
        var startedCount = firstAdvanceAt.Count(x => x != null);
        var latencies = trigAt.Zip(firstAdvanceAt, (t, a) => a == null ? (double?)null : a.Value - t)
            .Where(x => x != null).Select(x => Math.Round(x!.Value, 1)).OrderBy(x => x).ToList();
        // completion is only meaningful for triggers that STARTED — a never-started trigger is
        // trivially "inactive" and must not count as completed (vacuity guard).
        var completedAfterStart = Enumerable.Range(0, SfxCount)
            .Count(i => firstAdvanceAt[i] != null && !h.SfxActive(handles[i]));
        object? alDiag = h is OpenAlHarness oa
            ? new { lastAlError = oa.LastSfxError.ToString(), lastStateAfterPlay = oa.LastSfxState }
            : null;
        emit(h.Name, "sfx-overlap", new
        {
            triggers = SfxCount,
            spacingMs = SfxSpacingMs,
            startedCount,
            completedAfterStart,
            neverStarted = SfxCount - startedCount,
            perTriggerStarted = firstAdvanceAt.Select(x => x != null).ToArray(),
            maxSimultaneousActive = maxSimultaneous,
            latencyMinMs = latencies.Count > 0 ? latencies.First() : (double?)null,
            latencyMedianMs = latencies.Count > 0 ? latencies[latencies.Count / 2] : (double?)null,
            latencyMaxMs = latencies.Count > 0 ? latencies.Last() : (double?)null,
            latenciesMs = latencies,
            openAlDiag = alDiag,
            method = $"trigger→first position-advance, poll {LatencyPollMs}ms; resolution = poll interval + device period (see devices note)"
        });

        // ---- whisper busy/completion ----
        h.WhisperPlay(whisper, 0.8f);
        var tw = NowMs();
        var busyAtStart = h.WhisperBusy;
        var wEnd = WaitFor(() => h.WhisperEndAtMs, ToneGen.WhisperMs + 3000);
        SleepUntil(tw, ToneGen.WhisperMs + 600);
        var busyAfter = h.WhisperBusy;
        emit(h.Name, "whisper-busy-completion", new
        {
            durationMs = ToneGen.WhisperMs,
            busyAtStart,
            endSignalAtMs = wEnd != null ? Math.Round(wEnd.Value - tw, 1) : (double?)null,
            withinDeclaredWindow = wEnd != null &&
                                   wEnd - tw >= ToneGen.WhisperMs - CompletionSlopEarlyMs &&
                                   wEnd - tw <= ToneGen.WhisperMs + CompletionSlopLateMs,
            busyAfterCompletion = busyAfter,
            busyClearedByRealCompletion = busyAtStart && !busyAfter && wEnd != null
        });
    }

    /// <summary>Teardown probe: M init/play/dispose cycles, OS handle + thread counts around them.</summary>
    public static void RunTeardown(string backendName, string toneDir, Action<string, string, object> emit)
    {
        const int cycles = 10;
        var sfx = Path.Combine(toneDir, "sfx-300ms.wav");
        var proc = Process.GetCurrentProcess();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        proc.Refresh();
        int handlesBefore = proc.HandleCount, threadsBefore = proc.Threads.Count;
        for (int i = 0; i < cycles; i++)
        {
            using IAudioHarness h = backendName switch
            {
                "soundflow" => new SoundFlowHarness(),
                "openal" => new OpenAlHarness(),
                "naudio" => new NAudioHarness(),
                _ => throw new ArgumentException()
            };
            if (h.SupportedHere && h.TryInit(null, out _))
            {
                h.SfxPlay(sfx, 0.5f);
                Thread.Sleep(100);
            }
        }
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        proc.Refresh();
        emit(backendName, "teardown", new
        {
            cycles,
            handlesBefore,
            handlesAfter = proc.HandleCount,
            handleDelta = proc.HandleCount - handlesBefore,
            threadsBefore,
            threadsAfter = proc.Threads.Count,
            threadDelta = proc.Threads.Count - threadsBefore,
            method = "Process.HandleCount/Threads around 10 full init→play→dispose cycles with GC settle"
        });
    }

    private static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
    private static void SleepUntil(double t0, double offsetMs)
    {
        var remain = t0 + offsetMs - NowMs();
        if (remain > 0) Thread.Sleep((int)remain);
    }

    private static double? WaitFor(Func<double?> probe, int timeoutMs)
    {
        var end = NowMs() + Math.Max(timeoutMs, 0);
        while (NowMs() < end)
        {
            var v = probe();
            if (v != null) return v;
            Thread.Sleep(2);
        }
        return probe();
    }
}
