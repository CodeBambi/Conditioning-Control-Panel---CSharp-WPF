using System.Globalization;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Bug #615 - font fallback for the compositor's Skia text layers.
///
/// WPF gives font fallback away for free: a TextBlock asking for "Arial" that hits a glyph Arial
/// doesn't have silently walks the system font-link chain and finds one that does (Microsoft
/// YaHei for CJK, Segoe UI Emoji for emoji...). Skia does NOT. <c>SKCanvas.DrawText</c> is a
/// single-typeface primitive: any codepoint missing from the typeface you handed it maps to
/// glyph 0 and draws as nothing / tofu. So when the unified compositor overlay host became
/// default-ON, every layer that hard-coded a Latin family started dropping non-Latin text on the
/// floor - a zh-CN user's subliminals rendered blank, and the chaos bubble emoji labels
/// (clover / sparkle / heart / cross) went with them. The legacy WPF paths
/// (SubliminalService.CreateTextBlock, the bubble Grid tree) were unaffected, which is why this
/// only appeared after the update.
///
/// This is the missing fallback step, kept deliberately cheap because it runs off a render tick:
///   * pure-ASCII text (the overwhelming majority) short-circuits to the caller's primary face,
///     so the common path is a byte scan and nothing about its appearance changes;
///   * otherwise we ask the typeface itself which codepoint it cannot render (real coverage, not
///     a codepoint-range guess) and let <see cref="SKFontManager"/> pick a family that can;
///   * the answer is memoised per (primary family, exact string), because the inputs are
///     user-authored phrase lists and a fixed variant label set - finite, but user-controlled,
///     hence the hard cap below.
/// </summary>
internal static class GlyphFallback
{
    /// <summary>Cap on memoised strings. Keys come from user-authored phrase lists, so this is
    /// bounded by fiat rather than by trust: past the cap we drop the whole table and refill.
    /// Entries are never disposed - a cached SKTypeface may still be referenced by a live paint
    /// (native refcount) and by the render thread; letting the GC finaliser unref them is the
    /// only safe release.</summary>
    private const int MaxEntries = 256;

    private static readonly Dictionary<string, SKTypeface> _cache = new(StringComparer.Ordinal);
    private static readonly object _sync = new();
    private static string[]? _langTags;
    private static bool _langTagsBuilt;

    /// <summary>
    /// The typeface that should draw <paramref name="text"/>: <paramref name="primary"/> when it
    /// covers the string, otherwise a system family that covers the first codepoint it does not.
    /// Never returns null (falls back to <paramref name="primary"/> on any failure). Safe from
    /// any thread.
    ///
    /// NOTE (single-face limitation): one typeface is chosen for the WHOLE string, because the
    /// callers draw with one <c>DrawText</c> call. A string that mixes scripts no single font
    /// covers (Chinese + colour emoji, say) resolves to whichever family covers its FIRST
    /// uncovered codepoint - normally the bulk of the text - and the odd stray glyph can still
    /// come out blank. Fixing that properly means splitting into per-face runs and advancing the
    /// pen manually, which the outlined-subliminal draw would have to be rebuilt around.
    /// </summary>
    public static SKTypeface Resolve(string? text, SKTypeface primary, SKFontStyle style)
    {
        if (string.IsNullOrEmpty(text) || IsAscii(text)) return primary;

        // Cache key: the primary's identity (a different primary has a different hole in its
        // coverage) plus the exact string. '|' is a fine separator - no font family contains it.
        var key = primary.FamilyName + "|" + (int)style.Weight + "|" + (int)style.Slant + "|" + text;

        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var resolved = ResolveUncached(text, primary, style);
            if (_cache.Count >= MaxEntries) _cache.Clear();   // see MaxEntries note
            _cache[key] = resolved;
            return resolved;
        }
    }

    private static SKTypeface ResolveUncached(string text, SKTypeface primary, SKFontStyle style)
    {
        try
        {
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                int cp = c;

                if (char.IsHighSurrogate(c))
                {
                    // Astral plane (emoji live here): pair up, or skip a lone surrogate - asking
                    // the font manager to match half a codepoint returns garbage.
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        cp = char.ConvertToUtf32(c, text[i + 1]);
                        i++;
                    }
                    else continue;
                }
                else if (char.IsLowSurrogate(c)) continue;

                // ASCII is covered by every Latin UI font we use as a primary; skipping it keeps
                // the per-character interop down to the interesting characters only.
                if (cp < 0x80) continue;
                if (primary.ContainsGlyph(cp)) continue;

                // First glyph the primary genuinely cannot draw - let the platform font manager
                // pick a family for it (DirectWrite's font-link chain, i.e. the same answer WPF
                // would have arrived at). familyName null = "any family, best match".
                var match = SKFontManager.Default.MatchCharacter(null, style, LangTags(), cp);
                return match ?? primary;
            }
        }
        catch
        {
            // Native font enumeration can fail on a broken font cache; never take the overlay
            // down over a missing glyph.
        }

        return primary;   // primary covers everything in the string
    }

    /// <summary>BCP-47 hints for the font manager. The user's UI language first (Han unification
    /// means zh-CN, ja and ko want DIFFERENT faces for the SAME codepoint, and this tag is the
    /// only thing that disambiguates them), then the bare language.</summary>
    private static string[]? LangTags()
    {
        if (_langTagsBuilt) return _langTags;
        try
        {
            var ui = CultureInfo.CurrentUICulture;
            var name = ui.Name;
            var two = ui.TwoLetterISOLanguageName;
            _langTags = string.IsNullOrEmpty(name)
                ? null
                : string.Equals(name, two, StringComparison.OrdinalIgnoreCase)
                    ? new[] { name }
                    : new[] { name, two };
        }
        catch { _langTags = null; }
        _langTagsBuilt = true;
        return _langTags;
    }

    private static bool IsAscii(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] >= 0x80) return false;
        return true;
    }
}
