/* ============================================================================
 * exec/subliminals.js — GoonElement.Subliminals (2) + GoonPayloadKind.SubliminalStorm (1).
 *
 * One word at a time, large, brief, off-centre — the WPF SubliminalService bed,
 * re-cut for the duel. Uniform renderer shape: see the banner in exec/flashes.js.
 *
 * WHERE THE WORDS COME FROM (checked in this order):
 *   1. an explicit `phrases` array handed to the factory (the executor forwards
 *      whatever boot/H gives it);
 *   2. the host session, if the page has one — window.__gg.session.subliminals
 *      or .prefs.subliminals. NOTE (2026-08-03): GoonHostService does NOT send a
 *      subliminal pool in `init` today, so this path is dormant-but-wired; the
 *      day the host adds it, this file needs no change;
 *   3. DUEL_WORDS below — a small, neutral, duel-flavoured built-in so the
 *      element is never a silent no-op on a fresh install.
 *
 * OPPONENT TEXT: a SubliminalStorm may carry payload.text. The engine already
 * sanitized it on receipt (GoonMatchService.SanitizeText parity) — we sanitize
 * AGAIN here at render time, per the protocol's defence-in-depth rule, because
 * this is the line where a string becomes DOM. It goes in via textContent, and
 * it is styled .is-theirs so the player can tell whose word it is.
 * ==========================================================================*/

import { sanitizeText, TEXT_MAX_CHARS } from './sanitize.js';

/** The built-in floor. Neutral, short, duel-flavoured — no kink vocabulary. */
export const DUEL_WORDS = Object.freeze([
  'focus', 'hold', 'steady', 'deeper', 'give in', 'stay',
  'breathe', 'sink', 'slower', 'closer', 'still', 'yield',
]);

const MAX_LIVE = 3;      // concurrent word nodes
const POOL_MAX = 5;

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const lerp = (a, b, t) => a + (b - a) * clamp01(t);
const rand = (a, b) => a + Math.random() * (b - a);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};
const reducedMotion = () => {
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; }
  catch (_e) { return false; }
};

/**
 * Resolve the phrase pool (see the header for the order). Exported so
 * bouncingText.js draws from exactly the same words — the two elements must
 * never look like they belong to different games.
 */
export function resolvePhrases(explicit) {
  const clean = (arr) => (Array.isArray(arr) ? arr : [])
    .map((e) => sanitizeText(typeof e === 'string' ? e : (e && e.text), TEXT_MAX_CHARS))
    .filter((s) => s.length > 0);

  let out = clean(explicit);
  if (out.length) return out;

  try {
    const s = (typeof window !== 'undefined' && window.__gg) ? window.__gg.session : null;
    if (s) {
      out = clean(s.subliminals);
      if (!out.length) out = clean(s.prefs && s.prefs.subliminals);
      if (out.length) return out;
    }
  } catch (_e) { /* a page without a session is the normal dev case */ }

  return DUEL_WORDS.slice();
}

/** Cue intensity -> cadence + presence. */
export function subTuning(intensity, calm) {
  const i = clamp01(intensity);
  return {
    gapMs: Math.round(lerp(2800, 600, i) * (calm ? 1.7 : 1)),
    holdMs: Math.round(lerp(260, 520, i)),
    fadeMs: calm ? 300 : Math.round(lerp(200, 130, i)),
    opacity: +lerp(0.35, 0.9, i).toFixed(3),
    sizeVw: +lerp(5.2, 8.4, i).toFixed(2),
  };
}

