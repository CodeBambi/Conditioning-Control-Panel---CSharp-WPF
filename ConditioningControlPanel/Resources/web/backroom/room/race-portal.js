/** Same-window Racing Thoughts door. Only a camera pose survives the round trip. */
import { EYE, WALLS } from './walk.js';

export const RACE_POSE_KEY = 'backroom.race-return-pose.v1';
/** THE OWNED TRACKS, for the page on the other side of the door. The desktop host tells the race what the
 * account owns in `init.settings.racingTracks`; a same-window navigation has no host, so the room leaves the
 * list here (same origin, this tab only) and the race's web router reads it when `?casino=1` is on the URL.
 * Without it every level would be open, which is what the owner saw (2026-09-18). */
export const RACE_OWNERSHIP_KEY = 'backroom.race-ownership.v1';
export const CASINO_RETURN = '/backroom/index.html?raceReturn=1';
const RACE_PATHS = new Set(['/dtrh/race.html', '/backroom/racing/race.html']);
const browserLocation = () => typeof location === 'object' ? location : null;
const browserStorage = () => { try { return sessionStorage; } catch { return null; } };
const forget = storage => { try { storage?.removeItem(RACE_POSE_KEY); storage?.removeItem(RACE_OWNERSHIP_KEY); } catch { /* storage unavailable */ } };

/** The track numbers 0..10 out of a prize snapshot (shared/prize-state.js); anything else is nothing owned. */
export function raceOwnershipTracks(value) {
  const list = Array.isArray(value?.tracks) ? value.tracks : [];
  return [...new Set(list.filter(n => Number.isInteger(n) && n >= 0 && n <= 10))].sort((a, b) => a - b);
}

export function validatedRoomPose(value) {
  const p = value?.position;
  if (!Array.isArray(p) || p.length !== 3 || !p.every(Number.isFinite)) return null;
  if (Math.abs(p[0]) > WALLS.x || Math.abs(p[2]) > WALLS.z || Math.abs(p[1] - EYE) > 0.35) return null;
  if (!Number.isFinite(value.yaw) || Math.abs(value.yaw) > 1e6 || !Number.isFinite(value.pitch) || value.pitch < -1.12 || value.pitch > 1.2) return null;
  const yaw = ((value.yaw + Math.PI) % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2) - Math.PI;
  return { position: p.slice(), yaw, pitch: value.pitch };
}

/** A room reload cannot accidentally consume a previous departure's pose. */
export function consumeRoomPose({ location: loc = browserLocation(), storage = browserStorage() } = {}) {
  try {
    if (!loc) return null;
    const here = new URL(loc.href);
    if (here.pathname !== '/backroom/index.html' || here.searchParams.get('raceReturn') !== '1') return null;
    const raw = storage?.getItem(RACE_POSE_KEY);
    forget(storage);
    if (!raw || raw.length > 512) return null;
    const saved = JSON.parse(raw);
    return saved.version === 1 ? validatedRoomPose(saved.pose) : null;
  } catch { forget(storage); return null; }
}

export function racePortalUrl(href, racePath = '/dtrh/race.html') {
  if (!RACE_PATHS.has(racePath)) return null;
  try {
    const here = new URL(href);
    if (!['http:', 'https:'].includes(here.protocol)) return null;
    const target = new URL(racePath, here.origin);
    target.searchParams.set('casino', '1');
    target.searchParams.set('back', CASINO_RETURN);
    return target.href;
  } catch { return null; }
}

export function createRacePortal({ hosted = false, send, on, getPose, getOwnership, beforeNavigate,
  navigate, location: loc = browserLocation(), storage = browserStorage(),
  racePath = '/dtrh/race.html', onRefused } = {}) {
  let pending = false, disposed = false;
  function refused(message) {
    if (!pending || message?.game !== 'race' || message.ok !== false) return;
    pending = false; forget(storage); onRefused?.(message);
  }
  const unsubscribe = typeof on === 'function' ? on('game-open-result', refused) : null;
  function open() {
    if (pending || disposed) return false;
    const target = hosted ? null : racePortalUrl(loc?.href, racePath);
    if ((!hosted && !target) || (hosted && typeof send !== 'function')) return false;
    pending = true;
    try {
      const pose = validatedRoomPose(getPose?.());
      forget(storage);
      if (pose) { try { storage?.setItem(RACE_POSE_KEY, JSON.stringify({ version: 1, pose })); } catch { /* spawn normally on return */ } }
      // Only the same-window door needs the list; a native host sends racingTracks itself.
      if (!hosted) { try { storage?.setItem(RACE_OWNERSHIP_KEY, JSON.stringify({ version: 1, tracks: raceOwnershipTracks(getOwnership?.()) })); } catch { /* the race owns nothing then */ } }
      if (hosted) send({ type: 'game-open', game: 'race' });
      else {
        beforeNavigate?.();
        if (navigate) navigate(target); else loc.assign(target);
      }
      return true;
    } catch {
      refused({ game: 'race', ok: false, reason: 'navigation' });
      return false;
    }
  }
  return { open, get pending() { return pending; }, dispose() { disposed = true; unsubscribe?.(); } };
}
