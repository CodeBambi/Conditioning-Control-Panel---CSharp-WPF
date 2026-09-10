/* ============================================================================
 * smoke/theatre-shot.mjs - proof that the board performs.
 *
 * Loads the board in headless msedge, takes the frame loop over (the same rAF
 * hand-over jiggle-shot and feel-shot use, so a screenshot never costs game
 * time) and plays a scene by hand through window.PBP.game.tryMove, shooting
 * the exact beats that matter: the knight at the top of his hop, the castling
 * rook a beat behind his king, the three frames of a capture, the parade after
 * four takes, the mate and draw poses, the bloom off and on, the room turning
 * to watch a held man. Every frame is logged with what was in flight, so a
 * still frame can be told from a scene that never played.
 *
 *   node smoke/theatre-shot.mjs [--url <page url>] [--out <dir>] [--wait <ms>]
 *                               [--port <debug port>] [--scenes a,b,c]
 *
 * Needs a static server on the web root, for example:
 *   python -m http.server 8831   (run from Resources/web)
 * Exits non-zero when the page logged an error or a scene did not play.
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

const url = arg('url', 'http://localhost:8831/piecebypiece/index.html?hotseat=1');
const outDir = arg('out', 'C:/Users/PC/Pictures/Screenshots/piecebypiece/n');
const waitMs = Number(arg('wait', '8000'));
const port = Number(arg('port', '9273'));
const only = arg('scenes', '').split(',').filter(Boolean);

const FEN = {
  castle: 'r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1',
  capture: 'rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2',
  knightTake: 'rnbqkbnr/pppp1ppp/8/4p3/8/5N2/PPPPPPPP/RNBQKB1R w KQkq - 0 2',
  // white queen and rook vs a lone black king: four takes then a mate
  parade: 'k7/pppp4/8/8/8/8/8/R3Q2K w - - 0 1',
  mate: '6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1',            // Ra8 is mate
  draw: '7k/8/6K1/8/8/8/8/5Q2 w - - 0 1',           // Qf7 is stalemate
};

const profile = join(tmpdir(), 'pbp-theatre-' + Date.now());
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
const bad = (msg) => { console.log('ERROR ' + msg); failed = true; };

async function evalJs(expression) {
  const r = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
  return r.result?.value;
}
const json = (expr) => evalJs('JSON.stringify(' + expr + ')').then((v) => (v === undefined ? null : JSON.parse(v)));

async function shot(name, clip = null) {
  const s = await cdp.send('Page.captureScreenshot', clip ? { format: 'png', clip } : { format: 'png' });
  const path = join(outDir, name);
  writeFileSync(path, Buffer.from(s.data, 'base64'));
  console.log('  ' + path);
  return path;
}

const HIJACK = "window.__pbp = null; (async () => {"
  + " let q = [];"
  + " window.requestAnimationFrame = (fn) => { q.push(fn); return q.length; };"
  + " window.cancelAnimationFrame = () => {};"
  + " for (let i = 0; i < 40 && !q.length; i++) await new Promise((r) => setTimeout(r, 100));"
  + " let t = performance.now();"
  + " window.__pbp = { step(ms, n) { for (let i = 0; i < n; i++) { t += ms; const c = q; q = []; for (const fn of c) fn(t); } return q.length; } };"
  + " return q.length; })()";
const beat = (ms) => evalJs(`window.__pbp.step(16.667, ${Math.max(1, Math.round(ms / 16.667))})`);

const at = async (sq, height) => json(`window.PBP.board.projectSquare('${sq}', ${height})`);
async function mouse(type, x, y) {
  return cdp.send('Input.dispatchMouseEvent', {
    type, x, y, button: 'left', buttons: type === 'mouseReleased' ? 0 : 1, clickCount: 1, pointerType: 'mouse',
  });
}
async function closeUp(sq, height = 0.5, w = 300, h = 260) {
  const p = await at(sq, height);
  return { x: Math.max(0, p.x - w / 2), y: Math.max(0, p.y - h / 2), width: w, height: h, scale: 2 };
}

/** Load a position, hand the loop over, start recording the bus. */
async function open(fen = null, extra = '') {
  const sep = url.includes('?') ? '&' : '?';
  const page = url + (fen ? sep + 'fen=' + encodeURIComponent(fen) : '') + extra;
  await cdp.send('Page.navigate', { url: page });
  // Wait for the men to be dealt and for the art to arrive; a slow software
  // GPU on a shared machine can take a good while, so this polls rather than
  // trusting a fixed sleep (waitMs is the floor, 40 s the ceiling).
  const t0 = Date.now();
  await sleep(waitMs);
  for (;;) {
    let ready = false;
    try {
      ready = await evalJs("(() => { const P = window.PBP; if (!P || !P.board) return false;"
        + " const all = [...P.board.pieces.pieces.values()]; if (!all.length) return false;"
        + " return all.every((p) => p.children.some((m) => m.isMesh && m.geometry.type === 'BufferGeometry')); })()");
    } catch { /* still booting */ }
    if (ready || Date.now() - t0 > 40000) { console.log('  ready after ' + (Date.now() - t0) + ' ms' + (ready ? '' : ' (art still missing)')); break; }
    await sleep(500);
  }
  await evalJs('window.PBP.ramp && window.PBP.ramp.setEnabled(false)');
  await evalJs('window.PBP.board.setWobble(0); window.PBP.board.setCameraSway(0)');
  await evalJs("window.__ev = []; for (const t of ['land','sunk','capture','gameover','grab','drop']) window.PBP.bus.on(t, (p) => window.__ev.push({ t, at: window.__clock || 0, p: t === 'sunk' ? { piece: p.piece, side: p.side } : p }));");
  const queued = await evalJs(HIJACK);
  if (!queued) bad('the frame loop did not hand over');
  await beat(200);
}
const move = (from, to) => json(`window.PBP.game.tryMove('${from}', '${to}')`);
const flight = () => json('window.PBP.board.anim.stats()');
const events = () => json('window.__ev');
const view = (name) => evalJs(`window.PBP.camera.preset('${name}', true)`).then(() => beat(50));

