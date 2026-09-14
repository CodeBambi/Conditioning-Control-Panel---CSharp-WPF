/* ============================================================================
 * project.js - the object the UI talks to.
 *
 * Holds the state, owns the renderer, runs the play loop, and is the only
 * place that mutates anything. Every change emits 'change'; the play loop
 * emits 'frame'; media coming and going emits 'media'; play/pause emits
 * 'play' with the new flag; the undo stacks moving emits 'history'.
 *
 * Undo is checkpoint based: the UI mutates, then calls commit(label), and
 * the state as it stood at the previous checkpoint goes on the stack. A
 * commit that changes nothing is dropped. undo()/redo() hand back the label
 * they restored, or null. Capped at 50, media referenced by id.
 *
 * The edits that are nothing but a change to the state - tiles, dock, layout,
 * blocks, caption, focus, vibes, dice - live in ops.js and are wired in here
 * through createOps(). They land on the same api object under the same names.
 * ==========================================================================*/

import { Renderer } from './render.js';
import { decodeMedia, redecode, disposeMedia, probeMedia, sizeFor, OUTPUT_SIZES } from './decode.js';
import { seedToCode, randomSeed } from './code.js';
import { FPS, DURATIONS, framesForSeconds, stripNeedsMore } from './clock.js';
import { MAX_TILES, clampCount } from './layout.js';
import { clampBlock, EFFECTS } from './blocks.js';
import { ensureFonts, fontsInUse } from './fonts.js';
import { VIBES } from './vibes.js';
import { createOps, nextTileId, LOOPS, LAYOUT_MODES, ROLL_CAP } from './ops.js';
import {
  encodeGif, exportGifFitted, estimateSize as estimateGifSize, exportVideo as recordVideo,
  DISCORD_CAP, DISCORD_SAFE, LADDER,
} from './export.js';

export const UNDO_CAP = 50;
export { LOOPS, LAYOUT_MODES, ROLL_CAP };

