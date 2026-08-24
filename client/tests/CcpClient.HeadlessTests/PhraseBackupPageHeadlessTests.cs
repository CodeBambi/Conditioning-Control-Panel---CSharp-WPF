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
using CcpClient.Desktop.Storage;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.HeadlessTests;

/// <summary>
/// <b>Phrase backup, from the button</b> (census #9). The machinery landed proved and unreachable —
/// nothing in the shell called <see cref="PhraseBackup.ExportAsync"/> or
/// <see cref="PhraseBackup.ImportAsync"/> — so until these facts every phrase a user had written
/// still died with a bad update.
///
/// <para>Upstream is App Settings → Data: two buttons
/// (<c>Views/Controls/AppSettings/DataSettingsSection.xaml:101</c>, <c>:106</c>) wired at
/// <c>MainWindow/MainWindow.PresetIO.cs:62</c> and <c>:93</c>. The port's counterpart door is
/// System (§9 D2).</para>
///
/// <para><b>What is substituted, and what is not.</b> ONE seam:
/// <see cref="SystemPage.Picker"/>. Avalonia marks <c>IStorageProvider</c>
/// <c>[NotClientImplementable]</c> and enforces it with a member user code cannot write, so there
/// is no fake provider and the seam has to be replaced one level up — which is exactly why the
/// six lines inside <see cref="AvaloniaUserFilePicker"/> stay a HEADED gate. Everything else here
/// is real: the composition root, the shell, the page, the three live
/// <see cref="Persistence.PersistenceStore{TDocument}"/> instances the running modules read, the
/// real <see cref="PhraseBackup"/> and the real toast surface.</para>
///
/// <para>Draw-level only: visual tree, real input, real document state. Nothing here claims a
/// dialog opened, a pixel composited, or that any of it is legible.</para>
/// </summary>
public class PhraseBackupPageHeadlessTests : HeadlessTest
{
    private async Task<(ApplicationHost Host, MainWindow Window, SystemPage Page, ScriptedPicker Picker)>
        BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-phrase-page-" + Guid.NewGuid().ToString("N"));
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
        // Through the DOOR, with real input. The module has to be REACHABLE — that is the whole
        // content of the row this closes — not merely constructed.
        Click(window, window.FindControl<RadioButton>("DoorSystem")!);

