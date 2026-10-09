using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Input;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Wave 5 r11: hold-to-cut on the panic key (WPF 7.1.5 LeashHoldSwallows). The first down
/// is always THE panic press, leashed or not; repeats are swallowed; while leashed five seconds of
/// holding asks once; not leashed it never asks. Driven through OnDown / OnUp, no hook.</summary>
[Collection("Win32PanicKey")]
public sealed class LeashPanicHoldTests
{
    private static readonly DateTime T0 = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void HeldPanicKey_IsOnePress_AndAsksToCutOnlyWhileLeashed(bool leashed, int asks)
    {
        int presses = 0, due = 0, holding = 0, released = 0;
        void Due() => due++;
        void Holding(TimeSpan _) => holding++;
        void Released() => released++;
        var oldLeashed = Win32PanicKey.Leashed;
        Win32PanicKey.Leashed = () => leashed;
        Win32PanicKey.LeashHoldDue += Due;
        Win32PanicKey.LeashHolding += Holding;
        Win32PanicKey.PanicReleased += Released;
        try
        {
            Win32PanicKey.Bind(() => "F24", () => presses++);
            Win32PanicKey.OnUp(VirtualKeys.F24);
            released = 0;

            // Seven seconds held, a repeat every 33 ms.
            for (var ms = 0; ms <= 7000; ms += 33) Win32PanicKey.OnDown(VirtualKeys.F24, T0.AddMilliseconds(ms));
            Assert.Equal(1, presses);          // the first down is the panic press, at once; never another
            Assert.Equal(asks, due);           // asked once at five seconds, only while leashed
            Assert.Equal(leashed, holding > 0);

            Win32PanicKey.OnUp(VirtualKeys.F24);
            Assert.Equal(1, released);
            Win32PanicKey.OnDown(VirtualKeys.F24, T0.AddSeconds(9));
            Assert.Equal(2, presses);          // a new press after release always panics
            Assert.Equal(asks, due);
        }
        finally
        {
            Win32PanicKey.OnUp(VirtualKeys.F24);
            Win32PanicKey.Leashed = oldLeashed;
            Win32PanicKey.LeashHoldDue -= Due;
            Win32PanicKey.LeashHolding -= Holding;
            Win32PanicKey.PanicReleased -= Released;
        }
    }

    [Fact]
    public void ALeashedProbeThatThrows_NeverCostsThePress()
    {
        int presses = 0;
        var oldLeashed = Win32PanicKey.Leashed;
        Win32PanicKey.Leashed = () => throw new InvalidOperationException("boom");
        try
        {
            Win32PanicKey.Bind(() => "F24", () => presses++);
            Win32PanicKey.OnUp(VirtualKeys.F24);
            Win32PanicKey.OnDown(VirtualKeys.F24, T0);
            Assert.Equal(1, presses);
        }
        finally
        {
            Win32PanicKey.OnUp(VirtualKeys.F24);
            Win32PanicKey.Leashed = oldLeashed;
        }
    }
}
