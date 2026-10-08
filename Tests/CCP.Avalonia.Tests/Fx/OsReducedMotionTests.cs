using System.IO;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests.Fx;

/// <summary>
/// The desktop reduced-motion probes (WPF MotionFx.Level reads SystemParameters.ClientAreaAnimation):
/// the GNOME and KDE parsers, and the rule that the OS can only remove motion.
/// </summary>
public sealed class OsReducedMotionTests
{
    [Theory]
    [InlineData("true\n", true)]
    [InlineData("false\n", false)]
    [InlineData("'false'", false)]
    [InlineData("  TRUE ", true)]
    [InlineData("No such schema “org.gnome.desktop.interface”", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void GsettingsOutput(string? output, bool? expected) =>
        Assert.Equal(expected, OsReducedMotion.ParseGsettings(output));

    [Theory]
    [InlineData("[KDE]\nAnimationDurationFactor=0\n", false)]
    [InlineData("[KDE]\nAnimationDurationFactor=0.0\nLookAndFeelPackage=org.kde.breezedark.desktop\n", false)]
    [InlineData("[General]\nfoo=1\n[KDE]\nAnimationDurationFactor=0.5\n", true)]
    [InlineData("[KDE]\nAnimationDurationFactor[$e]=0\n", false)]
    [InlineData("[KDE]\r\nAnimationDurationFactor=2\r\n", true)]
    [InlineData("[General]\nAnimationDurationFactor=0\n", null)]   // wrong group
    [InlineData("[KDE]\nSingleClick=false\n", null)]               // Plasma default: not set
    [InlineData("[KDE]\nAnimationDurationFactor=fast\n", null)]
    [InlineData("", null)]
    public void KdeGlobals(string text, bool? expected) =>
        Assert.Equal(expected, OsReducedMotion.ParseKdeGlobals(text));

    [Theory]
    [InlineData(MotionLevel.Full, true, MotionLevel.Full)]
    [InlineData(MotionLevel.Full, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, false, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Reduced, true, MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off, true, MotionLevel.Off)]
    [InlineData(MotionLevel.Off, false, MotionLevel.Off)]
    public void OsPreferenceOnlyRemovesMotion(MotionLevel setting, bool osOn, MotionLevel expected) =>
        Assert.Equal(expected, AmbientFxCanvas.Env.ResolveLevel(setting, osOn));

    [Fact]
    public void LiveProbeAnswersWithoutThrowing()
    {
        var old = OsReducedMotion.TestOverride;
        try
        {
            OsReducedMotion.TestOverride = null;
            _ = OsReducedMotion.AnimationsEnabled;   // Windows: SPI call; Linux: cached, probed off-thread
            OsReducedMotion.Refresh();
        }
        finally { OsReducedMotion.TestOverride = old; }
    }

#if DEBUG
    [Fact]
    public void FxBisectParksListedGroups()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-fxbisect-").FullName;
        var oldFolder = FxBisect.Folder;
        try
        {
            FxBisect.Folder = dir;
            FxBisect.Invalidate();
            Assert.False(FxBisect.Off("canvas:Ambient"));

            File.WriteAllText(Path.Combine(dir, "fx-off.txt"), "# perf bisect\ncanvas\nforceactive\n");
            FxBisect.Invalidate();
            Assert.True(FxBisect.Off("canvas:Ambient"));   // a bare group parks every member
            Assert.True(FxBisect.Off("forceactive"));
            Assert.False(FxBisect.Off("orb"));
        }
        finally
        {
            FxBisect.Folder = oldFolder;
            FxBisect.Invalidate();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
#endif
}
