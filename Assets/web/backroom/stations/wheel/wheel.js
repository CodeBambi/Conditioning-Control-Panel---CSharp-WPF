/* wheel.js - the Daily Daze page's pure core: the slice layout from GET state, where the pointer is, the
 * landing on a server result, the deceleration plan, the day's countdown and the result reading. No DOM,
 * no three, no clock of its own, so node:test holds it to its word.
 *
 * Geometry follows blender-scripting wheel/preview/layout.js: angles are measured clockwise from the
 * pointer (top) as seen from the front, slice 0 is centred on the pointer at rotation 0, and the rotor's
 * local Z rotation r puts local angle r under the pointer.
 *
 * LAW I. The drawn widths are the picture (`width`, degrees); the printed odds are the ledger's. The page
 * never picks a slice: the server's result.sliceIndex does, and the landing only chooses WHERE inside that
 * slice the pointer rests, seeded by the day so a reopen shows the same landing (Law V). */

export const TAU = Math.PI * 2;
const MIN_SPAN = Math.PI / 180;   // 1 degree, the model's narrowest drawable slice

/** Server slices -> layout rows with start/end/mid/span. Null when the table cannot be drawn. */
export function layoutOf(slices) {
  if (!Array.isArray(slices) || slices.length < 3 || slices.length > 48) return null;
  const total = slices.reduce((s, x) => s + (Number(x && x.width) > 0 ? Number(x.width) : NaN), 0);
  if (!(total > 0) || !Number.isFinite(total)) return null;
  let a = -(Number(slices[0].width) / total) * TAU / 2;
  const out = [];
  for (let index = 0; index < slices.length; index++) {
    const s = slices[index], span = (Number(s.width) / total) * TAU;
    if (span < MIN_SPAN * 0.999 || typeof s.id !== 'string') return null;
    out.push({ id: s.id, index, label: String(s.label ?? s.id), pay: Number(s.pay) || 0, odds: String(s.odds ?? ''),
               kind: kindOf(s), width: Number(s.width), start: a, end: a + span, mid: a + span / 2, span });
    a += span;
  }
  return out;
}

/** The table carries no kind: the jackpot and the Snooze are known by id (server TABLE_V2). */
export function kindOf(s) {
  if (s && (s.kind === 'jackpot' || s.kind === 'malus' || s.kind === 'prize' || s.kind === 'decoration' || s.kind === 'double' || s.kind === 'nothing')) return s.kind;
  return s && s.id === 'jackpot' ? 'jackpot' : s && s.id === 'snooze' ? 'malus' : 'prize';
}

/** The slice under the pointer at rotor rotation `r`. */
export function sliceAt(layout, r) {
  const start = layout[0].start;
  const a = (((r - start) % TAU) + TAU) % TAU + start;
  return layout.find(s => a >= s.start && a < s.end) || layout[layout.length - 1];
}

/** FNV-1a 32-bit of a string, for seeded landings. */
export function hash32(str) {
  let h = 0x811c9dc5;
  for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 0x01000193); }
  return h >>> 0;
}

/** Where inside slice `index` the pointer rests for `day`: the middle 60% of the drawn slice, seeded. */
export function landingAngle(layout, index, day) {
  const s = layout[index];
  const u = hash32(`${day}|${s.id}|${index}`) / 4294967296;
  return s.mid + (u - 0.5) * s.span * 0.6;
}

/** The slice a result names: sliceIndex when it agrees with sliceId, else found by id. -1 when neither. */
export function resultIndex(layout, result) {
  if (!layout || !result) return -1;
  const i = Number(result.sliceIndex);
  if (Number.isInteger(i) && layout[i] && (result.sliceId == null || layout[i].id === result.sliceId)) return i;
  return layout.findIndex(s => s.id === result.sliceId);
}

/** A result body, read defensively (binding API: day, sliceId, sliceIndex, pay, snoozeCarryPaid, jackpot,
 *  jackpotFallback, snoozed, total, capped). `total` is pay + carry before the SP cap. */
export function readResult(r) {
  if (!r || typeof r !== 'object') return null;
  const n = v => (Number.isFinite(Number(v)) ? Math.max(0, Math.trunc(Number(v))) : 0);
  const pay = n(r.pay), carry = n(r.snoozeCarryPaid);
  return { day: String(r.day ?? ''), sliceId: r.sliceId == null ? null : String(r.sliceId), sliceIndex: r.sliceIndex,
           pay, ...(r.credited == null ? {} : { credited: n(r.credited) }), carryPaid: carry, total: r.total == null ? pay + carry : n(r.total), jackpotWon: r.jackpot === true,
           ...(r.reward ? {reward: r.reward} : {}), fallback: r.jackpotFallback === true, snoozed: r.snoozed === true, capped: r.capped === true };
}

