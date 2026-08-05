/* ============================================================================
 * boot.js — entry point for the Goon Game page (1v1 endurance duel).
 *
 * Boot order (load-bearing):
 *   error seams -> register EVERY bridge handler -> announceReady() -> the host
 *   flushes `init` (identity/caps/consent/net) then `manifest` (media URLs) ->
 *   build the singletons -> mount the title screen. Standalone (?solo=1) the
 *   same two frames are synthesized by bridge.js, so there is exactly ONE code
 *   path below.
 *
 * WHAT THIS FILE OWNS
 *   1. page lifecycle: handshake, liveness, exit, fullscreen, boot deadlines;
 *   2. the SINGLETONS every screen reads (prefs, audio, toasts, sheets, options,
 *      router, matchLog, executor);
 *   3. the createMatch FACTORY GoonSession injects — RNG, caps narrowing,
 *      sudden-death runner, presenter wiring all happen in one closure;
 *   4. ATTACH/DETACH on currentMatch changes. The relay fallback REBUILDS the
 *      match object (net/session.js _fallBackToRelay), so nothing may cache the
 *      first instance — the whole executor/log/HUD graph re-binds here;
 *   5. the Escape ladder (mercy is never more than one keypress away);
 *   6. phase-driven routing;
 *   7. Practice mode: a loopback pair and a scripted opponent, so the game is
 *      fully playable with no server and no second machine.
 *
 * NOTHING may throw at module import — WebView2 has no devtools, so a throw is
 * a silent infinite loader. Every DOM touch below is inside a function guarded
 * on `document`; the statically-imported modules are side-effect free by
 * contract, and the modules owned by sibling waves (exec/executor, ui/hud,
 * ui/mercy, ui/sd) are loaded DYNAMICALLY and optionally — a wave that has not
 * landed yet degrades to a logged stub instead of a blank page.
 * ==========================================================================*/

import * as bridge from './bridge.js';
import { media } from './exec/media.js';
import * as layers from './exec/layers.js';
// The per-match record of which artifacts a duel PARTNER put on this screen —
// exec/videos.js owns it, the recap's report card reads it, and this file is
// the only thing that resets it (see attachMatch). Import-safe: videos.js
// touches no DOM at module scope.
import { peerRenderLog, resetPeerRenderLog } from './exec/videos.js';
// The device performance tier. The DETECTOR lives in exec/ (with the renderers
// that obey it) and the PREF lives in ui/prefs.js (`perfMode`); this file is
// where they meet, because boot imports both tiers by charter — see buildApp.
import { applyPerfTier } from './exec/perfTier.js';

import { GoonMatchService } from './core/match.js';
import { GoonSuddenDeathRunner } from './core/suddenDeath.js';
import { GoonRng } from './core/rng.js';
import {
  GoonElement, GoonEndReason, GoonMatchPhase, GoonPayloadKind, GoonRoundKind, VOICE_CAP_VERSION,
} from './core/contracts.js';
import { local as localCapsOf, UNIVERSAL_ROUND } from './core/caps.js';
import { GoonReceiptStatus } from './core/scoring.js';
import { GoonSession } from './net/session.js';
import { createLoopbackPair, loopbackPresets } from './net/loopbackTransport.js';
import { createMediaQueue } from './net/mediaQueue.js';
import { probeDecodeCodecs } from './net/codecs.js';
import { createBlocklist } from './net/blocklist.js';
import { createReceivedStore } from './exec/receivedStore.js';

import { createRouter } from './ui/router.js';
import { createPrefs } from './ui/prefs.js';
import { createAudio } from './ui/audio.js';
import { createToasts } from './ui/toasts.js';
import { createCoach, COACH } from './ui/coach.js';
import { createSheets } from './ui/sheets.js';
import { createOptions } from './ui/options.js';
import { createSoloDriver } from './ui/soloDriver.js';
import { createAssetsStore, localPlayableEntries } from './ui/assetsStore.js';
import { createDiscord, confirmOpenDm } from './ui/discord.js';
import { emitAva, mountVsSplash } from './ui/avatar.js';
import { createWakeLock } from './ui/wakeLock.js';
// Statically imported like the rest of the wave-1 tier (and unlike ui/hud.js et
// al, which are sibling waves): this module has no DOM, no AudioContext and no
// bridge call at import time, and a duel where the voice service silently failed
// to load would be a duel where a consent the player gave has no effect.
import { createVoiceService } from './ui/voice/voiceService.js';
// ...and the library the service loads pre-recorded notes from. Same reasoning,
// plus one more: it is the ONE writer of prefs.voiceEmoteMap, and two of those
// would be two answers to "which note does this emote fire".
import { createNoteStore } from './ui/voice/noteStore.js';
import { setVoiceProvider } from './ui/emotes.js';
import { consumeJoinCode } from './ui/inviteLink.js';
import { S } from './ui/strings.js';

import * as titleScreen from './ui/screens/title.js';
import * as hostScreen from './ui/screens/host.js';
import * as joinScreen from './ui/screens/join.js';
import * as mediaSetupScreen from './ui/screens/mediaSetup.js';
import { needsMediaSetup } from './ui/screens/mediaSetup.js';
import * as lobbyScreen from './ui/screens/lobby.js';
import * as draftScreen from './ui/screens/draft.js';
import * as countdownScreen from './ui/screens/countdown.js';
import * as recapScreen from './ui/screens/recap.js';
import * as assetsScreen from './ui/screens/assets.js';
import * as voiceScreen from './ui/screens/voice.js';

/* --- ERROR SEAMS FIRST: everything after this reports instead of vanishing. */
if (typeof window !== 'undefined') {
  try {
    window.addEventListener('error', (e) => {
      const src = e.filename ? ' @ ' + String(e.filename).split('/').pop() + ':' + e.lineno : '';
      bridge.log('error: ' + (e.message || 'script error') + src);
    });
    window.addEventListener('unhandledrejection', (e) => {
      const r = e && e.reason;
      bridge.log('promise: ' + ((r && (r.message || r.stack || r)) || 'unknown'));
    });
  } catch (_e) { /* never throw at import */ }
}

const WARM_MS = 12000;   // hosted: "warming up" line in the loader
const BOOT_MS = 45000;   // hosted: give up and tell C#
const EXIT_FALLBACK_MS = 1200;
const ESC_HOLD_MS = 1200;

/**
 * How long after `matchEnded` we stop waiting for the countersignature and put
 * the recap in front of the player anyway.
 *
 * The engine already has a 10 s result handshake (core/match.js
 * RESULT_HANDSHAKE_TIMEOUT_MS, matching GoonMatchService.ResultHandshakeTimeoutMs)
 * and _endMatch flips the phase to Recap synchronously, so in the healthy case
 * this timer never does anything. It exists for the case the owner actually hit:
 * SOMETHING between the concede and the recap — an overlay, a screen that failed
 * to mount, a peer that vanished mid-handshake — leaves the player looking at a
 * dead end. Ending a match must always, always land on the recap.
 */
const RECAP_FALLBACK_MS = 4000;

/* ----------------------------------------------------------------------------
 * SESSION — the page's read-only view of who we are and what we agreed to.
 * Everything downstream (lobby, draft, HUD, executors) reads THIS, never the raw
 * init frame. The auth token is deliberately NOT here: it lives inside bridge's
 * net config so no screen can accidentally render or log it.
 * -------------------------------------------------------------------------- */
export const session = {
  protocol: bridge.PROTOCOL,
  hosted: bridge.isHosted,
  solo: false,
  identity: null,     // {unifiedId, displayName, appVersion}  (normalized below)
  caps: null,         // {haptics, brainDrain, camera, video, ...}
  consent: null,      // {liveDurationSec, toyCap, payloadMinGapMs}
  match: null,        // {mode, profile, skewMs} — standalone only; the host deals real matches
  net: null,          // {serverBase, viaHost, hasToken} — never the token itself
  prefs: null,
  fullscreen: false,
  haptics: null,      // last haptics-state {enabled, toys, cap}
  manifest: null,     // {images, videos, skipped, truncated}
  received: null,     // [{sha,ext,mime,bytes}] the inbox already holds (manifest frame)
  /**
   * {code, token, role} — stashed the moment a transport connects, because
   * actions.leave() DISPOSES the transport and the recap's report card is filed
   * after that. The token is a per-room signaling credential, not the Patreon
   * bearer, and it never leaves this object.
   */
  room: null,
  /**
   * The raw `init.discord` block (GOON_DISCORD_CONTRACT §4), parked here until
   * buildApp() exists to hand it to ui/discord.js. It is the ONLY frame that
   * carries `lastOpponent`; every later `discord` echo is flat and goes straight
   * to the module, which owns that verb at the bridge.
   */
  discord: null,
  initAt: 0,
  ready: false,       // init + manifest both in
};

let gotInit = false;
let gotManifest = false;
let bootSettled = false;
let warmTimer = 0;
let deadlineTimer = 0;
let exitTimer = 0;
let exiting = false;

const hasDom = () => typeof document !== 'undefined';
const el = (id) => (hasDom() ? document.getElementById(id) : null);

/* ----------------------------------------------------------------------------
 * LOGGER — one console-shaped object for the whole engine. info/debug go to the
 * browser console only; warn/error tunnel to C# as well, because those are the
 * lines a support log has to contain. The engine is CHATTY at info (every
 * payload, every phase) and the C# log is 400 chars per line — flooding it
 * would push the useful lines out.
 * -------------------------------------------------------------------------- */
/**
 * THE THIRD SINK (ui/debugOverlay.js, `?debug=1` only). warn/error already go to
 * the console and down the C# tunnel; neither of those exists on a phone running
 * this page standalone, which is where the failures nobody can read happen. The
 * tee is null until the overlay lands, and `debugPrelog` holds what was said
 * before that — losing the sibling-load warnings, the first thing to go wrong in
 * a broken boot, would defeat the point.
 */
let debugOverlay = null;
let debugArming = false;
const debugPrelog = [];
/**
 * ui/perfProbe.js, armed only alongside the overlay. It is the 2026-08-05
 * play-test's instrument: the phone reports lag, code-reading has run out of
 * road, and the next session has to come back with fps + frame-gap + long-task
 * numbers instead of an adjective. Its warn lines go through `logger.warn`
 * BELOW — one call that reaches the C# log, the console and the overlay's ring
 * at once — and its one-line readout goes to the overlay's status row.
 */
let perfProbe = null;

function teeDebug(level, m) {
  if (debugOverlay) { debugOverlay.push(level, m); return; }
  if (debugArming && debugPrelog.length < 50) debugPrelog.push([level, m]);
}

const logger = {
  debug: (m) => { try { console.debug('[gg]', m); } catch (_e) { /* ignore */ } },
  info: (m) => { try { console.info('[gg]', m); } catch (_e) { /* ignore */ } },
  warn: (m) => { try { console.warn('[gg]', m); } catch (_e) { /* ignore */ } bridge.log('warn: ' + m); teeDebug('warn', m); },
  error: (m) => { try { console.error('[gg]', m); } catch (_e) { /* ignore */ } bridge.log('error: ' + m); teeDebug('error', m); },
};

/**
 * Cheap pre-check, so the module is not even FETCHED unless somebody asked for
 * it — hosted especially, where a debug strip must never appear because a
 * browser tab once had one. The module owns the authoritative answer
 * (debugRequested); this only decides whether to go and ask it.
 */
function wantsDebugHint() {
  try {
    const s = (typeof location !== 'undefined' && location.search) || '';
    if (/[?&]debug\b/.test(s)) return true;
    if (bridge.isHosted) return false;          // standalone remembers, hosted never does
    const p = bridge.storedPrefs();
    return !!(p && p.debug);
  } catch (_e) { return false; }
}

/** Loaded dynamically and optionally, exactly like the sibling waves. */
function initDebugOverlay() {
  if (!hasDom() || !wantsDebugHint()) return;
  debugArming = true;
  try {
    import('./ui/debugOverlay.js')
      .then((mod) => {
        if (!mod || typeof mod.createDebugOverlay !== 'function') return;
        const want = mod.debugRequested({
          search: (typeof location !== 'undefined' && location.search) || '',
          prefs: bridge.storedPrefs(),
          hosted: bridge.isHosted,
        });
        if (!want) { debugArming = false; debugPrelog.length = 0; return; }
        debugOverlay = mod.createDebugOverlay({});
        mod.captureGlobalErrors(debugOverlay);
        debugOverlay.push('warn', 'debug on — ' + (bridge.isHosted ? 'hosted' : 'standalone')
          + ' protocol v' + bridge.PROTOCOL);
        for (const [lvl, m] of debugPrelog.splice(0)) debugOverlay.push(lvl, m);
        initPerfProbe();
      })
      .catch(() => { debugArming = false; });
  } catch (_e) { debugArming = false; }
}

/**
 * The perf readout. A SECOND dynamic import, nested inside the branch that has
 * already decided debug is on, so a shipped page never fetches it and a page
 * whose probe module is missing still gets its overlay — same "optional wave"
 * shape as loadSiblings, for the same reason.
 */
function initPerfProbe() {
  if (perfProbe || !debugOverlay) return;
  try {
    import('./ui/perfProbe.js')
      .then((mod) => {
        if (!mod || typeof mod.createPerfProbe !== 'function' || !debugOverlay) return;
        perfProbe = mod.createPerfProbe({
          setStatus: (t) => { try { debugOverlay.setStatus(t); } catch (_e) { /* ignore */ } },
          // logger.warn, NOT overlay.push: warn is the sink that reaches the C# host
          // log as well, and a phone-only number nobody can collect is not telemetry.
          onWarn: (m) => logger.warn(m),
        });
        perfProbe.start();
      })
      .catch(() => { /* no readout, no harm — the overlay still logs */ });
  } catch (_e) { /* ditto */ }
}

/* ----------------------------------------------------------------------------
 * SIBLING WAVES — loaded dynamically so a wave that has not merged yet is a
 * logged stub, not a white page. `stubs` is what the play-test report reads.
 * -------------------------------------------------------------------------- */
const stubs = [];
let createExecutor = null;    // exec/executor.js
let mountHud = null;          // ui/hud.js
let mountMercy = null;        // ui/mercy.js
let createSuddenDeathUi = null; // ui/sd/index.js

async function loadSiblings() {
  const want = [
    ['../exec/executor.js', './exec/executor.js', 'createExecutor', (v) => { createExecutor = v; }],
    ['./hud.js', './ui/hud.js', 'mountHud', (v) => { mountHud = v; }],
    ['./mercy.js', './ui/mercy.js', 'mountMercy', (v) => { mountMercy = v; }],
    ['./sd/index.js', './ui/sd/index.js', 'createSuddenDeathUi', (v) => { createSuddenDeathUi = v; }],
  ];
  for (const [, path, name, assign] of want) {
    try {
      const mod = await import(path);
      const fn = mod && mod[name];
      if (typeof fn !== 'function') throw new Error('no export ' + name);
      assign(fn);
    } catch (e) {
      stubs.push(name);
      logger.warn('sibling module ' + path + ' unavailable (' + ((e && e.message) || e) + ') — running without ' + name);
    }
  }
  if (stubs.length) bridge.log('running without: ' + stubs.join(', '));
}

