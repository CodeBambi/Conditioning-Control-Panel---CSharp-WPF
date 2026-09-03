/**
 * INTAKE PREFS — the tiny settings store behind the main menu's Options panel
 * and the in-game pause menu's Options panel (they are the SAME panel, mounted
 * in two places; see ui/options.js).
 *
 * Deliberately dumb and dependency-free:
 *   - one flat object of primitives, no nesting, no migrations
 *   - localStorage-backed, which works identically standalone and inside
 *     WebView2, so NO host/C# round-trip is needed to persist a volume slider
 *   - synchronous reads (getPrefs) so a render path can consult it without await
 *   - subscribe() so live sliders can retune audio while the run is going
 *
 * These are USER COMFORT controls and are intentionally separate from `caps`
 * (contracts.js DEFAULT_CAPS), which is the host's intensity governor. Caps say
 * how hard the experience is allowed to push; prefs say how loud it is in the
 * room. Multiply, never conflate: final gain = spec.gain * caps * prefs.
 */

const KEY = 'intake.prefs';

/** @typedef {{voEnabled:boolean, volMaster:number, volBinaural:number,
 *             volEffects:number, volVoice:number, sparkles:boolean}} IntakePrefs */

/** Shipping defaults: everything on, everything at full. */
export const PREF_DEFAULTS = Object.freeze({
  voEnabled:   true,  // master switch for spoken question/taunt VO
  volMaster:   1,     // 0..1, scales every other bus
  volBinaural: 1,     // the binaural bed (audio.js setDepth bus)
  volEffects:  1,     // sfx / chimes / pops / stings
  volVoice:    1,     // VO clip playback
  musicOn:     true,  // the SHELL music track (menus only) — its own kill switch
  volMusic:    0.7,   // 0..1, shell music level (starts under the sfx bus on purpose)
  sparkles:    true,  // kawaii sparkle + rainbow decor (off = calmer menus)
});

const NUMERIC = ['volMaster', 'volBinaural', 'volEffects', 'volVoice', 'volMusic'];
const BOOLEAN = ['voEnabled', 'musicOn', 'sparkles'];

const subs = new Set();
let cache = null;

function clamp01(n) {
  const v = Number(n);
  return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 1;
}

/** Coerce anything into a valid prefs object. Unknown keys are dropped. */
function sanitize(raw) {
  const out = Object.assign({}, PREF_DEFAULTS);
  if (raw && typeof raw === 'object') {
    for (const k of NUMERIC) if (raw[k] != null) out[k] = clamp01(raw[k]);
    for (const k of BOOLEAN) if (raw[k] != null) out[k] = !!raw[k];
  }
  return out;
}

function readStore() {
  try {
    const s = window.localStorage.getItem(KEY);
    return s ? JSON.parse(s) : null;
  } catch (_e) { return null; }   // private mode / quota / bad JSON -> defaults
}

function writeStore(p) {
  try { window.localStorage.setItem(KEY, JSON.stringify(p)); } catch (_e) { /* best-effort */ }
}

/**
 * Current prefs. Always returns a fresh copy — callers may not mutate the store
 * by holding the object. Lazily hydrates from localStorage on first call.
 * @returns {IntakePrefs}
 */
export function getPrefs() {
  if (!cache) cache = sanitize(readStore());
  return Object.assign({}, cache);
}

/** One pref value, already sanitized. */
export function getPref(key) { return getPrefs()[key]; }

/**
 * Write one pref. Persists, then notifies subscribers with the FULL new prefs
 * plus the key that changed, so a listener can cheaply ignore irrelevant edits.
 * No-ops (and does not notify) when the value is unchanged — slider `input`
 * events fire densely and we do not want a retune storm.
 */
export function setPref(key, value) {
  const cur = getPrefs();
  if (!(key in PREF_DEFAULTS)) return cur;
  const next = sanitize(Object.assign({}, cur, { [key]: value }));
  if (next[key] === cur[key]) return cur;
  cache = next;
  writeStore(next);
  notify(key);
  return Object.assign({}, next);
}

/** Restore shipping defaults (the Options panel's "Reset" affordance). */
export function resetPrefs() {
  cache = Object.assign({}, PREF_DEFAULTS);
  writeStore(cache);
  notify(null);
  return getPrefs();
}

function notify(changedKey) {
  const snap = getPrefs();
  for (const fn of Array.from(subs)) {
    try { fn(snap, changedKey); } catch (_e) { /* a bad listener never breaks a write */ }
  }
}

/**
 * Listen for pref changes. Returns an unsubscribe function.
 * @param {(prefs:IntakePrefs, changedKey:string|null)=>void} fn
 */
export function subscribe(fn) {
  if (typeof fn !== 'function') return () => {};
  subs.add(fn);
  return () => subs.delete(fn);
}

/* ----------------------------------------------------------------------------
 * DERIVED GAINS — the one place that decides how a pref turns into a multiplier,
 * so audio.js, the VO path and the menus can never drift apart on the maths.
 * -------------------------------------------------------------------------- */

/** Multiplier for the binaural bed. */
export function binauralScale(p = getPrefs()) { return p.volMaster * p.volBinaural; }

/** Multiplier for sfx / chimes / pops / stings. */
export function effectsScale(p = getPrefs()) { return p.volMaster * p.volEffects; }

/**
 * Multiplier for spoken VO. The on/off switch is folded in here so a caller
 * that only asks for a gain still honours it — 0 means "do not play".
 */
export function voiceScale(p = getPrefs()) {
  return p.voEnabled ? p.volMaster * p.volVoice : 0;
}

/**
 * Multiplier for the SHELL music track (ui/menuMusic.js).
 *
 * Deliberately NOT scaled by volMaster. Licensed music is the one element a
 * player is most likely to have a flat opinion about — they may want the room
 * loud and this track gone, or the reverse. Chaining it to the master slider
 * would mean "quiet everything a bit" also quiets the song, and muting the song
 * would leave the master slider lying about what it controls. It gets its own
 * kill switch (musicOn) with a prominent affordance in the menu, exactly
 * because taste on a named track is not a comfort setting.
 */
export function musicScale(p = getPrefs()) {
  return p.musicOn ? p.volMusic : 0;
}
