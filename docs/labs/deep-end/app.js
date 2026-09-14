/* ============================================================================
 * labs/deep-end/app.js - THE DEEP END, web teaser harness.
 *
 * Stands the Arcademy's 2048 class up in a plain browser page: the REAL game
 * module, the REAL Distraction Engine, the REAL WebAudio cue synth and the
 * shell's peek/ceremony verbs, with the shell's ctx stubbed the way the dev
 * rig stubs it (meta in localStorage, no host bridge, no XP, no timetable).
 *
 * Media: labs/deep-end/scrolller.js (the browser fetches Scrolller directly -
 * never our servers). Spirals: four of DTRH's bundled files under ./spirals.
 *
 * Unlisted: no link points here and the page carries noindex. Testers only.
 * ==========================================================================*/
import game from './arc/games/the-deep-end/index.js';
import { createEngine } from './arc/engine/index.js';
import { createPeek } from './arc/shell/peek.js';
import { createCeremonies } from './arc/shell/ceremonies.js';
import { createAudio } from './arc/shell/audio.js';
import { makeRng } from './arc/core/rng.js';
import { letterFor } from './arc/core/grades.js';
import { NICHES, probeSub, createScrolllerAssets, createPlaceholderAssets } from './scrolller.js';

const qs = new URLSearchParams(location.search);
const $ = (id) => document.getElementById(id);
const LS = { consent: 'de-teaser:consent', prefs: 'de-teaser:prefs', meta: 'de-teaser:meta' };
const DEV = qs.get('dev') === '1';

/* ---- tiny log (console + the hidden pane for headless dumps) -------------- */
const logEl = $('delog');
const lines = [];
const t0 = Date.now();
function log(msg) {
  const line = '+' + String(Date.now() - t0).padStart(6, ' ') + 'ms ' + msg;
  lines.push(line); if (lines.length > 300) lines.shift();
  if (logEl) logEl.textContent = lines.join('\n');
  if (DEV) console.log(line);
}
window.__de = { lines };
document.addEventListener('arcademy-log', (e) => { const d = (e && e.detail) || {}; log('[engine] ' + (d.msg == null ? JSON.stringify(d) : d.msg)); });
document.addEventListener('arcademy-fx', (e) => { const d = (e && e.detail) || {}; log('[fx] ' + d.kind + (d.variant ? ' (' + d.variant + ')' : '')); });

/* ---- prefs ----------------------------------------------------------------- */
/* customSubs: probed, user-added subs - [{name, ok, videoCount, checkedAt}], cap 20 */
const DEFAULT_PREFS = { niche: 'hypno', tier: 2, size: 4, mode: 'class', faces: 'media', perf: 'auto', sound: true, volume: 0.8, customSubs: [] };
const MAX_CUSTOM_SUBS = 20;
function loadPrefs() {
  let p;
  try { p = Object.assign({}, DEFAULT_PREFS, JSON.parse(localStorage.getItem(LS.prefs) || '{}')); } catch (e) { p = Object.assign({}, DEFAULT_PREFS); }
  // never hand out DEFAULT_PREFS' own array, and never trust stored shapes
  p.customSubs = (Array.isArray(p.customSubs) ? p.customSubs : [])
    .filter((s) => s && typeof s.name === 'string' && /^[A-Za-z0-9_]{2,40}$/.test(s.name))
    .map((s) => ({ name: s.name, ok: !!s.ok, videoCount: Number(s.videoCount) || 0, checkedAt: Number(s.checkedAt) || 0 }))
    .slice(0, MAX_CUSTOM_SUBS);
  return p;
}
function savePrefs(p) { try { localStorage.setItem(LS.prefs, JSON.stringify(p)); } catch (e) { /* private mode */ } }
const prefs = loadPrefs();
if (qs.get('tier')) prefs.tier = Math.max(1, Math.min(4, Number(qs.get('tier')) || prefs.tier));
if (qs.get('size')) prefs.size = qs.get('size') === '5' ? 5 : 4;
if (qs.get('niche')) prefs.niche = qs.get('niche');
if (qs.get('endless') === '1') prefs.mode = 'free';
if (qs.get('faces')) prefs.faces = qs.get('faces');
if (qs.get('perf')) prefs.perf = qs.get('perf');

