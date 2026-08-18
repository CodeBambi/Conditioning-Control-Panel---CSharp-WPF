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
    private readonly IFlashSurface _surface;
    private readonly UiDispatchBoundary _uiDispatch;

    public SessionParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        ISessionClock? clock = null,
        IFlashImagePool? pool = null,
        IFlashSurface? surface = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        _log = infra.Log;
        _uiDispatch = infra.UiDispatch;
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

        var sessionClock = clock ?? new SystemSessionClock();

        // SP-100: where a flash goes. The presenter is built here rather than in the composition
        // root because it needs the SAME clock the effect paces on (the stagger, the per-surface
        // lifetime and WPF's topmost cadence all ride it, so a test drives every one of them with
        // no wall-clock wait) and the SAME dispatch boundary the effect projects through (a native
        // window belongs to the thread that made it). Nothing is created until a flash really
        // draws: the factory inside it runs on the first surface, so a session that never flashes
        // — and every headless or unit run, where the boundary is unbound and the projection is
        // skipped — creates no window at all.
        _surface = surface ?? FlashSurfacePresenter.Product(
            sessionClock,
            action =>
            {
                if (infra.UiDispatch.IsBound)
                {
                    infra.UiDispatch.Post(action);
                }
            });

        Flash = new FlashImagesEffect(
            infra.OwnerFor("FlashImages"),
            infra.UiDispatch,
            sessionClock,
            pool ?? new FlashImagePool(
                AssetsRootFor(dataDirectory),
                () => (_assetSelection.Current.DisabledAssetPaths, _assetSelection.Current.UseAssetWhitelist)),
            _preset,
            random: null,
            surface: _surface);

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

    /// <summary>Where its flashes are drawn. Public for the same reason: a surface nobody can
    /// reach is a surface nobody can interrogate.</summary>
    public IFlashSurface Surface => _surface;

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
    /// <remarks>
    /// <para><b>The surface's teardown goes through the dispatch boundary, not down this thread.</b>
    /// A native window belongs to the thread that created it — the UI thread — and only that thread
    /// may destroy it. This method does NOT run there: <c>ShutdownAsync</c> resumes on a thread-pool
    /// thread, so a synchronous <c>Dispose</c> here would take the wrong-thread branch inside
    /// <c>Win32OverlayPresence</c>, <c>DestroyWindow</c> would fail, and the honest diagnostic it
    /// records would be read by nobody.</para>
    ///
    /// <para><b>And the bound on it, stated rather than assumed.</b> A post is delivered only while
    /// the dispatcher is still running. On the ordinary teardown path it is not, so the posted
    /// teardown does not run and the surfaces are reclaimed by the OPERATING SYSTEM at process exit.
    /// That is acceptable here and only here, because what the user can see has already been dealt
    /// with one line above: <c>Engine.Stop()</c> disarms the effect, and disarm posts
    /// <c>HideAll</c> from the UI thread the user pressed STOP on. The visible-stop guarantee rests
    /// on that, never on process death.</para>
    /// </remarks>
    public Task StopAsync()
    {
        // Stop the session first: its disarm is what takes any live flash off the user's screen
        // (WPF closes every flash window on stop, Services/Flash/FlashService.cs:3878-3884).
        Engine.Stop();
        if (_surface is IDisposable disposable)
        {
            if (_uiDispatch.IsBound)
            {
                _uiDispatch.Post(disposable.Dispose);
            }
            else
            {
                // Never bound means the projection was always skipped, so no surface was ever
                // created and there is no window whose thread could be wrong.
                disposable.Dispose();
            }
        }

        return Task.WhenAll(_preset.StopAsync(), _assetSelection.StopAsync());
    }

    /// <summary>Teardown flush for the reserved pre-drain slot (persistence contract §11). The
    /// asset selection has no writer, so it has nothing to flush.</summary>
    public Task FlushAsync(TimeSpan boundedWait) => _preset.FlushAsync(boundedWait);
}
