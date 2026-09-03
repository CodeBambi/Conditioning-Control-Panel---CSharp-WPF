/* ============================================================================
 * race/cocktail.js - THE MIX. How effect bubbles chain in The Caucus Race.
 *
 * Pass two ran one screen-level effect at a time and scored the rest as
 * treats. Pass three replaces that with CATEGORIES: one live effect per
 * category, and each category has its own rule for a second pop of the same
 * thing, because a second flash is more flash but a second spiral is just a
 * different spiral (owner's note). Ingredient sets that are live together are
 * COCKTAILS: named, served, scored. The recipe copy is one table (RECIPES) so
 * the names can be edited without touching the machine.
 *
 *   createCocktail({ now }) -> { add, tick, state, reset, live, CATEGORIES, RECIPES }
 *     add(kindId, { durationMult }) -> { action, category, kindId, charges, depth, recipe, prevKindId, reason }
 *       action: 'fire' | 'stack' | 'extend' | 'replace' | 'refresh' | 'held' | 'ignore'
 *     tick(dt) -> events: { type: 'pulse', kind: 'burst'|'roll', charges }
 *                         { type: 'decay', category, charges }
 *                         { type: 'expire', category, kindId }
 *                         { type: 'recipeEnd', recipe }
 *     state() -> { live: [slot...], recipe, video, load }
 *
 * Pure state: no DOM, no three.js, no scoring. run.js maps actions to
 * payloadFx calls, the HUD and the ledger. Nothing here can subtract; the
 * only thing a pop can lose is its effect ('held' = it scores as a treat).
 * ==========================================================================*/

import { KIND_BY_ID } from './bubbleKinds.js';

const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** One live effect per category. `sec` is the base life (payloadFx timings);
 * `scaled` lives stretch with the pop's durationMult, fixed ones do not.
 * mode:  stack   - charges pile up to `max`, decay one at a time (STROBE)
 *        extend  - a re-pop adds time and deepens one step, to `max` (TINT)
 *        replace - a different kind swaps in, the same kind refreshes (OVERLAY)
 *        refresh - a re-pop resets the clock and fires a one-shot (CORRUPTION)
 *        add     - each pop is its own card, up to `max` live (CARDS)
 *        solo    - one at a time, short (FREEZE)
 *        tape    - one at a time, and everything else is held while it runs (VIDEO) */
export const CATEGORIES = {
  strobe:     { label: 'strobe', glyph: '✦', mode: 'stack',   max: 5, sec: 2.6, scaled: true },
  tint:       { label: 'tint',   glyph: '◑', mode: 'extend',  max: 2, sec: 4.5, scaled: true },
  overlay:    { label: 'spin',   glyph: '◎', mode: 'replace', max: 1, sec: 4.5, scaled: true },
  corruption: { label: 'glitch', glyph: '▚', mode: 'refresh', max: 1, sec: 4.5, scaled: true },
  cards:      { label: 'cards',  glyph: '♥', mode: 'add',     max: 4, sec: 6,   scaled: true },
  freeze:     { label: 'freeze', glyph: '❄', mode: 'solo',    max: 1, sec: 1.7, scaled: false },
  video:      { label: 'tape',   glyph: '▶', mode: 'tape',    max: 1, sec: 9,   scaled: false },
};
export const CATEGORY_IDS = Object.keys(CATEGORIES);

/** Kinds whose life differs from their category's (payloadFx fixes these). */
const KIND_LIFE = { subliminal: { sec: 0.5, scaled: false } };

/** STROBE: a later charge lives this fraction of a fresh one, so five pops in
 * a row read as one rolling strobe that winds down, not a 13 s wall. */
const STROBE_DECAY = 0.6;
/** STROBE: extra flashes after a stacking pop, spaced this far apart (charges 2..3 = double / triple). */
const BURST_GAP = 0.18;
const BURST_MAX = 2;
/** STROBE: at 4 and 5 charges the world keeps flashing on its own, this often. */
const ROLL_PERIOD = { 4: 0.55, 5: 0.38 };
/** TINT: a re-pop can push the clock out to this many base lives. */
const TINT_CAP = 2.5;

