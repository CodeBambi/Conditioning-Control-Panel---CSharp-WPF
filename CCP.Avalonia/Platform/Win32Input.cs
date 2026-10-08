using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Input;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Whether a GLOBAL panic listener can deliver the key right now, on any platform (WPF
/// PanicHook.IsInstalled): the X11 listener or the Windows hook, bound to a key that can fire.</summary>
internal static class PanicListeners
{
    internal static bool Live =>
        (X11PanicKey.IsListening && X11PanicKey.BoundKeycode != 0)
        || (Win32PanicKey.IsListening && Win32PanicKey.BoundVirtualKey != 0);
}

/// <summary>
/// The Windows half of the shell's global key wiring (WPF MainWindow.xaml.cs:363 + OnGlobalKeyPressed):
/// starts <see cref="Win32PanicKey"/> with the same press entry point X11PanicKey feeds, the Lockdown
/// tripwire for a blocked system key, and the unfocused Esc door for a NON-strict mandatory video.
/// Everything the hook raises is posted; nothing runs inside the callback.
/// </summary>
internal static class Win32Input
{
    private static bool _escDown, _wired;

    /// <summary>MainShellWindow.StartPanicKey on Windows (the desktop path only, never tests or renders).
    /// False when the hook did not install.</summary>
    internal static bool Start(Action onPanicPress)
    {
        if (_wired) return Win32PanicKey.IsListening;
        _wired = true;
        Win32PanicKey.SystemKeyBlocked += () => Dispatcher.UIThread.Post(() =>
        {
            try { LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.SystemKey); }
            catch (Exception ex) { Log.Debug(ex, "Lockdown system-key tripwire failed"); }
        });
        Win32PanicKey.KeyDown += (vk, _) =>
        {
            if (vk != VirtualKeys.Escape || _escDown) return;   // a held Esc is one press here too
            _escDown = true;
            Dispatcher.UIThread.Post(EscapeDoor);
        };
        Win32PanicKey.KeyUp += (vk, _) => { if (vk == VirtualKeys.Escape) _escDown = false; };
        return Win32PanicKey.Start(() => CoreSettings.Current.PanicKey, () =>
        {
            Log.Information("Panic trigger: WH_KEYBOARD_LL key press");
            Dispatcher.UIThread.Post(onPanicPress);
        });
    }

    /// <summary>WPF OnLockdownActivated/Deactivated (MainWindow.Lab.cs:621/714): the system keys are
    /// blocked only while a Lockdown runs with its "block system keys" safety on. A no-op until
    /// <see cref="Start"/> ran, so a test or a render never hooks the desk's keyboard.</summary>
    internal static void ApplyLockdown(bool lockdownActive, bool blockSystemKeys)
    {
        if (!OperatingSystem.IsWindows() || !_wired) return;
        var want = lockdownActive && blockSystemKeys;
        if (Win32PanicKey.SuppressSystemKeys != want) Win32PanicKey.SuppressSystemKeys = want;
    }

    /// <summary>WPF OnGlobalKeyPressed's first branch (MainWindow.xaml.cs:885): a plain Esc reaches a
    /// non-strict mandatory video that lost focus. When Esc is the panic key and the panic can run, the
    /// press is the panic's (PanicPolicy.EscapeDismissesVideo); a lock card or the palette owns Esc first.</summary>
    internal static void EscapeDoor()
    {
        try
        {
            var s = CoreSettings.Current;
            var video = MandatoryVideoOverlay.Instance;
            if (!PanicPolicy.EscapeDismissesVideo(video.WantsGlobalEscape, LockCardWindow.IsAnyOpen(), SettingsPaletteWindow.IsOpen,
                    s.PanicKeyEnabled, s.PanicKey, MainShellWindow.LockdownActive)) return;
            if (video.TryEscapeFromGlobalKey()) Log.Information("Global Esc: unfocused video dismissed or grace-paused");
        }
        catch (Exception ex) { Log.Warning(ex, "Global Esc: video dismiss failed"); }
    }
}
