/* ============================================================================
 * ui/hitStamps.js - the rubber stamp that says your throw landed.
 *
 * Three stamps, all driven by events the engine already raises:
 *   HIT        our payload_receipt came back `accepted`. Big, tilted, a THUD
 *              and a spray of ink. The one stamp worth being loud about.
 *   dull       our receipt came back refused (`rejected_rate` or
 *              `rejected_filtered`). Grey, smaller, a flat dud cue.
 *   incoming   their payload landed on us. Smaller, names the thing, and it
 *              rides the SAME landing time the HUD's chip uses (fireAtLocalMs),
 *              so it never arrives before the effect does. Silent: the HUD
 *              already plays the landing cue.
 *
 * Only the FIRST receipt per payload id stamps. The engine can send a second
 * terminal receipt later (completed / survived) and a second HIT for one throw
 * would be a lie.
 *
 * Cosmetic only. It reads events, writes nothing to the match, sends nothing.
 * Its layer is pointer-events:none so it can never eat a click, and it sits
 * under the toasts, the Mercy button and every sheet.
 *
 * Reduced motion (the page's own pref, the data-gg-motion attribute or the OS
 * query): no particles, no slam, no tilt animation, just the stamp and a fade.
 * ==========================================================================*/

import { GoonPayloadKind } from '../core/contracts.js';
import { GoonReceiptStatus } from '../core/scoring.js';
import { localMonotonicMs } from '../core/clock.js';

export const STAMP_MS = 950;
export const STAMP_MAX = 3;
const PARTICLES = 14;

const KIND_WORD = Object.freeze({
  [GoonPayloadKind.FlashBurst]: 'FLASHED',
  [GoonPayloadKind.SubliminalStorm]: 'WORDS',
  [GoonPayloadKind.BubbleSwarm]: 'BUBBLES',
  [GoonPayloadKind.Video]: 'VIDEO',
  [GoonPayloadKind.LockCard]: 'LOCK CARD',
  [GoonPayloadKind.ToyPattern]: 'BUZZ',
  [GoonPayloadKind.BrainDrain]: 'DRAINED',
  [GoonPayloadKind.Spiral]: 'SPIRAL',
});

/** What stamp (if any) a receipt for OUR payload earns. Pure. */
export function stampForReceipt(receipt) {
  const status = receipt && receipt.status;
  if (status === GoonReceiptStatus.Accepted) return { text: 'HIT', tone: 'hit', size: 'big', sfx: 'gg-hit' };
  if (status === GoonReceiptStatus.RejectedRate) return { text: 'TOO SOON', tone: 'dull', size: 'mid', sfx: 'gg-hit-dull' };
  if (status === GoonReceiptStatus.RejectedFiltered) return { text: 'BLOCKED', tone: 'dull', size: 'mid', sfx: 'gg-hit-dull' };
  return null;
}

/** What stamp an admitted inbound payload earns. Pure. */
export function stampForInbound(kind) {
  const word = KIND_WORD[kind];
  return { text: word || 'INCOMING', tone: 'in', size: 'small', sfx: null };
}

/** A small tilt in degrees, never flat, never silly. rnd() in [0,1). */
export function stampTilt(rnd = Math.random) {
  const r = Number(rnd()) || 0;
  const deg = -9 + r * 18;
  return Math.abs(deg) < 3 ? (deg < 0 ? -3 : 3) : Math.round(deg * 10) / 10;
}

function reducedMotion(prefs) {
  try { if (prefs && prefs.get && prefs.get('reduceMotion')) return true; } catch (_e) { /* ignore */ }
  try {
    if (typeof document !== 'undefined' && document.documentElement
      && document.documentElement.getAttribute('data-gg-motion') === 'reduced') return true;
  } catch (_e) { /* ignore */ }
  try { return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches; } catch (_e) { return false; }
}

/**
 * @param {{audio?:object, prefs?:object, logger?:object}} [o]
 * @returns {{attach(match), detach(), stamp(spec)}}
 */
