using ConditioningControlPanel;
using ConditioningControlPanel.Services.Haptics;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The DtRH haptic director in Core (WPF Services/Haptics/DtrhHapticDirector.cs): the ambient curve,
/// the page feed and lifecycle it holds, and that a head with no device is a quiet no-op.
/// </summary>
public class DtrhHapticDirectorTests
{
    [Fact]
    public void TheAmbientFloorIsADepthGauge()
    {
        Assert.Equal(0, DtrhHapticDirector.AmbientTarget(0, 1, 1));               // slider at zero: off
        Assert.Equal(0.15, DtrhHapticDirector.AmbientTarget(0.5, 0, 0), 3);       // a whisper at the surface
        Assert.Equal(0.5, DtrhHapticDirector.AmbientTarget(0.5, 1, 0), 3);        // the slider's value at the bottom
        Assert.Equal(0.75, DtrhHapticDirector.AmbientTarget(0.5, 1, 1), 3);       // the melt pushes past it
        Assert.Equal(1.0, DtrhHapticDirector.AmbientTarget(1, 1, 1), 3);          // never over full
        Assert.Equal(0.06, DtrhHapticDirector.AmbientTarget(0.1, 0, 0), 3);       // clears the <= 0.05 = off cutoff
    }

    [Fact]
    public void WithNoDeviceEveryEntryIsAQuietNoOp()
    {
        var old = CoreHaptics.Service;
        CoreHaptics.Service = null;
        try
        {
            DtrhHapticDirector.OnLaunch(testMode: false);
            Assert.True(DtrhHapticDirector.Snapshot.Active);
            Assert.False(DtrhHapticDirector.IsReady);                             // nothing to drive

            DtrhHapticDirector.OnRunStarted();
            DtrhHapticDirector.OnHapticState(JObject.Parse("{\"running\":true,\"depth\":1.7,\"melt\":-2}"));
            var s = DtrhHapticDirector.Snapshot;
            Assert.True(s.RunActive);
            Assert.True(s.PageRunning);
            Assert.Equal(1, s.Depth);                                             // clamped 0..1
            Assert.Equal(0, s.Melt);

            DtrhHapticDirector.OnGameEvent(JObject.Parse("{\"event\":\"detonated\"}"));
            DtrhHapticDirector.OnGameEvent(JObject.Parse("{\"event\":\"effect-fired\",\"kind\":\"flash\"}"));
            DtrhHapticDirector.OnWorldFreeze(true);
            Assert.True(DtrhHapticDirector.Snapshot.WorldFrozen);
            DtrhHapticDirector.OnWorldFreeze(false);
            DtrhHapticDirector.OnVideoCovering(true);
            Assert.True(DtrhHapticDirector.Snapshot.VideoCovering);
            DtrhHapticDirector.OnVideoCovering(false);

            DtrhHapticDirector.OnRunEnded();
            s = DtrhHapticDirector.Snapshot;
            Assert.False(s.RunActive);
            Assert.False(s.PageRunning);
            Assert.Equal(0, s.Depth);
        }
        finally
        {
            DtrhHapticDirector.OnClosed();
            CoreHaptics.Service = old;
        }
        Assert.False(DtrhHapticDirector.Snapshot.Active);
        DtrhHapticDirector.OnClosed();                                            // twice is fine
    }
}
