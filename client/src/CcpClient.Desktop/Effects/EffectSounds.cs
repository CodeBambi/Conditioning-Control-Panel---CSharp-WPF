using CcpClient.Desktop.Audio;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// <b>The sound three modules make</b> — the flash's clip, the bubble field's pop, and the counting
/// clip's pop — on the app-wide <see cref="SoundArbitration"/>.
///
/// <para><b>Two doors, and which one a surface uses is decided by upstream's own mechanism rather
/// than by convenience.</b></para>
///
/// <list type="bullet">
/// <item><b>The flash goes to <see cref="SoundArbitration.PlayWhisper"/>.</b> Upstream's flash sound
/// is stop-replace by construction: <c>PlaySound</c> opens with <c>StopCurrentSound()</c>
/// (<c>Services/Flash/FlashService.cs:3516-3518</c>) and keeps exactly ONE <c>_currentSound</c>
/// field (<c>:3539</c>), so a second flash cuts the first one's clip off. Its caller then tells the
/// companion that a clip is audible so she will not talk over it — <c>App.Audio?.MarkWhisperAudio(duration)</c>
/// (<c>:1044</c>). The whisper channel here is those two halves composed: one active player, replaced
/// on the next play, with <c>WhisperBusy</c> raised at play and cleared by the REAL completion event.
/// That second half is not decorative in this build — <c>Companion/BarkPipeline.cs:416</c> already
/// suppresses a bark with the reason <c>whisper-active</c> while it holds.</item>
/// <item><b>Both pops go to <see cref="SoundArbitration.PlaySfx"/>.</b> Upstream's pops are
/// <c>App.Audio.PlayOneShot</c> (<c>Services/BubbleService.cs:2027</c>,
/// <c>Windows/BubbleCountWindow.xaml.cs:1343</c>), which OVERLAPS up to ten concurrent clips and
/// DROPS the eleventh (<c>Services/Audio/AudioService.Playback.cs:111</c>, <c>:212-217</c>). The SFX
/// pool is that shape: bounded, drop-on-overflow typed, slots reclaimed on the backend's own
/// <c>PlaybackEnded</c>. A pop must never take the whisper door — a burst of pops on a
/// stop-replace slot would cut each other off, which is a defect a user hears immediately.</item>
/// </list>
///
/// <para><b>THE CLIPS DO NOT SHIP, and that is refused rather than invented.</b> Upstream's flash
/// clips are 118 <c>.wav</c> voice lines under <c>Resources/sounds/flashes_audio</c>
/// (<c>Services/Flash/FlashService.cs:148</c> →
/// <c>Services/Companion/CompanionPhraseService.cs:49</c>, <c>:34-35</c>) and its pops are <c>Resources/sounds/bubbles/Pop{,2,3}.mp3</c>
/// (<c>Services/BubbleService.cs:1996-1998</c>). Those are legacy-tree bytes, and this port forks
/// none of them — the rule <see cref="AudioCuePool"/> already states for Mind Wipe and Brain Drain.
/// So both pools read the user-media root, both are EMPTY on a fresh install, and a play with
/// nothing to play returns <see cref="SoundOutcome.Unavailable"/> NAMING the folder rather than
/// failing, faking a clip, or synthesising one. Shipping bytes into <c>client/</c> is a payload
/// decision and is not taken here.</para>
///
/// <para><b>Volume is applied at the play site, with the play site's own law</b> — the arrangement
/// <see cref="AudioParticipant.MasterVolume"/> exists for and states in its own words ("a READING:
/// applying it is the play site's job, with the play site's own law"). Upstream's two laws differ
/// in a way the user hears — one has a five per cent floor, the other a second dial — so they are
/// separate functions here, each with its own citation.</para>
/// </summary>
public sealed class EffectSounds
{
    /// <summary>Upstream's flash-clip folder name, taken verbatim
    /// (<c>Services/Companion/CompanionPhraseService.cs:34-35</c> —
    /// <c>Resources/sounds/flashes_audio</c>).</summary>
    public const string FlashClipFolderName = "flashes_audio";

    /// <summary>Upstream's pop-clip folder name, taken verbatim (<c>Services/BubbleService.cs:1998</c>
    /// resolves <c>"bubbles/" + Pop.mp3</c>; <c>Windows/BubbleCountWindow.xaml.cs:1338</c> is the
    /// same folder).</summary>
    public const string PopClipFolderName = "bubbles";

    /// <summary>The exponent both volume laws share (<c>Services/Flash/FlashService.cs:3530</c>,
    /// <c>Services/BubbleService.cs:2004</c>, <c>Windows/BubbleCountWindow.xaml.cs:1342</c>).</summary>
    public const double VolumeCurve = 1.5;

