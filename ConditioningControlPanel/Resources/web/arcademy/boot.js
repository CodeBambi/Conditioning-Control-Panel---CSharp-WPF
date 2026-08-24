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
import { installDeviceClass } from './core/device.js';

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

/* ----------------------------------------------------------------------------
 * THE SPLASH DISMISSAL. The loader doubles as the ~3s intro splash (see
 * index.html): a boot that lands early WAITS OUT the beat instead of cutting
 * it, a boot that lands late dismisses on arrival. `.is-done` plays the CSS
 * exit (fade + zoom-through); the `hidden` attribute stays the ground truth a
 * beat later. FAILURE PATHS NEVER WAIT - failBoot below still snaps
 * `hidden = true` directly, so an error card is never delayed by a celebration.
 *
 * THE SPLASH-COMPLETE EDGE (FIRST BELL, 2026-08-24) hangs off the HAPPY PATH of
 * this function and nowhere else: once `hidden` has actually landed, the shell
 * is told, and it decides whether the opening has anything to play. failBoot
 * never calls it, so an error card can no more be held up by a scene than by
 * the splash itself (trap 66). The call is guarded and fire-and-forget - the
 * loader comes down whatever the shell does with the news.
 * -------------------------------------------------------------------------- */
const INTRO_MIN_MS = 2950;
const INTRO_EXIT_MS = 320;
const introT0 = Date.now();

/* ----------------------------------------------------------------------------
 * THE SPLASH HAS A VOICE (AV CLUB, 2026-08-24).
 *
 * The splash is a fixed CSS timeline that starts at `introT0` - before init,
 * before the mixer, before anything on this page can make a sound - so the
 * sound cannot be authored INTO it. It is STITCHED ONTO it instead: when the
 * consumer comes up we ask how much of the beat is already behind us and
 * schedule only the beats still in front. A beat we have missed is never struck
 * late; a late boot simply gets a quieter opening, which is the honest one.
 *
 * The beats are the CSS's own (index.html / styles.css): the wordmark THUDS in
 * at .64s, the rail deals from ~1.7s, and the sparkles and their flash close it
 * out at 2.35-2.65s. Two shapes, and the first is the one we want - once
 * `assets/sfx/intro_bed.mp3` ships and the host has armed us with no gesture,
 * ONE file plays the whole opening and nothing is stitched at all.
 *
 * THREE GUARDS, and all three are the loader contract (trap 66):
 *   - reduced motion gets no cues whatever: the splash it is watching is not
 *     the splash these beats were written for.
 *   - every timer re-checks that the loader is still up, so a beat can never
 *     land on the error card or over the campus.
 *   - failBoot cancels the schedule outright, and cancelling is a clearTimeout:
 *     the failure path is not delayed by so much as a frame.
 * Neither `dismissLoader` nor `failBoot` changes shape for any of this.
 * -------------------------------------------------------------------------- */
const INTRO_BEATS = [
  { at: 640,  name: 'thud',    level: 0.5 },
  { at: 1700, name: 'flap',    level: 0.2 },
  { at: 1760, name: 'flap',    level: 0.2 },
  { at: 1820, name: 'flap',    level: 0.2 },
  { at: 2350, name: 'jackpot', level: 0.35, pitch: 1.3 },
  { at: 2650, name: 'flash',   level: 0.25 },
];
/** Past this much of the splash the bed would start mid-CRT: stitch instead. */
const INTRO_BED_WINDOW_MS = 600;
/** After the knock (below) the splash lingers this long, so the bed's opening
 *  bar lands on the CRT and rides the exit into the campus reveal. */
const KNOCK_EXIT_MS = 700;
const introTimers = new Set();

/* THE KNOCK (web hosts only). `autoplayOk` is the WebView2 host's promise and
 * a browser never makes it: the mixer keeps its gesture gate, and the splash -
 * "the only cue that can never be re-fired" - would play its jingle into a
 * context that does not exist yet. So when the bed sample is shipped and the
 * host made no autoplay promise, the splash HOLDS on a small invitation and
 * the first pointer/key both arms the mixer (its own capture listeners run on
 * pointerdown, before our pointerup) and strikes the bed. Reduced motion gets
 * no cues and therefore no hold (trap 66), a host that promised autoplay is
 * untouched, and failBoot tears the whole thing down with the loader. */
let knockWanted = false;
let knockDone = false;
let knockDismissQueued = false;
let knockHint = null;

function disarmKnock() {
  try {
    if (doc && doc.removeEventListener) {
      doc.removeEventListener('pointerup', onKnock, true);
      doc.removeEventListener('keydown', onKnock, true);
    }
  } catch (e) { /* noop */ }
}

function onKnock() {
  disarmKnock();
  if (knockDone) return;
  knockDone = true;
  try { if (knockHint) knockHint.classList.add('is-heard'); } catch (e) { /* noop */ }
  if (!splashIsUp()) return;         // failBoot got there first: nothing to score
  cancelIntroCues();                 // the stitched beats yield to the real bed
  sfx('intro_bed', 0.6);
  if (knockDismissQueued) setTimeout(dismissLoader, KNOCK_EXIT_MS);
}

