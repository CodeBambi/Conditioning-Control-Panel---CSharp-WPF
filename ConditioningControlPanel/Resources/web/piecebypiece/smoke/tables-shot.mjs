/* ============================================================================
 * smoke/tables-shot.mjs - photograph the Open tables panel on the mock lobby
 * at a desk, a phone upright and a phone on its side, and check the page threw
 * nothing on the way. One browser at a time, as shoot.mjs insists.
 *
 *   node smoke/tables-shot.mjs [--out DIR] [--port 8824]
 *
 * Each shot prints what the panel holds and whether its list scrolls, so a
 * run log says more than the pictures do.
 * ==========================================================================*/

import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const argv = process.argv.slice(2);
const arg = (name, dflt) => { const i = argv.indexOf('--' + name); return i >= 0 && argv[i + 1] ? argv[i + 1] : dflt; };
const here = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(here, '..', '..');
const out = arg('out', path.join(here, 'out', 'tables'));
const port = Number(arg('port', '8824'));
mkdirSync(out, { recursive: true });

const server = spawn('python', ['-m', 'http.server', String(port), '--bind', '127.0.0.1'], { cwd: webRoot, stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 1200));

/** What the panel shows, read off the DOM. */
const READ = `(() => { const sc = document.querySelector('.tables-scroll');
  return JSON.stringify({ state: window.PBP.door.debug.state(),
    rows: document.querySelectorAll('.table-row').length, games: document.querySelectorAll('.playing-row').length,
    scrolls: !!sc && sc.scrollHeight > sc.clientHeight + 2,
    fits: document.querySelector('.door-card').getBoundingClientRect().right <= innerWidth }); })()`;

const HOST = `(async () => { window.PBP.door.debug.act('host'); await new Promise((r) => setTimeout(r, 300)); return ${READ}; })()`;

const SHOTS = [
  ['tables-1920x1080', '1920,1080', READ],
  ['tables-390x844', '390,844', READ],
  ['tables-844x390', '844,390', READ],
  ['tables-hosting-1920x1080', '1920,1080', HOST],
];

let failed = 0;
for (const [name, size, evalExpr] of SHOTS) {
  const url = `http://127.0.0.1:${port}/piecebypiece/index.html?lobby=mock&seed=7&media=none&screen=lobby`;
  const file = path.join(out, name + '.png');
  const r = spawnSync('node', [path.join(here, 'shot.mjs'), '--url', url, '--out', file, '--wait', '5000', '--eval', evalExpr, '--after', '900', '--size', size, '--viewport', size, '--port', '9310'], { encoding: 'utf8' });
  const lines = (r.stdout || '').trim().split('\n').filter((l) => /screenshot|eval|ERROR|clean|error/i.test(l));
  console.log(`[${name}] ${lines.join(' | ')}`);
  if (r.status !== 0) { failed++; console.log((r.stdout || '') + (r.stderr || '')); }
}
server.kill();
console.log(failed ? `${failed} shot(s) had errors` : 'every shot clean');
process.exit(failed ? 1 : 0);
