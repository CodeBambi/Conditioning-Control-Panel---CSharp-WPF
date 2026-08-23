using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Camera;

/// <summary>
/// The camera capability's APP-LIFETIME owner: one consent document, one enumeration route, and the
/// gate between them.
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
/// does not: <see cref="Enumerations"/> stays zero across a whole launch, and there is no capture
/// verb on <see cref="ICameraDeviceSource"/> for anything to call even if it wanted to.</para>
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
    public CameraParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        ICameraDeviceSource? source = null,
        bool? engineAdmitted = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);

        _log = infra.Log;
        _source = source ?? CameraDeviceSourceFactory.ForCurrentPlatform();
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

    /// <inheritdoc/>
    public Task StopAsync()
    {
        _running = false;
        return _store.StopAsync();
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
    /// the only path to <see cref="ICameraDeviceSource.Enumerate"/> is
    /// <see cref="ProbeAsync"/>, and there is no path to a capture at all.</para>
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
    /// <para><b>The roster is dropped because it was learned under a consent that no longer exists.</b>
    /// Upstream's revoke stops the service and clears the calibration before clearing the fields
    /// (<c>Services/Webcam/WebcamTrackingService.cs:1057-1068</c>); this build has neither a running
    /// service nor a calibration, and the roster is the only camera-derived thing it holds.</para>
    /// </summary>
    public async Task RevokeConsentAsync()
    {
        _store.Mutate(CameraConsent.Revoke);
        LastInventory = null;
        await _store.Save().ConfigureAwait(false);
        _log.Log("camera: consent revoked — the stored grant and its contract version are cleared, and the last "
            + "device roster is dropped");
    }
}
