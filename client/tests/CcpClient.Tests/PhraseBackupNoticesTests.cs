using System.Text.Json;
using CcpClient.Desktop.Session;
using CcpClient.Desktop.Storage;
using CcpClient.Desktop.Views;
using CcpClient.Desktop.Views.Pages;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// What the Phrase backup module SAYS, per typed outcome
/// (<see cref="PhraseBackupNotices"/>). Upstream reports the same six results through modal styled
/// dialogs (<c>MainWindow/MainWindow.PresetIO.cs:62-134</c>); the port says them through the in-app
/// toast surface, and each sentence is derived from a <see cref="PhraseExport"/> or
/// <see cref="PhraseImport"/> case rather than from a boolean.
///
/// <para>Pure logic — no Avalonia runtime. That the page really routes each outcome here, and that
/// the toast really wears the accent these say it should, is
/// <c>PhraseBackupPageHeadlessTests</c>.</para>
/// </summary>
public class PhraseBackupNoticesTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("repository root not found from the test binary");
    }

    /// <summary>Upstream's own localized strings, read out of the shipping product rather than
    /// restated here — a constant asserted against itself proves nothing.</summary>
    private static string Upstream(string key)
    {
        var path = Path.Combine(
            FindRepoRoot(), "ConditioningControlPanel", "Localization", "Languages", "en.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty(key).GetString()
            ?? throw new InvalidOperationException($"en.json has no string for '{key}'");
    }

    /// <summary>Upstream's captions carry a leading emoji; §9 D8 strips them and keeps the
    /// word.</summary>
    private static string WithoutEmoji(string value) =>
        new string([.. value.Where(c => c < 0x2000)]).Trim();

    /// <summary>Every sentence this module can produce, with the case that produces it. Used by the
    /// mechanical guards below so a new outcome cannot escape them by being added later.</summary>
    private static IEnumerable<(string Case, string Message, ToastKind Kind)> EverySentence()
    {
        yield return Named("Exported(1)", PhraseBackupNotices.Exported(1));
        yield return Named("Exported(97)", PhraseBackupNotices.Exported(97));
        yield return Named("Imported persisted", PhraseBackupNotices.Imported(3, 40, [], true));
        yield return Named("Imported dirty", PhraseBackupNotices.Imported(1, 1, [], false));
        yield return Named("Imported skipping", PhraseBackupNotices.Imported(1, 1, ["MantraPool"], true));
        yield return Named("Faulted", PhraseBackupNotices.Faulted("IOException"));

        foreach (var reason in Enum.GetValues<UserFileRefusal>())
        {
            yield return Named($"ExportRefused({reason})", PhraseBackupNotices.ExportRefused(reason));
            yield return Named($"ImportRefusedPicker({reason})", PhraseBackupNotices.ImportRefusedPicker(reason));
        }

        foreach (var reason in Enum.GetValues<PhraseFileRefusal>())
        {
            yield return Named($"ImportRefusedFile({reason})", PhraseBackupNotices.ImportRefusedFile(reason));
        }
    }

    private static (string Case, string Message, ToastKind Kind) Named(
        string which, (string Message, ToastKind Kind) notice) => (which, notice.Message, notice.Kind);

    // ======================================================================================
    // The module's own words are upstream's.
    // ======================================================================================

    /// <summary>
    /// The four strings on the module come from the SHIPPING PRODUCT'S localization file, read at
    /// test time (<c>en.json:4882-4886</c>). The hint in particular is the sentence that says why
    /// the feature exists — "Back them up before an update or when moving to a new PC" — which is
    /// the whole reason this entry was admitted ahead of the rest of its cluster.
    /// </summary>
    [Fact]
    public void TheModulesWordsAreReadBackOutOfUpstreamsOwnLocalizationFile()
    {
        // The port's constant is the 'expected' argument only because the xunit analyzer requires
        // a constant there; the FILE is the authority, and a drift in either direction reddens.
        Assert.Equal(PhraseBackupNotices.ModuleTitle, Upstream("set2_data_phrase_backup_title"));
        Assert.Equal(PhraseBackupNotices.Blurb, Upstream("set2_data_phrase_backup_hint"));
        Assert.Equal(PhraseBackupNotices.ExportTooltip, Upstream("set2_tooltip_export_phrases"));
        Assert.Equal(PhraseBackupNotices.ImportTooltip, Upstream("set2_tooltip_import_phrases"));

        // The captions ARE upstream's, minus the emoji §9 D8 strips — and the strip really happened,
        // which is what stops this pair passing on a caption that was never ported at all.
        var exportCaption = Upstream("set2_btn_export_phrases");
        var importCaption = Upstream("set2_btn_import_phrases");
        Assert.Equal(PhraseBackupNotices.ExportButton, WithoutEmoji(exportCaption));
        Assert.Equal(PhraseBackupNotices.ImportButton, WithoutEmoji(importCaption));
        Assert.NotEqual(PhraseBackupNotices.ExportButton, exportCaption);
        Assert.NotEqual(PhraseBackupNotices.ImportButton, importCaption);
    }

    // ======================================================================================
    // Export.
    // ======================================================================================

    /// <summary>
    /// A completed export reports upstream's confirmation COUNT
    /// (<c>Services/PhraseBackupService.cs:84</c>) and agrees with itself about singular and
    /// plural.
    /// </summary>
    [Fact]
    public void ACompletedExportReportsTheCountAndIsASuccess()
    {
        Assert.Equal(("Saved 1 phrase.", ToastKind.Success), PhraseBackupNotices.Exported(1));
        Assert.Equal(("Saved 0 phrases.", ToastKind.Success), PhraseBackupNotices.Exported(0));
        Assert.Equal(("Saved 97 phrases.", ToastKind.Success), PhraseBackupNotices.Exported(97));
    }

    // ======================================================================================
    // Import.
    // ======================================================================================

    /// <summary>
    /// <b><see cref="PhraseImport.Imported.Persisted"/> false is not a success.</b> The pools are
    /// live and the store is still dirty, so the sentence says "restored, but not yet saved" and
    /// the toast is a WARNING — a bare success here would tell a user their phrases are safe on
    /// disk when they are not, which is the exact failure this whole module exists to prevent.
    /// </summary>
    [Fact]
    public void ARestoreThatHasNotReachedDiskIsAWarningThatSaysSo()
    {
        var (dirty, dirtyKind) = PhraseBackupNotices.Imported(2, 9, [], persisted: false);
        Assert.Equal(ToastKind.Warning, dirtyKind);
        Assert.Contains("but not yet saved", dirty, StringComparison.Ordinal);
        Assert.Contains("when the app closes", dirty, StringComparison.Ordinal);

        var (saved, savedKind) = PhraseBackupNotices.Imported(2, 9, [], persisted: true);
        Assert.Equal(ToastKind.Success, savedKind);
        Assert.DoesNotContain("not yet saved", saved, StringComparison.Ordinal);

        // Same numbers, two different sentences: the flag is what the user reads, not decoration.
        Assert.NotEqual(saved, dirty);
        Assert.Contains("9 phrases", saved, StringComparison.Ordinal);
        Assert.Contains("9 phrases", dirty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Skipped pools are NAMED. This build has three of upstream's seventeen
    /// (<see cref="PhraseBackupFile"/>), so an import that reported a bare success would be hiding
    /// a partial restore — which is the silent partial write
    /// <see cref="PhraseImport.Imported.PoolsSkipped"/> exists to make impossible.
    /// </summary>
    [Fact]
    public void EverySkippedListIsNamedInTheSentenceAndSilenceMeansNoneWereSkipped()
    {
        var (one, _) = PhraseBackupNotices.Imported(1, 4, ["MantraPool"], persisted: true);
        Assert.Contains("1 phrase list in the file was skipped", one, StringComparison.Ordinal);
        Assert.Contains("MantraPool", one, StringComparison.Ordinal);

        var (two, _) = PhraseBackupNotices.Imported(1, 4, ["MantraPool", "AffirmationPool"], persisted: true);
        Assert.Contains("2 phrase lists in the file were skipped", two, StringComparison.Ordinal);
        Assert.Contains("MantraPool, AffirmationPool", two, StringComparison.Ordinal);

        var (none, _) = PhraseBackupNotices.Imported(1, 4, [], persisted: true);
        Assert.DoesNotContain("skipped", none, StringComparison.Ordinal);

        // Upstream's closing advice survives on every one of them (PresetIO.cs:126).
        foreach (var sentence in new[] { one, two, none })
        {
            Assert.EndsWith("You may need to reopen any open phrase editors to see them.", sentence, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Every typed refusal has its OWN sentence. A shared "something went wrong" would collapse a
    /// desktop with no picker at all, a file too large to hold, and a file that is not a backup
    /// into one message the user cannot act on.
    /// </summary>
    [Fact]
    public void EveryRefusalCodeProducesADistinctSentenceAndAnErrorToast()
    {
        var pickerReasons = Enum.GetValues<UserFileRefusal>();
        var fileReasons = Enum.GetValues<PhraseFileRefusal>();
        Assert.NotEmpty(pickerReasons);
        Assert.NotEmpty(fileReasons);

        var exports = pickerReasons.Select(PhraseBackupNotices.ExportRefused).ToArray();
        var opens = pickerReasons.Select(PhraseBackupNotices.ImportRefusedPicker).ToArray();
        var files = fileReasons.Select(PhraseBackupNotices.ImportRefusedFile).ToArray();

        Assert.Equal(pickerReasons.Length, exports.Select(e => e.Message).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(pickerReasons.Length, opens.Select(e => e.Message).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(fileReasons.Length, files.Select(e => e.Message).Distinct(StringComparer.Ordinal).Count());

        foreach (var (message, kind) in exports.Concat(opens).Concat(files))
        {
            Assert.Equal(ToastKind.Error, kind);
            Assert.EndsWith(".", message, StringComparison.Ordinal);
        }

        // The two directions do not share a sentence either: "could not be written" and "could not
        // be read" are different things to tell someone.
        Assert.Empty(exports.Select(e => e.Message).Intersect(opens.Select(o => o.Message), StringComparer.Ordinal));
    }

    // ======================================================================================
    // The seam's rule, over everything this module can say.
    // ======================================================================================

    /// <summary>
    /// <b>No sentence this module can produce carries a path, a file name, an extension or an
    /// exception message.</b> It is the deliberate divergence: upstream prints the full path in
    /// both confirmations (<c>MainWindow/MainWindow.PresetIO.cs:82</c>, <c>:127</c>) and
    /// <c>ex.Message</c> in both failures (<c>:87</c>, <c>:132</c>), and an
    /// <see cref="IOException"/>'s text carries the path of the file that failed — the shortest
    /// possible route to defeating the seam (<see cref="UserFileRefusal"/>).
    ///
    /// <para>Asserted over EVERY case rather than over the two that were thought of, so a later
    /// outcome cannot slip past by being added after this fact.</para>
    /// </summary>
    [Fact]
    public void NoSentenceThisModuleCanProduceCarriesAPathAFileNameOrAnExceptionMessage()
    {
        var sentences = 0;
        foreach (var (which, message, _) in EverySentence())
        {
            sentences++;
            Assert.False(string.IsNullOrWhiteSpace(message), $"{which} says nothing at all");
            Assert.DoesNotContain('\\', message);          // a Windows separator
            Assert.DoesNotContain('/', message);           // and a POSIX one
            Assert.DoesNotContain(":\\", message, StringComparison.Ordinal);   // a drive-rooted path
            Assert.DoesNotContain(
                PhraseBackup.FileKind.DefaultExtension, message, StringComparison.OrdinalIgnoreCase);
        }

        // Every enum arm of both refusal enums, plus the six fixed cases above them. If a case is
        // ever added without a sentence, this count moves and the guard is re-read rather than
        // silently covering less.
        Assert.Equal(
            6 + (2 * Enum.GetValues<UserFileRefusal>().Length) + Enum.GetValues<PhraseFileRefusal>().Length,
            sentences);
    }

    /// <summary>
    /// The fault sentence carries the exception's TYPE and nothing else. Upstream shows
    /// <c>ex.Message</c> (<c>PresetIO.cs:87</c>, <c>:132</c>), which is precisely the string that
    /// would carry the path.
    /// </summary>
    [Fact]
    public void AFaultIsReportedByTypeNameAndNeverByItsMessage()
    {
        var thrown = new IOException(@"Could not find file 'C:\Users\someone\Documents\mine.ccpphrases.json'.");
        var (message, kind) = PhraseBackupNotices.Faulted(thrown.GetType().Name);

        Assert.Equal(ToastKind.Error, kind);
        Assert.Contains("IOException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", message, StringComparison.Ordinal);
        Assert.DoesNotContain(thrown.Message, message, StringComparison.Ordinal);
        Assert.Contains("Nothing further was written", message, StringComparison.Ordinal);
    }
}