    /// <summary>
    /// The flash law's floor: <c>Math.Max(0.05f, …)</c> (<c>Services/Flash/FlashService.cs:3530</c>,
    /// whose own comment reads "Apply volume curve (gentler, minimum 5%)").
    ///
    /// <para><b>It is a real quirk and it is ported rather than corrected</b>: a user with master at
    /// zero still hears the flash clip at 5 %, because upstream's flash gate (<c>:1037</c>) tests
    /// <c>FlashAudioEnabled</c> and never the volume, and its <c>PlaySound</c> opens a raw
    /// <c>WaveOutEvent</c> rather than going through the one-shot path that refuses a zero volume.
    /// The pops have no such floor and DO fall silent at zero — the asymmetry is upstream's.</para>
    /// </summary>
    public const float FlashGainFloor = 0.05f;

    /// <summary>
    /// The bubble volume dial's value, because <b>this port has no such dial</b>. Upstream's law is
    /// <c>pow(master × bubbles, 1.5)</c> and its <c>BubblesVolume</c> defaults to 50
    /// (<c>Models/AppSettings.cs:2790-2791</c>). Dropping the factor instead would make every pop
    /// roughly three times as loud as the shipping app's default, so the LAW is ported with the missing
    /// dial pinned at upstream's own default. A row that adds the dial replaces this constant with
    /// the reading and changes nothing else.
    /// </summary>
    public const int BubblesVolumePercent = 50;

    private readonly AudioParticipant _audio;
    private readonly IAudioCuePool _flashClips;
    private readonly IAudioCuePool _popClips;
    private readonly Action<Action> _post;
    private readonly Action<string>? _log;

