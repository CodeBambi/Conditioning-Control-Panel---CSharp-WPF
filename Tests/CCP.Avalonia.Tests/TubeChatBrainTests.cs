using System.Collections.Generic;
using System.IO;
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
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Tube chat routes through the Core CompanionBrain while UseCompanionBrain is on (WPF
/// ChatInput.cs:772): the second send carries the first turn to the (loopback fake) model and the turn
/// log lands in the brain's session file. Off, it takes the stateless call and the brain sees nothing.</summary>
public sealed class TubeChatBrainTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task SendRoutesThroughTheBrainOnlyWhileTheKillSwitchIsOn(bool useBrain) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AiChatEnabled = true;
        CoreSettings.Current.OfflineMode = false;
        CoreSettings.Current.UseCompanionBrain = useBrain;

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var baseUrl = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
        probe.Stop();
        var requests = new List<string>();
        using var listener = new HttpListener();
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
                lock (requests) requests.Add(body);
                // The brain speaks companion protocol 2, which requires the request id echoed back.
                var id = System.Text.RegularExpressions.Regex.Match(body, "\"request_id\":\"([^\"]+)\"").Groups[1].Value;
                var json = Encoding.UTF8.GetBytes(id.Length == 0 ? "{\"content\":\"hi sweetie\"}"
                    : "{\"companion_protocol\":2,\"finish_reason\":\"stop\",\"request_id\":\"" + id + "\",\"content\":\"hi sweetie\"}");
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(json);
                ctx.Response.Close();
            }
        });

        var dir = Directory.CreateTempSubdirectory("ccp-brain-").FullName;
        var sessionPath = Path.Combine(dir, "session.json");
        var (previousAi, previousBrain) = (AvApp.Ai, AvApp.Brain);
        AvApp.Ai = new AiService(baseUrl);
        AvApp.Brain = new CompanionBrain(AvApp.Ai, memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(sessionPath, Path.Combine(dir, "legacy.json")));
        CoreAccount.UnifiedUserId = "u-brain";
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var input = tube.FindControl<TextBox>("TxtUserInput")!;
            input.Text = "remember the purple ribbon";
            await tube.SendChatAsync();
            input.Text = "what did I say";
            await tube.SendChatAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, requests.Count);
            Assert.Equal(useBrain, requests[1].Contains("purple ribbon"));   // the brain carries the thread
            Assert.Equal(useBrain ? 4 : 0, AvApp.Brain.Session.Turns.Count);
            AvApp.Brain.Flush();
            Assert.Equal(useBrain, File.Exists(sessionPath) && File.ReadAllText(sessionPath).Contains("purple ribbon"));
        }
        finally
        {
            tube.Close();
            Dispatcher.UIThread.RunJobs();
            listener.Stop();
            AvApp.Brain?.Dispose();
            AvApp.Ai?.Dispose();
            (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain);
            CoreAccount.UnifiedUserId = null;
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
            Directory.Delete(dir, true);
        }
    });
}
