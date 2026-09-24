/* ============================================================================
 * ui/duel/arcademyHost.js - runs a REAL Arcademy class inside the Goon window.
 *
 * Owner, 2026-09-23: "I want the actual game in particular: The grid with the
 * real tiles and real effects from the Deep End game - for the sort the card
 * swiping ui, etc - everything directly in that window as is on the arcademy".
 *
 * So this is a small shell. It mirrors what arcademy/shell/shell.js startClass
 * builds for a class (the ctx contract, CLAUDE.md section 5) and reuses the
 * Arcademy's own modules for every piece that has one:
 *
 *   engine      arcademy/engine/index.js createEngine  (the real effects)
 *   ceremonies  arcademy/shell/ceremonies.js           (the real stamps)
 *   assets      arcademy/provider/index.js createAssets, fed the Goon deck
 *               (exec/media.js: host preset + local picks + the online
 *               Scrolller set) as its local inventory; offline, no bridge
 *   keys        arcademy/shell/keybinds.js             (the class's own verbs)
 *   sound       arcademy/shell/audio.js createAudio    (its synth, standalone)
 *   styles      the class's own style.js injects itself; the shell sheet it
 *               leans on is fenced under .gg-arc by ui/duel/arcCss.js
 *
 * and stubs only what a duel must NOT do:
 *
 *   store       IN MEMORY for this duel. A duel never writes the player's
 *               Arcademy meta, records or XP (no bridge, no class-started,
 *               no class-ended: the host never hears a duel happened).
 *   lexicon     answers the class's own fallback (each class ships lex.js)
 *   mood / emi  inert (no mascot on the Goon page)
 *
 * The class is started on the DUEL SEED with the DUEL LENGTH as its budget, so
 * both machines deal the same board and the class's own bell is the duel bell.
 * The duel clock still wins: the controller asks result() when its clock runs
 * out whether or not the class has rung, then destroy() tears everything down
 * (engine effects stopped, listeners removed, media released, <html> classes
 * taken back off).
 *
 * The goon page is served from https://ccp.game/goon/ and ccp.game maps the
 * whole Resources/web folder, so every arcademy path here is same-origin. All
 * imports are DYNAMIC: a build that serves the goon folder alone (no arcademy
 * beside it) fails here, cleanly, and the duel reports a zero instead of the
 * page failing to boot.
 * ==========================================================================*/

import { duelGame } from './games.js';
import { scopeArcCss } from './arcCss.js';
import { createPilePool } from './piles.js';

const ARC = '../../../arcademy/';
const STYLE_ID = 'gg-arc-shell-style';
export const ARC_SCOPE_CLASS = 'gg-arc';

const url = (p) => new URL(p, import.meta.url).href;

let styleOnce = null;
/** Fetch arcademy/styles.css once and inject the fenced copy. Never throws. */
export function ensureArcShellStyle(d = typeof document !== 'undefined' ? document : null) {
  if (!d || !d.head) return Promise.resolve(false);
  if (d.getElementById(STYLE_ID)) return Promise.resolve(true);
  if (styleOnce) return styleOnce;
  const href = url(ARC + 'styles.css');
  styleOnce = (async () => {
    try {
      const res = await fetch(href);
      if (!res || !res.ok) return false;
      const css = scopeArcCss(await res.text(), '.' + ARC_SCOPE_CLASS, href);
      if (d.getElementById(STYLE_ID)) return true;
      const s = d.createElement('style');
      s.id = STYLE_ID;
      s.textContent = css;
      d.head.appendChild(s);
      return true;
    } catch (_e) { styleOnce = null; return false; }
  })();
  return styleOnce;
}

/** The Goon deck as provider manifest rows. Pure. */
export function manifestFromDeck(rows) {
  const out = [];
  for (const r of rows || []) {
    if (!r || !r.url) continue;
    const u = String(r.url);
    if (r.kind === 'video') { out.push({ url: u, kind: 'loop' }); continue; }
    // A blob: url has no extension to sniff; an image with one is the provider's to classify.
    if (/^blob:/i.test(u)) { out.push({ url: u, kind: 'still' }); continue; }
    out.push(u);
  }
  return out;
}

/** In-memory game meta: the class reads and writes it, nothing leaves the duel. */
export function memoryStore() {
  const meta = Object.create(null);
  return {
    gameMeta(key) { return Object.assign({}, meta[key] || {}); },
    mergeGameMeta(key, patch) { meta[key] = Object.assign({}, meta[key] || {}, patch || {}); return meta[key]; },
    get() { return undefined; },
    set() { /* a duel keeps nothing */ },
  };
}

