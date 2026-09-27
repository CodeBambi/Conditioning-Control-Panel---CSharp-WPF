using System;
using System.Collections.Generic;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The launcher's six cues, on the <see cref="EmiDesk.EmiSfx"/> shape: one-shots through
/// <c>App.Audio.PlayOneShot</c>, silent when output is suppressed, the master volume is 0 or the
/// file is missing. Quiet on purpose: the hover cue fires on every tile crossing, and only three
/// notes are ever left ringing (the oldest is cut), so a fast sweep never piles up.
///
/// <para>What a crossing plays is <see cref="LauncherMelody"/>'s call: the next note of a phrase
/// it composes as the pointer moves.</para>
/// </summary>
public static class LauncherSfx
{
    private const float OpenScale = 0.18f;
    private const float HoverScale = 0.05f;
    private const float ClickScale = 0.16f;
    private const float DeniedScale = 0.16f;
    private const float LaunchScale = 0.24f;
    private const float ReturnScale = 0.14f;

    /// <summary>Notes left ringing at once. The pluck is short, so three is already generous.</summary>
    private const int HoverVoices = 3;

    private static readonly object Gate = new();
    private static readonly LauncherMelody Melody = new();
    private static readonly Queue<AudioPlaybackHandle> Ringing = new();
    private static DateTime _lastHover = DateTime.MinValue;

    /// <summary>The Breakout cabinet's brick hit (stations/breakout/audio.js, combo 0), rendered
    /// once to a file: a soft C5 ping through the cabinet's small delay room, 0.58 s. Kept as the
    /// fallback for a bad install - a missing note must not make the launcher silent - and as the
    /// one file a mod can drop in to take the hover cue over.</summary>
    private const string HoverCue = "launcher/hover.wav";

    /// <summary>One rendered rung of the pluck (Scripts/render-launcher-notes.mjs).</summary>
    private static string NoteCue(int rung) => $"launcher/wood_{rung:00}.wav";

    /// <summary>The window coming up. The next hover opens a fresh phrase.</summary>
    public static void Open()
    {
        Melody.Reset();
        Play("chaos/reveal_chime.mp3", OpenScale, "launcher-open");
    }

    /// <summary>
    /// A tile under the cursor. Throttled, so a pointer thrown across the grid cannot machine-gun
    /// the ladder.
    ///
    /// Each crossing is the next note of a phrase <see cref="LauncherMelody"/> composes as it goes,
    /// so browsing the launcher plays a little tune, a different one every time, and the player is
    /// the one playing it.
    /// </summary>
    public static void Hover()
    {
        var now = DateTime.UtcNow;
        LauncherMelody.Cue cue;
        lock (Gate)
        {
            if ((now - _lastHover).TotalMilliseconds < LauncherMelody.MinGapMs) return;
            _lastHover = now;
            cue = Melody.Next(now);
        }

        var level = (float)Math.Clamp(cue.Level, 0d, 1d) * HoverScale;
        var handle = Play(NoteCue(cue.Rung), level, "launcher-hover", HoverCue);
        if (handle == null) return;

        AudioPlaybackHandle? oldest = null;
        lock (Gate)
        {
            Ringing.Enqueue(handle);
            while (Ringing.Count > 0 && Ringing.Peek().IsFinished) Ringing.Dequeue();
            if (Ringing.Count > HoverVoices) oldest = Ringing.Dequeue();
        }
        try { oldest?.Stop(); } catch (Exception ex) { Log.Debug(ex, "[Launcher] hover cut failed"); }
    }

    /// <summary>Any button.</summary>
    public static void Click() => Play("chaos/ui_click.mp3", ClickScale, "launcher-click");

    /// <summary>A locked tile pressed. The host paints the toast; this is its sound.</summary>
    public static void Denied() => Play("chaos/ui_denied.mp3", DeniedScale, "launcher-denied");

    /// <summary>Play or the CTA landing: the sting under the exit beat. Ends the hover phrase.</summary>
    public static void Launch()
    {
        Melody.Reset();
        Play("chaos/reveal_chime.mp3", LaunchScale, "launcher-launch");
    }

    /// <summary>The launcher back after a game. The next sweep starts a new phrase.</summary>
    public static void Return()
    {
        Melody.Reset();
        Play("chaos/dling.mp3", ReturnScale, "launcher-return");
    }

    private static bool Audible(out float master)
    {
        master = 0f;
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return false;
            // The speaker button in the launcher's title bar. One flag, ahead of every cue, so
            // muted means muted and not "muted except the one I forgot".
            if (App.Settings?.Current?.LauncherSoundEnabled == false) return false;
            int level = App.Settings?.Current?.MasterVolume ?? 0;
            if (level <= 0) return false;
            master = Math.Clamp(level / 100f, 0f, 1f);
            return master > 0f;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Launcher] sfx audibility probe failed");
            return false;
        }
    }

    private static AudioPlaybackHandle? Play(string rel, float scale, string tag, string? fallbackRel = null)
    {
        if (!Audible(out var master)) return null;
        try
        {
            var path = Resolve(rel) ?? (fallbackRel == null ? null : Resolve(fallbackRel));
            if (path == null) return null;
            return App.Audio?.PlayOneShot(path, Math.Clamp(master * scale, 0f, 1f), tag);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Launcher] sfx {Tag} failed", tag);
            return null;
        }
    }

    /// <summary>The clip's path on disk, or null when it is not installed.</summary>
    private static string? Resolve(string rel)
    {
        var path = ModResourceResolver.ResolveAudioPath(rel);
        return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : path;
    }
}
