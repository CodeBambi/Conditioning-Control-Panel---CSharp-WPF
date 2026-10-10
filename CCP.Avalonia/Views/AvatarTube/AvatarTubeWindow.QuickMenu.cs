// The tube's right-click menu: every handler and the state refresh, ported from WPF 7.1.5
// ConditioningControlPanel/AvatarTube/AvatarTubeWindow.ChatInput.cs
//   MenuItemEngine_Click :1059 (guards #479), IsEngineStopLocked :1096, MenuItemTriggerMode_Click :1101,
//   MenuItemBambiTakeover_Click :1121, MenuItemTalkToBambi_Click :1181, MenuItemMute_Click :1325,
//   MenuItemMuteWhispers_Click :1361, MenuItemPauseBrowser_Click :1383, UpdateQuickMenuState :1426.
// Detach / Attach are wired in Windowing.cs, the personality submenu in ContentGates.cs.

using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private static readonly IBrush MenuWhite = Brushes.White;
        private static readonly IBrush MenuRed = new SolidColorBrush(Color.FromRgb(255, 99, 71));
        private static readonly IBrush MenuGreen = new SolidColorBrush(Color.FromRgb(144, 238, 144));
        private static readonly IBrush MenuPatreon = new SolidColorBrush(Color.FromRgb(155, 89, 182));
        private static readonly IBrush MenuLocked = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x70));

        private MenuItem? QuickItem(string name) => this.FindControl<MenuItem>(name);

        private MainShellWindow? QuickShell => _parentWindow as MainShellWindow ?? MainShellWindow.Current;

        private static bool RemoteControllerConnected =>
            RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected;

        /// <summary>Called once from the ctor: one Click per item, each guarded so a handler can
        /// never throw into the menu.</summary>
        private void WireQuickMenu()
        {
            Wire("MenuItemTalkToBambi", OpenChatInput);
            Wire("MenuItemEngine", OnMenuEngine);
            Wire("MenuItemTriggerMode", OnMenuTriggerMode);
            Wire("MenuItemBambiTakeover", OnMenuTakeover);
            Wire("MenuItemMute", OnMenuMute);
            Wire("MenuItemMuteWhispers", OnMenuMuteWhispers);
            // Pause browser (WPF MenuItemPauseBrowser_Click :1383): mute + pause through the shell's
            // one seam (MainShellWindow.SetBrowserPaused over Views/Controls/WebHostMedia.cs).
            Wire("MenuItemPauseBrowser", OnMenuPauseBrowser);
            UpdateQuickMenuState();
        }

        private void Wire(string name, Action handler)
        {
            if (QuickItem(name) is not { } item) { Log.Warning("AvatarTubeWindow: {Item} not found", name); return; }
            item.Click += (_, _) =>
            {
                try { handler(); }
                catch (Exception ex) { Log.Warning(ex, "AvatarTubeWindow: {Item} click failed", name); }
            };
        }

        /// <summary>WPF IsEngineStopLocked (#479): Lockdown, a strict-locked video on screen, or a
        /// strict-locked bubble count. Same three terms as the shell's VoiceStopLocked.</summary>
        internal static bool IsEngineStopLocked() =>
            MainShellWindow.LockdownActive
            || (CoreEngine.Video is { IsPlaying: true, IsStrict: true })
            || (BubbleCountWindow.IsAnyOpen() && CoreSettings.Current.BubbleCountStrictLock);

        /// <summary>WPF MenuItemEngine_Click: no-op under a remote controller; a Stop while locked is
        /// refused and counted as the same escape as the main Stop button.</summary>
        internal void OnMenuEngine()
        {
            if (QuickShell is not { } shell) return;
            if (RemoteControllerConnected) return;
            if (CoreEngine.IsRunning)
            {
                if (IsEngineStopLocked())
                {
                    try { LockdownService.Current?.NotifyEscapeAttempt(EscapeKinds.Stop); } catch { }
                    Giggle("nuh-uh~ no stopping now, you're locked in~");
                    return;
                }
                // SEAM(shell): BtnStart_Click also calls the private NoteSchedulerManualStop; WPF's
                // tube menu calls StopEngine alone, so this matches WPF as it stands.
                MainShellWindow.StopEngine();
                Giggle("Engine stopped~");
            }
            else
            {
                shell.StartEngine();
                Giggle("Engine started! *giggles*");
            }
            UpdateQuickMenuState();
        }

        /// <summary>WPF MenuItemTriggerMode_Click.</summary>
        internal void OnMenuTriggerMode()
        {
            var on = !CoreSettings.Current.TriggerModeEnabled;
            CoreSettings.Current.TriggerModeEnabled = on;
            CoreSettings.Save();
            RestartTriggerTimer();
            UpdateQuickMenuState();
            QuickShell?.SyncHero();
            Giggle(on ? "Trigger mode ON~" : "Trigger mode off~");
        }

        /// <summary>WPF MenuItemBambiTakeover_Click. The switch itself goes through the shell's
        /// SetAutonomyEnabled, which carries the Lockdown refusal (#514) and starts or stops her.</summary>
        internal void OnMenuTakeover()
        {
            var s = CoreSettings.Current;
            if (!AutonomyScheduler.HasEntitlement)   // premium, or the ? box's free day (#978)
            {
                Giggle("This is Patreon only~");
                return;
            }
            if (QuickShell is not { } shell) return;
            var wasOn = s.AutonomyModeEnabled;
            if (wasOn && MainShellWindow.LockdownActive)
            {
                // The shell refuses a RUNNING Takeover with its dialog; she says it too, as WPF.
                var shown = shell.SetAutonomyEnabled(false);
                if (shown) { Giggle("Lockdown is active~ you can't turn me off~"); UpdateQuickMenuState(); return; }
            }
            else
            {
                // Choosing it from her own menu is the consent (WPF auto-grants here).
                if (!wasOn && !s.AutonomyConsentGiven) s.AutonomyConsentGiven = true;
                shell.SetAutonomyEnabled(!wasOn);
            }
            var nowOn = s.AutonomyModeEnabled;
            if (nowOn && !wasOn) Giggle(App.Mods?.GetAutonomyOnPhrase() ?? "Bambi takes over~ *giggles*");
            else if (!nowOn && wasOn) Giggle("Takeover mode off~");
            UpdateQuickMenuState();
        }

        /// <summary>WPF MenuItemMute_Click: her VOICE only; the bubble in flight stays (#445). The
        /// tube reads the setting live (<see cref="IsMuted"/>), so the flip is obeyed by the next line;
        /// the voice playing right now is cut so the click is heard at once.</summary>
        internal void OnMenuMute()
        {
            var muted = !CoreSettings.Current.AvatarMuted;
            CoreSettings.Current.AvatarMuted = muted;
            CoreSettings.Save();
            if (muted) { try { StopSpokenAudio(); } catch (Exception ex) { Log.Debug(ex, "mute: stop voice"); } }
            UpdateQuickMenuState();
            QuickShell?.SyncHero();   // the Companion hero card's mute switch re-reads the setting
        }

        /// <summary>WPF MenuItemMuteWhispers_Click: the dedicated MUTE, never the SubAudioEnabled
        /// master enable (a session prescribes and locks that one; a mute must stay available).</summary>
        internal void OnMenuMuteWhispers()
        {
            CoreSettings.Current.SubAudioMuted = !CoreSettings.Current.SubAudioMuted;
            CoreSettings.Save();
            UpdateQuickMenuState();
        }

        /// <summary>WPF MenuItemPauseBrowser_Click: flip the pause, the header follows.</summary>
        internal void OnMenuPauseBrowser()
        {
            if (QuickShell is { } shell) _ = shell.SetBrowserPaused(!shell.BrowserPaused);
            UpdateQuickMenuState();
        }

        /// <summary>WPF UpdateQuickMenuState: every item's header, colour and enabled state. Runs on
        /// the menu's Opened and after every click.</summary>
        internal void UpdateQuickMenuState()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(UpdateQuickMenuState); return; }
            var s = CoreSettings.Current;
            var remote = RemoteControllerConnected;

            if (QuickItem("MenuItemTalkToBambi") is { } talk)
            {
                var label = App.Mods?.GetTalkToLabel() ?? Loc.Get("menu_talk_to_companion");
                var available = App.Ai?.IsAvailable == true;
                talk.IsEnabled = available;
                talk.Header = Loc.GetF(available ? "menu_talk_to_format" : "menu_talk_to_locked_format", label);
                talk.Foreground = available ? AccentBrush : MenuPatreon;
            }

            var running = CoreEngine.IsRunning;
            if (QuickItem("MenuItemEngine") is { } engine)
            {
                engine.Header = Loc.Get(running ? "menu_stop_engine" : "menu_start_engine");
                engine.Foreground = running ? MenuRed : MenuGreen;
                // Disabled while a Stop would be refused anyway; starting is always allowed.
                engine.IsEnabled = !(running && IsEngineStopLocked());
                if (remote) { engine.IsEnabled = false; engine.Header = Loc.Get("label_start_engine"); engine.Foreground = MenuLocked; }
            }

            if (QuickItem("MenuItemTriggerMode") is { } trigger)
            {
                var on = s.TriggerModeEnabled;
                trigger.Header = Loc.Get(on ? "menu_trigger_mode_on" : "menu_trigger_mode_off");
                trigger.Foreground = remote ? MenuLocked : on ? MenuGreen : MenuWhite;
                trigger.IsEnabled = !remote;
            }

            if (QuickItem("MenuItemBambiTakeover") is { } takeover)
            {
                var available = AutonomyScheduler.HasEntitlement;
                var on = s.AutonomyModeEnabled;
                var name = App.Mods?.GetTakeoverLabel() ?? Loc.Get("menu_takeover");
                takeover.Header = !available ? Loc.GetF("menu_takeover_locked_format", name)
                    : Loc.GetF(on ? "menu_takeover_on_format" : "menu_takeover_off_format", name);
                takeover.Foreground = remote ? MenuLocked : !available ? MenuPatreon : on ? AccentBrush : MenuWhite;
                takeover.IsEnabled = available && !remote;
            }

            if (QuickItem("MenuItemMute") is { } mute)
            {
                var muted = s.AvatarMuted;
                mute.Header = Loc.Get(muted ? "menu_mute_avatar_on" : "menu_mute_avatar_off");
                mute.Foreground = remote ? MenuLocked : muted ? MenuRed : MenuWhite;
                mute.IsEnabled = !remote;
            }

            if (QuickItem("MenuItemPauseBrowser") is { } pauseBrowser)
            {
                pauseBrowser.Header = Loc.Get(QuickShell?.BrowserPaused == true ? "menu_resume_browser" : "menu_pause_browser");
                pauseBrowser.Foreground = MenuWhite;
            }

            if (QuickItem("MenuItemMuteWhispers") is { } whispers)
            {
                var muted = s.SubAudioMuted;
                whispers.Header = Loc.Get(muted ? "menu_mute_whispers_on" : "menu_mute_whispers_off");
                whispers.Foreground = remote ? MenuLocked : muted ? MenuRed : MenuWhite;
                whispers.IsEnabled = !remote;
            }
        }
    }
}
