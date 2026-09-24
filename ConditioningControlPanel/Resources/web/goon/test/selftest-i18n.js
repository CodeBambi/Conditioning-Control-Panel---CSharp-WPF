// The Goon page's language layer and its nine tables.
//
//   node Resources/web/goon/test/selftest-i18n.js
//
// What is asserted:
//   1. i18n/en.js is exactly what the copy decks say (test/gen-i18n-en.js
//      writes it; run that after any English change).
//   2. Every other table carries exactly en's keys, no more and no fewer, and
//      every value is a non-empty string.
//   3. Every {placeholder} in an English line survives in each translation, and
//      a translation invents none of its own.
//   4. No table carries double-encoded text (UTF-8 read as Latin-1) or an
//      em-dash, and the files have no BOM.
//   5. t() falls back to English for a key a table lacks, fills placeholders,
//      and leaves an unknown placeholder alone; setLang/normalizeLang/browserLang
//      map tags onto the nine languages.
//   6. The decks read the current language on access: S, DUEL_COPY and
//      MERCY_COPY switch with setLang, interpolators pick their variants, and
//      wire codes (report reasons) never translate.

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { LANGS, t, setLang, getLang, loadLang, normalizeLang, browserLang, registerTable } from '../core/i18n.js';
import EN from '../i18n/en.js';
import { englishEntries } from './gen-i18n-en.js';

let n = 0;
let failures = 0;
function ok(cond, what, detail) {
  n++;
  if (!cond) { failures++; console.error('FAIL: ' + what + (detail !== undefined ? ' (' + detail + ')' : '')); }
}

const here = path.dirname(fileURLToPath(import.meta.url));
const i18nDir = path.join(here, '..', 'i18n');
const holes = (s) => (String(s).match(/\{(\w+)\}/g) || []).sort();

// ------------------------------------------------ 1. en.js matches the decks
{
  const entries = englishEntries();
  const fromDecks = Object.fromEntries(entries);
  ok(entries.length === Object.keys(fromDecks).length, 'the decks produce no duplicate keys');
  const enKeys = Object.keys(EN);
  const missing = entries.map(([k]) => k).filter((k) => !(k in EN));
  const extra = enKeys.filter((k) => !(k in fromDecks));
  const changed = entries.filter(([k, v]) => k in EN && EN[k] !== v).map(([k]) => k);
  ok(missing.length === 0, 'en.js has every deck key - run node test/gen-i18n-en.js', missing.slice(0, 8).join(', '));
  ok(extra.length === 0, 'en.js carries no key the decks dropped - run node test/gen-i18n-en.js', extra.slice(0, 8).join(', '));
  ok(changed.length === 0, 'en.js text equals the decks - run node test/gen-i18n-en.js', changed.slice(0, 8).join(', '));
  ok(enKeys.length > 600, 'en.js is the whole deck', enKeys.length);
  ok(enKeys.every((k) => /^gg_[A-Za-z0-9_]+$/.test(k)), 'every key is gg_ prefixed');
}

// ------------------------------------------------ 2-4. the eight translations
const tables = {};
for (const lang of LANGS) {
  const file = path.join(i18nDir, lang + '.js');
  ok(fs.existsSync(file), 'i18n/' + lang + '.js exists');
  if (!fs.existsSync(file)) continue;
  const bytes = fs.readFileSync(file);
  ok(!(bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf), lang + '.js has no BOM');
  const text = bytes.toString('utf8');
  ok(!/\u00c3[\u0080-\u00bf]|\u00e2\u20ac/.test(text), lang + '.js carries no double-encoded UTF-8');
  ok(!text.includes('—'), lang + '.js carries no em-dash');
  const mod = await import('../i18n/' + lang + '.js');
  tables[lang] = mod.default;
}

for (const lang of LANGS.filter((l) => l !== 'en')) {
  const tb = tables[lang];
  if (!tb) continue;
  const keys = Object.keys(tb);
  const missing = Object.keys(EN).filter((k) => !(k in tb));
  const extra = keys.filter((k) => !(k in EN));
  ok(missing.length === 0, lang + ' has every en key', missing.length + ' missing: ' + missing.slice(0, 6).join(', '));
  ok(extra.length === 0, lang + ' has no key en lacks', extra.slice(0, 6).join(', '));
  const empty = keys.filter((k) => typeof tb[k] !== 'string' || !tb[k].trim());
  ok(empty.length === 0, lang + ' has no empty line', empty.slice(0, 6).join(', '));
  const badHoles = Object.keys(EN).filter((k) => k in tb && holes(EN[k]).join() !== holes(tb[k]).join());
  ok(badHoles.length === 0, lang + ' keeps every {placeholder}', badHoles.slice(0, 6).join(', '));
  const untranslated = Object.keys(EN).filter((k) => k in tb && tb[k] === EN[k] && /[a-z]{4}/.test(EN[k]));
  ok(untranslated.length < Object.keys(EN).length * 0.1, lang + ' is actually translated', untranslated.length + ' lines identical to English');
}

