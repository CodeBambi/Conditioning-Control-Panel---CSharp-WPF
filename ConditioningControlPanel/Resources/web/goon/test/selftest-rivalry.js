// Self-contained pass over Game Night's rivalry lane: the hit stamps, the
// per-opponent record and the finished-match count the duel lane gates on.
//
//   node Resources/web/goon/test/selftest-rivalry.js
//
// What is asserted:
//   1. ui/nightProgress.js reads 0 with no storage, counts up, survives junk,
//      and exports exactly the two functions the duel lane imports.
//   2. ui/rivalry.js keys on a SANITIZED name (a wire string is never a key),
//      books W / L / D, skips abandons, caps its size, and books a match once.
//   3. ui/hitStamps.js stamps HIT once per payload id, dull on a refusal,
//      nothing on the later completed / survived receipts, and names inbound.
//   4. Every string the recap reads for the rematch note exists.

import { GoonEndReason, GoonPayloadKind } from '../core/contracts.js';
import { GoonReceiptStatus } from '../core/scoring.js';

let n = 0;
let failures = 0;
function ok(cond, what, detail) {
  n++;
  if (!cond) { failures++; console.error('FAIL: ' + what + (detail !== undefined ? ' (' + detail + ')' : '')); }
}

function memStore() {
  const mem = new Map();
  return {
    mem,
    getItem: (k) => (mem.has(k) ? mem.get(k) : null),
    setItem: (k, v) => { mem.set(k, String(v)); },
    removeItem: (k) => { mem.delete(k); },
  };
}

// ------------------------------------------------ 1. finished-match count
{
  const np = await import('../ui/nightProgress.js');
  ok(Object.keys(np).sort().join(',') === 'finishedMatches,noteMatchFinished',
    'nightProgress exports exactly finishedMatches + noteMatchFinished', Object.keys(np).join(','));
  ok(np.finishedMatches() === 0, 'no storage reads 0');
  ok(np.noteMatchFinished() === 0, 'no storage writes nothing and says 0');

  const store = memStore();
  globalThis.localStorage = store;
  ok(np.finishedMatches() === 0, 'empty storage reads 0');
  ok(np.noteMatchFinished() === 1, 'first finish counts 1');
  ok(np.noteMatchFinished() === 2 && np.finishedMatches() === 2, 'second finish counts 2');
  ok(store.mem.get('goon.night.finished.v1') === '2', 'stored under goon.night.finished.v1');
  store.mem.set('goon.night.finished.v1', 'banana');
  ok(np.finishedMatches() === 0, 'junk reads 0');
  store.mem.set('goon.night.finished.v1', '-5');
  ok(np.finishedMatches() === 0, 'a negative reads 0');
  globalThis.localStorage = { getItem() { throw new Error('denied'); }, setItem() { throw new Error('denied'); } };
  ok(np.finishedMatches() === 0 && np.noteMatchFinished() === 0, 'a throwing storage reads and writes 0');
  delete globalThis.localStorage;
}

