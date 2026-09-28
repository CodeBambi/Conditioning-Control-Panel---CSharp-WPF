using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// achievements.json through <see cref="AchievementStore"/>, against a golden file written by the
/// PRE-CHANGE WPF serializer path (WriteIndented, File.WriteAllText UTF-8 with BOM) under
/// TZ=Europe/Berlin. Regenerate it the same way when a persisted field is added on purpose.
/// </summary>
public sealed class AchievementStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ccp-ach-").FullName;
    private string MainPath => Path.Combine(_dir, "achievements.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string FixturePath([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "Fixtures", "achievements_golden.json");

    private static IEnumerable<JsonPropertyInfo> Persisted() =>
        new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }
            .GetTypeInfo(typeof(AchievementProgress)).Properties.Where(p => p.Get != null);

    [Fact]
    public void LoadThenSaveGivesIdenticalBytes()
    {
        InZone("Europe/Berlin", TimeSpan.FromHours(1), () =>
        {
            var golden = File.ReadAllBytes(FixturePath());
            File.WriteAllBytes(MainPath, golden);
            var store = new AchievementStore(MainPath);

            Assert.True(store.Write(store.Load()));

            Assert.Equal(golden, File.ReadAllBytes(MainPath));
            Assert.Equal(golden, File.ReadAllBytes(MainPath + ".bak")); // previous good document kept
            Assert.False(File.Exists(MainPath + ".tmp"));
        });
    }

    [Fact]
    public void FixtureCoversExactlyThePersistedProperties()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var inFixture = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
        var persisted = Persisted().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);

        Assert.Equal(persisted, inFixture);
    }

    [Fact]
    public void TruncatedMainFileRecoversEveryUnlockFromTheBackup()
    {
        var golden = File.ReadAllBytes(FixturePath());
        File.WriteAllBytes(MainPath + ".bak", golden);
        File.WriteAllBytes(MainPath, golden[..(golden.Length / 2)]);

        var loaded = new AchievementStore(MainPath).Load();

        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var expected = doc.RootElement.GetProperty("UnlockedAchievements").EnumerateArray().Select(e => e.GetString()!);
        Assert.True(loaded.UnlockedAchievements.SetEquals(expected));
        Assert.Contains("ビンボ", loaded.UnlockedAchievements);
        Assert.Equal(12345, loaded.TotalBubblesPopped);
    }

    [Fact]
    public void LoadSaveLoadIsParsedEqual()
    {
        File.Copy(FixturePath(), MainPath);
        var store = new AchievementStore(MainPath);
        var first = store.Load();

        Assert.True(store.Write(first));
        var second = store.Load();

        Assert.Equal(double.MaxValue, second.FastestLockCardSeconds);
        Assert.Null(second.AvatarClickStartTime);
        Assert.NotNull(second.NeedyDollClickStartTime);
        foreach (var p in Persisted())
        {
            object? a = p.Get!(first), b = p.Get!(second);
            if (a is HashSet<string> setA)
                Assert.True(setA.SetEquals((HashSet<string>)b!), p.Name);
            else if (a is DateTime dtA)
                Assert.Equal((dtA, dtA.Kind), ((DateTime)b!, ((DateTime)b!).Kind));
            else
                Assert.Equal(a, b);
        }
    }

    /// <summary>
    /// Pins the oracle risk rather than fixing it: LastLaunchDate is written as local time with an
    /// offset and read back as the same INSTANT in the reader's zone, so a file written at Berlin
    /// midnight lands on the previous calendar day in Los Angeles. The streak math reads
    /// <c>LastLaunchDate.Date</c>. If this ever changes, change it on purpose.
    /// </summary>
    [Fact]
    public void LastLaunchDateKeepsTheInstantAcrossTimeZones()
    {
        File.Copy(FixturePath(), MainPath);

        InZone("Europe/Berlin", TimeSpan.FromHours(1), () =>
        {
            var d = new AchievementStore(MainPath).Load().LastLaunchDate;
            Assert.Equal(DateTimeKind.Local, d.Kind);
            Assert.Equal(new DateTime(2025, 3, 14), d);
        });

        InZone("America/Los_Angeles", TimeSpan.FromHours(-7), () =>
        {
            var d = new AchievementStore(MainPath).Load().LastLaunchDate;
            Assert.Equal(DateTimeKind.Local, d.Kind);
            Assert.Equal(new DateTime(2025, 3, 13, 23, 0, 0, DateTimeKind.Utc), d.ToUniversalTime());
            Assert.Equal(new DateTime(2025, 3, 13), d.Date); // the day shift the streak would see
        });
    }

    /// <summary>
    /// Runs <paramref name="body"/> with TimeZoneInfo.Local pinned via TZ. Only Unix honours TZ;
    /// elsewhere (Windows CI) the zone cannot be pinned, so the test skips there - the Linux core
    /// job is the one that proves it.
    /// </summary>
    private static void InZone(string tz, TimeSpan offsetOn20250314, Action body)
    {
        var old = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", tz);
        TimeZoneInfo.ClearCachedData();
        try
        {
            if (TimeZoneInfo.Local.GetUtcOffset(new DateTime(2025, 3, 14, 12, 0, 0)) != offsetOn20250314)
                Assert.Skip($"Cannot pin local time zone to {tz} on this platform");
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", old);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
