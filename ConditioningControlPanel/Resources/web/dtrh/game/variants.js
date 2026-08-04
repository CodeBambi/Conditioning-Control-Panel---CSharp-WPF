/* ============================================================================
 * variants.js - the FULL DtRH bubble pool as data, ported from
 * ChaosBubbleVariants.cs + ChaosTuning.cs (M4). Every number here mirrors the
 * C# tables - do not retune casually: score parity with the WPF game depends
 * on the size -> Strength mapping and the spawn odds.
 *
 * The pool: treats (flash/subliminal), lives (pink/spiral/braindrain), the
 * freeze pickup, the two giants (video / gif rain), plus the specials built by
 * their own constructors: darters (white rabbits), goldens, hearts, gold
 * droplets, heavies, prisms, brittles, echoes (+children), teases, bound
 * pairs, chaperone pairs and sweeper rabbits.
 * ==========================================================================*/

// Global size envelope (SizeMinGlobal/SizeMaxGlobal): normalises any bubble's
// size into a 0..100 Strength. Strength keys both the native payload power and
// BasePoints, so it uses the UNSCALED size band.
export const SIZE_MIN_GLOBAL = 150;
export const SIZE_MAX_GLOBAL = 320;
// Global field shrink: every bubble renders 25% smaller than its classic band.
export const GLOBAL_SIZE_SCALE = 0.75;
// The two giants (video + gif rain) run a further 30% smaller still.
export const GIANT_SIZE_SCALE = 0.70;

export const MOTION = { FloatUp: 'FloatUp', RainDown: 'RainDown', RoamBounce: 'RoamBounce', SideDrift: 'SideDrift' };

const SPRITE_BASE = '/dtrh/assets/bubbles/effects/';
const ART_BASE = 'https://ccp.art/bubbles/';   // bundled assets/Chaos/bubbles/{id}.png
const MAT_ART_BASE = 'https://ccp.art/materials/'; // bundled assets/Chaos/materials/{id}.png (crafting ingredient cutouts)
const PLAIN_SPRITE = '/dtrh/assets/bubbles/bubble.png'; // the classic dashboard Bubble-Pop soap bubble

// One row per variant: visual band + behaviour + native payload binding.
// payload is the bridge fire-payload shape (minus strength).
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
    sprite: SPRITE_BASE + 'braindrain.png', weight: 1.4, minIntensity: 0.40, fuseMin: 4500, fuseMax: 6500 },
  { id: 'bambifreeze', name: 'Freeze',      kind: 'freeze', payload: null,
    min: 190, max: 250, motion: MOTION.FloatUp,    tint: 'rgb(138,230,255)', label: '❄',
    sprite: ART_BASE + 'bambifreeze.png',   weight: 0.5, minIntensity: 0.15, fuseMin: 0, fuseMax: 0 },
  // The two giants (M4): a long trance, but a mandatory video / gif rain if it
  // goes off. 2026-07 retune (deliberate departure from the C# odds): the video
  // is the rarest live in the pool - VERY rare - and gif rain merely rare. In
  // region mode the spawner is the depth authority (chambers III-IV only, half
  // presence in III - chaosRun.js), so the intensity gates sit just under
  // chamber III's band start (0.46) and only matter on legacy non-region runs.
  { id: 'video',       name: 'Video',       kind: 'live',   payload: { kind: 'video' },
    min: 240, max: 300, motion: MOTION.RainDown,   tint: 'rgb(224,64,77)',   label: '▶',
    sprite: ART_BASE + 'video.png',         weight: 0.35, minIntensity: 0.45, fuseMin: 5000, fuseMax: 7000 },
  { id: 'htlink',      name: 'Gif Rain',    kind: 'live',   payload: { kind: 'gifCascade' },
    min: 200, max: 280, motion: MOTION.FloatUp,    tint: 'rgb(255,200,61)',  label: '▼',
    sprite: ART_BASE + 'htlink.png',        weight: 0.7, minIntensity: 0.45, fuseMin: 4500, fuseMax: 6500 },
];

export const ALL_IDS = VARIANTS.map((v) => v.id);
const byId = Object.fromEntries(VARIANTS.map((v) => [v.id, v]));