// ---------------------------------------------------------- 2. the record
{
  const rv = await import('../ui/rivalry.js');
  const { rivalKey, cleanName, outcomeOf, createRivalry, formatRecord, settleOnce, RIVALRY_KEY, RIVALRY_MAX } = rv;

  ok(rivalKey('  Sam  ') === 'n:sam', 'keys trim and fold case', rivalKey('  Sam  '));
  ok(rivalKey('SAM') === rivalKey('sam'), 'case does not split a rivalry');
  ok(rivalKey('Sa' + String.fromCharCode(0x200b) + 'm') === 'n:sam', 'zero-width characters are stripped');
  ok(rivalKey('<b>x</b>').indexOf('<') < 0, 'markup brackets never reach a key');
  ok(rivalKey('a' + String.fromCharCode(10) + 'b') === 'n:ab', 'control characters are stripped');
  ok(rivalKey('') === '' && rivalKey(null) === '' && rivalKey('   ') === '', 'nothing to key on is no key');
  ok(rivalKey('x'.repeat(500)).length <= 34, 'keys are length-capped');
  ok(cleanName({ toString() { throw new Error('boom'); } }) === '', 'a hostile toString reads empty');

  ok(outcomeOf({ endReason: GoonEndReason.Mercy, winnerIsHost: true, localWon: true }) === 'w', 'a win is w');
  ok(outcomeOf({ endReason: GoonEndReason.SuddenDeathLoss, winnerIsHost: false, localWon: false }) === 'l', 'a loss is l');
  ok(outcomeOf({ endReason: GoonEndReason.Draw, winnerIsHost: null, localWon: false }) === 'd', 'a draw is d');
  ok(outcomeOf({ endReason: GoonEndReason.Abandon, winnerIsHost: true, localWon: true }) === null, 'an abandon is not booked');
  ok(outcomeOf(null) === null, 'no result is not booked');

  const store = memStore();
  let t = 0;
  const r = createRivalry({ store, now: () => ++t });
  ok(r.recordFor('Sam').known === false, 'a stranger has no record');
  ok(formatRecord(r.recordFor('Sam'), 'Sam') === '', 'and no line');
  r.note('Sam', 'w'); r.note('sam', 'w'); r.note('SAM ', 'l');
  const rec = r.recordFor('Sam');
  ok(rec.w === 2 && rec.l === 1 && rec.d === 0 && rec.known, 'books w/w/l under one key', JSON.stringify(rec));
  ok(formatRecord(rec, 'Sam') === 'you 2 - 1 Sam', 'formats "you 2 - 1 Sam"', formatRecord(rec, 'Sam'));
  r.note('Sam', 'd');
  ok(formatRecord(r.recordFor('Sam'), 'Sam') === 'you 2 - 1 Sam, 1 draw', 'draws ride along', formatRecord(r.recordFor('Sam'), 'Sam'));
  ok(r.note('', 'w') === null && r.note('Sam', 'x') === null, 'no key or a bad outcome writes nothing');
  ok(formatRecord({ w: 1, l: 0, d: 0, known: true }, '') === 'you 1 - 0 them', 'a nameless peer reads "them"');
  ok(formatRecord(rec, 'A'.repeat(40)).indexOf('A'.repeat(33)) < 0, 'the shown name is length-capped');

  store.mem.set(RIVALRY_KEY, '{not json');
  ok(r.recordFor('Sam').known === false, 'corrupt storage reads as no record');
  store.mem.set(RIVALRY_KEY, JSON.stringify({ 'n:sam': { w: 'lots', l: -3, d: 1e99 } }));
  const junk = r.recordFor('Sam');
  ok(junk.w === 0 && junk.l === 0 && junk.d === 99999, 'junk counts are clamped', JSON.stringify(junk));

  const big = memStore();
  let tt = 0;
  const rb = createRivalry({ store: big, now: () => ++tt });
  for (let i = 0; i < RIVALRY_MAX + 5; i++) rb.note('p' + i, 'w');
  const kept = Object.keys(JSON.parse(big.mem.get(RIVALRY_KEY)));
  ok(kept.length === RIVALRY_MAX, 'the table is capped', String(kept.length));
  ok(kept.indexOf('n:p0') < 0 && kept.indexOf('n:p' + (RIVALRY_MAX + 4)) >= 0, 'the oldest-seen falls off first');

  const once = createRivalry({ store: memStore() });
  const match = { result: null, opponent: { displayName: 'Kit' } };
  ok(settleOnce(match, once) === null, 'no result yet books nothing');
  match.result = { endReason: GoonEndReason.Mercy, winnerIsHost: true, localWon: true };
  settleOnce(match, once); settleOnce(match, once); settleOnce(match, once);
  ok(once.recordFor('Kit').w === 1, 'a match books exactly once however often the recap paints');
  const practice = { result: { endReason: GoonEndReason.Mercy, winnerIsHost: true, localWon: true }, opponent: { displayName: 'Bot' } };
  settleOnce(practice, once, { practice: true });
  ok(once.recordFor('Bot').known === false, 'practice never books');
  ok(settleOnce(null, once) === null && settleOnce({ get result() { throw new Error('x'); } }, once) === null,
    'a missing or hostile match books nothing and does not throw');
}

