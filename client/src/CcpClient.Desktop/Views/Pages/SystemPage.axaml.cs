using Avalonia.Controls;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Motion;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Storage;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Integration proof (contract §10.1): the phase-outcome trace, the demonstrator participants'
/// running state and the typed capability states are visibly reachable from a user path — now
/// through the System door rather than off the retired dashboard card.
///
/// <para>It also carries the ONE app-scoped preference this port owns: the motion level
/// (<see cref="MotionLevel"/>). Upstream keeps it in App Settings → Performance
/// (<c>Views/Controls/AppSettings/PerformanceSettingsSection.xaml:173-179</c>, written back at
/// <c>MainWindow/MainWindow.Settings.cs:325-326</c>); this port has no App Settings door and the
/// System door is where that class of surface landed.</para>
///
/// <para><b>And now PHRASE BACKUP</b> (census #9), for the same reason: upstream's home for it is
/// App Settings → Data (<c>Views/Controls/AppSettings/DataSettingsSection.xaml:101</c>,
/// <c>:106</c>). It is the module whose absence was a DATA-LOSS risk rather than a missing
/// feature — the machinery landed with nothing calling it, so until now every phrase a user had
/// written still died with a bad update.</para>
/// </summary>
public partial class SystemPage : UserControl
{
    private readonly PersistenceStore<MotionSettingsDocument>? _motion;
    private readonly SessionParticipant _session;
    private readonly ToastHost _toasts;
    private TaskCompletionSource<bool>? _importAnswer;

    /// <param name="session">The ONE conditioning session the composition root built. The three
    /// phrase pools this build can back up are its documents
    /// (<c>Session/SessionParticipant.cs:704</c>, <c>:713</c>, <c>:755</c>), and they must be the
    /// SAME store objects the running modules read — a page-local second store would export a copy
    /// of the file rather than what the user has on screen.</param>
    /// <param name="toasts">The shell's ONE toast surface. Export and import outcomes are said
    /// here rather than in a modal, which is the divergence census #54 exists to close.</param>
    public SystemPage(ApplicationHost host, SessionParticipant session, ToastHost toasts)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(toasts);
        InitializeComponent();

        _session = session;
        _toasts = toasts;
        Picker = AvaloniaUserFilePicker.For(this);
        InitializePhraseBackup();

        _motion = HostedMotion.StoreOf(host);
        MotionBlurb.Text = MotionNotices.Blurb;
        // Upstream's own bind: SelectedIndex IS the enum ordinal, clamped to the item count
        // (PerformanceSettingsSection.xaml.cs:94-95).
        MotionLevelPicker.SelectedIndex = (int)(_motion?.Current.Level ?? MotionLevel.Full);
        MotionLevelPicker.SelectionChanged += (_, _) => OnMotionLevelPicked();
        DescribeMotion();

        var lines = host.Trace.Entries.Concat(
            host.Participants.Select(p => $"{p.Name}: {(p.Running ? "running" : "stopped")}"));
        if (host.Capabilities is { } capabilities)
        {
            // Capability contract §9.1: the shell surfaces each capability's typed state —
            // the composition root's probe results are visibly reachable from a user path.
            lines = lines.Concat(capabilities.Names.Select(n => $"capability {n}: {Describe(capabilities.GetState(n))}"));
        }

        TraceText.Text = string.Join(Environment.NewLine, lines);

