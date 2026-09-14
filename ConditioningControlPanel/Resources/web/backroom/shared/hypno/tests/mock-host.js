/* ============================================================================
 * shared/hypno/tests/mock-host.js - a stand-in for the host's side of CONTRACT
 * 10.13 for station harnesses and checks (node or a page). It records every
 * fx, fx-tunnel, fx-release and media-request, answers fx with an ack shaped
 * like the host's (gates and Calm enforced the way BackRoomFxPlan does), and
 * deals media by the 10.13.C rule.
 *
 *   const host = createMockHost({ gates: { flash: false }, intensity: 'calm', deal: [urls] });
 *   const ctx = host.attach({ root, request, lex, ... });   // adds fx, fxRelease, fxTunnel, media, gates, onSettings, ...
 *   host.settings({ gates: { spiral: false } });          // a live settings frame
 *   host.fx / host.tunnel / host.release / host.media / host.calls
 *
 * Never a copy of the host's recipes: it only says what fired and what a gate
 * or Calm skipped, so a check can assert the calls a moment made.
 * ==========================================================================*/

const FALLBACK = ['gif0', 'gif1', 'gif2', 'gif3'].map((f) => new URL('../../../stations/slot/fallback/' + f + '.webp', import.meta.url).href);
const PRIM = { 'fx.wash': 'wash', 'fx.gif_from': 'gif-from', 'fx.loom_spiral': 'spiral-loom', 'fx.haze': 'haze' };
const GATE = { 'fx.wash': 'flash', 'fx.gif_from': 'flash', 'fx.loom_spiral': 'spiral', 'fx.haze': 'brainDrain' };
const WASH_GAP_MS = 360;

let seq = 0;
const mint = () => (Date.now().toString(16) + (++seq).toString(16).padStart(8, '0')).padStart(32, '0').slice(-32);
const readGates = (g, prev) => {
  const q = g && typeof g === 'object' ? g : {};
  const p = prev || { flash: true, subliminal: true, spiral: true, brainDrain: true };
  const pick = (k) => (k in q ? q[k] !== false : p[k]);
  return Object.freeze({ flash: pick('flash'), subliminal: pick('subliminal'), spiral: pick('spiral'), brainDrain: pick('brainDrain') });
};

/**
 * @param {Object} [o]
 * @param {Object} [o.gates]      partial gates, the rest on
 * @param {'calm'|'normal'|'full'} [o.intensity]
 * @param {boolean} [o.reduced]
 * @param {string[]} [o.deal]     urls of the "pool" dealt in order; empty deals the four fallback loops
 * @param {number} [o.seed]
 * @param {number} [o.latency]    ms before an ack or a media reply
 * @param {() => number} [o.now]  clock for the wash gap
 */
export function createMockHost({ gates = null, intensity = 'normal', reduced = false, motion = null, deal = [], seed = 7, latency = 0, now = () => Date.now() } = {}) {
  const st = { gates: readGates(gates), intensity, reduced: !!reduced, motion: motion || (reduced ? 'reduced' : 'full') };
  const subs = new Set();
  let lastWash = -Infinity;
  const host = { calls: [], fx: [], tunnel: [], release: [], media: [], logs: [] };
  const later = (v) => (latency > 0 ? new Promise((r) => setTimeout(() => r(v), latency)) : Promise.resolve(v));
  const calm = () => st.intensity === 'calm' || st.reduced || st.motion !== 'full';

  function ack(fxId, token) {
    const prim = PRIM[fxId] || fxId;
    if (!PRIM[fxId]) return { token, fired: [], skipped: [{ prim, why: 'unknown' }] };
    if (!st.gates[GATE[fxId]]) return { token, fired: [], skipped: [{ prim, why: 'toggle' }] };
    if (fxId === 'fx.haze' && (calm() || st.intensity !== 'full')) return { token, fired: [], skipped: [{ prim, why: 'calm' }] };
    if (fxId === 'fx.wash') {
      const t = now();
      if (t - lastWash < WASH_GAP_MS) return { token, fired: [], skipped: [{ prim, why: 'busy' }] };
      lastWash = t;
    }
    return { token, fired: [prim], skipped: [] };
  }

  const members = {
    fx(fxId, symbols, args) {
      const token = mint();
      const rec = { type: 'fx', at: now(), token, fxId: String(fxId), symbols: Array.isArray(symbols) ? symbols.map(String) : undefined,
        args: args && typeof args === 'object' ? JSON.parse(JSON.stringify(args)) : undefined };
      rec.ack = ack(rec.fxId, token);
      host.calls.push(rec); host.fx.push(rec);
      const p = later(rec.ack);
      p.token = token;
      return p;
    },
    fxRelease(token) {
      if (typeof token !== 'string' || !token) return;
      const rec = { type: 'fx-release', at: now(), token };
      host.calls.push(rec); host.release.push(rec);
    },
    fxTunnel(level) {
      const n = Number(level);
      if (!Number.isFinite(n)) return;
      const rec = { type: 'fx-tunnel', at: now(), level: Math.min(1, Math.max(0, n)), applied: st.gates.brainDrain };
      host.calls.push(rec); host.tunnel.push(rec);
    },
    media(opts) {
      const c = opts && opts.count;
      const count = Number.isInteger(c) && c >= 1 && c <= 13 ? c : 4;
      const pool = Array.from(new Set(deal));
      const gifs = pool.length
        ? pool.slice(0, count).map((url, i) => ({ key: 'g' + i, url, w: 0, h: 0, src: 'pool' }))
        : FALLBACK.map((url, i) => ({ key: 'g' + i, url, w: 180, h: 180, src: 'fallback' }));
      const rec = { type: 'media-request', at: now(), count: c, dealt: gifs.length };
      host.calls.push(rec); host.media.push(rec);
      return later({ reqId: mint(), seed, gifs, words: [] });
    },
    onSettings(fn) {
      if (typeof fn !== 'function') return () => {};
      subs.add(fn);
      return () => subs.delete(fn);
    },
    log(msg) { host.logs.push(String(msg)); },
  };

  /** Add the hypno ctx members to a station ctx (getters kept live). */
  host.attach = (target = {}) => {
    Object.assign(target, members);
    for (const k of ['gates', 'intensity', 'reduced', 'motion']) {
      Object.defineProperty(target, k, { get: () => st[k], enumerable: true, configurable: true });
    }
    return target;
  };
  host.ctx = host.attach({});
  /** A settings frame: patch any of gates, intensity, reduced, motion, then tell every subscriber. */
  host.settings = (patch = {}) => {
    if (patch.gates) st.gates = readGates(patch.gates, st.gates);
    if (patch.intensity) st.intensity = patch.intensity;
    if ('reduced' in patch) st.reduced = !!patch.reduced;
    if (patch.motion) st.motion = patch.motion;
    const frame = { motion: st.motion, intensity: st.intensity, reduced: st.reduced, gates: st.gates };
    for (const fn of Array.from(subs)) { try { fn(frame); } catch (e) { host.logs.push('onSettings threw: ' + e); } }
    return frame;
  };
  host.clear = () => { for (const k of ['calls', 'fx', 'tunnel', 'release', 'media', 'logs']) host[k].length = 0; lastWash = -Infinity; };
  /** fxIds in call order, with what the ack said. */
  host.summary = () => host.fx.map((r) => r.fxId + (r.ack.fired.length ? '' : ':' + r.ack.skipped[0].why));
  return host;
}
