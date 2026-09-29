using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// <see cref="ReleaseFeed"/>, the logic half of the WPF updater moved to Core. The feed fixture has
/// the shape of the real GitHub releases/latest response for v6.11.3 (no network). The helper
/// goldens were printed by the PRE-MOVE WPF <c>UpdateService.WriteUpdateHelperScript</c>, copied
/// verbatim into a throwaway console app, with the installer path replaced by "{INSTALLER}".
/// </summary>
public sealed class ReleaseFeedTests
{
    private static string Fixture(string name, [CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "Update", name);

    private static string Latest() => File.ReadAllText(Fixture("releases_latest_v6.11.3.json"));

    private const string Url = "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/download/v6.11.3/";

    private static string Assets(params (string Name, long Size)[] assets)
    {
        var sb = new StringBuilder("{\"tag_name\":\"v6.11.3\",\"draft\":false,\"prerelease\":false,\"assets\":[");
        for (var i = 0; i < assets.Length; i++)
            sb.Append(i == 0 ? "" : ",").Append($"{{\"name\":\"{assets[i].Name}\",\"size\":{assets[i].Size},\"browser_download_url\":\"{Url}{assets[i].Name}\"}}");
        return sb.Append("],\"body\":\"notes\"}").ToString();
    }

    [Fact]
    public void CapturedV6113ResponseParses()
    {
        var json = Latest();
        Assert.Equal("6.11.3", ReleaseFeed.ParseTagVersion(json));
        Assert.True(Version.TryParse(ReleaseFeed.ParseTagVersion(json), out var v));
        Assert.Equal(new Version(6, 11, 3), v);

        var (notes, size) = ReleaseFeed.ParseNotesAndSetupSize(json);
        Assert.StartsWith("v6.11.3 - Locktober", notes);
        Assert.Equal(412345678L, size);

        Assert.Equal(Url + "ConditioningControlPanel-6.11.3-Setup.exe",
            ReleaseFeed.FindSetupAssetUrl(json, "6.11.3", "v6.11.3"));
    }

    [Fact]
    public void TagWithoutVOrMissingTag()
    {
        Assert.Equal("6.11.3", ReleaseFeed.ParseTagVersion("{\"tag_name\": \"6.11.3\"}"));
        Assert.Null(ReleaseFeed.ParseTagVersion("{\"name\":\"v6.11.3\"}"));
    }

    [Fact]
    public void PrereleaseTagIsNotASystemVersion()
    {
        // releases/latest never returns a prerelease; if one is tagged like one it still can't
        // update anyone, because the head rejects anything System.Version won't parse.
        var tag = ReleaseFeed.ParseTagVersion("{\"tag_name\":\"v6.12.0-beta.1\",\"prerelease\":true}");
        Assert.Equal("6.12.0-beta.1", tag);
        Assert.False(Version.TryParse(tag, out _));
    }

    [Fact]
    public void DraftAndPrereleaseFlagsAreNotRead()
    {
        // The filter is GitHub's (releases/latest excludes drafts and prereleases). Flags alone
        // change nothing here; contract 1 in the oracle is what keeps the release "Latest".
        var draft = Latest().Replace("\"draft\": false", "\"draft\": true").Replace("\"prerelease\": false", "\"prerelease\": true");
        Assert.Contains("\"draft\": true", draft);
        Assert.Equal("6.11.3", ReleaseFeed.ParseTagVersion(draft));
        Assert.Equal(ReleaseFeed.FindSetupAssetUrl(Latest(), "6.11.3", "v6.11.3"),
            ReleaseFeed.FindSetupAssetUrl(draft, "6.11.3", "v6.11.3"));
    }

    [Fact]
    public void VersionedSetupWinsOverExtraAssetsInAnyOrder()
    {
        var json = Assets(("ccp-linux-x64.tar.gz", 5), ("Other-Installer.exe", 6),
            ("ConditioningControlPanel-6.11.3-Setup.exe", 7), ("ConditioningControlPanel-6.11.3.zip", 8));
        Assert.Equal(Url + "ConditioningControlPanel-6.11.3-Setup.exe", ReleaseFeed.FindSetupAssetUrl(json, "6.11.3", "v6.11.3"));
        Assert.Equal(7L, ReleaseFeed.ParseNotesAndSetupSize(json).SetupSize);
    }

    [Fact]
    public void FallbackOrderIsInstallerThenAnySetup()
    {
        var installer = Assets(("Foo-Setup.exe", 1), ("CCP-Installer.exe", 2));
        Assert.Equal(Url + "CCP-Installer.exe", ReleaseFeed.FindSetupAssetUrl(installer, "6.11.3", "v6.11.3"));
        Assert.Equal(Url + "Foo-Setup.exe", ReleaseFeed.FindSetupAssetUrl(Assets(("Foo-Setup.exe", 1)), "6.11.3", "v6.11.3"));
        Assert.Null(ReleaseFeed.FindSetupAssetUrl(Assets(("ccp.zip", 1)), "6.11.3", "v6.11.3"));
        Assert.Null(ReleaseFeed.ParseNotesAndSetupSize(Assets(("ccp.zip", 1))).SetupSize);
    }

    [Fact]
    public void SecondSetupAssetBreaksTheSizeAndTheLastResort()
    {
        // Why contract 2 says "no other asset ending in Setup.exe": the size comes from the FIRST
        // *Setup.exe, and the last-resort pattern also takes the first one in document order.
        var json = Assets(("ConditioningControlPanel-Avalonia-Setup.exe", 1), ("ConditioningControlPanel-6.11.3-Setup.exe", 2));
        Assert.Equal(1L, ReleaseFeed.ParseNotesAndSetupSize(json).SetupSize);
        Assert.Equal(Url + "ConditioningControlPanel-Avalonia-Setup.exe", ReleaseFeed.FindSetupAssetUrl(json, "6.12.0", "v6.12.0"));
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "ccp-releasefeed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void SkipMarkerRoundTrips()
    {
        var d = TempDir();
        try
        {
            Assert.Null(ReleaseFeed.GetSkippedUpdateVersion(d));
            Assert.Equal(DateTime.MinValue, ReleaseFeed.GetSkippedUpdateTime(d));
            ReleaseFeed.SetSkippedUpdateVersion(d, "  ");
            Assert.False(File.Exists(Path.Combine(d, "update_skip.txt")));
            ReleaseFeed.SetSkippedUpdateVersion(d, "6.11.4");
            Assert.Equal("6.11.4", File.ReadAllText(Path.Combine(d, "update_skip.txt")));
            Assert.Equal("6.11.4", ReleaseFeed.GetSkippedUpdateVersion(d));
            Assert.True(DateTime.Now - ReleaseFeed.GetSkippedUpdateTime(d) < TimeSpan.FromMinutes(1));
            ReleaseFeed.ClearSkippedUpdateVersion(d);
            Assert.Null(ReleaseFeed.GetSkippedUpdateVersion(d));
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void AttemptMarkerSuccessClearsEverything()
    {
        var d = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(d, "update_result.txt"), "stale");
            ReleaseFeed.SetPendingUpdateAttempt(d, "6.11.4");
            Assert.False(File.Exists(Path.Combine(d, "update_result.txt")));
            Assert.Equal("6.11.4", File.ReadAllText(Path.Combine(d, "update_attempt.txt")));
            File.WriteAllText(Path.Combine(d, "update_result.txt"), "0 \r\n");   // helper writes "%RC% "
            ReleaseFeed.SetSkippedUpdateVersion(d, "6.11.4");

            var o = ReleaseFeed.ConsumePendingUpdateOutcome(d, "6.11.4");
            Assert.NotNull(o);
            Assert.True(o!.Succeeded);
            Assert.Equal(0, o.ExitCode);
            Assert.Equal("6.11.4", o.Version);
            Assert.Empty(Directory.GetFiles(d));
            Assert.Null(ReleaseFeed.ConsumePendingUpdateOutcome(d, "6.11.4"));
        }
        finally { Directory.Delete(d, true); }
    }

    [Fact]
    public void AttemptMarkerFailureSkipsTheVersion()
    {
        var d = TempDir();
        try
        {
            ReleaseFeed.SetPendingUpdateAttempt(d, "6.11.4");
            var o = ReleaseFeed.ConsumePendingUpdateOutcome(d, "6.11.3");   // no result: unknown exit
            Assert.NotNull(o);
            Assert.False(o!.Succeeded);
            Assert.Equal(ReleaseFeed.UnknownExitCode, o.ExitCode);
            Assert.Equal("6.11.4", ReleaseFeed.GetSkippedUpdateVersion(d));
            Assert.False(File.Exists(ReleaseFeed.AttemptFilePath(d)));

            // Too old to report: still cleared and skipped, but not surfaced.
            ReleaseFeed.SetPendingUpdateAttempt(d, "6.11.5");
            File.WriteAllText(ReleaseFeed.AttemptResultFilePath(d), "1");
            File.SetLastWriteTime(ReleaseFeed.AttemptFilePath(d), DateTime.Now.AddDays(-8));
            Assert.Null(ReleaseFeed.ConsumePendingUpdateOutcome(d, "6.11.5"));
            Assert.Equal("6.11.5", ReleaseFeed.GetSkippedUpdateVersion(d));
            Assert.False(File.Exists(ReleaseFeed.AttemptResultFilePath(d)));
        }
        finally { Directory.Delete(d, true); }
    }

    [Theory]
    [InlineData("6.7.4", "6.7.4", 0, true)]
    [InlineData("6.7.4", "6.7.3", 0, false)]
    [InlineData("6.7.4", "6.7.4", 2, false)]
    [InlineData("6.7.4", "6.7.4", ReleaseFeed.UnknownExitCode, true)]
    [InlineData("6.7.4", "6.8.0", 0, true)]
    [InlineData("not-a-version", "6.7.3", 0, true)]
    [InlineData("not-a-version", "6.7.3", ReleaseFeed.UnknownExitCode, false)]
    public void DidUpdateSucceed(string attempted, string current, int exit, bool expected) =>
        Assert.Equal(expected, ReleaseFeed.DidUpdateSucceed(attempted, current, exit));

    [Theory]
    [InlineData("plain", @"C:\Program Files\Conditioning Control Panel", false)]
    [InlineData("elevated", @"C:\Program Files\Conditioning Control Panel", true)]
    [InlineData("nodir", null, false)]
    public void HelperScriptMatchesPreMoveBytes(string name, string? installDir, bool elevated)
    {
        var golden = File.ReadAllBytes(Fixture($"update_helper_{name}_premove.cmd.txt"));
        Assert.Contains("\r\n", Encoding.UTF8.GetString(golden));   // a checkout that ate the CRLFs fails loudly
        var script = ReleaseFeed.BuildUpdateHelperScript("{INSTALLER}", installDir, 4242,
            @"C:\Program Files\Conditioning Control Panel\ConditioningControlPanel.exe",
            @"C:\Users\u\AppData\Local\ConditioningControlPanel\logs\update-helper.log",
            @"C:\Users\u\AppData\Local\ConditioningControlPanel\update_result.txt", elevated);
        Assert.Equal(golden, new UTF8Encoding(false).GetBytes(script));
    }
}
