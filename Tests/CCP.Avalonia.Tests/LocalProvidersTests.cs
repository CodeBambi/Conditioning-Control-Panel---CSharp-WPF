using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Mode = ConditioningControlPanel.Views.Controls.Companion.CompanionProviderMode;

namespace CCP.Avalonia.Tests;

/// <summary>local-providers: Core AiServiceStrategy routes the brain to the user's own Ollama /
/// OpenAI-compatible server (a loopback fake here), and the Engine Room drawer chooses and tests it.</summary>
public sealed class LocalProvidersTests
{
    private sealed record Hit(string Path, string Body, string? Auth);

    /// <summary>A loopback fake that speaks both Ollama (/api/*) and OpenAI (/v1/chat/completions).</summary>
    private static (HttpListener Listener, string BaseUrl, List<Hit> Hits) StartFake()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var baseUrl = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
        probe.Stop();
        var hits = new List<Hit>();
        var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl + "/");
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                string body;
                using (var r = new StreamReader(ctx.Request.InputStream)) body = r.ReadToEnd();
                var path = ctx.Request.Url!.AbsolutePath;
                lock (hits) hits.Add(new Hit(path, body, ctx.Request.Headers["Authorization"]));
                var json = path.StartsWith("/api/chat")
                    ? "{\"model\":\"m\",\"message\":{\"role\":\"assistant\",\"content\":\"hi from ollama\"},\"done\":true,\"done_reason\":\"stop\"}"
                    : path.EndsWith("/chat/completions")
                        ? "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"hi from openai\"},\"finish_reason\":\"stop\"}]}"
                        : "{\"models\":[]}";
                var bytes = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
        return (listener, baseUrl, hits);
    }

    private static SettingsService FreshSettings()
    {
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AiChatEnabled = true;
        CoreSettings.Current.OfflineMode = false;
        CoreSettings.Current.UseCompanionBrain = true;
        return service;
    }

    [Theory]
    [InlineData(AiProviderType.Local)]
    [InlineData(AiProviderType.OpenAiCompatible)]
    public async Task TheBrainTalksToTheProviderTheUserConfigured(AiProviderType provider)
    {
        var service = FreshSettings();
        var (listener, baseUrl, hits) = StartFake();
        var p = CoreSettings.Current.CompanionPrompt;
        p.AiProvider = provider;
        p.AiOllamaHost = baseUrl + "/";
        p.AiModel = "fake-llama";
        p.OpenAiCompatibleEndpoint = baseUrl + "/v1";
        p.OpenAiCompatibleModel = "fake-gpt";
        // A stored DPAPI blob with no unprotect seam (this head): must never go out as a Bearer token.
        p.OpenAiCompatibleApiKey = "STORED-DPAPI-BLOB";
        var dir = Directory.CreateTempSubdirectory("ccp-local-").FullName;
        var strategy = new AiServiceStrategy();
        var brain = new CompanionBrain(strategy, memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        try
        {
            if (provider == AiProviderType.OpenAiCompatible)
            {
                // Unseeded unprotect (this head has no DPAPI): the stored blob is never sent, so nothing is.
                await brain.ChatAsync("first try");
                lock (hits) Assert.Empty(hits);
                OpenAiCompatibleService.ApiKeyUnprotect = blob => blob == "STORED-DPAPI-BLOB" ? "sk-plain" : null;
            }
            var reply = await brain.ChatAsync("hello there");

            Hit hit;
            lock (hits) hit = Assert.Single(hits);
            if (provider == AiProviderType.Local)
            {
                Assert.Equal("/api/chat", hit.Path);
                Assert.Contains("fake-llama", hit.Body);
                Assert.Equal("hi from ollama", reply.Text);
            }
            else
            {
                Assert.Equal("/v1/chat/completions", hit.Path);
                Assert.Contains("fake-gpt", hit.Body);
                Assert.Equal("Bearer sk-plain", hit.Auth);   // WPF's seam: the decrypted key, never the blob
                Assert.Equal("hi from openai", reply.Text);
            }
            Assert.Contains("hello there", hit.Body);
        }
        finally
        {
            OpenAiCompatibleService.ApiKeyUnprotect = null;
            listener.Stop();
            brain.Dispose();
            strategy.Dispose();
            CoreSettings.ServiceProvider = null;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public Task EngineRoomPicksTestsAndClearsTheLocalProvider() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = FreshSettings();
        var (listener, baseUrl, hits) = StartFake();
        try
        {
            var vm = new EngineRoomVm(new Border());
            vm.Provider = Mode.LocalOllama;
            Assert.Equal(AiProviderType.Local, CoreSettings.Current.CompanionPrompt.AiProvider);
            Assert.True(CoreSettings.Current.AiChatEnabled);

            vm.OllamaHost = baseUrl + "/";
            Assert.Equal(baseUrl + "/", CoreSettings.Current.CompanionPrompt.AiOllamaHost);
            await vm.TestConnectionAsync();
            Assert.True(vm.IsHealthy, vm.StatusLine);
            Assert.StartsWith(Loc.Get("label_status_connected"), vm.StatusLine);
            lock (hits) Assert.Contains(hits, h => h.Path == "/api/tags");

            vm.Provider = Mode.Off;
            Assert.False(CoreSettings.Current.AiChatEnabled);
            Assert.False(vm.IsHealthy);

            // The legacy local transcript (WPF AiServiceStrategy.ClearLocalHistory) really leaves disk.
            var history = Path.Combine(CorePaths.UserData, "local_chat_history.json");
            File.WriteAllText(history, "[{\"role\":\"user\",\"content\":\"old secret\"}]");
            using (var strategy = new AiServiceStrategy()) strategy.ClearLocalHistory();
            Assert.False(File.Exists(history));
        }
        finally
        {
            listener.Stop();
            CoreSettings.ServiceProvider = null;
        }
    });

    /// <summary>The Engine Room's "Clear conversation" (WPF MainWindow.CompanionRoom.cs:353): Cancel
    /// changes nothing; OK forgets the thread (session + its file) and keeps what she knows.</summary>
    [Fact]
    public Task ClearConversationForgetsTheThreadAndKeepsMemoryOnlyWhenConfirmed() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = FreshSettings();
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        var (listener, baseUrl, _) = StartFake();
        CoreSettings.Current.CompanionPrompt.AiProvider = AiProviderType.Local;
        CoreSettings.Current.CompanionPrompt.AiOllamaHost = baseUrl + "/";
        var dir = Directory.CreateTempSubdirectory("ccp-clear-").FullName;
        var sessionPath = Path.Combine(dir, "session.json");
        var (previousAi, previousBrain) = (AvApp.Ai, AvApp.Brain);
        var memory = new MemoryStore(Path.Combine(dir, "memory.json"));
        AvApp.Ai = new AiServiceStrategy();
        AvApp.Brain = new CompanionBrain(AvApp.Ai, memory: memory,
            store: new CompanionSessionStore(sessionPath, Path.Combine(dir, "legacy.json")));
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            shell.Show();
            await AvApp.Brain.ChatAsync("remember the purple ribbon");
            memory.AddFact("likes purple ribbons", MemoryFactKind.Preference);
            AvApp.Brain.Flush();
            Assert.Equal(2, AvApp.Brain.Session.Count);
            Assert.True(File.Exists(sessionPath));

            async Task Answer(string button)
            {
                var clear = shell.ClearCompanionConversationAsync();
                for (var i = 0; i < 250 && !shell.OwnedWindows.OfType<MessageDialog>().Any(); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20);
                }
                shell.OwnedWindows.OfType<MessageDialog>().Single().FindControl<Button>(button)!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await clear;
            }

            await Answer("BtnCancel");
            Assert.Equal(2, AvApp.Brain.Session.Count);
            Assert.True(File.Exists(sessionPath));

            await Answer("BtnOk");
            Assert.Equal(0, AvApp.Brain.Session.Count);
            Assert.False(File.Exists(sessionPath) && File.ReadAllText(sessionPath).Contains("purple ribbon"));
            Assert.Contains(memory.GetFacts(), f => f.Text == "likes purple ribbons");
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            listener.Stop();
            AvApp.Brain?.Dispose();
            AvApp.Ai?.Dispose();
            (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
            Directory.Delete(dir, true);
        }
    });
}
