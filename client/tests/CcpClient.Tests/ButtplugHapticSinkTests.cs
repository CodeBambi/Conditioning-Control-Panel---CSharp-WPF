using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Haptics;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// The Buttplug route, driven END TO END through the real <c>ButtplugClient</c> against a server
/// speaking the captured protocol.
///
/// <para>The seam <see cref="IButtplugSession"/> exists so the sink's policy is testable, but a fact
/// that only ever drove a fake session would prove the policy and nothing about whether this port
/// calls the library correctly. So the facts below go through <see cref="RealButtplugSession"/> and
/// a real WebSocket, and the assertions are about what the SERVER received.</para>
///
/// <para>What is NOT claimed: that a motor moved. Every fact here observes a server's view of a
/// command. Hardware is a device gate a human reports.</para>
/// </summary>
public sealed class ButtplugHapticSinkTests
{
    private static ButtplugHapticSink SinkFor(ButtplugToyServer server) =>
        new(_ => { }, server.ServerUrl);

    [Fact]
    public async Task ConnectingToARunningServerWithADevice_IsAvailableAndEnumeratesIt()
    {
        using var server = new ButtplugToyServer();
        using var sink = SinkFor(server);

        var connect = await sink.ConnectAsync(TestContext.Current.CancellationToken);

        Assert.IsType<CapabilityState.Available>(connect);
        var observation = await sink.ObserveAsync(TestContext.Current.CancellationToken);
        Assert.True(observation.Confirmed);
        Assert.Equal([ButtplugToyServer.DeviceKey], observation.DeviceKeys);
    }

    /// <summary>
    /// The level is passed through UNQUANTIZED and the library maps it onto the feature's own step
    /// range. This is the fact that pins the difference from the Lovense route, where the sink must
    /// quantize because there is no client library to do it.
    /// </summary>
    [Fact]
    public async Task TheLevelIsPassedThroughAndTheLIBRARYQuantizesItOntoTheFeatureRange()
    {
        using var server = new ButtplugToyServer();
        using var sink = SinkFor(server);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var outcome = await sink.SetOutputsAsync(
            ButtplugToyServer.DeviceKey,
            [new HapticOutput(0, HapticLevel.Of(0.75))],
            TestContext.Current.CancellationToken);

        Assert.IsType<CapabilityState.Available>(outcome);

        // 0.75 of a [0,20] range is 15, computed by the LIBRARY and observed on the wire. If this
        // sink had rounded first, the value here would be the sink's arithmetic rather than the
        // device's own resolution.
        Assert.Equal([15], server.VibrateValues);
    }

    /// <summary>Every vibrate feature on the device is driven, not just the first. Upstream fans the
    /// same intensity across all of them (<c>ButtplugProvider.cs:264-278</c>).</summary>
    [Fact]
    public async Task EveryVibrateFeatureIsDriven_NotOnlyTheFirst()
    {
        using var server = new ButtplugToyServer(vibrateFeatures: 3);
        using var sink = SinkFor(server);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        await sink.SetOutputsAsync(
            ButtplugToyServer.DeviceKey,
            [new HapticOutput(0, HapticLevel.Of(1.0))],
            TestContext.Current.CancellationToken);

        Assert.Equal([20, 20, 20], server.VibrateValues);
    }

