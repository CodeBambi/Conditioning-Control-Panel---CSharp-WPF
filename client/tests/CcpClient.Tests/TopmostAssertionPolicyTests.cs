using System.Text.RegularExpressions;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The decision the board's row "should the four Win32 surfaces assert HWND_TOPMOST through the
/// attach ladder instead of bare" reached, pinned so it cannot be quietly reversed by the next
/// reader of a <c>style-refused</c> log line. The decision is NO, and the full argument with its
/// measurements is <c>client/docs/window-behavior-manifest.md</c> §8.7.
///
/// <para><b>The one sentence that matters.</b> The ladder's mechanism IS taking the foreground:
/// <c>Escalate</c> attaches this thread's input queue to the foreground thread's and then calls
/// <c>SetForegroundWindow</c>/<c>BringWindowToTop</c>/<c>SetActiveWindow</c>/<c>SetFocus</c> on the
/// target (<c>Input/Win32InputPresence.cs:692-715</c>). Measured on this machine over three runs of
/// a standalone Win32 probe with no test or product code in it: applied to a bare
/// <c>WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW</c> popup it does set <c>WS_EX_TOPMOST</c> — and it also
/// makes that popup the FOREGROUND WINDOW, taking the foreground off a window belonging to another
/// process. The four surfaces are non-activating by contract, and the overlay refuses in type when
/// its own surface becomes the foreground (<c>Overlay/Win32OverlayPresence.cs:565-571</c>), so
/// routing the topmost claim through the ladder trades one refusal for another and pays for it with
/// a real focus theft, at a 32-iteration re-assert cadence.</para>
///
/// <para><b>Why this guard is derived from source rather than given a list of four files, and why
/// it names no exemption.</b> A hard-coded list stops covering the tree the moment a sixth surface
/// appears — which is the same blind spot the §8 census exists to close. The set here is re-derived
/// on every run: every file under <c>client/src</c> that USES <c>HwndTopmost</c> (its own P/Invoke
/// constant declaration does not count) AND declares <c>WS_EX_NOACTIVATE</c>. That second half is
/// the discriminator and it is a property of the code rather than a file name: the lock card is the
/// one topmost surface that deliberately carries neither <c>WS_EX_NOACTIVATE</c> nor
/// <c>WS_EX_TRANSPARENT</c>, because "their absence is what makes this window the opposite kind of
/// window" (<c>Input/Win32InputPresence.cs:1097-1099</c>), so it falls out of the set on its own
/// and no exemption list has to be maintained or trusted.</para>
///
/// <para><b>What this does NOT claim.</b> Nothing here is a Linux statement — all five members are
/// <c>Win32*</c> types that do not exist off Windows. And a text scan is a scan: it pins that the
/// four surfaces declare and call no foreground-taking API, not that the OS never hands them the
/// foreground by some other route. The surfaces' own runtime read-backs are what cover that, and
/// they are deliberately not restated here (§8.4's one-guard rule).</para>
/// </summary>
public sealed class TopmostAssertionPolicyTests
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SourceRootParts = ["client", "src"];

    /// <summary>The topmost claim itself. A file that never names this is not in the set.</summary>
    private const string TopmostNeedle = "HwndTopmost";

    /// <summary>The non-activation declaration that separates the four surfaces this decision binds
    /// from the one topmost surface whose capability IS the foreground. Derived, never listed.
    /// </summary>
    private const string NonActivatingNeedle = "WsExNoactivate";

    /// <summary>The foreground-TAKING calls. <c>GetForegroundWindow</c> is deliberately absent:
    /// three of the four surfaces READ the foreground to refuse on it, which is the opposite act.
    /// </summary>
    private static readonly string[] TakeForegroundNeedles =
    [
        "SetForegroundWindow",
        "AttachThreadInput",
        "SetActiveWindow",
        "BringWindowToTop",
        "SetFocus",
    ];

    /// <summary>Comments are stripped before the scan so the decision can be EXPLAINED in the very
    /// files it binds. The removal can only hide a call, never invent one; the one way it could
    /// hide a real call is a call placed after a <c>//</c> that lives inside a string literal on the
    /// same line, which is noted rather than defended against.</summary>
    private static readonly Regex CommentPattern =
        new(@"/\*.*?\*/|//[^\r\n]*", RegexOptions.Compiled | RegexOptions.Singleline);

    [Fact]
    public void EveryNonActivatingSurfaceThatAssertsHwndTopmost_TakesNoForeground()
    {
        var sourceRoot = Path.Combine([FindRepoRoot(), .. SourceRootParts]);
        var nonActivating = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var activating = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var code = CommentPattern.Replace(File.ReadAllText(file), string.Empty);
            if (!UsesTopmost(code))
            {
                continue;
            }

            var path = Path.GetRelativePath(sourceRoot, file).Replace('\\', '/');
            if (code.Contains(NonActivatingNeedle, StringComparison.Ordinal))
            {
                nonActivating.Add(path, code);
            }
            else
            {
                activating.Add(path);
            }
        }

        // Two broken-detectors, because both halves of the partition can rot. A walk that finds no
        // non-activating surfaces has stopped seeing the tree; a walk in which the discriminator
        // separates nothing is not discriminating, and the argument below no longer follows from it.
        Assert.True(
            nonActivating.Count >= 2,
            $"only {nonActivating.Count} file(s) under {string.Join('/', SourceRootParts)} both use "
            + $"{TopmostNeedle} and declare {NonActivatingNeedle}; this guard has stopped seeing the tree "
            + "it is supposed to bind");
        Assert.True(
            activating.Count >= 1,
            $"every file that uses {TopmostNeedle} also declares {NonActivatingNeedle}, so the "
            + "discriminator separates nothing. Either the one topmost surface whose capability IS the "
            + "foreground has changed shape — in which case §8.3 S-04 and §8.7 both need re-reading — or "
            + "this scan is no longer measuring what it claims");

        var offenders = new List<string>();
        foreach (var (path, code) in nonActivating)
        {
            var found = TakeForegroundNeedles.Where(n => code.Contains(n + "(", StringComparison.Ordinal)).ToArray();
            if (found.Length > 0)
            {
                offenders.Add($"{path} calls or declares {string.Join(", ", found)}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A surface that asserts HWND_TOPMOST has acquired a foreground-taking call: "
            + string.Join("; ", offenders)
            + ". THIS IS THE DECISION IN client/docs/window-behavior-manifest.md §8.7 AND IT IS NOT A "
            + "CONSTANT TO UPDATE. Measured, three runs, standalone Win32 probe: the attach ladder does "
            + "restore WS_EX_TOPMOST in a process that has lost SetForegroundWindow permission, AND it "
            + "makes a WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW popup the foreground window, off another "
            + "process's window. These four surfaces are non-activating by contract and the overlay "
            + "refuses in type on exactly that outcome (overlay-stole-focus). If the topmost band is "
            + "genuinely being refused, the surface is already saying so from the OS's own read-back; "
            + "the answer is not to assert harder, it is to keep refusing honestly. Re-open the "
            + "decision in the manifest before changing this.");
    }

    /// <summary>True when the file USES the topmost handle rather than merely declaring it — the
    /// interop files that own the constant are not themselves asserters.</summary>
    private static bool UsesTopmost(string code)
    {
        foreach (Match match in Regex.Matches(code, Regex.Escape(TopmostNeedle)))
        {
            var lineStart = code.LastIndexOf('\n', Math.Max(match.Index - 1, 0)) + 1;
            var line = code[lineStart..match.Index];
            if (!line.Contains(" const ", StringComparison.Ordinal)
                && !line.Contains(" readonly ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine([dir.FullName, .. RepoAnchorParts])))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root (the directory holding {string.Join('/', RepoAnchorParts)}) not found above "
            + $"{AppContext.BaseDirectory} — this guard fails rather than skips");
    }
}
