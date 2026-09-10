/* ============================================================================
 * race/smoke/loom-spiral-check.mjs - OUR OWN SPIRALS, on the glass.
 *
 *   node race/smoke/loom-spiral-check.mjs      (from Resources/web/dtrh; 0 on pass)
 *
 * The book itself is race/smoke/loom-book-check.mjs, which needs only node.
 * This one drives a headless browser through a real run, because the things
 * this change can get wrong that no unit test sees are "the canvas never
 * reached the hold", "it is a 4K backing store on a phone", "it is still
 * spinning a minute after the pop", "a lost context leaves the screen bare" and
 * "reduced motion got an animation anyway". It also fires a DtRH-shaped spiral
 * on a payloadFx built WITHOUT the seam, which is the guard that the Descent
 * still paints a gif background and nothing else.
 *
 * CHROME: `CHROME_PATH` if it is set, else the usual Windows install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { readFileSync, mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = resolve(fileURLToPath(import.meta.url), '..');
const RACE = resolve(HERE, '..');
const DTRH = resolve(RACE, '..');
const WEB = resolve(DTRH, '..');                        // Resources/web

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };
const eq = (got, want, what) => ok(got === want, what + ' (got ' + JSON.stringify(got) + ', want ' + JSON.stringify(want) + ')');
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

{
  // the seams are the race's alone: nothing in game/ reaches for the race's files
  const pfx = readFileSync(resolve(DTRH, 'game/payloadFx.js'), 'utf8');
  const run = readFileSync(resolve(RACE, 'run.js'), 'utf8');
  ok(/spiralFx\s*=\s*null/.test(pfx), 'payloadFx defaults the seam to null, so the Descent keeps its gif');
  ok(!/from\s+'\.\.\/race\//.test(pfx), 'and game/payloadFx.js imports nothing out of race/');
  ok(/spiralFx\s*\}\)/.test(run) || /spiralFx\s*[,}]/.test(run), 'run.js is the caller that passes one');
  ok(/setLoomBook\(null\)/.test(run), 'and takes the book away again on dispose');
}
console.log('');
console.log('');

/* ============================================================================
 * the browser
 * ==========================================================================*/
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8878, DEBUG_PORT = 9348;
const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json', '.glb': 'model/gltf-binary', '.png': 'image/png', '.webp': 'image/webp', '.jpg': 'image/jpeg', '.gif': 'image/gif', '.mp3': 'audio/mpeg', '.wav': 'audio/wav', '.svg': 'image/svg+xml', '.woff2': 'font/woff2' };

if (!existsSync(CHROME)) { console.error('FAIL no chrome at ' + CHROME + ' (set CHROME_PATH)'); process.exit(1); }
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.indexOf('..') >= 0) { res.writeHead(400); return res.end(); }
  try {
    const body = await readFile(join(WEB, path));
    res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' });
    res.end(req.method === 'HEAD' ? undefined : body);
  } catch (e) { res.writeHead(404, { 'content-type': 'text/plain' }); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));

const prof = mkdtempSync(join(tmpdir(), 'race-loom-'));
const chrome = spawn(CHROME, [
  '--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`,
  '--no-first-run', '--no-default-browser-check', '--disable-gpu', '--mute-audio',
  '--enable-unsafe-swiftshader', '--autoplay-policy=no-user-gesture-required',
  '--window-size=1280,720', 'about:blank',
], { stdio: 'ignore' });

