/* ============================================================================
 * ui/flavours.js - "pick a flavour" for the Goon Game (2026-09-23).
 *
 * A PORT of Resources/web/backroom/stations/breakout/flavours.js. The goon tree
 * cannot import across web trees (the site vendors each tree on its own), so the
 * table is COPIED, and test/selftest-flavours.js reads both files and fails the
 * moment the two FLAVOURS tables drift. Change a niche there, change it here.
 *
 * What is NOT ported: the web-shell half (window.__brMedia, readCustom /
 * writeCustom, applyFlavour). Here the HOST owns persistence: the pick and the
 * player's changes ride the `init` frame in (`init.media`) and go back out as ONE
 * `media-flavour` frame, built by mediaFlavourFrame() below. Nothing here touches
 * localStorage, so a WebView profile reset keeps the player's picks.
 *
 * Pure: no DOM, no imports, safe under node.
 * ==========================================================================*/

/* ---- COPIED VERBATIM from breakout/flavours.js (the drift test compares these). */
export const FLAVOURS = [
  { id: 'trance', name: 'Trance', line: 'Spirals and soft voices.', tint: '#b99cff', subs: ['EroticHypnosis', 'HypnoHentai'], extras: ['GoonCaves'] },
  { id: 'pink', name: 'Pink', line: 'Bimbo, top to bottom.', tint: '#ff87c7', subs: ['bimbofication', 'Bimbos', 'BimboOrNot'], extras: ['bimbo', 'BimboHypno'] },
  { id: 'frills', name: 'Frills', line: 'Lace, bows, best behaviour.', tint: '#ffb3d9', subs: ['sissyhypno', 'SissyInspiration', 'Sissyperfection'], extras: ['sissydressing', 'sissycaptions'] },
  { id: 'shiny', name: 'Shiny', line: 'Latex, rubber, drones.', tint: '#5fffd0', subs: ['ShinyPorn', 'Dronification', 'latexcosplay'], extras: ['LatexUnderClothes', 'rubber'] },
  { id: 'censored', name: 'Censored', line: 'Look, never see.', tint: '#9fb4c8', subs: ['censoredporn', 'BetaCensored', 'Censored_Porn'], extras: ['censored'] },
];
/** The player's own list: nothing inside until they add to it. Offered in Options, never on the first-run card. */
export const MINE = { id: 'mine', name: 'Mine', line: 'Your own niches.', tint: '#c9c9d2', subs: [], extras: [] };
export const MAX_NICHES = 8;   // the host keeps eight at most, and re-checks it
/* ---- end of the copied table. */

/** Every id the wire may carry in `flavour`. '' = never picked (first run). */
export const FLAVOUR_IDS = Object.freeze([...FLAVOURS.map(f => f.id), MINE.id]);

const lower = s => String(s).toLowerCase();
const has = (list, name) => Array.isArray(list) && list.some(s => lower(s) === lower(name));
const without = (list, name) => (Array.isArray(list) ? list : []).filter(s => lower(s) !== lower(name));
export const flavourById = id => (id === MINE.id ? MINE : FLAVOURS.find(f => f.id === id) || null);

/** A niche as the player typed it (a name, r/name, or a reddit / scrolller link) to the bare name, or '' when it is not one. */
export function cleanNiche(text) {
  let s = String(text == null ? '' : text).trim();
  for (const lead of ['https://', 'http://']) if (lower(s).startsWith(lead)) s = s.slice(lead.length);
  for (const lead of ['www.', 'old.', 'reddit.com/', 'scrolller.com/', '/r/', 'r/']) if (lower(s).startsWith(lead)) s = s.slice(lead.length);
  s = s.split('/')[0].split('?')[0];
  return /^[a-zA-Z0-9_]{2,40}$/.test(s) ? s : '';
}
/** One flavour's changes, copied and made safe: only arrays of valid niche names survive. */
const names = list => (Array.isArray(list) ? list : []).map(cleanNiche).filter(Boolean).slice(0, 24);
const one = c => ({ on: names(c && c.on), off: names(c && c.off), added: names(c && c.added) });

