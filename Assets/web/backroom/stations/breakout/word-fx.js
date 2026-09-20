/* ============================================================================
 * stations/breakout/word-fx.js - the word triggers: one diegetic effect per
 * subliminal word (owner, 2026-09-19: "map effects to specific triggers").
 *
 * About one plain brick in six carries a word (game.js buildWall). Breaking it in
 * COLOUR fires the word's effect; in GREY the brick is a special (+3) and no
 * effect fires. Each word lives in its own module under words/ and exports:
 *
 *   {
 *     key: 'SINK', heavy: true, dur: 1.6,
 *     sim:    { start(g, fx, api), tick(g, fx, dt, api), end(g, fx, api) },
 *     render: { world(ctx, s, fx, R), over(ctx, s, fx, R), post(ctx, s, fx, R) },
 *     sound(synth, fx),
 *   }
 *
 * fx = { key, word, t, dur, phase (t/dur, 0..1), heavy, x, y, data: {} } and is
 * the same object in the sim, the renderer (via the snapshot's s.fx.active)
 * and the sound cue. `fx.t` runs on wall-clock seconds (not the sim's slowed
 * time), so a word that slows the game does not slow itself; it does pause
 * with the sim's own holds (g.freeze, g.hitStopMs, the transitions). The heavy
 * gap is measured on g.fx.wall, the same clock.
 *
 * sim.tick sets fields on g.mod (reset to MOD_DEFAULTS before each round of
 * ticks): timeScale (the lowest wins), paddleW (multiplier), ballSpeed
 * (multiplier), safe (the floor bounces, the ball cannot be lost), autopilot
 * (the paddle follows the lowest live ball; the player's input is ignored),
 * hideBricks (0..1, the bricks fade out of sight but still collide),
 * zoom (a field zoom the renderer applies around the ball, 1 = none).
 * api = { emit, au, rng, addSat, clamp, lerp, W, H }.
 *
 * render.world runs right after the camera transform, before the background,
 * and is NOT wrapped in save/restore, so a transform on ctx (scale, rotate,
 * translate) moves the whole world for the rest of the frame; leave fill and
 * alpha state as you found it. (over and post are wrapped.)
 * render.over runs after the overlays, still in field space (0..W x 0..H):
 * vignettes, mists, flashes. render.post runs after the post pass in canvas
 * pixel space (echoes, whole-frame tricks). R = { W, H, cw, ch, scale, ox, oy,
 * mix, dt, fxDt, P, stamps, cam, col, PINK, MINT, VIOLET, WHITE, BG, GOLD, FONT,
 * reduced, rng, media, frame(): a copy of the current canvas (post only) }.
 *
 * sound(synth, fx): synth = { ctx, now, play, tone, noise, bus, duck, glide,
 * SEMI, ROOT_HZ }. play(voices, t, dest); tone(hz, dur, level, opts);
 * noise(hz, dur, level, opts); duck(amount 0..1, holdS, releaseS) lowers the
 * bed and the other cues and brings them back.
 *
 * Rules (owner): a heavy word ducks the running soft ones; one heavy every
 * HEAVY_GAP_S at most, a second heavy inside the gap is a word stamp only; a
 * soft word while a heavy runs is a stamp only; the ball never dies during a
 * word (every module sets g.mod.safe while it runs); reduced motion keeps the
 * sound and the tint and drops shake, zoom and echoes.
 * ==========================================================================*/

import SINK from './words/sink.js';
import DROP from './words/drop.js';
import RELAX from './words/relax.js';
import LETGO from './words/let-go.js';
import DEEPER from './words/deeper.js';
import BLANK from './words/blank.js';

export const HEAVY_GAP_S = 4;
export const WORD_BRICK_P = 1 / 6;   // was 1/3; halved (owner, 2026-09-19: "a bit too many subliminals now")

export const WORD_FX = Object.freeze({ SINK, DROP, RELAX, 'LET GO': LETGO, DEEPER, BLANK });
export const WORD_KEYS = Object.freeze(Object.keys(WORD_FX));

