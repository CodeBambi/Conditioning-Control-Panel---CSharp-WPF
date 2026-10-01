using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Quiz;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core <see cref="IntakeRun"/>: the Graded Intake rules both heads' hosts share
/// (WPF Services/Quiz/IntakeHostService.cs delegates to it).</summary>
[Collection(SessionStatics.Name)] // CoreProgression providers are process-global
public class IntakeRunTests
{
    private static QuizRunResult Run(double total = 90, double max = 100, double depth = 0.5, int mantras = 2, string niche = " Bambi ") =>
        new()
        {
            Niche = niche, TotalScore = total, MaxScore = max, PeakDepth = depth,
            AffirmedMantras = new List<string>(new string[mantras]),
        };

    [Fact]
    public void GradeUsesTheNinetyPercentBarAndTheTrimmedNiche()
    {
        Assert.Equal((90, true, "bambi"), IntakeRun.Grade(Run(total: 90)));
        Assert.Equal((90, false, "bambi"), IntakeRun.Grade(Run(total: 89.6)));
        Assert.Equal((0, false, IntakeRun.FallbackNiche), IntakeRun.Grade(Run(total: 0, max: 0, niche: " ")));
    }

    [Fact]
    public void XpAndMantraCreditAreCapped()
    {
        Assert.Equal(25 + 25 + 10, IntakeRun.Xp(Run(depth: 0.5, mantras: 2)));
        Assert.Equal(5, IntakeRun.MantraCredits(Run(mantras: 9)));
        Assert.Equal(100, IntakeRun.Xp(Run(depth: 7, mantras: 9)));
    }

    [Fact]
    public void OnlyAParsedResultLatchesAndAWalkOutIsReportedOnce()
    {
        var run = new IntakeRun();
        Assert.Null(run.AcceptResult(JObject.Parse("{\"type\":\"quiz-result\",\"result\":\"nope\"}")));
        Assert.False(run.ResultReceived);
        Assert.True(run.TakeWalkOut());
        Assert.False(run.TakeWalkOut());   // exit + intake-close for one quit

        var done = new IntakeRun();
        Assert.NotNull(done.AcceptResult(JObject.Parse("{\"type\":\"quiz-result\",\"result\":{\"niche\":\"drone\",\"totalScore\":5}}")));
        Assert.True(done.ResultReceived);
        Assert.False(done.TakeWalkOut());  // closing after a result is a wind-down
    }

    [Fact]
    public void HeartbeatGoesSilentAfterTwentySecondsUnlessExiting()
    {
        var run = new IntakeRun();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        run.Beat(t0);
        // WPF's watchdog recovered after >20 s of silence; both heads now read this one rule.
        Assert.False(run.IsHeartbeatSilent(t0.AddSeconds(20)));
        Assert.True(run.IsHeartbeatSilent(t0.AddSeconds(20).AddTicks(1)));
        run.Beat(t0.AddSeconds(15));
        Assert.False(run.IsHeartbeatSilent(t0.AddSeconds(35)));
        run.Exiting = true;
        Assert.False(run.IsHeartbeatSilent(t0.AddSeconds(60)));
    }

    [Fact]
    public void CompleteSpendsThePassAndDraftsWithoutOverwriting()
    {
        var dir = Directory.CreateTempSubdirectory("intake-run-").FullName;
        try
        {
            var spent = 0;
            (int, bool, bool, string)? signal = null;
            var (s1, p1) = IntakeRun.Complete(Run(), dir, () => spent++, (a, b, c, d) => signal = (a, b, c, d));
            var (s2, p2) = IntakeRun.Complete(Run(), dir, () => spent++, null);

            Assert.Equal(2, spent);
            Assert.Equal((90, true, true, "bambi"), signal);
            Assert.NotNull(s1);
            Assert.NotNull(s2);
            Assert.True(File.Exists(p1));
            Assert.True(File.Exists(p2));
            Assert.NotEqual(p1, p2);
            Assert.EndsWith("-2.session.json", p2);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ACrashedDraftStillSpendsThePassOnce()
    {
        var spent = 0;
        // An invalid folder makes the draft throw after the pass is spent: completed, charged once.
        var (session, path) = IntakeRun.Complete(Run(), "\0bad", () => spent++, null);
        Assert.Equal(1, spent);
        Assert.Null(session);
        Assert.Null(path);
    }

    [Fact]
    public void AFailedXpGrantSkipsTheMantraCredit()
    {
        var (xp, mantra) = (CoreProgression.AddXPProvider, CoreProgression.TrackMantraCompletedProvider);
        var credited = 0;
        try
        {
            CoreProgression.TrackMantraCompletedProvider = () => credited++;
            CoreProgression.AddXPProvider = (_, _) => { };
            IntakeRun.Complete(Run(mantras: 3), "\0bad", () => { }, null);
            Assert.Equal(3, credited);

            CoreProgression.AddXPProvider = (_, _) => throw new InvalidOperationException();
            IntakeRun.Complete(Run(mantras: 3), "\0bad", () => { }, null);
            Assert.Equal(3, credited);
        }
        finally { (CoreProgression.AddXPProvider, CoreProgression.TrackMantraCompletedProvider) = (xp, mantra); }
    }

    [Fact]
    public void MessagesCountOnlyFromTheLoadedDocument()
    {
        var page = new Uri("https://ccp.game/intake/index.html");
        Assert.True(IntakeRun.SameDocument(new Uri("https://ccp.game/intake/index.html?t=1#x"), page));
        Assert.False(IntakeRun.SameDocument(new Uri("https://ccp.game/dtrh/index.html"), page));
        Assert.False(IntakeRun.SameDocument(new Uri("https://evil.example/intake/index.html"), page));
        Assert.False(IntakeRun.SameDocument(new Uri("http://ccp.game/intake/index.html"), page));
    }

    [Fact]
    public void PngSignatureIsRequired()
    {
        Assert.True(IntakeRun.LooksLikePng(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }));
        Assert.False(IntakeRun.LooksLikePng(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0, 0, 0 }));
    }

    /// <summary>The Avalonia carrier (window.invokeCSharpAction) is only ever picked when WebView2's
    /// chrome.webview is absent, so the WPF head's page-&gt;host and host-&gt;page paths stay as they were.</summary>
    [Fact]
    public void WebShimKeepsWebView2FirstAndGatesTheAvaloniaCarrier()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel.sln"))) dir = dir.Parent;
        var shim = File.ReadAllText(Path.Combine(dir!.FullName, "Assets", "web", "intake", "web-shim.js"));

        Assert.Contains("if (webview) webview.postMessage(msg);", shim);
        Assert.Contains("webview.addEventListener('message', (e) => dispatchHostMessage(e.data));", shim);
        Assert.Matches(new Regex(@"!webview && win && typeof win\.invokeCSharpAction === 'function'"), shim);
        Assert.Single(Regex.Matches(shim, "invokeCSharpAction\\("));
    }
}
