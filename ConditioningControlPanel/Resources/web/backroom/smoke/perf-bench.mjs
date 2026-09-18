/* perf-bench.mjs - where the room spends its frame. Real Chrome in an OFF-SCREEN window (nothing lands on
 * the desk), the smoke checks' fake WPF host, and for each scenario: the room's own fps, draw calls and
 * triangles per rendered frame, texture uploads per second by size, main-thread busy share with a CPU
 * profile by file and function, and (Windows) the CPU seconds of Chrome's renderer and GPU processes.
 *   node backroom/smoke/perf-bench.mjs [rest walk slot wheel cards roulette probe shots]
 * `probe` attributes every draw call to its fixture and re-measures with groups hidden one at a time;
 * `shots` saves counter and slot screenshots (BENCH_TAG names them). BENCH_WEB points it at another
 * Resources/web (a worktree) for a before/after; BENCH_W/H/DSF set the viewport; BENCH_MS the sample.
 * Numbers from one machine only compare against the same machine: see room/PERFORMANCE.md. */
import { spawn, execSync } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, extname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const WEB = process.env.BENCH_WEB || resolve(fileURLToPath(import.meta.url), '../../..');   // Resources/web of THIS tree
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = 8993, DEBUG_PORT = 9493;
const W = +(process.env.BENCH_W || 1727), H = +(process.env.BENCH_H || 942), DSF = +(process.env.BENCH_DSF || 1.25);
const PROFILE_MS = +(process.env.BENCH_MS || 6000);
const scenarios = process.argv.slice(2).length ? process.argv.slice(2) : ['rest', 'walk', 'slot', 'wheel', 'cards', 'roulette'];
const sleep = ms => new Promise(r => setTimeout(r, ms));

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css', '.json': 'application/json',
  '.png': 'image/png', '.webp': 'image/webp', '.gif': 'image/gif', '.svg': 'image/svg+xml', '.glb': 'model/gltf-binary', '.mp3': 'audio/mpeg' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise(r => server.listen(PORT, '127.0.0.1', r));

const FAKE_HOST = `(() => {
  const listeners = [], emit = (data) => setTimeout(() => listeners.forEach((fn) => fn({ data })), 0);
  const gates = { flash: true, subliminal: true, spiral: true, brainDrain: true, tunnel: true };
  const made = {};
  const mock = (id) => made[id] || (made[id] = import(id === 'bell' ? '/backroom/smoke/mock-bell.js' : '/backroom/stations/' + id + '/mock-server.js').then((m) => {
    const s = id === 'bell' ? m.createBellMock({}) : id === 'cards' ? m.createMockServer({ sp: 5700, floorMs: 600 }) : id === 'roulette' ? m.createMockServer({ sp: 5700, floorMs: 0 })
      : id === 'counter' ? m.createMockServer({ sp: 5700, on: 'jackpot_remix,rt_demo,high_roller' }) : m.createMockServer({ sp: 5700 });
    window.__servers[id] = s; return s; }));
  window.__hostEmit = emit; window.__posted = []; window.__servers = {};
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    addEventListener(type, fn) { if (type === 'message') listeners.push(fn); },
    postMessage(m) {
      window.__posted.push(JSON.parse(JSON.stringify(m)));
      if (m.type === 'ready') emit({ type: 'init', protocol: 1, sp: 5700, reduced: false, motion: 'full', intensity: 'normal', lang: 'en', gates, welcomeSeen: true,
        lex: { br_back: 'Back', br_balance: 'SP' }, stations: ['slot', 'wheel', 'cards', 'roulette', 'counter', 'bell'], open: true });
      if (m.type === 'station-request') mock(m.station).then((s) => s.handle(m.op, m.body || {}, m.idem))
        .then((r) => emit({ type: 'station-result', reqId: m.reqId, ok: r.ok, status: r.status, reason: r.reason, body: r.body || {} }));
      if (m.type === 'media-request') emit({ type: 'media', reqId: m.reqId, seed: 1, words: [],
        gifs: Array.from({ length: m.count || 4 }, (_, i) => ({ key: 'g' + i, url: '/backroom/stations/slot/fallback/gif' + (i % 4) + '.webp', w: 180, h: 180, src: 'pool' })) });
      if (m.type === 'fx') emit({ type: 'fx-ack', token: m.token, fired: [m.fxId], skipped: [] });
      if (m.type === 'word.speak') emit({ type: 'word-ack', token: m.token, source: 'none' });
    },
  };
})();`;

