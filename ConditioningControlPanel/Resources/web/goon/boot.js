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

import { GoonMatchService } from './core/match.js';
import { GoonSuddenDeathRunner } from './core/suddenDeath.js';
import { GoonRng } from './core/rng.js';
import { GoonElement, GoonEndReason, GoonMatchPhase, GoonPayloadKind, GoonRoundKind } from './core/contracts.js';
import { local as localCapsOf, UNIVERSAL_ROUND } from './core/caps.js';
import { GoonReceiptStatus } from './core/scoring.js';
import { GoonSession } from './net/session.js';
import { createLoopbackPair, loopbackPresets } from './net/loopbackTransport.js';
import { createMediaQueue } from './net/mediaQueue.js';
import { createBlocklist } from './net/blocklist.js';
import { createReceivedStore } from './exec/receivedStore.js';

import { createRouter } from './ui/router.js';
import { createPrefs } from './ui/prefs.js';
import { createAudio } from './ui/audio.js';
import { createToasts } from './ui/toasts.js';
import { createSheets } from './ui/sheets.js';
import { createOptions } from './ui/options.js';
import { createSoloDriver } from './ui/soloDriver.js';
import { createAssetsStore } from './ui/assetsStore.js';
import { createDiscord, confirmOpenDm } from './ui/discord.js';
import { emitAva, mountVsSplash } from './ui/avatar.js';
import { S } from './ui/strings.js';

import * as titleScreen from './ui/screens/title.js';
import * as hostScreen from './ui/screens/host.js';
import * as joinScreen from './ui/screens/join.js';
import * as lobbyScreen from './ui/screens/lobby.js';
import * as draftScreen from './ui/screens/draft.js';
import * as countdownScreen from './ui/screens/countdown.js';
import * as recapScreen from './ui/screens/recap.js';
import * as assetsScreen from './ui/screens/assets.js';

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
const logger = {
  debug: (m) => { try { console.debug('[gg]', m); } catch (_e) { /* ignore */ } },
  info: (m) => { try { console.info('[gg]', m); } catch (_e) { /* ignore */ } },
  warn: (m) => { try { console.warn('[gg]', m); } catch (_e) { /* ignore */ } bridge.log('warn: ' + m); },
  error: (m) => { try { console.error('[gg]', m); } catch (_e) { /* ignore */ } bridge.log('error: ' + m); },
};

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
  // WHAT A PARTNER SENT US IN AN EARLIER SESSION rides this same frame (spec §6.4)
  // rather than a new boot milestone — settle() already gates on gotManifest, so the
  // received set is primed before any screen mounts, which is what lets the very
  // first offer of a match answer `decline:'have'` instead of re-transferring 20 MB.
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
      router.show('title');
      bridge.log('boot ok');
    });
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
let router = null;
let executor = null;
let matchLog = null;
let assets = null;           // ui/assetsStore.js — owns every cache-* bridge verb
let receivedStore = null;    // exec/receivedStore.js — owns goon-recv-result
let blocklist = null;        // net/blocklist.js
let mediaQueue = null;       // net/mediaQueue.js — one per page, re-attached per match
let artifacts = null;        // the adapter below, over assets' item map
let discord = null;          // ui/discord.js — owns the discord + peer-card verbs
let vsSplash = null;         // the countdown's decorative VS card, if one is up
let ctx = null;

/* ---- match/session state. NEVER cached by a screen; always read through ctx. */
let goonSession = null;      // net/session.js GoonSession (host/join path only)
let currentMatch = null;
let currentTransport = null;
let currentSd = null;        // {presenter, inputs, dispose} from ui/sd
let hudHandle = null;
let mercyHandle = null;
let phaseUnsubs = [];
let escMercied = false;
let awaitingEntry = false;
let lastConnectFailed = null;
let recapFallbackTimer = 0;

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

  let rounds = Array.isArray(caps.rounds) ? caps.rounds.slice() : Object.values(GoonRoundKind);
  // Without a camera there is nothing to win a staring contest with.
  if (!caps.camera) rounds = rounds.filter((r) => r !== GoonRoundKind.StaringContest);
  if (!rounds.includes(UNIVERSAL_ROUND)) rounds.push(UNIVERSAL_ROUND);

  return localCapsOf({ elements, payloads, rounds, platform: 'web' });
}

