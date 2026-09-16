/* ============================================================================
 * smoke/jiggle-shot.mjs - proof that the men are soft.
 *
 * Loads the board in headless msedge, pokes one man, and saves a strip of
 * frames while the flex rings out, plus a mid-drag frame, the frame right
 * after a landing, and two idle frames at meter wobble 1.0. Every frame is
 * logged with the live spring state, so a strip that looks still can be told
 * apart from a spring that never moved. Also times the flex system with a
 * full board on it.
 *
 * Time is stepped by hand. A headless swiftshader screenshot costs more than a
 * second, so the page's own frame loop is taken over and advanced in exact
 * 1/60 s beats between captures; otherwise a strip meant to span 0.8 s would
 * span nine and show nothing but a settled piece.
 *
 *   node smoke/jiggle-shot.mjs [--url <page url>] [--out <dir>] [--wait <ms>]
 *
 * Needs a static server on the web root, for example:
 *   python -m http.server 8821   (run from Resources/web)
 * Exits non-zero when the page logged an error, or when the poke did not move
 * the mesh at all.
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

const url = arg('url', 'http://localhost:8821/piecebypiece/index.html?hotseat=1');
const outDir = arg('out', 'C:/Users/PC/Pictures/Screenshots/piecebypiece/e');
const waitMs = Number(arg('wait', '6000'));
const port = Number(arg('port', '9223'));
const pokeSq = arg('square', 'd8');

const profile = join(tmpdir(), 'pbp-jiggle-' + Date.now());
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

let cdp = null;

async function shot(name, clip = null) {
  const s = await cdp.send('Page.captureScreenshot', clip ? { format: 'png', clip } : { format: 'png' });
  const path = join(outDir, name);
  writeFileSync(path, Buffer.from(s.data, 'base64'));
  return path;
}

// Take the page's frame loop over, so a screenshot never costs game time. The
// wait matters: the loop's next callback was booked with the real rAF, so the
// queue is empty until it lands, and a step before then advances nothing.
const HIJACK = "window.__pbp = null; (async () => {"
  + " let q = [];"
  + " window.requestAnimationFrame = (fn) => { q.push(fn); return q.length; };"
  + " window.cancelAnimationFrame = () => {};"
  + " await new Promise((r) => setTimeout(r, 400));"
  + " let t = performance.now();"
  + " window.__pbp = { step(ms, n) { for (let i = 0; i < n; i++) { t += ms; const c = q; q = []; for (const fn of c) fn(t); } return q.length; } };"
  + " return q.length; })()";

/** Advance the page by `ms`, in 1/60 s beats, with nothing else running. */
const beat = (ms) => evalJs(`window.__pbp.step(16.667, ${Math.max(1, Math.round(ms / 16.667))})`);

/** A close crop around one man, which is where the flex is actually legible. */
async function closeUp(sq, height = 0.55) {
  const p = await at(sq, height);
  return { x: Math.max(0, p.x - 85), y: Math.max(0, p.y - 105), width: 170, height: 180, scale: 3 };
}

async function evalJs(expression) {
  const r = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result?.value;
}

const spring = (sq) => evalJs(`JSON.stringify(window.PBP.board.jiggle.debug('${sq}'))`).then(JSON.parse);

async function mouse(type, x, y) {
  return cdp.send('Input.dispatchMouseEvent', {
    type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, clickCount: 1, pointerType: 'mouse',
  });
}
const at = async (sq, height) => JSON.parse(await evalJs(
  `JSON.stringify(window.PBP.board.projectSquare('${sq}', ${height}))`));

