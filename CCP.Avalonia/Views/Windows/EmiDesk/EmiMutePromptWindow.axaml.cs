using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>What the user said when asked whether to mute the avatar while EMI is out.</summary>
    public enum EmiMuteChoice
    {
        /// <summary>Keep the avatar talking. Also what a dismissed dialog means.</summary>
        Keep = 0,

        /// <summary>Mute the avatar for this visit.</summary>
        Mute = 1,

        /// <summary>Mute, and stop asking. Persisted as <c>EmiDeskMuteDontAsk</c>.</summary>
        DontAsk = 2
    }

    /// <summary>
    /// The one question EMI Desk asks: two voices at once is the failure mode, so when she arrives
    /// while something that talks is already switched on, the user picks which voice wins.
    ///
    /// Shown at most once per app session, and only when a talking feature is actually live. Closing
    /// it without answering keeps the avatar: silence is never read as consent to being silenced.
    ///
    /// PORTED from ConditioningControlPanel/Windows/EmiDesk/EmiMutePromptWindow.xaml.cs. Deviations:
    ///  - WPF's <c>_choice</c> field plus <c>DialogResult = true</c> collapses into
    ///    <c>Close(choice)</c>: Avalonia carries the result through
    ///    <c>ShowDialog&lt;EmiMuteChoice&gt;</c>, and a dismissed window yields default(TResult),
    ///    which is <see cref="EmiMuteChoice.Keep"/> - the same "silence is not consent" answer.
    ///  - The five <c>Loc.Get</c> assignments are gone; the same five keys are <c>{loc:Str}</c> in
    ///    the markup, so a language change re-renders them (see the porting notes in CLAUDE.md).
    ///  - The constructor is public rather than private: it is also the render constructor, and
    ///    with no <c>Loc.Get</c> lines left there is nothing for a private one to guard.
    ///  - <c>PreviewKeyDown</c> -> <c>KeyDown</c>, wired in the constructor per the porting
    ///    convention. Avalonia has no preview phase and no button here handles Escape.
    ///  - The try/catch around the answer is dropped with the thing it guarded: WPF's
    ///    <c>DialogResult</c> setter throws when the window was not shown modally,
    ///    <c>Close(object)</c> does not.
    ///  - The namespace is the flat WPF one no longer: the WinRT <c>Windows</c>-shadowing trap the
    ///    original's header describes is a WPF-head problem, and this head has no OCR service.
    /// </summary>
    public partial class EmiMutePromptWindow : Window
    {
        public EmiMutePromptWindow()
        {
            AvaloniaXamlLoader.Load(this);

            this.FindControl<Button>("BtnMute")!.Click += (_, _) => Close(EmiMuteChoice.Mute);
            this.FindControl<Button>("BtnKeep")!.Click += (_, _) => Close(EmiMuteChoice.Keep);
            this.FindControl<Button>("BtnDontAsk")!.Click += (_, _) => Close(EmiMuteChoice.DontAsk);

            // Escape is "keep", matching a dismissed dialog. Never "mute": a reflex key press must not
            // silence something the user did not choose to silence.
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(EmiMuteChoice.Keep); };
        }

        /// <summary>
        /// Ask, modally. The safe default is leaving the app as it was, so every path that is not
        /// an explicit answer - dismissing the window included - comes back
        /// <see cref="EmiMuteChoice.Keep"/>.
        /// </summary>
        public static Task<EmiMuteChoice> Ask(Window owner) =>
            new EmiMutePromptWindow().ShowDialog<EmiMuteChoice>(owner);

        /// <summary>
        /// Ask with no owner in hand - WPF's own call shape (<c>EmiMutePromptWindow.Ask()</c> from
        /// <c>EmiDeskService</c>), which set <c>Owner = App.MainWindowRef</c> and fell back to
        /// <c>WindowStartupLocation.CenterScreen</c> when there was no main window.
        ///
        /// <para>The note here used to say this needed <c>App.MainWindowRef</c> "when the app shell
        /// moves to Core". That has been stale since the shell landed: the desktop lifetime's
        /// <see cref="IClassicDesktopStyleApplicationLifetime.MainWindow"/> IS what
        /// <c>App.MainWindowRef</c> was, and it is asked for through the lifetime rather than by
        /// naming <c>MainShellWindow</c> so it stays true whichever window the app has up. Same
        /// resolution <c>EmiDeskWindow.AppMainWindow</c> uses.</para>
        ///
        /// <para>WPF's ownerless branch cannot be ported: Avalonia's <c>ShowDialog</c> REQUIRES an
        /// owner, and there is nothing to be modal to when the app is showing no window at all - or
        /// is holding one that is not visible. Avalonia refuses that outright ("Cannot show window
        /// with non-visible owner.", read out of Avalonia.Controls 12.1.1) where WPF accepted it,
        /// and the lifetime's MainWindow is exactly that between construction and Show, and again
        /// whenever the shell is hidden to the tray. So the guard is <c>IsVisible</c>, not null -
        /// null alone would leave a FAULTED task on the two paths she is most likely to arrive on.
        /// Both answer <see cref="EmiMuteChoice.Keep"/> - not a refusal to ask so much as the file's
        /// own rule, that a question which was never put is never consent to being silenced. It is
        /// also the branch the headless render takes, which has no desktop lifetime.</para>
        ///
        /// <para>Uncalled on this head: the one caller is <c>EmiDeskService.MaybeAskAboutMuting</c>
        /// (ConditioningControlPanel/Services/EmiDesk/EmiDeskService.cs), which moves with the
        /// shell. It exists so that caller lands with no owner-threading edit.</para>
        /// </summary>
        public static Task<EmiMuteChoice> Ask()
        {
            Window? main = null;
            try
            {
                main = (Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            }
            catch (System.Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] mute prompt owner lookup failed");
            }

            if (main is null || !main.IsVisible)
            {
                Log.Debug("[EmiDesk] no visible window to own the mute prompt; keeping the avatar");
                return Task.FromResult(EmiMuteChoice.Keep);
            }

            return Ask(main);
        }
    }
}
