/* ============================================================================
 * board/drag.js - picking a man up and putting him down.
 *
 * Pointer down raycasts the men, and the spot on his body the ray struck is
 * remembered: from then on that spot rides under the cursor, so a held man is
 * in your hand rather than floating over it. He is lifted just enough to skim
 * the tops of the others, his base tells you which square he would land on,
 * and board/markers.js draws a dot on every square he may reach and a red ring
 * around every man he may take. A drop either plays the move or springs back
 * with a wobble. The screen position of the held piece goes out on the bus
 * every frame so the effects layer can draw over it.
 *
 * The hand knows two more things than it used to:
 *
 *   hover    with nothing held, the man under the cursor lifts a hair and is
 *            flagged `userData.hover` for the outline, and the canvas cursor
 *            says whether he can be picked up at all. Only the side to move
 *            answers. The raycast runs at most once a frame, and only when the
 *            pointer or the camera has actually moved.
 *   a click  a press and a release on the same man, quick and still, is a click
 *            and not a drag: he stays lifted, his legal squares stay lit, and
 *            the next click on one of them plays the move. A click elsewhere
 *            lets him go, a click on another of your men moves the selection
 *            over, and Esc drops it. A man waiting like that still counts as a
 *            man in hand for isDragging(), so whoever owns Esc reads the same
 *            "not now" a drag gives them; isHolding() is the narrower question,
 *            which is what the camera's touch guard wants.
 * ==========================================================================*/

import * as THREE from 'three';
import { worldToSquare, squareToWorld } from './scene.js';
import { createMarkers } from './markers.js';

/** Every number the hand is made of. One place, on purpose. */
export const TUNING = Object.freeze({
  lift: 0.52,        // how high above the board a held man rides
  lag: 11,           // follow stiffness; lower is lazier
  tilt: 0.22,        // how far he leans into the direction of travel
  grabMax: 0.75,     // highest point on a man the hand is allowed to hold
  grabMid: 0.45,     // where the hand lands when the square, not the man, was hit
  hoverLift: 0.04,   // a hair off the board, so the cursor has some weight
  hoverRise: 0.06,   // e-fold seconds up ...
  hoverFall: 0.12,   // ... and back down when the cursor leaves
  selectLift: 0.11,  // where a clicked man waits for his square
  tapMs: 250,        // a press shorter than this ...
  tapPx: 6,          // ... and stiller than this is a click, not a drag
});

const T = TUNING;

