/* ============================================================================
 * board/parade.js - the taken stand and watch.
 *
 * A captured man does not vanish. Once anim.js has sunk him through the
 * board he comes back up on the plinth rim on the side of whoever took him,
 * small (0.6 of himself), facing the board, in the order he was taken. White's
 * captures line up along white's left edge (the a-file end of his rim),
 * black's along black's left edge (the h-file end of his). Each new arrival
 * shuffles the row along; past eight a side starts a second row behind the
 * first. Fifteen a side is the most chess can take, and the most we stand.
 *
 * The man is the very same object: his materials, his flex (jiggle.js keeps
 * a state as long as he has a parent) and his outline all carry over, so a
 * parade man breathes and inks like his brothers on the board. He is made
 * unpickable (no raycast) and marked `parade` so nothing else reads him as a
 * man in play. Idle wobble already leaves `busy` men alone.
 *
 *   bus  'capture' {by, piece, square, victimSide}   a man came off; noted
 *        'sunk'    {piece, side, object}   he has finished sinking; he stands
 *        'takeback' {from, to, ply}        the last man to arrive steps down
 *        'local'                            a new game; the rim is cleared
 *
 * Respects window.PBP.settings.reducedMotion (and the media query): the men
 * appear and shuffle instantly instead of rising and easing.
 * ==========================================================================*/

import * as THREE from 'three';

/** Every number that decides where the taken stand. */
export const TUNING = Object.freeze({
  scale: 0.6,            // of the man's own size
  perRow: 8,             // men in the first row before a second starts
  maxPerSide: 15,
  rimZ: 4.24,            // first row, from the board centre (the plinth edge is 4.7)
  rowGap: 0.30,          // the second row stands this much further out
  spacing: 0.56,         // between neighbours along the rim
  start: 3.85,           // the first man stands this far from the centre line
  rimY: -0.08,           // the plinth top
  riseFrom: 0.55,        // a new arrival comes up from this far below the rim
  riseSec: 0.55,
  ease: 9,               // per-second pull toward a slot while the row shuffles
  contactAlpha: 0.32,    // the shade disc under a parade man, fixed
  arriveSquash: 2.2,     // jiggle squash as he settles
});

