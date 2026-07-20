using Avalonia.Platform;
using CcpClient.Desktop.Capabilities;
using CcpClient.Desktop.Lifecycle;

namespace CcpClient.Desktop.Features.AvatarTube;

/// <summary>
/// The AvatarTube demonstrator participant (explicitly labeled DEMONSTRATOR — the SP-007/
/// SP-013 pattern: really-functioning, superseded by the first real AvatarTube feature,
/// owner may async-veto). It owns pack loading through the SP-009 embedded-asset manifest
/// (StandardAssetLoader avares:// opens), the ONE engine generation, the typed
/// undecodable-asset capability state (SP-006 vocabulary — mechanism only; the
/// warning-vs-diagnostics UX choice stays pending-owner), and the structured trace sink.
/// Construction starts nothing (SP-003 §4.4); the tube opens on the user path in phase 4.
///
/// First-attempt leak REJECTs implemented as design: no timers outside the SP-004-owned
/// engine operation; pack switch disposes the old pack's frames before activating the
/// replacement (never two avatars); the view's event subscriptions are field-backed and
/// symmetric, and <see cref="FrameSubscriberCount"/> exposes the real count to tests.
/// </summary>
public sealed class AvatarTubeParticipant : IBackgroundParticipant
{
    /// <summary>Fallback definition used when a pack's sheet is undecodable: the generated static fallback frame.</summary>
    public static readonly AvatarPackDef FallbackDef = new(
        SyntheticAvatarPacks.FallbackPackId, "fallback",
        SyntheticAvatarPacks.CellWidth, SyntheticAvatarPacks.CellHeight,
        SyntheticAvatarPacks.FallbackPath,
        [new AvatarClipDef(SyntheticAvatarPacks.ClipFallback, "fallback", 1, [1000])]);

    private readonly AsyncOperationOwner _owner;
    private readonly UiDispatchBoundary _ui;
    private readonly ILogSink _log;
    private readonly Func<string, Stream> _openAsset;
    private readonly object _gate = new();
    private int _corruptPackId = -1;
    private AvatarAnimationEngine? _engine;
    private AvatarPack? _pack;
    private Task<OperationOutcome>? _lastCompletion;
    private int _activePackId = -1;
    private CapabilityState _capability = new CapabilityState.Unavailable(
        new CapabilityReason(CapabilityReasonCodes.NotProbed, "no pack decode attempted"));

    public AvatarTubeParticipant(
        AsyncOperationOwner owner,
        UiDispatchBoundary ui,
        ILogSink log,
        Func<string, Stream>? openAsset = null)
    {
        _owner = owner;
        _ui = ui;
        _log = log;
        _openAsset = openAsset ?? OpenEmbeddedAsset;
    }

    public string Name => "AvatarTubeDemo";

    public bool Running { get; private set; }

    /// <summary>Raised on the UI thread (inside the dispatch boundary) when the composition changed.</summary>
    public event EventHandler<AvatarFrameEventArgs>? FramePresented;

    /// <summary>The typed SP-006 capability state of the active pack's decode probe.</summary>
    public CapabilityState AvatarCapability { get { lock (_gate) { return _capability; } } }

    public int ActivePackId { get { lock (_gate) { return _activePackId; } } }

    /// <summary>Engine readback for tests/evidence (null before the tube starts).</summary>
    public AvatarAnimationEngine? Engine { get { lock (_gate) { return _engine; } } }

    /// <summary>The active pack's decoded frames (the view maps clip/frame to platform images).</summary>
    public AvatarPack? ActivePack { get { lock (_gate) { return _pack; } } }

    /// <summary>Real event-subscription count on the engine — the leak signal.</summary>
    public int FrameSubscriberCount => Engine?.FrameSubscriberCount ?? 0;

    /// <summary>Structured trace sink (wired by the --avatar-trace flag; content-free JSON lines).</summary>
    public Action<AvatarTraceEventArgs>? TraceSink { get; set; }

