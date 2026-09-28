using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>LayeredAudio's state machine with LibVLC stubbed: WPF LayeredAudioService's start
/// rules, live volume, cooperative duck and stop.</summary>
public sealed class LayeredAudioTests
{
    private sealed class FakePlayer : LayeredAudio.ILayerPlayer
    {
        public string Path = "";
        public int Volume { get; set; } = -1;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void StartVolumeDuckStopFollowWpfRules()
    {
        var s = CoreSettings.Current;
        var saved = (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume);
        var file = Path.GetTempFileName();
        try
        {
            var a = new AudioLayerTrack { Path = file, Volume = 100 };
            var b = new AudioLayerTrack { Path = file, Volume = 50 };
            s.AudioLayers = new List<AudioLayerTrack>
            {
                a, b,
                new() { Path = file, Enabled = false },          // disabled: skipped
                new() { Path = file + ".missing" },              // missing file: skipped
            };
            s.AudioLayersMasterVolume = 100;
            s.MasterVolume = 100;
            s.AudioLayersEnabled = false;

            var opened = new List<FakePlayer>();
            var layers = new LayeredAudio(p => { var f = new FakePlayer { Path = p }; opened.Add(f); return f; });

            layers.Start();                                      // master toggle off: nothing
            Assert.Empty(opened);
            layers.Start(ignoreMasterToggle: true);              // audio-only session path
            Assert.Equal(2, opened.Count);
            Assert.Equal(100, opened[0].Volume);
            Assert.Equal(79, opened[1].Volume);                  // cbrt(0.5): linear gain on LibVLC's cubic scale

            layers.SetTrackVolumeLive(b, 0);
            Assert.Equal(0, opened[1].Volume);
            s.AudioLayersMasterVolume = 50;
            layers.SetMasterVolumeLive();
            Assert.Equal(79, opened[0].Volume);
            layers.ApplyDuck(0.8f);                              // 0.5 x 0.2 = 0.1
            Assert.Equal(46, opened[0].Volume);
            layers.ReleaseDuck();
            Assert.Equal(79, opened[0].Volume);

            s.AudioLayersEnabled = true;
            layers.Restart();                                    // rebuilds from settings
            Assert.True(opened[0].Disposed && opened[1].Disposed);
            Assert.Equal(4, opened.Count);
            layers.Stop();
            Assert.True(opened[2].Disposed && opened[3].Disposed);
            Assert.False(layers.IsPlaying);
            s.AudioLayersEnabled = false;
            layers.Restart();                                    // stopped and master off: stays off
            Assert.Equal(4, opened.Count);
        }
        finally
        {
            (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume) = saved;
            File.Delete(file);
        }
    }
}
