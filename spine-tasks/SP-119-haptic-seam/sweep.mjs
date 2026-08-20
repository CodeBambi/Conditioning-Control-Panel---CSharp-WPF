#!/usr/bin/env node
// SP-119 mutation sweep.
//
// Lives inside this packet's folder and writes ONLY inside it (SP-112's rule, after a previous
// wave's driver wrote three levels above its own root into the shared checkout).
//
// Line endings: the working tree is CRLF and the needles below are LF, so every needle is
// normalised for MATCHING and the mutant is written back in the file's OWN endings. SP-112 lost 27
// of its hardest cases to exactly this, and a sweep that silently skips is worse than no sweep.
//
// The false-clean channels, named because SP-117's record obliges it. `runSuite` decides CAUGHT
// from a NON-ZERO EXIT CODE, and `dotnet test` exits non-zero for reasons that are not a failing
// assertion: a mutant that does not COMPILE, a `--filter` that matches no test, a crashed host, or
// the timeout. `compiles()` closes the first by building the product project BEFORE the suite runs
// and reporting NOT COMPILED as its own outcome. The others are unclosed and are named in
// record.md; every round's log shows a non-zero passing count from the same filters, so no filter
// here matched zero tests.
//
// Usage: node spine-tasks/SP-119-haptic-seam/sweep.mjs [--only M-a,M-b] [--round N] [--match-only]

import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(here, "..", "..");

const SRC = "client/src/CcpClient.Desktop";
const F = {
  seam: `${SRC}/Haptics/IHapticSink.cs`,
  factory: `${SRC}/Haptics/HapticSinkFactory.cs`,
  sink: `${SRC}/Haptics/UnadmittedHapticSink.cs`,
  gate: `${SRC}/Haptics/HapticGate.cs`,
  doc: `${SRC}/Haptics/HapticSettingsDocument.cs`,
  part: `${SRC}/Haptics/HapticParticipant.cs`,
  notices: `${SRC}/Views/Pages/HapticsPanelNotices.cs`,
  page: `${SRC}/Views/Pages/StudioPage.axaml.cs`,
  shell: `${SRC}/Views/MainWindow.axaml.cs`,
  root: `${SRC}/Lifecycle/CompositionRoot.cs`,
};

// This packet's own facts plus every landed suite that consumes a symbol it touched (the
// composition root's participant list, the real-root integration proof, the capability registry's
// name list and the scheduler's own composition facts).
const UNIT =
  "FullyQualifiedName~HapticCapabilityTests|" +
  "FullyQualifiedName~HapticGateTests|" +
  "FullyQualifiedName~HapticParticipantTests|" +
  "FullyQualifiedName~CompositionRootValidationTests|" +
  "FullyQualifiedName~IntegrationProofTests|" +
  "FullyQualifiedName~CapabilityTests|" +
  "FullyQualifiedName~SchedulerModuleTests";

const HEADLESS =
  "FullyQualifiedName~HapticsRowHeadlessTests|FullyQualifiedName~StudioRackHeadlessTests";

