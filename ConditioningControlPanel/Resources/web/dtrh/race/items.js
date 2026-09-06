/* ============================================================================
 * items.js - the sugar cube's toy shelf. Implements CONTRACT.md "race/items.js".
 *
 * Ten items, none of which hurt the player (the no-lose contract applied to
 * the item table). roll(position) is position-aware like Mario Kart: a low
 * multiplier leans the pool toward catch-up items, a high one toward
 * risk/reward. The cube rolls for ROLL_SEC (the roll is decided before the
 * animation starts: Fake Shuffle), then arms; E uses it, or the cup uses it
 * for you after AUTO_USE_SEC so autopilot players never touch a key. Every item
 * gets a visible beat on use (`beat`) and, for the quiet timed ones, one when it
 * runs out (`over`), so nothing ever fires into silence.
 *
 * Effects go through the contract APIs only (kart.applyBoost, bubbles.rain,
 * bubbles.setDensity, hud.*). Anything that needs the run brain's hands is
 * an event on onEvent(cb) instead of a reach into a module we do not own:
 *   { type:'itemRoll', id }                  the cube has decided (still rolling)
 *   { type:'itemArm', id }                   the slot is armed, E works now
 *   { type:'itemUse', id }                   fired on every use, before the effect
 *   { type:'itemEnd', id }                   a timed item ran out (every item with durationSec > 0)
 *   { type:'timeScale', value, sec }         tea_time: world clock, kart does not slow
 *   { type:'magnet', sec }                   magnet: treats within reach bend to the lane
 *   { type:'multBoost', mult, sec }          lucky_star: ladder mult x2 on top
 *   { type:'parasol', armed:true }           parasol: next effect pop scores as a treat, no payload
 *   { type:'flip', sec }                     mirror: the canvas flips, input does not (Law II)
 *   { type:'jump', vh }                      spring: give the kart this upward impulse
 *   { type:'comboFreeze', sec }              pocket_watch: hold the combo timer
 *   { type:'jackpotBias', mult, sec }        rabbit_foot: jackpot odds times mult
 * ==========================================================================*/

const ROLL_SEC = 0.9;
const AUTO_USE_SEC = 1.5;
const USED_FLASH_SEC = 0.8;

export const ITEMS = [
  { id: 'sugar_rush',   name: 'sugar rush',   glyph: '▲', desc: 'boost 2.6 s. the big word.',                 durationSec: 2.6, pool: 'catch', beat: 'sugar rush' },
  { id: 'tea_time',     name: 'tea time',     glyph: '◷', desc: 'the world slows for 4 s. you do not.',      durationSec: 4,   pool: 'mid',   beat: 'tea time. the world slows, you do not', over: 'tea is over' },
  { id: 'magnet',       name: 'the wand',     glyph: '✦', desc: 'treats bend to the cup for 7 s.',            durationSec: 7,   pool: 'catch', beat: 'the wand. treats come to you', over: 'wand down' },
  { id: 'bubble_wand',  name: 'bubble wand',  glyph: '○', desc: 'twelve treats rain in ahead of you.',        durationSec: 0,   pool: 'catch', beat: 'twelve, incoming' },
  { id: 'lucky_star',   name: 'lucky star',   glyph: '★', desc: 'x2 on top of the ladder for 8 s.',          durationSec: 8,   pool: 'risk',  beat: 'lucky star. x2 on top', over: 'star fell' },
  { id: 'parasol',      name: 'parasol',      glyph: '☂', desc: 'the next effect pops as a treat instead.',   durationSec: 0,   pool: 'mid',   beat: 'parasol up. next effect is a treat' },
  { id: 'mirror',       name: 'mirror',       glyph: '◫', desc: 'the picture flips for 6 s. left is still left.', durationSec: 6, pool: 'risk', beat: 'mirror. left is still left', over: 'mirror gone' },
  { id: 'spring',       name: 'spring',       glyph: '⤴', desc: 'a ramp, right here, right now.',            durationSec: 0,   pool: 'mid',   beat: 'spring' },
  { id: 'pocket_watch', name: 'pocket watch', glyph: '◔', desc: 'the combo timer stops for 10 s.',            durationSec: 10,  pool: 'mid',   beat: 'pocket watch. the combo waits', over: 'the watch ticks again' },
  { id: 'rabbit_foot',  name: 'rabbit foot',  glyph: '♣', desc: 'jackpots come easier for 12 s.',             durationSec: 12,  pool: 'risk',  beat: 'rabbit foot. jackpots lean in', over: 'foot wore off' },
];
const BY_ID = Object.fromEntries(ITEMS.map((it) => [it.id, it]));

