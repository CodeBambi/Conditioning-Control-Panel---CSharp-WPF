#!/usr/bin/env node
/* ============================================================================
 * weave-backroom-spirals.mjs - the Back Room's two fullscreen spirals, woven by
 * the Loom (CONTRACT 10.13.B, spiral source 2).
 *
 *   node ConditioningControlPanel/Scripts/weave-backroom-spirals.mjs [--check-only]
 *
 * Writes Resources/web/backroom/shared/hypno/spirals/{screen,wake}.gif and a
 * Loom v2 sidecar beside each ({screen,wake}.json, format 'wide'), from
 * LOOM_PRESETS.screen and .wake in shared/hypno/loom.js. The frames and the GIF
 * come from the Loom's own encoder, dtrh/engine/loomWorker.js, run as a module
 * worker in headless Chrome against a static server on 127.0.0.1: the same
 * field shader, the same frame table, the same quantizer and dither the Loom
 * studio saves with. Never at runtime; the host only plays the files.
 *
 * Budget: CONTRACT 10.13.B asks for at most 4 MB and a 720 long side. The
 * worker's long side is fixed at 640 (512 when a weave runs past its 6 MB soft
 * cap), so the files come out at 640 or 512. A file over 4 MB is written with a
 * warning (screen.gif, 72 frames of gradient threads, weaves to about 5.3 MB
 * at 512); over the Loom store's own 8 MB cap the run fails and writes nothing.
 * --report weaves and prints sizes only. --check-only re-weaves in memory and
 * fails when a file on disk differs in size by more than 2% or a sidecar differs.
 * Nothing leaves the machine. The only process this stops is the Chrome it
 * started, by its own handle. CHROME: CHROME_PATH, else the usual install.
 * ==========================================================================*/

import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, extname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const WEB = resolve(HERE, '..', 'Resources', 'web');
const OUT = join(WEB, 'backroom', 'shared', 'hypno', 'spirals');
const NAMES = ['screen', 'wake'];
const BUDGET_BYTES = 4 * 1024 * 1024;   // CONTRACT 10.13.B
const MAX_BYTES = 8 * 1024 * 1024;      // DtrhLoomStore.MaxGifBytes: a Loom spiral the app itself would refuse
const REPORT_ONLY = process.argv.includes('--report');   // weave and print sizes, check nothing, write nothing
const CHECK_ONLY = process.argv.includes('--check-only');
const CHROME = process.env.CHROME_PATH || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const PORT = Number(process.env.WEAVE_PORT || 8894), DEBUG_PORT = PORT + 500;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => { console.error('FAIL ' + msg); process.exitCode = 1; };
// The sidecar is meant to be read and pasted: a float from normalizeParams2's snap (0.30000000000000004) is written short.
const tidy = (k, v) => (typeof v === 'number' && !Number.isInteger(v) ? Number(v.toFixed(6)) : v);

if (!existsSync(CHROME)) { fail('no chrome at ' + CHROME); process.exit(1); }

// The page: import the presets, run the Loom worker on each, hand back base64.
const WEAVE_PAGE = `<!doctype html><meta charset="utf-8"><script type="module">
import { LOOM_PRESETS } from '/backroom/shared/hypno/loom.js';
import { normalizeParams2 } from '/arcademy/engine/loom/loomField.js';
window.weave = (name) => new Promise((resolve) => {
  const params = normalizeParams2({ ...JSON.parse(JSON.stringify(LOOM_PRESETS[name])), format: 'wide' });
  const w = new Worker('/dtrh/engine/loomWorker.js', { type: 'module' });
  const t0 = performance.now();
  w.onerror = (e) => { w.terminate(); resolve({ error: String(e.message || e) }); };
  w.onmessage = (e) => {
    const m = e.data;
    if (m.progress != null) return;
    w.terminate();
    if (m.error) { resolve({ error: m.error }); return; }
    const bytes = new Uint8Array(m.gif);
    let bin = '';
    for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
    resolve({ params, b64: btoa(bin), bytes: m.bytes, w: m.w, h: m.h, frames: m.frames, delayCs: m.delayCs, ms: Math.round(performance.now() - t0) });
  };
  w.postMessage({ id: 1, params });
});
window.__ready = true;
</script>`;

