using ConditioningControlPanel.Services.Logging;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Serialises every suite that configures <see cref="LogRedactor"/>'s PROCESS-WIDE roots. The
/// roots are static because the redactor runs on every log line and cannot afford an instance
/// lookup per call; that is right for the app and hostile to xUnit's default parallelism, so the
/// logging suites share one collection instead of racing each other's %DATA% folder.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LoggingStaticsCollection
{
    public const string Name = "LoggingStatics";
}

/// <summary>
/// The write-path redaction rules. Until now the only redaction in the app ran at bug-report
/// UPLOAD time (<see cref="ConditioningControlPanel.Services.LogScrubber"/>), so a week of logs
/// carried the user's home folder 1,763 times and crash.log carried it in every stack frame -
/// readable by anyone who opened the folder, whether or not a report was ever filed. These pin the
/// rules that now run before the text reaches the disk.
/// </summary>
[Collection(LoggingStaticsCollection.Name)]
public class LogRedactorTests
{
    private const string Data = @"C:\Users\alice\AppData\Local\ConditioningControlPanel";
    private const string AppDir = @"C:\Program Files\ConditioningControlPanel";
    private const string Assets = @"D:\ccp-assets";

    public LogRedactorTests() => LogRedactor.ConfigureRoots(Data, AppDir, Assets);

    [Fact]
    public void KnownRoots_CollapseToTokens()
    {
        Assert.Equal(@"%DATA%\settings.json", LogRedactor.Redact(Data + @"\settings.json"));
        Assert.Equal(@"%APP%\ccp.exe", LogRedactor.Redact(AppDir + @"\ccp.exe"));
        Assert.Equal(@"%ASSETS%\images\a.png", LogRedactor.Redact(Assets + @"\images\a.png"));
    }

    [Fact]
    public void KnownRoots_AreCaseAndSlashInsensitive()
    {
        // Paths reach the log from three places that disagree about case and separators: Path.Combine,
        // WebView2 file URIs, and hand-built strings.
        Assert.Equal("%DATA%/logs/app.log", LogRedactor.Redact(Data.ToUpperInvariant().Replace('\\', '/') + "/logs/app.log"));
    }

    [Fact]
    public void DataRoot_WinsOverTheHomeFolderRule()
    {
        // %DATA% lives under C:\Users\<name>, so rule order matters: the more useful token has to
        // be applied first or every data path degrades to "~\AppData\Local\...".
        Assert.StartsWith("%DATA%", LogRedactor.Redact(Data + @"\mods"));
    }

    [Fact]
    public void OtherHomeFolders_BecomeTilde()
    {
        Assert.Equal(@"~\Desktop\clip.mp4", LogRedactor.Redact(@"C:\Users\alice\Desktop\clip.mp4"));
        Assert.Equal("reading ~/app/log.txt", LogRedactor.Redact("reading /home/alice/app/log.txt"));
    }

    [Fact]
    public void EmailsAndTokens_AreRemoved()
    {
        Assert.Equal("mail <email> failed", LogRedactor.Redact("mail alice@example.com failed"));

        // A bearer header carries no path separator and no long digit run, so it only survives the
        // cheap pre-filter thanks to the base64-run arm. That arm is the point of this assertion.
        Assert.Equal("Bearer <token>", LogRedactor.Redact("Bearer abcdefghijklmnopqrstuvwxyz012345"));
        Assert.Equal(@"{""access_token"": <token>", LogRedactor.Redact(@"{""access_token"": ""abcdef0123456789"""));
    }

    [Fact]
    public void LongIds_KeepOnlyTheLastFour()
    {
        // Discord ids are 17-19 digits. The tail stays so two lines about the same account can
        // still be correlated during triage without naming the account.
        Assert.Equal("user <id:…7890>", LogRedactor.Redact("user 123456789012347890"));
        Assert.Equal("u_…9bc0", LogRedactor.Redact("u_4f2a71bc9bc0"));

        // Shorter runs are timestamps, sizes and counts, and must survive untouched.
        Assert.Equal("took 1234567890 ms", LogRedactor.Redact("took 1234567890 ms"));
    }

    [Fact]
    public void OrdinaryLines_ArePassedThroughUnchanged()
    {
        // The cheap pre-filter is the whole reason this is affordable per line: a line with no
        // separator, no "@" and no long digit run must not touch a single regex.
        const string line = "Flash shown (count 12, level 4)";
        Assert.Same(line, LogRedactor.Redact(line));
    }

    [Fact]
    public void Scrubber_StillRedactsAfterSharingTheRuleSet()
    {
        // LogScrubber now aliases the redactor's regexes rather than owning copies. Its OUTPUT
        // shape is different on purpose (a human reads it), so pin that it did not drift.
        var (scrubbed, counts) = ConditioningControlPanel.Services.LogScrubber.Scrub(
            @"crash in C:\Users\alice\app.exe for bob@example.com");
        Assert.Contains(@"Users\<redacted>", scrubbed);
        Assert.Contains("[email redacted]", scrubbed);
        Assert.Equal(1, counts.Paths);
        Assert.Equal(1, counts.Emails);
    }
}
