using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// A moment: the app telling EMI something happened. Chunk B3 turns these into lines; chunk B1
/// only fires them, so the wiring exists before there is anything to say.
/// </summary>
/// <param name="Id">The moment id, e.g. <c>sessionEnd</c>. Ids are the vocabulary the line pools key off.</param>
/// <param name="Context">Optional payload (an XP number, a level, a target id). May be null.</param>
public sealed record EmiMoment(string Id, object? Context);

/// <summary>
/// EMI Desk's facade: <c>App.EmiDesk</c>. Owns the widget window's lifetime, the summon hotkey,
/// the moment bus and the avatar-mute arbitration.
///
/// She is SUMMONED, not always on. Everything here is built so that "she is not out" is the cheap
/// path: no window until the first summon, no timers while she is away, and every gate the rest of
/// the app asks (<see cref="AvatarMuted"/>) short-circuits on <see cref="IsOut"/>.
/// </summary>
public sealed class EmiDeskService : IDisposable
{
    /// <summary>The default summon chord. A chord is required; bare keys are refused.</summary>
    public const string DefaultHotkey = "Ctrl+Alt+E";

    private EmiDeskWindow? _window;
    private bool _muteAccepted;
    private bool _mutePromptShownThisSession;
    private bool _hotkeyArmed;
    private bool _disposed;

    // ---------------------------------------------------------------- state

    /// <summary>True while she is on screen (including her intro and outro).</summary>
    public bool IsOut { get; private set; }

    /// <summary>The widget window, or null before her first summon. Chunk B2 / B3 reach her through this.</summary>
    public EmiDeskWindow? Window => _window;

    /// <summary>Raised whenever <see cref="IsOut"/> changes. The dock chip listens.</summary>
    public event EventHandler<bool>? OutChanged;

    /// <summary>
    /// Raised for every <see cref="Fire"/>. Chunk B3 subscribes and decides whether the moment is
    /// worth a line. B1 raises and forgets: no queue, no backlog, nothing to drain.
    /// </summary>
    public event EventHandler<EmiMoment>? MomentFired;

    /// <summary>Raised for every <see cref="NoteOpen"/>. Chunk B2's suggester listens.</summary>
    public event EventHandler<string>? TargetOpened;

    /// <summary>
    /// Raised when the avatar tube was about to speak. Fires whether or not the mute swallowed it,
    /// so chunk B3 can keep EMI from talking over her.
    /// </summary>
    public event EventHandler? AvatarSpeaking;

    // ---------------------------------------------------------------- mute arbitration

    /// <summary>
    /// THE gate the avatar's speech paths ask. True only while EMI is actually out, the user has
    /// the setting on, AND the user agreed (or said "do not ask", which counts as agreeing from
    /// then on). Two voices at once is the failure mode this whole feature exists to avoid, and a
    /// mute the user never chose is the second one, so both halves are required.
    /// </summary>
    public bool AvatarMuted
    {
        get
        {
            try
            {
                if (!IsOut) return false;
                var s = App.Settings?.Current;
                if (s == null || !s.EmiDeskMuteAvatar) return false;
                return _muteAccepted;
            }
            catch { return false; }
        }
    }

