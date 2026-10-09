using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Library pack strip (WPF 7.1.5 MainWindow.Assets.cs RefreshPacksAsync / BtnPackDownload / BtnPackActivate)
/// over the Core ContentPackService with a fake HTTP handler: cards from the server manifest, decoded
/// rotating previews for an installed pack, install + activate, and the strip hidden as on 7.1.5.
/// The view is not hosted in a Window, so the result dialogs log instead of blocking.
/// </summary>
public sealed class PackCardsTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    private const string Base = "http://127.0.0.1:9";

    [Fact]
    public Task Strip_LoadsCards_RotatesPreviews_InstallsAndToggles() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureAvalonia();
        var root = Path.Combine(Path.GetTempPath(), "ccp-packcards-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = new AppSettings();
        try
        {
            // An installed pack with two pictures (orphan-registered, active).
            var guid = Guid.NewGuid().ToString("N");
            var dir = Path.Combine(root, ".packs", guid);
            Directory.CreateDirectory(Path.Combine(dir, "content"));
            var manifest = new InstalledPackManifest { PackId = "have", PackGuid = guid, PackName = "Have" };
            foreach (var n in new[] { "a.png", "b.png" })
            {
                var obf = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
                File.WriteAllBytes(Path.Combine(dir, "content", obf), PackEncryptionService.Encrypt(Convert.FromBase64String(OnePixelPng)));
                manifest.Files.Add(new PackFileEntry { OriginalName = n, ObfuscatedName = obf, FileType = "image", Extension = ".png" });
            }
            PackEncryptionService.SaveEncryptedManifest(JsonConvert.SerializeObject(manifest), Path.Combine(dir, ".manifest.enc"));

            byte[] zip;
            using (var ms = new MemoryStream())
            {
                using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
                using (var s = z.CreateEntry("images/new.png").Open())
                    s.Write(Convert.FromBase64String(OnePixelPng));
                zip = ms.ToArray();
            }
            var h = new Fake(req => (req.Method.Method, req.RequestUri!.ToString()) switch
            {
                ("GET", Base + "/packs/manifest") => Json(new { packs = new object[] { new { id = "have", name = "Have" }, new { id = "get", name = "Get", sizeBytes = zip.Length } } }),
                ("POST", Base + "/pack/download-url") => Json(new { downloadUrl = "https://cdn.test/get.zip" }),
                ("GET", "https://cdn.test/get.zip") => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });

            var store = new ContentPackStore(() => root, () => settings, () => { });
            var svc = new ContentPackService(store, new HttpClient(h), () => Base, () => settings, () => { }, () => "pat",
                () => null, () => Path.Combine(root, "previews"), _ => Task.CompletedTask);
            var view = new AssetsTabView { AssetsRootOverride = root, PackStoreOverride = store, PackServiceOverride = svc };

            Assert.False(view.PacksSection.IsVisible);   // 7.1.5 ships the strip hidden

            await view.RefreshPacksAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { "have", "get" }, view.Browser.Packs.Select(c => c.Pack.Id));
            var have = view.Browser.Packs[0];
            Assert.True(have.IsDownloaded);
            Assert.Equal("Deactivate", have.ActivateButtonText);
            Assert.True(have.HasPreviewImages);
            var first = have.CurrentPreviewImage;
            have.AdvancePreviewImage();
            Assert.NotSame(first, have.CurrentPreviewImage);

            var get = view.Browser.Packs[1];
            Assert.Equal("Install", get.DownloadButtonText);
            await view.InstallAndActivateAsync(get);
            Dispatcher.UIThread.RunJobs();
            Assert.True(get.IsDownloaded);
            Assert.True(get.IsActive);
            Assert.False(get.IsDownloading);
            Assert.Equal("Uninstall", get.DownloadButtonText);
            Assert.True(store.IsPackActive("get"));
            Assert.True(get.HasPreviewImages);

            view.TogglePackActive(get);
            Assert.False(store.IsPackActive("get"));
            Assert.Equal("Activate", get.ActivateButtonText);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    });

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json") };

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(reply(request));
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is not null) return;
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }
}
