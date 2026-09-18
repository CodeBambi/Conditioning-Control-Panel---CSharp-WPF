using System;
using System.IO;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// The launcher's four cues, on the <see cref="EmiDesk.EmiSfx"/> shape: one-shots through
/// <c>App.Audio.PlayOneShot</c>, silent when output is suppressed, the master volume is 0 or the
/// file is missing. Quiet on purpose: the hover cue fires on every tile crossing.
/// </summary>
public static class LauncherSfx
{
    private const float OpenScale = 0.18f;
    private const float HoverScale = 0.06f;
    private const float ClickScale = 0.16f;
    private const float DeniedScale = 0.16f;
    private const int HoverMinGapMs = 130;

    private static readonly object Gate = new();
    private static DateTime _lastHover = DateTime.MinValue;

    /// <summary>The window coming up.</summary>
    public static void Open() => Play("chaos/reveal_chime.mp3", OpenScale, "launcher-open");

    /// <summary>A tile under the cursor. Throttled: a fast sweep across the grid is one cue.</summary>
    public static void Hover()
    {
        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if ((now - _lastHover).TotalMilliseconds < HoverMinGapMs) return;
            _lastHover = now;
        }
        Play("chaos/cards_in.mp3", HoverScale, "launcher-hover");
    }

    /// <summary>Any button.</summary>
    public static void Click() => Play("chaos/ui_click.mp3", ClickScale, "launcher-click");

    /// <summary>A locked tile pressed. The host paints the toast; this is its sound.</summary>
    public static void Denied() => Play("chaos/ui_denied.mp3", DeniedScale, "launcher-denied");

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

    private static void Play(string rel, float scale, string tag)
    {
        if (!Audible(out var master)) return;
        try
        {
            var path = ModResourceResolver.ResolveAudioPath(rel);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            App.Audio?.PlayOneShot(path, Math.Clamp(master * scale, 0f, 1f), tag);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Launcher] sfx {Tag} failed", tag);
        }
    }
}
