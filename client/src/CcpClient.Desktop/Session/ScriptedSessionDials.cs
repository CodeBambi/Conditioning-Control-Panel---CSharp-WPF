using System.Text.Json;
using System.Text.Json.Serialization;
using CcpClient.Desktop.Persistence;

namespace CcpClient.Desktop.Session;

/// <summary>
/// The user's dials, as a scripted session borrows them: <b>capture, replace, give back</b>.
///
/// <para>This is upstream's <c>SaveCurrentSettings</c> / <c>ApplySessionSettings</c> /
/// <c>RestoreSettings</c> trio (<c>Services/Session/SessionEngine.cs:864-923</c>,
/// <c>:1145-1443</c>,
/// <c>:1445-1557</c>) against the port's documents instead of one global <c>AppSettings</c>. The
/// promise it keeps is the one the confirm dialog makes in so many words — "Your current settings
/// will be temporarily replaced. They will be restored when the session ends."
/// (<c>MainWindow/MainWindow.Presets.cs:1467-1470</c>) — and it is the reason the snapshot is taken
/// BEFORE anything is applied (<c>Services/Session/SessionEngine.cs:172-183</c>).</para>
///
/// <para><b>The snapshot is the document, serialized.</b> Upstream copies field by field into a
/// spare <c>AppSettings</c> and hand-copies every one of them back, which is how a member gets
/// forgotten on one side of the pair. A whole-document round-trip cannot forget a member, restores
/// unknown members too (persistence contract §6), and lands through
/// <see cref="PersistenceStore{TModel}.Replace"/>, which is the contract's own whole-object path:
/// it swaps <see cref="PersistenceStore{TModel}.Current"/> synchronously, raises
/// <c>SettingsReplaced</c>, and enqueues the write.</para>
///
/// <para><b>Applied is a SUBSET of modelled, on purpose.</b> A session file carries dials for
/// features this port does not have (the flash draw dials it refuses to persist, the bubble burst
/// schedule, the corner GIF, mind wipe's escalation multiplier, whisper volume and audio ducking)
/// and every one of them is listed in <see cref="Apply"/>'s remarks with the reason. Applying a
/// dial no module reads would be the storage form of a greyed-out control.</para>
///
/// <para><b>Apply does not write to disk; Restore does.</b> Upstream saves mid-session
/// (<c>Services/Session/SessionEngine.cs:262</c>), so a crash during an upstream session leaves the
/// SESSION's dials
/// persisted and the user's lost. Here the mutations are marked dirty and left in memory, and the
/// restore's <c>Replace</c> is what enqueues a write — so an interrupted session gives the user's
/// own settings back rather than the session's. A recorded divergence, in the user's favour. One
/// document is not covered by it: <see cref="SessionEngine.Start"/> saves its own preset when it
/// arms (WPF <c>MainWindow/MainWindow.StartStop.cs:161</c>), so the flash document does reach disk
/// with the session's values while the session runs, and is written back at the restore.</para>
/// </summary>
public sealed class ScriptedSessionDials
{
    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PersistenceStore<SessionPresetDocument> _flash;
    private readonly PersistenceStore<VisualsPresetDocument> _visuals;
    private readonly PersistenceStore<SubliminalPresetDocument> _subliminal;
    private readonly PersistenceStore<BouncingTextPresetDocument> _bouncingText;
    private readonly PersistenceStore<PinkFilterPresetDocument> _pinkFilter;
    private readonly PersistenceStore<SpiralPresetDocument> _spiral;
    private readonly PersistenceStore<BubblePopPresetDocument> _bubblePop;
    private readonly PersistenceStore<MindWipePresetDocument> _mindWipe;
    private readonly PersistenceStore<MandatoryVideoPresetDocument> _video;
    private readonly PersistenceStore<LockCardPresetDocument> _lockCard;
    private readonly PersistenceStore<BubbleCountPresetDocument> _bubbleCount;
    private readonly IDocumentSlot[] _slots;

