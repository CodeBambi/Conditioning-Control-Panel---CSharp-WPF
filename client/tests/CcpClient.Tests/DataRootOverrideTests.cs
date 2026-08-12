using CcpClient.Desktop;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Features.Intake;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-057: the CCP_DATA_ROOT isolation seam. Two halves:
/// (1) <see cref="DataRootOverrideTests"/> — the PURE validation core
/// (<see cref="CompositionRoot.ResolveDataRoot"/>) plus the unset-env default-path pin;
/// no environment mutation, so this class runs anywhere.
/// (2) <see cref="DataRootOverrideEnvTests"/> — the env-honored half through the REAL
/// composition, in a non-parallel collection (process-wide env mutation is an xunit
/// parallelism hazard — consult A1/A2).
/// </summary>
public class DataRootOverrideTests
{
    [Fact]
    public void ResolveDataRoot_RelativePath_ThrowsTyped()
    {
        var ex = Assert.Throws<DataRootOverrideException>(() => CompositionRoot.ResolveDataRoot("relative/sandbox"));
        Assert.Contains("fully-qualified absolute path", ex.Message);
    }

    [Fact]
    public void ResolveDataRoot_DriveRelativePath_ThrowsTyped()
    {
        // C:foo is drive-RELATIVE (rooted per IsPathRooted, not fully qualified) — the
        // subtle case that would silently land wherever the drive's CWD points.
        Assert.Throws<DataRootOverrideException>(() => CompositionRoot.ResolveDataRoot("C:foo"));
    }

    [Fact]
    public void ResolveDataRoot_UncreatableDirectory_ThrowsTyped_NeverFallsBack()
    {
        // A path whose parent is a FILE can never become a directory.
        using var dir = new TempDir();
        var blocker = Path.Combine(dir.Root, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var ex = Assert.Throws<DataRootOverrideException>(
            () => CompositionRoot.ResolveDataRoot(Path.Combine(blocker, "child")));
        Assert.Contains("cannot be created/used", ex.Message);
    }

    [Fact]
    public void ResolveDataRoot_ValidAbsolutePath_NormalizesAndCreates()
    {
        using var dir = new TempDir();
        var target = Path.Combine(dir.Root, "fresh", "nested");
        var resolved = CompositionRoot.ResolveDataRoot(target);
        Assert.Equal(Path.GetFullPath(target), resolved);
        Assert.True(Directory.Exists(resolved)); // the harness may point at a fresh root
    }

    [Fact]
    public void DefaultSettingsPath_EnvUnset_IsThePlatformDefault()
    {
        // The reasoned negative demonstration (record.md Step 3): with no override, the
        // choke point resolves the REAL per-user profile — which is exactly why headed
        // evidence runs must set the override. Also pins both platform defaults against
        // accidental change (framing c).
        if (CompositionRoot.ActiveDataRootOverride() is not null)
        {
            return; // the runner itself set the override; the pin is meaningless here
        }

        var expectedRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(expectedRoot))
        {
            expectedRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var actual = CompositionRoot.DefaultSettingsPath();
        if (CompositionRoot.ActiveDataRootOverride() is not null)
        {
            // A sibling env-mutating fixture (DataRootOverrideEnvTests, the
            // ProcessEnvCollection half) set the process-wide override BETWEEN the guard
            // above and the read — the observed SP-057 flake class (SP-061 red classified
            // 2026-08-12: reproduced on the wave-18 base commit; the collection's
            // DisableParallelization serialization did not prevent the overlap under
            // xUnit.v3 3.2.2 + the MTP runner). Skip: the pin only binds when the override
            // was unset at BOTH checkpoints (the read then provably came from the unset state).
            return;
        }

        Assert.Equal(
            Path.Combine(expectedRoot, "CcpClient", "settings.json"),
            actual);
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ccp-sp057-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ }
        }
    }
}

/// <summary>The env-mutating half. DisableParallelization: this collection never runs
/// concurrently with ANY other, so no sibling test can observe the process-wide
/// override mid-flight (consult A1).</summary>
[CollectionDefinition(DisableParallelization = true)]
public sealed class ProcessEnvCollection;

