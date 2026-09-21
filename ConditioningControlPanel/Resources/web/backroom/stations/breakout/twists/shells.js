/* ============================================================================
 * stations/breakout/twists/shells.js - the pure sim half of the twist.
 * Twist: Shells (act 4, ENOUGH). Every relapse this run left an OLD SELF behind.
 * Up to five pale shells drift slowly across the field. The ball passes THROUGH
 * a shell and comes out slowed for a beat and desaturated (g.sat minus a little,
 * floor 0). Three touches pop a shell for good, with a soft release.
 * Failure subtracts, release gives back (AGENTS.md audio rule 6).
 *
 * A shell is NOT a collider: it never turns a ball, never costs a ball and never
 * touches the paddle. It takes a little colour off the room and hands the ball
 * back heavy. That is the whole price.
 *
 * Nothing here may touch the DOM, the clock or Math.random (twists/CONTRACT.md).
 * ==========================================================================*/
export const id = 'shells';

/** Never more than five, however many relapses the run has had. */
export const MAX_SHELLS = 5;
/** A shells board is never empty: two old selves are always standing there. */
export const MIN_SHELLS = 2;
/** Touches before a shell pops. */
export const SHELL_HP = 3;
/** A shell is the ghost ball, hollow and much bigger (the ball is r 8). */
export const SHELL_R = 36;

/** About one beat at 96 bpm: how long the ball comes out heavy. */
export const SLOW_S = 0.62;
/** How slow, at the moment of the touch. It eases back over the beat, it never stops. */
export const SLOW_MUL = 0.55;
/** What one touch takes off the room. Three of them cost less than one rung. */
export const SAT_COST = 0.035;
/** What the release hands back. Less than the three touches took: a shell is never profitable. */
export const SAT_GIVE = 0.05;

/** How many shells this board stands up. At least two, never more than five. */
export function shellCount(relapses) {
  const n = Math.floor(Number(relapses) || 0);
  return Math.max(MIN_SHELLS, Math.min(MAX_SHELLS, n));
}

/**
 * The twist's own state, on its own key (twists/CONTRACT.md 1b). Rebuilt with the
 * board, but `relapses` is carried over: the count belongs to the RUN, not the wall.
 */
export function stateOf(g) {
  if (!g.shells) g.shells = { board: g.doorBoard, list: [], t: 0, relapses: 0, was: g.state || 'colour' };
  return g.shells;
}

/** Where a shell is at sim time `t`. Pure, authored, and still in reduced motion. */
export function posAt(sh, t, reduced = false) {
  if (!sh) return { x: 0, y: 0 };
  if (reduced) return { x: sh.x0, y: sh.y0 };
  return {
    x: sh.x0 + sh.ax * Math.sin(sh.wx * t + sh.px),
    y: sh.y0 + sh.ay * Math.sin(sh.wy * t + sh.py),
  };
}

/** One old self, on its own lane across the field. Seeded once, at build, then pure. */
function mkShell(i, n, w, h, rng) {
  const lane = (i + 0.5) / Math.max(1, n);
  return {
    hp: SHELL_HP, r: SHELL_R, inside: [],
    x0: w * (0.14 + 0.72 * lane),
    y0: h * (0.36 + 0.34 * rng()),
    ax: 48 + 46 * rng(),
    ay: 22 + 30 * rng(),
    wx: 0.10 + 0.09 * rng(),
    wy: 0.14 + 0.13 * rng(),
    px: rng() * Math.PI * 2,
    py: rng() * Math.PI * 2,
    x: 0, y: 0, seed: rng(),
  };
}

/** Stand the shells up. One per relapse this run, floor two, cap five. */
export function build(g, ctx) {
  const carried = g.shells && Number.isFinite(g.shells.relapses) ? g.shells.relapses : 0;
  const rng = (ctx && ctx.rng) || (() => 0.5);
  const w = (ctx && ctx.w) || g.w || 1280, h = (ctx && ctx.h) || g.h || 720;
  const n = shellCount(carried);
  const list = [];
  for (let i = 0; i < n; i++) list.push(mkShell(i, n, w, h, rng));
  g.shells = { board: g.doorBoard, list, t: 0, relapses: carried, was: g.state || 'colour' };
  place(g.shells, !!g.reduced);
}

