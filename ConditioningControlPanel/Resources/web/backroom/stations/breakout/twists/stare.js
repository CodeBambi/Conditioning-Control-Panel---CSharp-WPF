/* ============================================================================
 * stations/breakout/twists/stare.js - the pure sim half of the twist.
 *
 * Twist: Stare (act 3, THEY NOTICE). A black and white spiral eye sits in the
 * background and looks down the field. While a ball sits inside its gaze cone
 * AND the eye can actually see it (any living brick breaks the line of sight)
 * the eye holds on. Hold long enough and the plain bricks nearest the ball go
 * grey: flag `judged`, one extra hit, drawn desaturated. Duck back behind the
 * wall, leave the cone, or break out, and the whole judgement lifts at once.
 *
 * It never touches paddle control, never costs a ball, and nothing it does can
 * make a wall unclearable: a judged brick is a plain brick with one more hit.
 *
 * Pure sim: no DOM, no clock, no Math.random. The look lives in stare-render.js.
 * The act 3 lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/
export const id = 'stare';

/** How long the ball has to sit in the cone before the eye judges. */
export const JUDGE_AFTER_S = 1.5;
/** How many bricks one judgement takes. */
export const JUDGE_N = 3;
/** The most bricks one board carries at once, so a long stare is pressure and not a wall. */
export const JUDGE_CAP = 9;
/** Half the gaze cone, in radians, off straight down. Generous: the eye is not a sniper. */
export const CONE_HALF = 0.62;

/** Where the eye hangs: high, centred, behind the wall. */
export const eyeAt = (w, h) => ({ x: w / 2, y: h * 0.16 });

/**
 * How plainly the eye is drawn on each board of the act: a glimpse the first
 * time, barely hiding by the last. stare-render.js reads it off `g.stare`.
 */
export const EYE_PRESENCE = { st_notice_03: 0.3, st_notice_07: 0.92 };
export const presenceFor = board => (board in EYE_PRESENCE ? EYE_PRESENCE[board] : 0.58);

/** A brick the eye can find fault with: plain, living, and nobody else's. */
export const isPlain = br => !!br && br.alive === true && !br.steel && !br.clay && !br.wire
  && !br.core && !br.key && !br.gate && !br.picture && !br.spiralBrick && !br.powerup && !br.treat;

/** Inside the downward cone? Anything level with the eye or above it is behind it. */
export function inCone(eye, x, y, half = CONE_HALF) {
  const dy = y - eye.y;
  if (!(dy > 0)) return false;
  return Math.abs(Math.atan2(x - eye.x, dy)) <= half;
}

/** Does the segment (x1,y1)-(x2,y2) cross this axis-aligned box? Liang-Barsky. */
export function segmentHitsBox(x1, y1, x2, y2, bx, by, bw, bh) {
  const dx = x2 - x1, dy = y2 - y1;
  let t0 = 0, t1 = 1;
  const clip = (p, q) => {
    if (p === 0) return q >= 0;
    const r = q / p;
    if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
    else { if (r < t0) return false; if (r < t1) t1 = r; }
    return true;
  };
  return clip(-dx, x1 - bx) && clip(dx, bx + bw - x1)
    && clip(-dy, y1 - by) && clip(dy, by + bh - y1);
}

/** Any living brick between the eye and the ball hides it. That is the whole defence. */
export function blocked(bricks, eye, x, y) {
  for (const br of bricks || []) {
    if (!br || br.alive !== true) continue;
    if (segmentHitsBox(eye.x, eye.y, x, y, br.x, br.y, br.w, br.h)) return true;
  }
  return false;
}

/** The ball the eye has locked on to, or null. A ghost or a falling ball is not in the world. */
export function seenBall(g, eye) {
  for (const b of (Array.isArray(g.balls) ? g.balls : [])) {
    if (!b || b.lost || b.falling || b.ghost) continue;
    if (!inCone(eye, b.x, b.y)) continue;
    if (blocked(g.bricks, eye, b.x, b.y)) continue;
    return b;
  }
  return null;
}