// ------------------------------------------------------------ 3. the stamps
{
  const hs = await import('../ui/hitStamps.js');
  const { stampForReceipt, stampForInbound, stampTilt, createHitStamps } = hs;

  ok(stampForReceipt({ status: GoonReceiptStatus.Accepted }).text === 'HIT', 'accepted is HIT');
  ok(stampForReceipt({ status: GoonReceiptStatus.Accepted }).size === 'big', 'and it is the big one');
  ok(stampForReceipt({ status: GoonReceiptStatus.RejectedRate }).tone === 'dull', 'too soon is dull');
  ok(stampForReceipt({ status: GoonReceiptStatus.RejectedFiltered }).text === 'BLOCKED', 'filtered is BLOCKED');
  ok(stampForReceipt({ status: GoonReceiptStatus.Completed }) === null, 'completed earns no second stamp');
  ok(stampForReceipt({ status: GoonReceiptStatus.Survived }) === null, 'survived earns no second stamp');
  ok(stampForReceipt(null) === null, 'no receipt, no stamp');
  ok(stampForInbound(GoonPayloadKind.Video).text === 'VIDEO' && stampForInbound(GoonPayloadKind.Video).size === 'small',
    'inbound names the thing, small');
  ok(stampForInbound(999).text === 'INCOMING', 'an unknown kind still stamps something plain');
  ok(stampForInbound(GoonPayloadKind.Video).sfx === null, 'inbound is silent (the HUD already plays the landing)');
  for (const k of Object.values(GoonPayloadKind)) {
    ok(typeof stampForInbound(k).text === 'string' && stampForInbound(k).text.length > 0, 'kind ' + k + ' has a word');
  }
  let tiltOk = true;
  for (let i = 0; i <= 20; i++) {
    const d = stampTilt(() => i / 20.0001);
    if (!(Math.abs(d) >= 3 && Math.abs(d) <= 9)) tiltOk = false;
  }
  ok(tiltOk, 'the tilt is between 3 and 9 degrees either way');

  // Wiring against a fake match. No document here: the stamp is a sound only.
  const cues = [];
  const audio = { sfx: (id) => cues.push(id) };
  const handlers = {};
  const offs = [];
  const fake = {};
  for (const name of ['onPayloadReceiptReceived', 'onPayloadAccepted']) {
    fake[name] = (fn) => { handlers[name] = fn; const off = () => { offs.push(name); handlers[name] = null; }; return off; };
  }
  const stamps = createHitStamps({ audio });
  stamps.attach(fake);
  handlers.onPayloadReceiptReceived({ id: 'p1h', status: GoonReceiptStatus.Accepted });
  handlers.onPayloadReceiptReceived({ id: 'p1h', status: GoonReceiptStatus.Completed });
  handlers.onPayloadReceiptReceived({ id: 'p1h', status: GoonReceiptStatus.Accepted });
  ok(cues.filter((c) => c === 'gg-hit').length === 1, 'one HIT per payload id', JSON.stringify(cues));
  handlers.onPayloadReceiptReceived({ id: 'p2h', status: GoonReceiptStatus.RejectedRate });
  ok(cues.indexOf('gg-hit-dull') >= 0, 'a refusal plays the dull cue');
  handlers.onPayloadReceiptReceived({ id: '', status: GoonReceiptStatus.Accepted });
  handlers.onPayloadReceiptReceived(null);
  ok(cues.length === 2, 'no id or no receipt stamps nothing', JSON.stringify(cues));
  handlers.onPayloadAccepted({ payload: { id: 'x', kind: GoonPayloadKind.Video }, fireAtLocalMs: 0 });
  stamps.detach();
  ok(offs.length === 2, 'detach unsubscribes both events');
  stamps.attach(fake);
  handlers.onPayloadReceiptReceived({ id: 'p1h', status: GoonReceiptStatus.Accepted });
  ok(cues.filter((c) => c === 'gg-hit').length === 2, 'a new match starts a fresh id ledger');
  stamps.detach();
  stamps.attach(null);
  ok(true, 'attach(null) is a no-op');

  const audioMod = await import('../ui/audio.js');
  ok(!!audioMod.SFX_REGISTRY['gg-hit'] && !!audioMod.SFX_REGISTRY['gg-hit-dull'], 'both stamp cues are registered');
}

// ---------------------------------------------------------- 4. the strings
{
  const { S } = await import('../ui/strings.js');
  for (const k of ['rematch', 'rematchSoon', 'rematchHost', 'rematchGuest', 'rematchPractice']) {
    ok(typeof S.recap[k] === 'string' && S.recap[k].length > 0, 'S.recap.' + k + ' exists');
    ok(S.recap[k].indexOf('!') < 0, 'S.recap.' + k + ' has no exclamation mark');
  }
}

if (failures) {
  console.error(`\nselftest-rivalry: ${n - failures}/${n} checks passed`);
  console.error(`${failures} FAILURE(S)`);
  process.exit(1);
}
console.log(`selftest-rivalry: ${n}/${n} checks passed`);
