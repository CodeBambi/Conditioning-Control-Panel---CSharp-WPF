/* ============================================================================
 * stations/breakout/game.js - the sim. DOM-free so `node --test` can drive it.
 *
 * One variable, saturation, drives everything (SPEC.md). Two states: COLOUR and
 * GREY. Lose the last ball in COLOUR -> RELAPSE (0.7 s of slow motion while the
 * ball falls under the paddle, then the grey cut, ball becomes the OLD SELF
 * ghost). Break `breakoutN` bricks in GREY -> BREAKOUT (0.3 s rewind, 100 ms
 * freeze, then the world snaps back to the saturation it had). Nobody loses.
 *
 * createGame({ w, h, rng, audio, onEvent, words }) -> { step(dt, input), snapshot(), ... }
 * Events (onEvent(name, data)): brick, hit, paddle, wallhit, gif, capture, spiral,
 * split, wall, relapse, relapseStart, breakout, breakoutStart, crack, lost,
 * launch, perfect, nearMiss, jackpot, mantra, shatterWall.
 * ==========================================================================*/

export const W = 480, H = 720;
export const RUNG_AT = [0, 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80, 0.90];
export const RUNG_NAMES = ['grey', 'colour', 'trail', 'particles', 'jelly', 'shake', 'words', 'spirals', 'colliders', 'crack'];
export const BRICK = { cols: 10, rows: 6, w: 42, h: 18, gap: 4, top: 80 };
export const PADDLE = { baseW: 90, h: 14 };
export const BALL_R = 8;
export const MAX_BALLS = 3;
export const DEFAULT_WORDS = ['DROP', 'RELAX', 'LET GO', 'SINK'];
export const ROW_COLORS = ['#ff5fa2', '#ff8ac4', '#c86bff', '#7fd6ff', '#ffd166', '#7bffb0'];
const STEP = 1 / 120;
const TAU = Math.PI * 2;
const RELAPSE_S = 0.7, BREAKOUT_S = 0.3, PUSH_S = 0.12, RING_S = 0.03, NEAR_MISS_PX = 6;
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const lerp = (a, b, t) => a + (b - a) * t;

/* 5x5 pixel font, A-Z, one string of 25 bits per glyph (row major). Space is 2 blank columns. */
export const FONT5 = {
  A: '0111010001111111000110001', B: '1111010001111101000111110', C: '0111110000100001000001111',
  D: '1111010001100011000111110', E: '1111110000111101000011111', F: '1111110000111101000010000',
  G: '0111110000100111000101111', H: '1000110001111111000110001', I: '1111100100001000010011111',
  J: '0011100010000101001001100', K: '1000110010111001001010001', L: '1000010000100001000011111',
  M: '1000111011101011000110001', N: '1000111001101011001110001', O: '0111010001100011000101110',
  P: '1111010001111101000010000', Q: '0111010001101011001001101', R: '1111010001111101001010001',
  S: '0111110000011100000111110', T: '1111100100001000010000100', U: '1000110001100011000101110',
  V: '1000110001100010101000100', W: '1000110001101011101110001', X: '1000101010001000101010001',
  Y: '1000101010001000010000100', Z: '1111100010001000100011111',
};

/** The ten juice rungs as booleans. `force[i]` (true/false) overrides the threshold; null/undefined = auto. */
export function rungsFor(sat, state, force) {
  const out = new Array(10);
  for (let i = 0; i < 10; i++) {
    const f = force ? force[i] : undefined;
    out[i] = (f === true || f === false) ? f : (i === 0 || (state !== 'grey' && sat >= RUNG_AT[i]));
  }
  return out;
}

/** Lay a word out as lit cells: returns { cols, rows: 5, cells: [{col,row,letter}] }. Up to 8 letters. */
export function layoutWord(word) {
  const text = String(word || '').toUpperCase().slice(0, 8);
  const cells = [];
  let col = 0;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i], glyph = FONT5[ch];
    if (!glyph) { col += 2 + 1; continue; }
    for (let r = 0; r < 5; r++) for (let c = 0; c < 5; c++) if (glyph[r * 5 + c] === '1') cells.push({ col: col + c, row: r, letter: ch });
    col += 5 + 1;
  }
  return { cols: Math.max(1, col - 1), rows: 5, cells };
}