        // Async contract §5.4: the heartbeat's tick text reaches the page through the real
        // dispatch boundary; the reporter is invoked only inside a boundary post.
        foreach (var heartbeat in host.Participants.OfType<HeartbeatParticipant>())
        {
            heartbeat.TickReporter = text => HeartbeatText.Text = text;
        }
    }

    /// <summary>
    /// The user's choice, written back the way upstream writes it: the picker's index mapped to
    /// the enum, with anything unexpected landing on Full
    /// (<c>MainWindow/MainWindow.UiUpdates.cs:2471-2477</c>). Persisted immediately — the enqueued
    /// write is serialized by the store and the teardown flush is the backstop.
    /// </summary>
    private void OnMotionLevelPicked()
    {
        var level = MotionLevelPicker.SelectedIndex switch
        {
            1 => MotionLevel.Reduced,
            2 => MotionLevel.Off,
            _ => MotionLevel.Full,
        };

        if (_motion is not null && level != _motion.Current.Level)
        {
            _motion.Mutate(document => document.Level = level);
            _ = _motion.Save();
        }

        DescribeMotion();
    }

    /// <summary>
    /// What the choice actually does, said on the page. The sentences are
    /// <see cref="MotionNotices"/>'s, and the only thing this method adds is the two runtime
    /// values they need: the stored level and this machine's OS animation flag.
    /// </summary>
    private void DescribeMotion()
    {
        MotionLevelState.Text = _motion is null
            ? MotionNotices.NoStore
            : MotionNotices.Describe(_motion.Current.Level, DtrhMotionPreference.ReadOsClientAreaAnimation());
    }

    /// <summary>
    /// The open-or-save seam this page uses. The product default is the real Avalonia picker on
    /// this page's own top level — resolved per operation, so a gesture always opens a dialog owned
    /// by the window the user is actually looking at
    /// (<see cref="AvaloniaUserFilePicker.For"/>).
    ///
    /// <para>Settable so headless facts drive the REAL page against a scripted seam. That is the
    /// shape the shell already uses for its launchers (<c>MainWindow.Intake.Open</c>), and it is
    /// forced here: Avalonia marks <c>IStorageProvider</c> <c>[NotClientImplementable]</c>, so
    /// there is no fake provider and the seam has to be replaced one level up.</para>
    /// </summary>
    public IUserFilePicker Picker { get; set; }

    private void InitializePhraseBackup()
    {
        PhraseBackupTitle.Text = PhraseBackupNotices.ModuleTitle;
        PhraseBackupBlurb.Text = PhraseBackupNotices.Blurb;
        ExportPhrasesButton.Content = PhraseBackupNotices.ExportButton;
        ImportPhrasesButton.Content = PhraseBackupNotices.ImportButton;
        ToolTip.SetTip(ExportPhrasesButton, PhraseBackupNotices.ExportTooltip);
        ToolTip.SetTip(ImportPhrasesButton, PhraseBackupNotices.ImportTooltip);
        PhraseImportConfirmTitle.Text = PhraseBackupNotices.ConfirmTitle;
        PhraseImportConfirmDetail.Text = PhraseBackupNotices.ConfirmDetail;
        PhraseImportAcceptButton.Content = PhraseBackupNotices.ConfirmAccept;
        PhraseImportDeclineButton.Content = PhraseBackupNotices.ConfirmDecline;
        NameButtons();

        ExportPhrasesButton.Click += (_, _) => _ = RunAsync(ExportAsync);
        ImportPhrasesButton.Click += (_, _) => _ = RunAsync(ImportAsync);
        PhraseImportAcceptButton.Click += (_, _) => AnswerImport(true);
        PhraseImportDeclineButton.Click += (_, _) => AnswerImport(false);
    }

    /// <summary>Buttons publish their caption to UIA so the headed harness reads what the user
    /// reads, the same way the shell's START control does (<c>MainWindow.axaml.cs:286</c>).</summary>
    private void NameButtons()
    {
        foreach (var button in new[]
                 {
                     ExportPhrasesButton, ImportPhrasesButton,
                     PhraseImportAcceptButton, PhraseImportDeclineButton,
                 })
        {
            button.SetValue(
                Avalonia.Automation.AutomationProperties.NameProperty,
                button.Content as string ?? string.Empty);
        }
    }

    /// <summary>
    /// One gesture, one operation, and the SHUT BUTTONS are the whole mechanism — there is no
    /// second flag, because a disabled Avalonia button raises no <c>Click</c> and a second guard
    /// would be a latch no test could ever red.
    ///
    /// <para>Upstream cannot need this: its pickers are modal
    /// (<c>MainWindow/MainWindow.PresetIO.cs:76</c>), so a second click while a dialog is up is
    /// impossible. Avalonia's are awaited and the window stays live, so without this the same
    /// button would open a second dialog over the first — and two imports racing would interleave
    /// their writes into the same three stores.</para>
    /// </summary>
    private async Task RunAsync(Func<PhraseBackup, Task> operation)
    {
        SetPhraseButtonsBusy(true);
        try
        {
            // Built per operation: PhraseBackup holds nothing but references, and building it here
            // is what lets Picker be replaced between operations without a second indirection.
            await operation(new PhraseBackup(
                Picker, _session.SubliminalPreset, _session.LockCardPreset, _session.BouncingTextPreset));
        }
        catch (Exception ex)
        {
            // A fault on this path is SAID, never swallowed: the whole reason this module exists
            // is that phrases can be lost, and an import that stopped halfway with the buttons
            // quietly re-enabled would be the silent failure. The exception's TYPE only — its
            // message would carry the path of the file that failed, which may not leave the seam
            // (Storage/UserFilePicker.cs, UserFileRefusal).
            PhraseImportConfirmPanel.IsVisible = false;
            _importAnswer = null;
            Say(PhraseBackupNotices.Faulted(ex.GetType().Name));
        }
        finally
        {
            SetPhraseButtonsBusy(false);
        }
    }

    private async Task ExportAsync(PhraseBackup backup) => ReportExport(await backup.ExportAsync());

    private async Task ImportAsync(PhraseBackup backup) =>
        ReportImport(await backup.ImportAsync(AskToReplace));

    private void SetPhraseButtonsBusy(bool busy)
    {
        ExportPhrasesButton.IsEnabled = !busy;
        ImportPhrasesButton.IsEnabled = !busy;
        ExportPhrasesButton.Content = busy ? PhraseBackupNotices.Busy : PhraseBackupNotices.ExportButton;
        ImportPhrasesButton.Content = busy ? PhraseBackupNotices.Busy : PhraseBackupNotices.ImportButton;
        NameButtons();
    }

    /// <summary>
    /// The confirmation gesture, as the <c>confirmReplace</c> callback
    /// <see cref="PhraseBackup.ImportAsync"/> requires. It is invoked ONLY for a file that has
    /// already validated — upstream's order (<c>MainWindow/MainWindow.PresetIO.cs:107-118</c>) —
    /// so a file that is not a backup never puts a scary question in front of the user.
    /// </summary>
    private Task<bool> AskToReplace()
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _importAnswer = answer;
        PhraseImportConfirmPanel.IsVisible = true;
        return answer.Task;
    }

    private void AnswerImport(bool replace)
    {
        PhraseImportConfirmPanel.IsVisible = false;
        var answer = _importAnswer;
        _importAnswer = null;
        answer?.TrySetResult(replace);
    }

    /// <summary>
    /// What an export attempt says.
    ///
    /// <para><b>A cancelled picker says NOTHING</b>, and that is upstream's behaviour rather than
    /// an omission: <c>if (dialog.ShowDialog() != true) return;</c>
    /// (<c>MainWindow/MainWindow.PresetIO.cs:76</c>). A user who closed the dialog does not need to
    /// be told they closed the dialog.</para>
    /// </summary>
    private void ReportExport(PhraseExport outcome)
    {
        switch (outcome)
        {
            case PhraseExport.Exported exported:
                Say(PhraseBackupNotices.Exported(exported.PhraseCount));
                break;
            case PhraseExport.Refused refused:
                Say(PhraseBackupNotices.ExportRefused(refused.Reason));
                break;
            // PhraseExport.Cancelled: upstream is silent, and so is this.
        }
    }

    /// <summary>
    /// What an import attempt says. A DECLINED confirmation is silent for the export path's
    /// reason — upstream's <c>if (!confirm) return;</c> (<c>PresetIO.cs:119</c>) — and the strip
    /// closing is the acknowledgement.
    /// </summary>
    private void ReportImport(PhraseImport outcome)
    {
        PhraseImportConfirmPanel.IsVisible = false;
        _importAnswer = null;
        switch (outcome)
        {
            case PhraseImport.Imported imported:
                Say(PhraseBackupNotices.Imported(
                    imported.PoolsApplied, imported.PhraseCount, imported.PoolsSkipped, imported.Persisted));
                break;
            case PhraseImport.RefusedFile refused:
                Say(PhraseBackupNotices.ImportRefusedFile(refused.Reason));
                break;
            case PhraseImport.RefusedPicker refused:
                Say(PhraseBackupNotices.ImportRefusedPicker(refused.Reason));
                break;
            // PhraseImport.Cancelled and PhraseImport.Declined: nothing happened, nothing is said.
        }
    }

    /// <summary>
    /// Every outcome stays on screen until the user closes it. Upstream reports both results
    /// through a MODAL styled dialog with an OK button (<c>PresetIO.cs:81-83</c>, <c>:125-127</c>);
    /// the non-blocking equivalent of "you must acknowledge this" is a toast that does not expire
    /// while it is being read — see <see cref="ToastHost.ShowUntilDismissed"/>.
    /// </summary>
    private void Say((string Message, ToastKind Kind) notice) =>
        _toasts.ShowUntilDismissed(notice.Message, notice.Kind);

    private static string Describe(CapabilityState state) => state switch
    {
        CapabilityState.Available available => $"Available — {available.Detail}",
        CapabilityState.Unavailable unavailable => $"Unavailable ({unavailable.Reason.Code}) — {unavailable.Reason.Detail}",
        CapabilityState.Degraded degraded => $"Degraded ({degraded.Reason.Code}) — survives: {degraded.SurvivingSemantics}; {degraded.Reason.Detail}",
        CapabilityState.PermissionRequired permission => $"PermissionRequired ({permission.Reason.Code}) — {permission.Reason.Detail}",
        CapabilityState.DependencyMissing missing => $"DependencyMissing ({missing.Dependency}) — {missing.Reason.Detail}",
        CapabilityState.Faulted faulted => $"Faulted ({faulted.Reason.Code}) — {faulted.Reason.Detail}",
        _ => state.GetType().Name,
    };
}
