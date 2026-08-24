/* ============================================================================
 * arcademy-boot-harness.mjs — run the REAL Arcademy payload's boot path outside
 * a browser, so the boot handshake can be proved as BEHAVIOUR instead of as a
 * frame this host happened to serialize.
 *
 * WHY THIS EXISTS. Every other Arcademy fact in this suite pins one half of the
 * conversation: ArcademyProtocolTests pins the frames C# emits, ArcademyServingTests
 * pins that the bytes are served. Neither runs a line of the payload's JavaScript,
 * and the board row says so in as many words. A handshake nobody on the page side
 * ever completed is not a handshake — the two halves could disagree about the
 * protocol number, the frame names or the ordering and every existing fact would
 * still be green.
 *
 * WHAT MAKES IT LEGITIMATE RATHER THAN A SIMULATION. Three things, and only these:
 *   1. The MODULES ARE THE SERVED BYTES. argv[2] is ArcademyServingRoots.PayloadRoot
 *      — the payload/ mirror beside the test assembly that the linked read-only glob
 *      copied, and the same subtree LoopbackServer serves at /dtrh/arcademy/*. No
 *      file is forked, patched or shimmed to make the boot succeed.
 *   2. The TRANSPORT IS THE PORT'S OWN. Avalonia's WebView host talks to a payload
 *      page through window.chrome.webview: page→host is postMessage
 *      (GoonHostWindow.axaml.cs:270, WebMessageReceived → HandleWebMessageBody) and
 *      host→page is a MessageEvent dispatched on that same object
 *      (GoonHostWindow.axaml.cs:629). The double below is that pair and nothing more.
 *   3. The HOST IS REAL. Frames go out on stdout and come back on stdin; the C# side
 *      is a live ArcademySession, not a script of canned replies.
 *
 * WHAT IS DOUBLED, AND THEREFORE WHAT THIS CANNOT PROVE. The DOM. There is no
 * layout, no paint, no compositor and no event loop driven by a user. Elements
 * accept property writes and hand back plausible empty rects. So this harness can
 * prove that the page's boot LOGIC completes and that the shell reports itself live
 * — it can NEVER prove that anything was drawn, was legible, or could be clicked.
 * Those need a headed capture, and the Arcademy door is shut, so they are unproven.
 *
 * NO WALL CLOCK LIVES HERE. There is no timer, deadline or sleep in this file: the
 * process runs until its stdin closes, and the C# side owns the bounded window
 * (TestWait) and the kill. A deadline on both sides is two things to keep in sync
 * and one to get wrong.
 *
 * PROTOCOL, stdout, one line per record:
 *   F <json>   a page→host frame, exactly as the page posted it
 *   X <text>   the harness itself failed (import threw, or an uncaught error)
 * stdin, one line per record: a host→page frame as JSON.
 * ==========================================================================*/
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const payloadRoot = process.argv[2];
if (!payloadRoot) {
  process.stdout.write('X no payload root argument\n');
  process.exit(2);
}

const emit = (line) => process.stdout.write(line + '\n');

/* ----------------------------------------------------------------------------
 * THE DOM DOUBLE. Deliberately the smallest surface the real shell actually
 * touches on the way up — it was grown by running the payload and adding only
 * what it asked for, never by guessing at an API list. Anything it does not have,
 * the shell does not use during boot.
 * -------------------------------------------------------------------------- */
function element(tag) {
  return {
    tagName: String(tag).toUpperCase(),
    id: '', hidden: false, disabled: false, type: '',
    textContent: '', innerHTML: '', className: '', value: '',
    children: [],
    appendChild(child) { this.children.push(child); return child; },
    append(...kids) { for (const k of kids) this.children.push(k); },
    removeChild(child) { this.children = this.children.filter((c) => c !== child); return child; },
    remove() {},
    insertBefore(child) { this.children.unshift(child); return child; },
    addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; },
    setAttribute() {}, removeAttribute() {}, getAttribute() { return null; },
    hasAttribute() { return false; },
    focus() {}, blur() {}, click() {},
    querySelector() { return null; }, querySelectorAll() { return []; },
    // A plausible non-degenerate box: a zero-sized rect makes some layout maths
    // divide by zero, which would be the harness inventing a failure.
    getBoundingClientRect: () => ({ x: 0, y: 0, top: 0, left: 0, right: 1280, bottom: 720, width: 1280, height: 720 }),
    style: { setProperty() {}, removeProperty() {}, getPropertyValue() { return ''; } },
    classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
    dataset: {},
  };
}

