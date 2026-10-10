using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Services.Webcam;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    // The camera start/stop shortcut (WPF MainWindow.SessionIO.cs ApplyCameraShortcutTo :1355,
    // ApplyGlobalCameraHotkey :1411, FormatCameraShortcut :1449, BtnCameraShortcut_Click :1485;
    // MainWindow.BlinkTrainer.cs ToggleWebcamFromHotkey :350). Default Ctrl+Alt+K.
    // In-window: a KeyBinding on the shell, both OSes. From any app: the Windows key hook, when the
    // row's "global" box is on.
    // not ported: the from-any-app half on Linux (X11 has no chord listener here but the EMI summon one).
    public partial class MainShellWindow
    {
        private sealed class ToggleCameraCommandImpl : ICommand
        {
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter)
            {
                try { Current?.ToggleWebcamFromHotkey(); } catch (Exception ex) { Log.Debug("Camera shortcut: {E}", ex.Message); }
            }
        }

        internal static readonly ICommand ToggleCameraCommand = new ToggleCameraCommandImpl();

        /// <summary>Tests count toggles instead of touching the tracker.</summary>
        internal static Action? CameraToggleOverride;

        internal static (Key Key, KeyModifiers Mods) CurrentCameraShortcut()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            var keyName = string.IsNullOrWhiteSpace(s?.CameraShortcutKey) ? "K" : s!.CameraShortcutKey;
            if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key)) key = Key.K;
            var mods = KeyModifiers.None;
            foreach (var part in (s?.CameraShortcutModifiers ?? "Control,Alt").Split(new[] { ',', '+', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                switch (part.Trim().ToLowerInvariant())
                {
                    case "control": case "ctrl": mods |= KeyModifiers.Control; break;
                    case "alt": mods |= KeyModifiers.Alt; break;
                    case "shift": mods |= KeyModifiers.Shift; break;
                    case "windows": case "win": case "meta": mods |= KeyModifiers.Meta; break;
                }
            if (mods == KeyModifiers.None) mods = KeyModifiers.Control | KeyModifiers.Alt;   // WPF ParseModifiers fallback
            return (key, mods);
        }

        /// <summary>"Ctrl+Alt+K", for the two shortcut pills.</summary>
        internal static string FormatCameraShortcut()
        {
            var (key, mods) = CurrentCameraShortcut();
            var parts = new List<string>();
            if ((mods & KeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((mods & KeyModifiers.Meta) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts);
        }

        /// <summary>Rebuilds the binding from the setting; repeated calls never stack.</summary>
        internal static void ApplyCameraShortcutTo(Window? window)
        {
            if (window == null) return;
            var (key, mods) = CurrentCameraShortcut();
            for (int i = window.KeyBindings.Count - 1; i >= 0; i--)
                if (ReferenceEquals(window.KeyBindings[i].Command, ToggleCameraCommand)) window.KeyBindings.RemoveAt(i);
            window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, mods), Command = ToggleCameraCommand });
        }

        private bool _cameraHookWired;

        private void InitializeCameraShortcut()
        {
            ApplyCameraShortcutTo(this);
            if (!OperatingSystem.IsWindows() || _cameraHookWired) return;
            _cameraHookWired = true;
            Platform.Win32PanicKey.KeyDown += OnGlobalKeyForCamera;
            Closed += (_, _) => Platform.Win32PanicKey.KeyDown -= OnGlobalKeyForCamera;
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);

        // Listener thread: compare and post, never work here.
        private void OnGlobalKeyForCamera(int vk, string? name)
        {
            try
            {
                if (CoreSettings.Current.CompanionPrompt?.CameraShortcutGlobal != true) return;
                var (key, mods) = CurrentCameraShortcut();
                if (!string.Equals(name, key.ToString(), StringComparison.OrdinalIgnoreCase)) return;
                if (!OperatingSystem.IsWindows()) return;
                bool Down(int k) => GetAsyncKeyState(k) < 0;
                var held = KeyModifiers.None;
                if (Down(0x11)) held |= KeyModifiers.Control;
                if (Down(0x12)) held |= KeyModifiers.Alt;
                if (Down(0x10)) held |= KeyModifiers.Shift;
                if (Down(0x5B) || Down(0x5C)) held |= KeyModifiers.Meta;
                if (held != mods) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsActive) return;   // the in-window binding already took this press
                    ToggleWebcamFromHotkey();
                });
            }
            catch (Exception ex) { Log.Debug("Camera shortcut hook: {E}", ex.Message); }
        }

        /// <summary>WPF ToggleWebcamFromHotkey: no-op without an engine; stop when running; else start,
        /// after the consent dialog when consent is not current (the window is raised first so the modal
        /// is not lost behind the tray). The camera rides the "camera" panic surface (StopCameraForPanic).</summary>
        internal async void ToggleWebcamFromHotkey()
        {
            try
            {
                if (CameraToggleOverride is { } o) { o(); return; }
                if (!CoreWebcam.IsAvailable || _windowClosed) return;
                var tracker = Platform.WebcamTracker.Instance;
                if (tracker.IsRunning || tracker.IsStarting)
                {
                    await tracker.StopAsync();
                    App.Notifications.Show(global::ConditioningControlPanel.Localization.Loc.Get("rf_webcam_stopped"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(3));
                    return;
                }
                if (!WebcamConsent.IsCurrent(CoreSettings.Current))
                {
                    ShowFromTray();
                    await new WebcamConsentDialog().ShowDialogSafe(this);
                    if (!WebcamConsent.IsCurrent(CoreSettings.Current)) return;
                }
                bool started = await tracker.StartAsync();
                App.Notifications.Show(started ? global::ConditioningControlPanel.Localization.Loc.Get("rf_webcam_tracking") : (tracker.LastError ?? global::ConditioningControlPanel.Localization.Loc.Get("rf_webcam_error")),
                    Helpers.NotificationType.Info, TimeSpan.FromSeconds(started ? 3 : 6));
            }
            catch (Exception ex) { Log.Warning(ex, "ToggleWebcamFromHotkey failed"); }
        }

        /// <summary>WPF BtnCameraShortcut_Click: the capture dialog the chat shortcut uses, then re-apply
        /// without a restart. Returns true when the combo changed hands.</summary>
        internal static async Task<bool> RebindCameraShortcutAsync(Window owner)
        {
            var prompt = CoreSettings.Current.CompanionPrompt;
            if (prompt == null) return false;
            var dlg = new ChatShortcutCaptureDialog { GlobalHotkey = prompt.CameraShortcutGlobal };
            if (!await dlg.ShowDialogSafe<bool>(owner)) return false;
            if (dlg.ResetToDefault)
            {
                prompt.CameraShortcutKey = "K";
                prompt.CameraShortcutModifiers = "Control,Alt";
            }
            else
            {
                prompt.CameraShortcutKey = dlg.CapturedKey.ToString();
                prompt.CameraShortcutModifiers = AvatarTube.AvatarTubeWindow.SerializeModifiers(dlg.CapturedModifiers);
            }
            prompt.CameraShortcutGlobal = dlg.GlobalHotkey;
            CoreSettings.Save();
            ApplyCameraShortcutTo(owner);
            if (!ReferenceEquals(owner, Current)) ApplyCameraShortcutTo(Current);
            Log.Information("Camera shortcut rebound to {Combo}", FormatCameraShortcut());
            return true;
        }
    }
}
