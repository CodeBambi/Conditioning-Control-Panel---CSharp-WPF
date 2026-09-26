/* ============================================================================
 * core/i18n.js - the Goon page's language layer.
 *
 * THE SHAPE. English stays written where it always was (ui/strings.js and the
 * two decks kept beside their features), so a copy pass is still a diff in one
 * readable file with its comments. `deck(prefix, raw)` turns such a table into
 * the SAME shape with every word behind a getter, so `S.title.host` reads the
 * current language at the moment a screen asks for it. Keys are the path:
 * `S.title.host` is `gg_title_host`.
 *
 * i18n/en.js is the flat English table, GENERATED from the decks
 * (`node test/gen-i18n-en.js`); test/selftest-i18n.js fails the moment it and
 * the decks drift apart. The other eight tables carry exactly en's keys.
 *
 * Interpolated lines are `tpl('text {name}', (a) => ({ name: a }))`. A line
 * whose wording depends on its argument (plurals, a missing name) carries
 * variants: `tpl({ one: '...', other: '...' }, args, pick)`, one key each
 * (`..._one`, `..._other`). A translation may say the same thing in both.
 *
 * Never translated: strings with no letter in them (glyphs, icons, '-') and
 * fields named in NEVER (wire codes, ids, asset names).
 *
 * Import-safe under node: no DOM, no side effects, English until told.
 * ==========================================================================*/

import EN from '../i18n/en.js';

/** The nine app languages, in AppSettings.Language's own spelling. */
export const LANGS = Object.freeze(['en', 'de', 'es', 'fr', 'ja', 'ko', 'pt-BR', 'ru', 'zh-CN']);

const TABLES = { en: EN };
let current = 'en';

/** Fields that are data, not copy, wherever they appear in a deck. */
const NEVER = new Set(['code', 'id', 'img', 'rail', 'tint', 'icon', 'glyph']);
const HAS_LETTER = /\p{L}/u;

/**
 * Any language tag -> one of LANGS, or 'en'. "pt", "pt-PT", "pt_br" -> "pt-BR";
 * any Chinese -> "zh-CN"; "de-AT" -> "de".
 */
export function normalizeLang(raw) {
  const s = String(raw || '').trim().replace(/_/g, '-').toLowerCase();
  if (!s) return 'en';
  for (const l of LANGS) if (l.toLowerCase() === s) return l;
  const base = s.split('-')[0];
  if (base === 'pt') return 'pt-BR';
  if (base === 'zh') return 'zh-CN';
  return LANGS.includes(base) ? base : 'en';
}

/** A plain browser's own preference, mapped; 'en' when there is no browser. */
export function browserLang(nav) {
  const n = nav || (typeof navigator !== 'undefined' ? navigator : null);
  if (!n) return 'en';
  const list = Array.isArray(n.languages) && n.languages.length ? n.languages : [n.language];
  for (const tag of list) {
    const l = normalizeLang(tag);
    if (l !== 'en' || /^en\b/i.test(String(tag || ''))) return l;
  }
  return 'en';
}

/** Hand a table in (tests, or a bundle that ships its own). */
export function registerTable(code, table) {
  const l = normalizeLang(code);
  if (table && typeof table === 'object') TABLES[l] = table;
  return l;
}

/** Switch to a language whose table is already here. Returns the language in force. */
export function setLang(code) {
  const l = normalizeLang(code);
  current = TABLES[l] ? l : 'en';
  return current;
}

export function getLang() { return current; }

/**
 * Fetch a table and switch to it. Never throws: a table that will not load
 * leaves the page in English, which is a working page.
 */
export async function loadLang(code) {
  const l = normalizeLang(code);
  if (!TABLES[l]) {
    try {
      const mod = await import('../i18n/' + l + '.js');
      if (mod && mod.default) TABLES[l] = mod.default;
    } catch (_e) { /* English it is */ }
  }
  return setLang(l);
}

function fill(s, vars) {
  if (!vars) return s;
  return s.replace(/\{(\w+)\}/g, (m, name) => (vars[name] === undefined || vars[name] === null ? m : String(vars[name])));
}