/** THE RECIPES. Ingredients are category ids; the first row whose needs are
 * all live wins, so the bigger pours sit above the pairs. mult/sec is the
 * score boost run.js hands to score.boostMult (never below x1). `marquee`
 * rows also ride the room banner. Copy is DtRH voice: lowercase, short, no
 * em-dashes. Edit the names here and nowhere else. */
export const RECIPES = [
  { id: 'full_pour',      name: 'the full pour', needs: ['tint', 'strobe', 'corruption'], mult: 2.5, sec: 8, marquee: true,
    line: 'pink, flash, static. all of it, all at once.' },
  { id: 'spun_sugar',     name: 'spun sugar',    needs: ['overlay', 'tint', 'strobe'],   mult: 2,   sec: 7,
    line: 'the spiral, in pink, under lights.' },
  { id: 'pink_lightning', name: 'pink lightning', needs: ['tint', 'strobe'],             mult: 1.5, sec: 6,
    line: 'every flash lands rosier than the last.' },
  { id: 'static_rose',    name: 'static rose',   needs: ['tint', 'corruption'],          mult: 1.5, sec: 6,
    line: 'the picture breaks. the colour holds.' },
  { id: 'snowblind',      name: 'snowblind',     needs: ['strobe', 'corruption'],        mult: 1.5, sec: 6,
    line: 'white noise with the lights on.' },
  { id: 'rose_spin',      name: 'rose spin',     needs: ['overlay', 'tint'],             mult: 1.5, sec: 6,
    line: 'round and round, in the right colour.' },
  { id: 'lighthouse',     name: 'lighthouse',    needs: ['overlay', 'strobe'],           mult: 1.5, sec: 6,
    line: 'the spiral blinks. you keep steering.' },
  { id: 'bad_reception',  name: 'bad reception', needs: ['overlay', 'corruption'],       mult: 1.5, sec: 6,
    line: 'still turning, through the snow.' },
  { id: 'love_letters',   name: 'love letters',  needs: ['tint', 'cards'],               mult: 1.4, sec: 5,
    line: 'pink cards. all of them addressed to you.' },
];
export const RECIPE_BY_ID = Object.fromEntries(RECIPES.map((r) => [r.id, r]));

/** Category of a bubble kind id (from bubbleKinds.js rows), or null for treats. */
export const categoryOf = (kindId) => { const k = KIND_BY_ID[kindId]; return k && k.category && CATEGORIES[k.category] ? k.category : null; };

