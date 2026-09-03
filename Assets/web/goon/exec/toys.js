/* ============================================================================
 * exec/toys.js — GoonElement.ToyPatterns (5) + GoonPayloadKind.ToyPattern (5).
 *
 * NO DOM. This renderer's whole job is to translate cues and payloads into the
 * two messages the host understands and hand them to the bridge:
 *
 *   { type:'toy-pattern', intensity, durationMs, pattern, source:'element'|'payload' }
 *   { type:'toy-stop' }
 *
 * The bridge (sibling H passes `bridge.send` in, or null) may be absent —
 * standalone in a browser there is no host, and caps.haptics is false until the
 * haptics-v2 overhaul lands. A null bridge is NOT an error: the renderer logs
 * what it would have sent and stays perfectly in step, so the executor's active
 * registry and the HUD's heat are identical with and without a toy.
 *
 * PRECEDENCE: a payload OUTRANKS the element bed while it runs (it cost the
 * opponent 2 charges), and when it ends the bed is restored — not stopped. Only
 * the last live run sends 'toy-stop', so a finished payload can never silence a
 * toy the ramp still wants running.
 *
 * Uniform renderer shape — see the banner in exec/flashes.js. (This module takes
 * one extra dependency, `toyBridge`, in the same options bag.)
 * ==========================================================================*/

const clamp01 = (n) => (typeof n === 'number' && n === n ? (n < 0 ? 0 : n > 1 ? 1 : n) : 0);
const soon = (fn, ms) => {
  const t = setTimeout(fn, Math.max(0, ms | 0));
  if (t && typeof t.unref === 'function') t.unref();
  return t;
};

export function createToys({ logger, toyBridge } = {}) {
  const log = logger || null;
  const info = (m) => { if (log && log.info) log.info(`[gg:toys] ${m}`); };
  const warn = (m) => { if (log && log.warn) log.warn(`[gg:toys] ${m}`); };

  const send = (msg) => {
    if (typeof toyBridge !== 'function') { info(`no toy bridge — would send ${JSON.stringify(msg)}`); return false; }
    try { toyBridge(msg); return true; }
    catch (e) { warn(`toy bridge threw: ${e && e.message}`); return false; }
  };

  let element = null;        // {intensity, durationMs, pattern}
  const payloads = new Set();

  function pushElement() {
    if (!element) return;
    send({
      type: 'toy-pattern',
      intensity: element.intensity,
      durationMs: element.durationMs,
      pattern: element.pattern,
      source: 'element',
    });
  }

  function releaseOne() {
    // Last one out turns the toy off; otherwise hand control back to the bed.
    if (payloads.size === 0 && !element) send({ type: 'toy-stop' });
    else if (payloads.size === 0 && element) pushElement();
  }

  return {
    name: 'toys',

    start(cue) {
      const c = cue || {};
      element = {
        intensity: clamp01(c.intensity),
        durationMs: Math.max(0, c.durationMs | 0),     // 0 = sustained until stop
        pattern: c.pattern || null,
      };
      if (payloads.size === 0) pushElement();          // a live payload keeps the floor
    },

    setIntensity(v) {
      if (!element) return;
      element.intensity = clamp01(v);
      if (payloads.size === 0) pushElement();
    },

    stop() {
      element = null;
      if (payloads.size === 0) send({ type: 'toy-stop' });
    },

    /** ToyPattern payload: outranks the bed for duration_ms, then hands it back. */
    renderPayload(payload, done) {
      const p = payload || {};
      const durationMs = Math.max(1000, (p.duration_ms | 0) || 30000);
      const run = { endTimer: 0 };
      payloads.add(run);

      send({
        type: 'toy-pattern',
        intensity: clamp01(p.intensity !== undefined ? p.intensity : 0.6),
        durationMs,
        pattern: p.pattern || null,
        source: 'payload',
      });

      let finished = false;
      const settle = (endured) => {
        if (finished) return;
        finished = true;
        try { clearTimeout(run.endTimer); } catch (_e) { /* ignore */ }
        payloads.delete(run);
        releaseOne();
        if (typeof done === 'function') { try { done(endured); } catch (e) { warn(`done() threw: ${e && e.message}`); } }
      };
      run.endTimer = soon(() => settle(true), durationMs);
      return () => settle(false);
    },
  };
}

export default createToys;
