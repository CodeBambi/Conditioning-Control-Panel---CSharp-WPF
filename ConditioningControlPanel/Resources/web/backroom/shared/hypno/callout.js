/* ============================================================================
 * shared/hypno/callout.js - the win callout, the glyph highlight timings, the
 * lone subliminal word and the dead-spin settle, shared by every Back Room
 * game (owner direction, 2026-09-15).
 *
 * CONTRACT (stable; other lanes build against it):
 *   createCallout({ mount, lex, ctx, moments, emi, font, seed, cues })
 *                                      one per station; mount is the station's root element. ctx is the station
 *                                      ctx (fxTunnel, gates, reduced, intensity); moments an existing
 *                                      createMoments(ctx) to share the tunnel path with, else one is made here.
 *                                      emi: { react(kind) } or an emi-idle { trigger(kind) } for the settle's
 *                                      shrug / wink; optional anchor() -> { x, y } (viewport px) for the bark.
 *   .show(key, fallback, { tier })     a diegetic name, e.g. ('br_callout_double_spin', 'Double Spin', { tier: 'small' })
 *                                      tier: 'small' | 'big' | 'hero'. Returns { done: Promise } resolving when the
 *                                      text has dissolved (CALLOUT_MS). A show while one is up replaces it.
 *                                      DRAWN: centred over the station, 60% -> 130% toward the viewer over 1 s,
 *                                      hold 200 ms, dissolve 400 ms under a chromatic smear (two offset copies).
 *                                      small ~7vh; big 10vh, gold rim; hero 14vh, rim and a short shake.
 *   .word(text, { chain, reversed, seed })
 *                                      the subliminal word BIG at centre (~12vh), same zoom, house colours
 *                                      (pink to purple, sometimes a two-stop gradient, seeded when `seed` is given).
 *                                      In 80 ms, hold 500 ms, out 400 ms. `chain` (1..2 more words) plays back to
 *                                      back, WORD_GAP_MS between onsets, colours drifting along the chain. A creeping
 *                                      tunnel (moments.tunnel -> fx-tunnel) breathes once for the chain: in 500 ms
 *                                      to 0.6, held to the last onset, out 500 ms. Each word: 'clicker' cue, spoken
 *                                      through speechSynthesis (rate 0.85, pitch 0.8), the 'word' cue under it.
 *                                      1 in 100 per word (the page's own seeded rng, never the payout): the word is
 *                                      reversed, mirrored and spoken as its reversed spelling at rate 0.7.
 *                                      Returns { done: Promise } resolving after the last word fades.
 *   .settle()                          a dead spin: a 600 ms light band sweeps the station at 20%, the 'settle' cue,
 *                                      and EMI shrugs / winks with one of eight rotating barks (br_emi_dead_1..8).
 *   .cancel()                          Law VI: suspend or leave drops text, words, tunnel and speech at once
 *   .dispose()
 *   .debug()                           { shown, words, tunnel, cues, barks, speech } for the tests and the smokes
 *
 * The flow every game follows on the frame the result SHOWS (Law I):
 *   0 ms                 the landing thud
 *   0..HIGHLIGHT_MS      the winning glyphs glow, HIGHLIGHT_GAP_MS apart in landing order (DOM: class GLYPH_HIT;
 *                        three.js: the station's own rim light, same timings)
 *   FX_DELAY_MS          the callout and the host effect start together
 *   FX_DELAY_MS + CALLOUT_MS   the next press unlocks (host overlays never block it; the jackpot hero holds itself)
 *
 * Sound: the cues go through the SFX kit (shared/sound/kit.js: 'clicker', 'word' { index }, 'settle') when it is
 * there, else an inline WebAudio click, a filtered-noise shimmer and a soft settle. Nothing here throws when
 * audio, speech or the DOM is missing (node tests run on small fakes).
 * ==========================================================================*/
import { createMoments } from './moments.js';
import { fitText } from '../text/wrap.js';

export const HIGHLIGHT_MS = 400;
export const HIGHLIGHT_GAP_MS = 80;
export const FX_DELAY_MS = 400;
export const CALLOUT_MS = 1600;
export const GLYPH_HIT = 'br-glyph-hit';
export const TIERS = Object.freeze(['small', 'big', 'hero']);