const prof = mkdtempSync(join(tmpdir(), 'backroom-bench-'));
const chrome = spawn(CHROME, [`--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run', '--no-default-browser-check', '--mute-audio',
  '--hide-scrollbars', `--window-size=${W + 16},${H + 80}`, '--window-position=-4200,100', '--disable-backgrounding-occluded-windows', '--disable-renderer-backgrounding',
  '--disable-features=CalculateNativeWinOcclusion', '--autoplay-policy=no-user-gesture-required', ...(process.env.BENCH_FLAGS ? process.env.BENCH_FLAGS.split(' ') : []), 'about:blank'], { stdio: 'ignore' });
async function done(code) { try { chrome.kill(); } catch {} server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch {} process.exit(code); }
let target = null;
for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find(t => t.type === 'page'); } catch {} }
if (!target) { console.error('chrome never answered'); await done(1); }
const ws = new WebSocket(target.webSocketDebuggerUrl);
await new Promise(r => { ws.onopen = r; });
let msgId = 0; const waits = new Map(), errs = [];
ws.onmessage = e => { const m = JSON.parse(e.data); if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); }
  if (m.method === 'Runtime.exceptionThrown') errs.push(m.params.exceptionDetails?.exception?.description || m.params.exceptionDetails?.text);
  if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errs.push('console: ' + m.params.args.map(a => a.value || a.description).join(' ')); };
const cdp = (method, params) => new Promise(res => { const i = ++msgId; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
const ev = async x => { const r = await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true }); if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description || r.result.exceptionDetails.text); return r.result?.result?.value; };
await cdp('Runtime.enable'); await cdp('Page.enable');
await cdp('Page.addScriptToEvaluateOnNewDocument', { source: FAKE_HOST });
await cdp('Emulation.setDeviceMetricsOverride', { width: W, height: H, deviceScaleFactor: DSF, mobile: false });
async function until(expr, ms = 20000, step = 50) { const t = Date.now(); while (Date.now() - t < ms) { if (await ev(expr)) return true; await sleep(step); } return false; }
const keyEv = (code, type) => cdp('Input.dispatchKeyEvent', { type, code, key: code.slice(3).toLowerCase(), windowsVirtualKeyCode: code.charCodeAt(3) });

const SAMPLER = `(ms) => new Promise(done => {
  const r = __backroom.scene.renderer; const canvas = r.domElement;
  const t0 = performance.now(); let last = t0; const gaps = []; let rafs = 0, calls = 0, tris = 0, longTasks = 0, longMs = 0, rendered = 0, lastFrame = r.info.render.frame;
  const gl = r.getContext(); const up = { calls: 0, px: 0, sizes: {} };
  const area = a => { const s = a[a.length - 1]; if (s && typeof s === 'object' && 'width' in s) return [s.width, s.height]; if (a.length >= 9) return [a[4], a[5]]; if (a.length >= 7 && typeof a[3] === 'number') return [a[3], a[4]]; return [0, 0]; };
  const wrap = name => { const orig = gl[name].bind(gl); gl[name] = (...a) => { const [w, h] = area(a); up.calls++; up.px += w * h; const k = w + 'x' + h; up.sizes[k] = (up.sizes[k] || 0) + 1; return orig(...a); }; return () => { gl[name] = orig; }; };
  const unwrap = [wrap('texSubImage2D'), wrap('texImage2D')];
  const po = new PerformanceObserver(l => { for (const e of l.getEntries()) { longTasks++; longMs += e.duration; } }); try { po.observe({ entryTypes: ['longtask'] }); } catch {}
  const tick = now => { rafs++; gaps.push(now - last); last = now; if (r.info.render.frame !== lastFrame) { rendered++; lastFrame = r.info.render.frame; calls += r.info.render.calls; tris += r.info.render.triangles; } if (now - t0 < ms) requestAnimationFrame(tick); else finish(); };
  requestAnimationFrame(tick);
  function finish() { po.disconnect(); unwrap.forEach(f => f());
    const s = gaps.slice(1).sort((a, b) => a - b), pct = p => +s[Math.min(s.length - 1, Math.floor(s.length * p))].toFixed(1);
    const q = window.__backroomQuality; const dbg = gl.getExtension('WEBGL_debug_renderer_info');
    const sizes = Object.entries(up.sizes).sort((a, b) => b[1] - a[1]).slice(0, 8).map(([k, n]) => k + ' x' + n).join(', ');
    done({ browserHz: +(rafs / (ms / 1000)).toFixed(1), roomFps: +(rendered / (ms / 1000)).toFixed(1), gap: { p50: pct(.5), p90: pct(.9), p99: pct(.99), max: +Math.max(...s).toFixed(1) }, longTasks, longMs: Math.round(longMs),
      callsPerFrame: Math.round(calls / Math.max(1, rendered)), trisPerFrame: Math.round(tris / Math.max(1, rendered)), uploadsPerFrame: +(up.calls / Math.max(1, rendered)).toFixed(1), uploadMpxPerSec: +(up.px / 1e6 / (ms / 1000)).toFixed(1), uploadSizes: sizes,
      quality: q && { mode: q.mode, performance: q.performance }, canvas: [canvas.width, canvas.height],
      memory: { geometries: r.info.memory.geometries, textures: r.info.memory.textures, programs: r.info.programs.length }, gpu: dbg && gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL), heapMB: Math.round(performance.memory.usedJSHeapSize / 1048576) }); }
})`;

async function profile(ms, during) {
  await cdp('Profiler.enable'); await cdp('Profiler.setSamplingInterval', { interval: 250 }); await cdp('Profiler.start');
  await during(ms);
  const { result: { profile } } = await cdp('Profiler.stop'); await cdp('Profiler.disable');
  const nodes = new Map(profile.nodes.map(n => [n.id, n])); const self = new Map();
  for (let i = 0; i < profile.samples.length; i++) self.set(profile.samples[i], (self.get(profile.samples[i]) || 0) + (profile.timeDeltas[i] || 0));
  const total = [...self.values()].reduce((a, b) => a + b, 0) / 1000; let busy = 0;
  const byFn = new Map(), byUrl = new Map();
  for (const [id, us] of self) { const cf = nodes.get(id).callFrame; if (cf.functionName === '(idle)') continue; busy += us / 1000;
    const url = (cf.url || '(native)').replace(/^.*\/web\//, ''); const fk = `${cf.functionName || '(anon)'} ${url}:${cf.lineNumber + 1}`;
    byFn.set(fk, (byFn.get(fk) || 0) + us); byUrl.set(url, (byUrl.get(url) || 0) + us); }
  const top = (m, n) => [...m].sort((a, b) => b[1] - a[1]).slice(0, n).map(([k, us]) => `${(us / 1000).toFixed(0).padStart(6)} ms ${(100 * us / 1000 / total).toFixed(1).padStart(5)}%  ${k}`).join('\n');
  return `main thread busy ${busy.toFixed(0)} ms of ${total.toFixed(0)} ms (${(100 * busy / total).toFixed(0)}%)\n-- by file\n${top(byUrl, 12)}\n-- by function\n${top(byFn, 28)}`;
}
const cpuOf = () => { try { return JSON.parse(execSync('powershell -NoProfile -Command "Get-Process chrome | Select Id,CPU,WorkingSet64,@{n=\'cmd\';e={(Get-CimInstance Win32_Process -Filter (\'ProcessId=\' + $_.Id)).CommandLine}} | ConvertTo-Json"', { encoding: 'utf8' })); } catch { return []; } };
const typeOf = p => (/--type=(\S+)/.exec(p.cmd || '') || [, 'browser'])[1];
async function scenario(name, prepare, during = sleep) {
  console.log(`\n===== ${name}`);
  await prepare();
  await sleep(1500);
  const before = cpuOf();
  const sampled = await ev(`(${SAMPLER})(${PROFILE_MS})`);   // during is applied only for the profile; the sample is the steady state
  const after = cpuOf();
  const cpu = {}; for (const p of after) { const b = before.find(x => x.Id === p.Id); if (b) { const t = typeOf(p); cpu[t] = +((cpu[t] || 0) + (p.CPU - b.CPU)).toFixed(2); } }
  console.log(JSON.stringify({ ...sampled, cpuSecondsOver: PROFILE_MS / 1000, cpuByProcess: cpu }, null, 1));
  console.log(await profile(PROFILE_MS, during));
}

await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/backroom/index.html` });
if (!await until("document.documentElement.classList.contains('br-ready')", 60000)) { console.error('room never booted', errs); await done(1); }
await sleep(3000);
const seatAt = async key => { await ev(`__backroom.visit(__backroom.stations.find(s=>s.key===${JSON.stringify(key)}))`); await until("!__backroom.scene.transitioning && __backroom.loader.current", 20000); await sleep(2500); };
const back = async () => { await ev('__backroom.back("back")'); await sleep(3000); };
const keys = await ev('__backroom.stations.map(s=>s.key)');
console.log('stations', keys.join(' '), '| viewport', W, 'x', H, '@', DSF);
const BREAKDOWN = `(ms) => new Promise(done => {
  const r = __backroom.scene.renderer, orig = r.renderBufferDirect.bind(r); const by = new Map(), byMat = new Map(); let frames = 0, lastFrame = r.info.render.frame;
  const pathOf = o => { const names = []; for (let p = o; p && p.type !== 'Scene'; p = p.parent) if (p.name) names.push(p.name); return names.reverse().slice(0, 2).join(' / ') || ('(' + o.type + ' unnamed)'); };
  r.renderBufferDirect = (camera, scene, geometry, material, object, group) => { const k = pathOf(object); by.set(k, (by.get(k) || 0) + 1); const m = material.type + (material.transparent ? ' transparent' : ''); byMat.set(m, (byMat.get(m) || 0) + 1); return orig(camera, scene, geometry, material, object, group); };
  const t0 = performance.now(); const tick = now => { if (r.info.render.frame !== lastFrame) { frames++; lastFrame = r.info.render.frame; } if (now - t0 < ms) requestAnimationFrame(tick); else { r.renderBufferDirect = orig;
    const fmt = m => [...m].sort((a, b) => b[1] - a[1]).map(([k, n]) => (n / frames).toFixed(1).padStart(7) + '  ' + k);
    done({ frames, total: [...by.values()].reduce((a, b) => a + b, 0) / frames, byObject: fmt(by).slice(0, 45), byMaterial: fmt(byMat) }); } };
  requestAnimationFrame(tick);
})`;
const CPU_SAMPLE = `(ms) => new Promise(done => { const r = __backroom.scene.renderer; let frames = 0, calls = 0, last = r.info.render.frame; const t0 = performance.now();
  const tick = now => { if (r.info.render.frame !== last) { frames++; last = r.info.render.frame; calls += r.info.render.calls; } if (now - t0 < ms) requestAnimationFrame(tick); else done({ fps: +(frames / (ms / 1000)).toFixed(1), calls: Math.round(calls / Math.max(1, frames)) }); }; requestAnimationFrame(tick); })`;
