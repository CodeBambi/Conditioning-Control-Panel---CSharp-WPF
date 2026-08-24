namespace CcpClient.Desktop.Effects;

/// <summary>
/// Where one audio module's clips come from. A seam, so the modules' tests never touch a disk and a
/// module never has to know where sound files live — the same reason
/// <see cref="IFlashImagePool"/> exists.
/// </summary>
public interface IAudioCuePool
{
    /// <summary>How many clips are available. The panel shows this so an empty folder has an answer,
    /// exactly as the flash panel names its images folder.</summary>
    int ActiveCount { get; }

    /// <summary>The folder this pool reads, shown to the user so "where do I put them" has an
    /// answer. A path — never a file name.</summary>
    string Folder { get; }

    /// <summary>One clip path, or null when the pool is empty. WPF returns before it plays or counts
    /// anything on an empty pool (<c>Services/LockCard/MindWipeService.cs:704-708</c>,
    /// <c>Services/LockCard/BrainDrainService.cs:181</c>), so the caller must render null as
    /// "nothing to play", never as a failure and never as a firing.</summary>
    string? Draw();

    /// <summary>
    /// Drop the cached enumeration so the next read re-scans the folder.
    ///
    /// <para>On the INTERFACE, and that omission was the defect: the concrete pool has always had
    /// this method, but nothing the product holds could reach it, so no shipping code ever called
    /// it and a clip dropped into the folder mid-session stayed invisible until the process was
    /// restarted. A method only the tests can call is not a re-scan mechanism.</para>
    /// </summary>
    void Invalidate();
}

/// <summary>
/// The product pool: a folder of the user's own audio clips.
///
/// <para><b>Location, and the one place this diverges from upstream.</b> WPF reads
/// <c>&lt;install&gt;/Resources/sounds/mindwipe</c> and <c>.../braindrain</c> — clips that SHIP with
/// the app (<c>MindWipeService.cs:149</c>, <c>BrainDrainService.cs:75</c>). Those bytes belong to the
/// legacy tree and are never forked into <c>client/</c> (the payload rule in this repo's own build
/// props), so the port reads <c>&lt;dataDir&gt;/assets/sounds/&lt;module&gt;</c> — the same
/// user-media root the flash pool, DTRH and Graded Intake already draw from. A fresh install
/// therefore has an EMPTY pool, which is a state upstream also has and handles the same way (its own
/// folder ships empty for Brain Drain — <c>Resources/sounds/braindrain</c> is an empty directory in
/// the shipping tree). The panel names the folder so the state is self-explaining rather than
/// mysterious.</para>
///
/// <para><b>Extensions</b> are upstream's own set, in upstream's own order:
/// <c>.mp3</c>/<c>.wav</c>/<c>.ogg</c> (<c>MindWipeService.cs:162-165</c>,
/// <c>BrainDrainService.cs:87-91</c>).</para>
///
/// <para><b>Draw policy.</b> One uniform pick per firing —
/// <c>_audioFiles[_random.Next(_audioFiles.Length)]</c> (<c>MindWipeService.cs:755</c>,
/// <c>BrainDrainService.cs:197</c>). With replacement, so the same clip can come up twice running;
/// that is upstream's behaviour and a user hears it.</para>
///
/// <para><b>Caching</b> follows <see cref="FlashImagePool"/>: the enumeration is cached and re-read
/// only while it is empty, so a session does not stat the folder on every tick but a user who drops
/// clips in mid-session is picked up without restarting. WPF caches harder — it enumerates once in
/// the constructor and only re-reads when <c>ReloadAudioFiles</c> is called
/// (<c>MindWipeService.cs:133,183-186</c>) — and the port's weaker cache is strictly friendlier.</para>
/// </summary>
public sealed class AudioCuePool : IAudioCuePool
{
    /// <summary>Upstream's extension set (<c>MindWipeService.cs:162-165</c>).</summary>
    private static readonly string[] Extensions = [".mp3", ".wav", ".ogg"];

    /// <summary>The folder under the user-media root that holds every module's clips.</summary>
    public const string SoundsFolderName = "sounds";

    private readonly string _folder;
    private readonly Random _random;
    private readonly bool _withoutReplacement;
    private readonly object _gate = new();
    private List<string> _cached = [];
    private Queue<string> _queue = new();

