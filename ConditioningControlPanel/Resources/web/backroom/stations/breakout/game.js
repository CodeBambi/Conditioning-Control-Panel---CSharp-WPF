/* ============================================================================
 * stations/breakout/game.js - the sim. DOM-free so `node --test` can drive it.
 *
 * One variable, saturation, drives everything (SPEC.md). Two states: COLOUR and
 * GREY. Lose the last ball in COLOUR -> RELAPSE (world grey, ball becomes the
 * OLD SELF ghost). Break `breakoutN` bricks in GREY -> BREAKOUT (100 ms freeze,
 * then the world snaps back to the saturation it had). Nobody ever loses.
 *
 * createGame({ w, h, rng, audio, onEvent }) -> { step(dt, input), snapshot(), ... }
 * Events (onEvent(name, data)): brick, paddle, wallhit, gif, capture, spiral,
 * split, wall, relapse, breakout, crack, lost, launch.
 * ==========================================================================*/

export const W = 480, H = 720;
export const RUNG_AT = [0, 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80, 0.90];
export const RUNG_NAMES = ['grey', 'colour', 'trail', 'particles', 'jelly', 'shake', 'words', 'spirals', 'colliders', 'crack'];
export const BRICK = { cols: 10, rows: 6, w: 42, h: 18, gap: 4, top: 80 };
export const PADDLE = { baseW: 90, h: 14 };
export const BALL_R = 8;
export const MAX_BALLS = 3;
const STEP = 1 / 120;
const TAU = Math.PI * 2;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;

/** The ten juice rungs as booleans. `force[i]` (true/false) overrides the threshold; null/undefined = auto. */
export function rungsFor(sat, state, force) {
  const out = new Array(10);
  for (let i = 0; i < 10; i++) {
    const f = force ? force[i] : undefined;
    out[i] = (f === true || f === false) ? f : (i === 0 || (state !== 'grey' && sat >= RUNG_AT[i]));
  }
  return out;
}

