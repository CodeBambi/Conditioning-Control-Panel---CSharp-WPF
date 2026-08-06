using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The deterministic half of memory (doc 01 §2.2): app state mirrored into the profile at zero token
/// cost. The mapping is worth testing because a mistake here is silent — the prompt simply stops
/// mentioning your streak and nobody notices for a release.
///
/// Only the pure helpers and the cooldown are exercised; <see cref="MemorySignalWriter.Start"/> wires
/// live app services, which do not exist headlessly (every subscription is null-guarded precisely so
/// that is a no-op rather than a crash — asserted below).
/// </summary>
public class MemorySignalWriterTests : IDisposable
{
    private readonly string _dir;
    private readonly List<IDisposable> _disposables = new();

    public MemorySignalWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccp-mem-signal-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { }
        }
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private MemoryStore NewStore()
    {
        var store = new MemoryStore(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json"), () => Now, 7);
        _disposables.Add(store);
        return store;
    }

    private static DateTime Now => new(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyDictionary<string, object?> Signals(
        AppSettings? settings, IReadOnlyDictionary<string, int>? usage = null, object? firstSeen = null)
        => MemorySignalWriter
            .BuildProfileSignals(settings, usage ?? new Dictionary<string, int>(), Now, firstSeen)
            .ToDictionary(p => p.Key, p => p.Value);

    // ================= profile mapping =================

    [Fact]
    public void BuildProfileSignals_MirrorsTheRealAppFields()
    {
        var settings = new AppSettings
        {
            PlayerLevel = 41,
            CurrentStreak = 12,
            TotalSessions = 87,
            LatestQuizArchetype = "Dollhouse Doll"
        };

        var signals = Signals(settings);

        Assert.Equal(41L, signals[MemoryStore.KeyLevel]);
        Assert.Equal(12L, signals[MemoryStore.KeyStreakDays]);
        Assert.Equal(87L, signals[MemoryStore.KeyTotalSessions]);
        Assert.Equal("Dollhouse Doll", signals[MemoryStore.KeyArchetype]);
    }

    [Fact]
    public void BuildProfileSignals_ClearsAnUnsetArchetypeRatherThanStoringEmptiness()
    {
        var signals = Signals(new AppSettings { LatestQuizArchetype = "   " });
        Assert.True(signals.ContainsKey(MemoryStore.KeyArchetype));
        Assert.Null(signals[MemoryStore.KeyArchetype]);
    }

    [Fact]
    public void BuildProfileSignals_LatchesFirstSeenAndNeverMovesIt()
    {
        // firstSeen is the anniversary the companion celebrates; recomputing it would silently reset
        // the user's whole history with her.
        var fresh = Signals(new AppSettings());
        Assert.Equal("2026-08-06", fresh[MemoryStore.KeyFirstSeen]);

        var returning = Signals(new AppSettings(), firstSeen: "2026-03-02");
        Assert.Equal("2026-03-02", returning[MemoryStore.KeyFirstSeen]);
    }

    [Fact]
    public void BuildProfileSignals_SurvivesAMissingSettingsObject()
    {
        var signals = Signals(null);

        Assert.Equal("2026-08-06", signals[MemoryStore.KeyFirstSeen]);
        Assert.False(signals.ContainsKey(MemoryStore.KeyLevel));
    }

    [Fact]
    public void BuildProfileSignals_ReservesTheLastSessionRecapKeyForTrain4()
    {
        // Train 1 has no episodic summariser, so the key must simply not be written — an empty
        // "lastSessionRecap=" in the prompt line would be pure noise.
        Assert.False(Signals(new AppSettings()).ContainsKey(MemoryStore.KeyLastSessionRecap));
    }

    // ================= favourite features =================

    [Fact]
    public void TopFeatures_RanksByUseAndCapsAtThree()
    {
        var usage = new Dictionary<string, int>
        {
            ["flash"] = 40, ["video"] = 12, ["bubbles"] = 30, ["mantra"] = 5, ["mindwipe"] = 4
        };

        Assert.Equal(new[] { "flash", "bubbles", "video" }, MemorySignalWriter.TopFeatures(usage));
    }

    [Fact]
    public void TopFeatures_IgnoresBarelyTouchedFeatures()
    {
        var usage = new Dictionary<string, int> { ["video"] = 1, ["flash"] = 2 };
        Assert.Empty(MemorySignalWriter.TopFeatures(usage));
    }

    [Fact]
    public void TopFeatures_BreaksTiesByNameSoTheProfileLineIsByteStable()
    {
        var usage = new Dictionary<string, int> { ["video"] = 9, ["bubbles"] = 9, ["flash"] = 9, ["mantra"] = 9 };

        var first = MemorySignalWriter.TopFeatures(usage);
        Assert.Equal(new[] { "bubbles", "flash", "mantra" }, first);
        Assert.Equal(first, MemorySignalWriter.TopFeatures(usage));
    }

    [Fact]
    public void BuildProfileSignals_ClearsFavoritesWhenNothingQualifies()
    {
        var signals = Signals(new AppSettings(), new Dictionary<string, int> { ["flash"] = 1 });
        Assert.Null(signals[MemoryStore.KeyFavoriteFeatures]);
    }

    [Fact]
    public void BuildProfileSignals_EmitsFavoritesAsAStringArray()
    {
        var usage = new Dictionary<string, int> { ["flash"] = 10, ["video"] = 8 };
        var favorites = Assert.IsType<string[]>(Signals(new AppSettings(), usage)[MemoryStore.KeyFavoriteFeatures]);
        Assert.Equal(new[] { "flash", "video" }, favorites);
    }

    // ================= usage cooldown =================

    [Fact]
    public void NoteFeatureUse_CountsOncePerCooldownWindow()
    {
        // Flashes fire hundreds of times an hour. Without the cooldown "favourite feature" would
        // always read "flash" for every user in the world.
        var now = Now;
        var store = NewStore();
        var writer = new MemorySignalWriter(store, () => now);
        _disposables.Add(writer);

        Assert.True(writer.NoteFeatureUse(MemorySignalWriter.FeatureFlash));
        for (int i = 0; i < 50; i++)
        {
            now = now.AddSeconds(2);
            Assert.False(writer.NoteFeatureUse(MemorySignalWriter.FeatureFlash));
        }

        now = now.Add(MemorySignalWriter.FeatureUseCooldown);
        Assert.True(writer.NoteFeatureUse(MemorySignalWriter.FeatureFlash));

        Assert.Equal(2, store.FeatureUsage[MemorySignalWriter.FeatureFlash]);
    }

    [Fact]
    public void NoteFeatureUse_TracksEachFeatureIndependently()
    {
        var store = NewStore();
        var writer = new MemorySignalWriter(store, () => Now);
        _disposables.Add(writer);

        Assert.True(writer.NoteFeatureUse(MemorySignalWriter.FeatureFlash));
        Assert.True(writer.NoteFeatureUse(MemorySignalWriter.FeatureVideo));
        Assert.False(writer.NoteFeatureUse(MemorySignalWriter.FeatureFlash));

        Assert.Equal(1, store.FeatureUsage[MemorySignalWriter.FeatureFlash]);
        Assert.Equal(1, store.FeatureUsage[MemorySignalWriter.FeatureVideo]);
    }

    [Fact]
    public void NoteFeatureUse_IgnoresBlankFeatureIds()
    {
        var store = NewStore();
        var writer = new MemorySignalWriter(store, () => Now);
        _disposables.Add(writer);

        Assert.False(writer.NoteFeatureUse(""));
        Assert.False(writer.NoteFeatureUse("   "));
        Assert.Empty(store.FeatureUsage);
    }

    [Fact]
    public void FavoriteFeaturesReachTheProfileOnceTheFloorIsCrossed()
    {
        var now = Now;
        var store = NewStore();
        var writer = new MemorySignalWriter(store, () => now);
        _disposables.Add(writer);

        for (int i = 0; i < MemorySignalWriter.FavoriteFeatureFloor; i++)
        {
            writer.NoteFeatureUse(MemorySignalWriter.FeatureBubbles);
            now = now.Add(MemorySignalWriter.FeatureUseCooldown);
        }

        var favorites = Assert.IsType<string[]>(store.Profile[MemoryStore.KeyFavoriteFeatures]);
        Assert.Equal(new[] { MemorySignalWriter.FeatureBubbles }, favorites);
        Assert.Contains("favoriteFeatures=bubbles", store.GetInjectionBlock(500));
    }

    // ================= lifecycle =================

    [Fact]
    public void StartAndStop_AreSafeWithoutAnyLiveAppServices()
    {
        // Every subscription is null-guarded; headless, Start() should mirror what it can (firstSeen)
        // and wire nothing. A throw here would take down App.OnStartup.
        var store = NewStore();
        var writer = new MemorySignalWriter(store, () => Now);
        _disposables.Add(writer);

        writer.Start();
        writer.Start();   // idempotent
        writer.Stop();
        writer.Stop();
        writer.Dispose();

        Assert.Equal("2026-08-06", store.Profile[MemoryStore.KeyFirstSeen]);
    }

    [Fact]
    public void WireDeferredSources_IsIdempotentAndSafeWithoutTheLateServices()
    {
        // Start() runs inside `new MemoryStore()` inside `new CompanionBrain(Ai)`, which OnStartup
        // builds ~200 lines before App.Mantra — and Start() is idempotent-by-flag, so without a
        // second pass MantraCompleted is never wired for the whole process lifetime and a user who
        // does mantras every session never sees "mantra" in their favourite features. Headless,
        // App.Mantra is null, so this must be a no-op rather than a throw.
        var store = NewStore();
        var writer = new MemorySignalWriter(store, () => Now);
        _disposables.Add(writer);

        writer.Start();
        writer.WireDeferredSources();
        writer.WireDeferredSources();
        store.WireDeferredSignals();   // the production entry point, via the store

        writer.Stop();
        writer.Dispose();
    }

    [Fact]
    public void NullStore_IsRejectedAtConstruction()
        => Assert.Throws<ArgumentNullException>(() => new MemorySignalWriter(null!));
}
