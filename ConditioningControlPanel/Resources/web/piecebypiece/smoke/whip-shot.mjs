/* ============================================================================
 * smoke/whip-shot.mjs - proof that the bishop takes the long way.
 *
 * Loads the board in headless msedge with a bishop able to take on d5, takes
 * the frame loop over (the same rAF hand-over feel-shot uses), drags Bc4xd5
 * and steps the clock through the whip, shooting the frames that matter: the
 * stand-off landing, the crack, the ring, the bishop on the square. Every
 * `land`, `hit` and `sunk` the bus carries is logged with its time; the sound
 * module reports its cues. Then the same capture again, skipped by a tap
 * mid-whip, which has to land everything at once.
 *
 *   node smoke/whip-shot.mjs [--url <page url>] [--out <dir>] [--wait <ms>]
 *
 * Needs a static server on the web root, for example:
 *   python -m http.server 8853   (run from Resources/web)
 * Exits non-zero when the page logged an error, when the whip did not crack,
 * or when the bishop did not end up on the square with the victim gone.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const EDGE = process.env.PBP_EDGE
  || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';

function arg(name, fallback) {
  const i = process.argv.indexOf('--' + name);
  return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const url = arg('url', 'http://localhost:8853/piecebypiece/index.html?hotseat=1');
const outDir = arg('out', join(tmpdir(), 'pbp-whip-shot'));
const waitMs = Number(arg('wait', '7000'));
const port = Number(arg('port', '9263'));
// white to move, Bc4 takes the pawn on d5 (no check, so the buzz stays out of it)
const FEN = 'rnbqkbnr/ppp1pppp/8/3p4/2B1P3/8/PPPP1PPP/RNBQK1NR w KQkq - 0 2';

const profile = join(tmpdir(), 'pbp-edge-whip-' + Date.now());
const edge = spawn(EDGE, [
  '--headless=new', '--remote-debugging-port=' + port, '--user-data-dir=' + profile,
  '--window-size=1280,860', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check',
  '--disable-extensions', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
  '--autoplay-policy=no-user-gesture-required',
], { stdio: 'ignore' });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function target() {
  for (let i = 0; i < 60; i++) {
    try {
      const res = await fetch('http://127.0.0.1:' + port + '/json/list');
      const list = await res.json();
      const page = list.find((t) => t.type === 'page');
      if (page) return page.webSocketDebuggerUrl;
    } catch { /* not up yet */ }
    await sleep(250);
  }
  throw new Error('edge never exposed a page');
}

function client(ws) {
  let id = 0;
  const pending = new Map();
  const events = [];
  ws.addEventListener('message', (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) { pending.get(msg.id)(msg.result); pending.delete(msg.id); }
    else if (msg.method) events.push(msg);
  });
  return {
    events,
    send(method, params = {}) {
      const n = ++id;
      ws.send(JSON.stringify({ id: n, method, params }));
      return new Promise((res) => pending.set(n, res));
    },
  };
}

let cdp = null;
let failed = false;
const bad = (what) => { console.log('ERROR ' + what); failed = true; };

async function evalJs(expression) {
  const r = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result?.value;
}

async function shot(name, clip = null) {
  const s = await cdp.send('Page.captureScreenshot', clip ? { format: 'png', clip } : { format: 'png' });
  const path = join(outDir, name);
  writeFileSync(path, Buffer.from(s.data, 'base64'));
  return path;
}

const HIJACK = "window.__pbp = null; (async () => {"
  + " let q = [];"
  + " window.requestAnimationFrame = (fn) => { q.push(fn); return q.length; };"
  + " window.cancelAnimationFrame = () => {};"
  + " await new Promise((r) => setTimeout(r, 400));"
  + " let t = performance.now();"
  + " window.__pbp = { t: () => t, step(ms, n) { for (let i = 0; i < n; i++) { t += ms; const c = q; q = []; for (const fn of c) fn(t); } return q.length; } };"
  + " return q.length; })()";
const beat = (ms) => evalJs(`window.__pbp.step(16.667, ${Math.max(1, Math.round(ms / 16.667))})`);

const at = async (sq, height) => JSON.parse(await evalJs(
  `JSON.stringify(window.PBP.board.projectSquare('${sq}', ${height}))`));
async function mouse(type, x, y) {
  return cdp.send('Input.dispatchMouseEvent', {
    type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, clickCount: 1, pointerType: 'mouse',
  });
}
async function closeUp(sq, height = 0.5) {
  const p = await at(sq, height);
  return { x: Math.max(0, p.x - 170), y: Math.max(0, p.y - 150), width: 340, height: 300, scale: 2 };
}

