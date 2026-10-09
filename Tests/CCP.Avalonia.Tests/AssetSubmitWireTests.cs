using System;
using System.Collections.Generic;
using System.IO;
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
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>dialogs-asset-submit: Presets "Share to catalogue" and the session rack ☁ open
/// AssetSubmitDialog (WPF MainWindow.PresetIO.cs:190-287); nothing is POSTed until the user names a
/// creator and ticks the affirmation, then the asset goes through Core CatalogueClient (fake handler,
/// never the network), is recorded and badged.</summary>
public sealed class AssetSubmitWireTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Paths.Add(r.RequestUri!.AbsolutePath);
            if (r.RequestUri.AbsolutePath == "/api/auth/token-exchange")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    $"{{\"access_token\":\"sb\",\"expires_at\":\"{DateTimeOffset.UtcNow.AddHours(1):O}\"}}") };
            Body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"c1\",\"status\":\"pending\"}") };
        }
    }

    [Fact]
    public Task PresetShareIsGatedByTheAffirmationThenSubmitsRecordsAndBadges() => Run(async (shell, fake) =>
    {
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var preset = tab.SaveNewPreset("Share Wire " + Guid.NewGuid().ToString("N")[..6])!;
        Assert.True(tab.BtnSharePreset.IsEnabled);

        // The button opens the modal (user path); Cancel sends nothing.
        tab.BtnSharePreset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var first = shell.OwnedWindows.OfType<AssetSubmitDialog>().Single();
        first.FindControl<Button>("BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<AssetSubmitDialog>());
        Assert.Empty(fake.Paths);

        var share = shell.SharePresetToCatalogueAsync(preset);
        Dispatcher.UIThread.RunJobs();
        var dialog = shell.OwnedWindows.OfType<AssetSubmitDialog>().Single();
        var submit = dialog.FindControl<Button>("BtnSubmit")!;
        dialog.FindControl<TextBox>("TxtCreator")!.Text = "wire-creator";
        dialog.FindControl<TextBox>("TxtTags")!.Text = "Soft, soft, deep";
        Assert.False(submit.IsEnabled);
        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));     // no affirmation: refused
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.IsVisible);
        Assert.Empty(fake.Paths);

        dialog.FindControl<CheckBox>("ChkAffirm")!.IsChecked = true;
        Assert.True(submit.IsEnabled);
        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await share;

        Assert.Equal(new[] { "/api/auth/token-exchange", "/api/catalogue/presets" }, fake.Paths);
        var bundle = JObject.Parse(fake.Body!)["bundle"]!;
        Assert.Equal("ccp-preset/v1", (string?)bundle["$schema"]);
        Assert.Equal("wire-creator", (string?)bundle["metadata"]!["creator"]);
        Assert.Equal(new[] { "Soft", "deep" }, bundle["metadata"]!["tags"]!.Select(t => (string)t!));
        Assert.Contains(preset.Name, bundle["asset"]!.ToString());
        Assert.Equal("c1", MainShellWindow.GetCatalogueRecord(MainShellWindow.CatalogueKindPresets, preset.Id)!.CatalogueId);
        Assert.NotEmpty(tab.FindControl<Panel>("PresetShareStatusHost")!.Children);   // badge painted
    });

    [Fact]
    public Task SignedOutShareOpensNothingAndSendsNothing() => Run(async (shell, fake) =>
    {
        CoreSettings.Current.AuthToken = null;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var preset = tab.SaveNewPreset("Share Wire " + Guid.NewGuid().ToString("N")[..6])!;
        var share = shell.SharePresetToCatalogueAsync(preset);
        Dispatcher.UIThread.RunJobs();
        var opened = shell.OwnedWindows.OfType<AssetSubmitDialog>().ToArray();
        foreach (var d in opened) d.Close(false);   // never hang on a modal the guard should have refused
        await share;
        Assert.Empty(opened);
        Assert.Empty(fake.Paths);
    });

    [Fact]
    public Task CustomSessionRowShareOpensTheModal() => Run((shell, fake) =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-asset-submit-").FullName;
        try
        {
            var custom = Path.Combine(root, "custom");
            var builtIn = Path.Combine(root, "built-in");
            Directory.CreateDirectory(custom);
            Directory.CreateDirectory(builtIn);
            var files = new SessionFileService(custom, builtIn);
            var exported = Path.Combine(root, "share-me.session.json");
            files.ExportSession(new Session { Id = "share-me", Name = "Share Me", IsAvailable = true }, exported);
            var manager = new SessionManager(files);
            manager.LoadAllSessions();
            var (ok, why, _) = manager.ImportSession(exported);
            Assert.True(ok, why);
            var tab = shell.Named<PresetsTabView>("PresetsTab")!;
            tab.UseSessionManager(manager);
            Dispatcher.UIThread.RunJobs();

            var row = tab.SessionRackPanel.Children.OfType<Border>().Single(b => (b.Tag as Session)?.Id == "share-me");
            var cloud = row.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(ToolTip.GetTip(b), ConditioningControlPanel.Localization.Loc.Get("tooltip_share_to_catalogue")));
            cloud.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var dialog = shell.OwnedWindows.OfType<AssetSubmitDialog>().Single();
            dialog.Close(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(fake.Paths);
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    });

    private static Task Run(Func<MainShellWindow, Fake, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (users, presets, sessions) = (s.UserPresets.ToList(),
            new Dictionary<string, DeeperSubmissionRecord>(s.CataloguePresetSubmissions), new Dictionary<string, DeeperSubmissionRecord>(s.CatalogueSessionSubmissions));
        var oldClient = AvApp.Catalogue;
        var fake = new Fake();
        var (oldGet, oldPut) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        string? token = "ccp-tok";   // in-memory secret: never the real keyring
        CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? token : null;
        CoreSecrets.StoreProvider = (n, v) => { if (n == CoreSecrets.AuthToken) token = v; };
        AvApp.Catalogue = new CatalogueClient(() => CoreSettings.Current.AuthToken, () => "uid-1", "9.9.9", fake);
        var shell = new MainShellWindow();
        shell.Show();
        try { await body(shell, fake); }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            AvApp.Catalogue = oldClient;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (oldGet, oldPut);
            s.UserPresets.Clear();
            s.UserPresets.AddRange(users);
            s.CataloguePresetSubmissions.Clear();
            foreach (var kv in presets) s.CataloguePresetSubmissions[kv.Key] = kv.Value;
            s.CatalogueSessionSubmissions.Clear();
            foreach (var kv in sessions) s.CatalogueSessionSubmissions[kv.Key] = kv.Value;
        }
    });
}
