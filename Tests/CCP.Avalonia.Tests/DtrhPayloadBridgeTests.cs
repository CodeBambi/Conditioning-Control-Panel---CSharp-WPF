using System;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF DtrhHostService.FirePayload: only video and audio cross the bridge since the
/// in-world cutover; video is silent on an empty library.</summary>
[Collection("DtrhPayloadBridge")]
public sealed class DtrhPayloadBridgeTests : IDisposable
{
    private readonly Func<bool> _has = DtrhPayloadBridge.HasVideos, _video = DtrhPayloadBridge.TriggerVideo, _whisper = DtrhPayloadBridge.Whisper;
    private int _videos, _whispers;

    public DtrhPayloadBridgeTests()
    {
        DtrhPayloadBridge.HasVideos = () => true;
        DtrhPayloadBridge.TriggerVideo = () => { _videos++; return true; };
        DtrhPayloadBridge.Whisper = () => { _whispers++; return true; };
    }

    public void Dispose()
    {
        DtrhPayloadBridge.HasVideos = _has;
        DtrhPayloadBridge.TriggerVideo = _video;
        DtrhPayloadBridge.Whisper = _whisper;
    }

    [Fact]
    public void Video_and_audio_fire_their_native_effect()
    {
        Assert.Equal("video", DtrhPayloadBridge.Fire("{\"kind\":\"video\",\"strength\":80}"));
        Assert.Equal("audio", DtrhPayloadBridge.Fire("{\"kind\":\"AUDIO\"}"));
        Assert.Equal(1, _videos);
        Assert.Equal(1, _whispers);
    }

    [Theory]
    [InlineData("{\"kind\":\"flash\"}")]
    [InlineData("{\"kind\":\"spiral\"}")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void In_world_kinds_and_junk_are_ignored(string json)
    {
        Assert.Null(DtrhPayloadBridge.Fire(json));
        Assert.Equal(0, _videos + _whispers);
    }

    [Fact]
    public void An_empty_video_library_fires_nothing_and_shows_no_dialog()
    {
        DtrhPayloadBridge.HasVideos = () => false;
        Assert.Null(DtrhPayloadBridge.Fire("{\"kind\":\"video\"}"));
        Assert.Equal(0, _videos);
    }
}
