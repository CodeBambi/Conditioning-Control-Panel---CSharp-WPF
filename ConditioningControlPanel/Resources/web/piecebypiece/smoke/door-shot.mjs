/* ============================================================================
 * smoke/door-shot.mjs - photograph every screen of the front door.
 *
 * Serves Resources/web itself (python -m http.server), then drives shot.mjs
 * once per screen with --eval levers on window.PBP.door.debug, so a human can
 * see the menu, the lobby, an incoming challenge, the match-found beat, the
 * shelf, a replay, the profile and the end card without a host. ONE browser
 * at a time, as shoot.mjs insists.
 *
 * The last five are the online seat's furniture and the host's states, which
 * a plain browser cannot reach on its own: an online game is dealt against a
 * fake transport planted through net/api.js's setTransport (the same seam
 * online-smoke.mjs uses), so the resign / draw buttons and his standing offer
 * can be photographed, and the hotseat shot right after proves the block is
 * absent there; door.debug.host stands in for the desktop for the signed-out
 * lobby and the read-only profile name.
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

/**
 * An online seat with a server behind it that is really a closure: GET match
 * answers a live 10+0 game with us as white, the events long poll answers
 * empty after a real wait (so the pump does not spin), and every verb is a
 * 200. DRAW is 'b' to have his offer standing in the state, else null.
 */
const MOCK_NET = (draw, then = '') => `(async () => {
  const a = await import('./net/api.js');
  const now = Date.now();
  const calls = [];
  const state = { ok: true, id: 'm_shot', you: 'w',
    white: { id: 'p_me', display_name: 'you', rating: 1200, self: true, online: true },
    black: { id: 'p_velvet', display_name: 'velvet', rating: 1301, self: false, online: true },
    time_control: { initial_ms: 600000, increment_ms: 0 },
    fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', moves: [],
    clocks: { w_ms: 600000, b_ms: 600000, turn: 'w', server_now_ms: now, turn_started_ms: now },
    status: 'live', result: null, draw_offer: ${JSON.stringify(draw)}, seq: 0, started_ms: now };
  a.setTransport(async (m, p) => {
    calls.push(m + ' ' + p.split('?')[0]);
    if (p.includes('/events')) {
      await new Promise((r) => setTimeout(r, 2000));
      return { status: 200, body: JSON.stringify({ ok: true, seq: 0, server_now_ms: Date.now(), status: 'live', events: [], opponent_online: true }) };
    }
    if (p.includes('/match/')) return { status: 200, body: JSON.stringify(state) };
    return { status: 200, body: '{"ok":true}' };
  });
  window.PBP.door.debug.matched({ id: 'm_shot', opponent: { id: 'p_velvet', name: 'velvet' }, side: 'w', clockMs: 600000, state });
  window.PBP.door.debug.act('go');
  await new Promise((r) => setTimeout(r, 400));
  ${then}
  const posts = calls.filter((c) => c.startsWith('POST')).map((c) => c.split('/').pop());
  return JSON.stringify({ online: window.PBP.game.isOnline, hud: window.PBP.board.hud.debug().online, posts });
})()`;

/** One tap on each: the draw goes out, the resign only asks. The posts in the log are the proof. */
const TAP_DRAW_THEN_RESIGN = `document.getElementById('hud-draw').click(); document.getElementById('hud-resign').click(); await new Promise((r) => setTimeout(r, 100));`;

/** A desktop host, as far as the door can tell: hosted, and either signed in as Bambi or signed out. */
const FAKE_HOST = (signedIn) => `window.PBP.door.debug.host({ isHosted: true,
  identity: () => (${signedIn ? "{ unifiedId: 'u_shot0000', displayName: 'Bambi', appVersion: '0', online: true }" : "{ unifiedId: '', displayName: '', appVersion: '0', online: false }"}),
  whenIdentity: async () => (${signedIn ? "{ unifiedId: 'u_shot0000', displayName: 'Bambi', appVersion: '0', online: true }" : "{ unifiedId: '', displayName: '', appVersion: '0', online: false }"}) })`;

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
  // the online seat's furniture: resign / offer draw, then his offer standing
  ['11-online-hud', 'menu', MOCK_NET(null), '1200'],
  ['12-online-draw-ask', 'menu', MOCK_NET('b'), '1200'],
  // and the same corner in a hotseat game, where there is nobody to say it to
  ['13-hotseat-no-online', 'menu', `(async () => { window.PBP.door.debug.act('hotseat'); await new Promise((r) => setTimeout(r, 300)); return JSON.stringify({ online: window.PBP.game.isOnline, hud: window.PBP.board.hud.debug().online }); })()`, '1200'],
  // hosted and signed out: one line, quick match off, never the mock's room
  ['14-lobby-signed-out', 'menu', `(async () => { ${FAKE_HOST(false)}; window.PBP.door.show('lobby'); await new Promise((r) => setTimeout(r, 300)); return window.PBP.door.debug.state(); })()`],
  // hosted and signed in: the account names the player, so the name is shown, not typed
  ['15-profile-hosted', 'menu', `(async () => { ${FAKE_HOST(true)}; window.PBP.settings.playerName = 'Bambi'; ${SEED_SHELF}; window.PBP.door.show('profile'); await new Promise((r) => setTimeout(r, 200)); return { readonly: !!document.querySelector('.door-name input[readonly]'), name: document.querySelector('.door-name input').value }; })()`],
  // one tap on offer draw and one on resign: "draw offered" stands, "resign?" is asked, and posts holds a draw and NO resign
  ['16-online-resign-ask', 'menu', MOCK_NET(null, TAP_DRAW_THEN_RESIGN), '1200'],
];

let failed = 0;
for (const [name, screen, evalExpr, afterMs] of SHOTS) {
  const url = `http://127.0.0.1:${port}/piecebypiece/index.html?lobby=mock&seed=7&media=none&screen=${screen}`;
  const file = path.join(out, name + '.png');
  const after = afterMs || (name.startsWith('09') || name.startsWith('10') ? '2600' : '900');
  const r = spawnSync('node', [path.join(here, 'shot.mjs'), '--url', url, '--out', file, '--wait', '5000', '--eval', evalExpr, '--after', after, '--port', String(9300)], { encoding: 'utf8' });
  const lines = (r.stdout || '').trim().split('\n').filter((l) => /screenshot|eval|ERROR|clean/.test(l));
  console.log(`[${name}] ${lines.join(' | ')}`);
  if (r.status !== 0) { failed++; console.log((r.stdout || '') + (r.stderr || '')); }
}
server.kill();
console.log(failed ? `${failed} screen(s) had errors` : 'every screen clean');
process.exit(failed ? 1 : 0);
