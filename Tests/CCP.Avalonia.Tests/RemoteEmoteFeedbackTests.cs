using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.SendEmoteAndReportAsync step 3.6 + AvatarTubeWindow.ShowEmoteFeedback: whichever
/// surface sends an emote (the tube menu passes no status line), the avatar says "Sending..." then "Sent".</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class RemoteEmoteFeedbackTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public HttpStatusCode Emote = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var status = path == "/v2/remote/emote" ? Emote : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(path == "/v2/remote/start" ? "{\"code\":\"ABC123\"}" : "{}") });
        }
    }

    [Fact]
    public void The_bubble_reads_sending_then_sent_with_the_text_cut_at_forty()
    {
        Assert.Equal("Sending...", AvatarTubeWindow.EmoteFeedbackText("hi", true));
        Assert.Equal("Sent: \"more please\"", AvatarTubeWindow.EmoteFeedbackText("  more please ", false));
        Assert.Equal("Sent: \"" + new string('a', 40) + "...\"", AvatarTubeWindow.EmoteFeedbackText(new string('a', 55), false));
    }

    [Fact]
    public Task A_menu_emote_tells_the_avatar_pending_then_sent_and_never_for_a_send_that_bounces() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (oldRelay, oldHook) = (RemoteControlTabView.Relay, RemoteControlTabView.EmoteFeedback);
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        relay.Now = () => now;
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var said = new List<(string Text, bool Pending)>();
        RemoteControlTabView.EmoteFeedback = (t, p) => said.Add((t, p));
        try
        {
            // No session: the send bounces at once, so no "Sending..." that would never resolve.
            Assert.False(await RemoteControlTabView.SendEmoteAndReportAsync("good", "", "preset", null));
            Assert.Empty(said);

            await relay.StartAsync("light");
            Assert.True(await RemoteControlTabView.SendEmoteAndReportAsync("good", "", "preset", null));
            Assert.Equal(new[] { ("good", true), ("good", false) }, said);

            // Inside the debounce window: silent.
            Assert.False(await RemoteControlTabView.SendEmoteAndReportAsync("again", "", "preset", null));
            Assert.Equal(2, said.Count);

            // The server refuses: pending was shown, "Sent" never is.
            now = now.AddSeconds(2);
            f.Emote = HttpStatusCode.InternalServerError;
            Assert.False(await RemoteControlTabView.SendEmoteAndReportAsync("more", "", "preset", null));
            Assert.Equal(("more", true), said[^1]);
            Assert.Equal(3, said.Count);
        }
        finally
        {
            relay.Dispose();
            (RemoteControlTabView.Relay, RemoteControlTabView.EmoteFeedback) = (oldRelay, oldHook);
        }
    });
}
