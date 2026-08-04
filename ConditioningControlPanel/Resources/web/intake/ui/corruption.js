/* ============================================================================
 * SHELL CORRUPTION — the storefront rots as the descent goes deeper.
 *
 * The kawaii kit (ui/kawaii.css) is deliberately innocent: a bouncy pastel shop
 * wrapped around a clinical descent. That innocence is a promise the run is
 * supposed to break. This module is the ONE owner of how far it has been
 * broken: a single 0..1 `corruption` level, derived from the engine's band walk,
 * which drives BOTH the palette and the shell music. One value, two outputs —
 * they can never disagree about how bad things have got.
 *
 * WHERE IT SHOWS. Only on shell surfaces (`--kw-*` tokens): the in-run pause
 * button, the pause menu and the Options panel mounted inside it. The run's own
 * `--intake-*` theme is untouched. The MAIN MENU is band-0 territory — it runs
 * before the first beat and after nothing — so it is always, structurally,
 * clean: nothing sets a non-zero level until the beat loop reaches Deepening,
 * and installPause()/dispose() reset the level at both ends of a run.
 *
 * ---------------------------------------------------------------------------
 * THE BAND MAPPING (and the Recovery decision)
 * ---------------------------------------------------------------------------
 * Bands are contracts.js's five-band walk: Calibration, Establishing,
 * Deepening, Climax, Recovery. Corruption starts at the THIRD — the same
 * "bands 3 + 4 of the five-band walk" convention render/beats.js uses for
 * FREEZE_GATE_BANDS — and, unlike the freeze gate, it does NOT stop at Climax.
 *
 *   Calibration  0.00        (clean — identical to today, pixel for pixel)
 *   Establishing 0.00        (clean)
 *   Deepening    0.22 -> 0.55
 *   Climax       0.55 -> 0.85
 *   Recovery     0.85 -> 1.00   <-- PEAKS HERE, deliberately
 *
 * WHY RECOVERY KEEPS CLIMBING instead of easing back:
 *   1. It is what was asked for — "acts 3, 4 and 5", in that order, more each
 *      time. Easing during Recovery would invert the last third of the ask.
 *   2. The fiction. Recovery is the walk-DOWN of *depth*, not an undo. The
 *      subject surfaces; the room they surface into does not get its pastels
 *      back. A storefront that healed itself on the way out would quietly say
 *      "nothing happened here", which is the opposite of the intended read, and
 *      it would leave the run's final shell state cleaner than its middle.
 *   3. It keeps the model monotonic. `beat.depth` falls 1 -> 0 across Recovery,
 *      so any depth-driven ramp would visibly UN-rot the menu mid-band. The
 *      level is ratcheted (never decreases inside a run) precisely so a
 *      surfacing player cannot watch the damage rewind.
 *
 * Intra-band position is continuous, not a step: Deepening and Climax ramp on
 * `beat.depth` against contracts.js's BAND_DEPTH_FLOOR windows, and Recovery —
 * whose depth is falling — ramps on beats-seen-in-band instead. So the level
 * bleeds in "more and more" rather than snapping three times.
 * ========================================================================== */

import { Band, BAND_DEPTH_FLOOR, DEFAULT_THEME } from '../core/contracts.js';
import { setMusicCorruption } from './menuMusic.js';

/** [floor, ceiling] of the corruption level each band occupies. Contiguous on
 *  purpose: the ceiling of one band is the floor of the next, so a band change
 *  is never a jump. */
const BAND_RANGE = Object.freeze({
  [Band.Calibration]:  [0.00, 0.00],
  [Band.Establishing]: [0.00, 0.00],
  [Band.Deepening]:    [0.22, 0.55],
  [Band.Climax]:       [0.55, 0.85],
  [Band.Recovery]:     [0.85, 1.00],
});

/** The bands corruption exists in at all — "band 3 onward". Exported because
 *  ui/pause.js gates its own deep-band behaviour (the Quit prank) on the SAME
 *  set, and two copies of that list would eventually drift apart. */
export const DEEP_BANDS = Object.freeze([Band.Deepening, Band.Climax, Band.Recovery]);

/** How many Recovery beats it takes to walk the last band's ramp to its top. */
const RECOVERY_RAMP_BEATS = 3;

