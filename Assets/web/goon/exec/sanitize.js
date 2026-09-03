/* ============================================================================
 * exec/sanitize.js — the page-side twin of C# GoonMatchService.SanitizeText /
 * Truncate (Services/GoonGame/GoonMatchService.cs:1446-1463).
 *
 * Opponent-supplied strings are UNTRUSTED. C# already sanitizes on receipt, but
 * the page renders them, so it sanitizes again at RENDER time — belt and braces,
 * and the web/mobile clients may one day read frames C# never touched.
 *
 * C# behaviour mirrored EXACTLY:
 *   SanitizeText(text):
 *     null/empty                -> ""
 *     drop '<' and '>'          (markup can never form)
 *     drop char.IsControl(c) unless c == ' '
 *     .Trim() the result
 *   Truncate(value, max):
 *     .Substring(0, max) when longer — i.e. the TRIM HAPPENS BEFORE THE CAP,
 *     which is why sanitizeText() below slices last, not first.
 *
 * Notes on the port (both harmless, both deliberate):
 *   - char.IsControl == Unicode category Cc == U+0000-U+001F and U+007F-U+009F.
 *     We test code units, exactly like C# iterating a string of UTF-16 chars, so
 *     surrogate pairs survive intact and .length/.Length agree on the cap.
 *   - String.Trim() (char.IsWhiteSpace) and JS trim() (WhiteSpace +
 *     LineTerminator) differ only on exotica such as U+FEFF; a stray BOM is
 *     trimmed here and kept by C#. Never load-bearing.
 *
 * Caps live with their call sites in C#:
 *   GoonConsts.OpponentTextMaxChars = 200   (payload/round text)
 *   EmoteTextMaxChars = 60 · EmoteIconMaxChars = 8   (GoonMatchService)
 * ==========================================================================*/

/** GoonConsts.OpponentTextMaxChars */
export const TEXT_MAX_CHARS = 200;
/** GoonMatchService.EmoteTextMaxChars */
export const EMOTE_TEXT_MAX_CHARS = 60;
/** GoonMatchService.EmoteIconMaxChars */
export const EMOTE_ICON_MAX_CHARS = 8;

/**
 * Strip markup/control chars, trim, then cap — Truncate(SanitizeText(s), max).
 * @param {*} s          anything; non-strings are coerced like C# would not see
 * @param {number} [maxChars=200]
 * @returns {string} never null
 */
export function sanitizeText(s, maxChars) {
  if (s === null || s === undefined) return '';
  const text = String(s);
  if (!text) return '';
  let out = '';
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    if (c === 60 /* < */ || c === 62 /* > */) continue;
    // char.IsControl(c) && c != ' '  (space is 0x20, already outside both bands)
    if (c <= 0x1f || (c >= 0x7f && c <= 0x9f)) continue;
    out += text[i];
  }
  out = out.trim();
  const max = (typeof maxChars === 'number' && maxChars >= 0) ? (maxChars | 0) : TEXT_MAX_CHARS;
  return out.length <= max ? out : out.substring(0, max);
}

/**
 * The emote pair, capped per GoonMatchService (text 60 / icon 8).
 * @returns {{text:string, icon:string}}
 */
export function sanitizeEmote(text, icon) {
  return {
    text: sanitizeText(text, EMOTE_TEXT_MAX_CHARS),
    icon: sanitizeText(icon, EMOTE_ICON_MAX_CHARS),
  };
}

/** Opponent display name, per GoonMatchService.OnHello (Truncate(..., 32)). */
export function sanitizeName(name) { return sanitizeText(name, 32); }
