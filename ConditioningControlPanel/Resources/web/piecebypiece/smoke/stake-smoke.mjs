/* ============================================================================
 * smoke/stake-smoke.mjs - node checks for PvP stakes on the page (net/stake.js):
 * the pills, the labels, the result line, the state fold, and the controller's
 * frames to the host. No browser and no dependencies:
 *
 *   node smoke/stake-smoke.mjs
 *
 * Exits non-zero with the checks that broke.
 * ==========================================================================*/

import {
  pills, stakeLabel, readStake, sameStake, onOffer, resultText, refusalText, reduce,
  initialState, createStake, EN,
} from '../net/stake.js';

let passed = 0;
const failures = [];
function ok(what, cond) { if (cond) { passed++; return; } failures.push(what); }
function eq(what, got, want) {
  ok(`${what} (got ${JSON.stringify(got)}, wanted ${JSON.stringify(want)})`, JSON.stringify(got) === JSON.stringify(want));
}

// --- the pills
{
  const noTime = pills(undefined, false).map((p) => stakeLabel(p));
  eq('no time without chaster', noTime, ['Off', '5 ✦', '10 ✦', '25 ✦']);
  const all = pills({ time: [900, 1800], sp: [5, 10, 25] }, true).map((p) => stakeLabel(p));
  eq('the whole row', all, ['Off', '15 min', '30 min', '5 ✦', '10 ✦', '25 ✦']);
  eq('server options win', pills({ time: [900], sp: [10] }, true).length, 3);
  ok('time pick is off offer without chaster', !onOffer({ kind: 'time', amount: 900 }, undefined, false));
  ok('sp pick is on offer', onOffer({ kind: 'sp', amount: 10 }, undefined, false));
  ok('an odd amount is not', !onOffer({ kind: 'sp', amount: 11 }, undefined, false));
}

// --- reading stakes
{
  eq('none normalises', readStake({ kind: 'none', amount: 9 }), { kind: 'none', amount: 0 });
  eq('garbage is null', readStake({ kind: 'sp', amount: -5 }), null);
  eq('garbage kind is null', readStake({ kind: 'coins', amount: 5 }), null);
  ok('null reads as none for same', sameStake(null, { kind: 'none', amount: 0 }));
  eq('labels come from the host', stakeLabel({ kind: 'time', amount: 1800 }, { minutes: '{0} Min' }), '30 Min');
}

// --- the result line
{
  const sp = { kind: 'sp', amount: 10 };
  const time = { kind: 'time', amount: 1800 };
  eq('a won sparkle stake', resultText(sp, { result: 'won', sp_delta: 10 }), '+10 ✦');
  eq('a won time stake past the prize cap', resultText(time, { result: 'won', sp_delta: 0 }), 'Stake returned');
  eq('a lost time stake says what landed', resultText(time, { result: 'lost', time_s: 1800 }, 1800), '+30 min on your lock');
  eq('the tab took less', resultText(time, { result: 'lost', time_s: 1800 }, 900), '+15 min on your lock');
  eq('the tab took nothing', resultText(time, { result: 'lost', time_s: 1800 }, 0), 'Stake lost');
  eq('a lost sparkle stake', resultText(sp, { result: 'lost', sp_delta: -10 }), 'Stake lost');
  eq('a void', resultText(sp, { result: 'void' }), 'Stake returned');
  eq('no stake, no line', resultText({ kind: 'none', amount: 0 }, { result: 'won', sp_delta: 5 }), null);
  eq('not settled, no line', resultText(sp, null), null);
  eq('refusal', refusalText('pair_cap'), EN.refused_pair_cap);
  eq('unknown refusal', refusalText('busy'), EN.refused_other);
  ok('no em-dash or exclamation anywhere in the copy', !Object.values(EN).some((v) => /[—–!]/.test(v)));
}

