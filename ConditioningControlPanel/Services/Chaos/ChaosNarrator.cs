using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The Madam's voice: plays a narrator line's audio (when present) + text, ducks the audio bed while
/// she speaks, and exposes <see cref="IsPlaying"/> as the single source of truth other emitters check.
///
/// The "bed" in this app is NOT an in-app music track — it is whatever else is audible: the user's own
/// system audio plus the app's videos/whispers. <see cref="AudioService.Duck"/> is the existing lever
/// (lowers other apps' WASAPI sessions, ref-counted + generation-guarded); we duck to ~28% while she
/// speaks and unduck after. Pop SFX (ChaosSfx) are intentionally left alone. Barks hold via a gate in
/// BarkService that consults <see cref="IsPlaying"/> (mirrors the whisper-active gate).
///
/// SLICE SCOPE: <see cref="AudioService.Duck"/>/Unduck are stepwise (no fade). The locked 150–200ms /
/// 400ms ramp is a follow-up. Audio clips are placeholders for now — a text-only line still ducks +
/// holds barks for its dwell, so the four risky systems are feel-able with the user's own music.
/// </summary>
public static class ChaosNarrator
{
    private const int DUCK_STRENGTH = 72;          // → ~28% remaining (target 25–30%)
    private const int MIN_DWELL_MS = 2400;         // text-only floor
    private const int MAX_DWELL_MS = 7000;

    private static long _busyUntilTicks;
    private static long _duckGen = -1;
    private static readonly object _lock = new();
    private static DispatcherTimer? _endTimer;

    /// <summary>True while the Madam is (approximately) still speaking. Time-based, like IsWhisperAudioPlaying.</summary>
    public static bool IsPlaying
    {
        get
        {
            var until = Interlocked.Read(ref _busyUntilTicks);
            return until > 0 && DateTime.UtcNow.Ticks < until;
        }
    }

    /// <summary>Speak a cue: resolve audio, show text (band-priority), duck the bed, play, schedule unduck.</summary>
    public static void Speak(ChaosNarrativeCue cue, bool interrupt)
    {
        try
        {
            string? audioPath = ResolveAudio(cue.AudioKey);
            int durMs = EstimateDurationMs(cue.Text, audioPath);

            // Text on screen (the announcer's FIFO is band-priority aware; STORY interrupts).
            ChaosAnnouncerOverlay.AnnounceNarrator(cue.Text, (int)cue.Band, interrupt, durMs);

            // Mark speaking + duck the bed.
            Interlocked.Exchange(ref _busyUntilTicks, DateTime.UtcNow.AddMilliseconds(durMs + 200).Ticks);
            BeginDuck();

            if (audioPath != null) PlayAsync(audioPath);

            // Schedule the unduck/clear on the UI thread (single timer, reset each Speak).
            var disp = Application.Current?.Dispatcher;
            if (disp != null && !disp.HasShutdownStarted)
            {
                disp.Invoke(() =>
                {
                    _endTimer ??= new DispatcherTimer();
                    _endTimer.Stop();
                    _endTimer.Interval = TimeSpan.FromMilliseconds(durMs + 220);
                    _endTimer.Tick -= OnEnd;
                    _endTimer.Tick += OnEnd;
                    _endTimer.Start();
                });
            }
        }
        catch (Exception ex) { App.Logger?.Warning("ChaosNarrator.Speak failed: {E}", ex.Message); }
    }

    /// <summary>
    /// Play one conversation-card line's audio (when present) and duck the bed, WITHOUT the announcer
    /// overlay — the card owns the on-screen text. Ducking is held across the whole card (BeginDuck is
    /// idempotent; each line extends the busy window so barks keep holding); call <see cref="EndCard"/>
    /// when the card closes to unduck. Returns the estimated line duration (ms) so the card can time
    /// auto-advance.
    /// </summary>
    public static int PlayCardLine(string? audioKey, string text)
    {
        try
        {
            string? path = ResolveAudio(audioKey);
            int durMs = EstimateDurationMs(text, path);
            Interlocked.Exchange(ref _busyUntilTicks, DateTime.UtcNow.AddMilliseconds(durMs + 200).Ticks);
            BeginDuck();
            if (path != null) PlayAsync(path);
            return durMs;
        }
        catch (Exception ex) { App.Logger?.Warning("ChaosNarrator.PlayCardLine failed: {E}", ex.Message); return MIN_DWELL_MS; }
    }

    /// <summary>Release the card's duck + speaking hold (conversation closed). Same teardown as <see cref="Reset"/>.</summary>
    public static void EndCard() => Reset();

    /// <summary>Force-stop ducking + clear state (run teardown).</summary>
    public static void Reset()
    {
        try { Application.Current?.Dispatcher.Invoke(() => { _endTimer?.Stop(); }); } catch { }
        Interlocked.Exchange(ref _busyUntilTicks, 0);
        EndDuck();
    }

    private static void OnEnd(object? sender, EventArgs e)
    {
        try { _endTimer?.Stop(); } catch { }
        // If a later Speak extended the window, don't unduck early.
        if (IsPlaying) return;
        EndDuck();
    }

    private static void BeginDuck()
    {
        lock (_lock)
        {
            try
            {
                if (_duckGen >= 0) return;   // already ducking for the narrator
                App.Audio?.Duck(DUCK_STRENGTH);
                _duckGen = App.Audio?.DuckGeneration ?? -1;
            }
            catch { _duckGen = -1; }
        }
    }

    private static void EndDuck()
    {
        lock (_lock)
        {
            if (_duckGen < 0) return;
            try { App.Audio?.Unduck(_duckGen); } catch { }
            _duckGen = -1;
        }
    }

    private static int EstimateDurationMs(string text, string? audioPath)
    {
        if (audioPath != null)
        {
            try
            {
                using var r = new AudioFileReader(audioPath);
                int ms = (int)r.TotalTime.TotalMilliseconds + 250;
                return Math.Clamp(ms, MIN_DWELL_MS, MAX_DWELL_MS);
            }
            catch { /* fall through to text estimate */ }
        }
        // ~55ms/char reading pace, floored.
        int est = 1200 + (text?.Length ?? 0) * 55;
        return Math.Clamp(est, MIN_DWELL_MS, MAX_DWELL_MS);
    }

    /// <summary>Resolve a narrator clip: user assets then bundled, at assets/Chaos/narrator/{key}.(mp3|wav). Null if absent.</summary>
    private static string? ResolveAudio(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        foreach (var root in Roots())
        {
            foreach (var ext in new[] { ".mp3", ".wav" })
            {
                var p = Path.Combine(root, "assets", "Chaos", "narrator", key + ext);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<string> Roots()
    {
        string? user = null;
        try { user = App.UserAssetsPath; } catch { }
        if (!string.IsNullOrEmpty(user)) yield return user!;
        yield return AppContext.BaseDirectory;
    }

    private static void PlayAsync(string path)
    {
        float vol = (App.Settings?.Current?.MasterVolume ?? 100) / 100f;
        App.Audio?.PlayOneShot(path, vol, "chaos-narrator");
    }
}