const MIME = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json' };
const server = createServer(async (req, res) => {
  const path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
  if (path.includes('..')) { res.writeHead(400); return res.end(); }
  if (path === '/__weave.html') { res.writeHead(200, { 'content-type': 'text/html' }); return res.end(WEAVE_PAGE); }
  try { const body = await readFile(join(WEB, path)); res.writeHead(200, { 'content-type': MIME[extname(path).toLowerCase()] || 'application/octet-stream' }); res.end(body); }
  catch { res.writeHead(404); res.end('no'); }
});
await new Promise((r) => server.listen(PORT, '127.0.0.1', r));
const prof = mkdtempSync(join(tmpdir(), 'backroom-weave-'));
const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${prof}`, '--no-first-run',
  '--no-default-browser-check', '--mute-audio', '--ignore-gpu-blocklist', '--enable-unsafe-swiftshader', 'about:blank'], { stdio: 'ignore' });
async function done() { try { chrome.kill(); } catch { /* our own child only */ } server.close(); await sleep(500); try { rmSync(prof, { recursive: true, force: true }); } catch { /* noop */ } }

try {
  let target = null;
  for (let i = 0; i < 60 && !target; i++) { await sleep(250); try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === 'page'); } catch { /* not up */ } }
  if (!target) throw new Error('chrome never answered');
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r) => { ws.onopen = r; });
  let id = 0;
  const waits = new Map();
  ws.onmessage = (e) => { const m = JSON.parse(e.data); if (m.id && waits.has(m.id)) { waits.get(m.id)(m); waits.delete(m.id); } };
  const cdp = (method, params) => new Promise((res) => { const i = ++id; waits.set(i, res); ws.send(JSON.stringify({ id: i, method, params })); });
  const ev = async (x) => (await cdp('Runtime.evaluate', { expression: x, returnByValue: true, awaitPromise: true })).result?.result?.value;
  await cdp('Page.enable');
  await cdp('Page.navigate', { url: `http://127.0.0.1:${PORT}/__weave.html` });
  for (let i = 0; i < 100 && !(await ev('!!window.__ready')); i++) await sleep(100);

  const woven = [];
  for (const name of NAMES) {
    const r = await ev(`window.weave(${JSON.stringify(name)})`);
    if (!r || r.error) throw new Error(name + ': ' + ((r && r.error) || 'no answer'));
    console.log(`  wove ${name}.gif ${r.w}x${r.h}, ${r.frames} frames at ${r.delayCs} cs, ${(r.bytes / 1048576).toFixed(2)} MB in ${r.ms} ms`);
    if (REPORT_ONLY) continue;
    if (r.bytes > MAX_BYTES) throw new Error(`${name}.gif is ${r.bytes} bytes, over the Loom store's 8 MB cap`);
    if (r.bytes > BUDGET_BYTES) console.warn(`  WARN ${name}.gif is over the contract's 4 MB budget (${r.bytes} bytes)`);
    woven.push({ name, gif: Buffer.from(r.b64, 'base64'), sidecar: JSON.stringify(r.params, tidy, 2) + '\n' });
  }

  if (REPORT_ONLY) {
    console.log('  report only: nothing checked or written');
  } else if (CHECK_ONLY) {
    for (const w of woven) {
      const gif = join(OUT, w.name + '.gif'), side = join(OUT, w.name + '.json');
      if (!existsSync(gif) || !existsSync(side)) { fail(w.name + ': not woven yet'); continue; }
      const drift = Math.abs(statSync(gif).size - w.gif.length) / w.gif.length;
      if (drift > 0.02) fail(`${w.name}.gif differs from a fresh weave by ${(drift * 100).toFixed(1)}%`);
      if (readFileSync(side, 'utf8').replace(/\r\n/g, '\n') !== w.sidecar) fail(`${w.name}.json differs from LOOM_PRESETS.${w.name}`);
    }
    if (!process.exitCode) console.log('  spirals on disk match a fresh weave');
  } else {
    mkdirSync(OUT, { recursive: true });
    for (const w of woven) {
      writeFileSync(join(OUT, w.name + '.gif'), w.gif);
      writeFileSync(join(OUT, w.name + '.json'), w.sidecar);
      console.log(`  wrote spirals/${w.name}.gif and spirals/${w.name}.json`);
    }
  }
} catch (e) {
  fail((e && e.message) || String(e));
} finally {
  await done();
}
