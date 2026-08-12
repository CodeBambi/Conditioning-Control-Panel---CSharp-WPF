using Avalonia.Platform;
using CcpClient.Desktop;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-007 first-visible-slice proofs: the demo.status-ticker toggle starts/cancels a REAL
/// SP-004 owned operation with typed outcomes; the ring derives from the operation
/// authority; the flag round-trips through the SP-005 store (FILE-content asserts, never
/// view-model state); restore-then-start is ordered through the real composition root; the
/// avares:// asset stream-opens. All through the REAL composition root and phase runner —
/// no mocks (contract §10.2 pattern).
/// </summary>
public class StatusTickerSliceTests
{
    private static (CompositionRoot Root, string SettingsPath, string Dir) RealRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp007-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        return (new CompositionRoot { SettingsPathFactory = () => settingsPath }, settingsPath, dir);
    }

    private static async Task<ApplicationHost> BootAsync(CompositionRoot root)
    {
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        return host!;
    }

    [Fact]
    public async Task ToggleOn_StartsRealOwnedOperation_ToggleOff_CompletesCancelled_RingFollowsOperation()
    {
        var (root, _, _) = RealRoot();
        var host = await BootAsync(root);
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        var vm = new MainWindowViewModel(host);

        Assert.False(vm.TickerLit); // ring dark before any toggle

        vm.ToggleCommand.Execute(StatusTickerParticipant.FeatureId); // the ONE command path (A-004)
        Assert.True(ticker.IsOperationLive);
        Assert.True(vm.TickerLit); // ring derives from the operation authority
        Assert.True(vm.TickerVisible);
        var completion = ticker.Completion;
        Assert.NotNull(completion);
        Assert.False(completion!.IsCompleted); // a real long-running operation

        var ticksBefore = ticker.TickCount;
        // Class 2 (SP-059): a REAL 500ms ticker — poll for the first tick via the approved
        // helper (returns at the first tick; tolerant window) instead of a fixed 1200ms sleep.
        await TestWait.Until(() => ticker.TickCount > ticksBefore, "the tick ADVANCES — the operation is real (500ms real interval)", () => $"ticks={ticker.TickCount}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ticker.TickCount > ticksBefore); // the tick ADVANCES — the operation is real

        vm.ToggleCommand.Execute(StatusTickerParticipant.FeatureId);
        Assert.False(ticker.IsOperationLive);
        Assert.False(vm.TickerLit);
        Assert.False(vm.TickerVisible);
        // Typed terminal outcome (async contract §2): stopping is proven by Cancelled,
        // not by the absence of ticks.
        Assert.IsType<OperationOutcome.Cancelled>(await completion);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task SetEnabled_IsIdempotent_NoSecondGenerationRace()
    {
        var (root, _, _) = RealRoot();
        var host = await BootAsync(root);
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();

        ticker.SetEnabled(true);
        var completion = ticker.Completion;
        ticker.SetEnabled(true); // re-entrant double-toggle guard: no-op, no second generation
        Assert.Same(completion, ticker.Completion);

        ticker.SetEnabled(false);
        ticker.SetEnabled(false); // idempotent off
        Assert.False(ticker.IsOperationLive);
        Assert.IsType<OperationOutcome.Cancelled>(await completion!);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task ToggleBeforePhase3Start_Throws_ToggleOn_WithoutStart_IsACompositionBug()
    {
        // Construction starts nothing (SP-003 §4.4): toggling a never-started participant
        // must fail loudly, not silently no-op.
        var host = new CompositionRoot { SettingsPathFactory = () => Path.Combine(Path.GetTempPath(), "ccp-sp007-neverboot", "settings.json") }
            .Build(new StartupTrace());
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        Assert.False(ticker.Running);
        Assert.Throws<InvalidOperationException>(() => ticker.SetEnabled(true));
    }

    [Fact]
    public async Task Toggle_PersistsFlagThroughStore_FileContentProof()
    {
        var (root, settingsPath, _) = RealRoot();
        var host = await BootAsync(root);
        var store = host.Participants.OfType<PersistenceStore<DemoSettings>>().Single();
        var vm = new MainWindowViewModel(host);

        vm.ToggleCommand.Execute(StatusTickerParticipant.FeatureId);
        var outcome = await store.SaveImmediate(); // await quiescence of the chained writer
        Assert.IsType<OperationOutcome.Completed>(outcome);

        // FILE content, not view-model state (persistence proof).
        var json = await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"statusTickerEnabled\": true", json);

        await host.ShutdownAsync();

        var jsonAfterTeardown = await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"statusTickerEnabled\": true", jsonAfterTeardown);
    }

    [Fact]
    public async Task Restart_RestoresFlag_AndRestartsOperation_ThroughRealCompositionRoot()
    {
        var (root, settingsPath, _) = RealRoot();
        var first = await BootAsync(root);
        var firstStore = first.Participants.OfType<PersistenceStore<DemoSettings>>().Single();
        new MainWindowViewModel(first).ToggleCommand.Execute(StatusTickerParticipant.FeatureId);
        Assert.IsType<OperationOutcome.Completed>(await firstStore.SaveImmediate());
        await first.ShutdownAsync();
        Assert.True(File.Exists(settingsPath));

        // Second boot, same path, REAL root and runner: restore-then-start is the phase-3
        // registration order — the store loads first, the ticker applies the restored flag.
        var second = await BootAsync(new CompositionRoot { SettingsPathFactory = () => settingsPath });
        var store2 = second.Participants.OfType<PersistenceStore<DemoSettings>>().Single();
        var ticker2 = second.Participants.OfType<StatusTickerParticipant>().Single();
        Assert.IsType<LoadOutcome.Loaded>(store2.LastLoadOutcome);
        Assert.True(store2.Current.StatusTickerEnabled);
        Assert.True(ticker2.IsOperationLive); // the operation restarted from the restored flag
        var vm2 = new MainWindowViewModel(second);
        Assert.True(vm2.TickerLit); // the ring is lit from the operation, phase 4, no toggle needed

        var ticks = ticker2.TickCount;
        await TestWait.Until(() => ticker2.TickCount > ticks, "the restored ticker advances (500ms real interval)", () => $"ticks={ticker2.TickCount}", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ticker2.TickCount > ticks);

        // Teardown mid-operation: the owned completion terminates typed Cancelled.
        var completion = ticker2.Completion!;
        await second.ShutdownAsync();
        Assert.IsType<OperationOutcome.Cancelled>(await completion);
    }

    [Fact]
    public async Task CapabilitySurface_PriorIntegrationProofs_StillIntact()
    {
        var (root, _, _) = RealRoot();
        var host = await BootAsync(root);
        Assert.NotNull(host.Capabilities);
        foreach (var name in host.Capabilities!.Names)
        {
            // Probed, never left "not-probed" — the SP-006 capability surface survives intact.
            if (host.Capabilities.GetState(name) is CapabilityState.Unavailable unavailable)
            {
                Assert.NotEqual("not-probed", unavailable.Reason.Code);
            }
        }

        await host.ShutdownAsync();
    }

    [Fact]
    public void DemoAsset_StreamOpens_FromCompiledResources()
    {
        // avares:// stream test (validation doc item 6): the asset compiled into the binary
        // opens as a real stream with a PNG signature — not markup presence.
        var loader = new StandardAssetLoader(typeof(MainWindow).Assembly);
        using var stream = loader.Open(new Uri("avares://CcpClient.Desktop/Assets/demo-status-ticker.png"));
        Assert.NotNull(stream);
        var header = new byte[8];
        var read = stream.Read(header, 0, header.Length);
        Assert.Equal(8, read);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header); // PNG magic
    }
}
