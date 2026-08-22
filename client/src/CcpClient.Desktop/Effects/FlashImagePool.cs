using CcpClient.Desktop.Features.Dtrh;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One flash's worth of image paths. A seam, so the session spine's tests never touch a disk
/// and the effect never has to know where images come from — the remote/pack pools WPF also
/// draws from (<c>Services/Flash/FlashService.cs:2595-2652</c>) arrive behind this same call
/// if they are ever ported.
/// </summary>
public interface IFlashImagePool
{
    /// <summary>
    /// <paramref name="count"/> paths, or FEWER (empty) when the pool is empty. WPF returns an
    /// empty list from an empty pool and the flash then shows nothing
    /// (<c>FlashService.cs:2589-2593,585-597</c>) — the caller must render that as "no images",
    /// never as a failure and never as a flash.
    /// </summary>
    IReadOnlyList<string> Draw(int count);
}

/// <summary>
/// The product pool: the user's own images folder.
///
/// <para><b>Location.</b> <c>&lt;dataDir&gt;/assets/images</c> — the same folder DTRH and Graded
/// Intake already draw from (<c>Features/Dtrh/DtrhParticipant.cs:92</c>,
/// <c>Features/Intake/IntakeParticipant.cs:61</c>, <c>DtrhUserMedia.ImagesFolder</c>), which is
/// the port's shape of WPF's <c>App.EffectiveAssetsPath/images</c>
/// (<c>FlashService.cs:331</c>). One folder, not a second one for flashes.</para>
///
/// <para><b>Active-pool discipline.</b> Whether a file is still in play is decided by
/// the port's ONE seam, <see cref="DtrhUserMedia.IsAssetActive"/> — the same uncheck that hides
/// an image from the descent hides it from a flash, which is upstream's shipped contract
/// (<c>AppSettings.cs:1637</c> gate + <c>IntakeHostService.cs:783-790</c> normalization). This
/// class adds no second opinion about selection; it only walks and draws.</para>
///
/// <para><b>Draw policy.</b> WPF takes <c>count</c> INDEPENDENT uniform picks — with
/// replacement, so one flash can legitimately show the same image twice
/// (<c>FlashService.cs:2647-2652</c>). Kept verbatim: it is the visible difference between a
/// flash burst and a slideshow.</para>
///
/// <para><b>Caching.</b> WPF caches the enumeration in <c>_imageList</c> and re-reads it only
/// when the pool is empty (<c>FlashService.cs:2573-2578</c>), so a session does not stat the
/// folder on every flash. Same here — and it means an empty folder is re-checked on every
/// draw, which is what lets a user who drops images in mid-session see flashes without
/// restarting.</para>
/// </summary>
public sealed class FlashImagePool : IFlashImagePool
{
    /// <summary>WPF's own image extension set for the user pool (<c>DtrhUserMedia</c>'s ported list).</summary>
    private static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private readonly string _assetsRoot;
    private readonly Func<(IReadOnlyCollection<string> Disabled, bool UseWhitelist)> _selection;
    private readonly Random _random;
    private readonly object _gate = new();
    private List<string> _cached = [];

    /// <param name="assetsRoot">The user-media root; images are read from its <c>images/</c> folder.</param>
    /// <param name="selection">The persisted deselection state, read per refresh so a later
    /// Assets-tree row's writes are picked up without restarting the pool.</param>
    public FlashImagePool(
        string assetsRoot,
        Func<(IReadOnlyCollection<string> Disabled, bool UseWhitelist)>? selection = null,
        Random? random = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetsRoot);
        _assetsRoot = assetsRoot;
        _selection = selection ?? (static () => (Array.Empty<string>(), false));
        _random = random ?? new Random();
    }

    /// <summary>The folder this pool reads. Content-free: a path, never a file name.</summary>
    public string ImagesFolder => DtrhUserMedia.ImagesFolder(_assetsRoot);

    /// <inheritdoc/>
    public IReadOnlyList<string> Draw(int count)
    {
        lock (_gate)
        {
            if (_cached.Count == 0)
            {
                _cached = Enumerate();
            }

            if (_cached.Count == 0 || count <= 0)
            {
                return [];
            }

            var drawn = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                drawn.Add(_cached[_random.Next(_cached.Count)]);
            }

            return drawn;
        }
    }

    /// <summary>Drops the cached enumeration; the next draw re-reads the folder (WPF's
    /// <c>ClearFileCache</c>/<c>ClearImageCache</c> on stop, <c>FlashService.cs:378</c>).</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = [];
        }
    }

    private List<string> Enumerate()
    {
        var folder = ImagesFolder;
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var (disabled, useWhitelist) = _selection();
        var disabledSet = DtrhUserMedia.BuildDisabledSet(disabled);
        var root = Path.GetFullPath(_assetsRoot);
        var found = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (!Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!DtrhUserMedia.IsAssetActive(disabledSet, root, file, useWhitelist))
                {
                    continue;
                }

                found.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be read is an EMPTY pool, never a fault: WPF's own empty
            // outcome is "no images found", logged and rendered as nothing to show
            // (FlashService.cs:589-593). Throwing here would turn a permissions quirk into a
            // faulted session.
            return found;
        }

        return found;
    }
}
