using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Capabilities;
using Xunit;

namespace CcpClient.Tests;

/// <summary>
/// <b>The packet's central trap, stated as assertions.</b>
///
/// <para>An earlier wave refused to ship an audio module because "nothing in this project can
/// actually verify that a sound played". A test that asserted <c>Play()</c> returned would be the
/// shape this port has rejected four times: the tray method returning, the overlay flag being set,
/// the fake dial re-imposing its own clamp. So not one fact in this file reads a return value from
/// the product as its evidence. Every one of them asks the <b>Windows audio engine</b>, through
/// <see cref="WasapiRenderProbe"/>'s own independent COM declarations, and compares the OS's answer
/// with the product's claim.</para>
///
/// <para><b>The four links, and each fact says which one it pins:</b></para>
/// <list type="number">
/// <item>the OS reports N active render endpoints;</item>
/// <item>after the port opens a device, the OS holds a render session whose owning process id is
/// this process — one it did not hold a moment earlier;</item>
/// <item>that session's state is <c>AudioSessionStateActive</c>, and it is not after teardown;</item>
/// <item><b>the OS's own peak meter reads a non-zero level on this process's stream while a clip
/// plays, and ZERO on the same stream with the device open and nothing cued.</b></item>
/// </list>
///
/// <para><b>Where the chain stops, and no fact here pretends otherwise.</b> Link 4 proves the
/// Windows audio engine metered non-silent samples from us. It does not prove the endpoint was
/// unmuted, that a DAC converted anything, that speakers were attached, or that a human heard it.
/// Those are a named manual gate (<c>record.md</c>), and a green run here never discharges it.</para>
///
/// <para><b>Why the shape is <c>Assert.Equal(machineFact, productClaim)</c>.</b> A box with no
/// output device is a real machine and the port must be honest there too: both sides of every
/// equality below go false together, so the fact still bites rather than being skipped. That is
/// <see cref="TrayCapabilityTests"/>' discipline and the reason no predicate appears in a fact body.</para>
///
/// <para><b>Why this class is in <see cref="RealRenderDeviceCollection"/>.</b> Links 1-3 are asked
/// of the PROCESS, and Windows folds every default-GUID stream a process opens into one session, so
/// a second real device anywhere in this process answers them. That collection's remarks carry the
/// measurements, the census of who else opens one, and the 9-of-20-to-0-of-20 before and after.</para>
/// </summary>
[Collection(nameof(RealRenderDeviceCollection))]
public class AudioCapabilityTests
{
    // =================================================================================
    //  the earned Available — links 1, 2 and 3
    // =================================================================================

    [Fact]
    public void WithNoDeviceOpen_TheOsReportsNoACTIVERenderSessionForThisProcess()
    {
        // THE NEGATIVE CONTROL FOR THE WHOLE FILE. If this were false the oracle would be
        // certifying a session that exists for some other reason, and every fact below would pass
        // without the product doing anything at all.
        //
        // IT ASSERTS `Active`, NOT `SessionForThisProcess`, AND THE DIFFERENCE IS MEASURED RATHER
        // THAN STYLISTIC (code review, and plan.md §0's raw run): once this process has opened and
        // torn down a device, the OS KEEPS reporting a session for our pid and only drops its STATE
        // to AudioSessionStateInactive. An earlier version of this fact asserted
        // `SessionForThisProcess` and was therefore order-dependent — any xunit ordering that put a
        // lifecycle fact first would have reddened it for a reason that is not a defect. `Active` is
        // false both before the first open and after every teardown, so the control holds whatever
        // order this class runs in, and it is still the control that matters: an already-ACTIVE
        // session for this pid is exactly what would make F2/F3 pass vacuously.
        var before = WasapiRenderProbe.SessionForThisProcess();

        Assert.False(
            before.Active,
            $"the OS already reports an ACTIVE render session for pid {Environment.ProcessId} with no "
            + $"device open in this suite: {before}. {SomethingElseInThisProcessHasTheEndpoint}");
    }