let opened = 0;
async function open(pageUrl) {
  // A fresh query each time: the same URL twice is not always a fresh page.
  await cdp.send('Page.navigate', { url: pageUrl + '&r=' + (++opened) });
  const t0 = Date.now();
  while (Date.now() - t0 < waitMs) {
    await sleep(250);
    try {
      if (await evalJs("!!(window.PBP && window.PBP.board && window.PBP.board.pieces.pieceAt('e1') && document.getElementById('loader').hidden)")) break;
    } catch { /* mid-load */ }
  }
  await sleep(2500);   // the art and the late modules
  await evalJs('window.PBP.ramp && window.PBP.ramp.setEnabled(false)');
  await evalJs('window.PBP.board.setWobble(0); window.PBP.board.setCameraSway(0)');
  await evalJs("window.__ev = []; window.__t0 = 0;"
    + " for (const k of ['land', 'hit', 'sunk']) window.PBP.bus.on(k, (p) => window.__ev.push({ k, at: window.__pbp ? window.__pbp.t() - window.__t0 : 0, p: k === 'sunk' ? { piece: p.piece, side: p.side } : p }));");
  const queued = await evalJs(HIJACK);
  if (!queued) bad('the frame loop did not hand over');
  await beat(200);
}

async function drag(from, to) {
  const a = await at(from, 0.45);
  await mouse('mousePressed', a.x, a.y);
  const held = Number(await evalJs(
    'window.PBP.board.drag && window.PBP.board.drag.holdHeight ? window.PBP.board.drag.holdHeight() : 0')) || 0;
  const b = await at(to, held);
  for (let i = 1; i <= 8; i++) {
    await mouse('mouseMoved', a.x + (b.x - a.x) * (i / 8), a.y + (b.y - a.y) * (i / 8));
    await beat(35);
  }
  await evalJs('window.__t0 = window.__pbp.t()');
  await mouse('mouseReleased', b.x, b.y);
}

const events = () => evalJs('JSON.stringify(window.__ev)').then(JSON.parse);
const stats = () => evalJs('JSON.stringify(window.PBP.board.anim.stats())').then(JSON.parse);
const men = () => evalJs(`JSON.stringify(window.PBP.board.view.pieceGroup.children.filter((c) => c.userData && c.userData.type && !c.userData.parade).map((c) => c.userData.type + c.userData.side + '@' + (c.userData.square || '?') + ':' + c.position.x.toFixed(2) + ',' + c.position.z.toFixed(2)))`).then(JSON.parse);

