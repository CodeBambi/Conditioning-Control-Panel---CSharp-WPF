/* backroom/room/tests/bell.test.mjs - the floor bell's wording (CONTRACT 10.16.B).
 * node --test ConditioningControlPanel/Resources/web/backroom/room/tests/*.test.mjs */

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { agoText, bellLine, bellLines, bellName, phraseOf, PHRASES, ROTATE_MS, MAX_ENTRIES, NAME_MAX } from '../bell.js';
import { createBellMock, BELL_FIXTURE } from '../../smoke/mock-bell.js';

const NOW = 1757890123456;
const en = (key, fallback) => fallback;                     // the page's English fallbacks (Law VII)
const de = (key, fallback) => ({                            // a translation, to prove nothing is baked in
  br_bell_line: '{ago}: {who} {what}',
  br_bell_someone: 'jemand',
  br_bell_slot_spiral3: 'traf 3 Spiralen',
  br_bell_ago_min: 'vor {n} Min.',
}[key] || fallback);

const entry = (over) => ({ t: NOW, station: 'slot', line: 'spiral3', pay: 10, name: null, ...over });

test('the contract line, word for word: "someone hit 3 spirals 4 min ago"', () => {
  assert.equal(bellLine(entry({ t: NOW - 4 * 60000 }), NOW, en), 'someone hit 3 spirals 4 min ago');
});

test('an opted-in entry puts the stored name where "someone" was', () => {
  assert.equal(bellLine(entry({ t: NOW - 4 * 60000, name: 'Rosewood' }), NOW, en), 'Rosewood hit 3 spirals 4 min ago');
});

test('every station and line the bell rings for has a phrase, and nothing else does', () => {
  const rows = [
    ['slot', 'emi3', 'someone hit the jackpot just now'],
    ['slot', 'gif3same', 'someone hit three of a kind just now'],
    ['slot', 'sub3', 'someone hit 3 subliminals just now'],
    ['slot', 'spiral3', 'someone hit 3 spirals just now'],
    ['wheel', 'keep_the_change', 'someone hit Keep the Change just now'],
    ['wheel', 'spoiled_rotten', 'someone got Spoiled Rotten just now'],
    ['wheel', 'jackpot', 'someone took the pot just now'],
    ['wheel', 'dazed', 'someone hit a big slice just now'],
    ['wheel', 'deep', 'someone hit a big slice just now'],
    ['cards', 'blackjack', 'someone drew a natural just now'],
    ['roulette', 'wake', 'someone doubled on a Spiral Wake just now'],
  ];
  for (const [station, line, want] of rows) assert.equal(bellLine(entry({ station, line }), NOW, en), want, station + '/' + line);
  // The 10.16.B table is exactly these rows, and the wheel's two big slices share one phrase.
  assert.equal(rows.length, Object.values(PHRASES).reduce((n, m) => n + Object.keys(m).length, 0));
  assert.equal(PHRASES.wheel.dazed[0], PHRASES.wheel.deep[0]);
  const bad = [null, undefined, {}, { station: 'slot' }, { station: 'slot', line: 'gif3' }, { station: 'lobby', line: 'spiral3' },
    { station: '__proto__', line: 'spiral3' }, { station: 'slot', line: 'toString' }];
  for (const row of bad) {
    assert.equal(phraseOf(row), null, JSON.stringify(row));
    assert.equal(bellLine(row, NOW, en), null, JSON.stringify(row));
  }
});

test('relative time, from the client clock: just now / N min / N h / N d', () => {
  assert.equal(agoText(0, en), 'just now');
  assert.equal(agoText(59999, en), 'just now');
  assert.equal(agoText(60000, en), '1 min ago');
  assert.equal(agoText(59 * 60000 + 59999, en), '59 min ago');
  assert.equal(agoText(3600000, en), '1 h ago');
  assert.equal(agoText(23.9 * 3600000, en), '23 h ago');
  assert.equal(agoText(86400000, en), '1 d ago');
  assert.equal(agoText(9 * 86400000, en), '9 d ago');
});

test('a client clock behind the server never reads as the future (Law I: display only)', () => {
  assert.equal(agoText(-500000, en), 'just now');
  assert.equal(bellLine(entry({ t: NOW + 600000 }), NOW, en), 'someone hit 3 spirals just now');
  assert.equal(bellLine(entry({ t: 'soon' }), NOW, en), 'someone hit 3 spirals just now');
});

test('the stored name is trimmed, collapsed and cut to 24', () => {
  assert.equal(bellName('  a  very   long   display name that runs on  '), 'a very long display name');
  assert.equal(bellName('a very long display name that runs on').length, NAME_MAX);
  for (const row of [null, undefined, 42, '', '   ', {}, []]) assert.equal(bellName(row), null);
  assert.equal(bellLine(entry({ name: '   ' }), NOW, en), 'someone hit 3 spirals just now');
});

