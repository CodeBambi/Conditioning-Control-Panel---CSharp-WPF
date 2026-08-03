/* ============================================================================
 * ui/announcer.js — the ribbon that says what is about to happen to BOTH of you.
 *
 * One narrow glass plate, top-centre, under the timer. It says two kinds of
 * thing and nothing else:
 *
 *   PRE   ~4 s before the ramp starts an element — "Get ready to watch!"
 *   ON    the moment it actually starts          — "Video on"
 *
 * WHY THIS IS HONEST FOR BOTH PLAYERS. core/draft.js buildRamp is a pure
 * function of (shared pool, match seed, duration): both machines roll the
 * IDENTICAL schedule and neither sends it. So reading our own ramp and saying
 * "get ready to watch" is a true statement about the other player too. Nothing
 * here crosses the wire, and nothing here needs the peer to be alive.
 *
 * WHY THERE ARE TWO SOURCES. A ramp element and an opponent payload both end up
 * at the same renderer, but they arrive on DIFFERENT engine events:
 *   - the ramp raises onElementStartRequested (core/match.js _pumpRamp);
 *   - a payload raises onPayloadAccepted and exec/executor.js maps its kind to
 *     an element itself — it never becomes an element cue.
 * So we subscribe to both, and mirror executor's kind->element table below as a
 * literal, exactly the way ui/hud.js mirrors BUBBLE_POP_EVENT: ui/ never
 * imports exec/.
 *
 * WHAT IT REFUSES TO SAY:
 *   - BUBBLES. They are always on, for both players, from t=0 to the end
 *     (core/draft.js ALWAYS_ON_ELEMENT). Announcing them would fire once at
 *     zero every single match and mean nothing.
 *   - STOPS. "it ended" is not news; the fx rail already drains its chip.
 *
 * MOTION. Two one-shots (the slide-in and the slide-out) and one decorative
 * sheen, and never a loop — the ribbon costs the chrome budget nothing to leave
 * on screen. `animation` is a shorthand, so the in/out pair live on mutually
 * exclusive classes and the sheen lives on its own child element. Under
 * .is-calm and prefers-reduced-motion every one of them is off and the banner
 * still appears: the retire timer is JS, not `animation-fill-mode`, precisely
 * so the words survive with the motion switched off.
 *
 * POINTER-EVENTS: NONE, everywhere, including the plate — .gg-hud-frame
 * .gg-plate turns them back on for the rest of the desk and this must opt out
 * again or a banner would eat a click meant for #gg-stage.
 *
 * Node-import-safe: no DOM at import, only inside mountAnnouncer().
 * ==========================================================================*/

import { GoonElement, GoonMatchPhase, GoonPayloadKind } from '../core/contracts.js';
import { GoonCueAction } from '../core/draft.js';
import { localMonotonicMs } from '../core/clock.js';
import { S } from './strings.js';

// ------------------------------------------------------------------ tuning

/** How far ahead of a Start cue the "get ready" lands. */
export const ANNOUNCE_LEAD_MS = 4000;
/** How long one banner sits before it starts leaving. */
export const ANNOUNCE_DWELL_MS = 2200;
/** The outro, and the JS timer that removes the node whether or not it played. */
export const ANNOUNCE_OUT_MS = 260;
/** Two banners for the SAME element inside this window are one event seen twice. */
export const ANNOUNCE_DEDUPE_MS = 3000;
/** Shortest a banner may hold the slot before a newer one may cut in. */
export const ANNOUNCE_MIN_HOLD_MS = 900;
/** The ramp look-ahead poll. */
export const ANNOUNCE_POLL_MS = 250;
/** Backlog cap. Deeper than this and the oldest is stale news anyway. */
export const ANNOUNCE_QUEUE_CAP = 4;

/**
 * The lead has to OUTRUN the dedupe window, or the "on" line for an element we
 * just pre-announced would be swallowed as a duplicate of its own warning.
 * This is a real constraint, not a coincidence — the suite pins it.
 */
export const ANNOUNCE_LEAD_BEATS_DEDUPE = ANNOUNCE_LEAD_MS > ANNOUNCE_DEDUPE_MS;

/** Never announced. Bubbles are the always-on baseline — see the header. */
export const ANNOUNCE_SKIP_ELEMENTS = Object.freeze([GoonElement.Bubbles]);