/* ----------------------------------------------------------------------------
 * HANDLERS — one per type (bridge.on throws on a duplicate, on purpose).
 * `net-post-result` is consumed INSIDE bridge.js; registering it here would
 * throw at wiring time. That is the intended alarm, not a bug.
 * -------------------------------------------------------------------------- */
bridge.on('init', (m) => {
  gotInit = true;
  session.solo = !!m.solo;
  // Normalize identity ONCE, here: the host sends {unifiedId, displayName,
  // appVersion} (GoonHostService.OnPageReady) and a future mobile/web host may
  // send {id, name}. Screens read session.identity.displayName and nothing else.
  const idm = m.identity || {};
  session.identity = {
    unifiedId: String(idm.unifiedId || idm.id || ''),
    displayName: String(idm.displayName || idm.name || ''),
    appVersion: String(idm.appVersion || ''),
    /* NO ACCOUNT AT ALL — set only by bridge.standaloneInit, for a visitor who arrived on an
     * invite link. /join then omits unified_id and plays on a server-minted guest seat. `=== true`
     * because a host that never heard of the field must not read as anonymous. */
    anonymous: idm.anonymous === true,
  };
  session.caps = m.caps || null;
  session.consent = m.consent || null;
  session.match = m.match || null;
  session.prefs = m.prefs || null;
  session.discord = (m.discord && typeof m.discord === 'object') ? m.discord : null;
  session.fullscreen = !!m.fullscreen;
  session.initAt = Date.now();
  session.net = bridge.configureNet(m.net || {});
  bridge.log('init: ' + (session.identity.displayName || '?')
    + ' solo=' + session.solo
    + ' mode=' + (session.match && session.match.mode || '-')
    + ' net=' + (session.net.viaHost ? 'via-host' : (session.net.serverBase || 'none')));
  armDeadline();
  settle();
});

bridge.on('manifest', (m) => {
  gotManifest = true;
  session.manifest = media.setManifest(m);
  // EPHEMERAL INBOX (owner decision, 2026-08-05): a partner's media never survives
  // the match — the page purges at teardownEverything and the host wipes at page
  // boot, so `received` is empty in production. The priming path is kept because
  // the manifest frame still carries the field (shape compatibility, and the
  // standalone/test harness feeds rows through it), not because anything should
  // arrive here. If this ever logs a nonzero count on a fresh page, a purge seam
  // regressed — that is the bug that put past partners' media into Practice.
  session.received = Array.isArray(m.received) ? m.received : [];
  primeReceived(session.received);
  bridge.log('manifest: ' + session.manifest.images + ' images, ' + session.manifest.videos + ' videos'
    + (session.manifest.skipped ? ', ' + session.manifest.skipped + ' skipped' : '')
    + (session.received.length ? ', ' + session.received.length + ' received' : ''));
  armDeadline();
  settle();
});

bridge.on('fullscreen', (m) => {
  session.fullscreen = !!(m && m.on);
  paintProbe();
});

bridge.on('ping', (m) => bridge.send({ type: 'pong', t: m && m.t }));

bridge.on('end-run', () => finishExit('end-run'));

// Reserved: the host does not post this yet (caps.haptics is false until the
// haptics-v2 overhaul merges). Wired now so the toy HUD has a seam on day one.
bridge.on('haptics-state', (m) => {
  session.haptics = m || null;
  paintProbe();
});

/* ----------------------------------------------------------------------------
 * BOOT DEADLINES — progress-aware, like DtRH: the clock restarts on every
 * milestone (script start, init, manifest). A slow-but-progressing boot (asset
 * scan, AV-scanned module downloads) must never be misread as a wedge, but a
 * boot that genuinely never lands has to FAIL LOUDLY instead of spinning: the
 * host's watchdog stays quiet while rAF keeps beating, so silence is not enough.
 * -------------------------------------------------------------------------- */
function armDeadline() {
  if (bootSettled || !bridge.isHosted) return;
  try { clearTimeout(warmTimer); clearTimeout(deadlineTimer); } catch (_e) { /* ignore */ }
  warmTimer = setTimeout(() => {
    if (bootSettled) return;
    const note = el('gg-loader-note');
    if (note) note.textContent = 'warming up' + (gotInit ? '' : ' — waiting for the app') + '…';
    bridge.log('boot: 12s with no ' + (gotInit ? 'manifest' : 'init'));
  }, WARM_MS);
  deadlineTimer = setTimeout(() => {
    if (bootSettled) return;
    bootSettled = true;
    const why = 'boot deadline: ' + (gotInit ? '' : '[no init] ') + (gotManifest ? '' : '[no manifest] ')
      + Math.round(BOOT_MS / 1000) + 's since last progress';
    bridge.log(why);
    bridge.bootError(why);
    showLoaderFailure(why);
  }, BOOT_MS);
}

function settle() {
  if (!gotInit || !gotManifest || bootSettled) return;
  bootSettled = true;
  session.ready = true;
  try { clearTimeout(warmTimer); clearTimeout(deadlineTimer); } catch (_e) { /* ignore */ }
  loadSiblings()
    .catch(() => {})
    .then(() => {
      try { buildApp(); } catch (e) {
        logger.error('app build failed: ' + ((e && e.stack) || e));
        showLoaderFailure('ui failed to start');
        return;
      }
      const loader = el('gg-loader');
      if (loader) loader.hidden = true;
      openFirstScreen();
      bridge.log('boot ok');
    });
}

/* ----------------------------------------------------------------------------
 * THE FIRST SCREEN — the title, unless somebody was LINKED here.
 *
 * `?join=ABC123` (ui/inviteLink.js) is the shareable half of hosting: a host
 * copies a URL, the other player taps it, and the page opens on the join screen
 * with the code already in and already submitted. The alternative — landing on a
 * menu and being asked to type six characters that were RIGHT THERE in the link
 * — is where invites die.
 *
 * `consumeJoinCode` reads AND strips the param in one call, before anything else
 * can look at it. The strip is not cosmetic: a link left in the address bar is
 * re-joined by every refresh, every back button and every home-screen pin made
 * from this page, forever, against a room whose TTL is five minutes. Failure is
 * the join screen's problem and it is well equipped for it — the code stays in
 * the field and the reason is on screen.
 * -------------------------------------------------------------------------- */
function openFirstScreen() {
  let code = '';
  try { code = consumeJoinCode(typeof window !== 'undefined' ? window : null); }
  catch (_e) { code = ''; }
  /* A LIVE GUEST SEAT SURVIVES THE PAGE THAT CLAIMED IT (2026-08-04 play-test).
   * iOS routinely kills this page while the player is off in the photo sheet,
   * and the reload comes back on a BARE url — consumeJoinCode stripped the
   * ?join on the first visit, so the one person with a seat waiting for them
   * landed on a menu with no way back but re-finding the link. If prefs hold a
   * fresh `g_` seat, walk them straight back into the join flow: presenting
   * the kept id is a server-side seat RECLAIM (pass:"rejoin"), and a room that
   * died in the meantime fails on the join screen with the code prefilled and
   * a plain sentence, not a dead end. An EXPLICIT link still wins — the player
   * asked for THAT room. */
  if (!code && guestSeat.id && guestSeat.code
    && guestSeat.at && (Date.now() - guestSeat.at) < GUEST_SEAT_FRESH_MS) {
    code = guestSeat.code;
    bridge.log('resuming guest seat in ' + code);
  }
  if (code) {
    bridge.log('invite link: joining ' + code);
    router.show('join', { autoCode: code });
    return;
  }
  router.show('title');
}

function showLoaderFailure(msg) {
  const note = el('gg-loader-note');
  if (note) note.textContent = 'could not start — ' + String(msg).slice(0, 120);
  const ring = hasDom() ? document.querySelector('.gg-loader-ring') : null;
  if (ring) ring.remove();
}

/* ============================================================================
 * SINGLETONS
 * ==========================================================================*/

let prefs = null;
let audio = null;
let toasts = null;
let sheets = null;
let options = null;
/* ui/coach.js — the one-time explainers. A boot singleton rather than a per-match
 * handle on purpose: "once ever" is a fact about the PLAYER, and a coach rebuilt
 * on every attachMatch (the relay fallback rebuilds the whole graph) would keep
 * its page-local ledger for a matter of seconds. */
let coach = null;
let router = null;
let executor = null;
let matchLog = null;
let assets = null;           // ui/assetsStore.js — owns every cache-* bridge verb
let receivedStore = null;    // exec/receivedStore.js — owns goon-recv-result
let blocklist = null;        // net/blocklist.js
let mediaQueue = null;       // net/mediaQueue.js — one per page, re-attached per match
let wakeLock = null;         // ui/wakeLock.js — held while a match is attached (phone screens)
let artifacts = null;        // the adapter below, over assets' item map
let discord = null;          // ui/discord.js — owns the discord + peer-card verbs
/* ui/voice/noteStore.js — PAGE-SCOPED, unlike the voice service beside it. The
 * eight pre-recorded notes belong to the player, not to a duel: they survive
 * every match, every relay rebuild and every trip back to the title menu, and
 * the library screen is reached with no match in existence at all. */
let noteStore = null;
let vsSplash = null;         // the countdown's decorative VS card, if one is up
let ctx = null;

/* ---- match/session state. NEVER cached by a screen; always read through ctx. */
let goonSession = null;      // net/session.js GoonSession (host/join path only)
let currentMatch = null;
let currentTransport = null;
let currentSd = null;        // {presenter, inputs, dispose} from ui/sd
let voice = null;            // ui/voice/voiceService.js — MATCH-SCOPED, see attachMatch
let micGateSaid = false;     // the mic breadcrumb is once per match — see reportMicGate
let hudHandle = null;
let mercyHandle = null;
let phaseUnsubs = [];
let escMercied = false;
let awaitingEntry = false;
let lastConnectFailed = null;
let recapFallbackTimer = 0;

/* ---- THE FIRST-RUN MEDIA STEP (ui/screens/mediaSetup.js).
 *
 * Set on the JOIN that landed, when the deck is empty — the case that only ever
 * happens to somebody who arrived on an invite link and has never opened this
 * before. While it is true the phase router shows the media screen instead of
 * the lobby; the ENGINE is untouched and walks Lobby -> Consent underneath it.
 *
 * `mediaPrepTold` is separate because the wire half is EDGE-triggered: onPhase
 * fires several times on the way through the lobby and the peer must hear
 * "preparing" once, not five times. */
let mediaPrepPending = false;
let mediaPrepTold = false;

/* ---- Practice mode */
let soloPair = null;
let soloOpponent = null;
let soloDriver = null;

/* ----------------------------------------------------------------------------
 * CAPS — narrowed to what exec/ can actually render. Advertising a SHORTER list
 * than you can run is always safe (core/caps.js); a longer one desyncs a match
 * the first time the peer drafts something we cannot show. ToyPatterns needs
 * real haptics, so it stays opt-in until the haptics merge.
 *
 * BrainDrain and Spiral are ON by default (owner call 2026-08-03). Both are
 * IN-PAGE veils drawn by exec/ inside this WebView, NOT the withheld native
 * OverlayService blur — see GoonHostService.BrainDrainAllowed for the full
 * reasoning. The host still sends the flags, so `!== false` keeps a host that
 * deliberately turns one off in control while defaulting a host that predates
 * the flag (or a browser opened without a host at all) to ON.
 * -------------------------------------------------------------------------- */
function localCaps() {
  const caps = session.caps || {};
  const elements = [
    GoonElement.Flashes,       // 0
    GoonElement.Videos,        // 1
    GoonElement.Subliminals,   // 2
    GoonElement.Bubbles,       // 3
    GoonElement.LockCards,     // 4
    GoonElement.BouncingText,  // 7
  ];
  const payloads = [
    GoonPayloadKind.FlashBurst,
    GoonPayloadKind.SubliminalStorm,
    GoonPayloadKind.BubbleSwarm,
    GoonPayloadKind.Video,
    GoonPayloadKind.LockCard,
  ];
  if (caps.haptics) { elements.push(GoonElement.ToyPatterns); payloads.push(GoonPayloadKind.ToyPattern); }
  if (caps.brainDrain !== false) { elements.push(GoonElement.BrainDrain); payloads.push(GoonPayloadKind.BrainDrain); }
  if (caps.spiral !== false) { elements.push(GoonElement.Spiral); payloads.push(GoonPayloadKind.Spiral); }

  /* THE VOICE-NOTE DISCRIMINATOR. Advertised unconditionally, for every build
   * that ships ui/voice/voiceService.js, because it is a statement about what
   * this BUILD can PARSE and never about what this player has agreed to or paid
   * for: the consent lives on the consent frame (`voice_notes`) and the local
   * opt-in lives in prefs. Advertising it does not turn anything on.
   *
   * It has to be here rather than at the send site because `t:'voice'` is
   * fire-and-forget — an old peer drops the frames without a word, and this
   * integer is the ONLY way the sender ever finds out.
   *
   * `transfer` (its sibling in core/caps.js) is advertised on EXACTLY the same
   * terms: this build ships net/mediaChannel.js and will parse offers, full
   * stop. It says nothing about consent (the sheet's `media_transfer` term),
   * nothing about entitlement (session.caps.mediaTransfer gates SENDING only,
   * and as of 2026-08-05 that is free for every seat anyway — receiving never
   * was), and nothing about the link (the lobby row checks supportsBulk
   * separately). Until 2026-08-04 NOBODY set this flag —
   * caps.js documented that boot advertises it and boot never did — so every
   * hello said `transfer:false`, both lobbies greyed the checkbox out with
   * "their build doesn't transfer", and the entire media lane was unreachable
   * end to end. The owner found it on the first duel that got past ICE. */
  const voiceCap = VOICE_CAP_VERSION;

  let rounds = Array.isArray(caps.rounds) ? caps.rounds.slice() : Object.values(GoonRoundKind);
  // Without a camera there is nothing to win a staring contest with.
  if (!caps.camera) rounds = rounds.filter((r) => r !== GoonRoundKind.StaringContest);
  if (!rounds.includes(UNIVERSAL_ROUND)) rounds.push(UNIVERSAL_ROUND);

  return localCapsOf({ elements, payloads, rounds, platform: 'web', voice: voiceCap, transfer: true });
}

/* ============================================================================
 * P2P MEDIA TRANSFER — the three singletons and the one adapter.
 *
 * The queue SENDS (session.caps.mediaTransfer — free for every seat since
 * 2026-08-05; the paid perk is HOSTING), the store RECEIVES (never gated at all,
 * in any era), and the blocklist is the render-time safety gate. All three are built in
 * buildApp() and the queue is attached/detached with the match, so the relay
 * rebuild re-binds it over the new transport and it simply goes dormant.
 * ==========================================================================*/

/** ext -> mime. The cache serves artifacts by extension; the protocol wants a mime. */
const ARTIFACT_MIME = {
  png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif',
  webp: 'image/webp', mp4: 'video/mp4', webm: 'video/webm', mov: 'video/quicktime',
};

