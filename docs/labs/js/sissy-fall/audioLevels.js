/* ============================================================================
 * audioLevels.js (Sissy Fall) - per-group volume levels, live + persisted.
 *
 * The master mute (audioMute.js) is a hard on/off shared with Explore; this is
 * a Fall-only set of 0..1 gains for the three voice/SFX groups the fall mixes,
 * so the audio button can expand into granular sliders:
 *   video - proximity video-card audio (spawner.js)
 *   voice - the drift-chain voiceover (driftChain.js)
 *   fx    - bubble pops / chimes / combo stings (bubbles.js) - the loud layer
 *   drops - subliminal word drops (bubbles.js)
 *   music - the ambient drone bed / background tone (scene.js). Unlike the
 *           others this is a MULTIPLIER on the speed-following drone curve
 *           (default 1.0 = unchanged), so the player can duck or mute it.
 *
 * Consumers read getLevel() live at play/tick time, so a slider move applies to
 * the next frame / next clip with no restart. Defaults match the constants the
 * groups shipped with, so default behavior is unchanged. Persisted to
 * localStorage under its own key (independent of the mute pref).
 * ==========================================================================*/

const KEY = 'sf-audio-levels';
const DEFAULTS = { video: 0.45, voice: 0.85, fx: 0.48, drops: 0.4, music: 1 };

// Most groups are 0..1 gains. 'music' is a MULTIPLIER over the speed-following
// drone bed, so it clamps to 0..2 - 100% is the default curve, 200% doubles it,
// letting the player push the music up as well as down.
const CEIL = { music: 2 };
const clamp = (group, v) => Math.max(0, Math.min(CEIL[group] ?? 1, Number(v)));

const levels = { ...DEFAULTS };
try {
  const raw = JSON.parse(localStorage.getItem(KEY) || 'null');
  if (raw && typeof raw === 'object') {
    for (const k of Object.keys(DEFAULTS)) {
      if (typeof raw[k] === 'number' && isFinite(raw[k])) levels[k] = clamp(k, raw[k]);
    }
  }
} catch (e) { /* fresh defaults */ }

const subs = new Set();
function save() { try { localStorage.setItem(KEY, JSON.stringify(levels)); } catch (e) { /* private mode */ } }

export function getLevel(group) {
  const v = levels[group];
  return typeof v === 'number' ? v : (DEFAULTS[group] ?? 1);
}

export function setLevel(group, value) {
  if (!(group in DEFAULTS)) return;
  levels[group] = clamp(group, value);
  save();
  subs.forEach((fn) => { try { fn(group, levels[group]); } catch (e) { /* ignore */ } });
}

export function audioGroups() {
  return [
    { key: 'video', label: 'video' },
    { key: 'voice', label: 'voice' },
    { key: 'fx', label: 'effects' },
    { key: 'drops', label: 'subliminals' },
    { key: 'music', label: 'music' },
  ];
}

// Subscribe to level changes; returns an unsubscribe fn.
export function onLevels(fn) { subs.add(fn); return () => subs.delete(fn); }
