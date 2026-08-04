/* ============================================================================
 * ui/closeness.js — the bottom-right dial: the ONE thing you tell them.
 *
 * Four stops, each carrying its word, wired straight to match.setCloseness().
 * It is a CLAIM, not a measurement — nothing reads a sensor, nothing verifies
 * it, and the fineprint says so out loud so nobody thinks the app is watching.
 *
 * Keyboard: Q steps down, E steps up. A 350 ms cooldown between changes keeps a
 * held key (or a nervous thumb) from spamming the tick stream.
 *
 * Node-import-safe: no DOM at import, only inside mountCloseness().
 * ==========================================================================*/

/** 0-3 -> the word. Colour never travels alone. */
export const CLOSENESS_STOPS = Object.freeze(['steady', 'warm', 'close', 'edge']);

const CHANGE_COOLDOWN_MS = 350;

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
    run() { while (list.length) { const fn = list.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } } },
  };
}

function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now();
  } catch (_e) { /* fall through */ }
  return Date.now();
}

/**
 * @param {object}  o
 * @param {Element} o.host
 * @param {object}  o.match
 * @param {object}  [o.audio]
 * @param {Function} [o.onLog]
 * @returns {{unmount:Function, set:Function, value:Function}}
 */
export function mountCloseness({ host, match, audio = null, onLog = null } = {}) {
  const led = createLedger();
  const root = el('div', 'gg-dial gg-plate');
  if (!root || !host) return { unmount() { led.run(); }, set() {}, value() { return null; } };

  add(root, el('div', 'gg-dial-label', "you're telling them"));
  const segRow = add(root, el('div', 'gg-dial-stops'));
  const stops = [];
  for (let i = 0; i < CLOSENESS_STOPS.length; i++) {
    const b = add(segRow, el('button', 'gg-dial-stop'));
    if (!b) continue;
    b.type = 'button';
    add(b, el('i', 'gg-dial-bar'));
    add(b, el('span', 'gg-dial-word', CLOSENESS_STOPS[i]));
    b.setAttribute && b.setAttribute('data-gg-stop', String(i));
    led.listen(b, 'click', () => set(i));
    stops.push(b);
  }
  add(root, el('div', 'gg-dial-fine', 'they can only see what you say.'));
  add(host, root);

  // -Infinity, not 0: nowMs() is a monotonic clock that starts near zero, so a
  // zero baseline would swallow the first change of the match.
  let last = -Infinity;
  let current = (match && match.localCloseness != null) ? (match.localCloseness | 0) : null;

  function paint() {
    for (let i = 0; i < stops.length; i++) {
      cls(stops[i], 'is-on', current !== null && i <= current);
      cls(stops[i], 'is-head', current === i);
    }
    root.setAttribute && root.setAttribute('data-gg-close', current === null ? 'none' : String(current));
  }

  function set(v) {
    const t = nowMs();
    if (t - last < CHANGE_COOLDOWN_MS) return;
    const next = Math.max(0, Math.min(3, v | 0));
    if (next === current) return;
    last = t;
    current = next;
    try { if (match && typeof match.setCloseness === 'function') match.setCloseness(next); } catch (_e) { /* engine said no */ }
    sfx(audio, 'gg-taunt-up');
    if (typeof onLog === 'function') { try { onLog({ t: 'closeness', value: next, word: CLOSENESS_STOPS[next] }); } catch (_e) { /* ignore */ } }
    paint();
  }

  led.listen(typeof window !== 'undefined' ? window : null, 'keydown', (e) => {
    if (!e || e.repeat || e.ctrlKey || e.altKey || e.metaKey) return;
    const d = doc();
    const active = d && d.activeElement;
    const tag = active && active.tagName ? String(active.tagName).toLowerCase() : '';
    if (tag === 'input' || tag === 'textarea' || (active && active.isContentEditable)) return;
    const k = String(e.key || '').toLowerCase();
    if (k !== 'q' && k !== 'e') return;
    const base = current === null ? -1 : current;
    set(k === 'e' ? base + 1 : base - 1);
  });

  paint();

  return {
    set,
    value() { return current; },
    unmount() {
      led.run();
      try { root.remove(); } catch (_e) { /* gone */ }
    },
  };
}
