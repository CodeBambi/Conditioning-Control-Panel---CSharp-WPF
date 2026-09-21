/**
 * "Pick a flavour": after Start, the player chooses what the picture bricks and bubbles wear (owner, 2026-09-21).
 * A flavour is a short list of Scrolller niches. Every niche here was checked against Scrolller on 2026-09-21 and
 * answers with clips AND stills; a niche that answers nothing deals an empty wall, so check before adding one.
 *
 * The web shell owns the media source (window.__brMedia, cclabs-web web/fx.js). This module only asks it, so where
 * that host is missing (the desktop room, the dev harness) there is no prompt and Start starts the game.
 * The shell keeps ONE niche list for the Back Room and Breakout (localStorage br.media.v1): picking a flavour
 * replaces it, which is why "keep mine" is always on the card.
 */
export const FLAVOURS = [
  { id: 'trance', name: 'Trance', line: 'Spirals and soft voices.', tint: '#b99cff', subs: ['EroticHypnosis', 'HypnoHentai'] },
  { id: 'pink', name: 'Pink', line: 'Bimbo, top to bottom.', tint: '#ff87c7', subs: ['bimbofication', 'Bimbos', 'BimboOrNot'] },
  { id: 'frills', name: 'Frills', line: 'Lace, bows, best behaviour.', tint: '#ffb3d9', subs: ['sissyhypno', 'SissyInspiration', 'Sissyperfection'] },
  { id: 'shiny', name: 'Shiny', line: 'Latex, rubber, drones.', tint: '#5fffd0', subs: ['ShinyPorn', 'Dronification', 'latexcosplay'] },
];
const same = (a, b) => a.length === b.length && [...a].map(s => s.toLowerCase()).sort().join() === [...b].map(s => s.toLowerCase()).sort().join();

/** The shell's media switch, or null where there is none (then nobody is asked anything). */
export function flavourHost(win = typeof window !== 'undefined' ? window : null) {
  const m = win && win.__brMedia;
  return m && typeof m.set === 'function' && typeof m.get === 'function' ? m : null;
}
/** Which flavour the player's current niches are, or null when the list is their own (or the source is not Scrolller). */
export function currentFlavour(host) {
  let now = null; try { now = host.get(); } catch (e) { return null; }
  if (!now || now.mode !== 'scrolller' || !Array.isArray(now.sources)) return null;
  const off = new Set((now.disabledSources || []).map(s => String(s).toLowerCase()));
  const live = now.sources.filter(s => !off.has(String(s).toLowerCase()));
  return FLAVOURS.find(f => same(f.subs, live)) || null;
}
/**
 * Switch the shell to a flavour. Resolves true once the new pictures are warm, false on any refusal: the caller
 * starts the game either way, a flavour never stands between the player and the first ball.
 */
export async function applyFlavour(host, flavour) {
  if (!host || !flavour || !Array.isArray(flavour.subs) || !flavour.subs.length) return false;
  try { await host.set('scrolller', flavour.subs.join(','), []); return true; } catch (e) { return false; }
}
