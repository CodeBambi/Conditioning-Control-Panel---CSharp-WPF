/* ============================================================================
 * chart/maker/words.js - find the words, read the triggers (MAKER.md, PR M1).
 *
 * The author picked an mp3; the maker goes and finds the aligned transcript for
 * it rather than asking. First `./words/<stem>.words.json`, then any name in an
 * optional `./words/index.json`, first by name and then by the audio hash, so a
 * renamed file still finds its own words. Detection is `maker/triggers.js`
 * `detect()` over the catalogue in `editor/triggerSets.js`, so a trigger word
 * means the same thing here as it does in the race.
 * ==========================================================================*/

import { detect, TRIGGER_SETS, labelInk } from './triggers.js';

const seg = (s) => encodeURIComponent(String(s));
const loose = (s) => String(s || '').toLowerCase().replace(/[^a-z0-9]+/g, '');
export const wordsUrl = (stem) => './words/' + seg(stem + '.words.json');

async function getJson(url) {
  try {
    const r = await fetch(url, { cache: 'no-store' });
    if (!r.ok) return null;
    return await r.json();
  } catch (e) { return null; }
}

/** True when this looks like an align.py words file and not some other json. */
export function isWords(json) {
  return !!(json && typeof json === 'object' && Array.isArray(json.words) && json.words.length
    && typeof json.words[0].t === 'number' && typeof json.words[0].w === 'string');
}

/**
 * The words for this audio, or null. `via` says how it was found, so the page
 * can be honest about it: 'name', 'manifest' (a name in index.json) or 'hash'.
 */
export async function findWords(stem, hash) {
  const direct = await getJson(wordsUrl(stem));
  if (isWords(direct)) return { json: direct, via: 'name' };

  const index = await getJson('./words/index.json');
  const names = Array.isArray(index) ? index : (index && Array.isArray(index.files) ? index.files : []);
  const urls = names.slice(0, 60).map((n) => './words/' + seg(String(n).replace(/\.words\.json$/i, '') + '.words.json'));
  const want = loose(stem);
  for (const u of urls) {
    if (!loose(decodeURIComponent(u)).includes(want) || !want) continue;
    const j = await getJson(u);
    if (isWords(j)) return { json: j, via: 'manifest' };
  }
  if (!hash) return null;
  for (const u of urls) {
    const j = await getJson(u);
    if (isWords(j) && j.source && j.source.hash === hash) return { json: j, via: 'hash' };
  }
  return null;
}

/** '' when the words were made from this very file, else the line to put on screen. */
export function hashNote(json, hash) {
  const h = json && json.source && json.source.hash;
  return (h && hash && h !== hash) ? 'words file was made from a different copy of this audio' : '';
}

/**
 * Every set the file actually says, most said first: `[{ set, hits }]` where a
 * hit is `{ id, t, setId, n }`. A set with no hits is never shown, so a row on
 * screen is always a row worth ticking.
 */
export function scan(json, durationSec) {
  const rows = [];
  for (const set of TRIGGER_SETS) {
    const ms = detect(json, set, [], { durationSec });
    if (!ms.length) continue;
    rows.push({ set, hits: ms.map((m, n) => ({ id: 'h:' + set.id + ':' + n, t: m.t, setId: set.id, n })) });
  }
  rows.sort((a, b) => b.hits.length - a.hits.length || a.set.name.localeCompare(b.set.name));
  return rows;
}

export { TRIGGER_SETS, labelInk };
