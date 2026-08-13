using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-064 refusal pin — table-driven over the ONE registry (<see cref="HarnessEntryPoints.All"/>).
/// A new class-1 flag added to the registry inherits the refusal pin automatically; a flag
/// silently RE-classified out of Harness fails the count pin. Pure: no environment reads —
/// Program.Main reads <see cref="CompositionRoot.ActiveDataRootOverride"/> itself, so these
/// facts never mutate process env and need no ProcessEnvCollection membership (SP-062).
/// The behavioral proof that the gate rides the REAL entry point is the real-process run
/// (record.md Step 3) backed by the wiring assertion in HarnessEntryPointGuardTests — NOT an
/// in-process Program.Main call (a gate regression would hang the suite or write the real
/// profile from the test host; consult, record.md Step 1).
/// </summary>
public class HarnessEntryPointGateTests
{
    [Fact]
    public void HarnessEntries_AreSingledOut_TableDrivenOverRegistry()
    {
        // SP-066 framing (c): the loop carries the only assertions — pin the registry
        // non-empty (the count pin below covers the Harness subset; this covers All).
        Assert.NotEmpty(HarnessEntryPoints.All);
        foreach (var (flag, disposition) in HarnessEntryPoints.All)
        {
            var found = HarnessEntryPoints.HarnessFlagsIn([flag]);
            if (disposition == EntryPointDisposition.Harness)
            {
                Assert.True(found.Count == 1 && found[0] == flag,
                    $"registry says {flag} is Harness but HarnessFlagsIn returned [{string.Join(", ", found)}]");
            }
            else
            {
                Assert.True(found.Count == 0,
                    $"registry says {flag} is {disposition} but HarnessFlagsIn singled it out — " +
                    "only Harness entries may trigger the refusal");
            }
        }
    }

    [Fact]
    public void HarnessRegistryCount_IsPinnedAtEight()
    {
        // The eight class-1 entries enumerated from the tree (record.md Step 1). A silently
        // dropped or reclassified harness flag fails HERE even though the table-driven fact
        // above would happily pin a smaller set.
        var harness = HarnessEntryPoints.All
            .Where(kv => kv.Value == EntryPointDisposition.Harness)
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["--dtrh-block-route", "--dtrh-fx-drive", "--dtrh-kill-renderers", "--dtrh-m2test",
             "--intake-drive", "--intake-kill-renderers", "--loom-drive", "--tunnel-drive"],
            harness);
    }

    [Fact]
    public void RefusalMessage_NamesTheVariableAndEveryOffendingFlag()
    {
        var message = HarnessEntryPoints.RefusalMessage(["--dtrh-m2test", "--loom-drive"]);
        Assert.Contains(CompositionRoot.DataRootOverrideVariable, message);
        Assert.Contains("--dtrh-m2test", message);
        Assert.Contains("--loom-drive", message);
        Assert.Contains("HARNESS-ONLY", message);
    }

    [Fact]
    public void UnknownArgs_AreIgnored_NoRefusal()
    {
        // Typos and foreign tokens must not refuse — the gate binds the registry, not a
        // prefix heuristic. (A NEW real flag unclassified in the registry is the guard
        // test's job, not the gate's.)
        Assert.Empty(HarnessEntryPoints.HarnessFlagsIn(["--dtrh-m2tes", "positional", "--"]));
        Assert.Empty(HarnessEntryPoints.HarnessFlagsIn([]));
    }
}
