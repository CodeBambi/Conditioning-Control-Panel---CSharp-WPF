/* ============================================================================
 * blocks.js - the timeline's unit of work.
 *
 * A block is { id, effect, mode, target, start, end, params }. `start`/`end`
 * are master frames, `end` exclusive, minimum length 1. `target` is 'canvas'
 * or a tile id. Strength ramps in over the first 3 frames and out over the
 * last 3, automatically, so nothing ever pops on unless the block is a flash.
 *
 * Pure. No DOM.
 * ==========================================================================*/

export const RAMP_FRAMES = 3;

export const TINT_COLOURS = {
  pink: '#FF69B4',
  lavender: '#B8A6E8',
  gold: '#F0C24B',
};

// Captions get two more than a tint does. A wash over the whole picture never
// wants plain white or plain black, but a word on top of one often does.
export const CAPTION_COLOURS = {
  pink: TINT_COLOURS.pink,
  lavender: TINT_COLOURS.lavender,
  gold: TINT_COLOURS.gold,
  white: '#FFFFFF',
  black: '#101018',
};

/** The panel contract: modes are chips, knobs are 0..100 sliders, max two. */
export const EFFECTS = {
  drain: {
    modes: ['soft', 'drip', 'spread'],
    knobs: [{ key: 'strength', label: 'Strength', def: 60 }],
    params: {},
  },
  tint: {
    modes: ['wash', 'creep'],
    knobs: [{ key: 'intensity', label: 'Intensity', def: 65 }],
    params: { colour: 'pink', custom: null, corner: 'tl' },
  },
  spiral: {
    modes: ['over', 'through'],
    knobs: [
      { key: 'strength', label: 'Strength', def: 55 },
      { key: 'speed', label: 'Speed', def: 50 },
    ],
    params: { style: 0, colour: 'pink', dir: 1, mediaId: null },
  },
  glitch: {
    // ghost lays another loop over the whole target; double blows up the one
    // underneath. `tear`, the old sideways band cut, is off the panel and out
    // of the dice as of 2026-09-07 (owner call), but a saved remix that has
    // one still loads and still renders: that is what legacyModes is for.
    modes: ['ghost', 'double'],
    legacyModes: ['tear'],
    knobs: [{ key: 'strength', label: 'Strength', def: 50 }],
    // `source` is which gif the ghost borrows: 'dice' leaves it to the seed
    // (and `pick`), a media id pins it there and a reroll no longer moves it
    params: { pick: null, source: 'dice' },
  },
  caption: {
    modes: ['text', 'window', 'flash'],
    knobs: [
      { key: 'glow', label: 'Glow', def: 45 },
      { key: 'size', label: 'Size', def: 55 },
    ],
    // a new caption starts centred across and low down, at 78 percent of the
    // height: under the picture's middle, clear of the stamp in the corner. A
    // drag on the canvas moves it anywhere after that.
    params: { text: 'drop', pos: { x: 0.5, y: 0.78 }, font: 'display', colour: 'pink' },
  },
  focus: {
    modes: ['ring', 'dark'],
    knobs: [{ key: 'radius', label: 'Radius', def: 40 }],
    params: { path: [{ x: 0.3, y: 0.5 }, { x: 0.7, y: 0.5 }] },
  },
};

export const EFFECT_NAMES = Object.keys(EFFECTS);

/** Every mode the engine will still render, offered on the panel or not. */
export function modesFor(effect) {
  const spec = EFFECTS[effect];
  if (!spec) return [];
  return spec.legacyModes ? spec.modes.concat(spec.legacyModes) : spec.modes;
}

/** Fresh params for an effect: knob defaults plus the non-knob extras. */
export function defaultParams(effect) {
  const spec = EFFECTS[effect];
  if (!spec) throw new Error('Unknown effect');
  const p = {};
  for (const k of spec.knobs) p[k.key] = k.def;
  for (const key of Object.keys(spec.params)) {
    const v = spec.params[key];
    p[key] = v && typeof v === 'object' ? JSON.parse(JSON.stringify(v)) : v;
  }
  return p;
}

let idCounter = 0;
export function nextBlockId() {
  idCounter += 1;
  return 'b' + idCounter.toString(36) + '-' + Math.floor(Math.random() * 1e6).toString(36);
}

