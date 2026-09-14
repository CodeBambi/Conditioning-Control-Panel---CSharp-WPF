/* ============================================================================
 * ops.js - the state edits.
 *
 * Everything a project does that is nothing but a change to its own state:
 * tiles, the dock tree, layout, duration, loop, stamp, caption, blocks, and
 * the vibes and dice that patch several of those at once. Split out of
 * project.js, which keeps the state itself, the events, media, playback,
 * rendering, history and export.
 *
 * createOps(ctx) closes over one project's context and hands back the edits
 * as plain functions. ctx is that project's insides:
 *
 *   state       the mutable state object, edited in place
 *   changed()   emit 'change'
 *   commit(l)   push an undo checkpoint (only the dock edits self-commit)
 *   mediaById   lookup into state.media
 *   getFrame()  the play head
 *   setFrame(i) move the play head, without emitting
 *
 * Nothing here emits anything but 'change', and nothing here touches the
 * renderer, the media decoder or the undo stacks.
 * ==========================================================================*/

import { PLAY_MODES, framesForSeconds } from './clock.js';
import { sizeFor } from './decode.js';
import { seedToCode, codeToSeed, randomSeed } from './code.js';
import {
  stagesFor, MAX_TILES, LAYOUTS, defaultTree, ensureTree, treeRects,
  swapInTree, dockInTree, pixelRect, DOCK_SIDES, GAP_PX, rectsAtFrame,
} from './layout.js';
import {
  createBlock, clampBlock, scaleBlocks, validateBlock, defaultBlockFor,
} from './blocks.js';
import { vibePatch, surprisePatch, rerollBlock } from './vibes.js';
import { autoCompose } from './auto.js';
import { nextCorner } from './stamp.js';

/**
 * The canvas shape the first gif asks for: wide goes landscape, tall goes
 * portrait, anything between the two is square. Pure, so the Auto page can
 * pick a shape off a decoded size without asking the project anything. Bad
 * numbers fall back to landscape.
 */
export function pickOrientation(w, h) {
  const a = Number(w) / Number(h);
  if (!Number.isFinite(a) || a <= 0) return 'landscape';
  if (a > 1.2) return 'landscape';
  if (a < 0.83) return 'portrait';
  return 'square';
}

export const LOOPS = ['clean', 'snap', 'seamless'];
export const LAYOUT_MODES = LAYOUTS.map((l) => l.id);
export const ROLL_CAP = 20;

let tileSeq = 0;
export function nextTileId() {
  tileSeq += 1;
  return 't' + tileSeq.toString(36) + '-' + Math.floor(Math.random() * 1e6).toString(36);
}