/* ---- meta store (localStorage, the rig's shape) --------------------------- */
function readMeta() { try { return JSON.parse(localStorage.getItem(LS.meta) || '{}'); } catch (e) { return {}; } }
const store = {
  gameMeta(key) { return readMeta()[key] || {}; },
  mergeGameMeta(key, patch) {
    const all = readMeta();
    all[key] = Object.assign({}, all[key] || {}, patch);
    try { localStorage.setItem(LS.meta, JSON.stringify(all)); } catch (e) { /* ignore */ }
  },
};

/* ---- audio: the shell's synth, levels moved through a fake 'setting' echo -- */
const settingSubs = new Set();
const fakeBridge = { on(type, fn) { if (type === 'setting' && typeof fn === 'function') { settingSubs.add(fn); return () => settingSubs.delete(fn); } return () => {}; } };
let audio = null;
try {
  audio = createAudio({
    init: { audioLevels: { fx: 0.55, voice: 0.85, tutorial: 0.85, drops: 0.4, music: 1 }, audioMute: !prefs.sound, masterVolume: prefs.volume },
    bridge: fakeBridge, log,
  });
} catch (e) { log('audio unavailable: ' + ((e && e.message) || e)); }
function echoSetting(key, value) { for (const fn of settingSubs) { try { fn({ key, value }); } catch (e) { /* ignore */ } } }

/* ---- spirals (DTRH's bundled files; the light ones weighted up) ---------- */
const SPIRAL_POOL = [['sp6.gif', 5], ['sp7.gif', 4], ['sp1.gif', 2], ['sp3.gif', 2]];
function pickSpiralUrl(seed) {
  const r = makeRng(seed + '|spiral');
  const total = SPIRAL_POOL.reduce((a, x) => a + x[1], 0);
  let t = r() * total;
  for (const pair of SPIRAL_POOL) { t -= pair[1]; if (t <= 0) return new URL('./spirals/' + pair[0], import.meta.url).href; }
  return new URL('./spirals/sp6.gif', import.meta.url).href;
}

/* ---- words: the sub flash pool (neutral; the app uses the day's pool) ---- */
const WORDS = ['sink', 'drift', 'soft', 'heavy', 'deeper', 'slow', 'quiet', 'good', 'float', 'blank', 'let go', 'yes'];

/* ---- the lobby ------------------------------------------------------------- */
let assets = null;            // the provider for the CURRENT niche
let assetsNiche = null;
let statusTimer = 0;

function nicheOf(key) { return NICHES.find((n) => n.key === key) || null; }
/** The custom subs that actually answered a probe - the only ones we fetch. */
function verifiedSubs() { return (prefs.customSubs || []).filter((s) => s.ok).map((s) => s.name); }
/** Remote media is on when a preset OR a verified custom sub is in play. */
function usesRemote() { return !!nicheOf(prefs.niche) || verifiedSubs().length > 0; }

function ensureAssets() {
  const extra = verifiedSubs();
  // the provider snapshots its subs at construction, so the custom subs belong
  // in the cache key too: adding or removing one rebuilds it like a niche switch
  const key = prefs.niche + '|' + extra.join(',');
  if (assets && assetsNiche === key) return assets;
  if (assets) { try { assets.dispose(); } catch (e) { /* ignore */ } }
  assetsNiche = key;
  const n = nicheOf(prefs.niche);
  const subs = (n ? n.subs : []).concat(extra);
  assets = subs.length ? createScrolllerAssets({ subs, rng: Math.random, log: (m) => log('[media] ' + m) }) : createPlaceholderAssets({});
  return assets;
}
function warmAssets() {
  const a = ensureAssets();
  try { a.warm(); } catch (e) { /* ignore */ }
  paintStatus();
  if (!statusTimer) statusTimer = setInterval(paintStatus, 700);
}
function paintStatus() {
  const el = $('mediastat');
  if (!el || !assets) return;
  const s = assets.stats();
  if (!usesRemote()) { el.textContent = 'no remote media: the tiles wear the bundled placeholder faces'; el.className = 'stat'; return; }
  const got = s.loop + s.still;
  if (!s.warmed) { el.textContent = 'media: idle'; el.className = 'stat'; return; }
  if (got === 0 && !s.done) { el.textContent = 'media: reaching scrolller.com…'; el.className = 'stat busy'; return; }
  if (got === 0 && s.done) { el.textContent = 'media: nothing came back (' + (s.dead ? 'dead sub' : 'network?') + ') - placeholder faces'; el.className = 'stat bad'; return; }
  // every stream spent and a thin pool: say so instead of quietly recycling
  const spent = s.done && s.loop > 0 && s.loop < 40 ? ' · all ' + s.loop + ' clips seen, reshuffling' : '';
  el.textContent = 'media: ' + s.loop + ' loops · ' + s.still + ' stills' + (s.done ? ' · ready' : ' · loading…') + spent;
  el.className = 'stat ' + (s.done ? 'ok' : 'busy');
}