export function createProject(opts = {}) {
  const listeners = { frame: new Set(), change: new Set(), media: new Set(), play: new Set(), history: new Set() };
  const renderer = new Renderer();

  const state = {
    orientation: OUTPUT_SIZES[opts.orientation] ? opts.orientation : 'landscape',
    frames: framesForSeconds(opts.seconds || 5),
    fps: FPS,
    seed: opts.seed != null ? opts.seed : randomSeed(),
    media: [],
    tiles: [],
    layout: { mode: 'grow', stageMs: 600, deckHold: 15, tree: null, docked: false, flip: false },
    loop: 'snap',
    blocks: [],
    stampCorner: 'br',
    captionText: '',
    shrink: 0,
    // the export sheet's toggle, kept on the project so the roll can read it
    discordSafe: false,
  };

  const undoStack = [];
  const redoStack = [];
  let baseline = null; // the state at the last checkpoint, as JSON
  // removed clips wait here so an undo can bring them back; the oldest are let go
  const graveyard = new Map();
  const GRAVEYARD_CAP = 4;
  let playing = false;
  let frame = 0;
  let rafId = 0;
  let lastTs = 0;
  let acc = 0;

  /* ---------------------------------------------------------- plumbing --*/

  const emit = (name, arg) => {
    for (const fn of [...listeners[name]]) {
      try { fn(arg); } catch (err) { if (typeof console !== 'undefined') console.warn('listener failed', name, err); }
    }
  };
  const changed = () => { syncFonts(); emit('change'); };

  // A bundled caption face is a file, so it lands a moment after the edit that
  // asked for it. Kick the load whenever the set of faces on the timeline
  // changes and repaint once it is there; until then the stack in FONTS draws.
  let fontsAsked = '';
  function syncFonts() {
    const keys = fontsInUse(state.blocks);
    const sig = keys.join(',');
    if (sig === fontsAsked) return;
    fontsAsked = sig;
    if (!keys.length) return;
    ensureFonts(keys).then((ok) => { if (ok) emit('frame', frame); });
  }

  /** Every bundled face the timeline asks for, ready to draw with. */
  const fontsReady = () => ensureFonts(fontsInUse(state.blocks));

  const mediaById = (id) => state.media.find((m) => m.id === id) || null;

  const ops = createOps({
    state,
    changed,
    commit,
    mediaById,
    setMediaSet,
    getFrame: () => frame,
    setFrame: (i) => { frame = i; },
  });
  const {
    syncTree, deriveEnters,
    addTile, removeTile, moveTile, setPlayMode,
    swapTiles, dockTile, previewDock, rectsAt,
    setLayout, setDiscordSafe, setDuration: opsSetDuration, setLoop, setStampCorner, setCaptionText,
    addBlock, updateBlock, removeBlock, blockFor, setCaptionPos, setFocusPath,
    applyVibe, surprise, rerollBlockById, setCode,
    roll, rollBack, rollForward, canRollBack, canRollForward, rollHistory, peekRoll,
  } = ops;

  function renderState() {
    return {
      size: sizeFor(state.orientation),
      orientation: state.orientation,
      frames: state.frames,
      fps: state.fps,
      seed: state.seed,
      code: seedToCode(state.seed),
      tiles: state.tiles,
      media: mediaById,
      layout: state.layout,
      loop: state.loop,
      blocks: state.blocks,
      stampCorner: state.stampCorner,
      captionText: state.captionText,
      mediaColour: state.media.length ? state.media[0].colour : null,
    };
  }

  function drawFrame(ctx, i, size) {
    const st = renderState();
    if (size && (size.w !== st.size.w || size.h !== st.size.h)) st.size = size;
    renderer.render(ctx, i, st);
  }

  /* ------------------------------------------------------------- media --*/

  async function addMedia(file, mediaOpts = {}) {
    // decoded to the canvas length, no longer; a longer canvas re-decodes what it needs
    const m = await decodeMedia(file, Object.assign({
      size: sizeFor(state.orientation),
      frames: state.frames,
      fps: state.fps,
    }, mediaOpts));
    state.media.push(m);
    emit('media');
    changed();
    return m;
  }

  function removeMedia(id) {
    const i = state.media.findIndex((m) => m.id === id);
    if (i < 0) return;
    const [m] = state.media.splice(i, 1);
    const gone = state.tiles.filter((t) => t.mediaId === id).map((t) => t.id);
    state.tiles = state.tiles.filter((t) => t.mediaId !== id);
    state.blocks = state.blocks.filter((b) => b.target === 'canvas' || !gone.includes(b.target));
    bury(m);
    syncTree();
    deriveEnters();
    emit('media');
    changed();
  }

  /** A clip the project can put on the canvas without a decode: in the list, or waiting in the graveyard. */
  const hasMedia = (id) => !!mediaById(id) || graveyard.has(id);

  /**
   * The pool lane's one door: exactly these clips on the canvas, in this order. Clips already
   * in the list stay decoded; clips in the graveyard come back; ids the project does not hold
   * are skipped (the UI decodes those first, with addMedia(file, { id })). Every other clip
   * goes to the graveyard. Tiles follow: one per id, kept where the clip already had one so
   * its blocks survive, made fresh otherwise. No commit: the caller's roll or commit owns it.
   */
  function setMediaSet(ids) {
    const wanted = [];
    for (const id of ids || []) {
      if (wanted.includes(id)) continue;
      const m = mediaById(id) || graveyard.get(id) || null;
      if (m) wanted.push(id);
    }
    let moved = false;
    const list = [];
    for (const id of wanted) {
      let m = mediaById(id);
      if (!m) { m = graveyard.get(id); graveyard.delete(id); moved = true; }
      list.push(m);
    }
    for (const m of state.media) if (!wanted.includes(m.id)) { bury(m); moved = true; }
    state.media = list;
    const byMedia = new Map(state.tiles.map((t) => [t.mediaId, t]));
    const tiles = [];
    for (const id of wanted) {
      const t = byMedia.get(id);
      tiles.push(t ? t : { id: nextTileId(), mediaId: id, enterFrame: 0, playMode: 'forward' });
    }
    const keep = new Set(tiles.map((t) => t.id));
    state.blocks = state.blocks.filter((b) => b.target === 'canvas' || keep.has(b.target));
    state.tiles = tiles;
    syncTree();
    deriveEnters();
    if (moved) emit('media');
    changed();
    return state.tiles;
  }

  function bury(m) {
    graveyard.set(m.id, m);
    while (graveyard.size > GRAVEYARD_CAP) {
      const [oldId, old] = graveyard.entries().next().value;
      graveyard.delete(oldId);
      disposeMedia(old);
    }
  }

  /* ---------------------------------------------------------- re-decode --*/

  let redecodeSeq = 0;
  /**
   * Re-decode the clips `pick` says yes to, at the current output size and
   * canvas length. Blocks stay put. The old bitmaps are let go as each new
   * strip lands.
   */
  async function redecodeMedia(pick) {
    const seq = ++redecodeSeq;
    const size = sizeFor(state.orientation);
    const next = [];
    for (const m of state.media.slice()) {
      if (!pick(m)) { next.push(m); continue; }
      try {
        const re = await redecode(m, { size, frames: state.frames, fps: state.fps });
        if (seq !== redecodeSeq) { disposeMedia(re); return; } // a newer turn won
        disposeMedia(m);
        next.push(re);
      } catch {
        next.push(m); // keep the old strip rather than losing the clip
      }
    }
    if (seq !== redecodeSeq) return;
    // clips added while decoding ran keep their place; clips removed stay gone
    const still = new Set(state.media.map((m) => m.id));
    const known = new Set(next.map((m) => m.id));
    const out = next.filter((m) => still.has(m.id));
    for (const m of state.media) if (!known.has(m.id)) out.push(m);
    state.media = out;
    syncTree();
    emit('media');
    changed();
  }

  const redecodeAll = () => redecodeMedia(() => true);
  /** After the canvas grew: only the strips that hit the old cap and have more source to show. */
  const redecodeGrown = (was) => redecodeMedia((m) => stripNeedsMore(m.strip.length, m.srcDurMs, was, state.frames, state.fps));

  async function setOrientation(orientation) {
    if (!OUTPUT_SIZES[orientation] || orientation === state.orientation) return;
    state.orientation = orientation;
    syncTree();
    changed();
    await redecodeAll();
  }

  /** setDuration from ops, plus the re-decode a longer canvas needs (same pattern as setOrientation). */
  function setDuration(seconds) {
    const was = state.frames;
    opsSetDuration(seconds);
    if (state.frames > was) redecodeGrown(was);
  }

  /* ----------------------------------------------------------- playback -*/

  function renderFrame(i, ctx) {
    renderer.render(ctx, i, renderState());
  }

  function frameAt(i) {
    const size = sizeFor(state.orientation);
    const b = renderer.buf('frameAt', size.w, size.h);
    renderer.render(b.ctx, i, renderState());
    return b.ctx.getImageData(0, 0, size.w, size.h);
  }

  function seek(i) {
    const next = Math.max(0, Math.min(state.frames - 1, Math.round(i)));
    if (next === frame) return;
    frame = next;
    emit('frame', frame);
  }

  function step() {
    frame = (frame + 1) % state.frames;
    emit('frame', frame);
  }

  function tick(ts) {
    if (!playing) return;
    if (!lastTs) lastTs = ts;
    acc += ts - lastTs;
    lastTs = ts;
    const spf = 1000 / state.fps;
    let guard = 0;
    while (acc >= spf && guard < 4) { acc -= spf; step(); guard++; }
    if (guard >= 4) acc = 0;
    rafId = requestAnimationFrame(tick);
  }

  function play() {
    if (playing) return;
    playing = true;
    lastTs = 0;
    acc = 0;
    rafId = requestAnimationFrame(tick);
    emit('play', true);
  }

  function pause() {
    if (!playing) return;
    playing = false;
    if (rafId) cancelAnimationFrame(rafId);
    rafId = 0;
    emit('play', false);
  }

  /* --------------------------------------------------------------- undo -*/

  function serialise() {
    return {
      v: 1,
      orientation: state.orientation,
      frames: state.frames,
      fps: state.fps,
      seed: state.seed,
      code: seedToCode(state.seed),
      loop: state.loop,
      layout: JSON.parse(JSON.stringify(state.layout)),
      stampCorner: state.stampCorner,
      captionText: state.captionText,
      shrink: state.shrink,
      discordSafe: !!state.discordSafe,
      tiles: state.tiles.map((t) => Object.assign({}, t)),
      blocks: state.blocks.map((b) => JSON.parse(JSON.stringify(b))),
      mediaIds: state.media.map((m) => m.id),
    };
  }

  function restore(json) {
    const turned = !!(json.orientation && OUTPUT_SIZES[json.orientation] && json.orientation !== state.orientation);
    const was = state.frames;
    state.orientation = json.orientation || state.orientation;
    state.frames = json.frames || state.frames;
    state.seed = json.seed != null ? json.seed : state.seed;
    state.loop = json.loop || state.loop;
    state.layout = Object.assign(
      { mode: 'grow', stageMs: 600, deckHold: 15, tree: null, docked: false, flip: false },
      json.layout || {},
    );
    state.stampCorner = json.stampCorner || 'br';
    state.captionText = json.captionText || '';
    state.shrink = json.shrink | 0;
    state.discordSafe = !!json.discordSafe;
    // the media list follows the snapshot: removed clips come back from the
    // graveyard, clips the snapshot never had go to it
    if (Array.isArray(json.mediaIds)) {
      const wanted = json.mediaIds;
      const byId = new Map(state.media.map((m) => [m.id, m]));
      let moved = false;
      const list = [];
      for (const id of wanted) {
        const m = byId.get(id) || graveyard.get(id);
        if (!m) continue;
        if (!byId.has(id)) { graveyard.delete(id); moved = true; }
        list.push(m);
      }
      for (const m of state.media) if (!wanted.includes(m.id)) { bury(m); moved = true; }
      state.media = list;
      if (moved) emit('media');
    }
    const have = new Set(state.media.map((m) => m.id));
    state.tiles = (json.tiles || []).filter((t) => have.has(t.mediaId)).map((t) => Object.assign({}, t));
    const tileIds = state.tiles.map((t) => t.id);
    state.blocks = (json.blocks || [])
      .filter((b) => b.target === 'canvas' || tileIds.includes(b.target))
      .map((b) => clampBlock(b, state.frames));
    syncTree();
    if (frame >= state.frames) frame = state.frames - 1;
    changed();
    if (turned) redecodeAll();
    else if (state.frames > was) redecodeGrown(was);
  }

  const snapshotJson = () => JSON.stringify(serialise());

  /**
   * Checkpoint the state as it is now. The previous checkpoint goes on the
   * undo stack under `label`; a commit that changed nothing is dropped.
   */
  function commit(label) {
    const now = snapshotJson();
    if (baseline === null) baseline = now;
    if (now === baseline) return false;
    undoStack.push({ label: label || '', json: baseline });
    if (undoStack.length > UNDO_CAP) undoStack.shift();
    redoStack.length = 0;
    baseline = now;
    emit('history');
    return true;
  }

  function undo() {
    if (!undoStack.length) return null;
    const entry = undoStack.pop();
    redoStack.push({ label: entry.label, json: snapshotJson() });
    restore(JSON.parse(entry.json));
    baseline = snapshotJson();
    emit('history');
    return entry.label;
  }

  function redo() {
    if (!redoStack.length) return null;
    const entry = redoStack.pop();
    undoStack.push({ label: entry.label, json: snapshotJson() });
    restore(JSON.parse(entry.json));
    baseline = snapshotJson();
    emit('history');
    return entry.label;
  }

  /** Forget the past; the state as it is now becomes the first checkpoint. Buried clips can never come back, so their bitmaps go too. */
  function clearHistory() {
    undoStack.length = 0;
    redoStack.length = 0;
    for (const m of graveyard.values()) disposeMedia(m);
    graveyard.clear();
    baseline = snapshotJson();
    emit('history');
  }

  /* ------------------------------------------------------------ export --*/

  const rung = () => LADDER[Math.max(0, Math.min(LADDER.length - 1, state.shrink | 0))];

  const exportOpts = (extra) => Object.assign({
    drawFrame,
    frames: state.frames,
    masterFps: state.fps,
    fps: rung().fps,
    scale: rung().scale,
    size: sizeFor(state.orientation),
  }, extra);

  /** The rung exports start from: 0 full, 1 is 12 fps, 2 is 12 fps at two thirds. */
  function setShrink(level) {
    const next = Math.max(0, Math.min(LADDER.length - 1, level | 0));
    if (next === state.shrink) return;
    state.shrink = next;
    changed();
  }

  async function estimateSize() {
    await fontsReady();
    return estimateGifSize(exportOpts({}));
  }

  /**
   * exportGif({ onProgress, discordSafe, maxBytes, shrink, onRung, signal })
   * discordSafe caps at 8 MB, maxBytes at whatever you say, shrink at 10 MB.
   * An aborted signal rejects with an AbortError at the next frame.
   * With a cap the ladder runs from the current shrink rung until it fits,
   * and onRung(rung, bytes) is called for each rung that was still too big.
   */
  async function exportGif(o = {}) {
    // the same shape as waiting on the decode: nothing is drawn until what the
    // frame needs is in hand
    await fontsReady();
    const cap = o.discordSafe ? DISCORD_SAFE : (o.maxBytes || (o.shrink ? DISCORD_CAP : Infinity));
    if (cap === Infinity) return encodeGif(exportOpts({ onProgress: o.onProgress, signal: o.signal }));
    const out = await exportGifFitted(exportOpts({
      onProgress: o.onProgress, maxBytes: cap, onRung: o.onRung, startRung: state.shrink | 0, signal: o.signal,
    }));
    return out.blob;
  }

  async function exportVideo(o = {}) {
    await fontsReady();
    return recordVideo(exportOpts({ onProgress: o.onProgress, loops: o.loops || 3, signal: o.signal }));
  }

  function filename(ext) {
    return 'remix-' + seedToCode(state.seed) + '.' + ext;
  }

  /* ---------------------------------------------------------------- api -*/

  const api = {
    // media
    addMedia,
    removeMedia,
    get media() { return state.media; },
    mediaById,
    /** Adopt an already decoded media, used by fromJSON. */
    adoptMedia(m) { state.media.push(m); emit('media'); changed(); return m; },
    hasMedia,
    setMediaSet,
    /** A cheap look at a file for the pool: kind, size, one small frame. Nothing joins the list. */
    probeMedia,

    // tiles + layout
    addTile,
    removeTile,
    moveTile,
    setPlayMode,
    swapTiles,
    dockTile,
    previewDock,
    rectsAt,
    setLayout,
    setOrientation,
    setDuration,
    setLoop,
    setStampCorner,
    setCaptionText,

    // blocks
    addBlock,
    updateBlock,
    removeBlock,
    blockFor,
    setCaptionPos,
    setFocusPath,

    get blocks() { return state.blocks; },
    get tiles() { return state.tiles; },
    get layout() { return state.layout; },
    get loop() { return state.loop; },
    get size() { return sizeFor(state.orientation); },
    get orientation() { return state.orientation; },
    get frames() { return state.frames; },
    get fps() { return state.fps; },
    get seed() { return state.seed; },
    get code() { return seedToCode(state.seed); },
    get captionText() { return state.captionText; },
    get stampCorner() { return state.stampCorner; },
    setCode,

    // rendering
    renderFrame,
    frameAt,
    drawFrame,
    play,
    pause,
    seek,
    step,
    get playing() { return playing; },
    get frame() { return frame; },

    on(name, fn) {
      if (!listeners[name]) throw new Error('No such event');
      listeners[name].add(fn);
      return () => listeners[name].delete(fn);
    },

    // vibes + dice
    applyVibe,
    surprise,
    rerollBlock: rerollBlockById,
    get vibes() { return VIBES; },
    get effects() { return EFFECTS; },

    // the roll (auto compose) and its history
    roll,
    rollBack,
    peekRoll,
    rollForward,
    get canRollBack() { return canRollBack(); },
    get canRollForward() { return canRollForward(); },
    get rollHistory() { return rollHistory(); },

    // export
    estimateSize,
    fontsReady,
    exportGif,
    exportVideo,
    filename,
    setShrink,
    get shrink() { return state.shrink; },
    setDiscordSafe,
    get discordSafe() { return state.discordSafe; },

    // undo
    commit,
    undo,
    redo,
    clearHistory,
    get canUndo() { return undoStack.length > 0; },
    get canRedo() { return redoStack.length > 0; },

    toJSON: serialise,
    dispose() {
      pause();
      for (const m of state.media) disposeMedia(m);
      for (const m of graveyard.values()) disposeMedia(m);
      graveyard.clear();
      state.media = [];
      renderer.dispose();
    },
  };

  return api;
}

