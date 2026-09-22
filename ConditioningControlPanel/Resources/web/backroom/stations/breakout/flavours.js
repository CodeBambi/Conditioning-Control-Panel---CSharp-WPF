/**
 * "Pick a flavour": what the picture bricks and bubbles wear (owner, 2026-09-21). A flavour is a short list of
 * Scrolller niches. The player is shown every niche inside one, can switch any of them off, switch a suggested
 * extra on, and add niches of their own; that lives per flavour in localStorage (CUSTOM_KEY).
 *
 * Every niche named here was checked against Scrolller on 2026-09-21. `subs` answer with clips AND stills and are on
 * by default; `extras` are suggestions, off until asked for (some are stills only). A niche that answers nothing
 * deals an empty wall, so check before adding one.
 *
 * The web shell owns the media source (window.__brMedia, cclabs-web web/fx.js). This module only asks it, so where
 * that host is missing (the desktop room, the dev harness) nobody is asked anything.
 * The shell keeps ONE niche list for the Back Room and Breakout (localStorage br.media.v1): a pick replaces it.
 */
export const FLAVOURS = [
  { id: 'trance', name: 'Trance', line: 'Spirals and soft voices.', tint: '#b99cff', subs: ['EroticHypnosis', 'HypnoHentai'], extras: ['GoonCaves'] },
  { id: 'pink', name: 'Pink', line: 'Bimbo, top to bottom.', tint: '#ff87c7', subs: ['bimbofication', 'Bimbos', 'BimboOrNot'], extras: ['bimbo', 'BimboHypno'] },
  { id: 'frills', name: 'Frills', line: 'Lace, bows, best behaviour.', tint: '#ffb3d9', subs: ['sissyhypno', 'SissyInspiration', 'Sissyperfection'], extras: ['sissydressing', 'sissycaptions'] },
  { id: 'shiny', name: 'Shiny', line: 'Latex, rubber, drones.', tint: '#5fffd0', subs: ['ShinyPorn', 'Dronification', 'latexcosplay'], extras: ['LatexUnderClothes', 'rubber'] },
  { id: 'censored', name: 'Censored', line: 'Look, never see.', tint: '#9fb4c8', subs: ['censoredporn', 'BetaCensored', 'Censored_Porn'], extras: ['censored'] },
];
/** The player's own list: nothing inside until they add to it. Offered in Options, never on the Start card. */
export const MINE = { id: 'mine', name: 'Mine', line: 'Your own niches.', tint: '#c9c9d2', subs: [], extras: [] };
export const CUSTOM_KEY = 'bo.flavours.v1', PICK_KEY = 'bo.flavour.v1', MAX_NICHES = 8;   // the shell keeps eight at most

const lower = s => String(s).toLowerCase();
const has = (list, name) => Array.isArray(list) && list.some(s => lower(s) === lower(name));
const without = (list, name) => (Array.isArray(list) ? list : []).filter(s => lower(s) !== lower(name));
const same = (a, b) => a.length === b.length && a.map(lower).sort().join() === b.map(lower).sort().join();
export const flavourById = id => (id === MINE.id ? MINE : FLAVOURS.find(f => f.id === id) || null);

/** A niche as the player typed it (a name, r/name, or a reddit / scrolller link) to the bare name, or '' when it is not one. */
export function cleanNiche(text) {
  let s = String(text == null ? '' : text).trim();
  for (const lead of ['https://', 'http://']) if (lower(s).startsWith(lead)) s = s.slice(lead.length);
  for (const lead of ['www.', 'old.', 'reddit.com/', 'scrolller.com/', '/r/', 'r/']) if (lower(s).startsWith(lead)) s = s.slice(lead.length);
  s = s.split('/')[0].split('?')[0];
  return /^[a-zA-Z0-9_]{2,40}$/.test(s) ? s : '';
}
const one = c => ({ on: Array.isArray(c && c.on) ? c.on.slice() : [], off: Array.isArray(c && c.off) ? c.off.slice() : [], added: Array.isArray(c && c.added) ? c.added.slice() : [] });