/* ---- B2: the sub list behind the selected preset (grey, collapsed) ------- */
let subsOpen = false;
function paintNicheSubs() {
  const el = $('nichesubs');
  if (!el) return;
  const n = nicheOf(prefs.niche);
  if (!n || !n.subs.length) { el.hidden = true; el.textContent = ''; return; }
  el.hidden = false;
  el.textContent = subsOpen
    ? '▾ ' + n.subs.map((s) => 'r/' + s).join(' · ')
    : '▸ ' + n.label + ' · ' + n.subs.length + ' sub' + (n.subs.length === 1 ? '' : 's');
  el.title = subsOpen ? 'hide the subs behind this preset' : 'show the subs behind this preset';
  el.onclick = () => { subsOpen = !subsOpen; paintNicheSubs(); };
}

/* ---- B3: custom subs, verified against Scrolller before they count ------- */
let pendingSub = null;          // the name currently being probed
function subNote(msg, bad) {
  const el = $('customstat');
  if (!el) return;
  el.hidden = !msg;
  el.textContent = msg || '';
  el.className = 'stat' + (msg && bad ? ' bad' : '');
}
/** '  https://reddit.com/r/GoneWild/ ' -> 'GoneWild'; null if it can't be one. */
function sanitizeSub(raw) {
  let s = String(raw == null ? '' : raw).trim();
  s = s.replace(/^(?:https?:)?\/\//i, '').replace(/^[^/\s]*\.[a-z]{2,}\//i, '');
  s = s.replace(/^\/+/, '').replace(/^(?:r\/)+/i, '');
  s = s.split(/[/?#\s]/)[0];
  return /^[A-Za-z0-9_]{2,40}$/.test(s) ? s : null;
}
function paintCustomSubs() {
  const row = $('customsubs');
  if (!row) return;
  const list = prefs.customSubs || [];
  row.replaceChildren();
  row.hidden = !list.length && !pendingSub;
  for (const s of list) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'chip verified';
    b.textContent = 'r/' + s.name + ' ✕';
    b.title = 'verified · ' + (s.videoCount | 0) + ' clips';
    b.onclick = () => {
      prefs.customSubs = list.filter((x) => x !== s);
      savePrefs(prefs);
      subNote('');
      paintCustomSubs();
      warmAssets();
    };
    row.appendChild(b);
  }
  if (pendingSub) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'chip pending';
    b.disabled = true;
    b.textContent = 'r/' + pendingSub + ' …';
    row.appendChild(b);
  }
}
async function addCustomSub() {
  if (pendingSub) return;
  const input = $('customsub');
  const name = sanitizeSub(input && input.value);
  if (!name) { subNote('a subreddit name is 2-40 letters, numbers or _', true); return; }
  const list = prefs.customSubs || (prefs.customSubs = []);
  if (list.some((x) => x.name.toLowerCase() === name.toLowerCase())) { subNote('r/' + name + ' is already on the list', true); return; }
  if (list.length >= MAX_CUSTOM_SUBS) { subNote(MAX_CUSTOM_SUBS + ' custom subs is the cap', true); return; }
  if (input) input.value = '';
  subNote('checking r/' + name + ' on scrolller.com…');
  pendingSub = name;
  paintCustomSubs();
  let verdict = null;
  try { verdict = await probeSub(name); } catch (e) { verdict = { ok: false, error: 'offline' }; }
  pendingSub = null;
  if (verdict && verdict.ok) {
    list.push({ name, ok: true, videoCount: Number(verdict.videoCount) || 0, checkedAt: Date.now() });
    savePrefs(prefs);
    subNote('');
    paintCustomSubs();
    warmAssets();               // rebuilds the provider over the new sub list
    return;
  }
  paintCustomSubs();            // the pending pill drops; nothing was persisted
  subNote(verdict && verdict.error ? 'Couldn\'t reach Scrolller - try again' : 'r/' + name + ' isn\'t on Scrolller', true);
}

function paintLobby() {
  const wrap = $('niches');
  wrap.replaceChildren();
  const all = NICHES.concat([{ key: 'none', label: 'No remote media', subs: [] }]);
  for (const n of all) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'chip' + (prefs.niche === n.key ? ' on' : '');
    b.textContent = n.label;
    b.onclick = () => { prefs.niche = n.key; savePrefs(prefs); paintLobby(); warmAssets(); };
    wrap.appendChild(b);
  }
  paintNicheSubs();
  paintCustomSubs();
  for (const [id, key, vals] of [['tier', 'tier', [1, 2, 3, 4]], ['size', 'size', [4, 5]], ['mode', 'mode', ['class', 'free']], ['faces', 'faces', ['media', 'still', 'plain']], ['perf', 'perf', ['auto', 'full', 'lite']]]) {
    const box = $(id);
    if (!box) continue;
    box.replaceChildren();
    for (const v of vals) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'seg' + (String(prefs[key]) === String(v) ? ' on' : '');
      b.textContent = ({ tier: (x) => 'Year ' + x, size: (x) => x + '×' + x, mode: (x) => (x === 'class' ? 'Class · 5 min' : 'Free swim'), faces: (x) => ({ media: 'Live', still: 'Stills', plain: 'Plain' })[x], perf: (x) => ({ auto: 'Auto', full: 'Full', lite: 'Lite' })[x] })[key](v);
      b.onclick = () => { prefs[key] = v; savePrefs(prefs); paintLobby(); };
      box.appendChild(b);
    }
  }
  $('sound').checked = !!prefs.sound;
  $('volume').value = String(Math.round(prefs.volume * 100));
}