/** The rotor rotation that rests `landing` under the pointer, reached from `from` turning `sign` (+1/-1)
 *  through at least `minTurns` whole turns. */
export function landingRotation(from, landing, sign, minTurns = 0) {
  const d = sign < 0 ? -1 : 1;
  const extra = (((d * (landing - from)) % TAU) + TAU) % TAU;
  return from + d * (TAU * Math.max(0, Math.floor(minTurns)) + extra);
}

/** Where a replayed (already landed) result rests: the same angle every reopen, in [0, TAU). */
export function restRotation(layout, result, day) {
  const i = resultIndex(layout, result);
  if (i < 0) return null;
  return ((landingAngle(layout, i, day) % TAU) + TAU) % TAU;
}

export const easeOutQuart = u => 1 - (1 - Math.min(1, Math.max(0, u))) ** 4;

/** A quartic ease-out from `from` that starts near `omega` (rad/ms, signed) and rests on `landing`.
 *  The distance is picked so the start speed matches the coast; the duration is clamped to [minMs, maxMs]. */
export function planLanding({ from, omega, landing, minMs = 3200, maxMs = 5600, targetMs = 4200 }) {
  const sign = omega < 0 ? -1 : 1, speed = Math.max(Math.abs(omega), 1e-4);
  const want = (speed * targetMs) / 4;                       // quartic: v0 = 4 * dist / ms
  const extra = (((sign * (landing - from)) % TAU) + TAU) % TAU;
  const turns = Math.max(1, Math.round((want - extra) / TAU));   // nearest: the start speed stays within a half turn
  const to = from + sign * (extra + turns * TAU);
  const ms = Math.min(maxMs, Math.max(minMs, (4 * Math.abs(to - from)) / speed));
  return { from, to, ms, sign };
}

export const rotationAt = (plan, elapsedMs) => plan.from + (plan.to - plan.from) * easeOutQuart(elapsedMs / plan.ms);

/** THE SETTLE (the landing's dead tail). A quartic ease-out spends the last stretch of its own clock moving
 *  less than the eye can read, and hypno.js's warped clock then stretches that stretch into a second and more
 *  of real time: the wheel LOOKED stopped on its slice while the plan was still officially running, so the
 *  landing beat (the thud, the party, the callout) was waiting on the clock instead of on the picture. A plan
 *  therefore ends when its remaining travel is under SETTLE_EPS_RAD, which is a fiftieth of the narrowest
 *  drawn slice and under 3 px at the rim, so the snap onto `to` is invisible and rides in under THE THUD's
 *  own flash on the same frame. Do not raise it back toward plan.ms: that IS the 1 to 2 second dead wait. */
export const SETTLE_EPS_RAD = (0.5 * Math.PI) / 180;

/** The plan-clock time (ms) at which `plan` is within `eps` radians of its landing: where the landing beat
 *  fires. easeOutQuart leaves (1 - u)^4 of the travel, so this solves dist * (1 - u)^4 = eps for u. A plan
 *  shorter than `eps` (a wind-down nudge) has no dead tail to trim and keeps its whole duration. */
export function settleMs(plan, eps = SETTLE_EPS_RAD) {
  const ms = Number(plan && plan.ms), dist = Math.abs(Number(plan && plan.to) - Number(plan && plan.from));
  if (!(ms > 0)) return 0;
  if (!(dist > eps) || !(eps > 0)) return ms;
  return ms * (1 - (eps / dist) ** 0.25);
}

/** Time to `nextResetAt` (ISO UTC) at client time `nowMs`. Display only: the server owns the day. */
export function countdown(nextResetAt, nowMs) {
  const at = Date.parse(nextResetAt);
  if (!Number.isFinite(at) || !Number.isFinite(nowMs)) return null;
  const ms = Math.max(0, at - nowMs), s = Math.ceil(ms / 1000);
  const pad = x => String(x).padStart(2, '0');
  return { ms, due: ms <= 0, text: `${pad(Math.floor(s / 3600))}:${pad(Math.floor((s % 3600) / 60))}:${pad(s % 60)}` };
}

/** Law I: the SP readout never shows more than the server holds. `owed` is pay the page has not landed yet. */
export function shownSp(serverSp, owed = 0, flying = null) {
  const server = Math.max(0, Math.trunc(Number(serverSp) || 0));
  const floor = Math.max(0, server - Math.max(0, Math.trunc(Number(owed) || 0)));
  return flying == null || !Number.isFinite(Number(flying)) ? floor : Math.min(server, Math.max(0, Math.trunc(Number(flying))));
}

/** Daily allowance and earned credits are independent. Legacy servers have no credits. */
export function bonusSpinsOf(state) {
  const n = Number(state?.bonusSpins);
  return Number.isSafeInteger(n) && n > 0 ? n : 0;
}
export function canSpinWheel(state) { return !!state && (!state.spun || bonusSpinsOf(state) > 0); }
