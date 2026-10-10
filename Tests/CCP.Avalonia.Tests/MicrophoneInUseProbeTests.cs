using System;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF WasapiMicrophoneProbe on the port: one sweep's answer is reused for five seconds, and a sweep
/// that cannot tell (Linux, no audio stack, a thrown endpoint) reads as "not in use". The real
/// sweep only lists capture sessions; it never opens the microphone.
/// </summary>
public sealed class MicrophoneInUseProbeTests
{
    private static readonly DateTime T0 = new(2026, 10, 10, 12, 0, 0);

    [Fact]
    public void OneSweepIsReusedForFiveSeconds()
    {
        var sweeps = 0;
        var answer = true;
        var probe = new MicrophoneInUseProbe(() => { sweeps++; return answer; });
        Assert.True(probe.IsInUse(T0));
        answer = false;
        Assert.True(probe.IsInUse(T0.AddSeconds(4.9)));
        Assert.Equal(1, sweeps);
        Assert.False(probe.IsInUse(T0.AddSeconds(5)));
        Assert.Equal(2, sweeps);
    }

    [Fact]
    public void Unknown_ReadsAsNotInUse()
    {
        Assert.False(new MicrophoneInUseProbe(() => null).IsInUse(T0));
    }

    [Fact]
    public void AThrowingSweep_ReadsAsNotInUse()
    {
        Assert.False(new MicrophoneInUseProbe(() => throw new InvalidOperationException("no audio stack")).IsInUse(T0));
    }

    [Fact]
    public void ThePlatformSweep_NeverThrows_AndIsUnknownOffWindows()
    {
        var answer = MicrophoneInUseProbe.PlatformSweep();
        if (!OperatingSystem.IsWindows()) Assert.Null(answer);
        // Desk check of the COM interop: CCP_MIC_PROBE_STRICT=1 demands a real answer from the audio stack.
        if (Environment.GetEnvironmentVariable("CCP_MIC_PROBE_STRICT") == "1") Assert.NotNull(answer);
        Assert.Equal(OperatingSystem.IsWindows(), MicrophoneInUseProbe.Supported);
    }
}