/** The announcer's zoom: 60% -> 130% over zoomMs, hold, then the dissolve (sums to CALLOUT_MS). */
export const ZOOM = Object.freeze({ from: 0.6, to: 1.3, zoomMs: 1000, holdMs: 200, outMs: 400 });
/** The lone word's fade and the chain's pace. */
export const WORD_IN_MS = 80;
export const WORD_HOLD_MS = 500;
export const WORD_OUT_MS = 400;
export const WORD_MS = WORD_IN_MS + WORD_HOLD_MS + WORD_OUT_MS;   // 980: one word, onset to gone
export const WORD_GAP_MS = 500;                                   // onset to onset along a chain
export const WORD_TUNNEL_MS = 500;                                // the tunnel's way in, and its way out
export const WORD_TUNNEL_LEVEL = 0.6;
export const WORD_SIZE_VH = 12;
export const WORD_MAX_LINES = 3;                                  // a long phrase wraps, it is never squeezed
export const WORD_WRAP_AT = 12;                                   // past this many characters it takes 2 lines
export const WORD_FIT_VW = 0.9;                                   // the block sits inside 90% of the viewport
const WORD_LINE_H = 1.05;
const GLYPH_W = 0.5;                                              // average glyph width of the display stack, in em
export const REVERSE_ODDS = 100;                                  // 1 in 100 per word
export const SETTLE_MS = 600;
export const BARK_MS = 2400;
export const TIER_VH = Object.freeze({ small: 7, big: 10, hero: 14 });
const TUNNEL_STEP_MS = 50;
const SPEECH = Object.freeze({ rate: 0.85, pitch: 0.8, reversedRate: 0.7 });
const FONT_STACK = '"Bahnschrift Condensed", "Bahnschrift SemiBold Condensed", "Arial Narrow", "Roboto Condensed", Impact, "Segoe UI", Arial, sans-serif';

/** EMI's dead-spin barks: playful, never mocking. Rotates in order, so eight spins hear eight lines. */
export const DEAD_BARKS = Object.freeze([
  { key: 'br_emi_dead_1', fallback: "Next one's yours." },
  { key: 'br_emi_dead_2', fallback: 'The reels are just warming up.' },
  { key: 'br_emi_dead_3', fallback: 'I felt that one wobble. So close.' },
  { key: 'br_emi_dead_4', fallback: 'A quiet spin. The loud ones are coming.' },
  { key: 'br_emi_dead_5', fallback: 'Still here. Still cheering.' },
  { key: 'br_emi_dead_6', fallback: 'That was a practice spin. Everyone gets those.' },
  { key: 'br_emi_dead_7', fallback: 'Shh. I think the jackpot is listening.' },
  { key: 'br_emi_dead_8', fallback: 'One more. I have a good feeling.' },
]);

/* ------------------------------------------------------------------ the page's own rng
 * mulberry32: small, seedable, good enough for a colour and a 1-in-100 draw. Never the payout's. */
