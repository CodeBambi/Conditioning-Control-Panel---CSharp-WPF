using System.Globalization;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Bug #615 / #717 - font fallback for the compositor's Skia text layers.
///
/// WPF gives font fallback away for free: a TextBlock asking for "Arial" that hits a glyph Arial
/// doesn't have silently walks the system font-link chain and finds one that does (Microsoft
/// YaHei for CJK, Segoe UI Emoji for emoji...), and it does so PER RUN - one string can be drawn
/// out of three different faces. Skia does NOT. <c>SKCanvas.DrawText</c> is a single-typeface
/// primitive: any codepoint missing from the typeface you handed it maps to glyph 0 and draws as
/// nothing / tofu. So when the unified compositor overlay host became default-ON, every layer
/// that hard-coded a Latin family started dropping non-Latin text on the floor - a zh-CN user's
/// subliminals rendered blank, and the chaos bubble emoji labels (clover / sparkle / heart /
/// cross) went with them. The legacy WPF paths (SubliminalService.CreateTextBlock, the bubble
/// Grid tree) were unaffected, which is why this only appeared after the update.
///
/// #615 fixed that with ONE fallback face for the whole string, chosen from the first codepoint
/// the primary could not draw. #717 is the hole that left: the same zh-CN reporter decorates
/// their phrases ("♤雌畜人妖♤"), and U+2664 WHITE SPADE SUIT - which Arial also lacks - comes
/// FIRST. The font manager answers that codepoint with Segoe UI Symbol, which has no Han
/// ideographs at all, so the entire Chinese body of the phrase drew as .notdef boxes. Picking
/// per string cannot win here: no single installed face covers both.
///
/// So this splits the string into runs instead, one per face, the way WPF would:
///   * pure-ASCII text (the overwhelming majority) short-circuits to a single run in the caller's
///     primary face, so the common path is a byte scan and nothing about its appearance changes;
///   * otherwise each codepoint goes to the primary when the primary covers it (Latin keeps its
///     designed look), and anything else is handed to <see cref="SKFontManager"/> - real coverage
///     and the platform's own font-link chain, not a codepoint-range guess. Consecutive
///     codepoints answered with the same FAMILY join one run, so a Han body stays a single
///     DrawText. Asking per codepoint rather than reusing whatever face the run already has costs
///     ~3us a character (once per phrase, then memoised) and buys the LANGUAGE-correct face: a
///     phrase led by a dingbat resolves that dingbat to one family and the Chinese after it to
///     Microsoft YaHei, instead of dragging the whole line into whichever face the dingbat won;
///   * the split is memoised per (primary, style, exact string), because the inputs are
///     user-authored phrase lists and a fixed variant label set - finite, but user-controlled,
///     hence the hard cap below.
/// Callers draw with <see cref="DrawCentered"/>, which keeps the single-run case on the caller's
/// centred <c>DrawText</c> untouched and only walks the pen manually for genuinely mixed text.
/// </summary>
internal static class GlyphFallback
{
    /// <summary>Cap on memoised strings. Keys come from user-authored phrase lists, so this is
    /// bounded by fiat rather than by trust: past the cap we drop the whole table and refill.
    /// Entries are never disposed - a cached SKTypeface may still be referenced by a live paint
    /// (native refcount) and by the render thread; letting the GC finaliser unref them is the
    /// only safe release.</summary>
    private const int MaxEntries = 256;

    /// <summary>Ceiling on runs per string. A pathological string alternating scripts every
    /// character would otherwise turn one DrawText into hundreds on a render tick; past this the
    /// tail is swept into the last run (it may tofu, but the layer keeps its frame budget).</summary>
    private const int MaxRuns = 32;

    private static readonly Dictionary<string, TextRun[]> _cache = new(StringComparer.Ordinal);
    private static readonly object _sync = new();
    private static string[]? _langTags;
    private static string? _langTagsFor;
    private static bool _langTagsBuilt;

    /// <summary>One stretch of text that a single typeface can draw.</summary>
    internal readonly struct TextRun
    {
        public TextRun(string text, SKTypeface face) { Text = text; Face = face; }
        public string Text { get; }
        public SKTypeface Face { get; }
    }

    /// <summary>
    /// <paramref name="text"/> split into consecutive runs, each tagged with the typeface that can
    /// actually draw it: <paramref name="primary"/> wherever it covers, a system fallback family
    /// elsewhere. Never returns null or an empty array for non-empty input (falls back to a single
    /// primary run on any failure). Safe from any thread; results are cached, so callers should
    /// resolve once at queue time and reuse the array for both measuring and drawing.
    /// </summary>
    public static TextRun[] Split(string? text, SKTypeface primary, SKFontStyle style)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<TextRun>();
        if (IsAscii(text)) return new[] { new TextRun(text, primary) };