// ------------------------------------------------ 5. t() and the language pickers
{
  setLang('en');
  ok(getLang() === 'en', 'starts in English');
  ok(t('gg_title_host') === EN.gg_title_host, 't() reads en');
  ok(t('gg_no_such_key') === 'gg_no_such_key', 'an unknown key reads as itself');
  ok(t('gg_units_min', { n: 3 }) === '3 min', 't() fills a placeholder');
  ok(t('gg_units_min') === '{n} min', 't() leaves an unfilled placeholder alone');

  ok(normalizeLang('de-AT') === 'de', 'de-AT -> de');
  ok(normalizeLang('pt') === 'pt-BR' && normalizeLang('pt_PT') === 'pt-BR' && normalizeLang('PT-br') === 'pt-BR', 'pt, pt_PT, PT-br -> pt-BR');
  ok(normalizeLang('zh-TW') === 'zh-CN' && normalizeLang('zh') === 'zh-CN', 'any Chinese -> zh-CN');
  ok(normalizeLang('it-IT') === 'en' && normalizeLang('') === 'en' && normalizeLang(null) === 'en', 'unknown, empty, null -> en');
  ok(normalizeLang('ja') === 'ja' && normalizeLang('ko-KR') === 'ko' && normalizeLang('ru-RU') === 'ru', 'ja, ko-KR, ru-RU');
  ok(browserLang({ languages: ['it-IT', 'fr-CA', 'en'] }) === 'fr', 'browserLang takes the first language we speak');
  ok(browserLang({ languages: ['en-GB', 'de'] }) === 'en', 'browserLang keeps an English first choice');
  ok(browserLang({ language: 'es-MX' }) === 'es', 'browserLang falls back to navigator.language');
  ok(browserLang({ languages: [] , language: 'xx' }) === 'en', 'browserLang with nothing we speak is en');

  // Fallback: a table that lacks a key reads English for it.
  registerTable('de', Object.assign({}, tables.de || {}, { gg_title_host: undefined }));
  setLang('de');
  ok(getLang() === 'de', 'setLang switches to a registered table');
  ok(t('gg_title_host') === EN.gg_title_host, 'a key the table lacks falls back to English');
  registerTable('de', tables.de);
  ok(t('gg_title_host') === tables.de.gg_title_host, 'the German line reads German');
  ok(setLang('it') === 'en', 'setLang to a language we do not speak is English');

  for (const lang of LANGS) {
    const got = await loadLang(lang);
    ok(got === lang, 'loadLang(' + lang + ') loads the table', got);
  }
  setLang('en');
}

// ------------------------------------------------ 6. the decks follow the language
{
  const { S, ELEMENTS } = await import('../ui/strings.js');
  const { DUEL_COPY } = await import('../ui/duel/copy.js');
  const { MERCY_COPY } = await import('../ui/mercy.js');
  setLang('en');
  ok(S.title.host === EN.gg_title_host, 'S reads English');
  ok(S.discord.agoHours(1) === '1 hour ago' && S.discord.agoHours(5) === '5 hours ago', 'plural variants pick one/other');
  ok(S.lobby.confirmed('Sam') === 'Ready - waiting for Sam' && S.lobby.confirmed('') === 'Ready - waiting for them', 'named/anon variants');
  ok(S.report.reasons.map((r) => r.code).join() === 'csam,nonconsensual,gore,illegal,other', 'report reason codes are wire codes, untouched');
  ok(S.how.bullets.length === 6, 'the six bullets');
  ok(S.hud.zenHideGlyph === '⊟', 'glyphs pass through');
  ok(JSON.stringify(Object.keys(S.title)).includes('host'), 'S is enumerable');

  setLang('fr');
  ok(S.title.host === tables.fr.gg_title_host, 'S reads French after setLang(fr)');
  ok(S.report.reasons[0].label === tables.fr.gg_report_reasons_csam_label, 'array items translate');
  ok(S.report.reasons[0].code === 'csam', 'but their code does not');
  ok(ELEMENTS[0].name === tables.fr['gg_element_' + ELEMENTS[0].id + '_name'], 'ELEMENTS translate');
  ok(DUEL_COPY.lost === tables.fr.gg_duel_lost, 'DUEL_COPY translates');
  ok(MERCY_COPY.youHeld === tables.fr.gg_mercy_youHeld, 'MERCY_COPY translates');
  ok(S.discord.agoHours(5).includes('5'), 'interpolators still interpolate in French');

  setLang('ja');
  ok(S.draft.pool(3).includes('3'), 'a Japanese plural keeps its number');
  setLang('en');
  ok(S.title.host === EN.gg_title_host, 'and back to English');
}

console.log(failures ? 'selftest-i18n: ' + (n - failures) + '/' + n + ' checks passed ' + failures + ' FAILURE(S)' : 'selftest-i18n: ' + n + '/' + n + ' checks passed');
if (failures) process.exitCode = 1;