// ---- pace / entry-variety tuning (ChaosTuning.cs) ----
export const SIDE_DRIFT_CHANCE = 0.30;       // Mixed motion: slice of verticals arriving sideways
export const SIDE_DRIFT_GRACE_SPAWNS = 5;    // the first few spawns keep the classic bottom rise
export const FREEZE_MAX_ON_SCREEN = 2;       // hard cap on live freeze pickups

// ---- fuse ring phases (ms of fuse remaining) ----
export const RING_FLASH_FROM_MS = 2400;      // yellow <-> red flashing
export const RING_BRINK_MS = 800;            // solid red - the brink window

// ---- global pace slowdown: live bubbles carry more fuse, treats linger longer ----
export const FUSE_GLOBAL_MULT = 1.4;         // every live fuse runs 40% longer (more time to defuse)
export const FUSE_LIVE_BONUS_MS = 1000;      // flat +1s on EVERY live fuse (added after the floor/mults)
export const FUSE_FLOOR_MS = 1600;           // deep-run fuse floor (was a hard 1200) so nothing feels twitchy
export const TREAT_LIFE_MS = 8000;           // how long a plain/treat bubble lingers before it rots (was 5000)
export const HEAVY_LIFE_MS = 12000;          // the slow giant treats get even longer to be reached (was 9000)

// ---- behavioral-bubble tuning (ChaosTuning.cs) ----
export const DEBUT_FUSE_MULT = 1.5;          // first-ever encounter: gentler, longer trance
export const ECHO_SPAWN_CHANCE = 0.05;
export const ECHO_CHILD_SCALE = 0.6;
export const ECHO_CHILD_SPEED_MULT = 1.5;
export const ECHO_CHILD_FUSE_MIN_MS = 3400;
export const ECHO_CHILD_FUSE_MAX_MS = 4100;
export const CHAPERONE_SPAWN_CHANCE = 0.04;
export const CHAPERONE_ORBIT_RADIUS = 80;    // min orbit radius (grows with the pair's sizes)
export const CHAPERONE_ORBIT_GAP = 18;
export const CHAPERONE_ORBIT_PERIOD_SEC = 2.5;
export const TEASE_SPAWN_CHANCE = 0.03;
export const TEASE_LIFE_MS = 6000;
export const TEASE_GOLD_MIN = 5, TEASE_GOLD_MAX = 10;
export const TEASE_DENIED_SCORE = 120;
export const TEASE_CENTER_PULL = 17;         // px/s toward screen center (C# 0.55 DIP/frame)
export const BOUND_SPAWN_CHANCE = 0.03;
export const BOUND_WINDOW_MS = 2500;         // the 2nd defuse must land inside this
export const BOUND_ENRAGE_SPEED_MULT = 1.4;
export const BRITTLE_SPAWN_CHANCE = 0.035;
export const BRITTLE_ARM_MS = 900;           // hover grace while it materialises
export const BRITTLE_SPEED_MULT = 0.85;

// ---- darter (white rabbit) tuning ----
export const DARTER_LIFETIME_MS = 8000;      // safety backstop; despawn is 3-bounces-then-exit
export const DARTER_QUICK_WINDOW_MS = 500;
export const DARTER_TELEGRAPH_MS = 400;
export const DARTER_MAX_BOUNCES = 3;
export const DARTER_SPEED_PXS = 420;         // self-calibrated (C# 9 DIP/frame fixed-step)
export const DARTER_BASE_POINTS = 120;
export const DARTER_QUICK_BONUS = 90;

const rand = (a, b) => a + Math.random() * (b - a);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const strengthOf = (classicSize, effectIntensity) => Math.round(clamp(
  Math.round(clamp((classicSize - SIZE_MIN_GLOBAL) / (SIZE_MAX_GLOBAL - SIZE_MIN_GLOBAL), 0, 1) * 100)
  * effectIntensity, 0, 100));

