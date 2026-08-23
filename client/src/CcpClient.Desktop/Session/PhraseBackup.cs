using System.Reflection;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Storage;

namespace CcpClient.Desktop.Session;

/// <summary>What an export attempt did.</summary>
public abstract record PhraseExport
{
    private PhraseExport() { }

    /// <summary>Written. <paramref name="PhraseCount"/> is upstream's confirmation count
    /// (<c>Services/PhraseBackupService.cs:84</c>) — and it is a COUNT rather than the path
    /// upstream shows (<c>MainWindow/MainWindow.PresetIO.cs:82</c>), because a path may not leave
    /// the seam.</summary>
    public sealed record Exported(int PhraseCount) : PhraseExport;

    /// <summary>The user closed the picker. Nothing was written.</summary>
    public sealed record Cancelled : PhraseExport
    {
        public static readonly Cancelled Instance = new();
    }

    /// <summary>Nothing was written, for the named reason.</summary>
    public sealed record Refused(UserFileRefusal Reason) : PhraseExport;
}

/// <summary>What an import attempt did.</summary>
public abstract record PhraseImport
{
    private PhraseImport() { }

    /// <param name="PoolsApplied">How many phrase pools were replaced.</param>
    /// <param name="PhraseCount">Upstream's confirmation count over the applied pools
    /// (<c>PhraseBackupService.cs:151</c>).</param>
    /// <param name="PoolsSkipped">Every member of the file that was NOT applied, by name. An import
    /// that drops half a file without saying so is the silent partial write this row forbids.</param>
    /// <param name="Persisted">Whether the replacement reached disk in this operation. False means
    /// the pools ARE live and the store is still dirty — the teardown flush is the backstop — so
    /// the honest sentence to a user is "restored, but not yet saved", never "restored".</param>
    public sealed record Imported(
        int PoolsApplied,
        int PhraseCount,
        IReadOnlyList<string> PoolsSkipped,
        bool Persisted) : PhraseImport;

    /// <summary>The user closed the picker. Nothing was read and nothing changed.</summary>
    public sealed record Cancelled : PhraseImport
    {
        public static readonly Cancelled Instance = new();
    }

    /// <summary>The file was good and the user said no to replacing their phrases. Nothing
    /// changed.</summary>
    public sealed record Declined : PhraseImport
    {
        public static readonly Declined Instance = new();
    }

    /// <summary>The chosen file is not a phrase backup this build can restore. Nothing changed and
    /// the user was never asked to confirm.</summary>
    public sealed record RefusedFile(PhraseFileRefusal Reason) : PhraseImport;

    /// <summary>The file was never read, for the picker's own reason.</summary>
    public sealed record RefusedPicker(UserFileRefusal Reason) : PhraseImport;
}

/// <summary>
/// <b>Phrase backup — export and import</b>, the first consumer of the client's open-or-save seam.
///
/// <para>Upstream: <c>Services/PhraseBackupService.cs</c> and the two buttons in
/// App Settings → Data (<c>Views/Controls/AppSettings/DataSettingsSection.xaml:101</c>,
/// <c>:106</c>) wired at <c>MainWindow/MainWindow.PresetIO.cs:62</c> (export) and <c>:93</c>
/// (import). Its stated purpose is a safety net: "a bad update (or a move to a new machine) can
/// never permanently cost someone the phrases they wrote" (<c>PhraseBackupService.cs:14-16</c>).
/// That is why this entry was admitted ahead of the rest of its cluster — its absence is a
/// data-loss risk rather than a missing feature.</para>
///
/// <para><b>Import REPLACES, it does not merge</b> (<c>PhraseBackupService.cs:110-112</c>: "an
/// import is a restore, not a merge"), and upstream asks first, in a dialog that says plainly what
/// will be replaced (<c>PresetIO.cs:114-118</c>). The order is preserved exactly:
/// <b>validate, then ask, then touch anything</b> (<c>PresetIO.cs:107-122</c>) — a file that is not
/// a backup is refused without ever putting a scary question in front of the user, and a declined
/// question leaves every pool untouched.</para>
///
/// <para><b>What this build has to restore.</b> Three modules own user-editable phrase pools here:
/// Subliminals, Lock Card and Bouncing Text. Upstream's other fourteen pools have no module in this
/// client, so they are reported as skipped rather than silently dropped — see
/// <see cref="PhraseBackupFile"/>, which also explains why the file is upstream's own format in
/// both directions.</para>
///
/// <para><b>No path, no file name, anywhere.</b> This class never sees one: the seam takes text and
/// returns text. Upstream logs the path on both paths and prints it in the confirmation
/// (<c>PresetIO.cs:82-83</c>, <c>:127</c>); that is the deliberate divergence, and the reason is on
/// <see cref="IUserFilePicker"/>.</para>
/// </summary>
public sealed class PhraseBackup
{
    /// <summary>Upstream's dialog titles (<c>MainWindow/MainWindow.PresetIO.cs:70</c>,
    /// <c>:101</c>).</summary>
    public const string ExportTitle = "Export Phrases";

