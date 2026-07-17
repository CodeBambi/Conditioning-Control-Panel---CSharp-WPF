/* ============================================================================
 * crafting.js - THE BOUDOIR's data spine: the six materials that drop in the
 * tube, the 21 pictogram recipes, and the grid matcher.
 *
 * PICTOGRAM RULE: a recipe's 3x3 arrangement literally DRAWS the object
 * (Minecraft-boat style). Matching tolerates translation (the drawing can sit
 * anywhere in the grid) and horizontal mirroring (a left-handed key is still
 * a key) - both collapse into one canonical form at load time.
 *
 * THIS FILE IS THE SINGLE SOURCE OF TRUTH FOR GRID SHAPES. The C# side
 * (ChaosCraftingIds.cs) holds only the id whitelists - if you add a recipe or
 * material here, add its id there too, or the bridge will reject the craft.
 *
 * Part 2 wires real effects: `effect` is the short mechanical line shown in
 * KEPT THINGS / craft cards. effect: null = deliberately dormant (the_loom
 * gates its pane instead; extensions + the_pink_heart wait for later parts).
 * ==========================================================================*/

const ART = 'https://ccp.art/';   // bundled assets/Chaos/<folder>/<id>.png

// ============================ materials ============================
// ch = the single-character grid alphabet used by recipe strings below.
// sprite art is Part-2+; in-run bubbles use tint gradient + glyph label.

export const MATERIALS = [
  { id: 'silicone', ch: 'S', name: 'Silicone', glyph: '🍮', weight: 22, tint: 'rgb(255,214,222)' },
  { id: 'makeup',   ch: 'M', name: 'Makeup',   glyph: '💄', weight: 20, tint: 'rgb(255,96,160)'  },
  { id: 'pills',    ch: 'P', name: 'Pink Pills', glyph: '💊', weight: 20, tint: 'rgb(255,148,196)' },
  { id: 'lace',     ch: 'L', name: 'Lace',     glyph: '🎀', weight: 14, tint: 'rgb(244,226,255)' },
  { id: 'lube',     ch: 'O', name: 'Lube',     glyph: '🧴', weight: 12, tint: 'rgb(168,230,255)' },
  { id: 'chrome',   ch: 'C', name: 'Chrome',   glyph: '⛓', weight: 12, tint: 'rgb(206,214,228)' },
];

export const MATERIAL_IDS = MATERIALS.map((m) => m.id);
export const MAT_BY_ID = Object.fromEntries(MATERIALS.map((m) => [m.id, m]));
const CH_TO_ID = Object.fromEntries(MATERIALS.map((m) => [m.ch, m.id]));
const ID_TO_CH = Object.fromEntries(MATERIALS.map((m) => [m.id, m.ch]));

// ============================ recipes ============================
// grid: rows top->bottom, '/' row separator, '.' = empty cell.
// resultKind: 'consumable' stacks in KEPT THINGS; 'permanent' crafts once.
// effect: null = Part-2 seam (real mechanics + consumable/permanent re-audit).

