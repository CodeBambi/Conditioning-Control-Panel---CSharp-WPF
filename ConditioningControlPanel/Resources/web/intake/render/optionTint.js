/* ============================================================================
 * optionTint.js — COLOUR-NAMED OPTIONS wear their colour.
 *
 * Any option button whose LABEL names a colour ("Pink", "Rose pink", "Sky blue")
 * gets a visual cue in that colour, so a colour choice is legible at a glance
 * instead of being four words in identical grey boxes. The cue is driven off the
 * SAME table the colour harvest resolves picks through (core/palette.js
 * COLOR_SWATCHES / swatchFor), so a bank label that harvests is a label that
 * tints, and neither side can drift.
 *
 * IT IS NOT JUST THE HARVEST. The banks are full of ordinary heat-0 trivia whose
 * options happen to be colours ("Which of these is a shade of blue?"); those
 * tint too. The scan is purely label-driven — beats.js hands the whole card over
 * after its mechanic renderer has built the grid and every `.ib-opt` in it is
 * inspected. Nothing is hardcoded per prompt.
 *
 * THE LATE-RUN LIE (Band.Deepening + Band.Climax only):
 *   From band 3 onward the cue deliberately shows the WRONG colour — the button
 *   reading RED wears blue. It is a fourth-wall/corruption cue in the same
 *   family as the corrupted-question set-piece and the HUD's "it doesn't matter"
 *   counter: the assessment has stopped bothering to be accurate.
 *   - ONE roll per BEAT, not per option. A card either tells the truth or lies
 *     about ALL of its colour options; a single odd button in a truthful row
 *     reads as a rendering bug, a whole row reads as intent.
 *   - the wrong hue is >= MIN_LIE_HUE_DIST degrees away from the true one and is
 *     never a colour another option on the same card names, so it can never be
 *     mistaken for two swatches swapped by accident.
 *   - Calibration / Establishing / Recovery NEVER lie (LIE_CHANCE has no entry
 *     for them), and the harvest prompts themselves are exempt outright — their
 *     picks build the spiral, so their swatches must stay honest. (They are
 *     Calibration-only anyway; the tag check is belt-and-braces.)
 *
 * LEGIBILITY IS NON-NEGOTIABLE. The button's own text colour and background are
 * never touched, so no swatch can ever put dark text on a dark plate: the cue is
 * a filled dot at the leading edge, a tinted border and the dot's own glow —
 * the existing `.ib-opt` visual language (translucent plate, 1px accent border,
 * 12px radius), just wearing a colour. Symmetric padding keeps the centred label
 * clear of the dot. Nothing here touches :hover / :active / :disabled, the
 * `.ib-opts-held` reveal, the freeze gate's `.ixfz-correct` ring (which owns
 * `box-shadow` + `outline` on the BUTTON — this module only ever puts a shadow
 * on the ::before dot, so the two cannot collide) or the focus outline.
 *
 * CAPS + REDUCED MOTION: the glow runs through clampIntensity(), so
 * masterIntensity governs it like any other visual (invariant #2), and a
 * masterIntensity of 0 also silences the lie. Reduced motion drops the glow to a
 * flat dot — the cue itself is static, so it is never withheld.
 *
 * MUST NOT THROW AT IMPORT. No DOM access at module load.
 * ==========================================================================*/

import { Band, clampIntensity, clamp01 } from '../core/contracts.js';
import { COLOR_SWATCHES, COLOR_TAG, swatchFor, hexToRgb, rgbToHsl } from '../core/palette.js';

/** Class beats.js's option buttons wear once cued. */
export const CUE_CLASS = 'ib-colorcue';

/** Chance a BAND-3+ beat lies about every colour option it shows (pre-caps). */
export const LIE_CHANCE = Object.freeze({
  [Band.Deepening]: 0.55,
  [Band.Climax]:    0.85,
});
/** A lie must be at least this far around the hue wheel to read as deliberate. */
export const MIN_LIE_HUE_DIST = 100;

