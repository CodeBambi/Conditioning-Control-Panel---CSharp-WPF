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
  gifSize: 1,          // 0.5 - 1.6 flash clip (gif) size multiplier
  gifOpacity: 0.95,    // 0.1 - 1  flash clip opacity
  hydraGen: 2,         // 0 | 1 | 2 generations a popped clip splits into
  spiralOpacity: 0.3,  // 0 - 1 spiral overlay + spiral veils
  pinkOpacity: 0.25,   // 0 - 1 pink washes + pink veils
  glitch: 1,           // 0 - 1 glitch bubble visual intensity (0 = score only)
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
};

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
  const out = { ...DEFAULTS, wordsOff: [], customWords: [] };
  try {
    const raw = JSON.parse(localStorage.getItem(KEY) || 'null');
    if (raw && typeof raw === 'object') {
      for (const k of Object.keys(DEFAULTS)) {
        if (Array.isArray(DEFAULTS[k])) {
          if (Array.isArray(raw[k])) out[k] = raw[k].filter((w) => typeof w === 'string').slice(0, 64);
        } else if (typeof raw[k] === 'number' && isFinite(raw[k])) {
          out[k] = raw[k];
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

// ---- subliminal word list -------------------------------------------------
export function allWords() {
  return [...SUBLIMINAL_WORDS, ...S.customWords];
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