async function gpuCost(label, ms = 4000) {
  const before = cpuOf(); const s = await ev(`(${CPU_SAMPLE})(${ms})`); const after = cpuOf();
  const cpu = {}; for (const p of after) { const b = before.find(x => x.Id === p.Id); if (b) { const t = typeOf(p); cpu[t] = +((cpu[t] || 0) + (p.CPU - b.CPU)).toFixed(2); } }
  console.log(`${label.padEnd(44)} fps ${String(s.fps).padStart(5)}  calls ${String(s.calls).padStart(4)}  gpu-process ${(100 * (cpu['gpu-process'] || 0) / (ms / 1000)).toFixed(0).padStart(3)}% core  renderer ${(100 * (cpu.renderer || 0) / (ms / 1000)).toFixed(0).padStart(3)}% core`);
}
const tag = process.env.BENCH_TAG || 'bench';
const shot = async (name) => { const r = await cdp('Page.captureScreenshot', { format: 'png' }); const { writeFile } = await import('node:fs/promises'); await writeFile(`${tag}-${name}.png`, Buffer.from(r.result.data, 'base64')); console.log('shot', `${tag}-${name}.png`); };
for (const s of scenarios) {
  if (s === 'shots') {
    await ev(`__backroom.scene.go(__backroom.stations.find(s=>s.key==='counter'))`); await sleep(3500); await shot('counter');
    await seatAt('slot:rose'); await shot('slot'); await sleep(1500); await shot('slot2'); await back();
    continue;
  }
  if (s === 'probe') {
    console.log('\n===== draw attribution at rest (per rendered frame)');
    const b = await ev(`(${BREAKDOWN})(2000)`);
    console.log('frames', b.frames, 'draws/frame', b.total.toFixed(0)); console.log('-- by material\n' + b.byMaterial.join('\n')); console.log('-- by object (top 2 names)\n' + b.byObject.join('\n'));
    console.log('\n===== cost of groups (hidden one at a time, 4 s each, room at rest)');
    const hide = (pred) => `(() => { const sc = __backroom.scene.scene; let n = 0; sc.traverse(o => { if ((${pred})(o) && o.visible) { o.visible = false; o.userData.__bench = true; n++; } }); return n; })()`;
    const restore = `(() => { __backroom.scene.scene.traverse(o => { if (o.userData.__bench) { o.visible = true; delete o.userData.__bench; } }); })()`;
    await gpuCost('baseline');
    const groups = [
      ['venue lights (bulbs, strips, loom tiles, particles)', `o => /^venue_lights_/.test(o.name)`],
      ['wall screens + projector', `o => o.material && o.material.uniforms && o.material.uniforms.ratioA`],
      ['prize marquee', `o => o.name === 'prize_parlour_marquee'`],
      ['decoration props', `o => /prop|plant|frame|ivy|decor/i.test(o.name) && o.parent && o.parent.type !== 'Scene'`],
      ['emis', `o => /emi/i.test(o.name)`],
      ['all Points + transparent', `o => o.isPoints || (o.material && o.material.transparent)`],
      ['everything except the shell (all holders)', `o => o.parent && o.parent.type === 'Scene' && !/room|shell|wall|floor|ceiling/i.test(o.name) && o.type === 'Group'`],
    ];
    for (const [label, pred] of groups) { const n = await ev(hide(pred)); await sleep(300); await gpuCost(label + ' OFF [' + n + ' nodes]'); await ev(restore); await sleep(300); }
    const shell = await ev(`(() => { const sc = __backroom.scene.scene; return sc.children.map(c => c.name + ':' + c.type).join(', '); })()`); console.log('scene children:', shell);
    continue;
  }
  if (s === 'rest') await scenario('rest at entry', async () => {});
  else if (s === 'walk') await scenario('walking + turning (W held, mouse drag)', async () => {}, async ms => { await keyEv('KeyW', 'keyDown'); await sleep(ms / 2); await keyEv('KeyW', 'keyUp'); await keyEv('KeyD', 'keyDown'); await sleep(ms / 2); await keyEv('KeyD', 'keyUp'); });
  else { const key = keys.find(k => k.startsWith(s)); if (!key) { console.log('no station', s); continue; }
    await scenario('seated ' + key, async () => seatAt(key)); await back(); }
}
if (errs.length) console.log('\npage errors:', errs.slice(0, 8));
await done(0);
