// race/hostWords.js: a wordless desktop chart for a shipped level comes back with the words on it,
// and a chart for a track with no transcript, or one that already has words, comes back null.
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { wordHostChart } from '../hostWords.js';

const here = new URL('../', import.meta.url);
const fsFetch = async (u) => {
  const p = fileURLToPath(new URL(u, here));
  try { const txt = await readFile(p, 'utf8'); return { ok: true, status: 200, json: async () => JSON.parse(txt) }; }
  catch (e) { return { ok: false, status: 404 }; }
};
let fail = 0;
const ok = (c, m) => { console.log((c ? 'ok   ' : 'FAIL ') + m); if (!c) fail++; };

const dur = 726, bins = Math.ceil(dur / 0.5);
const energy = Array.from({ length: bins }, (_, i) => 0.4 + 0.3 * Math.sin(i / 20));
const host = { version: 1, binSec: 0.5, energy, events: [], acts: [], source: { name: 'Bambi IQ Lock', hash: '823b233f84fe3f3525fea00b6967fe79650eeb1f', durationSec: dur, sampleRate: 16000 }, analysis: { partial: true } };

const got = await wordHostChart(host, { fetch: fsFetch, log: console.log });
ok(got && got.words.length > 100, `IQ Lock by hash gets words (${got && got.words.length})`);
ok(got && got.events.some((e) => e.kind === 'word'), 'word bubbles are on the road');
ok(got && got.source.cloudId === '6c909be2-7a5b-495b-9f09-fabe1e3d5c16', 'the road is filed under the level cloudId');
ok(got && got.source.durationSec === dur, 'the host duration stands');

ok(await wordHostChart({ ...host, source: { ...host.source, hash: 'nope' } }, { fetch: fsFetch }) === null, 'no transcript -> null');
ok(await wordHostChart({ ...host, words: [{ t: 1, w: 'x' }] }, { fetch: fsFetch }) === null, 'already worded -> null');
ok(await wordHostChart({ ...host, hand: true }, { fetch: fsFetch }) === null, 'authored -> null');
process.exit(fail ? 1 : 0);
