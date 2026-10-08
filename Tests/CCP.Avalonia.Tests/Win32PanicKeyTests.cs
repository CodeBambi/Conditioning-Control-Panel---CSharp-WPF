using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Input;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Windows panic hook's dispatch, driven without installing a hook (OnDown / OnUp are
/// what the WH_KEYBOARD_LL callback calls): the bound key fires once per press, a held key is one
/// press, every key's down and up are raised, a rebind applies on the next key. Plus the Lockdown
/// consent line. The real hook is proven by --win-panic-check.</summary>
[Collection("Win32PanicKey")]
public sealed class Win32PanicKeyTests
{
    private static readonly DateTime T0 = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BoundKeyFiresOncePerPressAndAHeldKeyIsOnePress()
    {
        var key = "F24";
        var presses = 0;
        var downs = new List<(int, string?)>();
        var ups = new List<(int, string?)>();
        void D(int vk, string? n) => downs.Add((vk, n));
        void U(int vk, string? n) => ups.Add((vk, n));
        Win32PanicKey.KeyDown += D;
        Win32PanicKey.KeyUp += U;
        try
        {
            Win32PanicKey.Bind(() => key, () => presses++);
            Win32PanicKey.OnUp(VirtualKeys.F24);   // clear any hold another test left
            Assert.Equal(VirtualKeys.F24, Win32PanicKey.BoundVirtualKey);

            Win32PanicKey.OnDown(0x41, T0);   // another key: raised, not a press
            Assert.Equal(0, presses);

            for (var i = 0; i < 30; i++) Win32PanicKey.OnDown(VirtualKeys.F24, T0.AddMilliseconds(33 * i));
            Assert.Equal(1, presses);   // one second held = one press
            Win32PanicKey.OnUp(VirtualKeys.F24);
            Win32PanicKey.OnDown(VirtualKeys.F24, T0.AddSeconds(2));
            Assert.Equal(2, presses);   // released and pressed again = a new press

            Assert.Contains((0x41, "A"), downs);
            Assert.Contains((VirtualKeys.F24, "F24"), downs);
            Assert.Contains((VirtualKeys.F24, "F24"), ups);

            key = "Pause";   // a rebind is read on the next key
            Win32PanicKey.OnUp(VirtualKeys.F24);
            Win32PanicKey.OnDown(VirtualKeys.F24, T0.AddSeconds(4));
            Assert.Equal(2, presses);
            Win32PanicKey.OnDown(0x13, T0.AddSeconds(5));
            Assert.Equal(3, presses);

            key = "";   // unbound: nothing fires
            Win32PanicKey.OnDown(0x13, T0.AddSeconds(9));
            Assert.Equal(0, Win32PanicKey.BoundVirtualKey);
            Assert.Equal(3, presses);
        }
        finally
        {
            Win32PanicKey.KeyDown -= D;
            Win32PanicKey.KeyUp -= U;
            Win32PanicKey.Bind(() => null, () => { });
        }
    }

    [Fact]
    public void LockdownConsentNamesTheBlockedKeysOnlyWhereTheyAreBlocked()
    {
        var cfg = new ConditioningControlPanel.Models.AppSettings { LockdownBlockSystemKeys = true };
        var win = LockdownTabView.LockdownWarning(20, cfg, windows: true);
        Assert.Contains("- Alt+F4, Alt+Tab, the Windows key and Ctrl+Esc will be BLOCKED", win);
        Assert.Contains("Ctrl+Alt+Del", win);
        var linux = LockdownTabView.LockdownWarning(20, cfg, windows: false);
        Assert.DoesNotContain("BLOCKED", linux);
        Assert.Contains("system monitor", linux);
        cfg.LockdownBlockSystemKeys = false;
        Assert.DoesNotContain("BLOCKED", LockdownTabView.LockdownWarning(20, cfg, windows: true));
    }
}
