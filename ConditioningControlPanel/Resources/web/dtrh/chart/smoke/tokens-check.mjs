/* ============================================================================
 * chart/smoke/tokens-check.mjs - the house rules, checked without a browser.
 *
 *   node chart/smoke/tokens-check.mjs     (from Resources/web/dtrh; 0 on pass, 1 on any failure)
 *
 * Three rules that are easier to enforce than to remember: no long dash
 * anywhere, never the word "bank" or "banked" in anything a reader sees, and
 * nothing on the Track Maker drawn under 13 px (it is the one screen an author
 * reads at a glance while the audio runs). Plus the shape of the page itself:
 * the maker is one screen, so no tabs and no settings panel.
 *
 * CARVE-OUT: trimmed from the Track Maker chain's tokens-check.mjs. The chart
 * room half (editor.css tokens, editor.html, the editor's own layout rules) is
 * not on this branch, so those checks are not either. Every path below is
 * optional: the maker page and MAKER.md land in later PRs, and a file that is
 * not here yet is skipped, not failed.
 * ==========================================================================*/

import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const CHART = join(dirname(fileURLToPath(import.meta.url)), '..');

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const rel = (f) => f.slice(Math.max(0, f.indexOf('dtrh'))).replace(/\\/g, '/');
const read = (f) => readFileSync(f, 'utf8');

/* Every file this branch actually carries, out of the set the maker will own. */
const walk = (dir, out = []) => {
  if (!existsSync(dir)) return out;
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (/\.(js|mjs|css|html|md)$/.test(name)) out.push(p);
  }
  return out;
};
const only = (dir, re) => walk(dir).filter((f) => re.test(f));
const FILES = [
  ...walk(join(CHART, 'maker')),
  ...only(join(CHART, 'editor'), /\.js$/),
  ...only(join(CHART, 'smoke'), /\.mjs$/),
  join(CHART, 'maker.html'), join(CHART, 'maker.css'), join(CHART, 'MAKER.md'),
].filter((f) => existsSync(f));
ok(FILES.length > 0, 'there is something to scan (' + FILES.length + ' files)');

/* ---- 1. no long dash, no bank talk --------------------------------------- */
const LONG_DASH = String.fromCharCode(0x2014);
const RULE_LINE = /no bank|"bank|'bank|\\bbank/;      // a line that quotes the rule, not copy that breaks it
const dashes = [], banks = [];
for (const f of FILES) {
  read(f).split('\n').forEach((line, i) => {
    if (line.includes(LONG_DASH)) dashes.push(rel(f) + ':' + (i + 1));
    if (/\bbanke?d?\b/i.test(line) && !RULE_LINE.test(line)) banks.push(rel(f) + ':' + (i + 1) + ' ' + line.trim().slice(0, 60));
  });
}
ok(dashes.length === 0, 'no long dash anywhere' + (dashes.length ? ' (' + dashes.slice(0, 5).join(', ') + ')' : ''));
ok(banks.length === 0, 'no bank talk anywhere' + (banks.length ? ' (' + banks.slice(0, 5).join(' / ') + ')' : ''));

/* ---- 2. nothing is drawn under 13 px ------------------------------------- */
const sheets = FILES.filter((f) => f.endsWith('.css'));
const small = [];
for (const f of sheets) {
  for (const m of read(f).matchAll(/font(?:-size)?:\s*([^;}]+)/g)) {
    for (const px of m[1].matchAll(/(\d+)px/g)) if (Number(px[1]) < 13) small.push(rel(f) + ' ' + m[0].trim());
  }
}
ok(small.length === 0, sheets.length
  ? 'no font size under 13 px' + (small.length ? ' (' + small.slice(0, 4).join(' | ') + ')' : '')
  : 'no font size under 13 px (no stylesheet on this branch yet)');

/* ---- 3. the maker stays one screen --------------------------------------- */
const HTML = join(CHART, 'maker.html');
if (!existsSync(HTML)) console.log('  ok  the maker is one screen: no tabs, no settings panel (maker.html lands in a later PR)');
else ok(!/<nav|role="tab"|id="settings"/.test(read(HTML)), 'the maker is one screen: no tabs, no settings panel');


/* ---- 4. the maker's own sheet keeps its shape (PR M4) --------------------- */
/* maker.css declares every token it spends, and the top bar stays one row: a
   long track name is cut with an ellipsis rather than wrapping the bar in two. */
const MCSS = join(CHART, 'maker.css');
if (!existsSync(MCSS)) console.log('  ok  maker.css keeps its shape (it lands in a later PR)');
else {
  const css = read(MCSS);
  const root = (css.match(/:root\s*\{([\s\S]*?)\n\}/) || [, ''])[1];
  const TOKENS = ['ink', 'panel', 'panel-2', 'line', 'text', 'dim', 'pink', 'violet', 'mint', 'gold',
    'sub', 'flash', 'drain', 'wall', 'display', 'body', 'gutter', 'sp'];
  const miss = TOKENS.filter((t) => !new RegExp('--' + t + '\\s*:').test(root));
  ok(miss.length === 0, 'maker.css declares every token it uses' + (miss.length ? ' (missing: ' + miss.join(', ') + ')' : ''));
  const top = (css.match(/#top\s*\{([^}]*)\}/) || [, ''])[1];
  ok(/flex-wrap:\s*nowrap/.test(top), 'the maker top bar stays one row, whatever the track is called');
  ok(/#top\s+\.name\s*\{[^}]*text-overflow:\s*ellipsis/.test(css), 'a long track name is cut, not wrapped');
}

console.log(fails ? '\ntokens-check: ' + fails + ' failed' : '\ntokens-check: all good');
process.exit(fails ? 1 : 0);
