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
/* THE DIRECT LAUNCH's two reads, and they are the only reason boot.js knows the
 * registry exists. Both are STATIC data (a frozen table and two Sets) - the game
 * modules themselves are dynamic imports inside `loadGames`, so this costs the
 * boot nothing it was not already paying. */
import { GAME_PATHS, isOpenSemester, gameTitle } from './games/registry.js';

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
// `log` is the funnel EVERY room reaches through `caps.log`/`say`, so its default level sets the
// volume of the whole campus. Quiet by default ('debug' is below the host's Information floor and
// is therefore dropped, not filed); pass a level as the second argument for anything a triage
// reader needs to see - `log(msg, 'warn')` for a degraded room, `'error'` for a broken one.
function log(msg, level) { bridge.log(level || 'debug', msg); }
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
 * THE DIRECT LAUNCH (the Discord per-class commands, 2026-08-30).
 *
 * `/deepend` and its nine siblings open this page with `init.launchGame` set to
 * ONE room. The player asked for a game, not for a school, so the boot they get
 * is the school's opening beat WITH THAT ROOM'S NAME UNDER IT and then the room
 * itself - the campus is never built, never revealed and never walked (shell.js
 * owns that half; see THE DIRECT LAUNCH there).
 *
 * boot.js owns the splash, so boot.js letters the plate, and it does it from
 * `init` alone: the title comes off the registry's static table (or the mod's
 * own `game_<key>` lexicon row), so the line is on screen from the first paint
 * rather than whenever the ten game modules finish importing.
 *
 * THREE THINGS THE DIRECT SPLASH DOES DIFFERENTLY, and all three are "brief":
 *   1. it leaves at DIRECT_MIN_MS instead of INTRO_MIN_MS,
 *   2. it never holds for the knock (the jingle is the campus's welcome, and a
 *      player who typed a command is not being welcomed; the mixer still arms
 *      itself on the first pointer inside the class, as it always has), and
 *   3. it never strikes the 4s intro bed for the same reason.
 * An UNKNOWN or RETIRED key is not a direct launch at all: the page boots the
 * ordinary campus and shell.js toasts the refusal, which is the same answer the
 * campus door gives.
 * -------------------------------------------------------------------------- */
/** The class this boot was asked for, or '' - resolved once, in the init handler. */
let directGame = '';

/** The requested room, IF it is a room this build actually has. */
function directLaunchKey(src) {
  const key = (src && typeof src.launchGame === 'string') ? src.launchGame : '';
  if (!key || !GAME_PATHS[key]) return '';
  try { if (!isOpenSemester(key)) return ''; } catch (e) { return ''; }
  return key;
}

/** Letter the plate. Idempotent, guarded, and never worth a boot. */
function dressDirectLaunch() {
  if (!directGame || !dom.loader || !doc) return;
  try {
    const line = dom.loader.querySelector('.arc-intro-class');
    const name = line && line.querySelector('.arc-intro-class-name');
    if (!line || !name) return;
    name.textContent = gameTitle(directGame, initMsg && initMsg.lexicon);
    line.hidden = false;
    dom.loader.classList.add('is-direct');
  } catch (e) { /* a plate may never cost the boot */ }
}

/** ...and take it off again when the shell refused the launch (a card that is
 *  not full, or a suspend): the player is landing on the campus, so the splash
 *  must not still be promising a room. shell.js fires the event; the ordinary
 *  boot never does. */
function undressDirectLaunch() {
  directGame = '';
  if (!dom.loader) return;
  try {
    dom.loader.classList.remove('is-direct');
    const line = dom.loader.querySelector('.arc-intro-class');
    if (line) line.hidden = true;
  } catch (e) { /* noop */ }
}

if (doc && doc.addEventListener) {
  doc.addEventListener('arcademy-direct-launch', (e) => {
    try {
      const d = (e && e.detail) || {};
      log('direct launch: ' + (d.gameKey || '?') + ' ' + (d.ok ? 'opening the room' : 'refused - campus'));
      if (!d.ok) undressDirectLaunch();
    } catch (_e) { /* noop */ }
  });
}

