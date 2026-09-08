/* ============================================================================
 * boot.js - entry point for Piece by Piece.
 *
 * Builds the 3D board, hands the effects layer its hooks, and runs the frame
 * loop. window.PBP = { bus, game, board } is set before the first frame so the
 * effects module can attach to a live board whatever order the imports settle.
 * ==========================================================================*/

import { createScene, FILES } from './board/scene.js';
import { createPieces } from './board/pieces.js';
import { createBus } from './game/events.js';

const dom = {
  canvas: document.getElementById('board-canvas'),
  stage: document.getElementById('stage'),
  fx: document.getElementById('fx'),
  loader: document.getElementById('loader'),
  nope: document.getElementById('nope'),
  nopeMsg: document.getElementById('nope-msg'),
};

const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR';

/** Placement field of a FEN to { e1: {type, side}, ... }. */
export function positionFromFen(placement) {
  const map = {};
  const rows = String(placement).split(' ')[0].split('/');
  for (let i = 0; i < rows.length && i < 8; i++) {
    const rank = 8 - i;
    let file = 0;
    for (const ch of rows[i]) {
      if (ch >= '1' && ch <= '8') { file += Number(ch); continue; }
      if (file > 7) break;
      map[FILES[file++] + rank] = { type: ch.toLowerCase(), side: ch === ch.toUpperCase() ? 'w' : 'b' };
    }
  }
  return map;
}

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

  const pieces = createPieces({ group: view.pieceGroup });
  pieces.setPosition(positionFromFen(START_FEN));
  view.setSide('w', true);
  // The board API the effects layer drives. Keep this surface stable.
  const board = {
    view,
    pieces,
    setWobble(v) { pieces.setWobble(v); },
    setCameraSway(v) { view.setCameraSway(v); },
    projectSquare(sq) { return view.projectSquare(sq); },
    setHighlights(list) { view.setHighlights(list); },
    setSide(side, instant) { view.setSide(side, instant); },
  };

  window.PBP = { bus, game: null, board };

  let last = performance.now();
  function frame(now) {
    const dt = Math.min(0.1, (now - last) / 1000);
    last = now;
    view.update(dt);
    pieces.update(dt);
    if (window.PBP.game && window.PBP.game.update) window.PBP.game.update(dt);
    view.render();
    requestAnimationFrame(frame);
  }
  requestAnimationFrame(frame);

  dom.loader.classList.add('gone');
  setTimeout(() => { dom.loader.hidden = true; }, 500);
  bus.emit('local', { sides: ['w', 'b'] });
  pieces.tryLoadGlb();          // optional art; missing files stay silent
  attachEffects(bus, board);    // optional layer; the board plays fine without it
}

async function attachEffects(bus, board) {
  try {
    const m = await import('./ramp/index.js');
    m.attachRamp({ bus, root: dom.fx, stage: dom.stage, board });
  } catch (e) { console.warn('ramp missing', e); }
}

main();