/** Short color-coded word flashed at the bubble the instant its effect fires. */
export function popWordFor(id) {
  switch (id) {
    case 'flash': return 'FLASH';
    case 'subliminal': return 'WHISPER';
    case 'pink': return 'PINK';
    case 'spiral': return 'SPIRAL';
    case 'braindrain': return 'DRAIN';
    case 'bambifreeze': return 'FREEZE';
    case 'video': return 'WATCH';
    case 'htlink': return 'RAIN';
    case 'golden': return 'LUCKY';
    case 'heart': return 'RESIST';
    case 'gold_droplet': return 'GOLD';
    case 'prism': return '10x!';
    case 'brittle': return 'SHATTER';
    case 'echo': return 'SPLIT';
    default: return '';
  }
}

export const NAME_OF = Object.fromEntries(VARIANTS.map((v) => [v.id, v.name]));

// ---- LIPSTICK (crafted, Part 2): bubble skins ---------------------------------
// Tint-only reskins for the SOFT bubbles (plain soap + benign treats). Threats
// keep their canonical warning colors - the skin never touches kind 'live', the
// giants, or any special constructor. Persisted in settings S.bubbleSkin; read
// live per build so a shade change applies to the next spawn.
import { S as _S, updateSetting as _updateSetting } from '../engine/settings.js';

export const BUBBLE_SKINS = [
  { id: 'default', name: 'bare', desc: 'the tube’s own colors.', acc: '184,222,255' },
  { id: 'gloss', name: 'gloss', desc: 'wet-look pink. everything soft shines.',
    acc: '255,150,205', treat: 'rgb(255,150,205)', plain: 'rgb(255,190,225)' },
  { id: 'noir', name: 'noir', desc: 'smoke and silver. the soft ones dress dark.',
    acc: '186,186,204', treat: 'rgb(186,186,204)', plain: 'rgb(146,146,166)' },
  { id: 'candy', name: 'candy', desc: 'sugar-bright. almost edible.',
    acc: '255,205,97', treat: 'rgb(255,205,97)', plain: 'rgb(151,255,187)' },
];
const skinById = (id) => BUBBLE_SKINS.find((s) => s.id === id) || BUBBLE_SKINS[0];
export function getBubbleSkin() { return skinById(_S.bubbleSkin).id; }
export function setBubbleSkin(id) { _updateSetting('bubbleSkin', skinById(id).id); }
/** The skinned tint for a benign spec, or the canonical tint untouched. */
function skinnedTint(kind, baseTint, plain = false) {
  const s = skinById(_S.bubbleSkin);
  if (s.id === 'default') return baseTint;
  if (plain) return s.plain || baseTint;
  return kind === 'treat' ? (s.treat || baseTint) : baseTint;
}

/**
 * Build one concrete bubble spec from a variant row - the exact C# Build():
 * size random across the band nudged up by intensity, Strength keyed to the
 * CLASSIC size, fuse shortened as the run deepens (min 1200ms), freeze forced
 * off RoamBounce, side-drift entry variety on Mixed motion, the giants a
 * further 30% smaller.
 */
export function build(variant, intensity, {
  fuseTimeMult = 1.0, motionOverride = null, effectIntensity = 1.0,
  sizeScale = 1.0, sideDriftChance = 0.0, fuseMult = 1.0,
} = {}) {
  const t = clamp(Math.random() * 0.7 + intensity * 0.45, 0, 1);
  const size = variant.min + (variant.max - variant.min) * t;
  const strength = strengthOf(size, effectIntensity);
  let visual = GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale);
  if (variant.id === 'video' || variant.id === 'htlink') visual *= GIANT_SIZE_SCALE;

  let motion = motionOverride || variant.motion;
  if (variant.kind === 'freeze' && motion === MOTION.RoamBounce) motion = MOTION.FloatUp;
  if (!motionOverride && motion !== MOTION.RoamBounce
      && sideDriftChance > 0 && Math.random() < sideDriftChance) {
    motion = MOTION.SideDrift;
  }

  let fuseMs = 0;
  if (variant.kind === 'live') {
    const base = variant.fuseMin + Math.random() * Math.max(1, variant.fuseMax - variant.fuseMin);
    fuseMs = Math.max(FUSE_FLOOR_MS, base * (1.0 - intensity * 0.25) * fuseTimeMult * Math.max(0.1, fuseMult) * FUSE_GLOBAL_MULT) + FUSE_LIVE_BONUS_MS;
  }

  return {
    variantId: variant.id,
    kind: variant.kind,
    payload: variant.payload,
    strength,
    sizePx: size * visual,
    tint: skinnedTint(variant.kind, variant.tint),   // LIPSTICK: benign treats only
    label: variant.label,
    sprite: variant.sprite,
    motion,
    fuseMs,
    speedMult: 1.0,
    payMult: 1.0,
    treatLifeMs: variant.kind === 'treat' ? TREAT_LIFE_MS : 0,
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
  return {
    variantId: 'golden',
    kind: 'golden',
    payload: null,
    strength: 0,
    sizePx: rand(110, 140),               // goldens never took the global shrink in C# either
    tint: 'rgb(255,215,0)',
    label: '🍀',
    sprite: SPRITE_BASE + 'golden.png',
    motion: Math.random() < 0.5 ? MOTION.FloatUp : MOTION.RainDown,
    fuseMs: 0,
    speedMult: 2.8,
    payMult: 1.0,
    treatLifeMs: 5000,
  };
}