const INERT_MOOD = Object.freeze({
  tense() {}, calm() {}, clutch() {}, stumble() {}, runLost() {}, note() {}, hold() {}, askHelp() { return false; },
});

function engineHandleFor(engine, manifest, say) {
  const allowed = new Set(Array.isArray(manifest && manifest.effectsConsumed) ? manifest.effectsConsumed : []);
  const guarded = (verb) => (kind, opts) => {
    if (!allowed.has(kind)) return false;
    try { return engine[verb](kind, opts); } catch (e) { say('engine.' + verb + ' threw: ' + ((e && e.message) || e)); return false; }
  };
  const safe = (name) => (...args) => {
    try { return engine[name] ? engine[name].apply(engine, args) : undefined; } catch (_e) { return undefined; }
  };
  const h = { fire: guarded('fire'), sustain: guarded('sustain') };
  for (const n of ['setHeat', 'stop', 'setpiece', 'beat', 'ceremony', 'setPhase', 'armTail', 'rewardRoll',
    'isPlainBeat', 'plainShare', 'cadenceMs', 'channels', 'diagnostics', 'deadBeat', 'pick', 'prewarm']) h[n] = safe(n);
  return h;
}

async function load(p, say) {
  try { return await import(url(p)); } catch (e) { say('import ' + p + ' failed: ' + ((e && e.message) || e)); return null; }
}

/**
 * Mount a real Arcademy class.
 * @param {object} o
 * @param {HTMLElement} o.root       the class root (the class builds its stage in here)
 * @param {HTMLElement} o.fxLayer    fixed layer the engine paints into
 * @param {HTMLElement} o.ceremonyLayer fixed layer for stamps
 * @param {string} o.game            ui/duel/games.js id
 * @param {string} o.seed            the duel seed, as a string
 * @param {number} o.lenSec          the duel length (the class's budget)
 * @param {object} [o.media]         exec/media.js pool (list())
 * @param {boolean} [o.reduced]
 * @param {object} [o.volume]        {level(): 0..1, subscribe(fn): unsubscribe} the Goon mix the
 *                                   class synth follows (master x game). Absent = the old fixed mix.
 * @param {object} [o.noise]         Sort duel only: {rows(): Goon rows} the player's NOISE board. Present =
 *                                   Sort deals two tagged piles (niche right, noise left) through its
 *                                   claimTagged seam instead of the QUICK SORT floor (moving vs still).
 * @param {Function} [o.onEnd]       (report) the class rang its own bell / ended early
 * @param {Function} [o.log]
 * @returns {Promise<{game, result(): object, destroy(): void, instance, ctx}|null>}
 */
/**
 * The pictures a duel's class plays with: the online flavour's Scrolller pool when there is
 * one (owner, 2026-09-24: the Sort duel never runs on a player's own files, it runs on the
 * zero-setup pool), and the whole deck only when no online pool exists. Pure over the pool.
 */
export function duelRows(media) {
  try {
    const online = media && typeof media.listOnline === 'function' ? media.listOnline() : [];
    if (online && online.length) return online;
    return media && typeof media.list === 'function' ? media.list() : [];
  } catch (_e) { return []; }
}

/** How long a Sort duel's claim waits for a noise board that is still landing. */
export const NOISE_WAIT_MS = 1500;

function safeRows(noise) {
  try { const r = noise.rows(); return Array.isArray(r) ? r : []; } catch (_e) { return []; }
}

