using System.Globalization;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Storage;

namespace CcpClient.Desktop.Views.Pages;

/// <summary>
/// Every sentence the <b>Phrase backup</b> module says, and the kind of toast each one is.
///
/// <para>Upstream is App Settings → Data: the two buttons
/// (<c>Views/Controls/AppSettings/DataSettingsSection.xaml:101</c>, <c>:106</c>), their strings
/// (<c>Localization/Languages/en.json:4881-4886</c>), and the six results their handlers report
/// through modal styled dialogs (<c>MainWindow/MainWindow.PresetIO.cs:62-134</c>).</para>
///
/// <para><b>Why the text lives here rather than in the page.</b> The <c>*Notices.cs</c> convention
/// this port already follows (<see cref="SessionRackNotices"/>, <see cref="SchedulerPanelNotices"/>):
/// a sentence in AXAML or in a code-behind can only be checked by a headless test that mounts a
/// window, while a sentence here is checked by a unit fact. Every one below is derived from a TYPED
/// outcome — <see cref="PhraseExport"/>, <see cref="PhraseImport"/> — so there is no path by which
/// the page can say "done" about something that did not happen.</para>
///
/// <para><b>Formatted with <see cref="CultureInfo.InvariantCulture"/></b>, for
/// <see cref="SessionRackNotices"/>'s reason: the headed capture harness reads these back through
/// UIA as text needles, so a machine whose culture renders digits differently would fail a capture
/// of a perfectly correct screen.</para>
///
/// <para><b>No path and no file name appears in any of them</b>, which is a deliberate divergence:
/// upstream prints the full path in both confirmations (<c>PresetIO.cs:82</c>, <c>:127</c>) and in
/// its log. The reason is on <see cref="IUserFilePicker"/> — the seam returns text and typed codes,
/// and nothing else leaves it.</para>
/// </summary>
public static class PhraseBackupNotices
{
    /// <summary>Upstream's section title (<c>en.json:4881</c>).</summary>
    public const string ModuleTitle = "Phrase backup";

    /// <summary>Upstream's hint, verbatim (<c>en.json:4882</c>) — it is the sentence that says WHY
    /// the feature exists, which is the whole reason this row was admitted ahead of its
    /// cluster.</summary>
    public const string Blurb =
        "Save or restore your lock-card phrases, subliminals, mantras and other custom text. "
        + "Back them up before an update or when moving to a new PC.";

    /// <summary>Upstream's export button (<c>en.json:4883</c>), minus its leading emoji under the
    /// §9 D8 emoji-stripping rule.</summary>
    public const string ExportButton = "Export";

    /// <summary>Upstream's import button (<c>en.json:4885</c>), same rule.</summary>
    public const string ImportButton = "Import";

    /// <summary>Upstream's export tooltip (<c>en.json:4884</c>).</summary>
    public const string ExportTooltip = "Save all your custom phrases to a backup file.";

    /// <summary>Upstream's import tooltip (<c>en.json:4886</c>).</summary>
    public const string ImportTooltip =
        "Restore custom phrases from a backup file (replaces your current phrases).";

    /// <summary>Upstream's confirmation title (<c>PresetIO.cs:115</c>).</summary>
    public const string ConfirmTitle = "Import Phrases?";

    /// <summary>Upstream's confirmation body, verbatim (<c>PresetIO.cs:116-117</c>). It is asked
    /// only AFTER the chosen file has validated, which is upstream's order
    /// (<c>PresetIO.cs:107-118</c>) and <see cref="PhraseBackup.ImportAsync"/>'s.</summary>
    public const string ConfirmDetail =
        "This replaces your current lock-card phrases, subliminals, mantras and other custom text "
        + "with the ones in the backup file. Continue?";

    /// <summary>Upstream's accept caption (<c>PresetIO.cs:118</c>).</summary>
    public const string ConfirmAccept = "Import";

    /// <summary>Upstream's decline caption (<c>PresetIO.cs:118</c>).</summary>
    public const string ConfirmDecline = "Cancel";

    /// <summary>
    /// A dialog is open and both buttons are shut. A DIVERGENCE forced by the seam rather than
    /// chosen: upstream's picker is modal (<c>Microsoft.Win32.SaveFileDialog.ShowDialog</c>,
    /// <c>PresetIO.cs:76</c>) so a second click is impossible while one is up, and Avalonia's
    /// <c>IStorageProvider</c> pickers are awaited rather than blocking — so without shutting them
    /// the same button could open a second dialog over the first.
    /// </summary>
    public const string Busy = "Working…";

    /// <summary>
    /// What a completed export says. Upstream's is "Saved {count} phrase(s) to:\n{path}"
    /// (<c>PresetIO.cs:82</c>); the count is upstream's own confirmation number
    /// (<c>Services/PhraseBackupService.cs:84</c>) and the path is the part that may not leave the
    /// seam.
    /// </summary>
    public static (string Message, ToastKind Kind) Exported(int phraseCount) =>
        (string.Create(CultureInfo.InvariantCulture, $"Saved {phraseCount} {Phrases(phraseCount)}."),
         ToastKind.Success);

    /// <summary>
    /// What a refused export says. Upstream's is "Could not export phrases:\n{ex.Message}"
    /// (<c>PresetIO.cs:86-87</c>); the port substitutes the typed reason for the exception text,
    /// because an <c>IOException</c>'s message carries the path
    /// (<see cref="UserFileRefusal"/>).
    /// </summary>
    public static (string Message, ToastKind Kind) ExportRefused(UserFileRefusal reason) =>
        ("Could not export phrases: " + Reason(reason), ToastKind.Error);

