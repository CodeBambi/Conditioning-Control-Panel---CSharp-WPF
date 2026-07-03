using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Core.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Cross-platform system audio ducking fallback. Linux uses PulseAudio's pactl;
/// macOS uses AppleScript; other platforms are no-ops.
/// </summary>
public sealed class AvaloniaSystemAudioDucker : ISystemAudioDucker
{
    /// <summary>Matches the WPF <c>DuckingLevel</c> default (80% reduction = 20% output).</summary>
    private const int DefaultDuckStrengthPercent = 80;

    private int? _originalLinuxVolume;
    private int? _originalMacVolume;
    private string? _linuxSink;

    /// <inheritdoc />
    public void Duck() => Duck(DefaultDuckStrengthPercent);

    /// <inheritdoc />
    public void Duck(int strengthPercent)
    {
        // This fallback controls the OUTPUT device volume (not per-session), so a
        // strength of N% maps to setting the output to (100 - N)% of full scale.
        var duckVolumePercent = 100 - Math.Clamp(strengthPercent, 0, 100);

        try
        {
            if (OperatingSystem.IsLinux())
            {
                _linuxSink = GetDefaultPulseAudioSink();
                if (!string.IsNullOrEmpty(_linuxSink) && IsSafeSinkName(_linuxSink))
                {
                    // Only capture the original once per duck window so repeated Duck()
                    // calls don't record the already-ducked volume as "original".
                    _originalLinuxVolume ??= GetPulseAudioSinkVolumePercent(_linuxSink);
                    SetPulseAudioSinkVolume(_linuxSink, duckVolumePercent);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                _originalMacVolume ??= GetMacOutputVolume();
                SetMacOutputVolume(duckVolumePercent);
            }
        }
        catch
        {
            // Best-effort ducking; fail silently on platforms without the expected tooling.
        }
    }

    public void Unduck()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                if (!string.IsNullOrEmpty(_linuxSink) && _originalLinuxVolume.HasValue)
                {
                    SetPulseAudioSinkVolume(_linuxSink, _originalLinuxVolume.Value);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                if (_originalMacVolume.HasValue)
                {
                    SetMacOutputVolume(_originalMacVolume.Value);
                }
            }
        }
        catch
        {
            // Best-effort restore.
        }
        finally
        {
            _originalLinuxVolume = null;
            _originalMacVolume = null;
        }
    }

    /// <summary>
    /// Reset and restore immediately. This fallback tracks a single original volume,
    /// so a forced release is identical to a normal one.
    /// </summary>
    public void ForceUnduck() => Unduck();

    /// <summary>
    /// Rejects sink names that could break out of a command argument (R1-22): double
    /// quotes, backslashes, or control characters. Legitimate PulseAudio sink names
    /// (e.g. <c>alsa_output.pci-0000_00_1b.0.analog-stereo</c>) never contain these.
    /// </summary>
    private static bool IsSafeSinkName(string sinkName)
    {
        foreach (var ch in sinkName)
        {
            if (ch == '"' || ch == '\\' || char.IsControl(ch))
                return false;
        }
        return true;
    }

    private static string? GetDefaultPulseAudioSink()
    {
        var output = RunCommand("pactl", "info");
        if (string.IsNullOrEmpty(output)) return null;

        foreach (var line in output.Split('\n'))
        {
            const string prefix = "Default Sink:";
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return line[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static int? GetPulseAudioSinkVolumePercent(string sinkName)
    {
        var output = RunCommand("pactl", "list", "sinks");
        if (string.IsNullOrEmpty(output)) return null;

        bool inTargetSink = false;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                inTargetSink = line.Equals($"Name: {sinkName}", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inTargetSink && line.StartsWith("Volume:", StringComparison.OrdinalIgnoreCase))
            {
                // Volume: front-left: 65536 / 100% / -0.00 dB ...
                var match = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                {
                    return Math.Clamp(percent, 0, 150);
                }
            }
        }

        return null;
    }

    private static void SetPulseAudioSinkVolume(string sinkName, int percent)
    {
        // Sink name is passed as a discrete argv entry (no string interpolation into a
        // command line), so it can never be re-tokenized or escape quoting (R1-22).
        RunCommand("pactl", "set-sink-volume", sinkName, $"{percent}%");
    }

    private static int? GetMacOutputVolume()
    {
        var output = RunCommand("osascript", "-e", "output volume of (get volume settings)");
        if (int.TryParse(output?.Trim(), out var volume))
        {
            return Math.Clamp(volume, 0, 100);
        }
        return null;
    }

    private static void SetMacOutputVolume(int percent)
    {
        RunCommand("osascript", "-e", $"set volume output volume {Math.Clamp(percent, 0, 100)}");
    }

    private static string? RunCommand(string fileName, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