// --- the fold
{
  let s = initialState({ kind: 'time', amount: 900 });
  s = reduce(s, { type: 'stake', op: 'limits', ok: true, enabled: true, time_ok: false, options: { time: [900, 1800], sp: [5, 10, 25] } });
  ok('enabled', s.enabled);
  eq('a time pick without chaster waits as off', s.pick, { kind: 'none', amount: 0 });
  s = reduce(s, { type: 'stake', op: 'limits', ok: false, reason: 'disabled', time_ok: true });
  ok('disabled hides the row', !s.enabled);

  s = { ...initialState(), enabled: true, match: 'm1' };
  s = reduce(s, { type: 'stake', op: 'state', match: 'm2', ok: true, you: { kind: 'sp', amount: 5 } });
  eq('another match is ignored', s.you, null);
  s = reduce(s, { type: 'stake', op: 'state', match: 'm1', ok: true, you: { kind: 'sp', amount: 5 }, them: { kind: 'time', amount: 1800 }, locked: false, settled: null });
  eq('them read', s.them, { kind: 'time', amount: 1800 });
  s = reduce(s, { type: 'stake', op: 'offer', match: 'm1', ok: false, reason: 'started' });
  ok('started locks', s.locked);
  eq('and says why', s.refusal, 'started');
  s = { ...s, ended: 'm1', pending: true };
  s = reduce(s, { type: 'stake', op: 'settled', match: 'm1', ok: true, settled: { result: 'lost', time_s: 900 }, booked_s: 900 });
  ok('settled clears pending', !s.pending);
  eq('booked kept', s.booked, 900);
  const g = reduce({ ...initialState(), ended: 'm3', pending: true }, { type: 'stake', op: 'settled', match: 'm3', ok: true, gave_up: true });
  eq('giving up reads as a void', g.settled, { result: 'void' });
}

// --- the controller's frames
{
  const sent = [];
  let hear = null;
  const st = createStake({ post: (m) => sent.push(m), onMessage: (fn) => { hear = fn; return () => {}; }, store: false });
  st.begin('m1');
  eq('nothing is offered before the host says stakes are on', sent.length, 0);
  st.limits();
  hear({ type: 'stake', op: 'limits', ok: true, enabled: true, time_ok: true });
  st.choose('sp', 10);
  eq('a pick on a live match is offered', sent.filter((m) => m.type === 'stake-offer').map((m) => [m.match, m.kind, m.amount]), [['m1', 'sp', 10]]);
  st.choose('sp', 11);
  eq('an odd pick is refused on the page', sent.filter((m) => m.type === 'stake-offer').length, 1);
  hear({ type: 'stake', op: 'offer', match: 'm1', ok: true, stake: { kind: 'sp', amount: 10 }, sp: 90 });
  st.end('m1');
  eq('the end asks the host to settle', sent.at(-1), { type: 'stake-settle', match: 'm1' });
  eq('and the card says it is settling', st.resultLine(), EN.pending);
  hear({ type: 'stake', op: 'settled', match: 'm1', ok: true, settled: { result: 'won', sp_delta: 10 }, booked_s: 0 });
  eq('then the result', st.resultLine(), '+10 ✦');
  eq('the finished match spends the pick', st.state.pick, { kind: 'none', amount: 0 });
  st.clear();
  eq('the menu forgets the match', st.state.match, null);
  const before = sent.filter((m) => m.type === 'stake-offer').length;
  st.begin('m2');
  eq('the next match offers nothing unasked', sent.filter((m) => m.type === 'stake-offer').length, before);
  st.choose('sp', 5);
  eq('until the player picks again', sent.filter((m) => m.type === 'stake-offer').at(-1), { type: 'stake-offer', match: 'm2', kind: 'sp', amount: 5 });
  st.clear();
  eq('a match left before its end keeps the pick', st.state.pick, { kind: 'sp', amount: 5 });

  const quiet = [];
  const off = createStake({ post: (m) => quiet.push(m), onMessage: () => () => {}, store: false });
  off.begin('m9');
  off.end('m9');
  eq('a page with stakes off never asks to settle', quiet.length, 0);
  st.dispose(); off.dispose();
}

if (failures.length) {
  console.error(`stake-smoke: ${failures.length} failed, ${passed} passed`);
  for (const f of failures) console.error('  FAIL ' + f);
  process.exit(1);
}
console.log(`stake-smoke: ${passed} passed`);
