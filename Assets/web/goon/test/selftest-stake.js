// PvP stakes on the Goon page (ui/stake.js, owner 2026-09-28).
//
//   node Resources/web/goon/test/selftest-stake.js
//
// What is asserted:
//   1. Eligibility: practice, a standalone page, an anonymous guest seat and a
//      missing room code never get a stake row.
//   2. Pills: off first, time only when the host says time is allowed, the
//      server's options win over the fallback table.
//   3. Labels, the opponent chip and refusals read as words, never codes.
//   4. The recap line: won / lost time / lost SP / void / settling / no stake.
//   5. The ledger claim carries exactly what the server settles on (end_reason,
//      winner_is_host, both scores) and never survived_ms; practice, a pre-Live
//      cancel and a guest seat claim nothing.
//   6. The client: offer, lock once, settle watch only when staked, a busy retry, the
//      settle give-up, and a disabled/sign-in reply hides the row.

import {
  normStake, sameStake, isStaked, stakeEligible, stakeOptions, stakeLabel, themLine,
  refusalLine, resultLine, buildClaimBody, submitClaim, createStakeClient,
  STAKE_FALLBACK, SETTLE_GIVE_UP_MS,
} from '../ui/stake.js';
import { S } from '../ui/strings.js';

let n = 0;
let failures = 0;
function ok(cond, what, detail) {
  n++;
  if (!cond) { failures++; console.error('FAIL: ' + what + (detail !== undefined ? ' (' + detail + ')' : '')); }
}

const ID = { unifiedId: 'u_abc123', displayName: 'me' };

// ------------------------------------------------ 1. eligibility
{
  const base = { hosted: true, practice: false, identity: ID, code: 'ABC123' };
  ok(stakeEligible(base) === true, 'hosted, signed in, real room: eligible');
  ok(stakeEligible({ ...base, practice: true }) === false, 'practice never stakes');
  ok(stakeEligible({ ...base, hosted: false }) === false, 'standalone page never stakes');
  ok(stakeEligible({ ...base, identity: { unifiedId: 'g_x', anonymous: true } }) === false, 'guest seat never stakes');
  ok(stakeEligible({ ...base, identity: { unifiedId: '' } }) === false, 'no account never stakes');
  ok(stakeEligible({ ...base, code: '' }) === false, 'no room code, no stake');
  ok(stakeEligible({ ...base, code: 'ab c' }) === false, 'a junk code is not a room');
}

// ------------------------------------------------ 2. pills
{
  const fb = stakeOptions(null);
  ok(fb[0].kind === 'none', 'off is the first pill');
  ok(fb.every((o) => o.kind !== 'time'), 'no time pills without time_ok');
  ok(fb.filter((o) => o.kind === 'sp').map((o) => o.amount).join(',') === STAKE_FALLBACK.sp.join(','), 'fallback SP pills');
  const withTime = stakeOptions({ ok: true, time_ok: true, options: { time: [900, 1800], sp: [5, 10, 25] } });
  ok(withTime.map((o) => o.kind + o.amount).join(',') === 'none0,time900,time1800,sp5,sp10,sp25', 'full row in order',
    withTime.map((o) => o.kind + o.amount).join(','));
  const server = stakeOptions({ ok: true, time_ok: false, options: { sp: [7, 'x', -1] } });
  ok(server.map((o) => o.kind + o.amount).join(',') === 'none0,sp7', "server's options win, junk dropped",
    server.map((o) => o.kind + o.amount).join(','));
}

