using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The summon-chord rules both heads share (lifted from WPF EmiDeskService
/// FormatChord / ValidateChord, which now delegate here).</summary>
public sealed class EmiDeskChordTests
{
    [Fact]
    public void FormatAndParseRoundTripInWpfOrder()
    {
        Assert.Equal("Ctrl+Alt+Shift+Win+E", EmiDeskChord.Format(ChordMods.Win | ChordMods.Shift | ChordMods.Alt | ChordMods.Ctrl, "E"));
        Assert.Equal((ChordMods.Ctrl | ChordMods.Alt, "E"), EmiDeskChord.Parse(" control + alt + E "));
        Assert.Null(EmiDeskChord.Parse("E"));          // bare key: refused
        Assert.Null(EmiDeskChord.Parse("Ctrl+"));      // no key
        Assert.Null(EmiDeskChord.Parse("Ctrl+E+F"));   // two keys
    }

    [Fact]
    public void ValidateRefusesBareHookClashAndQuickRecal()
    {
        var s = new AppSettings { PanicKeyEnabled = true, PanicKey = "F9" };
        Assert.NotNull(EmiDeskChord.Validate(ChordMods.Ctrl, null, s));
        Assert.NotNull(EmiDeskChord.Validate(ChordMods.None, "K", s));
        Assert.NotNull(EmiDeskChord.Validate(ChordMods.Ctrl | ChordMods.Shift, "F9", s));
        Assert.NotNull(EmiDeskChord.Validate(ChordMods.Ctrl | ChordMods.Alt, "G", s));
        Assert.Null(EmiDeskChord.Validate(ChordMods.Ctrl | ChordMods.Alt | ChordMods.Shift, "G", s));
        Assert.Null(EmiDeskChord.Validate(ChordMods.Ctrl | ChordMods.Alt, "E", s));
    }
}