    /// <summary>True while the avatar tube has a bubble on screen. Chunk B3 waits rather than overlapping.</summary>
    public bool TubeBubbleLive
    {
        get
        {
            try { return App.AvatarWindow?.HasBubbleUp == true; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Called from the tube's speech chokepoints the instant before a bubble goes up, muted or not.
    /// Keep it cheap: this runs on every giggle.
    /// </summary>
    public void NoteAvatarSpeaking()
    {
        try { AvatarSpeaking?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] AvatarSpeaking handler threw"); }
    }

    // ---------------------------------------------------------------- summon / dismiss

    /// <summary>Summon her if she is away, send her away if she is out.</summary>
    public void Toggle()
    {
        if (IsOut) Dismiss();
        else Summon();
    }

    /// <summary>
    /// Bring her out. Safe to call when she is already out (no-op) and when the feature is off
    /// (logged no-op). Builds the window on first use.
    /// </summary>
    public void Summon(string? why = null)
    {
        try
        {
            if (_disposed) return;
            var s = App.Settings?.Current;
            if (s != null && !s.EmiDeskEnabled)
            {
                Log.Debug("[EmiDesk] summon ignored: EmiDeskEnabled is off");
                return;
            }
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(() => Summon(why)));
                return;
            }
            if (IsOut) return;

            var win = EnsureWindow();
            if (win == null) return;

            IsOut = true;
            MaybeAskAboutMuting();

            win.RestorePlacement();
            win.Show();
            win.RunSummon();

            var st = EmiState.Current;
            bool first = !st.FirstBootSeen;
            if (first)
            {
                st.FirstBootSeen = true;
                EmiState.SaveSoon();
            }

            RaiseOutChanged();
            Log.Information("[EmiDesk] summoned ({Why}), firstBoot={First}", why ?? "user", first);
            Fire(first ? "deskFirstBoot" : "deskSummon");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] Summon failed");
        }
    }

    /// <summary>Send her away. Safe to call when she is not out.</summary>
    public void Dismiss()
    {
        try
        {
            if (_disposed) return;
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            if (!disp.CheckAccess())
            {
                disp.BeginInvoke(new Action(Dismiss));
                return;
            }
            if (!IsOut || _window == null) return;

            Fire("deskDismiss");
            _window.RunDismiss(() =>
            {
                IsOut = false;
                RaiseOutChanged();
                Log.Information("[EmiDesk] dismissed");
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] Dismiss failed");
            IsOut = false;
            RaiseOutChanged();
        }
    }

    private EmiDeskWindow? EnsureWindow()
    {
        try
        {
            if (_window != null) return _window;
            _window = new EmiDeskWindow();
            // Realise the HWND once, off screen and unlit, so SourceInitialized can apply
            // WS_EX_TOOLWINDOW before she is ever visible: applying it later makes her flash into
            // the taskbar for a frame.
            _window.Opacity = 0;
            _window.Show();
            _window.Hide();
            _window.Opacity = 1;
            _window.Closed += (_, _) =>
            {
                _window = null;
                IsOut = false;
                RaiseOutChanged();
            };
            return _window;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[EmiDesk] could not build the widget window");
            _window = null;
            return null;
        }
    }

    private void RaiseOutChanged()
    {
        try { OutChanged?.Invoke(this, IsOut); }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] OutChanged handler threw"); }
    }

    // ---------------------------------------------------------------- moments