/** Everything inside a flavour, as the player sees it: [{ name, on, kind: 'core' | 'extra' | 'added' }]. */
export function nichesOf(flavour, custom) {
  const c = one(custom), out = [];
  for (const name of flavour.subs) out.push({ name, kind: 'core', on: !has(c.off, name) });
  for (const name of flavour.extras || []) out.push({ name, kind: 'extra', on: has(c.on, name) });
  for (const name of c.added) if (!out.some(n => lower(n.name) === lower(name))) out.push({ name, kind: 'added', on: !has(c.off, name) });
  return out;
}
/** The niches a flavour sends to the shell: the ones switched on, eight at most. */
export const liveSubs = (flavour, custom) => nichesOf(flavour, custom).filter(n => n.on).map(n => n.name).slice(0, MAX_NICHES);

export function toggleNiche(flavour, custom, name) {
  const c = one(custom), n = nichesOf(flavour, c).find(x => lower(x.name) === lower(name));
  if (!n) return c;
  if (n.kind === 'extra') c.on = n.on ? without(c.on, name) : [...c.on, n.name];
  else c.off = n.on ? [...c.off, n.name] : without(c.off, name);
  return c;
}
/** Add one niche of the player's own. Returns { custom, error }: error is a short line for the card, '' when it went in. */
export function addNiche(flavour, custom, text) {
  const c = one(custom), name = cleanNiche(text);
  if (!name) return { custom: c, error: 'That is not a niche name. Letters, numbers and _ only.' };
  const known = nichesOf(flavour, c).find(n => lower(n.name) === lower(name));
  if (known) return { custom: known.on ? c : toggleNiche(flavour, c, known.name), error: known.on ? 'Already in.' : '' };
  if (nichesOf(flavour, c).length >= MAX_NICHES + 2) return { custom: c, error: 'That is plenty. Remove one first.' };
  c.added.push(name); return { custom: c, error: '' };
}
export function removeNiche(custom, name) { const c = one(custom); c.added = without(c.added, name); c.off = without(c.off, name); return c; }

/** All the player's changes, { [flavourId]: { on, off, added } }, from a store with get / set (station.js has one). */
export function readCustom(store) { try { const v = JSON.parse(store.get(CUSTOM_KEY) || '{}'); return v && typeof v === 'object' && !Array.isArray(v) ? v : {}; } catch (e) { return {}; } }
export function writeCustom(store, all) { try { store.set(CUSTOM_KEY, JSON.stringify(all)); } catch (e) { /* a full or private store keeps the defaults */ } }

/** The shell's media switch, or null where there is none (then nobody is asked anything). */
export function flavourHost(win = typeof window !== 'undefined' ? window : null) {
  const m = win && win.__brMedia;
  return m && typeof m.set === 'function' && typeof m.get === 'function' ? m : null;
}
/** What the shell is showing right now: the niches switched on, or null when the source is not Scrolller. */
export function shellNiches(host) {
  let now = null; try { now = host.get(); } catch (e) { return null; }
  if (!now || now.mode !== 'scrolller' || !Array.isArray(now.sources)) return null;
  return now.sources.filter(s => !has(now.disabledSources, s));
}
/** Which flavour the shell's niches are, the player's changes counted, or null when the list is nobody's. */
export function currentFlavour(host, all = {}) {
  const live = shellNiches(host);
  if (!live) return null;
  return [...FLAVOURS, MINE].find(f => { const subs = liveSubs(f, all[f.id]); return subs.length > 0 && same(subs, live); }) || null;
}
/**
 * Switch the shell to a flavour as the player has it. Resolves true once the new pictures are warm, false on any
 * refusal or an empty list: the caller carries on either way, a flavour never stands between the player and the ball.
 */
export async function applyFlavour(host, flavour, custom) {
  if (!host || !flavour) return false;
  const subs = liveSubs(flavour, custom);
  if (!subs.length) return false;
  try { await host.set('scrolller', subs.join(','), []); return true; } catch (e) { return false; }
}