export function createGame({ w = W, h = H, rng = Math.random, audio = null, onEvent = () => {}, breakoutN = 12,
  saturation = 0.15, speedScale = 0.55, words = DEFAULT_WORDS } = {}) {
  const g = {
    w, h, breakoutN, speedScale,
    sat: saturation, savedSat: saturation, state: 'colour', greyBricks: 0,
    force: {}, rungs: rungsFor(saturation, 'colour', null), speed: 0,
    paddle: { x: w / 2, w: PADDLE.baseW, h: PADDLE.h, y: h - 40, stretch: 0, tug: 0 },
    balls: [], bricks: [], colliders: [], well: null,
    stats: { bricks: 0, walls: 0, sp: 0 }, combo: 0, comboBest: 0, time: 0, freeze: 0, pendingBreakout: false, breakoutAt: null,
    wallAge: 1, landRow: 99, wobble: { side: '', t: 0 }, crackFired: false, nextCollider: 0, nextWell: 4, acc: 0, launchTimer: 0,
    // contract v2
    transition: null, timeScale: 1, hitStopMs: 0, smear: null, smearFading: false, fractures: 0, shatterWall: false,
    mantra: null, beatPhase: 0, lastPerfectAt: 0, nearMissT: 0,
    words: (Array.isArray(words) && words.length ? words : DEFAULT_WORDS).map(x => String(x)), wordIx: 0,
  };
  const emit = (name, data) => { try { onEvent(name, data); } catch (e) { /* the listener's problem */ } };
  const au = (fn, ...args) => { try { if (audio && typeof audio[fn] === 'function') audio[fn](...args); } catch (e) { /* audio is optional */ } };
  const spb = () => (audio && audio.beat && audio.beat.spb) || 60 / 96;
  const beatPhase = () => {
    try { if (audio && audio.beat && typeof audio.beat.phase === 'function') { const p = Number(audio.beat.phase(typeof audio.now === 'function' ? audio.now() : 0)); return Number.isFinite(p) ? p : 0; } } catch (e) { /* fine */ }
    return (g.time / spb()) % 1;
  };
  // One beat bottom-to-top at saturation 0, one and a half at 1 (breathing pace); never below a floor.
  const targetSpeed = () => Math.max(220, g.speedScale * h / (spb() * (1 + 0.5 * g.sat)));
  const setTimeScale = (s) => { if (s !== g.timeScale) { g.timeScale = s; au('setTimeScale', s); } };

  /* ------------------------------------------------------------ wall */
  function mkBrick(x, y, bw, bh, row, col, extra) {
    return { x, y, w: bw, h: bh, alive: true, row, col, gif: -1, split: false, jackpot: false, letter: null, color: ROW_COLORS[row % ROW_COLORS.length],
      jelly: 0, jellyIn: 0, push: { dx: 0, dy: 0 }, pushT: 0, push0: { dx: 0, dy: 0 }, ...extra };
  }
  function buildWall() {
    const bricks = [];
    const mantra = (g.stats.walls + 1) % 5 === 0 && g.words.length > 0;
    if (mantra) {
      const word = g.words[g.wordIx++ % g.words.length];
      const lay = layoutWord(word);
      const gap = 2, size = Math.max(6, Math.min(BRICK.w, Math.floor((w - 24 - (lay.cols - 1) * gap) / lay.cols)));
      const x0 = (w - (lay.cols * size + (lay.cols - 1) * gap)) / 2;
      for (const c of lay.cells) bricks.push(mkBrick(x0 + c.col * (size + gap), BRICK.top + c.row * (size + gap), size, size, c.row, c.col, { letter: c.letter }));
      g.mantra = word;
    } else {
      const x0 = (w - (BRICK.cols * BRICK.w + (BRICK.cols - 1) * BRICK.gap)) / 2;
      for (let row = 0; row < BRICK.rows; row++) for (let col = 0; col < BRICK.cols; col++) {
        bricks.push(mkBrick(x0 + col * (BRICK.w + BRICK.gap), BRICK.top + row * (BRICK.h + BRICK.gap), BRICK.w, BRICK.h, row, col,
          { gif: rng() < 0.15 ? Math.floor(rng() * 8) : -1, split: rng() < 0.05 }));
      }
      g.mantra = null;
    }
    if (bricks.length) bricks[Math.floor(rng() * bricks.length)].jackpot = true;   // one hidden jackpot per wall
    g.bricks = bricks;
    g.fractures = 0; g.shatterWall = false;
    if (mantra) emit('mantra', { word: g.mantra });
  }
  const bricksAlive = () => g.bricks.some(b => b.alive);

  /* ------------------------------------------------------------ balls */
  function newBall(ghost) {
    return { x: g.paddle.x, y: g.paddle.y - g.paddle.h / 2 - BALL_R, vx: 0, vy: 0, r: BALL_R, spin: 0, ghost: !!ghost, stuck: true,
      orbit: null, lost: false, falling: false, trail: [] };
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
  function bumpCombo(kind, x, y) {
    g.combo++; g.comboBest = Math.max(g.comboBest, g.combo);
    if (g.state === 'colour' && !g.transition) g.hitStopMs = Math.max(g.hitStopMs, 80 * clamp((g.combo - 1) / 9, 0, 1));
    emit('hit', { kind, combo: g.combo, x, y });
  }
  /** The slow-motion fall: the ball keeps dropping under the paddle for 0.7 s of real time, then the grey cut. */
  function startRelapse(b) {
    if (g.state !== 'colour' || g.transition) return;
    g.transition = { kind: 'relapse', t: 0 };
    g.combo = 0;
    g.smear = { x: b.x, y: Math.min(b.y, h - 4), a: 1 }; g.smearFading = false;
    b.falling = true; b.orbit = null;
    emit('relapseStart', { x: b.x, y: b.y });
  }
  function relapse(at) {
    g.transition = null; setTimeScale(1);
    g.savedSat = g.sat; g.sat = 0; g.state = 'grey'; g.greyBricks = 0; g.combo = 0; g.hitStopMs = 0; g.nearMissT = 0;
    g.colliders = []; g.well = null; g.paddle.tug = 0;
    au('relapse'); au('setState', 'grey'); au('setSaturation', 0);
    emit('relapse', { x: at ? at.x : w / 2, y: at ? at.y : h - 60 });
    respawn(true);
  }
  function startBreakout(b) {
    if (g.state !== 'grey' || g.pendingBreakout) return;
    g.pendingBreakout = true; g.acc = 0; g.hitStopMs = 0;
    g.transition = { kind: 'breakout', t: 0 };
    g.breakoutAt = { x: b ? b.x : w / 2, y: b ? b.y : h / 2 };
    emit('breakoutStart', { x: g.breakoutAt.x, y: g.breakoutAt.y });
  }
  function completeBreakout() {
    g.pendingBreakout = false; g.transition = null; g.state = 'colour'; g.sat = g.savedSat; g.greyBricks = 0;
    for (const b of g.balls) b.ghost = false;
    if (g.smear) g.smearFading = true;
    au('breakout'); au('setState', 'colour'); au('setSaturation', g.sat);
    emit('breakout', { x: g.breakoutAt.x, y: g.breakoutAt.y, sat: g.sat });
  }
  function lostAll(last) {
    if (g.state === 'colour') {
      // Only reached by the dev hook (a live ball starts its own relapse under the paddle).
      const b = last || newBall(false);
      b.stuck = false; b.falling = true; b.x = clamp(b.x, b.r, w - b.r); b.y = Math.max(b.y, g.paddle.y + g.paddle.h + b.r);
      if (b.vy <= 0) { b.vy = targetSpeed(); b.vx = 0; }
      g.balls = [b];
      startRelapse(b);
    } else { emit('lost', { x: last ? last.x : w / 2, y: last ? last.y : h }); respawn(true); }
  }

  /* ------------------------------------------------------------ bricks */
  function pushBrick(br, ball) {
    const len = ball ? Math.hypot(ball.vx, ball.vy) || 1 : 1;
    const ux = ball ? ball.vx / len : 0, uy = ball ? ball.vy / len : -1;
    br.push0 = { dx: ux * 6, dy: uy * 6 }; br.push = { dx: ux * 6, dy: uy * 6 }; br.pushT = 1;
    for (const o of g.bricks) {
      if (!o.alive || o === br) continue;
      const ring = Math.max(Math.abs(o.row - br.row), Math.abs(o.col - br.col));
      if (ring >= 1 && ring <= 3) o.jellyIn = ring * RING_S;
    }
  }
  function breakBrick(br, ball) {
    if (!br.alive) return;
    br.alive = false; g.stats.bricks++;
    const grey = g.state === 'grey';
    const cx = br.x + br.w / 2, cy = br.y + br.h / 2;
    au('hit', 'brick', { combo: g.combo + 1, x: br.x / w });
    pushBrick(br, ball);
    emit('brick', { x: cx, y: cy, w: br.w, h: br.h, color: br.color, row: br.row, col: br.col, gif: br.gif >= 0, gifIndex: br.gif,
      jackpot: br.jackpot, letter: br.letter, ghost: grey, sat: g.sat });
    bumpCombo(br.gif >= 0 ? 'gif' : 'brick', cx, cy);
    if (br.jackpot) { g.stats.sp += 5; emit('jackpot', { x: cx, y: cy, sp: g.stats.sp, ghost: grey }); }
    if (grey) { g.greyBricks++; if (g.greyBricks >= g.breakoutN) startBreakout(ball); }
    else {
      addSat(0.012);
      if (g.crackFired && !g.shatterWall) { g.fractures = Math.min(1, g.fractures + 0.04); if (g.fractures >= 1) { g.shatterWall = true; au('shatterWall'); emit('shatterWall', {}); } }
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
    buildWall(); g.wallAge = 0; g.landRow = 0;
    emit('wall', { walls: g.stats.walls, sp: g.stats.sp, mantra: g.mantra });
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
      addSat(0.05); au('hit', 'spiral', { combo: g.combo + 1, x: b.x / w });
      emit('spiral', { x: s.x, y: s.y });
      bumpCombo('spiral', s.x, s.y);
    }
  }

  /* ------------------------------------------------------------ collisions */
  function wallHit(side, b) {
    g.wobble = { side, t: 1 };
    au('hit', 'wall', { combo: g.combo, x: b.x / w });
    emit('wallhit', { side, x: b.x, y: b.y });
    emit('hit', { kind: 'wall', combo: g.combo, x: b.x, y: b.y });
  }
  function collideWalls(b) {
    if (b.x - b.r < 0) { b.x = b.r; b.vx = Math.abs(b.vx); wallHit('left', b); }
    else if (b.x + b.r > w) { b.x = w - b.r; b.vx = -Math.abs(b.vx); wallHit('right', b); }
    if (b.y - b.r < 0) { b.y = b.r; b.vy = Math.abs(b.vy); wallHit('top', b); }
    if (b.y - b.r > h) b.lost = true;
  }
  function collidePaddle(b, py) {
    const p = g.paddle;
    if (b.vy <= 0 || b.falling) return;
    const top = p.y - p.h / 2;
    if (b.y + b.r < top || b.y - b.r > p.y + p.h / 2) return;
    const gapX = Math.abs(b.x - p.x) - (p.w / 2 + b.r);
    if (gapX > 0) {
      // Crossing the paddle plane just outside a tip: a near miss, once per pass.
      if (gapX <= NEAR_MISS_PX && py + b.r < top && g.state === 'colour' && !g.transition) { g.nearMissT = 0.06; emit('nearMiss', { x: b.x, y: b.y }); }
      return;
    }
    const t = clamp((b.x - p.x) / (p.w / 2), -1, 1);
    const a = t * (Math.PI / 3);                      // up to 60 degrees off vertical at the tips
    const s = targetSpeed();
    b.vx = Math.sin(a) * s; b.vy = -Math.cos(a) * s; b.y = top - b.r;
    g.combo = 0; p.stretch = 1;
    au('hit', 'paddle', { combo: 0, x: b.x / w });
    emit('paddle', { x: b.x, t });
    emit('hit', { kind: 'paddle', combo: 0, x: b.x, y: b.y });
    const ph = g.beatPhase;
    if (Math.min(ph, 1 - ph) <= 0.08) { g.lastPerfectAt = g.time * 1000; addSat(0.02); au('perfect'); emit('perfect', { x: b.x, y: b.y }); }
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
      const dir = { vx: b.vx, vy: b.vy };
      if (fromX && !fromY) { b.vx = px < br.x ? -Math.abs(b.vx) : Math.abs(b.vx); b.x = px < br.x ? br.x - b.r : br.x + br.w + b.r; }
      else if (fromY || Math.abs(dy) >= Math.abs(dx)) { b.vy = py < br.y ? -Math.abs(b.vy) : Math.abs(b.vy); b.y = py < br.y ? br.y - b.r : br.y + br.h + b.r; }
      else { b.vx = dx < 0 ? -Math.abs(b.vx) : Math.abs(b.vx); b.x = dx < 0 ? br.x - b.r : br.x + br.w + b.r; }
      breakBrick(br, { ...b, vx: dir.vx, vy: dir.vy });
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
      addSat(0.03);
      au('hit', 'gif', { combo: g.combo + 1, x: b.x / w });
      if (g.rungs[5]) { g.freeze = Math.max(g.freeze, 0.04); g.acc = 0; }
      emit('gif', { x: c.x, y: c.y, r: c.r, hits: c.hits });
      bumpCombo('gif', c.x, c.y);
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
    if (s && !s.used && g.rungs[7] && g.state === 'colour' && !b.ghost && !b.falling && Math.hypot(b.x - s.x, b.y - s.y) < s.pull) { capture(b, s); return; }
    if (!b.falling) normalise(b);
    if (g.rungs[9] && g.state === 'colour' && !b.ghost && !b.falling) steerToBrick(b, dt);
    const speed = Math.hypot(b.vx, b.vy), n = Math.max(1, Math.ceil(speed * dt / b.r)), ds = dt / n;
    b.spin += (b.vx >= 0 ? 1 : -1) * speed * dt / (b.r * 2);
    for (let i = 0; i < n && !b.lost && g.freeze <= 0 && !b.orbit; i++) {
      const px = b.x, py = b.y;
      b.x += b.vx * ds; b.y += b.vy * ds;
      collideWalls(b);
      if (b.falling) continue;
      collidePaddle(b, py);
      collideBricks(b, px, py);
      if (g.state === 'colour') collideColliders(b);
      // The last live ball slipping under the paddle in COLOUR: the relapse begins here, in slow motion.
      if (g.state === 'colour' && !g.transition && b.vy > 0 && b.y - b.r > g.paddle.y + g.paddle.h / 2 && liveBalls() === 1) startRelapse(b);
    }
    pushTrail(b);
  }
  const liveBalls = () => g.balls.reduce((n, b) => n + (!b.lost && !b.falling ? 1 : 0), 0);
  function pushTrail(b) { b.trail.push(b.x, b.y); if (b.trail.length > 80) b.trail.splice(0, 2); }

  /* ------------------------------------------------------------ step */
  function movePaddle(dt, input) {
    const p = g.paddle;
    let base = p.x - p.tug;
    if (typeof input.x === 'number' && !Number.isNaN(input.x)) base = input.x;
    else if (input.left) base -= 640 * dt;
    else if (input.right) base += 640 * dt;
    // A live spiral within 200 px tugs the paddle toward its centre at 60 px/s; the player's input still wins.
    const s = g.well;
    if (s && g.state === 'colour' && g.rungs[7] && Math.hypot(s.x - p.x, s.y - p.y) < 200) p.tug = clamp(p.tug + Math.sign(s.x - p.x) * 60 * dt, -60, 60);
    else p.tug = p.tug > 0 ? Math.max(0, p.tug - 120 * dt) : Math.min(0, p.tug + 120 * dt);
    p.x = clamp(base + p.tug, p.w / 2, w - p.w / 2);
  }
  function tick(dt, input) {
    g.time += dt; g.wallAge += dt;
    // One tick per landing row as the new wall settles (rows stagger by 0.04 s, the bounce reads at about 0.3 s).
    while (g.landRow < 6 && g.wallAge >= 0.3 + g.landRow * 0.04) {
      const row = g.bricks.find(b => b.alive && b.row === g.landRow);
      if (row) emit('brickLand', { x: row.x + row.w / 2, y: row.y, row: g.landRow });
      g.landRow++;
    }
    g.rungs = rungsFor(g.sat, g.state, g.force);
    g.speed = targetSpeed();
    g.paddle.w = PADDLE.baseW * (1 + 0.6 * g.sat);
    g.paddle.stretch = Math.max(0, g.paddle.stretch - dt * 4);
    g.wobble.t = Math.max(0, g.wobble.t - dt * 2);
    for (const br of g.bricks) {
      if (br.jelly > 0) br.jelly = Math.max(0, br.jelly - dt * 3);
      if (br.jellyIn > 0) { br.jellyIn -= dt; if (br.jellyIn <= 0) { br.jellyIn = 0; br.jelly = 1; } }
      if (br.pushT > 0) { br.pushT = Math.max(0, br.pushT - dt / PUSH_S); br.push.dx = br.push0.dx * br.pushT; br.push.dy = br.push0.dy * br.pushT; }
    }
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
      const gone = g.balls.filter(b => b.lost);
      g.balls = g.balls.filter(b => !b.lost);
      if (g.transition) return;                        // the relapse is already on its way
      if (!g.balls.length) lostAll(gone[0]);
      else for (const b of gone) emit('lost', { x: b.x, y: b.y });
    }
  }
  function step(dt, input = {}) {
    dt = Math.min(Math.max(0, dt), 0.1);
    g.beatPhase = beatPhase();
    if (g.smear && g.smearFading) { g.smear.a -= dt; if (g.smear.a <= 0) { g.smear = null; g.smearFading = false; } }
    if (g.freeze > 0) {
      g.freeze -= dt;
      if (g.freeze <= 0) { g.freeze = 0; if (g.pendingBreakout) completeBreakout(); }
      return;
    }
    const tr = g.transition;
    if (tr && tr.kind === 'breakout') {                // the rewind: the world holds for 0.3 s, then the freeze and the snap
      tr.t = Math.min(1, tr.t + dt / BREAKOUT_S + 1e-9);
      if (tr.t >= 1) { g.transition = null; g.freeze = 0.1; g.acc = 0; }
      return;
    }
    if (g.hitStopMs > 0) { g.hitStopMs = Math.max(0, g.hitStopMs - dt * 1000); if (g.hitStopMs > 0) return; }
    if (tr && tr.kind === 'relapse') tr.t = Math.min(1, tr.t + dt / RELAPSE_S + 1e-9);
    if (g.nearMissT > 0) g.nearMissT = Math.max(0, g.nearMissT - dt);
    setTimeScale(tr && tr.kind === 'relapse' ? 0.35 : g.nearMissT > 0 ? 0.4 : 1);
    movePaddle(dt, input);
    g.acc += dt * g.timeScale;
    let guard = 0;
    while (g.acc >= STEP && guard++ < 24) { g.acc -= STEP; tick(STEP, input); if (g.freeze > 0) { g.acc = 0; break; } }
    if (tr && tr.kind === 'relapse' && tr.t >= 1) relapse(g.balls[0] || g.smear);
  }

  buildWall();
  respawn(false);
  au('setSaturation', g.sat); au('setState', 'colour');

  return {
    step,
    snapshot: () => g,
    setWords(list) { if (Array.isArray(list) && list.length) { g.words = list.map(x => String(x)); g.wordIx = 0; } },
    /* dev and test hooks */
    setSaturation(s) { g.sat = clamp(Number(s) || 0, 0, 1); if (g.state === 'grey') g.savedSat = g.sat, g.sat = 0; au('setSaturation', g.sat); },
    setForce(i, v) { g.force[i] = v; g.rungs = rungsFor(g.sat, g.state, g.force); },
    clearForce() { g.force = {}; g.rungs = rungsFor(g.sat, g.state, g.force); },
    setBreakoutN(n) { g.breakoutN = Math.max(1, Math.floor(Number(n) || 12)); },
    setSpeedScale(s) { g.speedScale = clamp(Number(s) || 0.55, 0.2, 3); },
    relapseNow() { if (g.state === 'colour') lostAll(g.balls[0]); },
    breakoutNow() { startBreakout(g.balls[0]); },
    breakBrick(i) { const br = g.bricks[i]; if (br) breakBrick(br, g.balls[0]); },
    loseBall() { if (g.state === 'colour') lostAll(g.balls[0]); else { g.balls = []; lostAll(); } },
    launchNow() { for (const b of g.balls) if (b.stuck) launch(b); },
  };
}
