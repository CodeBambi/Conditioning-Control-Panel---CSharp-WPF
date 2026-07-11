using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Core.Platform;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// Pins the <see cref="IAudioPlayer"/> Pause/Resume seam (WPF parity:
/// AvatarTubeWindow.Speech.cs:1655/1663). The members are default-interface no-ops so a
/// player that cannot pause inherits safe behavior, while a capable player (the LibVLC
/// head) overrides them. Default-interface members are only reachable through the
/// interface reference, which is exactly how the avatar seam calls them.
/// </summary>
public class AudioPlayerPauseResumeTests
{
    private sealed class NoOpAudioPlayer : IAudioPlayer
    {
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void SetVolume(double volume) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        // Pause/Resume intentionally NOT overridden — inherits the DIM no-op default.
    }

    private sealed class CapableAudioPlayer : IAudioPlayer
    {
        public int PauseCalls;
        public int ResumeCalls;
        public Task PlayAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayLoopAsync(string filePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void SetVolume(double volume) { }
        public void Pause() => PauseCalls++;
        public void Resume() => ResumeCalls++;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public void DimDefault_PauseResume_AreCallableNoOps()
    {
        IAudioPlayer player = new NoOpAudioPlayer();
        // Must not throw — the default no-op is the safe fallback for players that cannot pause.
        player.Pause();
        player.Resume();
    }

    [Fact]
    public void Override_PauseResume_AreInvokedThroughInterface()
    {
        var impl = new CapableAudioPlayer();
        IAudioPlayer player = impl;
        player.Pause();
        player.Resume();
        Assert.Equal(1, impl.PauseCalls);
        Assert.Equal(1, impl.ResumeCalls);
    }
}
