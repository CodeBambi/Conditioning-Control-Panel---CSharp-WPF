using CcpClient.Desktop.Audio;
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
    private readonly PersistenceStore<SubliminalPresetDocument> _subliminalPreset;
    private readonly PersistenceStore<PinkFilterPresetDocument> _pinkFilterPreset;
    private readonly PersistenceStore<SpiralPresetDocument> _spiralPreset;
    private readonly PersistenceStore<IntensityRampPresetDocument> _rampPreset;
    private readonly PersistenceStore<MindWipePresetDocument> _mindWipePreset;
    private readonly PersistenceStore<BrainDrainPresetDocument> _brainDrainPreset;
    private readonly PersistenceStore<AssetSelectionDocument> _assetSelection;
    private readonly ILogSink _log;
    private readonly IFlashSurface _surface;
    private readonly ISubliminalSurface _subliminalSurface;
    private readonly IPinkFilterSurface _pinkFilterSurface;
    private readonly ISpiralSurface _spiralSurface;
    private readonly IAudioPresence _audio;
    private readonly UiDispatchBoundary _uiDispatch;
    private readonly string _dataDirectory;

    public SessionParticipant(
        ParticipantInfrastructure infra,
        string dataDirectory,
        ISessionClock? clock = null,
        IFlashImagePool? pool = null,
        IFlashSurface? surface = null,
        ISubliminalSurface? subliminalSurface = null,
        Func<bool>? onSignalThread = null,
        IPinkFilterSurface? pinkFilterSurface = null,
        ISpiralSurface? spiralSurface = null,
        IAudioPresence? audio = null,
        IAudioCuePool? mindWipeClips = null,
        IAudioCuePool? brainDrainClips = null)
    {
        ArgumentNullException.ThrowIfNull(infra);
        ArgumentException.ThrowIfNullOrEmpty(dataDirectory);
        _log = infra.Log;
        _uiDispatch = infra.UiDispatch;
        _dataDirectory = dataDirectory;
        ImagesFolder = DtrhUserMedia.ImagesFolder(AssetsRootFor(dataDirectory));

        _preset = new PersistenceStore<SessionPresetDocument>(
            infra.OwnerFor("SessionPreset"), infra.Log,
            Path.Combine(dataDirectory, SessionPresetDocument.FileName),
            SessionPresetDocument.CurrentSchemaVersion);

        // SP-101: the Subliminals module's own document. One per module rather than more members on
        // the shared preset — see SubliminalPresetDocument for why, and for the File Scope fact that
        // is the other half of the reason.
        _subliminalPreset = new PersistenceStore<SubliminalPresetDocument>(
            infra.OwnerFor("SubliminalPreset"), infra.Log,
            Path.Combine(dataDirectory, SubliminalPresetDocument.FileName),
            SubliminalPresetDocument.CurrentSchemaVersion);

        // SP-105: the Pink Filter module's own document, on the same per-module precedent (D71).
        _pinkFilterPreset = new PersistenceStore<PinkFilterPresetDocument>(
            infra.OwnerFor("PinkFilterPreset"), infra.Log,
            Path.Combine(dataDirectory, PinkFilterPresetDocument.FileName),
            PinkFilterPresetDocument.CurrentSchemaVersion);

        // SP-106: the moving module's own document, on the same per-module precedent (D71/D80).
        _spiralPreset = new PersistenceStore<SpiralPresetDocument>(
            infra.OwnerFor("SpiralPreset"), infra.Log,
            Path.Combine(dataDirectory, SpiralPresetDocument.FileName),
            SpiralPresetDocument.CurrentSchemaVersion);

        // SP-108: the non-drawing module's own document, same precedent again.
        _rampPreset = new PersistenceStore<IntensityRampPresetDocument>(
            infra.OwnerFor("IntensityRampPreset"), infra.Log,
            Path.Combine(dataDirectory, IntensityRampPresetDocument.FileName),
            IntensityRampPresetDocument.CurrentSchemaVersion);

        // SP-109: the two AUDIO modules' own documents, same precedent again. Two documents rather
        // than one shared "audio" file for the same blast-radius reason: one hand-broken value must
        // not take both modules' dials to defaults, and these two rows are not equals — one is a
        // whole row and the other is deliberately half of one.
        _mindWipePreset = new PersistenceStore<MindWipePresetDocument>(
            infra.OwnerFor("MindWipePreset"), infra.Log,
            Path.Combine(dataDirectory, MindWipePresetDocument.FileName),
            MindWipePresetDocument.CurrentSchemaVersion);

        _brainDrainPreset = new PersistenceStore<BrainDrainPresetDocument>(
            infra.OwnerFor("BrainDrainPreset"), infra.Log,
            Path.Combine(dataDirectory, BrainDrainPresetDocument.FileName),
            BrainDrainPresetDocument.CurrentSchemaVersion);

        // A THIRD read-only reader of the shared deselection document (SP-055 named two: the
        // DTRH host and the intake host). It is opened here rather than skipped so the flash
        // pool cannot become the one consumer that ignores an uncheck — the exact
        // two-scans-disagree defect that document was written to prevent. No writer is added.
        _assetSelection = new PersistenceStore<AssetSelectionDocument>(
            infra.OwnerFor("SessionAssetSelection"), infra.Log,
            Path.Combine(dataDirectory, AssetSelectionStore.FileName),
            AssetSelectionDocument.CurrentSchemaVersion);

        // The real clock reports a faulting scheduled callback to the host log instead of letting
        // it kill the process from a pool thread (SP-101 — see SystemSessionClock).
        var sessionClock = clock ?? new SystemSessionClock(ex => infra.Log.Log(
            $"session-clock: a scheduled module callback faulted and was contained — "
            + $"{ex.GetType().Name}: {ex.Message}"));

        // SP-101: every module's Changed goes through ONE signal, so the marshalling is the
        // producer's duty and not fifteen consumers'. See EffectSignal. The thread test is a
        // parameter because the pure-logic test project has no Avalonia runtime to ask.
        var signal = new EffectSignal(infra.UiDispatch, onSignalThread);

        // The one surface-thread dispatch both presenters share. Skip-until-bound is applied here,
        // once, rather than inside each presenter.
        void Dispatch(Action action)
        {
            if (infra.UiDispatch.IsBound)
            {
                infra.UiDispatch.Post(action);
            }
        }

        // SP-100: where a flash goes. The presenter is built here rather than in the composition
        // root because it needs the SAME clock the effect paces on (the stagger, the per-surface
        // lifetime and WPF's topmost cadence all ride it, so a test drives every one of them with
        // no wall-clock wait) and the SAME dispatch boundary the effect projects through (a native
        // window belongs to the thread that made it). Nothing is created until a flash really
        // draws: the factory inside it runs on the first surface, so a session that never flashes
        // — and every headless or unit run, where the boundary is unbound and the projection is
        // skipped — creates no window at all.
        _surface = surface ?? FlashSurfacePresenter.Product(sessionClock, Dispatch);
        _subliminalSurface = subliminalSurface ?? SubliminalSurfacePresenter.Product(sessionClock, Dispatch);

        // SP-105: the continuous module's surface. It takes the same clock as the other two, and
        // for one reason only — the topmost cadence WPF spends on a layer that is up for a whole
        // session (OverlayService.cs:666-671). The module itself has no clock at all.
        _pinkFilterSurface = pinkFilterSurface ?? PinkFilterSurfacePresenter.Product(sessionClock, Dispatch);

        // SP-106: the moving module's surface. It takes the same clock for TWO cadences — the
        // topmost kick every continuous layer needs, and the GIF's own frame advance — and the
        // module itself still has no clock at all. That split is this packet's whole finding: see
        // SpiralSurfacePresenter's remarks.
        _spiralSurface = spiralSurface ?? SpiralSurfacePresenter.Product(sessionClock, Dispatch);

        Flash = new FlashImagesEffect(
            infra.OwnerFor("FlashImages"),
            signal,
            sessionClock,
            pool ?? new FlashImagePool(
                AssetsRootFor(dataDirectory),
                () => (_assetSelection.Current.DisabledAssetPaths, _assetSelection.Current.UseAssetWhitelist)),
            _preset,
            random: null,
            surface: _surface);

        Subliminals = new SubliminalsEffect(
            infra.OwnerFor("Subliminals"),
            signal,
            sessionClock,
            new SubliminalPhrasePool(_subliminalPreset),
            _subliminalPreset,
            random: null,
            surface: _subliminalSurface);

        Spiral = new SpiralOverlayEffect(
            infra.OwnerFor("SpiralOverlay"),
            signal,
            _spiralPreset,
            // Re-resolved on every engage, as WPF re-resolves inside its own reconcile
            // (OverlayService.cs:437): a spiral dropped into the folder mid-session is picked up at
            // the next gesture rather than at the next launch.
            () => SpiralLibrary.Resolve(AssetsRootFor(dataDirectory), _spiralPreset.Current.Path),
            _spiralSurface);

        PinkFilter = new PinkFilterEffect(
            infra.OwnerFor("PinkFilter"),
            signal,
            _pinkFilterPreset,
            _pinkFilterSurface);

        // SP-108: the first ported module that draws nothing. It takes NO surface and no presenter —
        // there is nothing to present — and it takes the same session clock the other modules' work
        // rides, because its 2 s progress sample is its own (WPF's _rampTimer,
        // MainWindow/MainWindow.StartStop.cs:426-431) and there is no surface to keep it in.
        //
        // Its dials are the two the port really has. WPF links five (AppSettings.cs:2589-2621); flash
        // opacity, master volume and subliminal volume have no dial on any ported panel, so they are
        // absent rather than present-and-inert (D93). The list is built HERE because the composition
        // root is the only thing that knows which modules exist — the ramp itself knows nothing about
        // spirals or tints, which is what lets it be exercised with no surface anywhere.
        // SP-109: the AUDIO capability. Built HERE, once, and SHARED by both audio modules — a
        // second presence would be a second device open on the same endpoint, and each module's
        // stop-replace already has its own slot inside the one presence (keyed by module id), which
        // is what upstream gets from having two separate services with one player field each.
        //
        // Selection is by platform and selection is NEVER availability: on Linux the factory hands
        // back a typed refusal naming the manual gate, and on Windows the presence still has to earn
        // Available from the operating system before either module's dot may light
        // (runtime-capability-contract §2 rule 2; Audio/AudioPresenceFactory.cs).
        _audio = audio ?? AudioPresenceFactory.Create(message => infra.Log.Log(message));

        Ramp = new IntensityRampEffect(
            infra.OwnerFor("IntensityRamp"),
            signal,
            sessionClock,
            _rampPreset,
            [
                new SpiralOpacityDial(_spiralPreset, Spiral),
                new PinkFilterOpacityDial(_pinkFilterPreset, PinkFilter),
            ],
            // WPF wraps the tick's dial writes in Dispatcher.Invoke (MainWindow.StartStop.cs:504).
            // Here only the half that touches a LIVE surface goes through the dispatch; the persisted
            // half is synchronous so a restore survives a teardown whose dispatcher is already down.
            Dispatch);

        // SP-109: the first two modules whose output is not on the screen at all. They take the ONE
        // shared audio presence and their own clip folder under the same user-media root every other
        // pool reads from. Neither takes a surface: there is nothing to draw.
        MindWipe = new MindWipeEffect(
            infra.OwnerFor("MindWipe"),
            signal,
            sessionClock,
            mindWipeClips ?? new AudioCuePool(AssetsRootFor(dataDirectory), MindWipeEffect.ClipFolderName),
            _audio,
            _mindWipePreset);

        // HALF a row, deliberately and permanently: upstream's same flag also drives a desktop-wide
        // blur this port cannot draw (OverlayService.cs:382-386, :1965-1995). The row's title, its
        // panel notice and its arm result all say so — see BrainDrainEffect.
        BrainDrain = new BrainDrainEffect(
            infra.OwnerFor("BrainDrain"),
            signal,
            sessionClock,
            brainDrainClips ?? new AudioCuePool(AssetsRootFor(dataDirectory), BrainDrainEffect.ClipFolderName),
            _audio,
            _brainDrainPreset);

        // Rack order is WPF's (StudioTabView.xaml.cs:484-493), and it is also the order StartEngine
        // arms in — flash first (MainWindow.StartStop.cs:178), then subliminals (:186), then the
        // overlay service that owns the continuous pair (:192-193). Mandatory Video sits between
        // them upstream and is not ported, so the four ported modules are adjacent here; the ORDER
        // between them is upstream's and is what this list encodes.
        //
        // Spiral Overlay and Pink Filter are ONE service upstream and are armed by one call
        // (MainWindow.StartStop.cs:192-193 -> OverlayService.Start). Inside it the tint is started
        // first and the spiral second (OverlayService.cs:371-381), which is the OPPOSITE of the
        // rack's order — so there is no single upstream order to copy and the RACK's is taken, on
        // the ground that the rack is the order the user has learned and the two are independent
        // full-screen layers whose start order nothing observable depends on. Recorded as D90.
        //
        // SP-108 adds the fifth, and it goes LAST for two reasons that agree: WPF's rack puts the
        // TIMING group after EFFECTS, GAMES & CARDS and IMMERSION (StudioTabView.xaml.cs:482-541),
        // and StartEngine starts the ramp timer after every effect service (:265-269). Arming it last
        // also means that at STOP the dials it gives back belong to modules that have already been
        // disarmed, so the restore is a settings write with nothing live behind it.
        //
        // SP-109 inserts the two IMMERSION rows BETWEEN the effects and the ramp, which is where both
        // orders that matter put them, and they agree: upstream's rack puts IMMERSION after EFFECTS
        // and GAMES & CARDS and before TIMING (StudioTabView.xaml.cs:482-541), and StartEngine starts
        // Mind Wipe (:229-230) and Brain Drain (:241-244) after every effect service and before the
        // ramp timer (:265-269). Mind Wipe first, Brain Drain second, upstream's own order in both.
        Engine = new SessionEngine(
            [Flash, Subliminals, Spiral, PinkFilter, MindWipe, BrainDrain, Ramp], _preset, signal);

        // WPF's ramp ends the session itself when the user asked it to (MainWindow.StartStop.cs:547-555
        // calls StopEngine()). A module cannot call the engine that owns it without closing the cycle,
        // so the module raises and the composition root — the only thing that knows a session exists —
        // makes the call. Stop() is idempotent and returns false when nothing is running.
        Ramp.Completed += () => Engine.Stop();
    }

    public string Name => "Session";

    public bool Running => _preset.Running;

    /// <summary>The session itself. The shell drives this; nothing else may.</summary>
    public SessionEngine Engine { get; }

    /// <summary>Flash Images (public so the Studio module panel and the tests reach the real object).</summary>
    public FlashImagesEffect Flash { get; }

    /// <summary>Subliminals, the second ported module (SP-101). Public for the same reason.</summary>
    public SubliminalsEffect Subliminals { get; }

    /// <summary>Pink Filter, the first CONTINUOUS module (SP-105). Public for the same reason.</summary>
    public PinkFilterEffect PinkFilter { get; }

    /// <summary>Spiral Overlay, the first MOVING module (SP-106). Public for the same reason.</summary>
    public SpiralOverlayEffect Spiral { get; }

    /// <summary>Intensity Ramp, the first module that draws NOTHING (SP-108). Public for the same
    /// reason, and it has no surface property beside it because it has no surface.</summary>
    public IntensityRampEffect Ramp { get; }

    /// <summary>Mind Wipe, the first module the user HEARS rather than sees (SP-109).</summary>
    public MindWipeEffect MindWipe { get; }

    /// <summary>Brain Drain's AUDIO half — half a row, permanently, and it says so (SP-109).</summary>
    public BrainDrainEffect BrainDrain { get; }

    /// <summary>
    /// The audio capability both audio modules play through. Public for the same reason every surface
    /// is: a capability nobody can reach is a capability nobody can interrogate — and this is the one
    /// whose <c>Available</c> is earned from the operating system rather than from a call returning.
    /// </summary>
    public IAudioPresence Audio => _audio;

    /// <summary>Where its flashes are drawn. Public for the same reason: a surface nobody can
    /// reach is a surface nobody can interrogate.</summary>
    public IFlashSurface Surface => _surface;

    /// <summary>Where its subliminals are drawn.</summary>
    public ISubliminalSurface SubliminalSurface => _subliminalSurface;

    /// <summary>Where its tint is drawn.</summary>
    public IPinkFilterSurface PinkFilterSurface => _pinkFilterSurface;

    /// <summary>Where its spiral is drawn.</summary>
    public ISpiralSurface SpiralSurface => _spiralSurface;

    /// <summary>The persisted preset store (public so a surface can save a dial it moved).</summary>
    public PersistenceStore<SessionPresetDocument> Preset => _preset;

    /// <summary>The Subliminals module's persisted store, same reason.</summary>
    public PersistenceStore<SubliminalPresetDocument> SubliminalPreset => _subliminalPreset;

    /// <summary>The Pink Filter module's persisted store, same reason.</summary>
    public PersistenceStore<PinkFilterPresetDocument> PinkFilterPreset => _pinkFilterPreset;

    /// <summary>The Spiral Overlay module's persisted store, same reason.</summary>
    public PersistenceStore<SpiralPresetDocument> SpiralPreset => _spiralPreset;

    /// <summary>The Intensity Ramp module's persisted store, same reason.</summary>
    public PersistenceStore<IntensityRampPresetDocument> RampPreset => _rampPreset;

    /// <summary>The Mind Wipe module's persisted store, same reason.</summary>
    public PersistenceStore<MindWipePresetDocument> MindWipePreset => _mindWipePreset;

    /// <summary>The Brain Drain module's persisted store, same reason.</summary>
    public PersistenceStore<BrainDrainPresetDocument> BrainDrainPreset => _brainDrainPreset;

    /// <summary>Where the spiral library lives. The module panel shows this so an empty library has
    /// an answer to "where do I put one", exactly as the flash panel names its images folder.</summary>
    public string SpiralsFolder => SpiralLibrary.Folder(AssetsRootFor(_dataDirectory));

    /// <summary>Where the flash pool reads the user's images from. The module panel shows this
    /// so an empty pool has an answer to "where do I put them", which WPF's own comment calls
    /// its most common first-run dead end (<c>FlashService.cs:589-597</c>).</summary>
    public string ImagesFolder { get; }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _preset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _subliminalPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _pinkFilterPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _spiralPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _rampPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _mindWipePreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _brainDrainPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _assetSelection.StartAsync(cancellationToken).ConfigureAwait(false);

        // Typed Degraded, never silent: a quarantined or newer-schema preset means the module runs
        // on defaults, and the user is entitled to know that before they press START. Per document,
        // because per document is the blast radius — one module's broken file no longer takes every
        // other module's dials to defaults.
        LogIfDegraded("session-preset", _preset.LastLoadOutcome);
        LogIfDegraded("subliminal-preset", _subliminalPreset.LastLoadOutcome);
        LogIfDegraded("pinkfilter-preset", _pinkFilterPreset.LastLoadOutcome);
        LogIfDegraded("spiral-preset", _spiralPreset.LastLoadOutcome);
        LogIfDegraded("ramp-preset", _rampPreset.LastLoadOutcome);
        LogIfDegraded("mindwipe-preset", _mindWipePreset.LastLoadOutcome);
        LogIfDegraded("braindrain-preset", _brainDrainPreset.LastLoadOutcome);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para><b>The surfaces' teardown goes through the dispatch boundary, not down this thread.</b>
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
    /// with one line above: <c>Engine.Stop()</c> disarms every module, and disarm posts
    /// <c>HideAll</c> from the UI thread the user pressed STOP on. The visible-stop guarantee rests
    /// on that, never on process death.</para>
    /// </remarks>
    public Task StopAsync()
    {
        // Stop the session first: its disarm is what takes any live surface off the user's screen
        // (WPF closes every flash window on stop, Services/Flash/FlashService.cs:3878-3884, and
        // blanks every subliminal card, Services/Subliminal/SubliminalService.cs:116-127).
        Engine.Stop();
        DisposeSurface(_surface);
        DisposeSurface(_subliminalSurface);
        DisposeSurface(_pinkFilterSurface);
        DisposeSurface(_spiralSurface);

        // The audio presence goes down HERE, inline, and not through the dispatch boundary the
        // surfaces use. A native window belongs to the thread that made it; an audio device does not
        // — upstream says the same of NAudio and deliberately tears audio down inline from its panic
        // path for exactly that reason (Services/LockCard/MindWipeService.cs:243-251). Disposing it
        // is also what drops this process's render session to AudioSessionStateInactive, which is the
        // OS-observable far end of the chain this capability claims.
        _audio.Dispose();

        return Task.WhenAll(
            _preset.StopAsync(), _subliminalPreset.StopAsync(), _pinkFilterPreset.StopAsync(),
            _spiralPreset.StopAsync(), _rampPreset.StopAsync(), _mindWipePreset.StopAsync(),
            _brainDrainPreset.StopAsync(), _assetSelection.StopAsync());
    }

    /// <summary>Teardown flush for the reserved pre-drain slot (persistence contract §11). The
    /// asset selection has no writer, so it has nothing to flush.</summary>
    public Task FlushAsync(TimeSpan boundedWait) =>
        Task.WhenAll(
            _preset.FlushAsync(boundedWait),
            _subliminalPreset.FlushAsync(boundedWait),
            _pinkFilterPreset.FlushAsync(boundedWait),
            _spiralPreset.FlushAsync(boundedWait),
            // The ramp's own dials. The dials it BORROWS are restored synchronously by
            // Engine.Stop() above, so no flush here can ever write a ramped value over the user's.
            _rampPreset.FlushAsync(boundedWait),
            _mindWipePreset.FlushAsync(boundedWait),
            _brainDrainPreset.FlushAsync(boundedWait));

    private void LogIfDegraded(string label, LoadOutcome? outcome)
    {
        if (outcome is { IsDegraded: true })
        {
            _log.Log($"{label}: load → {outcome.GetType().Name} (typed Degraded — the module will run on default dials)");
        }
    }

    private void DisposeSurface(object surface)
    {
        if (surface is not IDisposable disposable)
        {
            return;
        }

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
}
