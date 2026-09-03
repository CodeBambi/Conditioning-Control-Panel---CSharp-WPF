/* ============================================================================
 * palette.js — the COLOUR HARVEST: the player's own colours, collected in
 * Calibration, spent on the spiral.
 *
 * THE FEATURE, END TO END:
 *   1. banks/*.json carry a small set of shared COLOUR-PREFERENCE prompts
 *      (tag COLOR_TAG, `"freeChoice": 1`, heat 0) whose OPTIONS ARE COLOURS.
 *   2. core/engine.js serves them SPREAD across the front of Band.Calibration —
 *      on a slot schedule with ordinary heat-0 trivia between them, so they read
 *      as incidental questions rather than a survey block, and all of them land
 *      well before the band ends — via harvestWantsMore()/noteColorServed().
 *   3. boot.js, the moment a beat commits, feeds the chosen option label to
 *      noteColorPick() (which resolves it to a hex through COLOR_SWATCHES) and
 *      fires render/colorFlash.js with deepen(hex).
 *   4. render/beats.js + render/effects.js run their loom-spiral palette through
 *      spiralPalette(fallback), so every spiral in the run is woven from the
 *      colours the player named.
 *
 * WHY A MODULE SINGLETON: the harvest crosses core (engine) and render (beats /
 * effects / boot) and there is exactly one run per page. A singleton keeps the
 * seam to ONE import per consumer instead of threading a new dep through five
 * factory signatures. boot.js calls resetPalette() at run start.
 *
 * COUNTS: at least MIN_COLORS (2) and at most MAX_COLORS (4) colours reach the
 * spiral. The engine plans up to MAX_COLOR_BEATS (5) colour beats so duplicate
 * picks can still be topped up to 4; the harvest closes early the moment 4
 * distinct colours are in. Below 2, spiralPalette() hands back the caller's own
 * themed palette unchanged — a spiral can never end up colourless.
 *
 * MUST NOT THROW AT IMPORT (a throw = silent infinite loader; DtRH gotcha).
 * No DOM access anywhere in this file.
 * ==========================================================================*/

/** Bank tag that marks a colour-preference prompt (the engine's selector). */
export const COLOR_TAG = 'colorpick';

/** Harvest bounds (the product requirement: >=2, <=4 colours per run). */
export const MIN_COLORS = 2;
export const MAX_COLORS = 4;
/** Colour beats the engine may spend at the head of Calibration (>= MAX_COLORS
 *  so a duplicate pick still has a chance to be topped back up to 4). */
export const MAX_COLOR_BEATS = 5;

/** The white thread the loom keeps for contrast when the harvest is short. */
export const SPIRAL_HIGHLIGHT = '#ffffff';

/** Last-ditch palette if a caller passes no fallback of its own. */
const DEFAULT_FALLBACK = Object.freeze(['#ff69b4', '#b06cff', '#ffffff']);

/* ----------------------------------------------------------------------------
 * SWATCHES — bank option label -> hex. The labels here MUST match the option
 * strings of the COLOR_TAG prompts in banks/*.json verbatim (matching is
 * case/space-insensitive). An unknown label resolves to null and is simply not
 * harvested, so a bank typo costs one colour instead of breaking the run.
 * Deliberately all CHROMATIC — a neutral (grey/silver/beige) cannot be deepened
 * into a "deep, saturated hue" and would read as a dirty screen wash.
 * -------------------------------------------------------------------------- */
export const COLOR_SWATCHES = Object.freeze({
  pink:      '#ff69b4',
  blue:      '#3b6ef2',
  green:     '#2fbf5f',
  purple:    '#8b3ff0',
  lavender:  '#b08ae0',
  teal:      '#12a89a',
  rose:      '#f0507a',
  gold:      '#e8b32a',
  crimson:   '#d81b3c',
  turquoise: '#1fd6d0',
  violet:    '#9b30ff',
  bronze:    '#b5651d',
  peach:     '#ff9c73',
  'sky blue':'#57bdf5',
  mint:      '#56e0a8',
  magenta:   '#ff2fb0',
  amber:     '#ffb302',
  indigo:    '#4b3ce0',
  emerald:   '#0fae6e',
  coral:     '#ff6f5e',
});

/* ----------------------------------------------------------------------------
 * COLOUR MATH (pure, dependency-free). All inputs/outputs are '#rrggbb'.
 * -------------------------------------------------------------------------- */
const _clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : n);

