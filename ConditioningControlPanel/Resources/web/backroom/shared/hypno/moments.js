/* ============================================================================
 * shared/hypno/moments.js - a game moment -> the host's fullscreen effects and
 * the page's own (CONTRACT 10.13.B and F).
 *
 * A station never names a fullscreen fx id itself. It plays a moment on the
 * frame it SHOWS the result (Law I) and runs the page effect names it gets back
 * at strengthK(ctx). This module fires the host steps through ctx.fx with the
 * table's Normal args. GATES NO LONGER DROP A STEP (owner direction 2026-09-15:
 * the host stopped gating the Back Room's effects on the app toggles): every
 * step of a moment is posted and fx-tunnel always posts its level; the host
 * alone decides what it draws. ctx.gates still dresses the page (a brass star,
 * plain card faces) and never empties a moment. A page always sends Normal
 * values; the host applies Calm (law 6: nobody halves twice). A settings frame
 * below Full still releases a running haze hold.
 *
 * Tunnel vision is a level, not an fx: tunnel(level) posts fx-tunnel on a
 * change at most every 100 ms and re-posts every 1000 ms while it is above 0,
 * so the host's 1500 ms auto-release never fires on a moment that is still on.
 *
 * The two "continuous" moments, wheel.turn and roulette.run, are PLAYED ONCE when
 * they start; the level that follows goes through tunnel(level) every frame. A
 * hold step is idempotent anyway: while this instance still holds that fx id
 * (a roulette.run haze, a roulette.wake spiral), playing it again fires nothing.
 * Haze is Full only: a settings frame below Full, or with reduced motion,
 * releases a running haze hold.
 *
 * Args: the table's colour and strength win over the caller's (the 10.13.F
 * ladder stays fixed); a caller's colour or strength fills in only where the
 * table sets none.
 * ==========================================================================*/

const TUNNEL_GAP_MS = 100;
const TUNNEL_KEEP_MS = 1000;
const BREATH_STEP_MS = 50;
const HEX_RE = /^#[0-9a-fA-F]{6}$/;
const KEY_RE = /^g\d{1,2}$/;

/** Pocket colours (10.13.F): 0 mint, rose rose, plum plum. */
import { kit as sound } from '../sound/kit.js';

export const POCKET_COLORS = Object.freeze({ zero: '#5fffd0', rose: '#ff5fa2', plum: '#9b6bff' });

function deepFreeze(o) {
  if (o && typeof o === 'object' && !Object.isFrozen(o)) { Object.freeze(o); for (const v of Object.values(o)) deepFreeze(v); }
  return o;
}

/*
 * The table. host steps, in order:
 *   { fx, args, gif?: the caller's picture key rides in symbols, from?: the caller's rect goes in args.from,
 *     full?: only at Full intensity, bloomArgs?: args instead when this hand already bloomed,
 *     light?: a small overlay (a word, the flash windows) that may play while holdScreen is on, words?: n dealt word keys }
 *   { tunnel: 'wheel' | 'ball' }       continuous: played once, then the station calls tunnel(level) each frame (wheelTurnLevel, rouletteRunLevel)
 *   { tunnel: 'breath', peak, ms }     timed: this module runs peak x sin(PI x p) over ms, then 0
 * release: the moment first releases every hold this station still has (a roulette landing).
 * page: effect names; { name, when: 'full' | 'wake' } only at Full, or only when play() is given { wake: true }.
 */
