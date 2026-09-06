/* ============================================================================
 * chart/maker/model.js - what a track is made of (chart/MAKER.md, PR M1).
 *
 * Pure: no DOM, no window, no clock, so chart/smoke/maker-check.mjs imports it
 * under node. A track is two lists that share one time axis:
 *
 *   hits    { id, t, setId, n }                 one per trigger word heard
 *   bubs    { id, t, kind, eff, group, trig }   what the racer meets
 *
 * `kind` is a race bubble id ('subliminal', 'flash', 'braindrain', 'pink') or
 * the string 'wall', and a wall carries `eff`, one of the six frame effects.
 * A recipe is the little run of bubbles one trigger word gets; changing a
 * recipe re-places that trigger, hand-moved bubbles included, which is why
 * placement is a function of (hits, recipe, gap) and never of the old list.
 * ==========================================================================*/

/** The six frame effects a wall can carry, in the order the grid shows them. */
export const EFFECTS = {
  melt: ['\u{1FAE0}', 'melt', 'brain drain'],
  blink: ['\u{1F441}', 'blink', 'eyes shut a beat'],
  blackout: ['⬛', 'blackout', 'lights out'],
  shake: ['〰️', 'shake', 'screen judders'],
  snap: ['⚡', 'snap', 'one hard cut'],
  flash: ['✨', 'flash', 'white burst'],
};
export const FX_IDS = Object.keys(EFFECTS);
/** How long each frame effect runs, in seconds. Matches race/frameFx.js. */
export const FX_DUR = { blink: 0.45, blackout: 1.2, snap: 0.12, shake: 0.4, melt: 0.6, flash: 0.3 };

/** The bubble kinds a recipe may ask for: the race id, the css class, the glyph, the name. */
export const KINDS = {
  subliminal: { cls: 'sub', glyph: 'S', name: 'subliminal' },
  flash: { cls: 'flash', glyph: 'F', name: 'flash' },
  braindrain: { cls: 'drain', glyph: 'BD', name: 'brain drain' },
  pink: { cls: 'pink', glyph: 'P', name: 'pink' },
};

/** Six canned recipes, the whole choice there is. `wall:<eff>` is a full row block. */
export const RECIPES = [
  { id: 'shimmer', name: 'shimmer', gap: 0.45, seq: ['subliminal', 'flash', 'subliminal', 'flash', 'subliminal', 'flash', 'wall:melt'] },
  { id: 'drain', name: 'drain', gap: 0.6, seq: ['braindrain', 'braindrain', 'wall:blackout'] },
  { id: 'blink', name: 'blink', gap: 1, seq: ['wall:blink'] },
  { id: 'snap', name: 'snap', gap: 0.5, seq: ['flash', 'wall:snap'] },
  { id: 'triple', name: 'triple', gap: 0.5, seq: ['subliminal', 'subliminal', 'subliminal'] },
  { id: 'pinkwash', name: 'pink wash', gap: 0.5, seq: ['pink', 'pink', 'wall:flash'] },
];
export const RECIPE_BY_ID = Object.fromEntries(RECIPES.map((r) => [r.id, r]));

/** Ticked the moment a file loads. Everything else starts off. */
export const DEFAULT_ON = ['good-girl', 'bambi-sleep', 'w-drop', 'w-blank', 'w-empty', 'w-obey'];
/** The recipe a set starts on. Anything not named here starts on 'triple'. */
export const DEFAULT_RECIPE = {
  'good-girl': 'shimmer', 'bambi-sleep': 'drain', 'w-blank': 'drain',
  'w-empty': 'blink', 'w-drop': 'blink', 'w-obey': 'snap', 'bimbo-doll': 'pinkwash', 'w-pink': 'pinkwash',
};
export const LEAD_SEC = 0.15;          // the first bubble lands this far before the word
export const MIN_GAP_LO = 0.1, MIN_GAP_HI = 2, MIN_GAP_STEP = 0.1, MIN_GAP_DEF = 0.4;

