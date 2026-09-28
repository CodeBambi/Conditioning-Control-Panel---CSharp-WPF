// Local presentation choices. Host and OS motion restrictions always win.
import { onHostMessage } from '../bridge.js';
const KEY = 'pbp-presentation-v1';
let saved = {};
try { saved = JSON.parse(globalThis.localStorage?.getItem(KEY) || '{}') || {}; } catch { /* private storage */ }
if (!saved || typeof saved !== 'object' || Array.isArray(saved)) saved = {};
const settings = () => globalThis.window?.PBP?.settings || {};
let hostReduced = !!settings().reducedMotion;
let hostVolume = Number.isFinite(settings().sfxVolume) ? settings().sfxVolume : .6;
const listeners = new Set();
const osMotion = globalThis.window?.matchMedia?.('(prefers-reduced-motion: reduce)');
const clamp = value => Math.max(0, Math.min(1, value));
export function presentation() {
  const volume = Number.isFinite(saved.volume) ? clamp(saved.volume) : hostVolume;
  return {
    experience: saved.experience === 'distraction' ? 'distraction' : 'classic',
    volume: Math.min(volume, hostVolume),
    reducedMotion: !!saved.reducedMotion || hostReduced || !!osMotion?.matches,
    motionLocked: hostReduced || !!osMotion?.matches,
    soundLocked: hostVolume === 0,
  };
}
function apply() {
  const p = presentation();
  Object.assign(settings(), { experience: p.experience, sfxVolume: p.volume, reducedMotion: p.reducedMotion });
  globalThis.window?.PBP?.board?.sfx?.setVolume?.(p.volume);
  for (const fn of listeners) fn(p);
  return p;
}
export function setPresentation(patch) {
  if (['classic', 'distraction'].includes(patch.experience)) saved.experience = patch.experience;
  if (typeof patch.reducedMotion === 'boolean') saved.reducedMotion = patch.reducedMotion;
  if (Number.isFinite(patch.volume)) saved.volume = clamp(patch.volume);
  try { globalThis.localStorage?.setItem(KEY, JSON.stringify(saved)); } catch { /* session only */ }
  return apply();
}
export function onPresentation(fn) { listeners.add(fn); fn(presentation()); return () => listeners.delete(fn); }
onHostMessage(m => {
  if (m.type !== 'pbp:settings') return;
  if (typeof m.reducedMotion === 'boolean') hostReduced = m.reducedMotion;
  if (Number.isFinite(m.sfxVolume)) hostVolume = clamp(m.sfxVolume);
  apply();
});
osMotion?.addEventListener?.('change', apply);
apply();
