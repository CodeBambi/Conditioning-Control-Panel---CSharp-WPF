using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>
/// One subliminal's worth of text. The counterpart of <see cref="IFlashImagePool"/>, and
/// deliberately a SEPARATE seam rather than a shared generic one: a flash draws N items with
/// replacement out of a folder that may be slow, and a subliminal draws exactly ONE out of a
/// dictionary already in memory. The two share the word "pool" and nothing else, which is one of
/// SP-101's findings about how far the template actually generalises.
/// </summary>
public interface ISubliminalPhrasePool
{
    /// <summary>
    /// One active phrase, or <c>null</c> when nothing is active. WPF picks uniformly over the
    /// phrases whose pool value is true and returns without showing or counting anything when that
    /// set is empty (<c>Services/Subliminal/SubliminalService.cs:206-215</c>) — the caller must
    /// render that as "nothing to show", never as a failure and never as a subliminal.
    /// </summary>
    string? Draw();

    /// <summary>How many phrases are in play. Read at arm time so the module can say, in typed
    /// form, that it is paced but has nothing to show.</summary>
    int ActiveCount { get; }
}

/// <summary>
/// The product pool: the phrases in the module's own persisted preset.
///
/// <para>No filesystem and no cache. WPF's pool is a settings dictionary read on every firing
/// (<c>SubliminalService.cs:206</c>), so a phrase unchecked mid-session stops appearing at the next
/// firing — there is nothing to invalidate and nothing to re-read, which is the entire reason this
/// pool has none of <see cref="FlashImagePool"/>'s machinery.</para>
/// </summary>
public sealed class SubliminalPhrasePool : ISubliminalPhrasePool
{
    private readonly PersistenceStore<SubliminalPresetDocument> _preset;
    private readonly Random _random;

    public SubliminalPhrasePool(PersistenceStore<SubliminalPresetDocument> preset, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
        _random = random ?? new Random();
    }

    /// <inheritdoc/>
    public int ActiveCount => _preset.Current.ActivePhrases().Count;

    /// <inheritdoc/>
    public string? Draw()
    {
        var active = _preset.Current.ActivePhrases();
        if (active.Count == 0)
        {
            return null;
        }

        // WPF: activeTexts[_random.Next(activeTexts.Count)] (SubliminalService.cs:215). One
        // uniform pick, and repeats across firings are legitimate — there is no shuffle bag
        // upstream and adding one here would change how often a user sees the same phrase twice.
        return active[_random.Next(active.Count)];
    }
}
