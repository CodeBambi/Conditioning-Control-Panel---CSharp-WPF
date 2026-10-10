using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The one owner of whether EMI is out: summon, dismiss, toggle, and the mute prompt.
    ///
    /// <para>PORTED (the summon/dismiss half only) from
    /// <c>ConditioningControlPanel/Services/EmiDesk/EmiDeskService.cs</c>: Toggle :236, Summon :263,
    /// Dismiss :420, EnsureWindow :607, MaybeAskAboutMuting :1541, AvatarMuted :80. The dock chip,
    /// the hover x and the settings switch all route here, so none of them owns her twice.</para>
    ///
    /// <para>The moment bus (Fire, hold faces, the summon greeting) is EmiDeskService.Moments.cs, the
    /// nudge machine EmiDeskService.Nudges.cs, the knock EmiDeskService.Knock.cs. ponytail: offers
    /// (ShowAsk), the glass channels, the gif rain and the tube hand-off (<c>TubeDeskVisibility</c>)
    /// are not here yet. The system-wide chord (<c>ApplyHotkey</c>) is grabbed through Platform/X11SummonChord.</para>
    /// </summary>
    internal sealed partial class EmiDeskService
    {
        public static EmiDeskService Instance { get; } = new();   // ctor: EmiDeskService.Moments.cs (wires the bus)

        private EmiDeskWindow? _window;
        private long _summonGen;
        private bool _muteAccepted;
        private bool _mutePromptShownThisSession;

        /// <summary>True while she is on screen (including her intro and outro).</summary>
        public bool IsOut { get; private set; }

        /// <summary>The widget window, or null before her first summon.</summary>
        public EmiDeskWindow? Window => _window;

        /// <summary>Raised whenever <see cref="IsOut"/> changes. The dock chip listens.</summary>
        public event EventHandler<bool>? OutChanged;

        /// <summary>Setting on AND the user agreed - a mute the user never chose is not a mute.</summary>
        public bool AvatarMuted => IsOut && CoreSettings.Current.EmiDeskMuteAvatar && _muteAccepted;

        private bool WindowOnScreen => _window?.IsVisible == true;

        /// <summary>Summon her if she is away, send her away if she is out. A widget ON SCREEN wins over the flag.</summary>
        public void Toggle()
        {
            if (IsOut || WindowOnScreen) Dismiss();
            else _ = Summon();
        }

        /// <summary>Bring her out. No-op when she is out or the feature is off.</summary>
        public async Task Summon(string? why = null)
        {
            try
            {
                if (!CoreSettings.Current.EmiDeskEnabled)
                {
                    Log.Debug("[EmiDesk] summon ignored: EmiDeskEnabled is off");
                    return;
                }
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => _ = Summon(why));
                    return;
                }
                if (IsOut) return;

                var win = EnsureWindow();
                if (win == null) return;

                IsOut = true;
                long gen = ++_summonGen;

                // THE MODAL RE-ENTRY TRAP (WPF :295): the chip and the x still work while the
                // prompt is up, so a summon overtaken during it must not finish.
                await MaybeAskAboutMuting();
                if (_summonGen != gen || !IsOut)
                {
                    Log.Information("[EmiDesk] summon abandoned: she was sent away while the mute prompt was up");
                    return;
                }

                win.RestorePlacement();
                win.Show();
                win.RunSummon();
                bool first = !EmiState.Current.FirstBootSeen;
                int summons = EmiState.NoteSummon();
                RaiseOutChanged();
                Log.Information("[EmiDesk] summoned ({Why}), firstBoot={First}, summon #{N}", why ?? "user", first, summons);
                OnSummoned(why, summons);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] Summon failed");
            }
        }

        /// <summary>Send her away. Safe to call when she is not out.</summary>
        public void Dismiss()
        {
            // WPF :422: sending her away during the welcome show stops the show first.
            try { if (_window?.PresentationActive == true) _window.StopPresentation(); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] stopping the show on dismiss failed"); }
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(Dismiss);
                    return;
                }
                // Invalidate a summon parked behind the mute prompt before anything else.
                _summonGen++;

                if (_window == null) return;
                if (!IsOut && !WindowOnScreen) return;
                if (!IsOut)
                {
                    Log.Warning("[EmiDesk] dismissing a desynced widget: visible while IsOut was false");
                    IsOut = true;
                }

                OnDismissing();
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

        /// <summary>WPF :598 BeginPresentation: the welcome show borrows the real widget. Explicit demo
        /// activation bypasses the desk preference (EmiDeskEnabled) and the mute prompt without changing
        /// either; she stays out afterwards, as on WPF.</summary>
        internal EmiDeskWindow? BeginPresentation()
        {
            var window = EnsureWindow();
            if (window == null) return null;
            _summonGen++;   // a summon parked behind the mute prompt must not finish over the show
            if (!IsOut) window.RestorePlacement();
            IsOut = true; RaiseOutChanged();
            return window;
        }

        private EmiDeskWindow? EnsureWindow()
        {
            try
            {
                if (_window != null) return _window;
                var win = new EmiDeskWindow();
                win.Closed += (_, _) =>
                {
                    if (!ReferenceEquals(_window, win)) return;
                    _window = null;
                    IsOut = false;
                    RaiseOutChanged();
                };
                // WPF closed her in App.OnExit. Here a hidden window would keep an
                // OnLastWindowClose lifetime alive, so she goes with the main window.
                HookShell((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow, win);
                _window = win;
                return win;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[EmiDesk] could not build the widget window");
                _window = null;
                return null;
            }
        }

        internal static void HookShell(Window? main, EmiDeskWindow win)
        {
            if (main != null) main.Closed += (_, _) => win.ShutDown();
        }

        /// <summary>
        /// Ask, at most once per session, whether the avatar should sit out while she is here.
        /// Dismissing the dialog keeps the avatar. ponytail: WPF also skips when nothing talks
        /// (AnyTalkingFeatureLive); both heads share its settings half, Core <c>EmiMuteRule.SettingsTalk</c>.
        /// The live half (Autonomy, remote controller) has no service on this head.
        /// </summary>
        private async Task MaybeAskAboutMuting()
        {
            try
            {
                var s = CoreSettings.Current;
                if (!s.EmiDeskMuteAvatar) { _muteAccepted = false; return; }
                if (s.EmiDeskMuteDontAsk) { _muteAccepted = true; return; }
                if (_mutePromptShownThisSession) return;
                if (!ConditioningControlPanel.Services.EmiDesk.EmiMuteRule.SettingsTalk(s)) { _muteAccepted = false; return; }

                _mutePromptShownThisSession = true;
                var choice = await AskMute();
                _muteAccepted = choice != EmiMuteChoice.Keep;
                if (choice == EmiMuteChoice.DontAsk)
                {
                    s.EmiDeskMuteDontAsk = true;
                    App.Settings?.Save();
                }
                Log.Information("[EmiDesk] mute prompt answered: {Choice}", choice);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] mute prompt failed, keeping the avatar");
                _muteAccepted = false;
            }
        }

        // ---------------------------------------------------------------- hotkey (WPF :1633)

        /// <summary>True while the summon chord is grabbed (WPF HotkeyArmed).</summary>
        public bool HotkeyArmed { get; private set; }

        /// <summary>The OS grab. Seams so tests never touch the X server.</summary>
        internal Func<ChordMods, string, Action, bool> RegisterChord = Platform.X11SummonChord.Arm;
        internal Action UnregisterChord = Platform.X11SummonChord.Disarm;

        /// <summary>
        /// Arm (or disarm) the system-wide summon chord, WPF <c>ApplyHotkey</c> one for one: off, an
        /// unparseable chord, or a base key shared with the panic/pause hook all disarm with a log
        /// line; a combo another client holds leaves it unarmed. The dock chip keeps working either way.
        /// Called at startup (App, after the panic key) and whenever the settings section changes it.
        /// </summary>
        public void ApplyHotkey()
        {
            try
            {
                var s = CoreSettings.Current;
                if (!s.EmiDeskEnabled)
                {
                    Disarm();
                    Log.Information("[EmiDesk] summon hotkey not armed: EMI Desk is off");
                    return;
                }
                var chord = string.IsNullOrWhiteSpace(s.EmiDeskHotkey) ? EmiDeskChord.DefaultHotkey : s.EmiDeskHotkey;
                if (EmiDeskChord.Parse(chord) is not { } parsed
                    || !Enum.TryParse<global::Avalonia.Input.Key>(parsed.Key, ignoreCase: true, out _))
                {
                    Disarm();
                    Log.Warning("[EmiDesk] summon hotkey NOT armed: {Chord} is not a valid chord. Use the dock chip in the nav rail.", chord);
                    return;
                }
                // The panic/pause listener is modifier-blind and does not consume the press, so a
                // chord on the same base key would summon EMI and stop everything in one keystroke.
                if (ConditioningControlPanel.Services.Safety.PanicPolicy.FindHookClash(parsed.Key,
                        ConditioningControlPanel.Services.Safety.PanicPolicy.HookBoundBaseKeys(s)) is { } clash)
                {
                    Disarm();
                    Log.Warning("[EmiDesk] summon hotkey {Chord} NOT armed: it shares its base key with the {Binding} binding ({BoundKey}).",
                        chord, clash.Name, clash.Key);
                    return;
                }
                // X delivers the press on the listener thread: marshal before touching UI.
                HotkeyArmed = RegisterChord(parsed.Mods, parsed.Key, () => Dispatcher.UIThread.Post(Toggle));
                if (HotkeyArmed) Log.Information("[EmiDesk] summon hotkey armed: {Chord}", chord);
                else Log.Warning("[EmiDesk] summon hotkey {Chord} could not be grabbed: another client holds it. " +
                                 "The dock chip in the nav rail still summons her.", chord);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[EmiDesk] ApplyHotkey failed");
            }
        }

        private void Disarm()
        {
            UnregisterChord();
            HotkeyArmed = false;
        }

        /// <summary>The mute question. A seam only so a test can hold the prompt open.</summary>
        internal Func<Task<EmiMuteChoice>> AskMute = EmiMutePromptWindow.Ask;

        /// <summary>Test seam: forget this session's prompt, as a fresh launch would.</summary>
        internal void ResetMutePrompt() { _mutePromptShownThisSession = false; _muteAccepted = false; }

        private void RaiseOutChanged()
        {
            try { OutChanged?.Invoke(this, IsOut); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] OutChanged handler threw"); }
        }
    }
}
