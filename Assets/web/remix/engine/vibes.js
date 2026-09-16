/* ============================================================================
 * vibes.js - the on-ramp. Four recipes that fill a whole timeline, plus the
 * dice that rerolls one panel or the entire project.
 *
 * Every function here is pure: (state, seed) -> patch. The project applies the
 * patch, so undo gets one snapshot and the UI gets one 'change' event.
 *
 * A patch may carry: { frames, layout, loop, tiles, blocks }. `blocks` always
 * replaces the whole list; `tiles` only appears when a vibe drops tiles.
 * ==========================================================================*/

import { rngFrom } from './rng.js';
import { EFFECTS, createBlock, clampBlock, defaultParams } from './blocks.js';
import { framesForSeconds } from './clock.js';

export const VIBES = [
  { id: 'grow', name: 'Grow', line: 'tiles land one by one, colour creeps in' },
  { id: 'haunt', name: 'Haunt', line: 'a second loop bleeds in over the first' },
  { id: 'flashdeck', name: 'Flash Deck', line: 'hard cuts with a word between each' },
  { id: 'didyouseeit', name: 'Did You See It', line: 'slow build, one frame you almost catch' },
];

const FALLBACK_WORDS = ['drop', 'obey', 'good girl'];

function words(state) {
  const raw = String((state && state.captionText) || '').trim();
  const list = raw ? raw.split(/\s+/).filter(Boolean) : [];
  return list.length ? list : FALLBACK_WORDS.slice();
}

/** Apply a named vibe. Returns a patch; does not mutate `state`. */
export function vibePatch(name, state, seed) {
  switch (name) {
    case 'grow': return growVibe(state, seed);
    case 'haunt': return hauntVibe(state, seed);
    case 'flashdeck': return flashDeckVibe(state, seed);
    case 'didyouseeit': return didYouSeeItVibe(state, seed);
    default: throw new Error('Unknown vibe');
  }
}

/* ------------------------------------------------------------------ grow -*/

function growVibe(state, seed) {
  const frames = state.frames || framesForSeconds(5);
  const r = rngFrom(seed, 'vibe:grow');
  const blocks = [createBlock({
    effect: 'tint',
    mode: 'creep',
    target: 'canvas',
    start: Math.round(frames * 0.4),
    end: frames,
    params: Object.assign(defaultParams('tint'), {
      colour: r.pick(['pink', 'lavender']),
      corner: r.pick(['tl', 'tr', 'bl', 'br']),
      intensity: 70,
    }),
  }, frames)];
  return {
    frames,
    layout: { mode: 'grow', stageMs: 600 },
    loop: 'snap',
    blocks,
  };
}

/* ----------------------------------------------------------------- haunt -*/

function hauntVibe(state, seed) {
  const frames = state.frames || framesForSeconds(5);
  const r = rngFrom(seed, 'vibe:haunt');
  const blocks = [
    createBlock({
      effect: 'glitch',
      mode: 'ghost',
      target: 'canvas',
      start: Math.round(frames * 0.5),
      end: frames,
      params: Object.assign(defaultParams('glitch'), { strength: 55 }),
    }, frames),
    createBlock({
      effect: 'tint',
      mode: 'creep',
      target: 'canvas',
      start: Math.round(frames * (2 / 3)),
      end: frames,
      params: Object.assign(defaultParams('tint'), {
        colour: r.pick(['lavender', 'pink']),
        corner: r.pick(['tl', 'tr', 'bl', 'br']),
        intensity: 60,
      }),
    }, frames),
  ];
  return {
    frames,
    layout: { mode: 'grow', stageMs: 600 },
    loop: 'snap',
    blocks,
  };
}

/* ------------------------------------------------------------ flash deck -*/

function flashDeckVibe(state, seed) {
  const frames = state.frames || framesForSeconds(5);
  const r = rngFrom(seed, 'vibe:deck');
  const w = r.shuffle(words(state));
  const hold = 15;
  const n = Math.max(1, (state.tiles || []).length);
  const blocks = [];
  let cut = hold;
  let i = 0;
  while (cut < frames && n > 1) {
    blocks.push(createBlock({
      effect: 'caption',
      mode: 'flash',
      target: 'canvas',
      start: cut,
      end: cut + 1,
      params: Object.assign(defaultParams('caption'), {
        text: w[i % w.length],
        colour: 'pink',
        size: 70,
        glow: 60,
      }),
    }, frames));
    cut += hold;
    i++;
  }
  return {
    frames,
    layout: { mode: 'deck', stageMs: 600, deckHold: hold },
    loop: 'clean',
    blocks,
  };
}