/** Move every shell onto its drift, so a fresh board already has them where they belong. */
function place(st, reduced) {
  for (const sh of st.list) { const p = posAt(sh, st.t, reduced); sh.x = p.x; sh.y = p.y; }
}

/** The room loses a little colour. Never below zero, and never in GREY (there is none to take). */
function loseSat(g, v) {
  if (g.state !== 'colour') return;
  g.sat = Math.max(0, (Number(g.sat) || 0) - v);
}
/** The release hands a little back, under the act's cap, exactly as addSat is clamped. */
function gainSat(g, v) {
  if (g.state !== 'colour') return;
  const cap = Number.isFinite(g.storyCap) ? g.storyCap : 1;
  const sat = Number(g.sat) || 0;
  g.sat = Math.min(Math.max(cap, sat), sat + v);
}

/** The ball went through one. It comes out heavy, and the room goes a shade quieter. */
function touch(g, ctx, st, sh, b) {
  sh.hp--;
  b.shellSlow = SLOW_S;
  loseSat(g, SAT_COST);
  ctx.emit('shellTouch', { x: b.x, y: b.y, hp: Math.max(0, sh.hp) });
  if (sh.hp > 0) return;
  const left = st.list.reduce((n, s) => n + (s !== sh && s.hp > 0 ? 1 : 0), 0);
  gainSat(g, SAT_GIVE);
  ctx.emit('shellPop', { x: sh.x, y: sh.y, left });
}

/** Who is inside whom this frame. Entering is the event; sitting inside is not. */
function touchPass(g, ctx, st) {
  for (const sh of st.list) {
    const was = sh.inside || [];
    const now = [];
    for (const b of g.balls || []) {
      if (!b || b.lost || b.falling || b.stuck) continue;
      const dx = b.x - sh.x, dy = b.y - sh.y;
      if (dx * dx + dy * dy > sh.r * sh.r) continue;   // the centre has to be inside the hollow
      now.push(b);
      if (was.indexOf(b) >= 0) continue;              // still the same visit
      if (sh.hp > 0) touch(g, ctx, st, sh, b);
    }
    sh.inside = now;
  }
  st.list = st.list.filter(sh => sh.hp > 0);
}

/**
 * The weight. The game renormalises a ball's speed on every bounce, so the slow is
 * re-applied each frame and eases out over the beat. The last frame hands the ball
 * back at the board's own speed: nothing is kept, and nothing is ever stopped.
 */
function slowPass(g, dt) {
  for (const b of g.balls || []) {
    if (!b || !(b.shellSlow > 0)) continue;
    b.shellSlow = Math.max(0, b.shellSlow - dt);
    if (b.stuck) continue;
    const len = Math.hypot(b.vx || 0, b.vy || 0);
    if (!(len > 0)) continue;
    const k = b.shellSlow / SLOW_S;                   // 1 at the touch, 0 a beat later
    const mul = SLOW_MUL + (1 - SLOW_MUL) * (1 - k);
    const want = (g.speed > 0 ? g.speed : len) * mul;
    b.vx = b.vx / len * want; b.vy = b.vy / len * want;
  }
}

export function update(g, dt, ctx) {
  const st = stateOf(g);
  const step = Number(dt) || 0;
  st.t += step;
  place(st, !!g.reduced);

  /* A relapse this run leaves one more old self behind, if there is room for it. */
  if (st.was === 'colour' && g.state === 'grey') {
    st.relapses++;
    if (st.list.length < MAX_SHELLS) {
      const sh = mkShell(st.list.length, MAX_SHELLS, (ctx && ctx.w) || g.w || 1280, (ctx && ctx.h) || g.h || 720, (ctx && ctx.rng) || (() => 0.5));
      const p = posAt(sh, st.t, !!g.reduced); sh.x = p.x; sh.y = p.y;
      st.list.push(sh);
    }
  }
  st.was = g.state;

  // The touch comes first, so the weight it hands out is already on the ball when the slow runs.
  if (g.state === 'colour') touchPass(g, ctx, st);    // GREY is payload-free: nothing to take, nothing drawn
  slowPass(g, step);
}

export default { id, build, update };
