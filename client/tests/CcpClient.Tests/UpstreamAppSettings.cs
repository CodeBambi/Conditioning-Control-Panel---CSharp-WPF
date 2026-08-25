using System.Text.RegularExpressions;

namespace CcpClient.Tests;

/// <summary>
/// The shipping product's <c>Models/AppSettings.cs</c>, read out of this checkout so that a fact
/// about "what upstream ships" can compare against upstream rather than against a number retyped
/// into a test.
///
/// <para><b>Matched by FIELD NAME, never by line.</b> Every default in that file is a private
/// backing field with a literal initializer, and the field name is the stable thing: line numbers in
/// a 6 000-line file move whenever anything above them changes, and a fact anchored on one would rot
/// into a false red. A name that is missing, or declared twice, is a hard failure rather than a
/// zero — a silently-absent expectation would turn its whole consumer into a test of nothing.</para>
///
/// <para><b>It cannot cry wolf inside this repository.</b> <see cref="ReadOnlyWpfTreeGuardTests"/>
/// pins <c>ConditioningControlPanel/</c> byte-identical to its <c>main</c> baseline, so upstream
/// cannot move under a consumer here. If a future upstream sync really does change one of these
/// defaults, the resulting red is the correct answer: the port would genuinely have stopped
/// matching the product it is a port of.</para>
/// </summary>
public static class UpstreamAppSettings
{
    private static readonly string[] RepoAnchorParts = ["client", "CcpClient.sln"];
    private static readonly string[] SettingsParts = ["ConditioningControlPanel", "Models", "AppSettings.cs"];

    private static readonly Lazy<string> Text = new(Read, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The value of a <c>private bool _name = true|false;</c> declaration, by name.</summary>
    public static bool Bool(string field) =>
        Initializer(field, @"private\s+bool\s+" + Regex.Escape(field) + @"\s*=\s*(true|false)\s*;") == "true";

    /// <summary>The value of a <c>private int _name = N;</c> declaration, by name.</summary>
    public static int Int(string field) =>
        int.Parse(Initializer(field, @"private\s+int\s+" + Regex.Escape(field) + @"\s*=\s*(-?\d+)\s*;"));

    /// <summary>The value of a <c>private string _name = "…";</c> declaration, by name.</summary>
    public static string String(string field) =>
        Initializer(field, @"private\s+string\s+" + Regex.Escape(field) + @"\s*=\s*""([^""]*)""\s*;");

    private static string Initializer(string field, string pattern)
    {
        var matches = Regex.Matches(Text.Value, pattern);
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"expected exactly one declaration of {field} in ConditioningControlPanel/Models/AppSettings.cs, "
                + $"found {matches.Count} — the upstream-default reader refuses to guess");
        }

        return matches[0].Groups[1].Value;
    }

    private static string Read() => File.ReadAllText(Path.Combine([FindRepoRoot(), .. SettingsParts]));

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
            $"repo root not found walking up from {AppContext.BaseDirectory} "
            + $"(anchor: {string.Join('/', RepoAnchorParts)}) — the upstream-default reader refuses to skip");
    }
}
