using System;
using System.IO;
using System.Text;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Language.Tests;

/// <summary>
/// [reader-probe] TEST-ONLY controlled experiment. Diagnostic, not a fix, and it gates nothing.
///
/// <para>Hypothesis under test: <see cref="SettingsService.SaveImmediate"/> publishes via
/// <c>File.Move(temp, settings.json, overwrite: true)</c>. On Windows that replacement needs the
/// target's existing handles to permit deletion, so a concurrent reader that opened settings.json
/// WITHOUT <see cref="FileShare.Delete"/> blocks the publish while the byte-identical reader WITH
/// it does not. If that holds, a reader is sufficient to make a save silently not land — which is
/// the shape of the flaky "expected fr, disk ja" language failure.</para>
///
/// <para>The two controls differ in exactly one bit: <c>FileShare.Delete</c>. Everything else —
/// the service, the profile, the value written, the ordering — is identical, so a difference in
/// outcome can only be attributed to that bit.</para>
///
/// <para>The negative control is EXPECTED to fail to persist. That failure is the measurement.
/// It is not a regression, and making it pass is not a production fix.</para>
///
/// <para>Runs against the module-initialized owned profile from <c>TestProfile</c>
/// (<c>LanguageSelectorTests.cs</c>) — no CCP_USERDATA_DIR change, no real or default user data —
/// and uses only the public service surface plus the Serilog sink the product already writes to.</para>
/// </summary>
public sealed class ReaderSharingProbeTests
{
    private sealed record Observation(
        string Control,
        FileShare Share,
        string SeededLanguage,
        string AttemptedLanguage,
        string PersistedLanguage,
        bool ReplacementSucceeded,
        string[] SaveLogLines);

    [Fact]
    public void AtomicSaveIsBlockedByAReaderThatWithholdsFileShareDelete()
    {
        // Mandatory sharing is a Windows concept. On POSIX, rename(2) ignores open handles, so
        // BOTH controls replace the file and the experiment has no question to answer — verified
        // locally on .NET 10.0.12 / Linux, where negative and positive both persisted "fr".
        // Skipping keeps this out of build.yml's unfiltered ubuntu run of this same project.
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "Reader/writer sharing semantics under test are Windows-only.");

        // Byte-identical controls apart from FileShare.Delete.
        var negative = RunControl("negative", FileShare.Read);
        var positive = RunControl("positive", FileShare.Read | FileShare.Delete);

        // Both observations are preserved BEFORE any assertion, so a surprising result is still
        // evidence rather than a bare assertion failure with nothing attached.
        WriteEvidence(negative, positive);

        // Predictions. A failure here means the hypothesis is wrong, not that the product broke.
        Assert.False(negative.ReplacementSucceeded);
        Assert.Equal("ja", negative.PersistedLanguage);
        Assert.True(positive.ReplacementSucceeded);
        Assert.Equal("fr", positive.PersistedLanguage);
    }

    /// <summary>
    /// Seeds Language=ja through the real service, holds settings.json open with
    /// <paramref name="share"/>, attempts Language=fr through the same real service, and reports
    /// what actually reached disk. The reader handle is always closed before the method returns.
    /// </summary>
    private static Observation RunControl(string control, FileShare share)
    {
        var settingsPath = Path.Combine(TestProfile.DirectoryPath, "settings.json");

        var service = new SettingsService();
        service.Current.Language = "ja";
        service.SaveImmediate();
        var seeded = ReadPersistedLanguage(settingsPath);

        string persisted;
        string[] lines;
        // FileShare.Read on OUR side is what the probe varies; the `using` is what guarantees the
        // handle cannot leak into the next control or the next test.
        using (var reader = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, share))
        {
            // Actually touch the bytes: a handle that is never read is easy to dismiss as inert.
            reader.ReadByte();

            SaveProbeSink.Instance.Mark($"reader-probe {control} share={share}");
            service.Current.Language = "fr";
            service.SaveImmediate();

            lines = SaveProbeSink.Instance.SinceLastMark();
            persisted = ReadPersistedLanguage(settingsPath);
        }

        return new Observation(control, share, seeded, "fr", persisted, persisted == "fr", lines);
    }

    /// <summary>Reads the Language value straight off disk — never from the in-memory model.</summary>
    private static string ReadPersistedLanguage(string settingsPath)
    {
        try
        {
            // FileShare.ReadWrite|Delete so the probe's own inspection can never itself be the
            // thing that blocks a replacement it is trying to observe.
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
    /// Appends both observations to the probe artifact directory. Only the language code, the
    /// share mode and the product's own save log lines — never the settings file contents.
    /// </summary>
    private static void WriteEvidence(params Observation[] observations)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_PROBE_LOG_DIR");
        var report = new StringBuilder();
        report.AppendLine($"[reader-probe] {DateTime.UtcNow:O} pid={Environment.ProcessId}"
            + $" runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}"
            + $" os={System.Runtime.InteropServices.RuntimeInformation.OSDescription}"
            + $" owned-profile={TestProfile.DirectoryPath}");

        foreach (var o in observations)
        {
            report.AppendLine($"[reader-probe] control={o.Control} share={o.Share}"
                + $" seeded={o.SeededLanguage} attempted={o.AttemptedLanguage}"
                + $" persisted={o.PersistedLanguage} replaced={o.ReplacementSucceeded}");
            if (o.SaveLogLines.Length == 0)
                report.AppendLine("[reader-probe]   (no settings log lines in this window)");
            foreach (var line in o.SaveLogLines)
                report.AppendLine($"[reader-probe]   {line}");
        }

        // Always on the console, so the evidence survives even with no artifact directory.
        Console.WriteLine(report.ToString());
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "reader-sharing-probe.log"), report.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[reader-probe] evidence file unavailable: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
