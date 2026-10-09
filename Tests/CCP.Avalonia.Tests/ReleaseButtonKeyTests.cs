using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell's top-right "vX IS OUT" button names the current release through a loc key that is
/// bumped by hand each release. WPF's MainWindow.xaml is bumped by the release script; the Avalonia shell
/// must name the same key or it shows a stale version (user-reported: it read v6.9.1 on 6.11.3).</summary>
public sealed class ReleaseButtonKeyTests
{
    private static readonly Regex Key = new(@"x:Name=""BtnUpdateAvailable""\s+Content=""\{loc:Str (\w+)\}""");

    /// <summary>The parity target is WPF 7.1.5 (release/7.1.5, MainWindow/MainWindow.xaml:1334 names
    /// btn_v7_1_5_is_out). The WPF tree inside this repo is older than that release (main has not taken
    /// the 7.1.x line yet), so it cannot be the truth here. Bump this pin with each WPF release the port
    /// follows.</summary>
    private const string WpfReleaseKey = "btn_v7_1_5_is_out";

    [Fact]
    public void AvaloniaShellNamesTheSameReleaseKeyAsWpf()
    {
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var ava = Key.Match(Read("CCP.Avalonia", "Views", "Windows", "MainShellWindow.axaml"));
        Assert.True(ava.Success, "BtnUpdateAvailable not found in the Avalonia shell");
        Assert.Equal(WpfReleaseKey, ava.Groups[1].Value);

        // The key must resolve in every language file, or the button shows its raw key.
        var langs = Directory.GetFiles(Path.Combine(root, "CCP.Core", "Localization", "Languages"), "*.json");
        Assert.Equal(10, langs.Length);
        foreach (var lang in langs)
            Assert.True(File.ReadAllText(lang).Contains($"\"{WpfReleaseKey}\""), $"{Path.GetFileName(lang)} lacks {WpfReleaseKey}");
    }

    // Audit #2096 P2: the button read v7.0.5 while its tooltip still said v6.11.3 (WPF MainWindow.xaml:1992).
    private static readonly Regex Tip = new(@"x:Name=""BtnUpdateAvailable""[^>]*?ToolTip(?:\.Tip)?=""\{loc:Str (\w+)\}""");

    [Fact]
    public void AvaloniaShellNamesTheSameReleaseTooltipKeyAsWpf()
    {
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var wpf = Tip.Match(Read("ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));
        var ava = Tip.Match(Read("CCP.Avalonia", "Views", "Windows", "MainShellWindow.axaml"));
        Assert.True(wpf.Success && ava.Success, "BtnUpdateAvailable tooltip not found in one of the shells");
        Assert.Equal(wpf.Groups[1].Value, ava.Groups[1].Value);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