export const RECIPES = [
  { id: 'slippery_slope', name: 'Slippery Slope', glyph: '💨', grid: 'O../.O./..O',
    resultKind: 'consumable',
    desc: 'a smear of lube down the incline. one run, everything moves a little faster, a little smoother.',
    flavor: 'the first step was the hard one. the rest is geometry.',
    effect: 'one use · 10s: the fall runs ×1.5 faster and heat builds twice as fast.' },
  { id: 'rubber_up', name: 'Rubber Up', glyph: '🛡', grid: 'SSS/S.S/...',
    resultKind: 'consumable',
    desc: 'a silicone dome to hide under. one run, one extra mistake forgiven.',
    flavor: 'protection. how responsible of you.',
    effect: 'one use · 8s: nothing can touch you.' },
  { id: 'skeleton_key', name: 'Skeleton Key', glyph: '🗝', grid: '.C./.C./.CO',
    resultKind: 'permanent',
    desc: 'a chrome shaft with one lubed tooth. doors stop being suggestions - reroll the ones you don’t like.',
    flavor: 'it doesn’t open every door. just the ones you keep staring at.',
    effect: '+1 draft reroll every descent.' },
  { id: 'locked_collar', name: 'Locked Collar', glyph: '📿', grid: 'L.L/L.L/.C.',
    resultKind: 'permanent',
    desc: 'two lace straps meeting at a chrome clasp. the collar, but it locks now.',
    flavor: 'you could take it off before. you never did. now the question is settled.',
    effect: '+1 collar save every descent.' },
  { id: 'the_cage', name: 'The Cage', glyph: '🔒', grid: 'CCC/CSC/CCC',
    resultKind: 'permanent',
    desc: 'chrome closed around something soft. unlocks the Denial run modifier.',
    flavor: 'chrome keeps what silicone promises.',
    effect: 'unlocks the DENIAL modifier: no hearts fall, everything pays +50%.' },
  { id: 'ragdoll', name: 'Ragdoll', glyph: '🧸', grid: '.M./LLL/.L.',
    resultKind: 'permanent',
    desc: 'a lace doll with a painted face. stitches a second life - save slot 2.',
    flavor: 'she made a spare of you. she says it’s flattering.',
    effect: 'stitches save slot 2 open.' },
  { id: 'porcelain', name: 'Porcelain', glyph: '🎎', grid: '.P./LLL/.L.',
    resultKind: 'permanent',
    desc: 'a lace doll with a pill-white head. stitches a third life - save slot 3.',
    flavor: 'this one is more fragile. handle yourself with care.',
    effect: 'stitches save slot 3 open.' },
  { id: 'the_ballerina', name: 'The Ballerina', glyph: '🩰', grid: '.M./.L./CCC',
    resultKind: 'permanent',
    desc: 'a painted dancer on a lace stem over a chrome base. a music box for the dollhouse.',
    flavor: 'wind her up and she spins. sound familiar?',
    effect: 'a music-box bed for the fall — toggle it in the audio panel.' },
  { id: 'the_compact', name: 'The Compact', glyph: '🪞', grid: 'CCC/CMC/CCC',
    resultKind: 'permanent',
    desc: 'makeup sealed in a chrome shell. flip it open after a descent for the full run recap.',
    flavor: 'a little mirror that remembers everything you did down there.',
    effect: 'the recap gains the mirror — picks, haul, route.' },
  { id: 'the_loom', name: 'The Loom', glyph: '🌀', grid: 'MM./.M./.MM',
    resultKind: 'permanent',
    desc: 'a spiral drawn in pure makeup. the recipe is the product - unlocks the Spiral Maker.',
    flavor: 'you drew the spiral yourself. remember that, later, when it draws you.',
    effect: 'opens THE LOOM in THE BOUDOIR — spin a spiral of your own.' },
  { id: 'extensions', name: 'Extensions', glyph: '💇', grid: 'CCC/SSS/SSS',
    resultKind: 'permanent',
    desc: 'chrome clips over soft strands. clip in a little more headroom - a dial goes one higher.',
    flavor: 'longer. just a little longer. that’s what you said last time.', effect: null },
  { id: 'lipstick', name: 'Lipstick', glyph: '💋', grid: '.M./.C./.C.',
    resultKind: 'permanent',
    desc: 'a painted tip on a chrome tube. the first Vanity piece - bubble skins.',
    flavor: 'the tip defines the object. the shade defines you.',
    effect: 'bubble skins for the tube’s treats — pick a shade in THE BOUDOIR.' },
  { id: 'the_hourglass', name: 'The Hourglass', glyph: '⌛', grid: 'CCC/.P./CCC',
    resultKind: 'permanent',
    desc: 'a single pill pinched between chrome caps. lets you pause mid-descent.',
    flavor: 'time didn’t stop. it’s just holding its breath for you.',
    effect: 'Esc truly holds the fall — pause mid-descent.' },
  { id: 'the_timepiece', name: 'The Timepiece', glyph: '⏱', grid: 'CCC/CPC/CCC',
    resultKind: 'permanent',
    desc: 'a pill sealed in a chrome case. the descent ticks a touch slower.',
    flavor: 'tick. tock. slower now. you can savour it.',
    effect: 'every descent lasts 10% longer.' },
  { id: 'the_ring_box', name: 'The Ring Box', glyph: '💍', grid: 'CCC/CLC/CCC',
    resultKind: 'permanent',
    desc: 'lace cushioned in a chrome box. keep whole Looks - loadout presets.',
    flavor: 'she keeps your favourite arrangements. she noticed you have favourites.',
    effect: 'keep up to 3 saved Looks in the paint picker.' },
  { id: 'the_corset', name: 'The Corset', glyph: '👗', grid: 'L.L/.L./L.L',
    resultKind: 'permanent',
    desc: 'lace crossed tight in an X. holds you together - +1 max resistance.',
    flavor: 'it doesn’t squeeze. it reminds.',
    effect: '+1 starting resistance every descent.' },
  { id: 'sugar_cube', name: 'Sugar Cube', glyph: '🍬', grid: 'PP./PP./...',
    resultKind: 'consumable',
    desc: 'pills pressed into a neat little cube. one run, the White Rabbit comes when called.',
    flavor: 'he can smell it. he pretends he can’t. he comes anyway.',
    effect: 'one use: the White Rabbit comes immediately.' },
  { id: 'the_shot', name: 'The Shot', glyph: '💉', grid: '.P./.S./.C.',
    resultKind: 'permanent',
    desc: 'a pill through silicone on a chrome needle. it stays in your system - a standing XP drip.',
    flavor: 'just a little pinch. it barely even stings anymore.',
    effect: '+4% drops payout per shot taken (stacks to +40%).', repeatable: true },
  { id: 'headphones', name: 'Headphones', glyph: '🎧', grid: 'CCC/S.S/...',
    resultKind: 'permanent',
    desc: 'a chrome band over two soft cups. alternate mantra packs for the descent.',
    flavor: 'noise cancelling. the noise being your own opinion.',
    effect: 'alternate word packs for the descent — choose in THE BOUDOIR.' },
  { id: 'the_padlock', name: 'The Padlock', glyph: '🔐', grid: 'C.C/CCC/CCC',
    resultKind: 'permanent',
    desc: 'a chrome body under a chrome shackle. pin one boon so the first draft always offers it.',
    flavor: 'commitment, in hardware form.',
    effect: 'pin one mantra — the first draft of every descent offers it.' },
  { id: 'the_pink_heart', name: 'The Pink Heart', glyph: '💖', grid: 'P.P/PPP/.P.',
    resultKind: 'permanent',
    desc: 'a heart built entirely of pills. the last picture. what it does isn’t written anywhere.',
    flavor: 'you already know what it costs. you’ve been saving up without deciding to.', effect: null },
];

