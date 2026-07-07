/* ============================================================================
 * variants.js - the DtRH bubble pool as data, ported from ChaosBubbleVariants.cs.
 *
 * M3 subset: the two treats (flash / subliminal), the live trio (pink / spiral /
 * braindrain) and the freeze pickup, plus the lucky golden builder. The video /
 * gif-rain giants, darters, hearts, prisms and the behavioral menagerie arrive
 * with M4. Every number here mirrors the C# table - do not retune casually:
 * score parity with the WPF game depends on the size -> Strength mapping.
 * ==========================================================================*/

// Global size envelope (SizeMinGlobal/SizeMaxGlobal): normalises any bubble's
// CLASSIC size into a 0..100 Strength. Strength keys both the native payload
// power and BasePoints, so it uses the UNSCALED size band.
export const SIZE_MIN_GLOBAL = 150;
export const SIZE_MAX_GLOBAL = 320;
// Global field shrink: every bubble renders 25% smaller than its classic band.
export const GLOBAL_SIZE_SCALE = 0.75;

export const MOTION = { FloatUp: 'FloatUp', RainDown: 'RainDown', RoamBounce: 'RoamBounce', SideDrift: 'SideDrift' };

const SPRITE_BASE = '/dtrh/assets/bubbles/effects/';

// One row per variant: visual band + behaviour + native payload binding.
// payload() returns the bridge fire-payload shape (minus strength).
export const VARIANTS = [
  { id: 'flash',       name: 'Flash',       kind: 'treat',  payload: { kind: 'flash' },
    min: 150, max: 210, motion: MOTION.FloatUp,    tint: 'rgb(255,208,232)', label: '',
    sprite: SPRITE_BASE + 'flash.png',      weight: 3.0, minIntensity: 0.00, fuseMin: 0, fuseMax: 0 },
  { id: 'subliminal',  name: 'Subliminal',  kind: 'treat',  payload: { kind: 'subliminal' },
    min: 170, max: 220, motion: MOTION.FloatUp,    tint: 'rgb(176,128,255)', label: '♥',
    sprite: SPRITE_BASE + 'subliminal.png', weight: 3.0, minIntensity: 0.00, fuseMin: 0, fuseMax: 0 },
  { id: 'pink',        name: 'Pink Filter', kind: 'live',   payload: { kind: 'overlay', overlay: 'pink_filter' },
    min: 180, max: 240, motion: MOTION.RainDown,   tint: 'rgb(255,61,165)',  label: '◑',
    sprite: SPRITE_BASE + 'pinkfilter.png', weight: 2.0, minIntensity: 0.10, fuseMin: 3500, fuseMax: 5000 },
  { id: 'spiral',      name: 'Spiral',      kind: 'live',   payload: { kind: 'overlay', overlay: 'spiral' },
    min: 180, max: 240, motion: MOTION.RoamBounce, tint: 'rgb(64,208,192)',  label: '◎',
    sprite: SPRITE_BASE + 'spiral.png',     weight: 2.0, minIntensity: 0.15, fuseMin: 3500, fuseMax: 5000 },
  { id: 'braindrain',  name: 'BrainDrain',  kind: 'live',   payload: { kind: 'overlay', overlay: 'braindrain' },
    min: 240, max: 320, motion: MOTION.RoamBounce, tint: 'rgb(64,96,192)',   label: '☁',
    sprite: SPRITE_BASE + 'braindrain.png', weight: 1.4, minIntensity: 0.25, fuseMin: 4500, fuseMax: 6500 },
  { id: 'bambifreeze', name: 'Freeze',      kind: 'freeze', payload: null,
    min: 190, max: 250, motion: MOTION.FloatUp,    tint: 'rgb(138,230,255)', label: '❄',
    sprite: null,                           weight: 0.5, minIntensity: 0.15, fuseMin: 0, fuseMax: 0 },
];

// ---- pace / entry-variety tuning (ChaosTuning.cs) ----
export const SIDE_DRIFT_CHANCE = 0.30;       // Mixed motion: slice of verticals arriving sideways
export const SIDE_DRIFT_GRACE_SPAWNS = 5;    // the first few spawns keep the classic bottom rise
export const FREEZE_MAX_ON_SCREEN = 2;       // hard cap on live freeze pickups