    /// <summary>Upstream's import dialog title (<c>MainWindow/MainWindow.PresetIO.cs:101</c>).</summary>
    public const string ImportTitle = "Import Phrases";

    /// <summary>
    /// The document kind, as the OS dialog describes it. Upstream's filter is
    /// <c>"Phrase backup (*.ccpphrases.json)|*.ccpphrases.json"</c>
    /// (<c>MainWindow/MainWindow.PresetIO.cs:71</c>, <c>:102</c>); the MIME hint is added because
    /// that — not the glob — is what the Linux xdg-desktop-portal picker filters on.
    /// </summary>
    public static readonly UserFileKind FileKind = new(
        Label: "Phrase backup",
        Patterns: ["*.ccpphrases.json"],
        MimeTypes: ["application/json"],
        DefaultExtension: "ccpphrases.json");

    private readonly IUserFilePicker _picker;
    private readonly PersistenceStore<SubliminalPresetDocument> _subliminal;
    private readonly PersistenceStore<LockCardPresetDocument> _lockCard;
    private readonly PersistenceStore<BouncingTextPresetDocument> _bouncingText;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _appVersion;

    public PhraseBackup(
        IUserFilePicker picker,
        PersistenceStore<SubliminalPresetDocument> subliminal,
        PersistenceStore<LockCardPresetDocument> lockCard,
        PersistenceStore<BouncingTextPresetDocument> bouncingText,
        Func<DateTimeOffset>? clock = null,
        string? appVersion = null)
    {
        _picker = picker;
        _subliminal = subliminal;
        _lockCard = lockCard;
        _bouncingText = bouncingText;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _appVersion = appVersion
            ?? typeof(PhraseBackup).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? string.Empty;
    }

    /// <summary>
    /// Asks the user where to keep a copy of every phrase they have written, and writes it there.
    /// </summary>
    public async Task<PhraseExport> ExportAsync()
    {
        var now = _clock();
        var pools = CurrentPools();
        var text = PhraseBackupFile.Build(pools, now, _appVersion);

        var outcome = await _picker.SaveTextAsync(
            ExportTitle, FileKind, PhraseBackupFile.SuggestedFileName(now), text);

        return outcome switch
        {
            UserFileSave.Saved => new PhraseExport.Exported(PhraseBackupFile.CountEntries(pools)),
            UserFileSave.Cancelled => PhraseExport.Cancelled.Instance,
            UserFileSave.Refused refused => new PhraseExport.Refused(refused.Reason),
            _ => new PhraseExport.Refused(UserFileRefusal.WriteFailed),
        };
    }

