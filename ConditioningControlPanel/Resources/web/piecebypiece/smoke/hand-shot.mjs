/* ============================================================================
 * smoke/hand-shot.mjs - proof that the hand knows what it is over.
 *
 * Loads the board in headless msedge, takes the frame loop over (the same rAF
 * hand-over jiggle-shot and feel-shot use, so a screenshot never costs game
 * time) and drives the pointer the way a player would: hover a man, click him,
 * click his square. Every step prints what drag.debug() says, and a small ring
 * is drawn into the page at the pointer position so a still frame shows where
 * the cursor was standing.
 *
 *   node smoke/hand-shot.mjs [--url <page url>] [--out <dir>] [--wait <ms>]
 *                            [--port <debug port>]
 *
 * Needs a static server on the web root, for example:
 *   python -m http.server 8830   (run from Resources/web)
 * Exits non-zero when the page logged an error, when a hover did not lift, or
 * when a click did not select or did not play its move.
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

const url = arg('url', 'http://localhost:8830/piecebypiece/index.html?hotseat=1');
const outDir = arg('out', 'C:/Users/PC/Pictures/Screenshots/piecebypiece/m');
const waitMs = Number(arg('wait', '12000'));
const port = Number(arg('port', '9272'));

const profile = join(tmpdir(), 'pbp-hand-' + Date.now());
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
function bad(what) { console.log('ERROR ' + what); failed = true; }

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
  + " await new Promise((r) => setTimeout(r, 900));"
  + " let t = performance.now();"
  + " window.__pbp = { step(ms, n) { for (let i = 0; i < n; i++) { t += ms; const c = q; q = []; for (const fn of c) fn(t); } return q.length; } };"
  + " return q.length; })()";
const beat = (ms) => evalJs(`window.__pbp.step(16.667, ${Math.max(1, Math.round(ms / 16.667))})`);

// A headless screenshot has no pointer in it, so the page is told to draw one.
const MARK = `window.__mark = (x, y) => {
  let d = document.getElementById('hand-cursor');
  if (!d) {
    d = document.createElement('div');
    d.id = 'hand-cursor';
    d.style.cssText = 'position:fixed;width:26px;height:26px;margin:-13px 0 0 -13px;border:2px solid #FF3E8F;'
      + 'border-radius:50%;box-shadow:0 0 0 2px rgba(255,255,255,.75);pointer-events:none;z-index:99;';
    document.body.appendChild(d);
  }
  d.style.left = x + 'px';
  d.style.top = y + 'px';
}`;

/**
 * The pointer, dispatched from inside the page. A press and a release that
 * count as a click have to be under 250 ms apart, and one CDP input round trip
 * through headless swiftshader is slower than that on its own, so the events
 * are made in the page where the gap is a single turn of the event loop. They
 * are the same PointerEvents the canvas listens for; the real CDP input path is
 * what shot.mjs and feel-shot drive, and both still drag.
 */
const POINTER = `window.__pt = (x, y, type) => {
  const c = document.getElementById('board-canvas');
  c.dispatchEvent(new PointerEvent(type, { clientX: x, clientY: y, button: 0,
    buttons: type === 'pointerup' ? 0 : 1, bubbles: true, cancelable: true,
    pointerId: 1, pointerType: 'mouse', isPrimary: true }));
};
window.__pointAt = (sq, h) => { const p = window.PBP.board.projectSquare(sq, h); window.__mark(p.x, p.y); return p; };`;

/** One key press, through the real input path: no page-side shortcut needed. */
async function key(name) {
  const code = name === 'Backspace' ? 8 : name.toUpperCase().charCodeAt(0);
  const p = { key: name, code: name === 'Backspace' ? 'Backspace' : 'Key' + name.toUpperCase(), windowsVirtualKeyCode: code };
  await cdp.send('Input.dispatchKeyEvent', Object.assign({ type: 'keyDown' }, p));
  await cdp.send('Input.dispatchKeyEvent', Object.assign({ type: 'keyUp' }, p));
}