export function createHitStamps({ audio = null, prefs = null, logger = null } = {}) {
  let unsubs = [];
  let timers = new Set();
  let layer = null;
  let seen = new Set();

  function ensureLayer() {
    if (typeof document === 'undefined' || !document.body) return null;
    if (layer && layer.isConnected) return layer;
    layer = document.createElement('div');
    layer.className = 'gg-stamps';
    layer.setAttribute('aria-hidden', 'true');
    document.body.appendChild(layer);
    return layer;
  }

  function later(fn, ms) {
    const t = setTimeout(() => { timers.delete(t); try { fn(); } catch (_e) { /* ignore */ } }, ms);
    timers.add(t);
  }

  function stamp(spec) {
    if (!spec) return;
    const host = ensureLayer();
    if (spec.sfx) { try { audio?.sfx?.(spec.sfx); } catch (_e) { /* stub bus */ } }
    if (!host) return;
    try {
      while (host.childElementCount >= STAMP_MAX) host.firstElementChild.remove();
      const calm = reducedMotion(prefs);
      const node = document.createElement('div');
      node.className = 'gg-stamp is-' + spec.tone + ' is-' + spec.size + (calm ? ' is-calm' : '');
      node.style.setProperty('--gg-stamp-rot', stampTilt() + 'deg');
      // Incoming stamps sit a little lower so a HIT and a landing never overlap.
      if (spec.tone === 'in') node.style.setProperty('--gg-stamp-y', '14vh');
      const face = document.createElement('span');
      face.className = 'gg-stamp-face';
      face.textContent = spec.text;
      node.appendChild(face);
      if (!calm && spec.tone !== 'dull') {
        const n = spec.size === 'big' ? PARTICLES : Math.round(PARTICLES / 2);
        for (let i = 0; i < n; i++) {
          const p = document.createElement('i');
          p.className = 'gg-stamp-ink';
          const a = (i / n) * Math.PI * 2 + Math.random() * 0.4;
          const dist = (spec.size === 'big' ? 110 : 60) + Math.random() * 70;
          p.style.setProperty('--dx', Math.round(Math.cos(a) * dist) + 'px');
          p.style.setProperty('--dy', Math.round(Math.sin(a) * dist) + 'px');
          p.style.setProperty('--s', (0.5 + Math.random() * 0.9).toFixed(2));
          node.appendChild(p);
        }
      }
      host.appendChild(node);
      later(() => { try { node.remove(); } catch (_e) { /* gone */ } }, STAMP_MS);
    } catch (e) {
      try { logger?.warn?.('hit stamp failed: ' + ((e && e.message) || e)); } catch (_e) { /* optional */ }
    }
  }

  return {
    attach(match) {
      this.detach();
      if (!match) return;
      const sub = (name, fn) => {
        try { if (typeof match[name] === 'function') unsubs.push(match[name](fn)); } catch (_e) { /* old engine */ }
      };
      sub('onPayloadReceiptReceived', (r) => {
        const id = r && r.id;
        if (!id || seen.has(id)) return;
        const spec = stampForReceipt(r);
        if (!spec) return;
        seen.add(id);
        stamp(spec);
      });
      sub('onPayloadAccepted', (e) => {
        const p = e && e.payload;
        if (!p) return;
        let wait = 0;
        try { wait = Math.max(0, (Number(e.fireAtLocalMs) || 0) - localMonotonicMs()); } catch (_e) { wait = 0; }
        later(() => stamp(stampForInbound(p.kind)), Math.min(wait, 30000));
      });
    },

    detach() {
      for (const off of unsubs) { try { if (typeof off === 'function') off(); } catch (_e) { /* ignore */ } }
      unsubs = [];
      for (const t of timers) { try { clearTimeout(t); } catch (_e) { /* ignore */ } }
      timers = new Set();
      seen = new Set();
      try { layer?.replaceChildren?.(); } catch (_e) { /* ignore */ }
    },

    stamp,
  };
}
