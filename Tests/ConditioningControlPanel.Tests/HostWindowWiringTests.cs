using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1239, the WIRING half. The pure pieces (HostWindowBounds, VideoService.
/// ShouldAnnounceFeedDefer) have their own tests, but both fixes are one call site each inside WPF
/// code no unit test can drive - so reverting a single line left the suite green and the bug back.
///
/// <para>These are source facts, in the shape PanicPolicyTests already uses for the panic stop pass.
/// Every anchor is a substring of ONE line, so they read the same on a CRLF checkout as on an LF
/// one (this repo is core.autocrlf=true, and a scrape with an embedded newline is the trap that
/// made room/tests/fixture-visibility fail on every Windows machine and pass in CI).</para>
/// </summary>
public class HostWindowWiringTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    /// <summary>The body of a method, from its signature to the next doc comment at that
    /// indentation. Line-ending agnostic: the anchors never span a line break.</summary>
    private static string Body(string source, string signature, string endAnchor)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' was renamed - update this test with it");
        var end = source.IndexOf(endAnchor, start, StringComparison.Ordinal);
        return end > start ? source[start..end] : source[start..];
    }

    private static string ApplyWindowModeBody()
        => Body(Read("ConditioningControlPanel", "Chaos", "ChaosWebViewHost.cs"),
                "private void ApplyWindowMode(bool fullscreen)", "    /// <summary>");

    /// <summary>
    /// The bug itself: the fullscreen branch sized the window from the PRIMARY screen, so every
    /// game jumped to it whatever monitor the player had the window on.
    /// </summary>
    [Theory]
    [InlineData("SystemParameters.PrimaryScreenWidth")]
    [InlineData("SystemParameters.PrimaryScreenHeight")]
    public void ApplyWindowMode_NeverSizesItselfFromThePrimaryScreen(string forbidden)
        => Assert.DoesNotContain(forbidden, ApplyWindowModeBody());

    /// <summary>...and it has to ask which monitor, convert with THAT monitor's scale, and pin the
    /// result in physical pixels. Drop any one of the three and the mixed-DPI case is back.</summary>
    [Theory]
    [InlineData("TargetScreen()")]
    [InlineData("HostWindowBounds.Fullscreen(")]
    [InlineData("GetDpiForScreen(screen)")]
    [InlineData("PinFullscreenToScreen(screen)")]
    public void ApplyWindowMode_PlacesTheWindowOnItsOwnMonitor(string call)
        => Assert.Contains(call, ApplyWindowModeBody());

    /// <summary>The pin runs twice, as BrowserVideoWindow's does: WM_DPICHANGED can resize the
    /// window back after the move, so the SourceInitialized pass alone is not enough for a game
    /// launched fullscreen onto a monitor at a different scale from the primary.</summary>
    [Theory]
    [InlineData("_window.SourceInitialized += OnSource;")]
    [InlineData("_window.Loaded += OnLoaded;")]
    public void ThePhysicalPin_IsAppliedTwice(string hook)
        => Assert.Contains(hook, Body(Read("ConditioningControlPanel", "Chaos", "ChaosWebViewHost.cs"),
            "private void PinFullscreenToScreen(", "    /// <summary>"));

    /// <summary>Leaving fullscreen must not put the window back onto a monitor that has gone away
    /// since. Both exits - the plain one and the host-owned one - go through the same check.</summary>
    [Fact]
    public void BothWaysOutOfFullscreen_CheckTheFrameIsStillOnScreen()
    {
        var source = Read("ConditioningControlPanel", "Chaos", "ChaosWebViewHost.cs");
        Assert.Contains("RestoreCapturedFrame()", ApplyWindowModeBody());
        Assert.Contains("RestoreCapturedFrame()",
            Body(source, "private void LeaveHostFullscreen()", "    /// <summary>"));
        Assert.Contains("HostWindowBounds.IntersectsAnyScreen(",
            Body(source, "private bool RestoreCapturedFrame()", "    /// <summary>"));
    }

    /// <summary>#1239 part 2: the feed defer has to ASK whether it owes the user a word. Without
    /// this call the pure rule is dead code and the button is silent again.</summary>
    [Fact]
    public void TriggerVideo_AsksWhetherTheFeedDeferOwesTheUserAWord()
    {
        var body = Body(Read("ConditioningControlPanel", "Services", "Video", "VideoService.cs"),
            "public void TriggerVideo(", "        /// <summary>");
        Assert.Contains("ShouldAnnounceFeedDefer(", body);
        Assert.Contains("video_toast_feed_has_the_screen", body);
    }

    /// <summary>...and the Test Video button is what marks a press as the user's. Every other
    /// caller leaves the default, so the scheduler stays quiet.</summary>
    [Fact]
    public void TheTestVideoButton_SaysThePressWasTheUsers()
        => Assert.Contains("TriggerVideo(userInitiated: true)",
            Read("ConditioningControlPanel", "Features", "VideoFeatureControl.xaml.cs"));
}
