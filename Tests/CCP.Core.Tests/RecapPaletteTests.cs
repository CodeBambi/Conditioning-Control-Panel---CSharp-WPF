using System.Linq;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The recap palette both heads write: the exact bytes WPF RecapTheme produced inline.</summary>
public sealed class RecapPaletteTests
{
    [Fact]
    public void HotPinkAccentGivesWpfRecapThemeBytes()
    {
        var p = RecapPalette.For(0xFF, 0x69, 0xB4).ToDictionary(e => e.Key, e => (e.A, e.R, e.G, e.B));
        Assert.Equal(15, p.Count);
        Assert.Equal(((byte)0xFF, (byte)255, (byte)105, (byte)180), p["RecapMagenta"]);
        Assert.Equal(((byte)0xFF, (byte)140, (byte)57, (byte)99), p["RecapViolet"]);      // x0.55
        Assert.Equal(((byte)0xFF, (byte)255, (byte)172, (byte)213), p["RecapVioletLite"]); // +45% to white
        Assert.Equal(((byte)0xFF, (byte)15, (byte)6, (byte)10), p["RecapVoid"]);           // x0.06
        Assert.Equal(((byte)0x2E, (byte)255, (byte)172, (byte)213), p["RecapLine"]);
        Assert.Equal(((byte)0x14, (byte)140, (byte)57, (byte)99), p["RecapVerdictTintBottom"]);
    }
}
