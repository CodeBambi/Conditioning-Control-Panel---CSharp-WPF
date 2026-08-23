using CcpClient.Desktop.Features.Goon;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The guard for a claim that goes FALSE without anybody editing it.
///
/// <para><b>The defect class.</b> <c>port-lessons.md</c> wave 62: <i>"A frozen record is not a stale
/// sentence; a present-tense claim is. Rot is a property of TENSE, not of age."</i> A record may say
/// <i>"at this land the count was thirteen"</i> forever. A string that tells a user <b>"this build
/// cannot X"</b> goes false the moment another lane makes the build able to X — and nothing in this
/// repository detected that. It happened twice: one packet built the WebView2 deny hook and left
/// <c>GoonDoors.cs</c> telling the user the browser can still ask; another wired the haptic limb and
/// left <c>HapticsPanelNotices.cs</c> telling the user no module sends anything. In both cases the
/// XML docs beside the code were maintained (<c>HapticLimb.cs:139-140</c>,
/// <c>IHapticSink.cs:288</c>) and the sentence the USER reads was not.</para>
///
/// <para><b>What this file is: a claim-to-code BINDING, not a prose matcher.</b> Each entry pairs a
/// sentence read <b>from the running product</b> with <b>one machine-evaluated fact about this
/// repository</b>, and asserts the two agree. It never greps prose looking for "a capability claim".
/// A matcher over the 403 claim-shaped literals in
/// <c>client/src/CcpClient.Desktop/**</c> would assert a reach it cannot enforce — the packet's own
/// trap, and wave 62's <i>"the fix is never a cleverer matcher"</i>. A phrase-anchored enrolment
/// list was considered and rejected for a harder reason: <b>it would have caught neither defect</b>,
/// because nobody edited either sentence. The code moved.</para>
///
/// <para><b>THE THREAT MODEL, STATED SO THE REACH CANNOT BE OVERREAD.</b> This guard detects
/// <b>DRIFT</b>: the code moves and the sentence does not. That is the defect that happened twice
/// above, it is the only thing the red-watch demonstrates, and it is all this file claims.
/// <b>It does NOT detect a deliberate REWRITE of a sentence into a false one, and it cannot.</b>
/// These are token-presence needles, and token presence cannot encode ATTRIBUTION — "answers it
/// DENY" is a substring of "no code path answers it DENY". An adversarial pass over the corrected
/// text found several false-but-passing variants. Chasing them with more needles is the matcher
/// this port has already ruled out (<c>port-lessons.md</c> wave 62: <i>"the fix is never a cleverer
/// matcher"</i>), so the bound is written here instead of being narrowed and re-overclaimed.</para>
///
/// <para><b>WHAT THIS GUARD CANNOT SEE — five blind spots, named rather than implied.</b>
/// <list type="number">
/// <item><b>It cannot discover a fourth claim.</b> Its reach is exactly the THREE bindings in
/// <see cref="Bindings"/> — <c>goon-voice-prompt</c>, <c>haptic-absence-line</c>,
/// <c>haptic-armed-arm</c> — and <see cref="EveryClaimBinding_IsRegisteredAndAgreesWithTheCode"/>
/// pins that count so the registry cannot shrink silently.</item>
/// <item><b>It cannot check a claim whose falsifier is not a fact about this repository's
/// source.</b> Anything needing a real browser, a Linux session, a headed prompt, a device or a
/// server is outside it — which includes the entire residual gate the Goon sentence names.</item>
/// <item><b>Two of its three code facts are syntactic token presence</b>
/// (<see cref="DenyAttachNeedle"/>, <see cref="LimbNeedle"/>). A call site that exists but is dead,
/// or one that is renamed, moves the fact without moving the truth.</item>
/// <item><b>It does not check polarity or attribution</b> — see the threat model above. The
/// per-platform, per-clause polarity pin lives in <c>GoonPracticeHeadlessTests</c>, NOT here:
/// <see cref="VoiceVerdict"/> is unordered, unscoped <c>Contains</c> and passes on an inverted
/// sentence.</item>
/// <item><b>It checks the sentence, not the pixels.</b> A rail that stopped rendering
/// <c>Missing</c> at all would leave every fact here green;
/// <c>GoonPracticeHeadlessTests:194</c> is what carries the rendering.</item>
/// </list></para>
///
/// <para><b>Non-vacuity, claimed at exactly its real strength.</b> An earlier revision re-ran each
/// verdict with the code fact FLIPPED and called that a non-vacuity proof. It is not: for all three
/// verdicts the false arm is <c>s.Contains(FalsifiedX)</c> while the true arm conjoins
/// <c>!s.Contains(FalsifiedX)</c>, so the flip is <b>logically entailed</b> by the assertion made
/// one line above it and can never red on its own — a verdict whose false arm were hard-coded
/// <c>false</c> would still pass. It was removed rather than dressed up. What genuinely holds is
/// the registry-size pin and the per-binding agreement in
/// <see cref="EveryClaimBinding_IsRegisteredAndAgreesWithTheCode"/>, the non-empty pin ahead of
/// each walk, and — for the half that a boolean cannot cover —
/// <see cref="TheLimbSiteExtraction_CountsRealFilesAndExcludesGeneratedBytes"/>, which pins the
/// extractor against a temp tree of known content (the <c>HapticSiteCensusTests:16-26</c>
/// pattern).</para>
///
/// <para><b>Shape note.</b> Every needle lives in a class-level constant and every file walk lives in
/// a helper, so no <c>[Fact]</c> body carries a platform, environment or filesystem predicate.
/// <c>client/tests/floor/**</c> — and therefore <c>vacuous-shape-ledger.json</c> — is closed to this
/// packet, so a silencing shape here could not have been dispositioned (the D295 trap).</para>
/// </summary>
public sealed class UserFacingClaimTests
{
    // ---------- the needles (class-level: never inside a [Fact] body) ----------

