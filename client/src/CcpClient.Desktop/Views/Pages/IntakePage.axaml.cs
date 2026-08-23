using Avalonia.Controls;
using CcpClient.Desktop.Features.Intake;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Hop 2 of the Graded Intake route (wpf-surface-reachability.md §11 @ 7527243e7): rail door
/// <c>Graded Intake</c> -> this page -> <c>Begin Intake</c> -> <see cref="IntakeLaunch"/> ->
/// the weekly-pass gate. The door navigates and opens nothing
/// (<c>MainWindow/MainWindow.TabNavigation.cs:947</c> is a bare <c>ShowTab</c>); the button here
/// is the port's only intake launcher, which is WPF's one-entry rule
/// (<c>MainWindow/MainWindow.Presets.cs:1007</c>) and matches WPF's own count —
/// <c>BtnStartIntake</c> is the page's "primary (and only visible) action"
/// (<c>Views/Tabs/GradedIntakeTabView.xaml:151</c>).
///
/// <para>This class RENDERS a decision it does not make. The gate is
/// <see cref="IntakePassGate"/> — a pure function proved in the unit suite — so the four
/// branches cannot be quietly collapsed here: the page switches on the decision's TYPE, and the
/// three refusal types are different types precisely so one cannot be rendered as another. A
/// user who could not be determined must never be shown "you already had your run".</para>
/// </summary>
public partial class IntakePage : UserControl
{
    /// <summary>WPF's spent headline, <c>intake_gate_spent_headline</c> (<c>en.json:24</c>).</summary>
    public const string SpentBandTitle = "This week's intake is done";

    /// <summary>WPF's signed-out headline, <c>intake_gate_login_headline</c> (<c>en.json:21</c>).</summary>
    public const string NeedsAccountBandTitle = "Claim your weekly pass";

    /// <summary>The headline for the branch WPF does not have. It must never read like a refusal
    /// of the person: nothing was determined about them (the §10 D21 rule).</summary>
    public const string UndeterminableBandTitle = "COULD NOT DETERMINE YOUR PASS";

    /// <summary>WPF's own failure headline, <c>MainWindow/MainWindow.Lab.cs:164</c>. Not a caption
    /// for any of the three refusals, and never to be reused as one.</summary>
    public const string FaultBandTitleText = LaunchFaultText.IntakeHeadline;

    public IntakePage(IntakeLaunch intake)
    {
        ArgumentNullException.ThrowIfNull(intake);
        InitializeComponent();

        // Never disabled, in any branch: WPF's launch button is only ever covered by the gate
        // overlay, never greyed, so a refused press ARRIVES and is answered out loud.
        BeginIntakeButton.Click += (_, _) => intake.Launch();

        intake.Decided += Render;
        intake.Faulted += RenderFault;
    }

    private void Render(IntakePassDecision decision)
    {
        // A pass decision is the newest thing known about this page, so a failure plate from an
        // earlier press is stale — including on the SAME press, where Decided fires before the
        // Open call that may throw.
        ClearFault();

        switch (decision)
        {
            case IntakePassDecision.Proceed:
                // A run is opening. A band left over from an earlier refusal is stale the instant
                // a later press succeeds — the week can roll over between two presses.
                PassGate.IsVisible = false;
                PassGateTitle.Text = string.Empty;
                PassGateText.Text = string.Empty;
                break;

            case IntakePassDecision.RefusedSpent spent:
                Show(SpentBandTitle, spent.Message);
                break;

            case IntakePassDecision.RefusedNeedsAccount needsAccount:
                Show(NeedsAccountBandTitle, needsAccount.Message);
                break;

            case IntakePassDecision.RefusedUndeterminable undeterminable:
                Show(UndeterminableBandTitle, undeterminable.Message);
                break;
        }
    }

    /// <summary>
    /// The launch threw. WPF's counterpart is a modal warning dialog carrying this headline and
    /// the exception's message (<c>MainWindow/MainWindow.Lab.cs:161-166</c>).
    ///
    /// <para>The pass gate comes DOWN first, unconditionally. That gate can say "you already had
    /// your run this week"; leaving it up beside a fault would tell a user their week was gone
    /// when what actually happened is that the app broke.</para>
    /// </summary>
    private void RenderFault(Exception exception)
    {
        PassGate.IsVisible = false;
        PassGateTitle.Text = string.Empty;
        PassGateText.Text = string.Empty;

        FaultBandTitle.Text = FaultBandTitleText;
        FaultBandText.Text = LaunchFaultText.Compose(FaultBandTitleText, exception);
        FaultBand.IsVisible = true;
    }

    private void ClearFault()
    {
        FaultBand.IsVisible = false;
        FaultBandTitle.Text = string.Empty;
        FaultBandText.Text = string.Empty;
    }

    private void Show(string title, string message)
    {
        PassGateTitle.Text = title;
        PassGateText.Text = message;
        PassGate.IsVisible = true;
    }
}
