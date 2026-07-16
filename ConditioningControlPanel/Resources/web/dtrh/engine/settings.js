/* ============================================================================
 * settings.js - live-tunable options for the Sissy Fall.
 *
 * One mutable store (S), persisted to localStorage so a tuned fall stays
 * tuned across visits. Everything reads S live each time it needs a value,
 * so slider moves apply to the NEXT bubble/card/effect immediately - no
 * restart, no plumbing.
 *
 * Subliminal words: the built-in manifest list plus user-added custom words;
 * any of them can be switched off (wordsOff). activeWords() is what the
 * bubble flashes and the word cards actually draw from - it may be empty,
 * and consumers must skip word content when it is.
 * ==========================================================================*/

import { SUBLIMINAL_WORDS } from '/dtrh/assets/bubbles/manifest.js';

const KEY = 'sf-settings';

const DEFAULTS = {
  bubbleSize: 1,       // 0.5 - 2.5 bubble diameter multiplier
  bubbleDensity: 1,    // 0 - 1.25 population multiplier (0 = bubbles off entirely)
  gifSize: 0.5,        // 0.5 - 1.6 flash clip (gif) size multiplier (default halved - flashes ran too large)
  gifOpacity: 0.95,    // 0.1 - 1  flash clip opacity
  flashCount: 3,       // 1 - 10 flash gifs a flash spawns at once
  hydraGen: 2,         // 0 - 5 generations a popped clip splits into
  spiralOpacity: 0.3,  // 0 - 1 spiral overlay + spiral veils
  pinkOpacity: 0.25,   // 0 - 1 pink washes + pink veils
  glitch: 1,           // 0 - 1 glitch bubble visual intensity (0 = score only)
  glitchSeconds: 2.5,  // 1 - 8 s the glitch shudder holds ('glitch timer' dial)
  spotSeconds: 30,     // 10 - 30 s a video holds the spotlight
  wordsOff: [],        // disabled subliminal words (built-in or custom)
  customWords: [],     // user-added subliminal words
  // Tunnel theme colors (stored as 0xRRGGBB ints so the numeric load() filter
  // keeps them). Defaults match the original Sissy-pink look, so an untouched
  // fall is unchanged. Consumed live by fx.js (zones/ribbons/sparkles/lightning
  // + tunnel bg + particle fog). Edit via the paint (color) picker.
  colLine: 0xff69b4,    // tunnel rings / primary hue
  colSpiral: 0xe56cc0,  // helical spiral
  colFog: 0x220d1e,     // fog / haze
  colBg: 0x2b1024,      // tube background
  colRibbon: 0xff7ac8,  // ribbon strands
  colSparkle: 0xffd9ef, // glimmer sparkles
  // ---- descent settings (the old Warren DESCENT tab, moved into ⚙ options) ----
  // These ride along with request-run (warren.currentSetup merges descentSetup()
  // into the wire setup), so the host's PersistRunSetup/FromSettings contract is
  // untouched - the panel is only the edit surface. runSeeded flips to 1 after
  // the one-time migration from the host's saved runSetup (warren.js).
  runMotion: 'Mixed',        // 'Mixed' | 'FloatUp' | 'RainDown' | 'RoamBounce'
  runEffectIntensity: 0.85,  // 0.2 - 1.5 run effect-intensity multiplier
  runColorFlashes: true,     // color flashes on the edges
  runBoonDraft: true,        // mantra drafts between loops
  runAllowCurses: true,      // sins on the table
  runDarters: true,          // white rabbits
  runVariantsOff: [],        // bubble-pool variant ids switched OFF (empty = all on)
  runSeeded: 0,              // 1 once the host's saved runSetup seeded these keys
  // ---- the guide -------------------------------------------------------------
  hideTutorial: false,       // true = silence the Cheshire guide entirely (no
                             // portrait, no scenes, no tutorial VO). cheshireGuide
                             // no-ops every fire point when set (read live).
  // ---- crafted unlocks (THE BOUDOIR, Part 2) ----------------------------------
  wordPack: 'bimbo_classic', // HEADPHONES: active word pack id (WORD_PACKS below)
  bubbleSkin: 'default',     // LIPSTICK: bubble-skin id (game/variants.js BUBBLE_SKINS)
  musicBox: false,           // THE BALLERINA: the music-box bed under the fall
};

export const MOTIONS = ['Mixed', 'FloatUp', 'RainDown', 'RoamBounce'];

// Paint-picker rows (order shown in the UI).
export const THEME_COLORS = [
  { key: 'colLine', label: 'rings / hue' },
  { key: 'colSpiral', label: 'spiral' },
  { key: 'colFog', label: 'fog' },
  { key: 'colBg', label: 'background' },
  { key: 'colRibbon', label: 'ribbons' },
  { key: 'colSparkle', label: 'sparkles' },
];

