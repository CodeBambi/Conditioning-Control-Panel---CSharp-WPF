/* mock-server.js - a stand-in for /v2/backroom/counter/* for dev.html and the node tests. NOT the server
 * (that is CCP-Server proxy/backroom-counter.js): it reproduces the body shapes and the refusal order of
 * CONTRACT 10.17.C so the page can be driven through every case.
 *   GET state -> { ok, open, sp, catalogVersion, discordLinked, prizes:{revision, grants}, catalog:[row], delivery }
 *   POST buy  -> { ok, idem, prizeId, paidSp, spBefore, sp, catalogVersion, prizes, delivery }
 *   refusals  -> bad_input, idem_mismatch, catalog_changed (+ catalog, catalogVersion), unavailable, owned (+ prizes),
 *                discord_required, insufficient (+ sp), busy, too_fast: HTTP 200; closed: HTTP 403 (state behind the door: 200 open:false).
 * handle() resolves like the host relay: {ok:true, status, body} or a host refusal {ok:false, reason}.
 * `on` is BACKROOM_COUNTER_ON (a comma list, an array or '*'); `roleId: false` is a missing DISCORD_HIGH_ROLLER_ROLE_ID. */

export const CATALOG_V1 = [
  ['jackpot_remix', 15, ['fx.jackpot_remix']],
  ['rt_demo', 20, ['rt.original.00']],
  ['high_roller', 40, ['discord.high_roller']],
  ['flashes_v2', 30, ['fx.flash.drift_bounce', 'fx.flash.pendulum']],
  ['bubbles_v2', 30, ['fx.bubble.rain', 'fx.bubble.spiral_in']],
  ['rt_bundle_1', 1200, ['rt.original.01', 'rt.original.02', 'rt.original.03']],
  ['rt_bundle_2', 3600, ['rt.original.00', 'rt.original.04', 'rt.original.05', 'rt.original.06']],
  ['rt_bundle_3', 9000, ['rt.original.00', 'rt.original.07', 'rt.original.08', 'rt.original.09', 'rt.original.10']],
].map(([id, priceSp, grants], i) => ({ id, priceSp, grants, order: i + 1 }));

const IDEM = /^[A-Za-z0-9_-]{16,64}$/;
const clone = (v) => structuredClone(v);