// ---- fuse ring phases (ms of fuse remaining) ----
export const RING_FLASH_FROM_MS = 2400;      // yellow <-> red flashing
export const RING_BRINK_MS = 800;            // solid red - the brink window

const rand = (a, b) => a + Math.random() * (b - a);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

/** Short color-coded word flashed at the bubble the instant its effect fires. */
export function popWordFor(id) {
  switch (id) {
    case 'flash': return 'FLASH';
    case 'subliminal': return 'WHISPER';
    case 'pink': return 'PINK';
    case 'spiral': return 'SPIRAL';
    case 'braindrain': return 'DRAIN';
    case 'bambifreeze': return 'FREEZE';
    case 'golden': return 'LUCKY';
    default: return '';
  }
}

/**
 * Build one concrete bubble spec from a variant row - the exact C# Build():
 * size random across the band nudged up by intensity, Strength keyed to the
 * CLASSIC size, fuse shortened as the run deepens (min 1200ms), freeze forced
 * off RoamBounce, side-drift entry variety on Mixed motion.
 */
export function build(variant, intensity, {
  fuseTimeMult = 1.0, motionOverride = null, effectIntensity = 1.0,
  sizeScale = 1.0, sideDriftChance = 0.0,
} = {}) {
  const t = clamp(Math.random() * 0.7 + intensity * 0.45, 0, 1);
  const size = variant.min + (variant.max - variant.min) * t;
  const strength = Math.round(clamp((size - SIZE_MIN_GLOBAL) / (SIZE_MAX_GLOBAL - SIZE_MIN_GLOBAL), 0, 1) * 100);
  const visual = size * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale);

  let motion = motionOverride || variant.motion;
  if (variant.kind === 'freeze' && motion === MOTION.RoamBounce) motion = MOTION.FloatUp;
  if (!motionOverride && motion !== MOTION.RoamBounce
      && sideDriftChance > 0 && Math.random() < sideDriftChance) {
    motion = MOTION.SideDrift;
  }

  let fuseMs = 0;
  if (variant.kind === 'live') {
    const base = variant.fuseMin + Math.random() * Math.max(1, variant.fuseMax - variant.fuseMin);
    fuseMs = Math.max(1200, base * (1.0 - intensity * 0.25) * fuseTimeMult);
  }

  return {
    variantId: variant.id,
    kind: variant.kind,
    payload: variant.payload,
    strength: Math.round(clamp(strength * effectIntensity, 0, 100)),
    sizePx: visual,
    tint: variant.tint,
    label: variant.label,
    sprite: variant.sprite,
    motion,
    fuseMs,
    speedMult: 1.0,
    treatLifeMs: variant.kind === 'treat' ? 5000 : 0,
  };
}

/** Weighted pick over the enabled + intensity-gated pool (C# Pick()). */
export function pick(intensity, opts = {}) {
  const { enabledIds = null } = opts;
  const inPool = (v) => v.weight > 0 && (!enabledIds || enabledIds.includes(v.id));
  let pool = VARIANTS.filter((v) => intensity >= v.minIntensity && inPool(v));
  if (!pool.length) pool = VARIANTS.filter(inPool);
  if (!pool.length) pool = [VARIANTS[0]];

  const total = pool.reduce((s, v) => s + v.weight, 0);
  let roll = Math.random() * total;
  let variant = pool[pool.length - 1];
  for (const v of pool) {
    roll -= v.weight;
    if (roll <= 0) { variant = v; break; }
  }
  return build(variant, intensity, opts);
}

/** The lucky golden bubble (C# BuildGolden): benign, small, quick, gone fast.
 * No payload - popping it banks real gold on the spot. */
export function buildGolden() {
  const size = rand(110, 140);
  return {
    variantId: 'golden',
    kind: 'golden',
    payload: null,
    strength: 0,
    sizePx: size,                       // goldens never took the global shrink in C# either
    tint: 'rgb(255,215,0)',
    label: '🍀',              // lucky clover
    sprite: SPRITE_BASE + 'golden.png',
    motion: Math.random() < 0.5 ? MOTION.FloatUp : MOTION.RainDown,
    fuseMs: 0,
    speedMult: 2.8,
    treatLifeMs: 5000,
  };
}
