/* ============================================================================
 * cards.js - The Prize Parlour's cards (CONTRACT 10.17.F), no DOM.
 *
 *   readState / cardOf   what the server said, and which of the five faces a card shows
 *   classify             what a buy reply means for the page
 *   createCounter        the buying flow: open, confirm, one idem per confirm, refusals,
 *                        the SP chip on success, and nothing after close
 *
 * station.js paints `view()`; the node tests drive this file directly.
 * ==========================================================================*/

export const LEX = {
  br_counter_title: 'The Prize Parlour',
  br_counter_closed: 'The counter is closed for a moment.',
  br_counter_retry: 'The house did not answer. Confirm again to retry.',
  br_counter_buy: 'Buy',
  br_counter_confirm: 'Confirm',
  br_counter_cancel: 'Cancel',
  br_counter_after: 'Balance after: {0}',
  br_counter_owned: 'Owned',
  br_counter_soon: 'Soon',
  br_counter_short: 'Short by {0}',
  br_counter_link_discord: 'Link Discord first',
  br_counter_price: '{0} SP',
  br_counter_delivery_pending: 'The role is on its way to Discord.',
  br_counter_delivery_granted: 'The role is on your Discord account.',
  br_counter_delivery_not_in_guild: 'Join the Discord server and the role follows.',
  br_counter_delivery_failed: 'The role did not go through yet. It tries again on its own.',
  br_prize_rt_note: 'A first demo built on the original files. More themes and mods are coming, on request.',
  br_prize_jackpot_remix_name: 'Jackpot Remix',
  br_prize_jackpot_remix_blurb: 'The slot jackpot, played back your way.',
  br_prize_rt_demo_name: 'Racing Thoughts Demo',
  br_prize_rt_demo_blurb: 'Track 00, the original demo level.',
  br_prize_high_roller_name: 'High Roller',
  br_prize_high_roller_blurb: 'A Discord role that says you bought at the counter. Yours for good.',
  br_prize_flashes_v2_name: 'Flashes v2',
  br_prize_flashes_v2_blurb: 'Two new ways for flashes to move: drift and bounce, and pendulum.',
  br_prize_bubbles_v2_name: 'Bubbles v2',
  br_prize_bubbles_v2_blurb: 'Two new ways for bubbles to arrive: rain, and spiral in.',
  br_prize_rt_bundle_1_name: 'Racing Thoughts Bundle 1',
  br_prize_rt_bundle_1_blurb: 'Tracks 01, 02 and 03.',
  br_prize_rt_bundle_2_name: 'Racing Thoughts Bundle 2',
  br_prize_rt_bundle_2_blurb: 'Tracks 04, 05 and 06, with the demo track.',
  br_prize_rt_bundle_3_name: 'Racing Thoughts Bundle 3',
  br_prize_rt_bundle_3_blurb: 'Tracks 07 to 10, with the demo track.',
};

export const REPAINT = ['insufficient', 'owned', 'unavailable', 'discord_required'];
const int = (v, d = 0) => (Number.isFinite(Number(v)) ? Math.trunc(Number(v)) : d);
const obj = (v) => (v && typeof v === 'object' && !Array.isArray(v) ? v : null);

export const mintIdem = () => Array.from(crypto.getRandomValues(new Uint8Array(16)), (b) => b.toString(16).padStart(2, '0')).join('');
export const isRt = (id) => /^rt_/.test(String(id));

/** One catalog row as the page uses it. Unknown fields are dropped; keys fall back to the 10.17.A names. */
export function readRow(r) {
  const id = String(r.id);
  const owned = obj(r.owned);
  return { id, priceSp: Math.max(0, int(r.priceSp)), order: int(r.order, 99), sale: r.sale === 'on' ? 'on' : 'soon',
    nameKey: typeof r.nameKey === 'string' ? r.nameKey : `br_prize_${id}_name`,
    blurbKey: typeof r.blurbKey === 'string' ? r.blurbKey : `br_prize_${id}_blurb`,
    owned: owned ? { at: int(owned.at), paidSp: int(owned.paidSp) } : null, needs: r.needs === 'discord' ? 'discord' : null,
    noteKey: typeof r.noteKey === 'string' ? r.noteKey : isRt(id) ? 'br_prize_rt_note' : null };
}

