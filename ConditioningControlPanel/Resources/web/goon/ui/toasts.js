/* ============================================================================
 * ui/toasts.js — the transient notice stack on #gg-toasts (z50).
 *
 * A toast is for something that ALREADY HAPPENED and needs no decision: a
 * a payload landed, the peer wobbling. Anything the player must
 * answer is a sheet (ui/sheets.js), never this.
 *
 * The layer is aria-live="polite" in index.html, so a screen reader announces
 * new children without stealing focus. Toasts therefore must not contain
 * interactive elements — a control nobody can reach by keyboard is worse than
 * no control.
 *
 * Ceilinged at MAX_VISIBLE: during a payload storm the stack sheds the OLDEST,
 * so the newest state is always the readable one. Every timer is tracked and
 * cleared on dispose — a page teardown mid-toast must not leave a timeout
 * pointing at a removed node.
 * ==========================================================================*/

import { el } from './router.js';
import { isCalm, play, popIn } from './juiceDom.js';

const MAX_VISIBLE = 4;
const DEFAULT_MS = 2600;

/**
 * @param {object} [o]
 * @param {HTMLElement} [o.root] defaults to #gg-toasts
 * @param {object} [o.prefs] read for reduceMotion (skips the enter/exit animation)
 */
export function createToasts({ root = null, prefs = null } = {}) {
  const doc = (typeof document !== 'undefined') ? document : null;
  const host = root || (doc ? doc.getElementById('gg-toasts') : null);
  const live = new Map();      // node -> {timer, removing}
  let stack = null;
  let disposed = false;

  function ensureStack() {
    if (!host) return null;
    if (stack && stack.isConnected) return stack;
    stack = el('div', { class: 'gg-toast-stack' });
    host.appendChild(stack);
    return stack;
  }

  function drop(node, immediate) {
    const rec = live.get(node);
    if (!rec || rec.removing) return;
    rec.removing = true;
    try { clearTimeout(rec.timer); } catch (_e) { /* ignore */ }

    const finish = () => {
      live.delete(node);
      // FLIP: the toasts below slide up into the gap instead of jumping.
      const below = [];
      try {
        const parent = node.parentNode;
        if (parent && !isCalm()) {
          let sib = node.nextElementSibling;
          while (sib) { below.push([sib, sib.getBoundingClientRect().top]); sib = sib.nextElementSibling; }
        }
      } catch (_e) { /* stub DOM */ }
      try { node.remove(); } catch (_e) { /* ignore */ }
      for (const [sib, before] of below) {
        try {
          const dy = before - sib.getBoundingClientRect().top;
          if (Math.abs(dy) > 0.5) {
            play(sib, [{ transform: 'translateY(' + dy + 'px)' }, { transform: 'none' }],
              { duration: 220, easing: 'cubic-bezier(.2, .8, .3, 1)', composite: 'add' });
          }
        } catch (_e) { /* ignore */ }
      }
    };
    if (immediate || (prefs && prefs.get('reduceMotion'))) { finish(); return; }
    node.classList.add('is-leaving');
    rec.timer = setTimeout(finish, 220);
  }

  const api = {
    /**
     * @param {string} text
     * @param {object} [o]
     * @param {'info'|'good'|'warn'|'bad'} [o.kind]
     * @param {string} [o.icon] a single glyph rendered ahead of the text
     * @param {number} [o.ms]
     */
    show(text, { kind = 'info', icon = '', ms = DEFAULT_MS } = {}) {
      if (disposed || !host || !text) return null;
      const parent = ensureStack();
      if (!parent) return null;

      const node = el('div', { class: 'gg-toast gg-toast--' + kind, role: 'status' }, [
        icon ? el('span', { class: 'gg-toast-icon', text: icon, 'aria-hidden': 'true' }) : null,
        el('span', { class: 'gg-toast-text', text: String(text) }),
      ]);
      parent.appendChild(node);
      live.set(node, { timer: 0, removing: false });
      // The glyph lands a beat after the pill does, with its own overshoot.
      if (icon && node.firstChild) popIn(node.firstChild, { delay: 120, from: 0.3, over: 1.3, ms: 300 });

      // Shed the oldest before the newest can push the stack off-screen.
      while (parent.children.length > MAX_VISIBLE) drop(parent.children[0], true);

      const rec = live.get(node);
      if (rec) rec.timer = setTimeout(() => drop(node, false), Math.max(600, ms | 0));
      return node;
    },

    good(text, o) { return api.show(text, Object.assign({ kind: 'good' }, o)); },
    warn(text, o) { return api.show(text, Object.assign({ kind: 'warn' }, o)); },
    bad(text, o) { return api.show(text, Object.assign({ kind: 'bad' }, o)); },

    clear() {
      for (const node of Array.from(live.keys())) drop(node, true);
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      api.clear();
      for (const rec of live.values()) { try { clearTimeout(rec.timer); } catch (_e) { /* ignore */ } }
      live.clear();
      try { stack?.remove(); } catch (_e) { /* ignore */ }
      stack = null;
    },
  };
  return api;
}

export default createToasts;
