using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using CCP.Tests.Shared;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The Mod Manager's pack row (WPF ModManagerDialog.BtnDownloadPack_Click) against a loopback fake
/// release server: the click downloads, the row shows progress, ends Installed and the list refreshes.
/// </summary>
public sealed class ModManagerPackDownloadTests
{
    private static byte[] BuildZip()
    {
        var payload = new byte[2 * 1024 * 1024];
        new Random(7).NextBytes(payload);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var s = zip.CreateEntry("Resources/sounds/test/clip.mp3", CompressionLevel.NoCompression).Open())
            s.Write(payload);
        return ms.ToArray();
    }

    [Fact]
    public Task DownloadRowDownloadsShowsProgressEndsInstalledAndRefreshesTheList() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Assert.Equal(TestUserDataProfile.Root, CorePaths.UserData);   // never the real profile
        if (global::Avalonia.Application.Current is null)
            global::Avalonia.AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var zip = BuildZip();
        var sha = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        var manifest = System.Text.Encoding.UTF8.GetBytes(
            "{\"packs\":[{\"id\":\"mod-bambi\",\"file\":\"mod-bambi.zip\",\"sizeBytes\":" + zip.Length +
            ",\"sha256\":\"" + sha + "\",\"contentVersion\":1,\"targetRoot\":\"\"}]}");
        using var server = new FakeServer();
        server.Handle = c => c.Request.Url!.AbsolutePath switch
        {
            "/v6.6.0/content-manifest.json" => FakeServer.Send(c, manifest),
            "/v6.6.0/mod-bambi.zip" => FakeServer.Send(c, zip),
            _ => FakeServer.Send(c, Array.Empty<byte>(), 404),
        };
        var handler = new LoopbackOnlyHandler();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldVersion = CoreReleaseContent.AppVersionProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreReleaseContent.AppVersionProvider = () => "6.6.3";   // cycle v6.6.0
        using var svc = new ReleaseContentService(server.Prefix + "{0}/", handler);
        try
        {
            settings.Current.InstalledContentPacks.Clear();
            settings.Current.OfflineMode = false;
            settings.Current.ActiveModId = BuiltInMods.CCPDefaultId;
            AvApp.StartMods();
            AvApp.StartReleaseContent(svc);

            var dialog = new ModManagerDialog();
            var list = dialog.FindControl<ListBox>("ModList")!;
            var state = dialog.FindControl<TextBlock>("TxtPackState")!;
            var bar = dialog.FindControl<ProgressBar>("PackProgress")!;
            var button = dialog.FindControl<Button>("BtnDownloadPack")!;
            ListBoxItem Row() => list.Items.OfType<ListBoxItem>().Single(i => (string)i.Tag! == BuiltInMods.BambiSleepId);
            bool Badged() => ((StackPanel)Row().Content!).Children.Count > 1;

            list.SelectedItem = Row();
            Assert.True(Badged());
            Assert.True(button.IsEnabled);
            Assert.Equal(Loc.Get("modmgr_pack_not_downloaded"), state.Text);

            var seen = new List<double>();
            bar.PropertyChanged += (_, e) => { if (e.Property == ProgressBar.ValueProperty) seen.Add(bar.Value); };
            await dialog.BtnDownloadPack_Click();
            await Task.Delay(200);   // the posted PackInstalled / ModAvailabilityChanged refreshes

            Assert.Contains(seen, v => v > 0 && v < 100);   // progress was shown
            Assert.True(svc.IsInstalled("mod-bambi"));
            Assert.Equal(Loc.Get("modmgr_pack_ready"), state.Text);          // ends Installed
            Assert.Equal(100, bar.Value);
            Assert.False(button.IsEnabled);
            Assert.False(Badged());                                         // list refreshed
            Assert.Same(Row(), list.SelectedItem);                          // selection kept
            Assert.Equal(1, server.PackGets);
            Assert.Empty(handler.Violations);
        }
        finally
        {
            settings.SaveImmediate();
            settings.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            CoreReleaseContent.AppVersionProvider = oldVersion;
            CoreReleaseContent.StampProvider = null;
            CoreReleaseContent.PackInfoProvider = null;
            CoreReleaseContent.UiInvoke = null;
            AvApp.ResetReleaseContent();
        }
    });

    [Theory]
    [InlineData("/tmp/sandbox", null, true)]
    [InlineData("/tmp/sandbox", "", true)]
    [InlineData("/tmp/sandbox", "https://example.com/x", true)]      // non-loopback is ignored, so still GitHub
    [InlineData("/tmp/sandbox", "http://127.0.0.1:18931", false)]
    [InlineData(null, null, false)]                                    // a real profile keeps WPF's rules
    public void SandboxedStartupNeverFetchesFromGitHub(string? userData, string? baseUrl, bool skip) =>
        Assert.Equal(skip, AvApp.SkipStartupFetch(userData, baseUrl));
}