export const RECIPE_IDS = RECIPES.map((r) => r.id);
export const RECIPE_BY_ID = Object.fromEntries(RECIPES.map((r) => [r.id, r]));

// Consumables that dock as single-charge toys at run start (mirrored in
// ChaosCraftingIds.Consumables - the bridge only decrements these ids).
export const CONSUMABLE_IDS = ['slippery_slope', 'rubber_up', 'sugar_cube'];

// ============================ canonical matcher ============================
// A grid's canonical key: crop the occupied cells to their bounding box,
// serialize rows with '/', then take the lexicographic min of that form and
// its horizontal mirror. Two arrangements match iff their keys are equal -
// material characters are compared, not just occupancy (the CCC/CxC/CCC ring
// family differs only by its center).

function cropRows(rows) {
  let top = -1, bot = -1, left = 3, right = -1;
  for (let r = 0; r < rows.length; r++) {
    for (let c = 0; c < rows[r].length; c++) {
      if (rows[r][c] === '.') continue;
      if (top < 0) top = r;
      bot = r;
      if (c < left) left = c;
      if (c > right) right = c;
    }
  }
  if (top < 0) return null; // empty grid
  const out = [];
  for (let r = top; r <= bot; r++) out.push(rows[r].slice(left, right + 1));
  return out;
}

function canonicalKey(rows) {
  const cropped = cropRows(rows);
  if (!cropped) return null;
  const normal = cropped.join('/');
  const mirrored = cropped.map((row) => row.split('').reverse().join('')).join('/');
  return mirrored < normal ? mirrored : normal;
}

// cells9 -> char rows ('.'-padded). cells9 is the worktable's flat 9-array of
// material ids (or null/'' for empty), row-major, top-left first.
function cellsToRows(cells9) {
  const rows = [];
  for (let r = 0; r < 3; r++) {
    let row = '';
    for (let c = 0; c < 3; c++) {
      const id = cells9[r * 3 + c];
      row += (id && ID_TO_CH[id]) || '.';
    }
    rows.push(row);
  }
  return rows;
}

// RECIPE_INDEX: canonical key -> recipe. Built once at module load; the
// collision guard is the safety net for future recipe additions (all 21
// shipped recipes verified collision-free under translation + mirror).
const RECIPE_INDEX = new Map();
for (const r of RECIPES) {
  const key = canonicalKey(r.grid.split('/'));
  if (!key) { console.error(`[crafting] recipe ${r.id} has an empty grid`); continue; }
  const clash = RECIPE_INDEX.get(key);
  if (clash) console.error(`[crafting] RECIPE COLLISION: ${r.id} and ${clash.id} share canonical form "${key}"`);
  RECIPE_INDEX.set(key, r);
}

/** The recipe drawn by the worktable grid, or null. */
export function matchGrid(cells9) {
  const key = canonicalKey(cellsToRows(cells9));
  return key ? (RECIPE_INDEX.get(key) || null) : null;
}

/** Material cost of the current grid: { materialId: count } (occupied cells only). */
export function gridCost(cells9) {
  const cost = {};
  for (const id of cells9) {
    if (!id || !ID_TO_CH[id]) continue;
    cost[id] = (cost[id] || 0) + 1;
  }
  return cost;
}

/** Cost of a recipe by its authored grid (for one-click re-craft from a chip). */
export function recipeCost(recipe) {
  const cost = {};
  for (const ch of recipe.grid) {
    const id = CH_TO_ID[ch];
    if (id) cost[id] = (cost[id] || 0) + 1;
  }
  return cost;
}