const scenes = {
  // --- PR 1: the knight hops --------------------------------------------------
  async knight() {
    await open();
    await move('g1', 'f3');
    await beat(220);
    const f = await flight();
    const y = await json("window.PBP.board.pieces.pieceAt('f3').position.y");
    console.log('knight mid-hop: ' + JSON.stringify(f) + ' y=' + y.toFixed(3));
    if (!f.slides[0] || !f.slides[0].knight || y < 0.6) bad('the knight did not hop');
    await shot('knight-hop.png');
    await shot('knight-hop-near.png', await closeUp('f3', 0.9, 360, 320));
    await beat(260);
    await shot('knight-landed.png', await closeUp('f3', 0.4, 360, 320));
    const l = (await events()).filter((e) => e.t === 'land');
    console.log('knight landed: ' + JSON.stringify(l[l.length - 1] && l[l.length - 1].p));
  },
  // --- PR 1: the castle moves as one -------------------------------------------
  async castle() {
    await open(FEN.castle);
    await shot('castle-0-before.png', await closeUp('f1', 0.4, 520, 300));
    await move('e1', 'g1');
    await beat(100);
    let f = await flight();
    console.log('castle at 100 ms: ' + JSON.stringify(f));
    const rook = f.slides.find((s) => s.type === 'r');
    if (!rook || rook.delay < 0.1) bad('the rook did not wait for his king');
    await shot('castle-king-first.png', await closeUp('f1', 0.4, 520, 300));
    await beat(150);
    f = await flight();
    console.log('castle at 250 ms: ' + JSON.stringify(f));
    await shot('castle-mid.png', await closeUp('f1', 0.4, 520, 300));
    await shot('castle-mid-full.png');
    await beat(300);
    const l = (await events()).filter((e) => e.t === 'land').map((e) => e.p.piece + '@' + e.p.square);
    console.log('castle landed: ' + l.join(' '));
    if (!l.includes('k@g1') || !l.includes('r@f1')) bad('the castle did not land both men');
    await shot('castle-done.png', await closeUp('f1', 0.4, 520, 300));
  },
  // --- PR 1: capture order, three frames ---------------------------------------
  async capture() {
    await open(FEN.capture);
    await move('e4', 'd5');
    await beat(150);
    console.log('capture at 150 ms: ' + JSON.stringify(await flight()));
    await shot('capture-1-tipping.png', await closeUp('d5', 0.5, 420, 340));
    await beat(160);
    const l = (await events()).filter((e) => e.t === 'land');
    console.log('capture at 310 ms: ' + JSON.stringify(await flight()) + ' land=' + JSON.stringify(l[l.length - 1] && l[l.length - 1].p));
    if (!l.length || !l[l.length - 1].p.capture) bad('the taker did not land on the fall by 310 ms');
    await shot('capture-2-landing.png', await closeUp('d5', 0.5, 420, 340));
    await beat(260);
    console.log('capture at 570 ms: ' + JSON.stringify(await flight()));
    await shot('capture-3-rolling.png', await closeUp('d5', 0.5, 420, 340));
    await shot('capture-3-rolling-zoom.png', await closeUp('d6', 0.15, 200, 160));
    await beat(1000);
    const s = (await events()).filter((e) => e.t === 'sunk');
    if (!s.length) bad('the victim never sank');
    console.log('sunk: ' + JSON.stringify(s.map((e) => e.p)));
  },
  async captureSide() {
    await open(FEN.capture);
    await view('side');
    await beat(1200);
    await move('e4', 'd5');
    await beat(150);
    await shot('capture-side-1-tipping.png', await closeUp('d5', 0.5, 520, 360));
    await beat(160);
    await shot('capture-side-2-landing.png', await closeUp('d5', 0.5, 520, 360));
    await beat(260);
    await shot('capture-side-3-rolling.png', await closeUp('d5', 0.5, 520, 360));
  },
  // --- PR 2: the taken stand and watch ------------------------------------------
  async parade() {
    await open();
    const seq = [['e2', 'e4'], ['d7', 'd5'], ['e4', 'd5'], ['d8', 'd5'], ['b1', 'c3'], ['d5', 'g2'], ['f1', 'g2']];
    for (const [a, b] of seq) { const r = await move(a, b); await beat(r && r.captured ? 1700 : 450); }
    await beat(600);
    const st = await json('window.PBP.board.parade && window.PBP.board.parade.stats()');
    console.log('parade: ' + JSON.stringify(st));
    if (!st || st.w.length !== 2 || st.b.length !== 2) bad('the parade does not hold four men');
    if (st && st.w.concat(st.b).some((e) => e.rising)) bad('a parade man is still rising');
    const sunk = (await events()).filter((e) => e.t === 'sunk').length;
    console.log('sunk events: ' + sunk);
    await shot('parade-normal.png');
    await shot('parade-white-rim.png', { x: 0, y: 380, width: 1280, height: 480, scale: 1.5 });
    await shot('parade-black-rim.png', { x: 200, y: 60, width: 900, height: 340, scale: 1.5 });
    await view('top');
    await beat(1200);
    await shot('parade-top.png');
    // Straight down on each rim: white's is +z (the a-file end holds his first man).
    const rim = async (name, sq) => { const c = await closeUp(sq, 0, 420, 200); c.y += 55 * (sq[1] === '1' ? 1 : -1); await shot(name, c); };
    await rim('parade-top-white-rim.png', 'b1');
    await rim('parade-top-black-rim.png', 'g8');
    const pick = await json("(() => { const P = window.PBP.board.parade; const g = window.PBP.board.view.boardGroup; return g.children.filter((o) => o.userData && o.userData.parade).map((o) => ({ raycast: o.children.every((c) => !c.isMesh || c.raycast.length === 0 && c.raycast() === undefined), busy: !!o.userData.busy, scale: +o.scale.x.toFixed(3) })); })()");
    console.log('parade men: ' + JSON.stringify(pick));
    await evalJs("window.PBP.bus.emit('takeback', { from: 'f1', to: 'g2', ply: 7 })");
    await beat(400);
    console.log('after takeback: ' + JSON.stringify(await json('window.PBP.board.parade.stats()')));
    await evalJs("window.PBP.bus.emit('local', { sides: ['w', 'b'] })");
    await beat(50);
    console.log('after local: ' + JSON.stringify(await json('window.PBP.board.parade.stats()')));
  },
  async mate() {
    await open(FEN.mate);
    await move('a1', 'a8');
    await beat(2000);
    let st = await json('window.PBP.board.poses.stats()');
    console.log('mate at 2.0 s: ' + JSON.stringify(st));
    if (!st || st.kind !== 'mate' || !st.hit || st.kings[0].up > 0.15) bad('the king did not fall');
    const l = (await events()).filter((e) => e.t === 'land' && e.p.pose);
    console.log('pose land events: ' + l.length);
    await shot('mate-pose.png');
    await shot('mate-pose-near.png', await closeUp('g8', 0.3, 520, 360));
    await beat(1200);
    await shot('mate-pose-settled.png');
    const ov = await json('window.PBP.board.outline.stats()');
    console.log('outline: ' + JSON.stringify(ov));
  },
  async mateWarm() {
    await open(FEN.mate);
    await move('a1', 'a8');
    await beat(1450);
    console.log('mate at 1.45 s: ' + JSON.stringify(await json('window.PBP.board.poses.stats()')));
    await shot('mate-warm.png');
    await shot('mate-hit-near.png', await closeUp('g8', 0.3, 520, 360));
  },
  async draw() {
    await open(FEN.draw);
    const r = await move('f1', 'f7');
    console.log('draw played: ' + JSON.stringify(r && r.san) + ' over=' + await evalJs('JSON.stringify(window.PBP.game.result())'));
    await beat(1300);
    const st = await json('window.PBP.board.poses.stats()');
    console.log('draw at 1.3 s: ' + JSON.stringify(st));
    if (!st || st.kind !== 'draw') bad('the kings did not lean');
    await shot('draw-pose.png');
    await shot('draw-pose-near.png', await closeUp('g7', 0.3, 640, 400));
  },
  async resign() {
    await open();
    await evalJs("window.PBP.game.resign('w')");
    await beat(1500);
    console.log('resign at 1.5 s: ' + JSON.stringify(await json('window.PBP.board.poses.stats()')));
    await shot('resign-pose.png', await closeUp('e1', 0.3, 640, 400));
  },
  async knightTake() {
    await open(FEN.knightTake);
    console.log('knight take played: ' + JSON.stringify(await move('f3', 'e5')));
    await beat(120);
    const f = await flight();
    console.log('knight take at 120 ms: ' + JSON.stringify(f));
    if (!f.tumbles[0] || f.tumbles[0].delay < 0.1) bad('the knight victim did not wait');
    await shot('knight-take-1.png', await closeUp('e5', 0.6, 420, 360));
    await beat(330);
    const l = (await events()).filter((e) => e.t === 'land');
    console.log('knight take at 450 ms: land=' + JSON.stringify(l[l.length - 1] && l[l.length - 1].p));
    await shot('knight-take-2.png', await closeUp('e5', 0.6, 420, 360));
  },
};

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

  for (const name of Object.keys(scenes)) {
    if (only.length && !only.includes(name)) continue;
    console.log('--- ' + name);
    try { await scenes[name](); } catch (e) { bad(name + ': ' + (e.message || e)); }
  }

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
  const warns = cdp.events.filter((e) => e.method === 'Runtime.consoleAPICalled' && e.params.type === 'warning')
    .map((e) => e.params.args.map((a) => a.description || a.value).join(' '));
  ws.close();
  edge.kill();
  try { rmSync(profile, { recursive: true, force: true }); } catch { /* disposable */ }
  for (const w of warns) console.log('warn: ' + w);
  if (problems.length || failed) {
    for (const p of problems) console.log('ERROR ' + p);
    process.exit(1);
  }
  console.log('theatre shot done, no console errors');
}

main().catch((err) => {
  console.error(err.message || err);
  edge.kill();
  process.exit(2);
});