test('a translation reorders the line and re-voices every part (Law VII)', () => {
  assert.equal(bellLine(entry({ t: NOW - 4 * 60000 }), NOW, de), 'vor 4 Min.: jemand traf 3 Spiralen');
});

test('a lex that throws or answers rubbish still prints the English line', () => {
  const angry = () => { throw new Error('no lexicon'); };
  assert.equal(bellLine(entry({ t: NOW - 4 * 60000 }), NOW, angry), 'someone hit 3 spirals 4 min ago');
  assert.equal(bellLine(entry({ t: NOW - 4 * 60000 }), NOW, () => 42), 'someone hit 3 spirals 4 min ago');
  assert.equal(bellLine(entry({ t: NOW - 4 * 60000 }), NOW, undefined), 'someone hit 3 spirals 4 min ago');
});

test('bellLines: newest first, at most 20, unprintable entries dropped', () => {
  const many = Array.from({ length: 30 }, (_, i) => entry({ t: NOW - i * 60000 }));
  assert.equal(bellLines(many, NOW, en).length, MAX_ENTRIES);
  assert.equal(bellLines(many, NOW, en)[0], 'someone hit 3 spirals just now');
  assert.equal(bellLines(many, NOW, en)[1], 'someone hit 3 spirals 1 min ago');
  assert.deepEqual(bellLines([{ station: 'lobby', line: 'x' }], NOW, en), []);
  assert.deepEqual(bellLines(null, NOW, en), []);
  assert.deepEqual(bellLines('nope', NOW, en), []);
});

test('the must-hit standing line is the first of the rotation, and only while it is set (10.16.E)', () => {
  const rows = [entry({ t: NOW - 60000 })];
  assert.deepEqual(bellLines(rows, NOW, en, 'The pot has to fall today'),
    ['The pot has to fall today', 'someone hit 3 spirals 1 min ago']);
  assert.deepEqual(bellLines(rows, NOW, en, null), ['someone hit 3 spirals 1 min ago']);
  assert.deepEqual(bellLines([], NOW, en, 'The pot has to fall today'), ['The pot has to fall today']);
});

test('one line rotates every 8,000 ms', () => {
  assert.equal(ROTATE_MS, 8000);
});

/* ---------------------------------------------------- the room's bell mock */

test('the mock answers the 10.16.B state shape and counts every read', async () => {
  const mock = createBellMock({ now: () => NOW });
  const res = await mock.handle('state');
  assert.equal(res.ok, true);
  assert.equal(res.status, 200);
  assert.equal(res.body.ok, true);
  assert.equal(res.body.open, true);
  assert.equal(res.body.optIn, false);
  assert.equal(res.body.entries.length, BELL_FIXTURE.length);
  assert.equal(res.body.entries[0].t, NOW - 20000, 'fixture offsets are resolved against now');
  assert.ok(Number.isInteger(res.body.visit.day));
  assert.equal(res.body.jackpot.mustHit, false);
  assert.equal(mock.reads.state, 1);
});

test('the mock fixture prints one line of every kind, and the unknown row is dropped', () => {
  const mock = createBellMock({ now: () => NOW });
  const rows = BELL_FIXTURE.map((e) => ({ ...e, t: NOW + e.t }));
  assert.deepEqual(bellLines(rows, NOW, en), [
    'someone hit 3 spirals just now',
    'Rosewood took the pot 4 min ago',
    'someone drew a natural 47 min ago',
    'a very long display name doubled on a Spiral Wake 3 h ago',
    'someone hit the jackpot 2 d ago',
    'someone hit a big slice 3 d ago',
  ]);
  assert.equal(mock.reads.state, 0, 'the wording never asks the server anything');
});

test('opt writes the flag back, and refuses anything that is not a boolean', async () => {
  const mock = createBellMock({ now: () => NOW });
  assert.deepEqual((await mock.handle('opt', { on: true })).body, { ok: true, optIn: true });
  assert.equal((await mock.handle('state')).body.optIn, true);
  assert.deepEqual((await mock.handle('opt', { on: 'yes' })).body, { ok: false, reason: 'bad_input' });
  assert.equal((await mock.handle('state')).body.optIn, true, 'a refused write changes nothing');
  assert.deepEqual((await mock.handle('opt', { on: false })).body, { ok: true, optIn: false });
});

test('a shut door still answers state, with open:false', async () => {
  const mock = createBellMock({ now: () => NOW, open: false });
  const res = await mock.handle('state');
  assert.equal(res.body.ok, true);
  assert.equal(res.body.open, false);
  assert.equal((await mock.handle('opt', { on: true })).status, 403);
});

test('too_fast is an HTTP 200 soft refusal and the page keeps the lines it has', async () => {
  const mock = createBellMock({ now: () => NOW });
  mock.fail('state', 'too_fast');
  const res = await mock.handle('state');
  assert.equal(res.status, 200);
  assert.deepEqual(res.body, { ok: false, reason: 'too_fast', retryInMs: 5000 });
  assert.equal((await mock.handle('state')).body.ok, true, 'the next read is answered');
});