/* ----------------------------------------------------- did you see it ----*/

function didYouSeeItVibe(state, seed) {
  const frames = state.frames || framesForSeconds(5);
  const r = rngFrom(seed, 'vibe:dysi');
  const w = words(state);
  const flashAt = Math.max(1, Math.min(frames - 1, Math.round(frames * 0.62)));
  const y1 = r.range(0.28, 0.72);
  const y2 = r.range(0.28, 0.72);
  const leftFirst = r.chance(0.5);
  const path = leftFirst
    ? [{ x: 0.12, y: y1 }, { x: 0.5, y: (y1 + y2) / 2 }, { x: 0.88, y: y2 }]
    : [{ x: 0.88, y: y1 }, { x: 0.5, y: (y1 + y2) / 2 }, { x: 0.12, y: y2 }];
  const blocks = [
    createBlock({
      effect: 'focus',
      mode: 'ring',
      target: 'canvas',
      start: Math.round(frames * 0.15),
      end: frames,
      params: Object.assign(defaultParams('focus'), { radius: 38, path }),
    }, frames),
    createBlock({
      effect: 'caption',
      mode: 'flash',
      target: 'canvas',
      start: flashAt,
      end: flashAt + 1,
      params: Object.assign(defaultParams('caption'), {
        text: r.pick(w),
        size: 80,
        glow: 70,
      }),
    }, frames),
  ];
  return {
    frames,
    layout: { mode: 'grow', stageMs: 900 },
    loop: 'seamless',
    blocks,
  };
}

/* ------------------------------------------------------------- the dice -*/

/**
 * Reroll one block: a new mode and one of its knobs, plus the seeded extras
 * that belong to that effect (spiral arm style, tint colour and corner).
 */
export function rerollBlock(block, seed, opts = {}) {
  const spec = EFFECTS[block.effect];
  if (!spec) return {};
  const r = rngFrom(seed, 'reroll:' + (opts.label || block.id));
  const patch = { mode: r.pick(spec.modes), params: Object.assign({}, block.params) };
  const knob = r.pick(spec.knobs);
  patch.params[knob.key] = r.int(25, 95);
  if (block.effect === 'spiral') {
    patch.params.style = r.int(0, 2);
    patch.params.dir = r.chance(0.5) ? 1 : -1;
  }
  if (block.effect === 'tint') {
    patch.params.colour = r.pick(['pink', 'lavender', 'gold']);
    patch.params.corner = r.pick(['tl', 'tr', 'bl', 'br']);
  }
  if (block.effect === 'caption') {
    patch.params.font = r.pick(['display', 'mono', 'hand', 'block', 'script', 'round', 'pixel']);
    patch.params.colour = r.pick(['pink', 'lavender', 'gold', 'white', 'black']);
  }
  if (block.effect === 'glitch') {
    // which loop the ghost borrows; the panel dice is how you change it
    patch.params.pick = r.int(0, 96);
  }
  // a flash is one frame by definition; leaving one gives the block a span back
  if (block.effect === 'caption') {
    const wasFlash = block.end - block.start <= 1;
    if (patch.mode === 'flash') patch.end = block.start + 1;
    else if (wasFlash) patch.end = block.start + Math.max(2, Math.round((opts.frames || 75) * 0.2));
  }
  return patch;
}

/**
 * Surprise me: pick a vibe from the seed, lay it down, then reroll every block
 * it produced. Returns a full patch.
 */
export function surprisePatch(state, seed) {
  const r = rngFrom(seed, 'surprise');
  const vibe = r.pick(VIBES).id;
  const patch = vibePatch(vibe, state, seed);
  patch.vibe = vibe;
  // drain is shelved: the dice never rolls one, whatever a recipe holds
  patch.blocks = (patch.blocks || []).filter((b) => b.effect !== 'drain').map((b, i) => {
    const p = rerollBlock(b, seed, { label: vibe + ':' + i, frames: patch.frames });
    return clampBlock(Object.assign({}, b, p), patch.frames);
  });
  if (r.chance(0.45) && (state.tiles || []).length > 1) {
    patch.layout = Object.assign({}, patch.layout, { mode: r.pick(['grow', 'shuffle', 'mirror']) });
  }
  return patch;
}