// One-click themes. `sissy` equals the DEFAULTS above (the reset target).
export const THEME_PRESETS = {
  sissy:    { colLine: 0xff69b4, colSpiral: 0xe56cc0, colFog: 0x220d1e, colBg: 0x2b1024, colRibbon: 0xff7ac8, colSparkle: 0xffd9ef },
  drone:    { colLine: 0x39ff9d, colSpiral: 0x00d6a0, colFog: 0x05140d, colBg: 0x07160e, colRibbon: 0x4dffb0, colSparkle: 0xc9ffe6 },
  midnight: { colLine: 0x8a5cff, colSpiral: 0xb388ff, colFog: 0x0d0820, colBg: 0x12082a, colRibbon: 0x7a5cff, colSparkle: 0xd9c8ff },
  cyber:    { colLine: 0x00e5ff, colSpiral: 0xff2fb0, colFog: 0x06121a, colBg: 0x08141f, colRibbon: 0x22d3ff, colSparkle: 0xc8f5ff },
  ember:    { colLine: 0xff6a2f, colSpiral: 0xff2f5e, colFog: 0x1a0a06, colBg: 0x20100a, colRibbon: 0xff8a4d, colSparkle: 0xffe0c8 },
  mono:     { colLine: 0xd8d8e0, colSpiral: 0xa0a0b0, colFog: 0x0c0c10, colBg: 0x141418, colRibbon: 0xc0c0cc, colSparkle: 0xffffff },
};

export const intToHex = (n) => '#' + ((n >>> 0) & 0xffffff).toString(16).padStart(6, '0');
export const hexToInt = (h) => parseInt(String(h).replace('#', ''), 16) || 0;

function load() {
  const out = { ...DEFAULTS, wordsOff: [], customWords: [], runVariantsOff: [] };
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) || 'null');
    if (raw && typeof raw === 'object') {
      for (const k of Object.keys(DEFAULTS)) {
        // keep a saved value only when its type matches the default's (string
        // arrays / numbers / booleans / strings) so a stale or hand-edited
        // store can't smuggle in a wrong-typed value
        if (Array.isArray(DEFAULTS[k])) {
          if (Array.isArray(raw[k])) out[k] = raw[k].filter((w) => typeof w === 'string').slice(0, 64);
        } else if (typeof DEFAULTS[k] === 'number') {
          if (typeof raw[k] === 'number' && isFinite(raw[k])) out[k] = raw[k];
        } else if (typeof DEFAULTS[k] === 'boolean') {
          if (typeof raw[k] === 'boolean') out[k] = raw[k];
        } else if (typeof DEFAULTS[k] === 'string') {
          if (typeof raw[k] === 'string') out[k] = raw[k];
        }
      }
    }
  } catch (e) { /* fresh defaults */ }
  return out;
}

export const S = load();

const subs = new Set();
function save() { try { localStorage.setItem(KEY, JSON.stringify(S)); } catch (e) { /* private mode */ } }
function emit(key) { for (const cb of subs) { try { cb(key); } catch (e) { /* ignore */ } } }

export function updateSetting(key, value) {
  S[key] = value;
  save();
  emit(key);
}

export function onSettings(cb) {
  subs.add(cb);
  return () => subs.delete(cb);
}

// ---- progression: earning the dials -----------------------------------------
// The options panel starts almost fully locked - the fall decides. Each rung is
// bought on the Dollhouse DIALS console with GOLD 🪙 (the gold cutover: gold
// unlocks things, drops ✦ level things); the purchase is an authoritative C#
// meta-command (`purchase-dial`) that records the rung id into
// ChaosMetaState.PurchasedDials, so the unlocked set arrives via the meta
// snapshot - NOT this localStorage store (which the user could trivially edit).
// This table is the SHARED source of truth for both the panel (gating) and the
// Dollhouse console (rows/prices); consumers get the unlocked-id set injected.
//
// Ordered gentle -> feral: the two runaway dials (hydra generations, glitch
// timer) also require meta-rank Entranced (index 3). `keys` are the setting
// sliders a rung governs; `feature` marks the non-slider sections. STARTER dials
// (bubbles, reset, diagnostics) are never in the ladder - always available.
// Prices sized to gold income (~40-80/run early, 90-150 mid, 200-350 late):
// the whole console (incl. the 3 CONSOLE_EXTRAS) lands around 25-35 runs.
export const UNLOCK_LADDER = [
  { id: 'bubbleSize',  label: 'bubble size',           keys: ['bubbleSize'],             price: 25 },
  { id: 'words',       label: 'subliminal words',      feature: 'words',                 price: 50 },
  { id: 'spiral',      label: 'spiral opacity',        keys: ['spiralOpacity'],          price: 75 },
  { id: 'pink',        label: 'pink filter opacity',   keys: ['pinkOpacity'],            price: 100 },
  { id: 'gifLook',     label: 'gif look',              keys: ['gifSize', 'gifOpacity'],  price: 150 },
  { id: 'flash',       label: 'flash gifs',            keys: ['flashCount'],             price: 200 },
  { id: 'custom',      label: 'make it yours',         feature: 'custom',                price: 250 },
  { id: 'spotlight',   label: 'video spotlight time',  keys: ['spotSeconds'],            price: 325 },
  { id: 'glitch',      label: 'glitch intensity',      keys: ['glitch'],                 price: 400 },
  { id: 'hydra',       label: 'gif hydra generations', keys: ['hydraGen'],     price: 550, rankReq: 3 },
  { id: 'glitchTimer', label: 'glitch timer',          keys: ['glitchSeconds'], price: 700, rankReq: 3 },
];