/* ----------------------------------------------------------------------------
 * STATE — module-level, reset by resetCorruption().
 * -------------------------------------------------------------------------- */
let level = 0;            // the ratcheted 0..1 value; the single source of truth
let curBand = null;
let bandBeats = 0;        // beats seen in the current band (Recovery's ramp)
let applied = -1;         // last level written to the DOM (write-coalescing)

/** The current corruption level, 0..1. */
export function corruptionLevel() { return level; }

/** True for Deepening / Climax / Recovery — bands 3, 4, 5 of the walk. */
export function isDeepBand(band) { return DEEP_BANDS.indexOf(band) >= 0; }

function clamp01(n) {
  const v = Number(n);
  return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0;
}

/**
 * Feed the module a beat. Called once per beat from boot.js's loop (via the
 * pause handle) — cheap, idempotent, and monotonic.
 *
 * @param {string} band  a contracts.js Band value
 * @param {number} depth the beat's 0..1 depth
 */
export function setBandDepth(band, depth) {
  if (band !== curBand) { curBand = band; bandBeats = 0; }
  else { bandBeats++; }

  const range = BAND_RANGE[band];
  if (!range) return level;                    // unknown band: hold where we are
  const [lo, hi] = range;

  let t;
  if (band === Band.Recovery) {
    // Depth is falling here, so it cannot drive the ramp — count beats instead.
    t = clamp01(bandBeats / RECOVERY_RAMP_BEATS);
  } else {
    // Position inside the band's own depth window, e.g. Deepening spans the
    // depth floor .42 up to Climax's floor .72.
    const floor = BAND_DEPTH_FLOOR[band] != null ? BAND_DEPTH_FLOOR[band] : 0;
    const ceil = (band === Band.Climax) ? 1 : (BAND_DEPTH_FLOOR[Band.Climax] || 1);
    const span = Math.max(0.01, ceil - floor);
    t = clamp01((clamp01(depth) - floor) / span);
  }

  // RATCHET. Never decreases inside a run — see the Recovery note in the header.
  const next = lo + (hi - lo) * t;
  if (next > level) level = clamp01(next);
  applyCorruption();
  return level;
}

/** Wipe every trace of corruption. Called at installPause() and at dispose(),
 *  so run N+1 in the same page can never inherit run N's rot. */
export function resetCorruption() {
  level = 0;
  curBand = null;
  bandBeats = 0;
  applyCorruption(true);
}

/* ----------------------------------------------------------------------------
 * COLOUR
 *
 * The kit's tokens are recomputed in JS rather than swapped by a CSS class,
 * for one reason the CSS cannot cover: the bleed colour is the ACTIVE MOD's
 * accent, an arbitrary value out of banks/<niche>.json, and the level is
 * continuous. A `.kw-corrupt[data-band="4"]` block could only ever encode three
 * fixed steps of one fixed hue. So JS computes the final values and writes them
 * as inline custom properties on <html> — which is still "override the tokens at
 * the root", exactly the seam kawaii.css was tokenised for; not one component
 * rule is restyled here. Everything that CAN be expressed continuously from a
 * single scalar (opacities, filters, overlays, animation slowdown) is left to
 * the CORRUPTION section of kawaii.css, which reads `--kw-c`.
 * -------------------------------------------------------------------------- */

/** Base kit palette, mirroring kawaii.css `:root`. Kept here (rather than read
 *  back from the cascade) so a re-apply always starts from the CLEAN value and
 *  can never compound its own output. */
const BASE = Object.freeze({
  pink:      [255, 143, 199],
  pinkDeep:  [231,  95, 168],
  lilac:     [183, 155, 255],
  mint:      [143, 240, 212],
  lemon:     [255, 233, 143],
  cream:     [255, 244, 250],
  ink:       [ 91,  42,  72],
  inkSoft:   [138,  92, 120],
  softHi:    [255, 255, 255],   // .kw-btn--soft gradient top
  softLo:    [255, 230, 242],   // .kw-btn--soft gradient bottom
  scrim:     [ 38,  12,  32],
  /** The "bruise" the pale surfaces are dragged toward before the accent is
   *  folded in — a dead mauve-grey, not black. Keeping the PANEL INTERIOR a lit
   *  surface is what makes the pause menu stay readable at level 1 (see below). */
  bruise:    [154, 143, 160],
});
const BLACK = [0, 0, 0];

