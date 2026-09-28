// The share card's words and numbers (ui/shareWords.js), and the recap wiring.
//
//   node Resources/web/goon/test/selftest-share.js
//
// What is asserted:
//   1. The flavour word is deterministic, and the two sides of one match land on
//      the same PAIR (winner reads the left word, loser the right one).
//   2. Every word is short, public-channel tame, and free of dashes the house
//      style forbids.
//   3. statLines leads with sending and duels, caps at four, takes the scoring
//      lane's shape, and humanises a key it has never seen.
//   4. buildShareData never carries a non-data avatar (a canvas cannot export a
//      foreign picture) and clamps names.
//   5. The recap wires the card without a sheet or a modal, and the strings exist.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const W = await import('../ui/shareWords.js');
const { S } = await import('../ui/strings.js');
const here = path.dirname(fileURLToPath(import.meta.url));
const read = (p) => fs.readFileSync(path.join(here, '..', p), 'utf8');

// Built from char codes: an editor round trip once turned escaped dashes into literal ones.
const BANNED = new RegExp('[' + String.fromCharCode(0x2013, 0x2014) + '!]');

let n = 0;
let failures = 0;
function ok(cond, what, detail) {
  n++;
  if (!cond) { failures++; console.error('FAIL: ' + what + (detail !== undefined ? ' (' + detail + ')' : '')); }
}

// ------------------------------------------------ 1. the word
{
  for (const f of ['trance', 'pink', 'frills', 'shiny', 'censored', 'mine', '', 'nonsense']) {
    for (const seed of [0n, 1n, 7n, 918273645n, 2n ** 63n - 1n, 42, 'room-abc']) {
      const w = W.flavourWord(f, 'w', seed);
      const l = W.flavourWord(f, 'l', seed);
      ok(w === W.flavourWord(f, 'w', seed), 'deterministic: ' + f + ' ' + String(seed));
      const t = W.WORDS[W.flavourKey(f)];
      const i = W.seedIndex(seed, t.pairs.length);
      ok(t.pairs[i][0] === w && t.pairs[i][1] === l, 'winner and loser read the same pair', f + ' ' + String(seed));
      ok(t.draw.includes(W.flavourWord(f, 'd', seed)), 'draw word comes from the draw list', f);
    }
  }
  ok(W.flavourWord('trance', 'a', 5n) === W.ABANDON_WORD, 'an abandon reads the one abandon word');
  ok(W.flavourKey('mine') === 'plain' && W.flavourKey('') === 'plain', 'mine and no pick wear the plain table');
  ok(W.seedIndex(-5, 4) >= 0 && W.seedIndex(-5n, 4) >= 0, 'negative seeds still index');
}

// ------------------------------------------------ 2. every word is tame
{
  const all = [W.ABANDON_WORD];
  for (const t of Object.values(W.WORDS)) { for (const p of t.pairs) all.push(...p); all.push(...t.draw); }
  for (const w of all) {
    ok(typeof w === 'string' && w.length > 0 && w.length <= 14, 'word fits the card: ' + w);
    ok(!BANNED.test(w), 'no en/em dash or exclamation: ' + w);
    ok(!/cum|cock|slut|whore|pussy|dick|fuck|sissy|bimbo/i.test(w), 'public-channel tame: ' + w);
  }
}

// ------------------------------------------------ 3. stat lines
{
  const lines = W.statLines({
    you: { stats: { held: 1, popped: 30, landed: 9, duels: 2, combo: 5, zapCount: 3 } },
    them: { stats: { held: 2, popped: 20, landed: 4, duels: 1 } },
  });
  ok(lines.length === W.MAX_STAT_LINES, 'capped at four', lines.length);
  ok(lines[0].key === 'landed' && lines[1].key === 'duels', 'sending leads, then duels', lines.map((l) => l.key).join(','));
  ok(!lines.some((l) => /surviv|time/i.test(l.label)), 'time survived is not a line');

  const odd = W.statLines({ you: { stats: { zapCount: 3 } }, them: { stats: {} } });
  ok(odd.length === 1 && odd[0].label === 'Zap count' && odd[0].them === null, 'unknown key humanised, missing side is null', JSON.stringify(odd));

  const scored = W.statsFromScoring({
    score: 700, sent: { landed: 11, held: 3, byKind: { 0: 4 } }, received: { held: 6, byKind: {} },
    pops: 40, bestCombo: 8, duels: { won: 2, lost: 1, points: 90 },
  });
  ok(scored.landed === 11 && scored.duels === 2 && scored.popped === 40 && scored.combo === 8 && scored.held === 6,
    'the scoring lane shape flattens', JSON.stringify(scored));
  ok(Object.keys(W.statsFromScoring(null)).length === 0, 'no scoring, no stats');

  const d = W.buildShareData({
    result: { localScore: 10, remoteScore: 20 }, outcome: 'w',
    scoring: { you: { score: 700, sent: { landed: 5 } }, them: { score: 300, sent: { landed: 2 } } },
  });
  ok(d.you.score === 700 && d.them.score === 300, 'scoring scores win over the result', d.you.score + '/' + d.them.score);
  ok(d.you.stats.landed === 5, 'and scoring stats replace the log counts');
}

