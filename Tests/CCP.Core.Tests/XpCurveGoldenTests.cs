using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// <see cref="XpCurve"/> against values printed by the PRE-MOVE WPF <c>ProgressionService</c>
/// source (its curve statics plus the memoized instance <c>GetTotalXP</c>/<c>GetCurrentLevelXP</c>,
/// copied verbatim into a throwaway console app with <c>ActiveCurveEpoch</c> set to each epoch).
/// Round-trip ("R") formatting, so a one-ulp drift fails. Lines: C=cost/cumulative/quest scale,
/// T=total/current-level XP, D=derive level, R=reprice.
/// </summary>
public sealed class XpCurveGoldenTests
{
    private static string FixturePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "xp_curve_golden_premove.txt");

    private static string N(double d) => d.ToString("R", CultureInfo.InvariantCulture);
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void CoreCurveMatchesPreMoveWpfValues()
    {
        var lines = File.ReadAllLines(FixturePath());
        Assert.Equal(113, lines.Length);
        foreach (var line in lines)
        {
            var p = line.Split(' ');
            int e;
            string actual;
            switch (p[0])
            {
                case "C":
                    e = int.Parse(p[1]); var l = int.Parse(p[2]);
                    actual = $"C {e} {l} {N(XpCurve.GetXPForLevel(l, e))} {N(XpCurve.CumulativeXpToReachLevel(l, e))} {N(XpCurve.CumulativeXpBeforeLevel(l, e))} {N(XpCurve.QuestLevelScale(l, e))}";
                    break;
                case "T":
                    e = int.Parse(p[1]); l = int.Parse(p[2]);
                    actual = $"T {e} {l} {N(XpCurve.GetTotalXP(l, 123.5, e))} {N(XpCurve.GetCurrentLevelXP(l, 5000000, e))}";
                    break;
                case "D":
                    e = int.Parse(p[1]);
                    var d = XpCurve.DeriveLevelFromLifetimeXp(D(p[2]), e);
                    actual = $"D {e} {p[2]} {d.Level} {N(d.XpIntoLevel)}";
                    break;
                default:
                    var r = XpCurve.RepriceLedger(int.Parse(p[1]), D(p[2]), int.Parse(p[3]), int.Parse(p[4]));
                    actual = $"R {p[1]} {p[2]} {p[3]} {p[4]} {r.Level} {N(r.XpIntoLevel)}";
                    break;
            }
            Assert.Equal(line, actual);
        }
    }
}