    [Fact]
    public async Task OpenClaimsAvailableEXACTLYWhenTheOsReportsAnActiveRenderSessionForThisProcess()
    {
        var clip = NewClip();
        var run = AudioObservations.FullLifecycle(clip, WaitUntil);

        // The claim and the OS's answer, side by side. Not "Open returned Available" — that is the
        // banned shape. The equality is what makes a presence that shortcut the read-back fail.
        Assert.Equal(run.OsHeldActiveSessionAfterOpen, run.ClaimedAvailableOnOpen);

        // And the machine's own property decides which way both of them go, established by the
        // probe rather than by the product.
        Assert.Equal(run.MachineCanRender, run.ClaimedAvailableOnOpen);
        Assert.Equal(run.MachineCanRender, run.ClaimedRenderingAfterOpen);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task TheAvailableDetailNamesTheApiThatEarnedIt_AndRefusesTheClaimItCannotMake()
    {
        var run = AudioObservations.FullLifecycle(NewClip(), WaitUntil);

        // A detail that said "audio is working" would be a sentence; this one has to name what was
        // asked, which is what a bug report quotes and what a reviewer can check.
        var detail = run.OpenState switch
        {
            CapabilityState.Available a => a.Detail,
            CapabilityState.Unavailable u => u.Reason.Detail,
            CapabilityState.DependencyMissing m => m.Reason.Detail,
            _ => run.OpenState.ToString() ?? string.Empty,
        };

        Assert.Equal(run.MachineCanRender, detail.Contains("IAudioSessionControl2::GetProcessId", StringComparison.Ordinal));
        Assert.Equal(run.MachineCanRender, detail.Contains("AudioSessionStateActive", StringComparison.Ordinal));

        // The ceiling, stated in the product's own words rather than only in a record file.
        Assert.Equal(
            run.MachineCanRender,
            detail.Contains("does NOT mean", StringComparison.Ordinal)
            && detail.Contains("anybody heard it", StringComparison.Ordinal));

        await Task.CompletedTask;
    }

    // =================================================================================
    //  LINK 4 — the strongest fact this machine can produce, with its own negative control
    // =================================================================================

    [Fact]
    public async Task WhileAClipPlays_WindowsMetersANonZeroPeakOnThisProcessesOwnStream()
    {
        var run = AudioObservations.FullLifecycle(NewClip(), WaitUntil);

        // The number does not come from this process. IAudioMeterInformation::GetPeakValue reports
        // what the Windows audio engine measured on the samples it consumed from our stream.
        Assert.Equal(run.MachineCanRender, run.OsMeteredNonZeroPeakWhileRendering);
        Assert.Equal(run.MachineCanRender, run.OsSawActiveSessionWhileRendering);

        // AND THE CONTROL THAT MAKES IT BITE: the same meter, on the same stream, with the device
        // open and started and nothing cued, reads zero. Without this line the fact above would be
        // satisfied by opening a device and playing nothing — which is precisely the "the method
        // returned" evidence this packet exists to refuse.
        Assert.Equal(
            0f,
            run.PeakWithDeviceOpenAndNothingCued);

        Assert.True(
            !run.MachineCanRender || run.PeakWhileRendering > run.PeakWithDeviceOpenAndNothingCued,
            $"the metered peak did not rise once a clip was cued. {run.Evidence}. A machine whose "
            + "output endpoint is muted, or whose session was muted in the volume mixer, is the one "
            + "legitimate way this fails without a product defect — that is a property of this "
            + "machine and it is the honest failure, not a reason to weaken the assertion");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task CueClaimsAvailableEXACTLYWhenTheOsStillConfirmsTheSession_AndSilenceStopsTheSlot()
    {
        var run = AudioObservations.FullLifecycle(NewClip(), WaitUntil);

        Assert.Equal(run.MachineCanRender, run.ClaimedAvailableOnCue);

        // Silence reports Available only because it really had a player to stop. On a machine with
        // no endpoint the cue never produced one, so there is nothing to stop and it refuses —
        // which is the point: a stop that reported success for work it did not do would let a
        // teardown pin read green on a build that never played anything.
        Assert.Equal(run.MachineCanRender, run.ClaimedAvailableOnSilence);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task AfterTeardown_TheOsNoLongerReportsAnActiveRenderSession()
    {
        var run = AudioObservations.FullLifecycle(NewClip(), WaitUntil);

        // Link 3's other half. Measured 1 -> 0 on this machine: the session drops to
        // AudioSessionStateInactive when the device is disposed. Both columns again — the product
        // must stop claiming at the same moment the OS stops confirming.
        //
        // The teardown itself is SYNCHRONOUS and was measured to be: 30 open/cue/dispose cycles with
        // every core saturated by spinning threads reported the session Inactive on the very first
        // read after Dispose, 30 out of 30. So a True here is not a slow teardown and waiting for it
        // would be a lie — it is another live stream, named below.
        Assert.False(
            run.OsSessionStillActiveAfterDispose,
            run.Evidence
            + ". A DISPOSE THAT NO LONGER CLOSES THE DEVICE is the product regression this fact "
            + "exists to catch, and it is the first thing to check. If the presence really did tear "
            + "down, then: " + SomethingElseInThisProcessHasTheEndpoint);
        Assert.False(run.ClaimedRenderingAfterDispose, run.Evidence);

        await Task.CompletedTask;
    }

    // =================================================================================
    //  the read-back is what earns it — the branch that only exists because a call returning is not proof
    // =================================================================================

    [Fact]
    public void ADeviceThatOpensWithoutTheOsConfirmingASession_IsUNAVAILABLE_NotAvailable()
    {
        // A backend whose TryInit succeeds — the state a capability that trusted its own return
        // value would report as working — with a read-back that says the OS holds nothing for us.
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: true),
            _ => { },
            readback: _ => new AudioRenderObservation(
                Asked: true, EndpointName: "Stub Endpoint", SessionOwnedByThisProcess: false,
                SessionActive: false, MeterReadable: false, Peak: 0f, SessionsOnEndpoint: 7),
            endpointCount: () => 1);

        var open = presence.Open();

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(open);
        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, unavailable.Reason.Code);
        Assert.Contains("the native device open succeeded", unavailable.Reason.Detail, StringComparison.Ordinal);
        Assert.False(presence.IsRendering);
        presence.Dispose();
    }

