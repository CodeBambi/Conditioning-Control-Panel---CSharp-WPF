using System;
using System.IO;
using Xunit;
using static ConditioningControlPanel.Services.VideoSurfaceHealth;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Strict Lock vs the OS media keys (v6.9.1).
///
/// <para>Reported against v6.9.0: a mandatory video under Strict Lock ended early when the user
/// double-tapped the touchpad on a pair of Sony WH-1000XM6 headphones twice. Two OS media-key
/// pauses anywhere in the clip were enough, on every video, and changing the Panic Key made no
/// difference - because none of it went anywhere near the panic path.</para>
///
/// <para>The chain was: the focused WebView2 owns the OS media session, so Chromium pauses the
/// element itself; player.js counted unrequested pauses per LOAD with no decay and the SECOND one
/// reported ERR_PAUSE_LOOP (102); 102 correctly does not blame the file, so the engine raised a
/// plain failure; and VideoService's mid-clip policy ENDS a run rather than replaying it - which
/// released the lock. Three locks now sit in that chain, and all three are asserted here.</para>
///
/// <para>Only the WebView2 engine was ever affected. LibVLC is built with
/// <c>--no-keyboard-events</c>/<c>--no-mouse-events</c> and registers no OS media session at all,
/// so a media key has nothing to act on there.</para>
/// </summary>
public class StrictMediaKeyLockTests
{
    // ---- lock 3: a page failure under the lock must never end the run ----

