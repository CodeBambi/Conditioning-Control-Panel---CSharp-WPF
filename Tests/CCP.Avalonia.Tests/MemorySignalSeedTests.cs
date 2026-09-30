using System;
using System.IO;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The head's SignalMirrorFactory (WPF App.xaml.cs SignalMirrorFactory + WireMemorySignalSources):
/// a feature this head shows lands in the brain's memory as WPF's feature services do, and a disposed
/// writer stops listening.</summary>
public sealed class MemorySignalSeedTests
{
    [Fact]
    public void SeededMirrorCountsFeatureUseAndUnsubscribesOnDispose()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-memsig-").FullName;
        var (factory, sources) = (MemoryStore.SignalMirrorFactory, MemorySignalWriter.SourcesHook);
        try
        {
            AvApp.SeedMemorySignals();
            using var store = new MemoryStore(Path.Combine(dir, "memory.json"));
            var writer = MemoryStore.SignalMirrorFactory!(store)!;

            AvApp.NoteFeatureUsed(MemorySignalWriter.FeatureFlash);
            AvApp.NoteFeatureUsed(MemorySignalWriter.FeatureFlash);   // inside the 5-minute cooldown
            AvApp.NoteFeatureUsed(MemorySignalWriter.FeatureBubbles);
            Assert.Equal(1, store.FeatureUsage[MemorySignalWriter.FeatureFlash]);
            Assert.Equal(1, store.FeatureUsage[MemorySignalWriter.FeatureBubbles]);

            writer.Dispose();
            AvApp.NoteFeatureUsed(MemorySignalWriter.FeatureSubliminal);
            Assert.False(store.FeatureUsage.ContainsKey(MemorySignalWriter.FeatureSubliminal));
        }
        finally
        {
            (MemoryStore.SignalMirrorFactory, MemorySignalWriter.SourcesHook) = (factory, sources);
            Directory.Delete(dir, true);
        }
    }
}