[Collection(nameof(ProcessEnvCollection))]
public sealed class DataRootOverrideEnvTests
{
    [Fact]
    public void Override_IsHonoredByEveryCensusedConsumer()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "ccp-sp057-env-" + Guid.NewGuid().ToString("N"));
        var prior = Environment.GetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable);
        Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, sandbox);
        try
        {
            var expectedDir = Path.GetFullPath(sandbox);
            // The choke point itself.
            Assert.Equal(Path.Combine(expectedDir, "settings.json"), CompositionRoot.DefaultSettingsPath());
            // The static consumer that never sees Program args (DtrhProfileLock.cs:33).
            Assert.Equal(Path.Combine(expectedDir, "dtrh"), DtrhProfileLock.DtrhDataRoot());
            Assert.Equal(Path.Combine(expectedDir, "dtrh", "wv2-profile"), DtrhProfileLock.WebView2ProfileDir());
            // The ?? DefaultSettingsPath() fallbacks (DtrhParticipant.cs:57, IntakeParticipant.cs:42).
            var infra = new ParticipantInfrastructure(new OperationRegistry(), new UiDispatchBoundary(), new DebugLogSink());
            Assert.Equal(expectedDir, new DtrhParticipant(infra.OwnerFor("t"), new DebugLogSink()).DataDirectory);
            Assert.Equal(expectedDir, new IntakeParticipant(new DebugLogSink()).DataDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, prior);
            try { Directory.Delete(sandbox, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task Override_ThroughRealComposition_PersistsIntoOverrideRoot()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "ccp-sp057-boot-" + Guid.NewGuid().ToString("N"));
        var prior = Environment.GetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable);
        Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, sandbox);
        try
        {
            // The REAL composition root through the REAL phase runner (contract §10.2
            // shape, IntegrationProofTests precedent) — no SettingsPathFactory substitute,
            // so the env seam is the ONLY thing redirecting persistence.
            var trace = new StartupTrace();
            var root = new CompositionRoot();
            ApplicationHost? host = null;
            var outcome = await StartupPhaseRunner.RunAsync(
                Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
            Assert.IsType<StartupOutcome.Success>(outcome);
            Assert.NotNull(host);

            // Mutate through the real participants and force the writes: the override root
            // must end up demonstrably populated (record.md Step 3 positive-control class).
            var store = host!.Participants.OfType<PersistenceStore<DemoSettings>>().Single();
            store.Mutate(s => s.Volume = 51);
            Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());
            var slots = host.Participants.OfType<DtrhSaveSlots>().Single();
            slots.StoreFor(1).Mutate(d => d.Sparks = 123);
            Assert.IsType<OperationOutcome.Completed>(await slots.StoreFor(1).SaveImmediate());
            slots.IndexStore.Mutate(i => i.Difficulty = "Easy");
            Assert.IsType<OperationOutcome.Completed>(await slots.IndexStore.SaveImmediate());

            var dir = Path.GetFullPath(sandbox);
            Assert.True(File.Exists(Path.Combine(dir, "settings.json")), "settings.json must land in the override root");
            Assert.True(File.Exists(Path.Combine(dir, "dtrh_slot1.json")), "slot document must land in the override root");
            Assert.True(File.Exists(Path.Combine(dir, "dtrh_slots.json")), "the slot index (the file SP-052 Run A clobbered) must land in the override root");
            // The DTRH participant's data directory rode the same seam.
            Assert.Equal(dir, host.Participants.OfType<DtrhParticipant>().Single().DataDirectory);

            await host.ShutdownAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, prior);
            try { Directory.Delete(sandbox, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Override_BadValue_ThrowsTypedAtComposition_NeverRealProfile()
    {
        var prior = Environment.GetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable);
        Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, "relative/not-absolute");
        try
        {
            // The failure surfaces at path resolution — the first thing composition does.
            Assert.Throws<DataRootOverrideException>(() => CompositionRoot.DefaultSettingsPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(CompositionRoot.DataRootOverrideVariable, prior);
        }
    }
}
