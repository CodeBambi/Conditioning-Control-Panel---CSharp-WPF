/* ============================================================================
 * race/smoke/pixel-default-check.mjs - headless self-check for THE PIXEL
 * DEFAULT (race/pixel.js pixelDefault + race/menu.js loadOptions), driven
 * through the REAL page.
 *
 *   node race/smoke/pixel-default-check.mjs      (exits 0 on pass, 1 with a count)
 *
 * Why it exists: the big-pixel look used to start at 3 for everybody, which on a
 * phone panel is mush (the owner, phone testing 2026-09-09). Now a COARSE
 * pointer starts OFF and a fine one starts at the smallest block, and the old
 * fixed default that every player already has written into `race.options` is
 * read as "never chosen" so they get the new one too.
 *
 * Headless Chrome has a FINE pointer, so `?coarse=1` / `?coarse=0` force the
 * answer (race/pixel.js pixelDefault, the way race/touch.js takes `?touch=`).
 *
 * What it holds:
 *   1. a fresh page on a mouse starts at the smallest non-zero step
 *   2. a fresh page on glass starts with the look off
 *   3. the migration: a saved 3 (the old default) is not a choice, so both
 *      devices get their new default instead
 *   4. a value the player actually picked is kept, on both devices
 *   5. `?pixel=N` still beats all of it
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install. Nothing is
 * installed by this file and the profile it makes is deleted on the way out.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const WEB = resolve(HERE, '../../..');          // Resources/web
const PORT = 8869;
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});

if (!existsSync(CHROME)) {
  console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)');
  process.exit(1);
}
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-pixel-'));
const chrome = spawn(CHROME, [
  '--headless=new', '--remote-debugging-port=9341', `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=900,700', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch('http://127.0.0.1:9341/json/list')).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map();
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));

const site = `http://127.0.0.1:${PORT}`;
const url = (q) => `${site}/dtrh/race.html?intro=0&cards=0&${q}`;

async function bootAt(u) {
  await cdp('Page.navigate', { url: u });
  for (let i = 0; i < 80; i++) {
    await sleep(250);
    const up = await ev(`!!(window.__race && window.__race.menu && window.__race.race)`);
    if (up) { await sleep(200); return true; }
  }
  return false;
}
/** What the run and the option row agree the block is. */
const block = () => json(`({ pixel: window.__race.race.pixel.block, option: window.__race.menu.options.pixel,
  row: [...document.querySelectorAll('.rm-options .rm-row')].filter(r=>r.dataset.id==='pixel').map(r=>r.querySelector('.rm-row-val').textContent)[0] })`);
/** Wipe the saved options, or write one, then come back on the given device. */
async function withOptions(saved, q) {
  await bootAt(url('coarse=0'));
  await ev(saved === null
    ? `localStorage.removeItem('race.options'), 1`
    : `localStorage.setItem('race.options', ${JSON.stringify(JSON.stringify(saved))}), 1`);
  return bootAt(url(q));
}

await cdp('Runtime.enable'); await cdp('Page.enable');

/* ---- 1. a fresh page on a mouse ----------------------------------------- */
ok(await withOptions(null, 'coarse=0'), 'the page boots with nothing saved, on a fine pointer');
{
  const b = await block();
  ok(b.pixel === 2, `a mouse starts at the smallest block (${b.pixel})`);
  ok(b.option === 2 && b.row === '2 px', `and the option row says so: "${b.row}"`);
}

/* ---- 2. a fresh page on glass -------------------------------------------- */
ok(await withOptions(null, 'coarse=1'), 'the page boots with nothing saved, on a coarse pointer');
{
  const b = await block();
  ok(b.pixel === 0, `glass starts with the look OFF (${b.pixel})`);
  ok(b.option === 0 && b.row === 'off', `and the option row reads "${b.row}"`);
}

/* ---- 3. the migration: the old default is not a choice -------------------- */
const OLD = { pixel: 3, music: 0.8, sfx: 0.8, motion: 'system', seed: 'daily', seedValue: 7 };
ok(await withOptions(OLD, 'coarse=1'), 'a player carrying the old default (3) opens the race on glass');
ok((await block()).pixel === 0, 'and gets the new default, off, rather than the 3 nobody picked');
ok(await withOptions(OLD, 'coarse=0'), 'the same saved 3 on a mouse');
ok((await block()).pixel === 2, 'takes the smallest block, the new default there');

/* ---- 4. a value the player actually picked is theirs ---------------------- */
const PICKED = { ...OLD, pixel: 6 };
ok(await withOptions(PICKED, 'coarse=1'), 'a player who chose 6 opens the race on glass');
ok((await block()).pixel === 6, 'and keeps their 6');
ok(await withOptions(PICKED, 'coarse=0'), 'and on a mouse');
ok((await block()).pixel === 6, 'they keep it there too');

/* ---- 5. ?pixel still beats everything ------------------------------------- */
ok(await withOptions(null, 'coarse=1&pixel=4'), '?pixel=4 on glass, with nothing saved');
ok((await block()).pixel === 4, 'the query string still wins over the device default');

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\npixel-default-check: all good');
  process.exit(code);
}