/* ----------------------------------------------------------------------------
 * THE SPLASH DISMISSAL. The loader doubles as the ~3s intro splash (see
 * index.html): a boot that lands early WAITS OUT the beat instead of cutting
 * it, a boot that lands late dismisses on arrival. `.is-done` plays the CSS
 * exit (fade + zoom-through); the `hidden` attribute stays the ground truth a
 * beat later. FAILURE PATHS NEVER WAIT - failBoot below still snaps
 * `hidden = true` directly, so an error card is never delayed by a celebration.
 *
 * THE FLOOR IS NOT THE ONLY GATE. Since 2026-08-25 there are THREE things the
 * exit waits on, in this order, and each one is a "come back to me" rather than
 * a sleep - `dismissLoader` is re-entrant and every gate re-calls it:
 *     1. the KNOCK, on a gesture-gated host: no door, no campus.
 *     2. the INTRO BED, on both hosts: the splash may not leave in the middle
 *        of the jingle (see THE SPLASH WAITS FOR THE JINGLE below).
 *     3. INTRO_MIN_MS from t0, the CSS beat's own floor, which by then has
 *        almost always already passed.
 * Measured from t0 on the autoplay host: the wordmark thuds at .64s, the bed
 * runs about .3s to 4.3s, `.is-done` is added at ~4.3s and `hidden` lands at
 * ~4.6s. On a knock host everything after the strike is the same, measured
 * from the strike rather than from boot.
 *
 * THE SPLASH-COMPLETE EDGE (FIRST BELL, 2026-08-24) hangs off the HAPPY PATH of
 * this function and nowhere else: once `hidden` has actually landed, the shell
 * is told, and it decides whether the opening has anything to play. failBoot
 * never calls it, so an error card can no more be held up by a scene than by
 * the splash itself (trap 66). The call is guarded and fire-and-forget - the
 * loader comes down whatever the shell does with the news.
 * -------------------------------------------------------------------------- */
const INTRO_MIN_MS = 2950;
/** A DIRECT LAUNCH's floor. Short on purpose (the owner's word was "brief"):
 *  past the CRT power-on, the wordmark thud and the glint, and out before the
 *  sparkle tail - which `.is-direct` hides rather than cut in half. */
const DIRECT_MIN_MS = 1900;
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
/** The bed is a 4.0s piece and MUST SAY SO: the clip governor's silent default
 *  is 1.2s, which cut the jingle mid-phrase right as the splash exited (owner
 *  report, 2026-08-24). Length + air; the element's own `ended` stops it. */
const INTRO_BED_MAX_MS = 4600;
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
  /* W3 P0-32. The hint says "Knock to enter" and nothing ever knocked. It lands
   * BEFORE the bed so the bed reads as the door answering, and `knock` falls to
   * the `door` recipe on a build where the sample has not shipped. */
  sfx('knock', 0.4);
  strikeBed();
  /* KNOCK_EXIT_MS is now a FLOOR, not the whole wait: a bed that is still
   * playing sends this call straight back to the queue and settleBed re-issues
   * it. It governs on its own only for a knock whose bed never sounds (a muted
   * mixer, an absent file), which is the timing that host has always had. */
  if (knockDismissQueued) {
    const id = setTimeout(() => { exitTimers.delete(id); dismissLoader(); }, KNOCK_EXIT_MS);
    exitTimers.add(id);
  }
}

