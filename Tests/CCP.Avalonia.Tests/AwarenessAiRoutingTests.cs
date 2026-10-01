using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>shell-awareness -> AI (WPF AvatarTubeWindow.Reactions.cs:93-150 / 214-235, legacy pipeline):
/// against a loopback fake AI. Only with Awareness v2 off (the consent that says titles are sent),
/// awareness consented and chat on does an observed window reach the AI; a denied or incognito title
/// never does, nor does anything with v2 on or the consent revoked.</summary>
public sealed class AwarenessAiRoutingTests
{
    private static async Task WithTube(bool v2, bool consent, Func<AvatarTubeWindow, List<string>, Task> body)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.AiChatEnabled = true;
        s.OfflineMode = false;
        s.UseAwarenessV2 = v2;
        s.AwarenessModeEnabled = true;
        s.AwarenessConsentGiven = consent;
        s.AwarenessDenySeeded = false;
        s.AwarenessDenyList = new List<string> { "hades" };
        var oldEntitlement = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        CoreEntitlement.HasPremiumProvider = () => true;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        AwarenessPause.Resume();
        ResetCooldowns();

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
                using (var r = new StreamReader(ctx.Request.InputStream)) lock (requests) requests.Add(r.ReadToEnd());
                var json = Encoding.UTF8.GetBytes("{\"content\":\"caught you gaming\"}");
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(json);
                ctx.Response.Close();
            }
        });

        var previousAi = global::ConditioningControlPanel.Avalonia.App.Ai;
        global::ConditioningControlPanel.Avalonia.App.Ai = new AiService(baseUrl);
        CoreAccount.UnifiedUserId = "u-aware";
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            tube._startupTime = DateTime.MinValue;
            tube.FindControl<Border>("SpeechBubble")!.IsVisible = false;
            await body(tube, requests);
        }
        finally
        {
            tube.Close();
            Dispatcher.UIThread.RunJobs();
            global::ConditioningControlPanel.Avalonia.App.Ai = previousAi;
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = oldEntitlement;
            ResetCooldowns();
            service.SaveImmediate(); CoreSettings.ServiceProvider = null;
        }
    }

    /// <summary>App.WindowAwareness is one static observer shared by every tube test.</summary>
    private static void ResetCooldowns()
    {
        foreach (var f in new[] { "_lastReactionTime", "_lastStillOnTime" })
            typeof(WindowAwarenessService).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(global::ConditioningControlPanel.Avalonia.App.WindowAwareness, DateTime.MinValue);
    }

    private static ActivityChangedEventArgs Gaming() =>
        new(ActivityCategory.Gaming, ActivityCategory.Unknown, "Steam", "Steam", "Steam Library");

    [Fact]
    public Task ADeniedOrIncognitoTitleNeverReachesTheAiButAnAllowedOneDoes() => AvaloniaTestDispatcher.RunAsync(() =>
        WithTube(v2: false, consent: true, async (tube, requests) =>
        {
            string title = "";
            // The head's observer shape (App.axaml.cs): a title source plus the privacy filter.
            using var svc = new WindowAwarenessService(() => title, WindowAwarenessService.PassesPrivacyRules);
            var pending = new List<Task>();
            svc.ActivityChanged += (_, e) => pending.Add(tube.ReactToActivityAsync(e));

            foreach (var denied in new[] { "Hades", "YouTube - Google Chrome (Incognito)", "My Vault | Bitwarden - Mozilla Firefox" })
            {
                title = denied;
                svc.PollOnce();
            }
            await Task.WhenAll(pending);
            Assert.Empty(requests);

            title = "League of Legends";
            svc.PollOnce();
            await Task.WhenAll(pending);
            Dispatcher.UIThread.RunJobs();
            var request = Assert.Single(requests);
            Assert.Contains("League of Legends", request);
            Assert.Equal("caught you gaming", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        }));

    [Fact]
    public Task WithAwarenessV2OnNothingReachesTheAiEvenWithChatOn() => AvaloniaTestDispatcher.RunAsync(() =>
        WithTube(v2: true, consent: true, async (tube, requests) =>
        {
            await tube.ReactToActivityAsync(Gaming());
            tube.FindControl<Border>("SpeechBubble")!.IsVisible = false;
            await tube.ReactStillOnAsync(Gaming());
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(requests);
            Assert.False(string.IsNullOrWhiteSpace(tube.FindControl<TextBlock>("TxtSpeech")!.Text));   // the preset
        }));

    [Fact]
    public Task WithTheConsentRevokedNothingIsSent() => AvaloniaTestDispatcher.RunAsync(() =>
        WithTube(v2: false, consent: false, async (tube, requests) =>
        {
            await tube.ReactToActivityAsync(Gaming());
            tube.FindControl<Border>("SpeechBubble")!.IsVisible = false;
            await tube.ReactStillOnAsync(Gaming());
            Assert.Empty(requests);
        }));
}
