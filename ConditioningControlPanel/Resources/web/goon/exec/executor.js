/* ============================================================================
 * exec/executor.js — the fan-out. core/match.js PLANS (it raises element cues
 * and admits payloads); this file PERFORMS, by routing each one to the renderer
 * that owns it and keeping the registry of what is currently running.
 *
 *   createExecutor({ media, layers, audio, logger, toyBridge, phrases })
 *     -> { attach(match), detach(), stopAll(), activeCount(), rendererFor(code) }
 *
 * WHAT IT SUBSCRIBES (the executor seam, core/match.js:48-53):
 *   onElementStartRequested   -> renderer.start(cue)        keyed by element code
 *   onElementIntensityChanged -> renderer.setIntensity(v)   re-tunes a running one
 *   onElementStopRequested    -> renderer.stop()
 *   onPayloadAccepted         -> schedule at fireAtLocalMs, then renderPayload()
 *
 * …plus ONE seam that is not the match's: the bubble field's `gg-bubble-pop`
 * document event, forwarded to the videos renderer when a `video` bubble pops
 * (see THE BUBBLE -> WINDOW SEAM below). It is subscribed and unsubscribed with
 * the match subscriptions and carries no receipts.
 *
 * WHAT IT PUBLISHES BACK (one number, added 2026-08-04):
 *   videos.onWindowCountChanged(n) -> match.setLocalWindowCount(n)
 * How many floating video windows are up, on its way to the state tick's `vwin`
 * and from there to the opponent's monitor. See THE WINDOW COUNT SEAM below.
 *
 * FIRE-AT-LOCAL: `fireAtLocalMs` is a LOCAL performance.now()-based instant
 * (match.js converts the sender's match-ms through MatchClock.matchMsToLocal,
 * falling back to now+MinScheduleBufferMs when the clock is unusable), so the
 * delay is simply fireAtLocalMs - localMonotonicMs(). Both players' clients fire
 * the same payload at the same wall instant; that is the whole point of the
 * schedule buffer, so we never "catch up" a late payload by firing it early.
 *
 * RECEIPTS — exactly once per payload id, and here is the semantic, mirrored
 * from match.js:582 / GoonMatchService.NotifyInboundPayloadFinished:
 *     endured = true  -> receipt `survived`, and the RECEIVER earns +1 charge
 *     endured = false -> receipt `completed`, no charge
 *   Neither refunds the sender (only rejected_* statuses do), so the honest
 *   reading is: "survived" = you took the whole thing, "completed" = it ran and
 *   ended without you taking it (interrupted by mercy/stopAll, timed out, or
 *   there was nothing in your library to render).
 *   Calling it after the match has ended is SAFE: match._send() no-ops once the
 *   match is disposed, and a live-but-ended match just posts a last receipt. We
 *   still skip the call when we have already detached, because then there is no
 *   match object to ask.
 *
 * The registry drives layers.setFxHeat(count) on every change, which is what
 * flips html[data-gg-fx] idle -> warm -> hot and hardens the HUD plates.
 * ==========================================================================*/

import { GoonElement, GoonPayloadKind, PAYLOAD_ELEMENT, enumName } from '../core/contracts.js';
import { localMonotonicMs } from '../core/clock.js';

import * as defaultLayers from './layers.js';
import { media as defaultMedia } from './media.js';

import { createFlashes } from './flashes.js';
import { createVideos } from './videos.js';
import { createSubliminals } from './subliminals.js';
import { createBubbles, POP_EVENT } from './bubbles.js';
import { createLockCards } from './lockCards.js';
import { createToys } from './toys.js';
import { createBrainDrain } from './brainDrain.js';
import { createBouncingText } from './bouncingText.js';
import { createSpiral } from './spiral.js';

/* Which element renders a given payload kind. THE TABLE MOVED to core/contracts.js on 2026-08-06
   and is re-exported here unchanged, because core/match.js's agreement gate now reads it too and
   core/ may not import exec/ (node-import-safe: no DOM at import). Same object, one truth — a
   renderer that runs a kind and a gate that admits it can no longer disagree about the element. */
export { PAYLOAD_ELEMENT };

const MAX_SCHEDULE_LEAD_MS = 190000;   // > MAX_PAYLOAD_DURATION_MS; a sane wire guard

