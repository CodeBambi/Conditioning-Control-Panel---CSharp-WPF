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
    private readonly object _gate = new();
    private List<string> _cached = [];

    /// <param name="assetsRoot">The user-media root (<c>SessionParticipant.AssetsRootFor</c>).</param>
    /// <param name="moduleFolder">The module's own subfolder, upstream's own name
    /// (<c>mindwipe</c>, <c>braindrain</c>).</param>
    public AudioCuePool(string assetsRoot, string moduleFolder, Random? random = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetsRoot);
        ArgumentException.ThrowIfNullOrEmpty(moduleFolder);
        _folder = Path.Combine(assetsRoot, SoundsFolderName, moduleFolder);
        _random = random ?? new Random();
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

            return _cached.Count == 0 ? null : _cached[_random.Next(_cached.Count)];
        }
    }

    /// <summary>Drops the cached enumeration; the next draw re-reads the folder. WPF's
    /// <c>ReloadAudioFiles</c> (<c>MindWipeService.cs:189-192</c>).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = [];
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
