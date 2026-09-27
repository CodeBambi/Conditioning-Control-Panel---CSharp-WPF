/* ============================================================================
 * stations/breakout/haptics.js - the cabinet's touch.
 *
 *   createHaptics({ ctx, reduced, enabled }) -> { onEvent(name, d, s), frame(s, now), stop(), destroy() }
 *
 * station.js calls onEvent for every game event, frame once per rendered frame
 * and stop() on EVERY held frame (menu, pause, suspend), so stop() is idempotent.
 *
 * Three sinks take the same pulse:
 *   HOST     { type:'haptic', station:'breakout', level, ms, tag } through ctx.bridge.send (CONTRACT 10.23):
 *            the desktop app plays it on the Lovense / Intiface toy. The web shims ignore the type.
 *   PHONE    navigator.vibrate(ms scaled by level). iOS has no such call; guarded.
 *   GAMEPAD  vibrationActuator.playEffect('dual-rumble') on every connected pad. Guarded.
 *
 * A toy is NOT a waveform player: one command is one flat level for a duration, and a repeat of the
 * SAME level inside a second is silently dropped. So: flat pulses with distinct levels, about three a
 * second, a bigger moment may interrupt a smaller one, and a repeated level is nudged one toy step.
 * Haptics stay on under reduced motion: a pulse is not motion.
 * ==========================================================================*/

export const GAP_MS = 320;          // about three pulses a second
export const INTERRUPT_MS = 100;    // even an interrupt waits this long after the last send
export const NUDGE = 0.05;          // one step of a 0..20 toy; smaller would round to the same level
export const SAME_LEVEL_MS = 1100;
export const THUD_QUIET_MS = 1200;  // after the relapse thud: silence

const clamp01 = (v) => Math.min(1, Math.max(0, Number.isFinite(v) ? v : 0));
const P = (level, ms, priority, tag) => ({ level: Math.round(clamp01(level) * 1000) / 1000, ms, priority, tag });
/** These four cross the grey line by design; everything else in GREY is quiet. */
const ANY_STATE = new Set(['relapseStart', 'relapse', 'breakoutStart', 'breakout']);

/** Pure: a game event -> one flat pulse { level 0..1, ms, priority, tag } or null. */
export function planPulse(name, d, s) {
  d = d || {};
  const grey = !!s && s.state === 'grey';
  if (grey && !ANY_STATE.has(name)) {
    // Grey is dull: a brick is a faint tick, nothing else speaks.
    return name === 'hit' && d.kind !== 'paddle' && d.kind !== 'wall' ? P(0.08, 70, 1, 'grey') : null;
  }
  switch (name) {
    case 'hit': {
      if (d.kind === 'wall') return null;
      if (d.kind === 'paddle') return P(0.08, 60, 1, 'paddle');
      const climb = Math.min(1, Math.max(0, (Number(d.combo) || 1) - 1) / 15);
      return P(0.12 + 0.38 * climb, 90, 1, 'brick');
    }
    case 'brickDamage': return P(0.1, 70, 1, 'damage');
    case 'launch': return P(0.2, 90, 1, 'launch');
    case 'nearMiss': return P(0.25, 90, 1, 'nearMiss');
    case 'perfect': return P(0.45 + 0.05 * Math.min(5, Math.max(0, (Number(d.streak) || 1) - 1)), 120, 2, 'perfect');
    case 'bubblePop': return P(0.45 + 0.06 * Math.min(3, Number(d.tier) || 1), 170, 2, 'bubblePop');
    case 'capture': return P(0.5, 220, 2, 'capture');
    case 'powerCatch': return P(0.5, 180, 2, 'powerCatch');
    case 'powerSave': return P(0.6, 200, 2, 'powerSave');
    case 'lost': return P(0.35, 200, 2, 'lost');
    case 'word': return d.fired ? P(0.5, 260, 2, 'word') : null;
    case 'jackpot': return P(0.85, 600, 3, 'jackpot');
    case 'lastBrick': return P(0.75, 350, 3, 'lastBrick');
    case 'wall': return P(0.9, 800, 3, 'wall');
    case 'shatterWall': return P(0.9, 900, 3, 'shatterWall');
    case 'relapseStart': case 'relapse': return P(0.7, 450, 3, 'thud');
    case 'breakoutStart': return P(0.35, 140, 3, 'rise');
    case 'breakout': return P(1, 1200, 4, 'breakout');
    default: return null;
  }
}

