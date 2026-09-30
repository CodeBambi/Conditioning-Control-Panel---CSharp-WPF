using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
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
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Catalogue U4 on the shell, over a fake handler (never the real server): the HT lookup ->
/// toast -> picker -> download -> player chain, and the /mine share-status poll -> pill.</summary>
public sealed class CatalogueShellTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        public Func<HttpRequestMessage, string> Respond = _ => "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add(r.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(Respond(r), Encoding.UTF8, "application/json") });
        }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static IEnumerable<string> Texts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "");

    [Fact]
    public async Task HtLookupToastsPicksDownloadsIntoTheLibraryAndOpensThePlayer()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var lib = Directory.CreateTempSubdirectory("ccp-cat-lib-").FullName;
            var bundle = EnhancementSerializer.Save(new Enhancement());
            var fake = new Fake
            {
                Respond = r => r.RequestUri!.AbsolutePath == "/api/enhancements/by-ht-url"
                    ? "{\"enhancements\":[{\"id\":\"a\",\"title\":\"Alpha\",\"file_url\":\"http://127.0.0.1:9/a\"}," +
                      "{\"id\":\"b\",\"title\":\"Beta\",\"file_url\":\"http://127.0.0.1:9/b\"}]}"
                    : bundle,
            };
            var (oldLookup, oldClient) = (AppA.CatalogueLookup, AppA.Catalogue);
            AppA.CatalogueLookup = new CatalogueLookup(() => lib, "test", AppA.OnUiThread, fake);
            AppA.Catalogue = new CatalogueClient(() => null, () => null, "test", fake);   // startup poll never leaves the process
            var host = new StackPanel();
            AppA.Notifications.AttachHost(host);
            var shell = new MainShellWindow();   // registers the player opener
            shell.Show();
            try
            {
                await shell.RunCatalogueLookupAsync("https://hypnotube.com/video/123", default);
                Assert.StartsWith("https://app.cclabs.app/api/enhancements/by-ht-url?url=", fake.Seen.Single());
                Assert.Contains(string.Format(Loc.Get("catalogue_lookup_toast_many_fmt"), 2), Texts(host));

                // "Pick one" opens the picker over the shell.
                var action = host.GetVisualDescendants().OfType<Button>().Last(b => b.Name == "ToastAction");
                action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var picker = shell.OwnedWindows.OfType<CataloguePickerDialog>().Single();
                picker.Close(false);

                await shell.DownloadAndOpenCatalogueEntryAsync(new CatalogueEntry("a", "Alpha", "", "c", null,
                    new List<string>(), null, 0, "", null, "http://127.0.0.1:9/a"));
                Assert.Equal("http://127.0.0.1:9/a", fake.Seen.Last());
                Assert.True(File.Exists(Path.Combine(lib, "Alpha" + DeeperLocalLibrary.FileSuffix)));
                Assert.Single(shell.OwnedWindows.OfType<EnhancementPlayerWindow>());
                Assert.Contains(string.Format(Loc.Get("catalogue_lookup_toast_loaded_fmt"), "Alpha"), Texts(host));
            }
            finally
            {
                foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
                shell.Close();
                (AppA.CatalogueLookup, AppA.Catalogue) = (oldLookup, oldClient);
                Directory.Delete(lib, true);
            }
        });
    }

    /// <summary>The user path WPF has (MainWindow.Browser.cs:132-158): a site radio navigates the
    /// browser, and the lookup fires from the WebHost's NavigationCompleted with the LIVE url (here a
    /// video the user clicked into), not from the URL we asked for. Headless has no engine, so the
    /// test drives WebHost's completion seam.</summary>
    [Fact]
    public async Task SiteRadioNavigatesAndTheLiveUrlCompletionTriggersTheLookup()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var fake = new Fake { Respond = _ => "{\"enhancements\":[]}" };
            var (oldLookup, oldClient) = (AppA.CatalogueLookup, AppA.Catalogue);
            var oldOffline = CoreSettings.Current.OfflineMode;
            CoreSettings.Current.OfflineMode = false;
            AppA.CatalogueLookup = new CatalogueLookup(() => Path.GetTempPath(), "test", AppA.OnUiThread, fake);
            AppA.Catalogue = new CatalogueClient(() => null, () => null, "test", fake);
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                var tab = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView>("SettingsTab")!;
                var web = tab.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.WebHost>("BrowserWebHost")!;
                var hypno = tab.FindControl<RadioButton>("RbHypnoTube")!;
                hypno.IsChecked = true;
                hypno.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("https://hypnotube.com/", web.Source?.ToString());
                Assert.DoesNotContain(fake.Seen, u => u.Contains("by-ht-url"));

                var video = new Uri("https://hypnotube.com/video/sleepy-123.html");
                web.OnNavigationCompleted(video);
                Assert.Equal(video, web.CurrentUrl);
                Assert.Equal(Loc.Get("label_connected_2"), tab.FindControl<TextBlock>("TxtBrowserStatus")!.Text);
                for (var i = 0; i < 200 && !fake.Seen.Any(u => u.Contains("by-ht-url")); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }
                var lookup = fake.Seen.Single(u => u.Contains("by-ht-url"));
                Assert.Contains(Uri.EscapeDataString(video.AbsoluteUri), lookup);
            }
            finally
            {
                shell.Close();
                (AppA.CatalogueLookup, AppA.Catalogue) = (oldLookup, oldClient);
                CoreSettings.Current.OfflineMode = oldOffline;
            }
        });
    }

    [Fact]
    public async Task MinePollMarksThePresetApprovedToastsOnceAndPaintsItsPill()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var s = CoreSettings.Current;
            var preset = Preset.GetDefaultPresets()[0];
            var fake = new Fake
            {
                Respond = r => r.RequestUri!.AbsolutePath == "/api/auth/token-exchange"
                    ? $"{{\"access_token\":\"sb\",\"expires_at\":\"{DateTimeOffset.UtcNow.AddHours(1):O}\"}}"
                    : "{\"assets\":[{\"id\":\"c1\",\"status\":\"approved\"}]}",
            };
            var (oldClient, oldGet) = (AppA.Catalogue, CoreSecrets.RetrieveProvider);
            AppA.Catalogue = new CatalogueClient(() => "tok", () => "uid", "test", fake);
            s.CataloguePresetSubmissions[preset.Id] = new DeeperSubmissionRecord { CatalogueId = "c1", Status = "pending" };
            var host = new StackPanel();
            AppA.Notifications.AttachHost(host);
            var shell = new MainShellWindow();
            shell.Show();
            // After Show: the Opened startup poll must not race this test's poll.
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null;
            try
            {
                var tab = shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.PresetsTabView>("PresetsTab")!;
                typeof(ConditioningControlPanel.Avalonia.Views.Tabs.PresetsTabView)
                    .GetMethod("SelectPreset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .Invoke(tab, new object[] { preset });
                var pill = tab.FindControl<StackPanel>("PresetShareStatusHost")!;
                Assert.Contains("⏳ " + Loc.Get("catalogue_status_pending"), Texts(pill));

                await shell.CheckCatalogueSubmissionStatusesAsync(MainShellWindow.CatalogueKindPresets, force: true);
                Assert.Contains("https://app.cclabs.app/api/catalogue/presets/mine", fake.Seen);
                var rec = s.CataloguePresetSubmissions[preset.Id];
                Assert.Equal("approved", rec.Status);
                Assert.True(rec.AcceptedNotified);
                Assert.Contains("✅ " + Loc.Get("catalogue_status_approved"), Texts(pill));
                var toast = Loc.GetF("catalogue_submission_accepted_toast_fmt", preset.Id);
                Assert.Single(Texts(host), t => t == toast);

                // Nothing left open: the next forced poll makes no request and no second toast.
                var before = fake.Seen.Count;
                await shell.CheckCatalogueSubmissionStatusesAsync(MainShellWindow.CatalogueKindPresets, force: true);
                Assert.Equal(before, fake.Seen.Count);
                Assert.Single(Texts(host), t => t == toast);
            }
            finally
            {
                shell.Close();
                s.CataloguePresetSubmissions.Remove(preset.Id);
                (AppA.Catalogue, CoreSecrets.RetrieveProvider) = (oldClient, oldGet);
            }
        });
    }
}