/** @type {{id:string,file:string,find:string,replace:string,what:string,suite:"unit"|"headless"}[]} */
const MUTATIONS = [
  // ---- the classification: arm order and every conjunct -------------------------------------
  { id: "M-a", file: F.seam, what: "the ADMISSION arm never fires — a build with no client reports a missing device", suite: "unit",
    find: `        !ClientAdmitted
            ? new CapabilityState.Unavailable(new CapabilityReason(`,
    replace: `        !Asked && !ClientAdmitted
            ? new CapabilityState.Unavailable(new CapabilityReason(` },
  { id: "M-b", file: F.seam, what: "Confirmed drops ClientAdmitted", suite: "unit",
    find: `    public bool Confirmed => Asked && ClientAdmitted && ServerAnswered && DeviceCount >= 1;`,
    replace: `    public bool Confirmed => Asked && ServerAnswered && DeviceCount >= 1;` },
  { id: "M-c", file: F.seam, what: "Confirmed drops Asked", suite: "unit",
    find: `    public bool Confirmed => Asked && ClientAdmitted && ServerAnswered && DeviceCount >= 1;`,
    replace: `    public bool Confirmed => ClientAdmitted && ServerAnswered && DeviceCount >= 1;` },
  { id: "M-d", file: F.seam, what: "Confirmed drops ServerAnswered", suite: "unit",
    find: `    public bool Confirmed => Asked && ClientAdmitted && ServerAnswered && DeviceCount >= 1;`,
    replace: `    public bool Confirmed => Asked && ClientAdmitted && DeviceCount >= 1;` },
  { id: "M-e", file: F.seam, what: "Confirmed drops the device count — a server with no toy is Available", suite: "unit",
    find: `    public bool Confirmed => Asked && ClientAdmitted && ServerAnswered && DeviceCount >= 1;`,
    replace: `    public bool Confirmed => Asked && ClientAdmitted && ServerAnswered;` },
  { id: "M-f", file: F.seam, what: "the NOT-ASKED arm never fires", suite: "unit",
    find: `            : !Asked
                ? new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.NotProbed,`,
    replace: `            : false
                ? new CapabilityState.Unavailable(new CapabilityReason(
                    CapabilityReasonCodes.NotProbed,` },
  { id: "M-g", file: F.seam, what: "the SERVER-UNREACHABLE arm never fires", suite: "unit",
    find: `                : !ServerAnswered
                    ? new CapabilityState.Unavailable(new CapabilityReason(
                        HapticReasonCodes.HapticServerUnreachable,`,
    replace: `                : false
                    ? new CapabilityState.Unavailable(new CapabilityReason(
                        HapticReasonCodes.HapticServerUnreachable,` },
  { id: "M-h", file: F.seam, what: "the NO-DEVICE arm never fires — an empty server is Available", suite: "unit",
    find: `                    : DeviceCount == 0
                        ? new CapabilityState.DependencyMissing(`,
    replace: `                    : DeviceCount < 0
                        ? new CapabilityState.DependencyMissing(` },
  { id: "M-i", file: F.seam, what: "NotAsked claims a client is admitted", suite: "unit",
    find: `        new(false, HapticProviderRoute.None, false, false, []);`,
    replace: `        new(false, HapticProviderRoute.None, true, false, []);` },

  // ---- the level and the output line --------------------------------------------------------
  { id: "M-j", file: F.seam, what: "the level's UPPER clamp is gone", suite: "unit",
    find: `        double.IsNaN(value) ? Silent : new(Math.Clamp(value, 0.0, 1.0));`,
    replace: `        double.IsNaN(value) ? Silent : new(Math.Max(value, 0.0));` },
  { id: "M-k", file: F.seam, what: "the level's LOWER clamp is gone", suite: "unit",
    find: `        double.IsNaN(value) ? Silent : new(Math.Clamp(value, 0.0, 1.0));`,
    replace: `        double.IsNaN(value) ? Silent : new(Math.Min(value, 1.0));` },
  { id: "M-l", file: F.seam, what: "a NaN level is passed through instead of silenced", suite: "unit",
    find: `        double.IsNaN(value) ? Silent : new(Math.Clamp(value, 0.0, 1.0));`,
    replace: `        double.IsNaN(value) ? new(value) : new(Math.Clamp(value, 0.0, 1.0));` },
  { id: "M-m", file: F.seam, what: "IsSilent stops calling zero silent", suite: "unit",
    find: `    public bool IsSilent => Value <= 0.0;`,
    replace: `    public bool IsSilent => Value < 0.0;` },
  { id: "M-n", file: F.seam, what: "a NEGATIVE actuator index is accepted", suite: "unit",
    find: `    public int ActuatorIndex { get; } = ActuatorIndex >= 0`,
    replace: `    public int ActuatorIndex { get; } = ActuatorIndex >= int.MinValue` },

  // ---- the factory: admission, and the two routes -------------------------------------------
  { id: "M-o", file: F.factory, what: "a route is ADMITTED with no client behind it", suite: "unit",
    find: `    public static IReadOnlyList<HapticProviderRoute> AdmittedRoutes { get; } = [];`,
    replace: `    public static IReadOnlyList<HapticProviderRoute> AdmittedRoutes { get; } = [HapticProviderRoute.Buttplug];` },
  { id: "M-p", file: F.factory, what: "the selection manufactures a no-op for an admitted route", suite: "unit",
    find: `        return admittedRoutes.Count == 0`,
    replace: `        return admittedRoutes.Count >= 0` },
  { id: "M-q", file: F.factory, what: "the gap stops saying it is NOT a missing device", suite: "unit",
    find: `        "this build admits no haptic provider client, so nothing was attempted. THIS IS NOT \\"no device found\\": "`,
    replace: `        "this build admits no haptic provider client, so nothing was attempted. No devices found: "` },
  { id: "M-r", file: F.factory, what: "the gap names only ONE provider — the packet's central trap", suite: "unit",
    find: `        + "over HTTP to http://127.0.0.1:20010 into Lovense Connect or Lovense Remote "`,
    replace: `        + "over a second connection "` },
  { id: "M-s", file: F.factory, what: "both routes get the SAME description", suite: "unit",
    find: `        HapticProviderRoute.Lovense =>
            "Lovense Connect / Lovense Remote needs NO HAPTICS-SPECIFIC package at all: the shipping provider's "`,
    replace: `        HapticProviderRoute.Lovense =>
            "Buttplug 5.0.1 needs NO HAPTICS-SPECIFIC package at all: the shipping provider's "` },
  { id: "M-t", file: F.factory, what: "the device gate stops saying it is downstream of admission", suite: "unit",
    find: `        "MANUAL GATE (undischarged, and it CANNOT be attempted until a provider client is admitted): "`,
    replace: `        "MANUAL GATE (undischarged): "` },
  { id: "M-u", file: F.factory, what: "CreateFor ignores the route it was asked for", suite: "unit",
    find: `                AdmissionGap + " " + DescribeRoute(route));`,
    replace: `                AdmissionGap + " " + DescribeRoute(HapticProviderRoute.None));` },

  // ---- the refusing sink --------------------------------------------------------------------
  { id: "M-v", file: F.sink, what: "the ALL-STOP reports success for having stopped nothing", suite: "unit",
    find: `    public Task<CapabilityState> StopAllAsync()
    {
        RefusedCalls++;
        return Task.FromResult(Refuse());
    }`,
    replace: `    public Task<CapabilityState> StopAllAsync()
    {
        RefusedCalls++;
        return Task.FromResult<CapabilityState>(new CapabilityState.Available("stopped"));
    }` },
  { id: "M-w", file: F.sink, what: "CONNECT reports success", suite: "unit",
    find: `    public Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefusedCalls++;
        return Task.FromResult(Refuse());
    }`,
    replace: `    public Task<CapabilityState> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefusedCalls++;
        return Task.FromResult<CapabilityState>(new CapabilityState.Available("connected"));
    }` },
  { id: "M-x", file: F.sink, what: "a DISPOSED sink answers with the admission gap instead of the disposal", suite: "unit",
    find: `        LastOutcome = _disposed`,
    replace: `        LastOutcome = false` },
  { id: "M-y", file: F.sink, what: "SetOutputs stops validating its key and its list", suite: "unit",
    find: `        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(outputs);
        if (outputs.Count == 0)`,
    replace: `        if (outputs.Count == 0)` },
  // Added at code review with the guard it covers: the interface documented that an empty list is a
  // caller error and nothing enforced it, and the fact that passed [] observed the KEY's throw.
  { id: "M-bj", file: F.sink, what: "an EMPTY output list is a silent no-op after all", suite: "unit",
    find: `        if (outputs.Count == 0)
        {`,
    replace: `        if (outputs.Count < 0)
        {` },
  { id: "M-z", file: F.sink, what: "ObserveAsync claims a confirmed server", suite: "unit",
    find: `        return Task.FromResult(HapticServerObservation.NotAsked);`,
    replace: `        return Task.FromResult(new HapticServerObservation(true, HapticProviderRoute.Buttplug, true, true, ["x:0"]));` },
  { id: "M-aa", file: F.sink, what: "the sink claims a ROUTE it does not speak", suite: "unit",
    find: `    public HapticProviderRoute Route => HapticProviderRoute.None;`,
    replace: `    public HapticProviderRoute Route => HapticProviderRoute.Buttplug;` },

  // ---- the premium gate ---------------------------------------------------------------------
  { id: "M-ab", file: F.gate, what: "the bar is TIER 2 instead of upstream's tier 1", suite: "unit",
    find: `    public const EntitlementTier RequiredTier = EntitlementTier.Supporter;`,
    replace: `    public const EntitlementTier RequiredTier = EntitlementTier.Lab;` },
  { id: "M-ac", file: F.gate, what: "\"I could not tell\" renders as \"you are not a patron\" — THE rule", suite: "unit",
    find: `            reason => new HapticGateDecision.RefusedUnverified(reason.Code, UnverifiedMessage(reason.Code)));`,
    replace: `            reason => new HapticGateDecision.RefusedNotEntitled(TierRefusalMessage));` },
  { id: "M-ad", file: F.gate, what: "an authority's real refusal renders as an unknown", suite: "unit",
    find: `            _ => new HapticGateDecision.RefusedNotEntitled(TierRefusalMessage),`,
    replace: `            _ => new HapticGateDecision.RefusedUnverified(
                EntitlementReasonCodes.TierAuthorityFault,
                UnverifiedMessage(EntitlementReasonCodes.TierAuthorityFault)),` },
  { id: "M-ae", file: F.gate, what: "an UNDEFINED tier reaches the comparison and can open the door", suite: "unit",
    find: `            tier => !Enum.IsDefined(tier)`,
    replace: `            tier => Enum.IsDefined(tier) && false` },
  { id: "M-af", file: F.gate, what: "an authored unknown-reason sentence inherits the refusal wording", suite: "unit",
    find: `        EntitlementReasonCodes.TierAuthorityAbsent =>
            "This build has no entitlement authority configured, so your tier cannot be looked up. That is a gap "
            + "in the port, not a finding about your account.",`,
    replace: `        EntitlementReasonCodes.TierAuthorityAbsent => DeniedMessage,` },
  { id: "M-ag", file: F.gate, what: "the could-not-verify footer is dropped", suite: "unit",
    find: `        CouldNotVerifyHeader + "\\n" + Explain(reasonCode) + "\\n" + CouldNotVerifyFooter;`,
    replace: `        CouldNotVerifyHeader + "\\n" + Explain(reasonCode);` },
  { id: "M-ah", file: F.gate, what: "the refusal drops WPF's own message", suite: "unit",
    find: `    public static string TierRefusalMessage { get; } = DeniedMessage + "\\n" + UpgradeRoute;`,
    replace: `    public static string TierRefusalMessage { get; } = UpgradeRoute;` },

  // ---- the persisted setting ----------------------------------------------------------------
  { id: "M-ai", file: F.doc, what: "haptics SHIPS ENABLED", suite: "unit",
    find: `    public bool Enabled { get; set; }`,
    replace: `    public bool Enabled { get; set; } = true;` },

  // ---- the participant: phase 3, the gate's two writes, the transition, teardown -------------
  { id: "M-aj", file: F.part, what: "phase 3 connects even with NO route admitted", suite: "unit",
    find: `        if (Sink.Route == HapticProviderRoute.None)
        {`,
    replace: `        if (false)
        {` },
  { id: "M-ak", file: F.part, what: "a REFUSED tick writes the setting anyway — upstream's order reversed", suite: "unit",
    find: `        if (Gate is not HapticGateDecision.Allow)
        {
            return Gate;
        }

        _store.Mutate(document => document.Enabled = true);`,
    replace: `        _store.Mutate(document => document.Enabled = true);
        if (Gate is not HapticGateDecision.Allow)
        {
            return Gate;
        }
` },
  { id: "M-al", file: F.part, what: "switching OFF is gated too — a lapsed pledge traps a running toy", suite: "unit",
    find: `        if (!wanted)
        {
            _store.Mutate(document => document.Enabled = false);
            return Gate;
        }`,
    replace: `        if (!wanted && Gate is HapticGateDecision.Allow)
        {
            _store.Mutate(document => document.Enabled = false);
            return Gate;
        }` },
  { id: "M-am", file: F.part, what: "the gate CLOSING no longer stops anything", suite: "unit",
    find: `        if (wasOpen && !OutputAllowed)
        {`,
    replace: `        if (wasOpen && OutputAllowed)
        {` },
  { id: "M-an", file: F.part, what: "every gate apply all-stops, not only the transition", suite: "unit",
    find: `        if (wasOpen && !OutputAllowed)
        {`,
    replace: `        if (!OutputAllowed)
        {` },
  { id: "M-ao", file: F.part, what: "the all-stop's ONE-SHOT latch is gone", suite: "unit",
    find: `        Interlocked.Exchange(ref _shutdownStopped, 1) != 0 ? Task.CompletedTask : RunAllStopAsync();`,
    replace: `        RunAllStopAsync();` },
  { id: "M-ap", file: F.part, what: "the dot is read off the CHECKBOX — the reachability conjunct is gone", suite: "unit",
    find: `        Enabled && LastObservation is { Confirmed: true } ? EffectDotState.Armed : EffectDotState.Off;`,
    replace: `        Enabled ? EffectDotState.Armed : EffectDotState.Off;` },
  { id: "M-aq", file: F.part, what: "the dot claims LIVE — something is being sent, and nothing is (D179)", suite: "unit",
    find: `        Enabled && LastObservation is { Confirmed: true } ? EffectDotState.Armed : EffectDotState.Off;`,
    replace: `        Enabled && LastObservation is { Confirmed: true } ? EffectDotState.Live : EffectDotState.Off;` },
  { id: "M-ar", file: F.part, what: "the gate is PERMISSIVE before phase 3", suite: "unit",
    find: `    public bool OutputAllowed => Gate is HapticGateDecision.Allow;`,
    replace: `    public bool OutputAllowed => Gate is not HapticGateDecision.RefusedNotEntitled;` },
  { id: "M-as", file: F.part, what: "an authority's exception MESSAGE is carried instead of its type name", suite: "unit",
    find: `                    "the entitlement lookup failed: " + ex.GetType().Name));`,
    replace: `                    "the entitlement lookup failed: " + ex.Message));` },
  { id: "M-at", file: F.part, what: "teardown never disposes the sink", suite: "unit",
    find: `        Sink.Dispose();

        if (!_running)`,
    replace: `        if (!_running)` },
  // Added at code review. The early return used to sit ABOVE the disposal, so a participant that
  // never started leaked its sink — reachable because this one is registered LAST and any earlier
  // participant's phase-3 failure leaves it constructed and un-started.
  { id: "M-bk", file: F.part, what: "the sink is released ONLY on the started path (the leak)", suite: "unit",
    find: `        await ShutdownStopAsync().ConfigureAwait(false);

        // Then release the sink WHATEVER happened`,
    replace: `        if (!_running) { return; }
        await ShutdownStopAsync().ConfigureAwait(false);

        // Then release the sink WHATEVER happened` },
  // And the ORDER, which is upstream's fixed defect: an all-stop that arrives after the provider is
  // torn down reaches nothing.
  { id: "M-bl", file: F.part, what: "the sink is DISPOSED before the all-stop reaches it", suite: "unit",
    find: `        await ShutdownStopAsync().ConfigureAwait(false);

        // Then release the sink WHATEVER happened`,
    replace: `        Sink.Dispose();
        await ShutdownStopAsync().ConfigureAwait(false);

        // Then release the sink WHATEVER happened` },
  { id: "M-au", file: F.part, what: "the connect attempt is not counted", suite: "unit",
    find: `        ConnectAttempts++;
        LastConnectOutcome = await Sink.ConnectAsync(cancellationToken).ConfigureAwait(false);`,
    replace: `        LastConnectOutcome = await Sink.ConnectAsync(cancellationToken).ConfigureAwait(false);` },
  { id: "M-av", file: F.part, what: "SinkState ignores what the server actually said", suite: "unit",
    find: `    public CapabilityState SinkState => (LastObservation ?? HapticServerObservation.NotAsked).Classify();`,
    replace: `    public CapabilityState SinkState => HapticServerObservation.NotAsked.Classify();` },

  // ---- the composition root -----------------------------------------------------------------
  { id: "M-aw", file: F.root, what: "the ALL-STOP is not in the reserved pre-drain HEAD slot", suite: "unit",
    find: `                    if (haptics is not null) await haptics.ShutdownStopAsync().ConfigureAwait(false);
                    if (store is not null) await store.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);`,
    replace: `                    if (store is not null) await store.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);` },
  { id: "M-ax", file: F.root, what: "the haptic setting never flushes at teardown", suite: "unit",
    find: `                    if (haptics is not null) await haptics.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);`,
    replace: `                    if (haptics is null) await haptics!.FlushAsync(DefaultFlushTimeout).ConfigureAwait(false);` },
  { id: "M-ay", file: F.root, what: "the capability is never registered — the System page cannot report it", suite: "unit",
    find: `        if (haptics is not null)
        {
            capabilities.Register(HapticCapabilityName, async token =>`,
    replace: `        if (haptics is null)
        {
            capabilities.Register(HapticCapabilityName, async token =>` },
  { id: "M-az", file: F.root, what: "the participant gets NO entitlement authority", suite: "unit",
    find: `                _entitlementForParticipants is { } entitlement ? entitlement.ResolveAsync : null),`,
    replace: `                null),` },
  // Round 1 reported M-ba as NOT COMPILED, which is a fault in the DRIVER and not a finding about
  // the code: the replacement was not type-preserving. Re-stated as a swap for another participant
  // whose constructor really does type-check here, so the mutation is "the tenth participant is
  // something else" rather than "the file no longer builds".
  { id: "M-ba", file: F.root, what: "the participant is not registered at all", suite: "unit",
    find: `            new Haptics.HapticParticipant(
                infra, Path.GetDirectoryName(SettingsPathFactory())!,
                sink: null,
                _entitlementForParticipants is { } entitlement ? entitlement.ResolveAsync : null),`,
    replace: `            new HeartbeatParticipant(infra.OwnerFor("HeartbeatTwo"), infra.UiDispatch),` },

  // ---- the shell and the page ---------------------------------------------------------------
  { id: "M-bb", file: F.shell, what: "the shell builds its OWN haptic owner instead of the root's", suite: "headless",
    find: `        Haptics = host.Participants.OfType<Haptics.HapticParticipant>().FirstOrDefault()`,
    replace: `        Haptics = host.Participants.OfType<Haptics.HapticParticipant>().LastOrDefault(_ => false)` },
  { id: "M-bc", file: F.page, what: "the enable box is not re-synced from the document after a refusal", suite: "headless",
    find: `        _haptics.RequestEnable(target);
        LoadDialsFromPreset();
        Refresh();
    }

    /// <summary>The scheduler panel's Enable box`,
    replace: `        _haptics.RequestEnable(target);
        Refresh();
    }

    /// <summary>The scheduler panel's Enable box` },
  { id: "M-bd", file: F.page, what: "the row's LEFT click also flips the switch", suite: "headless",
    find: `        if (e.InitialPressMouseButton != MouseButton.Right)
        {
            return;
        }

        e.Handled = true;
        _haptics.RequestEnable(!_haptics.Enabled);`,
    replace: `        e.Handled = true;
        _haptics.RequestEnable(!_haptics.Enabled);` },
  { id: "M-be", file: F.page, what: "the panel never opens", suite: "headless",
    find: `        HapticsModulePanel.IsVisible = hapticsOpen;`,
    replace: `        HapticsModulePanel.IsVisible = false;` },
  { id: "M-bf", file: F.page, what: "the dot is painted from a CONSTANT rather than the module", suite: "headless",
    find: `        RenderedHapticsDot = PaintSchedulerDot(HapticsRowDot, _haptics.Dot);`,
    replace: `        RenderedHapticsDot = PaintSchedulerDot(HapticsRowDot, EffectDotState.Armed);` },
  { id: "M-bg", file: F.page, what: "the GATE line and the SINK line are swapped", suite: "headless",
    find: `        HapticsGateState.Text = HapticsPanelNotices.DescribeGate(_haptics.Gate);
        HapticsSinkState.Text = HapticsPanelNotices.DescribeSink(_haptics.SinkState);`,
    replace: `        HapticsGateState.Text = HapticsPanelNotices.DescribeSink(_haptics.SinkState);
        HapticsSinkState.Text = HapticsPanelNotices.DescribeGate(_haptics.Gate);` },
  { id: "M-bh", file: F.notices, what: "the absence line drops D179 — no effect sends anything here", suite: "headless",
    find: `        + "than a missing one. And even with a device attached, nothing would move: no effect in this build sends "
        + "anything to haptics yet.";`,
    replace: `        + "than a missing one.";` },
  { id: "M-bi", file: F.notices, what: "the lead line stops saying the other end is another program", suite: "headless",
    find: `        + "to a separate program you install — Intiface Central for Buttplug.io toys, or Lovense Connect / Lovense "`,
    replace: `        + "to a device — Intiface Central for Buttplug.io toys, or Lovense Connect / Lovense "` },
];

