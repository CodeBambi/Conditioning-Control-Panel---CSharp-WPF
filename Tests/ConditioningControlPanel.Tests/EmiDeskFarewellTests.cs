using System;
using System.IO;
using System.Text.Json;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// THE ARCADEMY FAREWELL: she does not follow you to school.
///
/// <para>When the Arcademy takes the screen and EMI Desk is out, she says a goodbye and winks
/// herself off a couple of seconds later. Three things about that beat can rot silently, so all
/// three are pinned here.</para>
///
/// <para>1. THE ID. It is fired through a const rather than a literal, because
/// <see cref="EmiMomentIdWiringTests"/> scans <c>Fire("...")</c> literals against the shipped
/// pool file and the pool is authored separately from the code. That deliberate step around the
/// typo guard is only safe while something else checks the id, which is what this file is.</para>
///
/// <para>2. THE HOOK. Every Arcademy launch path funnels through <c>ArcademyHostService.Launch</c>,
/// and the farewell has to sit BEFORE <c>arcademyOpened</c> in it. If it drifts below, the opening
/// moment speaks first and the goodbye is the thing that gets talked over.</para>
///
/// <para>3. THE FALLBACK. The feature has to work with no pool at all, because the pool may land
/// after the code does; an empty bubble followed by her vanishing reads as a crash.</para>
/// </summary>
public class EmiDeskFarewellTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string AppDir() => Path.Combine(RepoRoot(), "ConditioningControlPanel");

    [Fact]
    public void TheMomentIdIsTheOneThePoolWillBeAuthoredUnder()
    {
        Assert.Equal("arcademyBye", EmiDeskService.ArcademyByeMoment);
    }

    /// <summary>
    /// The hardcoded line exists, is a LINE and not a placeholder, and wears a face the pose map
    /// actually knows (an unpaired face resolves to idle, and a goodbye should not be idle).
    /// </summary>
    [Fact]
    public void TheFallbackLineIsSayableWithNoPoolAtAll()
    {
        Assert.False(string.IsNullOrWhiteSpace(EmiDeskService.ArcademyByeFallbackText));
        Assert.False(string.IsNullOrWhiteSpace(EmiDeskService.ArcademyByeFallbackFace));
        Assert.Equal("celebration", EmiChains.FrameForFace(EmiDeskService.ArcademyByeFallbackFace));
    }

    /// <summary>
    /// She leaves AFTER the words land, not 1.6 s after the fire. The say cadence is locked at
    /// 420 + 420 + 520 ms of dots before the line appears, so a flat 1.6 s would show the goodbye
    /// for about a fifth of a second and then power her off mid-read.
    /// </summary>
    [Fact]
    public void TheOutroWaitsOutTheLockedDotCadence()
    {
        const int dots = 420 + 420 + 520;
        Assert.True(EmiDeskService.ArcademyByeDismissMs >= dots + 1500,
            $"the farewell dismisses at {EmiDeskService.ArcademyByeDismissMs} ms, "
            + $"which is only {EmiDeskService.ArcademyByeDismissMs - dots} ms of readable line");

        // ...and the silence has to outlast the outro, or a late moment chases her off screen.
        Assert.True(EmiDeskService.ArcademyByeSuppressMs > EmiDeskService.ArcademyByeDismissMs);
    }

    /// <summary>
    /// THE ORDER IN THE HOST. Read out of the source, because reaching the real call needs a tier
    /// check, a door, a WebView2 host and a running app.
    /// </summary>
    [Fact]
    public void TheHostSaysGoodbyeBeforeItAnnouncesTheOpen()
    {
        var path = SourceRoots.FindProductFile("Services", "Arcademy", "ArcademyHostService.cs");
        Assert.True(File.Exists(path), "ArcademyHostService.cs is missing at " + path);

        var src = File.ReadAllText(path);
        int bye = src.IndexOf("FarewellForArcademy()", StringComparison.Ordinal);
        int opened = src.IndexOf("Fire(\"arcademyOpened\"", StringComparison.Ordinal);

        Assert.True(bye >= 0, "no Arcademy launch path calls FarewellForArcademy()");
        Assert.True(opened >= 0, "the arcademyOpened moment is no longer fired from the host");
        Assert.True(bye < opened,
            "the farewell must be fired BEFORE arcademyOpened, or the open moment talks over her goodbye");
    }

    /// <summary>
    /// When the pool HAS landed, it has to be a real speaking moment: an id that is only listed
    /// as deferred draws nothing, which would silently put her back on the fallback for ever.
    /// Skipped, deliberately, while the pool is still being authored.
    /// </summary>
    [Fact]
    public void WhenThePoolExistsItIsASpeakingMoment()
    {
        var path = Path.Combine(RepoRoot(), "Assets", "emi", "desk-lines.json");
        Assert.True(File.Exists(path), "desk-lines.json is missing at " + path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        if (!root.TryGetProperty("moments", out var moments)) return;
        if (!moments.TryGetProperty(EmiDeskService.ArcademyByeMoment, out var bye)) return;   // not authored yet

        Assert.False(bye.TryGetProperty("hold", out var hold) && hold.ValueKind == JsonValueKind.True,
            "arcademyBye is a spoken goodbye, not a wordless hold");

        if (root.TryGetProperty("deferred", out var deferred) && deferred.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in deferred.EnumerateArray())
            {
                var id = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                Assert.False(string.Equals(id, EmiDeskService.ArcademyByeMoment, StringComparison.Ordinal),
                    "arcademyBye is wired and firing, so it cannot also be listed as deferred");
            }
        }
    }
}