export function createCocktail({ now = null } = {}) {
  const live = {};        // category -> slot { category, kindId, charges, depth, sec, total, life, cards, at }
  let t = 0;              // seconds of tick time
  let recipe = null;      // { id, name, line, mult, sec, total, marquee, needs, at }
  const bursts = [];      // pending strobe burst pulses, absolute t
  let rollAt = 0;
  const stamp = () => (typeof now === 'function' ? now() : t);

  const lifeOf = (kindId, cat, dm) => {
    const spec = KIND_LIFE[kindId] || CATEGORIES[cat];
    return spec.sec * (spec.scaled ? clamp(dm == null ? 1 : +dm || 1, 0.1, 10) : 1);
  };
  function open(cat, kindId, sec) {
    const s = { category: cat, kindId, charges: 1, depth: 1, sec, total: sec, life: sec, cards: null, at: stamp() };
    if (CATEGORIES[cat].mode === 'add') s.cards = [sec];
    live[cat] = s;
    return s;
  }
  function scheduleBurst(charges) {
    // charges 2 and 3 add one and two quick extras; 4 and 5 keep the pair and roll on top
    const extra = Math.min(BURST_MAX, charges - 1);
    for (let i = 1; i <= extra; i++) bursts.push(t + BURST_GAP * i);
  }
  /** The best recipe the live set satisfies, if it is not the one already served. */
  function matchRecipe() {
    for (const r of RECIPES) {
      if (!r.needs.every((c) => live[c])) continue;
      if (recipe && recipe.id === r.id) return null;
      recipe = { ...r, sec: r.sec, total: r.sec, at: stamp() };
      return { ...recipe };
    }
    return null;
  }

  /** An effect bubble popped. Never throws; treats and unknown ids are 'ignore'. */
  function add(kindId, opts = {}) {
    const cat = categoryOf(kindId);
    if (!cat) return { action: 'ignore', category: null, kindId, charges: 0, depth: 0, recipe: null };
    const spec = CATEGORIES[cat];
    const sec = lifeOf(kindId, cat, opts.durationMult);
    const s = live[cat] || null;
    const held = (reason) => ({ action: 'held', category: cat, kindId, charges: s ? s.charges : 0, depth: s ? s.depth : 0, recipe: null, reason });
    if (live.video && cat !== 'video') return held('tape');
    const res = { action: 'fire', category: cat, kindId, charges: 1, depth: 1, recipe: null };
    switch (spec.mode) {
      case 'tape':
      case 'solo':
        if (s) return held('busy');
        open(cat, kindId, sec);
        break;
      case 'stack':
        if (!s) { open(cat, kindId, sec); }
        else if (s.charges >= spec.max) { s.sec = Math.max(s.sec, s.total); return held('full'); }
        else { s.charges++; s.sec = sec; s.total = sec; s.life = sec; s.kindId = kindId; res.action = 'stack'; res.charges = s.charges; scheduleBurst(s.charges); }
        break;
      case 'extend':
        if (!s) { open(cat, kindId, sec); }
        else { s.sec = Math.min(s.sec + sec, sec * TINT_CAP); s.total = s.sec; s.depth = Math.min(spec.max, s.depth + 1); res.action = 'extend'; res.depth = s.depth; }
        break;
      case 'replace':
        if (!s) { open(cat, kindId, sec); }
        else if (s.kindId === kindId) { s.sec = sec; s.total = sec; res.action = 'refresh'; }
        else { res.prevKindId = s.kindId; s.kindId = kindId; s.sec = sec; s.total = sec; s.at = stamp(); res.action = 'replace'; }
        break;
      case 'refresh':
        if (!s) { open(cat, kindId, sec); }
        else { s.sec = sec; s.total = sec; res.action = 'refresh'; }
        break;
      case 'add':
        if (!s) { open(cat, kindId, sec); }
        else if (s.cards.length >= spec.max) return held('full');
        else { s.cards.push(sec); s.charges = s.cards.length; s.sec = Math.max(s.sec, sec); s.total = Math.max(s.total, sec); s.kindId = kindId; res.action = 'stack'; res.charges = s.charges; }
        break;
      default: return held('busy');
    }
    res.recipe = matchRecipe();
    return res;
  }

  /** Advance the clocks. Returns the events that fell out. */
  function tick(dt) {
    dt = clamp(+dt || 0, 0, 1);
    t += dt;
    const ev = [];
    for (const cat of CATEGORY_IDS) {
      const s = live[cat];
      if (!s) continue;
      if (s.cards) {
        for (let i = s.cards.length - 1; i >= 0; i--) { s.cards[i] -= dt; if (s.cards[i] <= 0) s.cards.splice(i, 1); }
        s.charges = s.cards.length; s.sec = s.charges ? Math.max(...s.cards) : 0;
        if (!s.charges) { delete live[cat]; ev.push({ type: 'expire', category: cat, kindId: s.kindId }); }
        continue;
      }
      s.sec -= dt;
      if (s.sec > 0) continue;
      if (CATEGORIES[cat].mode === 'stack' && s.charges > 1) {
        s.charges--; s.sec = s.total = s.life * STROBE_DECAY;
        ev.push({ type: 'decay', category: cat, charges: s.charges });
      } else {
        delete live[cat];
        ev.push({ type: 'expire', category: cat, kindId: s.kindId });
      }
    }
    // strobe: the queued burst extras, then the roll at 4 and 5 charges
    while (bursts.length && bursts[0] <= t) { bursts.shift(); if (live.strobe) ev.push({ type: 'pulse', kind: 'burst', charges: live.strobe.charges }); }
    if (!live.strobe) bursts.length = 0;
    const st = live.strobe;
    if (st && ROLL_PERIOD[st.charges]) {
      if (t >= rollAt) { rollAt = t + ROLL_PERIOD[st.charges]; ev.push({ type: 'pulse', kind: 'roll', charges: st.charges }); }
    } else rollAt = 0;
    // the served recipe holds while its ingredients do, for its own window
    if (recipe) {
      recipe.sec -= dt;
      if (recipe.sec <= 0 || !recipe.needs.every((c) => live[c])) { ev.push({ type: 'recipeEnd', recipe: { ...recipe } }); recipe = null; }
    }
    return ev;
  }

  /** A snapshot for the HUD: live slots in category order, the recipe, the load. */
  function state() {
    const out = [];
    let load = 0;
    for (const cat of CATEGORY_IDS) {
      const s = live[cat];
      if (!s) continue;
      const spec = CATEGORIES[cat];
      load += 1 + (s.charges - 1) * 0.5;
      out.push({ category: cat, kindId: s.kindId, label: spec.label, glyph: spec.glyph, charges: s.charges, max: spec.max,
        depth: s.depth, sec: s.sec, total: s.total, frac: s.total > 0 ? clamp(s.sec / s.total, 0, 1) : 0 });
    }
    return {
      live: out, load, video: !!live.video,
      recipe: recipe ? { id: recipe.id, name: recipe.name, line: recipe.line, mult: recipe.mult, marquee: !!recipe.marquee,
        sec: recipe.sec, total: recipe.total, frac: recipe.total > 0 ? clamp(recipe.sec / recipe.total, 0, 1) : 0 } : null,
    };
  }
  function reset() {
    for (const cat of CATEGORY_IDS) delete live[cat];
    bursts.length = 0; rollAt = 0; recipe = null; t = 0;
  }

  return { add, tick, state, reset, live: (cat) => live[cat] || null, CATEGORIES, RECIPES };
}