function armKnockGate() {
  const src = initMsg || {};
  if (src.autoplayOk === true) return;                       // the host's promise stands
  if (!!src.reducedMotion || src.motionLevel === 0) return;  // no cues, no hold (trap 66)
  if (!audio || typeof audio.hasSample !== 'function' || !audio.hasSample('intro_bed')) return;
  if (!doc || !doc.addEventListener) return;
  knockWanted = true;
  try {
    if (dom.loader && typeof document !== 'undefined' && document.createElement) {
      knockHint = document.createElement('div');
      knockHint.className = 'arc-boot-knock';
      knockHint.textContent = (src.lexicon && src.lexicon.intro_knock) || 'Knock to enter';
      dom.loader.appendChild(knockHint);
    }
  } catch (e) { knockHint = null; /* a hint may never cost the boot */ }
  doc.addEventListener('pointerup', onKnock, true);
  doc.addEventListener('keydown', onKnock, true);
}

/* ONE AUDIO DOOR (GROUND-RULES §6, shell/ceremonies.js's exact pattern): a cue
 * is a REQUEST on `document`, never a node - shell/audio.js owns the only audio
 * graph on the page (trap 18). A cue path must never be the thing that throws,
 * and a dropped cue is not an error. */
function sfx(name, level, extra) {
  try {
    if (!doc || typeof doc.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    doc.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* never fatal */ }
}

function cancelIntroCues() {
  for (const id of Array.from(introTimers)) { try { clearTimeout(id); } catch (e) { /* noop */ } }
  introTimers.clear();
}

/** The splash is still the thing on screen (and not an error card behind it). */
function splashIsUp() { return !!(dom.loader && !dom.loader.hidden); }

function scheduleIntroCues() {
  try {
    const src = initMsg || {};
    // The same derivation the shell uses for the class it paints on <html>.
    if (!!src.reducedMotion || src.motionLevel === 0) return;
    if (!splashIsUp()) return;
    const elapsed = Date.now() - introT0;
    // hasSample is feature-detected: an older consumer simply stitches.
    const hasBed = !!(audio && typeof audio.hasSample === 'function' && audio.hasSample('intro_bed'));
    if (hasBed && src.autoplayOk === true && elapsed < INTRO_BED_WINDOW_MS) {
      sfx('intro_bed', 0.6);
      return;
    }
    for (const beat of INTRO_BEATS) {
      const wait = beat.at - elapsed;
      if (wait <= 0) continue;          // that beat is behind us; it is not re-struck
      const id = setTimeout(() => {
        introTimers.delete(id);
        if (!splashIsUp()) return;      // failBoot, or the splash already left
        sfx(beat.name, beat.level, beat.pitch ? { pitch: beat.pitch } : null);
      }, wait);
      introTimers.add(id);
    }
  } catch (e) { /* the opening may never cost us the boot */ }
}

function dismissLoader() {
  const el = dom.loader;
  if (!el || el.hidden) return;
  /* The knock outranks readiness: a booted school still waits for the door.
   * onKnock re-calls us KNOCK_EXIT_MS after the tap that struck the bed. */
  if (knockWanted && !knockDone) { knockDismissQueued = true; return; }
  const wait = Math.max(0, INTRO_MIN_MS - (Date.now() - introT0));
  setTimeout(() => {
    try {
      if (el.hidden) return;             // a failBoot got there first
      el.classList.add('is-done');
      setTimeout(() => {
        try { el.hidden = true; } catch (e) { /* noop */ }
        try { if (shell && shell.onSplashDone) shell.onSplashDone(); }
        catch (e) { warn('onSplashDone threw: ' + ((e && e.message) || e)); }
      }, INTRO_EXIT_MS);
    } catch (e) { try { el.hidden = true; } catch (e2) { /* noop */ } }
  }, wait);
}

function failBoot(msg) {
  if (bootSettled) return;
  bootSettled = true;
  clearTimeout(deadlineTimer);
  warn(msg);
  bridge.send({ type: 'boot-error', msg: String(msg).slice(0, 400) });
  if (dom.loader) dom.loader.hidden = true;
  knockDone = true;       // the error card is not held for a knock
  disarmKnock();
  try { if (knockHint) knockHint.remove(); } catch (e) { /* noop */ }
  cancelIntroCues();      // a clearTimeout, so the error card waits for nothing
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
    /* THE MOBILE CLASS GOES ON FIRST. `html.arc-mobile` is what every phone rule
     * in the sheets keys off, and the splash is on screen for about three
     * seconds before the shell exists - painting it here rather than in
     * createShell is the difference between a loader that is already phone-shaped
     * and one that re-lays itself out the moment the school opens. Idempotent:
     * the shell calls it again for the harness case where boot never ran. */
    try { installDeviceClass(); } catch (e) { /* never worth a boot */ }
    // The sfx consumer first, so a cue fired during the shell's own boot is heard.
    // OPTIONAL by construction: no audio must never cost us the page.
    try {
      const am = await import('./shell/audio.js');
      /* AUTOPLAY IS THE HOST'S PROMISE, NOT OURS. The WebView2 runs with
       * --autoplay-policy=no-user-gesture-required, so `init.autoplayOk` is the
       * host saying the consumer may arm itself at creation instead of waiting
       * for a pointer. A host that predates the field sends nothing, the strict
       * `=== true` reads false, and the mixer keeps its gesture gate. */
      audio = am.createAudio({
        init: initMsg,
        bridge,
        log,
        autoplayOk: !!initMsg && initMsg.autoplayOk === true,
      });
      // The splash is already a second or so into its timeline: score the rest.
      scheduleIntroCues();
      // And on a gesture-gated host, hold the door for the knock instead.
      armKnockGate();
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
    dismissLoader();
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
  cancelIntroCues();
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