/** exec/executor.js PAYLOAD_ELEMENT, mirrored as a literal so ui/ never imports exec/. */
export const PAYLOAD_ANNOUNCE_ELEMENT = Object.freeze({
  [GoonPayloadKind.FlashBurst]: GoonElement.Flashes,
  [GoonPayloadKind.SubliminalStorm]: GoonElement.Subliminals,
  [GoonPayloadKind.BubbleSwarm]: GoonElement.Bubbles,
  [GoonPayloadKind.Video]: GoonElement.Videos,
  [GoonPayloadKind.LockCard]: GoonElement.LockCards,
  [GoonPayloadKind.ToyPattern]: GoonElement.ToyPatterns,
  [GoonPayloadKind.BrainDrain]: GoonElement.BrainDrain,
  [GoonPayloadKind.Spiral]: GoonElement.Spiral,
});

/** The little mark in front of the words. Same vocabulary as ui/hud.js's rail chips. */
export const ANNOUNCE_GLYPH = Object.freeze({
  [GoonElement.Flashes]: '✦',
  [GoonElement.Videos]: '▶',
  [GoonElement.Subliminals]: '≋',
  [GoonElement.LockCards]: '▢',
  [GoonElement.ToyPatterns]: '∿',
  [GoonElement.BrainDrain]: '◍',
  [GoonElement.BouncingText]: '⇄',
  [GoonElement.Spiral]: '◎',
});

/** True for every element the ribbon is allowed to name. */
export function isAnnounceable(element) {
  if (typeof element !== 'number' || !Number.isFinite(element)) return false;
  if (ANNOUNCE_SKIP_ELEMENTS.indexOf(element) >= 0) return false;
  for (const k of Object.keys(GoonElement)) if (GoonElement[k] === element) return true;
  return false;
}

/** Every element that must therefore have copy. The string check in the suite walks this. */
export const ANNOUNCEABLE_ELEMENTS = Object.freeze(
  Object.keys(GoonElement).map((k) => GoonElement[k]).filter(isAnnounceable),
);

// ------------------------------------------------------------------ the pure bits

/**
 * Which Start cues fall in (nowMs, nowMs + leadMs] AND would really be a start.
 *
 * The engine turns a Start for an ALREADY-ACTIVE element into an intensity bump
 * (core/match.js _pumpRamp), so this replays the schedule's active set from the
 * top and skips those — a "get ready to watch" for a video that is already
 * playing is a lie the ribbon must not tell. Stops, intensity bumps and the
 * always-on bubbles never produce an entry.
 *
 * PURE: no clock, no DOM, no engine. The ramp is read, never mutated.
 *
 * @param {Array<{offsetMs:number,action:number,element:number}>} ramp sorted cues
 * @param {number} nowMs   live-elapsed milliseconds
 * @param {number} [leadMs]
 * @returns {Array<{element:number, atMs:number, inMs:number}>} in schedule order
 */
export function upcomingAnnouncements(ramp, nowMs, leadMs = ANNOUNCE_LEAD_MS) {
  const out = [];
  if (!ramp || typeof ramp.length !== 'number') return out;
  const now = Number(nowMs) || 0;
  const lead = Math.max(0, Number(leadMs) || 0);
  const until = now + lead;
  const active = new Set();

  for (let i = 0; i < ramp.length; i++) {
    const cue = ramp[i];
    if (!cue) continue;
    const at = Number(cue.offsetMs) || 0;
    if (at > until) break;                       // the ramp is sorted: nothing later is in the window
    switch (cue.action) {
      case GoonCueAction.Start:
        if (active.has(cue.element)) break;      // becomes an Intensity, not a start
        active.add(cue.element);
        if (at > now && isAnnounceable(cue.element)) out.push({ element: cue.element, atMs: at, inMs: at - now });
        break;
      case GoonCueAction.Stop:
        active.delete(cue.element);
        break;
      default:
        break;                                   // Intensity changes nothing we care about
    }
  }
  return out;
}

/**
 * One ribbon slot's worth of admission control.
 *
 * Per-ELEMENT dedupe, not per-line: "Get ready to watch!" then "Video on" is one
 * element twice, and only the lead being longer than the window lets the second
 * one through. The one deliberate bypass is the pre -> on upgrade, so a warning
 * that is somehow still on screen when the thing actually starts is REPLACED by
 * the real line instead of being dropped as a duplicate of itself.
 *
 * PURE-ish: a closure over two maps, no clock of its own — callers pass the time.
 */
export function createAnnounceQueue({ dedupeMs = ANNOUNCE_DEDUPE_MS, cap = ANNOUNCE_QUEUE_CAP } = {}) {
  const last = new Map();      // element -> {at, kind}
  const pending = [];

  return {
    /**
     * @param {number} element
     * @param {'pre'|'on'} kind
     * @param {number} atMs monotonic-ish now
     * @returns {boolean} true when it was admitted
     */
    offer(element, kind, atMs) {
      if (!isAnnounceable(element)) return false;
      if (kind !== 'pre' && kind !== 'on') return false;
      const at = Number(atMs) || 0;
      const prev = last.get(element);
      const upgrade = !!prev && prev.kind === 'pre' && kind === 'on';
      if (prev && (at - prev.at) < dedupeMs && !upgrade) return false;
      last.set(element, { at, kind });
      pending.push({ element, kind, atMs: at });
      while (pending.length > cap) pending.shift();
      return true;
    },
    take() { return pending.shift() || null; },
    peek() { return pending[0] || null; },
    get size() { return pending.length; },
    reset() { last.clear(); pending.length = 0; },
  };
}

