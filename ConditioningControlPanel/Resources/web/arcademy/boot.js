/* ============================================================================
 * boot.js - entry point for the in-app Arcademy page.
 *
 * Boot order (mirrors dtrh/boot.js and intake/boot.js):
 *   register bridge handlers -> announceReady() -> the host flushes exactly one
 *   `init` -> build the shell -> the split-flap board deals the day.
 *
 * Four pieces of hygiene copied from the siblings, because all four have been
 * paid for in support tickets:
 *   1. HEARTBEAT every 5s so the host's wedge watchdog can tell "busy" from
 *      "dead". If our main thread locks, the silence is the signal.
 *   2. PROGRESS-AWARE BOOT DEADLINE (45s since the last milestone, not since page
 *      load): a slow-but-progressing boot must not be misread as a wedge, and a
 *      boot that genuinely never arrives must not spin the loader forever.
 *      Failure posts `boot-error` so C# can show its fallback dialog and close.
 *   3. HOLD-ESC EXIT LADDER: a TAP walks the shell's own ladder (close settings ->
 *      pause -> leave class) and then unfullscreens; a HOLD leaves the Arcademy.
 *      Nobody is ever trapped, and nobody exits by fumbling one key.
 *   4. UNCAUGHT ERRORS AND REJECTIONS GO TO THE HOST LOG. There are no devtools
 *      in the hosted page; an unreported throw is an invisible bug.
 *
 * IMPORT SAFETY: nothing here throws at module import time - every document/window
 * touch is guarded - so `node -e "import('./boot.js')"` succeeds headless and the
 * shell can be smoke-tested against a DOM double.
 * ==========================================================================*/

import * as bridge from './bridge.js';

const doc = (typeof document !== 'undefined') ? document : null;
const win = (typeof window !== 'undefined') ? window : null;

const dom = {
  topbar: doc && doc.getElementById('arc-topbar'),
  screen: doc && doc.getElementById('arc-screen'),
  fx: doc && doc.getElementById('arc-fx'),
  ceremony: doc && doc.getElementById('arc-ceremony'),
  toast: doc && doc.getElementById('arc-toast'),
  loader: doc && doc.getElementById('arc-loader'),
  nope: doc && doc.getElementById('arc-nope'),
  nopeMsg: doc && doc.getElementById('arc-nope-msg'),
};

const BOOT_DEADLINE_MS = 45000;
const HOLD_EXIT_MS = 1200;
/* THE REACH FOR THE DOOR. A tap is under 200ms, so a hold still going at this
 * mark is a genuine attempt to leave - and the ~750ms of hold left after it is
 * the only runway EMI's wordless send-off ever gets (the host closes the window
 * within a frame or two of `exit`). A release between the two marks was a
 * false-alarm reach: both timers are cleared and nothing else happens. */
const EXIT_INTENT_MS = 450;
const TOAST_MS = 2200;

let shell = null;
let audio = null;
let initMsg = null;
let bootSettled = false;      // shell live OR boot failed - stops the deadline
let fullscreen = false;
let starting = false;
let pendingSuspend = null;    // a suspend frame that arrived before the shell existed

/* ----------------------------------------------------------------------------
 * DIAGNOSTICS - the only channel without devtools.
 * -------------------------------------------------------------------------- */
function log(msg) { bridge.log('info', msg); }
function warn(msg) { bridge.log('warning', msg); }

if (win) {
  win.addEventListener('error', (e) => {
    const src = e.filename ? ' @ ' + String(e.filename).split('/').pop() + ':' + e.lineno : '';
    bridge.log('error', 'error: ' + ((e && e.message) || 'script error') + src);
  });
  win.addEventListener('unhandledrejection', (e) => {
    const r = e && e.reason;
    bridge.log('error', 'promise: ' + ((r && (r.message || r.stack || r)) || 'unknown'));
  });
  // Loose-seam diagnostics: effect modules hold no bridge handle, they shout on
  // CustomEvents instead (GROUND-RULES §6). engine/index.js emits on `document`,
  // so listen there; window is kept for any consumer that picks the other target.
  // 'arcademy-sfx' IS consumed: shell/audio.js owns the only WebAudio graph on the
  // page and subscribes to that event on `document` itself (started below, once
  // init has told us the mixer levels).
  const onFxLog = (e) => {
    try { log('[fx] ' + ((e && e.detail && e.detail.msg) || '?')); } catch (_e) { /* never fatal */ }
  };
  win.addEventListener('arcademy-log', onFxLog);
  if (doc && doc.addEventListener) doc.addEventListener('arcademy-log', onFxLog);
}

/** Transient shell chatter. Never throws, harmless with no DOM. */
function toast(text) {
  if (!dom.toast || !doc) return;
  try {
    const node = doc.createElement('div');
    node.textContent = String(text);
    dom.toast.appendChild(node);
    setTimeout(() => { try { node.remove(); } catch (e) { /* noop */ } }, TOAST_MS);
  } catch (e) { /* noop */ }
}

/* ----------------------------------------------------------------------------
 * BOOT DEADLINE
 * -------------------------------------------------------------------------- */
let deadlineTimer = 0;