    [Fact]
    public void StrictMidClipFailure_ThatDoesNotBlameTheFile_ReplaysInsteadOfEndingTheRun()
    {
        // This is error 102 exactly: mid-clip, session-level, file blameless. Ending here is what
        // handed the user a way out of a video they must not be able to stop.
        Assert.Equal(BrowserFailureAction.FallbackWholeClip,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: false, playbackStartedFired: true,
                strictLock: true, blamesFile: false));
    }

    [Fact]
    public void StrictMidClipFailure_ThatBlamesTheFile_StillEndsTheRun()
    {
        // A file that genuinely cannot decode any further must not trap the user against a clip
        // that can never finish, so the lock does not buy it a replay.
        Assert.Equal(BrowserFailureAction.EndClip,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: false, playbackStartedFired: true,
                strictLock: true, blamesFile: true));
    }

    [Fact]
    public void WithoutTheLock_MidClipFailure_KeepsTheOldEndBehaviour()
    {
        // Unchanged for every ordinary video: a replay would re-fire VideoStarted and make the user
        // rewatch the whole clip from 0 for a stall at the end.
        Assert.Equal(BrowserFailureAction.EndClip,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: false, playbackStartedFired: true,
                strictLock: false, blamesFile: false));
        Assert.Equal(BrowserFailureAction.EndClip,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: false, playbackStartedFired: true,
                strictLock: false, blamesFile: true));
    }

    [Fact]
    public void TheLockChangesNothingAboutTheOtherArms()
    {
        // Pre-first-frame already replayed; a mirror is still never allowed to end the run; and the
        // one-replay-per-trigger latch still holds, so the strict arm cannot loop.
        Assert.Equal(BrowserFailureAction.FallbackWholeClip,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: false, playbackStartedFired: false,
                strictLock: true, blamesFile: false));
        Assert.Equal(BrowserFailureAction.DropSecondary,
            DecideBrowserFailure(isPrimarySurface: false, alreadyFellBack: false, playbackStartedFired: true,
                strictLock: true, blamesFile: false));
        Assert.Equal(BrowserFailureAction.Ignore,
            DecideBrowserFailure(isPrimarySurface: true, alreadyFellBack: true, playbackStartedFired: true,
                strictLock: true, blamesFile: false));
    }

    // ---- locks 1 + 2: the page halves ----
    //
    // player.js is plain JS with no test home in this repo, so its half of the contract is asserted
    // against the product source the same way BrowserSinkLabelTests asserts the sink contract.

    [Fact]
    public void PlayerPage_ArmsTheMediaKeyGuardFromTheLoadMessage()
    {
        var js = ReadProduct("ConditioningControlPanel", "Resources", "web", "player", "player.js");

        // The flag, read exactly as C# writes it, and the guard hung off it.
        Assert.Contains("strict: !!d.strict", js);
        Assert.Contains("setMediaKeyGuard(cur.strict)", js);
        // Released on teardown, so a non-strict clip on the same page is not left locked.
        Assert.Contains("setMediaKeyGuard(false)", js);

        // Feature-detected, never assumed: an older WebView2 runtime must fall through to the pause
        // rule rather than throw on the load path.
        Assert.Contains("'mediaSession' in navigator", js);
        Assert.Contains("setActionHandler", js);

        // Every action a headset or keyboard can send. Each one is a separate key that would
        // otherwise reach the element; nexttrack/previoustrack matter because a double-tap on an
        // XM6 sends those rather than play/pause.
        foreach (var action in new[]
                 {
                     "'play'", "'pause'", "'stop'", "'seekbackward'", "'seekforward'", "'seekto'",
                     "'previoustrack'", "'nexttrack'",
                 })
            Assert.Contains(action, js);

        // The handler must be a no-op FUNCTION when armed. Registering null would hand the action
        // straight back to Chromium, which is the default this exists to suppress.
        Assert.Contains("want ? function () { } : null", js);
    }

    [Fact]
    public void PlayerPage_CountsUnrequestedPausesInARollingWindow()
    {
        var js = ReadProduct("ConditioningControlPanel", "Resources", "web", "player", "player.js");

        // The rule and its tunables.
        Assert.Contains("const PAUSE_WINDOW_MS = 5000;", js);
        Assert.Contains("const PAUSE_WINDOW_MAX = 6;", js);
        Assert.Contains("function pauseLoopDetected(stamps, now)", js);
        // Trimmed in place, so a two-hour clip cannot accumulate stamps.
        Assert.Contains("while (stamps.length && stamps[0] < cutoff) stamps.shift();", js);
        Assert.Contains("return stamps.length > PAUSE_WINDOW_MAX;", js);

        // The 1-strike rule is gone. This is the literal line that ended BambiBirdy's video.
        Assert.DoesNotContain("cur.unrequestedPauses === 1", js);

        // Resuming is still the default answer to an unrequested pause.
        Assert.Contains("if (!pauseLoopDetected(cur.pauseStamps, performance.now()))", js);
    }

    [Fact]
    public void Engine_PutsTheStrictFlagOnEveryLoad()
    {
        var cs = ReadProduct("ConditioningControlPanel", "Services", "Video", "Browser", "BrowserVideoEngine.cs");

        // The load post carries the field player.js reads. It is NOT primary-gated: a mirror's
        // WebView2 owns a media session too and is just as pausable.
        Assert.Contains("strict = req.Strict,", cs);
        Assert.Contains("public bool Strict { get; init; }", cs);
    }

    [Fact]
    public void VideoService_FeedsTheEngineTheSameFlagStrictHandlersGet()
    {
        var cs = ReadProduct("ConditioningControlPanel", "Services", "Video", "VideoService.Browser.cs");

        // The fixed session fact, not the live IsStrictActive probe (which blinks across a handoff).
        Assert.Contains("Strict = strict,", cs);

        // And blameFile has to reach the policy, or the mid-clip strict arm cannot tell 102 from a
        // file that will not decode.
        Assert.Contains("FallbackToLibVlc(reason, blameFile)", cs);
        Assert.Contains("strictLock: _browserStrict, blamesFile: blameFile", cs);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ReadProduct(params string[] parts)
    {
        var path = Path.Combine(RepoRoot(), Path.Combine(parts));
        Assert.True(File.Exists(path), $"product source missing: {path}");
        return File.ReadAllText(path);
    }
}