const hand = () => evalJs('JSON.stringify(window.PBP.board.drag.debug())').then(JSON.parse);
const fen = () => evalJs('window.PBP.game.rules.fen()');

/**
 * Put the cursor on something and let the hand confirm it. A man standing in
 * front of the one you want can be in the way of the ray at one height and out
 * of it at another, so the heights are tried until the hand reports the thing
 * that was asked for: a man by his square, an empty square by the legal target
 * under the cursor. Returns the height that worked, or null.
 */
const MAN_HEIGHTS = [0.45, 0.62, 0.34, 0.78, 0.24];
const SQUARE_HEIGHTS = [0, 0.14, 0.3, 0.45];

async function hover(sq, { man = true } = {}) {
  for (const h of (man ? MAN_HEIGHTS : SQUARE_HEIGHTS)) {
    await evalJs(`(() => { const p = window.__pointAt('${sq}', ${h}); window.__pt(p.x, p.y, 'pointermove'); })()`);
    await beat(120);
    const d = await hand();
    if (man ? d.hover === sq : d.over === sq) return h;
  }
  return null;
}

/** Press and release on the same spot, in one turn: a click, not a drag. */
async function click(sq, opts = {}) {
  const h = await hover(sq, opts);
  if (h === null && !opts.anyway) bad(`the cursor could not be put on ${sq}`);
  const gap = await evalJs(`(() => {
    const p = window.__pointAt('${sq}', ${h === null ? 0.45 : h});
    window.__pt(p.x, p.y, 'pointermove');
    const t = performance.now();
    window.__pt(p.x, p.y, 'pointerdown');
    window.__pt(p.x, p.y, 'pointerup');
    return Math.round(performance.now() - t);
  })()`);
  await beat(120);
  const d = await hand();
  console.log(`  click ${sq} at ${h}: ${gap} ms of the 250 ms window -> `
    + JSON.stringify(d) + ' ' + (await fen()).split(' ').slice(0, 2).join(' '));
  return d;
}

/**
 * The toys arrive over the network, and each one that lands rebuilds every man
 * of that type, so a shot taken too early is a shot of the stand-in shapes
 * pieces.js lathes while it waits. Nothing on the frame clock brings them in.
 * A man wearing his glb stands at scale 1; a lathe is scaled to its type
 * height, so counting the men who are still not 1 says how many are waiting.
 */
async function artSettled(ms = 20000) {
  const left = "[...window.PBP.board.pieces.pieces.values()].filter((p) => p.scale.x !== 1).length";
  const t0 = Date.now();
  while (Date.now() - t0 < ms) {
    if ((await evalJs(left)) === 0) return true;
    await sleep(250);
  }
  console.log('  (some men are still in their stand-in shapes; the shots use what landed)');
  return false;
}

