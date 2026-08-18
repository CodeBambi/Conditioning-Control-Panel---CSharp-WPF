using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-107: the <see cref="RealDesktopCollection"/> membership convention, made mechanical.
///
/// <para><b>WHAT THIS BINDS.</b> The desktop is machine-global, so a class that puts a window on
/// it or reads pixels off it must run inside the collection that holds the machine-wide lease.
/// Without this guard the convention is TEXT, and the next probe file to appear silently rejoins
/// the racy default collection — which is precisely how SP-099's and SP-100's fixtures came to
/// contend with each other and with other processes in the first place. The symptom arrives as an
/// unrelated packet's land reddening a test it never touched (SP-106 §6.1/§6.2).</para>
///
/// <para><b>WHY A SOURCE WALK.</b> <c>[Collection]</c> is reflectable, but "this class reaches the
/// real window manager" is a property of a method body, which reflection cannot see without
/// decoding IL. Same shape and same lineage as
/// <see cref="ProcessEnvCollectionGuardTests"/> / <c>FloorWrapperGuardTests</c> /
/// <c>TestTimingGuardTests</c>: repo-root walk, never skips, fails closed, file:line
/// violations.</para>
///
/// <para><b>HONESTY.</b> This is LEXICAL and binds at FILE granularity. Named blind spots:
/// (1) a file declaring two classes where only one is bound lends the attribute to both — there
/// are no such files today; (2) a class that reaches the desktop transitively through a helper
/// this guard does not name is invisible, so the helper census (fact 2) exists to make a NEW probe
/// file fail loudly rather than join silently; (3) tokens inside string literals count, which is
/// why this file is exempt from its own scan (the same self-exemption
/// <c>TestTimingGuardTests</c> takes).</para>
/// </summary>
public class RealDesktopCollectionGuardTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] UnitProjectParts = ["client", "tests", "CcpClient.Tests"];

    private const string CollectionName = "RealDesktopCollection";
    private const string MembershipAttribute = "[Collection(nameof(RealDesktopCollection))]";

    /// <summary>This guard, and the collection's own declaration, hold the tokens as text.</summary>
    private static readonly string[] ExemptFileNames =
    [
        "RealDesktopCollectionGuardTests.cs",
        "RealDesktopCollection.cs",
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

    /// <summary>Broken-detector controls: these three must always come out bound.</summary>
    private static readonly string[] BoundControls =
    [
        "OverlayCapabilityTests.cs",
        "FlashDrawTests.cs",
        "TrayCapabilityTests.cs",
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
                    + "process AND with every other CcpClient.Tests process on the machine, which SP-107 measured "
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

    private static IReadOnlyList<(string Name, string Text)> UnitProjectSources()
    {
        var root = Path.Combine([FindRepoRoot(), .. UnitProjectParts]);
        Assert.True(Directory.Exists(root),
            $"{string.Join('/', UnitProjectParts)} not found at {root} — the real-desktop membership guard "
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
