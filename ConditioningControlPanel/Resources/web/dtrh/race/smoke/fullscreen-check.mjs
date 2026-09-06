/*
 * Node 22+ and a Chromium-family browser, no npm dependencies:
 *   CHROME_BIN=chromium node race/smoke/fullscreen-check.mjs
 *
 * Loads the real race.html / raceBoot.js / bridge.js over loopback. Only the
 * external WebView2 transport and timeout scheduler are fixture-controlled. The
 * host withholds init/manifest and timers are held, keeping the page at its
 * ready boundary without starting a renderer, audio or user media.
 * Explicit requests use the public bridge.send API: the race currently has
 * no fullscreen UI control. A fixture acknowledgement is NOT WPF execution.
 */
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { readFile, mkdtemp, rm } from 'node:fs/promises';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import path from 'node:path';

const webRoot = path.resolve(import.meta.dirname, '../../..');
const profile = await mkdtemp(path.join(tmpdir(), 'race-fullscreen-'));
const mime = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css' };
const server = createServer(async (req, res) => {
  const file = path.resolve(webRoot, '.' + new URL(req.url, 'http://localhost').pathname);
  if (!file.startsWith(webRoot + path.sep)) { res.writeHead(403).end(); return; }
  try {
    const data = await readFile(file);
    res.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream' }).end(data);
  } catch { res.writeHead(404).end(); }
});
let browser, socket;
const pending = new Map();
const deadline = setTimeout(() => {
  console.error('FAIL fullscreen check timed out');
  browser?.kill();
  socket?.close();
  server.closeAllConnections();
  server.close();
  process.exitCode = 1;
}, 20000);
try {
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  const origin = `http://127.0.0.1:${server.address().port}`;
  browser = spawn(process.env.CHROME_BIN || 'chromium', [
    '--headless', '--remote-debugging-port=0', `--user-data-dir=${profile}`,
    '--no-first-run', '--no-default-browser-check', '--disable-background-networking',
    '--disable-component-update', '--disable-sync', '--metrics-recording-only',
    '--use-angle=swiftshader', '--enable-unsafe-swiftshader',
    '--host-resolver-rules=MAP * ~NOTFOUND, EXCLUDE 127.0.0.1', 'about:blank',
  ], { stdio: ['ignore', 'ignore', 'pipe'] });
  const endpoint = await new Promise((resolve, reject) => {
    let stderr = '';
    browser.on('error', reject);
    browser.on('exit', (code, signal) => reject(new Error(`Browser exited before CDP: ${code ?? signal}\n${stderr}`)));
    browser.stderr.on('data', (data) => {
      stderr = (stderr + data).slice(-8000);
      const match = stderr.match(/DevTools listening on (ws:\/\/[^\s]+)/);
      if (match) resolve(new URL(match[1]).origin.replace('ws:', 'http:'));
    });
  });
  const targets = await (await fetch(endpoint + '/json/list')).json();
  socket = new WebSocket(targets.find((target) => target.type === 'page').webSocketDebuggerUrl);
  await once(socket, 'open');
  let id = 0;
  socket.addEventListener('message', ({ data }) => {
    const message = JSON.parse(data);
    const waiter = pending.get(message.id);
    if (!waiter) return;
    pending.delete(message.id);
    if (message.error) waiter.reject(new Error(JSON.stringify(message.error)));
    else waiter.resolve(message.result);
  });
  socket.addEventListener('close', () => {
    for (const waiter of pending.values()) waiter.reject(new Error('CDP closed'));
    pending.clear();
  });
  function call(method, params = {}) {
    return new Promise((resolve, reject) => {
      pending.set(++id, { resolve, reject });
      socket.send(JSON.stringify({ id, method, params }));
    });
  }
  console.log('Environment:', process.version, process.platform,
    (await call('Browser.getVersion')).product);
  await call('Page.enable');
  await call('Page.addScriptToEvaluateOnNewDocument', { source: `
    window.setTimeout = () => 0; // Hold the external host-init timeout.
    const transport = new EventTarget();
    transport.outgoing = [];
    transport.postMessage = (message) => transport.outgoing.push(structuredClone(message));
    window.chrome.webview = transport;
  ` });
  const loaded = new Promise((resolve, reject) => {
    socket.addEventListener('close', () => reject(new Error('Page did not finish loading')), { once: true });
    socket.addEventListener('message', function listener({ data }) {
      if (JSON.parse(data).method !== 'Page.loadEventFired') return;
      socket.removeEventListener('message', listener);
      resolve();
    });
  });
  await call('Page.navigate', { url: origin + '/dtrh/race.html' });
  await loaded;
  const result = await call('Runtime.evaluate', {
    awaitPromise: true, returnByValue: true,
    expression: `(${async function () {
      const bridge = await import('/dtrh/bridge.js');
      const transport = window.chrome.webview;
      const checks = [], trace = [];
      function check(name, actual, expected) {
        checks.push({ name, actual, expected, pass: JSON.stringify(actual) === JSON.stringify(expected) });
      }
      function receive(message) {
        trace.push({ direction: 'host->page', ...message });
        transport.dispatchEvent(new MessageEvent('message', { data: message }));
      }
      function drain() {
        const messages = transport.outgoing.splice(0);
        trace.push(...messages.filter((m) => m.type !== 'log').map((m) => ({ direction: 'page->host', ...m })));
        return messages;
      }
      check('real page announced ready without boot errors',
        drain().filter((m) => m.type !== 'log'), [{ type: 'ready', protocol: 1 }]);
      check('hosted transport selected', bridge.isHosted, true);
      for (const on of [true, true, false, false, true]) {
        receive({ type: 'fullscreen', on });
        check('notification on=' + on + ' emits no command', drain(), []);
      }
      // Model only the external host protocol: every request is acknowledged,
      // even when the host state did not change. Bound delivery to catch the
      // original endless exchange without hanging the test or hiding the echo.
      for (const on of [true, true, false, false]) {
        bridge.send({ type: 'fullscreen-set', on });
        let requests = drain();
        check('explicit request on=' + on + ' crosses transport', requests,
          [{ type: 'fullscreen-set', on }]);
        let acknowledgements = 0;
        while (requests.length && acknowledgements < 4) {
          for (const request of requests) {
            if (request.type === 'fullscreen-set') {
              acknowledgements++;
              receive({ type: 'fullscreen', on: request.on });
            }
          }
          requests = drain();
        }
        check('explicit request on=' + on + ' terminates after one acknowledgement',
          { acknowledgements, remaining: requests }, { acknowledgements: 1, remaining: [] });
      }
      receive({ type: 'ping', t: 658 });
      check('real boot handler still answers ping', drain(), [{ type: 'pong', t: 658 }]);
      return { checks, trace };
    }} )()` ,
  });
  assert.equal(result.exceptionDetails, undefined, JSON.stringify(result.exceptionDetails));
  const { checks, trace } = result.result.value;
  for (const check of checks) console.log(`${check.pass ? 'PASS' : 'FAIL'} ${check.name}`,
    check.pass ? '' : JSON.stringify({ actual: check.actual, expected: check.expected }));
  console.log('Protocol trace:', JSON.stringify(trace));
  assert.ok(checks.every((check) => check.pass), 'fullscreen message boundary regression');
  console.log(`PASS ${checks.length} checks (browser protocol fixture, not Windows host/UI)`);
} finally {
  clearTimeout(deadline);
  socket?.close();
  if (browser?.pid && browser.exitCode === null && browser.signalCode === null) {
    const exited = once(browser, 'exit');
    browser.kill();
    await exited;
  }
  server.closeAllConnections();
  await new Promise((resolve) => server.close(resolve));
  await rm(profile, { recursive: true, force: true });
}
