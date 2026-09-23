/* ============================================================================
 * ui/router.js — the screen stack, and the teardown ledger every screen uses.
 *
 * THE LEDGER (copied wholesale from intake/ui/menu.js, and for the same reason):
 * a screen registers every listener, timeout, interval and rAF it creates, and
 * unmount() reverses the whole list. A leaked 1 s interval on the countdown
 * screen does not "look wrong" — it quietly keeps ticking a dead DOM node and
 * desyncs the NEXT match's countdown. There is exactly one way to schedule work
 * in this UI and it is through a ledger.
 *
 * THE ROUTER: index.html ships nine <section class="gg-screen"> nodes. Exactly
 * one is unhidden at a time and html[data-gg-screen] names it, so CSS (and the
 * play-test driver's probe) can see the current screen without reading JS state.
 * `hide()` unmounts everything — that is the Live phase, where the HUD and the
 * effect layers own the whole viewport.
 *
 * A screen module is `{ mount(container, ctx) -> { unmount() } }`. mount() must
 * never throw: the router catches, logs and leaves the stack empty rather than
 * wedging on a half-built screen.
 *
 * No DOM at import — createRouter() is what first touches document.
 *
 * TRANSITIONS (2026-09-23 juice pass): nothing appears or vanishes in one frame.
 * The outgoing section keeps its nodes for EXIT_MS while it fades and lifts
 * away (inert, click-through), and the incoming one cascades its children in
 * (ui/juiceDom.js staggerIn). The leaving section is torn down on a timer, and
 * re-showing it (or dispose) finishes the exit at once, so a quick bounce
 * between two screens never shows two copies of one. Reduced motion: fades.
 * A section can opt out of the cascade with data-gg-no-stagger.
 * ==========================================================================*/

import { EXIT_MS, OUT_EASE, isCalm, play, staggerIn } from './juiceDom.js';

/** Screen name -> the section id index.html already ships. */
export const SCREEN_IDS = Object.freeze({
  title: 'scr-title',
  host: 'scr-host',
  join: 'scr-join',
  /**
   * The first-run media step (ui/screens/mediaSetup.js). NOT a match phase: the
   * engine walks Lobby -> Consent underneath it exactly as usual, and boot.js
   * simply holds this screen in front of the lobby until the player has a deck.
   */
  mediaSetup: 'scr-media-setup',
  lobby: 'scr-lobby',
  draft: 'scr-draft',
  countdown: 'scr-countdown',
  sd: 'scr-sd',
  recap: 'scr-recap',
  // The two screens that are not match phases: the media/compression library
  // and the voice-note library, both reached from the title menu and left with
  // Back. (Every name here MUST have a <section> in index.html — selftest-assets
  // walks this map and checks, because a missing section makes router.show()
  // return null and turns the menu item into a button that does nothing.)
  assets: 'scr-assets',
  voice: 'scr-voice',
});

/**
 * The teardown ledger. Everything a screen creates goes in; dispose() reverses
 * it in LIFO order and can be called twice safely.
 */
export function createLedger() {
  const cleanups = [];
  const timers = new Set();
  const intervals = new Set();
  const rafs = new Set();
  let disposed = false;

  const api = {
    get isDisposed() { return disposed; },

    /** addEventListener with automatic removal. Returns an early-off function. */
    listen(target, type, fn, opts) {
      if (disposed || !target || typeof target.addEventListener !== 'function') return () => {};
      try { target.addEventListener(type, fn, opts); } catch (_e) { return () => {}; }
      const off = () => { try { target.removeEventListener(type, fn, opts); } catch (_e) { /* gone */ } };
      cleanups.push(off);
      return off;
    },

    /** setTimeout that forgets itself when it fires. */
    timer(fn, ms) {
      if (disposed) return 0;
      let t = 0;
      try {
        t = setTimeout(() => {
          timers.delete(t);
          try { fn(); } catch (e) { api._err('timer', e); }
        }, Math.max(0, ms | 0));
      } catch (_e) { return 0; }
      timers.add(t);
      return t;
    },

    /** setInterval; cleared on dispose. */
    interval(fn, ms) {
      if (disposed) return 0;
      let t = 0;
      try {
        t = setInterval(() => { try { fn(); } catch (e) { api._err('interval', e); } }, Math.max(16, ms | 0));
      } catch (_e) { return 0; }
      intervals.add(t);
      return t;
    },

    /**
     * A self-rescheduling rAF loop. `fn(now)` returning false stops it.
     * The handle is tracked so dispose() cancels mid-flight.
     */
    frame(fn) {
      if (disposed || typeof requestAnimationFrame !== 'function') return () => {};
      let handle = 0;
      let stopped = false;
      const step = (now) => {
        rafs.delete(handle);
        if (stopped || disposed) return;
        let again = true;
        try { again = fn(now) !== false; } catch (e) { api._err('frame', e); again = false; }
        if (!again || stopped || disposed) return;
        handle = requestAnimationFrame(step);
        rafs.add(handle);
      };
      handle = requestAnimationFrame(step);
      rafs.add(handle);
      const stop = () => { stopped = true; try { cancelAnimationFrame(handle); } catch (_e) { /* gone */ } rafs.delete(handle); };
      cleanups.push(stop);
      return stop;
    },

    /** Any other undo (an engine unsubscribe, a node removal, a stopMusic). */
    add(fn) {
      if (typeof fn !== 'function') return;
      if (disposed) { try { fn(); } catch (_e) { /* ignore */ } return; }
      cleanups.push(fn);
    },

    /** Convenience for the engine's `onX(fn) -> unsub` events. */
    sub(unsub) { api.add(unsub); return unsub; },

    logger: null,
    _err(what, e) {
      const log = api.logger;
      if (log && log.warn) log.warn('[GG ui] ledger ' + what + ' threw: ' + ((e && e.message) || e));
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      for (const t of timers) { try { clearTimeout(t); } catch (_e) { /* ignore */ } }
      timers.clear();
      for (const t of intervals) { try { clearInterval(t); } catch (_e) { /* ignore */ } }
      intervals.clear();
      for (const r of rafs) { try { cancelAnimationFrame(r); } catch (_e) { /* ignore */ } }
      rafs.clear();
      while (cleanups.length) {
        const fn = cleanups.pop();
        try { fn(); } catch (e) { api._err('cleanup', e); }
      }
    },
  };
  return api;
}