/** The `n` plain bricks closest to a point, nearest first. Ties settle by cell, so it is deterministic. */
export function nearestPlain(bricks, x, y, n = JUDGE_N) {
  return (bricks || []).filter(br => isPlain(br) && !br.judged)
    .map(br => ({ br, d: Math.hypot(br.x + br.w / 2 - x, br.y + br.h / 2 - y) }))
    .sort((a, b) => (a.d - b.d) || (a.br.row - b.br.row) || (a.br.col - b.br.col))
    .slice(0, Math.max(0, n))
    .map(r => r.br);
}

/** One extra hit, and the armoured plate, so the extra hit can be seen coming. */
export function judge(br) {
  if (!br || br.judged) return false;
  br.judgedFrom = { hp: br.hp, strength: br.strength || 0 };
  br.judged = true;
  br.hp = (br.hp || 1) + 1;
  br.strength = Math.max(br.strength || 0, br.hp);
  return true;
}

/** The eye looked away. Give the hit back, and never take one below one. */
export function unjudge(br) {
  if (!br || !br.judged) return false;
  const from = br.judgedFrom || { hp: 1, strength: 0 };
  br.judged = false;
  br.judgedFrom = null;
  br.hp = Math.max(1, (br.hp || 1) - 1);
  br.strength = from.strength;
  return true;
}

const fresh = (g, board) => ({
  board, on: false, dwell: 0, judged: 0, rounds: 0, state: g.state || 'colour',
  eye: eyeAt(g.w, g.h), presence: presenceFor(board),
});

/** The twist's own state, on its own key. Rebuilt with the board. */
function state(g) {
  if (!g.stare || g.stare.board !== g.doorBoard) g.stare = fresh(g, g.doorBoard);
  return g.stare;
}

/** Lift the whole judgement. Returns how many bricks came back. */
export function clearJudgement(g, st) {
  let n = 0;
  for (const br of g.bricks || []) if (unjudge(br)) n++;
  st.judged = 0; st.dwell = 0;
  return n;
}

/** The eye loses the ball: everything it did lifts, and it says so once. */
function lookAway(g, st, ctx) {
  const held = st.on;
  clearJudgement(g, st);
  st.on = false;
  if (held && ctx) ctx.emit('stareOff', {});
}

export function build(g) { g.stare = null; state(g); }

export function update(g, dt, ctx) {
  const st = state(g);
  st.eye = eyeAt(g.w, g.h);
  // A breakout or a relapse wipes the slate: the room stops looking at you for a moment.
  if (g.state !== st.state) { st.state = g.state; lookAway(g, st, ctx); }
  // GREY is payload free, and a transition is nobody's fault.
  if (g.state !== 'colour' || g.transition) { lookAway(g, st, ctx); return; }

  const ball = seenBall(g, st.eye);
  if (!ball) { lookAway(g, st, ctx); return; }

  if (!st.on) { st.on = true; st.dwell = 0; ctx.emit('stareOn', { x: ball.x, y: ball.y }); }
  st.dwell += Math.max(0, Number(dt) || 0);
  if (st.dwell < JUDGE_AFTER_S) return;

  st.dwell = 0;
  const room = Math.max(0, JUDGE_CAP - st.judged);
  const taken = nearestPlain(g.bricks, ball.x, ball.y, Math.min(JUDGE_N, room)).filter(judge);
  if (!taken.length) return;
  st.judged += taken.length;
  st.rounds++;
  ctx.emit('stareJudge', { x: ball.x, y: ball.y, n: taken.length });
}

/** The board is done. The next one builds its own eye; no grey face carries over. */
export function wallCleared(g) {
  clearJudgement(g, state(g));
  g.stare = null;
}

export default {
  id, build, update, wallCleared,
  eyeAt, inCone, blocked, seenBall, nearestPlain, judge, unjudge, isPlain,
  segmentHitsBox, clearJudgement, presenceFor,
  JUDGE_AFTER_S, JUDGE_N, JUDGE_CAP, CONE_HALF, EYE_PRESENCE,
};
