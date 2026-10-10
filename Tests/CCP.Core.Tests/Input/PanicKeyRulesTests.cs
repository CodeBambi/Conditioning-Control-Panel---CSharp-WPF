using System;
using ConditioningControlPanel.Input;
using Xunit;

namespace CCP.Core.Tests.Input;

/// <summary>The pure half of the Windows panic hook: key names to virtual keys (every alias the
/// settings can hold), a held key is one press (WPF LeashHoldToCut), and Lockdown's system-key block
/// (WPF GlobalKeyboardHook.HookCallback).</summary>
public sealed class PanicKeyRulesTests
{
    [Theory]
    [InlineData("Escape", 0x1B)]
    [InlineData("F24", 0x87)]
    [InlineData("F1", 0x70)]
    [InlineData("F8", 0x77)]
    [InlineData("Pause", 0x13)]
    [InlineData("A", 0x41)]
    [InlineData("z", 0x5A)]
    [InlineData("D1", 0x31)]
    [InlineData("NumPad0", 0x60)]
    [InlineData("Return", 0x0D)]
    [InlineData("Enter", 0x0D)]
    [InlineData("PageDown", 0x22)]
    [InlineData("Next", 0x22)]
    [InlineData("OemTilde", 0xC0)]
    [InlineData("Oem3", 0xC0)]
    [InlineData("Capital", 0x14)]
    [InlineData("CapsLock", 0x14)]
    [InlineData("Scroll", 0x91)]
    [InlineData("LeftCtrl", 0xA2)]
    [InlineData(" Space ", 0x20)]
    public void KeyNamesMapToTheirVirtualKey(string name, int vk) => Assert.Equal(vk, VirtualKeys.Of(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NoSuchKey")]
    [InlineData("F25")]
    public void UnknownOrBlankNamesCannotFire(string? name) => Assert.Equal(0, VirtualKeys.Of(name));

    [Fact]
    public void EveryNamedCodeRoundTrips()
    {
        for (var vk = 1; vk < 256; vk++)
            if (VirtualKeys.NameOf(vk) is { } name)
                Assert.Equal(vk, VirtualKeys.Of(name));
        Assert.Equal("F24", VirtualKeys.NameOf(0x87));
        Assert.Equal("Escape", VirtualKeys.NameOf(0x1B));
        Assert.Null(VirtualKeys.NameOf(0xFF));
    }

    private static readonly DateTime T0 = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AHeldKeyIsOnePress()
    {
        var hold = new PanicKeyHold();
        Assert.False(hold.Down(T0, leashed: false).Repeat);   // the real press
        for (var i = 1; i <= 60; i++)   // two seconds of ~30 Hz auto-repeat
            Assert.True(hold.Down(T0.AddMilliseconds(33 * i), leashed: false).Repeat);
        Assert.True(hold.Held);
    }

    [Fact]
    public void AKeyUpEndsTheHoldSoTheNextDownIsAPress()
    {
        var hold = new PanicKeyHold();
        hold.Down(T0, false);
        hold.Up();
        Assert.False(hold.Held);
        Assert.False(hold.Down(T0.AddMilliseconds(100), false).Repeat);
    }

    [Fact]
    public void ALostKeyUpNeverEatsTheNextRealPress()
    {
        var hold = new PanicKeyHold();
        hold.Down(T0, false);
        Assert.False(hold.Down(T0 + PanicKeyHold.RepeatGap + TimeSpan.FromMilliseconds(1), false).Repeat);
        Assert.False(hold.Down(T0.AddSeconds(-1), false).Repeat);   // a clock step back is never a repeat
    }

    [Fact]
    public void LeashedHoldAsksOnceAtFiveSeconds()
    {
        var hold = new PanicKeyHold();
        var dues = 0;
        for (var ms = 0; ms <= 7000; ms += 33)
            if (hold.Down(T0.AddMilliseconds(ms), leashed: true).Due) dues++;
        Assert.Equal(1, dues);
        Assert.True(hold.HeldFor(T0.AddSeconds(7)) >= PanicKeyHold.Hold);

        var free = new PanicKeyHold();
        for (var ms = 0; ms <= 7000; ms += 33)
            Assert.False(free.Down(T0.AddMilliseconds(ms), leashed: false).Due);
    }

    [Theory]
    [InlineData(0x5B, false, false, true)]   // LWin
    [InlineData(0x5C, false, false, true)]   // RWin
    [InlineData(0x09, true, false, true)]    // Alt+Tab
    [InlineData(0x73, true, false, true)]    // Alt+F4
    [InlineData(0x1B, true, false, true)]    // Alt+Esc
    [InlineData(0x1B, false, true, true)]    // Ctrl+Esc, Ctrl+Shift+Esc
    [InlineData(0x1B, false, false, false)]  // bare Esc is never blocked (#680)
    [InlineData(0x09, false, false, false)]  // bare Tab
    [InlineData(0x73, false, false, false)]  // bare F4
    [InlineData(0x46, true, false, false)]   // Alt+F: menu mnemonics keep working (#338)
    [InlineData(0x25, true, false, false)]   // Alt+Left
    [InlineData(0x87, false, false, false)]  // F24
    public void LockdownBlocksOnlyTheTaskSwitchAndCloseKeys(int vk, bool sysKeyDown, bool ctrl, bool blocked)
        => Assert.Equal(blocked, SystemKeyBlock.Suppress(vk, sysKeyDown, ctrl));
}