    /// <summary>The Goon practice host's attach of the deny hook (<c>GoonHostWindow.axaml.cs:393</c>).</summary>
    private const string DenyAttachNeedle = "WebViewPermissionDeny.TryAttach(wv2.CoreWebView2";

    /// <summary>The non-Windows arm of the hook (<c>WebViewPermissionDeny.cs:115-121</c>).</summary>
    private const string WindowsOnlyGuardNeedle = "if (!OperatingSystem.IsWindows())";

    /// <summary>The typed outcome that arm returns.</summary>
    private const string UnsupportedPlatformNeedle = "\"unsupported-platform\"";

    /// <summary>The haptic limb, as held by an effect module (D210).</summary>
    private const string LimbNeedle = "IHapticLimb";

    private const string PracticeHostFile = "client/src/CcpClient.Desktop/Features/Goon/GoonHostWindow.axaml.cs";
    private const string DenyHookFile = "client/src/CcpClient.Desktop/Features/Dtrh/WebViewPermissionDeny.cs";
    private const string EffectsDirectory = "client/src/CcpClient.Desktop/Effects";

    /// <summary>The clause the deny hook falsified. Its RETURN would mean the correction was reverted.</summary>
    private const string FalsifiedVoiceClause = "so the browser can still ask you. Closing that needs a";

    /// <summary>The clause the haptic limb falsified, in the absence line.</summary>
    private const string FalsifiedHapticClause = "no effect in this build sends";

    /// <summary>
    /// The absence line's SECOND falsified clause, banned by name for the same reason as the first.
    ///
    /// <para>When the limb was wired, the sentence stopped blaming the modules and started blaming
    /// the BUILD: <i>"no provider route is admitted at all — so the thing to fix is the missing
    /// route"</i>. Admitting both of upstream's providers falsified that in turn, and a user reading
    /// it would have been sent to wait for a release while the repair — tick a box, start your
    /// server — was on the screen in front of them. Two clauses, one defect, and the pattern is
    /// why both are pinned rather than only the current one.</para>
    /// </summary>
    private const string FalsifiedAdmissionClause = "no provider route is admitted";

    /// <summary>The clause the haptic limb falsified, in the Armed arm.</summary>
    private const string FalsifiedArmedClause = "nothing sends anything to it yet";

    /// <summary>This packet's OWN first replacement for the Armed arm, banned by name. It was false in
    /// the only world where that arm renders — the dot cannot be lit unless a route has been
    /// admitted. A packet about sentences that go false may not leave its own behind.</summary>
    private const string SelfRefutingArmedClause = "the sink admits no provider route";

    // The two sentences the packet requires to SURVIVE. Trading one false sentence for another is
    // the failure mode this pins against: the crossing is the real reason, and the tightening is
    // deliberately not a guarantee.
    private const string CrossingClause = "The CROSSING is what is missing: there is no peer to send one to.";
    private const string TighteningClause = "a TIGHTENING over upstream, not a guarantee";
    private const string NoCaptureDeviceClause = "The host opens no capture device";