/* ----------------------------------------------------------------------------
 * Tiny DOM helpers. Every screen builds its nodes in JS (no innerHTML anywhere
 * with interpolated data — opponent display names are attacker-controlled).
 * -------------------------------------------------------------------------- */

/** el('div', {class:'x', text:'hi'}, [child, ...]) */
export function el(tag, attrs, children) {
  const node = document.createElement(tag);
  if (attrs) {
    for (const k of Object.keys(attrs)) {
      const v = attrs[k];
      if (v === null || v === undefined || v === false) continue;
      if (k === 'text') node.textContent = String(v);
      else if (k === 'class') node.className = String(v);
      else if (k === 'html') node.innerHTML = String(v);          // literal markup only (SVG glyphs)
      else if (k === 'dataset') Object.assign(node.dataset, v);
      else if (k === 'style') node.setAttribute('style', String(v));
      else if (v === true) node.setAttribute(k, '');
      else node.setAttribute(k, String(v));
    }
  }
  if (children) {
    for (const c of [].concat(children)) {
      if (c === null || c === undefined || c === false) continue;
      node.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
    }
  }
  return node;
}

/** A .gg-btn, wired through the ledger with an optional sfx cue. */
export function button(ledger, label, onClick, { variant = '', audio = null, sfx = 'ui-select', attrs = null } = {}) {
  const b = el('button', Object.assign({
    type: 'button',
    class: 'gg-btn' + (variant ? ' gg-btn--' + variant : ''),
    text: label,
  }, attrs || {}));
  ledger.listen(b, 'click', (e) => {
    e.preventDefault();
    if (b.disabled) return;
    try { audio?.sfx?.(sfx); } catch (_e) { /* stub bus */ }
    try { onClick(e); } catch (err) { ledger._err('click', err); }
  });
  return b;
}

/* -------------------------------------------------------------------------- */

/**
 * @param {object} o
 * @param {Record<string, {mount:Function}>} o.screens name -> module
 * @param {object} [o.ctx] shared context handed to every mount()
 * @param {object} [o.logger]
 */