export function createOps(ctx) {
  const { state, changed, commit, mediaById, getFrame, setFrame } = ctx;
  // the pool lane: puts exactly these media ids on the canvas, in this order (project.js)
  const setMediaSet = ctx.setMediaSet || null;

  /** The tree always holds exactly the tiles that exist, and no others. */
  function syncTree() {
    const ids = state.tiles.map((t) => t.id);
    if (!ids.length) { state.layout.tree = null; return; }
    state.layout.tree = state.layout.docked
      ? ensureTree(state.layout.tree, ids, state.orientation)
      : defaultTree(ids, state.orientation);
  }

  /** enterFrame comes from the grow schedule, never from the UI. */
  function deriveEnters() {
    if (!state.tiles.length) return;
    const s = stagesFor(state.tiles.length, state.layout.stageMs, state.fps, state.frames);
    const grow = state.layout.mode === 'grow';
    state.tiles.forEach((t, i) => { t.enterFrame = grow ? s.enterFrames[i] : 0; });
  }

  /* ------------------------------------------------------------- tiles --*/

  function addTile(mediaId) {
    if (state.tiles.length >= MAX_TILES) throw new Error('Eight is the most that fit');
    if (!mediaById(mediaId)) throw new Error('That clip is not in the list');
    const tile = { id: nextTileId(), mediaId, enterFrame: 0, playMode: 'forward' };
    state.tiles.push(tile);
    syncTree();
    deriveEnters();
    changed();
    return tile;
  }

  function removeTile(tileId) {
    const before = state.tiles.length;
    state.tiles = state.tiles.filter((t) => t.id !== tileId);
    if (state.tiles.length === before) return;
    state.blocks = state.blocks.filter((b) => b.target !== tileId);
    syncTree();
    deriveEnters();
    changed();
  }

  function moveTile(tileId, toIndex) {
    const from = state.tiles.findIndex((t) => t.id === tileId);
    if (from < 0) return;
    const to = Math.max(0, Math.min(state.tiles.length - 1, toIndex | 0));
    const [t] = state.tiles.splice(from, 1);
    state.tiles.splice(to, 0, t);
    deriveEnters();
    changed();
  }

  function setPlayMode(tileId, mode) {
    const t = state.tiles.find((x) => x.id === tileId);
    if (!t || !PLAY_MODES.includes(mode)) return;
    t.playMode = mode;
    changed();
  }

  /* -------------------------------------------------------------- dock --*/

  function leafIds() { return state.tiles.map((t) => t.id); }

  function hasTile(id) { return state.tiles.some((t) => t.id === id); }

  /** The two tiles trade places in the mosaic. */
  function swapTiles(aId, bId) {
    if (aId === bId || !hasTile(aId) || !hasTile(bId)) return state.layout.tree;
    syncTree();
    state.layout.tree = swapInTree(state.layout.tree, aId, bId);
    state.layout.docked = true;
    changed();
    commit('swap');
    return state.layout.tree;
  }

  /** Drop `movingId` onto a side of `targetId`, splitting the target's slot. */
  function dockTile(movingId, targetId, side) {
    if (!DOCK_SIDES.includes(side)) throw new Error('Pick a side to dock on');
    if (movingId === targetId || !hasTile(movingId) || !hasTile(targetId)) return state.layout.tree;
    syncTree();
    state.layout.tree = dockInTree(state.layout.tree, movingId, targetId, side);
    state.layout.docked = true;
    changed();
    commit('dock');
    return state.layout.tree;
  }

  /** What that dock would look like. Reads only; nothing moves. */
  function previewDock(movingId, targetId, side) {
    const ids = leafIds();
    const base = state.layout.docked
      ? ensureTree(state.layout.tree, ids, state.orientation)
      : defaultTree(ids, state.orientation);
    const next = movingId === targetId ? base : dockInTree(base, movingId, targetId, side);
    const size = sizeFor(state.orientation);
    return treeRects(next).map((r) => ({
      id: r.id, rect: r.rect, px: pixelRect(r.rect, size, GAP_PX),
    }));
  }

  /** Fraction rects by tile index for one frame, null where a tile is off. */
  function rectsAt(i) {
    return rectsAtFrame({
      tileIds: leafIds(),
      orientation: state.orientation,
      mode: state.layout.mode,
      stageMs: state.layout.stageMs,
      deckHold: state.layout.deckHold,
      tree: state.layout.docked ? state.layout.tree : null,
      flip: !!state.layout.flip,
      fps: state.fps,
      frames: state.frames,
      seed: state.seed,
    }, i == null ? getFrame() : i);
  }

  /* ------------------------------------------------------------ layout --*/

  function setLayout(patch = {}) {
    if (patch.mode) state.layout.mode = patch.mode;
    if (patch.stageMs) state.layout.stageMs = Math.max(100, patch.stageMs | 0);
    if (patch.deckHold) state.layout.deckHold = Math.max(1, patch.deckHold | 0);
    // flip: the same motion the other way round, every mode, images untouched
    if (patch.flip !== undefined) state.layout.flip = !!patch.flip;
    if (patch.tree !== undefined) {
      state.layout.tree = ensureTree(patch.tree, leafIds(), state.orientation);
      state.layout.docked = true;
    }
    syncTree();
    deriveEnters();
    changed();
  }

  /** The export sheet's Discord-safe toggle. The roll reads it: a dear
   * layout is a lot of rects a frame, and rects a frame cost bytes. */
  function setDiscordSafe(on) {
    const next = !!on;
    if (next === state.discordSafe) return;
    state.discordSafe = next;
    changed();
  }

  function setDuration(seconds) {
    const frames = framesForSeconds(seconds);
    if (frames === state.frames) return;
    state.blocks = scaleBlocks(state.blocks, state.frames, frames);
    state.frames = frames;
    deriveEnters();
    if (getFrame() >= frames) setFrame(frames - 1);
    changed();
  }

  function setLoop(kind) {
    if (!LOOPS.includes(kind)) return;
    state.loop = kind;
    changed();
  }

  function setStampCorner(corner) {
    state.stampCorner = corner || nextCorner(state.stampCorner);
    changed();
  }

  function setCaptionText(text) {
    state.captionText = String(text || '');
    changed();
  }

  /* ------------------------------------------------------------ blocks --*/

  function addBlock(input) {
    const b = createBlock(captionOnCanvas(input), state.frames);
    const bad = validateBlock(b, { frames: state.frames, tileIds: state.tiles.map((t) => t.id) });
    if (bad) throw new Error(bad);
    state.blocks.push(b);
    changed();
    return b;
  }

  function updateBlock(id, patch) {
    const i = state.blocks.findIndex((b) => b.id === id);
    if (i < 0) return null;
    const merged = Object.assign({}, state.blocks[i], patch);
    if (patch && patch.params) merged.params = Object.assign({}, state.blocks[i].params, patch.params);
    const next = clampBlock(merged, state.frames);
    const bad = validateBlock(next, { frames: state.frames, tileIds: state.tiles.map((t) => t.id) });
    if (bad) throw new Error(bad);
    state.blocks[i] = next;
    changed();
    return next;
  }

  function removeBlock(id) {
    const before = state.blocks.length;
    state.blocks = state.blocks.filter((b) => b.id !== id);
    if (state.blocks.length !== before) changed();
  }

  /**
   * A caption always paints the whole canvas. Owner call: a word inside one
   * tile of a mosaic is too small to read, so the target is ignored.
   */
  function captionOnCanvas(input) {
    if (!input || input.effect !== 'caption' || input.target === 'canvas') return input;
    return Object.assign({}, input, { target: 'canvas' });
  }

  /** The block a panel opens onto, created if the target has none yet. */
  function blockFor(effect, target) {
    if (effect === 'caption') target = 'canvas';
    const found = state.blocks.find((b) => b.effect === effect && b.target === target);
    if (found) return found;
    const tile = state.tiles.find((t) => t.id === target);
    const b = defaultBlockFor(effect, {
      frames: state.frames,
      target,
      enterFrame: tile ? tile.enterFrame : 0,
    });
    state.blocks.push(b);
    changed();
    return b;
  }

  function setCaptionPos(blockId, pos) {
    return updateBlock(blockId, { params: { pos: { x: clamp01(pos.x), y: clamp01(pos.y) } } });
  }

  function setFocusPath(blockId, path) {
    const b = state.blocks.find((x) => x.id === blockId);
    if (!b) return null;
    const pts = (path || []).map((p) => ({ x: clamp01(p.x), y: clamp01(p.y) }));
    return updateBlock(blockId, { params: { path: pts.length ? pts : [{ x: 0.5, y: 0.5 }] } });
  }

  function clamp01(v) { return Math.max(0, Math.min(1, Number(v) || 0)); }

  /* ------------------------------------------------------- vibes, dice --*/

  function applyPatch(patch) {
    if (patch.frames && patch.frames !== state.frames) {
      state.frames = patch.frames;
      if (getFrame() >= state.frames) setFrame(state.frames - 1);
    }
    if (patch.tiles) state.tiles = patch.tiles.slice();
    if (patch.layout) state.layout = Object.assign({}, state.layout, patch.layout);
    if (patch.loop) state.loop = patch.loop;
    if (patch.blocks) state.blocks = patch.blocks.map((b) => clampBlock(b, state.frames));
    deriveEnters();
    changed();
  }

  function applyVibe(name) {
    applyPatch(vibePatch(name, snapshotState(), state.seed));
    return state.blocks;
  }

  function surprise() {
    state.seed = randomSeed();
    applyPatch(surprisePatch(snapshotState(), state.seed));
    return seedToCode(state.seed);
  }

  /* -------------------------------------------------------------- roll --*/

  // the roll history: { seed, patch } per roll, back and forward walk it,
  // a new roll after going back drops the forward branch, capped at ROLL_CAP
  const rolls = { entries: [], index: -1 };

  function composeState() {
    return Object.assign(snapshotState(), {
      orientation: state.orientation,
      discordSafe: !!state.discordSafe,
      media: state.media.map((m) => ({ id: m.id, w: m.w, h: m.h, kind: m.kind })),
    });
  }

  /** Lay a roll down. The gifs come back first (a roll remembers which ones it
   *  was rolled on), then the patch. Caption blocks stay; every other block is the patch's. */
  function applyRoll(entry) {
    state.seed = entry.seed;
    if (entry.set && setMediaSet) setMediaSet(entry.set);
    const patch = JSON.parse(JSON.stringify(entry.patch));
    const captions = state.blocks.filter((b) => b.effect === 'caption');
    applyPatch(Object.assign({}, patch, { blocks: captions.concat(patch.blocks || []) }));
  }

  /** A new roll. `seed` may be a number, a code, or nothing for a fresh code. One undo step. */
  function roll(seed, opts = {}) {
    const s = seed == null ? randomSeed() : (typeof seed === 'string' ? codeToSeed(seed) : (seed >>> 0));
    const entry = { seed: s, patch: autoCompose(composeState(), s), set: state.tiles.map((t) => t.mediaId) };
    // replace: the current entry takes the new roll instead of a new one landing after it
    // (the Add step rerolls the same code once the whole batch is in)
    rolls.entries.length = opts.replace && rolls.index >= 0 ? rolls.index : rolls.index + 1;
    rolls.entries.push(entry);
    if (rolls.entries.length > ROLL_CAP) rolls.entries.shift();
    rolls.index = rolls.entries.length - 1;
    applyRoll(entry);
    commit('roll');
    return seedToCode(state.seed);
  }

  function rollBack() {
    if (rolls.index <= 0) return null;
    rolls.index -= 1;
    applyRoll(rolls.entries[rolls.index]);
    commit('roll back');
    return seedToCode(state.seed);
  }

  function rollForward() {
    if (rolls.index >= rolls.entries.length - 1) return null;
    rolls.index += 1;
    applyRoll(rolls.entries[rolls.index]);
    commit('roll forward');
    return seedToCode(state.seed);
  }

  const canRollBack = () => rolls.index > 0;
  const canRollForward = () => rolls.index < rolls.entries.length - 1;
  /** A read-only view: the index and one { seed, code, set } per entry. */
  function rollHistory() {
    return { index: rolls.index, entries: rolls.entries.map(viewEntry) };
  }
  const viewEntry = (e) => ({ seed: e.seed, code: seedToCode(e.seed), set: (e.set || []).slice() });
  /** The entry `steps` away from the current one (-1 = back, +1 = forward), without moving. The UI
   *  reads its `set` to bring any gif the graveyard has let go back from the pool before it walks. */
  function peekRoll(steps) {
    const e = rolls.entries[rolls.index + (steps | 0)];
    return e ? viewEntry(e) : null;
  }

  function rerollBlockById(id) {
    const b = state.blocks.find((x) => x.id === id);
    if (!b) return null;
    return updateBlock(id, rerollBlock(b, state.seed ^ Math.floor(Math.random() * 0xffff), { frames: state.frames }));
  }

  function setCode(code) {
    state.seed = codeToSeed(code);
    changed();
    return seedToCode(state.seed);
  }

  function snapshotState() {
    return {
      frames: state.frames,
      fps: state.fps,
      tiles: state.tiles.map((t) => Object.assign({}, t)),
      layout: JSON.parse(JSON.stringify(state.layout)),
      loop: state.loop,
      captionText: state.captionText,
    };
  }

  /* ---------------------------------------------------------------- ops -*/

  return {
    syncTree,
    deriveEnters,

    addTile,
    removeTile,
    moveTile,
    setPlayMode,

    swapTiles,
    dockTile,
    previewDock,
    rectsAt,

    setLayout,
    setDiscordSafe,
    setDuration,
    setLoop,
    setStampCorner,
    setCaptionText,

    addBlock,
    updateBlock,
    removeBlock,
    blockFor,
    setCaptionPos,
    setFocusPath,

    applyVibe,
    surprise,
    rerollBlockById,
    setCode,

    roll,
    rollBack,
    rollForward,
    canRollBack,
    canRollForward,
    rollHistory,
    peekRoll,
  };
}
