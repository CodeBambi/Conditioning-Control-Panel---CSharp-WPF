using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Lifecycle;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The conditioning session's composition point and its lifecycle participant.
///
/// <para><b>Phase-3 start does NOT start a session.</b> It loads the persisted preset and the
/// asset selection, and stops. WPF's engine runs only when the user presses START
/// (<c>MainWindow/MainWindow.StartStop.cs:34,105</c>); an app that began conditioning you
/// because it launched would be a different product. Construction starts nothing at all
/// (SP-003 contract §4.4).</para>
///
/// <para><b>Teardown.</b> The participant's stop ends any running session before the store
/// stops, so the reverse-order participant stop can never leave a schedule pointing at a
/// halted store. It is belt and braces: by the time participants stop, the host has already
/// cancelled and drained every generation (<c>ApplicationHost.ShutdownAsync</c>), which is what
/// actually kills the schedules. Both paths are exercised.</para>
/// </summary>
public sealed class SessionParticipant : IBackgroundParticipant
{
    /// <summary>The user-media root — the SAME one DTRH and Graded Intake use
    /// (<c>Features/Dtrh/DtrhParticipant.cs:92</c>, <c>Features/Intake/IntakeParticipant.cs:61</c>).
    /// One assets location for the whole app, as WPF has one <c>App.EffectiveAssetsPath</c>.</summary>
    public static string AssetsRootFor(string dataDirectory) => Path.Combine(dataDirectory, "assets");

    private readonly PersistenceStore<SessionPresetDocument> _preset;
    private readonly PersistenceStore<AssetSelectionDocument> _assetSelection;
    private readonly ILogSink _log;

    public SessionParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        ISessionClock? clock = null,
        IFlashImagePool? pool = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        _log = infra.Log;
        ImagesFolder = DtrhUserMedia.ImagesFolder(AssetsRootFor(dataDirectory));

        _preset = new PersistenceStore<SessionPresetDocument>(
            infra.OwnerFor("SessionPreset"), infra.Log,
            Path.Combine(dataDirectory, SessionPresetDocument.FileName),
            SessionPresetDocument.CurrentSchemaVersion);

        // A THIRD read-only reader of the shared deselection document (SP-055 named two: the
        // DTRH host and the intake host). It is opened here rather than skipped so the flash
        // pool cannot become the one consumer that ignores an uncheck — the exact
        // two-scans-disagree defect that document was written to prevent. No writer is added.
        _assetSelection = new PersistenceStore<AssetSelectionDocument>(
            infra.OwnerFor("SessionAssetSelection"), infra.Log,
            Path.Combine(dataDirectory, AssetSelectionStore.FileName),
            AssetSelectionDocument.CurrentSchemaVersion);

        Flash = new FlashImagesEffect(
            infra.OwnerFor("FlashImages"),
            infra.UiDispatch,
            clock ?? new SystemSessionClock(),
            pool ?? new FlashImagePool(
                AssetsRootFor(dataDirectory),
                () => (_assetSelection.Current.DisabledAssetPaths, _assetSelection.Current.UseAssetWhitelist)),
            _preset);

        // Rack order is WPF's (StudioTabView.xaml.cs:482-497), and it is also the order
        // StartEngine arms and StopEngineCore disarms in — flash first on both
        // (MainWindow.StartStop.cs:178, :305).
        Engine = new SessionEngine([Flash], _preset);
    }

    public string Name => "Session";

    public bool Running => _preset.Running;

    /// <summary>The session itself. The shell drives this; nothing else may.</summary>
    public SessionEngine Engine { get; }

    /// <summary>The one ported effect (public so the Studio module panel and the tests reach the real object).</summary>
    public FlashImagesEffect Flash { get; }

    /// <summary>The persisted preset store (public so a surface can save a dial it moved).</summary>
    public PersistenceStore<SessionPresetDocument> Preset => _preset;

    /// <summary>Where the flash pool reads the user's images from. The module panel shows this
    /// so an empty pool has an answer to "where do I put them", which WPF's own comment calls
    /// its most common first-run dead end (<c>FlashService.cs:589-597</c>).</summary>
    public string ImagesFolder { get; }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _preset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _assetSelection.StartAsync(cancellationToken).ConfigureAwait(false);

        if (_preset.LastLoadOutcome is { IsDegraded: true })
        {
            // Typed Degraded, never silent: a quarantined or newer-schema preset means the
            // session runs on defaults, and the user is entitled to know that before they
            // press START.
            _log.Log($"session-preset: load → {_preset.LastLoadOutcome.GetType().Name} (typed Degraded — the session will run on default dials)");
        }
    }

    /// <inheritdoc/>
    public Task StopAsync()
    {
        Engine.Stop();
        return Task.WhenAll(_preset.StopAsync(), _assetSelection.StopAsync());
    }

    /// <summary>Teardown flush for the reserved pre-drain slot (persistence contract §11). The
    /// asset selection has no writer, so it has nothing to flush.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _preset.FlushAsync(boundedWait);
}