export function seededRng(seed) {
  let a = (Number(seed) >>> 0) || 0x9e3779b9;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Folds v back inside lo..hi (a chain's drift bounces off the edges of the flavour instead of leaving it). */
function fold(v, lo, hi) {
  const span = hi - lo, t = ((v - lo) % (2 * span) + 2 * span) % (2 * span);
  return lo + (t <= span ? t : 2 * span - t);
}
/** House flavour: pink (hue 335) to purple (hue 270), and anything between. `drift` walks the hue along a chain. */
function houseColor(rng, base, drift) {
  const hue = fold(base + drift, 270, 335);
  const sat = 86 + rng() * 14, light = 58 + rng() * 12;
  const c1 = `hsl(${hue.toFixed(1)} ${sat.toFixed(0)}% ${light.toFixed(0)}%)`;
  if (rng() < 0.4) {
    const hue2 = (((hue + (rng() < 0.5 ? -1 : 1) * (14 + rng() * 22)) % 360) + 360) % 360;
    return { hue, stops: [c1, `hsl(${hue2.toFixed(1)} ${sat.toFixed(0)}% ${(light + 6).toFixed(0)}%)`] };
  }
  return { hue, stops: [c1] };
}

const reverseText = (s) => Array.from(String(s)).reverse().join('');

/**
 * The word beat's block: a long subliminal or trigger phrase wraps onto 2 or 3 lines and the tier font
 * steps down until the widest line fits WORD_FIT_VW of the viewport AT THE ZOOM'S PEAK (the beat scales to
 * ZOOM.to, so the block is fitted to that, not to its resting size). Short words keep WORD_SIZE_VH and one
 * line, exactly as before. PURE apart from the viewport read (`view` overrides it) and `measure`, which the
 * page hands in as a real canvas metric for the display font; without one an average-glyph estimate is used.
 * @returns {{sizeVh:number, lines:string[]}}
 */
export function wordBlock(text, { sizeVh = WORD_SIZE_VH, maxLines = WORD_MAX_LINES, view = null, measure = null } = {}) {
  const str = String(text == null ? '' : text);
  const vw = Number(view && view.w) || (typeof globalThis.innerWidth === 'number' ? globalThis.innerWidth : 1280);
  const vh = Number(view && view.h) || (typeof globalThis.innerHeight === 'number' ? globalThis.innerHeight : 720);
  const px = (size) => size * vh / 100;
  const metric = typeof measure === 'function' ? (t, size) => measure(String(t), px(size))
    : (t, size) => String(t).length * px(size) * GLYPH_W;
  const fit = fitText(str, { measure: metric, width: vw * WORD_FIT_VW / ZOOM.to, maxLines,
    minLines: str.replace(/\s+/g, ' ').trim().length > WORD_WRAP_AT ? 2 : 1,
    min: 4, max: sizeVh, lineHeight: WORD_LINE_H });
  return { sizeVh: fit.size, lines: fit.lines };
}

/**
 * The plan for one word or chain: onsets, colours, the reversal draw, the tunnel breath. Pure, so a replay with
 * the same seed shows the same thing. `reversed: true` forces the easter egg on every word (dev.html).
 */
export function wordPlan(text, { chain = [], reversed = false, seed } = {}) {
  const words = [String(text)].concat(Array.isArray(chain) ? chain.slice(0, 2).map(String) : []).filter((w) => w.length > 0);
  const rng = seededRng(seed === undefined || seed === null ? Math.floor(Math.random() * 4294967296) : seed);
  const base = 272 + rng() * 60;                                  // purple .. pink
  const step = (rng() < 0.5 ? -1 : 1) * (12 + rng() * 16);        // the drift along the chain
  const items = words.map((w, i) => {
    const flip = reversed === true || rng() < 1 / REVERSE_ODDS;
    const colour = houseColor(rng, base, step * i);
    return { text: w, shown: flip ? reverseText(w) : w, spoken: flip ? reverseText(w).toLowerCase() : w, reversed: flip,
      onsetMs: i * WORD_GAP_MS, inMs: WORD_IN_MS, holdMs: WORD_HOLD_MS, outMs: WORD_OUT_MS, hue: colour.hue, stops: colour.stops, index: i };
  });
  const lastOnset = (items.length - 1) * WORD_GAP_MS;
  return {
    words: items,
    totalMs: lastOnset + WORD_MS,
    tunnel: { inMs: WORD_TUNNEL_MS, holdUntilMs: lastOnset, outMs: WORD_TUNNEL_MS, level: WORD_TUNNEL_LEVEL, endMs: Math.max(WORD_TUNNEL_MS, lastOnset) + WORD_TUNNEL_MS },
  };
}

/** The tunnel breath's level at t ms into a plan (in over inMs, held to the last onset, out over outMs). */
export function tunnelLevelAt(plan, t) {
  const b = plan.tunnel;
  if (t <= 0 || t >= b.endMs) return 0;
  if (t < b.inMs) return b.level * (t / b.inMs);
  const outStart = Math.max(b.inMs, b.holdUntilMs);
  if (t < outStart) return b.level;
  return b.level * (1 - (t - outStart) / b.outMs);
}

/* ------------------------------------------------------------------ the cues
 * The SFX kit when it loads; else an inline click, shimmer and settle. Every play is logged for the tests. */
function resolveKit(m) {
  if (!m) return null;
  const pick = (o) => (o && typeof o.play === 'function' ? o : o && typeof o.cue === 'function' ? { play: (c, x) => o.cue(c, x) } : null);
  return pick(m) || pick(m.default) || (typeof m.createKit === 'function' ? pick(m.createKit()) : null);
}
function createCues(log) {
  let kitP = null, ac = null, noise = null;
  const load = () => kitP || (kitP = import('../sound/kit.js').then(resolveKit).catch(() => null));
  function graph() {
    if (ac) return ac.state === 'closed' ? null : ac;
    const AC = globalThis.AudioContext || globalThis.webkitAudioContext;
    if (!AC) return null;
    try {
      ac = new AC();
      const b = ac.createBuffer(1, ac.sampleRate / 2, ac.sampleRate), d = b.getChannelData(0);
      for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
      noise = b;
    } catch (e) { ac = null; }
    return ac;
  }
  function inline(cue, opts) {
    const c = graph();
    if (!c) return;
    const t = c.currentTime, g = c.createGain();
    g.connect(c.destination);
    if (cue === 'clicker') {
      const s = c.createBufferSource(), f = c.createBiquadFilter();
      s.buffer = noise; f.type = 'highpass'; f.frequency.value = 2400;
      g.gain.setValueAtTime(0.28, t); g.gain.exponentialRampToValueAtTime(0.001, t + 0.03);
      s.connect(f); f.connect(g); s.start(t); s.stop(t + 0.04);
    } else if (cue === 'word') {
      const s = c.createBufferSource(), f = c.createBiquadFilter(), k = Math.max(0, Math.min(2, Number(opts && opts.index) || 0));
      s.buffer = noise; f.type = 'bandpass'; f.Q.value = 9;
      f.frequency.setValueAtTime(700 + 250 * k, t); f.frequency.exponentialRampToValueAtTime(3200 + 400 * k, t + 0.35);
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.11, t + 0.06); g.gain.exponentialRampToValueAtTime(0.001, t + 0.42);
      s.connect(f); f.connect(g); s.start(t); s.stop(t + 0.45);
    } else if (cue === 'settle') {
      const o = c.createOscillator();
      o.type = 'sine'; o.frequency.setValueAtTime(220, t); o.frequency.exponentialRampToValueAtTime(110, t + 0.6);
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.07, t + 0.08); g.gain.exponentialRampToValueAtTime(0.001, t + 0.6);
      o.connect(g); o.start(t); o.stop(t + 0.62);
    }
  }
  return {
    play(cue, opts = {}) {
      log.push({ cue, ...opts, at: Date.now() });
      load().then((kit) => { try { if (kit) kit.play(cue, opts); else inline(cue, opts); } catch (e) { /* noop */ } }).catch(() => {});
    },
    close() { if (ac) { try { ac.close(); } catch (e) { /* noop */ } ac = null; } },
  };
}