/** The words. Missing copy is a missing announcement, never a blank plate. */
export function announceText(element, kind) {
  const table = S && S.announce ? (kind === 'on' ? S.announce.on : S.announce.ready) : null;
  const line = table ? table[element] : null;
  return typeof line === 'string' && line.length > 0 ? line : '';
}

// ------------------------------------------------------------------ dom helpers

const doc = () => (typeof document !== 'undefined' ? document : null);

function el(tag, cls2, text2) {
  const d = doc();
  if (!d || typeof d.createElement !== 'function') return null;
  const n = d.createElement(tag);
  if (cls2 && n) n.className = cls2;
  if (text2 != null && n) n.textContent = String(text2);
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

function createLedger() {
  const list = [];
  return {
    add(fn) { if (typeof fn === 'function') list.push(fn); },
    timer(id) { list.push(() => { try { clearTimeout(id); } catch (_e) { /* gone */ } }); return id; },
    interval(ms, fn) {
      if (typeof setInterval !== 'function') return 0;
      const id = setInterval(fn, ms);
      list.push(() => { try { clearInterval(id); } catch (_e) { /* gone */ } });
      return id;
    },
    run() { while (list.length) { const fn = list.pop(); try { fn(); } catch (_e) { /* keep unwinding */ } } },
  };
}

function nowLocalMs() {
  try { return localMonotonicMs(); } catch (_e) { return Date.now(); }
}

// ------------------------------------------------------------------ mount

/**
 * @param {object} o
 * @param {object} o.host   the .gg-hud-frame (the ribbon is absolutely placed inside it)
 * @param {object} o.match  GoonMatchService
 * @param {object} [o.audio] {sfx(id)} — the ribbon gets ONE soft cue, see show()
 * @param {Function|object} [o.onLog]
 * @returns {{root:object|null, unmount:Function, showing:Function, queued:Function}}
 */
export function mountAnnouncer({ host, match, audio = null, onLog = null } = {}) {
  const led = createLedger();
  const root = el('div', 'gg-announce');
  if (!root || !host || !match) {
    return { root: null, unmount() { led.run(); }, showing: () => null, queued: () => 0 };
  }
  if (root.setAttribute) {
    root.setAttribute('aria-live', 'polite');
    root.setAttribute('aria-atomic', 'true');
    root.setAttribute('role', 'status');
  }
  add(host, root);

  const q = createAnnounceQueue();
  const seenCues = new Set();     // element@offsetMs — one pre-announce per scheduled run
  let ramp = [];
  let current = null;             // {element, kind, node, shownAt, retired}
  let polling = false;

  function log(entry) {
    if (!onLog) return;
    try {
      if (typeof onLog === 'function') onLog(entry);
      else if (typeof onLog.push === 'function') onLog.push(entry);
    } catch (_e) { /* the log is never load-bearing */ }
  }

  /** The ribbon is a CAPTION, so its cue sits under every pop in the mix (see
   *  the 'announce-in' gain in ui/audio.js) and there is only one of it — the
   *  slide-OUT is deliberately silent, exactly like the fx rail draining a chip. */
  function cue() {
    try { if (audio && typeof audio.sfx === 'function') audio.sfx('announce-in'); } catch (_e) { /* stub */ }
  }

  function livePhase() {
    const p = match.phase;
    return p === GoonMatchPhase.Live || p === GoonMatchPhase.SuddenDeath;
  }

  function elapsedMs() {
    try { return match.liveElapsedMs | 0; } catch (_e) { return 0; }
  }

  function readRamp() {
    try {
      const r = match.rampCues;
      return r && typeof r.length === 'number' ? r : [];
    } catch (_e) { return []; }
  }

  // ------------------------------------------------------------- the slot

  function show(item) {
    const line = announceText(item.element, item.kind);
    if (!line) return;                                   // no copy, no banner
    const node = el('div', 'gg-announce-slot gg-plate ' + (item.kind === 'on' ? 'is-now' : 'is-pre'));
    if (!node) return;
    add(node, el('i', 'gg-announce-sheen'));
    add(node, el('span', 'gg-announce-glyph', ANNOUNCE_GLYPH[item.element] || '•'));
    add(node, el('span', 'gg-announce-text', line));
    add(root, node);
    cls(node, 'is-in', true);
    cue();

    const rec = { element: item.element, kind: item.kind, node, shownAt: nowLocalMs(), retired: false, timers: [] };
    current = rec;
    rec.timers.push(led.timer(setTimeout(() => retire(rec), ANNOUNCE_DWELL_MS)));
    log({ t: 'announce', kind: item.kind, element: item.element, text: line });
  }

  function retire(rec) {
    if (!rec || rec.retired) return;
    rec.retired = true;
    cls(rec.node, 'is-in', false);
    cls(rec.node, 'is-out', true);
    // The removal is a TIMER, not animation-fill-mode: with motion switched off
    // the outro never plays and the node would otherwise sit there forever.
    rec.timers.push(led.timer(setTimeout(() => {
      try { rec.node.remove(); } catch (_e) { /* gone */ }
      if (current === rec) { current = null; pump(); }
    }, ANNOUNCE_OUT_MS)));
  }

  function pump() {
    if (current || !livePhase()) return;
    const next = q.take();
    if (next) show(next);
  }

  /** Admitted -> either take the slot now, or ask the sitting banner to leave. */
  function offer(element, kind) {
    if (!livePhase()) return false;
    if (!q.offer(element, kind, nowLocalMs())) return false;
    if (!current) { pump(); return true; }

    // The sitting banner steps aside for the newcomer — but not before it has
    // been readable. The one exception is its OWN element starting for real:
    // the warning is replaced by the fact immediately, never queued behind it.
    const upgrade = current.kind === 'pre' && kind === 'on' && current.element === element;
    const heldFor = nowLocalMs() - current.shownAt;
    if (upgrade || heldFor >= ANNOUNCE_MIN_HOLD_MS) { retire(current); return true; }
    const rec = current;
    led.timer(setTimeout(() => {
      if (current === rec && q.size > 0) retire(rec);
    }, Math.max(0, ANNOUNCE_MIN_HOLD_MS - heldFor)));
    return true;
  }

  // ------------------------------------------------------- the ramp look-ahead

  function poll() {
    if (!livePhase() || match.phase !== GoonMatchPhase.Live) return;
    if (ramp.length === 0) ramp = readRamp();
    const now = elapsedMs();
    const soon = upcomingAnnouncements(ramp, now, ANNOUNCE_LEAD_MS);
    for (const a of soon) {
      const key = a.element + '@' + a.atMs;
      if (seenCues.has(key)) continue;
      seenCues.add(key);
      offer(a.element, 'pre');
    }
  }

  function startPolling() {
    if (polling) return;
    polling = true;
    ramp = readRamp();
    led.interval(ANNOUNCE_POLL_MS, () => { try { poll(); } catch (_e) { /* never take the match down */ } });
  }

  function clearAll() {
    q.reset();
    if (current) {
      try { current.node.remove(); } catch (_e) { /* gone */ }
      current = null;
    }
  }

  // ------------------------------------------------------------ subscriptions

  function sub(name, fn) {
    if (typeof match[name] !== 'function') return;
    const off = match[name](fn);
    led.add(typeof off === 'function' ? off : null);
  }

  // The ramp's own starts.
  sub('onElementStartRequested', (cue) => {
    if (!cue) return;
    offer(cue.element, 'on');
  });

  // Theirs. A payload never becomes an element cue (see the header), and it is
  // scheduled ahead of time — announce it when it actually lands on us.
  sub('onPayloadAccepted', (e) => {
    const p = e && e.payload;
    if (!p) return;
    const element = PAYLOAD_ANNOUNCE_ELEMENT[p.kind];
    if (element === undefined || !isAnnounceable(element)) return;
    const wait = Math.max(0, (e.fireAtLocalMs || 0) - nowLocalMs());
    led.timer(setTimeout(() => offer(element, 'on'), Math.min(wait, 190000)));
  });

  sub('onPhaseChanged', (phase) => {
    if (phase === GoonMatchPhase.Live) {
      // A fresh roll every time Live is entered — never trust a ramp captured
      // before this one was built.
      seenCues.clear();
      ramp = readRamp();
      startPolling();
      return;
    }
    if (phase === GoonMatchPhase.SuddenDeath) return;   // no ramp left to read, but a payload may still land
    clearAll();
  });

  if (match.phase === GoonMatchPhase.Live) startPolling();

  return {
    root,
    /** What is on screen right now, for a play-test driver. */
    showing: () => (current ? { element: current.element, kind: current.kind, node: current.node } : null),
    queued: () => q.size,
    unmount() {
      clearAll();
      seenCues.clear();
      led.run();
      try { root.remove(); } catch (_e) { /* gone */ }
    },
  };
}

export default mountAnnouncer;
