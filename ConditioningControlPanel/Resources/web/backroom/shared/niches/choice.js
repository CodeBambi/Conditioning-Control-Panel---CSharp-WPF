/* ============================================================================
 * shared/niches/choice.js - the saved picture choice, for any game that mixes
 * Scrolller pictures in (2026-09-30, Piece by Piece first).
 *
 * THE RULE THE OWNER SET: a game that can show online pictures asks ONCE, at the
 * first start, which pictures to use (a flavour, or the player's own), records
 * the answer and never asks again. The answer stays editable any time from the
 * game's niche manager (shared/niches/picker.js, mode 'manage').
 *
 * The flavour table and the niche edits are Breakout's (stations/breakout/
 * flavours.js), imported, not copied: that file is the one the Goon copy is
 * drift-tested against, and it sits in this same web tree, so every host that
 * vendors the Back Room (the site, the phone packs) carries it already.
 *
 * WHO STORES IT. Never this module. The host keeps the choice (desktop:
 * AppSettings, the site: the shim's localStorage, which IS the saved opt-in on
 * that browser) and hands it in as one frame; a change goes back out as one
 * frame. This module only reads and builds those frames, so it is pure: no DOM,
 * no storage, safe under node.
 *
 *   state = {
 *     flavour   '' | a FLAVOUR_IDS id     '' = no flavour (own pictures, or the app's)
 *     online    bool                       false = "no online pictures, use mine"
 *     custom    { [id]: { on, off, added } }  the player's edits per flavour
 *     chosen    bool                       a choice is saved: never ask again
 *     appWide   bool                       the app's own online source is on (desktop)
 *     library   bool                       the host has a library of the player's own
 *     canOnline bool                       this host can fetch online pictures at all
 *     last      '' | id                    the last flavour picked (a preselection)
 *   }
 * ==========================================================================*/

import {
  FLAVOURS, MINE, MAX_NICHES, flavourById, cleanNiche, nichesOf, liveSubs,
  toggleNiche, addNiche, removeNiche,
} from '../../stations/breakout/flavours.js';

export { FLAVOURS, MINE, MAX_NICHES, flavourById, cleanNiche, nichesOf, liveSubs, toggleNiche, addNiche, removeNiche };

/** Every id a frame may carry in `flavour`. */
export const FLAVOUR_IDS = Object.freeze([...FLAVOURS.map((f) => f.id), MINE.id]);

const names = (list) => (Array.isArray(list) ? list : []).map(cleanNiche).filter(Boolean).slice(0, 24);
/** One flavour's edits, copied and made safe: only arrays of valid niche names survive. */
export const cleanEdits = (c) => ({ on: names(c && c.on), off: names(c && c.off), added: names(c && c.added) });
const cleanCustom = (src) => {
  const out = {};
  const obj = src && typeof src === 'object' && !Array.isArray(src) ? src : {};
  for (const id of FLAVOUR_IDS) if (obj[id]) out[id] = cleanEdits(obj[id]);
  return out;
};
const idOr = (id) => (FLAVOUR_IDS.includes(id) ? id : '');

/**
 * A host's state frame made safe, or null when there is none (a plain browser,
 * or a host that has no online pictures to offer at all: nothing is asked then).
 * Absent capability flags read as the desktop's: a library, online possible.
 */
export function readChoice(m) {
  if (!m || typeof m !== 'object') return null;
  const flavour = idOr(m.flavour);
  const online = m.online !== false;
  return {
    flavour,
    online,
    custom: cleanCustom(m.custom),
    chosen: m.chosen === true,
    appWide: m.appWide === true,
    library: m.library !== false,
    canOnline: m.canOnline !== false,
    last: idOr(m.last) || flavour,
  };
}

/** Should a start ask first: a host spoke, Distraction is on, and nothing is saved. */
export function needsChoice(state, experience = 'distraction') {
  return !!state && experience !== 'classic' && !state.chosen;
}

/** Which of the choices the state is: 'own', 'app' or a flavour id. */
export function pickOf(state) {
  if (!state || state.online === false) return 'own';
  if (!state.flavour) return state.appWide ? 'app' : 'own';
  return state.flavour;
}

/** The state after picking `id`: 'own' (no online pictures), 'app' (the app's source) or a flavour id. */
export function choose(state, id) {
  const s = { ...(state || readChoice({})), chosen: true };
  if (id === 'own' || s.canOnline === false) return { ...s, online: false, flavour: '' };
  if (id === 'app') return { ...s, online: true, flavour: '' };
  const f = flavourById(id);
  return f ? { ...s, online: true, flavour: f.id, last: f.id } : s;
}

/** Apply one niche edit (fn(flavour, edits) -> edits | { custom, error }) to flavour `id`. Returns { state, error }. */
export function editNiches(state, id, fn) {
  const f = flavourById(id);
  if (!state || !f) return { state, error: '' };
  const out = fn(f, (state.custom || {})[f.id]);
  const edits = cleanEdits(out && out.custom ? out.custom : out);
  return { state: { ...state, custom: { ...(state.custom || {}), [f.id]: edits } }, error: (out && out.error) || '' };
}

/**
 * The ONE page -> host frame for a choice or a closed manager: { flavour, custom,
 * subs, online, chosen }. The caller types it for its host. Always `chosen: true`:
 * the page only ever sends one because the player chose.
 */
export function choiceFrame(state) {
  const s = state || {};
  const f = s.online === false ? null : flavourById(s.flavour);
  const custom = cleanCustom(s.custom);
  return {
    flavour: f ? f.id : '',
    custom,
    subs: f ? liveSubs(f, custom[f.id]) : [],
    online: s.online !== false,
    chosen: true,
  };
}

/** Did anything the host keeps move? Compares the frames, so a no-op close sends nothing. */
export function sameChoice(a, b) {
  return JSON.stringify(choiceFrame(a)) === JSON.stringify(choiceFrame(b));
}

/** "r/a  r/b" for a flavour as the player has it. */
export function nicheLine(state, id) {
  const f = flavourById(id);
  if (!f) return '';
  return liveSubs(f, ((state && state.custom) || {})[f.id]).map((s) => 'r/' + s).join('  ');
}

/**
 * The words for "no online pictures": what the player keeps. A host with no
 * library of the player's own (the site) has nothing to keep, and says so.
 */
export function ownLine(state) {
  if (state && state.library === false) return 'The effects run without pictures here.';
  return 'Your own library only.';
}

/** One status line for a host's online-media frame ({ state, have }). */
export function statusLine(state, status) {
  if (!state) return '';
  if (state.canOnline === false) return state.library === false ? 'No online pictures on this device.' : 'Online pictures are not available here. Your own library carries on.';
  if (state.online === false) return state.library === false ? 'No online pictures. The effects run without them.' : 'Your own pictures only.';
  if (!state.flavour && !state.appWide) return 'Pick a flavour to mix in online pictures.';
  const s = status || {};
  const have = Number(s.have) || 0;
  if (s.state === 'loading') return have > 0 ? `Finding pictures, ${have} so far.` : 'Finding pictures.';
  if (s.state === 'ready') return `${have} online pictures in the mix.`;
  if (s.state === 'empty') return 'Nothing found there. Try another flavour or add a niche.';
  if (s.state === 'error') return 'Scrolller did not answer. Your own pictures carry on.';
  if (s.state === 'off' && state.flavour) return 'No niches on. Switch one on or add one.';
  return '';
}