/* ============================================================================
 * P2P MEDIA TRANSFER — the three singletons and the one adapter.
 *
 * The queue SENDS (premium-gated, session.caps.mediaTransfer), the store RECEIVES
 * (never gated — a free player seeing a supporter's media is the whole product),
 * and the blocklist is the render-time safety gate. All three are built in
 * buildApp() and the queue is attached/detached with the match, so the relay
 * rebuild re-binds it over the new transport and it simply goes dormant.
 * ==========================================================================*/

/** ext -> mime. The cache serves artifacts by extension; the protocol wants a mime. */
const ARTIFACT_MIME = {
  png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif',
  webp: 'image/webp', mp4: 'video/mp4', webm: 'video/webm',
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
        out.push({ sha, bytes, mime, kind: it.kind === 'video' ? 'video' : 'image', exempt });
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

/** Hand artifacts to exec/media.js with a live view factory (real refcounts standalone). */
function registerReceived(list) {
  for (const e of (list || [])) {
    try {
      media.addReceived({
        sha: e.sha, kind: e.kind, mime: e.mime, url: e.url, bytes: e.bytes,
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

  try { executor?.attach?.(match); } catch (e) { logger.error('executor.attach threw: ' + ((e && e.stack) || e)); }
  try { matchLog.attach(match); } catch (e) { logger.error('matchLog.attach threw: ' + ((e && e.stack) || e)); }
  /* THE SECOND tryFirePayload INSTANCE WRAPPER, and the order is the point.
   * matchLog wrapped it a line ago; the queue wraps it now, so the queue's is the
   * OUTERMOST — a payload gets its `xfer:` tags before the log records it, and the
   * log therefore records exactly what went on the wire. Both are instance patches
   * and both die with this match. (Trap register #7 — documented where applied.) */
  try { mediaQueue?.attach?.(match, currentTransport); }
  catch (e) { logger.warn('mediaQueue.attach threw: ' + ((e && e.message) || e)); }
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
  for (const off of phaseUnsubs) { try { off(); } catch (_e) { /* ignore */ } }
  phaseUnsubs = [];
  unmountHud();
  unmountMercy();
  try { executor?.detach?.(); } catch (e) { logger.warn('executor.detach threw: ' + ((e && e.message) || e)); }
  try { matchLog?.detach?.(); } catch (_e) { /* ignore */ }
  // Cancels every transfer and clears the queue; the STORE is untouched, because a
  // committed artifact is hash-keyed and stays valid across matches and sessions.
  try { mediaQueue?.detach?.(); } catch (_e) { /* ignore */ }
  try { currentSd?.dispose?.(); } catch (_e) { /* ignore */ }
  currentSd = null;
  currentMatch = null;
  currentTransport = null;
  paintProbe();
}

function mountHudNow() {
  if (hudHandle || !mountHud || !currentMatch) return;
  try {
    hudHandle = mountHud({
      match: currentMatch, session, audio, prefs, media, matchLog, discord,
    }) || null;
  } catch (e) { logger.error('mountHud threw: ' + ((e && e.stack) || e)); hudHandle = null; }
}

function unmountHud() {
  if (!hudHandle) return;
  try { hudHandle.unmount?.(); } catch (e) { logger.warn('hud.unmount threw: ' + ((e && e.message) || e)); }
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
 * PHASE ROUTING — the engine's phase is the single source of truth for what is
 * on screen. Nothing else calls router.show() for a match screen.
 * -------------------------------------------------------------------------- */
function onPhase(phase) {
  try { document.documentElement.setAttribute('data-gg-phase', String(phase)); } catch (_e) { /* ignore */ }
  paintProbe();

  switch (phase) {
    case GoonMatchPhase.Lobby:
      // The host/join screen stays up until there is a second person to show —
      // a lobby with one silhouette in it is worse than the code the player is
      // still reading aloud.
      if (router.current !== 'host' && router.current !== 'join') router.show('lobby');
      break;

    case GoonMatchPhase.Consent:
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
function ensureSession() {
  if (goonSession) return goonSession;
  goonSession = new GoonSession({
    createMatch: (transport, isHost) => buildMatch(transport, isHost),
    identity: session.identity || {},
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
      if (!soloPair) router.show('title');
    }
  });
  goonSession.onConnectFailed((reason) => {
    lastConnectFailed = reason;
    logger.warn('connect failed: ' + reason);
    if (awaitingEntry) return;      // the entry screen renders it
    sheets?.showSignalError?.(errorInfo(reason)).then(() => router.show('title'));
  });
  return goonSession;
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
}

const actions = {
  goTitle() { router.show('title'); },
  goHost() { router.show('host'); },
  goJoin() { router.show('join'); },
  /** @param {{filter?:string}} [args] e.g. {filter:'needs'} from a "N need compressing" prompt. */
  goAssets(args) { router.show('assets', args || null); },

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
    if (ok) return { ok: true };
    return { ok: false, error: errorInfo(lastConnectFailed) };
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
    router.show('title');
  },
};

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
  /* PRACTICE HAS A FACE TOO — a tile, a name and dm:false (contract §6). It is
   * set AFTER attachMatch, which clears the peer, and it is the only way the
   * splash, the HUD minis and the recap plates are exercisable with no server
   * and no second machine. The bot is not a person: it never gets a DM button. */
  try { discord?.setSoloPeer?.(S.discord.practiceBot); } catch (_e) { /* ignore */ }
  local.adoptLobby();
  soloOpponent.adoptLobby();
  soloDriver.start();

  logger.info('practice: loopback "' + profile + '" latency ' + opts.latencyMs + 'ms skew ' + opts.guestClockSkewMs + 'ms');
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
  audio = createAudio({ prefs, logger });
  toasts = createToasts({ prefs });
  sheets = createSheets({ audio, logger });
  matchLog = createMatchLog();
  // ONE store, built once, and the only thing on the page allowed to register a
  // cache-* handler (bridge.on throws on a duplicate and the assets screen
  // mounts many times per session).
  assets = createAssetsStore({ session, logger });

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
  mediaQueue = createMediaQueue({
    artifacts,
    store: receivedStore,
    blocklist,
    logger,
    // === true, NOT !== false (the idiom brainDrain/spiral use above). Deliberate
    // inversion: sending is a new, Patreon-gated capability, so a host that
    // predates the flag must default it OFF. Receiving is never gated.
    canSend: () => !!(session.caps && session.caps.mediaTransfer === true),
  });
  mediaQueue.onReceived((a) => {
    registerReceived([a]);
    blocklist.check([a.sha]);
  });
  primeReceived(session.received);      // the manifest usually beat us here

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
    /** ui/screens/recap.js: which peer artifacts rendered (and were flagged) this match. */
    getPeerRenders: peerRenderLog,
    getMatch: () => currentMatch,
    getTransport: () => currentTransport,
    getClock: () => { try { return currentTransport ? currentTransport.clock : null; } catch (_e) { return null; } },
    getSd: () => currentSd,
  };

  router = createRouter({
    screens: {
      title: titleScreen,
      host: hostScreen,
      join: joinScreen,
      lobby: lobbyScreen,
      draft: draftScreen,
      countdown: countdownScreen,
      recap: recapScreen,
      assets: assetsScreen,
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
  try {
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('pagehide', () => { try { prefs?.flush?.(); } catch (_e) { /* ignore */ } });
  } catch (_e) { /* ignore */ }
  layers.setFxHeat(0);
  startHeartbeat();
  armDeadline();
  bridge.announceReady();
  bridge.log('boot: ready posted, waiting for init + manifest');
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
      get discord() { return discord; },
      get stubs() { return stubs.slice(); },
      actions,
    };
  } catch (_e) { /* ignore */ }
}
