using System.Reflection;
using System.Text.Json;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Storage;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Phrase backup export and import (census #9) — the first consumer of the open-or-save seam, and
/// the only entry in its cluster whose absence is a DATA-LOSS risk rather than a missing feature.
///
/// <para>The picker is faked; everything else is real, including three live
/// <see cref="PersistenceStore{TModel}"/> instances over temp files, so an import is proved against
/// the documents the running modules actually read.</para>
///
/// <para><b>What these facts do NOT prove.</b> No dialog is opened and no button exists yet: this
/// slice ships the seam and the consumer, not a user-reachable control. Whether a real save dialog
/// appears, is placed, and returns a file is a headed gate on both platforms.</para>
/// </summary>
public class PhraseBackupTests
{
    /// <summary>Half past one in the morning, two hours east of UTC — so the user's calendar day
    /// is the 24th while the same instant is still the 23rd in UTC. One instant, two days: the
    /// file name and the envelope stamp cannot both be right by accident.</summary>
    private static readonly DateTimeOffset EarlyHours =
        new(2026, 8, 24, 1, 30, 0, TimeSpan.FromHours(2));

    // ======================================================================================
    // Export.
    // ======================================================================================

    [Fact]
    public async Task ExportWritesUpstreamsEnvelopeAndUpstreamsPoolNames()
    {
        using var lab = new Lab();
        var outcome = await lab.Backup.ExportAsync();

        var document = JsonDocument.Parse(lab.Picker.Saved!).RootElement;
        Assert.IsType<PhraseExport.Exported>(outcome);
        // Upstream's four members and no others (Services/PhraseBackupService.cs:72-78): nothing
        // about the machine, the folder, or the user rides along with the phrases.
        Assert.Equal(
            new[] { "schema", "exported_at", "app_version", "phrases" },
            document.EnumerateObject().Select(member => member.Name).ToArray());
        Assert.Equal("ccp-phrases/v1", document.GetProperty("schema").GetString());
        Assert.Equal(
            new[] { "SubliminalPool", "LockCardPhrases", "BouncingTextPool" },
            document.GetProperty("phrases").EnumerateObject().Select(member => member.Name).ToArray());
    }

    [Fact]
    public async Task ExportStampsTheInstantAsUtcAndNamesTheFileOnTheUsersOwnDay()
    {
        using var lab = new Lab();
        await lab.Backup.ExportAsync();

        var document = JsonDocument.Parse(lab.Picker.Saved!).RootElement;
        // One instant read two ways, exactly as upstream does: its UTC clock for the envelope
        // (PhraseBackupService.cs:75) and its LOCAL clock for the file name (:53). At 01:30+02:00
        // the stamp is the PREVIOUS day in UTC while the name is the day the user thinks it is.
        Assert.StartsWith("2026-08-23T23:30:00", document.GetProperty("exported_at").GetString());
        Assert.Equal("ccp-phrases-20260824.ccpphrases.json", lab.Picker.SuggestedFileName);
        Assert.Equal("Export Phrases", lab.Picker.SaveTitle);
    }

    [Fact]
    public async Task ExportCarriesTheRunningVersionRatherThanALiteral()
    {
        using var lab = new Lab();
        await lab.Backup.ExportAsync();

        var expected = typeof(PhraseBackup).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var document = JsonDocument.Parse(lab.Picker.Saved!).RootElement;
        Assert.False(string.IsNullOrWhiteSpace(expected));
        Assert.Equal(expected, document.GetProperty("app_version").GetString());
    }

    [Fact]
    public async Task ExportCountsEveryPhraseTheUserHasEnabledOrNot()
    {
        using var lab = new Lab();
        lab.Subliminal.Mutate(document => document.Phrases = new Dictionary<string, bool>
        {
            ["KEEP"] = true,
            ["OFF FOR NOW"] = false,
        });
        lab.LockCard.Mutate(document => document.Phrases = new Dictionary<string, bool> { ["TYPE ME"] = true });
        lab.BouncingText.Mutate(document => document.Phrases = ["ONE", "TWO"]);

        var outcome = await lab.Backup.ExportAsync();

        // Upstream counts the KEYS of a flat pool, disabled ones included
        // (PhraseBackupService.cs:170-173): 2 + 1 + 2.
        Assert.Equal(5, Assert.IsType<PhraseExport.Exported>(outcome).PhraseCount);
    }

    [Fact]
    public async Task ExportToAClosedPickerIsCancelledAndNotAFailure()
    {
        using var lab = new Lab();
        lab.Picker.SaveOutcome = UserFileSave.Cancelled.Instance;

        var outcome = await lab.Backup.ExportAsync();

        Assert.IsType<PhraseExport.Cancelled>(outcome);
    }

    [Fact]
    public async Task ExportPassesThePickersRefusalThroughRatherThanClaimingSuccess()
    {
        using var lab = new Lab();
        lab.Picker.SaveOutcome = new UserFileSave.Refused(UserFileRefusal.NoPicker);

        var outcome = await lab.Backup.ExportAsync();

        Assert.Equal(UserFileRefusal.NoPicker, Assert.IsType<PhraseExport.Refused>(outcome).Reason);
    }

    // ======================================================================================
    // Import: the order upstream uses, and what a bad file does.
    // ======================================================================================

    [Fact]
    public async Task ImportReplacesEachPoolItDoesNotMergeThem()
    {
        using var lab = new Lab();
        lab.Subliminal.Mutate(document => document.Phrases = new Dictionary<string, bool> { ["OLD"] = true });
        lab.LockCard.Mutate(document => document.Phrases = new Dictionary<string, bool> { ["OLD CARD"] = true });
        lab.BouncingText.Mutate(document => document.Phrases = ["OLD WORD"]);
        lab.Picker.Opened = Backup(("SubliminalPool", "{\"NEW\":true}"),
            ("LockCardPhrases", "{\"NEW CARD\":true}"),
            ("BouncingTextPool", "{\"NEW WORD\":true}"));

        var outcome = await lab.Backup.ImportAsync(Confirm);

        // "an import is a restore, not a merge" (Services/PhraseBackupService.cs:110-112).
        Assert.Equal(new[] { "NEW" }, lab.Subliminal.Current.Phrases.Keys.ToArray());
        Assert.Equal(new[] { "NEW CARD" }, lab.LockCard.Current.Phrases.Keys.ToArray());
        Assert.Equal(new[] { "NEW WORD" }, lab.BouncingText.Current.Phrases.ToArray());
        Assert.Equal(3, Assert.IsType<PhraseImport.Imported>(outcome).PoolsApplied);
    }

    [Fact]
    public async Task ImportAsksBeforeItTouchesAnythingAndDecliningLeavesEveryPoolAlone()
    {
        using var lab = new Lab();
        lab.Subliminal.Mutate(document => document.Phrases = new Dictionary<string, bool> { ["MINE"] = true });
        lab.Picker.Opened = Backup(("SubliminalPool", "{\"THEIRS\":true}"));
        var asked = 0;

        var outcome = await lab.Backup.ImportAsync(() =>
        {
            asked++;
            // The question is asked while the user's own pool is still intact — upstream's order
            // is Validate, confirm, THEN import (MainWindow/MainWindow.PresetIO.cs:107-122).
            Assert.Equal(new[] { "MINE" }, lab.Subliminal.Current.Phrases.Keys.ToArray());
            return Task.FromResult(false);
        });

        Assert.IsType<PhraseImport.Declined>(outcome);
        Assert.Equal(1, asked);
        Assert.Equal(new[] { "MINE" }, lab.Subliminal.Current.Phrases.Keys.ToArray());
    }

    [Fact]
    public async Task AMalformedFileIsATypedRefusalNeverAThrowAndTheUserIsNeverAsked()
    {
        var cases = new (string Text, PhraseFileRefusal Expected)[]
        {
            ("not json at all {{{", PhraseFileRefusal.NotJson),
            ("", PhraseFileRefusal.NotJson),
            ("[1,2,3]", PhraseFileRefusal.NotAnObject),
            ("null", PhraseFileRefusal.NotAnObject),
            ("{\"schema\":\"ccp-preset/v1\",\"phrases\":{\"SubliminalPool\":{\"A\":true}}}", PhraseFileRefusal.WrongSchema),
            ("{\"schema\":42,\"phrases\":{\"SubliminalPool\":{\"A\":true}}}", PhraseFileRefusal.WrongSchema),
            ("{\"schema\":\"ccp-phrases/v1\"}", PhraseFileRefusal.NoPhrases),
            ("{\"schema\":\"ccp-phrases/v1\",\"phrases\":{}}", PhraseFileRefusal.NoPhrases),
            ("{\"schema\":\"ccp-phrases/v1\",\"phrases\":[]}", PhraseFileRefusal.NoPhrases),
            ("{\"schema\":\"ccp-phrases/v1\",\"phrases\":{\"MantraPool\":{\"A\":true}}}", PhraseFileRefusal.NoKnownPools),
        };

        var wrong = new List<string>();
        var asked = 0;
        foreach (var (text, expected) in cases)
        {
            using var lab = new Lab();
            lab.Picker.Opened = text;
            var outcome = await lab.Backup.ImportAsync(() =>
            {
                asked++;
                return Task.FromResult(true);
            });

            if (outcome is not PhraseImport.RefusedFile refused || refused.Reason != expected)
            {
                wrong.Add($"{text} -> {outcome} (wanted {expected})");
            }

            if (lab.Subliminal.IsDirty || lab.LockCard.IsDirty || lab.BouncingText.IsDirty)
            {
                wrong.Add($"{text} mutated a store");
            }
        }

        Assert.Empty(wrong);
        Assert.Equal(0, asked); // a file that is not a backup never puts a scary question up
    }

    [Fact]
    public async Task ImportKeepsTheGoodPoolsWhenOneIsShapedWrongAndNamesTheOneItSkipped()
    {
        using var lab = new Lab();
        lab.Picker.Opened = Backup(
            ("SubliminalPool", "{\"GOOD\":true}"),
            ("LockCardPhrases", "[\"an array, not a pool\"]"),
            ("BouncingTextPool", "{\"ALSO GOOD\":true}"));

        var outcome = await lab.Backup.ImportAsync(Confirm);

        // Upstream tolerates one unimportable member rather than failing the whole import
        // (PhraseBackupService.cs:136-147) — but it writes the skip to a log the user never reads,
        // and this names it in the outcome instead.
        var imported = Assert.IsType<PhraseImport.Imported>(outcome);
        Assert.Equal(2, imported.PoolsApplied);
        Assert.Equal(new[] { "LockCardPhrases" }, imported.PoolsSkipped.ToArray());
        Assert.Equal(new[] { "GOOD" }, lab.Subliminal.Current.Phrases.Keys.ToArray());
    }

    [Fact]
    public async Task ImportOfAWpfBackupRestoresWhatThisBuildHasAndNamesWhatItCannot()
    {
        using var lab = new Lab();
        // A file the shipping WPF product would write: its own seventeen whitelisted pools
        // (PhraseBackupService.cs:32-49), of which this client has three modules.
        lab.Picker.Opened = Backup(
            ("SubliminalPool", "{\"GOOD GIRL\":true,\"OBEY\":false}"),
            ("SubliminalPoolByMod", "{\"bambi\":{\"DROP\":true}}"),
            ("UserAddedSubliminals", "[\"DROP\"]"),
            ("LockCardPhrases", "{\"I LOVE BEING PROGRAMMED\":true}"),
            ("BouncingTextPool", "{\"PINK\":true}"),
            ("MantraPool", "{\"I OBEY\":true}"),
            ("CustomCompanionPhrases", "{\"hello\":true}"));

        var outcome = await lab.Backup.ImportAsync(Confirm);

        var imported = Assert.IsType<PhraseImport.Imported>(outcome);
        Assert.Equal(3, imported.PoolsApplied);
        Assert.Equal(4, imported.PhraseCount); // 2 + 1 + 1, counted as upstream counts
        Assert.Equal(
            new[] { "SubliminalPoolByMod", "UserAddedSubliminals", "MantraPool", "CustomCompanionPhrases" },
            imported.PoolsSkipped.ToArray());
    }

    [Fact]
    public async Task ThePortsOwnExportImportsBackIntoTheSamePools()
    {
        using var source = new Lab();
        source.Subliminal.Mutate(document => document.Phrases = new Dictionary<string, bool>
        {
            ["WROTE THIS MYSELF"] = true,
            ["AND THIS"] = false,
        });
        source.BouncingText.Mutate(document => document.Phrases = ["ZED", "ALPHA"]);
        await source.Backup.ExportAsync();

        using var destination = new Lab();
        destination.Picker.Opened = source.Picker.Saved;
        var outcome = await destination.Backup.ImportAsync(Confirm);

        Assert.IsType<PhraseImport.Imported>(outcome);
        Assert.Equal(
            new[] { "WROTE THIS MYSELF", "AND THIS" },
            destination.Subliminal.Current.Phrases.Keys.ToArray());
        Assert.False(destination.Subliminal.Current.Phrases["AND THIS"]); // a disabled phrase is still the user's
        // Order survives, which the bouncing-text document needs: a seeded run has to deal the
        // same word (Session/BouncingTextPresetDocument.cs Phrases).
        Assert.Equal(new[] { "ZED", "ALPHA" }, destination.BouncingText.Current.Phrases.ToArray());
    }

    [Fact]
    public async Task BouncingTextRoundTripsThroughUpstreamsDictionaryDroppingWhatIsTurnedOff()
    {
        using var lab = new Lab();
        lab.Picker.Opened = Backup(("BouncingTextPool", "{\"ON\":true,\"OFF\":false,\"ALSO ON\":true}"));

        var outcome = await lab.Backup.ImportAsync(Confirm);

        // The document stores the ENABLED set as an ordered list; upstream's service reads exactly
        // the true entries (BouncingTextService.cs:264-269), so the false one is not a phrase the
        // module would ever show.
        Assert.IsType<PhraseImport.Imported>(outcome);
        Assert.Equal(new[] { "ON", "ALSO ON" }, lab.BouncingText.Current.Phrases.ToArray());
    }

    [Fact]
    public async Task AnEmptyPoolIsNotAppliedBecauseTheDocumentWouldRestoreItsShippedDefaults()
    {
        using var lab = new Lab();
        lab.Subliminal.Mutate(document => document.Phrases = new Dictionary<string, bool> { ["MINE"] = true });
        lab.Picker.Opened = Backup(
            ("SubliminalPool", "{}"),
            ("BouncingTextPool", "{\"ALL OFF\":false}"),
            ("LockCardPhrases", "{\"REAL\":true}"));

        var outcome = await lab.Backup.ImportAsync(Confirm);

        // Applying either would hand the user the twenty-one shipped subliminals and the ten
        // shipped words their backup does not contain, and call that a restore.
        var imported = Assert.IsType<PhraseImport.Imported>(outcome);
        Assert.Equal(new[] { "SubliminalPool", "BouncingTextPool" }, imported.PoolsSkipped.ToArray());
        Assert.Equal(new[] { "MINE" }, lab.Subliminal.Current.Phrases.Keys.ToArray());
    }

    [Fact]
    public async Task ImportPersistsImmediatelyAndSaysSoWhenTheWriteFails()
    {
        using var writable = new Lab();
        writable.Picker.Opened = Backup(("SubliminalPool", "{\"SAVED\":true}"));
        var good = await writable.Backup.ImportAsync(Confirm);

        using var readOnly = new Lab(breakWrites: true);
        readOnly.Picker.Opened = Backup(("SubliminalPool", "{\"SAVED\":true}"));
        var bad = await readOnly.Backup.ImportAsync(Confirm);

        // Upstream persists straight after the import (MainWindow/MainWindow.PresetIO.cs:123).
        Assert.True(Assert.IsType<PhraseImport.Imported>(good).Persisted);
        Assert.Contains("SAVED", File.ReadAllText(writable.SubliminalPath));
        // A failed write is not a failed import: the pools ARE live and the store is still dirty.
        Assert.False(Assert.IsType<PhraseImport.Imported>(bad).Persisted);
        Assert.Equal(new[] { "SAVED" }, readOnly.Subliminal.Current.Phrases.Keys.ToArray());
    }

    [Fact]
    public async Task ImportIntoAStoreThatIsNotRunningAppliesWithoutThrowingAndReportsItUnsaved()
    {
        using var lab = new Lab(start: false);
        lab.Picker.Opened = Backup(("SubliminalPool", "{\"LIVE\":true}"));

        var outcome = await lab.Backup.ImportAsync(Confirm);

        // A store with no live operation generation cannot enqueue a write; asking it to would
        // throw out of an import that had already mutated the pools.
        Assert.False(Assert.IsType<PhraseImport.Imported>(outcome).Persisted);
        Assert.Equal(new[] { "LIVE" }, lab.Subliminal.Current.Phrases.Keys.ToArray());
    }

    [Fact]
    public async Task AClosedOrAbsentPickerChangesNothing()
    {
        using var cancelled = new Lab();
        cancelled.Picker.OpenOutcome = UserFileOpen.Cancelled.Instance;
        using var missing = new Lab();
        missing.Picker.OpenOutcome = new UserFileOpen.Refused(UserFileRefusal.NoPicker);

        var closed = await cancelled.Backup.ImportAsync(Confirm);
        var absent = await missing.Backup.ImportAsync(Confirm);

        Assert.IsType<PhraseImport.Cancelled>(closed);
        Assert.Equal(UserFileRefusal.NoPicker, Assert.IsType<PhraseImport.RefusedPicker>(absent).Reason);
        Assert.False(cancelled.Subliminal.IsDirty || missing.Subliminal.IsDirty);
    }

    [Fact]
    public void AnAbsurdMemberNameIsBoundedBeforeItIsReportedBack()
    {
        var name = new string('x', 500);
        var text = Backup((name, "{\"A\":true}"), ("SubliminalPool", "{\"A\":true}"));

        var parsed = Assert.IsType<PhraseFileRead.Parsed>(PhraseBackupFile.Read(text));

        // The names come out of a file the user chose, not out of this program: a 10 MB JSON key
        // pasted into a message box is a UI bomb rather than information.
        Assert.Equal(PhraseBackupFile.MaxSkipNameLength, Assert.Single(parsed.PoolsSkipped).Length);
    }

    // ======================================================================================
    // Helpers.
    // ======================================================================================

    private static Task<bool> Confirm() => Task.FromResult(true);

    /// <summary>A phrase backup file with the given pool members, in the given order.</summary>
    private static string Backup(params (string Name, string Json)[] pools)
    {
        var members = string.Join(",", pools.Select(pool => $"\"{pool.Name}\":{pool.Json}"));
        return $"{{\"schema\":\"ccp-phrases/v1\",\"exported_at\":\"2026-08-23T23:30:00.0000000Z\","
            + $"\"app_version\":\"0.1.0\",\"phrases\":{{{members}}}}}";
    }

    private sealed class Lab : IDisposable
    {
        private readonly string _root;

        public Lab(bool start = true, bool breakWrites = false)
        {
            _root = Path.Combine(Path.GetTempPath(), "ccp-phrase-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            var registry = new OperationRegistry();
            var hooks = breakWrites
                ? new AtomicWriteHooks
                {
                    WriteTempFile = static (_, _) => throw new UnauthorizedAccessException("read-only volume"),
                }
                : new AtomicWriteHooks();

            SubliminalPath = Path.Combine(_root, SubliminalPresetDocument.FileName);
            Subliminal = new PersistenceStore<SubliminalPresetDocument>(
                registry.OwnerFor("LabSubliminal"), new NullSink(), SubliminalPath,
                SubliminalPresetDocument.CurrentSchemaVersion, migrations: null, hooks: hooks);
            LockCard = new PersistenceStore<LockCardPresetDocument>(
                registry.OwnerFor("LabLockCard"), new NullSink(),
                Path.Combine(_root, LockCardPresetDocument.FileName),
                LockCardPresetDocument.CurrentSchemaVersion, migrations: null, hooks: hooks);
            BouncingText = new PersistenceStore<BouncingTextPresetDocument>(
                registry.OwnerFor("LabBouncingText"), new NullSink(),
                Path.Combine(_root, BouncingTextPresetDocument.FileName),
                BouncingTextPresetDocument.CurrentSchemaVersion, migrations: null, hooks: hooks);

            if (start)
            {
                // Not awaited and nothing is waited on: PersistenceStore.StartAsync loads INLINE
                // on the calling thread and hands back an already-complete task (its own remarks,
                // pinned by PersistenceStoreTests). This is the phase-3 start, not a background one.
                _ = Subliminal.StartAsync(CancellationToken.None);
                _ = LockCard.StartAsync(CancellationToken.None);
                _ = BouncingText.StartAsync(CancellationToken.None);
            }

            Backup = new PhraseBackup(Picker, Subliminal, LockCard, BouncingText, () => EarlyHours);
        }

        public FakePicker Picker { get; } = new();

        public PersistenceStore<SubliminalPresetDocument> Subliminal { get; }

        public PersistenceStore<LockCardPresetDocument> LockCard { get; }

        public PersistenceStore<BouncingTextPresetDocument> BouncingText { get; }

        public PhraseBackup Backup { get; }

        public string SubliminalPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The seam, scripted. Records what the consumer asked the dialog for.</summary>
    private sealed class FakePicker : IUserFilePicker
    {
        public string? Saved { get; private set; }

        public string? SaveTitle { get; private set; }

        public string? SuggestedFileName { get; private set; }

        public UserFileSave SaveOutcome { get; set; } = UserFileSave.Saved.Instance;

        public string? Opened { get; set; }

        public UserFileOpen? OpenOutcome { get; set; }

        public Task<UserFileSave> SaveTextAsync(
            string title, UserFileKind kind, string suggestedFileName, string contents)
        {
            SaveTitle = title;
            SuggestedFileName = suggestedFileName;
            if (SaveOutcome is UserFileSave.Saved)
            {
                Saved = contents;
            }

            return Task.FromResult(SaveOutcome);
        }

        public Task<UserFileOpen> OpenTextAsync(string title, UserFileKind kind) =>
            Task.FromResult(OpenOutcome ?? new UserFileOpen.Opened(Opened ?? string.Empty));
    }

    private sealed class NullSink : ILogSink
    {
        public void Log(string message)
        {
        }
    }
}
