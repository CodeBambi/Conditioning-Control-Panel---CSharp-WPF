using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// EMI's first summon from the side rail (ccp-bugs #1259: "I have to click 3 times for her to pop up.
/// After that, it works on single click").
///
/// <para><c>Visibility="Hidden"</c> on the window root queues a native hide that can land AFTER the
/// summon's own <c>Show()</c>. WPF then reports her visible while Win32 has her hidden, so click one
/// marks her out with nothing on screen, click two dismisses her, click three finally shows her. A new
/// WPF window is already hidden, and <c>EmiDeskService.EnsureWindow</c> realises the HWND with its own
/// Show/Hide pair, so the attribute buys nothing and must not come back.</para>
/// </summary>
public class EmiDeskFirstSummonTests
{
    [Fact]
    public void TheWindowRootDoesNotStartHidden()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Windows", "EmiDesk", "EmiDeskWindow.xaml"));
        var root = Regex.Match(xaml, @"<Window\b[^>]*>", RegexOptions.Singleline);
        Assert.True(root.Success, "no <Window> root in EmiDeskWindow.xaml");
        Assert.DoesNotContain("Visibility=", root.Value);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
