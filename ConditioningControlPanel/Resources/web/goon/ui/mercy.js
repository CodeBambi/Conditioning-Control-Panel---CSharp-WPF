/* ============================================================================
 * ui/mercy.js — MERCY. The one button that always works.
 *
 * SAFETY CONTRACT (read goon.css's DO NOT TOUCH block before changing anything
 * here). The button:
 *   * fires on POINTERDOWN, not click — no travel, no confirm, no animation
 *     between the intent and the concede;
 *   * is NOT tab-focusable and does NOT activate on Enter/Space, so it can never
 *     be triggered by a stray keypress during a lock card;
 *   * never pulses, breathes, dims or moves. It is the only uppercase word in
 *     the product because it is the only word that must be findable at a glance;
 *   * is inert for 700 ms after mount (`.is-arming`, hairline ring completing)
 *     so the tap that started the match cannot land on it.
 *
 * It talks to the engine through getMatch() every time — never a captured
 * reference — because the match can be rebuilt underneath the HUD (relay
 * fallback) and a stale handle would concede into the void.
 *
 * Node-import-safe: no DOM at import, only inside mountMercy().
 * ==========================================================================*/

import { GoonEndReason } from '../core/contracts.js';

const ARM_MS = 700;

/**
 * How long "you tapped out." holds before it gets out of the way.
 *
 * IT MUST GET OUT OF THE WAY. declareMercy() ends the match SYNCHRONOUSLY
 * (core/match.js _endMatch -> phase Recap), which makes boot.js unmount this
 * whole layer BEFORE declare() gets to its next statement — so the takeover is
 * built after our own ledger has already been run and cannot be owned by it.
 * It therefore owns itself: this timer, a tap-anywhere dismiss, and the sweep
 * in boot.js are three independent ways off the screen, because the recap is
 * already mounted UNDERNEATH it (z10) and a takeover that never leaves is a
 * player stranded on a full-bleed scrim with no button on it.
 */
export const TAKEOVER_MS = 2400;

const doc = () => (typeof document !== 'undefined' ? document : null);

function el(tag, cls, text) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls && n) n.className = cls;
  if (text != null && n) n.textContent = String(text);
  return n;
}

function add(parent, child) {
  if (parent && child && typeof parent.appendChild === 'function') parent.appendChild(child);
  return child;
}

function cls(node, name, on) {
  if (!node || !node.classList) return;
  try { node.classList[on ? 'add' : 'remove'](name); } catch (_e) { /* stub DOM */ }
}

function sfx(audio, id) {
  try { if (audio && typeof audio.sfx === 'function') audio.sfx(id); } catch (_e) { /* stub */ }
}

function createLedger() {
  const list = [];
  return {
    add(fn) { if (typeof fn === 'function') list.push(fn); },
    listen(target, type, fn, opts) {
      if (!target || typeof target.addEventListener !== 'function') return;
      target.addEventListener(type, fn, opts);
      list.push(() => { try { target.removeEventListener(type, fn, opts); } catch (_e) { /* gone */ } });
    },
    interval(ms, fn) {
      if (typeof setInterval !== 'function') return 0;
      const id = setInterval(fn, ms);
      list.push(() => { try { clearInterval(id); } catch (_e) { /* gone */ } });
      return id;
    },
    run() { while (list.length) { const fn = list.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } } },
  };
}

/**
 * @param {object}   o
 * @param {Function} o.getMatch  () => GoonMatchService|null
 * @param {object}   [o.audio]
 * @param {Function} [o.onLog]
 * @returns {{unmount:Function, declare:Function}}
 */