/* ---------------------------------------------------- the bubble -> window seam
 * Popping a `video` bubble (exec/bubbles.js's prism kind) spawns ONE floating
 * video window. That is a SECOND renderer reacting to a FIRST renderer's event,
 * and this file is where those two are allowed to meet: exec/ modules do not
 * import one another (bubbles.js already publishes its pops as a document event
 * for ui/drops.js — "an event, not an import"), so the fan-out subscribes to the
 * very same seam and forwards the one kind that needs a partner.
 *
 * IT IS NOT A PAYLOAD and must never be mistaken for one: no id, no entry in
 * activePayloads, no receipt, no charge, and NO HEAT BUMP — activeCount() counts
 * what the MATCH is running, and a bubble the player popped for themselves is
 * not that. The window's own decoration already parks under html[data-gg-fx]
 * (exec/fx.css), which is the heat behaviour it is supposed to have.
 * -------------------------------------------------------------------------- */

/** The bubble kind that earns a window (exec/bubbles.js KINDS). */
export const VIDEO_BUBBLE_KIND = 'video';
/** How long an earned window may float. windowCapMs() clamps it anyway. */
export const BUBBLE_WINDOW_MS = 30000;
/** Its volume band, matching renderPayload's default so earned and thrown
 *  windows sound alike. */
export const BUBBLE_WINDOW_INTENSITY = 0.6;

