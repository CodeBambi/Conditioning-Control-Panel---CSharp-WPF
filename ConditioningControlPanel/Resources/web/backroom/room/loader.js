/* ============================================================================
 * backroom/room/loader.js - how a station takes the screen and gives it back
 * (CONTRACT section 7). The room calls mount(ctx) and the four methods it
 * returns, and nothing else.
 *
 *   soon   -> a dust-sheet card. No code is loaded, no model is asked for.
 *   variant-> one station, several fixtures (the three slot colours): the row's
 *             variant reaches the station as ctx.variant, null for the rest.
 *   live   -> import(entry), mount(ctx), open(). A module that fails to load or
 *             to mount gets a plain card instead, and the room stays usable.
 *
 * LAW VI: Back is live before open() resolves and while close() runs. close()
 * gets CLOSE_BUDGET_MS; a station that has not settled by then is destroyed
 * anyway, because nobody waits on a station to leave a room.
 *
 * Hypno v3 (CONTRACT 10.13): ctx.gates, ctx.onSettings, fx args with the token
 * on the promise, fx-release, fx-tunnel and a media count. A settings
 * subscription a station forgets is dropped when it closes, and one asked for
 * after that (an open() that resolves after Back) is never made.
 * ==========================================================================*/

import * as bridge from '../bridge.js';

export const CLOSE_BUDGET_MS = 420;
export const REQUEST_TIMEOUT_MS = 6000;
export const MEDIA_COUNT_MAX = 13;

const withTimeout = (p, ms) => Promise.race([Promise.resolve(p).catch(() => {}), new Promise((r) => setTimeout(r, ms))]);

/**
 * @param {Object} room  { layer, state, lex(key, fallback), onSp(fn), onSettings(fn), spReadout, spChanged(),
 *                        chipSettle(), standUp(), log(level,msg) }
 */