    /// <summary>
    /// The eleven documents a scripted session can move. Each is the REAL store the module reads,
    /// never a copy — the same rule <see cref="Scheduling.SessionScheduler"/> states for the engine
    /// it owns: a double that diverges from the product is exactly where the defect hides.
    /// </summary>
    public ScriptedSessionDials(
        PersistenceStore<SessionPresetDocument> flash,
        PersistenceStore<VisualsPresetDocument> visuals,
        PersistenceStore<SubliminalPresetDocument> subliminal,
        PersistenceStore<BouncingTextPresetDocument> bouncingText,
        PersistenceStore<PinkFilterPresetDocument> pinkFilter,
        PersistenceStore<SpiralPresetDocument> spiral,
        PersistenceStore<BubblePopPresetDocument> bubblePop,
        PersistenceStore<MindWipePresetDocument> mindWipe,
        PersistenceStore<MandatoryVideoPresetDocument> video,
        PersistenceStore<LockCardPresetDocument> lockCard,
        PersistenceStore<BubbleCountPresetDocument> bubbleCount)
    {
        ArgumentNullException.ThrowIfNull(flash);
        ArgumentNullException.ThrowIfNull(visuals);
        ArgumentNullException.ThrowIfNull(subliminal);
        ArgumentNullException.ThrowIfNull(bouncingText);
        ArgumentNullException.ThrowIfNull(pinkFilter);
        ArgumentNullException.ThrowIfNull(spiral);
        ArgumentNullException.ThrowIfNull(bubblePop);
        ArgumentNullException.ThrowIfNull(mindWipe);
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(lockCard);
        ArgumentNullException.ThrowIfNull(bubbleCount);

        _flash = flash;
        _visuals = visuals;
        _subliminal = subliminal;
        _bouncingText = bouncingText;
        _pinkFilter = pinkFilter;
        _spiral = spiral;
        _bubblePop = bubblePop;
        _mindWipe = mindWipe;
        _video = video;
        _lockCard = lockCard;
        _bubbleCount = bubbleCount;

        // Order is the snapshot's order and must not change without changing nothing else: a
        // snapshot is positional, and a snapshot taken by one instance is restored by the same one.
        _slots =
        [
            new DocumentSlot<SessionPresetDocument>(flash),
            new DocumentSlot<VisualsPresetDocument>(visuals),
            new DocumentSlot<SubliminalPresetDocument>(subliminal),
            new DocumentSlot<BouncingTextPresetDocument>(bouncingText),
            new DocumentSlot<PinkFilterPresetDocument>(pinkFilter),
            new DocumentSlot<SpiralPresetDocument>(spiral),
            new DocumentSlot<BubblePopPresetDocument>(bubblePop),
            new DocumentSlot<MindWipePresetDocument>(mindWipe),
            new DocumentSlot<MandatoryVideoPresetDocument>(video),
            new DocumentSlot<LockCardPresetDocument>(lockCard),
            new DocumentSlot<BubbleCountPresetDocument>(bubbleCount),
        ];
    }

    /// <summary>How many documents a snapshot carries.</summary>
    public int DocumentCount => _slots.Length;

    /// <summary>
    /// Take the snapshot — upstream's <c>SaveCurrentSettings</c>
    /// (<c>Services/Session/SessionEngine.cs:864-923</c>), called before a single dial is touched
    /// (<c>:172</c>).
    /// </summary>
    public ScriptedSessionDialSnapshot Capture() =>
        new([.. _slots.Select(slot => slot.Capture())]);