export function createMockServer({ sp = 812, on = '', open = true, discordId = 'd_123', roleId = true, owned = {},
                                   delivery = 'granted', now = () => Date.now() } = {}) {
  let catalog = clone(CATALOG_V1), catalogVersion = 1, onList = on;
  const user = { sp, discordId, counter: { netSp: 0, revision: 0, owned: clone(owned), delivery: {} } };
  const receipts = new Map(), faults = [], log = [];

  const saleOf = (id) => {
    const list = Array.isArray(onList) ? onList : String(onList || '').split(',').map((s) => s.trim()).filter(Boolean);
    if (!(list.includes('*') || list.includes(id))) return 'soon';
    return id === 'high_roller' && !roleId ? 'soon' : 'on';
  };
  const grants = () => [...new Set(catalog.filter((r) => user.counter.owned[r.id]).flatMap((r) => r.grants))].sort();
  const prizes = () => ({ revision: user.counter.revision, grants: grants() });
  const rowJson = (r) => {
    const o = user.counter.owned[r.id];
    const row = { id: r.id, priceSp: r.priceSp, grants: [...r.grants], order: r.order, nameKey: `br_prize_${r.id}_name`,
      blurbKey: `br_prize_${r.id}_blurb`, sale: saleOf(r.id), owned: o ? { at: o.at, paidSp: o.paidSp } : null };
    if (r.id === 'high_roller' && !user.discordId) row.needs = 'discord';
    if (/^rt_/.test(r.id)) row.noteKey = 'br_prize_rt_note';
    return row;
  };
  const deliveryJson = () => {
    const d = user.counter.delivery.high_roller;
    return d ? { high_roller: { status: d.status, tries: d.tries, at: d.at } } : {};
  };

  function state() {
    return { ok: true, open: true, sp: user.sp, catalogVersion, discordLinked: !!user.discordId, prizes: prizes(),
      catalog: catalog.map(rowJson), delivery: deliveryJson() };
  }

  function buy(body, idem) {
    const b = body || {};
    idem = idem || b.idem;
    if (typeof idem !== 'string' || !IDEM.test(idem) || typeof b.prizeId !== 'string' || !Number.isInteger(b.catalogVersion)
        || !catalog.some((r) => r.id === b.prizeId)) return { ok: false, reason: 'bad_input' };
    const seen = receipts.get(idem);
    if (seen) return seen.prizeId === b.prizeId ? clone(seen.body) : { ok: false, reason: 'idem_mismatch' };
    if (b.catalogVersion !== catalogVersion) return { ok: false, reason: 'catalog_changed', catalogVersion, catalog: catalog.map(rowJson) };
    const row = catalog.find((r) => r.id === b.prizeId);
    if (saleOf(row.id) !== 'on') return { ok: false, reason: 'unavailable' };
    if (user.counter.owned[row.id]) return { ok: false, reason: 'owned', prizes: prizes() };
    if (row.id === 'high_roller' && !user.discordId) return { ok: false, reason: 'discord_required' };
    if (user.sp < row.priceSp) return { ok: false, reason: 'insufficient', sp: user.sp };
    const spBefore = user.sp, at = now();
    user.sp -= row.priceSp;
    user.counter.owned[row.id] = { at, paidSp: row.priceSp, catalogVersion };
    user.counter.netSp -= row.priceSp;
    user.counter.revision += 1;
    let d = null;
    if (row.id === 'high_roller') {   // settle, then one bounded grant attempt (10.17.D)
      user.counter.delivery.high_roller = { status: delivery, discordId: user.discordId, tries: 1, at };
      d = { status: delivery, tries: 1, at };
    }
    const out = { ok: true, idem, prizeId: row.id, paidSp: row.priceSp, spBefore, sp: user.sp, catalogVersion, prizes: prizes(), delivery: d };
    receipts.set(idem, { prizeId: row.id, body: clone(out) });
    return out;
  }

  async function handle(op, body = {}, idem) {
    log.push({ op, idem, body: clone(body) });
    const f = faults.find((x) => x.op === op && x.times > 0);
    if (f) {
      f.times--;
      if (f.apply) await handle.raw(op, body, idem);
      if (f.reason === 'closed') return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
      if (f.reason === 'busy' || f.reason === 'too_fast') return { ok: true, status: 200, body: { ok: false, reason: f.reason } };
      return { ok: false, status: 0, reason: f.reason };   // host refusals: timeout, offline
    }
    return handle.raw(op, body, idem);
  }
  handle.raw = async (op, body, idem) => {
    if (!open && op === 'state') return { ok: true, status: 200, body: { ok: true, open: false } };   // the server's read behind the door
    if (!open) return { ok: true, status: 403, body: { ok: false, reason: 'closed' } };
    if (op === 'state') return { ok: true, status: 200, body: state() };
    if (op === 'buy') return { ok: true, status: 200, body: buy(body, idem) };
    return { ok: false, status: 0, reason: 'bad_op' };
  };

  return {
    handle, log, user,
    get catalogVersion() { return catalogVersion; },
    /** Next `times` calls to `op` fail with `reason`; apply:true runs the op first (a lost reply). */
    fail(op, reason, times = 1, { apply = false } = {}) { faults.push({ op, reason, times, apply }); },
    setOn(v) { onList = v; },
    setOpen(v) { open = !!v; },
    setSp(v) { user.sp = v; },
    linkDiscord(id) { user.discordId = id || null; },
    setDelivery(status) { const d = user.counter.delivery.high_roller; if (d) { d.status = status; d.tries += 1; d.at = now(); } },
    /** A price change: bumps catalogVersion (10.17.A). */
    reprice(id, priceSp) { catalog = catalog.map((r) => (r.id === id ? { ...r, priceSp } : r)); catalogVersion += 1; },
  };
}