export const MOMENTS = deepFreeze({
  'wheel.turn': { host: [{ tunnel: 'wheel' }], page: ['last_turn'] },
  'wheel.land.quiet': { host: [], page: ['quiet_room'] },
  'wheel.land.flash': { host: [{ fx: 'fx.wash', args: { strength: 0.55 } }], page: ['quiet_room'] },
  'wheel.land.gif': { host: [{ fx: 'fx.wash', args: { strength: 0.9 } }, { fx: 'fx.gif_from', args: { ms: 3400 }, gif: true, from: true }],
    page: ['quiet_room'] },
  'wheel.land.jackpot': { host: [
    { fx: 'fx.loom_spiral', args: { preset: 'screen', ms: 4200, alpha: 0.9 } },
    { fx: 'fx.gif_from', args: { ms: 4600, scale: 0.46 }, gif: true, from: true },
    { fx: 'fx.wash', args: { color: '#e8c27a', strength: 1 } },
  ], page: ['quiet_room', 'reveal'] },
  'cards.sit': { host: [], page: ['sit_fan'] },
  // The table beats (2026-09-15). `light` steps are the app's small overlays (a subliminal word, the flash
  // windows), never fullscreen, so they may play while a decision is open (holdScreen). `words: n` sends
  // n of the caller's dealt word keys (s0..s3), so a whisper is one word and a split is two.
  'cards.deal': { host: [{ fx: 'fx.sub_single', args: {}, words: 1, light: true }], page: [] },
  'cards.hit': { host: [{ fx: 'fx.sub_single', args: {}, words: 1, light: true }], page: [] },
  'cards.double': { host: [{ fx: 'fx.gif_burst', args: {}, light: true }], page: [] },
  'cards.split': { host: [{ fx: 'fx.sub_single', args: {}, words: 2, light: true }], page: [] },
  'cards.reveal': { host: [{ fx: 'fx.gif_burst', args: {}, light: true }], page: [] },
  'cards.bust': { host: [], page: [] },   // the whisper is withheld: the silence is the beat; the settle's cards.lose carries the loss
  'cards.bloom': { host: [{ fx: 'fx.gif_from', args: { ms: 4000 }, gif: true, from: true }, { fx: 'fx.wash', args: { color: '#ff5fa2', strength: 0.8 } }],
    page: ['ace_glow'], bloom: true },
  'cards.win': { host: [{ fx: 'fx.wash', args: { color: '#5fffd0', strength: 0.7 }, gif: true, bloomArgs: { color: '#5fffd0', strength: 0.9 } }],
    page: ['win_tunnel', 'chip_vortex'], settles: true },
  'cards.dealer_bust': { host: [{ fx: 'fx.gif_burst', args: {} }, { fx: 'fx.wash', args: { color: '#5fffd0', strength: 0.8 }, gif: true, bloomArgs: { color: '#5fffd0', strength: 0.9 } }],
    page: ['win_tunnel', 'chip_vortex'], settles: true },
  'cards.streak': { host: [{ fx: 'fx.sub_pair', args: {}, words: 2 }, { fx: 'fx.wash', args: { color: '#5fffd0', strength: 0.9 }, gif: true, bloomArgs: { color: '#5fffd0', strength: 0.9 } }],
    page: ['win_tunnel', 'chip_vortex'], settles: true },
  'cards.sweep': { host: [{ fx: 'fx.gif_storm', args: {} }, { fx: 'fx.wash', args: { color: '#e8c27a', strength: 1 }, gif: true, bloomArgs: { color: '#e8c27a', strength: 1 } }],
    page: ['win_tunnel', 'chip_vortex'], settles: true },
  'cards.lose': { host: [{ tunnel: 'breath', peak: 0.75, ms: 2600 }], page: ['chip_vortex'], settles: true },
  'cards.push': { host: [], page: [], settles: true },
  'roulette.run': { host: [{ tunnel: 'ball' }, { fx: 'fx.haze', args: { hold: true }, full: true }],
    page: ['lighthouse', 'fret_rattle', { name: 'velvet_wake', when: 'full' }] },
  'roulette.wake': { host: [{ fx: 'fx.loom_spiral', args: { preset: 'wake', hold: true, alpha: 0.65 } }], page: ['turret_whirl'] },
  'roulette.land.miss': { release: true, host: [], page: ['chip_vortex'] },
  'roulette.land.win': { release: true, host: [{ fx: 'fx.wash', args: { strength: 0.6 } }], page: ['chips_in'] },
  'roulette.land.big': { release: true, host: [{ fx: 'fx.wash', args: { strength: 1 } }, { fx: 'fx.gif_from', args: { ms: 3600 }, gif: true, from: true }],
    page: ['chips_in', { name: 'pulled_pair', when: 'wake' }] },
});