function armBootDeadline() {
  if (bootSettled) return;
  clearTimeout(deadlineTimer);
  deadlineTimer = setTimeout(() => {
    if (bootSettled) return;
    failBoot('boot deadline: no init 45s after the last progress'
      + (initMsg ? '' : ' [no init]'));
  }, BOOT_DEADLINE_MS);
}

function failBoot(msg) {
  if (bootSettled) return;
  bootSettled = true;
  clearTimeout(deadlineTimer);
  warn(msg);
  bridge.send({ type: 'boot-error', msg: String(msg).slice(0, 400) });
  if (dom.loader) dom.loader.hidden = true;
  if (dom.nopeMsg) dom.nopeMsg.textContent = String(msg).slice(0, 200);
  if (dom.nope) dom.nope.hidden = false;
}

/* ----------------------------------------------------------------------------
 * START
 * -------------------------------------------------------------------------- */
async function start() {
  if (starting || shell || !initMsg) return;
  starting = true;
  try {
    // The sfx consumer first, so a cue fired during the shell's own boot is heard.
    // OPTIONAL by construction: no audio must never cost us the page.
    try {
      const am = await import('./shell/audio.js');
      audio = am.createAudio({ init: initMsg, bridge, log });
    } catch (e) { warn('audio consumer unavailable (' + ((e && e.message) || e) + ') - sfx are silent'); }
    const mod = await import('./shell/shell.js');
    shell = await mod.createShell({
      init: initMsg,
      bridge,
      dom: { topbar: dom.topbar, screen: dom.screen, fx: dom.fx, ceremony: dom.ceremony },
      toast,
      log,
    });
    bootSettled = true;
    clearTimeout(deadlineTimer);
    if (dom.loader) dom.loader.hidden = true;
    log('shell live');
    // Replay the native state that landed while we were importing (see the
    // 'suspend' handler). After the shell exists so the class_suspended treatment
    // has something to render into.
    if (pendingSuspend) {
      const m = pendingSuspend;
      pendingSuspend = null;
      try { shell.onSuspend(m); log('replayed buffered suspend (' + (m.reason || '?') + ')'); }
      catch (e) { warn('buffered suspend replay threw: ' + ((e && e.message) || e)); }
    }
  } catch (err) {
    starting = false;
    failBoot('shell boot failed: ' + ((err && (err.stack || err.message)) || err));
  }
}

/* ----------------------------------------------------------------------------
 * HOST FRAMES - one handler each, every body try/caught. A throw inside a
 * handler must never stop the next frame from being delivered.
 * -------------------------------------------------------------------------- */
function guard(name, fn) {
  return (m) => {
    try { fn(m); }
    catch (e) { bridge.log('error', name + ' handler threw: ' + ((e && (e.stack || e.message)) || e)); }
  };
}

bridge.on('init', guard('init', (m) => {
  if (initMsg) { warn('second init ignored'); return; }
  if (m.protocol !== bridge.PROTOCOL) {
    // Version skew is fatal by design: a page reading a projection it does not
    // understand would silently mis-clamp settings. Fail loudly instead.
    failBoot('protocol mismatch: host ' + m.protocol + ', page ' + bridge.PROTOCOL);
    return;
  }
  initMsg = m;
  bridge.markInitialized();       // flush anything the page queued pre-init
  armBootDeadline();              // progress milestone
  if (m.audioOnlySession) {
    // The host gate refuses the launch in this state (BUILD-CONTRACT §3); if we
    // somehow got here anyway, the shell renders the closed-school card.
    warn('init says audioOnlySession - the shell will refuse to open classes');
  }
  start();
}));

bridge.on('setting', guard('setting', (m) => { if (shell) shell.onSetting(m); }));
bridge.on('payout-result', guard('payout-result', (m) => {
  if (shell) shell.onPayout(m);
  log('payout: ' + m.gameKey + ' +' + Math.round(Number(m.xp) || 0) + 'xp' + (m.levelUp ? ' (level up)' : ''));
}));
/* THE PUNCH CARD (PUNCHCARD.md §2/§4). It lands right behind the meta snapshot
 * on both mint paths, and `bridge.on` REPLAYS anything that arrived before this
 * subscription existed - which is what makes a frame that beats the shell's two
 * dynamic imports safe without a buffer of its own (unlike suspend below, which
 * is a LEVEL and has to collapse rather than queue). */
bridge.on('punchcard-result', guard('punchcard-result', (m) => {
  if (shell) shell.onPunchCard(m);
  log('punch card: ' + m.gameKey + ' ' + (m.reason || '?')
    + (m.minted ? ' +' + m.minted : ' (no-op)')
    + (m.justUnlocked ? ' UNLOCKED' : ''));
}));
bridge.on('suspend', guard('suspend', (m) => {
  // PRE-SHELL SUSPEND REPLAY. The host seeds the current native state right after
  // `init` (a mandatory video already playing, AudioOnlySession flipped between the
  // launch gate and the first frame), and that lands while `shell` is still null
  // because start() is async (two dynamic imports). Dropping it opened the class
  // un-suspended over a running video. Buffer the LAST state only - suspend is a
  // level, not an edge, so an on/off pair collapses to "off" correctly.
  if (!shell) { pendingSuspend = m; }
  else shell.onSuspend(m);
  log('suspend ' + (m.on ? 'ON' : 'off') + ' (' + (m.reason || '?') + ')'
    + (shell ? '' : ' [buffered until the shell exists]'));
}));
bridge.on('meta', guard('meta', (m) => { if (shell) shell.onMeta(m); }));
bridge.on('fullscreen', guard('fullscreen', (m) => { fullscreen = !!m.on; }));
bridge.on('ping', guard('ping', (m) => bridge.send({ type: 'pong', t: m && m.t })));
bridge.on('end-run', guard('end-run', () => shutdown('host asked')));