    /// <summary>The engine operation's owned completion (async contract §1) — survives StopTube for awaiting.</summary>
    public Task<OperationOutcome>? Completion
    {
        get { lock (_gate) { return _engine?.Completion ?? _lastCompletion; } }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Running = true;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (!Running)
        {
            return Task.CompletedTask;
        }

        Running = false;
        StopTube();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the demonstrator pipeline: decode-probes the pack (the capability probe —
    /// runtime decode of the selected asset in the current environment) and starts the ONE
    /// engine generation. Idempotent.
    /// </summary>
    public void StartTube(IAvatarClock? clock = null)
    {
        lock (_gate)
        {
            if (_engine is not null)
            {
                return;
            }

            var pack = LoadPackUnsafe(SyntheticAvatarPacks.Circuit.PackId);
            _engine = new AvatarAnimationEngine(_owner, clock ?? new SystemAvatarClock(), _log, pack);
            _engine.FrameApplied += OnEngineFrame;
            _engine.Traced += OnEngineTrace;
            _engine.Start();
        }
    }

    /// <summary>Cancels the engine generation (idempotent). Attach/detach is NOT here — it is view-only.</summary>
    public void StopTube()
    {
        lock (_gate)
        {
            if (_engine is null)
            {
                return;
            }

            _engine.FrameApplied -= OnEngineFrame;
            _engine.Traced -= OnEngineTrace;
            _engine.Stop();
            _lastCompletion = _engine.Completion;
            _engine = null;
            _pack = null;
            _activePackId = -1;
        }
    }

    /// <summary>
    /// "Mod switching" demonstrator semantics (packet framing (b)): switch between the two
    /// synthetic packs through the SAME SP-009 manifest path. The old pack's decoded frames
    /// are dropped before the replacement activates (WPF disposal parity — never two
    /// avatars); the replacement is decode-probed fresh (capability re-probe per switch).
    /// </summary>
    public void SwitchPack(int packId)
    {
        lock (_gate)
        {
            if (_engine is null || _activePackId == packId)
            {
                return;
            }

            var pack = LoadPackUnsafe(packId);
            _engine.SetPack(pack);
            TraceSink?.Invoke(new AvatarTraceEventArgs(AvatarTraceEvent.PackSwitch, pack.Def.PackId, -1, -1,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }
    }

    /// <summary>
    /// Demo-only corruption seam (--avatar-corrupt-demo): the named pack's sheet bytes are
    /// replaced by garbage on the next decode — the typed undecodable-asset path runs for
    /// real without touching embedded assets.
    /// </summary>
    public void CorruptPackForDemo(int packId)
    {
        lock (_gate)
        {
            _corruptPackId = packId;
        }
    }

    /// <summary>Decode-probes one pack; on failure loads the static fallback and reports typed Degraded.</summary>
    private AvatarPack LoadPackUnsafe(int packId)
    {
        var def = SyntheticAvatarPacks.All.FirstOrDefault(d => d.PackId == packId)
            ?? throw new ArgumentException($"no synthetic pack with id {packId}", nameof(packId));
        try
        {
            var pack = AvatarPackLoader.Load(def, OpenAssetChecked);
            _pack = pack;
            _activePackId = def.PackId;
            _capability = new CapabilityState.Available(
                $"pack '{def.Name}' decoded: {def.Clips.Length} clips, strip self-checks passed");
            _log.Log($"avatar-capability: Available — pack '{def.Name}' decoded");
            return pack;
        }
        catch (AvatarPackLoadException ex)
        {
            // Typed undecodable-asset path (SP-006 vocabulary; UX choice pending-owner):
            // static fallback + bounded diagnostics (one line per activation attempt —
            // never a busy retry loop: exactly one decode attempt per switch).
            _capability = new CapabilityState.Degraded(
                "static fallback avatar displayed",
                new CapabilityReason(CapabilityReasonCodes.AssetUndecodable, ex.Message));
            _log.Log($"avatar-capability: Degraded (asset-undecodable) — {ex.Message}");
            var fallback = AvatarPackLoader.Load(FallbackDef, OpenAssetChecked);
            _pack = fallback;
            _activePackId = def.PackId;
            return fallback;
        }
    }

    private Stream OpenAssetChecked(string path)
    {
        var corrupting = SyntheticAvatarPacks.All
            .Where(d => d.PackId == _corruptPackId)
            .Any(d => d.SheetPath == path);
        return corrupting
            ? new MemoryStream([0xDE, 0xAD, 0xBE, 0xEF])
            : _openAsset(path);
    }

    private void OnEngineFrame(object? sender, AvatarFrameEventArgs args)
    {
        if (!_ui.IsBound)
        {
            return; // skip-until-bound (async contract §5.3)
        }

        var generation = _owner.Generation;
        _ui.Post(() =>
        {
            // Stale check inside the delegate on the UI thread (contract §5.5).
            if (_owner.IsLive(generation))
            {
                FramePresented?.Invoke(this, args);
            }
        });
    }

    private void OnEngineTrace(object? sender, AvatarTraceEventArgs args) => TraceSink?.Invoke(args);

    private static Stream OpenEmbeddedAsset(string path) =>
        new StandardAssetLoader(typeof(App).Assembly).Open(
            new Uri(Manifest.AssetManifest.AssemblyAssetUriPrefix + path));
}