async function main() {
  mkdirSync(outDir, { recursive: true });
  const wsUrl = await target();
  const ws = new WebSocket(wsUrl);
  await new Promise((res, rej) => {
    ws.addEventListener('open', res, { once: true });
    ws.addEventListener('error', rej, { once: true });
  });
  cdp = client(ws);
  await cdp.send('Runtime.enable');
  await cdp.send('Log.enable');
  await cdp.send('Page.enable');

  const sep = url.includes('?') ? '&' : '?';
  const page = url + sep + 'fen=' + encodeURIComponent(FEN);

  // --- 1. the whip, frame by frame --------------------------------------------
  await open(page);
  await drag('c4', 'd5');
  await beat(220);                       // the approach is 0.20 s: the bishop is at the stand-off
  let st = await stats();
  console.log('after the approach: ' + JSON.stringify(st));
  if (!st.whips.length) bad('no whip in flight after the approach');
  console.log('  ' + await shot('whip-1-standoff.png', await closeUp('d5')));
  await beat(80);                        // 300: deep in the wind
  console.log('  ' + await shot('whip-2-wind.png', await closeUp('d5')));
  await beat(50);                        // 350: the crack has just landed, the stride begun
  st = await stats();
  console.log('at the crack: ' + JSON.stringify(st));
  console.log('  ' + await shot('whip-3-crack.png', await closeUp('d5')));
  await beat(100);                       // 450: on the square, the ring, the victim over
  console.log('  ' + await shot('whip-4-square.png', await closeUp('d5')));
  await beat(450);                       // 900: the victim rolled, the tentacle still
  console.log('  ' + await shot('whip-5-rolled.png', await closeUp('d5')) + ' ' + await shot('whip-5-board.png'));
  await beat(1200);                      // the victim has sunk and stands on the rim

  const ev = await events();
  for (const e of ev) console.log('  ' + e.k + ' @' + Math.round(e.at) + 'ms ' + JSON.stringify(e.p.stage ? { piece: e.p.piece, capture: e.p.capture, manner: e.p.manner, stage: e.p.stage } : e.p.victim ? { piece: e.p.piece, victim: e.p.victim, square: e.p.square } : e.p));
  const lands = ev.filter((e) => e.k === 'land');
  const hits = ev.filter((e) => e.k === 'hit');
  const sunk = ev.filter((e) => e.k === 'sunk');
  if (lands.length !== 2) bad('expected two landings (stand-off, square), got ' + lands.length);
  if (lands[0] && !(lands[0].p.manner === 'whip' && lands[0].p.stage === 'standoff' && !lands[0].p.capture)) bad('first landing is not the stand-off');
  if (lands[1] && !(lands[1].p.manner === 'whip' && lands[1].p.stage === 'square' && lands[1].p.capture)) bad('second landing is not the capture on the square');
  if (hits.length !== 1) bad('expected one hit, got ' + hits.length);
  if (hits[0] && !(hits[0].p.piece === 'b' && hits[0].p.victim === 'p' && hits[0].p.square === 'd5')) bad('the hit is not the bishop on the d5 pawn');
  if (hits[0] && !(hits[0].at > 300 && hits[0].at < 380)) bad('the crack is off its beat: ' + Math.round(hits[0].at) + 'ms');
  if (lands[1] && !(lands[1].at > 400 && lands[1].at <= 450)) bad('the bishop stood on the square at ' + Math.round(lands[1].at) + 'ms, outside the 440 line');
  if (sunk.length !== 1 || sunk[0].p.piece !== 'p') bad('the victim never sank');
  if (sunk[0] && !(sunk[0].at < 1600)) bad('the victim sank late: ' + Math.round(sunk[0].at) + 'ms');
  const board = await men();
  console.log('the board: ' + board.filter((m) => m.startsWith('bw') || m.includes('@d5')).join(' '));
  if (!board.some((m) => m.startsWith('bw@d5:'))) bad('the white bishop is not on d5');
  if (board.some((m) => m.startsWith('pb@d5'))) bad('the black pawn is still on d5');
  st = await stats();
  if (st.slides.length || st.whips.length || st.tumbles.length) bad('something is still in flight: ' + JSON.stringify(st));

  const sfx = await evalJs(`(() => { const s = window.PBP.board.sfx; return s ? JSON.stringify(s.log().map((e) => e.name)) : null; })()`).then((v) => (v ? JSON.parse(v) : null));
  console.log('sound: ' + JSON.stringify(sfx));
  if (sfx && !sfx.includes('whip')) bad('the crack made no sound');
  if (sfx && sfx.includes('capture')) bad('the square landing squelched on top of the crack');
  const dust = await evalJs('JSON.stringify(window.PBP.board.dust && window.PBP.board.dust.stats())').then(JSON.parse);
  console.log('dust: ' + JSON.stringify(dust));
  if (dust && dust.bursts < 3) bad('expected three bursts (stand-off, hit, square), got ' + dust.bursts);

  // --- 2. the same whip, skipped mid-wind ----------------------------------------
  await open(page);
  await drag('c4', 'd5');
  await beat(280);                       // at the stand-off, winding up
  await evalJs('window.PBP.board.anim.skip()');
  await beat(40);
  const ev2 = await events();
  const board2 = await men();
  const st2 = await stats();
  console.log('skipped: ' + ev2.map((e) => e.k + '@' + Math.round(e.at)).join(' ') + ' | ' + board2.filter((m) => m.startsWith('bw') || m.includes('@d5')).join(' '));
  if (!ev2.some((e) => e.k === 'hit') || !ev2.some((e) => e.k === 'sunk') || !ev2.some((e) => e.k === 'land' && e.p.skipped)) bad('the skip did not land everything');
  if (!board2.some((m) => m.startsWith('bw@d5:'))) bad('after the skip the bishop is not on d5');
  if (st2.slides.length || st2.whips.length || st2.tumbles.length) bad('after the skip something is still in flight: ' + JSON.stringify(st2));
  console.log('  ' + await shot('whip-6-skipped.png', await closeUp('d5')));

  // --- 3. reduced motion takes the plain way -------------------------------------
  await open(page);
  await evalJs('window.PBP.settings.reducedMotion = true');
  await drag('c4', 'd5');
  await beat(400);
  const ev3 = await events();
  const l3 = ev3.filter((e) => e.k === 'land');
  console.log('reduced motion: ' + ev3.map((e) => e.k + '@' + Math.round(e.at)).join(' '));
  if (l3.length !== 1 || l3[0].p.manner !== 'plain' || !l3[0].p.capture) bad('reduced motion did not take the plain capture');
  if (ev3.some((e) => e.k === 'hit')) bad('reduced motion still whipped');

  const problems = [];
  for (const ev of cdp.events) {
    if (ev.method === 'Runtime.exceptionThrown') {
      const x = ev.params.exceptionDetails;
      problems.push('exception: ' + (x.exception?.description || x.text));
    } else if (ev.method === 'Runtime.consoleAPICalled' && ev.params.type === 'error') {
      problems.push('console.error: ' + ev.params.args.map((a) => a.description || a.value).join(' '));
    } else if (ev.method === 'Log.entryAdded' && ev.params.entry.level === 'error'
               && ev.params.entry.source !== 'network') {
      problems.push('log: ' + ev.params.entry.text);
    }
  }
  ws.close();
  edge.kill();
  try { rmSync(profile, { recursive: true, force: true }); } catch { /* disposable */ }
  if (problems.length || failed) {
    for (const p of problems) console.log('ERROR ' + p);
    process.exit(1);
  }
  console.log('whip shot done, no console errors');
}

main().catch((err) => {
  console.error(err.message || err);
  edge.kill();
  process.exit(2);
});
