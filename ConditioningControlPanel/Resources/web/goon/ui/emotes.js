/* ============================================================================
 * ui/emotes.js — the little sheet behind the emote item.
 *
 * Six canned lines and a row of icons. Nothing free-typed goes out in v1: the
 * whole point of a fixed set is that no player can hand the other one a
 * sentence nobody consented to. The engine sanitizes and caps anyway.
 *
 * A local 1-per-5s limiter keeps the sheet from becoming a spam cannon; while
 * it is closed the item fades instead of refusing silently.
 *
 * Node-import-safe: no DOM at import, only inside mountEmotes().
 * ==========================================================================*/

export const EMOTE_PRESETS = Object.freeze([
  'gg',
  'still here',
  'nice try',
  'that one hurt',
  'you good?',
  'not even close',
]);

export const EMOTE_ICONS = Object.freeze(['😏', '💦', '🔥', '🫠', '👀', '💪']);

/** One emote every five seconds, locally enforced. */
const RATE_MS = 5000;

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

function nowMs() {
  try {
    if (typeof performance !== 'undefined' && performance && typeof performance.now === 'function') return performance.now();
  } catch (_e) { /* fall through */ }
  return Date.now();
}

/**
 * @param {object}   o
 * @param {Element}  o.host      layer the sheet is appended to (HUD frame)
 * @param {object}   o.match
 * @param {object}   [o.audio]
 * @param {Function} [o.onLog]
 * @returns {{unmount:Function, open:Function, close:Function, toggle:Function, isOpen:Function}}
 */
export function mountEmotes({ host, match, audio = null, onLog = null } = {}) {
  const led = createLedger();
  const root = el('div', 'gg-emotes gg-plate');
  if (!root || !host) return { unmount() { led.run(); }, open() {}, close() {}, toggle() {}, isOpen() { return false; } };
  root.hidden = true;

  add(root, el('div', 'gg-emotes-title', 'say one thing'));
  const lines = add(root, el('div', 'gg-emotes-lines'));
  const buttons = [];
  for (const line of EMOTE_PRESETS) {
    const b = add(lines, el('button', 'gg-emote-line', line));
    if (!b) continue;
    b.type = 'button';
    led.listen(b, 'click', () => send(line, ''));
    buttons.push(b);
  }
  const icons = add(root, el('div', 'gg-emotes-icons'));
  for (const icon of EMOTE_ICONS) {
    const b = add(icons, el('button', 'gg-emote-icon', icon));
    if (!b) continue;
    b.type = 'button';
    led.listen(b, 'click', () => send('', icon));
    buttons.push(b);
  }
  const cool = add(root, el('div', 'gg-emotes-cool', ''));
  add(host, root);

  let last = -Infinity;
  let open = false;

  function paint() {
    const left = RATE_MS - (nowMs() - last);
    const cooling = left > 0;
    cls(root, 'is-cooling', cooling);
    for (const b of buttons) { b.disabled = cooling; }
    if (cool) cool.textContent = cooling ? 'one more in ' + Math.ceil(left / 1000) + 's' : '';
  }

  function send(text, icon) {
    if (nowMs() - last < RATE_MS) { paint(); return false; }
    last = nowMs();
    try { if (match && typeof match.sendEmote === 'function') match.sendEmote(text || '', icon || ''); }
    catch (_e) { /* the engine is allowed to be gone */ }
    sfx(audio, 'gg-emote');
    if (typeof onLog === 'function') { try { onLog({ t: 'emote-out', text: text || '', icon: icon || '' }); } catch (_e) { /* ignore */ } }
    paint();
    setOpen(false);
    return true;
  }

  function setOpen(v) {
    open = !!v;
    root.hidden = !open;
    cls(root, 'is-open', open);
    paint();
  }

  // A click anywhere else closes the sheet (no hover-only affordance anywhere).
  led.listen(doc(), 'pointerdown', (e) => {
    if (!open) return;
    if (e && e.target && typeof root.contains === 'function' && root.contains(e.target)) return;
    // The item that opened us handles its own toggle; give it this frame.
    setTimeout(() => { if (open) setOpen(false); }, 0);
  }, true);

  led.interval(500, () => { try { if (open) paint(); } catch (_e) { /* ignore */ } });

  return {
    open() { setOpen(true); },
    close() { setOpen(false); },
    toggle() { setOpen(!open); },
    isOpen() { return open; },
    send,
    unmount() {
      led.run();
      try { root.remove(); } catch (_e) { /* gone */ }
    },
  };
}