/** A plain soap bubble - no effect, no fuse. A slice of the ordinary field is
 * these (the classic dashboard Bubble-Pop bubble) so the effect pool is diluted
 * and the fall breathes instead of firing something on every single pop. It pops
 * for points like any treat; its null payload simply fires nothing. */
export function buildPlain(intensity, { sizeScale = 1.0, sideDriftChance = 0.0 } = {}) {
  const t = clamp(Math.random() * 0.7 + intensity * 0.45, 0, 1);
  const size = 170 + (250 - 170) * t;
  const visual = GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale);
  let motion = Math.random() < 0.5 ? MOTION.FloatUp : MOTION.RainDown;
  if (sideDriftChance > 0 && Math.random() < sideDriftChance) motion = MOTION.SideDrift;
  return {
    variantId: 'plain',
    kind: 'treat',            // pops instantly for points; isPlain()/scoring already handle 'treat'
    payload: null,            // firePayload() early-returns on null -> no effect fires
    strength: strengthOf(size, 1.0),
    sizePx: size * visual,
    tint: skinnedTint('treat', 'rgb(184,222,255)', true),   // LIPSTICK: the soap bubble reskins too
    label: '',
    sprite: PLAIN_SPRITE,
    motion,
    fuseMs: 0,
    speedMult: 1.0,
    payMult: 1.0,
    treatLifeMs: TREAT_LIFE_MS,
  };
}

/** Pop-up Notification heart: a lazy once-per-loop kindness (+1 resistance). */
export function buildHeart() {
  return {
    variantId: 'heart', kind: 'heart', payload: null, strength: 0,
    sizePx: rand(88, 110), tint: 'rgb(255,77,110)', label: '💖',
    sprite: ART_BASE + 'heart.png',
    motion: MOTION.RainDown, fuseMs: 0, speedMult: 0.8, payMult: 1.0, treatLifeMs: 0,
  };
}

/** Gold Digger droplet: a gold bead spilled from a popped lucky bubble. */
export function buildGoldDroplet(atX, atY) {
  return {
    variantId: 'gold_droplet', kind: 'droplet', payload: null, strength: 0,
    sizePx: rand(58, 74), tint: 'rgb(255,215,0)', label: '✧',
    sprite: ART_BASE + 'gold_droplet.png',
    motion: MOTION.RainDown, fuseMs: 0, speedMult: 2.2, payMult: 1.0, treatLifeMs: 0,
    spawnAt: { x: atX, y: atY },
  };
}

/** Crafting material (THE BOUDOIR): a grabbable ingredient in the tube. The material
 * row (id/glyph/tint from crafting.js MATERIALS) is passed in so this file stays free
 * of a crafting.js import. The ingredient cutout PNG (assets/Chaos/materials/{id}.png,
 * served off ccp.art) is the bubble face; the glyph rides along as label:null so no
 * emoji overlays the art (chaosField falls back to the tint gradient if the PNG is
 * missing). Pass atX/atY for the rare pop-drop (Gold-Digger droplet shape); omit for tube rises. */
