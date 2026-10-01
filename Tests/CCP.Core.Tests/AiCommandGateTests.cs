using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Commands;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The AI effect control gate, moved from the WPF head to Core so every head runs the same
/// one: master switch AND live Lab access, per-effect toggles, the Videos feature for videos, the
/// 3-per-reply cap, the executors' clamps, and an unseeded (absent) surface reported as not fired.</summary>
[Collection(SessionStatics.Name)]
public sealed class AiCommandGateTests : IDisposable
{
    private readonly Func<SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly Func<bool>? _lab = CoreAccount.HasLabAccessProvider;
    private readonly SettingsService _service = new();
    private readonly List<(int Amount, int Ms, int Size)> _flashes = new();
    private readonly List<string?> _videos = new();
    private readonly List<string> _feed = new();
    private bool _labAccess = true;

    public AiCommandGateTests()
    {
        CoreSettings.ServiceProvider = () => _service;
        CoreAccount.HasLabAccessProvider = () => _labAccess;
        var p = _service.Current.CompanionPrompt;
        p.AllowAiToControlEffects = true;
        p.AllowAiFlash = p.AllowAiVideo = p.AllowAiOverlay = true;
        _service.Current.MandatoryVideosEnabled = true;
        FlashImageCommand.Surface = (a, ms, s) => { _flashes.Add((a, ms, s)); return true; };
        MediaCommand.VideoSurface = path => { _videos.Add(path); return true; };
        SpiralCommand.Surface = null;
        AiCommandService.LiveActionSink = _feed.Add;
    }

    public void Dispose()
    {
        CoreSettings.ServiceProvider = _provider;
        CoreAccount.HasLabAccessProvider = _lab;
        FlashImageCommand.Surface = null;
        MediaCommand.VideoSurface = null;
        AiCommandService.LiveActionSink = null;
    }

    private static AiCommandData Flash(int amount = 3) =>
        new() { Command = AICommandType.flash_image, Data = new FlashImage(amount, 4, 100, 100) };

    private static void Run(params AiCommandData[] commands)
    {
        var service = new AiCommandService();
        service.BeginBatch();
        foreach (var c in commands) service.ExecuteCommand(c);
    }

    [Fact]
    public void AllowedFlashRunsClampedAndIsReported()
    {
        Run(Flash(amount: 99));
        Assert.Equal(new[] { (8, 4000, 100) }, _flashes);
        Assert.Single(_feed);
    }

    [Fact]
    public void MasterSwitchOffRefuses()
    {
        _service.Current.CompanionPrompt.AllowAiToControlEffects = false;
        Run(Flash());
        Assert.Empty(_flashes);
        Assert.Empty(_feed);
    }

    [Fact]
    public void NoLabAccessRefusesEvenWithTheSwitchOn()
    {
        _labAccess = false;
        Run(Flash());
        Assert.Empty(_flashes);
    }

    [Fact]
    public void PerEffectToggleOffRefuses()
    {
        _service.Current.CompanionPrompt.AllowAiFlash = false;
        Run(Flash());
        Assert.Empty(_flashes);
    }

    [Fact]
    public void VideoNeedsTheVideosFeature()
    {
        var video = new AiCommandData { Command = AICommandType.video, Data = new Media("", "", Random: true) };
        _service.Current.MandatoryVideosEnabled = false;
        Run(video);
        Assert.Empty(_videos);
        _service.Current.MandatoryVideosEnabled = true;
        Run(video);
        Assert.Equal(new string?[] { null }, _videos);
    }

    [Fact]
    public void AtMostThreeCommandsPerReply()
    {
        Run(Flash(), Flash(), Flash(), Flash(), Flash());
        Assert.Equal(AiCommandService.MaxCommandsPerResponse, _flashes.Count);
    }

    [Fact]
    public void AbsentSurfaceIsReportedAsNotFiredNeverFaked()
    {
        Run(new AiCommandData { Command = AICommandType.spiral, Data = new SpiralPinkFiler(true, 20) });
        Assert.Equal(2, _feed.Count);
        Assert.Contains("didn't fire", _feed[1]);
    }
}