    /// <summary>The strongest actuator wins. This route addresses one device per command, so a
    /// multi-actuator request is reduced rather than truncated to whichever entry came first.</summary>
    [Fact]
    public async Task AMultiActuatorRequestSendsTheStrongestLevel()
    {
        using var server = new ButtplugToyServer();
        using var sink = SinkFor(server);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        await sink.SetOutputsAsync(
            ButtplugToyServer.DeviceKey,
            [
                new HapticOutput(0, HapticLevel.Of(0.10)),
                new HapticOutput(1, HapticLevel.Of(1.00)),
                new HapticOutput(2, HapticLevel.Of(0.25)),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal([20], server.VibrateValues);
    }

    /// <summary>A stop reaches the device on the wire. This is the fact the teardown contract rests
    /// on for this route.</summary>
    [Fact]
    public async Task StopAllPutsAStopCommandOnTheWireForEveryDeviceTheServerKnows()
    {
        using var server = new ButtplugToyServer();
        using var sink = SinkFor(server);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);
        await sink.SetOutputsAsync(
            ButtplugToyServer.DeviceKey,
            [new HapticOutput(0, HapticLevel.Of(0.5))],
            TestContext.Current.CancellationToken);

        var stop = await sink.StopAllAsync();

        Assert.IsType<CapabilityState.Available>(stop);
        Assert.Equal(1, server.StopCommands);
    }

    /// <summary>
    /// A device that is present but advertises nothing that vibrates is DEGRADED, not Available and
    /// not a refusal. Reporting it as Available would claim a level was delivered to a motor that
    /// does not exist.
    /// </summary>
    [Fact]
    public async Task ADeviceWithNoVibrateFeature_IsDegradedRatherThanAClaimedSuccess()
    {
        using var server = new ButtplugToyServer(vibrateFeatures: 0);
        using var sink = SinkFor(server);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var outcome = await sink.SetOutputsAsync(
            ButtplugToyServer.DeviceKey,
            [new HapticOutput(0, HapticLevel.Of(0.9))],
            TestContext.Current.CancellationToken);

        var degraded = Assert.IsType<CapabilityState.Degraded>(outcome);
        Assert.Equal(HapticReasonCodes.HapticNoDevice, degraded.Reason.Code);
        Assert.Empty(server.VibrateValues);
    }

    /// <summary>A server that answers with no device is DEGRADED — the client half works. Different
    /// from a server that is not running, and collapsing them would tell a user to start an
    /// application that is already up.</summary>
    [Fact]
    public async Task AServerAnsweringWithNoDevice_IsDegradedNotUnreachable()
    {
        using var server = new ButtplugToyServer(withDevice: false);
        using var sink = SinkFor(server);

        var connect = await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var degraded = Assert.IsType<CapabilityState.Degraded>(connect);
        Assert.Equal(HapticReasonCodes.HapticNoDevice, degraded.Reason.Code);
    }

    /// <summary>No Intiface running is the ORDINARY case for a user who owns no toy. Typed refusal
    /// naming the separate program, never an exception and never "no device found".</summary>
    [Fact]
    public async Task NoServerAnswering_IsATypedRefusalNamingTheSeparateProgram()
    {
        using var sink = new ButtplugHapticSink(_ => { }, ButtplugToyServer.ReserveAndReleaseUrl());

        var connect = await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(connect);
        Assert.Equal(HapticReasonCodes.HapticServerUnreachable, unavailable.Reason.Code);
        Assert.Contains("Intiface Central or Intiface Engine", unavailable.Reason.Detail, StringComparison.Ordinal);
        Assert.Contains("not \"no device found\"", unavailable.Reason.Detail, StringComparison.Ordinal);
    }

    /// <summary>An unknown device key is typed rather than thrown: the limb hands back keys it was
    /// given, and a server that dropped a device between enumeration and command must not fault the
    /// schedule path.</summary>
    [Fact]
    public async Task AnUnknownDeviceKeyIsTyped_NotThrown()
    {
        using var server = new ButtplugToyServer();
        using var sink = SinkFor(server);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        var outcome = await sink.SetOutputsAsync(
            "no-such-device", [new HapticOutput(0, HapticLevel.Of(0.5))], TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(HapticReasonCodes.HapticDeviceUnknown, unavailable.Reason.Code);
    }

    [Fact]
    public async Task ADisposedSinkRefusesTyped_RatherThanThrowingOnATeardownRace()
    {
        using var server = new ButtplugToyServer();
        var sink = SinkFor(server);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);
        sink.Dispose();

        var outcome = await sink.SetOutputsAsync(
            ButtplugToyServer.DeviceKey,
            [new HapticOutput(0, HapticLevel.Of(0.5))],
            TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(outcome);
        Assert.Equal(HapticReasonCodes.HapticSinkDisposed, unavailable.Reason.Code);
    }

    [Fact]
    public async Task AnEmptyOutputListIsACallerError_NeverASilentStop()
    {
        using var server = new ButtplugToyServer();
        using var sink = SinkFor(server);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sink.SetOutputsAsync(ButtplugToyServer.DeviceKey, [], TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Teardown completes, and it completes WITHOUT spending the disconnect budget — which is the
    /// difference between a fix and a timeout wearing one's clothes.
    ///
    /// <para>This route's whole suite once passed while every fact in it took almost exactly the
    /// budget: the disconnect was deadlocked and the budget was quietly paying for it. Asserting
    /// only "dispose returned" would have been green then too. So the assertion is on the LOG: a
    /// healthy peer on loopback must never make this line appear.</para>
    /// </summary>
    [Fact]
    public async Task TeardownCompletes_WithoutSpendingTheDisconnectBudget()
    {
        using var server = new ButtplugToyServer();
        var log = new List<string>();
        var sink = new ButtplugHapticSink(m => { lock (log) { log.Add(m); } }, server.ServerUrl);
        await sink.ConnectAsync(TestContext.Current.CancellationToken);

        sink.Dispose();

        lock (log)
        {
            Assert.DoesNotContain(log, m => m.Contains("teardown budget", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Choke-point guard: EVERY await of the Buttplug library inside <c>RealButtplugSession</c> goes
    /// through its <c>DetachedAsync</c> helper.
    ///
    /// <para><b>Why a source guard and not only a behavioural one.</b> The library completes its
    /// promises from inside its own read loop, on a completion source with no
    /// <c>RunContinuationsAsynchronously</c>, so a direct <c>await</c> resumes this port's code ON
    /// that reader — the one thread the connector's disconnect later waits for. The failure that
    /// causes is a HANG, not a red: reverting the helper does not fail this suite, it stops it
    /// finishing. A guard is the only shape that turns that back into a message with a file and a
    /// line in it.</para>
    ///
    /// <para>Idiom follows <c>DataRootChokePointGuardTests.cs:26-56</c> — a guard never skips, and
    /// an unresolvable root fails rather than passes vacuously.</para>
    /// </summary>
    [Fact]
    public void EveryButtplugLibraryAwaitGoesThroughTheDetachHelper()
    {
        var session = Path.Combine(
            FindRepoRoot(), "client", "src", "CcpClient.Desktop", "Haptics", "RealButtplugSession.cs");
        Assert.True(File.Exists(session), $"RealButtplugSession.cs not found at {session} — this guard refuses to skip");

        string[] forbidden = ["await _client.", "await device.", "await feature."];
        var lines = File.ReadAllLines(session);
        var violations = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var token in forbidden)
            {
                if (lines[i].Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"RealButtplugSession.cs:{i + 1}: '{token}' awaits the library directly — "
                        + "wrap it in DetachedAsync(...) or this port's continuation runs on the library's "
                        + "reader thread and teardown deadlocks against it");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "buttplug reader-thread guard violations:" + Environment.NewLine + string.Join(Environment.NewLine, violations));

        // The helper must still be there to be routed through. Asserting only the absence of direct
        // awaits would pass on a file that had deleted both.
        var source = string.Join(Environment.NewLine, lines);
        Assert.Contains("private static async Task DetachedAsync(Task libraryCall)", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Yield();", source, StringComparison.Ordinal);
    }

    /// <summary>Repo-root walk. Precedent: <c>DataRootChokePointGuardTests.cs</c>.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "client", "CcpClient.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"repo root not found walking up from {AppContext.BaseDirectory} — the guard refuses to skip");
    }
}
