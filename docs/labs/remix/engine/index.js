/* ============================================================================
 * index.js - the engine's front door.
 *
 *   import { createProject } from './engine/index.js';
 *   const p = createProject({ orientation: 'landscape' });
 *
 * Everything the UI needs comes through here. Nothing in the engine fetches,
 * stores or reports anything: files go in, a blob comes out, and that is the
 * whole of its contact with the outside world.
 * ==========================================================================*/

export { createProject, UNDO_CAP, LOOPS, LAYOUT_MODES, ROLL_CAP } from './project.js';

export { FPS, DURATIONS, PLAY_MODES, framesForSeconds, secondsForFrames } from './clock.js';
export { EFFECTS, EFFECT_NAMES, TINT_COLOURS, CAPTION_COLOURS, defaultParams, defaultBlockFor } from './blocks.js';
export { VIBES } from './vibes.js';
export { autoCompose, AUTO_WEIGHTS, passesGate, coverageOf, longestClean } from './auto.js';
export {
  MAX_TILES, GAP_PX, DOCK_SIDES, LAYOUTS, rectFor, rectsFor, tableRects, treeRects,
  defaultTree, ensureTree, pruneTree, leavesOf, isLeaf, swapInTree, dockInTree,
  adjacency, adjacencyFromRects, pixelRect, layoutAtFrame, rectsAtFrame,
} from './layout.js';
export { ALPHABET, seedToCode, codeToSeed, isCode, normalizeCode, randomCode } from './code.js';
export { OUTPUT_SIZES, PROBE_SIZE, sizeFor, kindForFile, probeMedia, UNSUPPORTED, stopDecoder } from './decode.js';
export { POOL_CAP, pickSet, defaultCount, clampCountFor, sameSet } from './pool.js';
export { ZIP_CAP, isZipFile, unzip, keepsMedia } from './zip.js';
export { pickOrientation } from './ops.js';
export { CORNERS, nextCorner, stampRect } from './stamp.js';
export { DISCORD_CAP, DISCORD_SAFE, LADDER, videoMimeType, canRecordVideo, abortError, stopEncoder } from './export.js';
export { Renderer, GROUND } from './render.js';
export { BUNDLED_FONTS, BUNDLED_KEYS, ensureFonts, fontsInUse, fontUrl } from './fonts.js';