// ------------------------------------------------ 4. data safety
{
  const log = { payloads: () => [
    { dir: 'out', status: 'landed' }, { dir: 'out', status: 'endured' }, { dir: 'out', status: 'blocked' },
    { dir: 'in', status: 'endured' }, { dir: 'in', status: 'landed' },
  ] };
  const d = W.buildShareData({
    result: { localScore: 5, remoteScore: 3, survivedMs: 60000 }, outcome: 'w', log,
    youName: '  A\nvery   long name that goes on and on forever  ', themName: '',
    youAvatar: 'https://evil.example/a.png', themAvatar: 'data:image/png;base64,AAAA',
    duels: { won: 1, lost: 0, tied: 0 }, highlights: ['Stone wall', 'Iron edge', 'Third'],
  });
  ok(d.you.avatarUrl === '', 'a remote avatar is dropped (it would taint the canvas)');
  ok(d.them.avatarUrl.startsWith('data:image/png'), 'a data avatar is kept');
  ok(d.you.name.length <= 24 && !/\n/.test(d.you.name), 'names are single-line and clamped', d.you.name);
  ok(d.them.name === 'Them', 'a missing name reads Them');
  ok(d.you.stats.landed === 2 && d.them.stats.landed === 2, 'landed counts both directions', JSON.stringify(d.you.stats) + JSON.stringify(d.them.stats));
  ok(d.you.stats.duels === 1, 'duels ride along when any were played');
  ok(d.highlights.length === 2, 'two highlights at most');
  ok(W.buildShareData({ outcome: null }).outcome === 'a', 'an abandon (null outcome) is a');
  ok(W.cardKey(d) === W.cardKey(W.buildShareData({
    result: { localScore: 5, remoteScore: 3, survivedMs: 60000 }, outcome: 'w', log,
    youName: '  A\nvery   long name that goes on and on forever  ', themName: '',
    youAvatar: 'https://evil.example/a.png', themAvatar: 'data:image/png;base64,AAAA',
    duels: { won: 1, lost: 0, tied: 0 }, highlights: ['Stone wall', 'Iron edge', 'Third'], now: 1,
  })), 'the redraw key ignores the clock');
  ok(/^[A-Z][a-z]{2} \d{1,2}, \d{4}$/.test(W.cardDate(Date.UTC(2026, 8, 24, 12))), 'plain English date', W.cardDate(Date.UTC(2026, 8, 24, 12)));
}

// ------------------------------------------------ 5. recap wiring + strings
{
  const rsrc = read('ui/screens/recap.js');
  ok(/refreshShare\(result\)/.test(rsrc) && /column\.appendChild\(shareNode\)/.test(rsrc), 'recap paints the share card');
  ok(!/sheets|options\.open|showModal/.test(rsrc), 'still no sheet and no modal on the recap');
  const csrc = read('ui/shareCard.js');
  ok(/ClipboardItem/.test(csrc) && /'share-card'/.test(csrc), 'copy tries the browser, then the host');
  ok(!/matchLog|receivedStore|peerRender/.test(csrc), 'the card module never reads match media');
  for (const k of ['title', 'lead', 'preparing', 'copy', 'save', 'copied', 'saved', 'copyFailed', 'saveFailed']) {
    ok(typeof S.share[k] === 'string' && S.share[k].length > 0, 'S.share.' + k + ' exists');
    ok(!BANNED.test(S.share[k]), 'S.share.' + k + ' has no dash or exclamation');
  }
  ok(S.share.copied === 'Copied. Paste it anywhere.', 'the copied toast says what to do next');
  ok(typeof S.share.alt === 'function' && S.share.alt('Spun').includes('Spun'), 'alt text names the word');
}

if (failures) { console.error('selftest-share: ' + failures + ' FAILURE(S) of ' + n); process.exit(1); }
console.log('selftest-share: ' + n + '/' + n + ' checks passed');