/* `assets` frames belong to provider/index.js, which subscribes to them itself -
 * bridge.on is multi-subscriber precisely so it does not have to route through
 * here (see bridge.js header). */

/* ----------------------------------------------------------------------------
 * EXIT
 * -------------------------------------------------------------------------- */
function shutdown(reason) {
  try { if (shell) shell.destroy(); } catch (e) { /* best effort */ }
  try { if (audio) audio.destroy(); } catch (e) { /* best effort */ }
  shell = null;
  audio = null;
  bridge.stopHeartbeat();
  bridge.send({ type: 'exit', reason: String(reason || 'page') });
}

/* ----------------------------------------------------------------------------
 * THE ESC LADDER
 *   tap  -> shell rung (close settings / pause / leave class), then unfullscreen,
 *           then a hint telling you how to actually leave
 *   hold -> exit
 * -------------------------------------------------------------------------- */
let escTimer = 0;
let escIntentTimer = 0;
let escHint = null;

/* EMI, LAZILY AND OPTIONALLY. boot.js knows nothing about the mascot and keeps
 * it that way: the module is only reached for when a hold has actually started,
 * the promise is cached (a failure is permanent silence) and the call is fired
 * and forgotten. NOTHING HERE MAY DELAY OR GATE THE EXIT. */
let emiMoments = null;
function fireEmi(name, payload) {
  try {
    if (!emiMoments) emiMoments = import('./emi/moments.js').catch(() => null);
    emiMoments.then((m) => {
      try { if (m && typeof m.fireMoment === 'function') m.fireMoment(name, payload); }
      catch (e) { /* a mascot may never break the way out */ }
    });
  } catch (e) { /* noop */ }
}

function showEscHint() {
  if (!doc || escHint) return;
  try {
    escHint = doc.createElement('div');
    escHint.className = 'arc-esc-hint';
    escHint.textContent = 'HOLD ESC TO LEAVE';
    (doc.body || doc.documentElement).appendChild(escHint);
    setTimeout(() => { try { escHint.remove(); } catch (e) { /* noop */ } escHint = null; }, 1400);
  } catch (e) { escHint = null; }
}

if (win) {
  win.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape' || e.repeat) return;
    if (escTimer) return;
    escTimer = setTimeout(() => {
      escTimer = 0;
      shutdown('hold-esc');
    }, HOLD_EXIT_MS);
    // EMI SEAM: the hold is past a tap, so the player is leaving the Arcademy.
    escIntentTimer = setTimeout(() => {
      escIntentTimer = 0;
      fireEmi('exitIntent', { reason: 'hold-esc' });
    }, EXIT_INTENT_MS);
  });

  win.addEventListener('keyup', (e) => {
    if (e.key !== 'Escape') return;
    if (escIntentTimer) { clearTimeout(escIntentTimer); escIntentTimer = 0; }
    if (!escTimer) return;          // the hold already fired
    clearTimeout(escTimer);
    escTimer = 0;
    // A tap: walk the ladder one rung.
    let consumed = false;
    try { consumed = !!(shell && shell.escapeStep()); }
    catch (err) { bridge.log('error', 'escapeStep threw: ' + ((err && err.message) || err)); }
    if (consumed) return;
    if (fullscreen) { bridge.send({ type: 'fullscreen-request', on: false }); return; }
    showEscHint();
  });

  win.addEventListener('keydown', (e) => {
    // F11 is the app-wide fullscreen convention; the host owns the actual window.
    if (e.key !== 'F11' || e.repeat) return;
    e.preventDefault();
    bridge.send({ type: 'fullscreen-request', on: !fullscreen });
  });
}

/* ----------------------------------------------------------------------------
 * GO
 * -------------------------------------------------------------------------- */
bridge.startHeartbeat(5000);
armBootDeadline();
bridge.announceReady();
log('boot: ready posted, waiting for init');

if (!bridge.isHosted) {
  // Standalone (a plain browser, e.g. a future gated web app or a dev harness):
  // there is no host to answer `ready`, so say so instead of spinning until the
  // deadline. A web port supplies its own init through a shim, exactly like
  // intake/web-shim.js does.
  warn('no host bridge - standalone boot is not supported in v1');
}

export { dom, toast };

/** Test seam: the live sfx consumer (null until init). */
export function audioConsumer() { return audio; }

/** Test seam: the suspend frame buffered while the shell was still booting. */
export function bufferedSuspend() { return pendingSuspend; }
