using System;
using System.IO;
using System.Linq;
using System.Text;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Language.Tests;

/// <summary>
/// [save-access-probe] TEST-ONLY observation. Diagnostic, not a fix, and it gates nothing.
///
/// <para>WHY THIS EXISTS. The reader-sharing experiment (run 35532748991) contradicted its own
/// hypothesis: BOTH readers — the one withholding <c>FileShare.Delete</c> and the one granting it
/// — seeded <c>ja</c>, attempted <c>fr</c>, persisted <c>ja</c>, and BOTH failed with
/// <c>UnauthorizedAccessException (HResult=0x80070005)</c>, not the predicted sharing-violation
/// <c>IOException (0x80070020)</c>. That earlier test is preserved unchanged and still fails; its
/// prediction was wrong and nothing here rewrites it into a pass.</para>
///
/// <para>WHAT THIS ASKS. Only: what is the EXACT production exception, from WHICH call inside
/// <see cref="SettingsService.SaveImmediate"/>, and does a held reader matter at all? So it adds a
/// no-held-reader CONTROL the earlier experiment never had. Access-denied with no reader held would
/// show only that a held reader is NOT NECESSARY for that observed failure — an observation, not a
/// cause. Independent faults can coexist, and equal exception types or HResults do not establish
/// equal causes.</para>
///
/// <para>WHAT IT DELIBERATELY DOES NOT DO. It asserts no cause for the held-reader observations.
/// The stack is captured and uploaded; ARTIFACT INSPECTION is the gate, not an assertion encoding
/// a second guess. A green run here proves only that the three observations were captured — it
/// establishes neither the mechanism nor the cause of the original CI language failure.</para>
///
/// <para>Runs on the module-initialized owned profile from <c>TestProfile</c>, through the real
/// public service surface and the Serilog sink the product already writes to. No production or
/// Core hook is added.</para>
/// </summary>
public sealed class SaveAccessProbeTests
{
    private sealed record Observation(
        string Control,
        string Held,
        string SeededLanguage,
        string AttemptedLanguage,
        string PersistedLanguage,
        bool PersistedMatchesAttempt,
        string[] SaveLogLines,
        string[] ExceptionDetails,
        string DiskFacts);

    private static bool IsSuccessfulSave(string line) =>
        line.Contains("Settings saved to", StringComparison.Ordinal);

    private static bool IsAnySaveFailure(string line) =>
        line.Contains("Could not save settings", StringComparison.Ordinal);