export function createExecutor({ media, layers, audio, logger, toyBridge, phrases } = {}) {
  const L = layers || defaultLayers;
  const M = media || defaultMedia;
  const log = logger || null;
  const A = audio && typeof audio.sfx === 'function' ? audio : { sfx() {} };
  const bridge = typeof toyBridge === 'function' ? toyBridge : null;

  const info = (m) => { if (log && log.info) log.info(`[gg:exec] ${m}`); };
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:exec] ${m}`); };
  const error = (m) => { if (log && log.error) log.error(`[gg:exec] ${m}`); };

  const deps = { layers: L, media: M, audio: A, logger: log, phrases };

  // Declared before the renderers because one of them closes over it (see below).
  let match = null;

  /* ------------------------------------------- the window count seam (2026-08-04)
   * exec/videos.js owns the floating-window pool; core/match.js owns the wire. They
   * do not know about each other, and this file is where they are allowed to meet —
   * the same rule the bubble -> window seam above follows, just pointing the other
   * way: an EVENT comes in from a renderer, a NUMBER goes out to the match.
   *
   * The renderer is built once, at construct time, long before any match is
   * attached, so the callback closes over the mutable `match` instead of taking it:
   * an unattached executor drops the number on the floor, which is exactly right —
   * there is nobody to report to.
   *
   * PURELY ADDITIVE. It is not in activePayloads, it is not in activeElements, it
   * does NOT touch activeCount()/setFxHeat (a window the player popped for
   * themselves is not something the match is running — see the banner above) and
   * the match does nothing with the value except put it on its next tick. */
  function publishWindowCount(n) {
    if (!match || typeof match.setLocalWindowCount !== 'function') return;
    try { match.setLocalWindowCount(n); }
    catch (e) { warn(`setLocalWindowCount(${n}) threw: ${e && e.message}`); }
  }

  /** element code -> renderer. Built once; renderers are stateless until started. */
  const renderers = new Map([
    [GoonElement.Flashes, createFlashes(deps)],
    [GoonElement.Videos, createVideos(Object.assign({}, deps, { onWindowCountChanged: publishWindowCount }))],
    [GoonElement.Subliminals, createSubliminals(deps)],
    [GoonElement.Bubbles, createBubbles(deps)],
    [GoonElement.LockCards, createLockCards(deps)],
    [GoonElement.ToyPatterns, createToys({ logger: log, toyBridge: bridge })],
    [GoonElement.BrainDrain, createBrainDrain(deps)],
    [GoonElement.BouncingText, createBouncingText(deps)],
    [GoonElement.Spiral, createSpiral(deps)],
  ]);

  let unsubs = [];
  const activeElements = new Set();          // element codes with a sustained run
  const activePayloads = new Map();          // payload id -> {cancel, timer, kind, settle}

  const activeCount = () => activeElements.size + activePayloads.size;

  function syncHeat() {
    try { if (L && typeof L.setFxHeat === 'function') L.setFxHeat(activeCount()); }
    catch (e) { warn(`setFxHeat failed: ${e && e.message}`); }
  }

  function rendererFor(code) { return renderers.get(code) || null; }

  /* ------------------------------------------------------------- elements */

  function onStart(cue) {
    const c = cue || {};
    const r = rendererFor(c.element);
    if (!r) { warn(`no renderer for element ${enumName(GoonElement, c.element)}`); return; }
    try {
      if (activeElements.has(c.element)) { r.setIntensity(c.intensity); return; }
      r.start(c);
      activeElements.add(c.element);
      syncHeat();
      info(`start ${r.name} @ ${(+c.intensity || 0).toFixed(2)}`);
    } catch (e) { error(`start ${r.name} threw: ${(e && e.stack) || e}`); }
  }

  function onIntensity(cue) {
    const c = cue || {};
    const r = rendererFor(c.element);
    if (!r) return;
    try {
      // A tune for something we never started is a start — the ramp is the
      // authority on what should be running, not our bookkeeping.
      if (!activeElements.has(c.element)) { onStart(c); return; }
      r.setIntensity(c.intensity);
    } catch (e) { error(`setIntensity ${r.name} threw: ${(e && e.stack) || e}`); }
  }

  function onStop(cue) {
    const c = cue || {};
    const r = rendererFor(c.element);
    if (!r) return;
    try { r.stop(); } catch (e) { error(`stop ${r.name} threw: ${(e && e.stack) || e}`); }
    if (activeElements.delete(c.element)) syncHeat();
  }

  /* -------------------------------------------------- bubble -> video window */

  /**
   * One popped `video` bubble -> one local floating window. Everything this
   * handler can do is spawn; it never settles, never receipts and never touches
   * the registry (see the banner above the constants).
   */
  function onBubblePop(e) {
    const d = (e && e.detail) || {};
    if (d.kind !== VIDEO_BUBBLE_KIND) return;
    // Belt and braces on the mint rule: bubbles.js never mints `video` into an
    // inbound swarm (PAYLOAD_MINT_EXCLUDED), and if that ever regresses, the
    // opponent's clutter still buys them nothing here.
    if (d.payload) return;
    const r = rendererFor(GoonElement.Videos);
    if (!r || typeof r.spawnLocal !== 'function') return;
    try {
      const made = r.spawnLocal({ duration_ms: BUBBLE_WINDOW_MS, intensity: BUBBLE_WINDOW_INTENSITY });
      info(made ? 'video bubble popped -> local window' : 'video bubble popped -> fizzled (pool full or no clips)');
    } catch (err) { warn(`spawnLocal threw: ${err && err.message}`); }
  }

  /* ------------------------------------------------------------- payloads */

  function notifyFinished(id, endured) {
    if (!match || typeof match.notifyInboundPayloadFinished !== 'function') return;
    try { match.notifyInboundPayloadFinished(id, endured); }
    catch (e) { warn(`notifyInboundPayloadFinished(${id}) threw: ${e && e.message}`); }
  }

  function onPayload(evt) {
    const e = evt || {};
    const p = e.payload || {};
    const id = p.id;
    if (!id) { warn('payload with no id — ignored'); return; }
    if (activePayloads.has(id)) { warn(`duplicate payload ${id} — ignored (already running)`); return; }

    const element = PAYLOAD_ELEMENT[p.kind];
    const r = (element === undefined) ? null : rendererFor(element);
    if (!r) {
      // The engine already receipted `accepted`, so this needs a closing
      // receipt or the sender waits forever. Nothing ran: completed, no charge.
      warn(`no renderer for payload kind ${enumName(GoonPayloadKind, p.kind)} — receipting completed`);
      notifyFinished(id, false);
      return;
    }

    let settled = false;
    const entry = { cancel: null, timer: 0, kind: p.kind, settle: null };
    const settle = (endured) => {
      if (settled) return;
      settled = true;
      try { clearTimeout(entry.timer); } catch (_e) { /* ignore */ }
      activePayloads.delete(id);
      syncHeat();
      notifyFinished(id, endured);
      info(`payload ${id} ${enumName(GoonPayloadKind, p.kind)} finished (endured=${!!endured})`);
    };
    entry.settle = settle;
    activePayloads.set(id, entry);
    syncHeat();

    const fire = () => {
      if (settled) return;
      if (!activePayloads.has(id)) return;      // cancelled while pending
      try {
        entry.cancel = r.renderPayload(p, (endured) => settle(!!endured));
      } catch (err) {
        error(`renderPayload ${r.name} threw: ${(err && err.stack) || err}`);
        settle(false);
      }
    };

    const now = localMonotonicMs();
    const at = typeof e.fireAtLocalMs === 'number' && e.fireAtLocalMs === e.fireAtLocalMs ? e.fireAtLocalMs : now;
    const delay = Math.min(MAX_SCHEDULE_LEAD_MS, Math.max(0, at - now));
    info(`payload ${id} ${enumName(GoonPayloadKind, p.kind)} in ${Math.round(delay)}ms for ${p.duration_ms}ms`);
    entry.timer = setTimeout(fire, delay);
    if (entry.timer && typeof entry.timer.unref === 'function') entry.timer.unref();
  }

  /* ----------------------------------------------------------------- api
   * Methods never use `this` — the UI tier is free to destructure
   * (`const {stopAll} = createExecutor(...)`) without losing its binding. */

  const api = {
    /** Subscribe to a match's executor seam. Re-attaching detaches first. */
    attach(m) {
      if (!m) return;
      if (match) api.detach();
      match = m;
      const sub = (name, fn) => {
        if (typeof m[name] !== 'function') { warn(`match has no ${name}()`); return; }
        try {
          const off = m[name](fn);
          if (typeof off === 'function') unsubs.push(off);
        } catch (e) { error(`${name} subscribe failed: ${e && e.message}`); }
      };
      sub('onElementStartRequested', onStart);
      sub('onElementIntensityChanged', onIntensity);
      sub('onElementStopRequested', onStop);
      sub('onPayloadAccepted', onPayload);
      // The bubble field's pop seam is a DOCUMENT event, not a match one, so it
      // is subscribed by hand — and torn off with the rest on detach(), so a
      // pop after the match cannot conjure a window onto the recap.
      if (typeof document !== 'undefined' && document && typeof document.addEventListener === 'function') {
        try {
          document.addEventListener(POP_EVENT, onBubblePop);
          unsubs.push(() => { try { document.removeEventListener(POP_EVENT, onBubblePop); } catch (_e) { /* ignore */ } });
        } catch (e) { warn(`${POP_EVENT} subscribe failed: ${e && e.message}`); }
      }
      syncHeat();
      info(`attached (${unsubs.length} subscriptions)`);
    },

    /** Unsubscribe and tear every running effect down. Idempotent. */
    detach() {
      // stopAll FIRST, while `match` is still here, so in-flight payloads get
      // their closing receipt instead of vanishing.
      api.stopAll();
      for (const off of unsubs) { try { off(); } catch (_e) { /* already gone */ } }
      unsubs = [];
      match = null;
      info('detached');
    },

    /**
     * Kill every effect: pending payload fires, running payload renders (each
     * receipted `completed`), every sustained element, and the layers' DOM.
     */
    stopAll() {
      for (const [id, entry] of Array.from(activePayloads.entries())) {
        try { clearTimeout(entry.timer); } catch (_e) { /* ignore */ }
        if (typeof entry.cancel === 'function') {
          // The renderer's cancel calls back into our done(), which settles.
          try { entry.cancel(); } catch (e) { warn(`cancel ${id} threw: ${e && e.message}`); }
        }
        // Belt and braces: a renderer that ignored its cancel must not strand
        // the sender or leak a registry entry.
        if (activePayloads.has(id) && typeof entry.settle === 'function') entry.settle(false);
      }
      activePayloads.clear();

      for (const code of Array.from(activeElements)) {
        const r = rendererFor(code);
        if (!r) continue;
        try { r.stop(); } catch (e) { error(`stop ${r.name} threw: ${(e && e.stack) || e}`); }
      }
      activeElements.clear();

      // The floating video windows the player EARNED (a popped `video` bubble) have no payload
      // and no cancel fn, so neither loop above reaches them — and layers.stopAll() below only
      // deletes their NODES, leaving live records in the renderer's pool. Harmless while the pool
      // was private; not harmless now that it is reported to the opponent as `vwin`. Swept here,
      // BEFORE the DOM goes, so each one settles properly (and the count lands on 0).
      const vids = rendererFor(GoonElement.Videos);
      if (vids && typeof vids.stopWindows === 'function') {
        try { const swept = vids.stopWindows(); if (swept) info(`swept ${swept} floating window(s)`); }
        catch (e) { warn(`stopWindows threw: ${e && e.message}`); }
      }

      // Elements that were never in our registry (a renderer restarted by a
      // stray cue, say) still lose their nodes here: the layers own the DOM.
      try { if (L && typeof L.stopAll === 'function') L.stopAll(); }
      catch (e) { warn(`layers.stopAll failed: ${e && e.message}`); }
      syncHeat();
    },

    /** How many effects are running (elements + payload renders). */
    activeCount,

    /** Escape hatch for the UI tier (e.g. a preview button). Read-only use. */
    rendererFor,
  };

  return api;
}

export default createExecutor;
