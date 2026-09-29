using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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
    /// <para>ponytail: not here yet, each a named WPF member: the moment bus (<c>Fire</c>, greeting,
    /// backSoon/weekend/bedtime beats, needs EmiLineEngine + EmiState), the nudge machine, the
    /// knock (<c>TryKnock</c>), the tube hand-off (<c>TubeDeskVisibility</c>), the summon count
    /// (<c>EmiState.NoteSummon</c>), and the system-wide chord (<c>ApplyHotkey</c>: a modifier
    /// chord needs XGrabKey, which X11PanicKey's raw-key listener does not cover).</para>
    /// </summary>
    internal sealed class EmiDeskService
    {
        public static EmiDeskService Instance { get; } = new();

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
                RaiseOutChanged();
                Log.Information("[EmiDesk] summoned ({Why})", why ?? "user");
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
                if ((Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is Window main)
                    main.Closed += (_, _) => win.ShutDown();
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

        /// <summary>
        /// Ask, at most once per session, whether the avatar should sit out while she is here.
        /// Dismissing the dialog keeps the avatar. ponytail: WPF also skips when nothing talks
        /// (AnyTalkingFeatureLive: Autonomy, wake word, AI chat, Awareness, remote); this head reads
        /// the three settings flags only - Autonomy and RemoteControl have no service here.
        /// </summary>
        private async Task MaybeAskAboutMuting()
        {
            try
            {
                var s = CoreSettings.Current;
                if (!s.EmiDeskMuteAvatar) { _muteAccepted = false; return; }
                if (s.EmiDeskMuteDontAsk) { _muteAccepted = true; return; }
                if (_mutePromptShownThisSession) return;
                bool talking = s.SpeechWakeWordEnabled || s.AiChatEnabled
                               || (s.AwarenessModeEnabled && s.AwarenessConsentGiven);
                if (!talking) { _muteAccepted = false; return; }

                _mutePromptShownThisSession = true;
                var choice = await EmiMutePromptWindow.Ask();
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

        private void RaiseOutChanged()
        {
            try { OutChanged?.Invoke(this, IsOut); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] OutChanged handler threw"); }
        }
    }
}