function read(rel) {
  return fs.readFileSync(path.join(repo, rel), "utf8");
}

function write(rel, text) {
  fs.writeFileSync(path.join(repo, rel), text);
}

/** Normalise for matching; write back in the file's OWN endings. */
function applyMutation(rel, find, replace) {
  const original = read(rel);
  const crlf = original.includes("\r\n");
  const flat = crlf ? original.replaceAll("\r\n", "\n") : original;
  if (flat.split(find).length - 1 !== 1) {
    return { ok: false, original, hits: flat.split(find).length - 1 };
  }
  const mutated = flat.replace(find, replace);
  write(rel, crlf ? mutated.replaceAll("\n", "\r\n") : mutated);
  return { ok: true, original };
}

/** A mutant the compiler rejects is one no test was ever asked about. */
function compiles() {
  try {
    execFileSync(
      "dotnet",
      ["build", `${SRC}/CcpClient.Desktop.csproj`, "-c", "Debug", "--nologo", "-v", "q"],
      { cwd: repo, stdio: "pipe", encoding: "utf8", timeout: 10 * 60 * 1000 },
    );
    return true;
  } catch {
    return false;
  }
}

function runSuite(suite) {
  const project =
    suite === "headless"
      ? "client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj"
      : "client/tests/CcpClient.Tests/CcpClient.Tests.csproj";
  const filter = suite === "headless" ? HEADLESS : UNIT;
  try {
    const out = execFileSync(
      "dotnet",
      ["test", project, "-c", "Debug", "--nologo", "-v", "q", "--filter", filter],
      { cwd: repo, stdio: "pipe", encoding: "utf8", timeout: 15 * 60 * 1000 },
    );
    return { verdict: "SURVIVED", tail: lastLine(out) };
  } catch (err) {
    return { verdict: "CAUGHT", tail: lastLine(String(err.stdout ?? "")) };
  }
}

