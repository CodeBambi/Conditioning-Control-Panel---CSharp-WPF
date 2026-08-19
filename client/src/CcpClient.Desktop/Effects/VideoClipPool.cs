namespace CcpClient.Desktop.Effects;

/// <summary>
/// Where the Mandatory Video module's clips come from. A seam, so the module's facts never touch a
/// disk — the same reason <see cref="IFlashImagePool"/> and <see cref="IAudioCuePool"/> exist.
/// </summary>
public interface IVideoClipPool
{
    /// <summary>How many clips are available. The panel shows this, so an empty folder has an
    /// answer.</summary>
    int ActiveCount { get; }

    /// <summary>The folder this pool reads, shown to the user so "where do I put them" has an
    /// answer. A path — never a file name.</summary>
    string Folder { get; }

    /// <summary>One clip path, or null when the pool is empty. Upstream returns null and the caller
    /// resumes without counting anything (<c>Services/Video/VideoService.cs:6647-6650</c>,
    /// <c>Services/BubbleCountService.cs:210-217</c>), so the caller must render null as "nothing to
    /// play", never as a failure.</summary>
    string? Draw();
}

/// <summary>
/// The product pool: a folder of the user's own video clips, drawn as a SHUFFLED BAG.
///
/// <para><b>The draw policy is upstream's and it is NOT the audio pool's.</b> Mind Wipe picks
/// uniformly WITH replacement, so the same clip can come up twice running. Video does not: upstream
/// Fisher-Yates-shuffles the whole enumeration into a QUEUE and dequeues one per firing
/// (<c>Services/Video/VideoService.cs:7042-7044</c> for the shuffle and the queue,
/// <c>:7073-7079</c> for the shuffle itself, <c>:6684</c> for the dequeue), refilling only when the
/// queue runs dry (<c>:6611-6614</c>). <b>A user sees the difference:</b> every clip in the folder
/// plays once before any clip plays twice.</para>
///
/// <para><b>Location, and the divergence it carries.</b> Upstream reads
/// <c>&lt;EffectiveAssetsPath&gt;/videos</c> (<c>VideoService.cs:1212</c>) and CREATES the folder
/// if it is missing (<c>:1213</c>). The port reads <c>&lt;dataDir&gt;/assets/videos</c> — the same
/// user-media root the flash, audio and DTRH pools already draw from — and does NOT create it: a
/// folder the user never asked for is a folder they cannot find, and the panel names the path
/// instead. Same choice, same reason, as the audio pool (D104).</para>
///
/// <para><b>Enumeration is RECURSIVE</b>, and this too differs from the audio pool deliberately:
/// upstream walks <c>SearchOption.AllDirectories</c> for video (<c>:6944</c>, with its own comment
/// "Scan subfolders to support user-organized categories") while its audio pools take the
/// default top-level-only overload. Two upstream behaviours, ported as two.</para>
///
/// <para><b>Extensions</b> are upstream's own set, in upstream's own order
/// (<c>VideoService.cs:6925</c>).</para>
/// </summary>
public sealed class VideoClipPool : IVideoClipPool
{
    /// <summary>Upstream's extension set, in upstream's order (<c>VideoService.cs:6925</c>).</summary>
    private static readonly string[] Extensions = [".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm"];

    /// <summary>The folder under the user-media root that holds the clips. Upstream's own name
    /// (<c>VideoService.cs:1212</c>).</summary>
    public const string VideosFolderName = "videos";

    private readonly string _folder;
    private readonly Random _random;
    private readonly object _gate = new();
    private readonly Queue<string> _bag = new();
    private int _lastEnumeratedCount;

    /// <param name="assetsRoot">The user-media root (<c>SessionParticipant.AssetsRootFor</c>).</param>
    public VideoClipPool(string assetsRoot, Random? random = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetsRoot);
        _folder = Path.Combine(assetsRoot, VideosFolderName);
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
                if (_bag.Count == 0)
                {
                    Refill();
                }

                // What the folder HOLDS, not what is left in the bag: a user with four clips who has
                // seen three must not be told the pool has one.
                return _lastEnumeratedCount;
            }
        }
    }

    /// <inheritdoc/>
    public string? Draw()
    {
        lock (_gate)
        {
            if (_bag.Count == 0)
            {
                Refill();
            }

            return _bag.Count == 0 ? null : _bag.Dequeue();
        }
    }

    /// <summary>Drops the bag; the next draw re-reads and re-shuffles the folder. Upstream's
    /// <c>ReloadAssets</c> (<c>VideoService.cs:1223-1231</c>).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _bag.Clear();
            _lastEnumeratedCount = 0;
        }
    }

    private void Refill()
    {
        var files = Enumerate();
        _lastEnumeratedCount = files.Count;

        // Fisher-Yates, upstream's own (VideoService.cs:7073-7079).
        for (var i = files.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (files[i], files[j]) = (files[j], files[i]);
        }

        _bag.Clear();
        foreach (var file in files)
        {
            _bag.Enqueue(file);
        }
    }

    private List<string> Enumerate()
    {
        try
        {
            if (!Directory.Exists(_folder))
            {
                return [];
            }

            // AllDirectories, because upstream's video walk is recursive (VideoService.cs:6944).
            return Directory
                .EnumerateFiles(_folder, "*.*", SearchOption.AllDirectories)
                .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
