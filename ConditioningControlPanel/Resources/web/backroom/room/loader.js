/* ============================================================================
 * backroom/room/loader.js - how a station takes the screen and gives it back
 * (CONTRACT section 7). The room calls mount(ctx) and the four methods it
 * returns, and nothing else.
 *
 *   soon   -> a dust-sheet card. No code is loaded, no model is asked for.
 *   live   -> import(entry), mount(ctx), open(). A module that fails to load or
 *             to mount gets a plain card instead, and the room stays usable.
 *
 * LAW VI: Back is live before open() resolves and while close() runs. close()
 * gets CLOSE_BUDGET_MS; a station that has not settled by then is destroyed
 * anyway, because nobody waits on a station to leave a room.
 * ==========================================================================*/

import * as bridge from '../bridge.js';

export const CLOSE_BUDGET_MS = 420;
export const REQUEST_TIMEOUT_MS = 6000;

const withTimeout = (p, ms) => Promise.race([Promise.resolve(p).catch(() => {}), new Promise((r) => setTimeout(r, ms))]);

/**
 * @param {Object} room  { layer, state, lex(key, fallback), onSp(fn), standUp(), log(level,msg) }
 */
export function createLoader(room) {
  let current = null;   // { station, handle, root, kind }
  let seq = 0;

  function card(kind, station, title, body) {
    const root = document.createElement('div');
    root.className = 'br-card-veil';
    root.innerHTML = '<section class="br-card" role="dialog" aria-modal="true">'
      + '<div class="br-card-sheet" aria-hidden="true"></div>'
      + '<h2 class="br-card-title"></h2><p class="br-card-body"></p>'
      + '<button type="button" class="br-card-back"></button></section>';
    root.classList.add('is-' + kind);
    root.querySelector('.br-card-title').textContent = title;
    root.querySelector('.br-card-body').textContent = body;
    const back = root.querySelector('.br-card-back');
    back.textContent = room.lex('br_back', 'Back');
    back.addEventListener('click', () => room.standUp());
    root.addEventListener('pointerdown', (e) => { if (e.target === root) room.standUp(); });
    room.layer.appendChild(root);
    requestAnimationFrame(() => { try { back.focus(); } catch (e) { /* noop */ } });
    return root;
  }

  function buildCtx(station, root) {
    const s = room.state;
    return {
      root,
      bridge,
      request(op, body, idem) {
        const reqId = bridge.mintId();
        return bridge.request(
          { type: 'station-request', reqId, station: station.id, op: String(op), idem: idem || undefined, body: body || {} },
          'station-result', (m) => m.reqId === reqId, REQUEST_TIMEOUT_MS,
        ).then((res) => {
          if (res && res.body && Number.isFinite(res.body.sp)) s.sp = res.body.sp;   // true value; displays may lie
          return res;
        });
      },
      fx(fxId, symbols) {
        const token = bridge.mintId();
        const msg = { type: 'fx', token, fxId: String(fxId), station: station.id };
        if (Array.isArray(symbols)) msg.symbols = symbols.map(String);
        return bridge.request(msg, 'fx-ack', (m) => m.token === token, REQUEST_TIMEOUT_MS,
          { token, fired: [], skipped: [{ prim: String(fxId), why: 'unknown' }] });
      },
      media() {
        const reqId = bridge.mintId();
        return bridge.request({ type: 'media-request', reqId, station: station.id }, 'media',
          (m) => m.reqId === reqId, REQUEST_TIMEOUT_MS, { reqId, seed: 0, gifs: [], words: [], timeout: true });
      },
      sp: () => s.sp,
      onSp: (fn) => room.onSp(fn),
      get reduced() { return s.reduced; },
      get motion() { return s.motion; },
      get intensity() { return s.intensity; },
      lex: (key, fallback) => room.lex(key, fallback),
      standUp: () => room.standUp(),
    };
  }

  /** Take the screen for `station`. Resolves once it is interactive (or carded). */
  async function open(station) {
    if (current) await close();
    const my = ++seq;
    if (station.state !== 'live') {
      const root = card('soon', station, room.lex(station.labelKey, station.id),
        room.lex('br_soon_body', 'Under a dust sheet for now. This one opens soon.'));
      current = { station, handle: null, root, kind: 'soon' };
      return 'soon';
    }

    const root = document.createElement('div');
    root.className = 'br-station';
    root.dataset.station = station.id;
    room.layer.appendChild(root);
    current = { station, handle: null, root, kind: 'live' };
    try {
      const mod = await import('../' + station.entry);
      if (my !== seq) return 'superseded';
      if (typeof mod.mount !== 'function') throw new Error('no mount export');
      const handle = await mod.mount(buildCtx(station, root));
      if (my !== seq) { try { handle && handle.destroy && handle.destroy(); } catch (e) { /* noop */ } return 'superseded'; }
      current.handle = handle;
      bridge.send({ type: 'station-open', station: station.id });
      await (handle && typeof handle.open === 'function' ? handle.open() : null);
      return 'live';
    } catch (e) {
      if (my !== seq) return 'superseded';
      room.log('warn', 'station ' + station.id + ' failed: ' + ((e && e.message) || e));
      if (current && current.handle) {
        try { current.handle.destroy(); } catch (err) { /* noop */ }
        bridge.send({ type: 'station-close', station: station.id });
      }
      root.remove();
      current = { station, handle: null, root: card('failed', station, room.lex(station.labelKey, station.id),
        room.lex('br_station_failed', 'This station would not start. Try again in a moment.')), kind: 'failed' };
      return 'failed';
    }
  }

  /** Give the screen back. Settles within CLOSE_BUDGET_MS whatever the station does. */
  async function close(budgetMs) {
    const c = current;
    if (!c) return;
    current = null;
    seq++;
    if (c.handle) {
      await withTimeout(typeof c.handle.close === 'function' ? c.handle.close() : null, budgetMs || CLOSE_BUDGET_MS);
      try { if (typeof c.handle.destroy === 'function') c.handle.destroy(); } catch (e) { room.log('warn', 'destroy threw: ' + e); }
      bridge.send({ type: 'station-close', station: c.station.id });
    }
    try { c.root.remove(); } catch (e) { /* noop */ }
  }

  function suspend(on) {
    const h = current && current.handle;
    try { if (h && typeof h.suspend === 'function') h.suspend(!!on); } catch (e) { room.log('warn', 'suspend threw: ' + e); }
  }

  return { open, close, suspend, get current() { return current ? { id: current.station.id, kind: current.kind } : null; } };
}