// ------------------------------------------------ 3. words
{
  ok(normStake({ kind: 'none', amount: 99 }).amount === 0, 'none reads amount 0');
  ok(normStake({ kind: 'sp', amount: 0 }) === null, 'sp 0 is not a stake');
  ok(normStake({ kind: 'bogus', amount: 5 }) === null, 'unknown kind is not a stake');
  ok(sameStake(null, { kind: 'none', amount: 0 }), 'null equals off');
  ok(!isStaked({ kind: 'none', amount: 0 }) && isStaked({ kind: 'time', amount: 900 }), 'isStaked');
  ok(stakeLabel({ kind: 'time', amount: 1800 }) === '30 min', 'time label', stakeLabel({ kind: 'time', amount: 1800 }));
  ok(stakeLabel({ kind: 'sp', amount: 10 }) === '10 ✦', 'sp label', stakeLabel({ kind: 'sp', amount: 10 }));
  ok(stakeLabel(null) === S.stake.off, 'off label');
  ok(themLine(null, false) === '', 'them unknown: no chip');
  ok(themLine({ kind: 'none', amount: 0 }, true) === S.stake.themNone, 'them staked nothing');
  ok(themLine({ kind: 'sp', amount: 25 }, true) === 'them: 25 ✦', 'them chip', themLine({ kind: 'sp', amount: 25 }, true));
  for (const r of ['daily_cap', 'pair_cap', 'insufficient_sp', 'started', 'bad_amount', 'busy', 'disabled', undefined]) {
    const line = refusalLine(r);
    ok(typeof line === 'string' && line && !/_/.test(line), 'refusal ' + r + ' reads as words', line);
  }
  for (const [k, v] of Object.entries({
    label: S.stake.label, down: S.stake.down, returned: S.stake.returned, timeNote: S.stake.timeNote,
  })) {
    ok(!/—|!/.test(v), 'no em-dash or exclamation in ' + k, v);
  }
}

// ------------------------------------------------ 4. recap line
{
  const sp10 = { kind: 'sp', amount: 10 };
  const t30 = { kind: 'time', amount: 1800 };
  ok(resultLine(null, { kind: 'none', amount: 0 }) === null, 'no stake, no line');
  ok(resultLine(null, sp10).tone === 'pending', 'unsettled reads settling');
  ok(resultLine({ result: 'won', sp_delta: 20 }, sp10).text === '+20 ✦', 'won SP line');
  ok(resultLine({ result: 'won', sp_delta: 0 }, sp10).text === S.stake.returned, 'won past the prize cap: returned');
  ok(resultLine({ result: 'lost', sp_delta: 0, time_s: 1800 }, t30).text === '+30 min on your lock', 'lost time line');
  ok(resultLine({ result: 'lost', sp_delta: -10 }, sp10).text === '-10 ✦', 'lost SP from the delta');
  ok(resultLine({ result: 'lost', sp_delta: 0 }, sp10).text === '-10 ✦', 'lost SP (ante already taken)');
  ok(resultLine({ result: 'void', sp_delta: 10 }, sp10).text === S.stake.returned, 'void: returned');
  ok(resultLine({ result: 'void', local: true }, t30).tone === 'void', 'local give-up: returned');
}

// ------------------------------------------------ 4b. what the tab actually booked
// A lost time stake the player's own Circe's tab refused (safety hold after panic, day
// limit, pvp_loss row off, tab paused) or only part-booked still read "+30 min on your
// lock": the recap took the server's time_s and dropped the host's booked_s. Chess got
// it right (bug hunt 2026-09-29, STAKES-2).
{
  const t30 = { kind: 'time', amount: 1800 };
  const frame = (booked) => ({
    type: 'stake', op: 'settled', game: 'goon', ok: true, match: 'ABC123',
    you: t30, them: { kind: 'sp', amount: 10 }, locked: true,
    settled: { result: 'lost', sp_delta: 0, time_s: 1800, sp: 100 }, booked_s: booked,
  });
  const recap = (booked) => {
    const c = createStakeClient({ send: () => {} });
    c.receive({ type: 'stake', op: 'offer', ok: true, match: 'ABC123', stake: t30, sp: 100 });
    c.finish('ABC123');
    c.receive(frame(booked));
    return c;
  };
  const line = (c) => resultLine(c.settledFor('ABC123'), c.get('ABC123').you).text;
  ok(line(recap(1800)) === '+30 min on your lock', 'the tab took all of it', line(recap(1800)));
  ok(line(recap(600)) === '+10 min on your lock', 'the tab took part: the recap says what landed', line(recap(600)));
  ok(line(recap(0)) === S.stake.lost, 'the tab took nothing: no minutes are claimed', line(recap(0)));
  const later = recap(600);
  later.receive({ type: 'stake', op: 'state', game: 'goon', ok: true, match: 'ABC123', you: t30, them: null, locked: true, settled: { result: 'lost', sp_delta: 0, time_s: 1800, sp: 100 } });
  ok(line(later) === '+10 min on your lock', 'a later frame without booked_s keeps what the host booked', line(later));
  const c = createStakeClient({ send: () => {} });
  c.receive({ type: 'stake', op: 'offer', ok: true, match: 'NOBOOK', stake: t30, sp: 100 });
  c.finish('NOBOOK');
  c.receive({ ...frame(undefined), match: 'NOBOOK' });
  ok(resultLine(c.settledFor('NOBOOK'), c.get('NOBOOK').you).text === '+30 min on your lock', 'a host that says nothing about booking: the server figure');
}