/** '#rgb' / '#rrggbb' -> {r,g,b} 0..255, or null when unparseable. */
export function hexToRgb(hex) {
  const s = String(hex == null ? '' : hex).trim().replace(/^#/, '');
  if (s.length === 3) {
    const r = parseInt(s[0] + s[0], 16), g = parseInt(s[1] + s[1], 16), b = parseInt(s[2] + s[2], 16);
    return (r >= 0 && g >= 0 && b >= 0) ? { r, g, b } : null;
  }
  if (s.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(s)) return null;
  return { r: parseInt(s.slice(0, 2), 16), g: parseInt(s.slice(2, 4), 16), b: parseInt(s.slice(4, 6), 16) };
}

/** {r,g,b} 0..255 -> '#rrggbb'. */
export function rgbToHex({ r, g, b }) {
  const h = (n) => {
    const v = Math.max(0, Math.min(255, Math.round(n)));
    return (v < 16 ? '0' : '') + v.toString(16);
  };
  return '#' + h(r) + h(g) + h(b);
}

/** {r,g,b} 0..255 -> {h:0..360, s:0..1, l:0..1}. */
export function rgbToHsl({ r, g, b }) {
  const rr = r / 255, gg = g / 255, bb = b / 255;
  const max = Math.max(rr, gg, bb), min = Math.min(rr, gg, bb);
  const l = (max + min) / 2;
  const d = max - min;
  if (d === 0) return { h: 0, s: 0, l };
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h;
  if (max === rr) h = ((gg - bb) / d) % 6;
  else if (max === gg) h = (bb - rr) / d + 2;
  else h = (rr - gg) / d + 4;
  h *= 60;
  if (h < 0) h += 360;
  return { h, s, l };
}

/** {h,s,l} -> {r,g,b} 0..255. */
export function hslToRgb({ h, s, l }) {
  const hh = ((h % 360) + 360) % 360;
  const c = (1 - Math.abs(2 * _clamp01(l) - 1)) * _clamp01(s);
  const x = c * (1 - Math.abs(((hh / 60) % 2) - 1));
  const m = _clamp01(l) - c / 2;
  let r = 0, g = 0, b = 0;
  if (hh < 60) { r = c; g = x; }
  else if (hh < 120) { r = x; g = c; }
  else if (hh < 180) { g = c; b = x; }
  else if (hh < 240) { g = x; b = c; }
  else if (hh < 300) { r = x; b = c; }
  else { r = c; b = x; }
  return { r: (r + m) * 255, g: (g + m) * 255, b: (b + m) * 255 };
}

/**
 * DEEPEN — the flash colour: the same hue, pushed to a deep, saturated tone so
 * a 40% fullscreen wash reads as "their colour" and not as a pastel haze.
 * Hue is preserved exactly; saturation is floored high and lightness is pulled
 * into a narrow rich band. Pure. Unparseable input returns the input unchanged.
 */
export function deepen(hex) {
  const rgb = hexToRgb(hex);
  if (!rgb) return hex;
  const hsl = rgbToHsl(rgb);
  return rgbToHex(hslToRgb({
    h: hsl.h,
    s: Math.max(0.88, hsl.s),
    l: Math.max(0.34, Math.min(0.48, hsl.l)),
  }));
}

/**
 * SIBLING — a lighter or darker tone of the SAME hue (never a new one). Used
 * only for the 2-colour spiral case: mixing two picks would invent a third hue
 * the player never chose (pink + teal midpoints out to a muddy blue), whereas a
 * sibling gives the loom's bands a middle tone that still belongs to them.
 * Light colours darken, dark colours lighten, so the sibling never collides
 * with the white highlight thread. Pure.
 */
export function sibling(hex) {
  const rgb = hexToRgb(hex);
  if (!rgb) return hex;
  const hsl = rgbToHsl(rgb);
  const dl = hsl.l > 0.55 ? -0.22 : 0.20;
  return rgbToHex(hslToRgb({
    h: hsl.h,
    s: Math.min(1, hsl.s * 0.95),
    l: Math.max(0.16, Math.min(0.82, hsl.l + dl)),
  }));
}

/** Bank option label -> swatch hex, or null when the label is not a colour we
 *  know. Case/whitespace tolerant; also matches a known colour word EMBEDDED in
 *  a longer label ("Rose pink" -> pink) so richer copy still harvests. */
export function swatchFor(label) {
  const s = String(label == null ? '' : label).trim().toLowerCase().replace(/\s+/g, ' ');
  if (!s) return null;
  if (COLOR_SWATCHES[s]) return COLOR_SWATCHES[s];
  // longest key first so 'sky blue' beats 'blue'
  const keys = Object.keys(COLOR_SWATCHES).sort((x, y) => y.length - x.length);
  for (const k of keys) if (s.indexOf(k) >= 0) return COLOR_SWATCHES[k];
  return null;
}

/* ----------------------------------------------------------------------------
 * HARVEST STATE (the singleton). One run per page; boot.js resets it at start.
 * -------------------------------------------------------------------------- */
const harvest = {
  armed: false,   // a bank with colour prompts opened the harvest
  plan: 0,        // colour beats the engine intends to serve (<= MAX_COLOR_BEATS)
  served: 0,      // colour beats actually served
  colors: [],     // distinct hexes, in pick order (<= MAX_COLORS)
};

/** Wipe the harvest. boot.js calls this once per run, before the beat loop. */
export function resetPalette() {
  harvest.armed = false;
  harvest.plan = 0;
  harvest.served = 0;
  harvest.colors.length = 0;
}

/** Engine: arm the harvest with how many colour beats it can actually serve
 *  (0 -> the bank has none, so the harvest is closed immediately and every
 *  spiral falls straight back to its themed palette). */
export function beginHarvest(plan) {
  const n = Math.max(0, Math.min(MAX_COLOR_BEATS, plan | 0));
  harvest.plan = n;
  harvest.armed = n > 0;
  harvest.served = 0;
  harvest.colors.length = 0;
}

/** Engine: a colour beat was just built. */
export function noteColorServed() { harvest.served += 1; }

/** Engine: force the harvest shut (leaving Calibration ends it either way). */
export function closeHarvest() { harvest.armed = false; }

/** Engine: should the next Calibration beat be another colour question? */
export function harvestWantsMore() {
  return harvest.armed
    && harvest.served < harvest.plan
    && harvest.colors.length < MAX_COLORS;
}

/** Render: is the harvest still running? While TRUE the loom spiral stays
 *  retired, so the player can never see a spiral built from colours they have
 *  not given yet. LOAD-BEARING: now that the colour beats are spread across the
 *  front of Calibration (engine.js colorSlot), the last of them can sit at a
 *  depth the garnish spiral's own floor would admit — this flag, not the depth
 *  ramp, is what keeps the spiral behind the harvest. */
export function harvestOpen() { return harvestWantsMore(); }

/** The distinct colours harvested so far, in pick order (capped at MAX_COLORS). */
export function harvestedColors() { return harvest.colors.slice(); }

/**
 * boot.js: the player committed a colour question. Resolves `label` to a hex,
 * stores it (distinct, capped at MAX_COLORS) and returns the hex so the caller
 * can flash it — or null when the label is not a colour / the cap is full.
 */
export function noteColorPick(label) {
  const hex = swatchFor(label);
  if (!hex) return null;
  if (harvest.colors.indexOf(hex) < 0 && harvest.colors.length < MAX_COLORS) {
    harvest.colors.push(hex);
  }
  return hex;
}

/**
 * SPIRAL PALETTE — how N harvested colours become the loom's thread pool.
 *
 * loomField.randomParams2(palette) draws layer-1's 1-3 threads and layer-2's
 * single thread with `threads()` (shuffle + slice) and may pick a centerpiece
 * colour, so what matters is the pool's MEMBERSHIP, not its order. We always
 * hand it exactly FOUR entries so the spiral looks equally deliberate at any
 * harvest size:
 *
 *   4 colours -> [c0, c1, c2, c3]                       purely the player's picks
 *   3 colours -> [c0, c1, c2, white]                    white is the contrast thread
 *   2 colours -> [c0, c1, sibling(c1), white]           a lighter/darker tone of the
 *                                                       second pick gives the bands a
 *                                                       middle value, so a two-colour
 *                                                       spiral reads as banded rather
 *                                                       than striped — without
 *                                                       inventing a hue they never chose
 *   0-1       -> the caller's own themed palette, untouched (fallback)
 *
 * @param {string[]=} fallback  palette to use when the harvest came up short.
 */
export function spiralPalette(fallback) {
  const fb = (Array.isArray(fallback) && fallback.length) ? fallback.slice() : DEFAULT_FALLBACK.slice();
  const picks = harvest.colors.slice(0, MAX_COLORS);
  if (picks.length < MIN_COLORS) return fb;
  if (picks.length >= 4) return picks.slice(0, 4);
  if (picks.length === 3) return picks.concat([SPIRAL_HIGHLIGHT]);
  return [picks[0], picks[1], sibling(picks[1]), SPIRAL_HIGHLIGHT];
}
