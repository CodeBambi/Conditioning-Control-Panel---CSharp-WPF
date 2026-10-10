using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Remote tab "List me in the directory" (WPF MainWindow.RemoteControl.cs:513-735) and the tube's
/// emote rows (WPF AvatarTubeWindow.ChatInput.cs:883), against a fake relay. Swaps the process-wide relay,
/// so it runs alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class RemoteDirectoryTabTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<(string Path, JObject Body)> Seen = new();
        public string Poll = "{}";
        public HttpStatusCode OptIn = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            var body = JObject.Parse(await r.Content!.ReadAsStringAsync(ct));
            lock (Seen) Seen.Add((path, body));
            var (status, json) = path switch
            {
                "/v2/remote/start" => (HttpStatusCode.OK, "{\"code\":\"ABC123\"}"),
                "/v2/remote/poll" => (HttpStatusCode.OK, Poll),
                "/v2/directory/opt-in" => (OptIn, "{}"),
                _ => (HttpStatusCode.OK, "{}"),
            };
            return new HttpResponseMessage(status) { Content = new StringContent(json) };
        }
        public List<JObject> At(string path) { lock (Seen) return Seen.Where(s => s.Path == path).Select(s => s.Body).ToList(); }
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    private static void Click(CheckBox box)
    {
        box.IsChecked = !(box.IsChecked ?? false);
        box.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [Fact]
    public Task Opt_in_form_caps_tags_counts_the_status_lists_the_session_and_remembers_only_when_asked() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureAvalonia();
        var (oldRelay, oldProvider) = (RemoteControlTabView.Relay, CoreSettings.ServiceProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        Window? host = null;
        try
        {
            var s = CoreSettings.Current;
            (s.RememberDirectoryDetails, s.SavedDirectoryTags, s.SavedDirectoryStatusText) = (true, new List<string> { "trance" }, "saved line");
            var view = new RemoteControlTabView();
            host = new Window { Width = 1200, Height = 900, Content = view };
            host.Show();
            Dispatcher.UIThread.RunJobs();

            // Both blocks are on the page, as on WPF.
            Assert.True(view.FindControl<Border>("EmotePickerPanel")!.IsVisible);
            var section = view.FindControl<Border>("OptInSectionPanel")!;
            Assert.True(section.IsVisible);
            var form = view.FindControl<StackPanel>("OptInFormPanel")!;
            Assert.False(form.IsVisible);

            // Ticking opens the form with the remembered details.
            var optIn = view.FindControl<CheckBox>("ChkOptIntoDirectory")!;
            optIn.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(form.IsVisible);
            Assert.True(view.FindControl<CheckBox>("ChkTagTrance")!.IsChecked);
            Assert.False(view.FindControl<CheckBox>("ChkTagDrone")!.IsChecked);
            var status = view.FindControl<TextBox>("TxtOptInStatus")!;
            Assert.Equal("saved line", status.Text);
            Assert.Equal("10/80", view.FindControl<TextBlock>("TxtOptInStatusCount")!.Text);
            Assert.True(view.FindControl<CheckBox>("ChkRememberOptInDetails")!.IsChecked);
            status.Text = "hello";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("5/80", view.FindControl<TextBlock>("TxtOptInStatusCount")!.Text);

            // Five tags at most: the sixth tick is undone and says why.
            foreach (var name in new[] { "ChkTagBimbo", "ChkTagDrone", "ChkTagFeminization", "ChkTagSubmission" })
                Click(view.FindControl<CheckBox>(name)!);
            var feedback = view.FindControl<TextBlock>("TxtOptInFeedback")!;
            Assert.False(feedback.IsVisible);
            var sixth = view.FindControl<CheckBox>("ChkTagChastity")!;
            Click(sixth);
            Assert.False(sixth.IsChecked);
            Assert.True(feedback.IsVisible);
            Assert.Equal(Loc.Get("msg_optin_directory_max_tags"), feedback.Text);

            // No session: nothing is published, nobody is told they are listed.
            var listed = view.FindControl<Border>("ListedConfirmationPanel")!;
            await view.RunOptInChainAsync();
            Assert.Empty(f.At("/v2/directory/opt-in"));
            Assert.False(listed.IsVisible);
            Assert.Equal(Loc.Get("msg_optin_directory_failed"), feedback.Text);

            // A refusal (429 here) is the same one inline line; the session is untouched.
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            f.OptIn = HttpStatusCode.TooManyRequests;
            await view.RunOptInChainAsync();
            Assert.False(listed.IsVisible);
            Assert.True(relay.IsActive);
            Assert.Equal(Loc.Get("msg_optin_directory_failed"), feedback.Text);

            // Listed: the banner shows, and with Remember off the saved details go.
            f.OptIn = HttpStatusCode.OK;
            view.FindControl<CheckBox>("ChkRememberOptInDetails")!.IsChecked = false;
            await view.RunOptInChainAsync();
            Assert.True(listed.IsVisible);
            var body = f.At("/v2/directory/opt-in").Last();
            Assert.Equal(new[] { "bimbo", "drone", "trance", "feminization", "submission" }, body["tags"]!.Select(t => (string?)t).ToArray());
            Assert.Equal("hello", (string?)body["status_text"]);
            Assert.False(s.RememberDirectoryDetails);
            Assert.Empty(s.SavedDirectoryTags);
            Assert.Equal("", s.SavedDirectoryStatusText);

            // Remember on: the tags and the line are kept for next time.
            view.FindControl<CheckBox>("ChkRememberOptInDetails")!.IsChecked = true;
            await view.RunOptInChainAsync();
            Assert.True(s.RememberDirectoryDetails);
            Assert.Equal(5, s.SavedDirectoryTags.Count);
            Assert.Equal("hello", s.SavedDirectoryStatusText);

            // The session ends: the banner goes, the tick is cleared (re-opt every session).
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(listed.IsVisible);
            Assert.False(optIn.IsChecked);
            Assert.True(section.IsEnabled);
        }
        finally
        {
            host?.Close();
            relay.Dispose();
            (RemoteControlTabView.Relay, CoreSettings.ServiceProvider) = (oldRelay, oldProvider);
        }
    });

    [Fact]
    public Task Tube_menu_swaps_the_locked_rows_for_the_emote_presets_while_a_controller_is_connected() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureAvalonia();
        var (oldRelay, oldProvider) = (RemoteControlTabView.Relay, CoreSettings.ServiceProvider);
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        _ = RemoteControlTabView.Relay.Value;
        AvatarTubeWindow? tube = null;
        try
        {
            tube = new AvatarTubeWindow(null);
            var originals = new[] { "MenuItemEngine", "MenuItemTriggerMode", "MenuItemBambiTakeover", "MenuItemPersonality", "MenuItemMute" };
            MenuItem Item(string name) => tube.FindControl<MenuItem>(name)!;

            tube.UpdateQuickMenuState();
            Assert.All(originals, n => Assert.True(Item(n).IsVisible, n));
            Assert.All(AvatarTubeWindow.EmoteMenuItemNames, n => Assert.False(Item(n).IsVisible, n));

            await relay.StartAsync("light");
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            tube.UpdateQuickMenuState();
            Assert.All(originals, n => Assert.False(Item(n).IsVisible, n));
            var presets = CoreSettings.Current.RemoteEmotePresets;
            for (var i = 0; i < 5; i++)
            {
                var item = Item(AvatarTubeWindow.EmoteMenuItemNames[i]);
                Assert.True(item.IsVisible);
                Assert.NotNull(item.Foreground);
                Assert.Same(presets[i], item.Tag);
                Assert.EndsWith(presets[i].Text ?? "", item.Header as string);
            }
            // What stays is still locked for the engine-side rows.
            Assert.False(Item("MenuItemMuteWhispers").IsEnabled);

            // A click sends that preset, as a preset.
            Item("MenuItemEmote1").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            for (var i = 0; i < 100 && f.At("/v2/remote/emote").Count == 0; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
            var sent = Assert.Single(f.At("/v2/remote/emote"));
            Assert.Equal(presets[0].Text, (string?)sent["text"]);
            Assert.Equal("preset", (string?)sent["kind"]);

            // The controller leaves: the menu is the subject's again.
            f.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync();
            tube.UpdateQuickMenuState();
            Assert.All(originals, n => Assert.True(Item(n).IsVisible, n));
            Assert.All(AvatarTubeWindow.EmoteMenuItemNames, n => Assert.False(Item(n).IsVisible, n));
        }
        finally
        {
            tube?.Close();
            relay.Dispose();
            (RemoteControlTabView.Relay, CoreSettings.ServiceProvider) = (oldRelay, oldProvider);
        }
    });
}