/** Open a board and take its frame loop over, ready to be driven. */
async function load(where) {
  await cdp.send('Page.navigate', { url: where });
  await sleep(waitMs);
  await evalJs('window.PBP.ramp && window.PBP.ramp.setEnabled(false)');
  await evalJs('window.PBP.board.setWobble(0); window.PBP.board.setCameraSway(0)');
  await evalJs(MARK);
  await evalJs(POINTER);
  // The first pointerdown anywhere builds the AudioContext, which costs whole
  // seconds under swiftshader: spend that on the document, not on a click.
  await evalJs("document.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerId: 9, clientX: 4, clientY: 4 }));");
  if (!(await evalJs(HIJACK))) bad('the frame loop did not hand over');
  await beat(200);
  await artSettled();
  await beat(200);
}

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
  await load(url);

  // --- 1. hover: his own man lifts, the other side does not ------------------
  await evalJs("window.__pt(20, 20, 'pointermove')");   // nothing under him
  await beat(200);
  console.log('  ' + await shot('rest.png'));           // the same camera, no hover
  await hover('e2');
  await beat(400);                                      // the lift has settled
  let h = await hand();
  console.log('hover a white pawn: ' + JSON.stringify(h));
  console.log('  ' + await shot('hover.png'));
  if (h.hover !== 'e2') bad('the man under the cursor was not hovered');
  if (h.cursor !== 'grab') bad('the cursor did not offer a grab');
  if (!(h.hoverLift > 0.02)) bad('the hovered man did not lift');

  await hover('e7');   // no height finds him: he is not the side to move
  h = await hand();
  console.log('hover a black pawn (not his turn): ' + JSON.stringify(h));
  if (h.hover || h.cursor !== 'default') bad('the other side answered the cursor');

  // --- 2. a click leaves him waiting, with his squares lit --------------------
  await click('e2');
  h = await hand();
  console.log('clicked: ' + JSON.stringify(h));
  console.log('  ' + await shot('selected.png'));
  if (h.selected !== 'e2') bad('the click did not leave him waiting');
  if (h.markers < 3) bad('the waiting man got no markers');   // e3, e4, and his own square
  if (!(h.selectLift > 0.05)) bad('the waiting man is not lifted');

  // --- 3. Esc lets him go, and reads as busy while it does -------------------
  await evalJs("window.__esc = null; window.addEventListener('keydown', () => "
    + '{ window.__esc = window.PBP.board.drag.isDragging(); });');
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
  await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 });
  await beat(60);
  const escBusy = await evalJs('window.__esc');
  h = await hand();
  console.log('after esc: ' + JSON.stringify(h) + ' (the board read busy during the key: ' + escBusy + ')');
  if (h.selected) bad('esc did not let him go');
  if (escBusy !== true) bad('esc read the board as free, so leaving would fire mid selection');

  // --- 4. click him, click his square: the move plays -------------------------
  const before = await fen();
  await click('e2');
  await click('e4', { man: false });
  await beat(150);
  console.log('  ' + await shot('click-move-flight.png'));
  await beat(400);
  const after = await fen();
  h = await hand();
  console.log('click to move: ' + before.split(' ')[0] + ' -> ' + after.split(' ')[0]);
  console.log('  ' + await shot('click-move-landed.png'));
  if (before === after) bad('the click on a legal square played nothing');
  if (h.selected) bad('the man was still waiting after his move');

  // --- 5. clicking him twice puts him down -----------------------------------
  await beat(1400);                       // the camera has swung round to black
  await click('e7');                      // black to move now
  if ((await hand()).selected !== 'e7') bad('black could not be clicked on his own turn');
  await click('e7');
  h = await hand();
  console.log('clicked twice: ' + JSON.stringify(h));
  if (h.selected) bad('the second click did not put him down');

  // --- 6. reduced motion: the cursor still answers, the lift does not --------
  await evalJs('window.PBP.reducedMotion = true');
  await hover('a7');
  await beat(300);
  h = await hand();
  console.log('reduced motion hover: ' + JSON.stringify(h));
  if (h.cursor !== 'grab') bad('reduced motion lost the cursor');
  if (h.hoverLift > 0.005) bad('reduced motion still lifted him');
  await evalJs('window.PBP.reducedMotion = false');

  // --- 7. take back: the last ply comes home ---------------------------------
  await evalJs("window.__back = []; window.PBP.bus.on('takeback', (p) => window.__back.push(p));");
  const beforeBack = await fen();
  await key('Backspace');
  await beat(90);
  console.log('  ' + await shot('takeback-slide.png'));    // he is on his way home
  await beat(900);
  const afterBack = await fen();
  const backs = await evalJs('JSON.stringify(window.__back)');
  console.log('take back: ' + beforeBack.split(' ').slice(0, 2).join(' ') + ' -> '
    + afterBack.split(' ').slice(0, 2).join(' ') + ' ' + backs);
  console.log('  ' + await shot('takeback-done.png'));
  if (afterBack.split(' ')[0] !== 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR') bad('the ply did not come back');
  if (afterBack.split(' ')[1] !== 'w') bad('the side that moved did not get the move back');
  if (!JSON.parse(backs).length) bad('nothing said a ply had been taken back');
  // The gap between two of them, and then an empty history.
  await key('z');
  await beat(60);
  if ((await fen()).split(' ')[1] !== 'w') bad('a take-back inside the 400 ms gap went through');
  await beat(700);
  await key('z');
  await beat(120);
  const empty = await fen();
  console.log('nothing left to take back: ' + empty.split(' ').slice(0, 2).join(' '));
  if (empty.split(' ')[0] !== 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR') bad('an empty history still changed the board');

  // --- 8. promotion: he is asked what he comes up as -------------------------
  const promoUrl = url + '&fen=' + encodeURIComponent('8/P7/8/8/8/8/8/k6K w - - 0 1');
  await load(promoUrl);
  const fan = () => evalJs('JSON.stringify(window.PBP.board.promote.debug())').then(JSON.parse);
  await click('a7');
  await click('a8', { man: false });
  await beat(400);
  let f = await fan();
  console.log('the fan: ' + JSON.stringify(f) + ' ' + (await fen()).split(' ').slice(0, 2).join(' '));
  if (f.open !== 'a8') bad('the fan did not open on the eighth');
  if (f.kinds.join('') !== 'qrbn') bad('the fan is not queen rook bishop knight');
  if ((await fen()).split(' ')[1] !== 'w') bad('the move was played before he had chosen');
  // Put the cursor on the knight and let the fan say so.
  const at = await evalJs(`(() => {
    const g = window.PBP.board.view.scene.getObjectByName('promote');
    const s = g.children[3];
    const p = window.PBP.board.view.projectPoint(s.position.clone());
    window.__mark(p.x, p.y);
    window.__pt(p.x, p.y, 'pointermove');
    return JSON.stringify({ x: Math.round(p.x), y: Math.round(p.y) });
  })()`);
  await beat(120);
  f = await fan();
  console.log('the cursor on the knight at ' + at + ': ' + JSON.stringify(f));
  console.log('  ' + await shot('promo-fan.png'));
  if (f.hover !== 'n') bad('the fan did not brighten the one under the cursor');
  // And take him.
  const { x, y } = JSON.parse(at);
  await evalJs(`window.__pt(${x}, ${y}, 'pointerdown'); window.__pt(${x}, ${y}, 'pointerup');`);
  await beat(160);
  console.log('  ' + await shot('promo-flight.png'));
  await beat(700);
  const promoted = await fen();
  console.log('promoted: ' + promoted.split(' ').slice(0, 2).join(' ') + ' fan open: ' + (await fan()).open);
  console.log('  ' + await shot('promo-landed.png'));
  if (promoted.split(' ')[0] !== 'N7/8/8/8/8/8/8/k6K') bad('the knight he picked is not on the eighth');
  if ((await fan()).open) bad('the fan stayed up after he chose');
  if (await evalJs('window.PBP.board.drag.isSuspended()')) bad('the board was left standing down');

  // Auto queen: no answer inside the four seconds, and he comes up a queen.
  await load(promoUrl);
  await click('a7');
  await click('a8', { man: false });
  await beat(300);
  if (!(await fan()).open) bad('the second fan did not open');
  await beat(4200);
  const queened = await fen();
  console.log('left alone for four seconds: ' + queened.split(' ').slice(0, 2).join(' '));
  if (queened.split(' ')[0] !== 'Q7/8/8/8/8/8/8/k6K') bad('an unanswered fan did not queen him');

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
  console.log('hand shot done, no console errors');
}

main().catch((err) => {
  console.error(err.message || err);
  edge.kill();
  process.exit(2);
});
