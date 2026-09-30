/* ============================================================================
 * smoke/stake-smoke.mjs - node checks for PvP stakes on the page (net/stake.js):
 * the pills, the labels, the result line, the state fold, and the controller's
 * frames to the host. No browser and no dependencies:
 *
 *   node smoke/stake-smoke.mjs
 *
 * Exits non-zero with the checks that broke.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
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

// --- the shelf (localStorage): a window closed mid-match must not carry the pick
{
  const shelf = new Map();
  const hadWindow = 'window' in globalThis;
  const was = globalThis.window;
  globalThis.window = { localStorage: { getItem: (k) => shelf.get(k) ?? null, setItem: (k, v) => shelf.set(k, String(v)) } };
  const quietHost = () => createStake({ post: () => {}, onMessage: (fn) => { quietHost.hear = fn; return () => {}; } });
  const a = quietHost();
  quietHost.hear({ type: 'stake', op: 'limits', ok: true, enabled: true, time_ok: true });
  a.choose('sp', 10);
  a.begin('m1');
  a.clear();
  eq('a match left on the found screen keeps the pick on the shelf', createStake({ store: true }).state.pick, { kind: 'sp', amount: 10 });
  a.begin('m2');
  a.play();
  a.choose('sp', 25);
  // the window closes here: no end(), no clear()
  eq('a window closed mid-match leaves the shelf at Off', createStake({ store: true }).state.pick, { kind: 'none', amount: 0 });
  a.dispose();
  const door = readFileSync(new URL('../door/door.js', import.meta.url), 'utf8');
  ok('the door calls play() as the online board is dealt', /stake\.begin\(match\.id\); stake\.play\?\.\(\);/.test(door));
  if (hadWindow) globalThis.window = was; else delete globalThis.window;
}

// --- two quick taps (bug hunt 2026-09-29, STAKES-7). The server takes the first
// offer and answers one that meets its held stake lock 'busy', so the first stands.
// The row must never light an amount that is not the one at stake.
{
  const sent = [];
  const timers = [];
  let hear = null;
  const st = createStake({
    post: (m) => sent.push(m), onMessage: (fn) => { hear = fn; return () => {}; }, store: false,
    later: (fn, ms) => { timers.push({ fn, ms }); return timers.length; },
  });
  const offers = () => sent.filter((m) => m.type === 'stake-offer').map((m) => m.kind + m.amount);
  hear({ type: 'stake', op: 'limits', ok: true, enabled: true, options: { time: [900, 1800], sp: [5, 10, 25] }, time_ok: false });
  const M = 'm_0123456789abcdef';
  st.begin(M);
  st.choose('sp', 25);   // tap 1
  st.choose('sp', 5);    // tap 2, before the first reply lands
  eq('a second tap while the first offer is on the wire sends nothing', offers(), ['sp25']);
  eq('and the lit pill stays the one offered', st.state.pick, { kind: 'sp', amount: 25 });
  ok('the row is busy until the reply', st.state.busy === true);
  hear({ type: 'stake', op: 'offer', match: M, ok: true, stake: { kind: 'sp', amount: 25 }, sp: 75 });
  ok('the reply frees the row', st.state.busy === false);
  eq('the lit pill is the stake', [st.state.pick, st.state.you], [{ kind: 'sp', amount: 25 }, { kind: 'sp', amount: 25 }]);
  hear({ type: 'stake', op: 'state', match: M, ok: true, you: { kind: 'sp', amount: 25 }, them: null, locked: true, settled: null });
  eq('after the lock the lit pill and the stake still agree', st.state.pick, st.state.you);

  // the server's stake lock was held for a moment: one quiet retry, then words
  const N = 'm_fedcba9876543210';
  st.end(M); st.clear(); st.begin(N);   // the finished match spent the pick, so nothing is offered unasked
  st.choose('sp', 10);
  hear({ type: 'stake', op: 'offer', match: N, ok: false, reason: 'busy' });
  eq('busy is retried once, after a moment', [timers.length, (timers[0]?.ms || 0) >= 500], [1, true]);
  ok('the row stays busy for the retry, with no words yet', st.state.busy === true && !st.state.refusal);
  timers.shift()?.fn();
  eq('the retry offers the same pick', offers().at(-1), 'sp10');
  hear({ type: 'stake', op: 'offer', match: N, ok: false, reason: 'busy' });
  eq('a second busy is not retried', timers.length, 0);
  eq('it says so, and the lit pill goes back to what is at stake', [st.state.refusal, st.state.pick], ['busy', { kind: 'none', amount: 0 }]);

  // any other refusal: the pill goes back to the stake the server already holds
  st.choose('sp', 5);
  hear({ type: 'stake', op: 'offer', match: N, ok: true, stake: { kind: 'sp', amount: 5 }, sp: 95 });
  st.choose('sp', 25);
  hear({ type: 'stake', op: 'offer', match: N, ok: false, reason: 'insufficient_sp' });
  eq('a refused raise lights the stake that stands', [st.state.pick, st.state.you], [{ kind: 'sp', amount: 5 }, { kind: 'sp', amount: 5 }]);
  eq('and says why', st.state.refusal, 'insufficient_sp');
  st.dispose();

  const door = readFileSync(new URL('../door/door.js', import.meta.url), 'utf8');
  ok('the door disables the pills while an offer is on the wire', /stake-pill[\s\S]{0,400}?\(lock \|\| s\.busy\) \? ' disabled'/.test(door));
}

// --- leaving the found screen takes the ante back (bug hunt 2026-09-29, STAKES-4).
// A paired match that never starts held the ante until the server's match record
// expired, up to 24 h, relaunch or not. Esc on the found screen now says 'none'.
{
  const sent = [];
  const queued = [];
  let hear = null;
  const st = createStake({ post: (m) => sent.push(m), onMessage: (fn) => { hear = fn; return () => {}; }, store: false, later: (fn) => { queued.push(fn); return 0; } });
  hear({ type: 'stake', op: 'limits', ok: true, enabled: true, time_ok: false });
  const withdrawals = () => sent.filter((m) => m.type === 'stake-offer' && m.kind === 'none').map((m) => m.match);
  const A = 'm_aaaaaaaaaaaaaaaa', B = 'm_bbbbbbbbbbbbbbbb', C = 'm_cccccccccccccccc', D = 'm_dddddddddddddddd';
  // the door's order on Esc: withdraw, then clear
  st.begin(A);
  st.withdraw(); st.clear();
  eq('nothing held and nothing on the wire: nothing to take back', withdrawals(), []);

  st.begin(B);
  st.choose('sp', 10);
  hear({ type: 'stake', op: 'offer', match: B, ok: true, stake: { kind: 'sp', amount: 10 }, sp: 90 });
  st.withdraw(); st.clear();
  eq('a held ante is taken back', withdrawals(), [B]);
  hear({ type: 'stake', op: 'offer', match: B, ok: false, reason: 'offline' });
  eq('a refused take-back lights nothing: the pick waits for the next match', st.state.pick, { kind: 'sp', amount: 10 });

  st.begin(C);                                   // the pick is offered at once to the next pairing
  st.withdraw(); st.clear();
  eq('an offer still on the wire goes first, so the take-back cannot overtake it', withdrawals(), [B]);
  hear({ type: 'stake', op: 'offer', match: C, ok: true, stake: { kind: 'sp', amount: 10 }, sp: 80 });
  eq('then the ante it took is taken back', withdrawals(), [B, C]);
  eq('and its late reply lights nothing', st.state.you, null);
  hear({ type: 'stake', op: 'offer', match: C, ok: false, reason: 'busy' });
  eq('a take-back that met the stake lock is queued once more', queued.length, 1);
  queued.shift()();
  hear({ type: 'stake', op: 'offer', match: C, ok: false, reason: 'busy' });
  eq('and sent, once', [withdrawals(), queued.length], [[B, C, C], 0]);

  st.begin(D);
  hear({ type: 'stake', op: 'state', match: D, ok: true, you: { kind: 'sp', amount: 10 }, them: null, locked: true, settled: null });
  st.withdraw();
  eq('a locked stake is the game\'s, not the door\'s', withdrawals(), [B, C, C]);
  st.dispose();

  const door = readFileSync(new URL('../door/door.js', import.meta.url), 'utf8');
  ok('Esc on the found screen takes the ante back before it forgets the match', /screen === 'found'\) \{[^\n]*stake\.withdraw\(\); stake\.clear\(\);/.test(door));
}

if (failures.length) {
  console.error(`stake-smoke: ${failures.length} failed, ${passed} passed`);
  for (const f of failures) console.error('  FAIL ' + f);
  process.exit(1);
}
console.log(`stake-smoke: ${passed} passed`);