    /// <param name="audio">The app-wide audio owner: the one arbitration, the one master volume, and
    /// the device this brings up on first need.</param>
    /// <param name="flashClips">The flash module's clips.</param>
    /// <param name="popClips">The clips both bubble modules share.</param>
    /// <param name="post">
    /// How a POP leaves the caller's thread. Upstream's pop path is asynchronous on purpose and says
    /// why: "Pop sounds fire in bursts, which is exactly the pattern that used to park two
    /// thread-pool threads per bubble" (<c>Services/BubbleService.cs:2022-2026</c>), and
    /// <c>PlayOneShot</c> hands the open to a dedicated playback thread
    /// (<c>Services/Audio/AudioService.Playback.cs:223</c>). The counting window says it in a line
    /// of its own: "Run everything off UI thread to avoid blocking LibVLC rendering"
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1331</c>). It matters here for a concrete reason:
    /// a pop's two callers are a pointer message pump and a video frame painter, and this port's
    /// player construction BLOCKS its caller for up to two seconds against a wedged endpoint
    /// (<c>Audio/AudioSeams.cs:244-245</c>). The flash deliberately does NOT use this — upstream
    /// plays its flash clip inline on the UI thread (<c>Services/Flash/FlashService.cs:1042</c>,
    /// reached from <c>:634-637</c>).
    /// </param>
    /// <param name="log">Where a contained fault on the posted thread is named. An unhandled
    /// exception on a pool thread ends the process, so the hand-off catches — and a swallowed
    /// failure with nowhere to report is the shape this port refuses.</param>
    public EffectSounds(
        AudioParticipant audio,
        IAudioCuePool flashClips,
        IAudioCuePool popClips,
        Action<Action>? post = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(flashClips);
        ArgumentNullException.ThrowIfNull(popClips);
        _audio = audio;
        _flashClips = flashClips;
        _popClips = popClips;
        _post = post ?? (work => ThreadPool.QueueUserWorkItem(_ => work()));
        _log = log;
    }

    /// <summary>
    /// The product composition: both pools over the user-media root, upstream's own folder names,
    /// and the flash's own draw policy.
    ///
    /// <para><b>Each pool gets its OWN draw source, and that is not a detail.</b> The two are drawn
    /// on different threads — the flash on the effect signal thread, a pop on whatever thread the
    /// hand-off in <see cref="Pop"/> lands on — and <see cref="AudioCuePool"/>'s lock is
    /// per-instance, so one shared <see cref="Random"/> across both would be an unsynchronised
    /// <see cref="Random"/> across two gates. That is why this takes no random at all: a fact that
    /// wants a pinned order constructs its pools and passes them to the constructor, which is what
    /// every fact here already does.</para>
    /// </summary>
    /// <param name="assetsRoot">The user-media root (<c>SessionParticipant.AssetsRootFor</c>).</param>
    public static EffectSounds ForProduct(
        AudioParticipant audio, string assetsRoot, Action<string>? log = null) =>
        new(
            audio,
            // WITHOUT replacement: upstream deals the flash clips out of a shuffled queue and only
            // reshuffles when it empties (Services/Flash/FlashService.cs:3315-3329), so a user hears
            // every clip in the folder before hearing any of them twice.
            new AudioCuePool(assetsRoot, FlashClipFolderName, withoutReplacement: true),
            // WITH replacement: upstream picks one of the three pop files uniformly on every pop
            // (Services/BubbleService.cs:1996-1997, Windows/BubbleCountWindow.xaml.cs:1332,1339-1340), so
            // the same pop can sound twice running.
            new AudioCuePool(assetsRoot, PopClipFolderName),
            log: log);

    /// <summary>Where the flash's clips are read from, so a surface can tell the user where to put
    /// them. A path, never a file name.</summary>
    public string FlashClipFolder => _flashClips.Folder;

    /// <summary>Where both bubble modules' pop clips are read from.</summary>
    public string PopClipFolder => _popClips.Folder;

    /// <summary>The last thing <see cref="Flash"/> answered, or null before it was ever called.
    /// A typed refusal is what a surface reports; it is never rendered as silence with no
    /// reason.</summary>
    public SoundOutcome? LastFlash { get; private set; }

    /// <summary>The last thing <see cref="PopNow"/> answered, or null before any pop.</summary>
    public SoundOutcome? LastPop { get; private set; }

    /// <summary>
    /// The flash law: <c>max(0.05, pow(master ÷ 100, 1.5))</c>
    /// (<c>Services/Flash/FlashService.cs:3529-3530</c>).
    /// </summary>
    public static float FlashGain(int masterVolumePercent) =>
        Math.Max(FlashGainFloor, (float)Math.Pow(masterVolumePercent / 100.0, VolumeCurve));

    /// <summary>
    /// The pop law: <c>min(pow(master ÷ 100 × bubbles ÷ 100, 1.5), 1)</c>
    /// (<c>Services/BubbleService.cs:2002-2006</c>). The counting window computes the same product
    /// and leaves the ceiling to <c>PlayOneShot</c>'s own <c>Math.Clamp(volume, 0f, 1f)</c>
    /// (<c>Windows/BubbleCountWindow.xaml.cs:1342-1343</c>,
    /// <c>Services/Audio/AudioService.Playback.cs:221</c>) — the same value either way, so both
    /// modules share this one.
    /// </summary>
    public static float PopGain(int masterVolumePercent, int bubblesVolumePercent) =>
        Math.Min(
            1f,
            (float)Math.Pow(masterVolumePercent / 100.0 * (bubblesVolumePercent / 100.0), VolumeCurve));

    /// <summary>
    /// One flash's clip, on the whisper channel, on the caller's thread.
    ///
    /// <para>The clip is drawn BEFORE the device is asked for: a folder with nothing in it must not
    /// seize a render endpoint, which is the whole reason
    /// <see cref="AudioParticipant.EnsureDevice"/> is a first-need call rather than a startup
    /// one.</para>
    /// </summary>
    public SoundOutcome Flash()
    {
        SoundOutcome outcome;
        if (_flashClips.Draw() is not { } path)
        {
            outcome = new SoundOutcome.Unavailable(NoClips(_flashClips));
        }
        else
        {
            _audio.EnsureDevice();
            outcome = _audio.Arbitration.PlayWhisper(path, FlashGain(_audio.MasterVolume));
        }

        LastFlash = outcome;
        return outcome;
    }

    /// <summary>
    /// One bubble's pop, handed off the caller's thread (see the <c>post</c> parameter for why the
    /// pops leave the thread and the flash does not).
    /// </summary>
    public void Pop() => _post(RunPop);

    /// <summary>
    /// The pop body. Public because it is the whole behaviour, and a fact that drives it directly
    /// reads the outcome instead of the hand-off.
    /// </summary>
    public SoundOutcome PopNow()
    {
        SoundOutcome outcome;
        if (_popClips.Draw() is not { } path)
        {
            outcome = new SoundOutcome.Unavailable(NoClips(_popClips));
        }
        else if (PopGain(_audio.MasterVolume, BubblesVolumePercent) is var gain && gain <= 0f)
        {
            // Muted: upstream refuses at the top of PlayOneShot with the comment "Muted — don't
            // touch the audio stack at all" (Services/Audio/AudioService.Playback.cs:183-187), and
            // the refusal is BEFORE the device, so a user at zero master never brings an endpoint up
            // by clicking bubbles.
            outcome = new SoundOutcome.Unavailable("master volume is zero — nothing to play");
        }
        else
        {
            _audio.EnsureDevice();
            outcome = _audio.Arbitration.PlaySfx(path, gain);
        }

        LastPop = outcome;
        return outcome;
    }

    private void RunPop()
    {
        try
        {
            PopNow();
        }
        catch (Exception ex)
        {
            // Contained and NAMED: this runs on a pool thread by default, where an escape ends the
            // process. The arbitration answers typed rather than throwing, so reaching here is a
            // defect somewhere below and must not be silent.
            LastPop = new SoundOutcome.Failed($"{ex.GetType().Name}: {ex.Message}");
            _log?.Invoke($"effect-sound: a pop faulted and was contained ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static string NoClips(IAudioCuePool pool) =>
        $"no clips in {pool.Folder} — this build ships none (the shipping app's own are legacy bytes)";
}
