using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Storage;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// IMPORTING A SESSION, reached the way a user reaches it: a cold composition-root boot, the Studio
/// door, the Scripted Sessions row, and the strip's own Import button.
///
/// <para><b>The reachability facts here are the point of the file.</b> A unit fact that constructs
/// a <see cref="SessionImport"/> proves the arithmetic and nothing at all about the door — so every
/// fact below goes through <c>Descendant&lt;Button&gt;(window, "ScriptedSessionImportButton")</c>
/// on a MOUNTED page, which cannot resolve if the button is not in the markup, and then through a
/// real click, which imports nothing if the handler is not wired.</para>
///
/// <para><b>What is substituted, and what is not.</b> ONE seam:
/// <see cref="StudioPage.Picker"/>. Avalonia marks <c>IStorageProvider</c>
/// <c>[NotClientImplementable]</c> and enforces it with a member user code cannot write, so there
/// is no fake provider and the seam has to be replaced one level up — which is why the six lines
/// inside <see cref="AvaloniaUserFilePicker"/> stay a HEADED gate. Everything else is real: the
/// composition root, the shell, the page, the real <see cref="CustomSessionStore"/> under the
/// boot's own data root, and the REAL FILE the import writes.</para>
///
/// <para>Draw-level only (verification-harness.md evidence class): visual tree, real input routing,
/// and the file on disk. Nothing here claims a dialog opened, a pixel composited, or that any of it
/// is legible — and nothing here runs on Linux.</para>
/// </summary>
public class SessionImportHeadlessTests : HeadlessTest
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window, ScriptedPicker Picker)
    {
        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);

        public CustomSessionStore Store => Window.Session.CustomSessions;
    }

    /// <summary>The seam, scripted. Replaces <see cref="AvaloniaUserFilePicker"/> only —
    /// everything the page does with what comes back is the product's own.</summary>
    private sealed class ScriptedPicker : IUserFilePicker
    {
        private TaskCompletionSource? _held;

        public UserFileOpen OpenOutcome { get; set; } = UserFileOpen.Cancelled.Instance;

        /// <summary>Thrown instead of answering, the way a storage backend this port does not
        /// catch would.</summary>
        public Exception? Fault { get; set; }

        public int OpenCalls { get; private set; }

        /// <summary>Keeps the dialog "open" until <see cref="Release"/>, the way a real modal would
        /// while the user is still choosing. It is what makes the second-press latch observable at
        /// all — the pattern <c>PhraseBackupPageHeadlessTests</c> measured for the same seam.
        /// </summary>
        public bool HoldOpen { get; set; }

        public void Release() => _held?.TrySetResult();

        public Task<UserFileSave> SaveTextAsync(
            string title, UserFileKind kind, string suggestedFileName, string contents) =>
            throw new InvalidOperationException("import never saves through the picker");

        public async Task<UserFileOpen> OpenTextAsync(string title, UserFileKind kind)
        {
            OpenCalls++;
            if (Fault is not null)
            {
                throw Fault;
            }

            if (HoldOpen)
            {
                // BOUNDED, through the one approved helper: an unbounded await on a signal the test
                // may never set would hang the host rather than fail it. Same shape as
                // PhraseBackupPageHeadlessTests' own gate over the same seam.
                _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await CcpClient.Tests.TestWait.Until(
                    _held.Task, "the test to release the file dialog it is holding open");
            }

            return OpenOutcome;
        }
    }

    private async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-session-import-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot { SettingsPathFactory = () => Path.Combine(dir, "settings.json") };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        Track(host!);

        var window = new MainWindow(host!);
        host!.BindUiDispatch(new AvaloniaUiDispatch());
        window.Show();
        window.UpdateLayout();

        // Through the DOORS, with real input: the rack has to be REACHABLE, not merely built.
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));

        var page = (StudioPage)window.PageFor(ShellRoutes.Studio);
        var picker = new ScriptedPicker();
        page.Picker = picker;
        return new Boot(host!, window, picker);
    }

    private static T Descendant<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var centre = control.TranslatePoint(
                         new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
                     ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
        window.UpdateLayout();
    }

    private static IReadOnlyList<RadioButton> Rows(MainWindow window) =>
        [.. Descendant<StackPanel>(window, "ScriptedSessionRackPanel").Children.OfType<RadioButton>()];

    private static string RackLine(MainWindow window) =>
        Descendant<TextBlock>(window, "ScriptedSessionPhaseState").Text ?? string.Empty;

    /// <summary>Waits on the page's OWN published state: the button is re-enabled in the
    /// operation's <c>finally</c>, after the outcome has been reported. A deterministic condition
    /// through the shared bounded helper — never a sleep, and never an assumption that the await
    /// happened to complete synchronously.</summary>
    private static async Task SettleAsync(MainWindow window)
    {
        await CcpClient.Tests.TestWait.Until(
            () => Descendant<Button>(window, "ScriptedSessionImportButton").IsEnabled,
            "the session import to finish and re-enable its button");
        window.UpdateLayout();
    }

    /// <summary>The file names in the user's sessions folder. The folder-existence test lives here
    /// rather than in a fact body, on <c>ScriptedSessionLogTests</c>' precedent: a
    /// <c>Directory.Exists</c> inside a fact is an fs-predicate shape the vacuous-shape guard
    /// requires a ledger disposition for, and the honest fix is not to have one.</summary>
    private static IReadOnlyList<string> FileNamesIn(CustomSessionStore store) =>
        Directory.Exists(store.Folder)
            ? [.. Directory.GetFiles(store.Folder).Select(Path.GetFileName).OfType<string>()]
            : [];

    private static string FileText(string id, string name, int minutes = 45) =>
        new ScriptedSession
        {
            Id = id,
            Name = name,
            DurationMinutes = minutes,
            Description = "Someone else made this.",
            Difficulty = ScriptedSessionDifficulty.Hard,
        }.ToJson();

    // =====================================================================================
    //  the door
    // =====================================================================================

    /// <summary>
    /// THE REACHABILITY FACT. The Import button is IN the mounted Studio page beside Start, Pause,
    /// Edit and Recent sessions, arranged with real width and height, and carrying the rack's own
    /// caption and hint.
    ///
    /// <para><b>The arranged-width assertion is not decoration.</b> Two landed defects on this exact
    /// strip were of that shape — a toolbar count arranged 0 DIP wide, and a fourth button pushed
    /// off the visible strip by a <c>StackPanel</c> — so a fifth button that "exists" is not the
    /// claim being made here.</para>
    ///
    /// <para>This fact fails if the button is removed from the markup (<c>Descendant</c> throws by
    /// name) and if the caption drifts from <see cref="SessionRackNotices.ImportButton"/>.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheStudioDoorCarriesAnImportButton_ArrangedAndCarryingTheRacksOwnWords()
    {
        var boot = await BootAsync();
        var window = boot.Window;

        var import = Descendant<Button>(window, "ScriptedSessionImportButton");
        Assert.True(import.IsVisible);
        Assert.True(import.IsEnabled);
        Assert.True(import.Bounds.Width > 0 && import.Bounds.Height > 0);
        Assert.Equal(SessionRackNotices.ImportButton, import.Content);
        Assert.Equal(SessionRackNotices.ImportTooltip, ToolTip.GetTip(import));

        // Inside the strip it was declared in, rather than arranged past its parent's edge.
        var strip = import.GetVisualAncestors().OfType<WrapPanel>().First();
        var offset = import.TranslatePoint(default, strip);
        Assert.NotNull(offset);
        Assert.True(offset.Value.X + import.Bounds.Width <= strip.Bounds.Width + 0.5);
        Assert.True(offset.Value.Y + import.Bounds.Height <= strip.Bounds.Height + 0.5);

        // Nothing has been asked for yet.
        Assert.Equal(0, boot.Picker.OpenCalls);
        await boot.Host.ShutdownAsync();
    }

    // =====================================================================================
    //  the import a user can see
    // =====================================================================================

    /// <summary>
    /// THE WHOLE USER STORY, by real input only: press Import, choose a session file that came from
    /// somewhere else, and the rack comes back with a FIFTH row — the four shipped ones untouched —
    /// while the file itself is on disk under the user's own folder with the YOURS badge.
    ///
    /// <para>The row is located by the id the PRODUCT minted (read back off the disk), so this fact
    /// also pins that the rack the user sees was built from the bytes that landed rather than from
    /// the instance that produced them.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task PressingImportAddsARowAndAFile_ByRealInputOnly()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        boot.Picker.OpenOutcome = new UserFileOpen.Opened(FileText("from_a_friend", "Shared Drift"));

        Assert.Equal(4, Rows(window).Count);
        Click(window, Descendant<Button>(window, "ScriptedSessionImportButton"));
        await SettleAsync(window);

        var landed = Assert.Single(boot.Store.Read());
        Assert.Equal("Shared Drift", landed.Name);
        Assert.Equal(45, landed.DurationMinutes);
        Assert.Equal(ScriptedSessionOrigin.Custom, landed.Origin);
        Assert.NotEqual("from_a_friend", landed.Id);

        var rows = Rows(window);
        Assert.Equal(5, rows.Count);
        Assert.Contains("SessionRowMorningDrift", rows.Select(r => r.Name));
        var badges = Descendant<StackPanel>(window, "ScriptedSessionRackPanel")
            .GetVisualDescendants().OfType<Border>()
            .Where(b => b.Name?.StartsWith("SessionBadge", StringComparison.Ordinal) == true)
            .Select(b => (b.Child as TextBlock)?.Text)
            .ToList();
        Assert.Equal(4, badges.Count(b => b == "BUILT-IN"));
        Assert.Equal(1, badges.Count(b => b == "YOURS"));

        // The sentence the user reads names the session and the folder, and no path at all.
        Assert.Equal(SessionRackNotices.Imported(landed), RackLine(window));
        Assert.Contains("Shared Drift", RackLine(window), StringComparison.Ordinal);
        Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), RackLine(window), StringComparison.Ordinal);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// A file that is not a session is refused ON SCREEN with a reason the user can act on, and the
    /// rack does not move: still four rows, and the folder the import would have written into does
    /// not even exist. Upstream refuses the same way and in the same order
    /// (<c>MainWindow/MainWindow.SessionIO.cs:2099-2101</c>, "Failed: {message}").
    /// </summary>
    [AvaloniaFact]
    public async Task AFileThatIsNotASessionIsRefusedOnScreen_AndTheRackDoesNotMove()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        boot.Picker.OpenOutcome = new UserFileOpen.Opened("{\"id\": \"x\", \"name\": \"\"}");

        Click(window, Descendant<Button>(window, "ScriptedSessionImportButton"));
        await SettleAsync(window);

        Assert.Equal(
            SessionRackNotices.ImportRefusedFile(SessionFileRefusal.NoName), RackLine(window));
        Assert.Contains("Nothing was added.", RackLine(window), StringComparison.Ordinal);
        Assert.Equal(4, Rows(window).Count);
        Assert.Empty(boot.Store.Read());
        Assert.Empty(FileNamesIn(boot.Store));

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// An import does not re-aim the START button. The user's pick survives the repaint the import
    /// causes — which is the rack's own rule for every other repaint and matters more here, because
    /// the row that arrived came from outside the application.
    ///
    /// <para><b>The last two lines are the fact, and they were added because a mutation caught this
    /// one being weaker than its own name.</b> Re-pointing <c>_scriptedSelection</c> at the
    /// imported session AFTER the repaint leaves the rack looking exactly right — Morning Drift is
    /// still the checked row — while the START button is aimed at the file that just arrived. The
    /// checked radio is a claim; the confirmation names the session the button will really run
    /// (<c>StudioPage.RenderScriptedSession</c> builds it from <c>_scriptedSelection</c>), so the
    /// gesture is carried one press further and the sentence is read.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task AnImportDoesNotStealTheUsersPick()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Assert.Contains("Morning Drift", RackLine(window), StringComparison.Ordinal);

        boot.Picker.OpenOutcome = new UserFileOpen.Opened(FileText("from_a_friend", "Shared Drift"));
        Click(window, Descendant<Button>(window, "ScriptedSessionImportButton"));
        await SettleAsync(window);

        Assert.Equal(5, Rows(window).Count);
        var picked = Assert.Single(Rows(window), r => r.IsChecked == true);
        Assert.Equal("SessionRowMorningDrift", picked.Name);

        // What the button is really aimed at, asked by pressing it.
        Click(window, Descendant<Button>(window, "ScriptedSessionStartButton"));
        Assert.Equal(
            "Start Morning Drift?",
            Descendant<TextBlock>(window, "ScriptedSessionConfirmTitle").Text);

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// A seam that FAULTS is a sentence and a live button, not a dead one. An exception out of an
    /// unobserved <see cref="Task"/> is swallowed at collection, so without the page's catch a
    /// broken picker would be an Import button that silently does nothing for the rest of the
    /// session — and its own latch would leave it disabled forever.
    ///
    /// <para>The TYPE is shown and never the message, because the message of the classes this path
    /// raises carries the full path of the file that failed
    /// (<see cref="UserFileRefusal"/>).</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ASeamThatFaultsSaysSo_AndLeavesTheButtonUsable()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        boot.Picker.Fault = new IOException(
            "C:" + Path.DirectorySeparatorChar + "Secret Folder is busy");

        Click(window, Descendant<Button>(window, "ScriptedSessionImportButton"));
        await SettleAsync(window);

        Assert.Equal(SessionRackNotices.ImportFaulted(nameof(IOException)), RackLine(window));
        Assert.DoesNotContain("Secret Folder", RackLine(window), StringComparison.Ordinal);
        Assert.True(Descendant<Button>(window, "ScriptedSessionImportButton").IsEnabled);
        Assert.Empty(boot.Store.Read());

        await boot.Host.ShutdownAsync();
    }

    /// <summary>
    /// A second press while a dialog is already up opens NOTHING. Upstream cannot reach this state
    /// — its <c>OpenFileDialog.ShowDialog()</c> is modal
    /// (<c>Windows/SessionEditorWindow.xaml.cs:1068</c>) — and Avalonia's pickers are awaited rather
    /// than blocking, so the latch is the port's own and this is the fact that proves it is really
    /// there.
    /// </summary>
    [AvaloniaFact]
    public async Task ASecondPressWhileTheDialogIsUpOpensNothing()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        boot.Picker.HoldOpen = true;
        boot.Picker.OpenOutcome = new UserFileOpen.Opened(FileText("from_a_friend", "Shared Drift"));

        var button = Descendant<Button>(window, "ScriptedSessionImportButton");
        Click(window, button);
        await CcpClient.Tests.TestWait.Until(
            () => boot.Picker.OpenCalls == 1 && !Descendant<Button>(window, "ScriptedSessionImportButton").IsEnabled,
            "the first import dialog to be open and its button shut");

        Click(window, button);
        Assert.Equal(1, boot.Picker.OpenCalls);

        boot.Picker.Release();
        await SettleAsync(window);
        Assert.Single(boot.Store.Read());
        Assert.Equal(1, boot.Picker.OpenCalls);

        await boot.Host.ShutdownAsync();
    }
}