/** One line in the current language, English where the table has no such key. */
export function t(key, vars) {
  const cur = TABLES[current];
  let s = cur && typeof cur[key] === 'string' ? cur[key] : EN[key];
  if (typeof s !== 'string') s = key;
  return fill(s, vars);
}

/* ------------------------------------------------------------------ decks */

const TPL = Symbol('gg-tpl');

/**
 * An interpolated line. `text` is a string with {placeholders}, or an object
 * of variants picked by `pick(...args)`. `args(...args)` names the values.
 */
export function tpl(text, args, pick) {
  return { [TPL]: true, text, args: args || (() => ({})), pick: pick || null };
}

/** The usual plural pick: 1 is 'one', anything else is 'other'. */
export const one = (n) => ((n | 0) === 1 || n === 1 ? 'one' : 'other');

const isTpl = (v) => !!(v && typeof v === 'object' && v[TPL]);
const isPlain = (v) => !!(v && typeof v === 'object' && !Array.isArray(v) && !isTpl(v));
const words = (field, v) => typeof v === 'string' && !NEVER.has(field) && HAS_LETTER.test(v);

function itemSeg(item, i) {
  if (item && typeof item === 'object') {
    if (item.code !== undefined) return String(item.code);
    if (item.id !== undefined) return String(item.id);
  }
  return String(i);
}

function render(node, key, a) {
  const vars = node.args(...a);
  if (typeof node.text === 'string') return t(key, vars);
  const v = node.pick ? node.pick(...a) : 'other';
  return t(key + '_' + (Object.prototype.hasOwnProperty.call(node.text, v) ? v : 'other'), vars);
}

function localizeObject(raw, key) {
  const out = {};
  for (const field of Object.keys(raw)) {
    const v = raw[field];
    const k = key + '_' + field;
    if (words(field, v)) {
      Object.defineProperty(out, field, { enumerable: true, get: () => t(k) });
    } else if (isTpl(v)) {
      out[field] = (...a) => render(v, k, a);
    } else if (isPlain(v)) {
      out[field] = localizeObject(v, k);
    } else if (Array.isArray(v)) {
      const items = v.map((item, i) => {
        const ik = k + '_' + itemSeg(item, i);
        if (words('', item)) return { key: ik };
        if (isPlain(item)) return { obj: localizeObject(item, ik) };
        return { raw: item };
      });
      Object.defineProperty(out, field, {
        enumerable: true,
        get: () => Object.freeze(items.map((it) => (it.key ? t(it.key) : (it.obj || it.raw)))),
      });
    } else {
      out[field] = v;
    }
  }
  return Object.freeze(out);
}

/** The table, with every word read in the current language on access. */
export function deck(prefix, raw) {
  if (Array.isArray(raw)) {
    return Object.freeze(raw.map((item, i) => (isPlain(item)
      ? localizeObject(item, prefix + '_' + itemSeg(item, i)) : item)));
  }
  return localizeObject(raw, prefix);
}

/** Every English line a deck holds, as [key, text] pairs, in deck order. */
export function flattenDeck(prefix, raw, out = []) {
  const walk = (node, key) => {
    for (const field of Object.keys(node)) {
      const v = node[field];
      const k = key + '_' + field;
      if (words(field, v)) out.push([k, v]);
      else if (isTpl(v)) {
        if (typeof v.text === 'string') out.push([k, v.text]);
        else for (const variant of Object.keys(v.text)) out.push([k + '_' + variant, v.text[variant]]);
      } else if (isPlain(v)) walk(v, k);
      else if (Array.isArray(v)) {
        v.forEach((item, i) => {
          const ik = k + '_' + itemSeg(item, i);
          if (words('', item)) out.push([ik, item]);
          else if (isPlain(item)) walk(item, ik);
        });
      }
    }
  };
  if (Array.isArray(raw)) raw.forEach((item, i) => { if (isPlain(item)) walk(item, prefix + '_' + itemSeg(item, i)); });
  else walk(raw, prefix);
  return out;
}
