using System.Buffers;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// The camera capability's APP-LIFETIME owner: one consent document, one enumeration route, one
/// capture route, and the gate above all three.
///
/// <para><b>Why a participant, and why app-scoped.</b> Upstream's webcam service is a member of the
/// application, not of a conditioning session: it owns <i>"the only VideoCapture handle in the
/// application"</i> (<c>Services/Webcam/WebcamTrackingService.cs:86-90</c>) and its consent is read
/// from application settings by a dialog that runs outside any session
/// (<c>Dialogs/WebcamConsentDialog.xaml.cs:144-151</c>). So this is owned where the app is owned,
/// through the port's existing <see cref="IBackgroundParticipant"/> — the same decision, for the
/// same reason, as <c>Haptics/HapticParticipant</c>, and no second lifetime model.</para>
///
/// <para><b>PHASE 3 LOADS ONE FILE AND ASKS NOTHING OF ANY CAMERA.</b> That is the behaviour the
/// consent contract turns on: <c>client/docs/capability-inventory.md</c> requires that the camera
/// start only after current explicit consent AND an explicit user start, and that opening the
/// dashboard, restoring settings or finding a calibration never start it. Restoring settings is
/// literally what <see cref="StartAsync"/> does, so it is the exact path that must not look — and it
/// does not: <see cref="Enumerations"/> AND <see cref="CameraOpenAttempts"/> both stay zero across a whole
/// launch. The capture verb that now exists lives on a separate seam
/// (<see cref="ICameraCaptureSource"/>) behind <see cref="StartCaptureAsync"/>, which refuses at the
/// engine gate before a device is enumerated on every product build.</para>
///
/// <para><b>THE PROBE IS GATED, and the gate is the product behaviour rather than an optimisation.</b>
/// <c>Capabilities/CapabilityRegistry</c>'s runner probes every registered capability at launch. An
/// ungated registration would therefore ask the operating system for a camera roster on every launch
/// of a default install — for a feature nobody switched on, in a build that cannot use the answer,
/// on behalf of a user who has not consented. Enumeration touches no device, so that would not be a
/// privacy breach; it would still be this product asking about somebody's cameras for no reason they
/// could benefit from, and the consent contract's whole shape is that the camera question is not
/// asked until it has been earned.</para>
///
/// <para><b>Nothing logged here can carry frame data, and nothing logged here names a device.</b>
/// There is no frame type in this namespace to log (<c>Camera/ICameraDeviceSource.cs</c>), and the
/// log lines below carry a state and a COUNT only. Upstream logs device names
/// (<c>Services/Webcam/WebcamTrackingService.cs:1136-1141</c>) and that is not forbidden — a camera's
/// model name is not a biometric derivative — but a count answers the only question a log is asked
/// here ("did it find anything?") and a count cannot identify anybody's hardware in a shared log.</para>
/// </summary>
public sealed class CameraParticipant : IBackgroundParticipant
{
    private readonly ILogSink _log;
    private readonly PersistenceStore<CameraConsentDocument> _store;
    private readonly ICameraDeviceSource _source;
    private readonly ICameraCaptureSource _capture;
    private readonly bool _engineAdmitted;
    private bool _running;