const STYLE_ID = 'ibct-styles';
const CSS = `
.ib-opt.${CUE_CLASS} {
  position: relative;
  border-color: var(--ib-cue-line, rgba(255,105,180,.22));
  padding-left: 48px; padding-right: 48px;
}
.ib-opt.${CUE_CLASS}::before {
  content: ''; position: absolute; left: 17px; top: 50%;
  width: 18px; height: 18px; margin-top: -9px; border-radius: 50%;
  background: var(--ib-cue, #ff69b4);
  box-shadow: 0 0 10px 1px var(--ib-cue-glow, rgba(255,105,180,.3)),
              inset 0 0 0 1px rgba(255,255,255,.45);
  pointer-events: none;
}
@media (max-width: 700px) {
  .ib-opt.${CUE_CLASS} { padding-left: 42px; padding-right: 42px; }
  .ib-opt.${CUE_CLASS}::before { left: 14px; }
}`;

function ensureStyles() {
  if (typeof document === 'undefined') return;
  if (document.getElementById(STYLE_ID)) return;
  try {
    const s = document.createElement('style');
    s.id = STYLE_ID;
    s.textContent = CSS;
    (document.head || document.documentElement).appendChild(s);
  } catch (_e) { /* non-fatal: the buttons simply stay uncued */ }
}

/* ----------------------------------------------------------------------------
 * PURE helpers (no DOM) — exported so the headless smoke test drives the same
 * math the live path does.
 * -------------------------------------------------------------------------- */

/** Longest label (in words) that still reads as "this option IS a colour". */
export const MAX_CUE_WORDS = 3;

/**
 * Label -> swatch hex, or null when this option should stay uncued. swatchFor()
 * is the gate (same resolution the harvest uses, so "Rose pink" -> pink), then
 * two guards keep the cue meaningful on trivia banks:
 *   - WHOLE WORD only: "Blue" tints, "blueberry" does not.
 *   - SHORT labels only: a full sentence that merely mentions a colour
 *     ("Melty, pink, and eager for more") is an opinion, not a swatch.
 * Colours the harvest table does not carry (it is chromatic-only: no red /
 * black / grey) simply resolve to null and that button stays plain — the table
 * is the single source and this module never extends it. Pure.
 */
export function colorOfLabel(label) {
  const s = String(label == null ? '' : label).trim().toLowerCase().replace(/\s+/g, ' ');
  if (!s) return null;
  const hex = swatchFor(s);
  if (!hex) return null;
  if (s.split(' ').length > MAX_CUE_WORDS) return null;
  for (const k of Object.keys(COLOR_SWATCHES)) {
    if (COLOR_SWATCHES[k] !== hex) continue;
    const rx = new RegExp('(^|[^a-z])' + k.replace(/ /g, '\\s+') + '($|[^a-z])');
    if (rx.test(s)) return hex;
  }
  return null;
}

/** Hue of a hex, 0..360 (0 for anything unparseable). Pure. */
export function hueOf(hex) {
  const rgb = hexToRgb(hex);
  return rgb ? rgbToHsl(rgb).h : 0;
}

/** Shortest distance between two hues in degrees (0..180). Pure. */
export function hueDistance(a, b) {
  const d = Math.abs(((a - b) % 360 + 360) % 360);
  return d > 180 ? 360 - d : d;
}

/**
 * Pick a CLEARLY wrong swatch for `hex`: at least MIN_LIE_HUE_DIST degrees away,
 * never one of `taken` (the other options' own colours + lies already handed out
 * on this card). Falls back to the most distant swatch available, and finally to
 * null when nothing qualifies — a null lie simply tells the truth instead.
 * @param {string} hex          the colour the label actually names
 * @param {string[]=} taken     hexes this card may not reuse
 * @param {()=>number=} rnd     0..1 source (defaults to Math.random)
 */
