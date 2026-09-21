using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1233 / #1237: the media log drew an empty "file not found" card for everything the
/// online feed had shown, and printed its source URL through the library-path tidy-up, so the
/// link came out as "https:\\..." in a TextBlock nobody could select.
///
/// The pane's whole decision lives in <see cref="MediaHistoryPreviewRules"/> so it can be checked
/// without a window, a disk or a network.
/// </summary>
public class MediaHistoryPreviewRulesTests
{
    private const string Url = "https://cdn.example.com/a/b/still.webp";

    [Theory]
    [InlineData("https://cdn.example.com/x.jpg", true)]
    [InlineData("http://cdn.example.com/x.jpg", true)]
    [InlineData(@"D:\Assets\images\x.jpg", false)]
    [InlineData("D:/Assets/images/x.jpg", false)]
    [InlineData("", false)]
    public void IsRemote_TellsAUrlFromALibraryPath(string path, bool expected)
        => Assert.Equal(expected, MediaHistoryPreviewRules.IsRemote(path));

    [Fact]
    public void OnlyHttpsIsHandedToTheBrowser()
    {
        Assert.True(MediaHistoryPreviewRules.IsBrowsable("https://cdn.example.com/x.jpg"));
        Assert.False(MediaHistoryPreviewRules.IsBrowsable("http://cdn.example.com/x.jpg"));
        Assert.False(MediaHistoryPreviewRules.IsBrowsable(@"D:\Assets\x.jpg"));
    }

    [Theory]
    // A quote closes BrowserLauncher's `cmd /c start "" "{url}"` and starts a second command, on
    // exactly the machines with no default browser - the ones that reach that fallback at all.
    [InlineData("https://a.example.com/x.jpg\"&calc&\"")]
    [InlineData("https://a.example.com/\"")]
    // rundll32 takes the url unquoted, so control characters matter too.
    [InlineData("https://a.example.com/x\njpg")]
    [InlineData("https://a.example.com/x\rjpg")]
    public void APoisonedFeedUrlIsNeverLaunched(string url)
    {
        // Not a vacuous test: Uri.TryCreate is perfectly happy with every one of these - the
        // scheme really is https - which is why the first cut handed them to the launcher.
        Assert.True(MediaHistoryPreviewRules.IsRemote(url));

        Assert.Null(MediaHistoryPreviewRules.BrowsableUrl(url));
        Assert.False(MediaHistoryPreviewRules.IsBrowsable(url));

        var plan = MediaHistoryPreviewRules.Plan(url, MediaType.Image, false, false);
        Assert.False(plan.CanOpenSource);
        Assert.Null(plan.BrowseUrl);
        // Copying it is still fine: the clipboard is text, not a command line, and the user asked.
        Assert.True(plan.CanCopyLink);
    }

    [Fact]
    public void WhatGoesToTheBrowserIsTheParsedEscapedForm()
    {
        // AbsoluteUri, not OriginalString: what was stored never reaches a command line.
        Assert.Equal("https://cdn.example.com/a%20b/x.jpg",
            MediaHistoryPreviewRules.BrowsableUrl("https://cdn.example.com/a b/x.jpg"));

        var plan = MediaHistoryPreviewRules.Plan("https://cdn.example.com/a b/x.jpg",
            MediaType.Image, false, false);
        Assert.Equal("https://cdn.example.com/a%20b/x.jpg", plan.BrowseUrl);
        // ...while the line under the preview still shows what was logged, for copying.
        Assert.Equal("https://cdn.example.com/a b/x.jpg", plan.SourceText);
    }

    [Fact]
    public void WithoutRemoteConsentThereIsNoFetchButton()
    {
        // The media log is a recap window, not a way around the app-wide remote gate.
        var denied = MediaHistoryPreviewRules.Plan(Url, MediaType.Image,
            localExists: false, remoteCached: false, remoteConsent: false);
        var allowed = MediaHistoryPreviewRules.Plan(Url, MediaType.Image,
            localExists: false, remoteCached: false, remoteConsent: true);

        Assert.False(denied.CanLoadPreview);
        Assert.True(allowed.CanLoadPreview);
        // Everything that needs no network is unaffected.
        Assert.True(denied.CanCopyLink);
        Assert.True(denied.CanOpenSource);
    }