    /// <param name="infra">The participant infrastructure: this participant's async owner and the log sink.</param>
    /// <param name="dataDirectory">Where <c>camera-consent.json</c> lives — the same data root every other store uses.</param>
    /// <param name="source">
    /// The enumeration route. Null takes the real one for this platform. A test injects a recording
    /// double so that "nothing was asked of any camera" is an ASSERTION about a counter rather than
    /// a claim about code nobody executed.
    /// </param>
    /// <param name="engineAdmitted">
    /// Whether a gaze engine is implemented. Null takes the build's real answer
    /// (<see cref="CameraCapability.AdmittedEngines"/>, empty). It is a parameter for the reason
    /// <c>Haptics/HapticSinkFactory.CreateFrom</c>'s admitted-routes parameter is one: with the
    /// build's answer always false, every rung BELOW the engine would be unreachable and the consent
    /// gate, the permission refusal and the no-device refusal would never be executed by anything.
    ///
    /// <para>It cannot manufacture a working feature, and that is checked rather than asserted:
    /// <see cref="CameraCapability.Classify"/> returns <see cref="CapabilityState.Available"/> for no
    /// input at all, so the best this parameter can reach is a
    /// <see cref="CapabilityState.Degraded"/> that says out loud that no camera was opened.</para>
    /// </param>
    /// <param name="capture">
    /// The capture route. Null takes the real one for this platform. A test injects a recording
    /// double for the same reason <paramref name="source"/> takes one: "no camera was opened" has to
    /// be an assertion about a counter, not a claim about code nobody ran.
    /// </param>
    public CameraParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        ICameraDeviceSource? source = null,
        bool? engineAdmitted = null,
        ICameraCaptureSource? capture = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);

        _log = infra.Log;
        _source = source ?? CameraDeviceSourceFactory.ForCurrentPlatform();
        _capture = capture ?? CameraDeviceSourceFactory.CaptureForCurrentPlatform();
        _engineAdmitted = engineAdmitted ?? CameraCapability.AdmittedEngines.Count > 0;
        _store = new PersistenceStore<CameraConsentDocument>(
            infra.OwnerFor("CameraConsent"), infra.Log,
            Path.Combine(dataDirectory, CameraConsentDocument.FileName),
            CameraConsentDocument.CurrentSchemaVersion);
    }

    /// <inheritdoc/>
    public string Name => "Camera";

    /// <inheritdoc/>
    public bool Running => _running;

    /// <summary>The persisted consent, so a consent surface can save the decision it was given.</summary>
    public PersistenceStore<CameraConsentDocument> Consent => _store;

    /// <summary>Whether a gaze engine is implemented in this participant's build. False on every product path.</summary>
    public bool EngineAdmitted => _engineAdmitted;

    /// <summary>
    /// How many times this participant has asked the operating system which cameras exist.
    /// <b>Zero across a whole launch of a fresh install</b>, and it is evidence rather than a
    /// counter: it is what makes "opening the dashboard never starts the camera" a fact somebody can
    /// fail instead of a sentence in a document.
    /// </summary>
    public int Enumerations { get; private set; }

    /// <summary>
    /// How many times this participant has ASKED a camera to open. <b>Zero across a whole launch,
    /// zero after restoring settings, and zero after a user grants consent</b> — the capability
    /// contract's "the camera starts only after current explicit consent AND an explicit user start,
    /// and never from opening the dashboard, restoring settings or finding a calibration" turned into
    /// a number somebody can fail.
    ///
    /// <para><b>It counts the ASK and not the success, and that correction was forced by a mutation
    /// that should have failed and did not.</b> It first counted only opens that WORKED, on the
    /// reasoning that a refused open touched no device. That is false: reaching
    /// <see cref="ICameraCaptureSource.Open"/> at all starts the Media Foundation platform and
    /// enumerates this user's video devices before it can fail. A launch that tried to open a camera
    /// and was refused by the driver would have left a success-counter at zero and passed the fact
    /// that exists to forbid exactly that. The privacy question is "did this process ask?", so that
    /// is what is counted.</para>
    /// </summary>
    public int CameraOpenAttempts { get; private set; }

    /// <summary>Whether a camera is open right now, asked of the capture route rather than
    /// remembered here — a field this process remembers writing is not an answer about a device.</summary>
    public bool CaptureRunning => _capture.IsOpen;

    /// <summary>How many frames the open camera has delivered since it was opened. A COUNT and
    /// nothing else: there is no frame, no crop, no tensor and no landmark anywhere in this
    /// participant to report.</summary>
    public int FramesRead => _capture.FramesRead;

    /// <summary>The capture backend, for a System page line that can say HOW a camera would be
    /// opened. Never a claim that one was.</summary>
    public string CaptureBackend => _capture.Backend;

    /// <summary>Every ladder rung the last open attempted, with its outcome. Strings only.</summary>
    public IReadOnlyList<string> CaptureAttempts => _capture.AttemptedRungs;

    /// <summary>What the last enumeration learned, or null when none was made. Never cached across
    /// asks: it is replaced wholesale, so it can only ever describe one moment.</summary>
    public CameraInventory? LastInventory { get; private set; }

    /// <summary>
    /// The typed capability state behind this capability, right now — the SAME expression the
    /// registered probe evaluates, so a settings panel and the System page cannot tell two different
    /// stories about one camera.
    /// </summary>
    public CapabilityState State =>
        CameraCapability.Classify(_engineAdmitted, CameraConsent.Evaluate(_store.Current), LastInventory);

    /// <summary>
    /// Phase 3: load the consent document. That is all it does, and the omission is the feature —
    /// see the class remarks. No camera is enumerated, no device is opened, and no audio endpoint
    /// exists in this namespace to open.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_running)
        {
            return;
        }

        await _store.StartAsync(cancellationToken).ConfigureAwait(false);
        if (_store.LastLoadOutcome is { IsDegraded: true } outcome)
        {
            // Typed Degraded, never silent. The default this falls back to is Granted = false, which
            // is the only safe direction: a quarantined or newer-schema consent file must never be
            // read as an agreement, because the agreement is the thing it failed to read.
            _log.Log($"camera-consent: load → {outcome.GetType().Name} (typed Degraded — camera consent falls back "
                + "to NOT GRANTED, which is a refusal and never a grant)");
        }

        _running = true;
        _log.Log("camera: settings restored and NO camera was enumerated or opened — "
            + $"consent {(CameraConsent.Evaluate(_store.Current) is null ? "current" : "not current")}, "
            + $"gaze engine {(_engineAdmitted ? "implemented" : "not implemented in this build")}");
    }

    /// <summary>
    /// Stop: <b>release the camera first, then the store.</b>
    ///
    /// <para>The order is deliberate. A shutdown that flushed a file and left a device open would
    /// leave the camera indicator lit on a machine whose application has gone, which is the one
    /// failure mode a user cannot diagnose or repair. <see cref="ICameraCaptureSource.Close"/> is
    /// idempotent, so this costs nothing on the overwhelmingly common path where no camera was ever
    /// opened.</para>
    /// </summary>
    public async Task StopAsync()
    {
        _running = false;
        _capture.Close();
        await _store.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Flush the consent document in the host's reserved pre-drain slot (persistence
    /// contract §11). A consent a user gave or withdrew on the way out is the last thing this
    /// capability may lose.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _store.FlushAsync(boundedWait);

    /// <summary>
    /// The registered capability probe, in one place, <b>gated so it cannot ask the operating system
    /// about a user's cameras for a feature that has no engine and no consent.</b>
    ///
    /// <para>Both gates refuse BEFORE <see cref="ICameraDeviceSource.Enumerate"/> is reached, and
    /// they are asked in <see cref="CameraCapability.Classify"/>'s order rather than a second order
    /// invented here: a null inventory is passed on, which is how the classification learns that the
    /// question was never put rather than answered emptily.</para>
    ///
    /// <para>The enumeration runs on a thread-pool thread and observes the startup token, because
    /// <c>client/docs/capability-inventory.md</c> requires camera acquisition to be cancellable and
    /// off the UI thread. The route itself never throws (<see cref="ICameraDeviceSource.Enumerate"/>),
    /// so a route failure arrives as a typed <see cref="CapabilityState.Faulted"/> in the inventory
    /// rather than as an exception the registry has to classify for it.</para>
    /// </summary>
    public async Task<CapabilityState> ProbeAsync(CancellationToken cancellationToken)
    {
        var consentRefusal = CameraConsent.Evaluate(_store.Current);
        if (!_engineAdmitted || consentRefusal is not null)
        {
            return CameraCapability.Classify(_engineAdmitted, consentRefusal, inventory: null);
        }

        var inventory = await Task.Run(_source.Enumerate, cancellationToken).ConfigureAwait(false);
        Enumerations++;
        LastInventory = inventory;
        _log.Log($"camera: {inventory.Route} named {inventory.Devices.Count} device(s)"
            + (inventory.Refusal is null ? string.Empty : " and refused typed") + " — no camera was opened");
        return CameraCapability.Classify(_engineAdmitted, consentRefusal, inventory);
    }

    /// <summary>
    /// Record a completed consent and persist it. Returns false and writes NOTHING when the request
    /// has not passed every gate (<see cref="CameraConsentRequest.IsComplete"/>).
    ///
    /// <para><b>It does not start a camera, and it does not enumerate one either.</b> Upstream's own
    /// grant handler makes the same separation in as many words — <i>"Persist consent. Camera stays
    /// closed"</i> (<c>Dialogs/WebcamConsentDialog.xaml.cs:142-143</c>) — and here it is structural:
    /// granting consent reaches neither <see cref="ICameraDeviceSource.Enumerate"/> nor
    /// <see cref="ICameraCaptureSource.Open"/>, and <see cref="CameraOpenAttempts"/> is held at zero across
    /// a whole launch that includes a grant.</para>
    /// </summary>
    public async Task<bool> GrantConsentAsync(CameraConsentRequest request, DateTimeOffset grantedUtc)
    {
        var granted = false;
        _store.Mutate(document => granted = CameraConsent.TryGrant(document, request, grantedUtc));
        if (!granted)
        {
            _log.Log("camera: consent NOT granted — the request did not pass every gate, and nothing was written");
            return false;
        }

        await _store.Save().ConfigureAwait(false);
        _log.Log($"camera: consent granted against privacy contract {CameraConsent.CurrentVersion} — "
            + "the camera stays closed until an explicit user start");
        return true;
    }

    /// <summary>
    /// Withdraw consent and persist the withdrawal, dropping whatever the last enumeration learned.
    ///
    /// <para><b>IF A CAMERA IS OPEN, IT IS CLOSED FIRST — before the flag is even cleared.</b>
    /// Upstream stops the service before clearing the consent fields
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1057-1068</c>) and the order is the whole point:
    /// withdrawing consent while a camera is streaming has to mean the camera STOPS, not that the
    /// next start will be refused. A revoke that only wrote a file and left the indicator lit would
    /// be the single worst thing this capability could do to somebody who just changed their mind.</para>
    ///
    /// <para>The roster is dropped for the same reason: it was learned under a consent that no longer
    /// exists.</para>
    /// </summary>
    public async Task RevokeConsentAsync()
    {
        var wasOpen = CaptureRunning;
        await StopCaptureAsync().ConfigureAwait(false);
        _store.Mutate(CameraConsent.Revoke);
        LastInventory = null;
        await _store.Save().ConfigureAwait(false);
        _log.Log("camera: consent revoked — the stored grant and its contract version are cleared, the last "
            + $"device roster is dropped, and {(wasOpen ? "THE OPEN CAMERA WAS RELEASED FIRST" : "no camera was open")}");
    }

    // =========================================================================================
    //  Capture — the only path in this product that opens a camera
    // =========================================================================================

    /// <summary>
    /// <b>OPEN A CAMERA.</b> The only method in this product that can, and it is reachable only from
    /// an explicit user action — never from a probe, never from a start-up phase, never from
    /// restoring settings, and never from granting consent.
    ///
    /// <para><b>The gate is <see cref="CameraCapability.Classify"/>'s ladder, unchanged, and it is
    /// asked BEFORE anything is enumerated let alone opened.</b> No engine refuses first, then no
    /// consent, then the device facts — the same order the launch probe uses, so a user cannot be
    /// shown one story by the System page and a different one by pressing start. In a product build
    /// <see cref="EngineAdmitted"/> is false, so this method refuses on its first line and this
    /// product cannot open a camera at all.</para>
    ///
    /// <para><b>Consent is re-read here rather than trusted from start-up</b>, because consent can be
    /// withdrawn between launch and start, and a stale copy of a withdrawn agreement is the exact
    /// failure the whole consent contract exists to prevent.</para>
    ///
    /// <para>Enumeration and the open both run on thread-pool threads and observe the token:
    /// <c>client/docs/capability-inventory.md</c> requires camera acquisition to be cancellable and
    /// off the UI thread, and the open can take several seconds per ladder rung by design
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1262-1268</c>).</para>
    /// </summary>
    /// <param name="preferredStableId">
    /// The camera to open, as its <see cref="CameraDevice.StableId"/> from a roster the user chose
    /// from. Null opens the first camera the route names. Matched by hardware instance
    /// (<see cref="CameraHardwareKey"/>) and never by position; a named camera that is not in the
    /// roster is REFUSED rather than substituted, because opening somebody's other camera and
    /// opening one at random look the same from in front of it.
    /// </param>
    /// <param name="cancellationToken">Cancels between ladder rungs and between probe reads.</param>
    public async Task<CapabilityState> StartCaptureAsync(
        string? preferredStableId, CancellationToken cancellationToken)
    {
        var consentRefusal = CameraConsent.Evaluate(_store.Current);
        if (!_engineAdmitted || consentRefusal is not null)
        {
            var refusal = CameraCapability.Classify(_engineAdmitted, consentRefusal, inventory: null);
            _log.Log($"camera: START REFUSED at the {(_engineAdmitted ? "consent" : "engine")} gate — "
                + "NO CAMERA WAS OPENED and no device was enumerated");
            return refusal;
        }

        if (_capture.IsOpen)
        {
            return OpenState();
        }

        var inventory = await Task.Run(_source.Enumerate, cancellationToken).ConfigureAwait(false);
        Enumerations++;
        LastInventory = inventory;

        if (inventory.Refusal is not null || inventory.Devices.Count == 0)
        {
            _log.Log($"camera: START stopped at the device roster — {inventory.Devices.Count} device(s), "
                + $"{(inventory.Refusal is null ? "no refusal" : "typed refusal")} — no camera was opened");
            return CameraCapability.Classify(_engineAdmitted, consentRefusal, inventory);
        }

        var device = SelectDevice(inventory, preferredStableId);
        if (device is null)
        {
            _log.Log("camera: START refused — the named camera is not in the roster, and this product never "
                + "substitutes another one");
            return new CapabilityState.DependencyMissing(
                "the camera that was asked for",
                new CapabilityReason(
                    CameraReasonCodes.CameraDeviceNotMatched,
                    $"the camera this start named is not among the {inventory.Devices.Count} device(s) "
                    + $"{inventory.Route} reports. It has most likely been unplugged. NO CAMERA WAS OPENED: this "
                    + "product does not fall back to whichever other camera happens to be attached"));
        }

        // Counted BEFORE the ask, not after a success: reaching Open at all starts the capture
        // platform and touches this user's devices, whatever it then returns.
        CameraOpenAttempts++;
        var failure = await Task.Run(
            () => _capture.Open(device.Value, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (failure is not null)
        {
            _log.Log($"camera: OPEN FAILED after {_capture.AttemptedRungs.Count} ladder rung(s) — no camera is open");
            return failure;
        }

        _log.Log($"camera: OPEN — a camera is delivering frames after {_capture.AttemptedRungs.Count} ladder "
            + $"rung(s) on {_capture.AdoptedRung}. Frames are measured and dropped; none is kept, saved or logged");
        return OpenState();
    }

    /// <summary>
    /// Read up to <paramref name="frames"/> frames from the open camera, show each one to
    /// <paramref name="sink"/>, and drop it. Returns how many really arrived.
    ///
    /// <para><b>Bounded on purpose, and it is not the session loop.</b> There is no gaze engine in
    /// this build, so a perpetual 30fps loop would be burning a core for nobody: this exists so that
    /// "the camera delivers frames" is something a caller can OBSERVE rather than assume, and the
    /// engine slice replaces it with a loop that runs as long as a session does.</para>
    ///
    /// <para><b>The sink runs on the pump's thread, inside the read.</b> That is deliberate: the
    /// alternative — handing frames to somebody else's thread — requires copying them out of the
    /// driver's buffer first, which is the one thing this seam refuses to do. A consumer that is
    /// slower than the camera therefore slows the camera down instead of building a queue of
    /// retained frames, and a queue of retained frames is exactly what
    /// <c>client/docs/capability-inventory.md</c>'s memory-only rule forbids.</para>
    ///
    /// <para>Stops early after <see cref="CameraFrameProbe.MaxConsecutiveReadFailures"/> reads in a
    /// row that produce nothing, which is upstream's own budget for deciding an open camera has gone
    /// away (<c>Services/Webcam/WebcamTrackingService.cs:120</c>) rather than a new rule.</para>
    /// </summary>
    /// <param name="frames">How many frames to ask for. Never negative.</param>
    /// <param name="sink">The consumer, or null to read and drop unseen — which is what every
    /// product path passes today, because nothing in this build consumes a frame.</param>
    /// <param name="cancellationToken">Observed between every read.</param>
    public async Task<int> PumpAsync(
        int frames, ReadOnlySpanAction<byte, CameraFrameInfo>? sink, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        if (!_capture.IsOpen)
        {
            return 0;
        }

        var delivered = await Task.Run(
            () =>
            {
                var count = 0;
                var consecutiveFailures = 0;
                while (count < frames && consecutiveFailures < CameraFrameProbe.MaxConsecutiveReadFailures)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_capture.ReadFrame(sink, cancellationToken))
                    {
                        count++;
                        consecutiveFailures = 0;
                    }
                    else
                    {
                        consecutiveFailures++;
                    }
                }

                return count;
            },
            cancellationToken).ConfigureAwait(false);

        // A COUNT and nothing else, with or without a consumer: upstream's own rule is that per-frame
        // numbers are never logged (Services/Webcam/WebcamTrackingService.cs:28-29), and a line that
        // said what a consumer MADE of a frame would be the first per-frame derivative in this build.
        _log.Log(sink is null
            ? $"camera: {delivered} frame(s) delivered and dropped unseen — nothing from them was kept"
            : $"camera: {delivered} frame(s) delivered to a consumer that cannot retain them — the span ended "
                + "with each call and nothing from them was kept");
        return delivered;
    }

    /// <summary>
    /// <b>Release the camera.</b> Idempotent, and it is the thing this slice most has to get right:
    /// a camera the user stopped whose indicator is still lit is worse than a camera that never
    /// opened. The release itself is COM work, so it runs off the calling thread like the open did.
    /// </summary>
    public Task StopCaptureAsync()
    {
        var wasOpen = _capture.IsOpen;
        return Task.Run(() =>
        {
            _capture.Close();
            _log.Log(wasOpen
                ? "camera: CLOSED — the device is released and this process holds no camera handle"
                : "camera: stop requested with no camera open — nothing to release");
        });
    }

    /// <summary>
    /// The state of an OPEN camera: <see cref="CapabilityState.Degraded"/>, never
    /// <see cref="CapabilityState.Available"/>, because the feature a user wanted is GAZE and this
    /// build has no engine to produce any (<see cref="CameraCapability.AdmittedEngines"/> is empty).
    /// </summary>
    private CapabilityState OpenState() => new CapabilityState.Degraded(
        $"an open camera delivering frames through {_capture.Backend}",
        new CapabilityReason(
            CameraReasonCodes.CameraOpen,
            "a camera is OPEN and delivering frames, and every frame is measured and dropped: no frame, crop, "
            + "tensor, landmark or gaze sample is kept, saved, logged or transmitted. NO GAZE IS BEING TRACKED — "
            + "this build has no gaze engine, so an open camera produces nothing a user asked for. Stopping "
            + "releases the device"));

    /// <summary>The camera to open, or null when one was named and is not there. Never a fallback.</summary>
    private static CameraDevice? SelectDevice(CameraInventory inventory, string? preferredStableId)
    {
        if (string.IsNullOrWhiteSpace(preferredStableId))
        {
            return inventory.Devices[0];
        }

        foreach (var device in inventory.Devices)
        {
            if (CameraHardwareKey.Matches(device.StableId, preferredStableId))
            {
                return device;
            }
        }

        return null;
    }
}