export function mountMercy({ getMatch = null, audio = null, onLog = null } = {}) {
  const led = createLedger();
  const d = doc();
  const host = d && typeof d.getElementById === 'function' ? d.getElementById('gg-mercy') : null;
  if (!host) return { unmount() { led.run(); }, declare() {} };

  host.hidden = false;
  const wrap = add(host, el('div', 'gg-mercy-wrap'));
  const btn = add(wrap, el('button', 'gg-mercy-btn is-arming', 'MERCY'));
  const fine = add(wrap, el('div', 'gg-mercy-fine', 'esc · ends the match'));
  const ring = add(btn, el('i', 'gg-mercy-arm'));
  if (btn) {
    btn.type = 'button';
    btn.tabIndex = -1;
    btn.setAttribute && btn.setAttribute('aria-label', 'mercy — ends the match');
  }

  let armed = false;
  let localDeclared = false;
  let takeover = null;
  let dismissTakeover = null;

  const armTimer = setTimeout(() => {
    armed = true;
    cls(btn, 'is-arming', false);
    try { ring && ring.remove(); } catch (_e) { /* gone */ }
  }, ARM_MS);
  led.add(() => { try { clearTimeout(armTimer); } catch (_e) { /* gone */ } });

  // POINTERDOWN, not click. And no keyboard activation, ever.
  led.listen(btn, 'pointerdown', (e) => {
    if (e && e.preventDefault) e.preventDefault();
    declare();
  });
  led.listen(btn, 'keydown', (e) => {
    if (!e) return;
    if (e.key === 'Enter' || e.key === ' ' || e.key === 'Spacebar') {
      if (e.preventDefault) e.preventDefault();
      if (e.stopPropagation) e.stopPropagation();
    }
  });

  function declare() {
    if (!armed || localDeclared) return;
    localDeclared = true;
    sfx(audio, 'gg-mercy');
    if (typeof onLog === 'function') { try { onLog({ t: 'mercy', by: 'local' }); } catch (_e) { /* ignore */ } }
    const m = typeof getMatch === 'function' ? safeMatch() : null;
    try { if (m && typeof m.declareMercy === 'function') m.declareMercy(); }
    catch (_e) { /* the concede is a local truth even if the wire is dead */ }
    showTakeover('you tapped out.', "that's the whole point of the button.");
  }

  function safeMatch() {
    try { return getMatch ? getMatch() : null; } catch (_e) { return null; }
  }

  /**
   * Full-bleed. It goes on <body>, NOT inside #gg-mercy: that layer carries a
   * translateX(-50%), which would make it the containing block for a fixed
   * child and shrink the scrim to the size of the button.
   *
   * SELF-OWNING by necessity — see TAKEOVER_MS. Nothing here goes through the
   * ledger, because by the time this runs the ledger has already been disposed.
   */
  function showTakeover(title, line) {
    if (takeover) return;
    const node = el('div', 'gg-mercy-takeover');
    if (!node) return;
    takeover = node;
    add(node, el('h2', 'gg-mercy-takeover-title', title));
    add(node, el('p', 'gg-mercy-takeover-line', line));
    // The way out is on the node itself, so it works even after the recap has
    // replaced everything else on the page.
    add(node, el('p', 'gg-mercy-takeover-hint', 'tap to see the recap'));
    add((d && d.body) || host, node);
    if (wrap) wrap.hidden = true;
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => cls(node, 'is-in', true));
    else cls(node, 'is-in', true);

    let autoTimer = 0;
    const drop = () => {
      try { clearTimeout(autoTimer); } catch (_e) { /* gone */ }
      if (takeover === node) takeover = null;
      try { node.remove(); } catch (_e) { /* already gone */ }
    };
    dismissTakeover = drop;
    try { node.addEventListener('pointerdown', drop); node.addEventListener('click', drop); }
    catch (_e) { /* stub DOM */ }
    try { autoTimer = setTimeout(drop, TAKEOVER_MS); } catch (_e) { /* no timers: the tap still works */ }
  }

  /** Their concede: a sting, not a takeover — the recap is H's screen. */
  function showSting(name) {
    const node = el('div', 'gg-mercy-sting');
    if (!node) return;
    add(node, el('span', 'gg-mercy-sting-them', (name || 'they') + ' tapped out.'));
    add(node, el('span', 'gg-mercy-sting-you', 'you held.'));
    add((d && d.body) || host, node);   // same containing-block reason as the takeover
    if (typeof requestAnimationFrame === 'function') requestAnimationFrame(() => cls(node, 'is-in', true));
    else cls(node, 'is-in', true);
    sfx(audio, 'gg-victory');
    led.add(() => { try { node.remove(); } catch (_e) { /* gone */ } });
  }

  // The match object can be replaced under us (relay fallback rebuilds it), so
  // bind lazily and re-bind if a different instance shows up.
  let boundTo = null;
  let offEnded = null;
  function bind() {
    const m = safeMatch();
    if (!m || m === boundTo || typeof m.onMatchEnded !== 'function') return;
    if (typeof offEnded === 'function') { try { offEnded(); } catch (_e) { /* gone */ } }
    boundTo = m;
    offEnded = m.onMatchEnded((result) => onEnded(m, result));
    led.add(() => { if (typeof offEnded === 'function') { try { offEnded(); } catch (_e) { /* gone */ } } });
  }
  function onEnded(m, result) {
    if (localDeclared || !result) return;
    if (result.endReason !== GoonEndReason.Mercy) return;
    const op = m && m.opponent;
    showSting(op && op.displayName ? op.displayName : 'they');
  }
  bind();
  led.interval(400, bind);

  return {
    declare,
    /** For H's Esc ladder, if it wants to route through the same guard. */
    isArmed() { return armed; },
    /** boot.js's recap sweep calls this; so does a tap on the scrim. */
    dismissTakeover() { if (typeof dismissTakeover === 'function') dismissTakeover(); },
    unmount() {
      led.run();
      // NOTE: in the ordinary mercy path this runs BEFORE the takeover exists
      // (declareMercy is synchronous), which is exactly why the takeover carries
      // its own dismissal. This line only covers the unmount-after-show order.
      if (typeof dismissTakeover === 'function') { try { dismissTakeover(); } catch (_e) { /* gone */ } }
      try { wrap && wrap.remove(); } catch (_e) { /* gone */ }
      if (host) host.hidden = true;
    },
  };
}
