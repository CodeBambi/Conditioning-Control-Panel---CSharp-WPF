/* ============================================================================
 * boot.js - entry point for Piece by Piece.
 *
 * Builds the 3D board, hands the effects layer its hooks, and runs the frame
 * loop. window.PBP = { bus, game, board, ramp } is set before the first frame
 * so the effects module can attach to a live board whatever order the imports
 * settle; `ramp` is the handle attachRamp hands back, once it has.
 * ==========================================================================*/

import { createScene } from './board/scene.js';
import { createPieces } from './board/pieces.js';
import { createAnim } from './board/anim.js';
import { createJiggle } from './board/jiggle.js';
import { createDrag } from './board/drag.js';
import { createGlyphs } from './board/glyphs.js';
import { createBus } from './game/events.js';
import { createHotseat } from './game/hotseat.js';
import { DEFAULT_MS } from './game/clock.js';
import { postToHost, onHostMessage, signalReady } from './bridge.js';

const dom = {
  canvas: document.getElementById('board-canvas'),
  hud: { w: document.getElementById('time-w'), b: document.getElementById('time-b'), status: document.getElementById('status') },
  stage: document.getElementById('stage'),
  fx: document.getElementById('fx'),
  loader: document.getElementById('loader'),
  nope: document.getElementById('nope'),
  nopeMsg: document.getElementById('nope-msg'),
};

function fail(message) {
  dom.loader.hidden = true;
  dom.nopeMsg.textContent = message;
  dom.nope.hidden = false;
}

function main() {
  const bus = createBus();
  let view;
  try {
    view = createScene({ canvas: dom.canvas });
  } catch (err) {
    console.error('[pbp] the 3D board could not start', err);
    fail('The 3D board could not start. Check that hardware acceleration is available.');
    return;
  }

  // Soft-body flex. One system for the whole board; every piece gets its own
  // spring out of it when pieces.js builds it.
  const jiggle = createJiggle();
  const anim = createAnim({ group: view.pieceGroup, jiggle });
  const pieces = createPieces({ group: view.pieceGroup, hooks: anim.hooks, jiggle });
  // The board API the effects layer drives. Keep this surface stable.
  const board = {
    view,
    pieces,
    anim,
    jiggle: pieces.jiggle,
    buzzCheck(square) { anim.buzz(pieces.pieceAt(square)); },
    setWobble(v) { pieces.setWobble(v); },
    setCameraSway(v) { view.setCameraSway(v); },
    projectSquare(sq, height) { return view.projectSquare(sq, height); },
    setHighlights(list) { view.setHighlights(list); },
    setSide(side, instant) { view.setSide(side, instant); },
  };

  const params = new URLSearchParams(location.search);
  const game = createHotseat({
    bus,
    board,
    hud: dom.hud,
    clockMs: Number(params.get('clock')) > 0 ? Number(params.get('clock')) * 1000 : DEFAULT_MS,
    fen: params.get('fen') || undefined,
    auto: Number(params.get('auto')) || 0,
  });

  const drag = createDrag({ view, pieces, anim, bus, game, jiggle });
  board.drag = drag;

  // Host settings, with the standalone defaults already in place: the effects
  // layer reads window.PBP.settings and must never have to wait for a frame
  // that only arrives inside the desktop app.
  // `ramp` is filled in once the effects layer has attached. It is on the
  // object from the start so a reader never has to care whether that has
  // happened yet: it is simply null until it has.
  window.PBP = { bus, game, board, ramp: null, settings: { videoHoldSec: 15, reducedMotion: false } };
  onHostMessage((m) => {
    if (m.type !== 'pbp:settings') return;
    const { type, ...values } = m;   // the envelope's own key is not a setting
    Object.assign(window.PBP.settings, values);
  });
  // Esc closes the board - but never mid-drag, where it is "put the piece back".
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !drag.isDragging()) postToHost({ type: 'pbp:exit' });
  });
  signalReady();

  // --- camera: the rig the player drives ---
  // A touch that picked a man up is never an orbit, so the rig asks drag.js.
  window.PBP.camera = view.cameraRig;
  view.cameraRig.setDragGuard(() => drag.isDragging());
  view.cameraRig.attachUi();
  // From overhead the toys are unreadable, so the classic glyphs fade in over
  // them. It ticks itself off the renderer, so the frame loop stays as it was.
  window.PBP.glyphs = createGlyphs({ view, pieces });
  // --- end camera ---
  // --- J: the feel (outline, dust, sound) ---
  // Settings are merged, never replaced: the host bridge may have filled some.
  window.PBP.settings = Object.assign({ outline: true, sfxVolume: 0.6 }, window.PBP.settings || {});
  // Loaded late and guarded, so a missing module never holds the game. The
  // per-frame updates ride on view.render, which the loop calls last, after
  // jiggle.update has written the flex the outline reads.
  const feelLate = [];
  let feelLast = performance.now();
  const renderBase = view.render;
  view.render = () => {
    const now = performance.now();
    const dt = Math.min(0.1, (now - feelLast) / 1000);
    feelLast = now;
    for (const fn of feelLate) fn(dt);
    renderBase();
  };
  import('./board/outline.js').then((m) => {
    board.outline = m.createOutline({ group: view.pieceGroup, bus });
    feelLate.push((dt) => board.outline.update(dt, view.camera, view.renderer));
  }).catch((e) => console.warn('[pbp] outline missing', e));
  // --- end J ---

  let last = performance.now();
  function frame(now) {
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    view.update(dt);
    pieces.update(dt);
    anim.update(dt);
    drag.update(dt);
    jiggle.update(dt);   // last: it reads what everything else just decided
    if (window.PBP.game && window.PBP.game.update) window.PBP.game.update(dt);
    view.render();
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);

  dom.loader.classList.add('gone');
  setTimeout(() => { dom.loader.hidden = true; }, 500);
  pieces.tryLoadGlb();          // optional art; missing files stay silent
  // Give the optional effects layer a chance to subscribe before the first
  // turn is dealt; it is optional, so a missing module must not hold the game.
  attachEffects(bus, board, params).then(() => {
    bus.emit('local', { sides: ['w', 'b'] });
    game.start();
  });
}

/**
 * Attach the effects layer, and give it somewhere to get its pictures from.
 *
 *   default        the host. In WebView2 the C# side answers with the player's
 *                  own library; in a plain browser there is no bridge and the
 *                  pool stays empty, which the ramp treats as a normal state.
 *   ?media=fixture dev/media.json, so the ramp can be seen working in a plain
 *                  browser with no host at all (the screenshot harness uses it).
 *   ?media=none    an empty pool on purpose, to check the no-pictures path.
 */
async function attachEffects(bus, board, params) {
  try {
    const m = await import('./ramp/index.js');
    const media = await pickMedia(params);
    window.PBP.ramp = m.attachRamp({ bus, root: dom.fx, stage: dom.stage, board, media });
  } catch (e) { console.warn('ramp missing', e); }
}

async function pickMedia(params) {
  const mode = (params.get('media') || '').toLowerCase();
  try {
    const media = await import('./ramp/media.js');
    if (mode === 'fixture') {
      const list = await media.loadFixtureList(new URL('./dev/media.json', import.meta.url).href);
      return media.createFixtureMedia(list);
    }
    if (mode === 'none') return media.createFixtureMedia([]);
    return media.createHostMedia();
  } catch (e) {
    console.warn('[pbp] no media source; the ramp runs without pictures', e);
    return undefined;   // attachRamp falls back to an empty pool of its own
  }
}

main();