function mix(a, b, t) {
  const k = Math.min(1, Math.max(0, t));
  return [
    Math.round(a[0] + (b[0] - a[0]) * k),
    Math.round(a[1] + (b[1] - a[1]) * k),
    Math.round(a[2] + (b[2] - a[2]) * k),
  ];
}
function rgb(c) { return 'rgb(' + c[0] + ', ' + c[1] + ', ' + c[2] + ')'; }
function rgba(c, a) { return 'rgba(' + c[0] + ', ' + c[1] + ', ' + c[2] + ', ' + a.toFixed(3) + ')'; }

/** #abc / #aabbcc / #aabbccdd / rgb() / rgba() -> [r,g,b], else null. */
function parseColor(str) {
  if (!str) return null;
  const s = String(str).trim();
  if (s.charAt(0) === '#') {
    const h = s.slice(1);
    if (h.length === 3 || h.length === 4) {
      const v = [0, 1, 2].map((i) => parseInt(h[i] + h[i], 16));
      return v.some(Number.isNaN) ? null : v;
    }
    if (h.length === 6 || h.length === 8) {
      const v = [0, 2, 4].map((i) => parseInt(h.slice(i, i + 2), 16));
      return v.some(Number.isNaN) ? null : v;
    }
    return null;
  }
  const m = s.match(/^rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)/i);
  if (!m) return null;
  const v = [1, 2, 3].map((i) => Math.round(Number(m[i])));
  return v.some((n) => !Number.isFinite(n)) ? null : v;
}

/**
 * THE ACTIVE MOD COLOUR. boot.js's applyTheme() writes the resolved bank theme
 * (contracts.js themeOf -> theme.accent / theme.accent2) onto <html> as
 * `--intake-accent` / `--intake-accent-2`. We read it back from there rather
 * than re-deriving anything from a mod id, so a bank that ships a custom accent
 * bleeds ITS colour in with no extra wiring. Falls back to DEFAULT_THEME if the
 * theme has not been applied yet (harness / early call).
 */
function accents() {
  let a = null, b = null;
  try {
    const cs = getComputedStyle(document.documentElement);
    a = parseColor(cs.getPropertyValue('--intake-accent'));
    b = parseColor(cs.getPropertyValue('--intake-accent-2'));
  } catch (_e) { /* no DOM / locked down */ }
  return {
    a: a || parseColor(DEFAULT_THEME.accent) || [255, 105, 180],
    b: b || parseColor(DEFAULT_THEME.accent2) || [176, 108, 255],
  };
}

/** Write (or clear) the corrupted token set. `force` re-writes even if the level
 *  has not moved — used by resetCorruption to guarantee a clean slate. */