    // ---------- binding 1: the Goon voice refusal versus the deny hook ----------

    /// <summary>
    /// The sentence must say what the hook DOES, on both platforms.
    ///
    /// <para>Windows: <c>GoonHostWindow.axaml.cs:393</c> attaches, and
    /// <c>WebViewPermissionDeny.cs:262-278</c> writes <c>DenyState</c> through <c>put_State</c> for
    /// every kind but autoplay — microphone is kind 1. Linux: <c>WebViewPermissionDeny.cs:115-121</c>
    /// returns <c>unsupported-platform</c>, so the browser still owns the question there.</para>
    /// </summary>
    [Fact]
    public void TheVoiceNoteRefusal_SaysWhatTheDenyHookActuallyDoes()
    {
        var practiceHost = ReadRepoFile(PracticeHostFile);
        Assert.Contains(DenyAttachNeedle, practiceHost, StringComparison.Ordinal);

        var denyHook = ReadRepoFile(DenyHookFile);
        Assert.Contains(WindowsOnlyGuardNeedle, denyHook, StringComparison.Ordinal);
        Assert.Contains(UnsupportedPlatformNeedle, denyHook, StringComparison.Ordinal);

        var sentence = VoiceNoteMissingText();
        Assert.True(
            VoiceVerdict(windowsDenies: true, sentence),
            "The Goon practice host attaches the PermissionRequested deny hook and the hook refuses "
            + "to install off Windows, so the VoiceNotes refusal must state that split. It reads:\n"
            + sentence);
    }

    /// <summary>
    /// The two TRUE sentences either side of the corrected one, pinned so a later edit cannot trade
    /// one false sentence for another. The crossing (no peer) is the actual reason a voice note
    /// cannot be sent; the no-capture-device line is a tightening over upstream and says so.
    /// </summary>
    [Fact]
    public void TheVoiceNoteRefusal_KeepsTheCrossingAndTheTightening()
    {
        var sentence = VoiceNoteMissingText();
        Assert.Contains(CrossingClause, sentence, StringComparison.Ordinal);
        Assert.Contains(NoCaptureDeviceClause, sentence, StringComparison.Ordinal);
        Assert.Contains(TighteningClause, sentence, StringComparison.Ordinal);
        Assert.DoesNotContain(FalsifiedVoiceClause, sentence, StringComparison.Ordinal);
    }

    // ---------- binding 2: the haptics absence line versus the limb ----------

    /// <summary>
    /// The absence line must name where the send really stops.
    ///
    /// <para>Six live sites fire the limb (<c>FlashSurfacePresenter.cs:314</c>,
    /// <c>MandatoryVideoEffect.cs:307</c>, <c>:340</c>, <c>:419</c>,
    /// <c>SubliminalsEffect.cs:223</c>, <c>BouncingTextField.cs:237</c>), each incrementing
    /// <c>HapticLimb.Moments</c> and posting a real envelope. What stops delivery is no longer
    /// inside this program at all: <c>HapticSinkFactory.AdmittedRoutes</c> carries BOTH upstream
    /// routes, so <c>HapticLimb.Send</c> reaches <c>EvaluationsWithNoDevice++</c>
    /// (<c>HapticLimb.cs:566-570</c>) only while no observation has named a device — which needs a
    /// ticked provider box and a running server. The line must say THAT, and must carry neither of
    /// the two clauses this file has already watched go false.</para>
    /// </summary>
    [Fact]
    public void TheHapticAbsenceLine_SaysWhereTheSendReallyStops()
    {
        var limbSites = CountFilesContaining(RepoPath(EffectsDirectory), LimbNeedle);
        Assert.True(limbSites > 0,
            "no effect module holds an IHapticLimb — if the haptic limb was reverted, the absence line must go "
            + "back to saying the modules are silent");

        var absence = HapticAbsenceText();
        Assert.True(
            HapticAbsenceVerdict(modulesCommandTheLimb: true, absence),
            $"{limbSites} effect file(s) command the haptic limb, so the haptics panel must not tell "
            + "the user no module sends anything. It reads:\n" + absence);

        // The Armed arm is checked on its OWN text, never joined to the line above: a needle over
        // the two together is satisfied by the absence line while this arm says anything at all.
        var armed = HapticArmedArmText();
        Assert.True(
            HapticArmedArmVerdict(modulesCommandTheLimb: true, armed),
            "the Armed arm renders only where a provider route HAS been admitted, so it may not "
            + "claim that none is, and may not claim nothing has been sent. It reads:\n" + armed);
    }

