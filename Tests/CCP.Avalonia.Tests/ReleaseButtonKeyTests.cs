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

    [Fact]
    public void AvaloniaShellNamesTheSameReleaseKeyAsWpf()
    {
        var root = RepoRoot();
        string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
        var wpf = Key.Match(Read("ConditioningControlPanel", "MainWindow", "MainWindow.xaml"));
        var ava = Key.Match(Read("CCP.Avalonia", "Views", "Windows", "MainShellWindow.axaml"));
        Assert.True(wpf.Success && ava.Success, "BtnUpdateAvailable not found in one of the shells");
        Assert.Equal(wpf.Groups[1].Value, ava.Groups[1].Value);
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
