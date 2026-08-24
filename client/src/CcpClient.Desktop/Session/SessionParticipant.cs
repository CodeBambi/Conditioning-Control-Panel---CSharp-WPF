using CcpClient.Desktop.Audio;
using CcpClient.Desktop.Effects;
using CcpClient.Desktop.Features.Dtrh;
using CcpClient.Desktop.Haptics;
using CcpClient.Desktop.Input;
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
/// (contract §4.4).</para>
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
    private readonly PersistenceStore<LockCardPresetDocument> _lockCardPreset;
    private readonly PersistenceStore<MandatoryVideoPresetDocument> _videoPreset;
    private readonly PersistenceStore<BubbleCountPresetDocument> _bubbleCountPreset;
    private readonly PersistenceStore<BubblePopPresetDocument> _bubblePopPreset;
    private readonly PersistenceStore<PopQuizPresetDocument> _popQuizPreset;
    private readonly PersistenceStore<BouncingTextPresetDocument> _bouncingTextPreset;
    private readonly PersistenceStore<VisualsPresetDocument> _visualsPreset;
    private readonly PersistenceStore<AssetSelectionDocument> _assetSelection;
    private readonly ILogSink _log;
    private readonly IFlashSurface _surface;
    private readonly ISubliminalSurface _subliminalSurface;
    private readonly IPinkFilterSurface _pinkFilterSurface;
    private readonly ISpiralSurface _spiralSurface;
    private readonly IAudioPresence _audio;
    private readonly IInputPresence _input;
    private readonly IVideoSurface _videoSurface;
    private readonly IBubblePopSurface _bubblePopSurface;
    private readonly IBouncingTextSurface _bouncingTextSurface;
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
        IAudioCuePool? brainDrainClips = null,
        Random? mindWipeRandom = null,
        Random? brainDrainRandom = null,
        IInputPresence? input = null,
        ILockCardPhrasePool? lockCardPhrases = null,
        Random? lockCardRandom = null,
        Func<InputBounds>? lockCardPlacement = null,
        IVideoSurface? videoSurface = null,
        IVideoClipPool? videoClips = null,
        Random? videoRandom = null,
        IVideoClipPool? bubbleCountClips = null,
        Random? bubbleCountRandom = null,
        Func<InputBounds>? bubbleCountPlacement = null,
        Random? popQuizRandom = null,
        Func<InputBounds>? popQuizPlacement = null,
        IBubblePopSurface? bubblePopSurface = null,
        IBouncingTextSurface? bouncingTextSurface = null,
        IHapticLimb? haptics = null,
        IScriptedClock? scriptedClock = null,
        AudioParticipant? appAudio = null)
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

        // The Subliminals module's own document. One per module rather than more members on
        // the shared preset — see SubliminalPresetDocument for why, and for the File Scope fact that
        // is the other half of the reason.
        _subliminalPreset = new PersistenceStore<SubliminalPresetDocument>(
            infra.OwnerFor("SubliminalPreset"), infra.Log,
            Path.Combine(dataDirectory, SubliminalPresetDocument.FileName),
            SubliminalPresetDocument.CurrentSchemaVersion);

        // The Pink Filter module's own document, on the same per-module precedent (D71).
        _pinkFilterPreset = new PersistenceStore<PinkFilterPresetDocument>(
            infra.OwnerFor("PinkFilterPreset"), infra.Log,
            Path.Combine(dataDirectory, PinkFilterPresetDocument.FileName),
            PinkFilterPresetDocument.CurrentSchemaVersion);

        // The moving module's own document, on the same per-module precedent (D71/D80).
        _spiralPreset = new PersistenceStore<SpiralPresetDocument>(
            infra.OwnerFor("SpiralPreset"), infra.Log,
            Path.Combine(dataDirectory, SpiralPresetDocument.FileName),
            SpiralPresetDocument.CurrentSchemaVersion);

        // The non-drawing module's own document, same precedent again.
        _rampPreset = new PersistenceStore<IntensityRampPresetDocument>(
            infra.OwnerFor("IntensityRampPreset"), infra.Log,
            Path.Combine(dataDirectory, IntensityRampPresetDocument.FileName),
            IntensityRampPresetDocument.CurrentSchemaVersion);

        // The two AUDIO modules' own documents, same precedent again. Two documents rather
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

        // The first module that ASKS the user something, and its own document on the same
        // precedent. It carries a phrase POOL as well as dials, which no earlier module's document
        // does — and that is the whole reason it must not be merged into a shared file: a hand-broken
        // phrase list would otherwise take five other modules' dials to defaults with it.
        _lockCardPreset = new PersistenceStore<LockCardPresetDocument>(
            infra.OwnerFor("LockCardPreset"), infra.Log,
            Path.Combine(dataDirectory, LockCardPresetDocument.FileName),
            LockCardPresetDocument.CurrentSchemaVersion);

        _videoPreset = new PersistenceStore<MandatoryVideoPresetDocument>(
            infra.OwnerFor("MandatoryVideoPreset"), infra.Log,
            Path.Combine(dataDirectory, MandatoryVideoPresetDocument.FileName),
            MandatoryVideoPresetDocument.CurrentSchemaVersion);

        // The second consumer's own document, same per-module precedent again (D71/D80).
        _bubbleCountPreset = new PersistenceStore<BubbleCountPresetDocument>(
            infra.OwnerFor("BubbleCountPreset"), infra.Log,
            Path.Combine(dataDirectory, BubbleCountPresetDocument.FileName),
            BubbleCountPresetDocument.CurrentSchemaVersion);

        // The pointer module's own document, same per-module precedent again.
        _bubblePopPreset = new PersistenceStore<BubblePopPresetDocument>(
            infra.OwnerFor("BubblePopPreset"), infra.Log,
            Path.Combine(dataDirectory, BubblePopPresetDocument.FileName),
            BubblePopPresetDocument.CurrentSchemaVersion);

        // The asking module's own document, same per-module precedent again — and it is the one
        // document in this list that sits beside its MODULE rather than in this folder
        // (Effects/PopQuizPresetDocument.cs), because Session/ was outside the file scope of the
        // packet that wrote it. The store is generic over the model and cares about neither.
        _popQuizPreset = new PersistenceStore<PopQuizPresetDocument>(
            infra.OwnerFor("PopQuizPreset"), infra.Log,
            Path.Combine(dataDirectory, PopQuizPresetDocument.FileName),
            PopQuizPresetDocument.CurrentSchemaVersion);

        // Its own document, on the per-module precedent: the store's Degraded load path takes the
        // WHOLE document to defaults, so a shared file would let one broken value reset every other
        // module's dials.
        _bouncingTextPreset = new PersistenceStore<BouncingTextPresetDocument>(
            infra.OwnerFor("BouncingTextPreset"), infra.Log,
            Path.Combine(dataDirectory, BouncingTextPresetDocument.FileName),
            BouncingTextPresetDocument.CurrentSchemaVersion);

        // The Visuals row's document — the Flash Images module's three DRAW dials. Same
        // per-module precedent again (D71), and the same reason it is a document at all rather than
        // three more members on the shared session preset: SessionPresetDocument's own remarks said
        // these values "arrive with the surface that honours them", and the surface arrived at
        // the packet that made flashes draw.
        _visualsPreset = new PersistenceStore<VisualsPresetDocument>(
            infra.OwnerFor("VisualsPreset"), infra.Log,
            Path.Combine(dataDirectory, VisualsPresetDocument.FileName),
            VisualsPresetDocument.CurrentSchemaVersion);

        // Not an ISessionEffect, deliberately: upstream's Visuals is a settings page with no
        // service, no enable and no dot (Views/Tabs/StudioTabView.xaml.cs:494-496), so the port's
        // is an owner of three dials and nothing more. See VisualsDials.
        Visuals = new VisualsDials(_visualsPreset);

        // A THIRD read-only reader of the shared deselection document (two were already named: the
        // DTRH host and the intake host). It is opened here rather than skipped so the flash
        // pool cannot become the one consumer that ignores an uncheck — the exact
        // two-scans-disagree defect that document was written to prevent. No writer is added.
        _assetSelection = new PersistenceStore<AssetSelectionDocument>(
            infra.OwnerFor("SessionAssetSelection"), infra.Log,
            Path.Combine(dataDirectory, AssetSelectionStore.FileName),
            AssetSelectionDocument.CurrentSchemaVersion);

        // The real clock reports a faulting scheduled callback to the host log instead of letting
        // it kill the process from a pool thread (see SystemSessionClock).
        var sessionClock = clock ?? new SystemSessionClock(ex => infra.Log.Log(
            $"session-clock: a scheduled module callback faulted and was contained — "
            + $"{ex.GetType().Name}: {ex.Message}"));

        // Every module's Changed goes through ONE signal, so the marshalling is the
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

        // Where a flash goes. The presenter is built here rather than in the composition
        // root because it needs the SAME clock the effect paces on (the stagger, the per-surface
        // lifetime and WPF's topmost cadence all ride it, so a test drives every one of them with
        // no wall-clock wait) and the SAME dispatch boundary the effect projects through (a native
        // window belongs to the thread that made it). Nothing is created until a flash really
        // draws: the factory inside it runs on the first surface, so a session that never flashes
        // — and every headless or unit run, where the boundary is unbound and the projection is
        // skipped — creates no window at all.
        // The presenter now PULLS the Visuals row's three dials at the moment a flash is
        // shown, instead of drawing with three constants. A build with no document draws with
        // FlashDraw.Defaults, which are the same three numbers it used before.
        // The haptic limb reaches FIVE statements in this composition and no others — the
        // census's five port trigger points (client/docs/haptic-limb-census.md §3.1). It is OWNED by
        // the app-scoped HapticParticipant, beside the sink it drives, and threaded here rather than
        // constructed here: a session that built its own would be a SECOND sink the day a provider
        // route is admitted, which is two clients against one server.
        Haptics = haptics;

        // THE SOUND THREE OF THESE MODULES MAKE, on the app-wide arbitration. It is threaded here
        // rather than built here for the reason the haptic limb above is: the arbitration is ONE
        // object owned by AudioParticipant, and a session that constructed its own would be a second
        // native engine on the same endpoint — the exact defect Audio/AudioParticipant.cs:10-18
        // exists to remove. Null in an owner-less host, and every module below then has no clip path
        // at all rather than a silent one.
        //
        // Construction opens NO device and reads no folder: the pools are lazy and
        // EffectSounds calls EnsureDevice on the first play that really has a clip to play. So a
        // session that never flashes and never pops still never seizes a render endpoint, which is
        // the property AudioParticipant.DeviceInitAttempts exists to keep honest.
        Sounds = appAudio is null
            ? null
            : EffectSounds.ForProduct(appAudio, AssetsRootFor(dataDirectory), infra.Log.Log);

        _surface = surface ?? FlashSurfacePresenter.Product(
            sessionClock, Dispatch, draw: Visuals.Draw, haptics: haptics);
        _subliminalSurface = subliminalSurface ?? SubliminalSurfacePresenter.Product(sessionClock, Dispatch);

        // The continuous module's surface. It takes the same clock as the other two, and
        // for one reason only — the topmost cadence WPF spends on a layer that is up for a whole
        // session (OverlayService.cs:666-671). The module itself has no clock at all.
        _pinkFilterSurface = pinkFilterSurface ?? PinkFilterSurfacePresenter.Product(sessionClock, Dispatch);

        // The moving module's surface. It takes the same clock for TWO cadences — the
        // topmost kick every continuous layer needs, and the GIF's own frame advance — and the
        // module itself still has no clock at all. That split is this packet's whole finding: see
        // SpiralSurfacePresenter's remarks.
        _spiralSurface = spiralSurface ?? SpiralSurfacePresenter.Product(sessionClock, Dispatch);

        // The video surface. It takes the same clock as the other presenters, and for the
        // same kind of reason — a cadence that keeps a SURFACE correct is the surface's — but this
        // is the first module in the port that needs BOTH clocks: the FRAME advance lives here and
        // the FIRING interval lives on the module. It owns its own hwnd rather than drawing through
        // the overlay, because the capability's whole content is the operating system's answer about
        // frames on THAT surface, and IOverlayPresence cannot be asked it. Overlay/** is CONSUMED
        // (its display enumeration) and never edited.
        _videoSurface = videoSurface ?? VideoSurfacePresenter.Product(sessionClock, Dispatch);

        // The pointer surface. It takes the same clock as the other presenters and carries
        // BOTH of its module's cadences — the spawn interval and the 30 ms animation step — because
        // both keep a SURFACE correct rather than deciding when a module is due, so the module takes
        // no clock at all (the established rule, fourth application). It is a NEW capability rather than a
        // second consumer of Input/**: that presence handles no mouse message, owns one window and
        // places it once (the earlier census §1, re-verified in this packet's plan §1).
        // The pop SOUND is the surface's to raise and not the module's, because the surface is
        // where a click becomes a pop: BubblePopField.Hit answers false for a bubble already
        // popping, and upstream's own sound sits behind that same guard (Services/BubbleService.cs:3994
        // returns before ever reaching PlayPopSound at :961).
        _bubblePopSurface = bubblePopSurface ?? BubblePopSurfacePresenter.ForProduct(
            sessionClock, Dispatch, Sounds is null ? null : Sounds.Pop);

        // The per-pixel-alpha surface. Its cadence lives in the presenter for the established
        // reason - a cadence that keeps a SURFACE correct is the surface's - and the module itself
        // takes no clock at all.
        _bouncingTextSurface = bouncingTextSurface ?? BouncingTextSurfacePresenter.Product(sessionClock, Dispatch, haptics);

        Flash = new FlashImagesEffect(
            infra.OwnerFor("FlashImages"),
            signal,
            sessionClock,
            pool ?? new FlashImagePool(
                AssetsRootFor(dataDirectory),
                () => (_assetSelection.Current.DisabledAssetPaths, _assetSelection.Current.UseAssetWhitelist)),
            _preset,
            random: null,
            surface: _surface,
            sounds: Sounds);

        // The first module that plays a FILE. It sits between Flash Images and Subliminals
        // because both orders that matter agree — WPF's rack is Flash Images, Mandatory Video,
        // Subliminals (StudioTabView.xaml.cs:483-488) and StartEngine starts the flash service
        // (MainWindow.StartStop.cs:180-181), then the video service (:183-184), then subliminals
        // (:186-187). It ships as its VIDEO half: the clip's sound is not ported, and the row title,
        // the panel and its arm result all say so.
        MandatoryVideo = new MandatoryVideoEffect(
            infra.OwnerFor("MandatoryVideo"),
            signal,
            sessionClock,
            _videoPreset,
            videoClips ?? new VideoClipPool(AssetsRootFor(dataDirectory)),
            _videoSurface,
            // Injectable for the same reason every other module's Random is: the SPACING is what a
            // fact has to make deterministic, and the arithmetic it is fed into must stay the
            // module's own rather than a number a test double re-derives.
            videoRandom,
            haptics);

        Subliminals = new SubliminalsEffect(
            infra.OwnerFor("Subliminals"),
            signal,
            sessionClock,
            new SubliminalPhrasePool(_subliminalPreset),
            _subliminalPreset,
            random: null,
            surface: _subliminalSurface,
            haptics: haptics);

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

        // The first ported module that draws nothing. It takes NO surface and no presenter —
        // there is nothing to present — and it takes the same session clock the other modules' work
        // rides, because its 2 s progress sample is its own (WPF's _rampTimer,
        // MainWindow/MainWindow.StartStop.cs:426-431) and there is no surface to keep it in.
        //
        // Its dials are the three the port really has. WPF links five (AppSettings.cs:2589-2621);
        // the flash-opacity one was closed by giving flash opacity a dial on a ported panel,
        // which is exactly the condition D93 named. Master volume and subliminal volume have no
        // dial on any ported panel, so they are still absent rather than present-and-inert. The
        // list is built HERE because the composition root is the only thing that knows which
        // modules exist — the ramp itself knows nothing about spirals or tints, which is what lets
        // it be exercised with no surface anywhere.
        // The AUDIO capability. Built HERE, once, and SHARED by both audio modules — a
        // second presence would be a second device open on the same endpoint, and each module's
        // stop-replace already has its own slot inside the one presence (keyed by module id), which
        // is what upstream gets from having two separate services with one player field each.
        //
        // Selection is by platform and selection is NEVER availability: on Linux the factory hands
        // back a typed refusal naming the manual gate, and on Windows the presence still has to earn
        // Available from the operating system before either module's dot may light
        // (runtime-capability-contract §2 rule 2; Audio/AudioPresenceFactory.cs).
        _audio = audio ?? AudioPresenceFactory.Create(message => infra.Log.Log(message));

        // The INPUT capability. Built HERE, once, for the same reason the audio presence is:
        // it owns a native window, and a second presence would be a second window competing for the
        // one foreground the OS lends. Selection is by platform and selection is NEVER availability
        // — on Linux the factory hands back a typed refusal naming the manual gate, and on Windows
        // the presence still has to earn Available from the operating system's own foreground and
        // keyboard focus before the module's dot may light (runtime-capability-contract §2 rule 2;
        // Input/InputPresenceFactory.cs).
        //
        // Nothing is created on screen by this line: the presence builds its window lazily, on the
        // first card, so a session that never asks anything — and every headless or unit run —
        // creates no window at all. That is the same lazy-surface discipline the drawing modules'
        // presenters already follow.
        _input = input ?? InputPresenceFactory.Create();

        Ramp = new IntensityRampEffect(
            infra.OwnerFor("IntensityRamp"),
            signal,
            sessionClock,
            _rampPreset,
            [
                new SpiralOpacityDial(_spiralPreset, Spiral),
                new PinkFilterOpacityDial(_pinkFilterPreset, PinkFilter),
                // WPF's FIRST link (MainWindow.StartStop.cs:506-510), and the comment above
                // named the exact condition for it — "no dial on any ported panel". The Visuals row
                // is that panel. Master volume and subliminal volume are still absent because their
                // dials still are, so this list is three of WPF's five and says which three.
                new FlashOpacityDial(Visuals),
            ],
            // WPF wraps the tick's dial writes in Dispatcher.Invoke (MainWindow.StartStop.cs:504).
            // Here only the half that touches a LIVE surface goes through the dispatch; the persisted
            // half is synchronous so a restore survives a teardown whose dispatcher is already down.
            Dispatch,
            // THE STAND-DOWN'S ONE WIRE (MainWindow/MainWindow.StartStop.cs:492). It is a closure
            // rather than the object because the ramp is composed HERE, above, and the scripted run
            // below it — and the null test is upstream's own `_sessionEngine?.IsRunning == true`
            // rather than an ordering assumption: a tick that somehow arrived between these two
            // lines reads "no session", which is the truth at that instant.
            () => Scripted is { Running: true });

        // The first two modules whose output is not on the screen at all. They take the ONE
        // shared audio presence and their own clip folder under the same user-media root every other
        // pool reads from. Neither takes a surface: there is nothing to draw.
        MindWipe = new MindWipeEffect(
            infra.OwnerFor("MindWipe"),
            signal,
            sessionClock,
            mindWipeClips ?? new AudioCuePool(AssetsRootFor(dataDirectory), MindWipeEffect.ClipFolderName),
            _audio,
            _mindWipePreset,
            // Injectable for the same reason the other four modules' Random is: the ROLL is what a
            // fact has to make deterministic, and the PROBABILITY it is compared against must stay
            // the module's own arithmetic rather than a number a test double re-derives.
            mindWipeRandom);

        // HALF a row, deliberately and permanently: upstream's same flag also drives a desktop-wide
        // blur this port cannot draw (OverlayService.cs:382-386, :1965-1995). The row's title, its
        // panel notice and its arm result all say so — see BrainDrainEffect.
        BrainDrain = new BrainDrainEffect(
            infra.OwnerFor("BrainDrain"),
            signal,
            sessionClock,
            brainDrainClips ?? new AudioCuePool(AssetsRootFor(dataDirectory), BrainDrainEffect.ClipFolderName),
            _audio,
            _brainDrainPreset,
            brainDrainRandom);

        // The first module that asks the user something. It takes the ONE shared input
        // presence and its own phrase pool, and no surface: what it puts on screen is the capability's
        // window, not a drawing surface, and the distinction is why it consumes IInputPresence rather
        // than IOverlayPresence — one of those exists to let clicks THROUGH and the other to catch
        // them.
        LockCard = new LockCardEffect(
            infra.OwnerFor("LockCard"),
            signal,
            sessionClock,
            lockCardPhrases ?? new LockCardPhrasePool(_lockCardPreset),
            _input,
            _lockCardPreset,
            // Injectable for the same reason every other module's Random is: the SPACING is what a
            // fact has to make deterministic, and the arithmetic it is fed into must stay the
            // module's own rather than a number a test double re-derives.
            lockCardRandom,
            lockCardPlacement);

        // THE SECOND CONSUMER, and the wiring is the whole point of the packet. It takes the
        // SAME video surface Mandatory Video plays on and the SAME input presence the Lock Card asks
        // through — one instance of each, shared, which is the arrangement already established for the
        // audio presence and for the same reason: a second video surface would be a second window
        // contesting the same rectangle, and a second input presence would be a second window
        // contesting the one foreground the OS lends. Sharing is also what makes the two video-class
        // rows mutually exclusive without this port having upstream's interaction queue
        // (Services/BubbleCountService.cs:169-186).
        //
        // Its clip POOL is its own instance over the same folder, because upstream's two services
        // keep their own lists over the same directory (BubbleCountService.cs:63 and
        // Services/Video/VideoService.cs:1212 both compose <assets>/videos) — one shared bag would
        // make one row's draw remove a clip from the other's.
        BubbleCount = new BubbleCountEffect(
            infra.OwnerFor("BubbleCount"),
            signal,
            sessionClock,
            _bubbleCountPreset,
            bubbleCountClips ?? new VideoClipPool(AssetsRootFor(dataDirectory)),
            _videoSurface,
            _input,
            bubbleCountRandom,
            bubbleCountPlacement,
            // The SAME pop clips the ambient game uses, out of the same pool instance: upstream's
            // two bubble modules read one shipped folder (Services/BubbleService.cs:1998 and
            // Windows/BubbleCountWindow.xaml.cs:1338 both compose sounds/bubbles) and neither deals
            // a bag, so one pool with replacement is exactly their arrangement.
            Sounds is null ? null : Sounds.Pop);

        // THE ASKING MODULE THAT WAS BUILT AND CONSTRUCTED BY NOTHING. It takes the SAME input
        // presence the Lock Card and Bubble Count ask through — one window contesting the one
        // foreground the OS lends — and its two upstream guards ("a pop quiz is already open",
        // Services/Quiz/PopQuizService.cs:183-187, and the lock-card cross-check at :194) are the
        // presence's own single-tenancy rather than two reads this composition has to arrange.
        //
        // NO XP LEDGER, and that is a REFUSAL WITH A REASON rather than an omission. Upstream banks
        // twenty-five on every answer (Windows/PopQuizWindow.xaml.cs:161) and the module will bank it
        // the moment it is handed a ledger. The port's ledger is opened per WINDOW and disposed with
        // it (Features/Progression/ProgressionLedger.Open's own remarks: "the three hosts that call
        // this are modal windows the user opens one at a time"), and every one of its three callers
        // holds it for the life of one modal. A session-lifetime FOURTH store over the same
        // progression.json would be a second WRITER open across those windows: Grant mutates its own
        // in-memory copy and saves (ProgressionLedger.Grant), so a quiz answered after a DTRH run had
        // banked would write this store's stale level and XP back over it. With no ledger the module
        // banks nothing and the card makes no XP claim at all — its own documented state, not a stub.
        PopQuiz = new PopQuizEffect(
            infra.OwnerFor("PopQuiz"),
            signal,
            sessionClock,
            _input,
            _popQuizPreset,
            xp: null,
            // Injectable for the same reason every other module's Random is: the SPACING, the DRAW
            // and the SHUFFLE are what a fact makes deterministic, and the arithmetic they feed
            // stays the module's own rather than a number a test double re-derives.
            popQuizRandom,
            popQuizPlacement);

        // The eleventh module, and the first the user is expected to ACT on. It goes FIRST
        // in GAMES & CARDS because that is where the rack puts it — Bubble Pop, Bubble Count, Lock
        // Card, Bouncing Text (StudioTabView.xaml.cs:499-505) — and upstream's StartEngine offers no
        // competing order for it: the ambient game is started by the dashboard card's own toggle and
        // by SessionEngine (Services/Session/SessionEngine.cs:444), not from StartEngine's effect sequence.
        // So there is no D90-style disagreement here and the rack's order stands unopposed. Its surface is
        // its OWN — no other row places a clickable target — and that is not the single-tenancy
        // shortcut an earlier review's §2.3 F5 warned about: the capability is keyed from birth, every operation
        // names a target, so a second consumer inherits ownership rather than having to guard for it.
        BubblePop = new BubblePopEffect(
            infra.OwnerFor("BubblePop"),
            signal,
            _bubblePopPreset,
            _bubblePopSurface);

        // The module blocked since wave 46, on a capability whose transparent pixels are
        // measured on the composited desktop rather than assumed.
        BouncingText = new BouncingTextEffect(
            infra.OwnerFor("BouncingText"),
            signal,
            _bouncingTextPreset,
            _bouncingTextSurface);

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
        // Adds the fifth, and it goes LAST for two reasons that agree: WPF's rack puts the
        // TIMING group after EFFECTS, GAMES & CARDS and IMMERSION (StudioTabView.xaml.cs:482-541),
        // and StartEngine starts the ramp timer after every effect service (:265-269). Arming it last
        // also means that at STOP the dials it gives back belong to modules that have already been
        // disarmed, so the restore is a settings write with nothing live behind it.
        //
        // Inserts the two IMMERSION rows BETWEEN the effects and the ramp, which is where both
        // orders that matter put them, and they agree: upstream's rack puts IMMERSION after EFFECTS
        // and GAMES & CARDS and before TIMING (StudioTabView.xaml.cs:482-541), and StartEngine starts
        // Mind Wipe (:229-230) and Brain Drain (:241-244) after every effect service and before the
        // ramp timer (:265-269). Mind Wipe first, Brain Drain second, upstream's own order in both.
        //
        // Inserts Lock Card BETWEEN the effects and the IMMERSION pair, which is where both
        // orders that matter put it and they agree: upstream's rack puts GAMES & CARDS after EFFECTS
        // and before IMMERSION (StudioTabView.xaml.cs:483/498/508/530), and StartEngine starts the
        // lock card (:206-209) after the overlay service and before Mind Wipe (:229-230). It is the
        // port's first GAMES & CARDS row, so the group opens with it.
        // Adds Pop Quiz BETWEEN Brain Drain and the ramp, and it is the one module here whose
        // position has only ONE upstream order to take rather than two that must be reconciled:
        // upstream has no Studio rack row for it at all (its dials live on the Graded Intake door,
        // Views/Tabs/GradedIntakeTabView.xaml:255-292), so StartEngine's order is unopposed —
        // the lock card at :206-209, Mind Wipe at :229-230, Brain Drain at :241-244, then pop quiz
        // at :255-258 and the ramp timer last at :265-269. Its RACK ROW sits in GAMES & CARDS beside
        // the other three cards, which is the port's own placement for a row upstream does not have;
        // the two orders therefore differ on purpose and neither is a D90 tie-break.
        // Inserts Mandatory Video BETWEEN Flash Images and Subliminals, which is where both
        // orders that matter put it and they agree: WPF's rack is Flash Images, Mandatory Video,
        // Subliminals, Spiral Overlay (StudioTabView.xaml.cs:483-491) and StartEngine starts the
        // flash service (:180-181), the video service (:183-184) and subliminals (:186-187) in that
        // order.
        Engine = new SessionEngine(
            [
                Flash, MandatoryVideo, Subliminals, Spiral, BouncingText, PinkFilter, BubblePop, BubbleCount,
                LockCard, MindWipe, BrainDrain, PopQuiz, Ramp,
            ],
            _preset, signal);

        // WPF's ramp ends the session itself when the user asked it to (MainWindow.StartStop.cs:547-555
        // calls StopEngine()). A module cannot call the engine that owns it without closing the cycle,
        // so the module raises and the composition root — the only thing that knows a session exists —
        // makes the call. Stop() is idempotent and returns false when nothing is running.
        Ramp.Completed += () => Engine.Stop();

        // THE SCRIPTED SESSION, composed HERE and nowhere else — the timed session with named
        // phases that runs ON TOP of the engine above (WPF's Services/Session/SessionEngine.cs:22,
        // reached from MainWindow/MainWindow.Presets.cs:1509-1518). It lands beside the engine
        // rather than in the shell for the reason MainWindow states about the engine itself: a
        // second one would borrow the user's dials twice and give them back once.
        //
        // Its eleven documents are the REAL stores every module above reads, never copies — see
        // ScriptedSessionDials — and its ramp-curve document is the twelfth, READ per tick and
        // never borrowed. It takes the same EffectSignal every module takes, so its per-second
        // notifications reach a UI thread the same way theirs do.
        Scripted = new ScriptedSessionRun(
            Engine,
            new ScriptedSessionDials(
                _preset, _visualsPreset, _subliminalPreset, _bouncingTextPreset, _pinkFilterPreset,
                _spiralPreset, _bubblePopPreset, _mindWipePreset, _videoPreset, _lockCardPreset,
                _bubbleCountPreset),
            scriptedClock ?? new SystemScriptedClock(
                ex => _log.Log($"scripted session: a tick faulted and was contained: {ex.Message}")),
            _rampPreset,
            signal);

        // THE POST-SESSION MEDIA LOG (WPF Services/Session/SessionLogService.cs), wired HERE
        // because this is the only object that holds all three parts of it: the run that says
        // whether a scripted session is up, and the two modules that put media on screen.
        //
        // UPSTREAM SUBSCRIBES AND UNSUBSCRIBES AROUND EACH SESSION (:150-171) AND ALSO GUARDS ON
        // `_activeLog == null` INSIDE EVERY HANDLER (:182, :214). The port keeps the second guard
        // and drops the first: `Scripted.Running` IS `_activeLog != null`, read from the one object
        // that knows, and a subscription never taken cannot be leaked — which is the whole job of
        // upstream's `_isSubscribed` flag. The user-visible rule is unchanged and it is upstream's:
        // media outside a scripted session is not logged, because upstream only ever begins a log
        // from the scripted engine's own start (Services/Session/SessionEngine.cs:266).
        //
        // The offsets are the RUN's reconciled elapsed rather than a second wall clock, so the
        // timeline in the recap and the countdown the user was watching cannot disagree; upstream
        // stamps its offsets from an unguarded `DateTime.Now` (:183-184, :218-219) and a clock jump
        // moves them.
        MediaLog = new ScriptedSessionLogStore(dataDirectory, infra.Log);

        // THE USER'S OWN SESSIONS, from the same data root as the logs above and the eleven preset
        // documents. Built here rather than in the composition root for the reason MediaLog is: the
        // rack that reads it and the editor that writes it must be looking at ONE folder, and the
        // only object both of them already have is this participant.
        CustomSessions = new CustomSessionStore(dataDirectory, infra.Log);
        Flash.Fired += flash =>
        {
            if (Scripted.Running)
            {
                MediaLog.RecordImages(flash.ImagesDrawn, Scripted.Elapsed);
            }
        };
        MandatoryVideo.Fired += _ =>
        {
            if (Scripted.Running)
            {
                MediaLog.RecordVideo(Scripted.Elapsed);
            }
        };

        // Upstream finalizes the log for BOTH endings — "Aborted sessions still get a log … so the
        // post-session dialog shows what played even when the user cut things short"
        // (Services/Session/SessionEngine.cs:420-424) — and the recap is driven off the log's own
        // LogReady rather than off the end of the session, so the window can never open on a log
        // that has not been written yet (MainWindow/MainWindow.xaml.cs:373-378).
        Scripted.Ended += outcome => MediaLog.Complete(outcome);
    }

    public string Name => "Session";

    public bool Running => _preset.Running;

    /// <summary>The session itself. The shell drives this; nothing else may.</summary>
    public SessionEngine Engine { get; }

    /// <summary>
    /// The SCRIPTED session — the timed one with named phases that runs on top of
    /// <see cref="Engine"/> (WPF <c>Services/Session/SessionEngine.cs:22</c>). Public for the same
    /// reason <see cref="Engine"/> is: the Studio rack's Scripted Sessions row drives THIS object,
    /// so the surface that starts a session and the run that borrows the user's dials are the same
    /// one.
    /// </summary>
    public ScriptedSessionRun Scripted { get; }

    /// <summary>
    /// The post-session media log — WPF's <c>Services/Session/SessionLogService.cs</c>. Public for
    /// the same reason <see cref="Scripted"/> is: the Studio door's <b>Recent sessions</b> button
    /// and the recap that opens when a session ends must read the SAME store the running session
    /// writes into, never a second reader of the same folder.
    /// </summary>
    public ScriptedSessionLogStore MediaLog { get; }

    /// <summary>
    /// The user's own scripted sessions on disk — upstream's <c>CustomSessions</c> folder
    /// (<c>Services/Session/SessionFileService.cs:27-34</c>). Public for the same reason
    /// <see cref="MediaLog"/> is: the Studio rack builds its catalogue out of it and the session
    /// editor writes into it, and a second store over the same folder is how a rack and a disk stop
    /// agreeing.
    ///
    /// <para><b>It is deliberately NOT a persistence-store document.</b> The eleven preset
    /// documents are one known file each, loaded at boot and written by the teardown flush
    /// (persistence contract §11); these are an open-ended set of user files that exist only
    /// because a Save button was pressed. Nothing flushes this at shutdown and nothing should.
    /// </para>
    /// </summary>
    public CustomSessionStore CustomSessions { get; }

    /// <summary>
    /// The three sounding modules' clips, or null in a host built with no app-wide audio owner
    /// (owner-less test hosts, custom participant factories). Public so a panel can name the two
    /// clip folders to the user — which matters here more than it usually would, because this build
    /// ships neither folder's contents and both are EMPTY on a fresh install.
    /// </summary>
    public EffectSounds? Sounds { get; }

    /// <summary>Flash Images (public so the Studio module panel and the tests reach the real object).</summary>
    public FlashImagesEffect Flash { get; }

    /// <summary>Subliminals, the second ported module. Public for the same reason.</summary>
    public SubliminalsEffect Subliminals { get; }

    /// <summary>Pink Filter, the first CONTINUOUS module. Public for the same reason.</summary>
    public PinkFilterEffect PinkFilter { get; }

    /// <summary>Spiral Overlay, the first MOVING module. Public for the same reason.</summary>
    public SpiralOverlayEffect Spiral { get; }

    /// <summary>Bouncing Text, the module per-pixel alpha unblocked. Public for the same
    /// reason.</summary>
    public BouncingTextEffect BouncingText { get; }

    /// <summary>Intensity Ramp, the first module that draws NOTHING. Public for the same
    /// reason, and it has no surface property beside it because it has no surface.</summary>
    public IntensityRampEffect Ramp { get; }

    /// <summary>Mind Wipe, the first module the user HEARS rather than sees.</summary>
    public MindWipeEffect MindWipe { get; }

    /// <summary>Brain Drain's AUDIO half — half a row, permanently, and it says so.</summary>
    public BrainDrainEffect BrainDrain { get; }

    /// <summary>Lock Card, the first module that ASKS the user something.</summary>
    public LockCardEffect LockCard { get; }

    /// <summary>Bubble Count, the first module that consumes capabilities it did not shape — the
    /// video surface AND the input presence, in one firing.</summary>
    public BubbleCountEffect BubbleCount { get; }

    /// <summary>Bubble Pop, the first module the user ACTS on rather than watches.</summary>
    public BubblePopEffect BubblePop { get; }

    /// <summary>
    /// Pop Quiz, the module that asks a question with four answers and reinforces whichever one is
    /// given — every one of them is correct (<c>Services/Quiz/PopQuizService.cs:12</c>).
    /// </summary>
    public PopQuizEffect PopQuiz { get; }

    /// <summary>
    /// The pointer surface Bubble Pop places its targets on. Public for the same reason every other
    /// capability is: a capability nobody can reach is a capability nobody can interrogate — and this
    /// is the one whose <c>Available</c> is earned from the window manager's own routing answer while
    /// the foreground stays exactly where it was.
    /// </summary>
    public IBubblePopSurface BubblePopSurface => _bubblePopSurface;

    /// <summary>Mandatory Video's VIDEO half — the first module that plays a FILE, and half a row
    /// permanently because the clip's sound is not ported.</summary>
    public MandatoryVideoEffect MandatoryVideo { get; }

    /// <summary>
    /// The video surface the module plays through. Public for the same reason every surface and the
    /// audio and input presences are: a capability nobody can reach is a capability nobody can
    /// interrogate — and this is the one whose <c>Available</c> is earned from the operating system's
    /// own copy of the surface rather than from a decoder having produced bytes.
    /// </summary>
    public IVideoSurface VideoSurface => _videoSurface;

    /// <summary>The Mandatory Video module's persisted store, same reason as the others.</summary>
    public PersistenceStore<MandatoryVideoPresetDocument> MandatoryVideoPreset => _videoPreset;

    /// <summary>
    /// The audio capability both audio modules play through. Public for the same reason every surface
    /// is: a capability nobody can reach is a capability nobody can interrogate — and this is the one
    /// whose <c>Available</c> is earned from the operating system rather than from a call returning.
    /// </summary>
    public IAudioPresence Audio => _audio;

    /// <summary>
    /// The input capability the lock card asks through. Public for the same reason every surface and
    /// the audio presence are: a capability nobody can reach is a capability nobody can interrogate —
    /// and this is the one whose <c>Available</c> is earned from the operating system's own
    /// foreground and keyboard focus rather than from a handler having been attached.
    /// </summary>
    public IInputPresence Input => _input;

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

    /// <summary>The Bouncing Text module's persisted store, same reason.</summary>
    public PersistenceStore<BouncingTextPresetDocument> BouncingTextPreset => _bouncingTextPreset;

    /// <summary>
    /// The <b>Visuals</b> row: the Flash Images module's three DRAW dials.
    ///
    /// <para>It is NOT on <see cref="SessionEngine.Effects"/> and never will be — there is no
    /// module here to arm. Upstream's Visuals is a settings page with no service, no master toggle
    /// and, by its own rack entry's decision, no state dot
    /// (<c>Views/Tabs/StudioTabView.xaml.cs:494-496</c>).</para>
    /// </summary>
    public VisualsDials Visuals { get; }

    /// <summary>
    /// The haptic limb this session's five trigger points command, or null when none was
    /// supplied.
    ///
    /// <para><b>It is NOT owned here.</b> The limb belongs to the app-scoped
    /// <c>Haptics/HapticParticipant</c>, beside the sink it drives, because upstream's mixer is a
    /// member of the app's own haptic service and outlives every session
    /// (<c>ConditioningControlPanel/App.xaml.cs:2060</c>; zero hits for <c>App.Haptics</c> in
    /// <c>MainWindow/MainWindow.StartStop.cs</c>). This property exists so the composition can be
    /// ASSERTED rather than assumed: a root that quietly stopped passing the limb would leave every
    /// module silent and look identical from outside.</para>
    /// </summary>
    public IHapticLimb? Haptics { get; }

    /// <summary>The Visuals row's persisted store, same reason as the others.</summary>
    public PersistenceStore<VisualsPresetDocument> VisualsPreset => _visualsPreset;

    /// <summary>The Bouncing Text module's surface, so the panel can report what the OS said.</summary>
    public IBouncingTextSurface BouncingTextSurface => _bouncingTextSurface;

    /// <summary>The Intensity Ramp module's persisted store, same reason.</summary>
    public PersistenceStore<IntensityRampPresetDocument> RampPreset => _rampPreset;

    /// <summary>The Mind Wipe module's persisted store, same reason.</summary>
    public PersistenceStore<MindWipePresetDocument> MindWipePreset => _mindWipePreset;

    /// <summary>The Brain Drain module's persisted store, same reason.</summary>
    public PersistenceStore<BrainDrainPresetDocument> BrainDrainPreset => _brainDrainPreset;

    /// <summary>The Lock Card module's persisted store, same reason.</summary>
    public PersistenceStore<LockCardPresetDocument> LockCardPreset => _lockCardPreset;

    /// <summary>The Bubble Count module's persisted store, same reason.</summary>
    public PersistenceStore<BubbleCountPresetDocument> BubbleCountPreset => _bubbleCountPreset;

    /// <summary>The Bubble Pop module's persisted store, same reason as the others.</summary>
    public PersistenceStore<BubblePopPresetDocument> BubblePopPreset => _bubblePopPreset;

    /// <summary>The Pop Quiz module's persisted store, same reason as the others.</summary>
    public PersistenceStore<PopQuizPresetDocument> PopQuizPreset => _popQuizPreset;

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
        await _lockCardPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _videoPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _bubbleCountPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _bubblePopPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _popQuizPreset.StartAsync(cancellationToken).ConfigureAwait(false);

        // Found _bouncingTextPreset missing from all four of these lists. A store that is
        // never started is never LOADED — PersistenceStore.Load runs only from StartAsync — so
        // the Bouncing Text dials silently reverted to defaults on every launch and were never written back.
        // It is added here, and to the Degraded report, the stop and the flush below, because the
        // defect is data loss the user sees and because leaving one store out of four lists is
        // exactly how the next one gets missed.
        await _bouncingTextPreset.StartAsync(cancellationToken).ConfigureAwait(false);
        await _visualsPreset.StartAsync(cancellationToken).ConfigureAwait(false);
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
        LogIfDegraded("lockcard-preset", _lockCardPreset.LastLoadOutcome);
        LogIfDegraded("video-preset", _videoPreset.LastLoadOutcome);
        LogIfDegraded("bubblecount-preset", _bubbleCountPreset.LastLoadOutcome);
        LogIfDegraded("bubblepop-preset", _bubblePopPreset.LastLoadOutcome);
        LogIfDegraded("popquiz-preset", _popQuizPreset.LastLoadOutcome);
        LogIfDegraded("bouncingtext-preset", _bouncingTextPreset.LastLoadOutcome);
        LogIfDegraded("visuals-preset", _visualsPreset.LastLoadOutcome);
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
    /// <para><b>And the bound on it, stated rather than assumed — and now MEASURED rather than
    /// stated.</b> On the ordinary teardown path the posted teardown does not run, and the surfaces
    /// are reclaimed by the OPERATING SYSTEM at process exit. The reason is sharper than "the
    /// dispatcher has gone down", which is what this remark used to say: the lifetime's Exit
    /// handler calls <c>ShutdownAsync().GetAwaiter().GetResult()</c> ON the UI thread
    /// (<c>App.axaml.cs:95</c>), so that thread is BLOCKED inside this very teardown for its whole
    /// duration. Whether the dispatcher has been shut down by then is a detail this remark no
    /// longer rests on and does not need: a delegate posted to a thread that is parked inside
    /// <c>GetResult()</c> cannot run on it either way. A native window may be destroyed only by the
    /// thread that created it, so no other thread could take it instead.</para>
    ///
    /// <para>That is acceptable here and only here, and for two reasons rather than one. First,
    /// what the user can SEE has already been dealt with one line above: <c>Engine.Stop()</c>
    /// disarms every module, and disarm posts <c>HideAll</c> from the UI thread the user pressed
    /// STOP on. The visible-stop guarantee rests on that, never on process death. Second, the
    /// reclamation itself is no longer an assertion: a real process of this build's own surface
    /// types is brought up on a real desktop, torn down through the real
    /// <c>ApplicationHost.ShutdownAsync</c> with the disposals posted and never delivered, and the
    /// window manager is then asked what survived — normal exit AND abnormal termination
    /// (<c>client/tests/CcpClient.Tests/SurfaceExitObservations.cs</c>).</para>
    ///
    /// <para><b>The precondition, which is where the real hazard lived.</b> Reclamation at process
    /// exit is only as good as the process's ability to exit, and teardown runs with the UI thread
    /// blocked — so a teardown that never returned would leave a topmost, input-blocking surface up
    /// with nothing on screen to close it. <c>ApplicationHost.ShutdownAsync</c> now bounds its wait
    /// on every participant's stop for exactly that reason.</para>
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

        // The video surface owns a native window AND an open decoder, so it goes down the same
        // dispatched route the drawing surfaces take. Engine.Stop above already disarmed the module,
        // which calls End() on the UI thread the user pressed STOP on — so the picture is off the
        // screen before this line, and this line reclaims the window and the source reader.
        DisposeSurface(_videoSurface);

        // The pointer surface owns up to three native windows and a registered window class, so it
        // takes the same dispatched route. Engine.Stop above already disarmed the module, and the
        // module's ReleaseWork posts a Withdraw that closes every target from the UI thread the user
        // pressed STOP on — so the desktop is clear before this line, and this line reclaims the
        // windows and unregisters the class.
        DisposeSurface(_bubblePopSurface);

        // The audio presence goes down HERE, inline, and not through the dispatch boundary the
        // surfaces use. A native window belongs to the thread that made it; an audio device does not
        // — upstream says the same of NAudio and deliberately tears audio down inline from its panic
        // path for exactly that reason (Services/LockCard/MindWipeService.cs:243-251). Disposing it
        // is also what drops this process's render session to AudioSessionStateInactive, which is the
        // OS-observable far end of the chain this capability claims.
        _audio.Dispose();

        // The input presence's window DOES belong to the thread that made it, so it goes down the
        // same route the drawing surfaces take rather than the audio device's inline one. Engine.Stop
        // above has already disarmed the module, and disarm dismisses any card from the UI thread the
        // user pressed STOP on — so the visible-stop guarantee rests on that, never on this post or
        // on process death.
        DisposeSurface(_input);

        return Task.WhenAll(
            _preset.StopAsync(), _subliminalPreset.StopAsync(), _pinkFilterPreset.StopAsync(),
            _spiralPreset.StopAsync(), _rampPreset.StopAsync(), _mindWipePreset.StopAsync(),
            _brainDrainPreset.StopAsync(), _lockCardPreset.StopAsync(), _videoPreset.StopAsync(),
            _bubbleCountPreset.StopAsync(), _bubblePopPreset.StopAsync(), _popQuizPreset.StopAsync(),
            _bouncingTextPreset.StopAsync(), _visualsPreset.StopAsync(), _assetSelection.StopAsync());
    }

    /// <summary>
    /// Teardown flush for the reserved pre-drain slot (persistence contract §11). The
    /// asset selection has no writer, so it has nothing to flush.
    ///
    /// <para><b>A scripted session is ENDED here first, and that is a correctness requirement
    /// rather than tidiness.</b> A running one holds eleven documents at the SESSION's values,
    /// dirty and deliberately unwritten (<see cref="ScriptedSessionDials"/>) — so a flush that ran
    /// over them would persist the session's dials on top of the user's own and the user would
    /// never get them back. That is upstream's #471/#476 defect exactly, in the one place this
    /// port could still reproduce it, and the promise the confirmation makes
    /// (<c>MainWindow/MainWindow.Presets.cs:1467-1470</c>) covers closing the app as much as
    /// pressing STOP. This slot is the only one that works: it runs BEFORE the registry is
    /// cancelled, so the restore's own write is still live, where the same call in
    /// <see cref="StopAsync"/> would enqueue a write that terminates Cancelled.</para>
    ///
    /// <para>The ENGINE is stopped first, and only when a scripted session is really running:
    /// <see cref="ScriptedSessionRun.Stop"/> re-arms a running engine so the restored dials reach
    /// the modules (its own documented behaviour), which on the way out of the process would put
    /// every module back up for the last instant of the app's life.</para>
    /// </summary>
    public Task FlushAsync(TimeSpan boundedWait)
    {
        if (Scripted.Running)
        {
            Engine.Stop();
            Scripted.Stop();
        }

        return Task.WhenAll(
            _preset.FlushAsync(boundedWait),
            _subliminalPreset.FlushAsync(boundedWait),
            _pinkFilterPreset.FlushAsync(boundedWait),
            _spiralPreset.FlushAsync(boundedWait),
            // The ramp's own dials. The dials it BORROWS are restored synchronously by
            // Engine.Stop() above, so no flush here can ever write a ramped value over the user's.
            _rampPreset.FlushAsync(boundedWait),
            _mindWipePreset.FlushAsync(boundedWait),
            _brainDrainPreset.FlushAsync(boundedWait),
            _lockCardPreset.FlushAsync(boundedWait), _videoPreset.FlushAsync(boundedWait),
            _bubbleCountPreset.FlushAsync(boundedWait),
            _bubblePopPreset.FlushAsync(boundedWait),
            _popQuizPreset.FlushAsync(boundedWait),
            _bouncingTextPreset.FlushAsync(boundedWait),
            // The Visuals row's three dials. The flash-opacity one is also a dial the ramp BORROWS,
            // and the same guarantee holds for the same reason: Engine.Stop() above restores it
            // synchronously before this line, so no flush here can write a ramped value over the
            // user's own (Effects/IntensityDial.cs, the split-write rule).
            _visualsPreset.FlushAsync(boundedWait));
    }

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