export const recipeFor = (cfg, setId) => RECIPE_BY_ID[(cfg[setId] || {}).recipe] || RECIPE_BY_ID[DEFAULT_RECIPE[setId] || 'triple'];
export const isOn = (cfg, setId) => !!(cfg[setId] && cfg[setId].on);
export const byT = (list) => list.slice().sort((a, b) => a.t - b.t || (a.id < b.id ? -1 : 1));
export const clamp = (v, lo, hi) => (v < lo ? lo : v > hi ? hi : v);

/** m:ss.d, the only time format on the page. */
export function fmt(t) {
  const s = Math.max(0, t);
  return Math.floor(s / 60) + ':' + String(Math.floor(s % 60)).padStart(2, '0') + '.' + Math.floor((s % 1) * 10);
}
/** m:ss, for the ruler and the "showing" line. */
export function fmtShort(t) {
  const s = Math.max(0, t);
  return Math.floor(s / 60) + ':' + String(Math.floor(s % 60)).padStart(2, '0');
}

let seq = 0;
export const newId = (p) => p + (seq++);
export const resetIds = () => { seq = 0; };

/** The bubbles one hit gets: the recipe's run, from `t - LEAD_SEC`, never tighter than minGap. */
export function placeHit(hit, recipe, minGap) {
  const out = [];
  let t = Math.max(0, hit.t - LEAD_SEC);
  const step = Math.max(minGap, recipe.gap);
  for (const s of recipe.seq) {
    const [kind, eff] = s.split(':');
    out.push({ id: newId('b'), t, kind, eff: eff || null, group: hit.id, trig: hit.setId });
    t += step;
  }
  return out;
}

/** Every bubble of every ticked set, placed from scratch. Free walls are the caller's to keep. */
export function placeAll(hits, cfg, minGap) {
  const out = [];
  for (const h of hits) if (isOn(cfg, h.setId)) out.push(...placeHit(h, recipeFor(cfg, h.setId), minGap));
  return byT(out);
}

/**
 * How far the pick may really slide. Picked bubbles never land closer than
 * minGap to one that stays put; nothing goes past 0 or past the end of the file.
 * Both lists come in sorted, so this is one walk, not a pass per pair.
 */
export function clampSlide(bubs, sel, dt, minGap, durationSec = Infinity) {
  const picked = [], still = [];
  for (const b of bubs) (sel.has(b.id) ? picked : still).push(b);
  if (!picked.length) return 0;
  let lo = -Infinity, hi = Infinity, j = 0;
  for (const p of picked) {
    while (j < still.length && still[j].t < p.t) j++;
    if (j < still.length) hi = Math.min(hi, still[j].t - minGap - p.t);
    if (j > 0) lo = Math.max(lo, still[j - 1].t + minGap - p.t);
  }
  lo = Math.max(lo, -picked[0].t);
  if (isFinite(durationSec)) hi = Math.min(hi, durationSec - picked[picked.length - 1].t);
  // Two runs that overlap can leave a picked bubble already inside the gap of one
  // that stays put. Clamping to hi alone would then answer a nudge right with a
  // jump left, so the delta keeps the sign it was asked for: it stops, never turns.
  if (dt > 0) return Math.max(0, Math.min(hi, dt));
  if (dt < 0) return Math.min(0, Math.max(lo, dt));
  return 0;
}

/** Move the pick: bubbles by the clamped delta, tags along with them. */
export function slide(state, dt) {
  const d = clampSlide(state.bubs, state.sel, dt, state.minGap, state.durationSec);
  if (!d) return 0;
  for (const b of state.bubs) if (state.sel.has(b.id)) b.t += d;
  for (const h of state.hits) if (state.sel.has(h.id)) h.t = Math.max(0, h.t + d);
  state.bubs = byT(state.bubs);
  state.hits = byT(state.hits);
  return d;
}

/** shift click: every bubble of that kind (a wall: of that effect), or every tag of that trigger. */
export function alike(state, id) {
  const b = state.bubs.find((x) => x.id === id);
  if (b) return state.bubs.filter((x) => x.kind === b.kind && (b.kind !== 'wall' || x.eff === b.eff)).map((x) => x.id);
  const h = state.hits.find((x) => x.id === id);
  return h ? state.hits.filter((x) => x.setId === h.setId).map((x) => x.id) : [id];
}

