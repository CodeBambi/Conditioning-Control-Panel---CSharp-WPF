import {createPowerups, ordinaryTarget} from './powerups.js';
import { junctionProtected } from './junction-shield.js';
import { FINALE_REBUILD_SECONDS, FINALE_FEED_LIFE, FINALE_BRICK_LIMIT, FINALE_THREAD_COUNT, FINALE_GATE_COUNT, finaleChaosMetadata, finaleChaosPose, finaleWhirlPose } from './finale-chaos.js';
import {distributeGreyMetal, metalActive} from './grey-metal.js';
import { finaleHelpSeat, finaleHelpAngle } from './finale-help.js';
import { STRENGTH_BY_WALL, distributeStrength } from './brick-strength.js';
import { openPayloadSpot, brickOverlap } from './placement.js';
import { REFORM_WORDS, reformLayout, nextReformIndex, rotatedBrickContact } from './reform.js';
import { IRIS_ARMS, IRIS_LIFE, IRIS_INTERVAL, irisPose } from './iris.js';
import { TIDE_ROWS, TIDE_COLS, tidePose } from './tide.js';
import { CURTAIN_ROWS, CURTAIN_COLS, ANCHOR_GUARDS, createPendulums, curtainPose, advancePendulum, releasePendulum, collidePendulum } from './pendulum.js';
import { shieldY } from './words/let-go.js';
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
 * launch, perfect, nearMiss, jackpot, mantra, shatterWall, popOut, burst, word.
 *
 * Words: about one plain brick in six carries a subliminal word (g.words). Broken in
 * COLOUR it fires the word's diegetic effect (word-fx.js, one module per word
 * under words/); the effect's frame mods land in g.mod. In GREY a word brick is
 * a special (+3) and nothing fires.
 *
 * Payloads: the GIF brick and the SPIRAL brick are the spawners. Broken in
 * COLOUR the face pops out of the wall (g.pops), tumbles, and bursts: a picture
 * brick into a drifting collider bubble (g.colliders, up to 3), a spiral brick
 * (it wears one of the Loom's fields, brick.spiral) into the whirlwind well
 * (g.well, one at a time, no bubble: the field itself is the well). The brick
 * is the gate, not the rung: a spiral brick always makes its well; a new one
 * while one is live replaces it unless it is holding a ball.
 * No timer spawns. In GREY a special brick (gif, spiral, split, jackpot) is +3
 * on the breakout counter and nothing spawns.
 *
 * Word bricks swap their word on their own clock (WORD_SWAP_S, each brick out
 * of step with the others) with a short glitch (brick.glitch 1 -> 0), in COLOUR.
 * ==========================================================================*/

import { createWordSim, freshMod, WORD_BRICK_P } from './word-fx.js';

export const W = 480, H = 720;
export const RUNG_AT = [0, 0.10, 0.20, 0.30, 0.40, 0.50, 0.60, 0.70, 0.80, 0.90];
export const RUNG_NAMES = ['grey', 'colour', 'trail', 'particles', 'jelly', 'shake', 'words', 'spirals', 'colliders', 'crack'];
export const BRICK = { cols: 10, rows: 6, w: 42, h: 18, gap: 4, top: 80 };
export const PADDLE = { baseW: 90, h: 14 };
export const BALL_R = 8;
export const MAX_BALLS = 3;
/* The Loom fields a well can wear (shared/hypno/loom.js presets); each well draws one at spawn. */
export const WELL_PRESETS = ['candy', 'pinwheel', 'ribbon', 'mint', 'star', 'hub', 'whirl', 'wake'];
export const DEFAULT_WORDS = ['DROP', 'RELAX', 'LET GO', 'SINK'];
/* Spiral bricks: this share of the plain bricks wears a Loom field (about three or four a wall). */
export const SPIRAL_BRICK_P = 0.07;
/* A word brick swaps its word every WORD_SWAP_S (+ up to WORD_SWAP_J) seconds, its glitch lasting GLITCH_S. */
export const WORD_SWAP_S = 1.7, WORD_SWAP_J = 0.9, GLITCH_S = 0.36;
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
  saturation = 0.15, speedScale = 0.55, words = DEFAULT_WORDS, reduced = false } = {}) {
  const g = {
    w, h, breakoutN, speedScale, noLose: false,   // dev: the floor bounces, the ball never drops
    reduced: !!reduced,                            // reduced motion: no tumble, the bubble appears at the brick
    sat: saturation, savedSat: saturation, state: 'colour', greyBricks: 0,
    force: {}, rungs: rungsFor(saturation, 'colour', null), speed: 0,
    paddle: { x: w / 2, w: PADDLE.baseW, h: PADDLE.h, y: h - 40, stretch: 0, tug: 0 },
    balls: [], bricks: [], colliders: [], well: null, pops: [],
    stats: { bricks: 0, walls: 0, sp: 0 }, combo: 0, comboBest: 0, time: 0, freeze: 0, pendingBreakout: false, breakoutAt: null,
    wallAge: 1, landRow: 99, wobble: { side: '', t: 0 }, crackFired: false, acc: 0, launchTimer: 0,
    // contract v2
    transition: null, timeScale: 1, hitStopMs: 0, smear: null, smearFading: false, fractures: 0, shatterWall: false,
    mantra: null, beatPhase: 0, lastPerfectAt: 0, nearMissT: 0,
    words: (Array.isArray(words) && words.length ? words : DEFAULT_WORDS).map(x => String(x)), wordIx: 0,
    // word triggers (word-fx.js): the running effects and this frame's mods
    fx: { active: [], lastHeavyAt: -99 }, mod: freshMod(),
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
    return { x, y, w: bw, h: bh, alive: true, row, col, gif: -1, split: false, jackpot: false, letter: null, word: null, color: ROW_COLORS[row % ROW_COLORS.length],
      spiral: null, hue: 0, spin: 1,                 // a spiral brick: the Loom preset it wears (its hue and spin go to the well it becomes)
      wordAt: 0, glitch: 0, swaps: 0,                // a word brick: seconds to its next swap, the glitch left (1 -> 0), swaps so far
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
        const gif = rng() < 0.15 ? Math.floor(rng() * 8) : -1;
        // About one plain brick in six carries a word, dealt in turn so every word gets its share (owner, 2026-09-19).
        const word = gif < 0 && g.words.length && rng() < WORD_BRICK_P ? g.words[g.wordIx++ % g.words.length] : null;
        // A few plain bricks wear a spiral: the well comes out of the brick it was in (owner, 2026-09-19).
        const spiral = gif < 0 && !word && rng() < SPIRAL_BRICK_P ? WELL_PRESETS[Math.floor(rng() * WELL_PRESETS.length)] : null;
        bricks.push(mkBrick(x0 + col * (BRICK.w + BRICK.gap), BRICK.top + row * (BRICK.h + BRICK.gap), BRICK.w, BRICK.h, row, col,
          { gif, split: rng() < 0.05, word, spiral, hue: spiral ? Math.floor(rng() * 70) - 35 : 0, spin: spiral ? 0.75 + rng() * 0.5 : 1,
            wordAt: word ? 0.6 + rng() * (WORD_SWAP_S + WORD_SWAP_J) : 0 }));   // each word brick starts its clock somewhere else
      }
      g.mantra = null;
    }
    if (bricks.length) bricks[Math.floor(rng() * bricks.length)].jackpot = true;   // one hidden jackpot per wall
    g.bricks = bricks;
    g.fractures = 0; g.shatterWall = false;
    if (mantra) emit('mantra', { word: g.mantra });
  }
  function updatePendulums(dt) {
    const pendulums=g.pendulums;
    if(!pendulums || g.wallAge<1.9)return;
    for(const p of pendulums) {
      advancePendulum(p,dt,w,h,g.reduced);
      for(const br of g.bricks) if(br.alive && br.curtain && br.pendulumId===p.id && p.mode==='hung')
        Object.assign(br,curtainPose(p,br.curtainRow,br.curtainCol));
      // Collapse the freed curtain in a bounded cascade; sweeps hit neighbouring curtains and anchors.
      let collapsed=0; p.collapseClock=(p.collapseClock||0)-dt;
      for(const br of g.bricks) {
        if(g.pendulums!==pendulums)return;
        if(!br.alive || p.struck.has(br) || p.mode==='hung' || p.mode==='spent')continue;
        const own=br.curtain && br.pendulumId===p.id;
        const contact=p.mode==='sweep' && rotatedBrickContact({x:p.x,y:p.y,r:p.r},br);
        if((own && collapsed<1 && p.collapseClock<=0) || contact) {
          if(own){collapsed++;p.collapseClock=.065;}
          p.struck.add(br);
          if (own && br.strength) br.hp = 1; // A released curtain crushes its own reinforced pieces.
          breakBrick(br,{x:p.x,y:p.y,vx:p.toX-p.fromX,vy:-100});
        }
      }
    }
  }

  function updateTide(dt) {
    if (!g.tide) return;
    g.tide.age += dt;
    for (const brick of g.bricks) if (brick.alive) {
      Object.assign(brick, tidePose(brick.row, brick.col, g.tide.age, w, h, g.reduced));
    }
  }

  function makeIrisBrick(arm, age=0) {
    const gif=rng()<.12?Math.floor(rng()*8):-1;
    const word=gif<0&&g.words.length&&rng()<WORD_BRICK_P?g.words[g.wordIx++%g.words.length]:null;
    return mkBrick(0,0,39,24,arm,Math.floor(age/IRIS_INTERVAL),{
      ...irisPose(arm,age,w,h,g.iris?.rotation||0),arm,irisAge:age,gif,tier:gif>=0?bubbleTier(rng()):0,
      word,wordAt:.8+rng(),split:rng()<.05,
      spiral:gif<0&&!word&&rng()<SPIRAL_BRICK_P?WELL_PRESETS[Math.floor(rng()*WELL_PRESETS.length)]:null
    });
  }

  function irisGuardPose(arm,guard) {
    const pose=irisPose(arm,0,w,h,g.iris.rotation);
    const angle=pose.angle+guard*Math.PI/3;
    return {x:pose.x+pose.w/2+43*Math.cos(angle)-18,
      y:pose.y+pose.h/2+43*Math.sin(angle)-8,w:36,h:16,angle:angle+Math.PI/2};
  }

  function updateIris(dt) {
    if(!g.iris)return;
    g.iris.rotation+=dt*(g.reduced?.035:.14);
    g.iris.eye.age+=dt;g.iris.eye.born+=dt;g.iris.eye.rot-=dt*.28;
    for(const b of g.bricks)if(b.alive&&b.irisCore){
      const pose=irisPose(b.arm,0,w,h,g.iris.rotation);
      b.x=pose.x-5;b.y=pose.y-5;b.angle=pose.angle;
    }
    for(const b of g.bricks)if(b.alive&&b.irisGuard)Object.assign(b,irisGuardPose(b.arm,b.guard));
    for(const b of g.bricks) if(b.alive&&!b.irisCore&&!b.irisGuard) {
      b.irisAge+=dt;
      if(b.irisAge>=IRIS_LIFE){b.alive=false;continue;}
      Object.assign(b,irisPose(b.arm,b.irisAge,w,h,g.iris.rotation));
    }
    // Remove consumed/dead pieces: continuous play must not grow the array forever.
    g.bricks=g.bricks.filter(b=>b.alive);
    for(let arm=0;arm<IRIS_ARMS;arm++) {
      if(!g.bricks.some(b=>b.irisCore&&b.arm===arm))continue;
      g.iris.clocks[arm]+=dt;
      if(g.iris.clocks[arm]>=IRIS_INTERVAL){
        g.iris.clocks[arm]-=IRIS_INTERVAL;
        const b=makeIrisBrick(arm); b.reformSafe=true; g.bricks.push(b);
      }
    }
    for(const b of g.bricks)if(b.reformSafe)
      b.reformSafe=g.balls.some(ball=>!ball.lost&&rotatedBrickContact(ball,b));
    if(!bricksAlive())wallCleared();
  }

  const bricksAlive = () => g.bricks.some(b => b.alive);
  function updateReform(dt) {
    const r = g.reform; if (!r) return;
    if (r.moving > 0) {
      r.moving = Math.max(0, r.moving - dt);
      const t = 1 - r.moving / .65, ease = t * t * (3 - 2 * t);
      for (const b of g.bricks) if (b.alive && b.reformFrom) {
        for (const key of ['x','y','w','h','angle']) b[key] = lerp(b.reformFrom[key], b.reformTo[key], ease);
        if (!r.moving) { b.reformFrom = null; b.reformTo = null; }
      }
    }
    if (!r.moving) for (const b of g.bricks) if (b.reformSafe) {
      b.reformSafe = g.balls.some(ball => !ball.lost && rotatedBrickContact(ball, b));
    }
    const beat = g.beatPhase < r.lastPhase - .5; r.lastPhase = g.beatPhase;
    if (!beat || g.transition) return;
    r.beats++; emit('metronome', { accent: r.beats % r.every === 0 });
    if (r.stopped || r.beats % r.every !== 0) return;
    const alive = g.bricks.filter(b => b.alive);
    const next = nextReformIndex(r.index, alive.length);
    if (next < 0 || (next === r.index && r.word === 'I')) { r.stopped = true; return; }
    if (next === r.index) return;
    r.index = next; r.word = REFORM_WORDS[next];
    const targets = reformLayout(r.word, alive.length, w);
    const unused = [...targets];
    for (const b of alive) {
      let nearest = 0;
      for (let i = 1; i < unused.length; i++) if (Math.hypot(unused[i].x-b.x,unused[i].y-b.y) < Math.hypot(unused[nearest].x-b.x,unused[nearest].y-b.y)) nearest = i;
      const to = unused.splice(nearest,1)[0];
      b.reformFrom = { x:b.x,y:b.y,w:b.w,h:b.h,angle:b.angle||0 }; b.reformTo = to; b.reformSafe = true; b.letter = to.letter;
      if (g.reduced) { Object.assign(b,to); b.reformFrom = null; b.reformTo = null; }
    }
    r.moving = g.reduced ? 0 : .65;
    emit('reform', { word:r.word, count:alive.length });
  }

  function startSpellRound() {
    const previous = g.spell;
    const choices = SPELL_WORDS.filter(word => word !== previous?.word);
    const word = choices[Math.floor(rng() * choices.length)];
    g.spell = { word, filled: Array.from(word, letter => letter === ' '), round: (previous?.round || 0) + 1,
      hits: 0, complete: false, celebrate: 0 };
    g.mantra = word;
  }

  function cycleSpellLetters() {
    const spell = g.spell;
    if (!spell) return;
    const missing = Array.from(spell.word).filter((letter, i) => letter !== ' ' && !spell.filled[i]);
    const letters = missing.length ? missing : Array.from(spell.word).filter(letter => letter !== ' ');
    // All dealt letters are useful. Occasionally reverse their order for a last-slot-first hit.
    if (rng() < 0.2) letters.reverse();
    let i = spell.hits;
    for (const brick of g.bricks) if (brick.alive) brick.letter = letters[i++ % letters.length];
  }

  function fillSpell(brick, x, y) {
    const spell = g.spell;
    if (!spell) return;
    spell.hits++;
    if (g.state === 'colour' && !spell.complete) {
      const index = Array.from(spell.word).findIndex((letter, i) => letter === brick.letter && !spell.filled[i]);
      if (index >= 0) {
        spell.filled[index] = true;
        const style = g.reduced ? 'static' : rng() < 0.65 ? 'stamp' : 'drift';
        emit('spellFill', { word: spell.word, index, letter: brick.letter, x, y, style });
        if (spell.filled.every(Boolean)) {
          spell.complete = true; spell.celebrate = 1.6;
          emit('spellComplete', { word: spell.word, style });
        }
      }
    }
    cycleSpellLetters();
  }


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
    const s = targetSpeed() * g.mod.ballSpeed;
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
    g.transition = null; setTimeScale(1); wordSim.endAll();
    g.savedSat = g.sat; g.sat = 0; g.state = 'grey'; g.greyBricks = 0; g.combo = 0; g.hitStopMs = 0; g.nearMissT = 0;
    g.colliders = []; g.well = null; g.pops = []; g.paddle.tug = 0;
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
    const plus = (br.gif >= 0 || br.spiral || br.split || br.jackpot || br.word) ? 3 : 1;   // a special brick counts triple on the grey counter
    emit('brick', { x: cx, y: cy, w: br.w, h: br.h, color: br.color, row: br.row, col: br.col, gif: br.gif >= 0, gifIndex: br.gif,
      spiral: br.spiral, jackpot: br.jackpot, letter: br.letter, word: br.word, ghost: grey, sat: g.sat, plus });
    if (br.word && !grey) wordSim.fire(br.word, cx, cy);
    bumpCombo(br.gif >= 0 || br.spiral ? 'gif' : 'brick', cx, cy);
    if (br.jackpot) { g.stats.sp += 5; emit('jackpot', { x: cx, y: cy, sp: g.stats.sp, ghost: grey }); }
    if (grey) { g.greyBricks += plus; if (g.greyBricks >= g.breakoutN) startBreakout(ball); }
    else {
      addSat(0.012);
      if (br.gif >= 0 || br.spiral) popOut(br, ball);
      if (g.crackFired && !g.shatterWall) { g.fractures = Math.min(1, g.fractures + 0.04); if (g.fractures >= 1) { g.shatterWall = true; au('shatterWall'); emit('shatterWall', {}); } }
      if (br.split && ball && g.balls.length < MAX_BALLS) split(ball, br);
    }
    if (!bricksAlive()) wallCleared();
  }
  function makeFinaleFeed(id, age = 0) {
    const meta=finaleChaosMetadata(id);
    const br=mkBrick(0,0,meta.w,meta.h,id%6,0,{...meta,finaleFeed:true,feedAge:age});
    // Per 80 seats: twelve words and six spirals, both 20% above the prior feed.
    const kind=id%16, seat=id%80, cycle=Math.floor(id/16);
    if(kind===1||kind===9){br.gif=(cycle*2+(kind===9?1:0))%8;br.tier=1;}
    else if(kind===3||kind===11||seat===2||seat===34){br.word=['SINK','RELAX','LET GO','DROP'][cycle%4];br.wordAt=2;}
    else if(kind===5)br.split=true;
    else if(kind===7||seat===6){br.spiral=WELL_PRESETS[(cycle+(seat===6?1:0))%WELL_PRESETS.length];br.spin=1;}
    else if(seat===14||seat===46||seat===78)br.jackpot=true;
    if(br.finaleHinge){
      br.hp=3;
      br.gif=-1;br.word=null;br.split=false;br.jackpot=false;br.spiral=WELL_PRESETS[id%WELL_PRESETS.length];br.spin=1;
    }
    Object.assign(br,finaleChaosPose(br,g.finale,w,h,g.reduced));
    powers.assign(br);
    return br;
  }

  function dressFinale() {
    const f=g.finale;
    if(f.mixed)return;
    f.mixed=true;f.stageAge=0;
    const pieces=g.bricks.filter(b=>b.alive&&!b.finaleWord);
    f.initialBricks=pieces.length;
    pieces.forEach((br,i)=>{
      // Sparse permanent metal, tough dull pieces, and a varied colourful remainder.
      const kind=i%16;
      if(kind===0)br.finaleMetal=true;
      else if(kind<5){br.finaleGrey=true;br.strength=2;br.hp=2;}
      else if(kind===5||kind===11){br.gif=(Math.floor(i/16)*2+(kind===11?1:0))%8;br.tier=1;}
      else if(kind===7){br.word=['SINK','RELAX','LET GO'][Math.floor(i/16)%3];br.wordAt=2;}
      else if(kind===9)br.split=true;
      else if(kind===13){br.spiral=WELL_PRESETS[i%WELL_PRESETS.length];br.spin=1;br.hue=0;}
    });
    // A reachable centre is an alternate stage-one objective, protected by worn shields.
    for(let i=0;i<12;i++) {
      const a=i*TAU/12, strength=i%2?2:3, radius=57.6;
      g.bricks.push(mkBrick(f.centreX+Math.cos(a)*radius-10.8,f.centreY+Math.sin(a)*radius-7.2,21.6,14.4,i%6,i,
        {finaleCenterGuard:true,strength,hp:strength,angle:a+Math.PI/2}));
    }
    for(const br of g.bricks)powers.assign(br);
    f.initialBricks=g.bricks.filter(b=>b.alive&&!b.finaleWord).length;
  }

  function beginFinaleSpiral() {
    powers.reset();
    const f=g.finale; f.stage=2; f.stageAge=0; f.feedClock=0; f.feedIndex=0;
    f.centreY=h*.36; f.coreRadius=28;
    // Existing survivors fly into arm positions. New pieces enter from outside the field.
    g.bricks=g.bricks.filter(br=>br.alive).slice(0,Math.floor((FINALE_BRICK_LIMIT-FINALE_GATE_COUNT)*.6));
    g.bricks.forEach((br,i)=>{
      br.finaleMetal=false; br.finaleRing=false; br.finaleDefense=false; br.finaleFeed=true;
      br.flyFrom={x:br.x,y:br.y,angle:br.angle||0};
      Object.assign(br,finaleChaosMetadata(f.feedIndex++));
      if(br.finaleHinge){br.hp=3;br.strength=0;br.finaleGrey=false;br.gif=-1;br.word=null;br.split=false;br.jackpot=false;br.spiral=WELL_PRESETS[0];}
      br.feedAge=FINALE_FEED_LIFE*(.2+.8*(Math.floor(br.chaosId/FINALE_THREAD_COUNT)%24+.5)/24);
    });
    for(let ring=0;ring<2;ring++) {
      const count=ring===0?14:20,radius=ring===0?60:90,strength=3;
      for(let i=0;i<count;i++) {
        const a=i*TAU/count;
        const target={x:f.centreX+Math.cos(a)*radius-13.2,y:f.centreY+Math.sin(a)*radius-9.6,angle:a+Math.PI/2};
        const entry=a+ring*.37, reach=Math.hypot(w,h);
        const x=f.centreX+Math.cos(entry)*reach,y=f.centreY+Math.sin(entry)*reach;
        g.bricks.push(mkBrick(x,y,26.4,19.2,ring,i,{finaleGate:true,gateRing:ring,strength,hp:strength,
          finaleMetal:i%9===4,angle:target.angle,
          flyFrom:{x,y,angle:target.angle},flyTarget:target}));
      }
    }
    while(g.bricks.length<FINALE_BRICK_LIMIT) {
      const id=f.feedIndex++, br=makeFinaleFeed(id,FINALE_FEED_LIFE*(.2+.8*(Math.floor(id/FINALE_THREAD_COUNT)%24+.5)/24));
      const a=id*2.3999632297,reach=Math.hypot(w,h);
      br.flyFrom={x:f.centreX+Math.cos(a)*reach,y:f.centreY+Math.sin(a)*reach,angle:a};
      br.x=br.flyFrom.x;br.y=br.flyFrom.y;
      g.bricks.push(br);
    }
    emit('finaleStage',{stage:2,x:f.centreX,y:f.centreY});
  }

  function updateFinaleFormation(dt) {
    const f=g.finale;if(!f)return;
    if(f.phase==='interrupt'||f.phase==='outro')return;
    f.motionAge=(f.motionAge||0)+dt;
    if(f.stage===1) {
      const time=g.reduced?0:f.motionAge;
      for(const br of g.bricks)if(br.alive&&br.finaleRing){
        const beat=(time+br.ring*1.7)%9;
        const shiver=beat<1.2?Math.sin(beat*23)*Math.sin(beat*Math.PI/1.2):0;
        const angle=br.ringAngle+time*(br.ring%2?-.045:.035)+shiver*.014;
        const radius=br.ringRadius+shiver*2.5;
        br.x=f.centreX+Math.cos(angle)*radius*(br.ring===0?1.3:1)-br.w/2;
        br.y=f.centreY+Math.sin(angle)*radius-br.h/2;
        br.angle=angle+Math.PI/2;
      }
    }
    if(f.phase!=='released')return;
    f.stageAge+=dt;
    if(f.stage===1) {
      if(f.stageAge>=300 || g.bricks.filter(b=>b.alive&&!b.finaleWord).length<=f.initialBricks*.2)beginFinaleSpiral();
      return;
    }
    if(f.stage!==2)return;
    const arrival=Math.min(1,f.stageAge/FINALE_REBUILD_SECONDS);
    for(const br of g.bricks) {
      if(!br.alive)continue;
      if(br.finaleFeed) {
        br.feedAge+=dt;
        if(br.feedAge>=FINALE_FEED_LIFE){br.alive=false;continue;}
        Object.assign(br,finaleChaosPose(br,f,w,h,g.reduced));
        br.irisAlpha=Math.min(1,(FINALE_FEED_LIFE-br.feedAge)/1.2);
      } else if(br.flyTarget)Object.assign(br,br.flyTarget);
      if(br.flyFrom) {
        if(g.reduced || arrival===1){br.flyFrom=null;}
        else Object.assign(br,finaleWhirlPose(br.flyFrom,br,f,arrival,br.w,br.h));
      }
    }
    g.bricks=g.bricks.filter(br=>br.alive);
    {
      f.feedClock+=dt;
      if(f.feedClock>=.3) {
        f.feedClock-=.3;
        if(g.bricks.length<FINALE_BRICK_LIMIT)g.bricks.push(makeFinaleFeed(f.feedIndex++));
      }
    }
  }

  function finaleProgress(ball, value = 1) {
    if (g.pendingBreakout) return;
    g.greyBricks += value; g.fractures = Math.min(1, g.greyBricks/g.breakoutN);
    if (g.greyBricks >= g.breakoutN) startBreakout(ball);
  }

  function updateFinale(dt, input) {
    const f = g.finale; if (!f) return false;
    if(f.phase==='outro') { f.outroAge+=dt; return true; }
    f.age += dt;
    for (const burst of f.bursts) burst.age += dt;
    f.bursts = f.bursts.filter(b => b.age < .55);
    if (f.phase === 'interrupt') {
      if (f.age >= 3.95) {
        relapse(); au('finaleGrey',true); f.phase = 'locked'; f.age = 0; f.wordClock = 30; emit('finaleLocked',{});
        g.rungs = rungsFor(0, 'grey', g.force);
      }
      return true;
    }
    if (f.phase === 'forming' || f.phase === 'ready') {
      g.wallAge = Math.min(2, f.age); g.rungs = rungsFor(g.sat,g.state,g.force);
      movePaddle(dt,input);
      for(const b of g.balls) {b.x=g.paddle.x;b.y=g.paddle.y-g.paddle.h/2-b.r;}
      if (f.age >= 1.9) f.phase = 'ready';
      if (f.phase === 'ready' && input.launch) {f.phase='approach';for(const b of g.balls)launch(b);}
      return true;
    }
    if (f.phase === 'locked') {
      for (const br of g.bricks) if (br.finaleWord) {
        br.ttl -= dt;
        if (br.ttl <= 0) {
          br.alive = false;
          f.bursts.push({x:br.x+br.w/2,y:br.y+br.h/2,text:br.finaleWord,age:0});
        }
      }
      f.bursts = f.bursts.slice(-8);
      g.bricks = g.bricks.filter(br=>!br.finaleWord || br.alive);
      f.wordClock -= dt;
      if (f.wordClock <= 0 && g.bricks.filter(br=>br.finaleWord).length < 4) {
        const phrases=['GIVE UP','TOO LATE','STOP','GO BACK','NOT ENOUGH','WHY TRY'];
        // Upper side lanes stay reachable outside the sealed ring.
        const seats=[[.17,.14],[.83,.28],[.17,.42],[.83,.14],[.17,.28],[.83,.42]];
        const helpSeat=finaleHelpSeat(g);
        if(helpSeat) {
          const text=phrases[f.wordIndex++%phrases.length],life=5+rng();
          g.bricks.push(mkBrick(helpSeat.x,helpSeat.y,helpSeat.w,helpSeat.h,0,-1,
            {finaleWord:text,seat:-1,ttl:life,wordLife:life}));
        }
        for (let offset=0;!helpSeat&&offset<seats.length;offset++) {
          const seat=(f.wordIndex+offset)%seats.length;
          if(g.bricks.some(br=>br.finaleWord&&br.seat===seat))continue;
          const [x,y]=seats[seat],text=phrases[f.wordIndex++%phrases.length],life=5+rng();
          g.bricks.push(mkBrick(w*x-65,h*y-14,130,28,0,seat,{finaleWord:text,seat,ttl:life,wordLife:life}));
          break;
        }
        f.wordClock=2.7+rng()*.6;
      }
    }
    return false;
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

  /* ------------------------------------------------------------ payloads: the GIF brick pops out into a bubble */
  const POP_G = 1400, POP_KICK = 210, BAND = { x0: 110, y0: 280, y1: 520 };
  /** The broken GIF brick's face leaves the wall along the ball direction, tumbles, and bursts inside the band. */
  function popOut(br, ball) {
    const cx = br.x + br.w / 2, cy = br.y + br.h / 2;
    const len = ball ? Math.hypot(ball.vx || 0, ball.vy || 0) : 0;
    const ux = len > 1 ? ball.vx / len : 0, uy = len > 1 ? ball.vy / len : -1;
    const spiral = br.spiral || null;
    const pop = { x: cx, y: cy, w: br.w, h: br.h, vx: ux * POP_KICK + (rng() - 0.5) * 60, vy: uy * POP_KICK - 80, rot: 0,
      vr: (rng() < 0.5 ? -1 : 1) * (5 + rng() * 5), gif: spiral ? -1 : br.gif >= 0 ? br.gif : Math.floor(rng() * 8), color: br.color, t: 0, life: 0.5 + rng() * 0.3, done: false,
      spiral, hue: spiral ? (br.hue || 0) : 0, spin: spiral ? (br.spin || 1) : 1 };   // a spiral pop grows into the well, the same field
    g.pops.push(pop);
    emit('popOut', { x: cx, y: cy, w: br.w, h: br.h, vx: pop.vx, vy: pop.vy, gif: pop.gif, spiral, color: br.color });
    if (g.reduced) { burst(pop); g.pops = g.pops.filter(p => !p.done); }
    return pop;
  }
  function updatePops(dt) {
    if (!g.pops.length) return;
    for (const p of g.pops) {
      p.t += dt; p.vy += POP_G * dt; p.x += p.vx * dt; p.y += p.vy * dt; p.rot += p.vr * dt;
      if (p.x < BAND.x0) { p.x = BAND.x0; p.vx = Math.abs(p.vx); } else if (p.x > w - BAND.x0) { p.x = w - BAND.x0; p.vx = -Math.abs(p.vx); }
      // It bursts where it lands: once its life is up and it is inside the band (a face that leaves upward keeps falling), never past the band, never over 1.2 s.
      if ((p.t >= p.life && p.y >= BAND.y0) || p.y >= BAND.y1 || p.t >= 1.2) burst(p);
    }
    g.pops = g.pops.filter(p => !p.done);
  }
  /** The pop bursts: a spiral face into the whirlwind well (one live, no bubble; the brick is the gate, not the rung), a picture face into a collider bubble (up to 3). */
  function burst(p) {
    p.done = true;
    const x = clamp(p.x, BAND.x0, w - BAND.x0), y = clamp(p.y, BAND.y0, BAND.y1);
    let kind = 'none';
    if (g.state === 'colour') {
      if (p.spiral) { if (!g.well || !g.well.captured) { spawnWell(x, y, p); kind = 'well'; } }   // owner, 2026-09-19: the spiral must always show up
      else if (g.colliders.length < 3) { spawnCollider(x, y, p.gif); kind = 'collider'; }
    }
    emit('burst', { x, y, gif: p.gif, spiral: p.spiral || null, kind, color: p.color });
  }
  function spawnCollider(x, y, gif) {
    const r = 46, a = rng() * TAU;
    g.colliders.push({ x, y, r, vx: Math.cos(a) * 18, vy: Math.sin(a) * 18, hits: 0, pulse: 0, alpha: 0, fading: false, gif, age: 0 });
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
  function chargeDome(amount = .22) {
    if (g.well?.persistent && g.state === 'colour') {
      const s = g.well;
      s.energy = Math.min(1, s.energy + amount);
      s.turnPending = Math.min(2.4, s.turnPending + .84 + amount);
      s.hitPulse = 1;
    }
  }

  function spawnDome() {
    g.well = { x: w / 2, y: h / 2, r: 115, pull: 145, age: 0, born: 0, ttl: Infinity,
      rot: 0, used: false, fade: 1, captured: null, gif: -1, preset: 'whirl', spin: 1,
      hue: 0, persistent: true, energy: 0, cooldown: 0, turnPending: 0, musicPulse: 0, hitPulse: 0, lastBeatPhase: g.beatPhase };
  }

  function spawnWell(x, y, from) {
    // The field is the brick's own (preset, spin, hue ride in on the pop), so the well is the spiral the player saw in the wall. `born` never pauses (the inflate).
    const preset = from && WELL_PRESETS.includes(from.spiral) ? from.spiral : WELL_PRESETS[Math.floor(rng() * WELL_PRESETS.length)];
    g.well = { x, y, r: 70, pull: 110, age: 0, born: 0, ttl: 6, rot: 0, used: false, fade: 1, captured: null, gif: -1,
      preset, spin: from && Number.isFinite(from.spin) ? from.spin : 0.75 + rng() * 0.5, hue: from && Number.isFinite(from.hue) ? from.hue : Math.floor(rng() * 70) - 35 };
  }
  function updateWell(dt) {
    const s = g.well; if (!s) return;
    s.rot -= dt * (s.captured ? 4.4 : 1.6) * (s.spin || 1);   // turns the way the ball orbits, counter-clockwise on screen (owner, 2026-09-19); tightens while it holds a ball
    s.born += dt;
    if (s.captured) return;
    s.age += dt;
    if (s.age >= s.ttl) { s.fade -= dt / 0.4; if (s.fade <= 0) g.well = null; }
  }
  function capture(b, s) {
    const rx = b.x - s.x, ry = b.y - s.y;
    b.orbit = { r: clamp(Math.hypot(rx, ry), 40, 100), a: Math.atan2(ry, rx), dir: -1, turns: 1 + rng(), done: 0 };   // counter-clockwise on screen: the ball ran against the field the other way (owner, 2026-09-19)
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
    if (b.y - b.r > h) { if (g.noLose || g.mod.safe) { b.y = h - b.r; b.vy = -Math.abs(b.vy); wallHit('bottom', b); } else b.lost = true; }
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
    if (s && !s.used && g.state === 'colour' && !b.ghost && !b.falling && Math.hypot(b.x - s.x, b.y - s.y) < s.pull) { capture(b, s); return; }
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
      if (g.state === 'colour' && !g.transition && !g.noLose && !g.mod.safe && b.vy > 0 && b.y - b.r > g.paddle.y + g.paddle.h / 2 && liveBalls() === 1) startRelapse(b);
    }
    pushTrail(b);
  }
  const liveBalls = () => g.balls.reduce((n, b) => n + (!b.lost && !b.falling ? 1 : 0), 0);
  function pushTrail(b) { b.trail.push(b.x, b.y); if (b.trail.length > 80) b.trail.splice(0, 2); }

  /* ------------------------------------------------------------ step */
  function movePaddle(dt, input) {
    const p = g.paddle;
    let base = p.x - p.tug;
    if (g.mod.autopilot) {                           // LET GO: the paddle glides under the lowest live ball on its own
      const low = g.balls.filter(b => !b.lost && !b.stuck && !b.falling).sort((a, b) => b.y - a.y)[0];
      base += ((low ? low.x : w / 2) - base) * Math.min(1, dt * 14);
    }
    else if (typeof input.x === 'number' && !Number.isNaN(input.x)) base = input.x;
    else if (input.left) base -= 640 * dt;
    else if (input.right) base += 640 * dt;
    // A live spiral within 200 px tugs the paddle toward its centre at 60 px/s; the player's input still wins.
    const s = g.well;
    if (s && g.state === 'colour' && Math.hypot(s.x - p.x, s.y - p.y) < 200) p.tug = clamp(p.tug + Math.sign(s.x - p.x) * 60 * dt, -60, 60);
    else p.tug = p.tug > 0 ? Math.max(0, p.tug - 120 * dt) : Math.min(0, p.tug + 120 * dt);
    p.x = clamp(base + p.tug, p.w / 2, w - p.w / 2);
  }
  /** A word brick's own clock: when it runs out the brick glitches and shows another word from the list. */
  function swapWord(br, dt) {
    br.wordAt -= dt;
    if (br.wordAt > 0) return;
    br.wordAt = WORD_SWAP_S + rng() * WORD_SWAP_J;
    const list = g.words, n = list.length;
    if (n > 1) {
      const cur = list.indexOf(br.word);
      br.word = list[((cur < 0 ? Math.floor(rng() * n) : cur) + 1 + Math.floor(rng() * (n - 1))) % n];   // never the same word twice running
    }
    br.glitch = 1; br.swaps++;
    emit('wordSwap', { x: br.x + br.w / 2, y: br.y + br.h / 2, word: br.word, row: br.row, col: br.col });
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
    g.speed = targetSpeed() * g.mod.ballSpeed;
    g.paddle.w = PADDLE.baseW * (1 + 0.6 * g.sat) * g.mod.paddleW;
    g.paddle.stretch = Math.max(0, g.paddle.stretch - dt * 4);
    g.wobble.t = Math.max(0, g.wobble.t - dt * 2);
    for (const br of g.bricks) {
      if (br.jelly > 0) br.jelly = Math.max(0, br.jelly - dt * 3);
      if (br.jellyIn > 0) { br.jellyIn -= dt; if (br.jellyIn <= 0) { br.jellyIn = 0; br.jelly = 1; } }
      if (br.pushT > 0) { br.pushT = Math.max(0, br.pushT - dt / PUSH_S); br.push.dx = br.push0.dx * br.pushT; br.push.dy = br.push0.dy * br.pushT; }
      if (br.glitch > 0) br.glitch = Math.max(0, br.glitch - dt / GLITCH_S);
      if (br.word && br.alive && g.state === 'colour') swapWord(br, dt);
    }
    updatePops(dt); updateColliders(dt); updateWell(dt);
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
    wordSim.advance(dt);                              // wall-clock: a word that slows the game does not slow itself
    setTimeScale(Math.min(tr && tr.kind === 'relapse' ? 0.35 : g.nearMissT > 0 ? 0.4 : 1, g.mod.timeScale));
    movePaddle(dt, input);
    g.acc += dt * g.timeScale;
    let guard = 0;
    while (g.acc >= STEP && guard++ < 24) { g.acc -= STEP; tick(STEP, input); if (g.freeze > 0) { g.acc = 0; break; } }
    if (tr && tr.kind === 'relapse' && tr.t >= 1) relapse(g.balls[0] || g.smear);
  }

  const wordSim = createWordSim(g, { emit, au, rng, addSat, clamp, lerp, W: w, H: h });
  buildWall();
  respawn(false);
  au('setSaturation', g.sat); au('setState', 'colour');

  return {
    step,
    snapshot: () => g,
    setWords(list) { if (Array.isArray(list) && list.length) { g.words = list.map(x => String(x)); g.wordIx = 0; } },
    /* dev and test hooks */
    replayEntrance() { g.wallAge = 0; g.landRow = 0; },
    jumpToWall(n) {
      g.stats.walls = Math.max(0, Math.floor(Number(n) || 1) - 1);
      wordSim.endAll(); g.colliders = []; g.pops = []; g.well = null;
      g.hitStopMs = 0; g.freeze = 0; g.pendingBreakout = false; g.transition = null;
      buildWall(); g.wallAge = 2; g.landRow = 99; respawn(g.state === 'grey');
      emit('wall', { walls: g.stats.walls, sp: g.stats.sp, mantra: g.mantra });
    },
    /** Debug shortcuts map the three playable finale beats, not a new ending. */
    jumpToFinaleBeat(beat) {
      this.jumpToWall(8);
      if (beat === 'opening') return;
      if (!['words', 'rings', 'spiral', 'remaining'].includes(beat)) return;
      relapse();
      g.finale.phase = 'locked'; g.finale.age = 30; g.finale.wordClock = 0;
      g.rungs = rungsFor(0, 'grey', g.force);
      emit('finaleLocked', {});
      if (beat !== 'words') {
        startBreakout(g.balls[0]);
        completeBreakout();
        g.breakoutShield = null;
        if (beat === 'spiral') beginFinaleSpiral();
        if (beat === 'remaining') {
          const alive=g.bricks.filter(b=>b.alive);
          const keep=Math.floor(alive.length*.2);
          alive.forEach((br,i)=>{br.alive=Math.floor(i*keep/alive.length)!==Math.floor((i+1)*keep/alive.length);});
        }
      }
      respawn(g.state === 'grey');
    },
    setSaturation(s) { g.sat = clamp(Number(s) || 0, 0, 1); if (g.state === 'grey') g.savedSat = g.sat, g.sat = 0; au('setSaturation', g.sat); },
    setForce(i, v) { g.force[i] = v; g.rungs = rungsFor(g.sat, g.state, g.force); },
    clearForce() { g.force = {}; g.rungs = rungsFor(g.sat, g.state, g.force); },
    setBreakoutN(n) { g.breakoutN = Math.max(1, Math.floor(Number(n) || 12)); },
    setSpeedScale(s) { g.speedScale = clamp(Number(s) || 0.55, 0.2, 3); },
    setNoLose(on) { g.noLose = !!on; },
    setReduced(on) { g.reduced = !!on; },
    /** Dev: the same pop-out, from a random alive spiral brick (broken for real), else a spiral pop from the field centre. Returns the pop. */
    spawnWellNow() {
      const alive = g.bricks.filter(b => b.alive && b.spiral);
      if (g.state === 'colour' && alive.length) { breakBrick(alive[Math.floor(rng() * alive.length)], g.balls[0]); return g.pops[g.pops.length - 1] || null; }
      return popOut({ x: w / 2 - BRICK.w / 2, y: h / 2 - BRICK.h / 2, w: BRICK.w, h: BRICK.h, gif: -1, color: ROW_COLORS[0],
        spiral: WELL_PRESETS[Math.floor(rng() * WELL_PRESETS.length)], hue: Math.floor(rng() * 70) - 35, spin: 0.75 + rng() * 0.5 }, null);
    },
    /** Dev: pop a random alive picture brick (a collider), else a picture pop from the field centre. Returns the pop. */
    popGifNow() {
      const alive = g.bricks.filter(b => b.alive && b.gif >= 0);
      if (g.state === 'colour' && alive.length) { breakBrick(alive[Math.floor(rng() * alive.length)], g.balls[0]); return g.pops[g.pops.length - 1] || null; }
      return popOut({ x: w / 2 - BRICK.w / 2, y: h / 2 - BRICK.h / 2, w: BRICK.w, h: BRICK.h, gif: Math.floor(rng() * 8), color: ROW_COLORS[0] }, null);
    },
    /** Dev: fire a word as if its brick broke, at the first ball (or the field centre). Returns the fx or null (a stamp only). */
    fireWordNow(word) { const b = g.balls[0]; return wordSim.fire(word, b ? b.x : w / 2, b ? Math.min(b.y, h * 0.5) : h * 0.4); },
    relapseNow() { if (g.state === 'colour') lostAll(g.balls[0]); },
    breakoutNow() { startBreakout(g.balls[0]); },
    breakBrick(i) { const br = g.bricks[i]; if (br) breakBrick(br, g.balls[0]); },
    loseBall() { if (g.state === 'colour') lostAll(g.balls[0]); else { g.balls = []; lostAll(); } },
    launchNow() { for (const b of g.balls) if (b.stuck) launch(b); },
  };
}