/* Keyword families: a mod's own words land on the twelve by the first family that matches (order matters). */
const FAMILIES = [
  ['SINK', /\b(SINK|SLEEP|SINKING|DOWN)\b/],
  ['DROP', /\b(DROP|DROPPING|FALL)\b/],
  ['RELAX', /\b(RELAX|BREATHE|CALM|SOFT|EASE)\b/],
  ['LET GO', /\b(LET\s*GO|SURRENDER|GIVE\s*IN|RELEASE)\b/],
  ['DEEPER', /\b(DEEPER|DEEP|FURTHER)\b/],
  ['BLANK', /\b(BLANK|FREEZE|EMPTY|NOTHING|STILL)\b/],
];

/** The trigger key for a word, or null when it maps to nothing (a plain stamp). */
export function wordKey(text) {
  const t = String(text || '').toUpperCase().replace(/[^A-Z ]+/g, ' ').replace(/\s+/g, ' ').trim();
  if (!t) return null;
  if (WORD_FX[t]) return t;
  for (const [key, re] of FAMILIES) if (re.test(t)) return key;
  return null;
}

export const MOD_DEFAULTS = Object.freeze({ timeScale: 1, paddleW: 1, ballSpeed: 1, safe: false, autopilot: false, hideBricks: 0, zoom: 1 });

/** A fresh g.mod for a round of ticks. */
export function freshMod() { return { ...MOD_DEFAULTS }; }

/** The sim's view: fire, advance and end word effects on g. Called from game.js only. */
export function createWordSim(g, api) {
  const def = k => WORD_FX[k] || null;
  const call = (fx, hook, ...args) => { const d = def(fx.key); try { if (d && d.sim && typeof d.sim[hook] === 'function') d.sim[hook](g, fx, ...args); } catch (e) { /* a word's bug never stops the game */ } };
  function end(fx) { if (fx.done) return; fx.done = true; fx.phase = 1; call(fx, 'end', api); }
  const wall = () => (Number.isFinite(g.fx.wall) ? g.fx.wall : 0);
  return {
    /** Break of a word brick (or the dev button). Returns the fx when it fired, null for a stamp only. */
    fire(word, x, y) {
      const key = wordKey(word), d = def(key);
      const base = { word: String(word), key, x, y, heavy: !!(d && d.heavy), dur: d ? d.dur : 0 };
      if (!d || g.state !== 'colour') { api.emit('word', { ...base, fired: false }); return null; }
      const active = g.fx.active.filter(f => !f.done);
      const heavyRunning = active.some(f => f.heavy);
      if (d.heavy && (wall() - g.fx.lastHeavyAt < HEAVY_GAP_S || heavyRunning)) { api.emit('word', { ...base, fired: false }); return null; }
      if (!d.heavy && heavyRunning) { api.emit('word', { ...base, fired: false }); return null; }
      if (d.heavy) { for (const f of active) end(f); g.fx.lastHeavyAt = wall(); }
      else { for (const f of active) if (f.key === key) end(f); }        // the same soft word restarts rather than stacks
      const fx = { ...base, t: 0, phase: 0, done: false, data: {} };
      g.fx.active = g.fx.active.filter(f => !f.done).concat(fx);
      call(fx, 'start', api);
      api.emit('word', { ...base, fired: true, fx });
      return fx;
    },
    /** Wall-clock advance; sets g.mod for this frame. */
    advance(dt) {
      g.fx.wall = wall() + dt;                            // the word clock: wall seconds, paused only by the sim's own holds
      g.mod = freshMod();
      for (const fx of g.fx.active) {
        if (fx.done) continue;
        fx.t += dt; fx.phase = fx.dur > 0 ? Math.min(1, fx.t / fx.dur) : 1;
        call(fx, 'tick', dt, api);
        if (fx.t >= fx.dur) end(fx);
      }
      if (g.fx.active.some(f => f.done)) g.fx.active = g.fx.active.filter(f => !f.done);
    },
    endAll() { for (const fx of g.fx.active) end(fx); g.fx.active = []; g.mod = freshMod(); },
  };
}
