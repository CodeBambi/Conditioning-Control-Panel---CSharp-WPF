using System;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>When libvlc is missing the head leaves CoreAudio unseeded; callers sequencing on a
/// clip must still be told it finished, or they hang.</summary>
public sealed class CoreAudioTests
{
    [Fact]
    public void UnseededPlayOneShotStillFiresFinished()
    {
        Assert.Null(CoreAudio.PlayOneShotProvider);
        var started = false;
        var finished = false;
        CoreAudio.PlayOneShot("/nonexistent.wav", 1f, "test", _ => started = true, () => finished = true);
        Assert.True(finished);
        Assert.False(started);
    }
}