/** group -> its bubbles, in time order, for the bands behind them. */
export function groupsOf(bubs) {
  const out = new Map();
  for (const b of bubs) {
    if (!b.group) continue;
    if (!out.has(b.group)) out.set(b.group, []);
    out.get(b.group).push(b);
  }
  return out;
}

/** What the bottom bar says about the pick. */
export function pickLine(state) {
  if (!state.sel.size) return 'nothing picked';
  const n = new Map();
  let words = 0;
  const bump = (k) => n.set(k, (n.get(k) || 0) + 1);
  for (const b of state.bubs) if (state.sel.has(b.id)) bump(b.kind === 'wall' ? 'wall' : KINDS[b.kind].name);
  for (const h of state.hits) if (state.sel.has(h.id)) words++;
  const parts = [...n].map(([k, c]) => c + ' ' + k + (c > 1 && k === 'wall' ? 's' : ''));
  if (words) parts.push(words + ' word' + (words > 1 ? 's' : ''));
  return parts.join(', ') + ' picked. drag one, all slide.';
}

/* ---- what it writes ------------------------------------------------------ */

/** The name that goes on an event: the trigger it came from, else what it is. */
export function labelOf(state, b) {
  const set = state.setById && state.setById.get ? state.setById.get(b.trig) : null;
  if (set && set.name) return set.name;
  return b.kind === 'wall' ? 'wall' : KINDS[b.kind].name;
}

/**
 * The chart the race reads (chart JSON v1, see race/CHART.md and race/chart.js):
 * one event per bubble carrying a hand cue. A bubble is one lane spawn; a wall is
 * `cue.wall` plus the frame effect, and race/cues.js expands it into the six
 * bubbles of a full row and stamps `sure: true` so the density knob leaves them be.
 *
 * The generated road (maker/generate.js) rides underneath: its energy curve, its
 * acts and its analyzer events go in the same file, with no `hand` on them, so
 * race/cues.js reads the road off the table and the recipes off the author.
 */
export function buildChart(state, now = new Date()) {
  const src = state.audio || {};
  const durationSec = Number(state.durationSec) || Number(src.durationSec) || 0;
  const road = (state.road && Array.isArray(state.road.events)) ? state.road : null;
  const names = new Set();
  const events = byT(state.bubs).map((b, i) => {
    const label = labelOf(state, b);
    if (b.trig) names.add(label);
    const cue = b.kind === 'wall'
      ? { wall: 'pink', fx: [{ id: b.eff, strength: 1, dur: FX_DUR[b.eff] }] }
      : { spawn: [{ kindId: b.kind, placement: 'lane', x: 0, h: 1, at: 0 }] };
    return { id: 'm' + i, t: Math.max(0, Math.min(durationSec, b.t)), kind: b.trig ? 'trigger' : 'mark',
      label, conf: 1, dur: 0, weight: 1, hand: true, cue };
  });
  const road_ = road ? road.events.map((e, i) => ({ ...e, id: 'g' + i, t: clamp(e.t, 0, durationSec) })) : [];
  return {
    version: 1, hand: true,
    binSec: road ? road.binSec : 0.5,
    energy: road ? road.energy : [],
    acts: road ? road.acts : [],
    rules: [],
    events: road_.concat(events).sort((a, b) => a.t - b.t),
    source: { name: src.name || 'track', hash: src.hash || '', durationSec, sampleRate: 16000 },
    analysis: { energy: road ? 'maker' : '', words: 'whisper', lexicon: [...names],
      generatedAt: (road && road.generatedAt) || now.toISOString(), partial: false },
  };
}

/** The whole working state, small enough to sit in localStorage. The road goes with
 *  it: it is a minute of arithmetic to rebuild and the author already saw it. */
export function snapshotState(state) {
  return { v: 1, minGap: state.minGap, cfg: state.cfg, bubs: state.bubs, hits: state.hits, road: state.road || null };
}

/** Ids come back off a restore, so the next new wall cannot land on one of them. */
export function bumpIds(bubs) {
  let top = 0;
  for (const b of bubs || []) {
    const n = Number(String(b.id).slice(1));
    if (isFinite(n) && n >= top) top = n + 1;
  }
  while (Number(newId('b').slice(1)) < top) { /* walk the counter past them */ }
}
