using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Quiz;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Graded Intake speech bridge (T2 Ashley, 2026-08-28): say-it items never advanced on the
/// desktop because the page asked the browser for <c>SpeechRecognition</c>, which errors inside
/// WebView2. The fix routes the beat through the app's offline Vosk engine over a small
/// host&lt;-&gt;page protocol, and hardens BOTH ears so any error drops to typed input.
///
/// <para>Two halves are pinned here. <see cref="IntakeSpeechPolicy"/> is pure and tested directly.
/// The wire contract is textual across three files that cannot be loaded off a real app (the host
/// service is static over <c>App.*</c>; the page is an ES module with a DOM), so it is asserted
/// against the source — the same idiom as <c>Phase8RedirectContractTests</c> /
/// <c>ArcademyDoorOpenTests</c>. A renamed message type or reason string on one side is a
/// feature that silently never fires on the other.</para>
/// </summary>
public class IntakeSpeechBridgeTests
{
    // ---- source helpers ----

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadAsset(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Assets" }.Concat(parts).ToArray()));

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string Host() => SourceRoots.ReadProductFile("Services", "Quiz", "IntakeHostService.cs");
    private static string HostSpeech() => SourceRoots.ReadProductFile("Services", "Quiz", "IntakeHostService.Speech.cs");
    private static string Shim() => ReadAsset("web", "intake", "web-shim.js");
    private static string Beats() => ReadAsset("web", "intake", "render", "beats.js");
    private static string Boot() => ReadAsset("web", "intake", "boot.js");

    // ---- IntakeSpeechPolicy: availability reasons, in the app's reporting order ----

    [Fact]
    public void Unavailability_ConsentTrumpsEverything()
    {
        // Even with no mic and no model, the user's own "no" is the reason we report.
        Assert.Equal(IntakeSpeechPolicy.ReasonConsent,
            IntakeSpeechPolicy.Unavailability(consentGiven: false, hasCaptureDevice: false, SpeechModelStatus.NoModelFound, micHeldElsewhere: true));
    }

    [Fact]
    public void Unavailability_HardwareBeforeModel()
    {
        Assert.Equal(IntakeSpeechPolicy.ReasonNoMic,
            IntakeSpeechPolicy.Unavailability(true, hasCaptureDevice: false, SpeechModelStatus.LoadFailed, micHeldElsewhere: true));
    }

    [Theory]
    [InlineData(SpeechModelStatus.NoModelFound, IntakeSpeechPolicy.ReasonNoModel)]
    [InlineData(SpeechModelStatus.LoadFailed, IntakeSpeechPolicy.ReasonModelFailed)]
    [InlineData(SpeechModelStatus.NotProbed, IntakeSpeechPolicy.ReasonNoModel)]
    public void Unavailability_ModelStatesReadDifferently(SpeechModelStatus status, string expected)
    {
        // "no model" and "the model you installed refused to load" must not collapse into one line -
        // the second sends users off to download a model they already have.
        Assert.Equal(expected, IntakeSpeechPolicy.Unavailability(true, true, status, micHeldElsewhere: false));
    }

    [Fact]
    public void Unavailability_BusyIsReportedNotFought()
    {
        Assert.Equal(IntakeSpeechPolicy.ReasonBusy,
            IntakeSpeechPolicy.Unavailability(true, true, SpeechModelStatus.Ok, micHeldElsewhere: true));
    }

    [Fact]
    public void Unavailability_NullWhenEverythingIsThere()
    {
        Assert.Null(IntakeSpeechPolicy.Unavailability(true, true, SpeechModelStatus.Ok, micHeldElsewhere: false));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, 0, false)]
    [InlineData(3, 0, true)]    // three empty windows: stop re-opening the mic on our own
    [InlineData(0, 5, false)]
    [InlineData(0, 6, true)]    // six wrong utterances: backstop
    public void ShouldGoIdle_CapsSilenceAndMisses(int silent, int misses, bool expected)
    {
        Assert.Equal(expected, IntakeSpeechPolicy.ShouldGoIdle(silent, misses));
    }

    // ---- wire contract: every message type exists on both sides ----

    [Fact]
    public void Host_RoutesBothPageMessages()
    {
        var host = Host();
        Assert.Contains("case \"speech-start\":", host);
        Assert.Contains("case \"speech-stop\":", host);
        // The class had to become partial for the bridge file to attach.
        Assert.Matches(new Regex(@"internal\s+static\s+partial\s+class\s+IntakeHostService"), host);
    }

    [Fact]
    public void Host_AdvertisesTheBridgeOnInit()
    {
        // `speech = BuildSpeechCaps()` rides init.config; the page feature-detects `bridge:true`.
        Assert.Contains("speech = BuildSpeechCaps()", Host());
        Assert.Contains("bridge = true", HostSpeech());
    }