export async function mountArcademyGame({
  root, fxLayer = null, ceremonyLayer = null, game, seed, lenSec = 60, media = null,
  reduced = false, onEnd = null, log = null, volume = null, noise = null,
} = {}) {
  const lines = [];         // the last few log lines, for a driver or a bug report
  const say = (m) => {
    const line = '[arcduel] ' + m;
    lines.push(line); if (lines.length > 200) lines.shift();
    try { if (typeof log === 'function') log(line); } catch (_e) { /* never */ }
  };
  const row = duelGame(game);
  if (!row || !root) { say('no such game ' + game); return null; }

  const [modNs, engNs, provNs, cerNs, keyNs, audNs, rngNs, libNs] = await Promise.all([
    load(row.path, say),
    load(ARC + 'engine/index.js', say),
    load(ARC + 'provider/index.js', say),
    load(ARC + 'shell/ceremonies.js', say),
    load(ARC + 'shell/keybinds.js', say),
    load(ARC + 'shell/audio.js', say),
    load(ARC + 'core/rng.js', say),
    row.lib ? load(row.lib, say) : Promise.resolve(null),
  ]);
  const mod = modNs && modNs.default;
  if (!mod || typeof mod.create !== 'function' || !rngNs) { say('class module unavailable'); return null; }
  await ensureArcShellStyle();

  const key = mod.key || row.id;
  const manifest = mod.manifest || {};
  const makeRng = rngNs.makeRng;
  const platform = {
    isTouch: (() => { try { return !!(typeof matchMedia === 'function' && matchMedia('(pointer: coarse)').matches); } catch (_e) { return false; } })(),
    hasHaptics: false,
    host: 'desktop',
  };
  const motionLevel = reduced ? 0 : 2;
  const cleanups = [];
  const guard = (fn) => { try { fn(); } catch (_e) { /* teardown never throws */ } };

  /* --- media: the Goon deck, as the provider's local inventory --- */
  let assets = null;
  let rows = [];
  try {
    rows = duelRows(media);
    assets = provNs && provNs.createAssets({
      bridge: null, remoteMediaEnabled: false, remoteMediaRatio: 0, offlineMode: true,
      platform, localManifest: manifestFromDeck(rows),
      rng: makeRng(seed + '|assets'), log: (m) => say('[assets] ' + m),
    });
  } catch (e) { say('assets refused: ' + ((e && e.message) || e)); assets = null; }
  if (assets && typeof assets.dispose === 'function') cleanups.push(() => assets.dispose());

  /* --- the Sort duel's two piles (2026-09-25): the niche is YOURS, the noise board is the rest.
   * The class asks `claimTagged` exactly as its setup door would; the provider underneath is
   * untouched and still serves every other claim. */
  const piled = !!(noise && typeof noise.rows === 'function' && row.id === 'sort');
  if (piled) {
    const base = assets;
    const nicheRows = rows;
    const rand = makeRng(seed + '|piles');
    const tagged = {
      async claimTagged() {
        // A board that is still landing gets a short grace, never the whole bell.
        const until = Date.now() + NOISE_WAIT_MS;
        while (!(safeRows(noise).length) && Date.now() < until) await new Promise((r) => setTimeout(r, 150));
        const pool = createPilePool({ target: nicheRows, noise: () => safeRows(noise), rand });
        say('piles: ' + pool.counts().target.rows + ' niche / ' + pool.counts().noise.rows + ' noise');
        return pool;
      },
    };
    assets = base ? Object.assign(Object.create(base), tagged) : tagged;
  }

  /* --- the real effects engine --- */
  let engine = null;
  try {
    engine = engNs && engNs.createEngine({
      mount: fxLayer || root, caps: {}, masterIntensity: 1, effectIntensity: 0.85,
      rng: makeRng(seed + '|engine'), words: [], wordAudio: {}, assets,
      motionLevel, reducedMotion: !!reduced, spiralUrl: () => null, bus: null, seep: null,
    });
  } catch (e) { say('engine refused: ' + ((e && e.message) || e)); engine = null; }
  if (engine && typeof engine.dispose === 'function') cleanups.push(() => engine.dispose());

  /* --- sound: the Arcademy synth, while this class is up --- */
  /* THE GOON VOLUME (2026-09-24). The class synth is its own AudioContext, so the Goon mix
   * never reached it. The Arcademy audio already takes masterVolume / audioMute in its init
   * and through its own `setting` echo (onSetting), so this is only wiring: no arcademy file
   * changes. Zero is a real mute, which also cuts any clip the synth fell back to. */
  const level = () => {
    if (!volume || typeof volume.level !== 'function') return 0.8;
    try { const v = Number(volume.level()); return Number.isFinite(v) ? Math.max(0, Math.min(1, v)) : 0.8; } catch (_e) { return 0.8; }
  };
  let audio = null;
  try {
    const v0 = level();
    audio = audNs && audNs.createAudio({
      init: { audioLevels: { fx: 0.7, voice: 0.6, tutorial: 0, drops: 0.6, music: 0.4 }, masterVolume: v0, audioMute: v0 <= 0 },
      bridge: null, log: say, autoplayOk: true,
    });
  } catch (_e) { audio = null; }
  if (audio) cleanups.push(() => audio.destroy());
  if (audio && typeof audio.onSetting === 'function' && volume && typeof volume.subscribe === 'function') {
    let muted = level() <= 0;
    let off = null;
    try {
      off = volume.subscribe(() => {
        const v = level();
        guard(() => audio.onSetting({ key: 'masterVolume', value: v }));
        if ((v <= 0) !== muted) { muted = v <= 0; guard(() => audio.onSetting({ key: 'audioMute', value: muted })); }
      });
    } catch (_e) { off = null; }
    if (typeof off === 'function') cleanups.push(off);
  }

  let ceremonies = null;
  try {
    ceremonies = cerNs && cerNs.createCeremonies({
      engine, layer: ceremonyLayer, reducedMotion: !!reduced, confetti: () => false, log: say,
    });
  } catch (_e) { ceremonies = null; }
  if (ceremonies) cleanups.push(() => ceremonies.destroy());

  let keys = { on() { return () => {}; }, keyFor() { return null; }, labelFor() { return ''; }, panicKey: null, destroy() {} };
  try {
    const kb = keyNs && keyNs.createKeybinds({ init: {}, bridge: null, log: say });
    if (kb) {
      kb.declare(key, manifest.keybinds);
      keys = kb.runtime(key, typeof window !== 'undefined' ? window : null);
    }
  } catch (_e) { /* the class still binds its own pointer */ }
  cleanups.push(() => keys.destroy());

  let ended = false;
  let report = null;
  const ctx = {
    root,
    engine: engine ? engineHandleFor(engine, manifest, say) : null,
    assets,
    lexicon: (_k, fallback) => fallback,
    caps: {},
    rng: makeRng(seed),
    settings: {},
    keys,
    peek: { add() {}, show() {}, hide() {}, destroy() {} },
    ceremonies,
    store: memoryStore(),
    mood: INERT_MOOD,
    platform,
    motion: { reducedMotion: !!reduced, motionLevel },
    audioAudible: true,
    hideTutorial: true,
    words: [],
    absorb() {},
    sessionWords: [],
    triggers: [],
    spiralPool: [],
    classSpiral: null,
    exits: {
      sign: (btn) => btn,
      bar: (children) => {
        const b = document.createElement('div');
        b.className = 'arc-exitbar';
        for (const c of children || []) if (c) b.appendChild(c);
        return b;
      },
    },
    log: (m) => say('[' + key + '] ' + m),
    endClass: (r) => {
      if (ended) return;
      ended = true;
      report = r || null;
      try { if (typeof onEnd === 'function') onEnd(report); } catch (_e) { /* the duel reads result() */ }
    },
  };

  root.classList.add(ARC_SCOPE_CLASS);
  let instance = null;
  try {
    instance = mod.create(ctx);
    // The setup door (Sort's pile picker) is skipped on purpose: a duel is played on the deck the
    // player already has, and a class with no door claims its own floor (Sort: QUICK SORT).
    const spec = { gradeTier: 1, seed: String(seed), timeBudgetSec: Math.max(20, lenSec | 0), retake: false };
    if (piled) spec.sources = ['niche', 'noise'];
    instance.start(spec);
  } catch (e) {
    say(key + ' failed to start: ' + ((e && e.message) || e));
    guard(() => { if (instance) instance.destroy(); });
    for (const c of cleanups.reverse()) guard(c);
    return null;
  }
  say(key + ' mounted, seed ' + seed + ', ' + lenSec + 's');

  let dead = false;
  return {
    game: row.id,
    instance,
    ctx,
    get ended() { return ended; },
    /** The class's recent log lines (diagnostics). */
    log: () => lines.slice(),
    /** The class's own result right now: {game, score, tile?}. */
    result() {
      let r = null;
      try { r = row.result(instance, { report, lib: libNs }); } catch (e) { say('result read failed: ' + ((e && e.message) || e)); }
      const out = { game: row.id, score: Math.max(0, Math.round(Number(r && r.score) || 0)) };
      if (row.tiled) out.tile = Math.max(0, Math.round(Number(r && r.tile) || 0));
      return out;
    },
    destroy() {
      if (dead) return;
      dead = true;
      guard(() => instance.destroy());
      for (const c of cleanups.reverse()) guard(c);
      guard(() => { if (typeof document !== 'undefined') document.dispatchEvent(new CustomEvent('arcademy-sfx', { detail: { name: 'stop_clips' } })); });
      guard(() => { root.textContent = ''; root.classList.remove(ARC_SCOPE_CLASS); });
      guard(() => { if (fxLayer) fxLayer.textContent = ''; });
      guard(() => { if (ceremonyLayer) ceremonyLayer.textContent = ''; });
      guard(() => {
        const h = document.documentElement;
        for (const c of ['ae-lite', 'ae-touch']) h.classList.remove(c);
      });
      say(key + ' torn down');
    },
  };
}

export default mountArcademyGame;