        var page = (SystemPage)window.PageFor(ShellRoutes.System);
        var picker = new ScriptedPicker();
        page.Picker = picker;
        return (host, window, page, picker);
    }

    private static void Click(MainWindow window, Control control)
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

    private static Button ButtonOf(MainWindow window, string name) =>
        window.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Name == name)
        ?? throw new InvalidOperationException($"no {name} reachable through the System door");

    private static Border ConfirmPanel(MainWindow window) =>
        window.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Name == "PhraseImportConfirmPanel")
        ?? throw new InvalidOperationException("no PhraseImportConfirmPanel on the System page");

    /// <summary>
    /// Waits for the page's operation to have REPORTED, on the page's own published state: the
    /// buttons are re-enabled in the operation's <c>finally</c>, after its outcome has been said.
    /// A deterministic condition through the shared bounded helper — never a sleep, and never an
    /// assumption that the await happened to complete synchronously.
    /// </summary>
    private static async Task SettleAsync(MainWindow window)
    {
        await CcpClient.Tests.TestWait.Until(
            () => ButtonOf(window, "ExportPhrasesButton").IsEnabled,
            "the phrase-backup operation to finish and re-enable its buttons");
        window.UpdateLayout();
    }

    private static async Task AwaitConfirmationAsync(MainWindow window)
    {
        await CcpClient.Tests.TestWait.Until(
            () => ConfirmPanel(window).IsVisible,
            "the import confirmation strip to be put in front of the user");
        window.UpdateLayout();
    }

    private static string BackupText(params (string Pool, string Phrase)[] entries)
    {
        var pools = entries
            .GroupBy(e => e.Pool, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, Dictionary<string, bool>>(
                g.Key, g.ToDictionary(e => e.Phrase, _ => true, StringComparer.Ordinal)))
            .ToList();
        return PhraseBackupFile.Build(pools, DateTimeOffset.UnixEpoch, "test");
    }

    // ==========================================================================================
    // The buttons exist and say upstream's words.
    // ==========================================================================================

    /// <summary>
    /// Both buttons are reachable through the rail, and both carry upstream's captions and hints
    /// (<c>Localization/Languages/en.json:4881-4886</c>).
    /// </summary>
    [AvaloniaFact]
    public async Task BothButtonsAreReachableThroughTheSystemDoorAndCarryUpstreamsWords()
    {
        var (host, window, _, _) = await BootAsync();
        try
        {
            var export = ButtonOf(window, "ExportPhrasesButton");
            var import = ButtonOf(window, "ImportPhrasesButton");
            Assert.Equal(PhraseBackupNotices.ExportButton, export.Content);
            Assert.Equal(PhraseBackupNotices.ImportButton, import.Content);
            Assert.Equal(PhraseBackupNotices.ExportTooltip, ToolTip.GetTip(export));
            Assert.Equal(PhraseBackupNotices.ImportTooltip, ToolTip.GetTip(import));

            // The blurb is the sentence that says WHY the feature exists, and it is the reason this
            // entry was admitted ahead of the rest of its cluster.
            var blurb = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Name == "PhraseBackupBlurb");
            Assert.Equal(PhraseBackupNotices.Blurb, blurb.Text);

            // Nothing is being asked yet.
            Assert.False(ConfirmPanel(window).IsVisible);
            Assert.Empty(window.Toasts.Messages);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    // ==========================================================================================
    // Export.
    // ==========================================================================================

    /// <summary>
    /// A press really writes the user's phrases through the seam, and the toast says how many —
    /// upstream's own confirmation count (<c>Services/PhraseBackupService.cs:84</c>,
    /// <c>MainWindow/MainWindow.PresetIO.cs:82</c>).
    ///
    /// <para><b>And it names no file.</b> Upstream prints the full path in the same sentence
    /// (<c>PresetIO.cs:82</c>); the port cannot, and the assertion below is over the string the
    /// user actually reads rather than over the seam that produced it.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task AnExportSaysHowManyPhrasesItSaved_AndNamesNoFileOrFolderAtAll()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            Click(window, ButtonOf(window, "ExportPhrasesButton"));
            await SettleAsync(window);

            Assert.NotNull(picker.Saved);
            var written = Assert.IsType<PhraseFileRead.Parsed>(PhraseBackupFile.Read(picker.Saved!));
            var expected = PhraseBackupFile.CountEntries(written.Pools);
            Assert.Equal(
                [PhraseBackupNotices.Exported(expected).Message],
                window.Toasts.Messages);

            var said = window.Toasts.Messages.Single();
            Assert.DoesNotContain(".ccpphrases", said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(picker.SuggestedFileName!, said, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\\', said);
            Assert.DoesNotContain('/', said);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// A refused write is SAID, and it is said as the seam's typed code rather than as an
    /// exception message — upstream shows <c>ex.Message</c> (<c>PresetIO.cs:87</c>), which for the
    /// classes this path raises carries the path of the file that failed.
    /// </summary>
    [AvaloniaFact]
    public async Task ARefusedExportSaysTheTypedReasonAndNeverAnExceptionMessage()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            picker.SaveOutcome = new UserFileSave.Refused(UserFileRefusal.NoPicker);
            Click(window, ButtonOf(window, "ExportPhrasesButton"));
            await SettleAsync(window);

            Assert.Equal(
                [PhraseBackupNotices.ExportRefused(UserFileRefusal.NoPicker).Message],
                window.Toasts.Messages);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// <b>A cancelled picker says NOTHING</b>, in both directions. That is upstream's behaviour
    /// rather than an omission — <c>if (dialog.ShowDialog() != true) return;</c>
    /// (<c>PresetIO.cs:76</c>, <c>:104</c>) — and a toast for it would be the app telling the user
    /// what the user just did.
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingEitherPickerRaisesNoToastAtAll()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            picker.SaveOutcome = UserFileSave.Cancelled.Instance;
            Click(window, ButtonOf(window, "ExportPhrasesButton"));
            await SettleAsync(window);
            Assert.Empty(window.Toasts.Messages);

            picker.OpenOutcome = UserFileOpen.Cancelled.Instance;
            Click(window, ButtonOf(window, "ImportPhrasesButton"));
            await SettleAsync(window);
            Assert.Empty(window.Toasts.Messages);
            Assert.False(ConfirmPanel(window).IsVisible);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    // ==========================================================================================
    // Import.
    // ==========================================================================================

    /// <summary>
    /// <b>Validate, then ask, then touch anything</b> — upstream's order
    /// (<c>PresetIO.cs:107-122</c>). A file that is not a backup is refused without ever putting a
    /// scary question in front of the user, and nothing is replaced.
    /// </summary>
    [AvaloniaFact]
    public async Task AFileThatIsNotABackupIsRefusedWithoutEverAskingToReplaceAnything()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            var session = host.Participants.OfType<SessionParticipant>().Single();
            var before = session.SubliminalPreset.Current.Phrases.Keys.Order(StringComparer.Ordinal).ToArray();

            picker.OpenOutcome = new UserFileOpen.Opened("this is not json at all");
            Click(window, ButtonOf(window, "ImportPhrasesButton"));
            await SettleAsync(window);

            Assert.False(ConfirmPanel(window).IsVisible);
            Assert.Equal(
                [PhraseBackupNotices.ImportRefusedFile(PhraseFileRefusal.NotJson).Message],
                window.Toasts.Messages);
            Assert.Equal(before, session.SubliminalPreset.Current.Phrases.Keys.Order(StringComparer.Ordinal));
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// The confirmation is a SECOND, SEPARATE gesture, and declining it leaves every pool exactly
    /// as it was. Upstream asks in a modal that says plainly what will be replaced
    /// (<c>PresetIO.cs:114-118</c>); this port asks in an inline strip, for the reason
    /// <c>StudioPage.axaml:1898-1900</c> already records.
    /// </summary>
    [AvaloniaFact]
    public async Task TheConfirmationIsAskedFirst_AndDecliningItLeavesEveryPoolUntouched()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            var session = host.Participants.OfType<SessionParticipant>().Single();
            var before = session.SubliminalPreset.Current.Phrases.Keys.Order(StringComparer.Ordinal).ToArray();

            picker.OpenOutcome = new UserFileOpen.Opened(BackupText(
                (PhraseBackupFile.SubliminalPoolName, "you are getting sleepy")));
            Click(window, ButtonOf(window, "ImportPhrasesButton"));
            await AwaitConfirmationAsync(window);

            // The question is up, nothing has been replaced, and the app has said nothing.
            Assert.True(ConfirmPanel(window).IsVisible);
            Assert.Equal(before, session.SubliminalPreset.Current.Phrases.Keys.Order(StringComparer.Ordinal));
            Assert.Empty(window.Toasts.Messages);
            var title = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Name == "PhraseImportConfirmTitle");
            var detail = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Name == "PhraseImportConfirmDetail");
            Assert.Equal(PhraseBackupNotices.ConfirmTitle, title.Text);
            Assert.Equal(PhraseBackupNotices.ConfirmDetail, detail.Text);

            Click(window, ButtonOf(window, "PhraseImportDeclineButton"));
            await SettleAsync(window);

            Assert.False(ConfirmPanel(window).IsVisible);
            Assert.Empty(window.Toasts.Messages);
            Assert.Equal(before, session.SubliminalPreset.Current.Phrases.Keys.Order(StringComparer.Ordinal));
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// Accepting replaces the pools in the stores the running modules read, and the toast names
    /// what was NOT applied. This build has three of upstream's seventeen pools
    /// (<see cref="PhraseBackupFile"/>), so an import that reported a bare success would be hiding
    /// a partial restore — which is why <see cref="PhraseImport.Imported.PoolsSkipped"/> exists.
    /// </summary>
    [AvaloniaFact]
    public async Task AcceptingReplacesThePools_AndTheToastNamesEveryListItCouldNotRestore()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            var session = host.Participants.OfType<SessionParticipant>().Single();
            picker.OpenOutcome = new UserFileOpen.Opened(BackupText(
                (PhraseBackupFile.SubliminalPoolName, "good girl"),
                (PhraseBackupFile.SubliminalPoolName, "deeper"),
                ("MantraPool", "a pool this build does not have")));

            Click(window, ButtonOf(window, "ImportPhrasesButton"));
            await AwaitConfirmationAsync(window);
            Click(window, ButtonOf(window, "PhraseImportAcceptButton"));
            await SettleAsync(window);

            Assert.Equal(
                ["deeper", "good girl"],
                session.SubliminalPreset.Current.Phrases.Keys.Order(StringComparer.Ordinal));

            var said = window.Toasts.Messages.Single();
            Assert.Contains("MantraPool", said, StringComparison.Ordinal);
            Assert.Equal(
                PhraseBackupNotices.Imported(1, 2, ["MantraPool"], persisted: true).Message,
                said);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// <b>A restore that has not reached disk is NOT reported as a success.</b> The pools are live
    /// and the store is still dirty, so the honest sentence is "restored, but not yet saved" and
    /// the toast is a WARNING. Upstream saves inside its own handler
    /// (<c>PresetIO.cs:124</c>) and has no such state to report.
    ///
    /// <para>Driven by stopping the session first, which is what really produces it: a store with
    /// no live operation generation reports not-persisted rather than throwing
    /// (<see cref="PhraseBackup"/>), and the teardown flush is the backstop.</para>
    /// </summary>
    [AvaloniaFact]
    public async Task ARestoreThatDidNotReachDiskSaysSoAndIsAWarningRatherThanASuccess()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            var session = host.Participants.OfType<SessionParticipant>().Single();
            await session.StopAsync();
            Assert.False(session.SubliminalPreset.Running);

            picker.OpenOutcome = new UserFileOpen.Opened(BackupText(
                (PhraseBackupFile.SubliminalPoolName, "still in memory")));
            Click(window, ButtonOf(window, "ImportPhrasesButton"));
            await AwaitConfirmationAsync(window);
            Click(window, ButtonOf(window, "PhraseImportAcceptButton"));
            await SettleAsync(window);

            // The pools ARE live, which is why this is not a refusal.
            Assert.Equal(["still in memory"], session.SubliminalPreset.Current.Phrases.Keys);

            var said = window.Toasts.Messages.Single();
            Assert.Contains("but not yet saved", said, StringComparison.Ordinal);
            Assert.Equal(PhraseBackupNotices.Imported(1, 1, [], persisted: false).Message, said);

            // And it wears the warning accent rather than the success one — the difference a user
            // reads before they read the sentence.
            var toast = window.Toasts.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("toast"));
            Assert.Contains("warning", toast.Classes);
            Assert.DoesNotContain("success", toast.Classes);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// One gesture, one operation. Upstream cannot need this — its pickers are modal
    /// (<c>PresetIO.cs:76</c>) — but Avalonia's are awaited with the window still live, so a second
    /// press would open a second dialog over the first and two imports would race their writes into
    /// the same three stores.
    /// </summary>
    [AvaloniaFact]
    public async Task ASecondPressWhileAPickerIsStillOpenIsIgnored()
    {
        var (host, window, _, picker) = await BootAsync();
        try
        {
            picker.HoldOpen = true;
            var export = ButtonOf(window, "ExportPhrasesButton");
            Click(window, export);
            window.UpdateLayout();

            Assert.False(export.IsEnabled);
            Assert.Equal(PhraseBackupNotices.Busy, export.Content);
            Assert.False(ButtonOf(window, "ImportPhrasesButton").IsEnabled);

            // Press both again anyway. A disabled Avalonia button raises no Click, so the shut
            // buttons ARE the mechanism — and the seam's own call counts are what say so, rather
            // than the livery that produced them.
            Click(window, export);
            Click(window, ButtonOf(window, "ImportPhrasesButton"));
            Assert.Equal(1, picker.SaveCalls);
            Assert.Equal(0, picker.OpenCalls);

            picker.Release();
            await SettleAsync(window);
            Assert.True(export.IsEnabled);
            Assert.Equal(PhraseBackupNotices.ExportButton, export.Content);
            Assert.Equal(1, picker.SaveCalls);
        }
        finally
        {
            await host.ShutdownAsync();
        }
    }

    /// <summary>
    /// The seam, scripted. Replaces <see cref="AvaloniaUserFilePicker"/> only — everything the page
    /// does with what comes back is the product's own.
    /// </summary>
    private sealed class ScriptedPicker : IUserFilePicker
    {
        private TaskCompletionSource? _held;

        public string? Saved { get; private set; }

        public string? SuggestedFileName { get; private set; }

        public UserFileSave SaveOutcome { get; set; } = UserFileSave.Saved.Instance;

        public UserFileOpen OpenOutcome { get; set; } = UserFileOpen.Cancelled.Instance;

        public int SaveCalls { get; private set; }

        public int OpenCalls { get; private set; }

        /// <summary>Keeps the dialog "open" until <see cref="Release"/>, the way a real modal would
        /// while the user is still choosing. It is what makes the second-press latch observable at
        /// all: without it every scripted call returns before the test can press anything.</summary>
        public bool HoldOpen { get; set; }

        public void Release() => _held?.TrySetResult();

        public async Task<UserFileSave> SaveTextAsync(
            string title, UserFileKind kind, string suggestedFileName, string contents)
        {
            SaveCalls++;
            SuggestedFileName = suggestedFileName;
            await GateAsync();
            if (SaveOutcome is UserFileSave.Saved)
            {
                Saved = contents;
            }

            return SaveOutcome;
        }

        public async Task<UserFileOpen> OpenTextAsync(string title, UserFileKind kind)
        {
            OpenCalls++;
            await GateAsync();
            return OpenOutcome;
        }

        private async Task GateAsync()
        {
            if (!HoldOpen)
            {
                return;
            }

            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await CcpClient.Tests.TestWait.Until(
                _held.Task, "the test to release the file dialog it is holding open");
        }
    }
}