let page = null;
for (let i = 0; i < 60 && !page; i++) {
  await sleep(250);
  try { page = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch (e) { /* not up */ }
}
if (!page) { console.error('FAIL chrome never answered on the debug port'); await done(1); }

const ws = new WebSocket(page.webSocketDebuggerUrl);
await new Promise((r) => { ws.onopen = r; });
let msgId = 0;
const waits = new Map(), errs = [];
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push('thrown: ' + (m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text || '?'));
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push(m.params.args.map((a) => a.value ?? a.description ?? '?').join(' '));
};
const cdp = (method, params) => new Promise((res) => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
const json = async (x) => JSON.parse(await ev(`JSON.stringify(${x})`));
/** For an expression that ALREADY returns a JSON string (the async readers below): a promise
 *  has to be awaited BEFORE it is stringified, or JSON.stringify just writes "{}". */
const parse = async (x) => JSON.parse(await ev(x));
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Emulation.setDeviceMetricsOverride', { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });

const site = `http://127.0.0.1:${PORT}`;
await cdp('Page.navigate', { url: `${site}/dtrh/race.html?autostart=1&intro=0&cards=0` });
let up = false;
for (let i = 0; i < 120 && !up; i++) {
  await sleep(250);
  up = await ev(`!!(window.__race && window.__race.race && window.__race.race.perf().running)`);
}
ok(up, 'the page boots and the run is going');
if (!up) { if (errs.length) console.error('    said: ' + errs.slice(0, 6).join(' | ')); await done(1); }

/** What is inside the spiral hold right now. */
const HOLD = `(() => {
  const el = document.querySelector('.sf-pfx-spiral');
  if (!el) return JSON.stringify({ there: false });
  const c = el.querySelector('canvas.rh-loom-spiral');
  const cs = getComputedStyle(el);
  return JSON.stringify({ there: true, canvas: !!c, canvases: document.querySelectorAll('canvas.rh-loom-spiral').length,
    w: c ? c.width : 0, h: c ? c.height : 0, cw: c ? Math.round(c.getBoundingClientRect().width) : 0,
    bg: cs.backgroundImage, anim: cs.animationName, blend: cs.mixBlendMode,
    op: cs.opacity, canvasOp: c ? getComputedStyle(c).opacity : null, canvasBlend: c ? getComputedStyle(c).mixBlendMode : null });
})()`;

/* ============================================================================
 * 2. a spiral pop mounts a live canvas in the hold
 * ==========================================================================*/
ok(await ev(`window.__race.race.debugPayload('overlay', { overlay: 'spiral', strength: 70 })`), 'a spiral payload fires');
await sleep(300);
let h = JSON.parse(await ev(HOLD));
ok(h.there && h.canvas, 'a live Loom canvas is inside .sf-pfx-spiral');
eq(h.canvases, 1, 'exactly one, ever');
ok(Math.max(h.w, h.h) <= 512, `the backing store is ${h.w}x${h.h}, at or under the 512 long side`);
ok(h.cw >= 1200, `and it is CSS-stretched to the full ${h.cw}px of glass`);
eq(h.bg, 'none', 'the gif background stood down');
eq(h.anim, 'none', "and so did the element's own 14s CSS spin (the shader owns rotation)");
eq(h.blend, 'screen', 'the hold keeps its screen blend, which the canvas inherits');
eq(h.canvasOp, '1', 'the canvas has no opacity of its own: the hold is the one intensity channel');
eq(h.canvasBlend, 'normal', 'and no blend mode of its own either');
const diag = await json(`window.__race.race.loom.stats()`);
ok(diag.holds.length === 1 && /^loom:[0-9a-f]{8}$/.test(diag.holds[0].id), `the hold names the weave it is drawing (${diag.holds[0].id})`);
ok(diag.frames > 1, `and it is animating (${diag.frames} frames so far)`);

/* ============================================================================
 * 3. it lets go: the canvas is gone once the hold is over
 * ==========================================================================*/
{
  const before = (await json(`window.__race.race.loom.stats()`)).frames;
  await sleep(1200);
  const mid = (await json(`window.__race.race.loom.stats()`)).frames;
  ok(mid > before, 'it keeps drawing while the hold is up');
}
// a strength-70 spiral holds ~3.6 s, plus payloadFx's 0.45 s fade and the manager's breath
await sleep(4200);
h = JSON.parse(await ev(HOLD));
ok(!h.canvas, 'the canvas is off the element once the hold has faded');
eq(h.canvases, 0, 'nothing is left in the page');
eq(h.anim, 'sf-pfx-spin', 'and the element has its own CSS spin back for the gif path');
{
  const after = (await json(`window.__race.race.loom.stats()`)).frames;
  await sleep(500);
  eq((await json(`window.__race.race.loom.stats()`)).frames, after, 'and nothing is still drawing behind an invisible layer');
}

/* ============================================================================
 * 4. reduced motion: one frame, no loop
 * ==========================================================================*/
{
  await ev(`window.__race.race.dispose && 0`);   // no-op: keep the run, only the media query changes
  await cdp('Emulation.setEmulatedMedia', { features: [{ name: 'prefers-reduced-motion', value: 'reduce' }] });
  // the run built its manager with the run's own reducedMotion, so this half is asserted on a
  // fresh manager the page makes for us - the same file, the same flag, no second run
  const rm = await parse(`(async () => {
    const m = await import('/dtrh/race/loomSpiralFx.js');
    const b = await import('/dtrh/race/loomBook.js');
    const fx = m.createLoomSpiralFx({ reducedMotion: true });
    const el = document.createElement('div');
    el.className = 'sf-pfx-layer sf-pfx-spiral rh-loom-probe';
    document.querySelector('.sf-pfx').appendChild(el);
    const wrap = b.createLoomBook({ seed: 5 }).draw({ room: 'chapel' });
    const took = fx.mount(el, wrap, { kind: 'spiral', durMs: 4000 });
    const first = fx.diagnostics().frames;
    const size = fx.diagnostics().holds[0] ? fx.diagnostics().holds[0].backing : null;
    await new Promise((r) => setTimeout(r, 600));
    const later = fx.diagnostics().frames;
    fx.dispose(); el.remove();
    return JSON.stringify({ took, first, later, size });
  })()`);
  ok(rm.took, 'reduced motion still mounts a canvas');
  eq(rm.first, 1, 'and draws exactly one frame');
  eq(rm.later, 1, 'and never arms the loop');
  ok(rm.size, `at ${rm.size}`);
  await cdp('Emulation.setEmulatedMedia', { features: [] });
}

/* ============================================================================
 * 5. the WebGL floor: a lost context paints a gif
 * ==========================================================================*/
await ev(`window.__race.race.debugPayload('overlay', { overlay: 'spiral', strength: 70 })`);
await sleep(250);
ok(JSON.parse(await ev(HOLD)).canvas, 'a fresh pop is live again');
eq(await ev(`window.__race.race.loom.breakGl()`), 1, 'the context goes');
await sleep(120);
h = JSON.parse(await ev(HOLD));
ok(!h.canvas, 'the canvas came off with it');
ok(/url\(.*(gif|webp)/.test(h.bg), `and the hold is painting a gif instead (${h.bg.slice(0, 70)})`);
eq(h.anim, 'sf-pfx-spin', 'with the element spinning it, exactly as it always did');
ok((await json(`window.__race.race.loom.stats()`)).lost === true, 'the manager latched the loss');
// and it stays on the gif: no more canvases after a loss
await ev(`window.__race.race.debugPayload('overlay', { overlay: 'spiral', strength: 70 })`);
await sleep(250);
eq(JSON.parse(await ev(HOLD)).canvases, 0, 'every pop after a loss is a gif, with no retry storm');

/* ============================================================================
 * 6. the Descent is untouched: no seam, no canvas, a background url
 * ==========================================================================*/
{
  const tube = await parse(`(async () => {
    const m = await import('/dtrh/game/payloadFx.js');
    const hud = document.createElement('div');
    hud.style.cssText = 'position:fixed;inset:0;pointer-events:none;opacity:0';
    document.body.appendChild(hud);
    const pfx = m.createPayloadFx({ hud, fx: { pulseFlash() {} }, media: null, flashBurst: null });
    pfx.applyPayload({ payload: { kind: 'overlay', overlay: 'spiral' }, strength: 60 }, {});
    await new Promise((r) => setTimeout(r, 60));
    const el = hud.querySelector('.sf-pfx-spiral');
    const cs = el ? getComputedStyle(el) : null;
    const got = { there: !!el, canvas: el ? !!el.querySelector('canvas') : null,
      bg: cs ? cs.backgroundImage : null, anim: cs ? cs.animationName : null, scale: cs ? cs.scale : null };
    pfx.dispose(); hud.remove();
    return JSON.stringify(got);
  })()`);
  ok(tube.there, 'a payloadFx with no seam still makes the spiral hold');
  eq(tube.canvas, false, 'and puts NO canvas in it');
  ok(/url\(/.test(String(tube.bg)), `it paints a background url, the way dtrh.html always has (${String(tube.bg).slice(0, 60)})`);
  eq(tube.anim, 'sf-pfx-spin', "with the element's own spin");
  eq(tube.scale, '1.6', 'and its own 1.6 overscan');
}

/* ============================================================================
 * 7. a phone: the small backing store
 * ==========================================================================*/
{
  const phone = await parse(`(async () => {
    const m = await import('/dtrh/race/loomSpiralFx.js');
    return JSON.stringify({ desk: m.backingFor(1280, 720, false), touch: m.backingFor(390, 844, true), cap: m.TOUCH_FRAME_MS });
  })()`);
  ok(Math.max(phone.desk.w, phone.desk.h) === 512, `a 1280x720 desktop draws ${phone.desk.w}x${phone.desk.h}`);
  ok(Math.max(phone.touch.w, phone.touch.h) === 256, `a 390x844 phone draws ${phone.touch.w}x${phone.touch.h}`);
  eq(phone.cap, 42, 'and paces itself at 24 fps under touch');
}

/* ============================================================================
 * 7b. the phone's layers carry no filter (2026-09-10: "we still lag a lot on the
 *     fullscreen effects on iphone. the glitch bubble fullscreen in particular
 *     and the spiral"). Every hold is a fullscreen DOM layer over the glass; a
 *     `filter`, a `backdrop-filter` or a colour keyframe on one is a
 *     device-resolution pass on every frame it changes. On the touch tier the
 *     cap is inline (payloadFx `opacityCap`) and race.css takes the filters off.
 * ==========================================================================*/
{
  const run = readFileSync(resolve(RACE, 'run.js'), 'utf8');
  ok(/opacityCap:\s*touchTier\s*\?/.test(run), 'run.js passes the inline cap on the touch tier only');
  ok(/dataset\.touch\s*=\s*'1'/.test(run), 'and stamps data-touch on the root for race.css');
  const LAYER = (sel) => `(() => { const el = document.querySelector('${sel}'); if (!el) return JSON.stringify({ there: false });
    const cs = getComputedStyle(el); return JSON.stringify({ there: true, filter: cs.filter, bf: cs.backdropFilter || cs.webkitBackdropFilter, anim: cs.animationName, mask: cs.maskImage || cs.webkitMaskImage }); })()`;
  // the desktop this run is on: the caps and the drain's blur are the filters they always were
  await ev(`window.__race.race.debugPayload('glitch', { strength: 70 })`);
  await ev(`window.__race.race.debugPayload('overlay', { overlay: 'spiral', strength: 70 })`);
  await sleep(200);
  let d = JSON.parse(await ev(LAYER('.sf-pfx-drain'))), sp = JSON.parse(await ev(LAYER('.sf-pfx-spiral')));
  // (mid-glitch the keyframe's hue-rotate is the computed filter, over the 0.86 cap: still a filter pass)
  ok(d.there && d.filter !== 'none', `the desktop drain runs through a filter (${d.filter})`);
  ok(/blur/.test(d.bf), `and its backdrop blur (${d.bf})`);
  eq(d.anim, 'sf-pfx-glitch', 'and the glitch shudders with its colour keyframes');
  ok(/opacity\(0\.78\)/.test(sp.filter), `the desktop spiral keeps its filter cap (${sp.filter})`);
  // the same page stamped as a phone
  await ev(`document.getElementById('race-root').dataset.touch = '1'`);
  await sleep(50);
  d = JSON.parse(await ev(LAYER('.sf-pfx-drain'))); sp = JSON.parse(await ev(LAYER('.sf-pfx-spiral')));
  eq(d.filter, 'none', 'on the touch tier the drain carries no filter');
  eq(d.bf, 'none', 'and no backdrop blur');
  eq(d.anim, 'rhGlitchLite', 'the glitch shudders on transform alone');
  ok(/radial-gradient/.test(d.mask), 'the road cutout stays (a mask is a composite, not a filter pass)');
  eq(sp.filter, 'none', 'the spiral hold carries no filter either');
  const g = JSON.parse(await ev(LAYER('.sf-pfx-gifwash')));
  if (g.there) eq(g.filter, 'none', 'nor does the wash');
  // a spiral takes the slot: the glitch's shudder is over and the drain is the hold that lost
  await ev(`(() => { const r = document.getElementById('race-root'); r.dataset.ov = 'spiral'; document.querySelector('.sf-pfx-drain').classList.remove('sf-pfx-glitching'); return 1; })()`);
  await sleep(600);   // the layer's own 0.45 s opacity ease
  const cross = await ev(`(() => { const cs = getComputedStyle(document.querySelector('.sf-pfx-drain')); return cs.filter + '|' + cs.opacity; })()`);
  eq(cross, 'none|0', 'the MIX replace crossfades the drain out through opacity, not a filter');
  await ev(`(() => { const r = document.getElementById('race-root'); delete r.dataset.ov; delete r.dataset.touch; return 1; })()`);
  // the cap itself: a fresh payloadFx with the seam, the numbers the desktop css holds
  const cap = await parse(`(async () => {
    const m = await import('/dtrh/game/payloadFx.js');
    const hud = document.createElement('div');
    hud.style.cssText = 'position:fixed;inset:0;pointer-events:none;opacity:0';
    document.body.appendChild(hud);
    const asked = [];
    const pfx = m.createPayloadFx({ hud, fx: { pulseFlash() {} }, media: null, flashBurst: null,
      opacityCap: (kind) => { asked.push(kind); return kind === 'spiral' ? 0.78 : kind === 'braindrain' ? 0.86 : 1; } });
    pfx.applyPayload({ payload: { kind: 'overlay', overlay: 'spiral' }, strength: 60 }, {});
    pfx.applyPayload({ payload: { kind: 'glitch' }, strength: 60 }, {});
    pfx.applyPayload({ payload: { kind: 'overlay', overlay: 'pink_filter' }, strength: 60 }, {});
    await new Promise((r) => setTimeout(r, 60));
    const op = (c) => { const el = hud.querySelector(c); return el ? el.style.opacity : null; };
    const got = { asked, spiral: op('.sf-pfx-spiral'), drain: op('.sf-pfx-drain'), pink: op('.sf-pfx-pink') };
    pfx.dispose(); hud.remove();
    return JSON.stringify(got);
  })()`);
  // strength 60: spiral/pink 0.25..0.70 -> 0.52; drain 0.35..0.62 -> 0.512
  ok(Math.abs(parseFloat(cap.spiral) - 0.52 * 0.78) < 1e-6, `the spiral hold is set inline at 0.52 x 0.78 (${cap.spiral})`);
  ok(Math.abs(parseFloat(cap.drain) - 0.512 * 0.86) < 1e-6, `the drain at 0.512 x 0.86 (${cap.drain})`);
  ok(Math.abs(parseFloat(cap.pink) - 0.52) < 1e-6, `and a kind the seam answers 1 for is untouched (${cap.pink})`);
  ok(cap.asked.includes('spiral') && cap.asked.includes('braindrain') && cap.asked.includes('pink'), 'the seam is asked by hold kind');
  await sleep(4500);   // let the holds fade before the console tally
}

/* ============================================================================
 * 7c. the second pass (2026-09-10: "smoother but still not good enough"): the
 *     rest of the per-frame filters and repaints come off the touch tier -
 *     the melt's drip, the flash burst's drop-shadow, the blink / melt / fog
 *     plates, the lacquer's breath, the subliminal's rush, the strobe's blur.
 *     And the governor has a lower rung on a coarse pointer.
 * ==========================================================================*/
{
  const CS = (sel) => `(() => { const el = document.querySelector('${sel}'); if (!el) return JSON.stringify({ there: false });
    const cs = getComputedStyle(el); return JSON.stringify({ there: true, filter: cs.filter, anim: cs.animationName, shadow: cs.boxShadow, bg: cs.backgroundPositionY }); })()`;
  // probes: one of each kind, stamped as the phone, read, then taken away again
  const probe = await parse(`(async () => {
    const r = document.getElementById('race-root'); r.dataset.touch = '1';
    const hud = r.querySelector('.race-hud') || r;
    const pfx = r.querySelector('.sf-pfx') || r;
    const mk = (parent, cls, tag = 'div') => { const e = document.createElement(tag); e.className = cls; parent.appendChild(e); return e; };
    const els = [
      mk(pfx, 'sf-pfx-layer sf-pfx-pink is-melting rh-probe'),
      mk(pfx, 'sf-pfx-flash rh-probe', 'img'),
      mk(pfx, 'sf-pfx-cascade rh-probe', 'img'),
      mk(hud, 'rc-plate rc-plate--blink rh-probe'),
      mk(hud, 'rc-plate rc-plate--melt rh-probe'),
      mk(hud, 'rc-plate rc-plate--fog rh-probe'),
      mk(hud, 'rh-strobe rh-probe'),
    ];
    const lac = mk(pfx, 'sf-pfx-freeze is-lacquer rh-probe'); const span = document.createElement('span'); span.textContent = 'x'; lac.appendChild(span);
    const sub = mk(hud, 'rh-sub-layer is-on rh-probe'); const card = mk(sub, 'rh-sub-card');
    await new Promise((res) => setTimeout(res, 40));
    const read = (el) => { const cs = getComputedStyle(el); return { filter: cs.filter, anim: cs.animationName, shadow: cs.boxShadow }; };
    const got = { melt: read(els[0]), flash: read(els[1]), cascade: read(els[2]), blink: read(els[3]), pmelt: read(els[4]), fog: read(els[5]), strobe: read(els[6]), lacquer: read(span), sub: read(card) };
    for (const e of r.querySelectorAll('.rh-probe')) e.remove();
    delete r.dataset.touch;
    return JSON.stringify(got);
  })()`);
  eq(probe.melt.anim, 'rhSagLite', 'the melt sags on transform alone, no drip repainting the gradient');
  eq(probe.flash.filter, 'none', 'a flash burst carries no drop-shadow filter');
  ok(/rgba?\(/.test(probe.flash.shadow) && probe.flash.shadow !== 'none', `its glow is a box-shadow, painted once (${probe.flash.shadow.slice(0, 40)})`);
  eq(probe.cascade.filter, 'none', 'nor does a falling gif');
  eq(probe.blink.anim, 'rcZoom, rcBlinkLite', "the blink plate blinks in colour, not filter: brightness()");
  eq(probe.blink.filter, 'none', 'and carries no filter');
  eq(probe.pmelt.anim, 'rcMeltLite', 'the melt plate lets go without a blur');
  eq(probe.fog.anim, 'rcFogLite', 'so does the fog plate');
  eq(probe.lacquer.anim, 'rhLacquerLite', 'the doll breathes on scale alone');
  eq(probe.sub.anim, 'rhSubRushLite', 'the subliminal card rushes without a blur');
  eq(probe.sub.filter, 'none', 'and carries none at rest');
  ok(!/40px/.test(probe.strobe.shadow), `the strobe edge is the 5 px line alone (${probe.strobe.shadow.slice(0, 60)})`);
  // the desktop, untouched: the same probes without the stamp still carry what they always did
  const desk = await parse(`(async () => {
    const r = document.getElementById('race-root');
    const hud = r.querySelector('.race-hud') || r;
    const p = document.createElement('div'); p.className = 'rc-plate rc-plate--blink rh-probe'; hud.appendChild(p);
    const f = document.createElement('img'); f.className = 'sf-pfx-flash rh-probe'; r.appendChild(f);
    await new Promise((res) => setTimeout(res, 40));
    const got = { blink: getComputedStyle(p).animationName, flash: getComputedStyle(f).filter };
    p.remove(); f.remove();
    return JSON.stringify(got);
  })()`);
  eq(desk.blink, 'rcZoom, rcBlink', 'the desktop blink plate still blinks with brightness');
  ok(/drop-shadow/.test(desk.flash), 'and the desktop flash burst keeps its drop-shadow');
  // the governor's rungs
  const gov = await parse(`(async () => {
    const m = await import('/dtrh/race/pixel.js');
    const q = await import('/dtrh/shared/quality.js');
    const st = window.__race.race.perf();
    return JSON.stringify({ floor: m.TOUCH_DPR_FLOOR, sec: m.GOV_TOUCH_SEC,
      coarseOn: m.coarsePointer({ location: { search: '?coarse=1' } }), coarseOff: m.coarsePointer({ location: { search: '?coarse=0' }, matchMedia: () => ({ matches: true }) }),
      touch: st ? st.touch : null, cap: st ? st.dprCap : null });
  })()`);
  eq(gov.floor, 0.8, 'a coarse pointer can drop to 0.8 device pixels per css pixel');
  eq(gov.sec, 2, 'and is read every two seconds');
  ok(gov.coarseOn === true && gov.coarseOff === false, '?coarse= forces the answer either way');
  eq(gov.touch, false, 'this desktop run is not on the touch ladder');
}

/* ============================================================================
 * 8. and nothing said a word about it
 * ==========================================================================*/
ok(errs.length === 0, 'not one console error in the whole run' + (errs.length ? ':\n    ' + errs.slice(0, 5).join('\n    ') : ''));

await done(fails ? 1 : 0);

async function done(code) {
  try { ws && ws.close(); } catch (e) { /* never opened */ }
  try { chrome.kill(); } catch (e) { /* already gone */ }
  await new Promise((r) => server.close(r));
  await sleep(300);
  try { rmSync(prof, { recursive: true, force: true }); } catch (e) { /* chrome still has a lock */ }
  if (code) console.error(`\n${fails} failure${fails === 1 ? '' : 's'}`);
  else console.log('\nloom-spiral-check: all good');
  process.exit(code);
}