/** Which app toggle each id used to stand behind (CONTRACT 10.13.A). Kept for the record: nothing here reads it to
 *  drop a step any more; the host enforces its own toggles per primitive. */
export const GATE_OF = Object.freeze({ 'fx.wash': 'flash', 'fx.gif_from': 'flash', 'fx.loom_spiral': 'spiral', 'fx.haze': 'brainDrain',
  'fx.gif_burst': 'flash', 'fx.gif_storm': 'flash', 'fx.sub_single': 'subliminal', 'fx.sub_pair': 'subliminal' });
const WORD_RE = /^s\d$/;

/** Which landing a Daily Daze result gets. */
export function wheelSize(result) {
  const r = result || {};
  if (r.jackpot === true || r.jackpotWon === true) return 'jackpot';
  const pay = Number(r.pay) || 0;
  if (r.snoozed === true || pay <= 3) return 'quiet';
  return pay <= 12 ? 'flash' : 'gif';
}

/** 0.5 under reduced motion or Calm, else 1. For what the page draws itself; host args stay Normal. */
export const strengthK = (ctx) => (ctx && (ctx.reduced || ctx.intensity === 'calm') ? 0.5 : 1);

/** wheel.turn: the tunnel level for the long last turn's stage dim (0..1). */
export const wheelTurnLevel = (dim) => clamp01(Number(dim) * 0.85);
/** roulette.run: the tunnel level for the ball's speed in rad/s. */
export const rouletteRunLevel = (ballSpeed) => 0.75 * clamp01(0.35 + Math.abs(Number(ballSpeed) || 0) / 8);
/** A pocket's colour from state.rose (the rose numbers). */
export const pocketColor = (pocket, rose) => (Number(pocket) === 0 ? POCKET_COLORS.zero
  : (Array.isArray(rose) && rose.map(Number).includes(Number(pocket)) ? POCKET_COLORS.rose : POCKET_COLORS.plum));

/** A canvas-local rect (CSS px) in viewport CSS px, the space fx.gif_from's `from` is in. */
export function viewportRect(el, x, y, w, h) {
  const r = el && typeof el.getBoundingClientRect === 'function' ? el.getBoundingClientRect() : { left: 0, top: 0 };
  return { x: r.left + x, y: r.top + y, w, h };
}
/** A w x h box centred on a viewport point (the wheel: scene.project('landed') as 60 x 44). */
export const boxAround = (cx, cy, w = 60, h = 44) => ({ x: cx - w / 2, y: cy - h / 2, w, h });

function clamp01(v) { return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0; }

function cleanRect(r) {
  if (!r || typeof r !== 'object') return null;
  const o = { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.w), h: Math.round(r.h) };
  return [o.x, o.y, o.w, o.h].every(Number.isFinite) && o.w >= 8 && o.h >= 8 ? o : null;
}

const nowMs = () => Date.now();

/**
 * @param {Object} ctx      a station ctx (fx, fxRelease, fxTunnel, gates, onSettings, intensity, reduced, log)
 * @param {{station?: string}} o
 */