/** Authored grid as the worktable's flat 9-array (top-left anchored). */
export function recipeCells(recipe) {
  const rows = recipe.grid.split('/');
  const cells = new Array(9).fill(null);
  for (let r = 0; r < rows.length && r < 3; r++) {
    for (let c = 0; c < rows[r].length && c < 3; c++) {
      cells[r * 3 + c] = CH_TO_ID[rows[r][c]] || null;
    }
  }
  return cells;
}

// ============================ drops ============================

const TOTAL_WEIGHT = MATERIALS.reduce((s, m) => s + m.weight, 0);

/** Weighted random material id (tube + pop drops). */
export function rollMaterialId() {
  let roll = Math.random() * TOTAL_WEIGHT;
  for (const m of MATERIALS) {
    roll -= m.weight;
    if (roll <= 0) return m.id;
  }
  return MATERIALS[MATERIALS.length - 1].id;
}

// ============================ art ============================

export function matArt(id) { return `${ART}materials/${id}.png`; }
export function craftedArt(id) { return `${ART}crafted/${id}.png`; }

// ============================ THE PAPERWALL (Part 3) ============================
// Torn Lookbook sketches: one page tears loose per completed descent and pins up
// in THE BOUDOIR, showing an undiscovered recipe's pictogram with a corner torn
// away. The sketch teaches the SHAPE; the torn cells keep a sliver of puzzle
// (torn ≠ empty — something goes there, the page just doesn't say what).

/** Recipes the player hasn't discovered yet (paperwall hints draw from this). */
export function undiscoveredRecipes(v) {
  return RECIPES.filter((r) => !v.discoveredRecipes.has(r.id));
}

const occupiedCells = (r) => r.grid.split('').filter((ch) => ch !== '.' && ch !== '/').length;

/**
 * Which page tears loose tonight: a weighted pick over recipes that are neither
 * discovered nor already sketched — simpler drawings tear first. The Pink Heart
 * is the LAST page: it only comes loose once every other picture is known.
 * Returns a recipe id, or null (the book has no loose pages left).
 */
export function pickSketchId(discovered, sketched) {
  const open = RECIPES.filter((r) =>
    r.id !== 'the_pink_heart' && !discovered.has(r.id) && !sketched.has(r.id));
  if (!open.length) {
    const othersKnown = RECIPES.every((r) => r.id === 'the_pink_heart' || discovered.has(r.id));
    return (othersKnown && !discovered.has('the_pink_heart') && !sketched.has('the_pink_heart'))
      ? 'the_pink_heart' : null;
  }
  const weights = open.map((r) => 11 - occupiedCells(r));   // 3 cells -> 8, 9 cells -> 2
  let roll = Math.random() * weights.reduce((s, w) => s + w, 0);
  for (let i = 0; i < open.length; i++) {
    roll -= weights[i];
    if (roll <= 0) return open[i].id;
  }
  return open[open.length - 1].id;
}

// Deterministic torn cells: the same page always has the same corner missing
// (no save state needed). Rings (7+ cells) lose 2 cells, small drawings lose 1 —
// always OCCUPIED cells, so the drawing's outline is never ambiguous.
function tornIndexes(recipe) {
  let h = 2166136261;
  for (const c of recipe.id) { h ^= c.charCodeAt(0); h = Math.imul(h, 16777619) >>> 0; }
  const occ = [];
  const rows = recipe.grid.split('/');
  for (let r = 0; r < rows.length && r < 3; r++) {
    for (let c = 0; c < rows[r].length && c < 3; c++) {
      if (rows[r][c] !== '.') occ.push(r * 3 + c);
    }
  }
  const torn = new Set();
  const want = Math.min(occ.length >= 7 ? 2 : 1, Math.max(0, occ.length - 1));
  let guard = 32;
  while (torn.size < want && guard-- > 0) {
    torn.add(occ[h % occ.length]);
    h = Math.imul(h ^ (h >>> 13), 16777619) >>> 0;
  }
  return torn;
}

/**
 * The sketch as drawn on the page: a flat 9-array (row-major, top-left first) of
 * null (empty cell) | { glyph, tint, name, torn } — torn cells still carry their
 * material (hidden while unmade, revealed once the recipe is discovered).
 */
export function sketchView(recipe) {
  const torn = tornIndexes(recipe);
  const rows = recipe.grid.split('/');
  const cells = new Array(9).fill(null);
  for (let r = 0; r < rows.length && r < 3; r++) {
    for (let c = 0; c < rows[r].length && c < 3; c++) {
      const id = CH_TO_ID[rows[r][c]];
      if (!id) continue;
      const i = r * 3 + c;
      const m = MAT_BY_ID[id];
      cells[i] = { glyph: m.glyph, tint: m.tint, name: m.name, torn: torn.has(i) };
    }
  }
  return cells;
}