    /// <summary>
    /// Tell EMI something happened. A no-op in chunk B1 beyond the log line and the event: the
    /// pools, the cooldowns and the picker are chunk B3's. Call sites can therefore land NOW and
    /// stay unchanged when the voice arrives.
    /// </summary>
    public void Fire(string momentId, object? ctx = null)
    {
        if (string.IsNullOrWhiteSpace(momentId)) return;
        try
        {
            Log.Debug("[EmiDesk] moment {Moment} (out={Out})", momentId, IsOut);
            MomentFired?.Invoke(this, new EmiMoment(momentId, ctx));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] Fire({Moment}) failed", momentId);
        }
    }

    /// <summary>
    /// Tell EMI a target was opened, however it was opened (her ring, the nav rail, a hotkey). The
    /// suggester learns from ALL of them, so the ring reflects what you actually use rather than
    /// what you last used her for.
    /// </summary>
    public void NoteOpen(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return;
        try
        {
            EmiState.NoteUsage(targetId);
            Log.Debug("[EmiDesk] target opened: {Target}", targetId);
            TargetOpened?.Invoke(this, targetId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] NoteOpen({Target}) failed", targetId);
        }
    }

    // ---------------------------------------------------------------- the mute prompt

    /// <summary>
    /// Ask, at most once per app session and only when something that actually talks is live,
    /// whether the avatar should sit out while EMI is here. Dismissing the dialog keeps the avatar:
    /// silence is never assumed to be consent to being silenced.
    /// </summary>
    private void MaybeAskAboutMuting()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;

            if (!s.EmiDeskMuteAvatar)
            {
                _muteAccepted = false;
                return;
            }
            if (s.EmiDeskMuteDontAsk)
            {
                // "Do not ask again" was chosen ON the mute button, so it means mute from now on.
                _muteAccepted = true;
                return;
            }
            if (_mutePromptShownThisSession) return;
            if (!AnyTalkingFeatureLive())
            {
                // Nothing is going to talk over her, so there is nothing to arbitrate. Do NOT burn
                // the once-per-session prompt on a silent app.
                _muteAccepted = false;
                return;
            }

            _mutePromptShownThisSession = true;
            var choice = EmiMutePromptWindow.Ask();
            switch (choice)
            {
                case EmiMuteChoice.Mute:
                    _muteAccepted = true;
                    break;
                case EmiMuteChoice.DontAsk:
                    _muteAccepted = true;
                    s.EmiDeskMuteDontAsk = true;
                    App.Settings?.Save();
                    break;
                default:
                    _muteAccepted = false;
                    break;
            }
            Log.Information("[EmiDesk] mute prompt answered: {Choice}", choice);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] mute prompt failed, keeping the avatar");
            _muteAccepted = false;
        }
    }

    /// <summary>
    /// Is anything that can put words on screen or in your ears switched on? Takeover, the mic
    /// wake word, AI chat, Awareness, or a connected remote controller.
    /// </summary>
    private static bool AnyTalkingFeatureLive()
    {
        try
        {
            if (App.Autonomy?.IsEnabled == true) return true;
            var s = App.Settings?.Current;
            if (s != null)
            {
                if (s.SpeechWakeWordEnabled) return true;
                if (s.AiChatEnabled) return true;
                if (s.AwarenessModeEnabled && s.AwarenessConsentGiven) return true;
            }
            if (App.RemoteControl?.ControllerConnected == true) return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] live-feature probe failed");
        }
        return false;
    }

    // ---------------------------------------------------------------- hotkey

    /// <summary>
    /// Arm (or disarm) the system-wide summon chord. Every refusal is a logged no-op: the dock chip
    /// and the settings switch keep working, so a taken chord costs a line in the log and nothing
    /// else. Call it from the main window's Loaded and again whenever the setting changes.
    /// </summary>
    public void ApplyHotkey()
    {
        try
        {
            var s = App.Settings?.Current;
            var owner = App.MainWindowRef;

            if (s == null || !s.EmiDeskEnabled)
            {
                GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
                _hotkeyArmed = false;
                Log.Information("[EmiDesk] summon hotkey not armed: EMI Desk is off");
                return;
            }
            if (owner == null)
            {
                Log.Debug("[EmiDesk] summon hotkey deferred: no main window yet");
                return;
            }

            var chord = string.IsNullOrWhiteSpace(s.EmiDeskHotkey) ? DefaultHotkey : s.EmiDeskHotkey;
            var parsed = ParseChord(chord);
            if (parsed == null)
            {
                GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
                _hotkeyArmed = false;
                Log.Warning("[EmiDesk] summon hotkey NOT armed: {Chord} is not a valid chord " +
                            "(a modifier is required, bare keys are refused). Use the dock chip in the nav rail.", chord);
                return;
            }
            var (mods, key) = parsed.Value;

            // Same guard the Quick Recal chord uses. The panic and pause keys ride the modifier-blind
            // WH_KEYBOARD_LL hook and do NOT consume the press, so a summon chord whose BASE key is
            // one of them would summon EMI and tear the session down in the same keystroke. Refuse.
            if (Safety.PanicPolicy.FindHookClash(
                    key.ToString(), Safety.PanicPolicy.HookBoundBaseKeys(s)) is { } clash)
            {
                GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
                _hotkeyArmed = false;
                Log.Warning(
                    "[EmiDesk] summon hotkey {Chord} NOT armed: it shares its base key with the {Binding} binding ({BoundKey}), " +
                    "and the global keyboard hook ignores modifiers without consuming the press. Rebind one of them to free {Key}. " +
                    "The dock chip in the nav rail is unaffected.",
                    chord, clash.Name, clash.Key, key);
                return;
            }

            bool ok = GlobalHotkeyService.Register(
                GlobalHotkeyService.EmiDeskHotkeyId, owner, mods, key,
                // Win32 hotkeys arrive on the message-pump thread, so marshal before touching UI.
                () => owner.Dispatcher.BeginInvoke(new Action(Toggle)));

            _hotkeyArmed = ok;
            if (!ok)
            {
                Log.Warning("[EmiDesk] summon hotkey {Chord} could not be registered: another process holds it. " +
                            "The dock chip in the nav rail still summons her.", chord);
                return;
            }
            Log.Information("[EmiDesk] summon hotkey armed: {Chord} (slot=0x{Id:X})",
                chord, GlobalHotkeyService.EmiDeskHotkeyId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[EmiDesk] ApplyHotkey failed");
        }
    }

    /// <summary>True while the summon chord is registered with the OS.</summary>
    public bool HotkeyArmed => _hotkeyArmed;

    /// <summary>
    /// Parse a stored chord string ("Ctrl+Alt+E"). Returns null when it is unparseable OR carries
    /// no modifier: a bare-key global summon would eat that letter in every other app on the
    /// machine, which is exactly the class of bug the panic-key hook note warns about.
    /// </summary>
    public static (ModifierKeys Mods, Key Key)? ParseChord(string? chord)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(chord)) return null;
            var mods = ModifierKeys.None;
            Key key = Key.None;
            foreach (var raw in chord.Split('+'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": mods |= ModifierKeys.Control; continue;
                    case "alt": mods |= ModifierKeys.Alt; continue;
                    case "shift": mods |= ModifierKeys.Shift; continue;
                    case "win":
                    case "windows": mods |= ModifierKeys.Windows; continue;
                }
                if (!Enum.TryParse<Key>(part, ignoreCase: true, out var k)) return null;
                key = k;
            }
            if (key == Key.None) return null;
            if (mods == ModifierKeys.None) return null;
            return (mods, key);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Render a chord the way it is stored and shown: "Ctrl+Alt+E".</summary>
    public static string FormatChord(ModifierKeys mods, Key key)
    {
        var parts = new List<string>(4);
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>
    /// Why a candidate chord cannot be used, or null when it is fine. Localized, for the capture
    /// UI to show inline. Checks: a modifier is required, the base key must not be on the global
    /// keyboard hook (panic / pause), and it must not be the Quick Recal chord.
    /// </summary>
    public static string? ValidateChord(ModifierKeys mods, Key key)
    {
        try
        {
            if (key == Key.None) return Loc.Get("emi_desk_hotkey_err_empty");
            if (mods == ModifierKeys.None) return Loc.Get("emi_desk_hotkey_err_bare");

            var s = App.Settings?.Current;
            if (Safety.PanicPolicy.FindHookClash(
                    key.ToString(), Safety.PanicPolicy.HookBoundBaseKeys(s)) is { } clash)
            {
                return Loc.GetF("emi_desk_hotkey_err_hook", clash.Name, clash.Key);
            }

            // Ctrl+Alt+G is Quick Recal (MainWindow.QuickRecalHotkey*). Two Win32 slots cannot hold
            // the same combo and the loser would just fail to register, so say so up front.
            if (mods == (ModifierKeys.Control | ModifierKeys.Alt) && key == Key.G)
            {
                return Loc.Get("emi_desk_hotkey_err_quickrecal");
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- lifetime

    /// <summary>Tear her down at app shutdown. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            GlobalHotkeyService.Unregister(GlobalHotkeyService.EmiDeskHotkeyId);
            _hotkeyArmed = false;
            _window?.ShutDown();
            _window = null;
            IsOut = false;
            EmiState.SaveNow();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[EmiDesk] dispose failed");
        }
    }
}