export function mismatchHex(hex, taken, rnd) {
  const r = typeof rnd === 'function' ? rnd : Math.random;
  const used = new Set((taken || []).map((h) => String(h).toLowerCase()));
  const h0 = hueOf(hex);
  const pool = [];
  let widest = null; let widestD = -1;
  for (const k of Object.keys(COLOR_SWATCHES)) {
    const cand = COLOR_SWATCHES[k];
    if (cand.toLowerCase() === String(hex).toLowerCase()) continue;
    if (used.has(cand.toLowerCase())) continue;
    const d = hueDistance(h0, hueOf(cand));
    if (d > widestD) { widestD = d; widest = cand; }
    if (d >= MIN_LIE_HUE_DIST) pool.push(cand);
  }
  if (pool.length) return pool[Math.floor(r() * pool.length) % pool.length];
  return widest;
}

/**
 * Does this beat lie about its colour options? One roll per beat.
 * Deepening/Climax only; harvest prompts are always honest; masterIntensity
 * scales the chance (0 -> never).
 * @param {Object} o { band, prompt, caps, rnd }
 */
export function beatLies(o) {
  const opt = o || {};
  const prompt = opt.prompt;
  const tags = (prompt && Array.isArray(prompt.tags)) ? prompt.tags : [];
  if (tags.indexOf(COLOR_TAG) >= 0) return false;     // the harvest is never lied about
  const base = LIE_CHANCE[opt.band] || 0;
  if (base <= 0) return false;
  const chance = clamp01(clampIntensity(base, opt.caps));
  if (chance <= 0) return false;
  const r = typeof opt.rnd === 'function' ? opt.rnd : Math.random;
  return r() < chance;
}

/** The CSS custom properties one cue needs. Pure. */
export function cueVars(hex, o) {
  const opt = o || {};
  const rgb = hexToRgb(hex) || { r: 255, g: 105, b: 180 };
  const glow = clamp01(clampIntensity(opt.reduced ? 0 : 0.34, opt.caps));
  return {
    '--ib-cue': hex,
    '--ib-cue-line': `rgba(${Math.round(rgb.r)},${Math.round(rgb.g)},${Math.round(rgb.b)},0.55)`,
    '--ib-cue-glow': `rgba(${Math.round(rgb.r)},${Math.round(rgb.g)},${Math.round(rgb.b)},${glow.toFixed(2)})`,
  };
}

/* ----------------------------------------------------------------------------
 * LIVE PATH — one call per beat, from render/beats.js, after the mechanic
 * renderer has built its option grid(s).
 * -------------------------------------------------------------------------- */
/**
 * Cue every colour-named `.ib-opt` inside `container`. Never throws.
 * @param {HTMLElement} container  the beat card (the option grids live in it)
 * @param {Object=} o { prompt, band, depth, caps, reduced, rnd }
 * @returns {number} how many buttons were cued
 */
export function tintColorOptions(container, o) {
  const opt = o || {};
  if (!container || typeof container.querySelectorAll !== 'function') return 0;
  let btns;
  try { btns = Array.prototype.slice.call(container.querySelectorAll('.ib-opt')); }
  catch (_e) { return 0; }
  if (!btns.length) return 0;

  // Resolve every label FIRST: the lie needs to know which colours this card
  // already names before it can avoid handing one of them out as a fake.
  const found = [];
  for (const b of btns) {
    const hex = colorOfLabel(b && b.textContent);
    if (hex) found.push({ el: b, hex });
  }
  if (!found.length) return 0;

  ensureStyles();
  const lying = beatLies(opt);
  const taken = found.map((f) => f.hex);
  let n = 0;
  for (const f of found) {
    let hex = f.hex;
    if (lying) {
      const fake = mismatchHex(f.hex, taken, opt.rnd);
      if (fake) { hex = fake; taken.push(fake); }
    }
    const vars = cueVars(hex, opt);
    try {
      for (const k of Object.keys(vars)) f.el.style.setProperty(k, vars[k]);
      f.el.classList.add(CUE_CLASS);
      n += 1;
    } catch (_e) { /* one button failing must never cost the beat */ }
  }
  return n;
}

export default tintColorOptions;
