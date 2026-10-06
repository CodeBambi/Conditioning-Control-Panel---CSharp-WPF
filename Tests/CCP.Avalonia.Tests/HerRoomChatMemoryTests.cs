using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Her Room Z2/Z3 over the Core brain (WPF ChatThresholdRuntimeVm / MemoryDiaryRuntimeVm):
/// the chat zone sends through brain.ChatAsync to a loopback fake model and shows the thread; the
/// diary's forget / forget-everything and the chat-memory switch really delete from disk.</summary>
public sealed class HerRoomChatMemoryTests
{
    private static (CompanionBrain brain, string dir) Setup(string baseUrl)
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AiChatEnabled = true;
        CoreSettings.Current.OfflineMode = false;
        CoreSettings.Current.UseCompanionBrain = true;
        CoreSettings.Current.CompanionPrompt.AiProvider = AiProviderType.Cloud;
        CoreSettings.Current.CompanionPrompt.ChatMemoryEnabled = true;
        CoreAccount.UnifiedUserId = "u-room";
        var dir = Directory.CreateTempSubdirectory("ccp-room-").FullName;
        AvApp.Ai = new AiService(baseUrl);
        AvApp.Brain = new CompanionBrain(AvApp.Ai, memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        return (AvApp.Brain, dir);
    }

    private static string StartFakeModel(out HttpListener listener)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var baseUrl = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
        probe.Stop();
        var l = listener = new HttpListener();
        l.Prefixes.Add(baseUrl + "/");
        l.Start();
        _ = Task.Run(async () =>
        {
            while (l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await l.GetContextAsync(); } catch { return; }
                string body;
                using (var r = new StreamReader(ctx.Request.InputStream)) body = r.ReadToEnd();
                var id = System.Text.RegularExpressions.Regex.Match(body, "\"request_id\":\"([^\"]+)\"").Groups[1].Value;
                var json = Encoding.UTF8.GetBytes(id.Length == 0 ? "{\"content\":\"hi sweetie\"}"
                    : "{\"companion_protocol\":2,\"finish_reason\":\"stop\",\"request_id\":\"" + id + "\",\"content\":\"hi sweetie\"}");
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(json);
                ctx.Response.Close();
            }
        });
        return baseUrl;
    }

    [Fact]
    public Task ChatZoneSendsThroughTheBrainAndShowsTheThread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var baseUrl = StartFakeModel(out var listener);
        using var _ = listener;
        var (previousAi, previousBrain) = (AvApp.Ai, AvApp.Brain);
        try
        {
            var (brain, dir) = Setup(baseUrl);
            var vm = ChatThresholdViewModel.CreateLive();
            Assert.Equal(CompanionZoneState.Live, vm.State);
            Assert.Empty(vm.Turns);

            vm.Draft = "hello you";
            vm.SendCommand.Execute(null);
            Assert.True(vm.IsThinking);
            await vm.PendingSend!;
            for (int i = 0; i < 50 && vm.IsThinking; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(20); }

            Assert.False(vm.IsThinking);
            Assert.Equal(new[] { "hello you", "hi sweetie" }, vm.Turns.Select(t => t.Text));
            Assert.True(vm.Turns[0].IsYou);
            Assert.True(vm.Turns[1].IsHer && vm.Turns[1].IsAiGenerated);
            Assert.NotEqual(string.Empty, vm.LastHeardCopy);

            // Before the wipe the line really is on disk, so "gone" below proves a deletion.
            var session = Path.Combine(dir, "session.json");
            brain.Flush();
            Assert.Contains("hello you", File.ReadAllText(session));

            // A replaced viewmodel stops listening to the brain's turn log (WPF Detach).
            Assert.True(vm.IsAttached);
            var view = new ChatThresholdView { ViewModel = vm };
            view.ViewModel = ChatThresholdViewModel.CreateLive();
            Assert.False(vm.IsAttached);
            Assert.True(view.ViewModel!.IsAttached);

            // The Engine Room memory switch: OFF erases the saved conversation, as WPF does.
            var grid = new AiPermissionsGrid();
            grid.SyncFromSettings();
            grid.FindControl<CheckBox>("ChkChatMemoryEnabled")!.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(CoreSettings.Current.CompanionPrompt.ChatMemoryEnabled);
            Assert.Empty(brain.Session.Turns);
            Assert.True(!File.Exists(session) || !File.ReadAllText(session).Contains("hello you"));
        }
        finally { (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain); }
    });

    [Fact]
    public Task DiaryForgetAndForgetEverythingDeleteFromMemoryJson() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (previousAi, previousBrain) = (AvApp.Ai, AvApp.Brain);
        try
        {
            var (brain, dir) = Setup("http://127.0.0.1:9");
            var store = (MemoryStore)brain.Memory;
            store.AddFact("calls her cat Prime Minister Beans", MemoryFactKind.Joke);
            store.AddFact("wants level 50 before september", MemoryFactKind.Goal);
            store.SaveNow();
            var path = Path.Combine(dir, "memory.json");
            Assert.Contains("Prime Minister Beans", File.ReadAllText(path));

            var vm = MemoryDiaryViewModel.CreateLive();
            Assert.Equal(2, vm.Facts.Count(f => !f.IsDormant));
            Assert.False(vm.IsEmpty);

            var beans = vm.Facts.Single(f => f.Text.Contains("Beans"));
            beans.ForgetCommand.Execute(null);
            store.SaveNow();
            Assert.DoesNotContain("Prime Minister Beans", File.ReadAllText(path));
            Assert.Contains("level 50", File.ReadAllText(path));
            Assert.DoesNotContain(vm.Facts, f => f.Text.Contains("Beans"));

            // Forget everything also clears the tube's on-screen bubble log (WPF ChatHistory.Clear).
            var tube = new ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow(null);
            var previousLog = MemoryDiaryViewModel.TubeBubbleLog;
            MemoryDiaryViewModel.TubeBubbleLog = () => tube.ChatHistory;
            tube.ChatHistory.Add(new ConditioningControlPanel.Avalonia.Views.AvatarTube.ChatMessage { Text = "old bubble", IsUser = true });
            try { vm.ForgetEverythingCommand.Execute(null); }
            finally { MemoryDiaryViewModel.TubeBubbleLog = previousLog; }
            Assert.Empty(tube.ChatHistory);
            store = (MemoryStore)brain.Memory;
            store.SaveNow();
            Assert.DoesNotContain("level 50", File.ReadAllText(path));
            Assert.True(vm.IsEmpty);
            return Task.CompletedTask;
        }
        finally { (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain); }
    });
}