function lastLine(text) {
  const lines = text.split(/\r?\n/).filter((l) => l.includes("Failed:") && l.includes("Passed:"));
  return lines.length > 0 ? lines[lines.length - 1].trim() : "<no result line>";
}

const args = process.argv.slice(2);
const onlyArg = args.indexOf("--only");
const only = onlyArg >= 0 ? new Set(args[onlyArg + 1].split(",")) : null;
const roundArg = args.indexOf("--round");
const round = roundArg >= 0 ? args[roundArg + 1] : "1";
// Needle check only: apply and restore every mutation WITHOUT building or running anything, so a
// needle that no longer matches is found in seconds instead of an hour into a round. It reports
// NOT PATCHED exactly as a real round does and it writes no log — it is an instrument for the
// driver, never evidence about the code.
const matchOnly = args.includes("--match-only");

if (matchOnly) {
  let bad = 0;
  for (const m of MUTATIONS) {
    const applied = applyMutation(m.file, m.find, m.replace);
    if (!applied.ok) {
      bad++;
      console.log(`${m.id}  NOT PATCHED (${applied.hits} match(es))  ${m.file}  ${m.what}`);
    } else {
      write(m.file, applied.original);
    }
  }

  const dirty = execFileSync("git", ["status", "--porcelain", "client/src"], {
    cwd: repo,
    encoding: "utf8",
  });
  console.log(
    `match check: ${MUTATIONS.length - bad}/${MUTATIONS.length} needles matched exactly once; ` +
      `tree clean: ${dirty.trim() === "" ? "YES" : "NO — " + dirty}`,
  );
  process.exit(bad === 0 && dirty.trim() === "" ? 0 : 1);
}