// Memoized by id, so two getElementById calls for the same node are the SAME
// object — identity comparisons in the page must not silently succeed or fail
// for a reason the real document would never produce.
const byId = new Map();
const documentDouble = {
  documentElement: element('html'),
  body: element('body'),
  head: element('head'),
  hidden: false,
  getElementById(id) {
    if (!byId.has(id)) { const node = element('div'); node.id = id; byId.set(id, node); }
    return byId.get(id);
  },
  createElement: element,
  createElementNS: (_ns, tag) => element(tag),
  createTextNode: (text) => ({ textContent: String(text) }),
  createDocumentFragment: () => element('fragment'),
  addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; },
  querySelector() { return null; }, querySelectorAll() { return []; },
};

let deliverToPage = null;
const windowDouble = {
  addEventListener() {}, removeEventListener() {}, dispatchEvent() { return true; },
  innerWidth: 1280, innerHeight: 720, devicePixelRatio: 1,
  matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
  getComputedStyle: () => ({ getPropertyValue: () => '' }),
  document: documentDouble,
  chrome: {
    webview: {
      // page → host (WebMessageReceived on the real host).
      postMessage(message) { emit('F ' + JSON.stringify(message)); },
      // host → page (the real host's InvokeScript dispatches exactly this event).
      addEventListener(type, fn) { if (type === 'message') deliverToPage = (data) => fn({ data }); },
      removeEventListener() {},
    },
  },
};

globalThis.window = windowDouble;
globalThis.document = documentDouble;
globalThis.self = windowDouble;
globalThis.navigator ??= { userAgent: 'ccp-arcademy-boot-harness', language: 'en-US' };

process.on('uncaughtException', (e) => { emit('X uncaught ' + (e && e.stack || e)); process.exit(3); });
process.on('unhandledRejection', (e) => { emit('X rejection ' + (e && e.stack || e)); process.exit(3); });

const moduleUrl = (file) => pathToFileURL(path.join(payloadRoot, file)).href;

/* ----------------------------------------------------------------------------
 * THE BOOT, in three ordered steps. The order is the fact, not an implementation
 * detail: step 2 must happen while the page is still pre-init, and the only way to
 * be SURE of that is to do it before boot.js has posted `ready` at all.
 * -------------------------------------------------------------------------- */
let bridge;
try {
  // 1. bridge.js alone: it registers the host→page listener at import (bridge.js:68-72)
  //    and posts nothing. Importing it here yields the SAME module instance boot.js
  //    gets — ESM caches on resolved URL — so the queue below is the page's own queue.
  bridge = await import(moduleUrl('bridge.js'));

  // 2. THE PRE-INIT PROBE. A gameplay-shaped frame, posted before `ready` exists.
  //    bridge.js holds everything outside its boot allowlist until init lands
  //    (bridge.js:47 BOOT_LANE, :114-118 send). If that discipline were absent this
  //    frame would leave NOW and appear on stdout ahead of `ready`.
  bridge.send({ type: 'meta-command', op: 'get', key: 'ccp-boot-harness-probe' });

  // 3. boot.js: registers its handlers, starts the heartbeat and announces ready.
  await import(moduleUrl('boot.js'));
} catch (e) {
  emit('X import ' + (e && e.stack || e));
  process.exit(2);
}

createInterface({ input: process.stdin, crlfDelay: Infinity })
  .on('line', (line) => {
    const text = line.trim();
    if (!text) return;
    try {
      if (deliverToPage) deliverToPage(JSON.parse(text));
    } catch (e) {
      emit('X inbound ' + (e && e.message || e));
    }
  })
  .on('close', () => process.exit(0));
