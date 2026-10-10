using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;
using ILeashTaskRunner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r11: the leashed side's card extras (WPF 7.1.5 LeashSelfCard): chain + "?",
/// "Watch it" on an open video task through the runner, the sticker shelf, the preview toggle.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps LeashLocator.Runner and the notify seam
public sealed class LeashSelfExtrasTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class Runner : ILeashTaskRunner
    {
        public bool Accept = true;
        public string? Started;
        public bool IsRunning => false;
        public string? RunningPid => null;
        public bool Start(Punishment punishment) => false;
        public void Cancel() { }
        public bool StartAssignmentWatch(Assignment assignment) { if (Accept) Started = assignment.Aid; return Accept; }
        public event Action<string, int, int>? Progress { add { } remove { } }
        public event Action<string>? Completed { add { } remove { } }
    }

    private static readonly LeashPerson Vex = new("h1", "Vex", null);

    private static MyLeash Me(Assignment? a = null, int stickers = 0) => new(
        Vex, LeashIntensity.Standard, DateTimeOffset.UtcNow.AddDays(-2), 3, null, default,
        Array.Empty<Punishment>(), a, 0,
        Enumerable.Range(0, stickers).Select(i => new Sticker(i % 2 == 0 ? "good" : "heart", Vex, DateTimeOffset.UtcNow)).ToList());

    private static T? Find<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);

    [Fact]
    public async Task SelfCard_WearsChainHelpShelfAndPreview_AsWpf()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            var card = new LeashSelfCard(Me(stickers: 3), () => null);
            Assert.NotNull(Find<Control>(card, "leash-chain"));
            Assert.NotNull(Find<Button>(card, "leash-help:leashed"));
            var shelf = Find<Control>(card, "leash-shelf");
            Assert.NotNull(shelf);
            Assert.Equal(3, shelf!.GetLogicalDescendants().OfType<Control>().Count(c => (c.Tag as string)?.StartsWith("leash-sticker:") == true));
            Assert.Null(Find<Control>(card, "leash-preview"));
            card.TogglePreview();
            Assert.True(card.PreviewOpen);
            var preview = Find<Control>(card, "leash-preview");
            Assert.NotNull(preview);
            Assert.False(preview!.IsHitTestVisible);
            // No shelf without stickers.
            Assert.Null(Find<Control>(new LeashSelfCard(Me(), () => null), "leash-shelf"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task WatchIt_OnlyForAnOpenVideoTask_AndStartsTheRunnerWatch()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            var runner = new Runner();
            var oldRunner = LeashLocator.Runner;
            var oldNotify = LeashSelfCard.Notify;
            string? told = null;
            LeashSelfCard.Notify = t => told = t;
            LeashLocator.Runner = () => runner;
            try
            {
                var video = new Assignment("a1", AssignKind.Video, 1, new LeashWatch("catalogue", "v1", "Clip"), "2026-10-09", AssignStatus.Open, DateTimeOffset.UtcNow);
                var card = new LeashSelfCard(Me(video), () => null);
                Assert.NotNull(Find<Button>(card, "leash-self-watch"));
                Assert.True(card.StartWatch(video));
                Assert.Equal("a1", runner.Started);
                Assert.Null(told);

                runner.Accept = false;
                Assert.False(card.StartWatch(video));
                Assert.NotNull(told);

                var minutes = video with { Kind = AssignKind.Minutes, Watch = null };
                Assert.Null(Find<Button>(new LeashSelfCard(Me(minutes), () => null), "leash-self-watch"));
                var done = video with { Status = AssignStatus.Done };
                Assert.Null(Find<Button>(new LeashSelfCard(Me(done), () => null), "leash-self-watch"));
            }
            finally
            {
                LeashLocator.Runner = oldRunner;
                LeashSelfCard.Notify = oldNotify;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HelpButton_OpensTheExplainerForItsRole()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashExplainRole? asked = null;
            void On(LeashExplainRole r) => asked = r;
            var oldPresenter = LeashExplainHost.Presenter;
            LeashExplainHost.Presenter = _ => { };
            LeashExplainHost.Requested += On;
            try
            {
                var help = LeashLook.Help(LeashExplainRole.Offer);
                help.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(LeashExplainRole.Offer, asked);
            }
            finally
            {
                LeashExplainHost.Requested -= On;
                LeashExplainHost.Presenter = oldPresenter;
            }
            return Task.CompletedTask;
        });
    }
}
