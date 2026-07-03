using System;
using ConditioningControlPanel.Models;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

namespace ConditioningControlPanel.Avalonia.Services.Video;

/// <summary>
/// Helpers for applying CCP audio settings (master/video volume and preferred output device)
/// to a LibVLC <see cref="MediaPlayer"/>.
/// </summary>
internal static class LibVlcAudioHelper
{
    /// <summary>
    /// Calculates the effective video volume from master and video volume settings.
    /// </summary>
    public static int GetEffectiveVolume(AppSettings? settings)
    {
        var master = Math.Clamp(settings?.MasterVolume ?? 50, 0, 100);
        var video = Math.Clamp(settings?.VideoVolume ?? 50, 0, 100);
        return (int)(master * video / 100.0);
    }

    /// <summary>
    /// Applies volume and preferred output device to a LibVLC media player.
    /// </summary>
    /// <param name="player">The media player to configure.</param>
    /// <param name="settings">Current app settings.</param>
    /// <param name="withAudio">False for muted secondary-monitor players.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public static void ApplyAudioSettings(this MediaPlayer player, AppSettings? settings, bool withAudio, ILogger? logger = null)
    {
        try
        {
            var effectiveVolume = withAudio ? GetEffectiveVolume(settings) : 0;

            player.Mute = effectiveVolume <= 0;
            if (effectiveVolume > 0)
            {
                player.Volume = effectiveVolume;
            }

            if (withAudio && !string.IsNullOrEmpty(settings?.AudioOutputDeviceId))
            {
                ApplyOutputDevice(player, settings.AudioOutputDeviceId, logger);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to apply LibVLC audio settings");
        }
    }

    /// <summary>
    /// Applies volume/mute only — no output-device re-bind. Used for live volume-slider
    /// updates, matching the WPF contract (UpdatePlayingVideosVolume writes Volume only).
    /// </summary>
    public static void ApplyVolume(this MediaPlayer player, AppSettings? settings, bool withAudio)
    {
        try
        {
            var effectiveVolume = withAudio ? GetEffectiveVolume(settings) : 0;
            player.Mute = effectiveVolume <= 0;
            if (effectiveVolume > 0)
            {
                player.Volume = effectiveVolume;
            }
        }
        catch
        {
            // Player may be mid-teardown; volume updates are best-effort.
        }
    }

    private static void ApplyOutputDevice(MediaPlayer player, string deviceId, ILogger? logger)
    {
        try
        {
            bool found = false;
            try
            {
                var available = player.AudioOutputDeviceEnum;
                if (available != null)
                {
                    foreach (var d in available)
                    {
                        if (string.Equals(d.DeviceIdentifier, deviceId, StringComparison.Ordinal))
                        {
                            found = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception enumEx)
            {
                logger?.LogDebug(enumEx, "LibVLC audio output device enumeration failed");
                found = true;
            }

            if (found)
            {
                player.SetOutputDevice(deviceId);
            }
            else
            {
                logger?.LogWarning("Saved audio output device {DeviceId} is not present in LibVLC outputs; using system default", deviceId);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to apply LibVLC output device");
        }
    }
}
