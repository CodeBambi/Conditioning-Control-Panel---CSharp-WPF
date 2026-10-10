using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Showcase;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF 079d665ab/08e0c1e8c: the premium showcase rides the Tonight Board - its provider is
/// in the deck after the house cards, its card wears the clip art over its poster, Motion Off
/// starts no player, and a closed shell frees the art.</summary>
public sealed class BillboardShowcaseTests
{
    [Fact]
    public Task ShowcaseCardWearsItsPosterOnHomeAndLetsGoOnClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var oldMake = MainShellWindow.MakeShowcase;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.DashboardBrowserCollapsed = true;
        s.MotionLevel = MotionLevel.Off;   // no ease, and no clip may move
        s.PerformanceMode = false;
        s.BillboardSnoozedUntil.Clear();

        var dir = Directory.CreateTempSubdirectory("ccp-showcase-").FullName;
        var poster = Path.Combine(dir, "dtrh.png");
        using (var bmp = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul)) bmp.Save(poster);
        var video = Path.Combine(dir, "dtrh.mp4");
        File.WriteAllBytes(video, new byte[] { 0 });
        var clip = new ShowcaseClip("dtrh", ShowcaseTier.Prime, "showcase-dtrh.mp4", 1, "", "showcase-dtrh.jpg", 1, "", 6);
        string? Copy(string key) { var v = Loc.Get(key); return v == key ? null : v; }
        var card = ShowcaseRules.BuildCard(clip, new ShowcaseClipArt("dtrh", video, poster, null), Copy)!;
        var fake = new FakeShowcase(card);
        MainShellWindow.MakeShowcase = () => fake;

        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.BillboardHost!;
            Assert.Equal(new[] { "house.discord", "house.webapp", "showcase:dtrh" }, host.Deck.Cards.Select(c => c.Spec.Id).Order());
            while (host.CurrentCard?.Spec.Id != "showcase:dtrh") host.Advance();
            Dispatcher.UIThread.RunJobs();

            var art = Assert.Single(host.GetLogicalDescendants().OfType<ClipArtView>());
            Assert.NotNull(art.Poster.Source);
            Assert.False(art.HasPlayer);   // Motion Off: the poster stands, no decoder made

            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(art.Poster.Source);   // the art let go with the shell (P68)
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            MainShellWindow.MakeShowcase = oldMake;
            s.BillboardSnoozedUntil.Clear();
            s.MotionLevel = MotionLevel.Full;
            CoreSettings.ServiceProvider = old;
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });

    /// <summary>The board stays silent: the clip player is muted and opens no audio track.</summary>
    [Fact]
    public void ShowcaseClipIsMutedWithNoAudioTrack()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ConditioningControlPanel.sln"))) d = d.Parent;
        var code = File.ReadAllText(Path.Combine(d!.FullName, "CCP.Avalonia/Controls/Billboard/ClipArtView.cs"));
        code = Regex.Replace(code, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
        Assert.Matches(@"new MediaPlayer\(vlc\)\s*\{[^}]*Mute = true", code);
        Assert.Contains("AddOption(\":no-audio\")", code, StringComparison.Ordinal);
    }

    private sealed class FakeShowcase(BillboardCardSpec card) : IBillboardProvider
    {
        public string Id => "showcase";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) =>
            context.Tier == BillboardTier.Prime ? Array.Empty<BillboardCardSpec>() : new[] { card };
        public void Invoke(string actionTarget) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
