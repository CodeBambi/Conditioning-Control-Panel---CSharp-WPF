using System;
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
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Asks;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Tube parity for a brain-routed send (WPF ChatInput.cs:747 SendChatMessageAsync): the thinking bubble
/// animates while the (loopback fake, protocol 2) model is held, the reply stops it and double-bounces
/// the avatar, and an activity request is followed by an ask card whose chip, clicked, performs it.
/// </summary>
public sealed class TubeChatParityTests
{
    [Fact]
    public Task ThinkingBounceAndAskCardFollowABrainReply() => AvaloniaTestDispatcher.RunAsync(async () =>
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
        CoreSettings.Current.AvatarEnabled = true;
        CoreSettings.Current.CompanionAsksEnabled = true;

        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var baseUrl = $"http://127.0.0.1:{((IPEndPoint)probe.LocalEndpoint).Port}";
        probe.Stop();
        var gate = new TaskCompletionSource();
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
                await gate.Task;   // hold the reply so the thinking bubble can be observed
                var id = System.Text.RegularExpressions.Regex.Match(body, "\"request_id\":\"([^\"]+)\"").Groups[1].Value;
                var json = Encoding.UTF8.GetBytes(
                    "{\"companion_protocol\":2,\"finish_reason\":\"stop\",\"request_id\":\"" + id + "\",\"content\":\"hi sweetie\"}");
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(json);
                ctx.Response.Close();
            }
        });

        var dir = Directory.CreateTempSubdirectory("ccp-tube-").FullName;
        var (previousAi, previousBrain) = (AvApp.Ai, AvApp.Brain);
        AvApp.Ai = new AiService(baseUrl);
        AvApp.Brain = new CompanionBrain(AvApp.Ai, memory: new MemoryStore(Path.Combine(dir, "memory.json")),
            store: new CompanionSessionStore(Path.Combine(dir, "session.json"), Path.Combine(dir, "legacy.json")));
        CoreAccount.UnifiedUserId = "u-tube";
        var tube = new AvatarTubeWindow(null);
        var started = new List<string>();
        try
        {
            AvApp.SeedCompanionTubeSeams();
            Assert.True(ConversationDelivery.AskCardsShown?.Invoke());   // the model is told a card follows
            Assert.NotNull(PromptAssembler.NoticeSurface?.Invoke());       // the oversize toast has a surface
            CompanionAskService.RequestDelay = TimeSpan.Zero;
            CompanionAskService.ShowCardSurface = c => Dispatcher.UIThread.Post(() => tube.ShowAskCard(c));
            CompanionAskService.SessionOptions = () => new[] { new AskOption("s-one", "Session one", () => true) };
            CompanionAskService.StartSession = id => { started.Add(id); return true; };

            tube.Show();
            Dispatcher.UIThread.RunJobs();
            var speech = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.FindControl<TextBox>("TxtUserInput")!.Text = "what can I do";
            var send = tube.SendChatAsync();

            // In flight: an unbadged thinking phrase that grows dots, and barks are held.
            async Task Pump(int ms) { for (var t = 0; t < ms; t += 20) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); } }
            await Pump(100);
            var first = speech.Text;
            Assert.False(string.IsNullOrEmpty(first));
            Assert.True(tube.IsCompanionBusy(0));
            Assert.False(tube.FindControl<Border>("AiBadge")!.IsVisible);
            await Pump(600);
            Assert.NotEqual(first, speech.Text);

            gate.SetResult();
            var host = tube.FindControl<Grid>("AvatarBounceHost")!;
            double minY = 0;
            for (var t = 0; t < 2000 && !send.IsCompleted; t += 5) { await Task.Delay(5); Dispatcher.UIThread.RunJobs(); }
            for (var t = 0; t < 400; t += 5)
            {
                await Task.Delay(5);
                Dispatcher.UIThread.RunJobs();
                if (host.RenderTransform is TranslateTransform tt) minY = Math.Min(minY, tt.Y);
            }
            Assert.True(minY < -5, $"no double bounce (min Y {minY})");
            Assert.Null(host.RenderTransform);   // released afterwards, as WPF's FillBehavior.Stop

            // The ask card follows: the question takes the bubble with its chips under it (Game has
            // no games here, so WPF's fallback to a Session card).
            var buttons = tube.FindControl<WrapPanel>("AskButtons")!;
            for (var t = 0; t < 2000 && !buttons.IsVisible; t += 20) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            Assert.True(buttons.IsVisible);
            Assert.Equal(2, buttons.Children.Count);
            var question = speech.Text;
            Assert.NotEqual("hi sweetie", question);
            await Pump(600);
            Assert.Equal(question, speech.Text);   // the thinking ticker is dead

            var chip = (Border)buttons.Children[0];
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var at = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), tube)!.Value;
            tube.MouseDown(at, MouseButton.Left);
            tube.MouseUp(at, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "s-one" }, started);
        }
        finally
        {
            gate.TrySetResult();
            tube.Close();
            Dispatcher.UIThread.RunJobs();
            listener.Stop();
            CompanionAskService.RequestDelay = TimeSpan.FromSeconds(6);
            CompanionAskService.ShowCardSurface = null;
            CompanionAskService.SessionOptions = null;
            CompanionAskService.StartSession = null;
            AvApp.Brain?.Dispose();
            AvApp.Ai?.Dispose();
            (AvApp.Ai, AvApp.Brain) = (previousAi, previousBrain);
            CoreAccount.UnifiedUserId = null;
            CoreSettings.ServiceProvider = null;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public void ThinkingPhrasesStripTheirOwnDots()
    {
        Assert.Equal("Poppin bubbles", ThinkingPhrases.StripTrailingDots("*Poppin bubbles...*"));
        Assert.Equal("(thinking)", ThinkingPhrases.StripTrailingDots("(thinking...)"));
        Assert.Equal("*~*", ThinkingPhrases.StripTrailingDots("*~*"));
        Assert.Equal("x..", ThinkingPhrases.Frame("x", 2));
    }
}
