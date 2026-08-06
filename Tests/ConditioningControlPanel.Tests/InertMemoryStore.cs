using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Companion.Brain;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// An <see cref="IMemoryStore"/> that remembers nothing and touches nothing.
///
/// <para>Use this wherever a test needs a store only to satisfy a constructor. The parameterless
/// <see cref="MemoryStore"/> constructor is the PRODUCTION one: it reads the real
/// <c>%LOCALAPPDATA%\ConditioningControlPanel\companion\memory.json</c>, starts a
/// <see cref="MemorySignalWriter"/>, and registers an <c>AppDomain.ProcessExit</c> handler that
/// writes that same file back when the process ends. A test that builds one is therefore reading
/// and rewriting the developer's own companion memory, and any assertion about an "empty" store is
/// really an assertion about whatever happens to be on the machine.</para>
///
/// <para>Tests that need genuine store behaviour should use the test constructor
/// <c>new MemoryStore(tempPath, clock, seed)</c> instead, which subscribes to nothing.</para>
/// </summary>
internal sealed class InertMemoryStore : IMemoryStore
{
    public string? GetInjectionBlock(int tokenBudget) => null;
    public void UpdateProfileSignal(string key, object? value) { }
    public IReadOnlyDictionary<string, object?> Profile => new Dictionary<string, object?>();
    public IReadOnlyList<MemoryFact> GetFacts() => Array.Empty<MemoryFact>();

    public MemoryFact AddFact(string text, MemoryFactKind kind, double salience = 0.5,
        string source = MemoryFact.SourceChat) =>
        new("f-inert", text, kind, salience, DateTime.UtcNow, null, 0, false, source);

    public bool UpdateFact(string id, string? text = null, double? salience = null, bool? pinned = null) => false;
    public bool ForgetFact(string id) => false;
    public void NoteFactUsed(string id) { }

    /// <summary>Counts calls so a test can prove "Forget everything" actually reached the store.</summary>
    public int WipeCalls { get; private set; }
    public void Wipe() => WipeCalls++;
}
