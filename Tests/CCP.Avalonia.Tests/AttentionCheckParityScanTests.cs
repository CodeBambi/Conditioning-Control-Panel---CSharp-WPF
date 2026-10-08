using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Row ctrl-attention-check. WPF scrapped the attention-check mechanic pre-ship: App.xaml.cs
/// constructs AttentionCheckService only so BarkService can wire OnPass/OnFail, and nothing ever starts
/// it, so WPF never shows the ring (and has no panic/Lockdown handling for it). Parity on this head is
/// therefore "nothing shows it either". If WPF revives it, this fails so the service gets ported (with
/// panic + Lockdown) instead of drifting.</summary>
public sealed class AttentionCheckParityScanTests
{
    [Fact]
    public void WpfNeverStartsTheAttentionCheck()
    {
        var start = new Regex(@"AttentionCheck\??\.(Start|FireNow)\s*\(");
        var hits = Code("ConditioningControlPanel").Where(f => start.IsMatch(f.code)).Select(f => f.path).ToList();
        Assert.True(hits.Count == 0, "WPF now starts AttentionCheckService; port it to Avalonia: " + string.Join(", ", hits));
    }

    [Fact]
    public void AvaloniaNeverShowsTheAttentionCheckRing()
    {
        var hits = Code("CCP.Avalonia").Where(f => f.code.Contains("new AttentionCheckControl")).Select(f => f.path).ToList();
        Assert.True(hits.Count == 0, "WPF never shows the attention-check ring: " + string.Join(", ", hits));
    }

    private static readonly Regex CommentsAndStrings =
        new(@"//[^\n]*|/\*.*?\*/|@""(?:""""|[^""])*""|""(?:\\.|[^""\\\n])*""", RegexOptions.Singleline);

    private static (string path, string code)[] Code(string project) =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), project), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (p, CommentsAndStrings.Replace(File.ReadAllText(p), "")))
            .ToArray();

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