function reduced() {
  if (typeof window === 'undefined') return false;
  const s = window.PBP && window.PBP.settings;
  if ((s && s.reducedMotion) || (window.PBP && window.PBP.reducedMotion)) return true;
  try { return !!window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch { return false; }
}

const easeOut = (p) => 1 - Math.pow(1 - p, 3);

/**
 * `view` is board/scene.js (boardGroup is where the men stand); `bus` the game
 * bus; `jiggle` the flex system for the arrival squash (optional).
 */
export function createParade({ view, bus = null, jiggle = null }) {
  const T = TUNING;
  const rows = { w: [], b: [] };     // taker -> [{ piece, slot, want, t }]
  let pendingSkip = 0;               // taken back before he finished sinking

  /** World slot for the n-th man on `taker`'s rim. */
  function slotFor(taker, n) {
    const row = Math.floor(n / T.perRow);
    const i = n % T.perRow;
    const along = T.start - i * T.spacing - (row ? T.spacing * 0.5 : 0);
    // White's rim is +z and his left is the a-file (-x); black's rim is -z and
    // his left is the h-file (+x). Both rows run left to right for their owner.
    const sign = taker === 'w' ? 1 : -1;
    return {
      x: -sign * along,
      z: sign * (T.rimZ + row * T.rowGap),
      yaw: taker === 'w' ? 0 : Math.PI,   // facing the board from his rim
    };
  }

  function dress(piece) {
    const d = piece.userData;
    d.parade = true;
    d.busy = true;
    d.held = false;
    d.square = null;
    d.tookOne = false;
    if (!d.paradeScale) d.paradeScale = piece.scale.clone();
    piece.scale.copy(d.paradeScale).multiplyScalar(T.scale);
    for (const mat of d.materials || (d.material ? [d.material] : [])) {
      mat.transparent = false;
      mat.opacity = 1;
      mat.emissiveIntensity = 0;
    }
    piece.traverse((o) => { if (o.isMesh) o.raycast = () => {}; });
    const disc = d.contact;
    if (disc) {
      disc.visible = true;
      disc.material.opacity = T.contactAlpha;
      disc.position.set(0, 0.005, 0);
      disc.quaternion.identity();
      disc.scale.setScalar(disc.userData.foot || 1);
    }
  }

  function relayout(taker) {
    const row = rows[taker];
    for (let n = 0; n < row.length; n++) row[n].want = slotFor(taker, n);
  }

  function stand(piece) {
    const victim = piece.userData.side === 'b' ? 'b' : 'w';
    const taker = victim === 'w' ? 'b' : 'w';
    const row = rows[taker];
    if (row.length >= T.maxPerSide) return;   // more than chess allows; keep him gone
    dress(piece);
    const entry = { piece, want: null, t: 0, rising: !reduced() };
    row.push(entry);
    relayout(taker);
    const w = entry.want;
    piece.rotation.set(0, w.yaw, 0);
    piece.position.set(w.x, entry.rising ? T.rimY - T.riseFrom : T.rimY, w.z);
    view.boardGroup.add(piece);
    if (!entry.rising && jiggle) jiggle.impulse(piece, { squash: T.arriveSquash * 0.5 });
  }

  function stepDown(entry) {
    const piece = entry.piece;
    view.boardGroup.remove(piece);
    const d = piece.userData;
    if (d.paradeScale) piece.scale.copy(d.paradeScale);
    d.parade = false;
    d.busy = false;
  }

  function clear() {
    for (const side of ['w', 'b']) {
      for (const e of rows[side]) stepDown(e);
      rows[side].length = 0;
    }
    pendingSkip = 0;
  }

  /** The last man to arrive leaves the rim (a move was taken back). */
  function takeback() {
    let last = null;
    let side = null;
    for (const s of ['w', 'b']) {
      const row = rows[s];
      const e = row[row.length - 1];
      if (e && (!last || e.order > last.order)) { last = e; side = s; }
    }
    if (!last) { pendingSkip++; return; }   // he is still sinking; skip him when he surfaces
    rows[side].pop();
    stepDown(last);
    relayout(side);
  }

  let order = 0;
  const unsubs = [];
  if (bus && typeof bus.on === 'function') {
    unsubs.push(bus.on('capture', () => { /* noted; the man arrives on `sunk` */ }));
    unsubs.push(bus.on('sunk', (p) => {
      const piece = p && p.object;
      if (!piece || !piece.userData) return;
      if (pendingSkip > 0) { pendingSkip--; return; }
      stand(piece);
      const row = rows[piece.userData.side === 'b' ? 'w' : 'b'];
      row[row.length - 1].order = ++order;
    }));
    unsubs.push(bus.on('takeback', () => takeback()));
    unsubs.push(bus.on('local', () => clear()));
  }

  function update(dt) {
    const k = reduced() ? 1 : 1 - Math.exp(-T.ease * (dt || 0.016));
    for (const side of ['w', 'b']) {
      for (const e of rows[side]) {
        const piece = e.piece;
        const w = e.want;
        piece.position.x += (w.x - piece.position.x) * k;
        piece.position.z += (w.z - piece.position.z) * k;
        if (e.rising) {
          e.t += dt;
          const p = Math.min(1, e.t / T.riseSec);
          piece.position.y = T.rimY - T.riseFrom * (1 - easeOut(p));
          if (p >= 1) {
            e.rising = false;
            piece.position.y = T.rimY;
            if (jiggle) jiggle.impulse(piece, { squash: T.arriveSquash });
          }
        }
      }
    }
  }

  return {
    update,
    /** For the harness: who stands where. */
    stats() {
      const side = (s) => rows[s].map((e) => ({
        type: e.piece.userData.type, x: +e.piece.position.x.toFixed(2), z: +e.piece.position.z.toFixed(2), rising: e.rising,
      }));
      return { w: side('w'), b: side('b'), pendingSkip };
    },
    clear,
    dispose() { for (const u of unsubs) if (typeof u === 'function') u(); clear(); },
  };
}