export function buildMaterial(mat, atX, atY) {
  const spec = {
    variantId: 'material_' + mat.id, kind: 'material', matId: mat.id, payload: null, strength: 0,
    sizePx: rand(84, 104), tint: mat.tint, label: null,
    sprite: MAT_ART_BASE + mat.id + '.png',
    motion: atX != null ? MOTION.RainDown : MOTION.FloatUp,
    fuseMs: 0, speedMult: atX != null ? 1.8 : 1.0, payMult: 1.0, treatLifeMs: 5000,
  };
  if (atX != null) spec.spawnAt = { x: atX, y: atY };
  return spec;
}

/** Heavy Drop: every Nth bubble goes giant - x1.55 size, half speed, pays x3. */
export function buildHeavy(intensity, effectIntensity = 1.0, sizeScale = 1.0) {
  const variant = VARIANTS[Math.random() < 0.5 ? 0 : 1];   // the flash + subliminal treats
  const classic = variant.max;                             // top of the band
  return {
    variantId: variant.id,
    kind: 'treat',
    payload: variant.payload,
    strength: strengthOf(classic, effectIntensity),
    sizePx: classic * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale) * 1.55,
    tint: variant.tint,
    label: variant.label,
    sprite: variant.sprite,
    motion: MOTION.RainDown,
    fuseMs: 0,
    speedMult: 0.45,
    payMult: 3.0,          // read in onBenignPopped
    treatLifeMs: HEAVY_LIFE_MS,   // slow faller - give it time to be reached
  };
}

/** "Look at the bright colors..." prism: pays 10x AND fires the copied effect.
 * Video + freeze excluded from the mimic pool; treatOnly = the shielded sin. */
export function buildPrism(intensity, effectIntensity = 1.0, treatOnly = false) {
  const pool = VARIANTS.filter((v) => v.id !== 'video' && v.kind !== 'freeze'
    && (!treatOnly || v.kind !== 'live'));
  const mimic = pool[(Math.random() * pool.length) | 0];
  const size = rand(165, 215);
  return {
    variantId: 'prism', kind: 'prism',
    payload: mimic.payload,
    mimicId: mimic.id,
    strength: strengthOf(size, effectIntensity),
    sizePx: size * GLOBAL_SIZE_SCALE,
    tint: 'rgb(200,168,255)', label: '❂',
    sprite: ART_BASE + 'prism.png',
    motion: Math.random() < 0.5 ? MOTION.RainDown : MOTION.RoamBounce,
    fuseMs: 0, speedMult: 0.7, payMult: 1.0, treatLifeMs: 0,
  };
}

/** The Brittle: thin glass carrying a random LIVE effect - hovering shatters it. */
export function buildBrittle(intensity, effectIntensity = 1.0, sizeScale = 1.0, enabledIds = null) {
  // the mandatory video NEVER hides inside the glass: a shatter is a hover-level
  // accident, and the payload is a 15s unskippable in-face card (2026-07).
  let pool = VARIANTS.filter((v) => v.kind === 'live' && v.id !== 'video');
  // Respect the caller's enabled clamp (the spawner passes its chamber-gated
  // list, so gif rain can't ride the glass shallower than it's allowed to swim).
  if (enabledIds) {
    const ok = pool.filter((v) => v.id === 'pink' || v.id === 'spiral' || v.id === 'braindrain'
      || enabledIds.includes(v.id));
    if (ok.length) pool = ok;
  }
  const mimic = pool[(Math.random() * pool.length) | 0];
  const size = rand(150, 185);
  return {
    variantId: 'brittle', kind: 'brittle',
    payload: mimic.payload,
    mimicId: mimic.id,
    strength: strengthOf(size, effectIntensity),
    sizePx: size * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale),
    tint: 'rgb(217,239,255)', label: '◇',
    sprite: ART_BASE + 'brittle.png',
    motion: Math.random() < 0.5 ? MOTION.FloatUp : MOTION.RainDown,
    fuseMs: 0, speedMult: BRITTLE_SPEED_MULT, payMult: 1.0, treatLifeMs: 0,
    armMs: BRITTLE_ARM_MS,
  };
}

