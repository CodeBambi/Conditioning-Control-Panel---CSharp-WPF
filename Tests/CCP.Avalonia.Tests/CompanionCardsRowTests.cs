using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Moderation;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Her Room's attention gauge, constellation band and chat History link, driven from the
/// shell the way a user opens them (WPF AttentionGaugeRuntimeVm, RelationshipConstellationRuntimeVm,
/// CompanionTranscriptWindow).</summary>
public sealed class CompanionCardsRowTests
{
    [Fact]
    public Task RoomCardsReadLiveStateAndHistoryOpensTheTranscript() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var prompt = s.CompanionPrompt ??= new CompanionPromptSettings();
        var (oldProvider, oldLimit, oldAiOn) = (prompt.AiProvider, prompt.DailyRequestLimit, s.AiChatEnabled);
        var previousAi = AvApp.Ai;
        var ai = new BudgetAi { Remaining = 10 };
        prompt.AiProvider = AiProviderType.OpenAiCompatible;
        prompt.DailyRequestLimit = 10;
        AvApp.Ai = ai;
        Assert.False(CoreAccount.HasPremiumAccess);   // precondition for the upsell

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();

            // ---- Z6 attention: full budget ----
            var gauge = shell.GetLogicalDescendants().OfType<AttentionGaugeView>().Single().ViewModel!;
            Assert.Equal(Loc.Get("companion_attention_plenty"), gauge.StateCopy);
            Assert.False(gauge.ShowUpsell);
            Assert.False(gauge.ShowFloorNote);

            // spent down while the tab is hidden; re-read on return (WPF CompanionRoom.Sync on show)
            ai.Remaining = 1;
            shell.ShowTab("achievements");
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("companion_attention_whispering"), gauge.StateCopy);
            Assert.Equal(0.1, gauge.BarFraction, 3);
            Assert.True(gauge.ShowFloorNote);
            Assert.Equal(Loc.GetF("companion_attention_detail_fmt", 1), gauge.DetailLine);
            Assert.True(gauge.ShowUpsell);
            Assert.Same(shell, gauge.Shell);   // the upsell's ShowTab("patreon") target
            gauge.UpsellCommand.Execute(null);

            // local model picked in the Engine Room: unlimited, no meter, no upsell (WPF SyncBrain)
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var engine = (EngineRoomVm)shell.GetLogicalDescendants().OfType<EngineRoomDrawer>().Single().DataContext!;
            engine.Provider = global::ConditioningControlPanel.Views.Controls.Companion.CompanionProviderMode.LocalOllama;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(AiProviderType.Local, prompt.AiProvider);
            Assert.Equal(Loc.Get("companion_attention_detail_unlimited"), gauge.DetailLine);
            Assert.False(gauge.ShowUpsell);

            // ---- Z1 constellation: WPF's runtime state is dormant, and the sweep plays once ----
            var band = shell.GetLogicalDescendants().OfType<RelationshipConstellation>().Single();
            var cvm = band.ViewModel!;
            Assert.False(cvm.IsLive);
            Assert.All(cvm.Nodes, n => Assert.False(n.IsFilled || n.IsCurrent));
            Assert.Equal(Loc.Get("companion_stage_0"), cvm.Nodes[0].Name);
            Assert.Equal(Loc.Get("companion_constellation_flavor_new"), cvm.FlavorLine);
            Assert.True(band.ShimmerStarted);

            // ---- Z2 History: the stored transcript opens ----
            var chat = shell.GetLogicalDescendants().OfType<ChatThresholdView>().Single().ViewModel!;
            Assert.True(chat.HistoryCommand.CanExecute(null));
            chat.HistoryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var transcript = CompanionTranscriptWindow.LastShown;
            Assert.NotNull(transcript);
            Assert.True(transcript!.IsVisible);
            Assert.Equal(Loc.Get("companion_chat_history_title"), transcript.Title);
            transcript.Close();
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            AvApp.Ai = previousAi;
            prompt.AiProvider = oldProvider;
            prompt.DailyRequestLimit = oldLimit;
            s.AiChatEnabled = oldAiOn;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    /// <summary>Only the request mirror is read; nothing here talks to a model.</summary>
    private sealed class BudgetAi : IAiService
    {
        public int Remaining;
        public bool IsAvailable => false;
        public int DailyRequestsRemaining => Remaining;
        public Task<AiReplyResult> SendAsync(IReadOnlyList<ChatMessage> messages, AiCallOptions options,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetBambiReplyAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<AiReplyResult> GetBambiReplyExAsync(string userInput, bool isUserMessage = false) => throw new NotSupportedException();
        public Task<string?> GetAwarenessReactionAsync(string detectedName, string category, string serviceName = "",
            string pageTitle = "", TimeSpan? duration = null) => throw new NotSupportedException();
        public Task<string?> GetStillOnReactionAsync(string displayName, string category, TimeSpan duration) => throw new NotSupportedException();
        public Task<string?> GetKeywordCommentAsync(string keyword, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetLockScreenReaction(string sentance, int mistakes, int amount, string? promptTemplate = null) => throw new NotSupportedException();
        public Task<string?> GetVideoDoneReaction(string title, string? promptTemplate = null) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