    /// <param name="assetsRoot">The user-media root (<c>SessionParticipant.AssetsRootFor</c>).</param>
    /// <param name="moduleFolder">The module's own subfolder, upstream's own name
    /// (<c>mindwipe</c>, <c>braindrain</c>, <c>flashes_audio</c>, <c>bubbles</c>).</param>
    /// <param name="random">The draw source, injected so a fact pins an ORDER rather than
    /// re-deriving one.</param>
    /// <param name="withoutReplacement">
    /// Take the flash module's draw instead of the two immersion modules': a shuffled queue dealt
    /// out and refilled only when it empties, so every clip in the folder is heard once before any
    /// is heard twice (<c>Services/Flash/FlashService.cs:3315-3329</c>). The default is upstream's
    /// OTHER draw - one uniform pick per firing, WITH replacement, so the same clip can come up
    /// twice running (<c>Services/LockCard/MindWipeService.cs:755</c>). <b>A user hears the
    /// difference</b>, which is why it is a parameter and not a preference.
    /// </param>
    public AudioCuePool(
        string assetsRoot, string moduleFolder, Random? random = null, bool withoutReplacement = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetsRoot);
        ArgumentException.ThrowIfNullOrEmpty(moduleFolder);
        _folder = Path.Combine(assetsRoot, SoundsFolderName, moduleFolder);
        _random = random ?? new Random();
        _withoutReplacement = withoutReplacement;
    }

    /// <inheritdoc/>
    public string Folder => _folder;

    /// <inheritdoc/>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                if (_cached.Count == 0)
                {
                    _cached = Enumerate();
                }

                return _cached.Count;
            }
        }
    }

    /// <inheritdoc/>
    public string? Draw()
    {
        lock (_gate)
        {
            if (_cached.Count == 0)
            {
                _cached = Enumerate();
            }

            if (_cached.Count == 0)
            {
                return null;
            }

            if (!_withoutReplacement)
            {
                return _cached[_random.Next(_cached.Count)];
            }

            if (_queue.Count == 0)
            {
                // Upstream's refill, expression and all: shuffle the whole folder into a queue and
                // deal from it (`new Queue<string>(files.OrderBy(_ => _random.Next()))`,
                // Services/Flash/FlashService.cs:3325). The refill re-reads the folder here where
                // upstream re-reads its own 60-second file cache (:3321) - strictly fresher, and the
                // user-visible property (no clip repeats until the folder is exhausted) is identical.
                _queue = new Queue<string>(_cached.OrderBy(_ => _random.Next()));
            }

            return _queue.Dequeue();
        }
    }

    /// <summary>Drops the cached enumeration; the next draw re-reads the folder. WPF's
    /// <c>ReloadAudioFiles</c> (<c>MindWipeService.cs:189-192</c>), and - for the shuffled draw -
    /// its <c>ClearFileCache</c>, which throws the half-dealt queue away with the file list
    /// (<c>Services/Flash/FlashService.cs:3489</c>).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = [];
            _queue = new Queue<string>();
        }
    }

    private List<string> Enumerate()
    {
        var found = new List<string>();
        if (!Directory.Exists(_folder))
        {
            // WPF creates the folder here so the user can find it (MindWipeService.cs:153-157).
            // The port does NOT: a pool scan that writes to disk is a side effect in the middle of a
            // read, and the port's answer to "where do I put them" is the panel naming the path —
            // the shape the flash panel already uses (FlashImagePool has no create either).
            return found;
        }

        try
        {
            // TOP LEVEL ONLY, which is upstream's own enumeration: Directory.GetFiles(folder, "*.*")
            // with no SearchOption overload defaults to TopDirectoryOnly (MindWipeService.cs:162,
            // BrainDrainService.cs:87). An earlier draft of this file recursed, which would have made
            // a subfolder of clips audible here and silent in the shipping app — a behaviour
            // difference with no upstream evidence behind it.
            foreach (var file in Directory.EnumerateFiles(_folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read is an EMPTY pool, never a fault — upstream logs and
            // carries on with an empty array (MindWipeService.cs:179-182).
            return found;
        }

        return found;
    }
}