/**
 * THE ARTIFACT SOURCE — the only two verbs net/mediaQueue.js needs, adapted over
 * ui/assetsStore.js's item map (spec §0). Deliberately thin and deliberately here:
 * the queue must not know what a compression lane is, and assetsStore must not
 * know a duel exists.
 *
 * `sha` comes off the item when the store carries it, and otherwise off the
 * artifact URL, which is `https://ccp.cache/art/<sha>.<ext>` by construction
 * (GoonCacheBridge.ArtUrlFor). EXEMPT originals have no artifact URL — they ARE
 * the original — so they are only sendable when the item itself carries the sha.
 *
 * The bytes are fetched ONCE per artifact and sliced from the ArrayBuffer: one
 * transfer is in flight at a time and the cap is 24 MiB, so a two-entry cache is
 * all the memory this can ever hold. The cache vhost is mapped `Allow`
 * (GoonHostService.AddIfPresent), which is what makes a cross-origin fetch legal.
 */
function createArtifactSource() {
  const urls = new Map();     // sha -> where the bytes live
  const mimes = new Map();    // sha -> mime
  const cache = new Map();    // sha -> {buf, bytes, mime}
  let fetchWarned = false;

  const shaOf = (it) => {
    if (it && typeof it.sha === 'string' && /^[0-9a-f]{64}$/.test(it.sha)) return it.sha;
    const m = /\/art\/([0-9a-f]{64})\./i.exec(String((it && it.artUrl) || ''));
    return m ? m[1].toLowerCase() : '';
  };
  const mimeOf = (url) => {
    const m = /\.([a-z0-9]{2,5})(?:\?|#|$)/i.exec(String(url || ''));
    return m ? (ARTIFACT_MIME[m[1].toLowerCase()] || '') : '';
  };

  return {
    listSendable() {
      const out = [];
      let items = [];
      try { items = (assets && assets.items) || []; } catch (_e) { items = []; }
      for (const it of items) {
        if (!it) continue;
        const exempt = it.state === 'exempt';
        if (it.state !== 'ready' && !exempt) continue;
        const sha = shaOf(it);
        if (!sha) continue;                       // no identity, nothing to offer
        const url = exempt ? it.srcUrl : (it.artUrl || '');
        if (!url) continue;
        // Browser-picked files sit behind blob: URLs (no extension to sniff) and
        // carry their mime on the item; host items keep the URL-derived answer.
        const mime = String(it.mime || '') || mimeOf(url);
        if (!mime) continue;
        const bytes = exempt ? (it.srcBytes || it.bytes || 0) : (it.bytes || 0);
        if (!bytes) continue;
        urls.set(sha, url);
        mimes.set(sha, mime);
        /* THE WIRE KIND IS THE ARTIFACT'S, NOT THE SOURCE'S (2026-08-05). A gif
         * compresses into an mp4 artifact: the ITEM stays kind 'image' (that is
         * what it is in the deck), but what actually TRAVELS is video/mp4 — and
         * the channel's offer gate refuses any offer whose kind and mime
         * families disagree. In a gif-heavy library that refused nearly every
         * artifact ("refusing to offer …: kind/mime disagree", over and over),
         * the larder never filled, and every throw fired untagged into the
         * receiver's own pool. */
        const wireKind = mime.indexOf('video/') === 0 ? 'video' : 'image';
        /* …AND THE ORIGIN IS THE SOURCE'S, WHICH IS THE OTHER HALF OF THE SAME STORY. The wire
         * kind above deliberately forgets that this mp4 used to be a gif; the VIDEO lane wants
         * that fact back, so it travels beside it as `origin` and demotes (never excludes) the
         * artifact when a real clip is also available. `it.origin` is the host's own answer
         * (GoonCacheBridge, from TransferKinds.Gif); `it.kind === 'gif'` is the same answer read
         * off the item a host too old to send `origin` still provides; a browser-picked file
         * carries the flag from ui/assetsStore.js's compression lane.
         *
         * `codec` is what the producer knows it made ("avc1" from either compressor). An exempt
         * ORIGINAL knows nothing, which is the fail-open case: net/mediaQueue.js offers it. */
        const origin = (it.origin === 'gif' || it.kind === 'gif') ? 'gif' : '';
        const codec = String(it.codec || '');
        out.push({ sha, bytes, mime, kind: wireKind, exempt, origin, codec });
      }
      return out;
    },

    async open(sha) {
      let rec = cache.get(sha);
      if (!rec) {
        const url = urls.get(sha);
        if (!url) return null;
        let buf = null;
        try {
          const r = await fetch(url, { credentials: 'omit' });
          if (!r || !r.ok) return null;
          buf = await r.arrayBuffer();
        } catch (e) {
          if (!fetchWarned) {
            fetchWarned = true;
            logger.warn('could not read a cache artifact for sending (' + ((e && e.message) || e)
              + ') — nothing will be offered this session');
          }
          return null;
        }
        rec = { buf, bytes: buf.byteLength, mime: mimes.get(sha) || '' };
        cache.set(sha, rec);
        // One in flight at a time; keep the previous one only in case a resume asks
        // for it again inside the reconnect grace.
        while (cache.size > 2) {
          const oldest = cache.keys().next().value;
          if (oldest === sha) break;
          cache.delete(oldest);
        }
      }
      return {
        bytes: rec.bytes,
        mime: rec.mime,
        read: (offset, len) => rec.buf.slice(offset, Math.min(offset + len, rec.bytes)),
      };
    },
  };
}

/* ----------------------------------------------------------------------------
 * THE LOCAL LIBRARY IS A PLAYABLE LIBRARY.
 *
 * Standalone there is no host and bridge.js synthesizes an EMPTY manifest, so
 * exec/media.js's deck starts (and used to stay) empty: the files a player picks
 * on the assets screen only ever reached the SEND path (listSendable above).
 * A practice session on a phone therefore fired every flash, bubble and video
 * against nothing — the effects ran, the screen stayed blank. This is the other
 * half: the picks go into the deck too, so the player's OWN effects — in
 * practice AND in a duel — draw from the library they just loaded.
 *
 * Hosted this is inert (the picker only exists when there is no host cache, and
 * an empty list sets an empty local half) — the host's manifest still owns the
 * deck, exactly as before.
 * -------------------------------------------------------------------------- */
let syncedLocalVersion = -1;
let loggedLocalDeck = -1;

/**
 * @param {boolean} [force] re-feed the pool even when `localVersion` has not
 *   moved. The version guard is an optimisation against hosted `cache-list`
 *   churn, and it is the ONE thing that can turn a missed `onItems` emit into a
 *   permanently empty deck — so every road INTO a match (attachMatch, startSolo)
 *   forces one, which costs one array map and closes the hole for good.
 */
function syncLocalDeck(force) {
  if (!assets) return;
  const v = Number(assets.localVersion) || 0;
  if (v === syncedLocalVersion && !force) return;   // a hosted cache-list, not a pick
  syncedLocalVersion = v;
  try {
    const list = localPlayableEntries(assets.localItems);
    const c = media.setLocalLibrary(list);
    if (list.length !== loggedLocalDeck) {
      loggedLocalDeck = list.length;
      logger.info('local library: ' + list.length + ' playable item(s) — pool now '
        + c.images + ' images, ' + c.videos + ' videos');
    }
    // THE ONE CASE WORTH A WARN, and it has to be a warn: `logger.info` does not
    // reach the ?debug=1 overlay (see teeDebug — only warn/error do), so on a
    // phone an info line is a line nobody can read. Picks that produce no
    // playable entry means the store adopted rows with no URL or no kind, which
    // looks exactly like "my uploads do nothing" and is otherwise invisible.
    if (!list.length && (assets.localCount | 0) > 0) {
      logger.warn('local library: ' + assets.localCount + ' pick(s) but NOTHING playable — '
        + 'no url/kind survived adoption, so every effect will draw from an empty deck');
    }
  } catch (e) {
    logger.warn('could not fold the local library into the media pool: ' + ((e && e.message) || e));
  }
}

/**
 * Fold the received inbox into the store, the blocklist and the media pool.
 * A no-op until buildApp() has built them, which is why the `manifest` handler
 * calls it AND buildApp does: whichever comes second is the one that lands.
 */
function primeReceived(list) {
  if (!receivedStore || !Array.isArray(list)) return;
  try {
    const shas = receivedStore.primeReceived(list);
    blocklist?.prime?.(shas);
    registerReceived(receivedStore.list());
  } catch (e) {
    logger.warn('priming the received inbox failed: ' + ((e && e.message) || e));
  }
}

/**
 * THE EPHEMERAL-INBOX SEAM (owner decision, 2026-08-05): nothing a partner sent
 * survives the match. Dropping goes through the SAME two ledgers priming filled —
 * exec/media.js (so a Practice peer-run's drawReceived can never see a dead row)
 * and the received store (whose drop() also deletes the host's disk file) — in
 * that order, so no window exists where media can hand out a view the store has
 * already destroyed. Called from teardownEverything, which every road out of a
 * match funnels through and a relay REBUILD never touches; the recap always
 * mounts BEFORE its match is torn down, so its report card keeps its plates.
 */
function purgeReceived(why) {
  if (!receivedStore) return;
  try {
    const rows = receivedStore.list();
    if (!rows.length) return;
    for (const e of rows) {
      try { media.dropReceived(e.sha); } catch (_e) { /* one bad row never stops the sweep */ }
      try { receivedStore.drop(e.sha); } catch (_e) { /* ignore */ }
    }
    session.received = [];
    logger.info('received inbox purged (' + why + '): ' + rows.length + ' artifact(s)');
  } catch (e) {
    logger.warn('purging the received inbox failed: ' + ((e && e.message) || e));
  }
}

/** Hand artifacts to exec/media.js with a live view factory (real refcounts standalone). */
function registerReceived(list) {
  for (const e of (list || [])) {
    try {
      media.addReceived({
        sha: e.sha, kind: e.kind, mime: e.mime, url: e.url, bytes: e.bytes,
        // '' for everything that predates the field — which reads as footage, i.e. as today.
        origin: e.origin || '',
        acquire: () => receivedStore.view(e.sha),
      });
    } catch (_err) { /* one bad row is never the whole inbox */ }
  }
}

/* ----------------------------------------------------------------------------
 * MATCH LOG — a plain collector, owned here and read by the recap + the HUD.
 * It is the ONLY memory of what crossed the wire: the engine keeps score, not
 * history. Attaches to whatever match is current and re-attaches on a rebuild.
 * -------------------------------------------------------------------------- */
function createMatchLog() {
  let m = null;
  let unsubs = [];
  let entries = [];      // payload traffic, both directions
  let notes = [];        // UI events written by the HUD / mercy / sudden-death
  let phaseMarks = [];
  let emotes = [];
  let closeness = [];
  let origFire = null;
  let origFinish = null;
  const byId = new Map();

  const at = () => (m ? m.liveElapsedMs : 0);

  function push(e) { entries.push(e); byId.set(e.id, e); return e; }

  function statusFromReceipt(status) {
    if (status === GoonReceiptStatus.Survived) return 'endured';
    if (status === GoonReceiptStatus.RejectedRate) return 'too_soon';
    if (status === GoonReceiptStatus.RejectedFiltered) return 'blocked';
    return 'landed';
  }

  return {
    attach(match) {
      this.detach();
      if (!match) return;
      m = match;
      entries = []; notes = []; phaseMarks = []; emotes = []; closeness = []; byId.clear();

      unsubs.push(match.onPhaseChanged((p) => phaseMarks.push({ phase: p, at: Date.now(), liveMs: at() })));
      unsubs.push(match.onPayloadAccepted((ev) => {
        const p = ev && ev.payload;
        if (p) push({ dir: 'in', id: p.id, kind: p.kind, atMs: at(), status: 'landed' });
      }));
      unsubs.push(match.onPayloadRejected((receipt) => {
        if (receipt) push({ dir: 'in', id: receipt.id, kind: -1, atMs: at(), status: statusFromReceipt(receipt.status) });
      }));
      unsubs.push(match.onPayloadReceiptReceived((receipt) => {
        // Receipts are for OUR outbound payloads; the kind comes from the
        // interception below, because a receipt carries only an id + status.
        const e = byId.get(receipt && receipt.id);
        if (e) e.status = statusFromReceipt(receipt.status);
      }));
      unsubs.push(match.onEmoteReceived((e) => emotes.push({ at: at(), text: e.text, icon: e.icon })));
      unsubs.push(match.onOpponentStateChanged(() => {
        const c = m.opponent.closeness;
        const last = closeness.length ? closeness[closeness.length - 1].value : null;
        if (c !== last) closeness.push({ at: at(), value: c });
      }));

      // The two verbs the engine does not raise events for. Wrapping the
      // instance (not the prototype) keeps this local to the match we own and
      // dies with it — nothing else in the page can observe the patch.
      origFire = match.tryFirePayload.bind(match);
      match.tryFirePayload = (req) => {
        const res = origFire(req);
        if (res && res.ok) push({ dir: 'out', id: res.id, kind: req.kind, atMs: at(), status: 'landed' });
        return res;
      };
      origFinish = match.notifyInboundPayloadFinished.bind(match);
      match.notifyInboundPayloadFinished = (id, endured) => {
        const e = byId.get(id);
        if (e) e.status = endured ? 'endured' : 'landed';
        return origFinish(id, endured);
      };
    },

    detach() {
      for (const off of unsubs) { try { off(); } catch (_e) { /* ignore */ } }
      unsubs = [];
      m = null;
      origFire = null;
      origFinish = null;
    },

    /**
     * The sink the HUD / mercy / sudden-death UI write into (ui/hud.js logTo()
     * probes for push/add/log, in that order). Their entries are UI events, not
     * wire traffic, so they are kept apart from the payload ledger.
     */
    add(entry) {
      if (!entry) return;
      notes.push(Object.assign({ atMs: at() }, entry));
      if (notes.length > 400) notes.shift();
    },
    notes() { return notes.slice(); },

    payloads() { return entries.slice(); },
    emotes() { return emotes.slice(); },
    closenessTrack() { return closeness.slice(); },
    sawPhase(p) { return phaseMarks.some((x) => x.phase === p); },

    stats() {
      let landedOnYou = 0, enduredByYou = 0, blockedByYou = 0, sentByYou = 0, enduredByThem = 0;
      for (const e of entries) {
        if (e.dir === 'in') {
          if (e.status === 'landed' || e.status === 'endured') landedOnYou++;
          if (e.status === 'endured') enduredByYou++;
          if (e.status === 'blocked' || e.status === 'too_soon') blockedByYou++;
        } else {
          sentByYou++;
          if (e.status === 'endured') enduredByThem++;
        }
      }
      return { landedOnYou, enduredByYou, blockedByYou, sentByYou, enduredByThem };
    },
  };
}

/* ----------------------------------------------------------------------------
 * THE createMatch FACTORY — the one closure that knows how a match is built.
 * net/session.js injects it and never imports core/match.js itself, precisely
 * so the relay fallback can rebuild a match without this knowledge leaking into
 * the transport layer.
 * -------------------------------------------------------------------------- */
function buildMatch(transport, isHost, { withSuddenDeathUi = true, displayName = null } = {}) {
  const match = new GoonMatchService(transport, isHost, {
    rngFactory: (seed) => new GoonRng(seed),
    logger,
    displayName: displayName || (session.identity && session.identity.displayName) || 'Player',
    appVersion: (session.identity && session.identity.appVersion) || '',
    caps: localCaps(),
    tag: isHost ? 'GG:host' : 'GG:guest',
  });

  if (withSuddenDeathUi) {
    let sd = null;
    try {
      sd = createSuddenDeathUi ? createSuddenDeathUi({
        audio,
        getClock: () => { try { return transport ? transport.clock : null; } catch (_e) { return null; } },
        onLog: (entry) => matchLog?.add?.(entry),
      }) : null;
    }
    catch (e) { logger.warn('createSuddenDeathUi threw: ' + ((e && e.message) || e)); }
    currentSd = sd;

    /* SUDDEN DEATH IS DETACHED — owner's call, 2026-08-03, pending the rounds
     * rework. The run is just the Live phase now: Countdown -> Live -> Recap.
     *
     * This is a ONE-LINE detach on purpose. core/match.js _enterSuddenDeath()
     * already degrades to the score comparison when no runner is attached
     * (equal -> Draw, else the higher score wins; a mercy mid-run is still a
     * loss), so nothing else has to change and core/rounds/* + ui/sd/* stay in
     * the tree, wired and tested, ready to come back. Re-enable by restoring
     * the assignment below — nothing else was removed.
     *
     * match.suddenDeathRunner = new GoonSuddenDeathRunner({
     *   presenter: sd ? sd.presenter : null,   // null -> the headless presenter
     *   inputs: sd ? sd.inputs : null,         // null -> feeds that never fire
     *   logger,
     * });
     */
    void GoonSuddenDeathRunner;   // keeps the import honest while SD is detached
  }
  return match;
}

/**
 * THE SERVER'S SEND VERDICT -> session.caps.mediaTransfer. Standalone only.
 *
 * Sending is free for every seat now (bridge.js defaults the cap ON, and the C#
 * host's TransferAllowed() answers true), so this is no longer how a seat EARNS
 * the capability — it is how the server can still TAKE IT BACK. /invite and
 * /join answer `media_send`; net/signaling.js records it; this folds it in. A
 * `false` from a future policy turns sending off with no client release, and a
 * `null` (a server that predates the field) deliberately changes nothing.
 *
 * WHY THIS IS A FUNCTION AND NOT A LINE IN attachMatch — it has to run TWICE,
 * on two different roads, and running it only in attachMatch is exactly the bug
 * that made a verdict-driven gate unusable:
 *
 *   FIRST CONNECT.  GoonSession._beginSession() constructs a BRAND NEW signaling
 *     client (mediaSend === null), builds the match, and raises
 *     currentMatchChanged — i.e. calls attachMatch — and only THEN awaits
 *     createInvite/join, which is the round trip that learns the verdict. Read
 *     at attach time the answer is therefore ALWAYS null. Under the old
 *     default-OFF cap that left every standalone seat unable to send for the
 *     whole match, however loudly the server said yes. hostStart/joinStart call
 *     this again after their await, which is the only moment the answer exists.
 *
 *   RELAY REBUILD.  _fallBackToRelay disposes the match and raises
 *     currentMatchChanged over a new transport, KEEPING the signaling client. The
 *     call in attachMatch catches that road, where the verdict is long since in.
 *
 * Idempotent by construction (it assigns a value, it does not toggle one), so
 * calling it on both roads and twice on one of them costs nothing. Hosted pages
 * are skipped outright: the C# init frame is authoritative there and there is no
 * signaling client on this side to ask.
 */
function adoptServerSendVerdict() {
  if (session.hosted) return;
  const verdict = goonSession && goonSession.signaling ? goonSession.signaling.mediaSend : null;
  if (typeof verdict !== 'boolean') return;
  if (session.caps && session.caps.mediaTransfer === verdict) return;   // no churn, no log spam
  session.caps = Object.assign({}, session.caps, { mediaTransfer: verdict });
  logger.info('server send verdict adopted: caps.mediaTransfer = ' + verdict);
}

/* ----------------------------------------------------------------------------
 * ATTACH / DETACH — the rebuild-safe graph. Everything that observes a match is
 * bound HERE and nowhere else, so "the match was replaced" is one function call
 * rather than a hunt through seven subscribers.
 * -------------------------------------------------------------------------- */
function attachMatch(match, transport) {
  detachMatch();
  if (!match) return;

  currentMatch = match;
  currentTransport = transport || (goonSession ? goonSession.transport : null);
  escMercied = false;
  // A new match is a new answer to "is there a mic on the desk". See reportMicGate.
  micGateSaid = false;

  // EVERY match starts with the player's picks in the deck, proven rather than
  // assumed. `onItems` is the reactive path and it is the one that normally does
  // this; forcing it here is the belt to its braces, because "the effects ran and
  // the screen stayed blank" is the failure mode with no symptom of its own.
  // Hosted this is a no-op: there are no local picks, and setLocalLibrary([])
  // leaves the host's manifest half of the deck exactly where it was.
  syncLocalDeck(true);

  // The server's send verdict, for the REBUILD road into a match (the relay
  // fallback raises currentMatchChanged again with the room already redeemed, so
  // by here the verdict is known). The FIRST road is handled at the call sites —
  // see adoptServerSendVerdict for why one call here can never be enough.
  adoptServerSendVerdict();

  try { executor?.attach?.(match); } catch (e) { logger.error('executor.attach threw: ' + ((e && e.stack) || e)); }
  try { matchLog.attach(match); } catch (e) { logger.error('matchLog.attach threw: ' + ((e && e.stack) || e)); }
  /* THE SECOND tryFirePayload INSTANCE WRAPPER, and the order is the point.
   * matchLog wrapped it a line ago; the queue wraps it now, so the queue's is the
   * OUTERMOST — a payload gets its `xfer:` tags before the log records it, and the
   * log therefore records exactly what went on the wire. Both are instance patches
   * and both die with this match. (Trap register #7 — documented where applied.) */
  try { mediaQueue?.attach?.(match, currentTransport); }
  catch (e) { logger.warn('mediaQueue.attach threw: ' + ((e && e.message) || e)); }

  /* --- VOICE NOTES, per match. Built HERE rather than in buildApp() because it
   * subscribes to the match's own frame pump and consent events, and a relay
   * REBUILD hands us a brand new GoonMatchService — a singleton would go on
   * listening to a corpse and the mic would answer "available" against a match
   * nobody is in. Same lifetime as the HUD it feeds, torn down in detachMatch. */
  try {
    voice = createVoiceService({
      match, audio, prefs,
      // The library the emote hook fires from. Page-scoped and built in buildApp,
      // so every match shares the same eight notes; a null one (a host where
      // buildApp failed early) simply means sendNote() answers 'unavailable' and
      // live notes still work.
      noteStore,
      // The same content gate the media lane renders through. See the consult in
      // voiceService.onEnd: local map only, fails open, never waits on the net.
      blocklist,
      logger,
    });
  } catch (e) { logger.error('createVoiceService threw: ' + ((e && e.stack) || e)); voice = null; }
  /* SEED THE DECLARATION FROM THE PREFERENCE, on every attach.
   *
   * `prefs.voiceNotesEnabled` is the player's standing answer; `voice_notes` on
   * the consent frame is what the OPPONENT gets told. They are two different
   * things and this is the one line that keeps them in step — without it a
   * player who opted in on the title screen would reach the lobby with the
   * declaration off and the mic would never appear for either of them.
   *
   * Done HERE rather than in the lobby screen because a relay REBUILD hands us a
   * brand new GoonMatchService with a fresh (false) declaration and no screen
   * remount to notice. core/match.js refuses the call outside Lobby/Consent, so
   * a mid-match re-attach is a no-op rather than a signature-clearing surprise. */
  try {
    if (prefs.get('voiceNotesEnabled') && !match.setLocalVoiceNotes(true)) {
      // The engine refused the seed. That was the SILENT killer of the whole
      // transfer lane until 2026-08-05 (attachMatch runs while the match is
      // still Idle; the setter used to be Lobby/Consent-only), so a refusal is
      // never allowed to be quiet again.
      logger.warn('voice-note seed REFUSED by the engine (phase ' + match.phase + ') — declaration will not ride the consent frames');
    }
  } catch (e) { logger.warn('setLocalVoiceNotes threw: ' + ((e && e.message) || e)); }
  /* SENDING DEFAULTS ON (owner call, 2026-08-05). Sending is free for every
   * seat now and "my attacks carry MY media" is the product, so the standing
   * answer is yes unless this player has explicitly unticked the lobby box
   * before (prefs 'mediaTransferEnabled' === false — the checkbox writes it).
   * Same seam as the voice seeding above, for the same rebuild reason; the
   * engine accepts the seed in Idle too (core/match.js _seedDeclaration —
   * attachMatch runs BEFORE createInvite/join flips the phase, and the old
   * Lobby/Consent-only gate ate this exact call on every fresh P2P match),
   * still refuses it mid-match, and the peer still has to be opted in too
   * before a single byte moves. */
  try {
    if (prefs.get('mediaTransferEnabled') !== false && !match.setMediaTransfer(true)) {
      logger.warn('media-transfer seed REFUSED by the engine (phase ' + match.phase + ') — attacks will fall back to the receiver\'s local pool');
    }
  } catch (e) { logger.warn('setMediaTransfer threw: ' + ((e && e.message) || e)); }
  /* KEEP THE SCREEN ON for the duration. A phone that dims and locks mid-Live is
   * an unintended mercy; the lock is match-scoped (start here, stop in
   * detachMatch) so an idle title screen never holds one. Unsupported = no-op. */
  if (!wakeLock) wakeLock = createWakeLock({ logger });
  wakeLock.start();
  stashRoom();
  /* A NEW MATCH, A NEW PEER-RENDER LOG — and this is the ONLY place it is
   * cleared. The obvious-looking reset points are both wrong: stopWindows()
   * runs inside clearForRecap() immediately BEFORE the recap mounts, and
   * detachMatch() runs when the player leaves it — either would wipe the list
   * the report card is about to read. Clearing on attach means the log covers
   * exactly one match and stays readable for the whole recap after it. */
  resetPeerRenderLog();

  /* --- DISCORD, per match --------------------------------------------------
   * A new match is a new (or re-connected) opponent, so the card on the desk is
   * dropped and the version ledger with it: a relay REBUILD re-uses the room and
   * would otherwise skip the re-fetch as "same version" against a peer object
   * that is about to be repainted from scratch.
   *
   * `watchSignaling` is the only path by which a peer_card_ver ever reaches the
   * page. It is taken HERE rather than at ensureSession() because the signaling
   * client is created per host()/join(), i.e. after the session object exists —
   * and it is put on phaseUnsubs so the rebuild re-arms it with everything else. */
  try { discord?.clearPeer?.(); } catch (_e) { /* never load-bearing */ }
  if (goonSession && goonSession.signaling && discord) {
    try { phaseUnsubs.push(discord.watchSignaling(goonSession.signaling)); }
    catch (e) { logger.warn('discord.watchSignaling threw: ' + ((e && e.message) || e)); }
  }

  /* THE MERCY BEAT. Two callers reach declareMercy — the Escape ladder in this
   * file and the button in ui/mercy.js — and neither is privileged, so the emit
   * goes on the INSTANCE (the matchLog tryFirePayload precedent): one seam, both
   * paths, and it dies with this match. The engine's return value is passed
   * straight through; nothing about the concede may depend on a decoration. */
  if (typeof match.declareMercy === 'function') {
    const origMercy = match.declareMercy.bind(match);
    match.declareMercy = (...args) => {
      emitAva('mercy', 'you');
      return origMercy(...args);
    };
  }

  phaseUnsubs.push(match.onPhaseChanged(onPhase));
  phaseUnsubs.push(match.onConnectionHealthChanged((h) => {
    if (h === 1) toasts?.warn?.(S.toasts.peerWobbly);
    else if (h === 0) toasts?.good?.(S.toasts.peerBack);
  }));
  // THE RECAP IS NOT OPTIONAL (see RECAP_FALLBACK_MS). The timer is armed on
  // matchEnded and deliberately NOT cancelled by resultFinalized: the handshake
  // landing says nothing about whether the player can SEE the recap, which is
  // the thing that was broken.
  // THE COMPRESSION QUEUE YIELDS TO THE DUEL. Two workers transcoding video is
  // exactly the CPU a live match cannot spare, so the host parks the queue from
  // Countdown and picks it back up at the Recap. `reason:'match'` is a separate
  // flag from the user's own pause — a match ending can never resume a queue the
  // player stopped by hand. Subscribed HERE, inside attachMatch, so the relay
  // rebuild re-binds it with everything else.
  const syncQueue = (p) => {
    try {
      if (p === GoonMatchPhase.Recap || p === GoonMatchPhase.Idle) assets?.resume?.('match');
      else if (p >= GoonMatchPhase.Countdown) assets?.pause?.('match');
    } catch (_e) { /* the queue is never load-bearing for the match */ }
  };
  phaseUnsubs.push(match.onPhaseChanged(syncQueue));
  syncQueue(match.phase);      // a relay REBUILD lands mid-Live and never fires a change
  phaseUnsubs.push(match.onMatchEnded(() => armRecapFallback()));
  // THEIR mercy, from the only place it is knowable: the result. `localWon` on a
  // Mercy end means the other side tapped. Guarded to a fault — a decoration must
  // never be the thing that throws inside the end-of-match cascade.
  phaseUnsubs.push(match.onMatchEnded(() => {
    try {
      const r = match.result;
      if (r && r.endReason === GoonEndReason.Mercy && r.localWon) emitAva('mercy', 'opp');
    } catch (_e) { /* ignore */ }
  }));
  phaseUnsubs.push(match.onResultFinalized(() => forceRecap('finalized', false)));
  onPhase(match.phase);
  paintProbe();
}

/**
 * Remember the room while we still can. `actions.leave()` disposes the transport,
 * and the recap's report card is filed AFTER that — so {code, token, role} are
 * copied off the transport the moment a match binds to it. A rebuild (relay
 * fallback) re-runs this over the new transport with the SAME room, which is the
 * whole point of GoonSession re-using the signaling client.
 */
function stashRoom() {
  try {
    const t = currentTransport;
    const code = t && t.code ? String(t.code) : '';
    if (!code) return;
    session.room = {
      code,
      token: t.token ? String(t.token) : '',
      role: t.isHost ? 'host' : 'guest',
    };
  } catch (_e) { /* a transport without a room is Practice — nothing to report */ }
}

/* ----------------------------------------------------------------------------
 * THE RECAP SAFETY NET. Conceding is the one thing a player is promised will
 * always work, and "always works" has to include what happens after it.
 * -------------------------------------------------------------------------- */
function armRecapFallback() {
  clearRecapFallback();
  try { recapFallbackTimer = setTimeout(() => forceRecap('fallback', true), RECAP_FALLBACK_MS); }
  catch (_e) { forceRecap('fallback', true); }
}

function clearRecapFallback() {
  try { clearTimeout(recapFallbackTimer); } catch (_e) { /* ignore */ }
  recapFallbackTimer = 0;
}

/**
 * CLEARING THE WAY FOR THE RECAP — the teardown the end of a match owes the
 * player. Called the moment the phase turns Recap, and again on every forceRecap
 * pass.
 *
 * THE BUG THIS EXISTS FOR: core/match.js _endMatch() stops every sustained
 * ELEMENT (_stopAllElements), but it knows nothing about an in-flight PAYLOAD
 * render — a lock card or a video the opponent sent, which exec/ mounted on
 * #gg-stage and which happily runs out its own duration_ms (up to 45 s) after
 * the match is over. #gg-stage is z20, full-bleed and ABOVE the screen stack
 * (goon.css:71-72); ui/screens.css only makes it click-through while it is
 * :empty. One husk left on it therefore turns the whole recap into a picture:
 * the buttons are unreachable, and so is every click anywhere on the page.
 * That is exactly what the owner hit — "clicking any button does nothing".
 *
 * executor.stopAll() cancels the renders (each still gets its closing receipt)
 * AND empties the layers; layers.stopAll() repeats it in case the executor
 * never built. The sweep at the end is the assertion, not the fix.
 *
 * The other two things that can outlive a match are chrome, not effects: a
 * modal sheet (#gg-modal, z70) and the options drawer (z70). Nothing else
 * closes them, so a match that ends underneath either one lands the player on a
 * recap they cannot reach.
 */
/**
 * THE z70 CHROME — a modal sheet (#gg-modal) and the options drawer (#gg-drawer).
 * Both are full-height overlays with a scrim, and NOTHING else closes them, so
 * anything they are still covering when the match moves on is unreachable.
 *
 * Two places need that, for two different bugs:
 *   RECAP    a sheet over the end card = a recap the player cannot click out of;
 *   COUNTDOWN/LIVE  a sheet left open when the run starts (the "how it works"
 *            explainer is one tap from the lobby) sits over the bottom strip
 *            where exec/bubbles.js spawns the field. Its scrim eats every pop,
 *            which silently freezes the whole drop economy for the match — and
 *            it makes Escape ambiguous exactly when Escape must mean MERCY and
 *            nothing else. So the sheet is closed BEFORE the clock starts; the
 *            Escape ladder itself is untouched.
 */
function closeChrome() {
  try { if (sheets && sheets.isOpen) sheets.close(null); } catch (_e) { /* ignore */ }
  try { if (options && options.isOpen) options.close(); } catch (_e) { /* ignore */ }
}

/* ----------------------------------------------------------------------------
 * THE VS SPLASH — decoration with a hard deadline.
 *
 * It goes up alongside the countdown and is guaranteed gone by the Live arm.
 * That guarantee is the whole design: it is z55, pointer-events:none and under
 * MERCY, so it cannot eat a click or cover the concede even while it is up, and
 * dropIt() is idempotent and called from FOUR places (Live, Recap, detach,
 * exit) rather than trusted to one owner. It never delays anything — the
 * countdown numeral is driven by the shared clock and has never heard of this.
 * -------------------------------------------------------------------------- */
function raiseVsSplash() {
  dropVsSplash();
  if (!hasDom() || !currentMatch) return;
  try {
    const opp = discord ? discord.peer : null;
    const st = discord ? discord.state : null;
    vsSplash = mountVsSplash({
      you: {
        name: currentMatch.localDisplayName || (session.identity && session.identity.displayName) || S.discord.you,
        dataUri: (discord && discord.sharingAvatar && st) ? st.avatarDataUri : null,
      },
      opp: {
        name: (currentMatch.opponent && currentMatch.opponent.displayName) || (opp && opp.name) || S.lobby.them,
        dataUri: opp ? opp.avatarDataUri : null,
      },
      // The player's own switch first, then the OS media query — the same order
      // every other decoration on the page reads them in.
      reduced: !!(prefs && prefs.get('reduceMotion')),
      showOpponent: !discord || discord.showOpponentAvatars,
      vsLabel: S.discord.vs,
    });
  } catch (e) {
    vsSplash = null;
    logger.warn('vs splash failed to mount: ' + ((e && e.message) || e));
  }
}

function dropVsSplash() {
  if (!vsSplash) return;
  try { vsSplash.remove(); } catch (_e) { /* already gone */ }
  vsSplash = null;
}

function clearForRecap(why) {
  try { executor?.stopAll?.(); } catch (e) { logger.warn('executor.stopAll threw: ' + ((e && e.message) || e)); }
  try { layers.stopAll(); } catch (e) { logger.warn('layers.stopAll threw: ' + ((e && e.message) || e)); }
  closeChrome();
  // A splash cannot survive into a recap. It should already be gone (the Live
  // arm drops it) — this is the assertion, on the same pass that sweeps the
  // stage, so a countdown that skipped straight to Recap leaves nothing behind.
  dropVsSplash();

  if (!hasDom()) return;
  const stage = el('gg-stage');
  if (stage && stage.childElementCount) {
    logger.warn('recap (' + (why || '?') + '): #gg-stage still held ' + stage.childElementCount +
      ' node(s) after teardown — clearing, it would have eaten every click');
    try { stage.replaceChildren(); } catch (_e) { /* ignore */ }
  }
}

/**
 * Idempotent: puts the recap in front of the player, and (on the fallback pass)
 * clears anything left covering it. The mercy takeover dismisses itself
 * (ui/mercy.js), but it is a full-bleed z65 scrim over a z10 screen, so this
 * sweeps <body> for a stray one rather than trusting one owner to still be alive.
 * `sweep` is false on the resultFinalized pass — that can land in tens of ms over
 * a loopback and yanking "you tapped out." away that fast would just be a flicker.
 */
function forceRecap(why, sweep) {
  const m = currentMatch;
  if (!m || m.phase !== GoonMatchPhase.Recap) return;
  clearForRecap(why);
  if (sweep) {
    try { mercyHandle?.dismissTakeover?.(); } catch (_e) { /* ignore */ }
    if (hasDom()) {
      try {
        for (const node of Array.from(document.querySelectorAll('.gg-mercy-takeover') || [])) {
          try { node.remove(); } catch (_e) { /* gone */ }
        }
      } catch (_e) { /* stub DOM */ }
    }
  }
  if (router && router.current !== 'recap') {
    logger.warn('recap was not up after the match ended (' + why + ') — forcing it');
    unmountHud();
    unmountMercy();
    router.show('recap');
  }
}

function detachMatch() {
  clearRecapFallback();
  // The match this hold belonged to is going: there is nobody left to tell, and
  // a flag that survived would hold the NEXT match's lobby behind a media screen
  // the player already finished with.
  clearMediaPrep(false);
  for (const off of phaseUnsubs) { try { off(); } catch (_e) { /* ignore */ } }
  phaseUnsubs = [];
  unmountHud();
  unmountMercy();
  try { executor?.detach?.(); } catch (e) { logger.warn('executor.detach threw: ' + ((e && e.message) || e)); }
  try { matchLog?.detach?.(); } catch (_e) { /* ignore */ }
  // Cancels every transfer and clears the queue; the STORE is untouched, because a
  // committed artifact is hash-keyed and stays valid across matches and sessions.
  try { mediaQueue?.detach?.(); } catch (_e) { /* ignore */ }
  // The voice service dies WITH the match (see attachMatch) and takes the bus
  // with it: a note that landed in the last second of a duel must not still be
  // talking over the recap.
  try { voice?.dispose?.(); } catch (_e) { /* ignore */ }
  voice = null;
  try { wakeLock?.stop?.(); } catch (_e) { /* a screen convenience, never load-bearing */ }
  try { currentSd?.dispose?.(); } catch (_e) { /* ignore */ }
  currentSd = null;
  currentMatch = null;
  currentTransport = null;
  paintProbe();
}

/* ----------------------------------------------------------------------------
 * THE MIC BREADCRUMB — one warn, once per match, when the duel goes live with no
 * mic on the desk.
 *
 * THE BUG THIS EXISTS FOR (owner, 2026-08-05): "I cannot see the mic button."
 * Three play-tests, three setups (iPhone Safari, iPhone Safari incognito, a
 * desktop pair), never once visible — and the page said absolutely nothing in
 * any of the three, because a hidden mic is the CORRECT rendering of five ANDed
 * booleans and none of them is written down anywhere a play-test can read. Every
 * one of those three was the same first gate (their own seat had never opted
 * in), and finding that out took a code read rather than a log line.
 *
 * WARN, not info, and that is deliberate: warn is the level that reaches the C#
 * log through bridge.log AND the phone's ?debug=1 overlay. This is the exact
 * pattern (and the exact reasoning) as net/mediaQueue.js `whyNoTags` — the
 * untagged-payload breadcrumb that ended the same kind of three-round hunt on
 * the media lane a day earlier.
 *
 * ONCE. A match that reaches Live, then SuddenDeath, then a relay REBUILD would
 * otherwise say it three times for one fact; `micGateSaid` is cleared in
 * attachMatch, so a genuinely new match gets a genuinely new line.
 */
function reportMicGate() {
  if (micGateSaid) return;
  micGateSaid = true;
  const mic = hudHandle && hudHandle.parts ? hudHandle.parts.mic : null;
  try {
    if (!voice) { logger.warn('voice mic hidden: no voice service (createVoiceService failed at attach)'); return; }
    // The two facts the SERVICE cannot see for itself: the desk's zen bit and
    // whether this host has a microphone API at all. Both are read defensively —
    // a handle-shaped stub (no service = a no-op mic) answers neither.
    let hudHidden = false;
    let deskWhy = '';
    let micSupported;
    /* WHICH piece of chrome, not just "some chrome". Zen and the arsenal drawer
     * both take the slot away and they are undone by opposite gestures — and the
     * drawer is the DESKTOP failure this breadcrumb existed for and could not
     * name: an in-app seat plays with the drawer shut (every payload is on the
     * number row) and the mic is a flow child of the panel that shutting it
     * removes. Read defensively: a handle-shaped stub answers none of this. */
    try { if (mic && typeof mic.hiddenBy === 'function') deskWhy = String(mic.hiddenBy() || ''); }
    catch (_e) { /* an older handle simply cannot say */ }
    if (deskWhy) hudHidden = deskWhy === 'zen';
    else {
      try { if (mic && typeof mic.shown === 'function' && typeof mic.available === 'function') hudHidden = mic.available() && !mic.shown(); }
      catch (_e) { /* leave it false */ }
    }
    try { if (mic && mic.recorder && typeof mic.recorder.supported === 'function') micSupported = !!mic.recorder.supported(); }
    catch (_e) { /* leave it undefined — the gate is then not reported */ }

    const why = typeof voice.whyUnavailable === 'function'
      ? voice.whyUnavailable({ hudHidden, micSupported })
      : (voice.available() ? '' : 'unknown (this build has no whyUnavailable)');
    // Shown and working: say nothing. A quiet log is the good outcome.
    if (!why && mic && typeof mic.shown === 'function' && mic.shown()) return;
    if (!why && deskWhy === 'drawer') {
      logger.warn('voice mic hidden: the arsenal drawer is shut and the mic is inside it — hold V to record, or open the drawer');
      return;
    }
    if (!why) { logger.warn('voice mic hidden: the service says live but the desk has no strip (mount failure?)'); return; }
    logger.warn('voice mic hidden: ' + why);
  } catch (e) {
    // A diagnostic that can break a match is worse than no diagnostic.
    logger.warn('voice mic gate check threw: ' + ((e && e.message) || e));
  }
}

function mountHudNow() {
  if (hudHandle || !mountHud || !currentMatch) return;
  try {
    hudHandle = mountHud({
      // `voice` is threaded in now and UNUSED until wave 2 mounts ui/voice/micHud.js
      // from inside the HUD. mountHud takes a destructured options object, so an
      // extra key is inert — and handing it over here means the mic lands as one
      // line in ui/hud.js rather than as a second wiring pass through this file.
      match: currentMatch, session, audio, prefs, media, matchLog, discord, voice, coach,
    }) || null;
  } catch (e) { logger.error('mountHud threw: ' + ((e && e.stack) || e)); hudHandle = null; }
}

function unmountHud() {
  if (!hudHandle) return;
  try { hudHandle.unmount?.(); } catch (e) { logger.warn('hud.unmount threw: ' + ((e && e.message) || e)); }
  // A coached line still waiting its turn is about a match that has just ended.
  // The marks stay; only the queue goes. See coach.clearPending.
  try { coach?.clearPending?.(); } catch (_e) { /* a hint is never load-bearing */ }
  hudHandle = null;
  const hud = el('gg-hud');
  if (hud) { hud.replaceChildren(); hud.hidden = true; }
}

function mountMercyNow() {
  if (mercyHandle || !mountMercy) return;
  try {
    mercyHandle = mountMercy({
      getMatch: () => currentMatch,
      audio,
      onLog: (entry) => matchLog?.add?.(entry),
    }) || null;
  } catch (e) { logger.error('mountMercy threw: ' + ((e && e.stack) || e)); mercyHandle = null; }
}

function unmountMercy() {
  if (!mercyHandle) return;
  try { mercyHandle.unmount?.(); } catch (e) { logger.warn('mercy.unmount threw: ' + ((e && e.message) || e)); }
  mercyHandle = null;
  const m = el('gg-mercy');
  if (m) { m.replaceChildren(); m.hidden = true; }
}

/* ----------------------------------------------------------------------------
 * THE MEDIA STEP, HELD IN FRONT OF THE LOBBY.
 *
 * Returns true when it took the screen, so the phase arms can `break` on it
 * without knowing anything else about the feature. Deliberately NOT a phase and
 * NOT a gate on the engine: the match reaches Consent underneath and simply
 * waits, exactly as it would for a player who had wandered off to make tea.
 *
 * The `media_prep` frame is the other half — without it the host sits on
 * "waiting for them" with no way to tell an empty room from a busy one, which
 * is how a host gives up thirty seconds before the duel would have started.
 * -------------------------------------------------------------------------- */
function showMediaSetup() {
  if (!mediaPrepPending || !router) return false;
  if (!mediaPrepTold) {
    mediaPrepTold = true;
    try { currentMatch?.setMediaPrep?.(true); } catch (_e) { /* a status hint is never load-bearing */ }
  }
  if (router.current !== 'mediaSetup') router.show('mediaSetup');
  return true;
}

/** Clears the step (and tells the peer), whatever the reason. Idempotent. */
function clearMediaPrep(tellPeer) {
  const wasPending = mediaPrepPending;
  mediaPrepPending = false;
  if (tellPeer && mediaPrepTold) {
    try { currentMatch?.setMediaPrep?.(false); } catch (_e) { /* ignore */ }
  }
  mediaPrepTold = false;
  return wasPending;
}

/* ----------------------------------------------------------------------------
 * PHASE ROUTING — the engine's phase is the single source of truth for what is
 * on screen. Nothing else calls router.show() for a match screen.
 * -------------------------------------------------------------------------- */
function onPhase(phase) {
  try { document.documentElement.setAttribute('data-gg-phase', String(phase)); } catch (_e) { /* ignore */ }
  // The drone bed rides the phase like everything else on this screen: in at the
  // countdown, out at the recap, and idempotent, so a relay REBUILD re-firing the
  // phase mid-Live re-ramps the same oscillators instead of stacking a second bed
  // (ui/droneBed.js). Practice runs real phases over the loopback pair, so it is
  // covered by the same line.
  try { audio?.dronePhase?.(phase); } catch (_e) { /* the bed is never load-bearing */ }
  paintProbe();

  /* THE HOLD CANNOT OUTLIVE THE LOBBY. It is only ever consulted by the Lobby
   * and Consent arms, so a match that reached the draft with the flag still set
   * would carry a dead hold around for the rest of the session and re-assert it
   * on the next lobby. Clearing it here (and telling the peer, so their "picking
   * their media…" line comes down) makes "past Consent" the one exit that needs
   * no cooperation from the screen. */
  if (mediaPrepPending && phase >= GoonMatchPhase.Draft) clearMediaPrep(true);

  switch (phase) {
    case GoonMatchPhase.Lobby:
      // The host/join screen stays up until there is a second person to show —
      // a lobby with one silhouette in it is worse than the code the player is
      // still reading aloud.
      if (showMediaSetup()) break;
      if (router.current !== 'host' && router.current !== 'join') router.show('lobby');
      break;

    case GoonMatchPhase.Consent:
      // A joiner with an empty deck is held here — see showMediaSetup. The
      // engine has already reached Consent and stays there; the sheet is
      // waiting for them the moment they lock their picks in.
      if (showMediaSetup()) break;
      router.show('lobby');
      break;

    case GoonMatchPhase.Draft:
      router.show('draft');
      break;

    case GoonMatchPhase.Countdown:
      // Nothing may be left over the run when the clock starts — a forgotten
      // sheet's scrim would eat every bubble pop for the whole match (see
      // closeChrome). This is also what sweeps the first-duel share sheet if the
      // player left it open: a swept sheet resolves null and writes nothing.
      closeChrome();
      router.show('countdown');
      raiseVsSplash();
      break;

    case GoonMatchPhase.Live:
      closeChrome();          // belt and braces: a match can arrive at Live
                              // without us ever seeing its Countdown (rebuild,
                              // late join, a resumed transport)
      dropVsSplash();         // the splash's hard deadline, whatever it was doing
      router.hide();
      mountHudNow();
      mountMercyNow();
      // AFTER the HUD, because the answer is partly about the strip that was
      // just (not) put on the desk. See reportMicGate.
      reportMicGate();
      try { discord?.setRpState?.('live'); } catch (_e) { /* presence is never load-bearing */ }
      break;

    case GoonMatchPhase.SuddenDeath:
      // The HUD stays; the sudden-death UI drives #scr-sd through its presenter.
      break;

    case GoonMatchPhase.Recap:
      unmountHud();
      unmountMercy();
      // BEFORE the screen goes up, not after: everything that was over the
      // match has to come down or the recap is a picture (see clearForRecap).
      clearForRecap('phase');
      router.show('recap');
      try { discord?.setRpState?.('recap'); } catch (_e) { /* presence is never load-bearing */ }
      break;

    default:
      break;
  }
}

/* ----------------------------------------------------------------------------
 * ACTIONS — everything a screen is allowed to DO. Screens never touch
 * GoonSession, the transports or the match factory directly.
 * -------------------------------------------------------------------------- */
/* ----------------------------------------------------------------------------
 * FOLDING TO THE TITLE — deferred by one macrotask, and that is the whole fix.
 *
 * THE BUG THIS EXISTS FOR (owner's phone test, 2026-08-04): "when i accept the
 * invite briefly shows the page of the setup before game then bounces me back to
 * homepage." Every internal teardown inside net/session.js — a signaling pump
 * that gave up, a relay that never answered, a P2P attempt that failed in a way
 * nothing flagged for fallback — raises onCurrentMatchChanged(null), and this
 * file used to answer that with a bare `router.show('title')`. That is a SILENT
 * eviction: the lobby vanishes and nothing anywhere tells the player a single
 * thing about why. The explanation always exists (GoonSession raises
 * connectFailed immediately afterwards, and the entry screens render their own
 * errors) — it just arrived AFTER the screen was already gone.
 *
 * So the jump waits one turn of the event loop. The paths that own an
 * explanation (onConnectFailed above, actions.leave, the exit handshake) cancel
 * it and route the player themselves; a teardown nobody claims still lands on
 * the title, exactly as before, one tick later and with a line in the log.
 * -------------------------------------------------------------------------- */
let foldTimer = 0;

function foldToTitle() {
  cancelFoldToTitle();
  const go = () => {
    foldTimer = 0;
    // A session that came back (a relay rebuild lands a new match on the very
    // next tick) has nothing to fold, and neither does a screen that already
    // moved on under its own power.
    if (currentMatch || soloPair || !router) return;
    logger.warn('session folded with no explanation — returning to the title');
    router.show('title');
  };
  try { foldTimer = setTimeout(go, 0); } catch (_e) { go(); }
}

function cancelFoldToTitle() {
  if (!foldTimer) return;
  try { clearTimeout(foldTimer); } catch (_e) { /* ignore */ }
  foldTimer = 0;
}

/**
 * THE ANONYMOUS SEAT, remembered across reloads.
 *
 * A visitor who arrived on an invite link has no account, so /join mints them a `g_` identity and
 * that id IS their seat: lose it and a refresh comes back as a stranger, is told the room already
 * has two players — by its own ghost — and the invite is dead for the rest of the room's TTL.
 * It rides in the same localStorage blob as every other standalone pref, and `save` writes the
 * live fields too, because ensureSession() builds ONE GoonSession per page and the reclaim has to
 * be readable by the signaling client it constructs next.
 */
const guestSeat = {
  id: String(bridge.storedPrefs().guestId || ''),
  code: String(bridge.storedPrefs().guestRoom || ''),
  at: Number(bridge.storedPrefs().guestAt) || 0,
  save(id, code) {
    this.id = String(id || '');
    this.code = String(code || '');
    // The claim instant bounds the RESUME window (openFirstScreen): a seat is
    // only worth walking back into while its room can still exist server-side.
    this.at = this.id ? Date.now() : 0;
    try { bridge.savePrefs({ guestId: this.id, guestRoom: this.code, guestAt: this.at }); }
    catch (_e) { /* a lost seat costs a rejoin, never the match in progress */ }
  },
  /** The seat is spent — left on purpose, or its room is confirmed dead. */
  clear() { this.save('', ''); },
};

/** How long a remembered guest seat is worth resuming: the room's own match TTL. */
const GUEST_SEAT_FRESH_MS = 30 * 60 * 1000;

function ensureSession() {
  if (goonSession) return goonSession;
  goonSession = new GoonSession({
    createMatch: (transport, isHost) => buildMatch(transport, isHost),
    identity: session.identity || {},
    guest: guestSeat,
    logger,
  });
  // THE rebuild seam. The relay fallback disposes the old match and builds a
  // new one over a new transport; re-binding here is what keeps the executor,
  // the log and the HUD pointed at the live object.
  goonSession.onCurrentMatchChanged((match) => {
    if (match) {
      logger.info('currentMatch changed -> re-attaching');
      attachMatch(match, goonSession.transport);
    } else {
      detachMatch();
      if (!soloPair) foldToTitle();
    }
  });
  goonSession.onConnectFailed((reason) => {
    lastConnectFailed = reason;
    logger.warn('connect failed: ' + reason);
    // THE FOLD IS SPOKEN FOR. GoonSession tears the match down and THEN raises
    // this, so without the cancel the deferred jump would land first and the
    // sheet would open over a title screen — telling the player "we could not
    // connect" about a screen they were already yanked off.
    cancelFoldToTitle();
    if (awaitingEntry) return;      // the entry screen renders it
    sheets?.showSignalError?.(errorInfo(reason)).then(() => router.show('title'));
  });
  return goonSession;
}

/* ----------------------------------------------------------------------------
 * THE RESUME KICK — iOS's half of the connection story (2026-08-04 play-test).
 *
 * A phone in the OS photo sheet, a locked screen, an app switch: Safari clamps
 * or outright freezes this page's timers, and both network loops (the P2P
 * signal pump and the relay mailbox loop) park inside a setTimeout that never
 * fires on schedule. Meanwhile the desktop host reads the silence as the guest
 * being gone — the play-test host sat on "waiting for your opponent" while the
 * guest was right there, picking photos. The loops already expose a wake seam
 * (`nudge()`, used by their own send paths); this just pulls it the moment the
 * page is visible again, so the first poll happens NOW and not a backoff later.
 * Registered once, at module scope, like bridge's own listeners — the handler
 * reads the CURRENT session through the closure, so it survives every rebuild.
 * -------------------------------------------------------------------------- */
if (typeof document !== 'undefined' && typeof document.addEventListener === 'function') {
  const kick = () => {
    try { if (document.visibilityState === 'hidden') return; } catch (_e) { /* be generous */ }
    try { goonSession?.transport?.nudge?.(); } catch (_e) { /* a kick is never load-bearing */ }
  };
  document.addEventListener('visibilitychange', kick);
  if (typeof window !== 'undefined' && typeof window.addEventListener === 'function') {
    window.addEventListener('pageshow', kick);   // bfcache restores skip visibilitychange
    window.addEventListener('online', kick);     // radio came back before the timer did
    window.addEventListener('focus', kick);
  }
}

/** Merge the machine reason with whatever the signaling client recorded. */
function errorInfo(reason) {
  const sig = goonSession && goonSession.signaling ? goonSession.signaling.lastErrorInfo : null;
  if (sig && sig.kind) return sig;
  return { kind: String(reason || 'network'), detail: null, retryAfterSeconds: null };
}

async function teardownEverything() {
  detachMatch();
  /* RICH PRESENCE CANNOT BE ALLOWED TO STRAND. Every road out of a match runs
   * through here (leave, quit, host/join restart, connect failure, the exit
   * handshake below) and `off` is idempotent inside ui/discord.js, so posting it
   * on all of them costs one frame and closes every hole at once. A player whose
   * Discord still says "In a duel" an hour after they quit is the failure this
   * placement exists to make impossible. */
  try { discord?.setRpState?.('off'); } catch (_e) { /* presence is never load-bearing */ }
  try { discord?.clearPeer?.(); } catch (_e) { /* ignore */ }
  dropVsSplash();
  if (soloDriver) { try { soloDriver.stop(); } catch (_e) { /* ignore */ } soloDriver = null; }
  if (soloOpponent) { try { soloOpponent.dispose(); } catch (_e) { /* ignore */ } soloOpponent = null; }
  if (soloPair) { try { soloPair.dispose(); } catch (_e) { /* ignore */ } soloPair = null; }
  if (goonSession) {
    const s = goonSession;
    goonSession = null;
    try { await s.dispose(); } catch (_e) { /* ignore */ }
  }
  try { layers.stopAll(); } catch (_e) { /* ignore */ }
  try { executor?.stopAll?.(); } catch (_e) { /* ignore */ }
  // LAST, after the executor is stopped: nothing can be mid-draw on a view the
  // drop is about to destroy. See purgeReceived for why this seam is the one.
  purgeReceived('teardown');
}

const actions = {
  goTitle() { router.show('title'); },
  goHost() { router.show('host'); },
  goJoin() { router.show('join'); },
  /** @param {{filter?:string}} [args] e.g. {filter:'needs'} from a "N need compressing" prompt. */
  goAssets(args) { router.show('assets', args || null); },
  /** ui/screens/voice.js — the pre-recorded note library. Title menu only:
   *  there is deliberately no way into it from inside a match (the toggle it
   *  carries clears both consent signatures, and a live duel is the wrong place
   *  to renegotiate a term). */
  goVoice() { router.show('voice'); },

  async goPractice() { await startSolo(); },

  quit(why) { requestExit(why || 'menu'); },

  /** @returns {Promise<{ok:boolean, code?:string, error?:object}>} */
  async hostStart() {
    await teardownEverything();
    const s = ensureSession();
    awaitingEntry = true;
    lastConnectFailed = null;
    let code = null;
    try { code = await s.host(); } finally { awaitingEntry = false; }
    // /invite has answered, so `media_send` finally EXISTS. attachMatch already
    // ran (inside the await, before the POST) and read null — see
    // adoptServerSendVerdict. This is the first moment there is a verdict to fold.
    adoptServerSendVerdict();
    if (code) return { ok: true, code };
    return { ok: false, error: errorInfo(lastConnectFailed) };
  },

  /** @returns {Promise<{ok:boolean, error?:object}>} */
  async joinStart(inviteCode) {
    await teardownEverything();
    const s = ensureSession();
    awaitingEntry = true;
    lastConnectFailed = null;
    let ok = false;
    try { ok = await s.join(inviteCode); } finally { awaitingEntry = false; }
    // The guest's half of the same fold — and the one that actually mattered to a
    // FREE seat, which is the seat that reaches a duel down this road. See
    // adoptServerSendVerdict: attachMatch ran before /join was ever posted.
    adoptServerSendVerdict();
    if (!ok) {
      const err = errorInfo(lastConnectFailed);
      /* A DEAD ROOM SPENDS THE SEAT. Without this, the resume path
       * (openFirstScreen) walks every fresh page load straight back into the
       * same expired code for up to half an hour — an auto-retry loop nobody
       * asked for, wearing an error screen. Only the seat's OWN room counts,
       * and only verdicts that mean the room is truly gone. */
      const kind = err && err.kind;
      if ((kind === 'expired' || kind === 'unknown_code') && guestSeat.id
        && String(inviteCode || '').trim().toUpperCase() === guestSeat.code) {
        guestSeat.clear();
      }
      return { ok: false, error: err };
    }

    /* THE FIRST-RUN MEDIA STEP, decided HERE and nowhere else — one join, one
     * answer. A duel plays the player's OWN library at them, so a joiner with an
     * empty deck would watch every effect fire against a blank screen and read
     * it as broken rather than as empty. Only the JOIN path asks: a host has
     * been through the title screen (and, hosted, has a preset behind them), and
     * this is squarely about the person who arrived on a link.
     *
     * `syncLocalDeck` first, because the answer is about the DECK and a pick
     * made moments ago may still be sitting in the store un-fed. */
    syncLocalDeck(true);
    mediaPrepTold = false;
    mediaPrepPending = needsMediaSetup(media);
    if (mediaPrepPending) {
      logger.info('joined with an empty deck — media setup first');
      /* THE RACE THIS CLOSES. GoonSession.join() attaches the match and fires
       * onPhase(Lobby) INSIDE the await above, i.e. before the flag exists. The
       * Lobby arm happens to be harmless (it leaves the join screen up while
       * `router.current === 'join'`) and Consent normally lands a moment later,
       * after this line — but "normally" is doing the work in that sentence, and
       * a rejoin that arrives already consented would sail straight past. One
       * idempotent call here means the hold is applied to whatever phase the
       * match is ALREADY in, whenever it got there. */
      showMediaSetup();
    }
    return { ok: true };
  },

  /**
   * The media step's "I'm set". Clears the hold, tells the peer, and hands the
   * player to whatever phase the match reached while they were picking — the
   * screen itself decides nothing about where it goes next.
   */
  mediaPrepDone() {
    if (!clearMediaPrep(true)) return;
    syncLocalDeck(true);
    logDeck('media-setup');
    if (currentMatch) onPhase(currentMatch.phase);
    else router.show('title');
  },

  /** The host/join screen's Cancel: fold the pending room, stay on the page. */
  async cancelPending() {
    await teardownEverything();
  },

  /**
   * User-initiated exit from wherever we are. In Live/SuddenDeath GoonSession
   * routes this through cancelMatch, i.e. a MERCY — leaving is never a way to
   * dodge a loss. Pre-live it is a clean fold that never reaches the ledger.
   */
  async leave(why) {
    logger.info('leave (' + (why || '?') + ')');
    const s = goonSession;
    if (s) { try { await s.leave(); } catch (e) { logger.warn('leave threw: ' + ((e && e.message) || e)); } }
    else if (currentMatch) { try { currentMatch.cancelMatch('left'); } catch (_e) { /* ignore */ } }
    await teardownEverything();
    // Walking out is a decision — the resume path must not walk them back in.
    guestSeat.clear();
    cancelFoldToTitle();      // leaving is an explanation of its own
    router.show('title');
  },
};

/**
 * ONE LINE THAT SAYS WHAT THE DECK IS, and it is a WARN on purpose.
 *
 * `logger.info` goes to console.info and to the C# tunnel — neither of which
 * exists on a phone running this page standalone. Only warn/error reach the
 * ?debug=1 overlay (see teeDebug above), which is the only console an owner with
 * an iPhone has. Two blind round-trips on "practice fires no assets" were spent
 * guessing at exactly this number; do not demote it to info.
 *
 * It separates the three things that were indistinguishable from a screenshot:
 * what the HOST's preset gave us, what the PLAYER picked in this browser, and
 * how much of what they picked was actually playable.
 */
function logDeck(why) {
  try {
    const c = media.counts();
    const local = media.localCount();
    const host = Math.max(0, (c.images + c.videos) - local);
    const picked = assets ? (assets.localCount | 0) : 0;
    logger.warn(why + ' deck: ' + host + ' host + ' + local + ' local'
      + ' (' + c.images + ' img / ' + c.videos + ' vid)'
      + ' from ' + picked + ' pick(s), ' + media.receivedCount() + ' received');
  } catch (_e) { /* a diagnostic can never be the thing that breaks practice */ }
}

/* ----------------------------------------------------------------------------
 * THE PRACTICE SEED — the media-carrying slots start ARMED, in practice only.
 *
 * Owner report, 2026-08-04: "from mobile it's not triggering any asset even if I
 * uploaded them and went to practice", with every arsenal chip reading `locked`.
 * That reading was correct and the economy was working as designed: a slot is
 * earned by popping bubbles (ui/drops.js), and on a fresh match nothing is
 * earned yet. In a DUEL that ramp is the game. In PRACTICE it is a wall in front
 * of the one question practice exists to answer — "does my library work?" — so
 * practice hands over the two slots that draw from the library and lets the
 * player find out in the first ten seconds.
 *
 * IT USED TO HAND OVER CHARGES TOO (`match.creditCharges(charges,
 * 'practice-seed')`), because an armed slot with an empty meter would have fired
 * into a rejection: the receiver validated the sender's wallet. The owner deleted
 * that requirement on 2026-08-05, so the seed is now exactly the two stacks and
 * nothing else — there is no second truth left to keep in step.
 *
 * DUEL GATING IS UNTOUCHED: this is reached from startSolo's own phase
 * subscription and from nowhere else, and it goes through the same public seam
 * ui/drops.js uses (armDrop).
 * -------------------------------------------------------------------------- */
const PRACTICE_SEED = Object.freeze([
  // `cost` is not spent — it is the drop-rarity weight these two carry elsewhere,
  // kept here so the pair reads the same as an ARSENAL_ITEMS row.
  Object.freeze({ id: 'flash', cost: 1 }),    // images, the common one
  Object.freeze({ id: 'video', cost: 2 }),    // a clip, in a floating window
]);

function seedPracticeArsenal() {
  const arsenal = hudHandle && hudHandle.parts && hudHandle.parts.arsenal;
  if (!arsenal || typeof arsenal.armDrop !== 'function') return;
  let armed = 0;
  for (const seed of PRACTICE_SEED) {
    // `silent` — the drop flourish is a reward animation and this is a gift, not
    // a reward. It would also fire before the HUD has finished its entrance.
    if (arsenal.armDrop(seed.id, { count: 1, silent: true })) armed++;
  }
  if (!armed) return;
  logger.info('practice: seeded ' + armed + ' arsenal slot(s)');
  /* ...AND SAY SO. The seed is silent by design (`silent: true` above — a gift,
   * not a reward), which solved the "nothing is happening" report and left a
   * second one behind it: two stickers quietly light up in a drawer that is SHUT
   * by default on a phone, and nothing anywhere says they are there or what to
   * press. Practice is the one place coaching may be a little louder, so this is
   * the one hint fired from outside the desk.
   *
   * Deferred a beat: mountHudNow() has only just run and the HUD's entrance is
   * still animating, so a toast on this tick lands under a moving desk. */
  setTimeout(() => {
    try { coach?.fire?.(COACH.PRACTICE, S.coach.practice); } catch (_e) { /* never break practice */ }
  }, 900);
}

/* ----------------------------------------------------------------------------
 * PRACTICE — a loopback pair and a scripted opponent (ui/soloDriver.js).
 *
 * Two real matches over a real (in-process) transport: identical message types,
 * identical clock behaviour, real latency and a deliberately weird guest clock
 * skew, so a bug that only appears "when the other side does X" appears here.
 * The local side is the HOST, because that is the side that proposes the
 * consent sheet and the countdown, i.e. the side with more UI to exercise.
 * -------------------------------------------------------------------------- */
async function startSolo() {
  await teardownEverything();

  const profile = (session.match && session.match.profile) || 'p2p';
  const preset = loopbackPresets[profile] || loopbackPresets.p2p;
  const opts = preset();
  const skew = session.match && Number(session.match.skewMs);
  if (isFinite(skew) && skew) opts.guestClockSkewMs = skew;
  opts.logger = logger;

  soloPair = createLoopbackPair(opts);

  const local = buildMatch(soloPair.host, true);
  soloOpponent = buildMatch(soloPair.guest, false, { withSuddenDeathUi: false, displayName: 'Practice' });
  soloDriver = createSoloDriver({ match: soloOpponent, logger });

  attachMatch(local, soloPair.host);
  /* THE PRACTICE SEED — see seedPracticeArsenal. Registered AFTER attachMatch so
   * it runs after boot's own phase handler, i.e. after mountHudNow() has built
   * the arsenal it seeds. On phaseUnsubs so it dies with the match. */
  phaseUnsubs.push(local.onPhaseChanged((p) => {
    if (p === GoonMatchPhase.Live) seedPracticeArsenal();
  }));
  /* PRACTICE HAS A FACE TOO — a tile, a name and dm:false (contract §6). It is
   * set AFTER attachMatch, which clears the peer, and it is the only way the
   * splash, the HUD minis and the recap plates are exercisable with no server
   * and no second machine. The bot is not a person: it never gets a DM button. */
  try { discord?.setSoloPeer?.(S.discord.practiceBot); } catch (_e) { /* ignore */ }
  local.adoptLobby();
  soloOpponent.adoptLobby();
  soloDriver.start();

  logger.info('practice: loopback "' + profile + '" latency ' + opts.latencyMs + 'ms skew ' + opts.guestClockSkewMs + 'ms');
  logDeck('practice');
  try {
    const ok = await soloPair.connect();
    if (!ok) logger.warn('practice: clock sync did not converge');
  } catch (e) {
    logger.error('practice: connect failed: ' + ((e && e.stack) || e));
    toasts?.bad?.('practice could not start');
    await teardownEverything();
    router.show('title');
  }
}

/* ============================================================================
 * BUILD — one call, after init+manifest, before the first screen.
 * ==========================================================================*/
function buildApp() {
  prefs = createPrefs(session.prefs);
  /* STAMP THE PERFORMANCE TIER — <html data-gg-perf>, the attribute the lite
   * CSS and every renderer cap key off. Before any renderer is built (the
   * executor comes later in startup) so the first spawn already sees it, and
   * re-stamped on every change of the pref so the options toggle reaches a
   * match already running: the caps, the spiral throttle and the lite CSS all
   * read it lazily. ui/prefs.js cannot mirror this one itself — resolving
   * 'auto' needs the detector, which lives in exec/ (see the pref's banner). */
  applyPerfTier(prefs.get('perfMode'));
  prefs.subscribe((key, value) => { if (key === 'perfMode') applyPerfTier(value); });
  audio = createAudio({ prefs, logger });
  toasts = createToasts({ prefs });
  /* AFTER the toasts, because that is its whole output tier, and BEFORE the
   * options drawer, which offers its switch. Nothing coaches until a HUD is
   * mounted — this only builds the ledger. */
  coach = createCoach({ prefs, toasts, logger });
  sheets = createSheets({ audio, logger });
  matchLog = createMatchLog();
  // ONE store, built once, and the only thing on the page allowed to register a
  // cache-* handler (bridge.on throws on a duplicate and the assets screen
  // mounts many times per session).
  assets = createAssetsStore({ session, logger });
  // Every pick the player adopts (and every one they remove) re-feeds the media
  // deck — see syncLocalDeck above. `onItems` is the store's one change signal;
  // the version guard is what keeps a hosted cache-list out of the pool.
  assets.onItems(() => syncLocalDeck());
  syncLocalDeck();

  /* --- DISCORD. Built once, like the store above and for the same reason: it
   * registers the `discord` and `peer-card` handlers and bridge.on throws on a
   * duplicate, while the lobby it renders into mounts many times per session.
   *
   * `getRoom` is a THUNK, never a snapshot: /v2/goon/peercard is room-authed and
   * the page is the only side holding {code, token, role}, but a relay fallback
   * replaces session.room mid-match (stashRoom) and a captured copy would go
   * stale exactly when the fetch needed it. */
  discord = createDiscord({
    prefs, logger,
    getRoom: () => session.room,
  });
  discord.applyInit(session.discord);

  /* --- P2P media transfer. Built once; the queue re-attaches per match. ------ */
  // The store owns `goon-recv-result` (bridge.on throws on a duplicate — the module
  // that correlates the replies owns the handler, exactly like net-post-result).
  receivedStore = createReceivedStore({ logger });
  blocklist = createBlocklist({
    post: (path, body) => bridge.postNet(path, body),
    unifiedId: () => (session.identity && session.identity.unifiedId) || '',
    logger,
  });
  // The gate that actually matters is at render time, and this is where it is armed.
  media.attachBlocklist(blocklist);
  blocklist.onBlocked((sha) => {
    // A hash that comes back blocked stops rendering AND stops existing.
    try { media.dropReceived(sha); } catch (_e) { /* ignore */ }
    try { receivedStore.drop(sha); } catch (_e) { /* ignore */ }
  });

  artifacts = createArtifactSource();

  /* WHAT THIS DEVICE CAN DECODE, asked ONCE and told to the peer on the xfer hello.
   *
   * The sender's own probe (ui/assetsStore.js probeVideoDecodable) only ever proved that the
   * SENDER can play its clip — Safari decodes its own HEVC, adopts it, transfers it perfectly,
   * and the Windows peer paints a silent black window for the whole slot with no error anywhere.
   * This is the missing half: the peer's list reaches net/mediaQueue.js, which stops offering
   * what the other side has no decoder for. A runtime that cannot be asked (and node) advertises
   * nothing, which every peer reads as "send me anything" — the old behaviour, exactly. */
  let decodeCodecs = null;
  try { decodeCodecs = probeDecodeCodecs(); } catch (e) {
    logger.warn('the decode probe threw (' + ((e && e.message) || e)
      + ') — advertising nothing, so the peer will keep offering everything');
    decodeCodecs = null;
  }
  logger.info('decodes: ' + (decodeCodecs ? decodeCodecs.join(', ') : '(could not probe)'));

  mediaQueue = createMediaQueue({
    artifacts,
    store: receivedStore,
    blocklist,
    logger,
    acceptsCodecs: decodeCodecs,
    // === true, NOT !== false (the idiom brainDrain/spiral use above). Kept
    // strict even though the capability is free now: a host that predates the
    // flag entirely says nothing, and "said nothing" must not read as consent to
    // start a lane. Every host that DOES speak the flag sets it true (C#
    // TransferAllowed, bridge.js standalone default), so the strictness only
    // ever catches a genuinely ancient frame. Receiving is never gated.
    canSend: () => !!(session.caps && session.caps.mediaTransfer === true),
  });
  mediaQueue.onReceived((a) => {
    registerReceived([a]);
    blocklist.check([a.sha]);
  });
  primeReceived(session.received);      // the manifest usually beat us here

  /* --- VOICE NOTES: the library, and the emote hook -------------------------
   * ONE store for the page (see the declaration). It is built even on a host
   * with no IndexedDB — the store falls back to memory and the screen still
   * works for the length of the session.
   *
   * The provider below is how ui/emotes.js reaches a service it cannot import:
   * the sheet is mounted by ui/hud.js (a sibling wave's file), the service is
   * rebuilt per match, and the map lives in prefs. Asking through one closure
   * keeps all three facts in this file — and returns null for every case where
   * nothing should be sent, so the hot path in emotes.js is a single call with
   * no knowledge of any of it. */
  noteStore = createNoteStore({ prefs, logger });
  setVoiceProvider((emoteKey) => {
    if (!voice || !noteStore) return null;
    const noteId = noteStore.noteFor(emoteKey);
    if (!noteId) return null;
    return { voice, noteId };
  });

  options = createOptions({
    prefs, audio, session, logger,
    setFullscreen: (on) => bridge.send({ type: 'fullscreen-set', on: !!on }),
    isInMatch: () => !!currentMatch && currentMatch.phase !== GoonMatchPhase.Idle,
  });

  /* THE IN-MATCH OPTIONS SEAM. The HUD's gear (ui/hud.js) cannot reach `options`
   * — it is a boot singleton and the HUD is mounted by a dynamically loaded
   * sibling — so it asks by event and THIS is the ear. Without this listener the
   * gear is a button that does nothing, which is exactly how it shipped.
   *
   * It is a TOGGLE, and the drawer is an OVERLAY (#gg-drawer, z70) — never a
   * router.show(). Opening it during a live match must not touch the phase, the
   * HUD or the screen stack; the match keeps running underneath, Escape still
   * means MERCY (see the escape ladder), and the drawer clips itself clear of
   * the mercy button (ui/options.js MERCY_CLEARANCE_PX). */
  try {
    document.addEventListener('gg-options-open', () => {
      try { options?.toggle?.(); } catch (e) { logger.warn('options.toggle threw: ' + ((e && e.message) || e)); }
    });
  } catch (_e) { /* no document: nothing to open */ }

  /* THE HUD'S DM SEAM, and it is the gear's pattern for the gear's reason: the
   * opponent mini lives inside a dynamically loaded sibling and cannot reach
   * either `sheets` or `discord`, so it asks by event and THIS is the ear. The
   * confirm is not optional — opening a browser out of a fullscreen duel is a
   * big thing to do to somebody, and the sheet is where they can say no. */
  try {
    document.addEventListener('gg-discord-dm', (e) => {
      const d = (e && e.detail) || {};
      void confirmOpenDm({
        discord,
        sheets,
        which: d.which === 'last' ? 'last' : 'peer',
        name: String(d.name || (discord && discord.peer && discord.peer.name) || ''),
      });
    });
  } catch (_e) { /* no document: nothing to open */ }

  /* WORK ITEM E, OPTIONALLY. ui/avatarFx.js decorates the bubbles this file's
   * modules build; it is loaded dynamically and its absence is a cosmetic loss,
   * not a boot failure — exactly like the sibling waves above. It attaches to
   * <body> and finds bubbles by MutationObserver, so nothing has to hand it the
   * lobby, the splash, the HUD minis or the recap plates one at a time. */
  try {
    import('./ui/avatarFx.js')
      .then((mod) => {
        const fx = mod && (mod.avatarFx || mod.default);
        if (fx && typeof fx.attach === 'function' && hasDom()) fx.attach(document.body);
      })
      .catch(() => { logger.info('avatarFx not present — bubbles will be static'); });
  } catch (_e) { /* an import that cannot even be attempted is the same non-event */ }

  try {
    document.documentElement.setAttribute('data-gg-motion', prefs.get('reduceMotion') ? 'reduced' : 'full');
  } catch (_e) { /* ignore */ }

  if (createExecutor) {
    try {
      executor = createExecutor({
        media, layers, audio, logger,
        // The toy bridge only exists inside the app: a browser cannot reach a
        // toy, and a null bridge is how the executor knows to skip toy cues.
        toyBridge: session.hosted ? ((m) => bridge.send(m)) : null,
      });
    } catch (e) {
      logger.error('createExecutor threw: ' + ((e && e.stack) || e));
      executor = null;
    }
  }

  ctx = {
    session, prefs, audio, toasts, sheets, options, matchLog, actions, logger, assets,
    receivedStore, blocklist, mediaQueue, discord,
    /** ui/screens/voice.js: the pre-recorded note library (page-scoped). */
    notes: noteStore,
    /** ui/screens/recap.js: which peer artifacts rendered (and were flagged) this match. */
    getPeerRenders: peerRenderLog,
    getMatch: () => currentMatch,
    /** ui/screens/voice.js (wave 2): the live service, or null out of a match.
     *  A THUNK, never a snapshot — it is rebuilt per match (and per relay
     *  fallback), and a screen that captured one would be holding a corpse. */
    getVoice: () => voice,
    getTransport: () => currentTransport,
    getClock: () => { try { return currentTransport ? currentTransport.clock : null; } catch (_e) { return null; } },
    getSd: () => currentSd,
  };

  router = createRouter({
    screens: {
      title: titleScreen,
      host: hostScreen,
      join: joinScreen,
      mediaSetup: mediaSetupScreen,
      lobby: lobbyScreen,
      draft: draftScreen,
      countdown: countdownScreen,
      recap: recapScreen,
      assets: assetsScreen,
      voice: voiceScreen,
    },
    ctx,
    logger,
  });
  ctx.router = router;
  router.onChanged(paintProbe);

  ensureProbe();
  paintProbe();
}

/* ----------------------------------------------------------------------------
 * PROBE — #gg-boot-ok lives on <body>, not inside a screen, so the play-test
 * driver keeps a live readout of screen + phase for the WHOLE run rather than
 * only while the title is up.
 * -------------------------------------------------------------------------- */
function ensureProbe() {
  if (!hasDom() || el('gg-boot-ok')) return;
  const span = document.createElement('span');
  span.id = 'gg-boot-ok';
  span.textContent = 'boot ok';
  document.body.appendChild(span);
}

function paintProbe() {
  const ok = el('gg-boot-ok');
  if (!ok) return;
  ok.dataset.hosted = String(session.hosted);
  ok.dataset.init = String(gotInit);
  ok.dataset.manifest = String(gotManifest);
  ok.dataset.solo = String(!!soloPair);
  ok.dataset.screen = String(router ? (router.current || 'none') : 'boot');
  ok.dataset.phase = String(currentMatch ? currentMatch.phase : -1);
  ok.dataset.stubs = stubs.join(',');
  ok.dataset.fullscreen = String(session.fullscreen);
}

/* ----------------------------------------------------------------------------
 * EXIT HANDSHAKE (mirrors DtRH): we ask with `exit`, the host tears the match
 * down and answers `end-run`, and only then do we answer `exit-done` — which is
 * the host's cue that the page is finished and the window can go. The 1.2s
 * fallback exists because a host that never answers must not strand the player.
 * -------------------------------------------------------------------------- */
function requestExit(why) {
  if (exiting) return;
  exiting = true;
  bridge.log('exit requested (' + why + ')');
  try { prefs?.flush?.(); } catch (_e) { /* ignore */ }
  bridge.send({ type: 'exit' });
  try { exitTimer = setTimeout(() => finishExit('fallback'), EXIT_FALLBACK_MS); } catch (_e) { finishExit('fallback'); }
}

function finishExit(why) {
  try { clearTimeout(exitTimer); } catch (_e) { /* ignore */ }
  exiting = true;
  cancelFoldToTitle();      // the page is leaving; there is no title to fold to
  // BEFORE teardownEverything, which is async: the page may be seconds from
  // gone and `rp-state off` is the frame the host needs to clear (or restore)
  // the presence. teardownEverything posts it again and the second one is a
  // no-op — that is the point of the dedupe.
  try { discord?.setRpState?.('off'); } catch (_e) { /* presence is never load-bearing */ }
  try { void teardownEverything(); } catch (_e) { /* best effort */ }
  try { layers.stopAll(); } catch (_e) { /* best effort */ }
  bridge.log('exit-done (' + why + ')');
  bridge.send({ type: 'exit-done' });
}

/* ----------------------------------------------------------------------------
 * THE ESCAPE LADDER — the safety contract, in code.
 *
 *   TAP, Live or SuddenDeath   -> declareMercy() ON KEYDOWN. Not on keyup, not
 *                                 behind a confirm dialog, not after an
 *                                 animation. The dignified concede has to be
 *                                 the FASTEST thing on the page, and the engine
 *                                 ends the match locally even if the wire is
 *                                 dead (core/match.js §11).
 *   TAP, Lobby/Consent/Draft/Countdown
 *                              -> confirm-free cancel + leave. Pre-live is a
 *                                 clean fold that never reaches the ledger, so
 *                                 there is nothing to confirm.
 *   TAP, Recap                 -> leave, exactly like "Back to menu". The match
 *                                 is already over: there is nothing left to
 *                                 concede, and Escape must never be a key that
 *                                 does nothing. The owner hit precisely that —
 *                                 an end card with no way out.
 *   TAP, no match              -> close the topmost overlay, or nothing.
 *   HOLD >= 1.2s               -> mercy first (if we have not already), THEN
 *                                 the exit handshake. Closing the window can
 *                                 never be a way to dodge a loss or strand the
 *                                 opponent waiting on a countersignature.
 *
 * e.repeat is dropped and `escDown` latches, so a held key fires the tap action
 * exactly once — a mercy storm would spam the wire with `mercy` frames.
 * The mercy BUTTON (ui/mercy.js, sibling I) calls the same match API; neither
 * path is privileged over the other.
 * -------------------------------------------------------------------------- */
let escTimer = 0;
let escDown = false;

function isPreLive(phase) {
  return phase === GoonMatchPhase.Lobby || phase === GoonMatchPhase.Consent
    || phase === GoonMatchPhase.Draft || phase === GoonMatchPhase.Countdown;
}

function onKeyDown(e) {
  if (e.key === 'F11') {
    // Fullscreen is C#-OWNED (borderless window), never the browser Fullscreen
    // API — that one eats the first Escape, which would break the exit ladder.
    if (bridge.isHosted) {
      e.preventDefault();
      bridge.send({ type: 'fullscreen-set', on: !session.fullscreen });
    }
    return;
  }
  if (e.key !== 'Escape' || e.repeat || escDown) return;
  escDown = true;

  const m = currentMatch;
  const phase = m ? m.phase : GoonMatchPhase.Idle;

  if (m && (phase === GoonMatchPhase.Live || phase === GoonMatchPhase.SuddenDeath)) {
    if (!escMercied) {
      escMercied = true;
      logger.info('escape -> mercy');
      try { m.declareMercy(); } catch (err) { logger.error('declareMercy threw: ' + ((err && err.stack) || err)); }
    }
  } else if (m && isPreLive(phase)) {
    logger.info('escape -> pre-live cancel');
    void actions.leave('escape');
  } else if (sheets && sheets.isOpen) {
    sheets.close(null);
  } else if (options && options.isOpen) {
    options.close();
  } else if (phase === GoonMatchPhase.Recap || (router && router.current === 'recap')) {
    // The end card is a screen, not a match: Escape leaves it the same way the
    // button does. Checked AFTER the overlays so Escape still peels the drawer
    // or a sheet off the recap first, one layer per press.
    logger.info('escape -> leave the recap');
    void actions.leave('escape-recap');
  }

  try {
    escTimer = setTimeout(() => { if (escDown) holdExit(); }, ESC_HOLD_MS);
  } catch (_e) { /* ignore */ }
}

function holdExit() {
  const m = currentMatch;
  if (m && !escMercied && (m.phase === GoonMatchPhase.Live || m.phase === GoonMatchPhase.SuddenDeath)) {
    escMercied = true;
    try { m.declareMercy(); } catch (_e) { /* ignore */ }
  }
  requestExit('hold-escape');
}

function onKeyUp(e) {
  if (e.key !== 'Escape') return;
  escDown = false;
  try { clearTimeout(escTimer); } catch (_e) { /* ignore */ }
}

/* ----------------------------------------------------------------------------
 * LIVENESS — and it is TWO different questions, which is the whole point.
 *
 * "IS THE SCRIPT ALIVE?" is the beat itself: a plain timer posts every ~2s, and
 * its SILENCE (not this code) tells the host the main thread wedged.
 *
 * "ARE PIXELS STILL MOVING?" is the `paint` counter: a bare rAF loop that does
 * nothing but ++ a number, stamped onto every beat. One callback per frame, no
 * work inside it.
 *
 * THEY ARE ON SEPARATE CLOCKS ON PURPOSE. The beat used to BE the rAF loop, so
 * the only failure it could describe was "everything stopped". On 2026-08-04 the
 * page visually froze with its JS still running — a compositor/GPU stall: live
 * script, no crash, no dump — and a heartbeat riding frames cannot tell that
 * apart from a healthy page when the frames themselves are what died. A timer
 * beat carrying a frame count can: beats arriving + `paint` frozen IS that
 * stall, and GoonHostService recovers on it (grep "paint stall detected").
 *
 * VISIBILITY RIDES ALONG BECAUSE NOT PAINTING IS OFTEN CORRECT. A hidden,
 * minimized or occluded window stops getting frames by design; alt-tabbing out
 * of a fullscreen duel must never look like a freeze. The host only counts a
 * stall while the page reports `vis: 'visible'` — the page states what it knows
 * and the host decides, instead of the host guessing at window state.
 *
 * Hosted only: standalone there is nobody listening and no reason to burn frames.
 * -------------------------------------------------------------------------- */
const HEARTBEAT_MS = 2000;

function startHeartbeat() {
  if (!bridge.isHosted) return;

  // The paint counter. NOTHING else may go in this callback: it is a liveness
  // probe, and a probe that does work is a probe that can be the problem.
  let frames = 0;
  const painting = typeof requestAnimationFrame === 'function';
  if (painting) (function frame() { frames++; requestAnimationFrame(frame); })();

  const visibility = () => {
    try {
      if (typeof document === 'undefined' || !document) return 'visible';
      return document.visibilityState || 'visible';
    } catch (_e) { return 'visible'; }
  };

  const beat = () => {
    const msg = { type: 'heartbeat', t: Date.now(), vis: visibility() };
    // OMITTED, not zeroed, on a host without rAF: a `paint` that can never move
    // would read to the watchdog as a permanent stall, and "no frame counter" is
    // a different fact from "no frames".
    if (painting) msg.paint = frames;
    try { bridge.send(msg); } catch (_e) { /* the bridge is the host's problem */ }
  };

  try { setInterval(beat, HEARTBEAT_MS); } catch (_e) { /* no timers, no beat */ }
  beat();
}

/* ----------------------------------------------------------------------------
 * GO — only in a real page. Under node (the import sweep) this whole block is
 * skipped, so every module stays provably side-effect free.
 * -------------------------------------------------------------------------- */
if (hasDom()) {
  // FIRST, before anything can go wrong: the strip is only useful if it is
  // already listening when the boot it is meant to explain fails.
  initDebugOverlay();
  try {
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('pagehide', () => { try { prefs?.flush?.(); } catch (_e) { /* ignore */ } });
  } catch (_e) { /* ignore */ }
  layers.setFxHeat(0);
  startHeartbeat();
  armDeadline();
  bridge.announceReady();
  // The build stamp is the FIRST diagnostic of every cross-device session:
  // "which build is that phone actually running" must be answerable from the
  // C# log / ?debug=1 overlay alone (see GOON_BUILD in bridge.js).
  bridge.log('boot: ready posted, build ' + bridge.GOON_BUILD + ' (' + (bridge.isHosted ? 'hosted' : 'standalone') + '), waiting for init + manifest');
}

/* Handy for the play-test driver and for anything that needs the shell's guts
 * without importing it (the C# side can evaluate window.__gg.session). */
if (typeof window !== 'undefined') {
  try {
    window.__gg = {
      session, bridge, media, layers, requestExit,
      get match() { return currentMatch; },
      get transport() { return currentTransport; },
      get router() { return router; },
      get prefs() { return prefs; },
      get matchLog() { return matchLog; },
      get assets() { return assets; },
      get received() { return receivedStore; },
      get blocklist() { return blocklist; },
      get mediaQueue() { return mediaQueue; },
      // null unless ?debug=1 armed it. `.sample()` is the fps/gap/long-task
      // snapshot the C# play-test driver can read without a screenshot.
      get perf() { return perfProbe; },
      get discord() { return discord; },
      get stubs() { return stubs.slice(); },
      actions,
    };
  } catch (_e) { /* ignore */ }
}
