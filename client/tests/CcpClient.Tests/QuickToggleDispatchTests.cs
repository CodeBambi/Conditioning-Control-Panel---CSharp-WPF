using CcpClient.Desktop;
using CcpClient.Desktop.Features;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Views;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// SP-014 stable-identity quick-toggle dispatch proofs (A-004; the row's core claim).
/// The replaced mechanism: the first attempt's localized <c>switch (card.Title)</c>
/// (CCP.Avalonia SettingsTabView.axaml.cs:382) — dispatch keyed on mutable display text.
/// These tests prove the replacement falsifiably: the stable card ID is the ONLY key,
/// title strings never resolve, mutating the displayed title cannot break dispatch, and
/// both gestures converge on ONE command path. All through the REAL composition root and
/// phase runner — no mocks (contract §10.2 pattern). Toggle assertions route through the
/// command/dispatch only (pre-approach consult: no keyless bypass in the suite).
/// </summary>
public class QuickToggleDispatchTests
{
    private static async Task<(ApplicationHost Host, string SettingsPath)> BootAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-sp014-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(dir, "settings.json");
        var root = new CompositionRoot { SettingsPathFactory = () => settingsPath };
        var trace = new StartupTrace();
        ApplicationHost? host = null;
        var outcome = await StartupPhaseRunner.RunAsync(
            Program.CreateStartupPhases(root, trace, h => host = h), trace, CancellationToken.None);
        Assert.IsType<StartupOutcome.Success>(outcome);
        return (host!, settingsPath);
    }

    [Fact]
    public async Task StableId_Resolves_TogglesRealOperation_AndPersists()
    {
        var (host, settingsPath) = await BootAsync();
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        var store = host.Participants.OfType<PersistenceStore<DemoSettings>>().Single();
        var vm = new MainWindowViewModel(host);

        Assert.False(ticker.IsOperationLive);
        vm.ToggleCommand.Execute(StatusTickerParticipant.FeatureId); // the ONE command path (A-004)

        Assert.True(ticker.IsOperationLive); // live-start (SP-004 owned operation)
        Assert.True(vm.TickerLit); // ring derives from operation liveness, never the flag (SP-007 rule)

        Assert.IsType<OperationOutcome.Completed>(await store.SaveImmediate());
        var json = await File.ReadAllTextAsync(settingsPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"statusTickerEnabled\": true", json); // SP-005 file-content proof

        vm.ToggleCommand.Execute(StatusTickerParticipant.FeatureId);
        Assert.False(ticker.IsOperationLive); // live-stop through the same path

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task TitleMutation_DispatchStillResolves_ViaStableId()
    {
        // THE ROW'S CORE CLAIM (language-switch failure-mode stand-in; the client has no
        // localization system — A-014 honest absence): mutate the card's displayed title
        // (test-only) and prove dispatch still resolves via the stable ID.
        var (host, _) = await BootAsync();
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        var vm = new MainWindowViewModel(host);

        vm.CardTitle = "Geflügelte Kartoffel"; // simulated language switch / rewording
        vm.ToggleCommand.Execute(vm.CardId);
        Assert.True(ticker.IsOperationLive); // dispatch unaffected by the mutated title

        vm.CardTitle = "全然別の表示名"; // second mutation, non-Latin script
        vm.ToggleCommand.Execute(vm.CardId);
        Assert.False(ticker.IsOperationLive); // and back off through the same path

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task TitleStrings_NeverResolve_AsDispatchKeys()
    {
        var (host, _) = await BootAsync();
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        var vm = new MainWindowViewModel(host);

        // The exact first-attempt failure inverted: keying on the DISPLAYED title must not
        // resolve — before AND after a title mutation.
        vm.ToggleCommand.Execute("Demo: Status Ticker");
        Assert.False(ticker.IsOperationLive);

        vm.CardTitle = "Renamed Display Title";
        vm.ToggleCommand.Execute("Renamed Display Title");
        Assert.False(ticker.IsOperationLive);

        // Capitalization/whitespace near-misses never resolve (capability-inventory:
        // dispatch never keys on capitalization or object-title comparison).
        Assert.False(vm.QuickToggle.TryToggle("demo.status-ticker ")); // trailing space
        Assert.False(vm.QuickToggle.TryToggle("Demo.Status-Ticker")); // wrong case
        Assert.False(vm.QuickToggle.TryToggle(null)); // null key: no throw, no toggle
        Assert.False(ticker.IsOperationLive);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task UnknownOrNeutralId_SilentNoOp_WpfElseReturnParity()
    {
        // WPF parity (MainWindow.Presets.cs:818 `else return`): Visuals/System and unknown
        // cards have no dispatch entry — a silent no-op, not an error. Contract-only here:
        // no neutral/locked cards exist in the client (no fake cards synthesized).
        var (host, _) = await BootAsync();
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        var vm = new MainWindowViewModel(host);

        Assert.False(vm.QuickToggle.TryToggle("visuals"));
        Assert.False(vm.QuickToggle.TryToggle("system"));
        Assert.False(vm.QuickToggle.TryToggle("no.such.card"));
        Assert.False(ticker.IsOperationLive);

        await host.ShutdownAsync();
    }

    [Fact]
    public async Task OneCommandPath_GestureAndKeyboardConverge_OnSameOperation()
    {
        // Both gesture surfaces (the right-click PointerPressed handler and the keyboard
        // KeyBinding) execute the SAME command with the SAME stable ID; the dispatch holds
        // exactly one entry — the demonstrator card. No parallel/keyless path exists.
        var (host, _) = await BootAsync();
        var ticker = host.Participants.OfType<StatusTickerParticipant>().Single();
        var store = host.Participants.OfType<PersistenceStore<DemoSettings>>().Single();
        var vm = new MainWindowViewModel(host);

        var entry = Assert.Single(vm.QuickToggle.CardIds);
        Assert.Equal(StatusTickerParticipant.FeatureId, entry);

        // Command surface (what both gestures call): resolves the single shared operation.
        vm.ToggleCommand.Execute(vm.CardId);
        Assert.True(ticker.IsOperationLive);
        Assert.True(store.Current.StatusTickerEnabled);

        // Direct dispatch surface (what the command delegates to): same operation, same store.
        Assert.True(vm.QuickToggle.TryToggle(StatusTickerParticipant.FeatureId));
        Assert.False(ticker.IsOperationLive);
        Assert.False(store.Current.StatusTickerEnabled);

        await host.ShutdownAsync();
    }
}