/* ------------------------------------------------------------------ speech */
function speak(text, rate, log) {
  try {
    const S = globalThis.speechSynthesis, U = globalThis.SpeechSynthesisUtterance;
    if (!S || typeof U !== 'function') { log.push({ text, rate, spoken: false }); return false; }
    S.cancel();
    const u = new U(String(text));
    u.rate = rate; u.pitch = SPEECH.pitch;
    S.speak(u);
    log.push({ text, rate, spoken: true });
    return true;
  } catch (e) { log.push({ text, rate, spoken: false }); return false; }
}
function hush() { try { const S = globalThis.speechSynthesis; if (S && typeof S.cancel === 'function') S.cancel(); } catch (e) { /* noop */ } }

/* ------------------------------------------------------------------ the DOM */
const CSS = `
.br-callout{position:absolute;inset:0;z-index:60;pointer-events:none;overflow:hidden}
.br-callout-text{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%) scale(.6);opacity:0;font-weight:800;line-height:1;
  letter-spacing:.02em;text-align:center;white-space:nowrap;max-width:96vw;color:#ffe6f6;text-shadow:0 2px 12px #0d0616,0 0 28px #ff5fa280;
  will-change:transform,opacity}
.br-callout-text[data-tier=big]{-webkit-text-stroke:2px #e8c27a;text-shadow:0 2px 12px #0d0616,0 0 26px #e8c27a99}
.br-callout-text[data-tier=hero]{-webkit-text-stroke:3px #e8c27a;text-shadow:0 2px 14px #0d0616,0 0 36px #e8c27acc,0 0 70px #ff5fa266}
.br-callout-text[data-lines]{white-space:pre-line;line-height:1.05}
.br-callout-text .br-callout-ghost{position:absolute;inset:0;opacity:0;mix-blend-mode:screen;-webkit-text-stroke:0;text-shadow:none}
.br-callout-text .br-callout-ghost.r{color:#ff3d8f}.br-callout-text .br-callout-ghost.b{color:#5fb0ff}
.br-callout-word{color:transparent;-webkit-background-clip:text;background-clip:text;text-shadow:none;filter:drop-shadow(0 2px 10px #0d0616)}
.br-callout-word .br-callout-ghost{color:#ff3d8f;-webkit-background-clip:border-box;background-clip:border-box;-webkit-text-fill-color:#ff3d8f}
.br-callout-word .br-callout-ghost.b{color:#5fb0ff;-webkit-text-fill-color:#5fb0ff}
.br-callout-sweep{position:absolute;top:0;bottom:0;left:0;width:34%;opacity:.2;pointer-events:none;
  background:linear-gradient(90deg,transparent,#fff2fb 50%,transparent);transform:translateX(-110%)}
.br-callout-bark{position:absolute;left:50%;top:13%;transform:translate(-50%,0);max-width:min(260px,calc(100% - 24px));padding:10px 15px;
  border:1px solid #d8a6cb;border-radius:18px 18px 18px 4px;background:rgba(39,20,51,.96);color:#ffebf8;font:500 14px/1.4 system-ui,sans-serif;
  box-shadow:0 6px 25px #08040b80,inset 0 0 16px #cc78c51c;opacity:0}
.br-callout-bark[data-anchored]{left:0;top:0;transform:translate(-50%,-100%)}
`;
const styled = new WeakSet();
function ensureStyle(doc) {
  if (!doc || styled.has(doc) || !doc.head) return;
  styled.add(doc);
  try { const s = doc.createElement('style'); s.setAttribute('data-br-callout', ''); s.textContent = CSS; doc.head.append(s); } catch (e) { /* noop */ }
}
const nowMs = () => Date.now();
function animate(el, frames, opts) {
  if (!el || typeof el.animate !== 'function') return null;
  try { return el.animate(frames, opts); } catch (e) { return null; }
}
function stopAnim(a) { if (a) { try { a.cancel(); } catch (e) { /* noop */ } } }

