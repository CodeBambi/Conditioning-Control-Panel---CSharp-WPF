using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Owner calls of 2026-10-10 on the remote verbs: play_hypnotube is ported and site-locked
/// (<see cref="RemoteVideoLink"/> is the whole address rule), and Circe's Tab is charged for a controller's
/// video or picture only when it was actually shown.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteVideoLinkTests
{
    [Theory]
    [InlineData("https://hypnotube.com/video/12345")]
    [InlineData("https://hypnotube.com/video/12345/")]
    [InlineData("https://www.hypnotube.com/video/some-title-98765.html")]
    [InlineData("https://hypnotube.com/video/some-title-98765.html?t=10")]
    public void A_plain_https_video_page_on_the_site_passes(string raw)
    {
        Assert.True(RemoteVideoLink.TryParse(raw, out var link));
        Assert.Equal(Uri.UriSchemeHttps, link.Scheme);
        Assert.Contains(link.Host, new[] { "hypnotube.com", "www.hypnotube.com" });
        Assert.StartsWith("https://", link.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("hypnotube.com/video/12345")]                              // not absolute
    [InlineData("/video/12345")]
    [InlineData("javascript:alert(1)")]
    [InlineData("javascript://hypnotube.com/video/12345")]
    [InlineData("data:text/html,https://hypnotube.com/video/12345")]
    [InlineData("file:///C:/video/12345")]
    [InlineData("http://hypnotube.com/video/12345")]                       // WPF took http; the port does not
    [InlineData("HTTPS://hypnotube.com/video/12345")]
    [InlineData("ftp://hypnotube.com/video/12345")]
    [InlineData("https://hypnotube.co/video/12345")]                       // look-alikes
    [InlineData("https://hypnotube.com.evil.example/video/12345")]         // the site as a subdomain prefix
    [InlineData("https://evilhypnotube.com/video/12345")]
    [InlineData("https://hypnotube-com.example/video/12345")]
    [InlineData("https://hypn0tube.com/video/12345")]
    [InlineData("https://cdn.hypnotube.com/video/12345")]                  // WPF took any subdomain
    [InlineData("https://www.www.hypnotube.com/video/12345")]
    [InlineData("https://hypnotube.com./video/12345")]
    [InlineData("https://HYPNOTUBE.com/video/12345")]                      // must be written as it is read
    [InlineData("https://hypnotub\u0435.com/video/12345")]                 // Cyrillic e
    [InlineData("https://\uFF48ypnotube.com/video/12345")]                 // fullwidth h folds to ASCII in a parser
    [InlineData("https://xn--hypnotub-9ig.com/video/12345")]
    [InlineData("https://user:pw@hypnotube.com/video/12345")]              // embedded credentials
    [InlineData("https://hypnotube.com@evil.example/video/12345")]
    [InlineData("https://evil.example/video/12345#@hypnotube.com")]
    [InlineData("https://evil.example/?https://hypnotube.com/video/12345")]
    [InlineData("https://hypnotube.com:8443/video/12345")]                 // not the default port
    [InlineData("https://hypnotube.com:80/video/12345")]
    [InlineData("https://hypnotube.com:443/video/12345")]                  // no port is ever written
    [InlineData("https://127.0.0.1/video/12345")]
    [InlineData("https://[::1]/video/12345")]
    [InlineData("https://hypnotube.com/video/12345%0a")]                   // encoded newline
    [InlineData("https://hypnotube.com/video/12345%0D%0AHost:x")]
    [InlineData("https://hypnotube.com/video/12345%00")]
    [InlineData("https://hypnotube.com/video/12345%250a")]                 // doubly encoded
    [InlineData("https://hypnotube.com/video/12345%7f")]
    [InlineData("https://hypnotube.com/video/12345\n")]                    // control characters
    [InlineData("https://hypnotube.com/video/12345\r\nx")]
    [InlineData("https://hypnotube.com/video/12345\t")]
    [InlineData("https://hypnotube.com/video/12345\0")]
    [InlineData(" https://hypnotube.com/video/12345")]                     // whitespace
    [InlineData("https://hypnotube.com/video/12345 ")]
    [InlineData("https://hypnotube.com/video/12 345")]
    [InlineData("https://hypnotube.com/video/12345\"")]                    // quotes
    [InlineData("https://hypnotube.com/video/12345'")]
    [InlineData("https://hypnotube.com/video/12345`")]
    [InlineData("https://hypnotube.com/video/12345\" --flag")]
    [InlineData("https://hypnotube.com\\@evil.example/video/12345")]
    [InlineData("https://hypnotube.com/video/12345<script>")]
    [InlineData("https://hypnotube.com/")]                                 // not a video page
    [InlineData("https://hypnotube.com/videos/")]
    [InlineData("https://hypnotube.com/user/someone")]
    [InlineData("https://hypnotube.com/redirect?to=https://evil.example/video/1")]
    public void Anything_else_is_refused(string? raw) => Assert.False(RemoteVideoLink.TryParse(raw, out _));

    [Fact]
    public void An_overlong_address_is_refused()
    {
        var ok = "https://hypnotube.com/video/12345?x=" + new string('a', RemoteVideoLink.MaxLength - 36 - 1);
        Assert.True(ok.Length <= RemoteVideoLink.MaxLength);
        Assert.True(RemoteVideoLink.TryParse(ok, out _));
        Assert.False(RemoteVideoLink.TryParse(ok + new string('a', 40), out _));
    }

    private sealed class Head : RemoteCommands.IRemoteHead
    {
        public readonly List<string> Played = new();
        public int Stops;
        public string? Refusal;
        public bool RefreshOverlay(string which) => false;
        public string? RefreshBrainDrain() => null;
        public string? ShowLockCard() => null;
        public void CloseCards() { }
        public string? SetAutonomy(bool on) => null;
        public void CancelAutonomyPulses(bool restart) { }
        public string? Session(string verb, JObject? p) => null;
        public bool SessionIsRemoteStarted => false;
        public string? PlayVideoLink(string absoluteUri) { Played.Add(absoluteUri); return Refusal; }
        public void StopVideoLink() => Stops++;
    }

    private static JObject Url(string? url) => url == null ? new JObject() : new JObject { ["url"] = url };

    [Fact]
    public void The_verb_hands_the_head_the_parsed_address_and_refuses_the_rest()
    {
        var before = RemoteCommands.Head;
        var head = new Head();
        RemoteCommands.Head = head;
        try
        {
            Assert.Null(RemoteCommands.Execute("play_hypnotube", Url("https://hypnotube.com/video/My-Title-12345.html#top")));
            Assert.Equal(new Uri("https://hypnotube.com/video/My-Title-12345.html#top").AbsoluteUri, Assert.Single(head.Played));   // AbsoluteUri, never the raw string

            foreach (var bad in new[] { null, "", "javascript:alert(1)", "http://hypnotube.com/video/12345",
                         "https://hypnotube.com.evil.example/video/12345", "https://a:b@hypnotube.com/video/12345",
                         "https://hypnotube.com/video/12345%0a" })
                Assert.Equal(RemoteVideoLink.Refused, RemoteCommands.Execute("play_hypnotube", Url(bad)));
            Assert.Equal(RemoteVideoLink.Refused, RemoteCommands.Execute("play_hypnotube", null));
            Assert.Single(head.Played);   // nothing refused ever reached the head

            head.Refusal = "a game is on screen";   // the head's own reason is what the controller reads
            Assert.Equal("a game is on screen", RemoteCommands.Execute("play_hypnotube", Url("https://hypnotube.com/video/12345")));

            RemoteCommands.Head = null;
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("play_hypnotube", Url("https://hypnotube.com/video/12345")));
        }
        finally { RemoteCommands.Head = before; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_remote_stop_path_takes_the_video_link_down(bool panic)
    {
        var s = CoreSettings.Current;
        var (before, pink, spiral, strict, key) = (RemoteCommands.Head, s.PinkFilterEnabled, s.SpiralEnabled, s.StrictLockEnabled, s.PanicKeyEnabled);
        var head = new Head();
        RemoteCommands.Head = head;
        try
        {
            if (panic) Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            else RemoteCommands.StopEffects(force: false);
            Assert.Equal(1, head.Stops);
        }
        finally
        {
            RemoteCommands.Head = before;
            (s.PinkFilterEnabled, s.SpiralEnabled, s.StrictLockEnabled, s.PanicKeyEnabled) = (pink, spiral, strict, key);
        }
    }

    // ---- Circe's Tab: booked only for what was shown ----

    private sealed class VideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    [Fact]
    public void Remote_video_books_only_when_the_video_starts()
    {
        var (video, note) = (CoreEngine.Video, RemoteCommands.ChasterNote);
        var booked = new List<string>();
        RemoteCommands.ChasterNote = booked.Add;
        var clips = new List<string> { "/v/a.mp4" };
        var scheduler = new MandatoryVideoScheduler(new VideoHost(), TimeProvider.System, () => clips);
        CoreEngine.Video = scheduler;
        try
        {
            Assert.Null(RemoteCommands.Execute("trigger_video", null));
            Assert.Equal(new[] { "remote_video" }, booked);

            // One is already playing: refused, and the wearer's tab is not charged a second time.
            Assert.Equal("a video is already playing", RemoteCommands.Execute("trigger_video", null));
            Assert.Equal(new[] { "remote_video" }, booked);

            // Nothing in the library: nothing played, nothing booked.
            scheduler.Stop();
            clips.Clear();
            booked.Clear();
            Assert.NotNull(RemoteCommands.Execute("trigger_video", null));
            Assert.Empty(booked);

            CoreEngine.Video = null;
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("trigger_video", null));
            Assert.Empty(booked);
        }
        finally
        {
            try { scheduler.Stop(); } catch { }
            (CoreEngine.Video, RemoteCommands.ChasterNote) = (video, note);
        }
    }

    [Fact]
    public void Remote_media_books_only_when_a_picture_was_shown()
    {
        var (show, tryShow, note) = (CoreFlash.ShowProvider, CoreFlash.TryShowProvider, RemoteCommands.ChasterNote);
        var booked = new List<string>();
        var shown = true;
        var asked = 0;
        RemoteCommands.ChasterNote = booked.Add;
        CoreFlash.ShowProvider = () => { };
        CoreFlash.TryShowProvider = () => { asked++; return shown; };
        try
        {
            Assert.Null(RemoteCommands.Execute("trigger_flash", null));
            Assert.Equal(new[] { "remote_media" }, booked);

            shown = false;   // busy, held by do not disturb, no screen
            Assert.Equal(RemoteCommands.NothingShown, RemoteCommands.Execute("trigger_flash", null));
            Assert.Equal(new[] { "remote_media" }, booked);
            Assert.Equal(2, asked);

            CoreFlash.ShowProvider = null;   // no flash surface on this head at all
            booked.Clear();
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("trigger_flash", null));
            Assert.Empty(booked);
        }
        finally { (CoreFlash.ShowProvider, CoreFlash.TryShowProvider, RemoteCommands.ChasterNote) = (show, tryShow, note); }
    }
}