/** Pure: the rate limit. offer(pulse, nowMs) -> the pulse to send (level maybe nudged) or null. */
export function createScheduler({ gapMs = GAP_MS } = {}) {
  let lastAt = -Infinity, lastLevel = -1, lastPriority = -1, busyUntil = -Infinity, busyPriority = -1, quietUntil = -Infinity;
  return {
    offer(p, now) {
      if (!p || !(p.level > 0) || !(p.ms > 0)) return null;
      if (now < quietUntil && p.priority < 3) return null;
      if (now < busyUntil && p.priority <= busyPriority) return null;
      if (now - lastAt < (p.priority > lastPriority ? INTERRUPT_MS : gapMs)) return null;
      let level = p.level;
      if (Math.abs(level - lastLevel) < NUDGE / 2 && now - lastAt < SAME_LEVEL_MS) level = level + NUDGE <= 1 ? level + NUDGE : level - NUDGE;
      level = Math.round(level * 1000) / 1000;
      lastAt = now; lastLevel = level; lastPriority = p.priority; busyUntil = now + p.ms; busyPriority = p.priority;
      if (p.tag === 'thud') quietUntil = now + p.ms + THUD_QUIET_MS;
      return { ...p, level };
    },
    /** When a refused pulse could go: the later of the gap and the pulse in play. */
    freeAt() { return Math.max(busyUntil, lastAt + gapMs); },
    reset() { busyUntil = -Infinity; busyPriority = -1; quietUntil = -Infinity; },
  };
}

function hostPost(ctx) {
  if (ctx && ctx.bridge && typeof ctx.bridge.send === 'function') return (m) => ctx.bridge.send(m);
  // No bridge on the context (a bare harness): the same webview path bridge.js uses, a no-op on the web.
  return (m) => { const w = globalThis.chrome && globalThis.chrome.webview; if (w && typeof w.postMessage === 'function') w.postMessage(m); };
}

export function createHaptics({ ctx = null, reduced = false, enabled = true, clock = null, nav = null, post = null } = {}) {
  if (enabled === false) return { onEvent() {}, frame() {}, stop() {}, destroy() {} };
  const now = () => (typeof clock === 'function' ? clock() : (typeof performance !== 'undefined' ? performance.now() : Date.now()));
  const navigatorOf = () => nav || (typeof navigator !== 'undefined' ? navigator : null);
  const toHost = typeof post === 'function' ? post : hostPost(ctx);
  const sched = createScheduler();
  let queue = [], dirty = false, dead = false, lastThudAt = -Infinity;

  const pads = () => { try { const n = navigatorOf(); return n && typeof n.getGamepads === 'function' ? Array.from(n.getGamepads() || []).filter(Boolean) : []; } catch (e) { return []; } };
  const quiet = (r) => { if (r && typeof r.catch === 'function') r.catch(() => {}); };

  function emit(p) {
    dirty = true;
    try { toHost({ type: 'haptic', station: 'breakout', level: p.level, ms: p.ms, tag: p.tag }); } catch (e) { /* host gone */ }
    try { const n = navigatorOf(); if (n && typeof n.vibrate === 'function') n.vibrate(Math.max(10, Math.round(p.ms * (0.4 + 0.6 * p.level)))); } catch (e) { /* no vibrate */ }
    for (const pad of pads()) {
      try {
        const a = pad.vibrationActuator;
        if (a && typeof a.playEffect === 'function') quiet(a.playEffect('dual-rumble', { startDelay: 0, duration: p.ms, strongMagnitude: p.level, weakMagnitude: Math.min(1, p.level * 1.4) }));
      } catch (e) { /* no rumble */ }
    }
  }

  function offer(p, t, born = t) {
    const out = sched.offer(p, t);
    if (out) emit(out);
    // A big moment that lost its slot waits for it (lastBrick then wall); chatter is simply dropped.
    else if (p && p.priority >= 3 && queue.length < 2) queue.push({ p, at: sched.freeAt(), born });
    return !!out;
  }

  function stop() {
    queue = [];
    if (!dirty) return;
    dirty = false; sched.reset();
    try { toHost({ type: 'haptic', station: 'breakout', level: 0, ms: 0, tag: 'stop' }); } catch (e) { /* host gone */ }
    try { const n = navigatorOf(); if (n && typeof n.vibrate === 'function') n.vibrate(0); } catch (e) { /* noop */ }
    for (const pad of pads()) { try { const a = pad.vibrationActuator; if (a && typeof a.reset === 'function') quiet(a.reset()); } catch (e) { /* noop */ } }
  }

  return {
    reduced,
    onEvent(name, d, s) {
      if (dead) return;
      const p = planPulse(name, d, s);
      if (!p) return;
      const t = now();
      // relapseStart and relapse are ONE thud, and whatever was waiting dies with the colour.
      if (p.tag === 'thud') { if (t - lastThudAt < 2500) return; lastThudAt = t; queue = []; }
      offer(p, t);
      // The way out of grey is a rising pair: the second, stronger pulse follows on the frame clock.
      if (name === 'breakoutStart') queue.push({ p: P(0.6, 220, 3, 'rise2'), at: t + 360, born: t });
    },
    frame() {
      if (dead) return;
      const t = now();
      if (queue.length) {
        const due = queue.filter((q) => q.at <= t);
        queue = queue.filter((q) => q.at > t);
        for (const q of due) if (t - q.born < 1500) offer(q.p, t, q.born);
      }
    },
    stop,
    destroy() { if (dead) return; stop(); dead = true; },
  };
}
export default createHaptics;