// ------------------------------------------------ 5. the claim
{
  const session = { identity: ID, room: { code: 'ABC123', token: 'tok', role: 'guest' } };
  const result = { endReason: 1, winnerIsHost: true, hostScore: 120.7, guestScore: 80, survivedMs: 91234, countsForLedger: true };
  const body = buildClaimBody({ session, result });
  ok(body && body.code === 'ABC123' && body.role === 'guest' && body.token === 'tok' && body.unified_id === ID.unifiedId, 'claim addresses the room');
  ok(body && Object.keys(body.result).sort().join(',') === 'end_reason,guest_score,host_score,winner_is_host',
    'claim carries exactly the settled fields', body && Object.keys(body.result).join(','));
  ok(body && body.result.end_reason === 1 && body.result.winner_is_host === true && body.result.host_score === 120, 'claim values');
  ok(buildClaimBody({ session, result: { ...result, winnerIsHost: null, endReason: 3 } }).result.winner_is_host === null, 'draw claims null');
  ok(buildClaimBody({ session, result: { ...result, countsForLedger: false } }) === null, 'pre-Live cancel claims nothing');
  ok(buildClaimBody({ session: { ...session, identity: { unifiedId: 'g_x', anonymous: true } }, result }) === null, 'guest seat claims nothing');
  ok(buildClaimBody({ session: { identity: ID, room: {} }, result }) === null, 'practice (no room) claims nothing');
  let posted = null;
  const r = await submitClaim({ session, result, post: async (p, b) => { posted = { p, b }; return { status: 200, body: '{}' }; } });
  ok(r.ok && posted && posted.p === '/v2/goon/ledger', 'claim posts to the ledger');
  const bad = await submitClaim({ session, result, post: async () => { throw new Error('x'); } });
  ok(bad.ok === false, 'a throwing post resolves not ok');
}