export function createGame({ w = W, h = H, rng = Math.random, audio = null, onEvent = () => {}, breakoutN = 12,
  saturation = 0.05, speedScale = 0.55 } = {}) {
  const g = {
    w, h, breakoutN, speedScale,
    sat: saturation, savedSat: saturation, state: 'colour', greyBricks: 0,
    force: {}, rungs: rungsFor(saturation, 'colour', null), speed: 0,
    paddle: { x: w / 2, w: PADDLE.baseW, h: PADDLE.h, y: h - 40, stretch: 0 },
    balls: [], bricks: [], colliders: [], well: null,
    stats: { bricks: 0, walls: 0, sp: 0 }, combo: 0, time: 0, freeze: 0, pendingBreakout: false, breakoutAt: null,
    wallAge: 1, wobble: { side: '', t: 0 }, crackFired: false, nextCollider: 0, nextWell: 4, acc: 0, launchTimer: 0,
  };
  const emit = (name, data) => { try { onEvent(name, data); } catch (e) { /* the listener's problem */ } };
  const au = (fn, ...args) => { try { if (audio && typeof audio[fn] === 'function') audio[fn](...args); } catch (e) { /* audio is optional */ } };
  const spb = () => (audio && audio.beat && audio.beat.spb) || 60 / 96;
  // One beat bottom-to-top at saturation 0, one and a half at 1 (breathing pace); never below a floor.
  const targetSpeed = () => Math.max(220, g.speedScale * h / (spb() * (1 + 0.5 * g.sat)));

  /* ------------------------------------------------------------ wall */
  function buildWall() {
    const bricks = [];
    const x0 = (w - (BRICK.cols * BRICK.w + (BRICK.cols - 1) * BRICK.gap)) / 2;
    for (let row = 0; row < BRICK.rows; row++) for (let col = 0; col < BRICK.cols; col++) {
      bricks.push({ x: x0 + col * (BRICK.w + BRICK.gap), y: BRICK.top + row * (BRICK.h + BRICK.gap), w: BRICK.w, h: BRICK.h,
        alive: true, row, col, gif: rng() < 0.15 ? Math.floor(rng() * 8) : -1, split: rng() < 0.05, jelly: 0 });
    }
    g.bricks = bricks;
  }
  const bricksAlive = () => g.bricks.some(b => b.alive);

  /* ------------------------------------------------------------ balls */
  function newBall(ghost) {
    return { x: g.paddle.x, y: g.paddle.y - g.paddle.h / 2 - BALL_R, vx: 0, vy: 0, r: BALL_R, ghost: !!ghost, stuck: true,
      orbit: null, lost: false, trail: [] };
  }
  function respawn(ghost) { g.balls = [newBall(ghost)]; g.launchTimer = 0; }
  function launch(b) {
    const a = (rng() < 0.5 ? -1 : 1) * (0.25 + rng() * 0.3);
    const s = targetSpeed();
    b.vx = Math.sin(a) * s; b.vy = -Math.cos(a) * s; b.stuck = false;
    emit('launch', { x: b.x, y: b.y });
  }
  function normalise(b) {
    const s = targetSpeed();
    let len = Math.hypot(b.vx, b.vy) || 1;
    b.vx = b.vx / len * s; b.vy = b.vy / len * s;
    // Never let it settle into a horizontal shuttle.
    const minVy = 0.25 * s;
    if (Math.abs(b.vy) < minVy) { b.vy = (b.vy < 0 ? -1 : 1) * minVy; len = Math.hypot(b.vx, b.vy); b.vx = b.vx / len * s; b.vy = b.vy / len * s; }
  }

  /* ------------------------------------------------------------ saturation and states */
  function addSat(v) {
    if (g.state !== 'colour') return;
    g.sat = Math.min(1, g.sat + v);
    au('setSaturation', g.sat);
    if (g.sat >= RUNG_AT[9] && !g.crackFired) { g.crackFired = true; au('crack'); emit('crack', { sat: g.sat }); }
  }
  function relapse(at) {
    g.savedSat = g.sat; g.sat = 0; g.state = 'grey'; g.greyBricks = 0; g.combo = 0;
    g.colliders = []; g.well = null;
    au('relapse'); au('setState', 'grey'); au('setSaturation', 0);
    emit('relapse', { x: at ? at.x : w / 2, y: at ? at.y : h - 60 });
    respawn(true);
  }
  function startBreakout(b) {
    if (g.state !== 'grey' || g.pendingBreakout) return;
    g.pendingBreakout = true; g.freeze = 0.1; g.acc = 0;
    g.breakoutAt = { x: b ? b.x : w / 2, y: b ? b.y : h / 2 };
  }
  function completeBreakout() {
    g.pendingBreakout = false; g.state = 'colour'; g.sat = g.savedSat; g.greyBricks = 0;
    for (const b of g.balls) b.ghost = false;
    au('breakout'); au('setState', 'colour'); au('setSaturation', g.sat);
    emit('breakout', { x: g.breakoutAt.x, y: g.breakoutAt.y, sat: g.sat });
  }
  function lostAll() {
    const last = g.balls[0];
    if (g.state === 'colour') relapse(last);
    else { emit('lost', { x: last ? last.x : w / 2 }); respawn(true); }
  }

  /* ------------------------------------------------------------ bricks */
  function breakBrick(br, ball) {
    if (!br.alive) return;
    br.alive = false; g.stats.bricks++; g.combo++;
    au('hit', 'brick', { combo: g.combo, x: br.x / w });
    for (const o of g.bricks) if (o.alive && Math.abs(o.row - br.row) <= 1 && Math.abs(o.col - br.col) <= 1) o.jelly = 1;
    const grey = g.state === 'grey';
    emit('brick', { x: br.x + br.w / 2, y: br.y + br.h / 2, row: br.row, col: br.col, gif: br.gif, ghost: grey, sat: g.sat });
    if (grey) { g.greyBricks++; if (g.greyBricks >= g.breakoutN) startBreakout(ball); }
    else {
      addSat(0.012);
      if (br.split && ball && g.balls.length < MAX_BALLS) split(ball, br);
    }
    if (!bricksAlive()) wallCleared();
  }
  function split(ball, br) {
    const nb = { ...newBall(false), x: br.x + br.w / 2, y: br.y + br.h / 2, vx: -ball.vx, vy: ball.vy, stuck: false, trail: [] };
    g.balls.push(nb);
    au('split');
    emit('split', { x: nb.x, y: nb.y });
  }
  function wallCleared() {
    g.stats.walls++; g.stats.sp = Math.min(20, g.stats.sp + 1);
    addSat(0.1);
    au('wallCleared');
    buildWall(); g.wallAge = 0;
    emit('wall', { walls: g.stats.walls, sp: g.stats.sp });
  }

  /* ------------------------------------------------------------ payloads: colliders and wells */
  function spawnCollider() {
    const r = 28;
    const a = rng() * TAU;
    g.colliders.push({ x: lerp(r + 20, w - r - 20, rng()), y: lerp(250, 540, rng()), r, vx: Math.cos(a) * 18, vy: Math.sin(a) * 18,
      hits: 0, pulse: 0, alpha: 0, fading: false, gif: Math.floor(rng() * 8), age: 0 });
    const mean = lerp(8, 5, clamp((g.sat - 0.8) / 0.2, 0, 1));
    g.nextCollider = g.time + mean * (0.5 + rng());
  }
  function updateColliders(dt) {
    for (const c of g.colliders) {
      c.age += dt;
      c.x += c.vx * dt; c.y += c.vy * dt;
      if (c.x < c.r) { c.x = c.r; c.vx = Math.abs(c.vx); } else if (c.x > w - c.r) { c.x = w - c.r; c.vx = -Math.abs(c.vx); }
      if (c.y < 240) { c.y = 240; c.vy = Math.abs(c.vy); } else if (c.y > 560) { c.y = 560; c.vy = -Math.abs(c.vy); }
      c.pulse = Math.max(0, c.pulse - dt * 3);
      c.alpha = c.fading ? c.alpha - dt / 0.6 : Math.min(1, c.alpha + dt * 2);
    }
    g.colliders = g.colliders.filter(c => c.alpha > 0);
  }
  function spawnWell() {
    g.well = { x: lerp(110, w - 110, rng()), y: lerp(280, 520, rng()), r: 70, pull: 110, age: 0, ttl: 6, rot: 0, used: false, fade: 1, captured: null };
    g.nextWell = g.time + 12;
  }
  function updateWell(dt) {
    const s = g.well; if (!s) return;
    s.rot += dt * 1.6;
    if (s.captured) return;
    s.age += dt;
    if (s.age >= s.ttl) { s.fade -= dt / 0.4; if (s.fade <= 0) g.well = null; }
  }
  function capture(b, s) {
    const rx = b.x - s.x, ry = b.y - s.y;
    const cross = rx * b.vy - ry * b.vx;
    b.orbit = { r: clamp(Math.hypot(rx, ry), 40, 100), a: Math.atan2(ry, rx), dir: cross < 0 ? -1 : 1, turns: 1 + rng(), done: 0 };
    s.used = true; s.captured = b;
    emit('capture', { x: s.x, y: s.y });
  }
  function orbitStep(b, dt) {
    const s = g.well, o = b.orbit;
    if (!s || s.captured !== b) { b.orbit = null; return; }
    const speed = targetSpeed(), wA = speed / o.r;
    o.a += o.dir * wA * dt; o.done += wA * dt;
    b.x = s.x + Math.cos(o.a) * o.r; b.y = s.y + Math.sin(o.a) * o.r;
    b.vx = -Math.sin(o.a) * o.dir * speed; b.vy = Math.cos(o.a) * o.dir * speed;
    if (o.done >= o.turns * TAU) {
      b.orbit = null; s.captured = null; s.age = s.ttl;
      addSat(0.05); au('hit', 'spiral', { combo: g.combo, x: b.x / w });
      emit('spiral', { x: s.x, y: s.y });
    }
  }

  /* ------------------------------------------------------------ collisions */
  function wallHit(side, b) {
    g.wobble = { side, t: 1 };
    au('hit', 'wall', { combo: g.combo, x: b.x / w });
    emit('wallhit', { side, x: b.x, y: b.y });
  }
  function collideWalls(b) {
    if (b.x - b.r < 0) { b.x = b.r; b.vx = Math.abs(b.vx); wallHit('left', b); }
    else if (b.x + b.r > w) { b.x = w - b.r; b.vx = -Math.abs(b.vx); wallHit('right', b); }
    if (b.y - b.r < 0) { b.y = b.r; b.vy = Math.abs(b.vy); wallHit('top', b); }
    if (b.y - b.r > h) b.lost = true;
  }
  function collidePaddle(b) {
    const p = g.paddle;
    if (b.vy <= 0) return;
    const top = p.y - p.h / 2;
    if (b.y + b.r < top || b.y - b.r > p.y + p.h / 2 || Math.abs(b.x - p.x) > p.w / 2 + b.r) return;
    const t = clamp((b.x - p.x) / (p.w / 2), -1, 1);
    const a = t * (Math.PI / 3);                      // up to 60 degrees off vertical at the tips
    const s = targetSpeed();
    b.vx = Math.sin(a) * s; b.vy = -Math.cos(a) * s; b.y = top - b.r;
    g.combo = 0; p.stretch = 1;
    au('hit', 'paddle', { combo: 0, x: b.x / w });
    emit('paddle', { x: b.x, t });
  }
  function collideBricks(b, px, py) {
    if (g.wallAge < 0.5) return;                      // a wall still tweening in is not solid yet
    for (const br of g.bricks) {
      if (!br.alive) continue;
      const cx = clamp(b.x, br.x, br.x + br.w), cy = clamp(b.y, br.y, br.y + br.h);
      const dx = b.x - cx, dy = b.y - cy;
      if (dx * dx + dy * dy >= b.r * b.r) continue;
      // Which face: the side the ball came from, else the shallower penetration.
      const fromX = px < br.x || px > br.x + br.w, fromY = py < br.y || py > br.y + br.h;
      if (fromX && !fromY) { b.vx = px < br.x ? -Math.abs(b.vx) : Math.abs(b.vx); b.x = px < br.x ? br.x - b.r : br.x + br.w + b.r; }
      else if (fromY || Math.abs(dy) >= Math.abs(dx)) { b.vy = py < br.y ? -Math.abs(b.vy) : Math.abs(b.vy); b.y = py < br.y ? br.y - b.r : br.y + br.h + b.r; }
      else { b.vx = dx < 0 ? -Math.abs(b.vx) : Math.abs(b.vx); b.x = dx < 0 ? br.x - b.r : br.x + br.w + b.r; }
      breakBrick(br, b);
      return;
    }
  }
  function collideColliders(b) {
    for (const c of g.colliders) {
      if (c.fading) continue;
      const dx = b.x - c.x, dy = b.y - c.y, d = Math.hypot(dx, dy) || 0.001, rr = b.r + c.r;
      if (d >= rr) continue;
      const nx = dx / d, ny = dy / d, dot = b.vx * nx + b.vy * ny;
      if (dot < 0) { b.vx -= 2 * dot * nx; b.vy -= 2 * dot * ny; }
      b.x = c.x + nx * rr; b.y = c.y + ny * rr;
      c.hits++; c.pulse = 1; if (c.hits >= 3) c.fading = true;
      g.combo++; addSat(0.03);
      au('hit', 'gif', { combo: g.combo, x: b.x / w });
      if (g.rungs[5]) { g.freeze = Math.max(g.freeze, 0.04); g.acc = 0; }
      emit('gif', { x: c.x, y: c.y, r: c.r, hits: c.hits });
      return;
    }
  }
  function steerToBrick(b, dt) {
    if (b.vy >= 0) return;
    let best = null, bd = Infinity;
    for (const br of g.bricks) if (br.alive) { const d = Math.hypot(br.x + br.w / 2 - b.x, br.y + br.h / 2 - b.y); if (d < bd) { bd = d; best = br; } }
    if (!best) return;
    const want = Math.atan2(best.y + best.h / 2 - b.y, best.x + best.w / 2 - b.x), cur = Math.atan2(b.vy, b.vx);
    let d = want - cur; while (d > Math.PI) d -= TAU; while (d < -Math.PI) d += TAU;
    const turn = clamp(d, -1.2 * dt, 1.2 * dt), s = Math.hypot(b.vx, b.vy);
    b.vx = Math.cos(cur + turn) * s; b.vy = Math.sin(cur + turn) * s;
  }
  function moveBall(b, dt) {
    if (b.stuck) { b.x = g.paddle.x; b.y = g.paddle.y - g.paddle.h / 2 - b.r; return; }
    if (b.orbit) { orbitStep(b, dt); pushTrail(b); return; }
    const s = g.well;
    if (s && !s.used && g.rungs[7] && g.state === 'colour' && !b.ghost && Math.hypot(b.x - s.x, b.y - s.y) < s.pull) { capture(b, s); return; }
    normalise(b);
    if (g.rungs[9] && g.state === 'colour' && !b.ghost) steerToBrick(b, dt);
    const speed = Math.hypot(b.vx, b.vy), n = Math.max(1, Math.ceil(speed * dt / b.r)), ds = dt / n;
    for (let i = 0; i < n && !b.lost && g.freeze <= 0 && !b.orbit; i++) {
      const px = b.x, py = b.y;
      b.x += b.vx * ds; b.y += b.vy * ds;
      collideWalls(b);
      collidePaddle(b);
      collideBricks(b, px, py);
      if (g.state === 'colour') collideColliders(b);
    }
    pushTrail(b);
  }
  function pushTrail(b) { b.trail.push(b.x, b.y); if (b.trail.length > 80) b.trail.splice(0, 2); }

  /* ------------------------------------------------------------ step */
  function movePaddle(dt, input) {
    const p = g.paddle;
    if (typeof input.x === 'number' && !Number.isNaN(input.x)) p.x = input.x;
    else if (input.left) p.x -= 640 * dt;
    else if (input.right) p.x += 640 * dt;
    p.x = clamp(p.x, p.w / 2, w - p.w / 2);
  }
  function tick(dt, input) {
    g.time += dt; g.wallAge += dt;
    g.rungs = rungsFor(g.sat, g.state, g.force);
    g.speed = targetSpeed();
    g.paddle.w = PADDLE.baseW * (1 + 0.6 * g.sat);
    g.paddle.stretch = Math.max(0, g.paddle.stretch - dt * 4);
    g.wobble.t = Math.max(0, g.wobble.t - dt * 2);
    for (const br of g.bricks) if (br.jelly > 0) br.jelly = Math.max(0, br.jelly - dt * 3);
    if (g.state === 'colour') {
      if (g.rungs[8] && g.colliders.length < 3 && g.time >= g.nextCollider) spawnCollider();
      if (g.rungs[7] && !g.well && g.time >= g.nextWell) spawnWell();
    }
    updateColliders(dt); updateWell(dt);
    if (g.balls.some(b => b.stuck)) {
      g.launchTimer += dt;
      if (input.launch || g.launchTimer >= 1.2) { for (const b of g.balls) if (b.stuck) launch(b); g.launchTimer = 0; }
    }
    for (const b of g.balls) { if (g.freeze > 0) break; moveBall(b, dt); }
    if (g.balls.some(b => b.lost)) {
      g.balls = g.balls.filter(b => !b.lost);
      if (!g.balls.length) lostAll();
    }
  }
  function step(dt, input = {}) {
    dt = Math.min(Math.max(0, dt), 0.1);
    if (g.freeze > 0) {
      g.freeze -= dt;
      if (g.freeze <= 0) { g.freeze = 0; if (g.pendingBreakout) completeBreakout(); }
      return;
    }
    movePaddle(dt, input);
    g.acc += dt;
    let guard = 0;
    while (g.acc >= STEP && guard++ < 24) { g.acc -= STEP; tick(STEP, input); if (g.freeze > 0) { g.acc = 0; break; } }
  }

  buildWall();
  respawn(false);
  au('setSaturation', g.sat); au('setState', 'colour');

  return {
    step,
    snapshot: () => g,
    /* dev and test hooks */
    setSaturation(s) { g.sat = clamp(Number(s) || 0, 0, 1); if (g.state === 'grey') g.savedSat = g.sat, g.sat = 0; au('setSaturation', g.sat); },
    setForce(i, v) { g.force[i] = v; g.rungs = rungsFor(g.sat, g.state, g.force); },
    clearForce() { g.force = {}; g.rungs = rungsFor(g.sat, g.state, g.force); },
    setBreakoutN(n) { g.breakoutN = Math.max(1, Math.floor(Number(n) || 12)); },
    setSpeedScale(s) { g.speedScale = clamp(Number(s) || 0.55, 0.2, 3); },
    relapseNow() { if (g.state === 'colour') { g.balls = []; lostAll(); } },
    breakoutNow() { startBreakout(g.balls[0]); },
    breakBrick(i) { const br = g.bricks[i]; if (br) breakBrick(br, g.balls[0]); },
    loseBall() { g.balls = []; lostAll(); },
    launchNow() { for (const b of g.balls) if (b.stuck) launch(b); },
  };
}