/** Rebuild a project from toJSON output plus the media it referenced. */
createProject.fromJSON = function fromJSON(json, mediaMap) {
  const p = createProject({ orientation: json.orientation, seed: json.seed });
  const list = mediaMap instanceof Map ? [...mediaMap.values()] : Object.values(mediaMap || {});
  for (const m of list) p.adoptMedia(m);
  p.setDuration((json.frames || 75) / (json.fps || FPS));
  p.setLayout(json.layout || {});
  p.setLoop(json.loop || 'snap');
  p.setStampCorner(json.stampCorner || 'br');
  p.setCaptionText(json.captionText || '');
  p.setDiscordSafe(!!json.discordSafe);

  // tiles keep their ids so the blocks that point at them still land
  const idMap = new Map();
  for (const t of json.tiles || []) {
    if (!p.mediaById(t.mediaId)) continue;
    const tile = p.addTile(t.mediaId);
    idMap.set(t.id, tile.id);
    p.setPlayMode(tile.id, t.playMode || 'forward');
  }
  for (const b of json.blocks || []) {
    const target = b.target === 'canvas' ? 'canvas' : idMap.get(b.target);
    if (!target) continue;
    try { p.addBlock(Object.assign({}, b, { id: undefined, target })); } catch { /* skip a block that no longer fits */ }
  }
  return p;
};

export { clampCount, MAX_TILES, DURATIONS, LOOPS as LOOP_MARKERS };