/** Pool weights by multiplier: x1 is generous with catch-up, x8 leans into risk/reward. */
function weightFor(item, mult) {
  const t = Math.min(1, Math.max(0, (mult - 1) / 7));   // 0 at x1, 1 at x8
  if (item.pool === 'catch') return 3.0 - 2.5 * t;
  if (item.pool === 'risk') return 0.6 + 2.6 * t;
  return 1.4;
}

export function createItems({ kart, bubbles, score, fx, hud, payload, rng, autoUseSec = AUTO_USE_SEC } = {}) {
  const rand = typeof rng === 'function' ? rng : Math.random;
  const listeners = [];
  const active = new Map();     // id -> seconds left (timed items only)
  let rolled = null;            // item decided by roll(), shown after ROLL_SEC
  let armed = null;             // item in the slot, usable
  let rollLeft = 0, armedFor = 0, usedFlash = 0;

  const emit = (ev) => { for (const cb of listeners) { try { cb(ev); } catch (e) { /* a listener never breaks the run */ } } };
  const multOf = (position) => {
    if (typeof position === 'number') return position;
    if (position && typeof position.mult === 'number') return position.mult;
    return score && score.state ? (score.state.mult || 1) : 1;
  };

  function apply(it) {
    const d = it.durationSec;
    switch (it.id) {
      case 'sugar_rush':   if (kart && kart.applyBoost) kart.applyBoost(d); break;
      case 'tea_time':     emit({ type: 'timeScale', value: 0.5, sec: d }); break;
      case 'magnet':       emit({ type: 'magnet', sec: d }); break;
      case 'bubble_wand':  if (bubbles && bubbles.rain) bubbles.rain(kart && kart.state ? kart.state.d : 0, 12); break;
      case 'lucky_star':   emit({ type: 'multBoost', mult: 2, sec: d }); break;
      case 'parasol':      emit({ type: 'parasol', armed: true }); break;
      case 'mirror':       emit({ type: 'flip', sec: d }); break;
      case 'spring':       emit({ type: 'jump', vh: 9 }); break;
      case 'pocket_watch': emit({ type: 'comboFreeze', sec: d }); break;
      case 'rabbit_foot':  emit({ type: 'jackpotBias', mult: 2, sec: d }); break;
    }
    if (d > 0) active.set(it.id, d);
  }

  const items = {
    /** Decide the item now, show the roll, arm after ROLL_SEC. Returns the item or null if the slot is busy. */
    roll(position) {
      if (armed || rolled) return null;
      const mult = multOf(position);
      const weights = ITEMS.map((it) => weightFor(it, mult));
      let r = rand() * weights.reduce((a, b) => a + b, 0);
      let pick = ITEMS[ITEMS.length - 1];
      for (let i = 0; i < ITEMS.length; i++) { r -= weights[i]; if (r <= 0) { pick = ITEMS[i]; break; } }
      rolled = pick;
      rollLeft = ROLL_SEC;
      if (hud) hud.item('?', 'rolling');
      emit({ type: 'itemRoll', id: pick.id });
      return pick;
    },
    get current() { return armed; },
    get active() { return active; },
    /** Use the armed item. Returns true when something fired. */
    use() {
      const it = armed;
      if (!it) return false;
      armed = null; armedFor = 0;
      emit({ type: 'itemUse', id: it.id });
      apply(it);
      if (hud) { hud.item(it.glyph, `${it.name} used`); hud.toast(it.beat || it.name, 'item'); }
      usedFlash = USED_FLASH_SEC;
      return true;
    },
    update(dt) {
      if (!(dt > 0)) return;
      if (rolled) {
        rollLeft -= dt;
        if (rollLeft <= 0) {
          armed = rolled; rolled = null; armedFor = 0;
          if (hud) hud.item(armed.glyph, armed.name);
          emit({ type: 'itemArm', id: armed.id });
        }
      } else if (armed && autoUseSec > 0) {
        armedFor += dt;
        if (armedFor >= autoUseSec) items.use();
      }
      if (usedFlash > 0) {
        usedFlash -= dt;
        if (usedFlash <= 0 && !armed && !rolled && hud) hud.item(null, 'no item yet');
      }
      for (const [id, left] of active) {
        const next = left - dt;
        if (next > 0) { active.set(id, next); continue; }
        active.delete(id);
        emit({ type: 'itemEnd', id });
        if (hud && BY_ID[id] && BY_ID[id].over) hud.toast(BY_ID[id].over, 'item');
      }
    },
    onEvent(cb) { if (typeof cb === 'function') listeners.push(cb); return () => { const i = listeners.indexOf(cb); if (i >= 0) listeners.splice(i, 1); }; },
    byId(id) { return BY_ID[id] || null; },
  };
  return items;
}

// self-check: node --check is the bar. `node -e "import('./items.js').then(m => console.log(m.ITEMS.length))"` prints 10.