        // Cache key: the primary's identity (a different primary has a different hole in its
        // coverage) plus the exact string. '|' is a fine separator - no font family contains it.
        var key = primary.FamilyName + "|" + (int)style.Weight + "|" + (int)style.Slant + "|" + text;

        lock (_sync)
        {
            // Switching UI language changes which face a Han codepoint should resolve to, so the
            // memoised runs stop being right the moment the user picks a different language.
            if (RefreshLangTags()) _cache.Clear();

            if (_cache.TryGetValue(key, out var cached)) return cached;

            var runs = SplitUncached(text, primary, style);
            if (_cache.Count >= MaxEntries) _cache.Clear();   // see MaxEntries note
            _cache[key] = runs;
            return runs;
        }
    }

    private static TextRun[] SplitUncached(string text, SKTypeface primary, SKFontStyle style)
    {
        try
        {
            var runs = new List<TextRun>(4);
            var sb = new System.Text.StringBuilder(text.Length);
            var runFace = primary;

            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                int cp = c;
                int charLen = 1;
                bool lookup = true;

                if (char.IsHighSurrogate(c))
                {
                    // Astral plane (emoji live here): pair up. A LONE surrogate is invalid text
                    // that no face can draw - carry it in the current run rather than querying the
                    // font manager with half a codepoint (which returns garbage) or dropping it
                    // (which would silently edit the user's phrase).
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        cp = char.ConvertToUtf32(c, text[i + 1]);
                        charLen = 2;
                    }
                    else lookup = false;
                }
                else if (char.IsLowSurrogate(c)) lookup = false;

                // Which face draws this codepoint? ASCII is covered by every Latin UI font we use
                // as a primary, so it skips the interop entirely.
                SKTypeface face;
                if (!lookup || IsJoiner(cp)) face = runFace;   // never break a glyph cluster
                else if (cp < 0x80 || primary.ContainsGlyph(cp)) face = primary;
                else if (runs.Count + 1 >= MaxRuns) face = runFace;   // see MaxRuns
                else
                {
                    // Let the platform font manager pick a family for it (DirectWrite's font-link
                    // chain, i.e. the same answer WPF would have arrived at). familyName null =
                    // "any family, best match".
                    var match = SKFontManager.Default.MatchCharacter(null, style, LangTags(), cp);
                    face = match == null
                        ? runFace   // nothing can draw it; splitting the run would not help
                        : !ReferenceEquals(runFace, primary) &&
                          string.Equals(runFace.FamilyName, match.FamilyName, StringComparison.Ordinal)
                            ? runFace   // same family - keep the run (and the instance) we have
                            : match;
                }

                if (!ReferenceEquals(face, runFace) && sb.Length > 0)
                {
                    runs.Add(new TextRun(sb.ToString(), runFace));
                    sb.Clear();
                }
                runFace = face;
                sb.Append(text, i, charLen);
                i += charLen - 1;
            }

            if (sb.Length > 0) runs.Add(new TextRun(sb.ToString(), runFace));
            if (runs.Count > 0) return runs.ToArray();
        }
        catch
        {
            // Native font enumeration can fail on a broken font cache; never take the overlay
            // down over a missing glyph.
        }

        return new[] { new TextRun(text, primary) };
    }

    /// <summary>
    /// Total advance width of <paramref name="runs"/> at <paramref name="paint"/>'s current text
    /// size, plus the vertical extents ACROSS the faces involved (a fallback face is rarely
    /// metric-compatible with the primary, and centring on the primary's metrics alone would sit
    /// the text off-centre). Fills <paramref name="widths"/> with the per-run advances when given.
    /// Leaves <c>paint.Typeface</c> pointing at the last run.
    /// </summary>
    internal static float Measure(TextRun[] runs, SKPaint paint, float[]? widths,
        out float ascent, out float descent)
    {
        float total = 0f;
        ascent = 0f;
        descent = 0f;

        for (int i = 0; i < runs.Length; i++)
        {
            paint.Typeface = runs[i].Face;
            var w = paint.MeasureText(runs[i].Text);
            if (widths != null && i < widths.Length) widths[i] = w;
            total += w;

            var fm = paint.FontMetrics;
            if (fm.Ascent < ascent) ascent = fm.Ascent;      // ascent is negative (up from baseline)
            if (fm.Descent > descent) descent = fm.Descent;
        }
        return total;
    }

    /// <summary>
    /// Draws the runs as one line centred on <paramref name="cx"/>. Single-run text - everything
    /// Latin, and any string one fallback face covers outright - is drawn by the caller's own
    /// centred <c>DrawText</c>, so the common path costs exactly what it did before. Mixed text is
    /// laid out left to right from the centred start; pass <paramref name="widths"/> /
    /// <paramref name="totalWidth"/> from a queue-time <see cref="Measure"/> to keep the render
    /// tick free of measuring. Restores <c>paint.TextAlign</c>; leaves the typeface on the last run.
    /// </summary>
    internal static void DrawCentered(SKCanvas canvas, TextRun[] runs, float cx, float baseline,
        SKPaint paint, float[]? widths = null, float totalWidth = 0f)
    {
        if (runs.Length == 0) return;

        if (runs.Length == 1)
        {
            paint.Typeface = runs[0].Face;
            canvas.DrawText(runs[0].Text, cx, baseline, paint);   // caller's SKTextAlign.Center
            return;
        }

        if (widths == null || widths.Length < runs.Length)
        {
            widths = new float[runs.Length];
            totalWidth = Measure(runs, paint, widths, out _, out _);
        }

        var align = paint.TextAlign;
        paint.TextAlign = SKTextAlign.Left;
        float x = cx - totalWidth / 2f;
        for (int i = 0; i < runs.Length; i++)
        {
            paint.Typeface = runs[i].Face;
            canvas.DrawText(runs[i].Text, x, baseline, paint);
            x += widths[i];
        }
        paint.TextAlign = align;
    }

    /// <summary>
    /// BCP-47 hints for the font manager. Han unification means zh-CN, ja and ko want DIFFERENT
    /// faces for the SAME codepoint, and this is the only thing that disambiguates them.
    ///
    /// ORDER MATTERS AND IS BACKWARDS FROM THE OBVIOUS: Skia's matchFamilyStyleCharacter takes
    /// bcp47 least-significant FIRST, most-significant LAST. #615 passed { "zh-CN", "zh" }, which
    /// hands the decision to the bare "zh" - and Windows answers that with Yu Gothic UI, a
    /// JAPANESE face. The zh-CN reporter's Chinese therefore came out in Japanese glyph forms even
    /// where it rendered at all. Specific tag last => Microsoft YaHei UI, as intended.
    ///
    /// The language is the one the user picked IN THE APP (which is what the phrase list is
    /// written in), not the OS UI culture - the app never assigns CurrentUICulture, so a zh-CN
    /// user on an English Windows would otherwise be handed Latin-first fallbacks. Falls back to
    /// the OS culture if the localization manager is not up yet.
    ///
    /// Returns true when the tags changed (caller drops the memoised runs). Call under _sync.
    /// </summary>
    private static bool RefreshLangTags()
    {
        string? lang = null;
        try { lang = Localization.LocalizationManager.Instance.CurrentLanguage; } catch { }
        if (string.IsNullOrWhiteSpace(lang))
        {
            try { lang = CultureInfo.CurrentUICulture.Name; } catch { }
        }

        if (_langTagsBuilt && string.Equals(lang, _langTagsFor, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var two = string.IsNullOrEmpty(lang)
                ? null
                : new CultureInfo(lang!).TwoLetterISOLanguageName;
            _langTags = string.IsNullOrEmpty(lang)
                ? null
                : string.Equals(lang, two, StringComparison.OrdinalIgnoreCase)
                    ? new[] { lang! }
                    : new[] { two!, lang! };   // least significant first (see above)
        }
        catch { _langTags = string.IsNullOrEmpty(lang) ? null : new[] { lang! }; }

        var changed = _langTagsBuilt;   // first build is not a change - nothing is cached yet
        _langTagsFor = lang;
        _langTagsBuilt = true;
        return changed;
    }

    private static string[]? LangTags() => _langTags;

    /// <summary>
    /// Codepoints that modify the glyph before them rather than standing alone: emoji variation
    /// selectors and ZWJ, skin-tone modifiers, keycaps, combining diacriticals. The font manager
    /// answers these on their own terms (U+200D comes back as Segoe UI, U+FE0F as nothing at all),
    /// so resolving them independently would cut an emoji sequence in half and shape it as two
    /// broken glyphs. They always ride along with the run they attach to.
    /// </summary>
    private static bool IsJoiner(int cp)
        => cp == 0x200D                             // zero-width joiner
        || (cp >= 0xFE00 && cp <= 0xFE0F)           // variation selectors
        || (cp >= 0x1F3FB && cp <= 0x1F3FF)         // skin-tone modifiers
        || (cp >= 0x0300 && cp <= 0x036F)           // combining diacriticals
        || cp == 0x20E3                             // combining enclosing keycap
        || cp == 0xFEFF;                            // zero-width no-break space

    private static bool IsAscii(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] >= 0x80) return false;
        return true;
    }
}