const log = [];
let caught = 0;
let survived = 0;
let skipped = 0;
let notCompiled = 0;

for (const m of MUTATIONS) {
  if (only && !only.has(m.id)) {
    continue;
  }

  const applied = applyMutation(m.file, m.find, m.replace);
  if (!applied.ok) {
    skipped++;
    const line = `${m.id}  NOT PATCHED (${applied.hits} match(es))  ${m.file}  ${m.what}`;
    console.log(line);
    log.push(line);
    continue;
  }

  let verdict = "NOT COMPILED";
  let tail = "<not built>";
  try {
    if (compiles()) {
      const run = runSuite(m.suite);
      verdict = run.verdict;
      tail = run.tail;
    }
  } finally {
    write(m.file, applied.original);
  }

  if (verdict === "NOT COMPILED") {
    notCompiled++;
  } else if (verdict === "CAUGHT") {
    caught++;
  } else {
    survived++;
  }

  const line = `${m.id}  ${verdict}  [${m.suite}]  ${m.what}  ||  ${tail}`;
  console.log(line);
  log.push(line);
}

const status = execFileSync("git", ["status", "--porcelain", "client/src"], {
  cwd: repo,
  encoding: "utf8",
});
const clean = status.trim() === "";
const summary =
  `\nround ${round}: ${caught} caught, ${survived} survived, ${skipped} not patched, ` +
  `${notCompiled} not compiled ` +
  `(${caught + survived + skipped + notCompiled} attempted)\n` +
  `tree restored byte-identically: ${clean ? "YES" : "NO — " + status}`;
console.log(summary);
log.push(summary);

fs.writeFileSync(path.join(here, `sweep-round${round}.log`), log.join("\n") + "\n");

if (!clean) {
  process.exitCode = 1;
}
