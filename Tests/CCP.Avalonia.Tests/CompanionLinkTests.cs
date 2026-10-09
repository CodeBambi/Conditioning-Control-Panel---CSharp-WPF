using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane r13: links the companion offers. One launcher (remote guard, http(s) only,
/// embedded browser first, https-only system fallback) and the pink underlined link Runs the tube
/// bubble and its chat log draw, with the click resolved from the text layout.</summary>
[Collection(RunsAloneCollection.Name)]   // swaps the launcher's process-wide seams
public sealed class CompanionLinkTests : IDisposable
{
    private readonly Func<bool> _remote = CompanionLinkLauncher.RemoteControllerConnected;
    private readonly Func<string, bool> _embedded = CompanionLinkLauncher.NavigateEmbedded;
    private readonly Action<string> _external = CompanionLinkLauncher.OpenExternal;
    private readonly List<string> _navigated = new();
    private readonly List<string> _opened = new();
    private bool _remoteOn, _embeddedWorks = true;

    private static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bambi TikTok - In Beat - Longer Version"] = "https://example.test/video/in-beat-longer-1.html",
    };

    public CompanionLinkTests()
    {
        CompanionLinkLauncher.RemoteControllerConnected = () => _remoteOn;
        CompanionLinkLauncher.NavigateEmbedded = url => { _navigated.Add(url); return _embeddedWorks; };
        CompanionLinkLauncher.OpenExternal = url => _opened.Add(url);
    }

    public void Dispose()
    {
        CompanionLinkLauncher.RemoteControllerConnected = _remote;
        CompanionLinkLauncher.NavigateEmbedded = _embedded;
        CompanionLinkLauncher.OpenExternal = _external;
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void LauncherPrefersTheEmbeddedBrowser()
    {
        Assert.Equal(CompanionLinkLauncher.Outcome.Embedded, CompanionLinkLauncher.Open("https://example.test/a"));
        Assert.Single(_navigated);
        Assert.Empty(_opened);
    }

    [Fact]
    public void LauncherRefusesWhileARemoteControllerIsConnected()
    {
        _remoteOn = true;
        Assert.Equal(CompanionLinkLauncher.Outcome.BlockedByRemote, CompanionLinkLauncher.Open("https://example.test/a"));
        Assert.Empty(_navigated);
        Assert.Empty(_opened);
    }

    [Fact]
    public void LauncherOnlyOpensWebLinksAndFallsBackForHttpsOnly()
    {
        Assert.Equal(CompanionLinkLauncher.Outcome.Refused, CompanionLinkLauncher.Open("file:///c:/x.exe"));
        Assert.Equal(CompanionLinkLauncher.Outcome.Refused, CompanionLinkLauncher.Open("javascript:alert(1)"));
        Assert.Equal(CompanionLinkLauncher.Outcome.Refused, CompanionLinkLauncher.Open(" "));
        Assert.Empty(_navigated);

        _embeddedWorks = false;
        Assert.Equal(CompanionLinkLauncher.Outcome.External, CompanionLinkLauncher.Open("https://example.test/a"));
        Assert.Single(_opened);
        Assert.Equal(CompanionLinkLauncher.Outcome.Refused, CompanionLinkLauncher.Open("http://example.test/a"));
        Assert.Single(_opened);   // plain http never leaves for the system browser
    }

    [Fact]
    public Task LinkedLineDrawsAPinkUnderlinedRunAndTheClickFindsItsUrl() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        const string line = "Alright, let's watch Bambi TikTok - In Beat - Longer Version and see how it goes!";
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, Padding = new Thickness(0, 0, 6, 0), FontSize = 15 };
        var window = new Window { Width = 320, Height = 400, Content = tb };
        try
        {
            window.Show();
            var ranges = CompanionLinkedText.Apply(tb, line, Table);
            Dispatcher.UIThread.RunJobs();

            var range = Assert.Single(ranges);
            Assert.Equal(line.IndexOf("Bambi TikTok", StringComparison.Ordinal), range.Start);
            var runs = tb.Inlines!.OfType<Run>().ToList();
            Assert.Equal(3, runs.Count);
            Assert.Same(CompanionLinkedText.LinkBrush, runs[1].Foreground);
            Assert.NotNull(runs[1].TextDecorations);
            Assert.Equal(line, string.Concat(runs.Select(r => r.Text)));
            Assert.True(tb.TextLayout.TextLines.Count > 1, "the linked line still wraps");

            // A point inside the title hits the link; one inside "Alright" does not.
            var inLink = tb.TextLayout.HitTestTextPosition(range.Start + 3);
            var inPlain = tb.TextLayout.HitTestTextPosition(2);
            Assert.Equal(range.Url, CompanionLinkedText.UrlAt(tb, inLink.Center));
            Assert.Null(CompanionLinkedText.UrlAt(tb, inPlain.Center));

            // Typing the next line as plain text drops the links.
            tb.Text = "next line";
            Assert.Empty(CompanionLinkedText.RangesOf(tb));
            Assert.Null(CompanionLinkedText.UrlAt(tb, inLink.Center));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PlainLineStaysPlainTextAndTheBoundPropertyLinksAChatLogRow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var tb = new TextBlock();
        Assert.Empty(CompanionLinkedText.Apply(tb, "just breathe for me", Table));
        Assert.Equal("just breathe for me", tb.Text);
        Assert.True(tb.Inlines == null || tb.Inlines.Count == 0);

        var row = new TextBlock();
        CompanionLinkedText.SetText(row, "see https://example.test/video/soft-spiral-99.html");
        var range = Assert.Single(CompanionLinkedText.RangesOf(row));
        Assert.Equal("https://example.test/video/soft-spiral-99.html", range.Url);
        return Task.CompletedTask;
    });
}