// ------------------------------------------------ 6. the client
{
  const sent = [];
  let clock = 1000;
  const c = createStakeClient({ send: (m) => sent.push(m), now: () => clock });
  let changes = 0;
  c.onChange(() => changes++);
  ok(!c.enabled(), 'disabled before limits');
  c.askLimits(); c.askLimits();
  ok(sent.filter((m) => m.type === 'stake-limits').length === 1, 'limits asked once');
  c.receive({ type: 'stake', op: 'limits', ok: true, enabled: true, time_ok: true, options: { time: [900, 1800], sp: [5, 10, 25] } });
  ok(c.enabled(), 'enabled after limits');

  ok(c.offer('ABC123', { kind: 'sp', amount: 10 }), 'offer sent');
  ok(!c.offer('ABC123', { kind: 'sp', amount: 5 }), 'no second offer while one is in flight');
  const o = sent.filter((m) => m.type === 'stake-offer');
  ok(o.length === 1 && o[0].match === 'ABC123' && o[0].kind === 'sp' && o[0].amount === 10, 'offer frame shape');
  c.receive({ type: 'stake', op: 'offer', ok: true, match: 'ABC123', stake: { kind: 'sp', amount: 10 }, sp: 90 });
  ok(sameStake(c.get('ABC123').you, { kind: 'sp', amount: 10 }) && !c.get('ABC123').pending, 'offer adopted');

  c.offer('ABC123', { kind: 'sp', amount: 25 });
  c.receive({ type: 'stake', op: 'offer', ok: false, match: 'ABC123', reason: 'insufficient_sp' });
  ok(c.get('ABC123').error === S.stake.noSp && sameStake(c.get('ABC123').you, { kind: 'sp', amount: 10 }), 'refusal keeps the old stake, says why');

  c.receive({ type: 'stake', op: 'state', ok: true, match: 'ABC123', you: { kind: 'sp', amount: 10 }, them: { kind: 'time', amount: 900 }, locked: false, settled: null });
  ok(c.get('ABC123').known && c.get('ABC123').them.kind === 'time', 'state shows their stake');

  c.lock('ABC123'); c.lock('ABC123');
  const locks = sent.filter((m) => m.type === 'stake-state' && m.lock === true);
  ok(locks.length === 1 && locks[0].match === 'ABC123', 'lock sent once to the server');
  ok(!c.offer('ABC123', { kind: 'none', amount: 0 }), 'no offer after the lock');

  c.finish('ABC123'); c.finish('ABC123');
  const finals = sent.filter((m) => m.type === 'stake-settle' && m.match === 'ABC123');
  ok(finals.length === 1, 'staked match asks the host to watch the settle, once');
  ok(c.settledFor('ABC123') === null, 'not settled yet');
  clock += SETTLE_GIVE_UP_MS + 1;
  ok(c.settledFor('ABC123') && c.settledFor('ABC123').result === 'void', 'page gave up: reads returned');
  c.receive({ type: 'stake', op: 'settled', game: 'goon', ok: true, match: 'ABC123', you: { kind: 'sp', amount: 10 }, them: null, locked: true, settled: { result: 'won', sp_delta: 20, time_s: 0, sp: 110 }, booked_s: 0 });
  ok(c.settledFor('ABC123').result === 'won', 'the host\'s settled frame wins over the give-up');
  ok(resultLine(c.settledFor('ABC123'), c.get('ABC123').you).text === '+20 ✦', 'settled frame reads as the recap line');

  // The host's own give-up.
  c.offer('GIVEUP', { kind: 'time', amount: 900 });
  c.receive({ type: 'stake', op: 'offer', ok: true, stake: { kind: 'time', amount: 900 } });
  c.finish('GIVEUP');
  c.receive({ type: 'stake', op: 'settled', game: 'goon', ok: true, gave_up: true, match: 'GIVEUP' });
  ok(c.settledFor('GIVEUP').result === 'void', 'host gave up: void, nothing booked');

  // Unstaked match: lock still goes, the settle watch does not.
  c.lock('XYZ789');
  c.finish('XYZ789');
  ok(sent.filter((m) => m.match === 'XYZ789' && m.type === 'stake-settle').length === 0, 'unstaked match: no settle watch');
  ok(sent.filter((m) => m.match === 'XYZ789' && m.lock).length === 1, 'unstaked match: still locks');

  // Busy: one quiet retry.
  c.offer('RETRY1', { kind: 'sp', amount: 5 });
  c.receive({ type: 'stake', op: 'offer', ok: false, match: 'RETRY1', reason: 'busy' });
  ok(c.get('RETRY1').pending && !c.get('RETRY1').error, 'busy keeps the offer pending');
  await new Promise((r) => setTimeout(r, 1100));
  ok(sent.filter((m) => m.type === 'stake-offer' && m.match === 'RETRY1').length === 2, 'busy retried once');
  c.receive({ type: 'stake', op: 'offer', ok: false, match: 'RETRY1', reason: 'busy' });
  ok(!c.get('RETRY1').pending && c.get('RETRY1').error === S.stake.down, 'second busy gives words');

  // Sign-in / disabled refusal hides the row.
  c.receive({ type: 'stake', op: 'offer', ok: false, match: 'RETRY1', reason: 'disabled' });
  ok(!c.enabled(), 'disabled reply hides the row');
  ok(changes > 0, 'painters were told');

  // A reply for no match at all is dropped, not thrown.
  const c2 = createStakeClient({ send: () => {} });
  c2.receive({ type: 'stake', op: 'state', ok: true });
  c2.receive(null);
  ok(true, 'orphan replies are dropped');
}

console.log('selftest-stake: ' + (n - failures) + '/' + n + ' checks passed');
if (failures) process.exit(1);