function reducedMotion() {
  if (typeof window === 'undefined') return false;
  const pbp = window.PBP;
  if (pbp && (pbp.reducedMotion || (pbp.settings && pbp.settings.reducedMotion))) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

/** Keys belong to whoever is not filling in a form. */
function typing() {
  if (typeof document === 'undefined') return false;
  const el = document.activeElement;
  if (!el) return false;
  const tag = el.tagName;
  return el.isContentEditable || tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

export function createDrag({ view, pieces, anim, bus, game, jiggle = null }) {
  const canvas = view.renderer.domElement;
  const ray = new THREE.Raycaster();
  const ndc = new THREE.Vector2();
  const ground = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const holdPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  const hit = new THREE.Vector3();
  const target = new THREE.Vector3();
  const grab = new THREE.Vector2();   // piece origin minus the grabbed point, in x/z

  const markers = createMarkers({ group: view.boardGroup });
  if (view.setHighlightSink) view.setHighlightSink(markers.show);

  let held = null;        // the piece object under the cursor
  let from = null;        // the square it was picked up from
  let legal = [];         // every square it may land on
  let takes = new Set();  // the ones holding a man it may take
  let hover = null;
  let holdY = T.lift;     // world height of the plane the cursor drags along
  const at = { x: 0, y: 0 };   // last pointer position, in client pixels

  // --- the hand with nothing in it -------------------------------------------
  let hoverPiece = null;       // the man under the cursor, when he is yours
  let pointerIn = false;
  let pointerMoved = true;
  let cursor = '';
  const camAt = new THREE.Vector3(Infinity, 0, 0);
  const lifts = new Map();     // piece -> { cur, want } height off his own square

  // --- a click instead of a drag ---------------------------------------------
  let press = null;            // { x, y, t } of the pointer that is down
  let pending = false;         // this press could still turn out to be a click
  let toggledOff = null;       // the square this press let go of, so it cannot re-take it
  let selected = null;         // the man waiting for his square
  let selSquare = null;
  let selLegal = [];
  let selTakes = new Set();
  let selHover = null;

  function toNdc(ev) {
    if (ev) { at.x = ev.clientX; at.y = ev.clientY; }
    const r = canvas.getBoundingClientRect();
    ndc.set(((at.x - r.left) / r.width) * 2 - 1, -((at.y - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(ndc, view.camera);
  }

  /** Where the cursor meets the board, as a square name (or null, off board). */
  function squareUnder(ev) {
    toNdc(ev);
    if (!ray.ray.intersectPlane(ground, hit)) return null;
    return worldToSquare(hit.x, hit.z);
  }

  /** The man under the cursor, and the point on him the ray landed on. */
  function pieceUnder(ev) {
    toNdc(ev);
    const hits = ray.intersectObjects(view.pieceGroup.children, true);
    for (const h of hits) {
      let node = h.object;
      while (node && node.parent !== view.pieceGroup) node = node.parent;
      if (node && node.userData.square) return { piece: node, point: h.point };
    }
    return null;
  }

  /**
   * Aim the held man at the cursor. The ray is cut with a horizontal plane at
   * the height of the spot that was grabbed, not with the board, so that spot
   * stays under the cursor whatever angle the camera sits at. The man hangs off
   * it by the offset the grab recorded, which leaves his base over the square
   * the markers say he is going to. Called with no event it re-uses the last
   * pointer position, which is how the camera can sway under a still cursor
   * without the man sliding out of your hand.
   *
   * While the press could still turn out to be a click he does not follow the
   * cursor at all: he lifts straight up off his own square, to the height a
   * waiting man sits at. That is what makes a click a click. A man hanging at
   * hold height stands over a square nearer the camera than the one he came
   * from, so a press that took him there and let go would read as a move; and
   * it means a click never yanks him into the air to put him straight back
   * down. The moment the press becomes a drag he flies to the hand.
   */
  function aim(ev) {
    toNdc(ev);
    if (pending && held && held.userData.home) {
      const home = held.userData.home;
      target.set(home.x, T.selectLift, home.z);
      return true;
    }
    holdPlane.constant = -holdY;
    if (!ray.ray.intersectPlane(holdPlane, hit)) return false;
    target.set(hit.x + grab.x, T.lift, hit.z + grab.y);
    return true;
  }

  /** The square the held man is standing over right now. */
  function overSquare() {
    return worldToSquare(target.x, target.z);
  }

  /**
   * The hint layer, for whichever man the hand is thinking about: the one in
   * the hand, or the one waiting on his square. It goes out through
   * view.setHighlights, so a lane holding only the view sees the same list.
   */
  function paint() {
    const src = held ? { at: from, list: legal, take: takes, hot: hover }
      : (selected ? { at: selSquare, list: selLegal, take: selTakes, hot: selHover } : null);
    if (!src) { view.setHighlights([]); return; }
    const out = src.list.map((sq) => ({
      square: sq,
      kind: src.take.has(sq) ? 'capture' : 'move',
      hover: sq === src.hot,
    }));
    if (src.at) out.push({ square: src.at, kind: 'origin' });
    view.setHighlights(out);
  }

  /**
   * chess.js hands back a flag string per move: 'c' is a capture and 'e' is en
   * passant, which takes a man standing on a square the pawn does not land on.
   * Castling ('k' and 'q') takes nothing, so it stays a plain dot.
   */
  function readMoves(sq) {
    const rules = game.rules;
    const verbose = rules && rules.movesFrom ? rules.movesFrom(sq) : [];
    if (!verbose.length) return { squares: game.legalTargets(sq), captures: new Set() };
    const captures = new Set();
    for (const m of verbose) {
      const flags = String(m.flags || '');
      if (m.captured || flags.includes('c') || flags.includes('e')) captures.add(m.to);
    }
    return { squares: verbose.map((m) => m.to), captures };
  }

  // --- lift -------------------------------------------------------------------
  // A hovered or waiting man rides a little off his square. It is written on his
  // root position, and drag.update runs after pieces.update, so it sits over the
  // idle sway instead of fighting the flex, which is the mesh's own business.
  function wantLift(piece, y) {
    if (!piece) return;
    const s = lifts.get(piece) || { cur: piece.position.y || 0, want: 0 };
    s.want = y;
    lifts.set(piece, s);
  }

  function updateLifts(dt) {
    const still = reducedMotion();
    for (const [piece, s] of [...lifts]) {
      // A man in hand, in flight, or off the board is not ours to place.
      if (!piece.parent || piece.userData.held || piece.userData.busy) { lifts.delete(piece); continue; }
      const tau = Math.max(0.001, s.want > s.cur ? T.hoverRise : T.hoverFall);
      s.cur = still ? s.want : s.cur + (s.want - s.cur) * (1 - Math.exp(-dt / tau));
      if (s.want <= 0 && s.cur < 0.0006) { piece.position.y = 0; lifts.delete(piece); continue; }
      piece.position.y = s.cur;
    }
  }

  function setCursor(name) {
    if (cursor === name) return;
    cursor = name;
    canvas.style.cursor = name;
  }

  /** Has the camera moved since the last look? A still cursor still needs one. */
  function cameraMoved() {
    if (camAt.distanceToSquared(view.camera.position) < 1e-8) return false;
    camAt.copy(view.camera.position);
    return true;
  }

  /** One raycast a frame: who is under the cursor, and what the cursor says. */
  function look() {
    let piece = null;
    if (pointerIn) {
      const found = pieceUnder(null);
      const sq = found && found.piece.userData.square;
      if (sq && game.canPick(sq)) piece = found.piece;
    }
    if (piece !== hoverPiece) {
      if (hoverPiece) {
        hoverPiece.userData.hover = false;
        if (hoverPiece !== selected) wantLift(hoverPiece, 0);
      }
      hoverPiece = piece;
      if (piece) {
        piece.userData.hover = true;
        if (piece !== selected && !reducedMotion()) wantLift(piece, T.hoverLift);
      }
    }
    // With a man waiting, the square he could land on answers too.
    let want = piece ? 'grab' : 'default';
    if (selected) {
      const sq = piece ? piece.userData.square : (pointerIn ? squareUnder(null) : null);
      const hot = sq && sq !== selSquare && selLegal.includes(sq) ? sq : null;
      if (hot) want = 'pointer';
      if (hot !== selHover) { selHover = hot; paint(); }
    }
    setCursor(want);
  }

  // --- the man who waits -------------------------------------------------------
  function select(piece, square) {
    if (selected && selected !== piece) clearSelection(false);
    selected = piece;
    selSquare = square;
    const moves = readMoves(square);
    selLegal = moves.squares;
    selTakes = moves.captures;
    selHover = null;
    const home = piece.userData.home;
    if (home) { piece.position.x = home.x; piece.position.z = home.z; }
    piece.rotation.x = 0;
    piece.rotation.z = 0;
    wantLift(piece, T.selectLift);
    paint();
  }

  function clearSelection(repaint = true) {
    if (!selected) return;
    const piece = selected;
    selected = null; selSquare = null; selLegal = []; selTakes = new Set(); selHover = null;
    wantLift(piece, piece === hoverPiece && !reducedMotion() ? T.hoverLift : 0);
    if (repaint) paint();
  }

  /** A click on a legal square: the man waiting on his own flies over. */
  function playSelected(to) {
    const start = selSquare;
    clearSelection();
    return game.tryMove(start, to);
  }

  function onDown(ev) {
    if (held || ev.button > 0) return;
    pointerIn = true;
    const found = pieceUnder(ev);
    const square = found ? null : squareUnder(ev);
    const under = found ? found.piece.userData.square : square;
    const piece = found ? found.piece : (square ? pieces.pieceAt(square) : null);

    // A man is already waiting: this click either sends him somewhere or lets
    // him go. Sending him is the whole gesture, so nothing is picked up after.
    toggledOff = null;
    if (selected) {
      if (under && under !== selSquare && selLegal.includes(under)) {
        playSelected(under);
        ev.preventDefault();
        return;
      }
      toggledOff = selSquare;
      clearSelection();
    }

    if (!piece || !piece.userData.square) return;
    const sq = piece.userData.square;
    if (!game.canPick(sq)) return;

    held = piece;
    from = sq;
    lifts.delete(piece);
    const moves = readMoves(sq);
    legal = moves.squares;
    takes = moves.captures;
    hover = null;
    held.userData.held = true;
    held.userData.busy = true;
    if (jiggle) jiggle.grab(held);   // lifted off the board, so the tip sags
    press = { x: ev.clientX, y: ev.clientY, t: performance.now() };
    pending = true;                  // ... until he travels, or outstays a click

    // Hold him where he was actually grabbed. A hit on the body gives the exact
    // spot; the square fallback (a click that slipped past the mesh) takes him
    // around the middle. The height is capped, so grabbing a king by his crown
    // does not swing his base half a board away from the cursor.
    const base = squareToWorld(sq, 0);
    if (found) {
      grab.set(base.x - found.point.x, base.z - found.point.z);
      holdY = T.lift + THREE.MathUtils.clamp(found.point.y - piece.position.y, 0.04, T.grabMax);
    } else {
      grab.set(0, 0);
      holdY = T.lift + Math.min(T.grabMax, T.grabMid * (piece.userData.scaleBase || 1));
    }
    target.copy(held.position);
    aim(ev);                     // so he lifts on the click, not on the first move
    const on = overSquare();
    hover = legal.includes(on) ? on : null;
    paint();
    setCursor('grabbing');
    // A synthetic pointer (the smoke harness, some remote-control paths) has no
    // capture to take, and asking for one throws.
    try { canvas.setPointerCapture(ev.pointerId); } catch { /* nothing to hold */ }
    bus.emit('grab', { square: sq, piece: piece.userData.type, screen: view.projectPoint(held.position) });
    ev.preventDefault();
  }

  function onMove(ev) {
    at.x = ev.clientX;
    at.y = ev.clientY;
    pointerIn = true;
    pointerMoved = true;
    if (!held) return;
    if (pending && press && Math.hypot(ev.clientX - press.x, ev.clientY - press.y) > T.tapPx) pending = false;
    if (!aim(ev)) return;
    const next = overSquare();
    const want = legal.includes(next) ? next : null;
    if (want !== hover) { hover = want; paint(); }
  }

  function onUp(ev) {
    if (!held) return;
    const piece = held;
    const start = from;
    aim(ev);
    const to = overSquare();
    const tap = pending && press
      && performance.now() - press.t <= T.tapMs
      && Math.hypot(ev.clientX - press.x, ev.clientY - press.y) <= T.tapPx;
    held = null;
    from = null;
    hover = null;
    legal = [];
    takes = new Set();
    pending = false;
    press = null;
    markers.clear();
    piece.userData.held = false;
    piece.userData.busy = false;
    piece.rotation.x = 0;
    piece.rotation.z = 0;
    setCursor('grab');
    try { canvas.releasePointerCapture(ev.pointerId); } catch { /* never taken */ }

    // He never left his square and the press was quick: that was a click, not a
    // drag. He waits where he is with his squares lit, instead of being told no.
    if (tap && (!to || to === start)) {
      const again = toggledOff === start;
      toggledOff = null;
      if (again) wantLift(piece, 0);      // the same click that let him go
      else select(piece, start);
      bus.emit('drop', { ok: true, select: !again, square: start });
      return;
    }
    toggledOff = null;

    // A legal drop is played by the game, which moves the man and lets anim.js
    // fly it down from where it was being held.
    const played = to && to !== start ? game.tryMove(start, to) : null;
    if (played) {
      bus.emit('drop', { ok: true });
    } else {
      anim.springBack(piece);
      bus.emit('drop', { ok: false });
    }
  }

  function onCancel() {
    if (!held) return;
    const piece = held;
    held = null; from = null; hover = null; legal = []; takes = new Set();
    pending = false; press = null;
    markers.clear();
    piece.userData.held = false;
    piece.rotation.x = 0;
    piece.rotation.z = 0;
    anim.springBack(piece);
    bus.emit('drop', { ok: false });
  }

  function onLeave() {
    pointerIn = false;
    pointerMoved = true;
  }

  function onBlur() {
    onCancel();
    clearSelection();
    onLeave();
    setCursor('default');
  }

  function onKey(ev) {
    if (ev.ctrlKey || ev.altKey || ev.metaKey || typing()) return;
    if (ev.key !== 'Escape' || !selected) return;
    // Esc belongs to whoever wants to leave the board as well, and they ask
    // isDragging() inside this same event: the man is only let go once the
    // whole event is over. A microtask would not do, because the browser runs
    // one of those between two listeners on the same key.
    setTimeout(() => clearSelection(), 0);
  }

  /** Lagged follow, a lean into the travel, and the per-frame screen ping. */
  function update(dt) {
    markers.update(dt);
    updateLifts(dt);
    if (!held) {
      if (pointerMoved || cameraMoved()) { look(); pointerMoved = false; }
      return;
    }
    if (pending && press && performance.now() - press.t > T.tapMs) pending = false;
    // The camera sways, so where the cursor points moves even when it does not.
    const was = hover;
    if (aim(null)) {
      const next = overSquare();
      hover = legal.includes(next) ? next : null;
      if (hover !== was) paint();
    }
    const k = 1 - Math.exp(-T.lag * dt);
    const dx = target.x - held.position.x;
    const dz = target.z - held.position.z;
    held.position.x += dx * k;
    held.position.y += (target.y - held.position.y) * k;
    held.position.z += dz * k;
    held.rotation.z = THREE.MathUtils.clamp(-dx * T.tilt, -0.35, 0.35);
    held.rotation.x = THREE.MathUtils.clamp(dz * T.tilt, -0.35, 0.35);
    // Whatever the body just covered, the soft top has yet to catch up on.
    if (jiggle) jiggle.lag(held, dx * k, dz * k);
    bus.emit('dragmove', { screen: view.projectPoint(held.position) });
  }

  canvas.addEventListener('pointerdown', onDown);
  canvas.addEventListener('pointermove', onMove);
  canvas.addEventListener('pointerup', onUp);
  canvas.addEventListener('pointercancel', onCancel);
  canvas.addEventListener('pointerleave', onLeave);
  window.addEventListener('keydown', onKey);
  window.addEventListener('blur', onBlur);
  // A move played by anyone (a drag, a click, the demo) ends the wait.
  const offTurn = bus.on('turn', () => { clearSelection(); pointerMoved = true; });
  const offOver = bus.on('gameover', () => clearSelection());

  return {
    update,
    markers,
    /** A man in hand OR a man waiting on his square: the board is busy either way. */
    isDragging: () => !!held || !!selected,
    /** The narrower question: is one actually in the hand right now? */
    isHolding: () => !!held,
    /** The square of the man waiting for his, or null. */
    selection: () => selSquare,
    /** Let go of him, from anywhere. */
    deselect: () => clearSelection(),
    // The height of the plane the cursor is dragging along. The screenshot
    // harness aims its release with this, because board level is no longer
    // where the held man is.
    holdHeight: () => (held ? holdY : 0),
    /** What the hand is doing, for the smoke harness. */
    debug() {
      return {
        held: held ? held.userData.square : null,
        selected: selSquare,
        hover: hoverPiece ? hoverPiece.userData.square : null,
        over: selHover,          // the legal square under the cursor, while one waits
        cursor,
        hoverLift: hoverPiece ? Number(hoverPiece.position.y.toFixed(4)) : 0,
        selectLift: selected ? Number(selected.position.y.toFixed(4)) : 0,
        markers: markers.count(),
      };
    },
    dispose() {
      canvas.removeEventListener('pointerdown', onDown);
      canvas.removeEventListener('pointermove', onMove);
      canvas.removeEventListener('pointerup', onUp);
      canvas.removeEventListener('pointercancel', onCancel);
      canvas.removeEventListener('pointerleave', onLeave);
      window.removeEventListener('keydown', onKey);
      window.removeEventListener('blur', onBlur);
      offTurn();
      offOver();
      if (view.setHighlightSink) view.setHighlightSink(null);
      lifts.clear();
      markers.dispose();
    },
  };
}
