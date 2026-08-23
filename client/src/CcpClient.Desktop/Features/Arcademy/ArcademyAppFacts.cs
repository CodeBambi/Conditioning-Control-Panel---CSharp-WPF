using CcpClient.Desktop.Motion;

namespace CcpClient.Desktop.Features.Arcademy;

/// <summary>
/// The APP-WIDE values the init projection reads but the Arcademy does not own
/// (<c>Models/AppSettings.cs:6531-6537</c> lists them by name and says why they are not
/// duplicated: one photosensitivity guard, one asset-source vocabulary, one master volume, one
/// word pool, one motion level). Upstream reads them off <c>App.Settings.Current</c> inside
/// <c>BuildInit</c>; this port passes them in, the way <c>DtrhProtocol.BuildInit</c> already takes
/// <c>masterVolume</c> rather than reaching for a global.
///
/// <para><b>Every default below is upstream's own fallback</b> — the value
/// <c>ArcademyHostService.BuildInit</c> projects when its settings object is null (the <c>??</c>
/// right-hand sides at <c>:533-576</c>) — with two stated exceptions, both about a subsystem this
/// build does not have: see <see cref="RemoteMediaEnabled"/> and <see cref="OfflineMode"/>.</para>
/// </summary>
public sealed record ArcademyAppFacts
{
    /// <summary><c>init.platform.hasHaptics</c> (<c>ArcademyHostService.cs:538</c> →
    /// <c>SafeHasHaptics</c>, <c>:2042-2046</c>: <c>App.Haptics?.IsConnected == true</c>). False
    /// until a haptic route is actually connected — never "a sink exists".</summary>
    public bool HasHaptics { get; init; }

    /// <summary><c>init.modId</c> (<c>:541</c> → <c>SafeActiveModId</c>, <c>:2048-2052</c>), whose
    /// own fallback is the literal <c>builtin-bambisleep</c>. This build resolves no creator mod,
    /// so the fallback IS the value: the page skins nothing and asks for nothing.</summary>
    public string ModId { get; init; } = "builtin-bambisleep";

    /// <summary><c>init.effectIntensity</c> (<c>:547</c>) — the app-wide photosensitivity guard
    /// (<c>ChaosEffectIntensity ?? 0.85</c>), reused and never duplicated.</summary>
    public double EffectIntensity { get; init; } = 0.85;

    /// <summary>App-wide comfort volume, stored 0-100 and projected 0..1 (<c>:550</c>,
    /// <c>MasterVolume ?? 32</c>).</summary>
    public int MasterVolume { get; init; } = 32;

    /// <summary><c>init.remoteMediaEnabled</c> (<c>:551</c> → <c>RemoteMediaEnabled</c>,
    /// <c>:1628-1631</c>: <c>MediaSource != "local" &amp;&amp; HasRemoteMediaConsent</c>).
    /// <b>DIVERGENCE, defaulting to the honest value:</b> this build ships no remote-media broker
    /// (that is slice 7 of the board row, explicitly out of this scope) and opens no outbound
    /// connection, so there is nothing for a consent flag to enable. False is what the page must
    /// be told, not a placeholder.</summary>
    public bool RemoteMediaEnabled { get; init; }

    /// <summary><c>init.remoteMediaRatio</c> (<c>:552</c>, stored 5-95 and projected 0..1 —
    /// <c>RemoteMediaRatio ?? 30</c>). Projected even while remote media is off, because the page
    /// keeps the row and the echo loop must have something to echo.</summary>
    public int RemoteMediaRatio { get; init; } = 30;

    /// <summary><c>init.offlineMode</c> (<c>:553</c>). <b>DIVERGENCE, same reason:</b> upstream
    /// projects the user's own offline switch; here the app IS offline for this surface, and the
    /// page reads this as "kill every remote fetch" (<c>arcademy/provider/remote.js:72</c>,
    /// <c>provider/index.js:96</c>). True is the truth about this build.</summary>
    public bool OfflineMode { get; init; } = true;

    /// <summary><c>init.audioAudible</c> (<c>:554</c>, <c>SubAudioAudible ?? false</c>) — whether
    /// subliminal audio is audible rather than masked.</summary>
    public bool AudioAudible { get; init; }