export function createRouter({ screens = {}, ctx = null, logger = null } = {}) {
  const doc = (typeof document !== 'undefined') ? document : null;
  let currentName = null;
  let currentHandle = null;
  let currentEl = null;
  const listeners = new Set();

  function sectionFor(name) {
    const id = SCREEN_IDS[name];
    return (doc && id) ? doc.getElementById(id) : null;
  }

  function markScreen(name) {
    try { doc?.documentElement?.setAttribute('data-gg-screen', name || 'none'); } catch (_e) { /* ignore */ }
  }

  /* ---- exits: section -> {timer, anim} while its old nodes fade away ---- */
  const leaving = new Map();

  function finishLeave(node) {
    const rec = leaving.get(node);
    if (!rec) return;
    leaving.delete(node);
    try { clearTimeout(rec.timer); } catch (_e) { /* ignore */ }
    try { rec.anim?.cancel?.(); } catch (_e) { /* ignore */ }
    try {
      node.classList?.remove?.('is-leaving');
      node.removeAttribute?.('inert');
      node.removeAttribute?.('aria-hidden');
      node.replaceChildren();
      node.hidden = true;
    } catch (_e) { /* ignore */ }
  }

  function leave(node) {
    if (!node) return;
    finishLeave(node);
    // No WAAPI (node self-tests, a stub DOM): the old instant swap.
    if (typeof node.animate !== 'function') {
      try { node.replaceChildren(); node.hidden = true; } catch (_e) { /* ignore */ }
      return;
    }
    try {
      node.classList?.add?.('is-leaving');
      node.setAttribute?.('inert', '');
      node.setAttribute?.('aria-hidden', 'true');
    } catch (_e) { /* ignore */ }
    const frames = isCalm()
      ? [{ opacity: 1 }, { opacity: 0 }]
      : [{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(-10px) scale(.975)' }];
    const anim = play(node, frames, { duration: EXIT_MS, easing: OUT_EASE, fill: 'forwards' });
    const timer = setTimeout(() => finishLeave(node), EXIT_MS + 30);
    leaving.set(node, { timer, anim });
  }

  function enter(section, name) {
    if (!section || typeof section.animate !== 'function') return;
    try {
      section.classList?.add?.('is-entering');
      setTimeout(() => { try { section.classList?.remove?.('is-entering'); } catch (_e) { /* ignore */ } }, 700);
    } catch (_e) { /* ignore */ }
    play(section, [{ opacity: 0 }, { opacity: 1 }], { duration: 200, easing: 'ease-out', fill: 'backwards' });
    if (section.hasAttribute?.('data-gg-no-stagger') || name === 'countdown') return;
    // Cascade the screen's pieces in. A screen that is one card wrapping the
    // rest cascades the card's own children instead, capped at twelve.
    let kids = Array.from(section.children || []);
    if (kids.length === 1 && kids[0].children && kids[0].children.length > 1) kids = Array.from(kids[0].children);
    staggerIn(kids.slice(0, 12), { start: 60 });
  }

  function tearDown(animate) {
    const handle = currentHandle;
    const node = currentEl;
    currentHandle = null;
    currentEl = null;
    currentName = null;
    if (handle && typeof handle.unmount === 'function') {
      try { handle.unmount(); } catch (e) { logger?.error?.('[GG ui] unmount threw: ' + ((e && e.stack) || e)); }
    }
    if (node) {
      if (animate) leave(node);
      else { try { finishLeave(node); node.replaceChildren(); node.hidden = true; } catch (_e) { /* ignore */ } }
    }
  }

  const api = {
    get current() { return currentName; },

    /** fn(name|null) after every transition. */
    onChanged(fn) {
      if (typeof fn !== 'function') return () => {};
      listeners.add(fn);
      return () => listeners.delete(fn);
    },

    /**
     * Swap to `name`. Re-showing the SAME screen remounts it (a phase can
     * legitimately re-enter, e.g. a counter-proposal bouncing lobby->consent).
     * @param {object} [args] merged into the ctx handed to mount()
     */
    show(name, args) {
      if (!doc) return null;
      const mod = screens[name];
      const section = sectionFor(name);
      if (!mod || !section) {
        logger?.warn?.('[GG ui] no screen "' + name + '"');
        return null;
      }

      // Re-showing the same screen remounts it in place, so it does not also
      // fade out a copy of itself.
      tearDown(currentEl !== section);
      finishLeave(section);

      // Belt and braces: any other section left visible by a crash is hidden.
      for (const other of Object.keys(SCREEN_IDS)) {
        if (other === name) continue;
        const n = sectionFor(other);
        if (n && !n.hidden && !leaving.has(n)) { n.hidden = true; n.replaceChildren(); }
      }

      currentName = name;
      currentEl = section;
      section.replaceChildren();
      section.hidden = false;
      // Sections persist across shows, and so does their scroll position — a
      // screen taller than the viewport (assets) must not reopen mid-scroll.
      try { section.scrollTop = 0; } catch (_e) { /* ignore */ }
      markScreen(name);

      const mountCtx = args ? Object.assign({}, ctx, args) : ctx;
      try {
        currentHandle = mod.mount(section, mountCtx) || null;
      } catch (e) {
        logger?.error?.('[GG ui] mount("' + name + '") threw: ' + ((e && e.stack) || e));
        currentHandle = null;
      }
      enter(section, name);
      for (const fn of Array.from(listeners)) { try { fn(name); } catch (_e) { /* ignore */ } }
      return currentHandle;
    },

    /** Live phase: no screen at all — the HUD and the fx layers own the view. */
    hide() {
      if (!doc) return;
      tearDown(true);
      for (const other of Object.keys(SCREEN_IDS)) {
        const n = sectionFor(other);
        if (n && !leaving.has(n)) { n.hidden = true; n.replaceChildren(); }
      }
      markScreen('none');
      for (const fn of Array.from(listeners)) { try { fn(null); } catch (_e) { /* ignore */ } }
    },

    dispose() {
      tearDown(false);
      for (const n of Array.from(leaving.keys())) finishLeave(n);
      listeners.clear();
    },
  };
  return api;
}

export default createRouter;
