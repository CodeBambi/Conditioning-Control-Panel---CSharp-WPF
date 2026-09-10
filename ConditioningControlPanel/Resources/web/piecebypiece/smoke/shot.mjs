/* ============================================================================
 * smoke/shot.mjs - load the page in headless msedge, report console errors and
 * save a screenshot. No dependencies: node's global WebSocket drives CDP.
 *
 *   node smoke/shot.mjs [--url <page url>] [--out <png path>] [--wait <ms>]
 *                       [--drag e2:e4]   drag a man from one square to another
 *                       [--hold <png>]   also capture the moment mid-drag
 *                       [--eval <js>]    run an expression once the page has
 *                                        settled and print what it returns, so
 *                                        a run can pin the ramp meter or dump
 *                                        window.PBP.ramp.debug() into the log
 *                       [--after <ms>]   how long to let the page run on after
 *                                        that expression (default 1200)
 *
 * Needs a static server on the web root, for example:
 *   python -m http.server 8821   (run from Resources/web)
 * Exits non-zero when the page logged an error or threw.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';

const EDGE = process.env.PBP_EDGE
  || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';

function arg(name, fallback) {
  const i = process.argv.indexOf('--' + name);
  return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const url = arg('url', 'http://localhost:8821/piecebypiece/index.html?hotseat=1');
const out = arg('out', 'C:/Users/PC/Pictures/Screenshots/piecebypiece/a/board.png');
const waitMs = Number(arg('wait', '6000'));
const port = Number(arg('port', '9222'));
const drag = arg('drag', '');
const hold = arg('hold', '');
const evalExpr = arg('eval', '');
const afterMs = Number(arg('after', '1200'));

const profile = join(tmpdir(), 'pbp-edge-' + Date.now());
const edge = spawn(EDGE, [
  '--headless=new',
  '--remote-debugging-port=' + port,
  '--user-data-dir=' + profile,
  '--window-size=1280,860',
  '--hide-scrollbars',
  '--no-first-run',
  '--no-default-browser-check',
  '--disable-extensions',
  '--use-gl=angle',
  '--use-angle=swiftshader',
  '--enable-unsafe-swiftshader',
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

function save(shot, path) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, Buffer.from(shot.data, 'base64'));
  console.log('screenshot: ' + path);
}

/** Pick a man up and put him down, through real pointer events. */
async function dragPiece(cdp, spec) {
  const [fromSq, toSq] = spec.split(':');
  // Press on the man's body (the default projection height), release on the
  // square itself: at board level a nearer man would be under the cursor.
  const at = async (sq, height) => {
    const r = await cdp.send('Runtime.evaluate', {
      expression: `JSON.stringify(window.PBP.board.projectSquare('${sq}', ${height}))`, returnByValue: true,
    });
    return JSON.parse(r.result.value);
  };
  await cdp.send('Runtime.evaluate', { expression:
    `window.__ev = []; for (const t of ['grab','dragmove','drop','turn']) window.PBP.bus.on(t, (p) => window.__ev.push(t + (p && p.ok === false ? ':refused' : '')));` });
  const a = await at(fromSq, 0.45);
  const b = await at(toSq, 0);
  const mouse = (type, x, y) => cdp.send('Input.dispatchMouseEvent', {
    type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, clickCount: 1, pointerType: 'mouse',
  });
  await mouse('mousePressed', a.x, a.y);
  for (let i = 1; i <= 8; i++) {
    await mouse('mouseMoved', a.x + (b.x - a.x) * (i / 8), a.y + (b.y - a.y) * (i / 8));
    await sleep(40);
  }
  if (hold) await save(await cdp.send('Page.captureScreenshot', { format: 'png' }), hold);
  await mouse('mouseReleased', b.x, b.y);
  const after = await cdp.send('Runtime.evaluate', {
    expression: `JSON.stringify({ fen: window.PBP.game.rules.fen(), ev: window.__ev.filter((e, i, a) => e !== 'dragmove' || a.indexOf(e) === i) })`,
    returnByValue: true,
  });
  const { fen, ev } = JSON.parse(after.result.value);
  console.log(`dragged ${fromSq} to ${toSq}: ${ev.join(' ')} | ${fen}`);
  await sleep(700);
}

let failed = false;

async function main() {
  const wsUrl = await target();
  const ws = new WebSocket(wsUrl);
  await new Promise((res, rej) => {
    ws.addEventListener('open', res, { once: true });
    ws.addEventListener('error', rej, { once: true });
  });
  const cdp = client(ws);

  await cdp.send('Runtime.enable');
  await cdp.send('Log.enable');
  await cdp.send('Page.enable');
  await cdp.send('Page.navigate', { url });
  await sleep(waitMs);

  // --eval: a hook for the effects layer. The page has no dev UI, so pinning
  // the meter or reading the ramp's debug snapshot happens from here.
  if (evalExpr) {
    const r = await cdp.send('Runtime.evaluate', {
      expression: evalExpr, returnByValue: true, awaitPromise: true,
    });
    if (r.exceptionDetails) {
      console.log('ERROR eval: ' + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
      failed = true;
    } else {
      console.log('eval: ' + JSON.stringify(r.result?.value));
    }
    await sleep(afterMs);
  }

  if (drag) await dragPiece(cdp, drag);

  await save(await cdp.send('Page.captureScreenshot', { format: 'png' }), out);

  const problems = [];
  for (const ev of cdp.events) {
    if (ev.method === 'Runtime.exceptionThrown') {
      const d = ev.params.exceptionDetails;
      problems.push('exception: ' + (d.exception?.description || d.text));
    } else if (ev.method === 'Runtime.consoleAPICalled' && ev.params.type === 'error') {
      problems.push('console.error: ' + ev.params.args.map((a) => a.description || a.value).join(' '));
    } else if (ev.method === 'Log.entryAdded' && ev.params.entry.level === 'error'
               && ev.params.entry.source !== 'network') {
      problems.push('log: ' + ev.params.entry.text);
    }
  }
  const warns = cdp.events.filter((e) => e.method === 'Runtime.consoleAPICalled' && e.params.type === 'warning')
    .map((e) => e.params.args.map((a) => a.description || a.value).join(' '));

  ws.close();
  edge.kill();
  try { rmSync(profile, { recursive: true, force: true }); } catch { /* profile is disposable */ }

  for (const w of warns) console.log('warn: ' + w);
  if (problems.length || failed) {
    for (const p of problems) console.log('ERROR ' + p);
    process.exit(1);
  }
  console.log('clean boot, no console errors');
}

main().catch((err) => {
  console.error(err.message || err);
  edge.kill();
  process.exit(2);
});
