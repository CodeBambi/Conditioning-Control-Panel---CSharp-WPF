/* ============================================================================
 * boot.js - entry point for Piece by Piece.
 *
 * Builds the 3D board, hands the effects layer its hooks, and runs the frame
 * loop. window.PBP = { bus, game, board } is set before the first frame so the
 * effects module can attach to a live board whatever order the imports settle.
 * ==========================================================================*/

import { createScene } from './board/scene.js';
import { createPieces } from './board/pieces.js';
import { createAnim } from './board/anim.js';
import { createDrag } from './board/drag.js';
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

  const anim = createAnim({ group: view.pieceGroup });
  const pieces = createPieces({ group: view.pieceGroup, hooks: anim.hooks });
  // The board API the effects layer drives. Keep this surface stable.
  const board = {
    view,
    pieces,
    anim,
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

  const drag = createDrag({ view, pieces, anim, bus, game });
  board.drag = drag;

  // Host settings, with the standalone defaults already in place: the effects
  // layer reads window.PBP.settings and must never have to wait for a frame
  // that only arrives inside the desktop app.
  window.PBP = { bus, game, board, settings: { videoHoldSec: 15, reducedMotion: false } };
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

  let last = performance.now();
  function frame(now) {
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    view.update(dt);
    pieces.update(dt);
    anim.update(dt);
    drag.update(dt);
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
  attachEffects(bus, board).then(() => {
    bus.emit('local', { sides: ['w', 'b'] });
    game.start();
  });
}

async function attachEffects(bus, board) {
  try {
    const m = await import('./ramp/index.js');
    m.attachRamp({ bus, root: dom.fx, stage: dom.stage, board });
  } catch (e) { console.warn('ramp missing', e); }
}

main();
