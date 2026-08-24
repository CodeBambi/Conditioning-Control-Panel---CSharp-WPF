using Avalonia.Controls;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Features.Progression;

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

        // The card's fixed words. They are properties of the BUILD, not of the record, so they are
        // written once and never re-rendered — and they live as constants on TrainerCard so the
        // unit suite can hold them to what the build actually is.
        TrainerCardTitle.Text = TrainerCard.Title;
        TrainerCardLevelNote.Text = TrainerCard.LevelNote;
        TrainerCardPortraitNote.Text = TrainerCard.NoPortraitNote;
        TrainerCardTierNote.Text = TrainerCard.NoTierNote;
        TrainerCardLocalOnlyNote.Text = TrainerCard.LocalOnlyNote;

        // WHEN THE CARD READS THE RECORD, and the bound on it, stated rather than left to be
        // discovered. Two triggers, both deterministic and both free: every time the page is
        // mounted (the shell swaps PageHost.Content, so navigating here re-attaches it), and every
        // gate decision, which is every press of Begin Intake.
        //
        // THE BOUND: a run that FINISHES while this page is already mounted does not refresh the
        // card by itself. The coordinator's FlowEnded is one-shot per coordinator
        // (IntakeLaunchCoordinator.cs:_flowEnded, an Interlocked latch that never resets), so a
        // second run's end raises nothing at all and subscribing to it would refresh the card
        // exactly once per process — worse than a rule a user can predict. Navigating away and
        // back, or starting another run, shows the current record.
        //
        // THE LEVEL RIDES THE SAME TWO TRIGGERS, and it must: a run banks XP into progression.json
        // (Features/Progression/ProgressionLedger.Grant) from inside the modal intake window, with
        // this page unmounted behind it, so the level a user sees is only ever as fresh as the last
        // time this page was attached. That is the same bound the award record already carries and
        // it is stated once here rather than twice.
        AttachedToVisualTree += (_, _) =>
        {
            RenderTrainerCard(intake.ReadTrainerCard());
            RenderLevel(intake.ReadTrainerCardLevel());
        };
        intake.Decided += _ =>
        {
            RenderTrainerCard(intake.ReadTrainerCard());
            RenderLevel(intake.ReadTrainerCardLevel());
        };
    }

    /// <summary>
    /// Render the level. Every string is the model's — this page formats nothing, which is what
    /// keeps "the ledger could not be read" from being rendered as "level 1" by a later edit here.
    ///
    /// <para>Each of the four visual pieces is switched by the presence of its own value rather than
    /// by the state enum, so an Unknown level cannot leave a stale bar or a stale rank behind it from
    /// the previous render. The bar in particular: <see cref="TrainerCardLevel.Fill"/> is null
    /// exactly when there is no level, and the track is hidden on that null rather than drawn
    /// empty — an empty bar under a number would say "you are at the very start of this level",
    /// which is a claim, not an absence.</para>
    /// </summary>
    private void RenderLevel(TrainerCardLevel level)
    {
        TrainerCardLevelLine.Text = level.LevelLine;

        TrainerCardRankLine.Text = level.RankLine;
        TrainerCardRankLine.IsVisible = level.RankLine.Length > 0;

        TrainerCardXpLine.Text = level.XpLine;
        TrainerCardXpLine.IsVisible = level.XpLine.Length > 0;

        TrainerCardLevelUnknownNote.Text = level.Note;
        TrainerCardLevelUnknownNote.IsVisible = level.Note.Length > 0;

        TrainerCardXpTrack.IsVisible = level.Fill is not null;
        if (level.Fill is { } fill)
        {
            // Upstream assigns a measured pixel width (MainWindow.ChromeFx.cs:826-829); star
            // weights reach the same fraction without needing the track's ActualWidth, so this is
            // correct on the first layout pass and stays correct across a resize with no handler.
            TrainerCardXpBar.ColumnDefinitions[0].Width = new GridLength(fill, GridUnitType.Star);
            TrainerCardXpBar.ColumnDefinitions[1].Width = new GridLength(1 - fill, GridUnitType.Star);
        }
    }

    /// <summary>
    /// Render whatever the record could say. The awards are handed over as the model's own typed
    /// rows: the page chooses no wording of its own, which is what keeps "could not read" from being
    /// rendered as "not earned" by a later edit here.
    /// </summary>
    private void RenderTrainerCard(TrainerCard card)
    {
        TrainerCardRecordNote.Text = card.RecordNote;
        TrainerCardRecordNote.IsVisible = card.RecordNote.Length > 0;
        TrainerCardAwards.ItemsSource = card.Awards;
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