    [Fact]
    public void ASourceUrlIsShownExactlyAsStored()
    {
        // The bug: the #1108 backslash tidy-up ran on URLs too and produced "https:\\cdn...".
        Assert.Equal(Url, MediaHistoryPreviewRules.SourceText(Url));
    }

    [Fact]
    public void ALibraryPathStillGetsTheBackslashTidyUp()
    {
        Assert.Equal(@"D:\Assets\images\x.gif",
            MediaHistoryPreviewRules.SourceText(@"D:/Assets/images\x.gif"));
    }

    [Fact]
    public void AnOnlineStillStillInTheCacheIsDrawn()
    {
        var plan = MediaHistoryPreviewRules.Plan(Url, MediaType.Image, localExists: false, remoteCached: true);

        Assert.Equal(MediaPreviewKind.RemoteCached, plan.Kind);
        Assert.True(plan.IsRemote);
        Assert.False(plan.CanLoadPreview);        // nothing to fetch, it is already here
        Assert.True(plan.CanCopyLink);
        Assert.True(plan.CanOpenSource);
        Assert.False(plan.CanOpenFolder);         // an online item has no folder of its own
        Assert.False(plan.CanOpenFile);
    }

    [Fact]
    public void AnOnlineStillThatHasBeenEvictedSaysSoAndOffersToFetchIt()
    {
        var plan = MediaHistoryPreviewRules.Plan(Url, MediaType.Image,
            localExists: false, remoteCached: false, remoteConsent: true);

        Assert.Equal(MediaPreviewKind.RemoteUncached, plan.Kind);
        Assert.True(plan.CanLoadPreview);
        Assert.True(plan.CanCopyLink);
        Assert.True(plan.CanOpenSource);
    }

    [Fact]
    public void AnOnlineClipIsNeverDrawnFromTheCache()
    {
        // LibVLC streams a remote clip FromLocation, so there is no frame to pull out of the
        // byte cache and no local file either. Source actions only.
        var plan = MediaHistoryPreviewRules.Plan("https://v.example.com/p/DASH_720.mp4",
            MediaType.Video, localExists: false, remoteCached: true, remoteConsent: true);

        Assert.Equal(MediaPreviewKind.RemoteUncached, plan.Kind);
        Assert.False(plan.CanLoadPreview);   // consent or not, there is no frame to pull
        Assert.True(plan.CanOpenSource);
    }

    [Fact]
    public void APlainHttpItemCanBeCopiedButNotOpened()
    {
        var plan = MediaHistoryPreviewRules.Plan("http://cdn.example.com/x.jpg",
            MediaType.Image, localExists: false, remoteCached: false);

        Assert.True(plan.CanCopyLink);
        Assert.False(plan.CanOpenSource);
    }

    [Fact]
    public void ALocalFileThatIsStillThereKeepsBothFileActions()
    {
        var plan = MediaHistoryPreviewRules.Plan(@"D:\Assets\images\x.jpg",
            MediaType.Image, localExists: true, remoteCached: false);

        Assert.Equal(MediaPreviewKind.LocalFile, plan.Kind);
        Assert.False(plan.IsRemote);
        Assert.True(plan.CanOpenFolder);
        Assert.True(plan.CanOpenFile);
        Assert.False(plan.CanCopyLink);
        Assert.False(plan.CanOpenSource);
    }

    [Fact]
    public void AMissingLocalFileKeepsOnlyTheFolderAction()
    {
        // The reveal helper falls back to the containing folder for a missing file (#998).
        var plan = MediaHistoryPreviewRules.Plan(@"D:\Assets\images\gone.jpg",
            MediaType.Image, localExists: false, remoteCached: false);

        Assert.Equal(MediaPreviewKind.LocalMissing, plan.Kind);
        Assert.True(plan.CanOpenFolder);
        Assert.False(plan.CanOpenFile);
        Assert.False(plan.CanLoadPreview);
    }

    [Fact]
    public void NothingSelectedIsNoPlanAtAll()
    {
        var plan = MediaHistoryPreviewRules.Plan("", MediaType.Image, false, false);

        Assert.Equal(MediaPreviewKind.None, plan.Kind);
        Assert.False(plan.CanCopyLink);
        Assert.Equal("", plan.SourceText);
    }
}