/** The Echo: a live whose payload never fires - triggering it SPLITS it instead. */
export function buildEcho(intensity, fuseTimeMult = 1.0, sizeScale = 1.0, fuseMult = 1.0) {
  const t = clamp(Math.random() * 0.7 + intensity * 0.45, 0, 1);
  const size = 180 + (240 - 180) * t;
  const baseFuse = 3500 + Math.random() * 1500;
  return {
    variantId: 'echo', kind: 'live', isEcho: true,
    payload: null,                       // never fires - the split IS the trigger
    strength: 0,
    sizePx: size * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale),
    tint: 'rgb(201,196,232)', label: '◌',
    sprite: ART_BASE + 'echo.png',
    motion: MOTION.FloatUp,
    fuseMs: Math.max(FUSE_FLOOR_MS, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult * Math.max(0.1, fuseMult) * FUSE_GLOBAL_MULT) + FUSE_LIVE_BONUS_MS,
    speedMult: 1.0, payMult: 1.0, treatLifeMs: 0,
  };
}

/** One Echo split-child at the parent's pop point: a NORMAL light-trio live,
 * smaller, faster, short trance. Children never re-split. */
export function buildEchoChild(parentVisualSizePx, atX, atY, effectIntensity = 1.0) {
  const v = VARIANTS[2 + ((Math.random() * 3) | 0)];   // pink / spiral / braindrain
  const size = Math.max(60, parentVisualSizePx * ECHO_CHILD_SCALE);
  const classicEq = size / GLOBAL_SIZE_SCALE;          // Strength keyed back through the shrink
  return {
    variantId: v.id, kind: 'live',
    payload: v.payload,
    strength: strengthOf(classicEq, effectIntensity),
    sizePx: size, tint: v.tint, label: v.label, sprite: v.sprite,
    motion: MOTION.RoamBounce,
    fuseMs: ECHO_CHILD_FUSE_MIN_MS + Math.random() * Math.max(1, ECHO_CHILD_FUSE_MAX_MS - ECHO_CHILD_FUSE_MIN_MS) + FUSE_LIVE_BONUS_MS,
    speedMult: ECHO_CHILD_SPEED_MULT, payMult: 1.0, treatLifeMs: 0,
    spawnAt: { x: atX, y: atY },
  };
}

/** The Tease: don't touch it. Any mouse-down triggers it AND halves the streak;
 * ignored to expiry it pays the DENIED bonus. Video + freeze excluded. */
export function buildTease(intensity, effectIntensity = 1.0, sizeScale = 1.0) {
  const pool = VARIANTS.filter((v) => v.id !== 'video' && v.kind !== 'freeze');
  const v = pool[(Math.random() * pool.length) | 0];
  const size = rand(170, 210);
  return {
    variantId: 'tease', kind: 'tease',
    payload: v.payload,
    mimicId: v.id,
    strength: strengthOf(size, effectIntensity),
    sizePx: size * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale),
    tint: 'rgb(179,14,46)', label: '✖',
    sprite: ART_BASE + 'tease.png',
    motion: MOTION.RoamBounce,   // drift handled per-frame (center pull + wiggle)
    fuseMs: 0, speedMult: 1.0, payMult: 1.0, treatLifeMs: TEASE_LIFE_MS,
  };
}

let _nextBoundPairId = 1;

/** The Bound: two tethered lives (light trio) - both must come down quickly. */
export function buildBoundPair(intensity, fuseTimeMult = 1.0, effectIntensity = 1.0,
                               sizeScale = 1.0, fuseMult = 1.0) {
  const pairId = _nextBoundPairId++;
  const one = () => {
    const v = VARIANTS[2 + ((Math.random() * 3) | 0)];
    const t = clamp(Math.random() * 0.7 + intensity * 0.45, 0, 1);
    const size = v.min + (v.max - v.min) * t;
    const baseFuse = v.fuseMin + Math.random() * Math.max(1, v.fuseMax - v.fuseMin);
    return {
      variantId: v.id, kind: 'live',
      payload: v.payload,
      strength: strengthOf(size, effectIntensity),
      sizePx: size * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale),
      tint: v.tint, label: v.label, sprite: v.sprite,
      motion: MOTION.RoamBounce,
      fuseMs: Math.max(FUSE_FLOOR_MS, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult * Math.max(0.1, fuseMult) * FUSE_GLOBAL_MULT) + FUSE_LIVE_BONUS_MS,
      speedMult: 1.0, payMult: 1.0, treatLifeMs: 0,
      boundPairId: pairId,
    };
  };
  return [one(), one()];
}