$('customsub-add').onclick = () => { addCustomSub(); };
$('customsub').onkeydown = (e) => { if (e.key === 'Enter') { e.preventDefault(); addCustomSub(); } };

$('sound').onchange = (e) => { prefs.sound = !!e.target.checked; savePrefs(prefs); echoSetting('audioMute', !prefs.sound); };
$('volume').oninput = (e) => { prefs.volume = Math.max(0, Math.min(1, Number(e.target.value) / 100)); savePrefs(prefs); echoSetting('masterVolume', prefs.volume); };

/* ---- the gate -------------------------------------------------------------- */
function consented() { try { return localStorage.getItem(LS.consent) === '1'; } catch (e) { return false; } }
function showScreen(id) {
  for (const s of ['gate', 'lobby', 'stage', 'result']) { const el = $(s); if (el) el.hidden = (s !== id); }
}
$('gate-yes').onclick = () => { try { localStorage.setItem(LS.consent, '1'); } catch (e) { /* ignore */ } showScreen('lobby'); warmAssets(); };
$('gate-no').onclick = () => { location.href = 'https://cclabs.app/'; };

/* ---- the class ------------------------------------------------------------- */
let handle = null;
let peek = null;
let ceremonies = null;
let engineImpl = null;
let seed = qs.get('seed') || null;
let lastSpec = null;
let paused = false;
let running = false;
let devDeepest = 0;
{
  const d = Number(qs.get('deep'));
  if (DEV && Number.isFinite(d) && d >= 2 && d <= 11) devDeepest = d | 0;
}
const reduced = (() => { try { return qs.get('reduced') === '1' || window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch (e) { return false; } })();

/** The shell's manifest fence: an undeclared kind is refused, logged once. */
function guardEngine(impl, manifest) {
  const allowed = new Set(Array.isArray(manifest && manifest.effectsConsumed) ? manifest.effectsConsumed : []);
  const warned = new Set();
  const refuse = (verb, kind) => { const id = verb + ':' + kind; if (!warned.has(id)) { warned.add(id); log('REFUSED engine.' + verb + '(' + kind + ')'); } return false; };
  const guarded = (verb, fn) => (kind, o) => { if (!allowed.has(kind)) return refuse(verb, kind); try { return fn(kind, o); } catch (e) { log('THREW engine.' + verb + '(' + kind + '): ' + ((e && e.message) || e)); return false; } };
  const safe = (name) => (...args) => { try { return impl[name] ? impl[name].apply(impl, args) : undefined; } catch (e) { log('THREW engine.' + name + ': ' + ((e && e.message) || e)); return undefined; } };
  return {
    setHeat: safe('setHeat'), fire: guarded('fire', (k, o) => impl.fire(k, o)), sustain: guarded('sustain', (k, o) => impl.sustain(k, o)),
    stop: safe('stop'), setpiece: safe('setpiece'), beat: safe('beat'), ceremony: safe('ceremony'), setPhase: safe('setPhase'),
    armTail: safe('armTail'), rewardRoll: safe('rewardRoll'), isPlainBeat: safe('isPlainBeat'), plainShare: safe('plainShare'),
    cadenceMs: safe('cadenceMs'), channels: safe('channels'), diagnostics: safe('diagnostics'),
  };
}

function teardown() {
  running = false; paused = false;
  $('pause').hidden = true;
  if (handle) { try { handle.destroy(); } catch (e) { /* ignore */ } handle = null; }
  if (peek) { try { peek.destroy(); } catch (e) { /* ignore */ } peek = null; }
  if (ceremonies) { try { ceremonies.destroy(); } catch (e) { /* ignore */ } ceremonies = null; }
  if (engineImpl) { try { engineImpl.dispose(); } catch (e) { /* ignore */ } engineImpl = null; }
  $('mount').replaceChildren();
}

/** The first media usually lands ~1.5s after warm(); a tester who slams DIVE
 *  before that would dive into grey tiles, so hold the door a moment. */
function firstMedia(a, maxMs) {
  return new Promise((resolve) => {
    const s0 = a.stats();
    if (s0.done || (s0.loop + s0.still) > 0) { resolve(); return; }
    let off = () => {};
    const t = setTimeout(() => { off(); resolve(); }, maxMs);
    off = a.onUpdate(() => { const s = a.stats(); if (s.done || (s.loop + s.still) > 0) { clearTimeout(t); off(); resolve(); } });
  });
}

async function dive(opts = {}) {
  teardown();
  const a = ensureAssets();
  try { a.warm(); } catch (e) { /* ignore */ }
  const btn = $('dive');
  if (usesRemote()) {
    const s = a.stats();
    if (!s.done && (s.loop + s.still) === 0) {
      if (btn) { btn.disabled = true; btn.textContent = 'reaching scrolller.com…'; }
      await firstMedia(a, 4500);
      if (btn) { btn.disabled = false; btn.textContent = 'DIVE IN'; }
    }
  }
  if (!opts.sameSeed || !seed) seed = qs.get('seed') && !lastSpec ? qs.get('seed') : ('web-' + Math.random().toString(36).slice(2, 8));
  const endless = prefs.mode === 'free';
  const mount = $('mount');
  const root = document.createElement('div');
  root.style.cssText = 'position:absolute;inset:0;';
  mount.appendChild(root);
  const fxLayer = document.createElement('div');
  fxLayer.style.cssText = 'position:absolute;inset:0;pointer-events:none;z-index:40;';
  mount.appendChild(fxLayer);

  const caps = { bgIntensity: 1, subDensity: 1, flashRate: 1 };
  const spiral = pickSpiralUrl(seed);
  try {
    engineImpl = createEngine({
      mount: fxLayer, caps, masterIntensity: 1,
      effectIntensity: 0.85,                      // the shell's shipped value
      rng: makeRng(seed + '|engine'), words: WORDS.slice(), assets: a,
      motionLevel: reduced ? 0 : 2, reducedMotion: reduced,
      spiralUrl: () => spiral, bus: null, seed, gameKey: 'the-deep-end',
    });
  } catch (e) { log('createEngine threw: ' + ((e && e.message) || e)); engineImpl = null; }
  const engine = engineImpl ? guardEngine(engineImpl, game.manifest || {}) : guardEngine({}, {});
  peek = createPeek({ log });
  ceremonies = createCeremonies({ engine: engineImpl, layer: fxLayer, reducedMotion: reduced, log });

  const ctx = {
    root, engine, assets: a, store, peek, ceremonies,
    keys: { labelFor: () => '', on() { return () => {}; }, panicKey: 'Escape' },
    rng: makeRng(seed + '|de'),
    lexicon: (k, f) => (f == null ? k : f),
    caps, dev: DEV, hideTutorial: true,   // the rules sheet (Deck VI) shows once per Year, then stays down
    settings: { de_board_size: prefs.size === 5 ? '5x5' : '4x4', boardSize: prefs.size, de_tile_faces: prefs.faces, de_perf: prefs.perf },
    platform: { isTouch: (() => { try { return window.matchMedia('(pointer: coarse)').matches; } catch (e) { return false; } })(), hasHaptics: false, host: 'web' },
    motion: { reducedMotion: reduced, motionLevel: reduced ? 0 : 2 },
    audioAudible: !!prefs.sound,
    words: WORDS.slice(), sessionWords: { add() {} }, absorb() {},
    log: (m) => log('[de] ' + m),
    endClass(res) { onEnded(res); },
  };
  if (reduced) document.documentElement.classList.add('arc-reduced');

  handle = game.create(ctx);
  const spec = endless
    ? { gradeTier: prefs.tier, seed, timeBudgetSec: 0, retake: false, endless: true }
    : { gradeTier: prefs.tier, seed, timeBudgetSec: Number(qs.get('t') || game.timeBudgetSec || 300), retake: false };
  if (devDeepest) spec.devDeepest = devDeepest;
  lastSpec = spec;
  log('[start] ' + JSON.stringify(spec) + ' niche=' + prefs.niche + ' faces=' + prefs.faces + ' perf=' + prefs.perf + ' spiral=' + spiral.split('/').pop());
  showScreen('stage');
  running = true;
  handle.start(spec);
  window.__deHandle = handle; window.__deGame = game; window.__deEngine = engineImpl;
  document.title = 'The Deep End — ' + (endless ? 'free swim' : 'Year ' + prefs.tier);
  if (qs.get('autoplay')) armAutoplay(Number(qs.get('autoplay')));
}

function onEnded(res) {
  running = false;
  const m = (res && res.metrics) || {};
  const endless = !!(res && res.endless);
  let deepest = 0;
  try { const d = game.diagnostics(); deepest = d && d.deepest ? Number(d.deepest) : 0; } catch (e) { /* ignore */ }
  const comp = Math.max(0, Math.min(1, Number(m.composite) || 0));
  const letter = endless ? null : letterFor(comp);
  $('res-grade').textContent = endless ? '∞' : letter;
  $('res-grade').className = 'grade ' + (endless ? 'g-free' : 'g-' + letter);
  $('res-title').textContent = endless ? 'Free swim over' : 'Class dismissed';
  const names = ['', 'Awake', 'Drowsy', 'Soft', 'Heavy', 'Sinking', 'Under', 'Deep', 'Deeper', 'Fathoms', 'Abyss', 'Blackout'];
  $('res-sub').textContent = (deepest > 0 ? 'deepest tier ' + deepest + ' · ' + (names[deepest] || '') : '') + (endless ? '' : ' · composite ' + Math.round(comp * 100) + '%');
  log('[end] ' + JSON.stringify({ endless, composite: comp, letter, deepest }));
  // let the game's own end card breathe for a beat before the result sheet
  setTimeout(() => { if (!running) showScreen('result'); }, 2200);
}

/* result buttons */
$('res-again').onclick = () => { showScreen('stage'); dive({ sameSeed: true }); };
$('res-new').onclick = () => { showScreen('stage'); dive({}); };
$('res-lobby').onclick = () => { teardown(); showScreen('lobby'); warmAssets(); };

/* dive */
$('dive').onclick = () => {
  if (usesRemote() && !consented()) { showScreen('gate'); return; }
  dive({});
};

/* ---- pause / esc / fullscreen / surface -------------------------------- */
function setPaused(on) {
  if (!running || !handle) return;
  if (on === paused) return;
  paused = on;
  try { if (on) handle.pause(); else handle.resume(); } catch (e) { /* ignore */ }
  $('pause').hidden = !on;
}
window.addEventListener('keydown', (e) => {
  if (e.key !== 'Escape') return;
  if ($('stage').hidden) return;
  if (!running) return;
  e.preventDefault();
  setPaused(!paused);
}, true);
$('p-resume').onclick = () => setPaused(false);
$('p-surface').onclick = () => { teardown(); showScreen('lobby'); warmAssets(); };
$('hud-pause').onclick = () => setPaused(!paused);
$('hud-full').onclick = () => {
  try {
    if (document.fullscreenElement) document.exitFullscreen();
    else document.documentElement.requestFullscreen({ navigationUI: 'hide' });
  } catch (e) { /* ignore */ }
};
document.addEventListener('visibilitychange', () => { if (document.hidden && running && !paused) setPaused(true); });

/* ---- headless autoplay (the rig's greedy player; ?autoplay=ms) ------------ */
function armAutoplay(ms) {
  if (!(ms > 0)) return;
  const DIRS = { up: { dr: -1, dc: 0, key: 'ArrowUp' }, down: { dr: 1, dc: 0, key: 'ArrowDown' }, left: { dr: 0, dc: -1, key: 'ArrowLeft' }, right: { dr: 0, dc: 1, key: 'ArrowRight' } };
  function simulate(tiles, n, dir) {
    const g = []; for (let r = 0; r < n; r++) { g.push([]); for (let c = 0; c < n; c++) g[r].push(null); }
    for (const t of tiles) if (t.r >= 0 && t.r < n && t.c >= 0 && t.c < n) g[t.r][t.c] = { tier: t.tier, silt: !!t.silt };
    const d = DIRS[dir]; let merges = 0; let moved = false;
    for (let i = 0; i < n; i++) {
      const line = [];
      for (let j = 0; j < n; j++) { let r, c; if (d.dr !== 0) { c = i; r = d.dr > 0 ? n - 1 - j : j; } else { r = i; c = d.dc > 0 ? n - 1 - j : j; } line.push({ r, c, v: g[r][c] }); }
      const vals = line.filter((x) => x.v).map((x) => x.v); const out = [];
      for (let k = 0; k < vals.length; k++) { const cur = vals[k], nxt = vals[k + 1]; if (nxt && !cur.silt && !nxt.silt && cur.tier === nxt.tier) { out.push({ tier: cur.tier + 1, silt: false }); merges += 1; k += 1; } else out.push(cur); }
      for (let k = 0; k < line.length; k++) { const b = line[k].v, a = out[k] || null; if ((b == null) !== (a == null) || (b && a && (b.tier !== a.tier || b.silt !== a.silt))) moved = true; }
    }
    return { moved, merges };
  }
  const id = setInterval(() => {
    try {
      if (!running || paused) return;
      const d = game.diagnostics();
      if (!d || d.ended || d.busy || d.paused || d.dead) return;
      if (['briefing', 'resurface', 'ended', 'ceiling'].includes(d.phase)) return;
      let best = null, bs = -Infinity;
      for (const dir of ['down', 'left', 'right', 'up']) { const s = simulate(d.tiles || [], Number(d.n) || 4, dir); if (!s.moved) continue; const sc = s.merges * 10 + (dir === 'down' || dir === 'left' ? 0.5 : 0); if (sc > bs) { bs = sc; best = dir; } }
      if (best) window.dispatchEvent(new KeyboardEvent('keydown', { key: DIRS[best].key, code: DIRS[best].key, bubbles: true, cancelable: true }));
    } catch (e) { /* ignore */ }
  }, ms);
  window.__deAutoplay = id;
}

/* ---- boot ------------------------------------------------------------------ */
paintLobby();
if (qs.get('go') === '1' && DEV) { showScreen('stage'); dive({}); }
else if (consented()) { showScreen('lobby'); warmAssets(); }
else showScreen('gate');
log('[boot] ok');