    // ---------- the registry itself ----------

    /// <summary>
    /// The registry is COMPLETE and every entry agrees with the code as it stands.
    /// 
    /// <para><b>This fact used to assert one thing more, and that assertion was a lie of
    /// strength.</b> It re-ran each verdict with the code fact FLIPPED and required the result to
    /// be false, calling that a non-vacuity proof. For all three verdicts the false arm IS
    /// <c>s.Contains(FalsifiedX)</c> and the true arm CONJOINS <c>!s.Contains(FalsifiedX)</c>, so
    /// the flipped assertion merely negates a conjunct asserted true on the line above: it is
    /// logically entailed, it can never red alone, and a verdict whose false arm were hard-coded
    /// <c>false</c> would have passed it. The red-watch confirms this — the failure that fired was
    /// always the TRUE-arm message, never the inverted one. It is removed rather than kept for the
    /// look of coverage.</para>
    ///
    /// <para>What remains is real and load-bearing: the <b>registry-size pin</b>, which is what
    /// stops a binding being dropped silently (this packet had to grow the registry from two to
    /// three mid-review, and the pin is what forces that to be a visible edit), and the
    /// <b>per-binding agreement</b> with the shipped sentence.</para>
    /// </summary>
    [Fact]
    public void EveryClaimBinding_IsRegisteredAndAgreesWithTheCode()
    {
        var bindings = Bindings();
        Assert.Equal(3, bindings.Count);
        Assert.Equal(
            new[] { "goon-voice-prompt", "haptic-absence-line", "haptic-armed-arm" },
            bindings.Select(b => b.Id).ToArray());

        foreach (var binding in bindings)
        {
            Assert.True(binding.Verdict(true, binding.Sentence),
                $"{binding.Id}: the shipped sentence disagrees with the code as it stands");
        }
    }

    /// <summary>
    /// The extraction itself, pinned against a tree of known content.
    ///
    /// <para>No verdict assertion can see the walk that PRODUCES its boolean degrading to
    /// constant-true — that is a property of the extractor, not of the verdict. So this
    /// fact builds three files — one carrying the needle, one not, and one carrying it inside
    /// <c>obj/</c> — and requires exactly ONE. A walk that stopped excluding generated bytes returns
    /// two; a walk that stopped reading content returns three.</para>
    /// </summary>
    [Fact]
    public void TheLimbSiteExtraction_CountsRealFilesAndExcludesGeneratedBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ccp-sp137-" + Guid.NewGuid().ToString("N"));
        WriteFixtureFile(root, "Effects/Commanding.cs", "class C { private readonly IHapticLimb? _h; }");
        WriteFixtureFile(root, "Effects/Silent.cs", "class S { /* draws only */ }");
        WriteFixtureFile(root, "obj/Debug/Generated.cs", "class G { IHapticLimb generated; }");

        var counted = CountFilesContaining(root, LimbNeedle);
        DeleteTree(root);

