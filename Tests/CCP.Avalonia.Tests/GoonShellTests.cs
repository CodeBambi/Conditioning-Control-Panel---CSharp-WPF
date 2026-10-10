using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Goon window and the desktop around it (WPF GoonShareCard, OnBootError, the browser
/// switches): the recap card reaches the clipboard or the save dialog only as checked PNG bytes under a
/// tamed name, and a boot failure is named in a box. Process-wide seams, so alone.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class GoonShellTests
{
    private const string TinyPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private static (GameWindow W, List<JObject> Posted) Open()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        GoonHostService.DetachWindow();
        var w = new GameWindow(GameWindow.Games["goon"]);
        var posted = new List<JObject>();
        w.Posted += json => { lock (posted) posted.Add(JObject.Parse(json)); };
        w.Show();
        return (w, posted);
    }

    private static List<JObject> Results(List<JObject> posted)
    {
        lock (posted) return posted.Where(p => (string?)p["type"] == "share-card-result").ToList();
    }

    [Fact]
    public async Task ShareCard_CopiesOrSavesCheckedPngBytes_UnderATamedName()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var (w, posted) = Open();
            try
            {
                byte[]? copied = null, saved = null;
                string? savedAs = null;
                w.GoonCopyCard = (_, png) => { copied = png; return Task.FromResult(true); };
                w.GoonSaveCard = (_, png, name) => { saved = png; savedAs = name; return Task.FromResult(""); };

                w.HandleMessage(new JObject { ["type"] = "share-card", ["id"] = "c1", ["action"] = "copy", ["png"] = TinyPng }.ToString());
                for (int i = 0; i < 200 && Results(posted).Count < 1; i++) await Task.Delay(10);
                Assert.True((bool)Results(posted)[0]["ok"]!);
                Assert.Equal(Convert.FromBase64String(TinyPng), copied);

                // The page's name is a suggestion: no path survives, and it is always a .png.
                w.HandleMessage(new JObject { ["type"] = "share-card", ["id"] = "s1", ["action"] = "save", ["png"] = TinyPng,
                    ["name"] = "..\\..\\Windows\\evil.exe" }.ToString());
                for (int i = 0; i < 200 && Results(posted).Count < 2; i++) await Task.Delay(10);
                Assert.True((bool)Results(posted)[1]["ok"]!);
                Assert.Equal("Windows-evil.exe.png", savedAs);
                Assert.NotNull(saved);

                // Backing out of the dialog is an answer, not a failure of the page.
                w.GoonSaveCard = (_, _, _) => Task.FromResult("cancelled");
                w.HandleMessage(new JObject { ["type"] = "share-card", ["id"] = "s2", ["action"] = "save", ["png"] = TinyPng }.ToString());
                for (int i = 0; i < 200 && Results(posted).Count < 3; i++) await Task.Delay(10);
                Assert.False((bool)Results(posted)[2]["ok"]!);
                Assert.Equal("cancelled", (string?)Results(posted)[2]["error"]);

                // Bytes that are not a PNG never reach either door.
                copied = null;
                w.HandleMessage(new JObject { ["type"] = "share-card", ["id"] = "x1", ["action"] = "copy",
                    ["png"] = Convert.ToBase64String(new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4 }) }.ToString());
                Assert.Equal("bad-format", (string?)Results(posted)[3]["error"]);
                Assert.Null(copied);
            }
            finally { w.Close(); }
        });
    }

    [Fact]
    public async Task BootError_ClosesTheWindow_ThenNamesTheFailure()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var box = GameWindow.GoonBootErrorBox;
            string? shown = null;
            GameWindow.GoonBootErrorBox = text => { shown = text; return Task.CompletedTask; };
            var (w, _) = Open();
            bool closed = false;
            w.Closed += (_, _) => closed = true;
            try
            {
                w.HandleMessage("{\"type\":\"boot-error\",\"msg\":\"no WebGL2\"}");
                for (int i = 0; i < 200 && shown == null; i++) await Task.Delay(10);
                Assert.True(closed);
                Assert.True(GoonHostService.BootFailedThisSession);
                Assert.Contains("no WebGL2", shown);
                Assert.Contains(GoonHostService.ProductName, shown);
            }
            finally
            {
                if (!closed) w.Close();
                GameWindow.GoonBootErrorBox = box;
                GoonHostService.BootFailedThisSession = false;
            }
        });
    }

    [Fact]
    public void EveryWebView_StartsWithTheSameBrowserSwitches()
    {
        // WPF GoonHostService.cs:284. One constant: the web views share a user-data folder.
        Assert.Contains("--autoplay-policy=no-user-gesture-required", WebHost.WindowsBrowserArguments);
        Assert.Contains("--disable-background-timer-throttling", WebHost.WindowsBrowserArguments);
        Assert.Contains("--disable-backgrounding-occluded-windows", WebHost.WindowsBrowserArguments);

        // ONE mechanism (the environment options), and what a page is told matches what the browser got:
        // CCP_WEBVIEW_AUTOPLAY=off drops only the autoplay switch, and Arcademy's init.autoplayOk reads false.
        Assert.Equal(WebHost.WindowsBrowserArguments, WebHost.BrowserArgumentsFor(autoplay: true));
        Assert.DoesNotContain("autoplay", WebHost.BrowserArgumentsFor(autoplay: false));
        Assert.Contains("--disable-background-timer-throttling", WebHost.BrowserArgumentsFor(autoplay: false));
        Assert.True(WebHost.AutoplayFor(windows: true, env: null));
        Assert.False(WebHost.AutoplayFor(windows: true, env: "OFF"));
        Assert.False(WebHost.AutoplayFor(windows: false, env: null));   // WebKitGTK has no such switch
        Assert.Equal(WebHost.BrowserArgumentsFor(WebHost.AutoplayWithoutGesture), WebHost.BrowserArguments);
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(WebHostRepoRoot(), "CCP.Avalonia", "Views", "Controls", "WebHost.axaml.cs"));
        Assert.DoesNotContain("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", src);
    }

    private static string WebHostRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here)!, "..", ".."));
}
