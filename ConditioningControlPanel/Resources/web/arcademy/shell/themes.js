/* ============================================================================
 * shell/themes.js - CAMPUS LOOK, the table and nothing else.
 *
 * COUNTER STOCK wave 2/3 ships two campus themes as Prize Counter unlocks
 * (`theme_drone`, `theme_snowday`). A theme is DATA: a palette bag the shell
 * lays over the existing `applyPalette` seam, plus the name of an optional
 * weather layer (shell/themefx.js draws it, this file never does).
 *
 * THE ANNEX'S LAW, taken to the letter. This module imports NOTHING - no
 * store, no bridge, no lexicon, no DOM. It is pure data plus four pure
 * helpers, so the whole table is importable in bare Node and every rule about
 * what a player may select is testable without a browser.
 *
 * WHY THE PALETTE MOVES THIRTEEN TOKENS AND NOT SIX. styles.css derives every
 * campus hue (`--campus-sky`, `--campus-plan`, `--campus-hall`, ...) from the
 * base tokens with `color-mix`, so moving ground, navy, panel, panel2, line,
 * the three inks, pink, lav, slate and gold reskins the plan, the chrome, the
 * counter and the report card in one write. Six tokens (init.palette's old
 * set) would have moved the text and left the campus lavender-blue under a
 * green sky.
 *
 * OWNERSHIP IS THE WALLET'S, NOT THIS FILE'S. `sku` names the row the Prize
 * Counter sells; the shell answers `owns(sku)` off `wallet.inv` and hands the
 * answer in. An unowned theme is not "locked" here - it is ABSENT from every
 * list this file builds, because a restock should appear, not be spoiled.
 * ==========================================================================*/

/** The page-owned meta key. Page-owned needs no C# change - `ArcademyMetaStore
 *  .Set` takes any new top-level key under its own caps (the recordsRoom*
 *  precedent, shell.js's records seam). */
export const THEME_META_KEY = 'campusTheme';

/** The house look. Always present, never owned, never for sale. */
export const STANDARD_ID = 'standard';

/** Every token a theme may move, and the CSS custom property it drives. This
 *  is the SAME map shell.js's PALETTE_TOKENS carries (a theme and a mod skin
 *  write through one seam); it is repeated here so a test can assert the two
 *  agree without importing the shell. */
export const THEME_TOKENS = Object.freeze({
  ground: '--ground',
  navy: '--navy',
  panel: '--panel',
  panel2: '--panel2',
  ink: '--ink',
  inkDim: '--ink-dim',
  inkFaint: '--ink-faint',
  accent: '--pink',
  accent2: '--lav',
  gold: '--gold',
  line: '--line',
  slate: '--slate',
  pinkDeep: '--pink-deep',
});

/**
 * THE TABLE. Order is the order the options row lists them (after House
 * Standard, which is always first).
 *
 * `fx` is a KEY, not a painter: shell/themefx.js owns the canvas and looks the
 * key up in its own table. A theme with `fx:null` costs exactly nothing at
 * runtime - the fx layer is never created for it.
 */
export const THEMES = Object.freeze([
  Object.freeze({
    id: 'drone',
    sku: 'theme_drone',
    nameKey: 'prize_theme_drone',
    nameEn: 'DRONE PROTOCOL',
    fx: 'drone',
    palette: Object.freeze({
      ground: '#06110C',
      navy: '#0A1F12',
      panel: '#102819',
      panel2: '#153524',
      ink: '#D6F5D0',
      inkDim: '#8FBF95',
      inkFaint: '#5E8A68',
      accent: '#5CE85C',
      accent2: '#9BE8A8',
      gold: '#B6FF6B',
      line: '#1E3B26',
      slate: '#4E7A5C',
      pinkDeep: '#3FA84F',
    }),
  }),
  Object.freeze({
    id: 'snowday',
    sku: 'theme_snowday',
    nameKey: 'prize_theme_snowday',
    nameEn: 'SNOW DAY',
    fx: 'snow',
    palette: Object.freeze({
      ground: '#0E1626',
      navy: '#101C33',
      panel: '#18263F',
      panel2: '#1F3050',
      ink: '#E8F1FA',
      inkDim: '#A8BFD8',
      inkFaint: '#7A93B5',
      accent: '#BFE3FF',
      accent2: '#D6EFFF',
      gold: '#FFE9B6',
      line: '#27395C',
      slate: '#5C7FB8',
      pinkDeep: '#7FB3D8',
    }),
  }),
]);

/** Legal ids, house first. Membership test, not a menu - see ownedThemes. */
export function themeIds() {
  return [STANDARD_ID].concat(THEMES.map((th) => th.id));
}

/** The row, or null. `standard` is not a row: it is the ABSENCE of one, which
 *  is what makes "revert" a single code path (remove what the theme set). */
export function themeById(id) {
  const want = String(id == null ? '' : id);
  for (const th of THEMES) if (th.id === want) return th;
  return null;
}

/** The row that sells a theme, or null. */
export function themeBySku(sku) {
  const want = String(sku == null ? '' : sku);
  for (const th of THEMES) if (th.sku === want) return th;
  return null;
}

/**
 * THE MENU. House Standard plus every theme the player actually owns, in table
 * order. An unowned theme does not appear at all - no ghost row, no padlock,
 * no "coming soon". A missing or throwing `owns` means owns nothing, which is
 * the right answer on a wallet that has never been written.
 *
 * @param {function(string):boolean} owns
 * @returns {Array<{id:string, sku:?string, nameKey:?string, nameEn:?string, fx:?string}>}
 */
export function ownedThemes(owns) {
  const ask = typeof owns === 'function' ? owns : () => false;
  const out = [{ id: STANDARD_ID, sku: null, nameKey: 'opt_theme_standard', nameEn: 'The usual', fx: null }];
  for (const th of THEMES) {
    let has = false;
    try { has = ask(th.sku) === true; } catch (e) { has = false; }
    if (has) out.push({ id: th.id, sku: th.sku, nameKey: th.nameKey, nameEn: th.nameEn, fx: th.fx });
  }
  return out;
}

/**
 * Clamp a stored pick to what is legal RIGHT NOW. Junk, an unknown id and a
 * theme whose sku is no longer in the wallet all answer `standard` - the same
 * shape leverPick() uses, and for the same reason: a pick the page thinks it
 * has and the wallet does not may never paint.
 */
export function clampThemeId(id, owns) {
  const want = String(id == null ? '' : id);
  if (want === STANDARD_ID || want === '') return STANDARD_ID;
  const th = themeById(want);
  if (!th) return STANDARD_ID;
  const ask = typeof owns === 'function' ? owns : () => false;
  let has = false;
  try { has = ask(th.sku) === true; } catch (e) { has = false; }
  return has ? th.id : STANDARD_ID;
}

/** The fx key for a pick, or null (standard, unowned, or a theme with no fx). */
export function themeFxFor(id, owns) {
  const th = themeById(clampThemeId(id, owns));
  return th && th.fx ? th.fx : null;
}

export default THEMES;