    /// <summary>
    /// Give the user's dials back — upstream's <c>RestoreSettings</c> (<c>:1445-1557</c>), called
    /// on both exits, completion and abort (<c>:347</c>).
    ///
    /// <para>Each document is swapped whole through <see cref="PersistenceStore{TModel}.Replace"/>:
    /// the swap is synchronous, so a caller that returns from here has already handed the modules
    /// their old dials; the write it enqueues is owned by the store and chains behind any write
    /// already in flight. The task is deliberately not awaited, which is the port's established
    /// shape for a save on a state transition (<see cref="SessionEngine.Start"/>).</para>
    /// </summary>
    /// <exception cref="ArgumentException">The snapshot did not come from a compatible
    /// instance.</exception>
    public void Restore(ScriptedSessionDialSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Documents.Count != _slots.Length)
        {
            throw new ArgumentException(
                $"snapshot carries {snapshot.Documents.Count} documents; this instance owns {_slots.Length}",
                nameof(snapshot));
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            _slots[i].Restore(snapshot.Documents[i]);
        }
    }

    /// <summary>
    /// Impose the session's dials — upstream's <c>ApplySessionSettings</c>
    /// (<c>Services/Session/SessionEngine.cs:1145-1443</c>) plus the mind-wipe block its
    /// <c>StartSessionAsync</c> keeps separately (<c>:207-212</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>What upstream applies that this does not, each with its reason:</b></para>
    /// <list type="bullet">
    /// <item><c>FlashClickable</c>, <c>CorruptionMode</c> (hydra) and <c>FlashAudioEnabled</c>
    /// (<c>:1157-1159</c>) — draw dials <see cref="SessionPresetDocument"/> deliberately does not
    /// persist, because this port draws no flash window.</item>
    /// <item><c>SubAudioEnabled</c>/<c>SubAudioVolume</c> and the ducking pair
    /// (<c>:1226-1239</c>) — the port has no whisper or ducking dial at all.</item>
    /// <item><c>BubblesClickable</c> (<c>:1313</c>) — <see cref="BubblePopPresetDocument"/> states
    /// why the port has no such dial: unclickable bubbles are scenery, and the honest port of that
    /// is not a pointer target.</item>
    /// <item>the lock-card phrase override (<c>:1370-1383</c>) — no shipped session file writes
    /// <c>lockCardPhrases</c>, so the member is not modelled and there is nothing to apply.</item>
    /// <item><c>MindWipeBaseMultiplier</c> (<c>:212</c>) — session mode's escalating frequency,
    /// which <see cref="MindWipePresetDocument"/> records as absent.</item>
    /// <item>every <c>StartMinute</c>-gated deferral (<c>DeferFeatureStart</c>, <c>:1136</c>) — the
    /// delayed feature starts are not in this slice. The dial state at t=0 IS ported: a feature
    /// whose start minute is not zero is applied OFF, exactly as upstream applies it
    /// (<c>:1288-1296</c> for pink, <c>:1299-1307</c> for spiral, <c>:1315</c> for bubbles).</item>
    /// <item><c>flashSmallSize</c> — upstream never reads it at runtime at all — and
    /// <c>flashScale</c>, which upstream DOES read, but only in the ramp
    /// (<c>Services/Session/SessionEngine.cs:596-599</c>), which is not ported. See
    /// <see cref="ScriptedSessionSettings"/>: the first is upstream's behaviour, the second is
    /// this slice's boundary.</item>
    /// </list>
    /// </remarks>
    public void Apply(ScriptedSessionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Flash images (:1151-1156). The frequency and image count live on the session preset; the
        // opacity is the visuals document's, which is where this port keeps it.
        _flash.Mutate(d =>
        {
            d.FlashEnabled = settings.FlashEnabled;
            if (settings.FlashEnabled)
            {
                d.FlashesPerHour = settings.FlashPerHour;
                d.ImagesPerFlash = settings.FlashImages;
            }
        });

        if (settings.FlashEnabled)
        {
            _visuals.Mutate(d => d.FlashOpacityPercent = settings.FlashOpacity);
        }

        // Subliminals (:1180-1206). The phrase override disables every phrase the user had and
        // enables the session's, which is upstream's two loops (:1191-1201) — their pool entries
        // survive as false, so the restore gives back the pool and not just the flags.
        _subliminal.Mutate(d =>
        {
            d.Enabled = settings.SubliminalEnabled;
            if (!settings.SubliminalEnabled)
            {
                return;
            }

            d.PerMinute = settings.SubliminalPerMin;
            d.OpacityPercent = settings.SubliminalOpacity;
            d.DurationFrames = settings.SubliminalFrames;
            if (settings.SubliminalPhrases.Count == 0)
            {
                return;
            }

            foreach (var key in d.Phrases.Keys.ToList())
            {
                d.Phrases[key] = false;
            }

            foreach (var phrase in settings.SubliminalPhrases)
            {
                d.Phrases[phrase] = true;
            }
        });

        // Bouncing text (:1243-1263). The port's document holds the ENABLED phrases as a list (its
        // own recorded divergence), so upstream's disable-all-then-enable pair is one assignment.
        _bouncingText.Mutate(d =>
        {
            d.Enabled = settings.BouncingTextEnabled;
            if (!settings.BouncingTextEnabled)
            {
                return;
            }

            d.Speed = settings.BouncingTextSpeed;
            d.SizePercent = settings.BouncingTextSize;
            d.OpacityPercent = settings.BouncingTextOpacity;
            if (settings.BouncingTextPhrases.Count > 0)
            {
                d.Phrases = [.. settings.BouncingTextPhrases];
            }
        });

        // Pink filter (:1288-1296): ON at its START opacity only when it begins at t=0; otherwise
        // OFF and left to the delayed start, which is not in this slice.
        _pinkFilter.Mutate(d =>
        {
            var immediate = settings.PinkFilterEnabled && settings.PinkFilterStartMinute == 0;
            d.Enabled = immediate;
            if (immediate)
            {
                d.OpacityPercent = settings.PinkFilterStartOpacity;
            }
        });

        // Spiral (:1299-1307): the same rule, with the spiral's own opacity.
        _spiral.Mutate(d =>
        {
            var immediate = settings.SpiralEnabled && settings.SpiralStartMinute == 0;
            d.Enabled = immediate;
            if (immediate)
            {
                d.OpacityPercent = settings.SpiralOpacity;
            }
        });

        // Bubbles (:1310-1334). The frequency is written even when the spawner is held back,
        // exactly as upstream writes it before deciding the flag (:1312 before :1315), and the flag
        // is upstream's own conjunction: t=0 AND not intermittent.
        _bubblePop.Mutate(d =>
        {
            if (settings.BubblesEnabled)
            {
                d.PerMinute = settings.BubblesFrequency;
                d.Enabled = settings.BubblesStartMinute == 0 && !settings.BubblesIntermittent;
            }
            else
            {
                d.Enabled = false;
            }
        });

        // Mandatory videos (:1339-1344): a null rate means the session leaves the user's alone.
        _video.Mutate(d =>
        {
            d.Enabled = settings.MandatoryVideosEnabled;
            if (settings.MandatoryVideosEnabled && settings.VideosPerHour.HasValue)
            {
                d.PerHour = settings.VideosPerHour.Value;
            }
        });

        // Lock cards (:1361-1366), same null rule.
        _lockCard.Mutate(d =>
        {
            d.Enabled = settings.LockCardEnabled;
            if (settings.LockCardEnabled && settings.LockCardFrequency.HasValue)
            {
                d.PerHour = settings.LockCardFrequency.Value;
            }
        });

        // Bubble count (:1416-1421), same null rule.
        _bubbleCount.Mutate(d =>
        {
            d.Enabled = settings.BubbleCountEnabled;
            if (settings.BubbleCountEnabled && settings.BubbleCountFrequency.HasValue)
            {
                d.PerHour = settings.BubbleCountFrequency.Value;
            }
        });

        // Mind wipe (:207-212). Upstream starts the service directly and hands it the volume as a
        // 0..1 gain; the port's module reads its dial when it arms, so writing the dial IS the
        // start, and the document keeps the percent upstream persists.
        _mindWipe.Mutate(d =>
        {
            d.Enabled = settings.MindWipeEnabled;
            if (settings.MindWipeEnabled)
            {
                d.VolumePercent = settings.MindWipeVolume;
            }
        });
    }

    private interface IDocumentSlot
    {
        string Capture();

        void Restore(string json);
    }

    private sealed class DocumentSlot<TModel>(PersistenceStore<TModel> store) : IDocumentSlot
        where TModel : class, new()
    {
        public string Capture() => JsonSerializer.Serialize(store.Current, SnapshotOptions);

        public void Restore(string json)
        {
            var model = JsonSerializer.Deserialize<TModel>(json, SnapshotOptions);
            if (model is null)
            {
                // Unreachable for text this class produced; a null here would mean the snapshot was
                // the literal "null", and silently keeping the session's dials is better than
                // throwing out of a teardown path.
                return;
            }

            _ = store.Replace(model);
        }
    }
}

/// <summary>
/// One captured set of the user's dials — the documents this session borrowed, serialized, in the
/// order <see cref="ScriptedSessionDials"/> owns them.
/// </summary>
/// <param name="Documents">Each document's JSON, positionally.</param>
public sealed record ScriptedSessionDialSnapshot(IReadOnlyList<string> Documents);
