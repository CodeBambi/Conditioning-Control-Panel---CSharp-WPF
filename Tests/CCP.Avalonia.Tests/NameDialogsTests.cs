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
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Trainer Card's rename and delete buttons (WPF MainWindow.Browser.cs:1677-1785) through the real
/// DisplayNameDialog and the seeded CoreAccount transport, over a fake handler (never the real server).
/// </summary>
public sealed partial class AccountSeedTests
{
    private sealed class NameWire : HttpMessageHandler
    {
        public readonly List<(string Path, JObject? Body, string? Token)> Seen = new();
        public Func<string, (HttpStatusCode, string)> Reply = _ => (HttpStatusCode.OK, "{}");
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : JObject.Parse(await r.Content.ReadAsStringAsync(ct));
            var path = $"{r.Method} {r.RequestUri!.AbsolutePath}";
            Seen.Add((path, body, r.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null));
            var (status, reply) = Reply(path);
            return new HttpResponseMessage(status) { Content = new StringContent(reply) };
        }
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }
        Assert.True(done());
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T One<T>(Window owner) where T : Window => owner.OwnedWindows.OfType<T>().Single();

    private static void SetupHeadless()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public Task TrainerCard_Rename_ShowsOnOwnCardOnly_SendsTheName_AdoptsTheServersName_ShowsItsRefusal() =>
        AvaloniaTestDispatcher.RunAsync(() => Fresh(async () =>
        {
            SetupHeadless();
            var s = CoreSettings.Current;
            var wire = new NameWire();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            (s.UnifiedId, s.AuthToken, s.UserDisplayName, s.OfflineMode) = ("u1", "tok", "Old", true);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                var tab = shell.ProfilePage!;
                var rename = tab.FindControl<Button>("BtnChangeDisplayName")!;
                tab.DisplayOwnProfile();
                Assert.True(rename.IsVisible);
                Assert.True(tab.FindControl<Button>("BtnDeleteProfile")!.IsVisible);

                // Someone else's card: no rename. No unified id: no rename either (WPF Browser.cs:1936/2217).
                tab.DisplayProfileEntry(new LeaderboardRow { DisplayName = "Nyx" });
                Assert.False(rename.IsVisible);
                s.UnifiedId = null;
                tab.DisplayOwnProfile();
                Assert.False(rename.IsVisible);
                s.UnifiedId = "u1";
                tab.DisplayOwnProfile();

                wire.Reply = _ => (HttpStatusCode.OK, "{\"success\":true,\"new_display_name\":\"BAMBI\"}");
                Click(rename);
                Dispatcher.UIThread.RunJobs();
                var dlg = One<DisplayNameDialog>(shell);
                var box = dlg.FindControl<TextBox>("TxtDisplayName")!;
                Assert.Equal("Old", box.Text);
                box.Text = "Bambi";
                Click(dlg.FindControl<Button>("BtnConfirm")!);
                await Until(() => s.UserDisplayName == "BAMBI");
                var (path, body, token) = wire.Seen.Single();
                Assert.Equal("POST /v2/user/change-display-name", path);
                Assert.Equal("u1", (string?)body!["unified_id"]);
                Assert.Equal("Bambi", (string?)body["new_display_name"]);
                Assert.Equal("tok", token);
                Assert.Equal("BAMBI", tab.FindControl<TextBlock>("TxtProfileViewerName")!.Text);
                Assert.True(rename.IsEnabled);

                // The server refuses: its text is shown and nothing local changes.
                wire.Reply = _ => (HttpStatusCode.Conflict, "{\"error\":\"Name taken\"}");
                Click(rename);
                Dispatcher.UIThread.RunJobs();
                dlg = One<DisplayNameDialog>(shell);
                dlg.FindControl<TextBox>("TxtDisplayName")!.Text = "Nyx";
                Click(dlg.FindControl<Button>("BtnConfirm")!);
                await Until(() => shell.OwnedWindows.OfType<MessageDialog>().Any());
                Assert.Contains("Name taken", Texts(One<MessageDialog>(shell)));
                Assert.Equal("BAMBI", s.UserDisplayName);
                One<MessageDialog>(shell).Close();

                // Cancel sends nothing.
                Click(rename);
                Dispatcher.UIThread.RunJobs();
                Click(One<DisplayNameDialog>(shell).FindControl<Button>("BtnCancel")!);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, wire.Seen.Count);
            }
            finally
            {
                foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
                shell.Close();
            }
        }));

    [Fact]
    public Task TrainerCard_Delete_NeedsDELETE_DeletesServerSide_SignsOutWithoutAPush() =>
        AvaloniaTestDispatcher.RunAsync(() => Fresh(async () =>
        {
            SetupHeadless();
            var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
            Assert.True(await AccountSeed.LoadProfileAsync());
            sync.UtcNow = () => T0.AddMinutes(5);   // past the cooldown: a plain logout WOULD push now
            var pushes = wire.Syncs.Count();
            var s = CoreSettings.Current;
            s.UserDisplayName = "Old";
            s.OfflineMode = true;   // no startup polls while the shell opens
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                var tab = shell.ProfilePage!;
                tab.DisplayOwnProfile();
                s.OfflineMode = false;  // signed in for real again (offline is not signed in to SyncPush)
                Click(tab.FindControl<Button>("BtnDeleteProfile")!);
                Dispatcher.UIThread.RunJobs();
                var dlg = One<DisplayNameDialog>(shell);
                var confirm = dlg.FindControl<Button>("BtnConfirm")!;
                var box = dlg.FindControl<TextBox>("TxtDisplayName")!;
                box.Text = "delete";
                Dispatcher.UIThread.RunJobs();
                Assert.False(confirm.IsEnabled);
                box.Text = "DELETE";
                Dispatcher.UIThread.RunJobs();
                Assert.True(confirm.IsEnabled);
                Click(confirm);
                await Until(() => shell.OwnedWindows.OfType<MessageDialog>().Any());

                Assert.Contains(wire.Seen, r => r.Path == "POST /v2/user/delete-account" && (string?)r.Body!["unified_id"] == "u1"
                                               && (string?)r.Body["confirmation"] == "DELETE");
                Assert.Contains(Loc.Get("msg_profile_deleted"), Texts(One<MessageDialog>(shell)));
                Assert.Null(s.UnifiedId);
                Assert.Null(s.AuthToken);
                Assert.Null(s.UserDisplayName);
                Assert.Equal(pushes, wire.Syncs.Count());   // the deleted account is never pushed back
                Assert.False(tab.FindControl<Grid>("ProfileCardWrapper")!.IsVisible);
            }
            finally
            {
                foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
                shell.Close();
            }
        }));

    private static IEnumerable<string> Texts(Window w) =>
        w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");
}
