using Avalonia;
using Avalonia.Controls;
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
/// THE LEVEL ON THE MOUNTED CARD, driven the way a user reaches it: a cold composition-root boot, a
/// real headless left-click on the Graded Intake rail door, and the page's own attach-time read of a
/// real <c>progression.json</c> on disk.
///
/// <para><b>What is substituted:</b> the data root only (<c>IntakeLaunch.DataDirectory</c>, the seam
/// the intake facts already use). Nothing stubs the projection —
/// <see cref="TrainerCardLevel"/> is pure and proved in the unit suite — and what is checked here is
/// that it reaches the visual tree unaltered, that the BAR's geometry is the fraction the model
/// computed, and that an unreadable ledger leaves no number anywhere on the page.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, layout, rendered
/// text and column weights. Nothing here claims composited pixels, colour, legibility, or anything
/// at all on Linux — the pixels are the headed <c>trainer-card-level</c> capture.</para>
/// </summary>
public class TrainerCardLevelHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window, string LedgerPath)> BootAsync(string? seededLedger)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-trainer-level-headless-" + Guid.NewGuid().ToString("N"));
        var intakeDir = Path.Combine(dir, "intake");
        Directory.CreateDirectory(intakeDir);
        var ledgerPath = Path.Combine(intakeDir, ProgressionDocument.FileName);
        if (seededLedger is not null)
        {
            File.WriteAllText(ledgerPath, seededLedger);
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
        return (host!, window, ledgerPath);
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

    /// <summary>Every line the mounted page actually put on screen.</summary>
    private static List<string> PageText(MainWindow window) =>
        Assert.IsType<IntakePage>(window.PageFor(ShellRoutes.Intake))
            .GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.IsVisible)
            .Select(t => t.Text ?? string.Empty)
            .ToList();

    [AvaloniaFact]
    public async Task NavigatingToTheCard_ShowsTheLevelTheLedgerHolds_AndABarAtItsFraction()
    {
        // Level 42 with 1000.5 banked into it. 42 clears the first band, so the level costs
        // Math.Round(800 + 41 x 1700/79) = Math.Round(1682.278) = 1682
        // (Services/Progression/ProgressionService.cs:301-305) — derived here rather than read back
        // from XpCurve, because an assertion against the curve would pass against a wrong curve.
        var (host, window, _) = await BootAsync("""{ "level": 42, "xp": 1000.5 }""");

        // Nothing of the card exists before the door is pressed: the page is built on navigation.
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Control>(), c => c.Name == "TrainerCardLevelLine");

        Click(window, Door(window, "DoorIntake"));
        Assert.Equal(ShellRoutes.Intake, window.Router.Current.Id);

        Assert.Equal("LVL 42", Descendant<TextBlock>(window, "TrainerCardLevelLine").Text);
        Assert.Equal("DUMB AIRHEAD", Descendant<TextBlock>(window, "TrainerCardRankLine").Text);
        Assert.Equal("1000 / 1682 XP", Descendant<TextBlock>(window, "TrainerCardXpLine").Text);

        // THE BAR IS THE POINT OF DOING THIS HEADLESS AT ALL. Its fill is two star columns rather
        // than a measured pixel width (upstream measures: MainWindow/MainWindow.ChromeFx.cs:826-829),
        // and the thing that could silently break is the weights, not the strings.
        var track = Descendant<Border>(window, "TrainerCardXpTrack");
        Assert.True(track.IsVisible);
        var bar = Descendant<Grid>(window, "TrainerCardXpBar");
        Assert.Equal(1000.5 / 1682, bar.ColumnDefinitions[0].Width.Value, 10);
        Assert.Equal(1 - (1000.5 / 1682), bar.ColumnDefinitions[1].Width.Value, 10);

        // And the weights really reached LAYOUT: the fill's arranged width is that fraction of the
        // track's, which is what a wrong GridUnitType would break while leaving the weights right.
        //
        // THE TOLERANCE IS ONE DEVICE PIXEL, AND IT IS A RULE RATHER THAN A TUNED NUMBER.
        // UseLayoutRounding snaps an arranged width to the pixel grid, so the fill of a 420 DIP
        // track at this fraction arranges to 250 where the exact product is 249.83 — measured, not
        // guessed. Anything wider than one pixel of disagreement is a wrong fraction, not rounding.
        var fill = Descendant<Border>(window, "TrainerCardXpFill");
        var exact = (1000.5 / 1682) * track.Bounds.Width;
        Assert.InRange(fill.Bounds.Width, exact - 1.0, exact + 1.0);

        // The inversion the tolerance must survive: a bar drawn full, or drawn empty, is further
        // than a pixel away and this assertion catches both.
        Assert.True(fill.Bounds.Width < track.Bounds.Width - 1.0);
        Assert.True(fill.Bounds.Width > 1.0);

        // The unknown-note line is a thing the card says when it CANNOT read the ledger, and it read
        // this one, so it must not be on screen at all.
        Assert.False(Descendant<TextBlock>(window, "TrainerCardLevelUnknownNote").IsVisible);

        // The fixed sentence beside the numbers, which is what stops the level reading as a key.
        Assert.Equal(TrainerCard.LevelNote, Descendant<TextBlock>(window, "TrainerCardLevelNote").Text);

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task AnUnreadableLedger_PutsWordsOnScreen_AndNoNumberAnywhereOnThePage()
    {
        // The failure this whole block exists to avoid: a ledger that could not be read rendered as
        // "LVL 1" with an empty bar under it. On screen that is indistinguishable from a fresh
        // account, which is a claim about a user who may be standing at 40.
        var (host, window, _) = await BootAsync("{ not json");

        Click(window, Door(window, "DoorIntake"));

        Assert.Equal("Level unknown", Descendant<TextBlock>(window, "TrainerCardLevelLine").Text);

        // The bar and the readout are GONE rather than zeroed, and so is the rank: an empty bar says
        // "you are at the very start of this level", which is a number the app does not have.
        Assert.False(Descendant<Border>(window, "TrainerCardXpTrack").IsVisible);
        Assert.False(Descendant<TextBlock>(window, "TrainerCardXpLine").IsVisible);
        Assert.False(Descendant<TextBlock>(window, "TrainerCardRankLine").IsVisible);

        var note = Descendant<TextBlock>(window, "TrainerCardLevelUnknownNote");
        Assert.True(note.IsVisible);
        Assert.Contains(ProgressionDocument.FileName, note.Text ?? "", StringComparison.Ordinal);
        Assert.Contains("not valid JSON", note.Text ?? "", StringComparison.Ordinal);

        // And the whole-page inversion: no level chip and no rank title reached the screen by any
        // route, including the award rows and the fixed notes.
        var text = PageText(window);
        Assert.DoesNotContain(text, line => line.StartsWith("LVL ", StringComparison.Ordinal));
        foreach (var rank in new[] { "BASIC BIMBO", "DUMB AIRHEAD", "SYNTHETIC BLOWDOLL", "PERFECT FUCKPUPPET" })
        {
            Assert.DoesNotContain(text, line => line.Contains(rank, StringComparison.Ordinal));
        }

        await host.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task TheCardRereadsTheLevel_WhenThePageIsMountedAgain()
    {
        // The bound stated in IntakePage's own comment, made mechanical. A run banks XP from inside
        // the modal intake window with this page unmounted behind it, so the level is only ever as
        // fresh as the last attach. Without the attach-time read a user would see the level they had
        // when the shell was built — for the rest of the process.
        var (host, window, ledgerPath) = await BootAsync("""{ "level": 3, "xp": 0 }""");

        Click(window, Door(window, "DoorIntake"));
        Assert.Equal("LVL 3", Descendant<TextBlock>(window, "TrainerCardLevelLine").Text);

        // What a completed run leaves behind, written while the page is mounted.
        File.WriteAllText(ledgerPath, """{ "level": 4, "xp": 12 }""");
        Assert.Equal("LVL 3", Descendant<TextBlock>(window, "TrainerCardLevelLine").Text);

        Click(window, Door(window, "DoorStudio"));
        Click(window, Door(window, "DoorIntake"));

        Assert.Equal("LVL 4", Descendant<TextBlock>(window, "TrainerCardLevelLine").Text);
        // Math.Round(800 + 3 x 1700/79) = Math.Round(864.557) = 865 (ProgressionService.cs:301-305).
        Assert.Equal("12 / 865 XP", Descendant<TextBlock>(window, "TrainerCardXpLine").Text);

        await host.ShutdownAsync();
    }
}