export function createMoments(ctx, { station = '' } = {}) {
  const c = ctx || {};
  const say = (m) => { try { if (c.bridge && typeof c.bridge.log === 'function') c.bridge.log('warn', m); else if (typeof c.log === 'function') c.log(m); } catch (e) { /* noop */ } };
  const holds = new Map();   // token -> fxId
  let held = false, bloomed = false, disposed = false;
  let want = 0, posted = 0, lastPostAt = -Infinity, gapTimer = 0, keepTimer = 0, breathTimer = 0;

  function postTunnel(level) {
    if (typeof c.fxTunnel === 'function') { try { c.fxTunnel(level); } catch (e) { /* noop */ } }
    posted = level; lastPostAt = nowMs();
    if (posted > 0 && !keepTimer) {
      keepTimer = setInterval(() => {
        if (posted <= 0) { clearInterval(keepTimer); keepTimer = 0; return; }
        if (nowMs() - lastPostAt >= TUNNEL_KEEP_MS) postTunnel(posted);
      }, TUNNEL_KEEP_MS / 4);
    } else if (posted <= 0 && keepTimer) { clearInterval(keepTimer); keepTimer = 0; }
  }
  function flushTunnel() {
    if (want === posted) return;
    const since = nowMs() - lastPostAt;
    if (since >= TUNNEL_GAP_MS) { postTunnel(want); return; }
    if (!gapTimer) gapTimer = setTimeout(() => { gapTimer = 0; if (!disposed) flushTunnel(); }, TUNNEL_GAP_MS - since);
  }
  function tunnelNow0() {
    if (gapTimer) { clearTimeout(gapTimer); gapTimer = 0; }
    want = 0;
    if (posted > 0) postTunnel(0);
  }
  function stopBreath() { if (breathTimer) { clearInterval(breathTimer); breathTimer = 0; } }
  function setTunnel(level) {
    let v = Math.round(clamp01(Number(level)) * 100) / 100;
    if (typeof c.fxTunnel !== 'function') v = 0;
    if (held && v > 0) return;
    want = v;
    flushTunnel();
  }
  function breath(peak, ms) {
    stopBreath();
    const t0 = nowMs();
    const step = () => {
      const p = (nowMs() - t0) / ms;
      if (p >= 1 || disposed || held) { stopBreath(); setTunnel(0); return; }
      setTunnel(peak * Math.sin(Math.PI * p));
    };
    breathTimer = setInterval(step, BREATH_STEP_MS);
    step();
  }
  function releaseAll() {
    for (const token of Array.from(holds.keys())) api.release(token);
  }

  // A settings frame: a gate going off changes nothing here (the host enforces); haze is Full only.
  const unsub = typeof c.onSettings === 'function' ? c.onSettings((f) => {
    if (!f) return;
    const belowFull = ('intensity' in f && f.intensity !== 'full') || f.reduced === true;
    for (const [token, fxId] of Array.from(holds)) {
      if (fxId === 'fx.haze' && belowFull) api.release(token);
    }
  }) : null;
  const holding = (fxId) => { for (const v of holds.values()) { if (v === fxId) return true; } return false; };
  /** The kit under a host step it has just fired: the spiral's hum for as long as the spiral (a hold runs until its
   *  release), a 6 s breath under the haze, the whisper bed under each subliminal word (a chain climbs a tone a word). */
  function cueFor(step, args, symbols) {
    if (step.fx === 'fx.loom_spiral') sound.play('spiral', { ms: args.hold ? 0 : args.ms });
    else if (step.fx === 'fx.haze') sound.play('breath');
    else if (step.words > 0) { const n = Math.min(3, Array.isArray(symbols) ? symbols.length : 1); for (let i = 0; i < n; i++) sound.play('word', { index: i, at: i * 0.9 }); }
  }

  const api = {
    /**
     * Fire moment `id`. Returns the tokens fired, the page effects to run, and whether holdScreen stopped the host steps.
     * `color` and `strength` fill in a wash only where the table sets none; `from` and `gif` ride on the steps that take them;
     * `words` (dealt word keys s0..s3) feed the steps that take `words: n`. While the screen is held only `light` steps play.
     */
    play(id, { color, strength, from, gif, words, wake = false } = {}) {
      const m = Object.prototype.hasOwnProperty.call(MOMENTS, id) ? MOMENTS[id] : null;
      const out = { tokens: [], page: [], held: false };
      if (!m) { say('moments: unknown moment ' + id + (station ? ' (' + station + ')' : '')); return out; }
      if (disposed) return out;
      const full = c.intensity === 'full' && !c.reduced;
      out.page = m.page.filter((p) => typeof p === 'string' || (p.when === 'full' ? full : p.when === 'wake' ? !!wake : true))
        .map((p) => (typeof p === 'string' ? p : p.name));
      if (m.release) releaseAll();
      const afterBloom = bloomed;
      if (m.bloom) bloomed = true;
      if (m.settles) bloomed = false;
      if (held && m.host.some((s) => !s.light)) out.held = true;
      const wordKeys = Array.isArray(words) ? words.filter((w) => typeof w === 'string' && WORD_RE.test(w)) : [];
      for (const step of m.host) {
        if (held && !step.light) continue;
        if (step.tunnel === 'breath') { breath(step.peak, step.ms); sound.play('breath', { ms: step.ms }); continue; }
        if (step.tunnel) continue;
        if (step.full && !full) continue;
        if (typeof c.fx !== 'function') continue;
        const bloomStep = afterBloom && step.bloomArgs;
        const args = { ...(bloomStep ? step.bloomArgs : step.args) };
        if (args.hold && holding(step.fx)) continue;   // still held from an earlier play: one hold per fx id
        if (step.fx === 'fx.wash') {
          if (!args.color && typeof color === 'string' && HEX_RE.test(color)) args.color = color;
          if (!Number.isFinite(args.strength) && Number.isFinite(strength)) args.strength = Math.min(1, Math.max(0.1, strength));
        }
        if (step.from) { const r = cleanRect(from); if (r) args.from = r; }
        let symbols = step.gif && !bloomStep && typeof gif === 'string' && KEY_RE.test(gif) ? [gif] : undefined;
        if (step.words > 0 && wordKeys.length) symbols = wordKeys.slice(0, step.words);
        let p = null;
        try { p = c.fx(step.fx, symbols, args); } catch (e) { p = null; }
        if (p && typeof p.catch === 'function') p.catch(() => {});
        const token = p && typeof p.token === 'string' ? p.token : null;
        cueFor(step, args, symbols);
        if (!token) continue;
        out.tokens.push(token);
        if (args.hold) holds.set(token, step.fx);
      }
      return out;
    },
    /** Tunnel vision 0..1 (Normal values). Throttled; ignored while the screen is held. */
    tunnel(level) {
      if (disposed) return;
      if (Number(level) > 0) stopBreath();   // a live level takes over from a running breath; 0 leaves it be
      else if (breathTimer) return;
      setTunnel(level);
    },
    /** True while a timed tunnel breath (cards.lose) is still running: cards hold the next deal on it. */
    breathing() { return !!breathTimer; },
    /** Cards: while on, play() fires no host fx and tunnel(> 0) is ignored. */
    holdScreen(on) {
      held = !!on;
      if (held) { stopBreath(); tunnelNow0(); }
    },
    /** fx-release each token (a string or a list). */
    release(tokens) {
      const list = Array.isArray(tokens) ? tokens : [tokens];
      for (const t of list) {
        if (typeof t !== 'string' || !t) continue;
        if (holds.get(t) === 'fx.loom_spiral') sound.stop('spiral');
        holds.delete(t);
        if (typeof c.fxRelease === 'function') { try { c.fxRelease(t); } catch (e) { /* noop */ } }
      }
    },
    /** Suspend or close: tunnel 0 now, every hold released, every timer stopped. */
    cancel() {
      stopBreath(); sound.stop('spiral'); sound.stop('breath'); sound.stop('word');
      tunnelNow0();
      if (keepTimer) { clearInterval(keepTimer); keepTimer = 0; }
      releaseAll();
      bloomed = false;
    },
    dispose() {
      if (disposed) return;
      api.cancel();
      disposed = true;
      if (unsub) { try { unsub(); } catch (e) { /* noop */ } }
    },
    /** Test seam. */
    debug() { return { held, bloomed, holds: holds.size, tunnel: posted, want, disposed, breathing: !!breathTimer }; },
  };
  return api;
}
