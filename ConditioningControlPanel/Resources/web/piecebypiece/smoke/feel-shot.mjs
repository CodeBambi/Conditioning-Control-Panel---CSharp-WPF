/* ============================================================================
 * smoke/feel-shot.mjs - proof that a landing pays out.
 *
 * Loads the board in headless msedge, takes the frame loop over (the same
 * rAF hand-over jiggle-shot uses, so a screenshot never costs game time),
 * drags a pawn and captures the frame right after it lands, dust mid-burst;
 * then loads a position with a take on the board and does it again. Every
 * `land` the bus carries is logged with its payload, the dust reports how many
 * motes are alive, and reduced motion is checked to puff smaller.
 *
 *   node smoke/feel-shot.mjs [--url <page url>] [--out <dir>] [--wait <ms>]
 *
 * Needs a static server on the web root, for example:
 *   python -m http.server 8825   (run from Resources/web)
 * Exits non-zero when the page logged an error, when no `land` arrived, or
 * when the dust did not burst.
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

const url = arg('url', 'http://localhost:8825/piecebypiece/index.html?hotseat=1');
const outDir = arg('out', 'C:/Users/PC/Pictures/Screenshots/piecebypiece/j');
const waitMs = Number(arg('wait', '6000'));
const port = Number(arg('port', '9251'));   // off the lanes' shared 922x range, one browser per port
// white to move with e4xd5 on the board
const CAPTURE_FEN = 'rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2';

const profile = join(tmpdir(), 'pbp-feel-' + Date.now());
const edge = spawn(EDGE, [
  '--headless=new', '--remote-debugging-port=' + port, '--user-data-dir=' + profile,
  '--window-size=1280,860', '--hide-scrollbars', '--no-first-run', '--no-default-browser-check',
  '--disable-extensions', '--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
], { stdio: 'ignore' });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function target() {
  for (let i = 0; i < 60; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' });
      if (res.ok) return (await res.json()).webSocketDebuggerUrl;
    } catch { /* browser still starting */ }
    await sleep(250);
  }
  throw new Error('headless msedge did not open a debugging port');
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
  + " window.__pbp = { step(ms, n) { for (let i = 0; i < n; i++) { t += ms; const c = q; q = []; for (const fn of c) fn(t); } return q.length; } };"
  + " return q.length; })()";
const beat = (ms) => evalJs(`window.__pbp.step(16.667, ${Math.max(1, Math.round(ms / 16.667))})`);

const at = async (sq, height) => JSON.parse(await evalJs(
  `JSON.stringify(window.PBP.board.projectSquare('${sq}', ${height}))`));
async function mouse(type, x, y) {
  return cdp.send('Input.dispatchMouseEvent', {
    type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, clickCount: 1, pointerType: 'mouse',
  });
}
async function closeUp(sq, height = 0.4) {
  const p = await at(sq, height);
  return { x: Math.max(0, p.x - 120), y: Math.max(0, p.y - 120), width: 240, height: 220, scale: 2.5 };
}

/** Load a page, hand the loop over, and start recording `land`. */
async function open(pageUrl) {
  await cdp.send('Page.navigate', { url: pageUrl });
  await sleep(waitMs);
  await evalJs('window.PBP.ramp && window.PBP.ramp.setEnabled(false)');
  await evalJs('window.PBP.board.setWobble(0); window.PBP.board.setCameraSway(0)');
  await evalJs("window.__land = []; window.PBP.bus.on('land', (p) => window.__land.push(p));");
  const queued = await evalJs(HIJACK);
  if (!queued) { console.log('ERROR the frame loop did not hand over'); failed = true; }
  await beat(200);
}

async function drag(from, to) {
  const a = await at(from, 0.45);
  const b = await at(to, 0);
  await mouse('mousePressed', a.x, a.y);
  for (let i = 1; i <= 8; i++) {
    await mouse('mouseMoved', a.x + (b.x - a.x) * (i / 8), a.y + (b.y - a.y) * (i / 8));
    await beat(35);
  }
  await mouse('mouseReleased', b.x, b.y);
}

const dust = () => evalJs('JSON.stringify(window.PBP.board.dust && window.PBP.board.dust.stats())').then(JSON.parse);
const lands = () => evalJs('JSON.stringify(window.__land)').then(JSON.parse);

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

  // --- 1. a plain move: land, then the dust a few frames in -----------------
  await open(url);
  await drag('e2', 'e4');
  await beat(300);            // the slide is 0.30 s
  await beat(90);             // dust is 90 ms into its half second
  let d = await dust();
  let l = await lands();
  console.log('move landed: ' + JSON.stringify(l[l.length - 1]));
  console.log('dust after the move: ' + JSON.stringify(d));
  console.log('  ' + await shot('dust-land.png') + ' ' + await shot('dust-land-near.png', await closeUp('e4')));
  if (!l.length) { console.log('ERROR no land event'); failed = true; }
  if (!d || d.bursts < 1 || d.alive < 6) { console.log('ERROR the dust did not burst'); failed = true; }
  await beat(600);
  d = await dust();
  console.log('dust settled: ' + JSON.stringify(d));
  if (d && d.alive !== 0) { console.log('ERROR motes never die'); failed = true; }

  // --- 2. a refused drop: a shrug of dust, flagged refused -------------------
  // black to move now, so it is a black pawn that gets told no
  await drag('d7', 'd3');
  await beat(400);
  l = await lands();
  const refused = l[l.length - 1];
  console.log('refused landing: ' + JSON.stringify(refused));
  if (!refused || !refused.refused) { console.log('ERROR the spring-back did not land as refused'); failed = true; }

  // --- 3. a capture: bigger, and the payload says so -------------------------
  const sep = url.includes('?') ? '&' : '?';
  await open(url + sep + 'fen=' + encodeURIComponent(CAPTURE_FEN));
  await drag('e4', 'd5');
  await beat(300);
  await beat(70);
  d = await dust();
  l = await lands();
  const take = l[l.length - 1];
  console.log('capture landed: ' + JSON.stringify(take));
  console.log('dust after the take: ' + JSON.stringify(d));
  console.log('  ' + await shot('dust-capture.png') + ' ' + await shot('dust-capture-near.png', await closeUp('d5')));
  if (!take || !take.capture) { console.log('ERROR the capture did not land as a capture'); failed = true; }
  await beat(900);

  // --- 4. reduced motion puffs smaller ---------------------------------------
  await evalJs('window.PBP.reducedMotion = true; window.PBP.board.dust.puff({x:0,y:0,z:0}, 1, "move")');
  await beat(40);
  d = await dust();
  console.log('reduced motion puff: ' + JSON.stringify(d));
  if (!d.reduced || d.alive > 8 || d.rings > 0) { console.log('ERROR reduced motion did not take'); failed = true; }
  await evalJs('window.PBP.reducedMotion = false');

  // --- 5. what it costs ------------------------------------------------------
  const cost = await evalJs(`(() => {
    const b = window.PBP.board; const v = b.view;
    for (let i = 0; i < 8; i++) b.dust.puff({x: (i % 8) - 3.5, y: 0, z: 0}, 1, i % 3 ? 'move' : 'capture', 'k');
    const t = performance.now();
    for (let i = 0; i < 300; i++) b.dust.update(0.016, v.camera, v.renderer);
    return JSON.stringify({ updateMs: (performance.now() - t) / 300, alive: b.dust.stats().alive, calls: v.renderer.info.render.calls });
  })()`);
  console.log('dust cost: ' + cost);

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
  console.log('feel shot done, no console errors');
}

main().catch((err) => {
  console.error(err.message || err);
  edge.kill();
  process.exit(2);
});