// self-check (node only): `RACE_SELFCHECK=1 node --input-type=module -e "import './cocktail.js'"`
if (typeof process !== 'undefined' && process.env && process.env.RACE_SELFCHECK) {
  const ok = (c, m) => { if (!c) { console.error('cocktail.js self-check FAILED: ' + m); process.exitCode = 1; } };
  const drain = (m, sec) => { const ev = []; for (let i = 0; i < Math.ceil(sec / 0.05); i++) ev.push(...m.tick(0.05)); return ev; };
  const types = (ev, ty) => ev.filter((e) => e.type === ty);
  let m = createCocktail();
  // treats are ignored, nothing is live
  ok(m.add('treat').action === 'ignore' && m.state().live.length === 0, 'treat ignored');
  // STROBE stacks to 5, then holds; charges decay one at a time
  ok(m.add('flash').action === 'fire', 'first flash fires');
  for (let i = 2; i <= 5; i++) { const r = m.add('flash'); ok(r.action === 'stack' && r.charges === i, 'flash stacks to ' + i); }
  ok(m.add('flash').action === 'held' && m.live('strobe').charges === 5, 'sixth flash held at 5');
  let ev = drain(m, 2.7);
  ok(types(ev, 'decay').length === 1 && m.live('strobe').charges === 4, 'one charge decays at a time');
  ok(types(ev, 'pulse').some((e) => e.kind === 'burst') && types(ev, 'pulse').some((e) => e.kind === 'roll'), 'burst + roll pulses');
  ev = drain(m, 12);
  ok(!m.live('strobe') && types(ev, 'expire').some((e) => e.category === 'strobe'), 'strobe fully decays and expires');
  // TINT extends and deepens to 2, never stacks
  ok(m.add('pink').action === 'fire' && m.live('tint').depth === 1, 'tint fires');
  let r = m.add('pink'); ok(r.action === 'extend' && r.depth === 2 && m.live('tint').sec > 4.5, 'tint extends + deepens');
  r = m.add('pink'); ok(r.action === 'extend' && r.depth === 2, 'tint depth caps at 2');
  ok(m.live('tint').sec <= 4.5 * 2.5 + 1e-9, 'tint clock capped');
  // OVERLAY: same kind refreshes, a different kind replaces
  ok(m.add('spiral').action === 'fire', 'spiral fires');
  ok(m.add('spiral').action === 'refresh', 'second spiral refreshes');
  r = m.add('braindrain'); ok(r.action === 'replace' && r.prevKindId === 'spiral' && m.live('overlay').kindId === 'braindrain', 'braindrain replaces spiral');
  // CORRUPTION refreshes
  ok(m.add('glitch').action === 'fire' && m.add('glitch').action === 'refresh', 'glitch fires then refreshes');
  // CARDS add to 4 then hold
  ok(m.add('subliminal').action === 'fire', 'card fires');
  ok(m.add('gifrain').action === 'stack' && m.add('gifrain').action === 'stack' && m.add('gifrain').action === 'stack', 'cards add to 4');
  ok(m.add('subliminal').action === 'held', 'fifth card held');
  // FREEZE is solo
  ok(m.add('freeze').action === 'fire' && m.add('freeze').action === 'held', 'freeze solo');
  // VIDEO: exclusive over everything
  ok(m.add('video').action === 'fire', 'video fires over a full mix');
  ok(m.add('flash').action === 'held' && m.add('pink').action === 'held' && m.add('video').action === 'held', 'everything held under the tape');
  ok(m.state().video === true, 'state.video');
  m.reset(); ok(m.state().live.length === 0 && m.state().recipe === null, 'reset clears');
  // every recipe triggers from its own ingredients, and only once while it stays live
  const seed = { strobe: 'flash', tint: 'pink', overlay: 'spiral', corruption: 'glitch', cards: 'subliminal' };
  for (const rc of RECIPES) {
    m.reset();
    let got = null;
    for (const c of rc.needs) { const a = m.add(seed[c]); if (a.recipe) got = a.recipe; }
    ok(got && got.id === rc.id, 'recipe ' + rc.id + ' fires from ' + rc.needs.join('+'));
    ok(m.state().recipe && m.state().recipe.name === rc.name, 'recipe ' + rc.id + ' is live');
    ok(rc.mult >= 1 && rc.sec > 0, 'recipe ' + rc.id + ' never subtracts');
    ok(!/—|–/.test(rc.name + rc.line) && rc.name === rc.name.toLowerCase(), 'recipe ' + rc.id + ' copy is house voice');
    const again = m.add(seed[rc.needs[0]]);
    ok(again.recipe === null, 'recipe ' + rc.id + ' does not re-serve while live');
  }
  // the full pour outranks pink lightning when glitch lands on top
  m.reset(); m.add('pink'); r = m.add('flash'); ok(r.recipe && r.recipe.id === 'pink_lightning', 'pair first');
  r = m.add('glitch'); ok(r.recipe && r.recipe.id === 'full_pour' && r.recipe.marquee, 'full pour on top');
  ev = drain(m, 9); ok(types(ev, 'recipeEnd').length === 1 && m.state().recipe === null, 'recipe ends with its window');
  // nothing in the machine ever goes negative
  m.reset(); m.add('flash'); m.add('pink'); m.add('subliminal');
  for (let i = 0; i < 200; i++) { m.tick(0.1); for (const s of m.state().live) ok(s.sec >= 0 && s.charges >= 1 && s.frac >= 0, 'no negative clocks'); }
  if (!process.exitCode) console.log('cocktail.js self-check ok', RECIPES.length + ' recipes, ' + CATEGORY_IDS.length + ' categories');
}