export function createLoader(room) {
  let current = null;   // { station, handle, root, kind }
  let seq = 0;

  /** Drop a station's settings subscriptions and mark it closed, so a late onSettings subscribes nothing. */
  function dropSubs(subs) {
    if (!subs) return;
    subs.closed = true;
    for (const stop of Array.from(subs)) stop();
  }

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

  function buildCtx(station, root, variant, subs, stage) {
    const s = room.state;
    return {
      root,
      stage,
      bridge,
      request(op, body, idem) {
        const reqId = bridge.mintId();
        return bridge.request(
          { type: 'station-request', reqId, station: station.id, op: String(op), idem: idem || undefined, body: body || {} },
          'station-result', (m) => m.reqId === reqId, REQUEST_TIMEOUT_MS,
        ).then((res) => {
          if (res && res.body && Number.isFinite(res.body.sp)) {
            s.sp = res.body.sp;   // true value; displays may lie
            if (room.spChanged) room.spChanged();   // the chip repaints next frame, once the station has adopted the reply
          }
          return res;
        });
      },
      /** Fire an fx id. The ack promise carries its token at once (`p.token`) for fx-release. */
      fx(fxId, symbols, args) {
        const token = bridge.mintId();
        const msg = { type: 'fx', token, fxId: String(fxId), station: station.id };
        if (Array.isArray(symbols)) msg.symbols = symbols.map(String);
        if (args && typeof args === 'object' && !Array.isArray(args)) msg.args = args;
        const p = bridge.request(msg, 'fx-ack', (m) => m.token === token, REQUEST_TIMEOUT_MS,
          { token, fired: [], skipped: [{ prim: String(fxId), why: 'unknown' }] });
        p.token = token;
        return p;
      },
      /** Fade out what that fx token still holds on screen. No reply. */
      fxRelease(token) {
        if (typeof token !== 'string' || !token) return;
        bridge.send({ type: 'fx-release', token, station: station.id });
      },
      /** Tunnel vision level 0..1. No reply; the kit throttles it to 10 a second. */
      fxTunnel(level) {
        const n = Number(level);
        if (!Number.isFinite(n)) return;
        bridge.send({ type: 'fx-tunnel', station: station.id, level: Math.round(Math.min(1, Math.max(0, n)) * 1000) / 1000 });
      },
      /** Deal media for this sit-down. `count` 1..13 (the host's default is 4). */
      media(opts) {
        const reqId = bridge.mintId();
        const msg = { type: 'media-request', reqId, station: station.id };
        const count = opts && opts.count;
        if (Number.isInteger(count) && count >= 1 && count <= MEDIA_COUNT_MAX) msg.count = count;
        return bridge.request(msg, 'media',
          (m) => m.reqId === reqId, REQUEST_TIMEOUT_MS, { reqId, seed: 0, gifs: [], words: [], timeout: true });
      },
      sp: () => s.sp,
      onSp: (fn) => room.onSp(fn),
      /** The room's SP chip (CONTRACT 7.1): { set(value|null), owe(n | () => n), thud(), target() }. */
      spReadout: room.spReadout,
      rewardLanded: body => { if(!subs.closed && current?.subs === subs && station.id === 'wheel') room.rewardLanded?.(body); },
      revealedWin: (amount,tier,text) => { if(!subs.closed) room.revealedWin?.(station.key,amount,tier,text); },
      get reduced() { return s.reduced; },
      get motion() { return s.userStill ? 'off' : s.motion; },
      get intensity() { return s.intensity; },
      /** The host's hypno toggles, frozen { flash, subliminal, spiral, brainDrain, tunnel }, live on every read (10.13.A). */
      get gates() { return s.gates; },
      /** Subscribe to { motion, intensity, reduced, gates } on every settings frame. Returns an unsubscribe. */
      onSettings(fn) {
        if (typeof fn !== 'function' || typeof room.onSettings !== 'function' || subs.closed) return () => {};
        const off = room.onSettings(fn);
        const stop = () => { subs.delete(stop); try { off(); } catch (e) { /* noop */ } };
        subs.add(stop);
        return stop;
      },
      lex: (key, fallback) => room.lex(key, fallback),
      standUp: () => room.standUp(),
      /** { id, name, palette:{materialName: 'rrggbb'} | null } or null. Optional for a station to honour. */
      variant: variant || null,
      /** The room draws the only Back (CONTRACT 7): a station hides its own. */
      hostBack: true,
    };
  }

  /** Take the screen for `station`. Resolves once it is interactive (or carded). */
  async function open(station, extra) {
    if (current) await close();
    const my = ++seq;
    if (station.state !== 'live') {
      const root = card('soon', station, room.lex(station.labelKey, station.name || station.id),
        room.lex('br_soon_body', 'Under a dust sheet for now. This one opens soon.'));
      current = { station, handle: null, root, kind: 'soon' };
      return 'soon';
    }

    const root = document.createElement('div');
    root.className = 'br-station br-seat'; // The room stays visible while code and camera arrive.
    root.dataset.station = station.id;
    room.layer.appendChild(root);
    const subs = new Set();   // .closed once dropSubs has run
    current = { station, handle: null, root, kind: 'live', subs };
    try {
      const mod = await import('../' + station.entry);
      if (my !== seq) return 'superseded';
      if (typeof mod.mount !== 'function') throw new Error('no mount export');
      const stage = mod.roomStage === true ? room.stage?.(station) : null;
      if (stage) {
        subs.add(() => stage.dispose()); root.classList.add('br-seat');
        const arrived=await stage.arrived;
        if(my!==seq||arrived===false){dropSubs(subs);return 'superseded';}
      }
      if (!stage && room.approach) {
        const trip=room.approach(station);
        if(trip){subs.add(()=>trip.dispose());const arrived=await trip.arrived;if(my!==seq||arrived===false){dropSubs(subs);return 'superseded';}}
      }
      if(!stage)root.classList.remove('br-seat');
      // `export const roomBehind = true` (the slot): the root stays see-through, so the pan-in is the load screen
      // and the station's own canvas comes up over the room's held frame, with no ground colour and no card between.
      if(!stage&&mod.roomBehind===true)root.classList.add('br-through');
      const handle = await mod.mount(buildCtx(station, root, extra && extra.variant, subs, stage));
      if (my !== seq) { dropSubs(subs); try { handle && handle.destroy && handle.destroy(); } catch (e) { /* noop */ } return 'superseded'; }
      current.handle = handle;
      bridge.send({ type: 'station-open', station: station.id });
      await (handle && typeof handle.open === 'function' ? handle.open() : null);
      return 'live';
    } catch (e) {
      if (my !== seq) { dropSubs(subs); return 'superseded'; }
      room.log('warn', 'station ' + station.id + ' failed: ' + ((e && e.message) || e));
      dropSubs(subs);
      if (current && current.handle) {
        try { current.handle.destroy(); } catch (err) { /* noop */ }
        bridge.send({ type: 'station-close', station: station.id });
      }
      root.remove();
      current = { station, handle: null, root: card('failed', station, room.lex(station.labelKey, station.name || station.id),
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
      await withTimeout(Promise.resolve().then(() => typeof c.handle.close === 'function' ? c.handle.close() : null), budgetMs || CLOSE_BUDGET_MS);
      try { if (typeof c.handle.destroy === 'function') c.handle.destroy(); } catch (e) { room.log('warn', 'destroy threw: ' + e); }
      bridge.send({ type: 'station-close', station: c.station.id });
    }
    dropSubs(c.subs);
    if (room.chipSettle) room.chipSettle();
    try { c.root.remove(); } catch (e) { /* noop */ }
  }

  function suspend(on) {
    const h = current && current.handle;
    try { if (h && typeof h.suspend === 'function') h.suspend(!!on); } catch (e) { room.log('warn', 'suspend threw: ' + e); }
  }

  /** `current.debug()` is a test seam (smoke/): the station's own debug(), never read by the room. */
  return { open, close, suspend, get current() { return current ? { id: current.station.id, kind: current.kind, debug: () => current?.handle?.debug?.() } : null; } };
}
