using CcpClient.Desktop;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// Runtime-capability-contract tests: every state reachable, probe-throw → Faulted,
/// unregistered → Unavailable(unknown), registration alone never Available, and the real
/// composition root populates states via real probes (contract conformance checklist).
/// </summary>
public class CapabilityTests
{
    private static CapabilityProbeRunner RunnerFor(CapabilityRegistry registry, out OperationRegistry operations)
    {
        operations = new OperationRegistry();
        return new CapabilityProbeRunner(operations.OwnerFor("test-probes"), registry);
    }

    // --- Query honesty (contract §3 rule 4) ---

    [Fact]
    public void UnregisteredQuery_ReturnsUnavailableUnknown_NeverFabricated()
    {
        var registry = new CapabilityRegistry();
        var state = registry.GetState("nope");
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(CapabilityReasonCodes.UnknownCapability, unavailable.Reason.Code);
    }

    [Fact]
    public void RegistrationAlone_NeverYieldsAvailable_StaysNotProbed()
    {
        var registry = new CapabilityRegistry();
        registry.Register("display-session", _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("lie")));
        var state = registry.GetState("display-session");
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(CapabilityReasonCodes.NotProbed, unavailable.Reason.Code);
    }

    // --- Outcome→state mapping (contract §3 rule 3) ---

    public static IEnumerable<object[]> AllStates()
    {
        yield return [new CapabilityState.Available("confirmed")];
        yield return [new CapabilityState.Unavailable(new CapabilityReason("x", "absent"))];
        yield return [new CapabilityState.Degraded("survives: a", new CapabilityReason("x", "b broken"))];
        yield return [new CapabilityState.PermissionRequired(new CapabilityReason("x", "grant needed"))];
        yield return [new CapabilityState.DependencyMissing("libfoo", new CapabilityReason("x", "not found"))];
        yield return [new CapabilityState.Faulted(new CapabilityReason("x", "boom"))];
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public async Task EveryState_ReachableViaProbe(CapabilityState expected)
    {
        var registry = new CapabilityRegistry();
        registry.Register("cap", _ => Task.FromResult(expected));
        var runner = RunnerFor(registry, out _);

        await runner.RunAllAsync(CancellationToken.None);

        Assert.Equal(expected, registry.GetState("cap"));
    }

    [Fact]
    public async Task ProbeThrows_MapsToFaulted_WithExceptionClass_NeverAvailable()
    {
        var registry = new CapabilityRegistry();
        registry.Register("cap", _ => throw new InvalidOperationException("probe exploded"));
        var runner = RunnerFor(registry, out _);

        await runner.RunAllAsync(CancellationToken.None); // never unhandled

        var faulted = Assert.IsType<CapabilityState.Faulted>(registry.GetState("cap"));
        Assert.Equal(CapabilityReasonCodes.ProbeFault, faulted.Reason.Code);
        Assert.Contains(nameof(InvalidOperationException), faulted.Reason.Detail);
    }

    [Fact]
    public async Task ProbeReturnsNoState_MapsToFaulted()
    {
        var registry = new CapabilityRegistry();
        registry.Register("cap", _ => Task.FromResult<CapabilityState>(null!));
        var runner = RunnerFor(registry, out _);

        await runner.RunAllAsync(CancellationToken.None);

        Assert.IsType<CapabilityState.Faulted>(registry.GetState("cap"));
    }

    [Fact]
    public async Task StartupCancelled_LeavesRemainingProbesHonestlyNotProbed()
    {
        var registry = new CapabilityRegistry();
        registry.Register("first", _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("ok")));
        registry.Register("second", _ => Task.FromResult<CapabilityState>(new CapabilityState.Available("ok")));
        var runner = RunnerFor(registry, out _);

        await runner.RunAllAsync(new CancellationToken(canceled: true));

        foreach (var name in registry.Names)
        {
            var unavailable = Assert.IsType<CapabilityState.Unavailable>(registry.GetState(name));
            Assert.Equal(CapabilityReasonCodes.NotProbed, unavailable.Reason.Code);
        }
    }

    // --- Demonstrator 1: session probe (contract §8.1) ---

    private sealed class FakeSessionEnvironment(bool windows, bool linux, string? wayland, string? x11)
        : ISessionEnvironment
    {
        public bool IsWindows { get; } = windows;

        public bool IsLinux { get; } = linux;

        public string? WaylandDisplay { get; } = wayland;

        public string? X11Display { get; } = x11;
    }

    [Theory]
    [InlineData(true, false, null, null, SessionKind.Windows)]
    [InlineData(false, true, null, ":0", SessionKind.LinuxX11)]
    [InlineData(false, true, "wayland-0", null, SessionKind.LinuxWayland)]
    [InlineData(false, true, "wayland-0", ":0", SessionKind.LinuxWaylandWithX11)]
    public void SessionProbe_TypedSessionFact(bool windows, bool linux, string? wayland, string? x11, SessionKind expected)
    {
        var env = new FakeSessionEnvironment(windows, linux, wayland, x11);
        Assert.Equal(expected, SessionProbe.Detect(env));
        var available = Assert.IsType<CapabilityState.Available>(SessionProbe.Probe(env));
        Assert.NotEmpty(available.Detail);
    }

    [Fact]
    public void SessionProbe_WslgShape_ReportsSessionFacts_DisclaimsBackendClaim()
    {
        // WSLg sets BOTH variables while Avalonia runs as an X11 client by default.
        var state = SessionProbe.Probe(new FakeSessionEnvironment(false, true, "wayland-0", ":0"));
        var available = Assert.IsType<CapabilityState.Available>(state);
        Assert.Contains("XWayland", available.Detail);
        Assert.Contains("not a claim", available.Detail);
    }

    [Fact]
    public void SessionProbe_HeadlessLinux_UnavailableNoDisplayServer()
    {
        var state = SessionProbe.Probe(new FakeSessionEnvironment(false, true, null, null));
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(CapabilityReasonCodes.NoDisplayServer, unavailable.Reason.Code);
    }

    [Fact]
    public void SessionProbe_UnsupportedPlatform_Unavailable()
    {
        var state = SessionProbe.Probe(new FakeSessionEnvironment(false, false, null, null));
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
        Assert.Equal(CapabilityReasonCodes.UnsupportedPlatform, unavailable.Reason.Code);
    }

    // --- Demonstrator 2: atomic filesystem probe (contract §8.2) ---

    private sealed class FakeMountTable(string? fsType) : IMountTable
    {
        public string? FilesystemTypeOf(string path) => fsType;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void FileSystemProbe_RealIO_Performed_AvailableOnLocalFilesystem()
    {
        var dir = NewTempDir();
        try
        {
            var state = AtomicFileSystemProbe.Probe(dir, new ProcMountsTable());

            // Honesty rule: derive the expectation from the same kernel evidence — never
            // assume the filesystem (WSL temp is ext4; a DrvFs temp would truthfully degrade).
            var fsType = new ProcMountsTable().FilesystemTypeOf(Path.GetFullPath(dir));
            var passthrough = fsType is "9p" or "drvfs" or "cifs" or "smbfs" or "nfs" or "nfs4";
            if (passthrough)
            {
                Assert.IsType<CapabilityState.Degraded>(state);
            }
            else
            {
                Assert.IsType<CapabilityState.Available>(state);
            }

            // The I/O really happened and cleaned up after itself.
            Assert.Empty(Directory.EnumerateFiles(dir, "ccp-capability-probe-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileSystemProbe_PassthroughMount_DowngradesToTruthfulDegraded()
    {
        var dir = NewTempDir();
        try
        {
            var state = AtomicFileSystemProbe.Probe(dir, new FakeMountTable("9p"));

            var degraded = Assert.IsType<CapabilityState.Degraded>(state);
            Assert.Equal(CapabilityReasonCodes.DurabilityUnverified, degraded.Reason.Code);
            Assert.Contains("verified by real I/O", degraded.SurvivingSemantics); // surviving semantics NAMED
            Assert.Contains("9p", degraded.Reason.Detail);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileSystemProbe_NativeMount_NoDowngrade()
    {
        var dir = NewTempDir();
        try
        {
            var state = AtomicFileSystemProbe.Probe(dir, new FakeMountTable("ext4"));
            Assert.IsType<CapabilityState.Available>(state);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FileSystemProbe_IoFailure_UnavailableWithFsReason()
    {
        // A path whose parent is a FILE: CreateDirectory fails with IOException on every OS.
        var fileParent = Path.Combine(NewTempDir(), "a-file");
        File.WriteAllText(fileParent, "x");
        try
        {
            var state = AtomicFileSystemProbe.Probe(Path.Combine(fileParent, "sub"), new FakeMountTable(null));
            var unavailable = Assert.IsType<CapabilityState.Unavailable>(state);
            Assert.Equal(CapabilityReasonCodes.IoFailure, unavailable.Reason.Code);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(fileParent)!, recursive: true);
        }
    }

    // --- Composition-root path: real probes, no doubles (contract §9.2) ---

    [Fact]
    public async Task RealCompositionRoot_CapabilityProbesPhase_PopulatesStatesViaRealProbes()
    {
        var trace = new StartupTrace();
        var settingsDir = Path.Combine(Path.GetTempPath(), "ccp-capint-" + Guid.NewGuid().ToString("N"));
        var root = new CompositionRoot
        {
            SettingsPathFactory = () => Path.Combine(settingsDir, "settings.json"),
        };
        ApplicationHost? host = null;

        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);

        try
        {
            Assert.IsType<StartupOutcome.Success>(outcome);
            Assert.Contains("CapabilityProbes: ok", trace.Entries);

            var capabilities = Assert.IsType<CapabilityRegistry>(host!.Capabilities);
            Assert.Equal(["display-session", "atomic-filesystem"], capabilities.Names);

            // Real session probe: this test machine is Windows or a sessioned Linux.
            Assert.IsType<CapabilityState.Available>(capabilities.GetState("display-session"));

            // Real fs probe: exercised the real data directory; Available on local fs,
            // truthfully Degraded on passthrough — never not-probed, never faked.
            var fsState = capabilities.GetState("atomic-filesystem");
            Assert.True(fsState is CapabilityState.Available or CapabilityState.Degraded,
                $"unexpected fs state: {fsState}");
        }
        finally
        {
            if (host is not null)
            {
                await host.ShutdownAsync();
            }

            if (Directory.Exists(settingsDir))
            {
                Directory.Delete(settingsDir, recursive: true);
            }
        }
    }
}