/** Build a valid block, filling in anything missing. Throws on bad input. */
export function createBlock(input, frames = 75) {
  const effect = input && input.effect;
  const spec = EFFECTS[effect];
  if (!spec) throw new Error('Unknown effect');
  const mode = modesFor(effect).includes(input.mode) ? input.mode : spec.modes[0];
  const b = {
    id: input.id || nextBlockId(),
    effect,
    mode,
    target: input.target || 'canvas',
    start: Math.round(input.start || 0),
    end: Math.round(input.end != null ? input.end : frames),
    params: Object.assign(defaultParams(effect), input.params || {}),
  };
  return clampBlock(b, frames);
}

/** Keep a block inside the canvas and at least one frame long. */
export function clampBlock(block, frames) {
  const f = Math.max(1, frames | 0);
  const b = Object.assign({}, block);
  b.start = Math.max(0, Math.min(f - 1, Math.round(b.start)));
  b.end = Math.max(b.start + 1, Math.min(f, Math.round(b.end)));
  return b;
}

/** Human readable problem, or null when the block is fine. */
export function validateBlock(block, ctx = {}) {
  if (!block || !EFFECTS[block.effect]) return 'Unknown effect';
  if (!modesFor(block.effect).includes(block.mode)) return 'Unknown mode for this effect';
  if (!Number.isFinite(block.start) || !Number.isFinite(block.end)) return 'Block needs a start and an end';
  if (block.end - block.start < 1) return 'A block has to be at least one frame';
  if (ctx.frames && (block.start < 0 || block.end > ctx.frames)) return 'Block runs past the loop';
  if (block.target !== 'canvas' && ctx.tileIds && !ctx.tileIds.includes(block.target)) {
    return 'Block points at a gif that is not on the canvas';
  }
  return null;
}

/** Length change: keep blocks in proportion, then clamp. */
export function scaleBlocks(blocks, oldFrames, newFrames) {
  const r = newFrames / Math.max(1, oldFrames);
  return blocks.map((b) => clampBlock(Object.assign({}, b, {
    start: Math.round(b.start * r),
    end: Math.max(Math.round(b.start * r) + 1, Math.round(b.end * r)),
  }), newFrames));
}

export function blockLength(block) {
  return Math.max(1, block.end - block.start);
}

/** How many frames the ramp takes on each side of this block. */
export function rampFrames(block) {
  const len = blockLength(block);
  if (len <= 1) return 0;
  return Math.max(1, Math.min(RAMP_FRAMES, Math.floor(len / 2)));
}

/** Raw progress through the block, 0..1 across its frames. */
export function progressAt(block, frame) {
  const len = blockLength(block);
  if (len <= 1) return 1;
  return Math.max(0, Math.min(1, (frame - block.start) / (len - 1)));
}

/** Eased strength multiplier: in over the first frames, out over the last. */
export function rampAt(block, frame) {
  const len = blockLength(block);
  const r = rampFrames(block);
  if (r === 0) return 1;
  const k = frame - block.start;
  if (k < 0 || k >= len) return 0;
  const inU = Math.min(1, (k + 1) / (r + 1));
  const outU = Math.min(1, (len - k) / (r + 1));
  return smooth(Math.min(inU, outU));
}

function smooth(u) {
  const c = Math.max(0, Math.min(1, u));
  return c * c * (3 - 2 * c);
}

export function isActive(block, frame) {
  return frame >= block.start && frame < block.end;
}

/** Active blocks at a frame, canvas last, drawn in timeline order. */
export function blocksAt(blocks, frame, target) {
  return blocks
    .filter((b) => isActive(b, frame) && (target === undefined || b.target === target))
    .sort((a, b) => a.start - b.start || (a.id < b.id ? -1 : 1));
}

/**
 * The block a panel creates when you open an effect with nothing on the
 * current target: canvas gets the full length, a tile gets its enter frame to
 * the end, a caption flash is a single frame at 62 percent of the loop.
 */
export function defaultBlockFor(effect, opts = {}) {
  const frames = Math.max(1, opts.frames || 75);
  const target = opts.target || 'canvas';
  const mode = opts.mode || EFFECTS[effect].modes[0];
  let start = target === 'canvas' ? 0 : Math.max(0, Math.min(frames - 1, opts.enterFrame || 0));
  let end = frames;
  if (effect === 'caption' && mode === 'flash') {
    start = Math.max(0, Math.min(frames - 1, Math.round(frames * 0.62)));
    end = start + 1;
  }
  return createBlock({ effect, mode, target, start, end, params: opts.params }, frames);
}