    /// <summary>
    /// Asks the user for a backup file, checks it, asks whether to replace what they have, and only
    /// then replaces it.
    /// </summary>
    /// <param name="confirmReplace">The user's answer to "this replaces your current phrases".
    /// Required, and invoked only for a file that has already validated — upstream's order
    /// (<c>MainWindow/MainWindow.PresetIO.cs:107-118</c>).</param>
    public async Task<PhraseImport> ImportAsync(Func<Task<bool>> confirmReplace)
    {
        ArgumentNullException.ThrowIfNull(confirmReplace);

        var opened = await _picker.OpenTextAsync(ImportTitle, FileKind);
        if (opened is UserFileOpen.Cancelled)
        {
            return PhraseImport.Cancelled.Instance;
        }

        if (opened is UserFileOpen.Refused refused)
        {
            return new PhraseImport.RefusedPicker(refused.Reason);
        }

        var read = PhraseBackupFile.Read(((UserFileOpen.Opened)opened).Text);
        if (read is PhraseFileRead.Refused refusedFile)
        {
            return new PhraseImport.RefusedFile(refusedFile.Reason);
        }

        var parsed = (PhraseFileRead.Parsed)read;

        if (!await confirmReplace())
        {
            return PhraseImport.Declined.Instance;
        }

        var skipped = new List<string>(parsed.PoolsSkipped);
        var applied = new List<KeyValuePair<string, Dictionary<string, bool>>>();
        var writes = new List<Task<bool>>();

        foreach (var (name, pool) in parsed.Pools)
        {
            switch (name)
            {
                case PhraseBackupFile.SubliminalPoolName:
                    _subliminal.Mutate(document => document.Phrases = pool);
                    applied.Add(new KeyValuePair<string, Dictionary<string, bool>>(name, pool));
                    writes.Add(PersistAsync(_subliminal));
                    break;

                case PhraseBackupFile.LockCardPhrasesName:
                    _lockCard.Mutate(document => document.Phrases = pool);
                    applied.Add(new KeyValuePair<string, Dictionary<string, bool>>(name, pool));
                    writes.Add(PersistAsync(_lockCard));
                    break;

                case PhraseBackupFile.BouncingTextPoolName:
                    // This module stores the ENABLED set as an ordered list rather than upstream's
                    // dictionary (Session/BouncingTextPresetDocument.cs Phrases) — a recorded
                    // divergence there, and the same read upstream's service performs
                    // (BouncingTextService.cs:264-269). A pool with nothing enabled is skipped for
                    // the reason the document's setter gives: an empty list restores the shipped
                    // defaults, which is not what the backup contained.
                    var enabled = pool.Where(entry => entry.Value).Select(entry => entry.Key).ToList();
                    if (enabled.Count == 0)
                    {
                        skipped.Add(name);
                        break;
                    }

                    _bouncingText.Mutate(document => document.Phrases = enabled);
                    applied.Add(new KeyValuePair<string, Dictionary<string, bool>>(name, pool));
                    writes.Add(PersistAsync(_bouncingText));
                    break;

                default:
                    skipped.Add(name);
                    break;
            }
        }

        if (applied.Count == 0)
        {
            // Every pool the file offered turned out to be unusable after all. Nothing was
            // mutated, so this is still a refusal rather than an empty success.
            return new PhraseImport.RefusedFile(PhraseFileRefusal.NoKnownPools);
        }

        var persisted = true;
        foreach (var write in writes)
        {
            persisted &= await write;
        }

        return new PhraseImport.Imported(
            applied.Count, PhraseBackupFile.CountEntries(applied), skipped, persisted);
    }

    private List<KeyValuePair<string, Dictionary<string, bool>>> CurrentPools()
    {
        var bouncing = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var phrase in _bouncingText.Current.Phrases)
        {
            // The document holds only the enabled set, so every entry it has is a true one —
            // written back in upstream's dictionary shape so the WPF product can read the file.
            bouncing[phrase] = true;
        }

        return
        [
            new(PhraseBackupFile.SubliminalPoolName, new Dictionary<string, bool>(_subliminal.Current.Phrases, StringComparer.Ordinal)),
            new(PhraseBackupFile.LockCardPhrasesName, new Dictionary<string, bool>(_lockCard.Current.Phrases, StringComparer.Ordinal)),
            new(PhraseBackupFile.BouncingTextPoolName, bouncing),
        ];
    }

    /// <summary>
    /// Persists one store now. A store that was never started (or has already stopped) has no live
    /// operation generation to write in, so it is reported as not persisted rather than throwing:
    /// the pools are live either way and the teardown flush is the backstop.
    /// </summary>
    private static async Task<bool> PersistAsync<TDocument>(PersistenceStore<TDocument> store)
        where TDocument : class, new()
    {
        if (!store.Running)
        {
            return false;
        }

        return await store.Save() is OperationOutcome.Completed;
    }
}