export function readCatalog(list) {
  return (Array.isArray(list) ? list : []).filter((r) => obj(r) && typeof r.id === 'string' && r.id)
    .map(readRow).sort((a, b) => a.order - b.order);
}

/** GET state -> the page's state, or null when the body is not a usable state (a shut door answers open:false). */
export function readState(body) {
  if (!obj(body) || body.ok !== true || body.open === false || !Array.isArray(body.catalog)) return null;
  return { sp: int(body.sp), catalogVersion: int(body.catalogVersion, 1), discordLinked: body.discordLinked === true,
    catalog: readCatalog(body.catalog), delivery: obj(body.delivery) || {}, grants: grantsOf(body.prizes) };
}
export const grantsOf = (prizes) => (obj(prizes) && Array.isArray(prizes.grants) ? prizes.grants.map(String) : []);

/**
 * Which face a card shows. Owned beats everything, then Soon (dust sheet), then Link Discord first,
 * then Short by N (price over the balance), then Buy.
 */
export function cardOf(row, sp) {
  if (row.owned) return { face: 'owned' };
  if (row.sale !== 'on') return { face: 'soon' };
  if (row.needs === 'discord') return { face: 'discord' };
  const short = row.priceSp - int(sp);
  if (short > 0) return { face: 'short', short };
  return { face: 'buy' };
}

/** The delivery line key for an owned row with an outside delivery (today only high_roller), or null. */
export function deliveryKeyOf(row, delivery) {
  const d = row && row.owned && obj(delivery) && obj(delivery[row.id]);
  const s = d && String(d.status);
  return s && ['pending', 'granted', 'not_in_guild', 'failed'].includes(s) ? `br_counter_delivery_${s}` : null;
}

/** A host relay result ({ok, status, body} or a host refusal {ok:false, reason}) -> what the page does. */
export function classify(res) {
  const body = res && obj(res.body);
  if (res && res.ok && res.status === 403) return { kind: 'closed', reason: 'closed', body };
  if (res && res.ok && body && body.ok === true && body.open === false) return { kind: 'closed', reason: 'closed', body };   // state behind the door
  if (res && res.ok && body && body.ok === true) return { kind: 'ok', body };
  const reason = String((body && body.reason) || (res && res.reason) || 'offline');
  if (reason === 'closed') return { kind: 'closed', reason, body };
  if (REPAINT.includes(reason)) return { kind: 'repaint', reason, body };
  if (reason === 'catalog_changed') return { kind: 'catalog', reason, body };
  if (reason === 'busy' || reason === 'too_fast' || !body) return { kind: 'retry', reason, body };   // busy, too_fast, timeout, offline, a lost reply
  return { kind: 'refresh', reason, body };   // bad_input, idem_mismatch, anything new: read state again
}

/**
 * The buying flow. deps: { request(op, body, idem), sp(), spReadout?, chime(), onChange(), mint?, now? }.
 * Every reply is checked against the session it was sent in: after close() nothing is adopted and the chip
 * is never touched (a buy in flight settles on the server; the next open's state shows it).
 */
