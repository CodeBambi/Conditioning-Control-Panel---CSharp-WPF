/* ============================================================================
 * race/bubbleKinds.js - Racing Thoughts bubble table, data only (no three.js)
 * so score.js and the HUD can read it without pulling the renderer in.
 * Implements the "Bubble kinds" table of CONTRACT.md section `race/bubbles.js`;
 * bubbles.js re-exports BUBBLE_KINDS from here.
 *
 * Every row mirrors game/variants.js + engine/bubbles.js: same sprites, same
 * tints, same payload names, so the race bubbles read as THE SAME bubbles the
 * player pops in DtRH and on the desktop. Every sprite is a page-relative
 * path under /dtrh/assets/, so the table loads the same in a plain browser as
 * it does in WebView2: the four faces that used to come off the ccp.art
 * virtual host (assets/Chaos/bubbles/<id>.png, a 404 outside the app) now ship
 * as 256px copies beside their siblings in assets/bubbles/effects/.
 * ==========================================================================*/

const SPRITE_BASE = '/dtrh/assets/bubbles/effects/';
const PLAIN_SPRITE = '/dtrh/assets/bubbles/bubble.png';   // the classic soap bubble

// kind: 'treat' pops for points only; 'effect' also fires a payloadFx spec.
// payload / overlayKind are the game/payloadFx.js applyPayload names.
// strength: the base 0..1 power of the effect (intensity nudges it up at pop).
// weight: spawn odds; minIntensity gates the kind until the run is that deep.
// category: THE MIX slot (race/cocktail.js CATEGORIES) an effect pop lands in; treats have none.
// spawn: false darkens a row. The row keeps its data, sprite, tint, points and THE MIX slot so the
//   rest of the game still knows the kind, but rollKind never returns it and bubbles.js refuses to
//   place one, so no roll, lane line, rain or track cue can put it on the road. 'video' is dark
//   since 2026-09-06: the tape bubble fired a real mandatory video mid-run and the owner has
//   another use in mind for the slot. The host refuses a video fire-payload too.
export const BUBBLE_KINDS = [
  { id: 'treat',      label: '',  kind: 'treat',  payload: null,         overlayKind: null,  category: null,
    strength: 0,    points: 10, weight: 22,  minIntensity: 0,    tint: 'rgb(184,222,255)', sprite: PLAIN_SPRITE },
  { id: 'golden',     label: '🍀', kind: 'treat',  payload: null,         overlayKind: null,  category: null,
    strength: 0,    points: 50, weight: 1.2, minIntensity: 0,    tint: 'rgb(255,215,0)',   sprite: SPRITE_BASE + 'golden.png' },
  { id: 'lucky',      label: '✧', kind: 'treat',  payload: null,         overlayKind: null,  category: null,
    strength: 0,    points: 25, weight: 1.6, minIntensity: 0,    tint: 'rgb(255,215,0)',   sprite: SPRITE_BASE + 'gold_droplet.png' },
  { id: 'prism',      label: '❂', kind: 'treat',  payload: null,         overlayKind: null,  category: null,
    strength: 0,    points: 30, weight: 1.4, minIntensity: 0.1,  tint: 'rgb(200,168,255)', sprite: SPRITE_BASE + 'prism.png' },
  { id: 'flash',      label: '',  kind: 'effect', payload: 'flash',      overlayKind: null,  category: 'strobe',
    strength: 0.45, points: 15, weight: 6,   minIntensity: 0,    tint: 'rgb(255,208,232)', sprite: SPRITE_BASE + 'flash.png' },
  { id: 'subliminal', label: '♥', kind: 'effect', payload: 'subliminal', overlayKind: null,  category: 'cards',
    strength: 0.45, points: 15, weight: 5,   minIntensity: 0,    tint: 'rgb(176,128,255)', sprite: SPRITE_BASE + 'subliminal.png' },
  { id: 'pink',       label: '◑', kind: 'effect', payload: 'overlay',    overlayKind: 'pink_filter',  category: 'tint',
    strength: 0.5,  points: 20, weight: 4,   minIntensity: 0.1,  tint: 'rgb(255,61,165)',  sprite: SPRITE_BASE + 'pinkfilter.png' },
  { id: 'spiral',     label: '◎', kind: 'effect', payload: 'overlay',    overlayKind: 'spiral',  category: 'overlay',
    strength: 0.5,  points: 20, weight: 4,   minIntensity: 0.15, tint: 'rgb(64,208,192)',  sprite: SPRITE_BASE + 'spiral.png' },
  { id: 'braindrain', label: '☁', kind: 'effect', payload: 'overlay',    overlayKind: 'braindrain',  category: 'overlay',
    strength: 0.55, points: 25, weight: 2.4, minIntensity: 0.4,  tint: 'rgb(64,96,192)',   sprite: SPRITE_BASE + 'braindrain.png' },
  { id: 'glitch',     label: '▚', kind: 'effect', payload: 'glitch',     overlayKind: null,  category: 'corruption',
    strength: 0.5,  points: 20, weight: 3,   minIntensity: 0.2,  tint: 'rgb(120,255,190)', sprite: SPRITE_BASE + 'glitch.png' },
  { id: 'freeze',     label: '❄', kind: 'effect', payload: 'bambiFreeze', overlayKind: null,  category: 'freeze',
    strength: 0.6,  points: 25, weight: 1,   minIntensity: 0.15, tint: 'rgb(138,230,255)', sprite: SPRITE_BASE + 'bambifreeze.png' },
  { id: 'gifrain',    label: '▼', kind: 'effect', payload: 'gifCascade', overlayKind: null,  category: 'cards',
    strength: 0.6,  points: 25, weight: 1.2, minIntensity: 0.45, tint: 'rgb(255,200,61)',  sprite: SPRITE_BASE + 'htlink.png' },
  { id: 'video',      label: '▶', kind: 'effect', payload: 'video',      overlayKind: null,  category: 'video',
    spawn: false,   // dark since 2026-09-06, see the header; the row stays for a later use
    strength: 0.7,  points: 40, weight: 0.35, minIntensity: 0.45, tint: 'rgb(224,64,77)',  sprite: SPRITE_BASE + 'video.png' },
];

export const KIND_BY_ID = Object.fromEntries(BUBBLE_KINDS.map((k) => [k.id, k]));
export const TREAT_IDS = BUBBLE_KINDS.filter((k) => k.kind === 'treat').map((k) => k.id);

/** Weighted roll over the kinds allowed at this intensity, in this room. Dark rows (spawn:false)
 *  are out of the pool entirely, at every intensity and whatever the bias says.
 *  `bias` is the room's bubbleBias map; `extra(kind)` is an optional per-call
 *  multiplier (air lines favour gold, etc). rng defaults to Math.random. */
export function rollKind(intensity, bias = null, extra = null, rng = Math.random) {
  let pool = BUBBLE_KINDS.filter((k) => k.spawn !== false && k.minIntensity <= intensity);
  if (!pool.length) pool = [BUBBLE_KINDS[0]];
  const w = pool.map((k) => k.weight * ((bias && bias[k.id]) || 1) * (extra ? extra(k) : 1));
  let r = rng() * w.reduce((s, v) => s + v, 0);
  for (let i = 0; i < pool.length; i++) { r -= w[i]; if (r <= 0) return pool[i]; }
  return pool[pool.length - 1];
}
