#!/usr/bin/env node
/* ============================================================================
 * scripts/backroom-words-manifest.mjs
 *
 * Lists the Back Room's PRESET subliminal phrases and the clip files they want, so the owner can feed
 * the phrase list to the ElevenLabs pipeline and drop the results into
 * ConditioningControlPanel/Resources/Audio/backroom/words/ (CONTRACT.md 10.21).
 *
 * The preset lexicon is the one place a phrase can come from that the PLAYER did not choose: the four
 * fallback words in BackRoomMedia.PresetWords, resolved through the `br_word_*` keys in the English
 * locale when those exist. Everything else the slot says comes from the player's own subliminal pool
 * and their enabled Awareness triggers, and those already have the player's own audio (or Windows
 * speech) behind them - there is nothing to generate for them here.
 *
 *   node scripts/backroom-words-manifest.mjs            the table: phrase, key, file
 *   node scripts/backroom-words-manifest.mjs --json     a ready-made words.json to paste
 *   node scripts/backroom-words-manifest.mjs --missing  only the files not already in the folder
 *   node scripts/backroom-words-manifest.mjs --ext wav  ask for .wav instead of .mp3
 * ==========================================================================*/

import { readFileSync, existsSync, readdirSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const MEDIA_CS = join(ROOT, 'ConditioningControlPanel', 'Services', 'BackRoom', 'BackRoomMedia.cs');
const EN_JSON = join(ROOT, 'ConditioningControlPanel', 'Localization', 'Languages', 'en.json');
const WORDS_DIR = join(ROOT, 'ConditioningControlPanel', 'Resources', 'Audio', 'backroom', 'words');

/** The manifest key: lower case, every run of non-letter / non-digit one space, trimmed.
 *  MUST stay the same rule as BackRoomVoice.Normalize. */
export function normalize(text) {
  return String(text == null ? '' : text).trim().toLowerCase().replace(/[^\p{L}\p{N}]+/gu, ' ').trim();
}
/** The file stem: the key with spaces as hyphens. Same rule as BackRoomVoice.Slug. */
export function slug(text) {
  return normalize(text).replace(/ /g, '-');
}

/** The (key, fallback) rows of BackRoomMedia.PresetWords, read straight out of the C# so the two
 *  can never drift apart. */
function presetRows() {
  const cs = readFileSync(MEDIA_CS, 'utf8');
  const block = cs.match(/PresetWords\s*=\s*\{([\s\S]*?)\};/);
  if (!block) throw new Error('BackRoomMedia.PresetWords not found in ' + MEDIA_CS);
  const rows = [];
  for (const m of block[1].matchAll(/\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*\)/g)) rows.push({ lexKey: m[1], fallback: m[2] });
  if (!rows.length) throw new Error('BackRoomMedia.PresetWords is empty');
  return rows;
}

/** The English text for a lexicon key, or null when the locale does not carry it (which is the case
 *  today: the neutral fallback in the C# is the shipped word). */
function localised(lexKey) {
  try {
    const en = JSON.parse(readFileSync(EN_JSON, 'utf8'));
    const v = en && typeof en === 'object' ? en[lexKey] : null;
    return typeof v === 'string' && v.trim() ? v.trim() : null;
  } catch { return null; }
}

function main(argv) {
  const json = argv.includes('--json');
  const missingOnly = argv.includes('--missing');
  const extAt = argv.indexOf('--ext');
  const ext = (extAt >= 0 && argv[extAt + 1] ? argv[extAt + 1] : 'mp3').replace(/^\./, '').toLowerCase();

  const have = existsSync(WORDS_DIR) ? new Set(readdirSync(WORDS_DIR).map((f) => f.toLowerCase())) : new Set();

  const seen = new Set();
  const rows = [];
  for (const { lexKey, fallback } of presetRows()) {
    const phrase = localised(lexKey) || fallback;
    const key = normalize(phrase);
    if (!key || seen.has(key)) continue;
    seen.add(key);
    const file = slug(phrase) + '.' + ext;
    rows.push({ phrase, lexKey, key, file, present: have.has(file.toLowerCase()) });
  }

  const wanted = missingOnly ? rows.filter((r) => !r.present) : rows;

  if (json) {
    const words = {};
    for (const r of wanted) words[r.key] = r.file;
    console.log(JSON.stringify({ version: 1, words }, null, 2));
    return 0;
  }

  console.log('Back Room preset word clips -> ' + WORDS_DIR);
  console.log('');
  console.log('  ' + 'PHRASE'.padEnd(18) + 'MANIFEST KEY'.padEnd(18) + 'FILE'.padEnd(22) + 'STATE');
  console.log('  ' + '-'.repeat(64));
  for (const r of wanted) {
    console.log('  ' + r.phrase.padEnd(18) + r.key.padEnd(18) + r.file.padEnd(22) + (r.present ? 'present' : 'TO MAKE'));
  }
  console.log('');
  console.log('  ' + wanted.filter((r) => !r.present).length + ' to generate, ' + wanted.filter((r) => r.present).length + ' already there.');
  console.log('');
  console.log('  Phrases for the generator, one per line:');
  for (const r of wanted) if (!r.present) console.log('    ' + r.phrase);
  console.log('');
  console.log('  Then: save each clip under its FILE name above and add its row to words.json');
  console.log('  (--json prints the whole manifest ready to paste).');
  return 0;
}

if (process.argv[1] && import.meta.url === new URL('file://' + process.argv[1].replace(/\\/g, '/')).href) {
  process.exit(main(process.argv.slice(2)));
}
