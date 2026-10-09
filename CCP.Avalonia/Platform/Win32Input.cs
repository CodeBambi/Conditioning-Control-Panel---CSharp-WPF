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
    private static bool _escDown, _pauseDown, _wired;

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
            OnPauseKeyDown(vk);
            if (vk != VirtualKeys.Escape || _escDown) return;   // a held Esc is one press here too
            _escDown = true;
            Dispatcher.UIThread.Post(EscapeDoor);
        };
        Win32PanicKey.KeyUp += (vk, _) =>
        {
            if (vk == VirtualKeys.Escape) _escDown = false;
            if (vk != 0 && vk == VirtualKeys.Of(CoreSettings.Current.PauseKey)) _pauseDown = false;
        };
        return Win32PanicKey.Start(() => CoreSettings.Current.PanicKey, () => OnPanicPress(onPanicPress));
    }

    /// <summary>One press of the bound panic key, on the listener thread (WPF OnGlobalKeyPressed's panic
    /// branch, MainWindow.xaml.cs:921-951). Reads only, then queues; never works here.</summary>
    internal static void OnPanicPress(Action onPanicPress)
    {
        var s = CoreSettings.Current;
        // Panic off, rebinding or Lockdown: the handler refuses on the UI thread as before. No watchdog:
        // WPF never armed one there, so the off-thread teardown cannot outrank those rules.
        if (!s.PanicKeyEnabled || MainShellWindow.CapturingPanicKey || MainShellWindow.LockdownActive
            || Views.Controls.AppSettings.DevicesSettingsSection.CapturingPauseKey)
        {
            // SEAM: LeashTaskHost.OnPanicPress(false)   (WPF LeashPanicKeyWhilePanicOff: while leashed,
            // a press the panic cannot run still parks the leash task; posted to the UI thread.)
            Dispatcher.UIThread.Post(onPanicPress);
            return;
        }
        // An Escape aimed at a CCP surface that drops or closes on it is that surface's (TAB-8 / DESK-3).
        if (EscapeClaim.TakenBySurface(s.PanicKeyEnabled, s.PanicKey, LockCardWindow.IsAnyOpen(), DateTime.UtcNow)) return;
        Log.Information("Panic trigger: WH_KEYBOARD_LL key press");
        PanicWatchdog.QueueWatched(onPanicPress);   // #919b: torn down off-thread if the UI never runs it
    }

    /// <summary>WPF's optional Pause key (v6.8.5, MainWindow.xaml.cs:955): parks a playing mandatory video
    /// behind the grace card. Unbound by default; the panic key always wins a shared binding; Lockdown
    /// ignores it (WPF returns before it); a held key is one press. No watchdog. True = queued.</summary>
    internal static bool OnPauseKeyDown(int vk)
    {
        var s = CoreSettings.Current;
        int pauseVk = VirtualKeys.Of(s.PauseKey);
        if (vk == 0 || vk != pauseVk || _pauseDown) return false;
        _pauseDown = true;
        if (PanicPolicy.PauseKeyIsShadowedByPanicKey(s.PanicKey, s.PanicKeyEnabled, s.PauseKey)
            || (s.PanicKeyEnabled && pauseVk == VirtualKeys.Of(s.PanicKey))
            || MainShellWindow.CapturingPanicKey || MainShellWindow.LockdownActive
            || Views.Controls.AppSettings.DevicesSettingsSection.CapturingPauseKey)
            return false;
        Log.Information("Pause key {Key} received - queueing the video grace pause", s.PauseKey);
        Dispatcher.UIThread.Post(() =>
        {
            try { MandatoryVideoOverlay.Instance.TryGracePause(fromPanicKey: false); }
            catch (Exception ex) { Log.Warning(ex, "Pause key: grace pause failed"); }
        });
        return true;
    }

    /// <summary>Test seam: forget a held pause key.</summary>
    internal static void ResetPauseKeyForTest() => _pauseDown = false;

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