    /// <summary><c>init.protectBrowserVideo</c> (<c>:558</c>,
    /// <c>ProtectBrowserVideoPlayback ?? true</c>). Projected for the page's own courtesy
    /// behaviour; the suspension it feeds is slice 5.</summary>
    public bool ProtectBrowserVideo { get; init; } = true;

    /// <summary>The app's motion preference. Feeds BOTH <c>init.motionLevel</c> (<c>:559</c> →
    /// <c>ResolvedMotionLevel</c>, <c>:706-711</c>) and <c>init.reducedMotion</c> (<c>:561</c>,
    /// <c>MotionFx.Level != Full</c>) — one source, two projections, exactly as upstream.</summary>
    public MotionLevel Motion { get; init; } = MotionLevel.Full;

    /// <summary><c>init.performanceMode</c> (<c>:560</c>,
    /// <c>PerformanceProfile.CurrentTier != Quality</c>). This build has no performance tiering, so
    /// the value is the Quality answer until one exists.</summary>
    public bool PerformanceMode { get; init; }

    /// <summary>The user's ENABLED subliminal phrases, in their stored order. Shuffled and capped
    /// by the projection itself (<c>BuildWords</c>, <c>:616-637</c>) — the ordering rule belongs to
    /// the port, not to the caller. MAY BE EMPTY, and that is a contract, not a failure: every
    /// consumer degrades to a word-free look (<c>:611-615</c>).</summary>
    public IReadOnlyList<string> SubliminalPhrases { get; init; } = [];

    /// <summary><c>init.panicKeyEnabled</c> (<c>:575</c>, default true) — projected for ONE
    /// reason: <c>arcademy/shell/keybinds.js</c> refuses to let a game bind over the panic key.
    /// The page never handles the key itself (<c>:572-574</c>).</summary>
    public bool PanicKeyEnabled { get; init; } = true;

    /// <summary><c>init.panicKey</c> (<c>:576</c>, default <c>Escape</c>).</summary>
    public string PanicKey { get; init; } = "Escape";

    /// <summary>The clock the two date fields are read from, ONCE, at boot (<c>:530</c>,
    /// <c>:563-566</c>). A <see cref="DateTimeOffset"/> carries both halves of upstream's
    /// <c>DateTime.UtcNow</c> / <c>DateTime.Now</c> pair, which is what makes the UTC-seeds-content
    /// / LOCAL-rolls-attendance split (upstream regression #978) checkable without a machine
    /// timezone.</summary>
    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;

    /// <summary>The user-media pool root and the media origin its urls are built against — the
    /// two inputs <c>BuildLocalAssets</c> (<c>:656-691</c>) reads off <c>App.EffectiveAssetsPath</c>
    /// and the <c>ccp.assets</c> host. Null means "no pool": <c>init.settings.localAssets</c> is
    /// then two empty arrays, which is what upstream produces for a missing images folder.</summary>
    public string? UserMediaRoot { get; init; }

    /// <summary>The loopback media origin (<c>{origin}/umedia/…</c>) — this port's
    /// <c>https://ccp.assets/</c> (<c>ToAssetsUrl</c>, <c>:693-698</c>).</summary>
    public string MediaOrigin { get; init; } = "";

    /// <summary>The Assets tree's deselection blacklist, normalized by
    /// <c>DtrhUserMedia.BuildDisabledSet</c>. Upstream honours the same blacklist here
    /// (<c>:662</c>, <c>:669</c>): unchecking an image hides it from the Arcademy too.</summary>
    public HashSet<string>? DisabledAssets { get; init; }

    /// <summary>Whether the deselection blacklist is actually in force (the client's documented
    /// whitelist gate, <c>DtrhUserMedia.IsAssetActive</c>). Upstream has no such gate on this
    /// path; the port's shared enumerator does, and honouring it here is what keeps ONE uncheck
    /// meaning one thing everywhere.</summary>
    public bool UseAssetWhitelist { get; init; }

    /// <summary>Path to the server override-calendar cache (holidays / event weeks) —
    /// <c>LoadOverrideCalendar</c> (<c>:715-728</c>), <c>&lt;dataDir&gt;/arcademy_calendar.json</c>.
    /// Absent is the designed offline fallback: run on the seeded timetable.</summary>
    public string? OverrideCalendarPath { get; init; }
}
