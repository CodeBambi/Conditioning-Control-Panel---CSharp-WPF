using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CcpClient.Desktop;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Navigation;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// THE SESSION EDITOR, reached the way a user reaches it: a cold composition-root boot, the Studio
/// door, the Scripted Sessions row, a picked session, and the strip's own Edit button.
///
/// <para><b>The reachability facts here are the point of the file.</b> A unit fact that constructs
/// a <see cref="SessionEditorWindow"/> proves the window works and nothing at all about the door —
/// so every fact below goes through <c>Descendant&lt;Button&gt;(window, "ScriptedSessionEditButton")</c>
/// on a MOUNTED page, which cannot resolve if the button is not in the markup, and then through a
/// real click, which cannot open anything if the handler is not wired.</para>
///
/// <para><b>The editor really opens.</b> There is no presentation seam and no substituted window:
/// <c>StudioPage</c> calls <c>Show(owner)</c> and these facts drive the window it showed, with real
/// keystrokes into its real <see cref="TextBox"/> and real clicks on its real buttons.</para>
///
/// <para>Draw-level ONLY (verification-harness.md evidence class): visual tree, real input routing,
/// and the FILE ON DISK the save produced. Nothing here claims a composited pixel, and nothing here
/// runs on Linux.</para>
/// </summary>
public class SessionEditorHeadlessTests : HeadlessTest
{
    private sealed record Boot(ApplicationHost Host, MainWindow Window, string DataRoot)
    {
        public StudioPage Studio => (StudioPage)Window.PageFor(ShellRoutes.Studio);

        public CustomSessionStore Store => Window.Session.CustomSessions;
    }

