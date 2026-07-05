using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services.Settings;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Core.Tests;

/// <summary>
/// File-I/O contract tests for <see cref="SettingsService"/>: round trip through disk,
/// atomic-write (.tmp) recovery, corrupt-file handling, and the on-load migrations
/// (plaintext auth token to <see cref="ISecretStore"/>, legacy loudness threshold relax).
/// Each test gets its own temp directory; no process-wide static state is used.
/// </summary>
public sealed class SettingsServiceFileIoTests : IDisposable
{
    private readonly string _root;
    private readonly string _userDataPath;
    private readonly string _baseDirectory;

    public SettingsServiceFileIoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ccp-tests", Guid.NewGuid().ToString("N"));
        _userDataPath = Path.Combine(_root, "data");
        _baseDirectory = Path.Combine(_root, "base");
        Directory.CreateDirectory(_userDataPath);
        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        // A debounced save scheduled by the service may still fire after the test;
        // SettingsService catches its own I/O failures, so best-effort cleanup is safe.
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_userDataPath, "settings.json");
    private string TempFilePath => SettingsPath + ".tmp";

    private SettingsService CreateService(ISecretStore? secretStore = null) =>
        new(new FakeAppEnvironment(_baseDirectory, _userDataPath), secretStore ?? new RecordingSecretStore());

    private static void WriteJson(string path, JObject content) =>
        File.WriteAllText(path, content.ToString());

    [Fact]
    public void SaveImmediate_ThenFreshInstance_RoundTripsThroughDisk()
    {
        var service = CreateService();
        Assert.True(service.WasSettingsFileMissing);

        service.Current.Language = "fr";
        service.Current.PlayerLevel = 12;
        service.Current.FlashFrequency = 25;
        service.SaveImmediate();

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(TempFilePath));

        var reloaded = CreateService();
        Assert.False(reloaded.WasSettingsFileMissing);
        Assert.Equal("fr", reloaded.Current.Language);
        Assert.Equal(12, reloaded.Current.PlayerLevel);
        Assert.Equal(25, reloaded.Current.FlashFrequency);
    }

    [Fact]
    public void Load_LoneTempFile_IsRecoveredAsSettingsFile()
    {
        // Simulates a save interrupted after writing the .tmp but before the atomic move.
        WriteJson(TempFilePath, new JObject
        {
            ["Language"] = "de",
            ["FlashFrequency"] = 33
        });

        var service = CreateService();

        Assert.Equal("de", service.Current.Language);
        Assert.Equal(33, service.Current.FlashFrequency);
        Assert.False(service.WasSettingsFileMissing);
        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(TempFilePath));
    }

    [Fact]
    public void Load_TempFileAlongsideMainFile_MainFileWins_TempDeleted()
    {
        WriteJson(SettingsPath, new JObject { ["Language"] = "fr" });
        WriteJson(TempFilePath, new JObject { ["Language"] = "de" });

        var service = CreateService();

        Assert.Equal("fr", service.Current.Language);
        Assert.False(File.Exists(TempFilePath));
    }

    [Fact]
    public void Load_CorruptSettingsFile_ReturnsDefaults_AndPreservesFileAsTimestampedBackup()
    {
        var garbage = new byte[] { 0x00, 0x01, 0xFF, (byte)'{', (byte)'g', (byte)'a', (byte)'r', 0xFE };
        File.WriteAllBytes(SettingsPath, garbage);

        var service = CreateService();

        // Falls back to defaults without throwing...
        Assert.Equal("en", service.Current.Language);
        // ...and reports the file as CORRUPT, not a fresh install (v6.2.9 parity: the two are now
        // distinguished so callers can surface "we preserved a backup" instead of silent reset).
        Assert.True(service.WasSettingsFileCorrupt);
        Assert.False(service.WasSettingsFileMissing);
        // The unparseable original is MOVED aside to a timestamped backup (not left in place), so the
        // first debounced Save can't clobber it. LastCorruptBackupPath points at the preserved bytes.
        Assert.NotNull(service.LastCorruptBackupPath);
        Assert.False(File.Exists(SettingsPath));
        Assert.True(File.Exists(service.LastCorruptBackupPath!));
        Assert.Equal(garbage, File.ReadAllBytes(service.LastCorruptBackupPath!));
    }

    [Fact]
    public void Load_CorruptSettingsFile_IsQuarantinedBeforeDefaultsWin()
    {
        var garbage = new byte[] { 0x00, 0x01, 0xFF, (byte)'{', (byte)'g', 0xFE };
        File.WriteAllBytes(SettingsPath, garbage);

        var service = CreateService();

        // A recoverable copy survives under a timestamped settings.corrupt-*.json name so a later
        // Save (which writes a clean settings.json) can't destroy the original bytes.
        var backups = Directory.GetFiles(_userDataPath, "settings.corrupt-*.json");
        Assert.Single(backups);
        Assert.Equal(garbage, File.ReadAllBytes(backups[0]));
        Assert.Equal(backups[0], service.LastCorruptBackupPath);
    }

    [Fact]
    public void Load_SettingsWithOneUnparseableMember_KeepsTheRestInsteadOfResettingToDefaults()
    {
        // A single bad member (here an invalid value for a strongly-typed int field) must NOT
        // discard the whole document and reset every phrase/pool to factory defaults. This is the
        // v6.2.9 "my lock-card phrases / subliminals get wiped every time I update" fix: the load
        // is resilient, skips only the unparseable member, and keeps everything that DID parse.
        WriteJson(SettingsPath, new JObject
        {
            ["Language"] = "de",
            ["PlayerLevel"] = 42,
            ["FlashFrequency"] = "not-a-number"   // unparseable int -> per-member error, skipped
        });

        var service = CreateService();

        // The good members were preserved (NOT reset to defaults)...
        Assert.Equal("de", service.Current.Language);
        Assert.Equal(42, service.Current.PlayerLevel);
        // ...and this counts as a successful load, not a corrupt-file fallback.
        Assert.False(service.WasSettingsFileCorrupt);
        Assert.False(service.WasSettingsFileMissing);
    }

    [Fact]
    public void SaveImmediate_AfterCorruptLoad_OverwritesCorruptFileWithDefaults()
    {
        File.WriteAllBytes(SettingsPath, new byte[] { 0x00, 0xFF, (byte)'!', 0xFE });

        var service = CreateService();
        service.SaveImmediate();

        var reloaded = CreateService();
        Assert.False(reloaded.WasSettingsFileMissing);
        Assert.Equal("en", reloaded.Current.Language);
    }

    [Fact]
    public void Load_PlaintextAuthToken_IsMigratedToSecretStore()
    {
        WriteJson(SettingsPath, new JObject
        {
            ["Language"] = "en",
            ["auth_token"] = "secret-token-123"
        });

        var secrets = new RecordingSecretStore();
        CreateService(secrets);

        Assert.Contains("auth_token", secrets.StoreCalls);
        Assert.Equal("secret-token-123", Encoding.UTF8.GetString(secrets.Stored["auth_token"]));
    }

    [Fact]
    public void Load_PlaintextAuthToken_DoesNotClobberExistingSecret()
    {
        WriteJson(SettingsPath, new JObject { ["auth_token"] = "stale-plaintext" });

        var secrets = new RecordingSecretStore();
        secrets.Stored["auth_token"] = Encoding.UTF8.GetBytes("already-migrated");
        CreateService(secrets);

        Assert.Empty(secrets.StoreCalls);
        Assert.Equal("already-migrated", Encoding.UTF8.GetString(secrets.Stored["auth_token"]));
    }

    [Fact]
    public void Load_LegacyLoudnessThreshold_IsRelaxedOnce()
    {
        WriteJson(SettingsPath, new JObject
        {
            ["SpeechLoudnessThreshold"] = 0.04,
            ["LoudnessThresholdRelaxed"] = false
        });

        var service = CreateService();

        Assert.Equal(0.015, service.Current.SpeechLoudnessThreshold, precision: 6);
        Assert.True(service.Current.LoudnessThresholdRelaxed);
    }

    [Fact]
    public void Load_LoudnessThreshold_AlreadyRelaxed_IsLeftAlone()
    {
        // One-shot guard: once relaxed, a value parked at the old default is a deliberate choice.
        WriteJson(SettingsPath, new JObject
        {
            ["SpeechLoudnessThreshold"] = 0.04,
            ["LoudnessThresholdRelaxed"] = true
        });

        var service = CreateService();

        Assert.Equal(0.04, service.Current.SpeechLoudnessThreshold, precision: 6);
        Assert.True(service.Current.LoudnessThresholdRelaxed);
    }

    [Fact]
    public void Load_NonLegacyLoudnessThreshold_IsNotRelaxed_ButGuardIsSet()
    {
        // A value outside the legacy 0.035-0.045 window is user-tuned; only the guard flips.
        WriteJson(SettingsPath, new JObject
        {
            ["SpeechLoudnessThreshold"] = 0.10,
            ["LoudnessThresholdRelaxed"] = false
        });

        var service = CreateService();

        Assert.Equal(0.10, service.Current.SpeechLoudnessThreshold, precision: 6);
        Assert.True(service.Current.LoudnessThresholdRelaxed);
    }

    private sealed class FakeAppEnvironment : IAppEnvironment
    {
        public FakeAppEnvironment(string baseDirectory, string userDataPath)
        {
            BaseDirectory = baseDirectory;
            UserDataPath = userDataPath;
        }

        public string BaseDirectory { get; }
        public string UserDataPath { get; }
        public string ApplicationDataPath => UserDataPath;
        public string EffectiveAssetsPath => Path.Combine(UserDataPath, "assets");
    }

    private sealed class RecordingSecretStore : ISecretStore
    {
        public Dictionary<string, byte[]> Stored { get; } = new();
        public List<string> StoreCalls { get; } = new();

        public void Store(string key, byte[] value)
        {
            StoreCalls.Add(key);
            Stored[key] = value;
        }

        public byte[]? Retrieve(string key) => Stored.TryGetValue(key, out var value) ? value : null;

        public void Delete(string key) => Stored.Remove(key);
    }
}