/**
 * @param {Object} o
 * @param {Element} [o.mount]      the station's root; the overlay is appended here
 * @param {Function} [o.lex]       (key, fallback) -> text
 * @param {Object} [o.ctx]         the station ctx: fxTunnel, gates, reduced, intensity
 * @param {Object} [o.moments]     a createMoments(ctx) to share; else one is made from ctx here
 * @param {Object} [o.emi]         { react(kind) } | { trigger(kind) }, optional anchor() -> { x, y } viewport px
 * @param {string} [o.font]        the cabinet's display font stack; else a bold condensed system stack
 * @param {number} [o.seed]        default seed for .word() when a call gives none
 * @param {Object} [o.cues]        { play(cue, opts) } to use instead of the kit adapter (tests)
 */
export function createCallout({ mount = null, lex = (_, f) => f, ctx = null, moments = null, emi = null, font = '', seed, cues = null } = {}) {
  const shown = [], words = [], tunnelLog = [], cueLog = [], barks = [], speechLog = [];
  const doc = mount && mount.ownerDocument ? mount.ownerDocument : (typeof document !== 'undefined' ? document : null);
  const reduced = () => !!(ctx && ctx.reduced) || (typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches);
  const own = !moments && ctx && typeof ctx.fxTunnel === 'function' ? createMoments(ctx, { station: 'callout' }) : null;
  const tunnelPath = moments || own;
  const sound = cues && typeof cues.play === 'function' ? { play(c, o) { cueLog.push({ cue: c, ...o, at: nowMs() }); cues.play(c, o); } } : createCues(cueLog);
  const timers = new Set(), anims = new Set();
  let layer = null, textEl = null, wordEls = [], tunnelTimer = 0, tunnelPosted = 0, barkAt = 0, disposed = false;

  /* The display font's real width, off one 2D context. The node tests have no canvas: wordBlock then falls
   * back to its average-glyph estimate, which is what the fixtures measure against. */
  let metricCtx;
  function textWidth(str, px) {
    if (metricCtx === undefined) {
      metricCtx = null;
      try { const c = doc && doc.createElement ? doc.createElement('canvas') : null; metricCtx = c && c.getContext ? c.getContext('2d') : null; } catch (e) { metricCtx = null; }
    }
    if (!metricCtx || typeof metricCtx.measureText !== 'function') return null;
    try { metricCtx.font = `800 ${px}px ${fontStack()}`; return metricCtx.measureText(str).width; } catch (e) { return null; }
  }
  const wordMeasure = (str, px) => { const w = textWidth(str, px); return w === null ? String(str).length * px * 0.5 : w; };

  const fontStack = () => {
    if (font) return font;
    try {
      const v = mount && typeof getComputedStyle === 'function' ? getComputedStyle(mount).getPropertyValue('--br-display-font').trim() : '';
      if (v) return v;
    } catch (e) { /* noop */ }
    return FONT_STACK;
  };
  function ensureLayer() {
    if (layer || !mount || !doc || typeof doc.createElement !== 'function') return layer;
    ensureStyle(doc);
    layer = doc.createElement('div');
    layer.className = 'br-callout';
    layer.setAttribute('aria-hidden', 'true');
    try { if (typeof getComputedStyle === 'function' && getComputedStyle(mount).position === 'static') mount.style.position = 'relative'; } catch (e) { /* noop */ }
    mount.append(layer);
    return layer;
  }
  function after(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); if (!disposed) fn(); }, Math.max(0, ms));
    timers.add(id);
    return id;
  }
  function track(a) { if (a) anims.add(a); return a; }
  function clearTimers() { for (const id of timers) clearTimeout(id); timers.clear(); for (const a of anims) stopAnim(a); anims.clear(); }

  /** One text element, centred, with its two chromatic ghosts. `lines` (2 or 3) makes it a wrapped block. */
  function makeText(text, sizeVh, cls, lines = null) {
    const l = ensureLayer();
    if (!l) return null;
    const el = doc.createElement('div');
    el.className = 'br-callout-text' + (cls ? ' ' + cls : '');
    el.style.fontSize = sizeVh + 'vh';
    el.style.fontFamily = fontStack();
    const body = Array.isArray(lines) && lines.length > 1 ? lines.join('\n') : text;
    el.textContent = body;
    if (Array.isArray(lines) && lines.length > 1) el.setAttribute('data-lines', String(lines.length));
    for (const k of ['r', 'b']) {
      const g = doc.createElement('span');
      g.className = 'br-callout-ghost ' + k; g.textContent = body; g.setAttribute('aria-hidden', 'true');
      el.append(g);
    }
    l.append(el);
    return el;
  }
  /** The zoom toward the viewer, the hold, the dissolve under the smear. `base` is a transform suffix (the mirror). */
  function zoomIn(el, { from, to, zoomMs, holdMs, outMs }, base = '') {
    if (!el) return;
    const still = reduced();
    const tf = (s) => `translate(-50%,-50%) scale(${still ? 1 : s})${base}`;
    const total = zoomMs + holdMs + outMs, tHold = zoomMs / total, tOut = (zoomMs + holdMs) / total;
    if (still) {
      el.style.transform = tf(1);
      track(animate(el, [{ opacity: 0 }, { opacity: 1, offset: Math.min(0.25, tHold) }, { opacity: 1, offset: tOut }, { opacity: 0 }],
        { duration: total, fill: 'forwards', easing: 'linear' }));
      return;
    }
    track(animate(el, [
      { transform: tf(from), opacity: 0, offset: 0 },
      { transform: tf(from + (to - from) * 0.12), opacity: 1, offset: Math.min(0.06, tHold * 0.5) },
      { transform: tf(to), opacity: 1, offset: tHold },
      { transform: tf(to), opacity: 1, offset: tOut },
      { transform: tf(to * 1.06), opacity: 0, offset: 1 },
    ], { duration: total, fill: 'forwards', easing: 'cubic-bezier(.2,.7,.2,1)' }));
    const ghosts = typeof el.querySelectorAll === 'function' ? Array.from(el.querySelectorAll('.br-callout-ghost')) : [];
    ghosts.forEach((g, i) => {
      const dir = i === 0 ? -1 : 1;
      track(animate(g, [
        { transform: 'translateX(0)', opacity: 0, offset: 0 }, { transform: 'translateX(0)', opacity: 0, offset: tOut },
        { transform: `translateX(${dir * 0.12}em)`, opacity: 0.75, offset: tOut + (1 - tOut) * 0.4 }, { transform: `translateX(${dir * 0.3}em)`, opacity: 0, offset: 1 },
      ], { duration: total, fill: 'forwards', easing: 'ease-out' }));
    });
  }
  function shake() {
    if (!layer || reduced()) return;
    track(animate(layer, [{ transform: 'translate(0,0)' }, { transform: 'translate(-6px,3px)' }, { transform: 'translate(5px,-4px)' }, { transform: 'translate(-4px,-2px)' },
      { transform: 'translate(3px,3px)' }, { transform: 'translate(0,0)' }], { duration: 320, easing: 'ease-out' }));
  }
  function dropText() { if (textEl) { try { textEl.remove(); } catch (e) { /* noop */ } textEl = null; } }
  function dropWords() { for (const w of wordEls) { try { w.remove(); } catch (e) { /* noop */ } } wordEls = []; }

  /* the tunnel breath, one per plan, through moments.tunnel(level) -> fx-tunnel */
  function tunnelTo(level) {
    const v = Math.round(level * 100) / 100;
    if (v === tunnelPosted) return;
    tunnelPosted = v;
    tunnelLog.push({ level: v, at: nowMs() });
    if (tunnelPath) { try { tunnelPath.tunnel(v); } catch (e) { /* noop */ } }
  }
  function stopTunnel() {
    if (tunnelTimer) { clearInterval(tunnelTimer); tunnelTimer = 0; }
    tunnelTo(0);
    if (own) own.cancel();   // our own path: fx-tunnel 0 on this frame, past the throttle (a shared one posts within 100 ms)
  }
  function breathe(plan) {
    if (tunnelTimer) { clearInterval(tunnelTimer); tunnelTimer = 0; }
    if (ctx && ctx.gates && ctx.gates.tunnel === false) return;
    const t0 = nowMs();
    const step = () => {
      const t = nowMs() - t0;
      if (disposed || t >= plan.tunnel.endMs) { stopTunnel(); return; }
      tunnelTo(tunnelLevelAt(plan, t));
    };
    tunnelTimer = setInterval(step, TUNNEL_STEP_MS);
    step();
  }
  function bark() {
    const i = barkAt++ % DEAD_BARKS.length, line = DEAD_BARKS[i];
    const text = lex(line.key, line.fallback) || line.fallback;
    barks.push({ key: line.key, text, at: nowMs() });
    const l = ensureLayer();
    if (!l) return text;
    const b = doc.createElement('div');
    b.className = 'br-callout-bark'; b.textContent = text; b.setAttribute('role', 'status');
    let anchored = null;
    try { anchored = emi && typeof emi.anchor === 'function' ? emi.anchor() : null; } catch (e) { anchored = null; }
    if (anchored && Number.isFinite(anchored.x) && Number.isFinite(anchored.y) && typeof mount.getBoundingClientRect === 'function') {
      const r = mount.getBoundingClientRect();
      b.setAttribute('data-anchored', '');
      b.style.left = (anchored.x - r.left) + 'px'; b.style.top = (anchored.y - r.top - 8) + 'px';
    }
    l.append(b);
    const rest = anchored ? 'translate(-50%,-100%)' : 'translate(-50%,0)';
    const a = animate(b, [{ opacity: 0, transform: rest + ' translateY(6px)' }, { opacity: 1, transform: rest, offset: 0.08 }, { opacity: 1, transform: rest, offset: 0.85 },
      { opacity: 0, transform: rest }], { duration: BARK_MS, fill: 'forwards', easing: 'ease-out' });
    if (a) track(a); else b.style.opacity = '1';
    after(BARK_MS, () => { try { b.remove(); } catch (e) { /* noop */ } });
    return text;
  }
  function emiReact(kind) {
    if (!emi) return false;
    try {
      if (typeof emi.react === 'function') { emi.react(kind); return true; }
      if (typeof emi.trigger === 'function') return emi.trigger(kind === 'wink' ? 'look' : 'bow') !== false;
    } catch (e) { /* noop */ }
    return false;
  }

  const api = {
    show(key, fallback = '', { tier = 'small' } = {}) {
      if (disposed) return { done: Promise.resolve() };
      const t = TIERS.includes(tier) ? tier : 'small';
      const text = lex(key, fallback) || fallback;
      shown.push({ key, text, tier: t, at: typeof performance !== 'undefined' ? performance.now() : Date.now(), schedule: { ...ZOOM, sizeVh: TIER_VH[t] } });
      dropText();
      textEl = makeText(text, TIER_VH[t], '');
      if (textEl) { textEl.setAttribute('data-tier', t); zoomIn(textEl, ZOOM); if (t === 'hero') shake(); }
      const mine = textEl;
      return { done: new Promise((r) => after(CALLOUT_MS, () => { if (textEl === mine) dropText(); r(); })) };
    },
    /** See the header. Resolves after the last word has faded. */
    word(text, { chain = [], reversed = false, seed: s } = {}) {
      if (disposed || !text) return { done: Promise.resolve(), plan: null };
      const plan = wordPlan(text, { chain, reversed, seed: s === undefined ? seed : s });
      api.cancelWords();
      words.push({ text: String(text), chain: plan.words.slice(1).map((w) => w.text), at: nowMs(), plan });
      const still = reduced();
      for (const w of plan.words) {
        const play = () => {
          sound.play('clicker', { index: w.index });
          speak(w.spoken, w.reversed ? SPEECH.reversedRate : SPEECH.rate, speechLog);
          sound.play('word', { index: w.index });
          const block = wordBlock(w.shown, { measure: textWidth(' ', 10) === null ? null : wordMeasure });
          const el = makeText(w.shown, block.sizeVh, 'br-callout-word', block.lines);
          if (!el) return;
          el.style.backgroundImage = w.stops.length > 1 ? `linear-gradient(100deg, ${w.stops[0]}, ${w.stops[1]})` : `linear-gradient(${w.stops[0]}, ${w.stops[0]})`;
          if (w.reversed) el.setAttribute('data-reversed', '');
          wordEls.push(el);
          zoomIn(el, { from: ZOOM.from, to: ZOOM.to, zoomMs: w.inMs, holdMs: w.holdMs, outMs: w.outMs }, w.reversed && !still ? ' scaleX(-1)' : '');
          after(WORD_MS, () => { wordEls = wordEls.filter((x) => x !== el); try { el.remove(); } catch (e) { /* noop */ } });
        };
        if (w.onsetMs === 0) play(); else after(w.onsetMs, play);   // Law I: the first word is on this frame
      }
      breathe(plan);
      return { done: new Promise((r) => after(plan.totalMs, r)), plan };
    },
    /** A dead spin: the sweep, the settle cue, EMI's small reaction and one rotating bark. */
    settle() {
      if (disposed) return { done: Promise.resolve(), bark: null };
      sound.play('settle', {});
      const l = ensureLayer();
      if (l && !reduced()) {
        const band = doc.createElement('div');
        band.className = 'br-callout-sweep';
        l.append(band);
        track(animate(band, [{ transform: 'translateX(-110%)' }, { transform: 'translateX(320%)' }], { duration: SETTLE_MS, easing: 'ease-in-out', fill: 'forwards' }));
        after(SETTLE_MS, () => { try { band.remove(); } catch (e) { /* noop */ } });
      }
      emiReact(barkAt % 2 ? 'shrug' : 'wink');
      const text = bark();
      return { done: new Promise((r) => after(BARK_MS, r)), bark: text };
    },
    /** Drops the words, the tunnel and the speech (the announcer text stays). */
    cancelWords() {
      dropWords();
      if (tunnelTimer) stopTunnel();
      hush();
    },
    cancel() {
      clearTimers();
      dropText();
      dropWords();
      stopTunnel();
      hush();
      if (layer && typeof layer.querySelectorAll === 'function') {
        for (const n of Array.from(layer.querySelectorAll('.br-callout-sweep,.br-callout-bark'))) { try { n.remove(); } catch (e) { /* noop */ } }
      }
    },
    dispose() {
      if (disposed) return;
      api.cancel();
      disposed = true;
      if (typeof sound.close === 'function') sound.close();
      if (own) own.dispose();
      if (layer) { try { layer.remove(); } catch (e) { /* noop */ } layer = null; }
    },
    debug() {
      return { shown: shown.slice(), words: words.slice(), tunnel: tunnelLog.slice(), cues: cueLog.slice(), barks: barks.slice(), speech: speechLog.slice(),
        mount: !!mount, live: { text: !!textEl, words: wordEls.length, tunnel: tunnelPosted, timers: timers.size } };
    },
  };
  return api;
}
