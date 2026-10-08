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
        // An alias (var ac = App.AttentionCheck; ac.Start()) would dodge the call pattern, so also pin
        // WHO may touch the service at all: its own file, App (constructs it) and BarkService (events).
        var touch = new Regex(@"App\.AttentionCheck\b|\bAttentionCheckService\b");
        string[] allowed = { "AttentionCheckService.cs", "App.xaml.cs", "BarkService.cs" };
        var hits = Code("ConditioningControlPanel")
            .Where(f => !f.path.EndsWith("AttentionCheckService.cs") && start.IsMatch(f.code)
                     || touch.IsMatch(f.code) && !allowed.Contains(Path.GetFileName(f.path)))
            .Select(f => f.path).ToList();
        Assert.True(hits.Count == 0, "WPF may now start AttentionCheckService; port it to Avalonia: " + string.Join(", ", hits));
    }

    [Fact]
    public void AvaloniaNeverShowsTheAttentionCheckRing()
    {
        // Any construction (new X(), X x = new(), or an .axaml element) outside the control's own files.
        var use = new Regex(@"new\s+AttentionCheckControl\b|AttentionCheckControl\s+\w+\s*=\s*new\s*\(|<\w+:AttentionCheckControl\b");
        var hits = Code("CCP.Avalonia", "*.cs").Concat(Code("CCP.Avalonia", "*.axaml"))
            .Where(f => use.IsMatch(f.code)).Select(f => f.path).ToList();
        Assert.True(hits.Count == 0, "WPF never shows the attention-check ring: " + string.Join(", ", hits));
    }

    private static readonly Regex CommentsAndStrings =
        new(@"//[^\n]*|/\*.*?\*/|@""(?:""""|[^""])*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'", RegexOptions.Singleline);

    private static (string path, string code)[] Code(string project, string glob = "*.cs") =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), project), glob, SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(p => (p, CommentsAndStrings.Replace(File.ReadAllText(p), "")))
            .ToArray();

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