let failed = false;

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
  await cdp.send('Page.navigate', { url });
  await sleep(waitMs);

  // The ramp pushes the meter onto the board every beat, which would fight
  // every wobble this harness sets.
  await evalJs('window.PBP.ramp && window.PBP.ramp.setEnabled(false)');
  await evalJs("window.PBP.board.setWobble(0); window.PBP.board.setCameraSway(0);");
  const queued = await evalJs(HIJACK);
  if (!queued) { console.log('ERROR the frame loop did not hand over'); failed = true; }
  await beat(200);

  // --- 1. the poke strip ----------------------------------------------------
  const crop = await closeUp(pokeSq);
  await shot('poke-0-rest.png', crop);
  await shot('poke-0-rest-full.png');
  await evalJs(`window.PBP.board.jiggle.poke('${pokeSq}', { bend: [7, 0], squash: 3 })`);
  let peak = 0;
  for (let i = 0; i < 6; i++) {
    await beat(i === 0 ? 40 : 133);
    const s = await spring(pokeSq);
    const mag = Math.hypot(s.uBend[0], s.uBend[1]);
    peak = Math.max(peak, mag);
    const path = await shot(`poke-${i + 1}.png`, crop);
    if (i === 0) await shot('poke-1-full.png');
    console.log(`poke frame ${i + 1} at ${40 + i * 133} ms  bend=${s.uBend.map((v) => v.toFixed(3))} squash=${s.uSquash.toFixed(3)}  ${path}`);
  }
  console.log('peak bend during the strip: ' + peak.toFixed(3) + ' local units');
  if (peak < 0.02) { console.log('ERROR the poke did not move the mesh'); failed = true; }

  // --- 2. a drag, held then dropped ----------------------------------------
  const a = await at('e2', 0.45);
  await mouse('mousePressed', a.x, a.y);
  // The held man rides under the cursor at the height he was grabbed at, so
  // the release is aimed at that height over e4 rather than at board level.
  const b = await at('e4', Number(await evalJs(
    'window.PBP.board.drag && window.PBP.board.drag.holdHeight ? window.PBP.board.drag.holdHeight() : 0')) || 0);
  for (let i = 1; i <= 8; i++) {
    await mouse('mouseMoved', a.x + (b.x - a.x) * (i / 8), a.y + (b.y - a.y) * (i / 8));
    await beat(35);
  }
  console.log('mid drag: ' + JSON.stringify(await spring('e2')));
  const dragCrop = await closeUp('e4', 1.5);   // the held man rides above the board
  console.log('  ' + await shot('drag-hold.png') + ' ' + await shot('drag-hold-near.png', dragCrop));
  await mouse('mouseReleased', b.x, b.y);
  await beat(320);   // the slide is 0.30 s, so this is the moment it lands
  const landCrop = await closeUp('e4');
  console.log('on landing: ' + JSON.stringify(await spring('e4')));
  console.log('  ' + await shot('land-1.png') + ' ' + await shot('land-1-near.png', landCrop));
  await beat(120);
  console.log('after landing: ' + JSON.stringify(await spring('e4')));
  console.log('  ' + await shot('land-2.png') + ' ' + await shot('land-2-near.png', landCrop));

  // --- 3. a capture landing and a check buzz, for the paths a drag misses ---
  await evalJs(`(() => {
    const b = window.PBP.board;
    b.pieces.pieceAt('d8').userData.tookOne = true;
    b.jiggle.system.land(b.pieces.pieceAt('d8'), [1, 0]);
    b.anim.buzz(b.pieces.pieceAt('d1'));
  })()`);
  await beat(50);
  const takeCrop = await closeUp('d8');
  console.log('capture landing: ' + JSON.stringify(await spring('d8')));
  console.log('  ' + await shot('capture-near.png', takeCrop));
  console.log('check buzz: ' + JSON.stringify(await spring('d1')));
  console.log('  ' + await shot('buzz-near.png', await closeUp('d1')));
  await beat(900);

  // --- 4. idle sway at full wobble -----------------------------------------
  await evalJs('window.PBP.board.setWobble(1)');
  await beat(600);
  const idleCrop = await closeUp('d8');
  console.log('idle a: ' + JSON.stringify(await spring('d8')));
  console.log('  ' + await shot('idle-a.png') + ' ' + await shot('idle-a-near.png', idleCrop));
  await beat(500);
  console.log('idle b: ' + JSON.stringify(await spring('d8')));
  console.log('  ' + await shot('idle-b.png') + ' ' + await shot('idle-b-near.png', idleCrop));
  await evalJs('window.PBP.board.setWobble(0)');

  // --- 5. motion turned down: impulses drop and the idle sway stops --------
  await evalJs('window.PBP.reducedMotion = true; window.PBP.board.setWobble(1)');
  await beat(60);
  const still = Math.hypot(...(await spring('d8')).uBend);
  await evalJs("window.PBP.board.jiggle.poke('d8', { bend: [7, 0], squash: 3 })");
  await beat(50);
  const soft = Math.abs((await spring('d8')).bend[0]);
  console.log(`reduced motion: idle ${still.toFixed(4)} (wants 0), poke peak ${soft.toFixed(3)} (wants about a third of ${peak.toFixed(3)})`);
  if (still > 0.001 || soft > peak * 0.5) { console.log('ERROR reduced motion did not take'); failed = true; }
  await evalJs('window.PBP.reducedMotion = false; window.PBP.board.setWobble(0)');
  await beat(900);

  // --- 6. what the flex costs with a full board ----------------------------
  const cost = await evalJs(`(() => {
    const j = window.PBP.board.jiggle.system;
    for (let i = 0; i < 30; i++) j.update(0.016);
    const t = performance.now();
    for (let i = 0; i < 600; i++) j.update(0.016);
    const ms = (performance.now() - t) / 600;
    return JSON.stringify({ pieces: window.PBP.board.jiggle.stats().pieces, msPerFrame: ms });
  })()`);
  console.log('flex cost: ' + cost);

  const problems = [];
  for (const ev of cdp.events) {
    if (ev.method === 'Runtime.exceptionThrown') {
      const d = ev.params.exceptionDetails;
      problems.push('exception: ' + (d.exception?.description || d.text));
    } else if (ev.method === 'Runtime.consoleAPICalled' && ev.params.type === 'error') {
      problems.push('console.error: ' + ev.params.args.map((x) => x.description || x.value).join(' '));
    } else if (ev.method === 'Log.entryAdded' && ev.params.entry.level === 'error'
               && ev.params.entry.source !== 'network') {
      problems.push('log: ' + ev.params.entry.text);
    }
  }
  ws.close();
  edge.kill();
  try { rmSync(profile, { recursive: true, force: true }); } catch { /* profile is disposable */ }
  if (problems.length || failed) {
    for (const p of problems) console.log('ERROR ' + p);
    process.exit(1);
  }
  console.log('jiggle shot done, no console errors');
}

main().catch((err) => {
  console.error(err.message || err);
  edge.kill();
  process.exit(2);
});