    /// <summary>
    /// What a completed import says.
    ///
    /// <para>Upstream's is "Restored {count} phrase(s). You may need to reopen any open phrase
    /// editors to see them." (<c>PresetIO.cs:125-126</c>). Two things are added, and both are
    /// things upstream cannot say because it has no typed outcome to say them from:</para>
    ///
    /// <list type="number">
    /// <item><description><b>The pools that were NOT applied are named.</b>
    /// <see cref="PhraseImport.Imported.PoolsSkipped"/> exists precisely so an import that drops
    /// half a file cannot report a bare success — this build has three of upstream's seventeen
    /// pools (<see cref="PhraseBackupFile"/>).</description></item>
    /// <item><description><b><see cref="PhraseImport.Imported.Persisted"/> false is NOT a
    /// success.</b> The pools are live and the store is still dirty, so the honest sentence is
    /// "restored, but not yet saved" and the toast is a WARNING rather than a success. Upstream
    /// saves inside the handler and has no such state (<c>PresetIO.cs:124</c>).</description></item>
    /// </list>
    /// </summary>
    public static (string Message, ToastKind Kind) Imported(
        int poolsApplied, int phraseCount, IReadOnlyList<string> poolsSkipped, bool persisted)
    {
        ArgumentNullException.ThrowIfNull(poolsSkipped);

        var phrases = Count(phraseCount) + " " + Phrases(phraseCount);
        var lists = Count(poolsApplied) + " " + Lists(poolsApplied);
        var head = persisted
            ? "Restored " + phrases + " into " + lists + "."
            : "Restored " + phrases + " into " + lists + ", but not yet saved: they are in use now "
                + "and will be written to disk when the app closes.";

        if (poolsSkipped.Count > 0)
        {
            var one = poolsSkipped.Count == 1;
            head += " " + Count(poolsSkipped.Count) + " " + Lists(poolsSkipped.Count) + " in the file "
                + (one ? "was" : "were") + " skipped because this build has no module for "
                + (one ? "it" : "them") + ": " + string.Join(", ", poolsSkipped) + ".";
        }

        // Upstream's closing advice, unchanged (PresetIO.cs:126).
        head += " You may need to reopen any open phrase editors to see them.";
        return (head, persisted ? ToastKind.Success : ToastKind.Warning);
    }

    /// <summary>
    /// What a refused FILE says. Upstream's is "That file isn't a valid phrase backup:\n{error}"
    /// (<c>PresetIO.cs:109-110</c>). Nothing was changed and the user was never asked to confirm,
    /// which is upstream's order and the reason the sentence can be this plain.
    /// </summary>
    public static (string Message, ToastKind Kind) ImportRefusedFile(PhraseFileRefusal reason) =>
        ("That file isn't a phrase backup: " + reason switch
        {
            PhraseFileRefusal.NotJson => "the bytes are not JSON.",
            PhraseFileRefusal.NotAnObject => "the JSON in it is not an object.",
            // Upstream's "Unrecognized file (schema '<found>')"
            // (Services/PhraseBackupService.cs:97) minus the schema it found: the typed refusal
            // does not carry that string, and a user cannot act on it either way.
            PhraseFileRefusal.WrongSchema => "it is not a backup this build recognises.",
            // Upstream's own "No phrases in file" (Services/PhraseBackupService.cs:99).
            PhraseFileRefusal.NoPhrases => "there are no phrases in it.",
            PhraseFileRefusal.NoKnownPools =>
                "none of the phrase lists in it is one this build has, so restoring it would change "
                + "nothing.",
            _ => "it cannot be restored by this build.",
        }, ToastKind.Error);

    /// <summary>What a refused READ says — the picker's own reason, before any file was
    /// parsed (<see cref="PhraseImport.RefusedPicker"/>).</summary>
    public static (string Message, ToastKind Kind) ImportRefusedPicker(UserFileRefusal reason) =>
        ("Could not read that file: " + Reason(reason), ToastKind.Error);

    /// <summary>
    /// What an unexpected fault says. Upstream catches around both handlers and shows
    /// <c>ex.Message</c> (<c>PresetIO.cs:86-87</c>, <c>:131-132</c>); this port shows the exception
    /// TYPE and nothing else, because the message of the exception classes this path actually
    /// raises carries the full path of the file that failed.
    /// </summary>
    public static (string Message, ToastKind Kind) Faulted(string exceptionTypeName) =>
        ("Phrase backup could not finish (" + exceptionTypeName + "). Nothing further was written.",
         ToastKind.Error);

    /// <summary>
    /// The picker's typed codes, said in words. Never <c>ex.Message</c>: an
    /// <see cref="IOException"/>'s text carries the full path of the file that failed, which is the
    /// shortest possible route to defeating the seam (<see cref="UserFileRefusal"/>).
    /// </summary>
    private static string Reason(UserFileRefusal reason) => reason switch
    {
        // TopLevel.StorageProvider is never null — it degrades to a NoopStorageProvider — so
        // without the probe this would be a dead button rather than a message.
        UserFileRefusal.NoPicker =>
            "this desktop has no file picker, so nothing was asked for and nothing was changed.",
        UserFileRefusal.ReadFailed => "it could not be read.",
        UserFileRefusal.WriteFailed => "the place you chose could not be written.",
        UserFileRefusal.TooLarge =>
            "it is larger than " + Count(UserFilePicker.MaxTextBytes / (1024 * 1024))
                + " MB, which is more than a phrase backup can be.",
        _ => "it could not be used.",
    };

    /// <summary>Numbers in these sentences are read back through UIA by the headed harness, so they
    /// are formatted invariantly — <see cref="SessionRackNotices"/>'s rule and its reason.</summary>
    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Phrases(int count) => count == 1 ? "phrase" : "phrases";

    private static string Lists(int count) => count == 1 ? "phrase list" : "phrase lists";
}
