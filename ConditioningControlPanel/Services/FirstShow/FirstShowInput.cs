using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ConditioningControlPanel.Services.FirstShow;

internal static class FirstShowInput
{
    // A painted layered window catches the mouse even when its WPF drawing ignores hit tests.
    // Emi and speech are separate owned HWNDs and keep their own interactive styles.
    internal static void PassThrough(Window stage)
    {
        stage.IsHitTestVisible = false;
        var handle = new WindowInteropHelper(stage).EnsureHandle();
        SetWindowLong(handle,-20,GetWindowLong(handle,-20) | 0x20 | 0x08000000);
        SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr handle,int index,int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle,IntPtr after,int x,int y,int w,int h,uint flags);
}
