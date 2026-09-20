using System;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The launcher's six cues, on the <see cref="EmiDesk.EmiSfx"/> shape: one-shots through
/// <c>App.Audio.PlayOneShot</c>, silent when output is suppressed, the master volume is 0 or the
/// file is missing. Quiet on purpose: the hover cue fires on every tile crossing, and only one
/// hover instance is ever in flight (the previous one is stopped), so a sweep never stacks.
/// </summary>
public static class LauncherSfx
{
    private const float OpenScale = 0.18f;
    private const float HoverScale = 0.05f;
    private const float ClickScale = 0.16f;
    private const float DeniedScale = 0.16f;
    private const float LaunchScale = 0.24f;
    private const float ReturnScale = 0.14f;
    private const int HoverMinGapMs = 130;

    private static readonly object Gate = new();
    private static DateTime _lastHover = DateTime.MinValue;
    private static AudioPlaybackHandle? _hover;

    /// <summary>The Breakout cabinet's brick hit (stations/breakout/audio.js, combo 0), rendered
    /// once to a file: a soft C5 ping through the cabinet's small delay room, 0.58 s.</summary>
    private const string HoverCue = "launcher/hover.wav";

    /// <summary>The window coming up.</summary>
    public static void Open() => Play("chaos/reveal_chime.mp3", OpenScale, "launcher-open");

    /// <summary>A tile under the cursor. Throttled, and the previous hover is cut before the
    /// next starts: a sweep across the grid is one soft ping moving, never a pile of them.</summary>
    public static void Hover()
    {
        var now = DateTime.UtcNow;
        AudioPlaybackHandle? previous;
        lock (Gate)
        {
            if ((now - _lastHover).TotalMilliseconds < HoverMinGapMs) return;
            _lastHover = now;
            previous = _hover;
        }
        try { previous?.Stop(); } catch (Exception ex) { Log.Debug(ex, "[Launcher] hover cut failed"); }
        var handle = Play(HoverCue, HoverScale, "launcher-hover");
        lock (Gate) _hover = handle;
    }

    /// <summary>Any button.</summary>
    public static void Click() => Play("chaos/ui_click.mp3", ClickScale, "launcher-click");

    /// <summary>A locked tile pressed. The host paints the toast; this is its sound.</summary>
    public static void Denied() => Play("chaos/ui_denied.mp3", DeniedScale, "launcher-denied");

    /// <summary>Play or the CTA landing: the sting under the exit beat.</summary>
    public static void Launch() => Play("chaos/reveal_chime.mp3", LaunchScale, "launcher-launch");

    /// <summary>The launcher back after a game.</summary>
    public static void Return() => Play("chaos/dling.mp3", ReturnScale, "launcher-return");

    private static bool Audible(out float master)
    {
        master = 0f;
        try
        {
            var audio = App.Audio;
            if (audio == null || audio.IsOutputSuppressed) return false;
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

    private static AudioPlaybackHandle? Play(string rel, float scale, string tag)
    {
        if (!Audible(out var master)) return null;
        try
        {
            var path = ModResourceResolver.ResolveAudioPath(rel);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return App.Audio?.PlayOneShot(path, Math.Clamp(master * scale, 0f, 1f), tag);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Launcher] sfx {Tag} failed", tag);
            return null;
        }
    }
}
