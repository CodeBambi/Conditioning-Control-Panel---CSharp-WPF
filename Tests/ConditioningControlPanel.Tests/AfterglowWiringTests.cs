using System;
using System.IO;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Afterglow wiring: the ghosts ride every teardown path the subliminal card does.</summary>
public class AfterglowWiringTests
{
    /// <summary>Panic and the emergency exit stop subliminals through Stop / BlankCards / Dispose; each must
    /// take the ghosts down too, and switching the Super off must clear them.</summary>
    [Fact]
    public void Service_ClearsGhostsOnEveryCardTeardownPath()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Subliminal", "SubliminalService.cs"));
        foreach (var method in new[] { "public void Stop()", "private void BlankCards()" })
        {
            var body = Body(src, method);
            Assert.Contains("ClearAfterglow();", body);
        }
        Assert.Contains("SuperAccess.Changed += OnSuperChanged", src);
        Assert.Contains("SuperAccess.Changed -= OnSuperChanged", Body(src, "public void Dispose()"));
        Assert.Contains("_afterglow.GetActiveTextRectsPx()", Body(src, "public System.Drawing.Rectangle[] GetActiveTextScreenRects()"));
    }

    private static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(i >= 0, signature);
        int open = src.IndexOf('{', i), depth = 0;
        for (int j = open; j < src.Length; j++)
        {
            if (src[j] == '{') depth++;
            else if (src[j] == '}' && --depth == 0) return src.Substring(open, j - open + 1);
        }
        return src.Substring(open);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}
