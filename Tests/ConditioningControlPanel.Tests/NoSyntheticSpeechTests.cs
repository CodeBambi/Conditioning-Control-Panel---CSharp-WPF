using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// NO SYNTHETIC SPEECH (owner, 2026-09-25). No spoken word in the app may come from a speech
/// synthesiser: not Windows speech (System.Speech, WinRT Windows.Media.SpeechSynthesis, SAPI's
/// SpVoice) and not the browser's speechSynthesis inside a WebView page. A phrase with no recorded
/// clip stays silent.
///
/// Three players on 6.10.3 heard English words read aloud in their own language: the Back Room
/// word voice fell back to Windows speech, which picked a voice in the machine's own language. This
/// scan fails the build if any of those APIs reappears in shipped client code, C# or web.
/// Tests (which stub the browser API as a spy) are exempt; docs are not scanned.
/// </summary>
public class NoSyntheticSpeechTests
{
    /// <summary>Case-insensitive, so it catches <c>speechSynthesis</c>, <c>SpeechSynthesisUtterance</c>,
    /// <c>SpeechSynthesizer</c> and <c>Windows.Media.SpeechSynthesis</c> with one rule.</summary>
    private static readonly Regex[] Banned =
    {
        new(@"speech\s*synthes", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"System\.Speech\.Synthesis", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"Include\s*=\s*""System\.Speech""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bI?SpVoice\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    };

    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".xaml", ".csproj", ".props", ".targets", ".js", ".mjs", ".cjs", ".ts", ".html", ".htm",
    };

    /// <summary>Build output mirrors Resources/web, and test folders stub the API on purpose.</summary>
    private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", "tests", "test", "__tests__",
    };

    internal static bool IsBanned(string line) => Banned.Any(r => r.IsMatch(line));

    internal static bool IsTestFile(string name)
        => name.Contains(".test.", StringComparison.OrdinalIgnoreCase)
           || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("const S = globalThis.speechSynthesis;")]
    [InlineData("new SpeechSynthesisUtterance(text)")]
    [InlineData("window['speechSynthesis'].speak(u)")]
    [InlineData("using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();")]
    [InlineData("using System.Speech.Synthesis;")]
    [InlineData("var s = new SpeechSynthesizer();")]
    [InlineData("<PackageReference Include=\"System.Speech\" Version=\"8.0.0\" />")]
    [InlineData("dynamic v = Activator.CreateInstance(Type.GetTypeFromProgID(\"SAPI.SpVoice\"));")]
    public void TheMatcherCatchesEverySynthesiser(string line) => Assert.True(IsBanned(line), line);

    [Theory]
    [InlineData("vox.speak(text, { face: 'idle' });")]
    [InlineData("App.Audio?.PlayOneShot(path, vol, \"chaos-narrator\");")]
    [InlineData("using Vosk; // speech RECOGNITION is fine, only synthesis is banned")]
    [InlineData("var action = SynthesizeEffectAction(item);")]
    public void TheMatcherLeavesOrdinaryCodeAlone(string line) => Assert.False(IsBanned(line), line);

    [Fact]
    public void NoClientCodeUsesASpeechSynthesiser()
    {
        // Every product root plus the shared web assets (moved out of the WPF head to /Assets).
        var root = SourceRoots.RepoRoot;
        var hits = new List<string>();
        int scanned = 0;
        foreach (var file in SourceRoots.ProductDirectories.Append(Path.Combine(root, "Assets")).SelectMany(Walk))
        {
            scanned++;
            string text;
            try { text = File.ReadAllText(file); } catch (IOException) { continue; }
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (IsBanned(line))
                    hits.Add(Path.GetRelativePath(root, file) + ":" + (i + 1) + ": " + line.Trim());
            }
        }

        Assert.True(scanned > 500, "the scan found only " + scanned + " files under " + root);
        Assert.True(hits.Count == 0,
            "Synthetic speech is banned (owner, 2026-09-25). A phrase with no recorded clip stays silent. Found:\n"
            + string.Join("\n", hits));
    }

    private static IEnumerable<string> Walk(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (CodeExtensions.Contains(Path.GetExtension(file)) && !IsTestFile(name)) yield return file;
        }
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (SkippedDirs.Contains(Path.GetFileName(sub))) continue;
            foreach (var f in Walk(sub)) yield return f;
        }
    }
}