export function createCounter(deps) {
  const mint = deps.mint || mintIdem, now = deps.now || Date.now;
  let session = 0, st = null, phase = 'idle', confirm = null, flipped = null, log = [];
  const changed = () => { try { deps.onChange && deps.onChange(); } catch (e) { /* a paint never breaks a buy */ } };
  const note = (what, extra = {}) => { log = [...log.slice(-49), { what, ...extra }]; };
  const spNow = () => { const v = typeof deps.sp === 'function' ? Number(deps.sp()) : NaN; return Number.isFinite(v) ? v : st ? st.sp : 0; };
  const rowOf = (id) => (st ? st.catalog.find((r) => r.id === id) : null) || null;
  const fresh = (row, asked) => ({ prizeId: row.id, idem: mint(), priceSp: row.priceSp, catalogVersion: st.catalogVersion, pending: false, retry: false, asked });
  /** An open confirm against the current catalog: kept as is, asked again at a new price or version, or gone. */
  const askAgain = (c) => {
    const row = rowOf(c.prizeId);
    if (!row || cardOf(row, spNow()).face !== 'buy') return null;
    return row.priceSp === c.priceSp && st.catalogVersion === c.catalogVersion ? c : fresh(row, true);
  };
  const send = (op, body, idem) => Promise.resolve().then(() => deps.request(op, body, idem)).catch(() => ({ ok: false, reason: 'offline' }));

  async function load(my) {
    const c = classify(await send('state', {}));
    if (my !== session) return false;
    const next = c.kind === 'ok' ? readState(c.body) : null;
    if (!next) { if (!st || c.kind === 'closed') { phase = 'closed'; confirm = null; } note('state', { kind: c.kind }); changed(); return false; }
    st = next; phase = 'ready';
    if (confirm && !confirm.pending) confirm = askAgain(confirm);
    changed();
    return true;
  }

  function adoptSuccess(b, prizeId) {
    const row = rowOf(prizeId);
    if (st) {
      if (Number.isFinite(Number(b.sp))) st.sp = int(b.sp);
      if (row) row.owned = { at: now(), paidSp: int(b.paidSp, row.priceSp) };
      if (b.prizes) st.grants = grantsOf(b.prizes);
      if (obj(b.delivery)) st.delivery = { ...st.delivery, [prizeId]: b.delivery };
    }
    const r = deps.spReadout;
    if (r && typeof r.set === 'function') { r.set(int(b.sp)); r.set(null); if (typeof r.thud === 'function') r.thud(); }
    try { deps.chime && deps.chime(); } catch (e) { /* sound never breaks a buy */ }
  }

  return {
    get phase() { return phase; },
    get state() { return st; },
    get confirm() { return confirm && { ...confirm }; },
    get log() { return log; },
    sp: spNow,

    async open() { const my = ++session; phase = 'loading'; confirm = null; flipped = null; changed(); return load(my); },
    close() { session++; confirm = null; phase = 'idle'; },

    /** Cards in `order`, each with its face, delivery key and whether its confirm is open. */
    view() {
      const sp = spNow();
      return (st ? st.catalog : []).map((row) => ({ ...row, ...cardOf(row, sp),
        deliveryKey: deliveryKeyOf(row, st.delivery), flip: flipped === row.id,
        confirm: confirm && confirm.prizeId === row.id ? { ...confirm, after: sp - confirm.priceSp } : null }));
    },

    /** Buy: an inline confirm with a fresh idem. Only a Buy face opens one; a pending confirm is never replaced. */
    ask(prizeId) {
      const row = rowOf(prizeId);
      if (phase !== 'ready' || !row || cardOf(row, spNow()).face !== 'buy' || (confirm && confirm.pending)) return false;
      confirm = fresh(row, false);
      changed();
      return true;
    },
    cancel() { if (!confirm || confirm.pending) return false; confirm = null; changed(); return true; },

    /** Confirm: one request with the confirm's idem. busy and network errors keep it (and its idem) for the next press. */
    async buy() {
      if (!confirm || confirm.pending || phase !== 'ready') return null;
      const my = session, c0 = confirm;
      c0.pending = true; c0.retry = false; changed();
      note('buy', { prizeId: c0.prizeId, idem: c0.idem });
      const c = classify(await send('buy', { prizeId: c0.prizeId, catalogVersion: c0.catalogVersion }, c0.idem));
      if (my !== session || confirm !== c0) { note('late', { kind: c.kind }); return 'gone'; }
      c0.pending = false;
      note('reply', { kind: c.kind, reason: c.reason || null });
      if (c.kind === 'ok') {
        confirm = null; flipped = c0.prizeId;
        adoptSuccess(c.body, c0.prizeId);
        changed();
        load(my);   // live delivery and the server's own rows; the flip already shows
      } else if (c.kind === 'retry') {
        c0.retry = true; changed();
      } else if (c.kind === 'catalog') {
        const fresh = readCatalog(c.body && c.body.catalog);
        if (st && fresh.length) { st.catalog = fresh; st.catalogVersion = int(c.body.catalogVersion, st.catalogVersion); }
        confirm = askAgain({ ...c0, catalogVersion: -1 });   // a new confirm (and idem) at the new price; never buys on its own
        changed();
      } else if (c.kind === 'closed') {
        confirm = null; phase = 'closed'; changed();
      } else {
        const b = c.body || {};
        if (st && Number.isFinite(Number(b.sp))) st.sp = int(b.sp);
        if (st && b.prizes) st.grants = grantsOf(b.prizes);
        if (st && c.reason === 'owned') { const row = rowOf(c0.prizeId); if (row && !row.owned) row.owned = { at: now(), paidSp: 0 }; }
        confirm = null; changed();
        await load(my);
      }
      return c.kind;
    },
  };
}
