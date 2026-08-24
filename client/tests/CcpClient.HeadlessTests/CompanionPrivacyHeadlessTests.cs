using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Ai;
using CcpClient.Desktop.Features.Companion;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// The privacy dial (audit row A3), the per-app editor (A4) and the transcript window (D11) as
/// CONTROLS: real layout, real binding application, real headless pointer input. Draw level only
/// (verification-harness.md evidence-class rule) — no compositor, no pixel, no focus and no
/// window-manager claim; the headed `companion-privacy` and `companion-transcript` pairs own those.
/// </summary>
public class CompanionPrivacyHeadlessTests
{
    /// <summary>
    /// The strip renders all three stops, "Off" is the one selected on a fresh process, and the
    /// hint under it is the sentence for that stop.
    /// </summary>
    [AvaloniaFact]
    public async Task TheStripRendersThreeStops_OffSelected_WithItsOwnSentence()
    {
        var (host, _, companion) = await OpenAsync();

        var stops = Stops(companion);
        Assert.Equal(["─ Off", "◔ App names only", "◉ + Page titles"], stops.Select(s => (string)s.Content!));
        Assert.True(stops[0].IsChecked);
        Assert.All(stops.Skip(1), stop => Assert.False(stop.IsChecked));

        Assert.Equal("what leaves your PC", companion.FindControl<TextBlock>("PrivacyDialHead")!.Text);
        Assert.Equal(
            "her eyes are closed. nothing is watched, nothing is counted.",
            companion.FindControl<TextBlock>("PrivacyDialHint")!.Text);
        Assert.False(companion.FindControl<Border>("TitleAllowEditor")!.IsVisible);

        await host.ShutdownAsync();
    }

    /// <summary>
    /// THE INVERSION, driven by real presses. Pressing "+ Page titles" opens the editor and moves
    /// nothing else; the strip snaps back to the middle stop because that is what the state says
    /// (WPF <c>Views/Controls/Companion/Runtime/AwarenessPrivacyRuntimeVm.cs:106-113</c>, reason at
    /// <c>:24-27</c>). Naming an app with a real press is what moves it.
    /// </summary>
    [AvaloniaFact]
    public async Task PressingPageTitles_OpensTheEditor_AndTheStripStaysOnAppNamesUntilAnAppIsNamed()
    {
        var (host, _, companion) = await OpenAsync();
        var participant = host.Participants.OfType<CompanionParticipant>().Single();
        var stops = Stops(companion);

        Click(companion, stops[2]);

        Assert.True(companion.FindControl<Border>("TitleAllowEditor")!.IsVisible);
        Assert.Empty(participant.Awareness.TitleAllowList.Entries);
        Assert.True(participant.Awareness.Consent.Granted);
        Assert.True(stops[1].IsChecked);   // the dial reports the state, not the press
        Assert.False(stops[2].IsChecked);
        Assert.Equal(
            "the category, the app name and a rounded time. never a page title.",
            companion.FindControl<TextBlock>("PrivacyDialHint")!.Text);

        companion.FindControl<TextBox>("TitleAllowInput")!.Text = "Browser";
        companion.UpdateLayout();
        Click(companion, companion.FindControl<Button>("TitleAllowAdd")!);

        Assert.True(stops[2].IsChecked);
        Assert.False(stops[1].IsChecked);
        Assert.Equal(
            "app names, plus page titles for the apps you name yourself.",
            companion.FindControl<TextBlock>("PrivacyDialHint")!.Text);
        Assert.True(participant.Awareness.TitleAllowList.AllowsTitleFor("Browser"));

        // The chip renders the SANITISED entry — the string the filter matches, not the typed text.
        var chips = companion.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "browser" && t.IsVisible).ToList();
        Assert.Single(chips);