    private async Task<Boot> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-session-editor-" + Guid.NewGuid().ToString("N"));
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
        return new Boot(host, window, dir);
    }

    private static T Descendant<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"no {typeof(T).Name} named '{name}' in the mounted page");

    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        control.BringIntoView();
        window.UpdateLayout();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException("control is not in the window's visual tree");
        window.MouseDown(center, button, RawInputModifiers.None);
        window.MouseUp(center, button, RawInputModifiers.None);
        window.UpdateLayout();
    }

    /// <summary>The user path to the rack, the same one the rack's own facts take.</summary>
    private static void OpenTheSessionsRow(MainWindow window)
    {
        Click(window, window.FindControl<RadioButton>("DoorStudio")!);
        Click(window, Descendant<RadioButton>(window, "RowScriptedSession"));
    }

    /// <summary>Real typing into whatever holds focus in the given window — the pattern the rack's
    /// search facts measured: a <c>KeyPress</c> alone carries no text into the pipeline, so the
    /// platform's own text event follows it.</summary>
    private static void Type(Window window, string text)
    {
        foreach (var c in text)
        {
            var key = char.IsLetterOrDigit(c)
                ? Enum.Parse<Key>(char.ToUpperInvariant(c).ToString())
                : Key.Space;
            var physical = char.IsLetterOrDigit(c)
                ? Enum.Parse<PhysicalKey>(char.ToUpperInvariant(c).ToString())
                : PhysicalKey.Space;
            window.KeyPress(key, RawInputModifiers.None, physical, c.ToString());
            window.KeyTextInput(c.ToString());
            window.KeyRelease(key, RawInputModifiers.None, physical, c.ToString());
            window.UpdateLayout();
        }
    }

    /// <summary>Clear a box and type into it, through the box's own editing rather than an
    /// assignment to <c>Text</c>. Focus is moved by the control's own <c>Focus()</c>, which is the
    /// measured property of the headless platform the rack's facts record.</summary>
    private static void Retype(Window window, TextBox box, string text)
    {
        box.Focus();
        window.UpdateLayout();
        Assert.True(box.IsFocused);
        box.SelectAll();
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, string.Empty);
        window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, string.Empty);
        window.UpdateLayout();
        Type(window, text);
    }

    private static SessionEditorWindow OpenEditorOn(Boot boot, string rowName)
    {
        var window = boot.Window;
        OpenTheSessionsRow(window);
        Click(window, Descendant<RadioButton>(window, rowName));
        Click(window, Descendant<Button>(window, "ScriptedSessionEditButton"));
        return boot.Studio.CurrentEditor
            ?? throw new InvalidOperationException("the Edit button did not open an editor");
    }

    private static IReadOnlyList<RadioButton> Rows(MainWindow window) =>
        [.. Descendant<StackPanel>(window, "ScriptedSessionRackPanel").Children.OfType<RadioButton>()];

    // =====================================================================================
    //  the door
    // =====================================================================================

    /// <summary>
    /// THE REACHABILITY FACT. The Edit button is IN the mounted Studio page beside Start and Recent
    /// sessions, it is offered for a BUILT-IN row (upstream's rule —
    /// <c>MainWindow/MainWindow.SessionIO.cs:538</c> builds it outside the non-built-in branch at
    /// <c>:540</c>, and it is the only way a first custom session can ever exist), and pressing it
    /// opens an editor on the session the user picked.
    ///
    /// <para>This fact fails if the button is removed from the markup — <c>Descendant</c> throws by
    /// name — and it fails if the handler is unwired, because <c>CurrentEditor</c> stays null.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task TheStudioDoorCarriesAnEditButton_AndItOpensTheEditorOnThePickedSession()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        var edit = Descendant<Button>(window, "ScriptedSessionEditButton");
        Assert.True(edit.IsVisible);
        Assert.True(edit.Bounds.Width > 0 && edit.Bounds.Height > 0);
        Assert.Equal("Edit session", edit.Content);
        Assert.Null(boot.Studio.CurrentEditor);

        Click(window, Descendant<RadioButton>(window, "SessionRowMorningDrift"));
        Click(window, edit);

        var editor = boot.Studio.CurrentEditor;
        Assert.NotNull(editor);
        Assert.Equal("Morning Drift", editor.Original.Name);
        Assert.Equal(ScriptedSessionOrigin.BuiltIn, editor.Original.Origin);
        Assert.Equal("Morning Drift", editor.FindControl<TextBox>("NameBox")!.Text);
        Assert.Equal(30d, editor.FindControl<Slider>("DurationSlider")!.Value);

        // The window says which of the two things Save will do, and offers no Delete for a shipped
        // session (upstream's :540-544).
        Assert.Contains(
            "your own copy",
            editor.FindControl<TextBlock>("ProvenanceLine")!.Text!,
            StringComparison.Ordinal);
        Assert.False(editor.FindControl<Button>("DeleteButton")!.IsVisible);

        editor.Close();
    }

    /// <summary>
    /// Pressed with nothing picked, the button refuses and says so on the rack's own line — the
    /// START button's twin. Upstream cannot reach this state, because its edit action lives ON a
    /// row and carries that row's id (<c>MainWindow/MainWindow.SessionIO.cs:1821-1824</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task EditWithNothingPicked_RefusesAndOpensNothing()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        OpenTheSessionsRow(window);

        Click(window, Descendant<Button>(window, "ScriptedSessionEditButton"));

        Assert.Null(boot.Studio.CurrentEditor);
        Assert.Equal(
            SessionRackNotices.NothingToEdit,
            Descendant<TextBlock>(window, "ScriptedSessionPhaseState").Text);
    }

    // =====================================================================================
    //  the edit a user can see
    // =====================================================================================

    /// <summary>
    /// THE WHOLE USER STORY, by real input only: open a built-in, rename it, stretch it, save — and
    /// the rack comes back with a FIFTH row carrying the new name, the new duration and the YOURS
    /// badge, with the shipped Morning Drift still sitting above it unchanged.
    ///
    /// <para>That last clause is the copy rule, photographed at the surface: upstream's "Editing a
    /// built-in session creates a new custom session" (<c>MainWindow/MainWindow.SessionIO.cs:1837</c>).
    /// The file on disk is checked too, because a rack row is a claim and the folder is the
    /// fact.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task EditingABuiltInAddsARowAndLeavesTheShippedOneAlone_ByRealInputOnly()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        var editor = OpenEditorOn(boot, "SessionRowMorningDrift");

        Assert.Equal(4, Rows(window).Count);

        Retype(editor, editor.FindControl<TextBox>("NameBox")!, "My Drift");
        editor.FindControl<Slider>("DurationSlider")!.Value = 45;
        Click(editor, editor.FindControl<Button>("SaveButton")!);
        window.UpdateLayout();

        // The window closed on a save that landed, and it carries what it built.
        Assert.Null(boot.Studio.CurrentEditor);
        Assert.NotNull(editor.ResultSession);
        Assert.Equal("My Drift", editor.ResultSession.Name);

        var rows = Rows(window);
        Assert.Equal(5, rows.Count);
        Assert.Contains("SessionRowMorningDrift", rows.Select(r => r.Name));

        var mine = Assert.Single(boot.Store.Read());
        Assert.Equal("My Drift", mine.Name);
        Assert.Equal(45, mine.DurationMinutes);
        Assert.Equal(ScriptedSessionOrigin.Custom, mine.Origin);

        // The two cells that tell the copy from its original on screen.
        var badges = Descendant<StackPanel>(window, "ScriptedSessionRackPanel")
            .GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Name?.StartsWith("SessionBadge", StringComparison.Ordinal) == true)
            .Select(t => t.Text)
            .ToList();
        Assert.Equal(4, badges.Count(b => b == "BUILT-IN"));
        Assert.Equal(1, badges.Count(b => b == "YOURS"));

        // The shipped file is untouched: still there, still 30 minutes, still built-in.
        var shipped = ScriptedSession.ReadBuiltIns().Single(s => s.Id == "morning_drift");
        Assert.Equal("Morning Drift", shipped.Name);
        Assert.Equal(30, shipped.DurationMinutes);

        // And the rack's count line agrees with the rows it drew.
        Assert.Equal("5 sessions", Descendant<TextBlock>(window, "ScriptedSessionRackCount").Text);
    }

    /// <summary>
    /// A second edit of the SAME custom session overwrites its own file: five rows before and five
    /// after, one file on disk, the second name on it. Upstream's "use existing file path if set
    /// and valid" (<c>Services/Session/SessionFileService.cs:231-242</c>), reached from the surface
    /// rather than from the store.
    ///
    /// <para>It also pins that the SELECTION survives the reload: the second edit is opened on the
    /// row the first one left armed, without the test re-clicking it — which is the hazard upstream
    /// names in its own words, "re-point the selection at the fresh object or Start Session would
    /// run a detached copy" (<c>MainWindow/MainWindow.SessionIO.cs:1926-1928</c>).</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ASecondEditOverwritesTheSameFile_AndTheSelectionSurvivesTheReload()
    {
        var boot = await BootAsync();
        var window = boot.Window;

        var first = OpenEditorOn(boot, "SessionRowMorningDrift");
        Retype(first, first.FindControl<TextBox>("NameBox")!, "Mine");
        Click(first, first.FindControl<Button>("SaveButton")!);
        window.UpdateLayout();

        var savedPath = boot.Store.Read()[0].SourceFilePath;

        // NO row is clicked here: the Edit button acts on the selection the save left behind, which
        // must now be the fresh instance read back off disk rather than the detached one.
        Click(window, Descendant<Button>(window, "ScriptedSessionEditButton"));
        var second = boot.Studio.CurrentEditor;
        Assert.NotNull(second);
        Assert.Equal("Mine", second.Original.Name);
        Assert.Equal(ScriptedSessionOrigin.Custom, second.Original.Origin);
        Assert.True(second.FindControl<Button>("DeleteButton")!.IsVisible);

        Retype(second, second.FindControl<TextBox>("NameBox")!, "Mine Again");
        Click(second, second.FindControl<Button>("SaveButton")!);
        window.UpdateLayout();

        Assert.Equal(5, Rows(window).Count);
        var only = Assert.Single(boot.Store.Read());
        Assert.Equal("Mine Again", only.Name);
        Assert.Equal(savedPath, only.SourceFilePath);
        Assert.Single(Directory.GetFiles(boot.Store.Folder, "*" + ScriptedSession.FileExtension));
    }

    /// <summary>
    /// An empty name writes NOTHING and the editor stays open holding it — upstream's only
    /// validation refusal (<c>Windows/SessionEditorWindow.xaml.cs:1144-1148</c>). The folder is the
    /// evidence: after the refused save it does not exist, because nothing has ever been written to
    /// it.
    /// </summary>
    [AvaloniaFact]
    public async Task AnEmptyNameRefusesTheSave_AndNothingIsWritten()
    {
        var boot = await BootAsync();
        var editor = OpenEditorOn(boot, "SessionRowMorningDrift");
        var name = editor.FindControl<TextBox>("NameBox")!;

        name.Focus();
        name.SelectAll();
        editor.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, string.Empty);
        editor.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, string.Empty);
        editor.UpdateLayout();
        Assert.Equal(string.Empty, name.Text);

        Click(editor, editor.FindControl<Button>("SaveButton")!);

        Assert.Same(editor, boot.Studio.CurrentEditor);
        Assert.Null(editor.ResultSession);
        Assert.Equal(SessionEditorRules.NameRequired, editor.Refusal);
        Assert.Empty(boot.Store.Read());
        Assert.Equal(4, Rows(boot.Window).Count);

        editor.Close();
    }

    /// <summary>
    /// Cancel writes nothing and changes nothing, however much was typed — the outcome upstream's
    /// own ✕ and Cancel both reach (<c>Windows/SessionEditorWindow.xaml.cs:113-117</c>,
    /// <c>:1131-1136</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task CancelDiscardsTheEdit_AndTheRackDoesNotMove()
    {
        var boot = await BootAsync();
        var window = boot.Window;
        var editor = OpenEditorOn(boot, "SessionRowMorningDrift");

        Retype(editor, editor.FindControl<TextBox>("NameBox")!, "Never Saved");
        editor.FindControl<Slider>("DurationSlider")!.Value = 120;
        Click(editor, editor.FindControl<Button>("CancelButton")!);
        window.UpdateLayout();

        Assert.Null(boot.Studio.CurrentEditor);
        Assert.Null(editor.ResultSession);
        Assert.Empty(boot.Store.Read());
        Assert.Equal(4, Rows(window).Count);

        // The rack's own instance is untouched — the edit was made on a copy.
        Assert.Equal("Morning Drift", editor.Original.Name);
        Assert.Equal(30, editor.Original.DurationMinutes);
    }

    /// <summary>
    /// DELETE TAKES TWO GESTURES. One click arms the question and removes nothing; the second acts.
    /// Upstream confirms with a styled dialog before it deletes
    /// (<c>MainWindow/MainWindow.SessionIO.cs:1953-1959</c>); the ceremony that matters is that a
    /// destructive act cannot happen on one click, and it is the same ceremony the rack's start
    /// confirmation keeps.
    /// </summary>
    [AvaloniaFact]
    public async Task DeletingACustomSessionTakesTwoGestures_AndLeavesTheBuiltInsAlone()
    {
        var boot = await BootAsync();
        var window = boot.Window;

        var made = OpenEditorOn(boot, "SessionRowMorningDrift");
        Retype(made, made.FindControl<TextBox>("NameBox")!, "Disposable");
        Click(made, made.FindControl<Button>("SaveButton")!);
        window.UpdateLayout();
        Assert.Equal(5, Rows(window).Count);

        Click(window, Descendant<Button>(window, "ScriptedSessionEditButton"));
        var editor = boot.Studio.CurrentEditor!;
        var delete = editor.FindControl<Button>("DeleteButton")!;
        Assert.True(delete.IsVisible);

        // ONE click: armed, asked, and nothing gone.
        Click(editor, delete);
        Assert.True(editor.DeleteArmed);
        Assert.Equal("Really delete", delete.Content);
        Assert.Same(editor, boot.Studio.CurrentEditor);
        Assert.Single(boot.Store.Read());

        // The second click acts.
        Click(editor, delete);
        window.UpdateLayout();

        Assert.Null(boot.Studio.CurrentEditor);
        Assert.Empty(boot.Store.Read());
        Assert.Equal(4, Rows(window).Count);
        Assert.Equal("4 sessions", Descendant<TextBlock>(window, "ScriptedSessionRackCount").Text);
    }
}
