using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The <see cref="RealDesktopCollection"/> membership convention, made mechanical.
///
/// <para><b>WHAT THIS BINDS.</b> The desktop is machine-global, so a class that puts a window on
/// it or reads pixels off it must run inside the collection that holds the machine-wide lease.
/// Without this guard the convention is TEXT, and the next probe file to appear silently rejoins
/// the racy default collection — which is precisely how the overlay fixtures came to
/// contend with each other and with other processes in the first place. The symptom arrives as an
/// unrelated packet's land reddening a test it never touched.</para>
///
/// <para><b>WHY A SOURCE WALK.</b> <c>[Collection]</c> is reflectable, but "this class reaches the
/// real window manager" is a property of a method body, which reflection cannot see without
/// decoding IL. Same shape and same lineage as
/// <see cref="ProcessEnvCollectionGuardTests"/> / <c>VacuousShapeGuardTests</c> /
/// <c>TestTimingGuardTests</c>: repo-root walk, never skips, fails closed, file:line
/// violations.</para>
///
/// <para><b>THE ASSEMBLY BOUNDARY.</b> xunit collections do not span assemblies and
/// <see cref="RealDesktopCollection"/> is defined in <c>CcpClient.Tests</c>, so a class in
/// <c>CcpClient.HeadlessTests</c> CANNOT join it. Membership is therefore not an available remedy
/// over there and the only meaningful rule for that project is the stronger one — no real-desktop
/// probe at all — which fact 3 binds. Same asymmetry, and the same reason for it, as
/// <see cref="ProcessEnvCollectionGuardTests"/>.</para>
///
/// <para><b>HONESTY.</b> This is LEXICAL and binds at FILE granularity. Named blind spots:
/// (1) a file declaring two classes lends the attribute to both, and there IS one such file —
/// <c>RealDesktopLeaseTests.cs</c> declares <c>RealDesktopLeaseTests</c> (in the collection) beside
/// <c>RealDesktopLeasePrimitiveTests</c> (deliberately outside it, since it touches only a private
/// temp path). Neither reaches the desktop, so nothing is currently mis-bound, but a real-desktop
/// class added to a file that already carries the attribute would be accepted without ever joining
/// anything; (2) a class that reaches the desktop transitively through a helper this guard does not
/// name is invisible, so the helper census (fact 2) exists to make a NEW probe file fail loudly
/// rather than join silently; (3) tokens inside string literals count, which is why this file is
/// exempt from its own scan (the same self-exemption <c>TestTimingGuardTests</c> takes).</para>
///
/// <para><b>FACT 5 — THE PLATFORM KEY, AND WHY IT BELONGS BESIDE MEMBERSHIP.</b> A real-desktop
/// class that never names the machine is a class whose whole off-Windows column is meaningless,
/// and it fails in the WORSE of the two possible directions. Every probe in this project folds
/// <c>IsWindows()</c> into its own reading, so off Windows a handle is 0 and a comparison of two
/// absent windows is <c>0 == 0</c> — the fact does not go RED, it goes VACUOUSLY GREEN about a
/// window that was never created. Both remedies this suite already uses are accepted: SKIP on an
/// OS predicate (the idiom pinned by name in <c>floor.json</c>'s <c>allowedSkips</c>), or KEY the
/// expectation to a machine property the test establishes for itself. Fact 5 makes doing NEITHER
/// impossible to land. It binds at FILE granularity because that is the granularity the
/// convention actually lives at: these classes reach their run through a class-level alias
/// property, so a per-fact rule would have to resolve that alias and a per-file rule does not —
/// and a per-fact rule would additionally red the platform-independent facts (record-clause,
/// arithmetic and factory-selection facts) that correctly run on both.</para>
///
/// <para><b>FACT 7 — THE PER-FACT RULE FACT 5 SAID IT WAS NOT.</b> Fact 5's paragraph above names
/// the two things a per-fact rule must do before it is worth having, and fact 7 does both: it reads
/// the class level FIRST and turns any member declared from a real-desktop helper into an alias
/// token, so the six classes whose fact bodies say only <c>var run = Run;</c> are visible rather
/// than silently clean; and it looks at a fact only once that fact has been seen to name a machine
/// property, so the platform-independent facts — typed refusals, factory branches, arithmetic —
/// are never dragged in. What it then demands is that the machine question be a GATE
/// (<c>Assert.Skip*</c>) rather than a KEY, because a key disables the anti-vacuity control using
/// the very property the control exists to detect. Classes not yet converted are named in
/// <see cref="KeyedOnlyClasses"/>, which reds in both directions.</para>
/// </summary>
public class RealDesktopCollectionGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] UnitProjectParts = ["client", "tests", "CcpClient.Tests"];
    private static readonly string[] HeadlessProjectParts = ["client", "tests", "CcpClient.HeadlessTests"];

    private const string CollectionName = "RealDesktopCollection";
    private const string MembershipAttribute = "[Collection(nameof(RealDesktopCollection))]";

    /// <summary>The base whose constructor arms this thread's window floor. See RealDesktopFacts.</summary>
    private const string FloorBaseClause = ": RealDesktopFacts";

    /// <summary>This guard, and the collection's own declaration, hold the tokens as text.
    ///
    /// <para><c>UnboundedWaitGuardTests.cs</c> is here for the SAME reason and for no other: its
    /// pin table quotes a repo-relative path per pinned site, so the moment a real-desktop helper
    /// holds a pinned wait, that helper's NAME is a string literal in the guard's own table. It
    /// declares one class, that class is a source walk over <c>client/tests/**</c>, and it reaches
    /// no window manager — blind spot (3) of this file's own honesty paragraph, in the one other
    /// file in the project shaped like this one.</para></summary>
    private static readonly string[] ExemptFileNames =
    [
        "RealDesktopCollectionGuardTests.cs",
        "RealDesktopCollection.cs",
        "UnboundedWaitGuardTests.cs",
    ];

    /// <summary>
    /// The named helpers through which this project reaches the real desktop. A test class that
    /// mentions one of these outside a comment is putting a window on the user's screen or reading
    /// pixels off it.
    /// </summary>
    private static readonly string[] RealDesktopHelpers =
    [
        "OverlayWindowProbe",
        "OverlayObservations",
        "FlashPixelProbe",
        "FlashDrawObservations",
        "FlashEndToEndObservations",
        "TrayObservations",
        "TrayShellProbe",
        "Win32OverlayPresence",
        // An input-capturing window is the most contended thing this suite puts on the
        // desktop: it does not merely occupy a point in the z-order, it TAKES THE FOREGROUND and the
        // keyboard focus away from whatever else is running — including another CcpClient.Tests
        // process's own card, and including the scratch rigs the overlay and flash fixtures depend
        // on. Two of these running concurrently would fight over one machine-wide resource that only
        // one window can hold.
        "InputWindowProbe",
        "InputCaptureObservations",
        "Win32InputPresence",

        // A video surface is a layered topmost window that this process paints frame by
        // frame and then READS BACK, and the read-back leg the harness adds reads the composited
        // DESKTOP. Two of these running concurrently would contest the same points on the one
        // machine-global screen, and the desktop-capture control would be reading the other run.
        "VideoSurfaceObservations",
        "Win32VideoPresence",

        // The second consumer's own run uses ALL THREE of the above at once — an overlay, a
        // video surface carrying a picture this process painted, and a card that takes the
        // foreground after it — so it contends for everything the three lines above contend for,
        // and it must join the same collection for the same reasons.
        "BubbleCountObservations",

        // A pointer target is the most invasive thing this suite has put on the desktop yet
        // and it is invasive in a NEW way: the runs behind these helpers SYNTHESISE MOUSE CLICKS at
        // points on the one machine-global screen. Two of them running concurrently would not merely
        // contest a rectangle — one run's click would land in the other run's window, or in whatever
        // the other run had just moved out of the way.
        "PointerWindowProbe",
        "PointerSurfaceObservations",
        "Win32PointerSurface",

        // A per-pixel-alpha surface is a layered topmost window this process composites and
        // then reads back, and its central fact reads the COMPOSITED DESKTOP over a known
        // background. Two runs sharing the machine would each be reading the other's background,
        // and the run's own occlusion arbitration would name the peer's window as the intruder --
        // which is a true report of a contended desktop and a useless one for the fact.
        "GlyphWindowProbe",
        "GlyphSurfaceObservations",
        "Win32GlyphSurface",

        // The teardown run brings FIVE of those surfaces up at once — an overlay, a glyph surface, a
        // video surface carrying a picture, a pointer target that is deliberately not click-through,
        // and a card that takes the foreground and the keyboard — and then destroys them and asks
        // the window manager what is left. It contends for everything every line above contends for,
        // and it adds one of its own: its central reading is a walk of the WHOLE z-order filtered to
        // this process, so a second run's windows would be counted as this run's survivors.
        "SurfaceTeardownObservations",

        // The process-exit run reaches the desktop through a CHILD PROCESS rather than through this
        // one, and that is a reason to name it here rather than an exemption from doing so: the two
        // dangerous surfaces are on the one machine-global screen either way, it takes the
        // foreground, and its central readings are a z-order walk plus hit tests at fixed points. A
        // peer run's windows would be counted, or would win those points, exactly as they would for
        // every helper above.
        "SurfaceExitObservations",

        // The overlay input-invariant run contends for everything the pointer lines contend for and
        // then adds the two most exclusive resources on the machine: it SYNTHESISES KEYSTROKES into
        // the system input stream, and it takes the FOREGROUND twice — once for the window it types
        // into and once for the keeper that must still hold it after a handled click. A peer run's
        // card taking the foreground mid-pass would read here as a click leaking through an overlay,
        // which is a true report of a contended desktop and a useless one for the fact. Its
        // task-switcher half additionally walks the whole z-order filtered to this process, so a
        // peer's windows would be counted as ours.
        "OverlayDesktopInputObservations",
    ];

    /// <summary>
    /// The raw calls that create a top-level window, place a shell icon, or read the screen. A file
    /// carrying one of these IS a real-desktop helper whether or not it is named above — which is
    /// what stops the list from silently rotting.
    /// </summary>
    private static readonly string[] RealDesktopCalls =
    [
        "CreateWindowExW(",
        "Shell_NotifyIconW(",
        "TrackPopupMenu",
        "GetDC(0)",
    ];

    /// <summary>
    /// The ONE earned exemption: a message-only window (<c>HWND_MESSAGE</c> parent) is never on the
    /// desktop, never hit-tested and never in the z-order, so it cannot contend for anything. The
    /// exemption is pinned by file NAME as well as by the token, so a new file cannot quietly take
    /// it — taking it requires editing this list, which is the review friction.
    /// </summary>
    private static readonly string[] MessageOnlyExemptFiles = ["AiAwarenessTests.cs"];

    private const string MessageOnlyToken = "HwndMessage";

    /// <summary>
    /// The machine/OS properties a real-desktop class may key its expectation to (fact 5). Two
    /// spellings of one discipline: the OS predicate the skip idiom takes
    /// (<c>Assert.SkipUnless</c>), and the machine facts the probes establish for themselves and
    /// compare every window expectation against. Both are established BY THE TEST and never taken
    /// from the product, which is what makes either of them an honest key.
    /// </summary>
    private static readonly string[] MachineKeys =
    [
        "OperatingSystem.Is",
        "RuntimeInformation.IsOSPlatform",
        "WindowsHost",
        "MachineHasInteractiveDesktop",
    ];

    /// <summary>
    /// The ONE class that names a real-desktop helper and keys nothing, with its reason.
    /// <c>PopQuizCardPresentationTests</c> mentions <c>Win32InputPresence</c> only to read two
    /// compile-time colour constants out of it: it opens no window, reads no pixel and asks the
    /// operating system for nothing, and it is in the collection at all only because fact 1 is
    /// LEXICAL (its own remarks say exactly that). Copying the two hex strings in by hand to evade
    /// membership would break the product link its first fact exists to hold, so the serialisation
    /// is paid instead — and there is no machine reading here to key. Pinned by NAME so a new file
    /// cannot quietly take the exemption, and a file that LATER gains a key reds as stale.
    /// </summary>
    private static readonly string[] UnkeyedExemptFiles = ["PopQuizCardPresentationTests.cs"];

    /// <summary>
    /// <b>FACT 7's census: every class that still holds at least one KEYED-ONLY fact.</b>
    ///
    /// <para>A keyed-only fact reads a real-desktop run, compares its readings against a machine
    /// property (<see cref="MachineKeys"/>), and does NOT gate on one. Off Windows — and in a
    /// Windows session with no display — the probes answer all-zero, the comparisons become
    /// <c>0 == 0</c>, and the fact passes having measured NOTHING. That is the hazard fact 5's own
    /// remarks admit it does not close, at the finer grain it survives at.</para>
    ///
    /// <para><b>This list is a census, not an exemption.</b> Its job is to stop the shape RECURRING:
    /// a new real-desktop class arriving keyed-only reds until somebody either converts it or writes
    /// its name here, and a class whose last keyed-only fact is converted reds as STALE until its
    /// name comes off. Both directions, same as <see cref="UnkeyedExemptFiles"/>. It deliberately
    /// pins no per-class COUNT: a count would make every one of these files a shared chokepoint for
    /// unrelated edits, and the ratchet that matters is a class LEAVING the list.</para>
    ///
    /// <para><b>What a converted class looks like</b> is <c>OverlayTaskSwitcherTests</c>, which is
    /// absent below and is fact 7's positive control: it asks the machine question ONCE, as
    /// <c>Assert.SkipUnless</c>, and every reading after that gate is unconditional. Off the desktop
    /// it produces a NotExecuted result carrying its reason — a refusal the floor must be told about
    /// by name — instead of a green.</para>
    /// </summary>
    private static readonly string[] KeyedOnlyClasses =
    [
        "BubbleCountCapabilityTests.cs",
        "FlashDrawTests.cs",
        "GlyphAlphaDifferentialTests.cs",
        "GlyphCapabilityTests.cs",
        "GlyphCoexistenceTests.cs",
        "InputCapabilityTests.cs",
        "InputOverlayCoexistenceTests.cs",
        "OverlayCapabilityTests.cs",
        "OverlayDesktopInputTests.cs",
        "OverlayFrameSurfaceRetentionTests.cs",
        "OverlayTopmostRebuildObservations.cs",
        "PointerCapabilityTests.cs",
        "PointerCoexistenceTests.cs",
        "SurfaceExitTests.cs",
        "SurfaceTeardownTests.cs",
        "TrayCapabilityTests.cs",
        "VideoCapabilityTests.cs",
        "VideoInputRoutingTests.cs",
        "VideoOverlayCoexistenceTests.cs",
    ];

    /// <summary>
    /// The class fact 7 must see ONLY through an alias, and the alias it must resolve. Every fact in
    /// <c>BubbleCountCapabilityTests</c> reaches its run through <c>Run</c> and no fact body there
    /// names a real-desktop helper at all, so a scan that does not resolve class-level aliases finds
    /// ZERO real-desktop facts in it — and then reports it clean. That is the exact way a per-fact
    /// rule over this collection goes vacuous, so it is asserted rather than trusted.
    /// </summary>
    private const string AliasOnlyControlFile = "BubbleCountCapabilityTests.cs";

    private const string AliasOnlyControlAlias = "Run";

    /// <summary>The converted class: seen, and holding no keyed-only fact. Fact 7's other control.</summary>
    private const string ConvertedControlFile = "OverlayTaskSwitcherTests.cs";

    /// <summary>Broken-detector controls: these must always come out bound.</summary>
    private static readonly string[] BoundControls =
    [
        "OverlayCapabilityTests.cs",
        "FlashDrawTests.cs",
        "TrayCapabilityTests.cs",
        "InputCapabilityTests.cs",
        "InputOverlayCoexistenceTests.cs",
        "VideoCapabilityTests.cs",
        "VideoOverlayCoexistenceTests.cs",
        "PointerCapabilityTests.cs",
        "PointerCoexistenceTests.cs",
        "GlyphCapabilityTests.cs",
        "GlyphAlphaDifferentialTests.cs",
        "GlyphCoexistenceTests.cs",
        "OverlayDesktopInputTests.cs",
        "OverlayTaskSwitcherTests.cs",
    ];

    [Fact]
    public void EveryTestClassThatTouchesTheRealDesktop_RunsInsideTheRealDesktopCollection()
    {
        var files = UnitProjectSources();
        var violations = new List<string>();
        var bound = new List<string>();
        var exemptionsTaken = new List<string>();

        foreach (var (name, raw) in files)
        {
            if (ExemptFileNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var code = StripComments(raw);
            var reasons = RealDesktopHelpers.Where(h => code.Contains(h, StringComparison.Ordinal))
                .Concat(RealDesktopCalls.Where(c => code.Contains(c, StringComparison.Ordinal)))
                .ToArray();
            var declaresTests = code.Contains("[Fact]", StringComparison.Ordinal)
                || code.Contains("[Theory]", StringComparison.Ordinal);

            var messageOnly = code.Contains(MessageOnlyToken, StringComparison.Ordinal);
            if (messageOnly)
            {
                exemptionsTaken.Add(name);
            }

            var isBound = reasons.Length > 0 && declaresTests && !messageOnly;
            if (!isBound)
            {
                continue;
            }

            bound.Add(name);
            if (!code.Contains(MembershipAttribute, StringComparison.Ordinal))
            {
                violations.Add($"CcpClient.Tests/{name}: declares tests and reaches the real desktop "
                    + $"[{string.Join("; ", reasons)}] but does not carry {MembershipAttribute}. The interactive "
                    + "desktop is MACHINE-global: this class contends with every other real-desktop class in the "
                    + "process AND with every other CcpClient.Tests process on the machine, measured "
                    + "as 8 red in 12 concurrent floor runs. The fix is membership, never a skip, never a retry, "
                    + "and never an allowedSkips entry.");
            }
        }

        var unexpectedExemptions = exemptionsTaken
            .Where(f => !MessageOnlyExemptFiles.Contains(f, StringComparer.Ordinal)).ToArray();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.True(unexpectedExemptions.Length == 0,
            $"file(s) taking the message-only window exemption without being pinned for it: "
            + $"{string.Join(", ", unexpectedExemptions)}. A HWND_MESSAGE parent is the ONLY thing that makes a "
            + "window invisible to the desktop; if the file really is message-only, pin its name here so the "
            + "exemption stays reviewed rather than inferred.");
        Assert.Equal(BoundControls.OrderBy(n => n, StringComparer.Ordinal),
            bound.Where(BoundControls.Contains).OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>
    /// Membership in the collection and having a WINDOW FLOOR are the same thing, mechanically.
    ///
    /// <para>The floor is <see cref="ThreadStaticAttribute"/> because the revocation's trigger is
    /// thread-scoped, so it has to be armed on the thread that RUNS the fact —
    /// <see cref="RealDesktopFacts"/>'s constructor, which xunit calls on exactly that thread. It
    /// used to be armed at two pointer call sites instead, and a fact that reached a band assertion
    /// through any other probe was floored only by luck of the thread pool: adding fourteen
    /// unrelated facts elsewhere in the assembly turned a green run into three reds. Convention
    /// cannot hold that, so this makes it fail closed.</para>
    /// </summary>
    [Fact]
    public void EveryClassInTheCollection_DerivesFromTheBaseThatPutsTheWindowFloorUp()
    {
        var files = UnitProjectSources();
        var members = new List<string>();
        var violations = new List<string>();

        foreach (var (name, raw) in files)
        {
            if (ExemptFileNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var code = StripComments(raw);
            if (!code.Contains(MembershipAttribute, StringComparison.Ordinal))
            {
                continue;
            }

            members.Add(name);
            if (!code.Contains(FloorBaseClause, StringComparison.Ordinal))
            {
                violations.Add($"CcpClient.Tests/{name}: carries {MembershipAttribute} but does not declare "
                    + $"'{FloorBaseClause}'. Every fact in this collection reaches the OS through the top-most "
                    + "band, and a thread that reaches zero top-level windows after one of them was clicked "
                    + "costs the WHOLE PROCESS that band with SetWindowPos returning TRUE and applying nothing. "
                    + "The floor is thread-scoped, so it must be armed on the thread that runs the fact, which "
                    + "is what the base constructor does. Deriving is the fix — never a retry, never a skip.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));

        // The detector's own control. A walk that found NO members would satisfy the assertion above
        // for the worst possible reason, and this guard has already been wrong about how many
        // classes are in the collection once today.
        Assert.True(members.Count >= BoundControls.Length,
            $"the membership walk found {members.Count} file(s) carrying {MembershipAttribute}, fewer than the "
            + $"{BoundControls.Length} broken-detector controls that must always be in it. The scan is not "
            + "reading the collection at all, so the check above proves nothing");
    }

    /// <summary>
    /// <b>FACT 5.</b> Every class that reaches the real desktop names the machine somewhere — an OS
    /// predicate it skips on, or a machine property it compares its expectations against.
    ///
    /// <para>This is the guard the first Linux run of this port needed and did not have. That run
    /// left roughly 66 facts red for one reason: they measured a Win32 mechanism and were written
    /// where that mechanism is always present. Every one of those was fixed by hand, and nothing
    /// stopped the next one — five new real-desktop classes landed the day after. They all followed
    /// the convention, which is what makes it worth binding rather than rewriting.</para>
    ///
    /// <para><b>What this does NOT claim.</b> It cannot tell a well-keyed expectation from a badly
    /// keyed one, and it does not run on Linux to find out. A class that keys ONE control fact and
    /// leaves its invariants unconditional still passes here while those invariants read all-zero
    /// off Windows — that is the shape most of this collection is in, it is named in the classes
    /// themselves, and closing it is a per-fact question this file deliberately does not answer.
    /// What fact 5 removes is the class that keys NOTHING, whose entire off-Windows column is
    /// either red noise or vacuous green with no reading behind it either way.</para>
    /// </summary>
    [Fact]
    public void EveryRealDesktopClass_KeysItsExpectationToTheMachine_OrOffWindowsItPassesVacuously()
    {
        var files = UnitProjectSources();
        var keyed = new List<string>();
        var violations = new List<string>();
        var staleExemptions = new List<string>();

        foreach (var (name, raw) in files)
        {
            if (ExemptFileNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var code = StripComments(raw);
            if (!code.Contains(MembershipAttribute, StringComparison.Ordinal))
            {
                continue;
            }

            var reasons = RealDesktopHelpers.Where(h => code.Contains(h, StringComparison.Ordinal)).ToArray();
            if (reasons.Length == 0)
            {
                // In the collection for serialisation alone (the lease's own facts, the preflight):
                // no probe, so no reading that could go zero, so nothing to key.
                continue;
            }

            var exempt = UnkeyedExemptFiles.Contains(name, StringComparer.Ordinal);
            if (MachineKeys.Any(k => code.Contains(k, StringComparison.Ordinal)))
            {
                keyed.Add(name);
                if (exempt)
                {
                    staleExemptions.Add($"CcpClient.Tests/{name}: is pinned in UnkeyedExemptFiles as a class with no "
                        + "machine reading to key, but it now names one. Either the exemption's reason is no longer "
                        + "true and the pin must go, or the key is accidental — an exemption nobody re-reads is a hole "
                        + "with a comment.");
                }

                continue;
            }

            if (exempt)
            {
                continue;
            }

            violations.Add($"CcpClient.Tests/{name}: reaches the real desktop [{string.Join("; ", reasons)}] but "
                + $"names no machine or OS property ({string.Join(", ", MachineKeys)}). Every probe in this project "
                + "folds the Windows check into its own reading, so off Windows this class's handles are all 0 and "
                + "its comparisons are 0 == 0: it does not go RED, it goes VACUOUSLY GREEN about windows that were "
                + "never created. Gate each fact that drives a real Win32 rig on an OS predicate (Assert.SkipUnless, "
                + "pinned by name in floor.json's allowedSkips with the machine class where it executes), or key the "
                + "expectation to the machine fact the probe already establishes. Never neither, and never a weakened "
                + "assertion.");
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.True(staleExemptions.Count == 0, string.Join(Environment.NewLine, staleExemptions));

        // The detector's own control, and the non-vacuity proof: a walk that keyed NOTHING would
        // satisfy both assertions above for the worst possible reason. Every broken-detector control
        // drives a real Win32 rig, so every one of them must come out KEYED.
        Assert.Equal(BoundControls.OrderBy(n => n, StringComparer.Ordinal),
            keyed.Where(BoundControls.Contains).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void TheRealDesktopHelperCensus_IsClosed_SoANewProbeCannotJoinTheSuiteUnnoticed()
    {
        var files = UnitProjectSources();
        var strays = new List<string>();

        foreach (var (name, raw) in files)
        {
            if (ExemptFileNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var code = StripComments(raw);
            var calls = RealDesktopCalls.Where(c => code.Contains(c, StringComparison.Ordinal)).ToArray();
            var named = RealDesktopHelpers.Any(h => name.StartsWith(h, StringComparison.Ordinal));
            var messageOnly = code.Contains(MessageOnlyToken, StringComparison.Ordinal);
            var declaresTests = code.Contains("[Fact]", StringComparison.Ordinal)
                || code.Contains("[Theory]", StringComparison.Ordinal);

            var stray = calls.Length > 0 && !named && !messageOnly && !declaresTests;
            if (stray)
            {
                strays.Add($"CcpClient.Tests/{name}: creates a real top-level window or reads the screen "
                    + $"[{string.Join("; ", calls)}] but is neither one of the named real-desktop helpers "
                    + $"({string.Join(", ", RealDesktopHelpers)}) nor a test class that can carry "
                    + $"{MembershipAttribute}. A helper nobody can put inside {CollectionName} is a window on the "
                    + "user's desktop that no lease covers — name it in RealDesktopHelpers, or give the class the "
                    + "attribute.");
            }
        }

        Assert.True(strays.Count == 0, string.Join(Environment.NewLine, strays));
        Assert.NotEmpty(files); // an empty walk is a broken detector, not a clean tree
    }

    [Fact]
    public void TheHeadlessProject_CarriesNoRealDesktopProbeAtAll_BecauseItCannotJoinTheCollection()
    {
        // The asymmetry, and why it is the STRONGER rule over there: xunit collections do not span
        // assemblies, so nothing in CcpClient.HeadlessTests can hold the machine-wide lease. A real
        // window opened from that project would be on the user's desktop with nothing serializing it
        // against the unit project's fixtures OR against another process — the exact defect that was
        // measured, with no remedy available short of moving the fact.
        var files = ProjectSources(HeadlessProjectParts);
        var strays = new List<string>();

        foreach (var (name, raw) in files)
        {
            var code = StripComments(raw);
            var calls = RealDesktopCalls.Where(c => code.Contains(c, StringComparison.Ordinal)).ToArray();
            var helpers = RealDesktopHelpers.Where(h => code.Contains(h, StringComparison.Ordinal)).ToArray();
            var messageOnly = code.Contains(MessageOnlyToken, StringComparison.Ordinal);
            if (calls.Length + helpers.Length > 0 && !messageOnly)
            {
                strays.Add($"CcpClient.HeadlessTests/{name}: reaches the real desktop "
                    + $"[{string.Join("; ", calls.Concat(helpers))}]. Collections do not span assemblies, so this "
                    + $"class cannot join {CollectionName} and NOTHING can serialize it — not against the unit "
                    + "project's real-desktop fixtures and not against another CcpClient.Tests process. Move the "
                    + "fact into CcpClient.Tests and give it the attribute, or make it a headless fact that opens "
                    + "no window. Never leave it here unguarded.");
            }
        }

        Assert.True(strays.Count == 0, string.Join(Environment.NewLine, strays));
        Assert.NotEmpty(files); // an empty walk is a broken detector, not a clean tree
    }

    /// <summary>
    /// <b>FACT 7.</b> A class in this collection either GATES its real-desktop facts on the machine,
    /// or its name is in the census of classes that still key them instead.
    ///
    /// <para><b>Why keying is not a gate.</b> Fact 5 asks whether a class names the machine at all,
    /// and its own remarks say what it cannot then ask: a class that keys ONE control fact and leaves
    /// the rest unconditional passes it while every reading goes to zero off Windows. The finer case
    /// is worse than that, because the KEY ITSELF disables the control —
    /// <c>Assert.True(run.Control.Visible == run.MachineHasInteractiveDesktop)</c> is satisfied by a
    /// window that was never created, and the invariant it exists to protect then reads
    /// <c>0 == 0</c> about three more of them. An anti-vacuity control that can be switched off by
    /// the same property it is controlling for is not a control.</para>
    ///
    /// <para><b>Refusal versus a blocked suite, which is the whole reason this admits a skip.</b> The
    /// remedy this fact demands is <c>Assert.Skip*</c> on a machine property: a NotExecuted result
    /// carrying its reason, which <c>check-floor.mjs:240-251</c> then REFUSES unless the name is
    /// pinned in <c>allowedSkips</c> under the machine/OS admission rule. So the off-platform column
    /// stops being a pass and becomes something a human had to admit in writing. What this fact
    /// deliberately does NOT demand is an off-platform assertion FAILURE. That would red the whole
    /// class on Linux, which is the bring-up fact 5 describes — roughly 66 facts red at once — and a
    /// suite that cannot go green on a platform stops being read on that platform.</para>
    ///
    /// <para><b>Two blind spots, named.</b> (1) It is LEXICAL and per-FILE, like every other fact
    /// here. (2) A fact that skips on <c>OperatingSystem.IsWindows()</c> and then keys on
    /// <c>MachineHasInteractiveDesktop</c> counts as gated, though a Windows session with no display
    /// still reads it vacuously; that is strictly narrower than the hazard this closes, and closing
    /// it would need the two predicates compared rather than counted.</para>
    /// </summary>
    [Fact]
    public void EveryRealDesktopFact_GatesOnTheMachine_OrItsClassIsInTheKeyedOnlyCensus()
    {
        var files = UnitProjectSources();
        var keyedOnly = new List<string>();
        var seen = new List<string>();
        var violations = new List<string>();
        var stale = new List<string>();
        var aliasControlFacts = 0;
        var aliasControlFactsNamingAHelperDirectly = 0;
        var convertedControlFacts = 0;

        foreach (var (name, raw) in files)
        {
            if (ExemptFileNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            var code = StripComments(raw);
            if (!code.Contains(MembershipAttribute, StringComparison.Ordinal))
            {
                continue;
            }

            var facts = FactBodies(code);
            var aliases = RunAliases(code, facts);
            var isAliasControl = string.Equals(name, AliasOnlyControlFile, StringComparison.Ordinal);
            var offenders = new List<string>();
            var reachesTheDesktop = false;

            foreach (var fact in facts)
            {
                var namesHelper = RealDesktopHelpers.Any(h => fact.Body.Contains(h, StringComparison.Ordinal));
                var namesAlias = aliases.Any(a => NamesToken(fact.Body, a));
                if (!namesHelper && !namesAlias)
                {
                    continue;
                }

                reachesTheDesktop = true;
                if (isAliasControl)
                {
                    aliasControlFacts++;
                    if (namesHelper)
                    {
                        aliasControlFactsNamingAHelperDirectly++;
                    }
                }

                if (string.Equals(name, ConvertedControlFile, StringComparison.Ordinal))
                {
                    convertedControlFacts++;
                }

                // A fact with no machine reading at all is PLATFORM-INDEPENDENT — a typed refusal,
                // a factory's platform branch, a constant. Those must never be dragged into this
                // rule: a guard that fires on correct code is worse than no guard.
                if (!MachineKeys.Any(k => fact.Body.Contains(k, StringComparison.Ordinal)))
                {
                    continue;
                }

                if (fact.Body.Contains("Assert.Skip", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add(fact.Name);
            }

            if (!reachesTheDesktop)
            {
                continue;
            }

            seen.Add(name);
            var censused = KeyedOnlyClasses.Contains(name, StringComparer.Ordinal);
            if (offenders.Count > 0 && !censused)
            {
                keyedOnly.Add(name);
                violations.Add($"CcpClient.Tests/{name}: {offenders.Count} fact(s) compare a real-desktop reading "
                    + $"against a machine property and gate on none — {string.Join(", ", offenders)}. Off Windows, "
                    + "and in a Windows session with no display, every probe in this project answers all-zero, so "
                    + "each of those comparisons becomes 0 == 0 and the fact PASSES having measured nothing. Ask "
                    + "the machine question ONCE as Assert.SkipUnless and make every reading after it "
                    + "unconditional (OverlayTaskSwitcherTests is the worked example), pinning the skipped names in "
                    + "floor.json's allowedSkips in the same commit. Or, if that conversion is not this packet's "
                    + "work, add this file to KeyedOnlyClasses — which is an admission in writing that the class's "
                    + "off-Windows column proves nothing, never a fix.");
            }
            else if (offenders.Count > 0)
            {
                keyedOnly.Add(name);
            }
            else if (censused)
            {
                stale.Add($"CcpClient.Tests/{name}: is in KeyedOnlyClasses but now gates every real-desktop fact it "
                    + "has. The census is a list of work outstanding, and a name left on it after the work is done "
                    + "is how the next reader concludes there is more of this shape than there is. Take it off.");
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        Assert.True(stale.Count == 0, string.Join(Environment.NewLine, stale));

        // ------------------------------------------------------------------ the detector's controls
        //
        // CONTROL A — ALIAS RESOLUTION. Every fact in the alias-control class reaches its run through
        // a class-level property and NONE of them names a helper directly, so a scan that skipped
        // alias resolution would find zero real-desktop facts here and call the class clean. Both
        // halves are asserted: that the facts were seen, and that they could ONLY have been seen
        // through the alias.
        Assert.True(aliasControlFacts > 0,
            $"fact 7 found NO real-desktop fact in {AliasOnlyControlFile}, which reaches its entire run through the "
            + $"class-level alias '{AliasOnlyControlAlias}'. The alias resolution is broken, so every class shaped "
            + "like that one is being reported clean without being read — which is this guard going vacuous about "
            + "vacuity.");
        Assert.True(aliasControlFactsNamingAHelperDirectly == 0,
            $"{aliasControlFactsNamingAHelperDirectly} fact(s) in {AliasOnlyControlFile} now name a real-desktop "
            + "helper directly, so that file no longer proves the alias resolution runs. Point "
            + "AliasOnlyControlFile at a class that still reaches its run only through a class-level member.");

        // CONTROL B — THE CONVERTED CLASS. It must be SEEN (so the scan really reads it) and must be
        // absent from the census (so 'no violations' is not merely 'everything is exempt').
        Assert.True(convertedControlFacts > 0,
            $"fact 7 found no real-desktop fact in {ConvertedControlFile}, the converted worked example. With that "
            + "class invisible, an empty violation list says nothing about whether the converted shape is even "
            + "detectable.");
        Assert.DoesNotContain(ConvertedControlFile, KeyedOnlyClasses);
        Assert.DoesNotContain(ConvertedControlFile, keyedOnly);

        // CONTROL C — the census is neither empty nor the whole world. Empty would mean the scan
        // classifies nothing; equal to `seen` would mean nothing in the collection is gated at all
        // and the rule has no worked example left in the tree.
        Assert.Equal(KeyedOnlyClasses.OrderBy(n => n, StringComparer.Ordinal), keyedOnly.OrderBy(n => n, StringComparer.Ordinal));
        Assert.True(seen.Count > keyedOnly.Count,
            $"every one of the {seen.Count} real-desktop class(es) fact 7 can see is keyed-only. There is no gated "
            + "class left for the rule to be measured against.");
    }

    /// <summary>One <c>[Fact]</c>/<c>[Theory]</c> method: its name and its body text.</summary>
    private readonly record struct FactMethod(string Name, string Body, int Start, int End);

    /// <summary>
    /// Every fact body in a comment-stripped file. Fails closed: an attribute this cannot resolve to
    /// a body throws rather than being skipped, because a fact the scanner cannot parse is exactly
    /// the one somebody would hide a keyed control in (same discipline as
    /// <c>VacuousShapeDetector.cs:88-94</c>).
    /// </summary>
    private static IReadOnlyList<FactMethod> FactBodies(string code)
    {
        var facts = new List<FactMethod>();
        foreach (Match attribute in Regex.Matches(code, @"\[(?:Fact|Theory)\b"))
        {
            var declaration = Regex.Match(
                code[attribute.Index..],
                @"(?:public|internal|private|protected)\s+(?:static\s+)?(?:async\s+)?(?:void|Task|ValueTask)\b[\w<>\[\]\?,\. ]*?(\w+)\s*\(");
            Assert.True(declaration.Success,
                $"a [Fact]/[Theory] at offset {attribute.Index} resolves to no method declaration — the real-desktop "
                + "gating guard refuses to go blind on a fact it cannot parse");

            var i = attribute.Index + declaration.Index + declaration.Length;
            for (var depth = 1; i < code.Length && depth > 0; i++)
            {
                if (code[i] == '(')
                {
                    depth++;
                }
                else if (code[i] == ')')
                {
                    depth--;
                }
            }

            while (i < code.Length && char.IsWhiteSpace(code[i]))
            {
                i++;
            }

            Assert.True(i < code.Length,
                $"a [Fact]/[Theory] at offset {attribute.Index} has no body — the real-desktop gating guard refuses "
                + "to go blind on a fact it cannot parse");

            var start = i;
            if (code[i] == '{')
            {
                for (var depth = 0; i < code.Length; i++)
                {
                    if (code[i] == '{')
                    {
                        depth++;
                    }
                    else if (code[i] == '}' && --depth == 0)
                    {
                        break;
                    }
                }
            }
            else
            {
                while (i < code.Length && code[i] != ';')
                {
                    i++;
                }
            }

            facts.Add(new FactMethod(declaration.Groups[1].Value, code[start..Math.Min(i, code.Length)], start, i));
        }

        return facts;
    }

    /// <summary>
    /// <b>The class-level alias, which is how a per-fact rule over this collection goes vacuous.</b>
    /// Six classes here reach their whole run through one static member —
    /// <c>private static BubbleCountObservations.PaintedRun Run => BubbleCountObservations.Painted;</c>
    /// — and their fact bodies then say only <c>var run = Run;</c>. A scan that looks for helper
    /// names inside fact bodies finds NOTHING in those files and reports them clean. So the class
    /// level (everything outside a fact body) is read first, and any member declared from a
    /// real-desktop helper becomes a token that means "this fact reads the run".
    /// </summary>
    private static IReadOnlyList<string> RunAliases(string code, IReadOnlyList<FactMethod> facts)
    {
        var classLevel = new StringBuilder();
        var cursor = 0;
        foreach (var fact in facts.OrderBy(f => f.Start))
        {
            classLevel.Append(code[cursor..fact.Start]);
            cursor = Math.Min(fact.End, code.Length);
        }

        classLevel.Append(code[cursor..]);

        var aliases = new List<string>();
        foreach (var line in classLevel.ToString().Split('\n'))
        {
            if (!RealDesktopHelpers.Any(h => line.Contains(h, StringComparison.Ordinal)))
            {
                continue;
            }

            var declared = Regex.Match(line, @"\b(\w+)\s*(?:=>|=)");
            if (declared.Success && !aliases.Contains(declared.Groups[1].Value, StringComparer.Ordinal))
            {
                aliases.Add(declared.Groups[1].Value);
            }
        }

        return aliases;
    }

    /// <summary>Whole-token containment, so the alias <c>Run</c> never matches <c>RunTaskSwitcher</c>.</summary>
    private static bool NamesToken(string body, string token) =>
        Regex.IsMatch(body, $@"\b{Regex.Escape(token)}\b");

    private static IReadOnlyList<(string Name, string Text)> UnitProjectSources() =>
        ProjectSources(UnitProjectParts);

    private static IReadOnlyList<(string Name, string Text)> ProjectSources(string[] projectParts)
    {
        var root = Path.Combine([FindRepoRoot(), .. projectParts]);
        Assert.True(Directory.Exists(root),
            $"{string.Join('/', projectParts)} not found at {root} — the real-desktop membership guard "
            + "refuses to skip");

        return [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal)
                && !f.Replace('\\', '/').Contains("/bin/", StringComparison.Ordinal))
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))
            .OrderBy(f => f.Item1, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Removes line and block comments, leaving string literals alone. Doc comments are where every
    /// false positive in this tree lives: three test files mention <c>Win32OverlayPresence</c> or
    /// <c>FlashDrawObservations</c> only inside a <c>///</c> reference.
    ///
    /// <para>Every literal form in this suite is consumed WHOLE, closing delimiter included. That is
    /// not fussiness: a scanner that stops ON the closing quote re-enters string mode at it and then
    /// swallows everything up to the NEXT quote, which desynchronises the rest of the file and hides
    /// real <c>//</c> comments behind it. That bug was live in the first draft of this guard and it
    /// showed up as two false positives, so the raw-string form (<c>"""</c>, 30+ files here) is
    /// handled explicitly rather than left to luck.</para>
    /// </summary>
    private static string StripComments(string text)
    {
        var output = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                {
                    i++;
                }

                i = Math.Min(text.Length, i + 2);
                continue;
            }

            // Raw string literal: N quotes open it, the same N close it, and nothing inside escapes.
            var rawOpen = QuoteRunLength(text, i);
            if (rawOpen >= 3)
            {
                var end = FindRawStringEnd(text, i + rawOpen, rawOpen);
                output.Append(text[i..end]);
                i = end;
                continue;
            }

            if (c == '@' && i + 1 < text.Length && text[i + 1] == '"')
            {
                output.Append('@').Append('"');
                i += 2;
                while (i < text.Length)
                {
                    if (text[i] == '"' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        output.Append('"').Append('"');
                        i += 2;
                        continue;
                    }

                    output.Append(text[i]);
                    var closed = text[i] == '"';
                    i++;
                    if (closed)
                    {
                        break;
                    }
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                var quote = c;
                output.Append(c);
                i++;
                while (i < text.Length && text[i] != '\n')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        output.Append(text[i]).Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    output.Append(text[i]);
                    var closed = text[i] == quote;
                    i++;
                    if (closed)
                    {
                        break;
                    }
                }

                continue;
            }

            output.Append(c);
            i++;
        }

        return output.ToString();
    }

    /// <summary>How many consecutive <c>"</c> start at <paramref name="index"/>.</summary>
    private static int QuoteRunLength(string text, int index)
    {
        var run = 0;
        while (index + run < text.Length && text[index + run] == '"')
        {
            run++;
        }

        return run;
    }

    /// <summary>The offset just past a raw string's closing run of <paramref name="fence"/> quotes.</summary>
    private static int FindRawStringEnd(string text, int from, int fence)
    {
        var i = from;
        while (i < text.Length)
        {
            if (text[i] != '"')
            {
                i++;
                continue;
            }

            var run = QuoteRunLength(text, i);
            if (run >= fence)
            {
                return i + run;
            }

            i += run;
        }

        return text.Length;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine([directory.FullName, .. RepoAnchorParts])))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new Xunit.Sdk.XunitException(
            $"repo root not found above {AppContext.BaseDirectory} (anchor: {string.Join('/', RepoAnchorParts)}) — "
            + "the real-desktop membership guard refuses to skip");
    }
}