    [Fact]
    public void ASessionTheOsOWNSButReportsINACTIVE_IsUNAVAILABLE_NotAvailable()
    {
        // THE SIBLING CONJUNCT, and it was open until the code review found it.
        //
        // `Confirmed` is `Asked && SessionOwnedByThisProcess && SessionActive`. Every other injected
        // observation in this file moved the last two clauses TOGETHER, and the real device reports
        // both true, so deleting `&& SessionActive` left the whole suite green — a session the OS
        // holds but reports Inactive would have earned Available, in the one capability whose entire
        // point is not trusting its own return value. This is that state, isolated.
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: true),
            _ => { },
            readback: _ => new AudioRenderObservation(
                Asked: true, EndpointName: "Stub Endpoint", SessionOwnedByThisProcess: true,
                SessionActive: false, MeterReadable: true, Peak: 0f, SessionsOnEndpoint: 4),
            endpointCount: () => 1);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(presence.Open());

        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, unavailable.Reason.Code);

        // And the detail takes the GetState branch rather than the no-session-at-all branch: the
        // product carries a bespoke sentence for exactly this case (WasapiAudioPresence.cs), and
        // until now nothing executed it.
        Assert.Contains(
            "owns one whose IAudioSessionControl::GetState is not AudioSessionStateActive",
            unavailable.Reason.Detail,
            StringComparison.Ordinal);
        Assert.DoesNotContain("owns none of them", unavailable.Reason.Detail, StringComparison.Ordinal);

        Assert.False(presence.IsRendering);
        presence.Dispose();
    }

    [Fact]
    public void AnACTIVESessionOwnedByANOTHERProcess_DoesNotEarnAvailable()
    {
        // THE THIRD CONJUNCT, and the mutation sweep found it open: every injected observation moved
        // ownership and liveness TOGETHER, so `Confirmed` dropping `SessionOwnedByThisProcess` left
        // the whole suite green — and a machine playing music in another app would have earned this
        // process an Available. GetProcessId is the entire reason the read-back enumerates sessions
        // instead of asking the endpoint.
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: true),
            _ => { },
            readback: _ => new AudioRenderObservation(
                Asked: true, EndpointName: "Stub Endpoint", SessionOwnedByThisProcess: false,
                SessionActive: true, MeterReadable: true, Peak: 0.9f, SessionsOnEndpoint: 9),
            endpointCount: () => 1);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(presence.Open());

        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, unavailable.Reason.Code);
        // And the detail takes the no-session-for-us branch, not the inactive-session branch.
        Assert.Contains("owns none of them", unavailable.Reason.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("GetState is not", unavailable.Reason.Detail, StringComparison.Ordinal);
        Assert.False(presence.IsRendering);
        presence.Dispose();
    }

    [Fact]
    public void AnObservationThatWasNeverASKED_IsNotConfirmed_WhateverItsOtherFieldsSay()
    {
        // The `Asked` conjunct pinned directly on the record, because the sweep showed it is
        // unreachable through the presence: the only Asked=false value the product produces is
        // AudioRenderObservation.NotAsked, whose other fields are already false, so dropping the
        // clause was invisible. A read-back that reported fields it never measured is exactly the
        // "we did not ask" / "we asked and it was fine" collapse this type exists to prevent.
        var neverAsked = new AudioRenderObservation(
            Asked: false, EndpointName: "Stub", SessionOwnedByThisProcess: true, SessionActive: true,
            MeterReadable: true, Peak: 1f, SessionsOnEndpoint: 3);

        Assert.False(neverAsked.Confirmed);
        Assert.False(AudioRenderObservation.NotAsked.Confirmed);

        // The positive control, so this is not satisfied by Confirmed being false forever.
        Assert.True(new AudioRenderObservation(true, "Stub", true, true, true, 1f, 3).Confirmed);
    }

    [Fact]
    public void ADeviceThatREFUSESToOpen_IsUnavailable_AndCarriesTheBackendsOwnError()
    {
        // The sweep's M-i: keeping the TryInit CALL but ignoring its ANSWER survived, because no
        // fact drove a backend that refuses. An endpoint exists, the native open is really
        // attempted, and it fails — WPF's own #778/#779 class.
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: false),
            _ => { },
            readback: _ => new AudioRenderObservation(true, "Stub", true, true, true, 1f, 3),
            endpointCount: () => 1);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(presence.Open());

        Assert.Equal(AudioReasonCodes.RenderDeviceRefused, unavailable.Reason.Code);
        // The backend's own words survive to the user, never a summary of them.
        Assert.Contains("stub refused", unavailable.Reason.Detail, StringComparison.Ordinal);

        // And a confirming read-back cannot rescue it: the device is not open, so nothing renders.
        Assert.False(presence.IsRendering);
        Assert.IsType<CapabilityState.Unavailable>(presence.Cue(new AudioCue("slot", "a.wav", 1f)));
        presence.Dispose();
    }

    [Fact]
    public void AnEndpointPulledMidSessionStopsRendering_AtTheNextOpenOrCue()
    {
        // A first Open confirms; the output device is then pulled and the OS stops reporting a
        // session for this process. The presence must stop claiming AT THE NEXT ASK, so the modules'
        // dots go dark instead of staying lit on a machine with nothing to play to.
        var confirming = true;
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: true),
            _ => { },
            readback: _ => confirming
                ? new AudioRenderObservation(true, "Stub", true, true, true, 0.5f, 3)
                : new AudioRenderObservation(true, "Stub", false, false, false, 0f, 2),
            endpointCount: () => 1);

        Assert.IsType<CapabilityState.Available>(presence.Open());
        Assert.True(presence.IsRendering);

        confirming = false;
        var unavailable = Assert.IsType<CapabilityState.Unavailable>(presence.Open());

        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, unavailable.Reason.Code);
        Assert.False(presence.IsRendering);
        Assert.False(presence.LastObservation.Confirmed);

        // A SECOND Open is what re-asks. The device is NOT re-initialised (that would stop the other
        // module's clip), so the READ-BACK is the only thing that can notice — which is the whole
        // reason a second Open still costs a round trip.
        //
        // NOTE, and it is a finding rather than a claim: this fact does NOT discriminate the
        // `Remember` clear (mutation M-l). That clear is redundant with the `_deviceUp` conjunct in
        // IsRendering on every path that can reach it, and the sweep proved it — see record.md §3.
        presence.Dispose();
    }

    [Fact]
    public void AReadBackThatWasNeverASKED_IsUNAVAILABLE_AndCannotMasqueradeAsAMeasurement()
    {
        // The FIRST conjunct, isolated for the same reason: `Asked: false` is the Linux/no-mechanism
        // shape, and a presence that treated "we never asked" as "we asked and it was fine" would be
        // the platform-check-produces-Available the contract bans (§2 rule 2).
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: true),
            _ => { },
            readback: _ => AudioRenderObservation.NotAsked,
            endpointCount: () => 1);

        var unavailable = Assert.IsType<CapabilityState.Unavailable>(presence.Open());

        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, unavailable.Reason.Code);
        Assert.False(presence.IsRendering);
        Assert.False(presence.LastObservation.Asked);
        presence.Dispose();
    }

    [Fact]
    public void ACueOnASessionTheOsReportsINACTIVE_IsDEGRADED_NotAvailable()
    {
        // The same sibling conjunct on the CUE path. Open confirms, then the OS drops the session to
        // Inactive while still owning it — the player really starts and nothing may be claimed about
        // it reaching an output.
        var confirmed = new AudioRenderObservation(true, "Stub Endpoint", true, true, true, 0.5f, 3);
        var inactive = new AudioRenderObservation(true, "Stub Endpoint", true, false, true, 0f, 3);
        var calls = 0;
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: true),
            _ => { },
            readback: _ => calls++ == 0 ? confirmed : inactive,
            endpointCount: () => 1);

        Assert.IsType<CapabilityState.Available>(presence.Open());
        var degraded = Assert.IsType<CapabilityState.Degraded>(
            presence.Cue(new AudioCue("slot", "irrelevant.wav", 0.5f)));

        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, degraded.Reason.Code);
        Assert.False(presence.IsRendering);
        presence.Dispose();
    }

    [Fact]
    public void ACueThatStartsWhileTheOsStopsConfirming_IsDEGRADED_NamingWhichHalfHolds()
    {
        // The device came up confirmed and the OS withdrew the session afterwards. One half really
        // holds (a player is on the mixer) and one really does not, which is exactly what Degraded
        // is for — and it must never be reported as a working cue.
        var confirmed = new AudioRenderObservation(true, "Stub Endpoint", true, true, true, 0.5f, 3);
        var withdrawn = new AudioRenderObservation(true, "Stub Endpoint", false, false, false, 0f, 3);
        var calls = 0;
        var presence = new WasapiAudioPresence(
            new StubBackend(initialises: true),
            _ => { },
            readback: _ => calls++ == 0 ? confirmed : withdrawn,
            endpointCount: () => 1);

        Assert.IsType<CapabilityState.Available>(presence.Open());
        var cue = presence.Cue(new AudioCue("slot", "irrelevant.wav", 0.5f));

        var degraded = Assert.IsType<CapabilityState.Degraded>(cue);
        Assert.Equal(AudioReasonCodes.RenderSessionUnconfirmed, degraded.Reason.Code);
        Assert.Contains("attached to the device's mixer", degraded.SurvivingSemantics, StringComparison.Ordinal);
        Assert.False(presence.IsRendering);
        presence.Dispose();
    }

    [Fact]
    public void NoEndpointAtAll_IsDependencyMissing_AndNothingIsEverAttempted()
    {
        var backend = new StubBackend(initialises: true);
        var presence = new WasapiAudioPresence(
            backend, _ => { },
            readback: _ => new AudioRenderObservation(true, null, true, true, true, 1f, 1),
            endpointCount: () => 0);

        var missing = Assert.IsType<CapabilityState.DependencyMissing>(presence.Open());

        Assert.Equal(AudioReasonCodes.NoRenderEndpoint, missing.Reason.Code);
        // Nothing was attempted: the endpoint question is asked BEFORE the device call, so a box
        // with no output never reaches a native init at all.
        Assert.Equal(0, backend.InitAttempts);
        Assert.False(presence.IsRendering);
        presence.Dispose();
    }

    [Fact]
    public void ASecondOpenDoesNotReInitTheDevice_ButDoesAskTheOsAgain()
    {
        // Two modules share one presence, so a mid-session quick-toggle of one must not stop the
        // device out from under the other's playing clip. Re-ASKING is the part worth repeating.
        var backend = new StubBackend(initialises: true);
        var reads = 0;
        var presence = new WasapiAudioPresence(
            backend, _ => { },
            readback: _ => { reads++; return new AudioRenderObservation(true, "Stub", true, true, true, 0f, 2); },
            endpointCount: () => 1);

        presence.Open();
        presence.Open();

        Assert.Equal(1, backend.InitAttempts);
        Assert.Equal(2, reads);
        presence.Dispose();
    }

    [Fact]
    public void CueBeforeOpen_RefusesTyped_AndNoPlayerIsEverBuilt()
    {
        var backend = new StubBackend(initialises: true);
        var presence = new WasapiAudioPresence(backend, _ => { });

        var refusal = Assert.IsType<CapabilityState.Unavailable>(
            presence.Cue(new AudioCue("slot", "irrelevant.wav", 1f)));

        Assert.Equal(AudioReasonCodes.RenderDeviceNotOpen, refusal.Reason.Code);
        Assert.Equal(0, backend.PlayersCreated);
        presence.Dispose();
    }

    /// <summary>
    /// A clip that reaches its own end releases its slot and is disposed — and a LATE end from an
    /// already-displaced player does not touch the clip that replaced it.
    ///
    /// <para>Nothing used to release a finished player. Slots were cleared only by displacement or
    /// by an explicit Silence, so every clip that simply ran out sat in its slot holding a native
    /// player for the rest of the session. It also made "is the slot occupied" useless as a proxy
    /// for "is it playing", which is why IAudioPresence.IsSounding has to ask the player.</para>
    ///
    /// <para>The second half is the race that makes ownership matter: the device thread can report
    /// player A's end AFTER player B has taken the slot. Ownership is the slot, so A's late event
    /// must do nothing rather than evict B.</para>
    /// </summary>
    [Fact]
    public void AClipThatReachesItsEndReleasesItsSlot_AndALateEndDoesNotEvictItsReplacement()
    {
        var backend = new StubBackend(initialises: true);
        var presence = new WasapiAudioPresence(
            backend, _ => { },
            readback: _ => new AudioRenderObservation(true, "Stub", true, true, true, 0f, 1),
            endpointCount: () => 1);
        presence.Open();

        presence.Cue(new AudioCue("braindrain", "a.wav", 1f));
        var first = backend.Players[0];
        Assert.True(first.HasEndSubscriber, "the presence never subscribed, so no end could ever be noticed");
        Assert.True(presence.IsSounding("braindrain"));

        // The clip runs out on its own.
        first.RaiseEnded();

        Assert.True(first.Disposed, "a finished player must be disposed, not left holding the slot");
        Assert.False(presence.IsSounding("braindrain"), "the slot still reads as sounding after the clip ended");

        // Silence now has nothing to stop, which is the observable proof the slot was released
        // rather than merely reporting a stopped player.
        Assert.IsType<CapabilityState.Unavailable>(presence.Silence("braindrain"));

        // THE RACE. A displaced player reports its end late; the replacement must survive it.
        presence.Cue(new AudioCue("mindwipe", "b.wav", 1f));
        var displaced = backend.Players[1];
        presence.Cue(new AudioCue("mindwipe", "c.wav", 1f));
        var replacement = backend.Players[2];

        displaced.RaiseEnded();

        Assert.False(replacement.Disposed, "a late end from the displaced player disposed its replacement");
        Assert.True(presence.IsSounding("mindwipe"), "a late end from the displaced player emptied the slot");
        presence.Dispose();
    }

    [Fact]
    public void ASecondCueOnOneSlotREPLACESTheFirst_AndTheDisplacedPlayerIsStoppedAndDisposed()
    {
        // WPF's per-service stop-replace (MindWipeService.cs:825-848 publishes the new pair and
        // disposes the displaced one; BrainDrainService.cs:271-292 does the same).
        var backend = new StubBackend(initialises: true);
        var presence = new WasapiAudioPresence(
            backend, _ => { },
            readback: _ => new AudioRenderObservation(true, "Stub", true, true, true, 0f, 1),
            endpointCount: () => 1);
        presence.Open();

        presence.Cue(new AudioCue("mindwipe", "a.wav", 1f));
        var first = backend.Players[0];
        presence.Cue(new AudioCue("mindwipe", "b.wav", 1f));

        Assert.True(first.Stopped, "the displaced player was stopped");
        Assert.True(first.Disposed, "the displaced player was disposed");
        Assert.False(backend.Players[1].Disposed, "the new player is still live");

        // And a cue on the OTHER slot does not touch it: one module's clip can never silence the
        // other's, which is what upstream gets from having two separate services.
        presence.Cue(new AudioCue("braindrain", "c.wav", 1f));
        Assert.False(backend.Players[1].Disposed);
        presence.Dispose();
    }

    // =================================================================================
    //  Linux refuses, typed, with the gate named — and it is not a no-op
    // =================================================================================

    [Fact]
    public void Linux_RefusesTyped_AndNamesTheApiThatWouldEarnItPlusTheManualGate()
    {
        var presence = Assert.IsType<UnsupportedAudioPresence>(
            AudioPresenceFactory.CreateFor(AudioHostPlatform.Linux, _ => { }));

        Assert.Equal(AudioReasonCodes.RenderReadbackAbsent, presence.Reason.Code);

        // The refusal is NOT "Linux cannot play audio" — the port's own backend enumerates there.
        // It is that this build cannot EARN the claim, and the detail has to say so, name the
        // Windows API that earns it here, name the Linux analogue that would earn it there, and
        // name why this machine cannot discharge the gate.
        var detail = presence.Reason.Detail;
        Assert.Contains("libminiaudio.so", detail, StringComparison.Ordinal);
        Assert.Contains("IAudioMeterInformation::GetPeakValue", detail, StringComparison.Ordinal);
        Assert.Contains("pactl list sink-inputs", detail, StringComparison.Ordinal);
        Assert.Contains("application.process.id", detail, StringComparison.Ordinal);
        Assert.Contains("pw-dump", detail, StringComparison.Ordinal);
        Assert.Contains("RDP Sink", detail, StringComparison.Ordinal);
        Assert.Contains("MANUAL GATE (Linux, undischarged)", detail, StringComparison.Ordinal);

        // The gate names the step NO platform's automation discharges, Windows included.
        Assert.Contains("a human confirms they heard it", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLinuxRefusalIsNotANoOp_EveryPathRefusesAndNothingIsEverObserved()
    {
        // A no-op would swallow the call and return success. For audio that is indistinguishable
        // from working, which is why every path is asserted rather than only the first.
        var presence = AudioPresenceFactory.CreateFor(AudioHostPlatform.Linux, _ => { });

        Assert.IsType<CapabilityState.Unavailable>(presence.Open());
        Assert.IsType<CapabilityState.Unavailable>(presence.Cue(new AudioCue("slot", "a.wav", 1f)));
        Assert.IsType<CapabilityState.Unavailable>(presence.Silence("slot"));
        Assert.False(presence.IsRendering);

        // "We did not ask" and "we asked and the level was zero" are different facts. A zeroed
        // observation would read like a measurement; Asked=false says no measurement was taken.
        Assert.False(presence.Observe().Asked);
        Assert.False(presence.LastObservation.Asked);
        Assert.False(presence.Observe().Confirmed);
        presence.Dispose();
    }

    [Fact]
    public void SelectionIsNeverAvailability_NoPlatformBranchProducesAvailableWithoutAskingTheOs()
    {
        // runtime-capability-contract §2 rule 2. Three of the four branches cannot produce Available
        // at all, and the fourth only does after WasapiAudioPresence.Open has read the OS back.
        foreach (var platform in new[]
                 {
                     AudioHostPlatform.Linux, AudioHostPlatform.MacOs, AudioHostPlatform.Unknown,
                 })
        {
            var presence = AudioPresenceFactory.CreateFor(platform, _ => { });
            Assert.IsNotType<CapabilityState.Available>(presence.Open());
            presence.Dispose();
        }

        Assert.IsType<WasapiAudioPresence>(AudioPresenceFactory.CreateFor(AudioHostPlatform.Windows, _ => { }));
    }

    // =================================================================================

    /// <summary>The diagnosis both negative controls carry, because a bare False here cost two lanes
    /// a day each: the question is per-PROCESS and cannot be narrowed further, so the only thing that
    /// can make it true is another live render stream in this same process.</summary>
    private const string SomethingElseInThisProcessHasTheEndpoint =
        "WHAT THIS MEANS, AND WHAT IT DOES NOT. It is NOT another process: WasapiRenderProbe skips "
        + "every session whose IAudioSessionControl2::GetProcessId is not this pid, and a peer test "
        + "process holding a real ACTIVE session for 90 seconds left this class green 8 runs out of 8. "
        + "It is NOT a slow teardown either (30 of 30 cycles reported Inactive on the first read after "
        + "Dispose with every core saturated). It is ANOTHER LIVE RENDER STREAM IN THIS PROCESS: "
        + "Windows folds every default-GUID stream a process opens into ONE session with ONE instance "
        + "identifier, which stays Active until the last of them is disposed, so this question cannot "
        + "be asked per-stream and no assertion here can be narrowed to fix it. The classes that open "
        + "a real render device are co-located in RealRenderDeviceCollection so they cannot run "
        + "beside this one; if a new class opens one and did not join, THAT is what this failure is "
        + "reporting, and its name belongs in that collection — see RealRenderDeviceCollection.cs for "
        + "the census method that finds it";

    private static Task WaitUntil(Func<bool> condition, string what) =>
        TestWait.Until(condition, what, window: TimeSpan.FromSeconds(15));

    private static string NewClip()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "ccp-sp109-" + Guid.NewGuid().ToString("N"), "tone.wav");
        return TestWav.Write(path, seconds: 4.0);
    }

    /// <summary>A backend that never touches a device. It exists so the branches that only occur
    /// when the OS and the API DISAGREE can be reached on a machine whose OS agrees.</summary>
    private sealed class StubBackend : IAudioBackend
    {
        private readonly bool _initialises;

        public StubBackend(bool initialises) => _initialises = initialises;

        public List<StubPlayer> Players { get; } = [];

        public int InitAttempts { get; private set; }

        public int PlayersCreated => Players.Count;

        public IReadOnlyList<string> EnumerateDevices() => ["Stub Endpoint"];

        public bool TryInit(string? deviceName, out string? error)
        {
            InitAttempts++;
            error = _initialises ? null : "stub refused";
            return _initialises;
        }

        public IAudioPlayer CreatePlayer(string path, float volume)
        {
            var player = new StubPlayer();
            Players.Add(player);
            return player;
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubPlayer : IAudioPlayer
    {
        /// <summary>A REAL event, not a no-op pair. The stub used to swallow both add and remove,
        /// so no fact could express a clip reaching its end — which is how a presence that never
        /// released a finished player stayed green.</summary>
        public event EventHandler? PlaybackEnded;

        /// <summary>The backend reporting natural completion. Never raised by Stop(), matching the
        /// seam's own contract.</summary>
        public void RaiseEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);

        public bool HasEndSubscriber => PlaybackEnded is not null;

        public AudioPlayerState State { get; private set; } = AudioPlayerState.Stopped;

        public double PositionSec => 0;

        public float Volume { get; set; }

        public bool Stopped { get; private set; }

        public bool Disposed { get; private set; }

        public void Play() => State = AudioPlayerState.Playing;

        public void Pause() => State = AudioPlayerState.Paused;

        public void Stop()
        {
            Stopped = true;
            State = AudioPlayerState.Stopped;
        }

        public void Dispose() => Disposed = true;
    }
}
