/* ============================================================================
 * tools/racechart/scan-hits.mjs - the words-file trigger scan, as JSON, for fingerprint.py
 *
 *   node tools/racechart/scan-hits.mjs [--words-dir <dir>] > hits.json
 *
 * One row per transcript in race/words/index.json, with every hit the maker's own
 * detector (chart/maker/triggers.js `detect`) finds for every TRIGGER_SET, in the
 * shape race/cloudChart.js scanTriggers() lays on the road: { setId, t, dur, conf,
 * i0, i1 }. fingerprint.py runs this so the windows it fingerprints are exactly the
 * ones the game would have used, ported nowhere twice.
 *
 * Reads the files on disk, prints JSON, touches nothing else. Never prints a word
 * of a transcript: seconds, set ids and confidences only.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const argv = process.argv.slice(2);
const flag = (name, dflt) => { const i = argv.indexOf(name); return i >= 0 && argv[i + 1] ? argv[i + 1] : dflt; };
const WORDS = resolve(flag('--words-dir', resolve(HERE, '../../Resources/web/dtrh/race/words')));
const WEB = resolve(HERE, '../../Resources/web/dtrh');

const { TRIGGER_SETS } = await import(new URL('file:///' + resolve(WEB, 'chart/editor/triggerSets.js').replace(/\\/g, '/')));
const { detect } = await import(new URL('file:///' + resolve(WEB, 'chart/maker/triggers.js').replace(/\\/g, '/')));

const clamp01 = (v) => Math.max(0, Math.min(1, Number(v) || 0));
const r3 = (v) => Math.round(v * 1000) / 1000;
const index = JSON.parse(readFileSync(resolve(WORDS, 'index.json'), 'utf8'));
const rows = [];
for (const row of index.rows || []) {
  const words = JSON.parse(readFileSync(resolve(WORDS, row.file), 'utf8'));
  const list = Array.isArray(words.words) ? words.words : [];
  // the phrase is only as sure as the least sure word in it (cloudChart.js scanTriggers)
  const conf = (i0, i1) => {
    let low = 1;
    for (let i = i0; i <= i1 && i < list.length; i++) { const c = list[i] && typeof list[i].conf === 'number' ? list[i].conf : 1; if (c < low) low = c; }
    return Math.round(clamp01(low) * 100) / 100;
  };
  const hits = [];
  for (const set of TRIGGER_SETS) {
    for (const m of detect(words, set, [], { durationSec: words.durationSec })) {
      hits.push({ setId: set.id, t: r3(m.t), dur: r3(Number(m.dur) || 0), conf: conf(m.i0, m.i1), i0: m.i0, i1: m.i1 });
    }
  }
  hits.sort((a, b) => a.t - b.t || a.setId.localeCompare(b.setId));
  rows.push({ cloudId: row.cloudId, hash: row.hash, title: row.title, file: row.file, durationSec: Number(words.durationSec) || 0, words: list.length, hits });
}
process.stdout.write(JSON.stringify({ version: 1, sets: TRIGGER_SETS.map((s) => ({ id: s.id, name: s.name, phrase: s.phrase, mode: s.mode })), rows }) + '\n');