        await host.ShutdownAsync();
    }

    /// <summary>
    /// Pressing the middle stop empties the list, because that stop is a promise no page title
    /// travels (WPF <c>AwarenessPrivacyRuntimeVm.cs:97-101</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task PressingAppNamesOnly_EmptiesTheList_AndClosesTheEditor()
    {
        var (host, _, companion) = await OpenAsync();
        var participant = host.Participants.OfType<CompanionParticipant>().Single();
        var stops = Stops(companion);

        Click(companion, stops[2]);
        companion.FindControl<TextBox>("TitleAllowInput")!.Text = "browser";
        companion.UpdateLayout();
        Click(companion, companion.FindControl<Button>("TitleAllowAdd")!);
        Assert.True(stops[2].IsChecked);

        Click(companion, stops[1]);

        Assert.True(stops[1].IsChecked);
        Assert.Empty(participant.Awareness.TitleAllowList.Entries);
        Assert.False(companion.FindControl<Border>("TitleAllowEditor")!.IsVisible);
        Assert.True(participant.Awareness.Consent.Granted); // awareness stays ON; only the breadth narrowed

        await host.ShutdownAsync();
    }

    /// <summary>
    /// The three forget buttons are three buttons, and each one puts a different question in the
    /// SAME confirm overlay — whose default answer is still No.
    /// </summary>
    [AvaloniaFact]
    public async Task ThreeForgetButtons_ThreeQuestions_InTheOneConfirmOverlay()
    {
        var (host, _, companion) = await OpenAsync();
        var overlay = companion.FindControl<Border>("ConfirmOverlay")!;
        var title = companion.FindControl<TextBlock>("ConfirmTitleText")!;
        var body = companion.FindControl<TextBlock>("ConfirmBodyText")!;
        Assert.False(overlay.IsVisible);

        var asked = new List<string>();
        foreach (var id in new[] { "ForgetThreadButton", "ClearMemoryButton", "ForgetEverythingButton" })
        {
            Click(companion, companion.FindControl<Button>(id)!);
            Assert.True(overlay.IsVisible);
            asked.Add(title.Text + "|" + body.Text);
            Click(companion, companion.FindControl<Button>("ConfirmNoButton")!);
            Assert.False(overlay.IsVisible);
        }

        Assert.Equal(3, asked.Count);
        Assert.Equal(3, asked.Distinct(StringComparer.Ordinal).Count());

        await host.ShutdownAsync();
    }

    // ---- D11: the transcript window as a control ----

    [AvaloniaFact]
    public void TranscriptEmpty_ShowsUpstreamsEmptyLine_AndNoTurnRows()
    {
        var window = new CompanionTranscriptWindow([]);
        window.Show();
        window.UpdateLayout();

        var empty = ById(window, "TranscriptEmpty");
        Assert.NotNull(empty);
        Assert.Equal("nothing yet. the first thing you say is the first thing she keeps.", empty!.Text);
        Assert.Equal("Everything you two have said", ById(window, "TranscriptHeading")!.Text);
        Assert.Equal("her memory lives on this machine only", ById(window, "TranscriptNote")!.Text);
        Assert.DoesNotContain(Texts(window), t => t is "you" or "her");

        window.Close();
    }

    [AvaloniaFact]
    public void TranscriptWithTurns_ShowsEveryPairInOrder_LabelledYouAndHer()
    {
        var window = new CompanionTranscriptWindow(
        [
            new AiMemoryTurn(AiMemoryRole.User, "first thing said"),
            new AiMemoryTurn(AiMemoryRole.Assistant, "her answer"),
            new AiMemoryTurn(AiMemoryRole.User, "second thing said"),
        ]);
        window.Show();
        window.UpdateLayout();

        // Order is the record's order — a transcript that reshuffles is not a transcript.
        Assert.Equal(
            ["Everything you two have said", "you", "first thing said", "her", "her answer", "you", "second thing said", "her memory lives on this machine only"],
            Texts(window));
        Assert.Null(ById(window, "TranscriptEmpty"));

        window.Close();
    }

    /// <summary>
    /// Read-only means read-only: the window offers no way to edit or delete what it shows. A
    /// viewer that grew an editor would be a second, unconfirmed write path into the one document
    /// the three forget scopes are careful about.
    /// </summary>
    [AvaloniaFact]
    public void TheTranscriptOffersNoWayToChangeWhatItShows()
    {
        var window = new CompanionTranscriptWindow([new AiMemoryTurn(AiMemoryRole.User, "a turn")]);
        window.Show();
        window.UpdateLayout();

        Assert.Empty(window.GetVisualDescendants().OfType<Button>());
        Assert.Empty(window.GetVisualDescendants().OfType<TextBox>());
        Assert.Empty(window.GetVisualDescendants().OfType<CheckBox>());
        // Nothing that takes a value at all, by base type rather than by enumerating the ones
        // someone happened to think of.
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.TemplatedControl>(),
            c => c is not ScrollViewer and not Avalonia.Controls.Primitives.ScrollBar
                and not Avalonia.Controls.Primitives.Thumb and not RepeatButton);

        // ...and the turn it shows really is on screen, so the emptiness above is about controls,
        // not about an empty window.
        Assert.Contains("a turn", Texts(window));

        window.Close();
    }

    // =====================================================================================

    /// <summary>The transcript is built in code (no XAML name scope), so its parts are found the
    /// way the headed harness finds them: by automation id.</summary>
    private static TextBlock? ById(Window window, string automationId) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => AutomationProperties.GetAutomationId(t) == automationId);

    private static IReadOnlyList<string> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text))
            .Select(t => t.Text!)];

    private static IReadOnlyList<RadioButton> Stops(CompanionWindow companion) =>
    [
        companion.FindControl<RadioButton>("DialOff")!,
        companion.FindControl<RadioButton>("DialBroad")!,
        companion.FindControl<RadioButton>("DialTitles")!,
    ];

    private static async Task<(ApplicationHost Host, MainWindow Window, CompanionWindow Companion)> OpenAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-privacy-headless-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);

        var window = new MainWindow(host!);
        window.Show();
        window.UpdateLayout();

        ClickIn(window, window.FindControl<RadioButton>("DoorCompanion")!);
        window.UpdateLayout();
        ClickIn(window, window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CompanionButton"));

        var companion = window.Companion;
        Assert.NotNull(companion);
        companion!.UpdateLayout();
        return (host!, window, companion);
    }

    private static void Click(CompanionWindow companion, Control control)
    {
        ClickIn(companion, control);
        companion.UpdateLayout();
    }

    private static void ClickIn(Window window, Control control)
    {
        var center = control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
    }
}