export function createSubliminals({ layers, media, audio, logger, phrases } = {}) {
  const log = logger || null;
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:subliminals] ${m}`); };
  const calm = reducedMotion();

  const words = resolvePhrases(phrases);
  const recent = [];        // no-repeat-last-3 rotation
  const free = [];
  let live = 0;
  let sustained = null;

  const layer = () => (layers && typeof layers.get === 'function' ? layers.get('sub') : null);

  function pick() {
    if (words.length === 1) return words[0];
    for (let i = 0; i < 8; i++) {
      const w = words[(Math.random() * words.length) | 0];
      if (!recent.includes(w)) {
        recent.push(w);
        if (recent.length > Math.min(3, words.length - 1)) recent.shift();
        return w;
      }
    }
    return words[(Math.random() * words.length) | 0];
  }

  function takeNode() {
    const n = free.pop();
    if (n) return n;
    if (typeof document === 'undefined') return null;
    const el = document.createElement('div');
    el.className = 'gg-sub-word';
    return el;
  }

  function flash(text, tune, theirs) {
    const host = layer();
    if (!host || live >= MAX_LIVE) return;
    const node = takeNode();
    if (!node) return;
    live++;

    node.className = theirs ? 'gg-sub-word is-theirs' : 'gg-sub-word';
    node.textContent = text;                         // never innerHTML — untrusted text
    node.style.left = `${rand(22, 78).toFixed(1)}%`;
    node.style.top = `${rand(24, 76).toFixed(1)}%`;
    node.style.setProperty('--gg-sub-op', String(tune.opacity));
    node.style.setProperty('--gg-sub-fade', `${tune.fadeMs}ms`);
    node.style.setProperty('--gg-sub-size', `clamp(1.8rem, ${tune.sizeVw}vw, 6rem)`);
    host.appendChild(node);

    soon(() => node.classList.add('is-on'), 16);
    soon(() => {
      node.classList.remove('is-on');
      soon(() => {
        live = Math.max(0, live - 1);
        try { node.remove(); } catch (_e) { /* ignore */ }
        if (free.length < POOL_MAX) free.push(node);
      }, tune.fadeMs + 40);
    }, tune.holdMs + 16);

    if (audio && typeof audio.sfx === 'function') { try { audio.sfx('subliminal'); } catch (_e) { /* ignore */ } }
  }

  function loop(run) {
    if (!run.alive) return;
    const tune = subTuning(run.intensity, calm);
    // Weave the opponent's word in on ~40% of beats when a storm carries one.
    const theirs = run.theirText && Math.random() < 0.4;
    flash(theirs ? run.theirText : pick(), tune, !!theirs);
    run.timer = soon(() => loop(run), rand(tune.gapMs * 0.65, tune.gapMs * 1.35));
  }

  return {
    name: 'subliminals',

    start(cue) {
      const intensity = clamp01(cue && cue.intensity);
      if (sustained) { sustained.intensity = intensity; return; }
      sustained = { alive: true, intensity, theirText: '', timer: 0 };
      loop(sustained);
    },

    setIntensity(v) { if (sustained) sustained.intensity = clamp01(v); },

    stop() {
      if (!sustained) return;
      sustained.alive = false;
      try { clearTimeout(sustained.timer); } catch (_e) { /* ignore */ }
      sustained = null;
    },

    /** SubliminalStorm: a high-density burst, optionally carrying their word. */
    renderPayload(payload, done) {
      const p = payload || {};
      // Defence in depth: the engine sanitized this already; we are the line
      // where it becomes DOM, so we sanitize again before we ever touch a node.
      const theirText = sanitizeText(p.text, TEXT_MAX_CHARS);
      const runMs = Math.max(1500, (p.duration_ms | 0) || 8000);
      const run = {
        alive: true,
        intensity: Math.max(0.6, clamp01(p.intensity !== undefined ? p.intensity : 0.7)),
        theirText,
        timer: 0,
        endTimer: 0,
      };

      let finished = false;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        run.alive = false;
        try { clearTimeout(run.timer); } catch (_e) { /* ignore */ }
        try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };

      // Storms run tighter than the bed at the same intensity.
      const stormLoop = () => {
        if (!run.alive) return;
        const tune = subTuning(run.intensity, calm);
        tune.gapMs = Math.round(tune.gapMs * 0.3);
        const theirs = run.theirText && Math.random() < 0.45;
        flash(theirs ? run.theirText : pick(), tune, !!theirs);
        run.timer = soon(stormLoop, rand(tune.gapMs * 0.7, tune.gapMs * 1.3));
      };
      stormLoop();
      run.endTimer = soon(() => settle(true), runMs);
      return () => settle(false);
    },
  };
}

export default createSubliminals;
