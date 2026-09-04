using System;
using ConditioningControlPanel.Services.Logging;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Contract tests for <see cref="UrlLog.Host"/>, the helper every log line now goes through
/// instead of interpolating a URL.
///
/// The point of the helper is what it throws away. A HypnoTube path is the title of the video
/// the user was watching, a query string carries session tokens and signed asset URLs, and a
/// fragment can carry either - none of that may reach a log file that gets pasted into a
/// support thread. So the tests here mostly assert absence: whatever comes out must not
/// contain the path, the query, or the fragment of what went in.
///
/// The other half of the contract is that a log call can never take the app down: no input,
/// however malformed, may throw, and no failure path may hand the caller back the raw URL.
/// </summary>
public class UrlLogHostTests
{
    [Theory]
    [InlineData("https://hypnotube.com/video/12345/some-very-revealing-title/", "hypnotube.com")]
    [InlineData("https://www.bambicloud.com/watch?v=abc&token=secret", "www.bambicloud.com")]
    [InlineData("http://example.org/a/b/c#fragment", "example.org")]
    [InlineData("https://EXAMPLE.ORG/Path", "example.org")]
    [InlineData("  https://example.org/path  ", "example.org")]
    public void Host_keeps_only_the_host(string url, string expected)
    {
        Assert.Equal(expected, UrlLog.Host(url));
    }

    [Fact]
    public void Host_drops_path_query_and_fragment()
    {
        const string url = "https://hypnotube.com/videos/999/deep-trance-title?token=abcdef123456#t=90";
        var host = UrlLog.Host(url);

        Assert.Equal("hypnotube.com", host);
        Assert.DoesNotContain("deep-trance-title", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abcdef123456", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("90", host, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_keeps_a_non_default_port()
    {
        // Local haptics and dev endpoints are identified by their port, so it survives.
        Assert.Equal("127.0.0.1:20010", UrlLog.Host("http://127.0.0.1:20010/command?command=GetToys"));
        Assert.Equal("192.168.1.5:30010", UrlLog.Host("https://192.168.1.5:30010/command"));
    }

    [Fact]
    public void Host_drops_a_default_port()
    {
        Assert.Equal("example.org", UrlLog.Host("https://example.org:443/x"));
        Assert.Equal("example.org", UrlLog.Host("http://example.org:80/x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Host_reports_blank_input_as_none(string? url)
    {
        Assert.Equal(UrlLog.Empty, UrlLog.Host(url));
    }

    [Theory]
    // Relative paths, schemes with no host, and pasted junk are all "not a URL we can name".
    [InlineData("/videos/some-title")]
    [InlineData("not a url at all")]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("about:blank")]
    public void Host_reports_unparseable_input_as_invalid(string url)
    {
        Assert.Equal(UrlLog.Invalid, UrlLog.Host(url));
    }

    [Fact]
    public void Host_never_echoes_the_input_back_on_failure()
    {
        // The failure path is exactly the case most likely to be a blob of pasted user text,
        // so it must not fall back to returning what it was given.
        const string junk = "this is a sentence the user typed into the address bar";
        Assert.Equal(UrlLog.Invalid, UrlLog.Host(junk));
        Assert.DoesNotContain("sentence", UrlLog.Host(junk), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Host_accepts_a_uri_overload()
    {
        Assert.Equal("example.org", UrlLog.Host(new Uri("https://example.org/path?q=1")));
        Assert.Equal(UrlLog.Empty, UrlLog.Host((Uri?)null));
        Assert.Equal(UrlLog.Invalid, UrlLog.Host(new Uri("/relative/path", UriKind.Relative)));
    }

    [Fact]
    public void Host_never_throws()
    {
        // A log call must not be able to take the app down.
        var nasty = new[]
        {
            "http://",
            "https://[::1",
            @"file://C:\Users\alice\Videos\clip.mp4",
            "\u0000\u0001\u0002",
            new string('x', 70000),
            "https://" + new string('a', 5000) + ".com/p",
        };
        foreach (var s in nasty)
        {
            var result = UrlLog.Host(s);
            Assert.NotNull(result);
        }
    }

    [Fact]
    public void Host_of_a_file_url_does_not_leak_the_local_path()
    {
        var host = UrlLog.Host("file:///C:/Users/alice/Videos/private-clip.mp4");
        Assert.DoesNotContain("alice", host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-clip", host, StringComparison.OrdinalIgnoreCase);
    }
}