    /// <summary>
    /// [save-access-probe] Negative control for the CAPTURE ITSELF, and the only part of this file
    /// that can run off Windows. If the sink ever stopped recording stacks, the Windows run would
    /// report "no exception detail" and look like a clean save; this fails instead. Verified to
    /// fail by removing the stack write from <c>SaveProbeSink.Emit</c>.
    /// </summary>
    [Fact]
    public void SinkCapturesTheExactExceptionStackNotJustItsMessage()
    {
        SaveProbeSink.Instance.Mark("save-access-probe capture self-check");
        try { ThrowFromAKnownFrame(); }
        catch (Exception ex) { Serilog.Log.Error(ex, "Could not save settings"); }

        var detail = Assert.Single(SaveProbeSink.Instance.ExceptionDetailsSinceLastMark());
        Assert.Contains("Could not save settings", detail, StringComparison.Ordinal);
        Assert.Contains(nameof(UnauthorizedAccessException), detail, StringComparison.Ordinal);
        // The point of the addition: a frame, not merely the message.
        Assert.Contains(nameof(ThrowFromAKnownFrame), detail, StringComparison.Ordinal);

        // And the one-line buffer the earlier probe asserts on keeps its old shape.
        var line = Assert.Single(SaveProbeSink.Instance.SinceLastMark());
        Assert.Contains("HResult=0x", line, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ThrowFromAKnownFrame), line, StringComparison.Ordinal);
    }

    private static void ThrowFromAKnownFrame() => throw new UnauthorizedAccessException("probe self-check");

    /// <summary>
    /// [save-access-probe] Three real saves through the production service: one with nothing held,
    /// then the two held-reader shapes from the contradicted experiment. Every observation is
    /// written out BEFORE any assertion runs.
    /// </summary>
    [Fact]
    public void RecordsTheExactWindowsSaveOutcomeWithAndWithoutAHeldReader()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "Windows file-access semantics under observation; POSIX rename ignores open handles.");

        // Order matters for honesty, not for the result: the control runs FIRST, so it cannot be
        // explained away by state a held-reader run left behind.
        // Each observation is persisted the moment it completes, so a later sequence that cannot
        // even start (absent file, failed open) can never erase the evidence already gathered.
        var control = RunAndRecord("no-reader-control", null);
        var heldRead = RunAndRecord("held-reader-read", FileShare.Read);
        var heldReadDelete = RunAndRecord("held-reader-read-delete", FileShare.Read | FileShare.Delete);

        // --- Control only. A genuine production SaveImmediate with nothing held: if this cannot
        // persist either, that records only that a held reader is not necessary for the failure —
        // never that the reader was never a factor. Asserted because it is the baseline the whole
        // probe is calibrated against.
        Assert.Equal("ja", control.SeededLanguage);
        Assert.Contains(control.SaveLogLines, IsSuccessfulSave);
        Assert.DoesNotContain(control.SaveLogLines, IsAnySaveFailure);
        Assert.Equal("fr", control.PersistedLanguage);

        // --- Held-reader observations. NO cause is asserted: whichever way they land is data.
        // The only requirement is that each one is CONCLUSIVE — the seed really happened, and the
        // run produced either an observed success or a failure carrying a usable stack. Anything
        // else would be an empty artifact masquerading as a result.
        foreach (var held in new[] { heldRead, heldReadDelete })
        {
            Assert.Equal("ja", held.SeededLanguage);
            var succeeded = held.SaveLogLines.Any(IsSuccessfulSave);
            var failedWithStack = held.SaveLogLines.Any(IsAnySaveFailure)
                && held.ExceptionDetails.Any(d => d.Contains("   at ", StringComparison.Ordinal));
            Assert.True(succeeded || failedWithStack,
                $"{held.Control}: neither an observed save success nor a failure with a stack was captured — "
                + "the artifact cannot answer anything. See save-access-probe.log.");
        }
    }

    /// <summary>
    /// Runs one sequence and PERSISTS its result before returning, so the next sequence cannot lose
    /// it. A setup failure (missing settings.json, an open that throws) is recorded explicitly and
    /// then rethrown: an unexpected setup failure must never pass silently.
    /// </summary>
    private static Observation RunAndRecord(string control, FileShare? share)
    {
        Observation observation;
        try
        {
            observation = RunControl(control, share);
        }
        catch (Exception ex)
        {
            WriteReport($"[save-access-probe] control={control} held={Describe(share)}"
                + " SETUP-FAILED before an observation could be formed" + Environment.NewLine
                + $"[save-access-probe]   setup-exception: {ex}");
            throw;
        }

        WriteEvidence(observation);
        return observation;
    }

    /// <summary>
    /// Seeds Language=ja through the real service, optionally holds settings.json open with
    /// <paramref name="share"/>, attempts Language=fr through the same real service, and reports
    /// what actually reached disk. Any reader handle is closed before the method returns.
    /// </summary>
    private static Observation RunControl(string control, FileShare? share)
    {
        var settingsPath = Path.Combine(TestProfile.DirectoryPath, "settings.json");

        var service = new SettingsService();
        service.Current.Language = "ja";
        service.SaveImmediate();
        var seeded = ReadPersistedLanguage(settingsPath);

        FileStream? reader = null;
        try
        {
            if (share is { } s)
            {
                reader = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, s);
                reader.ReadByte(); // a handle that never reads is easy to dismiss as inert
            }

            SaveProbeSink.Instance.Mark($"save-access-probe {control} held={Describe(share)}");
            service.Current.Language = "fr";
            service.SaveImmediate();

            var persisted = ReadPersistedLanguage(settingsPath);
            return new Observation(control, Describe(share), seeded, "fr",
                persisted,
                PersistedMatchesAttempt: persisted == "fr",
                SaveProbeSink.Instance.SinceLastMark(),
                SaveProbeSink.Instance.ExceptionDetailsSinceLastMark(),
                DiskFacts(settingsPath));
        }
        finally
        {
            reader?.Dispose();
        }
    }

    private static string Describe(FileShare? share) => share is { } s ? s.ToString() : "nothing";

    /// <summary>
    /// Shape of the directory after the attempt — never its contents. The leftover <c>*.tmp</c>
    /// count is recorded as a plain observation only: production deletes the temporary file after
    /// EITHER a write failure or a publication failure, so the count cannot discriminate between
    /// them.
    /// </summary>
    private static string DiskFacts(string settingsPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(settingsPath)!;
            var temps = Directory.GetFiles(dir, Path.GetFileName(settingsPath) + "*.tmp").Length;
            var info = new FileInfo(settingsPath);
            return $"settings-exists={info.Exists} attributes={(info.Exists ? info.Attributes.ToString() : "n/a")}"
                + $" bytes={(info.Exists ? info.Length : -1)} leftover-temp-files={temps}";
        }
        catch (Exception ex)
        {
            return $"<disk facts unavailable: {ex.GetType().Name}: {ex.Message}>";
        }
    }

    /// <summary>Reads the Language value straight off disk — never from the in-memory model.</summary>
    private static string ReadPersistedLanguage(string settingsPath)
    {
        try
        {
            // ReadWrite|Delete so this inspection can never itself block what it is observing.
            using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var text = new StreamReader(stream);
            var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(text.ReadToEnd());
            return settings?["Language"]?.ToString() ?? "<absent>";
        }
        catch (Exception ex)
        {
            return $"<unreadable: {ex.GetType().Name}>";
        }
    }

    /// <summary>
    /// Appends one completed observation to the probe artifact directory: language codes, the held share
    /// mode, directory shape, and the product's own save log lines and exception stacks. Never the
    /// settings file contents.
    /// </summary>
    private static void WriteEvidence(params Observation[] observations)
    {
        var report = new StringBuilder();

        foreach (var o in observations)
        {
            report.AppendLine($"[save-access-probe] control={o.Control} held={o.Held}"
                + $" seeded={o.SeededLanguage} attempted={o.AttemptedLanguage}"
                + $" persisted={o.PersistedLanguage} persisted-matches-attempt={o.PersistedMatchesAttempt}");
            report.AppendLine($"[save-access-probe]   disk: {o.DiskFacts}");
            if (o.SaveLogLines.Length == 0)
                report.AppendLine("[save-access-probe]   (no settings log lines in this window)");
            foreach (var line in o.SaveLogLines)
                report.AppendLine($"[save-access-probe]   {line}");
            if (o.ExceptionDetails.Length == 0)
                report.AppendLine("[save-access-probe]   (no exception in this window)");
            foreach (var detail in o.ExceptionDetails)
                report.AppendLine($"[save-access-probe]   stack: {detail}");
        }

        WriteReport(report.ToString().TrimEnd());
    }

    /// <summary>
    /// Emits one record to the console and, when configured, appends it to the probe log at once,
    /// so evidence already gathered survives anything that happens next.
    /// </summary>
    private static void WriteReport(string body)
    {
        var report = $"[save-access-probe] {DateTime.UtcNow:O} pid={Environment.ProcessId}"
            + $" runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}"
            + $" os={System.Runtime.InteropServices.RuntimeInformation.OSDescription}"
            + $" owned-profile={TestProfile.DirectoryPath}" + Environment.NewLine
            + body + Environment.NewLine;

        Console.WriteLine(report);
        Console.Out.Flush();
        var dir = Environment.GetEnvironmentVariable("CCP_PROBE_LOG_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "save-access-probe.log"), report);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[save-access-probe] evidence file unavailable: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
