/* ============================================================================
 * smoke/door-shot.mjs - photograph every screen of the front door.
 *
 * Serves Resources/web itself (python -m http.server), then drives shot.mjs
 * once per screen with --eval levers on window.PBP.door.debug, so a human can
 * see the menu, the lobby, an incoming challenge, the match-found beat, the
 * shelf, a replay, the profile and the end card without a host. ONE browser
 * at a time, as shoot.mjs insists.
 *
 *   node smoke/door-shot.mjs [--out DIR] [--port 8823]
 * ==========================================================================*/

import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const argv = process.argv.slice(2);
const arg = (name, dflt) => { const i = argv.indexOf('--' + name); return i >= 0 && argv[i + 1] ? argv[i + 1] : dflt; };
const here = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(here, '..', '..');
const out = arg('out', path.join(here, 'out', 'door'));
const port = Number(arg('port', '8823'));
mkdirSync(out, { recursive: true });

const server = spawn('python', ['-m', 'http.server', String(port), '--bind', '127.0.0.1'], { cwd: webRoot, stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 1200));

// a shelf with three games on it, so the shelf, the replay and the profile have something to show
const SEED_SHELF = `(() => { const d = window.PBP.door.debug.shelf; window.localStorage.removeItem('pbp-games');
  d.save({ id: 'g1', at: new Date(Date.now() - 3600e3).toISOString(), mode: 'online', me: 'w', opponent: 'velvet', moves: ['e4','e5','Qh5','Nc6','Bc4','Nf6','Qxf7#'], plies: 7, result: { result: 'checkmate', winner: 'w', reason: 'checkmate' }, durationMs: 252e3, captures: { w: 1, b: 0 } });
  d.save({ id: 'g2', at: new Date(Date.now() - 86400e3).toISOString(), mode: 'online', me: 'b', opponent: 'moth', moves: ['d4','d5','c4','e6','Nc3','Nf6','Bg5','Be7','e3','O-O','Nf3','h6','Bh4','b6'], plies: 14, result: { result: 'flag', winner: 'w', reason: 'flag' }, durationMs: 900e3, captures: { w: 0, b: 0 } });
  d.save({ id: 'g3', at: new Date(Date.now() - 2 * 86400e3).toISOString(), mode: 'hotseat', me: null, opponent: 'a friend here', moves: ['e4','c5','Nf3','d6','d4','cxd4','Nxd4','Nf6','Nc3','a6'], plies: 10, result: { result: 'draw', winner: null, reason: 'agreement' }, durationMs: 480e3, captures: { w: 1, b: 1 } });
  return 'shelf seeded'; })()`;

const SHOTS = [
  ['01-menu', 'menu', 'window.PBP.door.debug.state()'],
  ['02-lobby', 'lobby', 'window.PBP.door.debug.state()'],
  ['03-lobby-looking', 'lobby', `(() => { window.PBP.door.debug.act('quick'); return 'looking'; })()`],
  ['04-lobby-ask', 'lobby', `(() => { window.PBP.lobby.debug.askMe('moth'); return 'asked'; })()`],
  ['05-found', 'lobby', `(() => { window.PBP.door.debug.act('quick'); setTimeout(() => window.PBP.lobby.debug.matchNow(), 200); return 'matching'; })()`],
  ['06-games', 'games', `${SEED_SHELF}; window.PBP.door.show('games'); 'shelf'`],
  ['07-replay', 'games', `${SEED_SHELF}; window.PBP.door.debug.openReplay('g1'); window.PBP.door.debug.act('rnext'); window.PBP.door.debug.act('rnext'); window.PBP.door.debug.act('rnext'); 'replay'`],
  ['08-profile', 'profile', `${SEED_SHELF}; window.PBP.door.show('profile'); 'profile'`],
  ['09-end', 'menu', `(() => { window.PBP.door.debug.act('hotseat'); for (const m of [['e2','e4'],['e7','e5'],['d1','h5'],['b8','c6'],['f1','c4'],['g8','f6'],['h5','f7']]) window.PBP.game.tryMove(m[0], m[1]); return 'mated'; })()`],
  ['10-end-online-loss', 'menu', `(() => { window.PBP.door.debug.matched({ id: 'gx', opponent: { id: 'm-velvet', name: 'velvet' }, side: 'b', clockMs: 900000 }); window.PBP.door.debug.act('go'); for (const m of [['e2','e4'],['e7','e5'],['d1','h5'],['b8','c6'],['f1','c4'],['g8','f6'],['h5','f7']]) window.PBP.game.tryMove(m[0], m[1]); return 'lost as black'; })()`],
];

let failed = 0;
for (const [name, screen, evalExpr] of SHOTS) {
  const url = `http://127.0.0.1:${port}/piecebypiece/index.html?lobby=mock&seed=7&media=none&screen=${screen}`;
  const file = path.join(out, name + '.png');
  const after = name.startsWith('09') || name.startsWith('10') ? '2600' : '900';
  const r = spawnSync('node', [path.join(here, 'shot.mjs'), '--url', url, '--out', file, '--wait', '5000', '--eval', evalExpr, '--after', after, '--port', String(9300)], { encoding: 'utf8' });
  const lines = (r.stdout || '').trim().split('\n').filter((l) => /screenshot|eval|ERROR|clean/.test(l));
  console.log(`[${name}] ${lines.join(' | ')}`);
  if (r.status !== 0) { failed++; console.log((r.stdout || '') + (r.stderr || '')); }
}
server.kill();
console.log(failed ? `${failed} screen(s) had errors` : 'every screen clean');
process.exit(failed ? 1 : 0);