/** The Chaperone: a live (light trio) shielded while its escort treat orbits. */
export function buildChaperonePair(intensity, fuseTimeMult = 1.0, effectIntensity = 1.0,
                                   sizeScale = 1.0, fuseMult = 1.0) {
  const v = VARIANTS[2 + ((Math.random() * 3) | 0)];
  const t = clamp(Math.random() * 0.7 + intensity * 0.45, 0, 1);
  const size = v.min + (v.max - v.min) * t;
  const baseFuse = v.fuseMin + Math.random() * Math.max(1, v.fuseMax - v.fuseMin);
  const live = {
    variantId: v.id, kind: 'live',
    payload: v.payload,
    strength: strengthOf(size, effectIntensity),
    sizePx: size * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale),
    tint: v.tint, label: v.label, sprite: v.sprite,
    motion: MOTION.RoamBounce,
    fuseMs: Math.max(FUSE_FLOOR_MS, baseFuse * (1.0 - intensity * 0.25) * fuseTimeMult * Math.max(0.1, fuseMult) * FUSE_GLOBAL_MULT) + FUSE_LIVE_BONUS_MS,
    speedMult: 1.0, payMult: 1.0, treatLifeMs: 0,
    isChaperoneLive: true,
  };
  const ev = VARIANTS[Math.random() < 0.5 ? 0 : 1];   // flash / subliminal escort
  const esize = rand(95, 120);
  const escort = {
    variantId: ev.id, kind: 'treat',
    payload: ev.payload,
    strength: Math.round(clamp(Math.max(10,
      Math.round(clamp((esize - SIZE_MIN_GLOBAL) / (SIZE_MAX_GLOBAL - SIZE_MIN_GLOBAL), 0, 1) * 100))
      * effectIntensity, 0, 100)),
    sizePx: esize * GLOBAL_SIZE_SCALE * Math.max(0.5, sizeScale),
    tint: ev.tint, label: ev.label, sprite: ev.sprite,
    motion: MOTION.RoamBounce,   // overridden by the orbit while linked
    fuseMs: 0, speedMult: 1.0, payMult: 1.0, treatLifeMs: 0,   // escorts never rot
    isEscort: true,
  };
  return [live, escort];
}

/** Build a darter (white rabbit): benign, fast, telegraphed, self-expiring.
 * sweeper = GG make more GG: born spanked, never caught, mows what it crosses. */
export function buildDarter(intensity, { atX = null, atY = null, sweeper = false } = {}) {
  return {
    variantId: 'darter', kind: 'darter',
    payload: { kind: 'flash' },   // a brief micro-flash on catch
    strength: 8,
    sizePx: rand(72, 96),
    tint: 'rgb(255,77,196)', label: '',
    sprite: ART_BASE + 'darter.png',
    motion: MOTION.RoamBounce,    // darter path overrides speed
    fuseMs: 0, speedMult: 1.0, payMult: 1.0, treatLifeMs: 0,
    isSweeper: sweeper,
    telegraphMs: sweeper ? 150 : DARTER_TELEGRAPH_MS,
    lifetimeMs: DARTER_LIFETIME_MS,
    quickWindowMs: DARTER_QUICK_WINDOW_MS,
    spawnAt: atX != null ? { x: atX, y: atY } : null,
  };
}

/** Per-spawn-tick darter roll (C# RollDarter): density climbs with intensity. */
export function rollDarter(intensity, rateMult = 1.0) {
  // Rabbit spawn rate +15% (x1.15 on both the base and the intensity ramp).
  const chance = (0.014375 + clamp(intensity, 0, 1) * 0.0345) * Math.max(0, rateMult);
  if (Math.random() >= chance) return null;
  return buildDarter(intensity);
}
