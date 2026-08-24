using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Features.Progression;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The Trainer Card on the mounted Graded Intake page, driven by real headless input from a cold
/// composition-root boot: rail door <c>Graded Intake</c> -> the page -> the card, reading the award
/// record the intake writes (<c>Features/Progression/GradedRunAwards.cs</c>).
///
/// <para><b>What is substituted:</b> the data root only (<c>IntakeLaunch.DataDirectory</c>, the seam
/// the intake facts already use), so the record these facts seed is a real file the real reader
/// opens. Nothing stubs the card: <see cref="TrainerCard"/> is a pure projection proved in the unit
/// suite, and what is checked here is that the projection reaches the screen unaltered.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, realized item
/// containers, rendered text and hit-testable controls. Nothing here claims composited pixels, a
/// legible layout, colour, focus, window activation, or anything at all on Linux.</para>
/// </summary>
public class TrainerCardHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window, string RecordPath)> BootAsync(
        string? seededRecord)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-trainer-card-headless-" + Guid.NewGuid().ToString("N"));
        var intakeDir = Path.Combine(dir, "intake");
        Directory.CreateDirectory(intakeDir);
        var recordPath = Path.Combine(intakeDir, GradedRunAwardsDocument.FileName);
        if (seededRecord is not null)
        {
            File.WriteAllText(recordPath, seededRecord);
        }

        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(dir, "settings.json"),
            LogSinkFactory = () => new DebugLogSink(),
        };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        window.Intake.DataDirectory = intakeDir;
        window.Show();
        window.UpdateLayout();
        return (host!, window, recordPath);
    }

    private static RadioButton Door(MainWindow window, string name) =>
        window.FindControl<RadioButton>(name) ?? throw new InvalidOperationException($"no rail door '{name}'");

    private static T Descendant<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(MainWindow window, Control control)
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    /// <summary>Every line the card actually put on screen, in tree order.</summary>
    private static List<string> CardText(MainWindow window) =>
        Descendant<ItemsControl>(window, "TrainerCardAwards")
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? string.Empty)
            .ToList();

    [AvaloniaFact]
    public async Task NavigatingToTheCard_ShowsWhatTheRecordSays_RowByRow()
    {
        // One earned award and two cleared categories on disk. The card must show the earned one as
        // earned, the unearned one with its REAL distance from the threshold, and the two this build
        // cannot earn with the reason it cannot.
        var (host, window, _) = await BootAsync(
            """{ "awardedIds": ["top_of_the_class"], "perfectedCategories": ["bambi", "sissy"] }""");

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "TrainerCardAwards");

        Click(window, Door(window, "DoorIntake"));
        Assert.Equal(ShellRoutes.Intake, window.Router.Current.Id);
        Assert.IsType<IntakePage>(window.PageFor(ShellRoutes.Intake));

        var text = CardText(window);

        Assert.Contains(TrainerCard.TopOfTheClassName, text);
        Assert.Contains(TrainerCard.TopOfTheClassRequirement, text);
        Assert.Contains(TrainerCard.EarnedStatus, text);

        Assert.Contains(TrainerCard.HonorRollName, text);
        Assert.Contains(text, line => line.Contains($"2 of {GradedRunAwards.HonorRollCategories}", StringComparison.Ordinal));

        Assert.Contains(TrainerCard.TeachersPetStatus, text);
        Assert.Contains(TrainerCard.HeldBackStatus, text);

        // A readable record has nothing to apologise for, so the notice line is not on screen.
        Assert.False(Descendant<TextBlock>(window, "TrainerCardRecordNote").IsVisible);

        // And the three sentences that keep the card honest about what it is NOT are.
        Assert.Equal(TrainerCard.NoLevelNote, Descendant<TextBlock>(window, "TrainerCardLevelNote").Text);
        Assert.Equal(TrainerCard.NoTierNote, Descendant<TextBlock>(window, "TrainerCardTierNote").Text);
        Assert.Equal(TrainerCard.LocalOnlyNote, Descendant<TextBlock>(window, "TrainerCardLocalOnlyNote").Text);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AnUnreadableRecord_PutsTheNoticeOnScreen_AndNeverTheWordsNotEarned()
    {
        // The failure this whole surface exists to avoid: a record that could not be read rendered
        // as a card with nothing on it. On screen that is indistinguishable from "you have earned
        // nothing", which is a claim the app cannot make here.
        var (host, window, _) = await BootAsync("{ not json");

        Click(window, Door(window, "DoorIntake"));

        var notice = Descendant<TextBlock>(window, "TrainerCardRecordNote");
        Assert.True(notice.IsVisible);
        Assert.Contains(GradedRunAwardsDocument.FileName, notice.Text ?? "", StringComparison.Ordinal);
        Assert.Contains("not valid JSON", notice.Text ?? "", StringComparison.Ordinal);

        var text = CardText(window);
        Assert.DoesNotContain(TrainerCard.NotEarnedStatus, text);
        Assert.Contains(TrainerCard.UnknownStatus, text);

        // The two rows that do not depend on the file still answer, because an unreadable file says
        // nothing about them.
        Assert.Contains(TrainerCard.TeachersPetStatus, text);
        Assert.Contains(TrainerCard.HeldBackStatus, text);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheCardRereadsTheRecord_WhenThePageIsMountedAgain()
    {
        // The card reads on ATTACH, not once at construction. Without that, a run completed this
        // session would leave the card showing the record as it was when the shell was built — for
        // the rest of the process.
        var (host, window, recordPath) = await BootAsync("""{ "awardedIds": [] }""");

        Click(window, Door(window, "DoorIntake"));
        Assert.DoesNotContain(TrainerCard.EarnedStatus, CardText(window));

        // What a completed graded run leaves behind, written while the page is mounted.
        File.WriteAllText(recordPath, """{ "awardedIds": ["top_of_the_class"] }""");

        Click(window, Door(window, "DoorStudio"));
        Click(window, Door(window, "DoorIntake"));

        Assert.Contains(TrainerCard.EarnedStatus, CardText(window));

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheMountedCard_OffersNothingToPress_ExceptTheIntakeLauncher()
    {
        // Sharing is unapproved, so it is ABSENT rather than present-and-disabled — and "absent"
        // is a claim about the rendered tree, not only about the markup. The unit suite pins the
        // same rule at the source; this one pins what a user can actually reach on the page.
        var (host, window, _) = await BootAsync("""{ "awardedIds": ["honor_roll"] }""");

        Click(window, Door(window, "DoorIntake"));

        // TemplatedParent is null for a control the PAGE declares and non-null for one a control
        // template supplied — the scroll bar's four RepeatButtons, which arrived with the
        // ScrollViewer and are nobody's feature.
        var page = Assert.IsType<IntakePage>(window.PageFor(ShellRoutes.Intake));
        var pressable = page.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.TemplatedParent is null && c is Button or CheckBox or ToggleButton)
            .ToList();

        var only = Assert.Single(pressable);
        Assert.Equal("BeginIntakeButton", only.Name);

        await host.ShutdownAsync();
    }
}