        Assert.Equal(1, counted);
    }

    // ---------- the registry ----------

    private sealed record ClaimBinding(string Id, string Sentence, Func<bool, string, bool> Verdict);

    private static IReadOnlyList<ClaimBinding> Bindings() =>
    [
        new ClaimBinding("goon-voice-prompt", VoiceNoteMissingText(), VoiceVerdict),
        new ClaimBinding("haptic-absence-line", HapticAbsenceText(), HapticAbsenceVerdict),
        new ClaimBinding("haptic-armed-arm", HapticArmedArmText(), HapticArmedArmVerdict),
    ];

    /// <summary>
    /// Does the VoiceNotes sentence agree with the code? With the hook in place it must state the
    /// platform split and must NOT carry the clause the deny hook falsified; without it, the old clause is
    /// the honest one.
    /// </summary>
    private static bool VoiceVerdict(bool windowsDenies, string sentence) =>
        windowsDenies
            ? sentence.Contains("ON WINDOWS", StringComparison.Ordinal)
                && sentence.Contains("answers it DENY", StringComparison.Ordinal)
                && sentence.Contains("ON LINUX", StringComparison.Ordinal)
                && sentence.Contains("the browser still owns the question and can still ask you",
                    StringComparison.Ordinal)
                && !sentence.Contains(FalsifiedVoiceClause, StringComparison.Ordinal)
            : sentence.Contains(FalsifiedVoiceClause, StringComparison.Ordinal);

    /// <summary>
    /// The absence line, which renders TODAY. With the limb commanded it must name the modules as
    /// commanding it and the missing provider route as the stop, and must not blame the modules.
    /// </summary>
    private static bool HapticAbsenceVerdict(bool modulesCommandTheLimb, string sentence) =>
        modulesCommandTheLimb
            ? sentence.Contains("command the haptic limb now", StringComparison.Ordinal)
                && sentence.Contains("tick a provider above", StringComparison.Ordinal)
                && !sentence.Contains(FalsifiedHapticClause, StringComparison.Ordinal)
                && !sentence.Contains(FalsifiedAdmissionClause, StringComparison.Ordinal)
            : sentence.Contains(FalsifiedHapticClause, StringComparison.Ordinal);

    /// <summary>
    /// The Armed arm, which renders in ONE world only: the one where a provider route HAS been
    /// admitted (<c>HapticParticipant.cs:257-258</c> → <c>IHapticSink.cs:196</c> →
    /// <c>HapticParticipant.cs:306-335</c>). Bound SEPARATELY from the absence line because a
    /// needle over the two joined would let the absence line satisfy it while this arm said
    /// anything at all — which is how this packet's own first attempt shipped a sentence that is FALSE
    /// on the only day it renders. Both self-refuting wordings are banned by name.
    /// </summary>
    private static bool HapticArmedArmVerdict(bool modulesCommandTheLimb, string sentence) =>
        modulesCommandTheLimb
            ? sentence.Contains("reports REACH, not traffic", StringComparison.Ordinal)
                && sentence.Contains("entitlement gate", StringComparison.Ordinal)
                && !sentence.Contains(FalsifiedArmedClause, StringComparison.Ordinal)
                && !sentence.Contains(SelfRefutingArmedClause, StringComparison.Ordinal)
            : sentence.Contains(FalsifiedArmedClause, StringComparison.Ordinal);

    // ---------- product sentences, read from the running product ----------

    private static string VoiceNoteMissingText() =>
        GoonDoors.For(GoonDoors.Refused, GoonDoors.GoonDoor.VoiceNotes)!.Missing;

    /// <summary>The absence line, which every user of this panel reads today.</summary>
    private static string HapticAbsenceText() => HapticsPanelNotices.DescribeAbsences();

    /// <summary>The Armed arm ALONE — never joined to the absence line. It was unreachable while
    /// <c>AdmittedRoutes</c> was empty, which is precisely why it needed its OWN binding: it went live
    /// the day the routes were admitted, and a sentence that is only NEGATIVELY constrained can be
    /// false on exactly that day while a needle shared with the absence line stays green. It is now
    /// reachable by any user who ticks a provider and runs its server.</summary>
    private static string HapticArmedArmText() =>
        HapticsPanelNotices.DescribeLiveState(EffectDotState.Armed, enabled: true, reachable: true);

    // ---------- the walk (helpers: never inside a [Fact] body) ----------

    /// <summary>Files under <paramref name="root"/> carrying <paramref name="needle"/>, over the
    /// The universe rule: every <c>.cs</c> at any depth, excluding any <c>bin</c> or <c>obj</c>
    /// path segment.</summary>
    private static int CountFilesContaining(string root, string needle) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .Count(path => File.ReadAllText(path).Contains(needle, StringComparison.Ordinal));

    private static bool IsGeneratedPath(string path) =>
        path.Replace('\\', '/').Split('/').Any(segment => segment is "bin" or "obj");

    private static string ReadRepoFile(string relative) => File.ReadAllText(RepoPath(relative));

    private static string RepoPath(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The checkout root, anchored on <c>client/CcpClient.sln</c> — present in every
    /// checkout and in a worktree. Not found is a hard FAILURE, never a skip.</summary>
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "client", "CcpClient.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "client/CcpClient.sln not found above " + AppContext.BaseDirectory
            + " — the user-facing claim guard refuses to skip");
    }

    private static void WriteFixtureFile(string root, string relative, string content)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static void DeleteTree(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // a temp directory that outlives the run is not a test failure
        }
    }
}
