using CcpClient.Desktop.Persistence;
using CcpClient.Desktop.Session;

namespace CcpClient.Desktop.Effects;

/// <summary>One card's worth of phrase, with upstream's no-repeat rotation.</summary>
public interface ILockCardPhrasePool
{
    /// <summary>
    /// One enabled phrase, or <c>null</c> when none is enabled. Upstream returns without showing a
    /// card at all in that case (<c>Services/LockCard/LockCardService.cs:275-280</c>) — the caller
    /// must render that as "nothing to ask", never as a failure and never as an empty card.
    /// </summary>
    string? Draw();

    /// <summary>How many phrases are in play. Read at arm time so the module can say, in typed form,
    /// that it is paced and has nothing to ask.</summary>
    int ActiveCount { get; }
}

/// <summary>
/// The product pool: the phrases in the module's own persisted preset, drawn with upstream's
/// per-session no-repeat rotation.
///
/// <para><b>The rotation is behaviour, not tidying.</b> <c>PickPhrase</c>
/// (<c>Services/LockCard/LockCardService.cs:324-346</c>) filters the enabled set against the last
/// few shown rather than re-rolling in a loop, so a pure random draw cannot show the same phrase
/// twice running — and if that filter empties the pool, or only one phrase is enabled, it draws from
/// the full list so the module can never loop forever or go silent. Both fallbacks are ported: they
/// are the difference between "you will occasionally see the same card twice" and "the app hangs
/// picking one".</para>
///
/// <para>The window is capped at <c>Math.Min(pool - 1, 3)</c> (<c>:340</c>) so there is always at
/// least one fresh candidate, and a lone phrase is never tracked at all (<c>:337</c>).</para>
///
/// <para>In-memory only, deliberately: upstream's comment at <c>:28-30</c> says the rotation
/// "deliberately does NOT persist to disk".</para>
/// </summary>
public sealed class LockCardPhrasePool : ILockCardPhrasePool
{
    /// <summary>How many distinct just-shown phrases to avoid replaying — upstream's
    /// <c>RecentPhrasesMemory = 3</c> (<c>LockCardService.cs:34</c>).</summary>
    public const int RecentPhrasesMemory = 3;

    private readonly PersistenceStore<LockCardPresetDocument> _preset;
    private readonly Random _random;
    private readonly Queue<string> _recent = new();
    private readonly HashSet<string> _recentSet = new(StringComparer.OrdinalIgnoreCase);

    public LockCardPhrasePool(PersistenceStore<LockCardPresetDocument> preset, Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        _preset = preset;
        _random = random ?? new Random();
    }

    /// <inheritdoc/>
    public int ActiveCount => _preset.Current.EnabledPhrases().Count;

    /// <summary>The enabled phrases themselves, so a panel can show the user what it will ask.</summary>
    public IReadOnlyList<string> ActivePhrases => _preset.Current.EnabledPhrases();

    /// <inheritdoc/>
    public string? Draw()
    {
        var enabled = _preset.Current.EnabledPhrases();
        if (enabled.Count == 0)
        {
            return null;
        }

        // WPF :327-331 — filter, and fall back to the whole list when the filter empties it.
        IReadOnlyList<string> candidates = enabled;
        if (enabled.Count > 1)
        {
            var fresh = enabled.Where(p => !_recentSet.Contains(p)).ToList();
            if (fresh.Count > 0)
            {
                candidates = fresh;
            }
        }

        var phrase = candidates[_random.Next(candidates.Count)];

        // WPF :337-343 — remember it, then trim the window to at most (pool - 1), capped at the
        // memory, so there is always at least one fresh candidate next time. A lone phrase is never
        // tracked, or the filter above would empty on every draw.
        if (enabled.Count > 1 && _recentSet.Add(phrase))
        {
            _recent.Enqueue(phrase);
            var cap = Math.Min(enabled.Count - 1, RecentPhrasesMemory);
            while (_recent.Count > cap)
            {
                _recentSet.Remove(_recent.Dequeue());
            }
        }

        return phrase;
    }
}
