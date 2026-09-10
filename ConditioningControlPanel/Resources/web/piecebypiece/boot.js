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
import { createDriverSwitch, startOnlineMatch } from './net/online.js';
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
  // THE DRIVER IS BEHIND A SWITCH, always - even in a hotseat-only session.
  // drag.js, promote.js, hud.js and sfx.js are each handed `game` once and hold
  // that reference for the life of the page, so the thing they are handed has
  // to be the one object that outlives any particular game. See net/online.js.
  const hotseat = createHotseat({
    bus,
    board,
    hud: dom.hud,
    clockMs: Number(params.get('clock')) > 0 ? Number(params.get('clock')) * 1000 : DEFAULT_MS,
    fen: params.get('fen') || undefined,
    auto: Number(params.get('auto')) || 0,
  });
  const game = createDriverSwitch(hotseat);

  const drag = createDrag({ view, pieces, anim, bus, game, jiggle });
  board.drag = drag;

  // `ramp` is filled in once the effects layer has attached. It is on the
  // object from the start so a reader never has to care whether that has
  // happened yet: it is simply null until it has.
  window.PBP = { bus, game, board, ramp: null, settings: { videoHoldSec: 15, reducedMotion: false } };
  /**
   * Deal an online game onto this board. The front door calls it with the Match
   * its lobby handed back; everything after that - the seat, the clocks, the
   * long poll - belongs to net/match.js, and nothing else on the page has to
   * know the game changed hands.
   */
  window.PBP.startOnline = (match) => startOnlineMatch({ bus, board, hud: dom.hud, game, match });
  onHostMessage((m) => {
    if (m.type !== 'pbp:settings') return;
    const { type, ...values } = m;   // the envelope's own key is not a setting
    Object.assign(window.PBP.settings, values);
  });

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
  anim.bindBus(bus, (v) => view.projectPoint(v));   // anim.js speaks `land` and `sunk`
  // Loaded late and guarded, so a missing module never holds the game. The
  // per-frame updates ride on view.render, which the loop calls last, after
  // jiggle.update has written the flex the outline reads; the dt is the
  // loop's own (taken off view.update), so a stepped harness clock stays true.
  const feelLate = [];
  let feelDt = 0;
  const updateBase = view.update;
  view.update = (dt) => { feelDt = dt; updateBase(dt); };
  const renderBase = view.render;
  view.render = () => {
    for (const fn of feelLate) fn(feelDt);
    renderBase();
  };
  import('./board/outline.js').then((m) => {
    board.outline = m.createOutline({ group: view.pieceGroup, bus });
    feelLate.push((dt) => board.outline.update(dt, view.camera, view.renderer));
  }).catch((e) => console.warn('[pbp] outline missing', e));
  import('./board/dust.js').then((m) => {
    board.dust = m.createDust({ scene: view.scene, bus });
    feelLate.push((dt) => board.dust.update(dt, view.camera, view.renderer));
  }).catch((e) => console.warn('[pbp] dust missing', e));
  Promise.all([import('./audio/sfx.js'), import('./board/scene.js')]).then(([m, sc]) => {
    board.sfx = m.createSfx({ bus, game, group: view.pieceGroup, squareOf: sc.worldToSquare, root: dom.fx });
  }).catch((e) => console.warn('[pbp] sfx missing', e));
  // --- end J ---
  // --- K: a room to reflect, and where he just came from ---
  // The room is rendered once off the live renderer and hung on the scene; the
  // sweep re-dresses a man who was rebuilt (the art arriving, a promotion).
  import('./board/env.js').then((m) => {
    board.env = m.createEnv({ renderer: view.renderer, scene: view.scene });
    board.env.dressBoard(view.boardGroup);
    if (board.room) board.env.dressBoard(board.room.group);   // the floor, if it got here first
    board.env.dress(view.pieceGroup);
    feelLate.push((dt) => board.env.update(dt, view.pieceGroup));
  }).catch((e) => console.warn('[pbp] env missing', e));
  // The files and the ranks, cut into the plinth's rim. Nothing about them
  // moves, so they are built once and never spoken to again.
  import('./board/rim.js').then((m) => {
    board.rim = m.createRim({ group: view.boardGroup });
  }).catch((e) => console.warn('[pbp] rim missing', e));
  // A turn carries no squares, so the referee is asked which move it was. A new
  // game forgets, and so does a take-back, when one turns up.
  {
    const marks = board.drag && board.drag.markers;
    const remember = () => {
      const played = game.rules.chess.history({ verbose: true }).pop();
      if (marks) marks.setLastMove(played ? played.from : null, played ? played.to : null);
    };
    bus.on('turn', remember);
    bus.on('gameover', remember);
    bus.on('local', () => { if (marks) marks.setLastMove(null); });
  }
  // --- end K ---
  // --- L: the HUD (corner clocks, sliding light, tally, meter vignette) ---
  // The chain never replaces what is already there: another lane may want the
  // meter too. hudL is filled in when the module lands; until then the meter is
  // simply dropped, which is what a HUD that has not arrived should do.
  let hudL = null;
  { const prev = board.setMeter; board.setMeter = (m) => { if (prev) prev(m); if (hudL) hudL.setMeter(m); }; }
  import('./hud.js').then((m) => {
    hudL = m.createHud({ bus, game, board, root: document.getElementById('hud'), params });
    board.hud = hudL;
  }).catch((e) => console.warn('[pbp] hud missing', e));
  // --- end L ---
  // --- M: the hand ---
  // A man waiting on his square counts as being in hand for Esc, but he is not
  // in the way of the camera: one finger may still orbit around him, and only a
  // real grab stands the rig off.
  view.cameraRig.setDragGuard(() => drag.isHolding());
  // A pawn on the eighth is asked what he comes up as. Loaded late and guarded,
  // so a board without the module simply queens him the way it always did.
  import('./board/promote.js').then((m) => {
    board.promote = m.createPromote({ view, pieces, anim, bus, game, drag, jiggle });
    drag.setMoveHook((from, to) => board.promote.intercept(from, to));
    // On the loop's own dt, so the four second offer counts game time and a
    // harness that steps the clock by hand sees it run out.
    const base = view.update;
    view.update = (dt) => { base(dt); board.promote.update(dt); };
  }).catch((e) => console.warn('[pbp] the promotion picker is missing', e));
  // --- end M ---

  // --- N: the theatre (parade, poses, bloom, the room watches) ---
  window.PBP.settings = Object.assign({ bloom: true }, window.PBP.settings);
  { const prev = board.setMeter; board.setMeter = (m) => { if (prev) prev(m); if (board.bloom) board.bloom.setMeter(m); if (board.watch) board.watch.setMeter(m); if (board.sfx && board.sfx.setMeter) board.sfx.setMeter(m); }; }
  import('./board/parade.js').then((m) => {
    board.parade = m.createParade({ view, bus, jiggle });
    feelLate.push((dt) => board.parade.update(dt));
  }).catch((e) => console.warn('[pbp] parade missing', e));
  import('./board/poses.js').then((m) => {
    board.poses = m.createPoses({ view, pieces, bus, jiggle, outline: () => board.outline, project: (v) => view.projectPoint(v) });
    feelLate.push((dt) => board.poses.update(dt));
  }).catch((e) => console.warn('[pbp] poses missing', e));
  import('./board/watch.js').then((m) => {
    board.watch = m.createWatch({ group: view.pieceGroup, bus, jiggle, sfx: () => board.sfx });
    feelLate.push((dt) => board.watch.update(dt));
  }).catch((e) => console.warn('[pbp] watch missing', e));
  import('./board/bloom.js').then((m) => {
    board.bloom = m.createBloom({ view });
    feelLate.push((dt) => board.bloom.update(dt));
  }).catch((e) => console.warn('[pbp] bloom missing', e));
  // --- end N ---
  // --- T: the room (floor, dome, and the light that leans to the mover) ---
  // Loaded late and guarded like the rest: without it the board sits in the
  // flat navy it always did. The turn tell rides the bus on its own.
  import('./board/room.js').then((m) => {
    board.room = m.createRoom({ view, bus, env: () => board.env });
    feelLate.push((dt) => board.room.update(dt));
  }).catch((e) => console.warn('[pbp] room missing', e));
  // The air in it: motes drifting round the board, scenery only.
  import('./board/motes.js').then((m) => {
    board.motes = m.createMotes({ scene: view.scene });
    feelLate.push((dt) => board.motes.update(dt, view.camera, view.renderer));
  }).catch((e) => console.warn('[pbp] motes missing', e));
  // --- end T ---
  // Esc closes the board - but never mid-drag, where it is "put the piece back".
  window.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && !drag.isDragging()) postToHost({ type: 'pbp:exit' });
  });
  // --- Q: a capture animation is never a hostage ---
  // A tap on the board or a Space/Enter while the bishop is mid-whip lands
  // everything at once: no animation debt, the next move is already yours.
  window.addEventListener('keydown', (e) => {
    if ((e.key === ' ' || e.key === 'Enter') && anim.whipping && anim.whipping()) { anim.skip(); e.preventDefault(); }
  });
  dom.canvas.addEventListener('pointerdown', () => { if (anim.whipping && anim.whipping()) anim.skip(); }, { capture: true });
  // --- end Q ---
  signalReady();

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
  // --- O: the front door ---
  // The game is dealt by startGame, never by the door itself: the door only
  // decides WHEN. ?hotseat=1, ?auto=N and ?door=0 deal at once, so every
  // harness that photographs a live board keeps doing so; the host opens the
  // page bare and gets the menu. The lobby is the server module when one has
  // landed (net/lobbyServer.js, another lane's), else the mock, and ?lobby=mock
  // asks for the mock on purpose.
  const dealAtOnce = params.has('hotseat') || Number(params.get('auto')) > 0 || params.get('door') === '0';
  function startGame({ mode = 'hotseat', match = null } = {}) {
    window.PBP.match = match;                         // the online lane reads this
    // an online seat is built and switched in by net/online.js; the hotseat's
    // reset-and-start is not what it wants
    if (mode === 'online' && match) { window.PBP.startOnline(match); return; }
    if (game.reset) game.reset();
    bus.emit('local', { sides: ['w', 'b'], mode, match });
    game.start();
  }
  const doorReady = attachEffects(bus, board, params).then(async () => {
    if (dealAtOnce) { startGame({ mode: 'hotseat' }); return null; }
    let lobby = null;
    try {
      const wantMock = params.get('lobby') === 'mock';
      if (!wantMock) {
        try { const s = await import('./net/lobbyServer.js'); lobby = s.createServerLobby({ bus, params }); } catch { lobby = null; }
      }
      if (!lobby) { const m = await import('./net/lobby.js'); lobby = m.createMockLobby({ seed: Number(params.get('seed')) || undefined }); }
    } catch (e) { console.warn('[pbp] no lobby; the door opens without one', e); }
    const m = await import('./door/door.js');
    const door = m.createDoor({ bus, game, board, lobby, root: document.getElementById('door'), params, startGame });
    window.PBP.door = door;
    window.PBP.lobby = lobby;
    // the men stand on their squares behind the menu, clocks stopped
    pieces.setPosition(game.rules.position());
    board.setSide('w', true);
    door.show(params.get('screen') || 'menu');
    return door;
  }).catch((e) => {
    console.warn('[pbp] the door did not open; dealing a game', e);
    startGame({ mode: 'hotseat' });
    return null;
  });
  window.PBP.doorReady = doorReady;
  // --- end O ---
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