export function applyCorruption(force) {
  if (typeof document === 'undefined' || !document.documentElement) return;
  const c = level;
  if (!force && Math.abs(c - applied) < 0.005) return;
  applied = c;

  const root = document.documentElement;
  const st = root.style;

  if (c <= 0) {
    try {
      root.classList.remove('kw-corrupt');
      root.removeAttribute('data-kw-band');
      for (const p of TOKENS) st.removeProperty(p);
    } catch (_e) { /* cosmetic only */ }
    try { setMusicCorruption(0); } catch (_e) {}
    return;
  }

  const { a: acc, b: acc2 } = accents();
  // The colour every pale surface is dragged toward: the dead mauve, already
  // carrying a third of the mod's accent. This is the "bleed".
  const tint = mix(BASE.bruise, acc, 0.35 * Math.min(1, 0.4 + 0.6 * c));
  const bleed = 0.60 * c;
  const bleed2 = 0.55 * c;

  const pink     = mix(mix(BASE.pink,     acc,  bleed),        BLACK, 0.42 * c);
  const pinkDeep = mix(mix(BASE.pinkDeep, acc,  bleed),        BLACK, 0.50 * c);
  const lilac    = mix(mix(BASE.lilac,    acc2, bleed2),       BLACK, 0.45 * c);
  const mint     = mix(mix(BASE.mint,     acc2, bleed2 * 0.7), BLACK, 0.50 * c);
  const lemon    = mix(mix(BASE.lemon,    acc,  bleed2 * 0.5), BLACK, 0.50 * c);

  /* CONTRAST INVARIANT — the pause menu is the screen somebody opens because
   * they want OUT, so it has to stay readable at level 1. The two directions
   * are deliberately DIVERGENT rather than crossfading past each other:
   *   panel interior  cream -> a dusty bruise, capped at 0.55 of the way (it
   *                   stays a lit surface, never a black hole)
   *   body ink        already dark -> pushed toward black
   * Contrast therefore INCREASES monotonically with corruption instead of
   * dipping through a mid-grey collision. Measured at level 1 with circe's
   * #d0104a (the most saturated shipped accent): panel rgb(210,164,184) against
   * ink rgb(18,8,14) is ~8.9:1, versus ~11:1 clean — both far past WCAG AA, and
   * the level never passes through a worse value on the way. The DARKNESS the
   * player reads comes from the surround (near-black scrim, vignette, deepened
   * buttons and decor), not from putting text on a dark plate. */
  const cream   = mix(BASE.cream,   tint,  0.55 * c);
  const ink     = mix(BASE.ink,     BLACK, 0.80 * c);
  const inkSoft = mix(BASE.inkSoft, BLACK, 0.55 * c);
  const softHi  = mix(BASE.softHi,  tint,  0.50 * c);
  const softLo  = mix(BASE.softLo,  tint,  0.60 * c);
  const btnEdge = mix(BASE.softHi,  tint,  0.70 * c);
  const scrimC  = mix(BASE.scrim,   mix(BLACK, acc, 0.22), c);

  set(st, '--kw-c', c.toFixed(3));
  set(st, '--kw-bleed', rgb(acc));
  set(st, '--kw-bleed-2', rgb(acc2));
  set(st, '--kw-pink', rgb(pink));
  set(st, '--kw-pink-deep', rgb(pinkDeep));
  set(st, '--kw-lilac', rgb(lilac));
  set(st, '--kw-mint', rgb(mint));
  set(st, '--kw-lemon', rgb(lemon));
  set(st, '--kw-cream', rgb(cream));
  set(st, '--kw-ink', rgb(ink));
  set(st, '--kw-ink-soft', rgb(inkSoft));
  set(st, '--kw-soft-1', rgb(softHi));
  set(st, '--kw-soft-2', rgb(softLo));
  set(st, '--kw-btn-edge', rgb(btnEdge));
  set(st, '--kw-panel-bg', rgba(cream, 0.96 + 0.04 * c));
  set(st, '--kw-panel-border', rgba(mix(pink, acc, 0.5), 0.9));
  set(st, '--kw-panel-shadow',
    '0 18px 48px rgba(' + Math.round(20 - 14 * c) + ', 4, ' + Math.round(16 - 10 * c) + ', ' +
    (0.38 + 0.42 * c).toFixed(2) + '), 0 0 0 ' + (6 + 4 * c).toFixed(0) + 'px ' + rgba(acc, 0.22 + 0.20 * c));
  set(st, '--kw-scrim', rgba(scrimC, 0.62 + 0.30 * c));
  // .kw-pause-scrim hardcodes its own (lighter) background so the frozen run
  // stays visible through it; give it a token of its own to darken toward.
  set(st, '--kw-pause-scrim', rgba(scrimC, 0.46 + 0.42 * c));

  try {
    root.classList.add('kw-corrupt');
    if (curBand) root.setAttribute('data-kw-band', String(curBand));
  } catch (_e) { /* cosmetic only */ }

  try { setMusicCorruption(c); } catch (_e) { /* audio is best-effort */ }
}

function set(st, prop, value) { try { st.setProperty(prop, value); } catch (_e) {} }

/** Every property applyCorruption() may write, so reset can remove exactly them
 *  and nothing else (the run's own --intake-* vars live on the same element). */
const TOKENS = Object.freeze([
  '--kw-c', '--kw-bleed', '--kw-bleed-2', '--kw-pink', '--kw-pink-deep',
  '--kw-lilac', '--kw-mint', '--kw-lemon', '--kw-cream', '--kw-ink',
  '--kw-ink-soft', '--kw-soft-1', '--kw-soft-2', '--kw-btn-edge',
  '--kw-panel-bg', '--kw-panel-border', '--kw-panel-shadow', '--kw-scrim',
  '--kw-pause-scrim',
]);
