/* ============================================================================
 * stations/breakout/twists/justone.js - the pure sim half of the twist.
 * Twist: Just One (The Ward). A red brick drops a treat. Catching it is the
 * player's choice: six seconds of the game's own fireball, and then the
 * comedown - a forced relapse, a narrower paddle, and a grey that is longer
 * every time. Missing the treat costs nothing at all.
 *
 * Pure sim: no DOM, no clock, no Math.random. Every number here is authored.
 * The comedown takes the high back and nothing the player had before it
 * (AGENTS.md: failure subtracts, it never punishes).
 * ==========================================================================*/

export const id = 'justone';

/** The treat's own high: six seconds, not the eight a plain fireball pickup buys. */
export const FIREBALL_S = 6;
/** How wide the paddle is while the comedown runs (game.js reads g.paddleScale every frame). */
export const PADDLE_SCALE = 0.62;
/** Bricks to break out of the comedown's grey: 6 + 3 per treat taken, never past 18. */
export const COMEDOWN_BASE = 6, COMEDOWN_PER_DOSE = 3, COMEDOWN_CAP = 18;

/** The price of the next comedown, in grey bricks. */
export function comedownCount(doses) {
  const n = Math.max(0, Math.floor(Number(doses) || 0));
  return Math.min(COMEDOWN_CAP, COMEDOWN_BASE + COMEDOWN_PER_DOSE * n);
}

/**
 * The tab, per game object and never module-level: two games in one process
 * (a test, a dev page) must not share a dose count.
 *   doses    treats taken on this board - the HUD mark and the price both read it
 *   flights  the treat drops still in the air
 *   want     the grey the comedown is waiting to buy, 0 when nothing is owed
 *   landed   the comedown's grey has actually arrived
 */
export function stateOf(g) {
  if (!g.justone) g.justone = { doses: 0, flights: [], cancel: null, want: 0, landed: false, applied: false, high: 0 };
  return g.justone;
}

/** Treats taken, for the HUD mark and the renderer. Safe on any snapshot. */
export const doseCount = snap => ((snap && snap.justone ? snap.justone.doses : 0) | 0);

/** A fresh board is a fresh tab. */
export function build(g, ctx) {
  g.justone = { doses: 0, flights: [], cancel: null, want: 0, landed: false, applied: false, high: 0 };
  g.paddleScale = 1;
}

/**
 * A treat brick broke. game.js has already called powers.drop(br), so the real
 * fireball drop is the last one in the list: tag it and it is a treat from here
 * on. In GREY nothing drops at all (payload-free by design), so nothing is owed.
 */
export function onBreak(g, br, ball, ctx) {
  if (!br || !br.treat) return;
  const drops = g.power && g.power.drops;
  if (!drops || !drops.length) return;
  const d = drops[drops.length - 1];
  if (!d || d.treat || d.kind !== 'fireball') return;
  if (Math.abs(d.x - (br.x + br.w / 2)) > 1) return;          // not this brick's drop
  d.treat = true;
  stateOf(g).flights.push(d);
  ctx.emit('treatDrop', { x: d.x, y: d.y });
}

/**
 * The paddle caught a fireball. powerups.js rebuilds its drop list only after
 * the whole sweep, so the caught drop is still in the array here: match it the
 * way the catch itself did, by where it is over the paddle.
 */
export function onCatch(g, drop, ctx) {
  if (!drop || drop.kind !== 'fireball') return;
  const st = stateOf(g), p = g.paddle || { x: 0, y: 0, w: 0, h: 0 };
  const top = p.y - p.h / 2, px = Number.isFinite(drop.x) ? drop.x : p.x;
  let best = null;
  for (const d of st.flights) {
    if (d.done || d.missed) continue;
    if (d.y + 12 < top) continue;                             // still on its way down
    if (Math.abs(d.x - px) > p.w / 2 + 18) continue;          // fell past a tip
    if (!best || d.y > best.y) best = d;                      // the lowest one is the one on the paddle
  }
  if (!best) return;
  best.done = 'caught';
  take(g, ctx);
}

/** Take one. The high starts over, the bill gets bigger, and one comedown is owed. */
function take(g, ctx) {
  const st = stateOf(g);
  st.doses++;
  st.high = FIREBALL_S;
  if (g.power) g.power.fireball = FIREBALL_S;                 // six, not the pickup's eight
  if (typeof st.cancel === 'function') st.cancel();           // a second treat pushes the comedown back, it never doubles it
  st.cancel = ctx.schedule(FIREBALL_S, comedown);
  ctx.emit('treatCatch', { doses: st.doses });
}

/** The high runs out. Ask for the relapse; `update` sees it land and sets the price. */
export function comedown(g, ctx) {
  const st = stateOf(g);
  st.cancel = null;
  st.want = comedownCount(st.doses);
  st.applied = false;
  st.landed = g.state === 'grey';                             // already grey: this one only lengthens it
  g.paddleScale = PADDLE_SCALE;
  ctx.emit('comedown', { doses: st.doses, count: st.want });
  if (st.landed) applyPrice(g, st);
  else ctx.startRelapse();
}

/** The grey is here: this is how many bricks it takes. */
function applyPrice(g, st) {
  st.applied = true;
  g.breakoutN = st.landed && (g.greyBricks || 0) > 0 ? Math.max(g.breakoutN || 0, st.want) : st.want;
  g.fractures = Math.min(1, (g.greyBricks || 0) / g.breakoutN);
}

export function update(g, dt, ctx) {
  const st = stateOf(g);
  st.high = Math.max(0, (g.power && g.power.fireball) || 0);

  /* A treat that reached the floor: it says so once, and it costs nothing. */
  if (st.flights.length) {
    const drops = (g.power && g.power.drops) || [];
    for (const d of st.flights) {
      if (d.done) continue;
      if (d.missed || drops.indexOf(d) < 0) { d.done = 'missed'; ctx.emit('treatMiss', {}); }
    }
    st.flights = st.flights.filter(d => !d.done || drops.indexOf(d) >= 0);
  }

  if (!st.want) return;
  if (g.state === 'grey') {
    if (!st.applied) applyPrice(g, st);
    st.landed = true;
    return;
  }
  /* The relapse could not start this frame (a transition was already running, or
   * the ball was still up). Keep asking until it takes; asking twice is harmless. */
  if (!st.landed) { if (!g.transition) ctx.startRelapse(); return; }
  /* Out the other side: the paddle comes back and the next relapse is the game's own again. */
  if (!g.transition) { g.paddleScale = 1; st.want = 0; st.applied = false; st.landed = false; }
}

export default { id, build, onBreak, onCatch, update };