// Meta-rank names (mirrors Services/Chaos/ChaosRanks.cs) for lock-hint text.
export const RANK_NAMES = ['Curious', 'Tempted', 'Slipping', 'Entranced', 'Devoted', 'Claimed'];

// setting-key / feature -> owning rung id (starter controls map to nothing).
const RUNG_BY_KEY = {};
const RUNG_BY_FEATURE = {};
for (const r of UNLOCK_LADDER) {
  if (r.keys) for (const k of r.keys) RUNG_BY_KEY[k] = r.id;
  if (r.feature) RUNG_BY_FEATURE[r.feature] = r.id;
}
export const rungForKey = (key) => RUNG_BY_KEY[key] || null;
export const rungForFeature = (feature) => RUNG_BY_FEATURE[feature] || null;

// ---- descent settings snapshot ------------------------------------------------
// The old DESCENT tab's choices, read from S at request-run time. variantsOff is
// the raw switched-OFF id list - the game layer (warren.js) maps it against
// POOL_VARIANTS into the wire `enabledVariants` (null = all on), keeping this
// store ignorant of the game catalog.
export function descentSetup() {
  return {
    motion: MOTIONS.includes(S.runMotion) ? S.runMotion : 'Mixed',
    effectIntensity: Math.min(1.5, Math.max(0.2, S.runEffectIntensity)),
    colorFlashes: !!S.runColorFlashes,
    boonDraftEnabled: !!S.runBoonDraft,
    allowCurses: !!S.runAllowCurses,
    dartersEnabled: !!S.runDarters,
    variantsOff: [...S.runVariantsOff],
  };
}

// Restore a set of numeric options to their shipped defaults. Used by the gear
// panel's "reset to default" button; scoped to the keys the caller passes (the
// visible sliders) so it never silently wipes custom words or a chosen theme.
export function resetOptions(keys) {
  for (const k of keys) {
    if (k in DEFAULTS && !Array.isArray(DEFAULTS[k])) updateSetting(k, DEFAULTS[k]);
  }
}

// Apply a named theme (sets all six color keys; each emits so fx.js re-themes).
export function applyThemePreset(name) {
  const p = THEME_PRESETS[name];
  if (!p) return;
  for (const k of Object.keys(p)) updateSetting(k, p[k]);
}

// ---- THE RING BOX (crafted, Part 2): kept Looks -----------------------------
// Up to 3 saved theme snapshots (the six color keys), own localStorage row so
// a settings reset never eats them. The paint panel gates the UI on owning
// the_ring_box; the store itself is dumb.
const LOOKS_KEY = 'dtrh.looks.v1';
export function getLooks() {
  try {
    const a = JSON.parse(localStorage.getItem(LOOKS_KEY) || '[]');
    return Array.isArray(a) ? a.slice(0, 3).map((l) => (l && typeof l === 'object' ? l : null)) : [];
  } catch (e) { return []; }
}
function putLooks(looks) {
  try { localStorage.setItem(LOOKS_KEY, JSON.stringify(looks.slice(0, 3))); } catch (e) { /* private mode */ }
}
/** Keep the CURRENT six theme colors in slot 0-2. */
export function saveLook(slot) {
  const looks = getLooks();
  const look = {};
  for (const c of THEME_COLORS) look[c.key] = S[c.key];
  looks[slot] = look;
  putLooks(looks);
}
export function clearLook(slot) {
  const looks = getLooks();
  looks[slot] = null;
  putLooks(looks);
}
/** Wear a kept look (updateSetting per key so fx re-themes live). */
export function applyLook(slot) {
  const look = getLooks()[slot];
  if (!look) return false;
  for (const c of THEME_COLORS) {
    if (typeof look[c.key] === 'number' && isFinite(look[c.key])) updateSetting(c.key, look[c.key]);
  }
  return true;
}

