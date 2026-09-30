using System;
using System.IO;
using System.Reflection;
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
        var (factory, sources, sent) = (MemoryStore.SignalMirrorFactory, MemorySignalWriter.SourcesHook, CompanionBrain.UserMessageSent);
        var (features0, chats0) = (Handlers("FeatureUsed"), Handlers("UserMessageSent"));
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

            Assert.Equal(features0 + 1, Handlers("FeatureUsed"));
            Assert.Equal(chats0 + 1, Handlers("UserMessageSent"));
            writer.Dispose();   // a disposed writer must leave the head's events, not just ignore them
            Assert.Equal(features0, Handlers("FeatureUsed"));
            Assert.Equal(chats0, Handlers("UserMessageSent"));
        }
        finally
        {
            (MemoryStore.SignalMirrorFactory, MemorySignalWriter.SourcesHook, CompanionBrain.UserMessageSent) = (factory, sources, sent);
            Directory.Delete(dir, true);
        }
    }

    private static int Handlers(string evt) =>
        (typeof(AvApp).GetField(evt, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) as Delegate)?.GetInvocationList().Length ?? 0;
}
