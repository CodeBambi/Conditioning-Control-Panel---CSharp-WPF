using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

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
        // ponytail: needs App.MainWindowRef for WPF's ownerless CenterScreen fallback, wired when
        // the app shell moves to Core. Avalonia's ShowDialog requires an owner, so the caller
        // passes the one it already has.
        public static Task<EmiMuteChoice> Ask(Window owner) =>
            new EmiMutePromptWindow().ShowDialog<EmiMuteChoice>(owner);
    }
}