// ---- word packs (HEADPHONES craft, Part 2) ----------------------------------
// The base word list + the veil punch-through words, as swappable packs. The
// classic pack IS the shipped default (byte-identical fall for non-owners).
// Words without a whispered drop flash silently (bubbles.js null-guards).
export const WORD_PACKS = [
  { id: 'bimbo_classic', name: 'bimbo classic', desc: 'the words the hole was born with.',
    veil: { spiral: 'Sink', pinkfilter: 'Drop', glitch: 'Blank', flash: 'Empty' },
    words: SUBLIMINAL_WORDS },
  { id: 'good_girl', name: 'good girl', desc: 'soft praise. obedient warmth.',
    veil: { spiral: 'Obey', pinkfilter: 'Good girl', glitch: 'Submit', flash: 'Pretty' },
    words: ['Good girl', 'Obey', 'Submit', 'Yes', 'Sweet', 'Pretty', 'Behave', 'Smile', 'Please her'] },
  { id: 'blank_slate', name: 'blank slate', desc: 'no thoughts. just the fall.',
    veil: { spiral: 'Deeper', pinkfilter: 'Sleep', glitch: 'Mindless', flash: 'Empty' },
    words: ['Blank', 'Empty', 'Mindless', 'Sleep', 'Deeper', 'Sink', 'Drop', "Don't think"] },
];
const packById = (id) => WORD_PACKS.find((p) => p.id === id) || WORD_PACKS[0];
export function getWordPack() { return packById(S.wordPack).id; }
export function setWordPack(id) {
  updateSetting('wordPack', packById(id).id);
  emit('words');   // word-card consumers redraw from the new pack
}
/** The veil punch-through words of the active pack (bubbles.js triggerVeil). */
export function getVeilWords() { return packById(S.wordPack).veil; }

// ---- crafted-gate enforcement (slot load) ------------------------------------
// The crafted picks (word pack / bubble skin / music box) persist in the shared
// sf-settings row, but OWNERSHIP is per save slot - a slot that never crafted
// the gate item must not inherit another slot's pick (its revert controls are
// hidden there). Snaps each un-owned pick back to its shipped default, persisting
// the reset. Call it only once ownership is reliably KNOWN (the meta snapshot is
// in) - an ownedFn that answers false because the meta simply hasn't landed yet
// would wipe a legitimate owner's choices.
export function enforceCraftedDefaults(ownedFn) {
  if (S.wordPack !== DEFAULTS.wordPack && !ownedFn('headphones')) setWordPack(DEFAULTS.wordPack);
  if (S.bubbleSkin !== DEFAULTS.bubbleSkin && !ownedFn('lipstick')) updateSetting('bubbleSkin', DEFAULTS.bubbleSkin);
  if (S.musicBox !== DEFAULTS.musicBox && !ownedFn('the_ballerina')) updateSetting('musicBox', DEFAULTS.musicBox);
}

// ---- subliminal word list -------------------------------------------------
export function allWords() {
  return [...packById(S.wordPack).words, ...S.customWords];
}

export function activeWords() {
  const off = new Set(S.wordsOff);
  return allWords().filter((w) => !off.has(w));
}

export function setWordOn(word, on) {
  const i = S.wordsOff.indexOf(word);
  if (on && i >= 0) S.wordsOff.splice(i, 1);
  else if (!on && i < 0) S.wordsOff.push(word);
  save();
  emit('words');
}

// ---- custom spiral ----------------------------------------------------------
// Session-only (a dropped file's blob URL can't survive a reload, so this is
// deliberately NOT persisted). Replaces the built-in spiral.webm overlay that
// spiral bubbles fire.
let customSpiral = null; // { url, isVideo, name }

export function setCustomSpiral(file) {
  if (customSpiral) { try { URL.revokeObjectURL(customSpiral.url); } catch (e) { /* ignore */ } }
  customSpiral = null;
  if (file) {
    const isVideo = /^video\//.test(file.type) || /\.(mp4|webm|m4v)$/i.test(file.name || '');
    const isImage = /^image\//.test(file.type) || /\.(gif|png|jpe?g|webp)$/i.test(file.name || '');
    if (isVideo || isImage) {
      customSpiral = { url: URL.createObjectURL(file), isVideo, name: file.name || 'spiral' };
    }
  }
  emit('customSpiral');
  return customSpiral;
}

export function getCustomSpiral() {
  return customSpiral;
}

export function addCustomWord(word) {
  const w = String(word || '').trim().slice(0, 24);
  if (!w) return false;
  const lower = w.toLowerCase();
  if (allWords().some((x) => x.toLowerCase() === lower)) return false;
  if (S.customWords.length >= 32) return false;
  S.customWords.push(w);
  save();
  emit('words');
  return true;
}
