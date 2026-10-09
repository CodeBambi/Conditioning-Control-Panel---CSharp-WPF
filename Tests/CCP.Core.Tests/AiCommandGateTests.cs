using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        p.AllowAiFlash = p.AllowAiVideo = p.AllowAiOverlay = p.AllowAiGetBackToMe = true;
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
        GetBackToMeCommand.AiProvider = null;
        GetBackToMeCommand.SaySurface = null;
        ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.CommandExecutor = null;
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

    // ---- getbacktome follow-ups: re-gated when they fire, 3 per follow-up, cancelled by panic / switch-off ----

    private static AiCommandData FollowUp(params AiCommandData[] nested) => new()
    {
        Command = AICommandType.getbacktome,
        Data = new GetBackToMe(1, "t" + Guid.NewGuid().ToString("N"), nested.ToList(), null, JsonOnly: true),
    };

    private static Task PastTheDelay() => Task.Delay(3000);   // the 1 s minimum delay, with a wide margin

    [Fact]
    public async Task NestedCommandWithItsToggleOffIsRefused()
    {
        Run(FollowUp(Flash()));
        _service.Current.CompanionPrompt.AllowAiFlash = false;   // changed during the delay
        await PastTheDelay();
        Assert.Empty(_flashes);
        Assert.Contains(_feed, l => l.Contains("Follow-up flash_image blocked"));
    }

    [Fact]
    public async Task FourthNestedCommandIsRefused()
    {
        Run(FollowUp(Flash(), Flash(), Flash(), Flash(), Flash()));
        await PastTheDelay();
        Assert.Equal(AiCommandService.MaxCommandsPerResponse, _flashes.Count);
    }

    [Fact]
    public async Task PanicDuringTheDelayMeansNothingFires()
    {
        Run(FollowUp(Flash()));
        AiCommandService.CancelAll();
        await PastTheDelay();
        Assert.Empty(_flashes);
    }

    [Fact]
    public async Task SwitchOffDuringTheDelayCancelsTheFollowUp()
    {
        Run(FollowUp(Flash()));
        _service.Current.CompanionPrompt.AllowAiToControlEffects = false;
        await PastTheDelay();
        Assert.Empty(_flashes);
    }

    /// <summary>An AI stand-in whose follow-up reply waits until the test releases it.</summary>
    public class HeldAi : System.Reflection.DispatchProxy
    {
        public readonly TaskCompletionSource<ConditioningControlPanel.Services.Moderation.AiReplyResult> Reply = new();
        public volatile bool Asked;
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "GetBambiReplyExAsync") { Asked = true; return Reply.Task; }
            var t = method?.ReturnType;
            return t != null && t.IsValueType && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
    }

    [Fact]
    public async Task PanicDuringTheAiReplyDropsTheReplyAndTheNestedCommands()
    {
        var ai = System.Reflection.DispatchProxy.Create<ConditioningControlPanel.Services.AIService.IAiService, HeldAi>();
        var held = (HeldAi)(object)ai;
        var said = new List<string>();
        GetBackToMeCommand.AiProvider = () => ai;
        GetBackToMeCommand.SaySurface = (text, _) => said.Add(text);
        Run(new AiCommandData
        {
            Command = AICommandType.getbacktome,
            Data = new GetBackToMe(1, "t" + Guid.NewGuid().ToString("N"), new List<AiCommandData> { Flash() }, null, JsonOnly: false),
        });
        for (var i = 0; i < 100 && !held.Asked; i++) await Task.Delay(50);
        Assert.True(held.Asked);
        AiCommandService.CancelAll();                                // panic while the AI is answering
        held.Reply.SetResult(new ConditioningControlPanel.Services.Moderation.AiReplyResult("hi", true, null));
        await Task.Delay(300);
        Assert.Empty(said);
        Assert.Empty(_flashes);
    }

    // ---- audit #1987: the follow-up's reply runs its own commands inside the AI call (CompanionBrain.CommandExecutor) ----

    /// <summary>An AI stand-in that, like the real providers, executes its reply's commands inside
    /// GetBambiReplyExAsync once the test releases it.</summary>
    public class ActingAi : System.Reflection.DispatchProxy
    {
        public readonly TaskCompletionSource Asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new();
        public List<AiCommandData> ReplyCommands = new();
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "GetBambiReplyExAsync") return Reply();
            var t = method?.ReturnType;
            return t != null && t.IsValueType && t != typeof(void) ? Activator.CreateInstance(t) : null;
        }
        private async Task<ConditioningControlPanel.Services.Moderation.AiReplyResult> Reply()
        {
            Asked.TrySetResult();
            await Release.Task;
            ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.CommandExecutor?.Invoke(ReplyCommands);
            return new ConditioningControlPanel.Services.Moderation.AiReplyResult("hi", true, null);
        }
    }

    private ActingAi SeedActingAi()
    {
        var ai = System.Reflection.DispatchProxy.Create<ConditioningControlPanel.Services.AIService.IAiService, ActingAi>();
        var acting = (ActingAi)(object)ai;
        acting.ReplyCommands.Add(Flash(5));
        GetBackToMeCommand.AiProvider = () => ai;
        var service = new AiCommandService();
        ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.CommandExecutor = commands =>
        {
            service.BeginBatch();
            foreach (var c in commands) service.ExecuteCommand(c);
        };
        return acting;
    }

    [Fact]
    public async Task PanicDuringTheFollowUpRoundTripDropsTheReplysOwnCommands()
    {
        var acting = SeedActingAi();
        Run(FollowUp());
        await acting.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        AiCommandService.CancelAll();             // panic while the AI is answering
        acting.Release.SetResult();               // the late reply executes its commands inline
        Assert.DoesNotContain(_flashes, f => f.Amount == 5);
    }

    [Fact]
    public async Task AFollowUpThatIsNotCancelledStillRunsItsReplysCommands()
    {
        var acting = SeedActingAi();
        Run(FollowUp());
        await acting.Asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        acting.Release.SetResult();
        Assert.Contains(_flashes, f => f.Amount == 5);
    }

    [Fact]
    public void AChatReplysCommandsRunEvenAfterAnEarlierPanic()
    {
        SeedActingAi();
        AiCommandService.CancelAll();             // an earlier panic does not poison later chat replies
        ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.CommandExecutor!(new List<AiCommandData> { Flash(5) });
        Assert.Contains(_flashes, f => f.Amount == 5);
    }
}
