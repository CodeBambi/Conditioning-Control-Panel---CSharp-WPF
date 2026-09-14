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

/* ----------------------------------------------------------------------------
 * THE VOICE-NOTE HOOK (docs/GOON_VOICE_PLAN.md §UI "Emote hook").
 *
 * A pre-recorded note can be pinned to an emote; firing that emote also sends
 * the note, and it plays on the other side with the bubble. The rule this hook
 * lives by is one line long and it is the whole design:
 *
 *   THE EMOTE NEVER WAITS FOR THE NOTE. Not one frame. `match.sendEmote` has
 *   already happened by the time anything below runs, the send is fired and
 *   forgotten, every failure is swallowed, and a voice tier that is missing,
 *   broken or throwing is indistinguishable from one that simply had no note
 *   pinned to this emote. Six icons in a sheet must not be able to fail because
 *   of a microphone feature.
 *
 * ...WHICH IS ALSO HOW THE SEND PERK LANDS HERE (2026-08-06). Sending voice notes
 * is tier 1+ now, gated at the one choke point in ui/voice/voiceService.js
 * (`sendBlob` -> reason 'not-entitled'). Nothing in this file learned a new fact:
 * an ungated seat's emote goes out exactly as it always did and simply does not
 * carry its note, the rejected promise is swallowed by the same handler below,
 * and the player is told once in the lobby (S.voice.lobbyNoPerk) rather than six
 * times a match by a toast. NO ERROR SPAM is the requirement, and the existing
 * fire-and-forget shape is what satisfies it.
 *
 * IT IS A MODULE-LEVEL PROVIDER, set by boot.js, rather than a mountEmotes()
 * dependency — because this sheet is mounted by ui/hud.js, which is owned by
 * another wave and cannot be asked to thread a new dep through. An instance
 * provider passed to mountEmotes() wins over the module one where both exist,
 * so the day the HUD does thread it, nothing here has to change.
 *
 * The provider is asked for BOTH halves at once — the service and the note id —
 * so this file needs to know nothing about prefs, the emote map or the store.
 * -------------------------------------------------------------------------- */

/** @type {null|((emoteKey:string) => ({voice:object, noteId:string}|null))} */
let moduleVoiceProvider = null;

/**
 * boot.js calls this once. Pass null to unhook (a page teardown).
 * @param {null|((emoteKey:string) => ({voice:object, noteId:string}|null))} fn
 */
export function setVoiceProvider(fn) {
  moduleVoiceProvider = typeof fn === 'function' ? fn : null;
}

/** The provider currently in force. Exported for the self-tests only. */
export function getVoiceProvider() { return moduleVoiceProvider; }

/**
 * Fire the note pinned to `emoteKey`, if there is one. Returns true when a send
 * was STARTED (not when it succeeded — nothing here ever learns that).
 * Synchronous, total, and it swallows everything.
 */
export function fireVoiceForEmote(emoteKey, provider, onLog) {
  const p = typeof provider === 'function' ? provider : moduleVoiceProvider;
  if (!p) return false;
  try {
    const key = String(emoteKey || '');
    if (!key) return false;
    const hit = p(key);
    if (!hit || !hit.voice || !hit.noteId) return false;
    if (typeof hit.voice.sendNote !== 'function') return false;
    // Fire and forget, with the rejection handler attached in the same
    // expression: an unhandled rejection out of an emote click would surface as
    // a page-level error for a feature the player did not even use.
    const p2 = hit.voice.sendNote(hit.noteId, { emote: key });
    if (p2 && typeof p2.then === 'function') {
      p2.then(
        (res) => { if (onLog && res && !res.ok) { try { onLog({ t: 'voice-note-dropped', emote: key, reason: res.reason }); } catch (_e) { /* ignore */ } } },
        () => { /* the service already logged it; the emote is long gone */ },
      );
    }
    return true;
  } catch (_e) {
    // A provider that throws is a bug in boot, not a reason the emote failed.
    return false;
  }
}

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
 * @param {Function} [o.voiceProvider]  see THE VOICE-NOTE HOOK above — an
 *                   instance override for the module-level provider boot sets.
 * @returns {{unmount:Function, open:Function, close:Function, toggle:Function, isOpen:Function}}
 */
export function mountEmotes({ host, match, audio = null, onLog = null, voiceProvider = null } = {}) {
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
    /* THE VOICE NOTE RIDES ALONG — AFTER the emote is on the wire, never before,
     * and never awaited. The key is the ICON where there is one (that is what the
     * picker in ui/screens/voice.js binds and what travels in the `emote` field
     * of the voice meta); a preset LINE is accepted as a key too, so a future
     * picker that offers the sentences needs no change here. */
    fireVoiceForEmote(icon || text, voiceProvider, onLog);
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