/** Everything inside a flavour, as the player sees it: [{ name, on, kind: 'core' | 'extra' | 'added' }]. */
export function nichesOf(flavour, custom) {
  const c = one(custom), out = [];
  for (const name of flavour.subs) out.push({ name, kind: 'core', on: !has(c.off, name) });
  for (const name of flavour.extras || []) out.push({ name, kind: 'extra', on: has(c.on, name) });
  for (const name of c.added) if (!out.some(n => lower(n.name) === lower(name))) out.push({ name, kind: 'added', on: !has(c.off, name) });
  return out;
}
/** The niches a flavour sends to the host: the ones switched on, eight at most. */
export const liveSubs = (flavour, custom) => nichesOf(flavour, custom).filter(n => n.on).map(n => n.name).slice(0, MAX_NICHES);

export function toggleNiche(flavour, custom, name) {
  const c = one(custom), n = nichesOf(flavour, c).find(x => lower(x.name) === lower(name));
  if (!n) return c;
  if (n.kind === 'extra') c.on = n.on ? without(c.on, name) : [...c.on, n.name];
  else c.off = n.on ? [...c.off, n.name] : without(c.off, name);
  return c;
}
/**
 * Add one niche of the player's own. Returns { custom, error }: error is a CODE here, not copy
 * ('' went in, 'bad' not a name, 'dup' already on, 'full' too many), so ui/strings.js owns the words.
 */
export function addNiche(flavour, custom, text) {
  const c = one(custom), name = cleanNiche(text);
  if (!name) return { custom: c, error: 'bad' };
  const known = nichesOf(flavour, c).find(n => lower(n.name) === lower(name));
  if (known) return { custom: known.on ? c : toggleNiche(flavour, c, known.name), error: known.on ? 'dup' : '' };
  if (nichesOf(flavour, c).length >= MAX_NICHES + 2) return { custom: c, error: 'full' };
  c.added.push(name); return { custom: c, error: '' };
}
export function removeNiche(custom, name) { const c = one(custom); c.added = without(c.added, name); c.off = without(c.off, name); return c; }

/* ---------------------------------------------------------------- goon-only */

/**
 * `init.media` made safe: { flavour, custom, online }. A host that predates the block (or a
 * standalone page) hands nothing, which reads as null - "no online pictures here at all".
 */
export function readMediaInit(m) {
  if (!m || typeof m !== 'object') return null;
  const flavour = FLAVOUR_IDS.includes(m.flavour) ? m.flavour : '';
  const custom = {};
  const src = m.custom && typeof m.custom === 'object' && !Array.isArray(m.custom) ? m.custom : {};
  for (const id of FLAVOUR_IDS) if (src[id]) custom[id] = one(src[id]);
  return { flavour, custom, online: m.online !== false };
}

/** The ONE page -> host frame for a pick or a closed options sheet. */
export function mediaFlavourFrame(state) {
  const s = state || {};
  const f = flavourById(s.flavour);
  const custom = {};
  for (const id of Object.keys(s.custom || {})) if (FLAVOUR_IDS.includes(id)) custom[id] = one(s.custom[id]);
  return {
    type: 'media-flavour',
    flavour: f ? f.id : '',
    custom,
    subs: f ? liveSubs(f, custom[f.id]) : [],
    online: s.online !== false,
  };
}

/** Did anything the host cares about move? (Compares the frames, so a no-op close sends nothing.) */
export function sameMediaState(a, b) {
  return JSON.stringify(mediaFlavourFrame(a)) === JSON.stringify(mediaFlavourFrame(b));
}

/** `online-media` made safe for the page: state, counts and the two url lists. */
export function readOnlineFrame(m) {
  const st = ['loading', 'ready', 'empty', 'off', 'error'];
  const x = m || {};
  const list = l => (Array.isArray(l) ? l : []).filter(e => e && typeof e.url === 'string' && e.url);
  const images = list(x.images), videos = list(x.videos);
  const p = x.progress || {};
  return {
    state: st.includes(x.state) ? x.state : 'loading',
    subs: Array.isArray(x.subs) ? x.subs.map(String) : [],
    images, videos,
    have: Number.isFinite(p.have) ? p.have : images.length + videos.length,
    want: Number.isFinite(p.want) ? p.want : 0,
  };
}