    [Fact]
    public void Host_PostsEveryEventKindThePageHandles()
    {
        var hostSpeech = HostSpeech();
        var beats = Beats();
        foreach (var kind in new[] { "listening", "partial", "final", "silence", "idle", "unavailable", "stopped" })
        {
            Assert.Contains($"case '{kind}':", beats);
            // Every kind the page handles is one the host can actually send (stopped rides StopSpeechBridge).
            Assert.Contains($"\"{kind}\"", hostSpeech);
        }
        Assert.Contains("[\"type\"] = \"speech-event\"", hostSpeech);
        Assert.Contains("on('speech-event'", Shim());
    }

    [Fact]
    public void Host_ClosesTheMicWhenTheWindowGoes()
    {
        // DisposeAll is the single teardown funnel; a closed window must never leave Vosk capturing.
        var host = Host();
        var dispose = host.Substring(host.IndexOf("private static void DisposeAll()", StringComparison.Ordinal));
        Assert.Contains("StopSpeechBridge(", dispose);
    }

    [Fact]
    public void Host_ReasonStringsMatchThePageNotes()
    {
        // The page prints a per-reason note when it drops to typed input; a reason the page does not
        // know falls to the generic line, which is legal but wrong for the user in front of it.
        var beats = Beats();
        foreach (var reason in new[]
                 {
                     IntakeSpeechPolicy.ReasonConsent, IntakeSpeechPolicy.ReasonNoMic, IntakeSpeechPolicy.ReasonNoModel,
                     IntakeSpeechPolicy.ReasonModelFailed, IntakeSpeechPolicy.ReasonBusy, IntakeSpeechPolicy.ReasonError,
                 })
        {
            Assert.Contains($"'{reason}':", beats);
        }
    }

    [Fact]
    public void Shim_ExportsHostSpeechAndSendsBothMessages()
    {
        var shim = Shim();
        Assert.Contains("export const hostSpeech", shim);
        Assert.Contains("type: 'speech-start'", shim);
        Assert.Contains("type: 'speech-stop'", shim);
        // Feature-detect is the host's explicit flag, never "hosted" (the RN host is hosted too).
        Assert.Contains("c.speech.bridge === true", shim);
    }

    [Fact]
    public void Boot_HandsTheBridgeToBeats()
    {
        Assert.Contains("speech: shim.hostSpeech", Boot());
        Assert.Contains("micEnabled, speech }", Beats());
    }

    [Fact]
    public void Beats_FeatureDetectsTheBridgeBeforeTheBrowserEar()
    {
        var beats = Beats();
        var mantra = beats.Substring(beats.IndexOf("function renderMantra()", StringComparison.Ordinal));
        var hostEar = mantra.IndexOf("if (speechBridge) { renderHostEar(); return; }", StringComparison.Ordinal);
        var browserProbe = mantra.IndexOf("window.SpeechRecognition || window.webkitSpeechRecognition", StringComparison.Ordinal);
        Assert.True(hostEar >= 0, "the host ear must be feature-detected");
        Assert.True(browserProbe > hostEar, "the browser SpeechRecognition probe must come AFTER the bridge check");
    }

    [Fact]
    public void Beats_BrowserEarNeverLoopsOnAHardError()
    {
        // The original bug: rec.onerror printed "didn't catch that" for a 'network' error and left the
        // user with no way forward but skip. Now only the two soft errors are allowed to retry, and
        // even those are capped.
        var beats = Beats();
        var browser = beats.Substring(beats.IndexOf("function renderBrowserEar()", StringComparison.Ordinal));
        var onerror = browser.Substring(browser.IndexOf("rec.onerror", StringComparison.Ordinal));
        onerror = onerror.Substring(0, onerror.IndexOf("rec.onend", StringComparison.Ordinal));
        Assert.Contains("err === 'no-speech' || err === 'aborted'", onerror);
        Assert.Contains("softErrors >= 3", onerror);
        Assert.Equal(2, Regex.Matches(onerror, @"dropToTyped\(").Count);   // soft cap + hard error
        Assert.DoesNotContain("status.textContent = 'didn’t catch that';", onerror);
    }

    [Fact]
    public void Beats_HostEarAutoStartsAndStopsOnCleanup()
    {
        var beats = Beats();
        var host = beats.Substring(beats.IndexOf("function renderHostEar()", StringComparison.Ordinal));
        host = host.Substring(0, host.IndexOf("// =================== BROWSER EAR", StringComparison.Ordinal));
        // Auto-open on mount (the way spoken mantras behave), the last statement of the ear.
        Assert.Matches(new Regex(@"start\(\);\s*\}\s*$"), host.TrimEnd());
        // Unmount closes the host session so the mic never outlives the card.
        Assert.Contains("cleanups.push(() => {", host);
        Assert.Contains("try { stop(); } catch (_e) {}", host);
        // A stale session's events are dropped, not applied to the wrong beat.
        Assert.Contains("m.id !== sid", host);
    }

    [Fact]
    public void Shim_KeepsTheWebsiteSyncPatchTarget()
    {
        // cclabs-web/scripts/sync-intake.mjs asserts EXACTLY ONE occurrence of this literal when it
        // vendors the intake; a second one (or none) fails the sync loudly.
        var shim = Shim();
        Assert.Equal(1, Regex.Matches(shim, Regex.Escape("location.href = 'about:blank'")).Count);
    }
}