function armKnockGate() {
  const src = initMsg || {};
  if (directGame) return;                                    // a direct launch is not welcomed in
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

/* THE SPLASH STOPS PRETENDING TO LOAD (T2 tester report, 2026-08-26: sat on the
 * splash on a PC waiting for a rail that had already finished, because nothing
 * on screen changed when the school was ready).
 *
 * `dismissLoader` queues on the knock, and until now that queue was INVISIBLE:
 * the rail kept chasing, the caption kept saying Opening, and the one line that
 * mattered stayed a 12.5px dim caption at the bottom of the frame. This is the
 * escalation, and it runs exactly once - on the frame boot is done and the
 * gesture is the ONLY thing outstanding.
 *
 * `is-ready` is the whole visual half (styles.css): the rail lands full and
 * stops, the caption steps back, and the hint steps up to real size and full
 * ink with a soft scale pulse - which reduced motion drops while keeping the
 * size and the contrast, because the READING is the fix and the pulse is only
 * the seasoning.
 *
 * After KNOCK_WAIT_MS with still no gesture the LINE changes rather than
 * repeating: someone who has not understood "Knock to enter" needs different
 * words, not the same ones louder. The timer rides `introTimers`, so failBoot
 * and shutdown sweep it with everything else, and it re-checks the splash on
 * the way in - a beat can never land on the error card (trap 66's discipline).
 *
 * NO SOUND, EVER. The knock IS the autoplay gate: there is no cue this side of
 * it that any browser would let us play. */
const KNOCK_WAIT_MS = 4000;

function escalateKnock() {
  try { if (dom.loader) dom.loader.classList.add('is-ready'); } catch (e) { /* noop */ }
  if (!knockHint) return;
  const id = setTimeout(() => {
    introTimers.delete(id);
    if (knockDone || !knockHint || !splashIsUp()) return;
    try {
      const src = initMsg || {};
      knockHint.textContent =
        (src.lexicon && src.lexicon.intro_knock_wait) || 'Tap anywhere to knock';
    } catch (e) { /* a hint may never cost the boot */ }
  }, KNOCK_WAIT_MS);
  introTimers.add(id);
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

/* ----------------------------------------------------------------------------
 * THE SPLASH WAITS FOR THE JINGLE (owner report, 2026-08-25).
 *
 * `assets/sfx/intro_bed.mp3` is a 4.000s piece and the splash used to walk out
 * on it: the autoplay host dismissed at INTRO_MIN_MS + the exit (hidden ~3.27s
 * from t0, cutting the bed's last ~1.1s) and the knock host at KNOCK_EXIT_MS +
 * the exit (hidden ~1.02s after the strike, cutting ~3.0s). The campus revealed
 * mid-phrase on both. So THE STRIKE NOW OWNS THE EXIT: `dismissLoader` defers
 * while a bed is in the air and is re-called the moment it settles - which for
 * the knock path means the wait is measured FROM THE STRIKE, not from boot.
 *
 * THE HOLD IS ALWAYS RELEASED, and there are four ways out on purpose:
 *   - the consumer answers `onEnded` (shell/audio.js's one-shot hook): 'ended'
 *     when the element played the file out, 'cap' when its own maxMs governor
 *     cut it, 'stopped'/'error' when the clip was taken away from it.
 *   - 'dropped' comes back SYNCHRONOUSLY, inside the dispatch, whenever the cue
 *     never sounded at all: a muted mixer, a zero master or fx bus, no audio
 *     context, or no file behind the name. No sound means NO HOLD, so all of
 *     those dismiss on precisely the timing they had before this wave.
 *   - our own cap timer, for a consumer that answers nothing whatever (an older
 *     shell/audio.js, a dropped event, an element that never fires anything):
 *     BED_HOLD_CAP_MS after the strike the hold is over, whatever the audio is
 *     doing. A stalled or blocked element can NEVER hold the splash hostage.
 *   - failBoot and shutdown, which clear the hold with everything else.
 *
 * WHAT DID NOT MOVE: reduced motion gets no cues, so it strikes no bed, so it
 * holds for nothing and dismisses exactly as before (trap 66). The campus and
 * the first frame are still built underneath while the bed plays - only the
 * EXIT waits; `start()` is not delayed by so much as a frame.
 * -------------------------------------------------------------------------- */
/** The hold's own deadline, measured from the strike. INTRO_BED_MAX_MS is
 *  already the 4.0s piece plus air, and the consumer's governor answers us from
 *  inside it; the slack on top is for the answer that never comes at all. */
const BED_HOLD_CAP_MS = INTRO_BED_MAX_MS + 200;
let bedStruck = false;        // the bed is the one cue that may never be re-fired
let bedHolding = false;       // a bed is in the air and the exit is waiting on it
let bedDismissQueued = false; // ...and a ready boot has already asked to leave
let bedCapTimer = 0;

function strikeBed() {
  if (bedStruck) return;
  bedStruck = true;
  bedHolding = true;
  sfx('intro_bed', 0.6, {
    maxMs: INTRO_BED_MAX_MS,
    onEnded: (reason) => settleBed(reason || 'ended'),
  });
  // A cue that was never going to sound has already answered by now, inside the
  // dispatch above - so there is no hold to cap and no timer to mint.
  if (!bedHolding) return;
  bedCapTimer = setTimeout(() => {
    bedCapTimer = 0;
    settleBed('cap-local');
  }, BED_HOLD_CAP_MS);
}

function settleBed(reason) {
  if (!bedHolding) return;              // already settled, or never struck
  bedHolding = false;
  if (bedCapTimer) { clearTimeout(bedCapTimer); bedCapTimer = 0; }
  stopEarlyBed();
  log('intro bed settled (' + reason + ') at ' + (Date.now() - introT0) + 'ms');
  if (!bedDismissQueued) return;
  bedDismissQueued = false;
  dismissLoader();
}

function cancelBedHold() {
  bedHolding = false;
  bedDismissQueued = false;
  if (bedCapTimer) { clearTimeout(bedCapTimer); bedCapTimer = 0; }
  stopEarlyBed();
}

/* ----------------------------------------------------------------------------
 * THE BED STRIKES ON THE FIRST FRAME (owner report, third time of asking,
 * 2026-08-25: "wait for the jingle to end before entering, the jingle should
 * start sooner").
 *
 * The hold above was right and still did nothing on the desktop, because the
 * bed it waits for was never struck there. strikeBed lives behind the mixer,
 * the mixer lives behind `init`, and on the WebView2 host `init` lands about
 * three seconds after the page (log: launched 10:28:49.686, sent init
 * 10:28:52.922) - five times INTRO_BED_WINDOW_MS. scheduleIntroCues therefore
 * took the stitched beats every time, the splash walked out at INTRO_MIN_MS,
 * and the one jingle in the school played late or not at all.
 *
 * So on the app host the bed is a plain media element struck HERE, at module
 * evaluation, before anyone has said init - the same file, the same level, the
 * same hold. `bedStruck` is set, so the mixer's strikeBed later declines (the
 * bed is the one cue that may never be re-fired) and scheduleIntroCues deals no
 * stitched beats over it. When init does land, tuneEarlyBed() applies the real
 * levels: the mixer's own element math (sqrt(level) * CLIP_GAIN * fx * master),
 * and a muted school, a zero bus, or reduced motion (trap 66: no cues) STOPS it
 * and releases the hold on the spot, which is the timing those hosts had.
 *
 * WHICH HOST: only a real WebView2 (`window.chrome.webview` without the web
 * shim's `__deliver` seam). The shim fakes the bridge for app.cclabs.app, where
 * play() without a gesture is refused - and a refused play hands the strike
 * back (bedStruck cleared) so the knock strikes the bed through the mixer as it
 * always has. A missing file 404s into 'error', which releases the hold with
 * nothing else changed. Nothing here can throw past its try.
 * -------------------------------------------------------------------------- */
const EARLY_BED_URL = './assets/sfx/intro_bed.mp3';   // shell/audio.js SAMPLES.intro_bed
const EARLY_BED_LEVEL = 0.6;   // what strikeBed asks the mixer for
const EARLY_BED_GAIN = 0.5;    // shell/audio.js CLIP_GAIN
const EARLY_BED_FX = 0.85;     // shell/audio.js DEFAULT_LEVELS.fx
let earlyBed = null;           // the element in the air, or null
let earlyBedStruckAt = -1;     // ms from introT0, -1 = never (test seam)

function clamp01(v) { v = Number(v); return v > 1 ? 1 : (v > 0 ? v : 0); }
function earlyBedVolume(fx, master) {
  return clamp01(Math.sqrt(EARLY_BED_LEVEL) * EARLY_BED_GAIN * clamp01(fx) * clamp01(master));
}
function isRealWebView2() {
  try {
    const wv = win && win.chrome && win.chrome.webview;
    return !!wv && typeof wv.__deliver !== 'function';
  } catch (e) { return false; }
}

function stopEarlyBed() {
  const el = earlyBed;
  if (!el) return;
  earlyBed = null;
  try { el.pause(); } catch (e) { /* noop */ }
  try { el.removeAttribute('src'); el.load(); } catch (e) { /* noop */ }
}

function strikeEarlyBed() {
  try {
    if (bedStruck || !isRealWebView2() || !splashIsUp()) return;
    if (typeof Audio !== 'function') return;
    const el = new Audio(EARLY_BED_URL);
    el.preload = 'auto';
    el.volume = earlyBedVolume(EARLY_BED_FX, 1);
    bedStruck = true;
    bedHolding = true;
    earlyBed = el;
    earlyBedStruckAt = Date.now() - introT0;
    const settle = (reason) => { if (earlyBed !== el) return; settleBed(reason); };
    el.addEventListener('ended', () => settle('ended'));
    el.addEventListener('error', () => settle('error'));
    let p = null;
    try { p = el.play(); } catch (e) { p = null; }
    if (p && typeof p.catch === 'function') {
      p.catch(() => {
        // Refused without a gesture: this is a browser after all. Hand the
        // strike back to the knock, which reaches the bed through the mixer.
        if (earlyBed !== el) return;
        settleBed('blocked');
        bedStruck = false;
      });
    }
    bedCapTimer = setTimeout(() => { bedCapTimer = 0; settleBed('cap-local'); }, BED_HOLD_CAP_MS);
    log('intro bed struck early at ' + earlyBedStruckAt + 'ms');
  } catch (e) { /* the opening may never cost us the boot */ }
}

/** init has landed: the early bed takes the school's real levels, or stops. */
function tuneEarlyBed() {
  const el = earlyBed;
  if (!el) return;
  try {
    const src = initMsg || {};
    const fx = (src.audioLevels && src.audioLevels.fx != null) ? clamp01(src.audioLevels.fx) : EARLY_BED_FX;
    const master = src.masterVolume == null ? 1 : clamp01(src.masterVolume);
    const quiet = !!src.audioMute || master <= 0 || fx <= 0
      || !!src.reducedMotion || src.motionLevel === 0;
    if (quiet) { settleBed('muted-at-init'); return; }
    el.volume = earlyBedVolume(fx, master);
  } catch (e) { /* a volume is not worth the boot */ }
}

/* ----------------------------------------------------------------------------
 * THE FEED SLATE (THE SEEP, tell 10). One boot in forty, a single ~80ms frame
 * inside the loader: a green slate saying a camera feed dropped, before the
 * campus fades up like nothing happened. The very first tell most players will
 * ever half-see, and the ONLY one that exists at tier zero.
 *
 * FOUR THINGS KEEP IT OUT OF THE BOOT'S WAY, and they are the whole design:
 *   - the module is reached for with a DYNAMIC import that nothing awaits. boot
 *     never blocks on it, a module that fails to load costs the frame and
 *     nothing else, and `shell/shell.js` imports the same file a moment later
 *     anyway, so the fetch is not even extra work.
 *   - it reads the tier off the RAW `init.meta` blob (`sealedFromMeta`), because
 *     there is no store, no registry and no shell this early.
 *   - the loader's contract is untouched (trap 66): the slate is a child that is
 *     appended and taken away again, and `hidden` / `.is-done` still mean
 *     exactly what boot.js has always meant by them. A slate whose beat has
 *     already passed is never struck late, and failBoot cancels it outright -
 *     an error card is not delayed by a haunting any more than by a celebration.
 *   - reduced motion never sees it (the photosensitivity note), and the roll is
 *     SEEDED on the UTC day plus the hour, so a reload replays the same answer
 *     rather than handing a player a re-roll.
 * -------------------------------------------------------------------------- */
/** ~40% through the ~3s splash: past the CRT power-on, before the rail deals. */
const SLATE_AT_MS = 1150;
let cancelSlate = null;

function armFeedSlate() {
  if (cancelSlate || !dom.loader) return;
  /* ...and never on a DIRECT LAUNCH: that splash leaves at 1.9s, which is half
   * a slate, and half a haunting is a glitch rather than a beat. */
  if (directGame) return;
  const src = initMsg || {};
  try {
    import('./shell/seep.js').then((m) => {
      try {
        if (!m || cancelSlate || !splashIsUp()) return;
        const opts = m.seepOptions();
        if (opts.off) return;
        const tier = opts.tier != null ? opts.tier : m.tierForSealed(m.sealedFromMeta(src.meta));
        const want = m.slateWanted({
          utcDay: src.utcDateSeed,
          tier,
          reducedMotion: !!src.reducedMotion || src.motionLevel === 0,
          forced: opts.forced,
        });
        if (!want) return;
        const elapsed = Date.now() - introT0;
        cancelSlate = m.mountFeedSlate(dom.loader, { atMs: Math.max(0, SLATE_AT_MS - elapsed) });
      } catch (e) { /* a haunting may never cost the boot */ }
    }).catch(() => {});
  } catch (e) { /* noop */ }
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
    if (bedStruck) return;            // the early bed owns the opening: no stitched beats over it
    const elapsed = Date.now() - introT0;
    // hasSample is feature-detected: an older consumer simply stitches.
    const hasBed = !!(audio && typeof audio.hasSample === 'function' && audio.hasSample('intro_bed'));
    if (hasBed && !directGame && src.autoplayOk === true && elapsed < INTRO_BED_WINDOW_MS) {
      strikeBed();
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

/** The two timers of the exit itself, OWNED so failBoot and shutdown can take
 *  them back: a page that is tearing down owes nobody a fade. */
const exitTimers = new Set();

function cancelExitTimers() {
  for (const id of Array.from(exitTimers)) { try { clearTimeout(id); } catch (e) { /* noop */ } }
  exitTimers.clear();
}

function dismissLoader() {
  const el = dom.loader;
  if (!el || el.hidden) return;
  /* The knock outranks readiness: a booted school still waits for the door.
   * onKnock re-calls us KNOCK_EXIT_MS after the tap that struck the bed.
   * THE FIRST TIME WE QUEUE, THE SPLASH SAYS SO (escalateKnock): this is the
   * one frame on which the wait stops being a boot and starts being a door. */
  if (knockWanted && !knockDone) {
    if (!knockDismissQueued) { knockDismissQueued = true; escalateKnock(); }
    return;
  }
  /* And the jingle outranks both: settleBed() re-calls us the moment the bed is
   * over, or the moment the cap says it is (THE SPLASH WAITS FOR THE JINGLE). */
  if (bedHolding) { bedDismissQueued = true; return; }
  const wait = Math.max(0, (directGame ? DIRECT_MIN_MS : INTRO_MIN_MS) - (Date.now() - introT0));
  const id = setTimeout(() => {
    exitTimers.delete(id);
    try {
      if (el.hidden) return;             // a failBoot got there first
      el.classList.add('is-done');
      const done = setTimeout(() => {
        exitTimers.delete(done);
        try { el.hidden = true; } catch (e) { /* noop */ }
        try { if (shell && shell.onSplashDone) shell.onSplashDone(); }
        catch (e) { warn('onSplashDone threw: ' + ((e && e.message) || e)); }
      }, INTRO_EXIT_MS);
      exitTimers.add(done);
    } catch (e) { try { el.hidden = true; } catch (e2) { /* noop */ } }
  }, wait);
  exitTimers.add(id);
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
  cancelBedHold();        // ...and the jingle holds up an error card least of all
  cancelExitTimers();     // a fade already in flight is abandoned, not finished
  try { if (cancelSlate) cancelSlate(); } catch (e) { /* noop */ }
  cancelSlate = null;
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
    /* An early bed takes the school's real levels first, or stops if the school
     * is muted - BEFORE the mixer, because it needs only `init`, and a host whose
     * audio consumer fails to load must not keep playing at the default level. */
    tuneEarlyBed();
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
  /* THE STORE-BUILD FLAG, copied onto the window before anything downstream is
   * imported. `core/storesafe.js` is the reader and games/* are the callers; a
   * host that never sets the field (the desktop, the web build, Android) leaves
   * this undefined, which reads as false, which is the full school. */
  try { window.__ccpStoreSafe = !!(m.platform && m.platform.storeSafe); }
  catch (e) { /* noop - a page with no window is a page with no games */ }
  directGame = directLaunchKey(m);
  dressDirectLaunch();            // THE DIRECT LAUNCH: name the room on the splash
  bridge.markInitialized();       // flush anything the page queued pre-init
  armBootDeadline();              // progress milestone
  armFeedSlate();                 // THE SEEP, tell 10 - fire and forget
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
/* THE PRIZE COUNTER'S ECHO. The host answers every `prize-buy` with exactly one
 * of these, ok or not, and the Prize Counter paints nothing until it arrives -
 * so this subscription is not a nicety, it is the other half of the buy. Like
 * every frame on this deck it REPLAYS if it beat the shell here. */
bridge.on('wallet-result', guard('wallet-result', (m) => {
  if (shell && shell.onWalletResult) shell.onWalletResult(m);
  log('wallet: ' + (m.sku || '?') + ' ' + (m.ok ? 'bought' : 'refused (' + (m.reason || '?') + ')'));
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
/* THE STUDENT ID's profile (the STUDENT ID contract). Additive and OPTIONAL on
 * both sides: a host that never pushes one leaves the card drawing "Student"
 * and the drawn stand-in portrait, and a shell built before this frame existed
 * simply has no `onProfile` - hence the same `shell &&` guard `annex-stats`
 * carries. Like `punchcard-result` it needs no buffer of its own: `bridge.on`
 * REPLAYS anything that landed before this subscription existed (trap 11). */
bridge.on('profile', guard('profile', (m) => {
  if (shell && shell.onProfile) shell.onProfile(m);
  log('profile' + (m && m.result ? ' (' + m.result + ')' : '')
    + (m && m.profile ? (m.profile.discordLinked ? ' linked' : ' not linked') : ' [no body]'));
}));
bridge.on('meta', guard('meta', (m) => { if (shell) shell.onMeta(m); }));
bridge.on('annex-stats', guard('annex-stats', (m) => { if (shell && shell.onAnnexStats) shell.onAnnexStats(m); }));
bridge.on('share-image-result', guard('share-image-result', (m) => {
  if (shell && shell.onShareImageResult) shell.onShareImageResult(m);
}));
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
  cancelBedHold();
  cancelExitTimers();
  try { if (cancelSlate) cancelSlate(); } catch (e) { /* best effort */ }
  cancelSlate = null;
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

/* THE CLOCK UNDER THE HOLD (W3 P0-33, vn/index.js's skip pill verbatim). 1200ms
 * of nothing is a long time to wonder whether the key was heard, so the reach
 * for the door counts itself out loud: one soft tick every 250ms, climbing a
 * step each time, and the door itself on the way out. The first tick is 250ms
 * in, so a TAP is silent and the ladder's rungs keep their own voices. It stops
 * three ways - a release, the hold completing, and losing the window. */
const ESC_TICK_MS = 250;
let escTickTimer = 0;
let escTicks = 0;

function stopEscTicks() {
  if (escTickTimer) { clearTimeout(escTickTimer); escTickTimer = 0; }
}

function escTick() {
  escTickTimer = 0;
  escTicks += 1;
  sfx('clock_tick', 0.2, { pitch: 1 + 0.08 * escTicks });
  escTickTimer = setTimeout(escTick, ESC_TICK_MS);
}

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
    escTicks = 0;
    escTickTimer = setTimeout(escTick, ESC_TICK_MS);   // W3 P0-33
    escTimer = setTimeout(() => {
      escTimer = 0;
      stopEscTicks();
      // W3 P0-33: the hold landed. The school shuts its door behind you, and
      // the cue goes out before the teardown because nothing may delay an exit.
      sfx('door', 0.3);
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
    // W3 P0-33: let go part-way and the count-out gets its own answer. A tap
    // never ticked, so it never sighs either - the rung below keeps that beat.
    stopEscTicks();
    if (escTicks > 0) sfx('slide', 0.18, { pitch: 0.9 });
    escTicks = 0;
    // A tap: walk the ladder one rung.
    let consumed = false;
    try { consumed = !!(shell && shell.escapeStep()); }
    catch (err) { bridge.log('error', 'escapeStep threw: ' + ((err && err.message) || err)); }
    if (consumed) return;
    if (fullscreen) { bridge.send({ type: 'fullscreen-request', on: false }); return; }
    showEscHint();
  });

  /* A HOLD THAT LOSES THE WINDOW IS NOT A HOLD. The exit timer is only ever
   * cancelled by keyup - but if focus leaves mid-press (alt-tab, a popup
   * stealing the foreground, the host suspending us), the keyup lands in some
   * other window, the timer completes on its own, and the player watches the
   * Arcademy dismiss itself. Losing the window cancels the hold outright and
   * never counts as a tap: the ladder must only ever move on a real keyup. */
  const cancelEscHold = () => {
    if (escIntentTimer) { clearTimeout(escIntentTimer); escIntentTimer = 0; }
    if (escTimer) { clearTimeout(escTimer); escTimer = 0; }
    stopEscTicks();                 // W3 P0-33: a lost window ticks no further
    escTicks = 0;
  };
  win.addEventListener('blur', cancelEscHold);
  if (doc) doc.addEventListener('visibilitychange', () => { if (doc.hidden) cancelEscHold(); });

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
strikeEarlyBed();

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

/** Test seam: the opening bed - struck, holding, early element live, ms of the early strike. */
export function introBedState() {
  return { struck: bedStruck, holding: bedHolding, early: !!earlyBed, earlyAt: earlyBedStruckAt };
}

/** Test seam: the suspend frame buffered while the shell was still booting. */
export function bufferedSuspend() { return pendingSuspend; }
