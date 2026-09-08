/* ============================================================================
 * hud.js - the screen furniture: two clocks, one status line, the tally, and
 * the meter drawn as the frame of the screen warming.
 *
 * The board is orbitable, so nothing in here may depend on which way the camera
 * faces: clocks and words live on the screen, anything that belongs to a square
 * lives on the board itself. The chips sit in the LEFT corners, black on top and
 * white on the bottom, because the middle and the right are the lane the ramp's
 * video card rides through.
 *
 * This layers on top of hotseat.js rather than rewiring it. hotseat still writes
 * the two times and the base status line into the ids it was handed; everything
 * here is driven off the bus (clock, turn, check, gameover, capture, local) and
 * off game.rules for the material count, so the match code never has to know the
 * HUD changed shape.
 *
 *   createHud({ bus, game, board, root, params }) -> { setMeter, debug, dispose }
 *
 * Nothing here may throw at import or at attach time: a missing node, a missing
 * bus and a missing game all degrade to a quieter HUD, never to a dead board.
 * ==========================================================================*/

/** Every number the HUD decides with. One place, on purpose. */
export const TUNING = Object.freeze({
  slideMs: 260,        // the light moving from one chip to the other
  gapLine: 6,          // px, chip bottom to the sliding line
  gapStatus: 14,       // px, chip bottom to the status column
  lowMs: 30000,        // the mover's clock goes red and the chip shivers
  panicMs: 10000,      // and the shiver gets sharper
  hintMs: 3000,        // how long "hold esc to leave the board" stays
  vigFull: 0.5,        // meter at which the vignette is at full strength
  vigMax: 0.9,         // and how strong full is
  vigBreathe: 0.7,     // past this the glow breathes
  unlocks: Object.freeze([0.25, 0.40, 0.55, 0.70]),   // the debug dot row
});

const T = TUNING;
const VALUE = Object.freeze({ p: 1, n: 3, b: 3, r: 5, q: 9 });
const NAMES = Object.freeze({ p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen' });
const COUNT = Object.freeze(['one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten']);

const clamp01 = (v) => Math.min(1, Math.max(0, Number(v) || 0));
const other = (s) => (s === 'w' ? 'b' : 'w');
const sideWord = (s) => (s === 'w' ? 'white' : 'black');

function reducedMotion() {
  try {
    if (typeof window === 'undefined') return false;
    const pbp = window.PBP;
    if (pbp && pbp.settings && pbp.settings.reducedMotion) return true;
    return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  } catch { return false; }
}

/**
 * The material count as one word, from `side`'s point of view.
 * A clean one-man lead is named ("up a knight"); anything messier falls back to
 * the point difference ("down two"). Kings are not counted, they never come off.
 */
export function tallyWord(position, side = 'w') {
  const count = { w: {}, b: {} };
  for (const sq of Object.keys(position || {})) {
    const man = position[sq];
    if (!man || man.type === 'k' || !count[man.side]) continue;
    count[man.side][man.type] = (count[man.side][man.type] || 0) + 1;
  }
  let points = 0;
  const differing = [];
  for (const type of Object.keys(VALUE)) {
    const d = (count.w[type] || 0) - (count.b[type] || 0);
    points += d * VALUE[type];
    if (d !== 0) differing.push({ type, d });
  }
  if (side === 'b') { points = -points; for (const x of differing) x.d = -x.d; }
  if (points === 0) return 'even';
  const way = points > 0 ? 'up' : 'down';
  if (differing.length === 1 && Math.abs(differing[0].d) === 1) {
    return way + ' a ' + NAMES[differing[0].type];
  }
  const n = Math.abs(points);
  return way + ' ' + (COUNT[n - 1] || String(n));
}

export function createHud(opts = {}) {
  const bus = opts.bus && typeof opts.bus.on === 'function' ? opts.bus : null;
  const game = opts.game || null;
  const root = opts.root || (typeof document !== 'undefined' ? document.getElementById('hud') : null);
  if (!root) {
    return { setMeter() {}, dispose() {}, debug: () => ({ ok: false, why: 'no #hud' }) };
  }
  const pick = (id) => root.querySelector('#' + id) || (typeof document !== 'undefined' ? document.getElementById(id) : null);
  const el = {
    vig: pick('hud-vig'),
    line: pick('turn-line'),
    col: pick('status-col'),
    status: pick('status'),
    hint: pick('hud-hint'),
    dots: pick('hud-dots'),
    chip: { w: pick('chip-w'), b: pick('chip-b') },
    tally: { w: pick('tally-w'), b: pick('tally-b') },
  };

  let active = 'w';         // whose clock is running
  let over = null;          // the gameover payload, once it lands
  let meter = 0;
  let lastSecond = -1;      // so the shiver fires once a second, not every tick
  let hinted = false;
  const timers = new Set();
  const unbind = [];

  const later = (fn, ms) => { const id = setTimeout(() => { timers.delete(id); fn(); }, ms); timers.add(id); return id; };
  const on = (type, fn) => {
    if (!bus) return;
    const safe = (p) => { try { fn(p); } catch (e) { console.warn('[pbp/hud] ' + type + ': ' + (e && e.message)); } };
    try { unbind.push(bus.on(type, safe)); } catch { /* a bus that will not take listeners is still a bus */ }
  };

  /* ---- the sliding light -------------------------------------------------- */

  /** Put the line and the status column under whichever chip is on the move. */
  function place(instant) {
    const chip = el.chip[active];
    if (!chip) return;
    const y = chip.offsetTop + chip.offsetHeight;
    const set = (node, dy) => {
      if (!node) return;
      if (instant) node.style.transition = 'none';
      node.style.transform = 'translateY(' + Math.round(y + dy) + 'px)';
      if (instant) { void node.offsetWidth; node.style.transition = ''; }
    };
    set(el.line, T.gapLine);
    set(el.col, T.gapStatus);
    if (el.line) el.line.classList.toggle('on', !over);
  }

  function setActive(side, instant) {
    active = side === 'b' ? 'b' : 'w';
    for (const s of ['w', 'b']) {
      if (el.chip[s]) el.chip[s].classList.toggle('on', s === active && !over);
    }
    place(instant);
  }

  /* ---- check, low clock, tally -------------------------------------------- */

  /** The checked side wears the red tinge for exactly as long as the check stands. */
  function paintCheck() {
    let checked = null;
    try { if (game && game.rules && game.rules.inCheck() && !over) checked = game.rules.turn(); } catch { checked = null; }
    for (const s of ['w', 'b']) {
      if (el.chip[s]) el.chip[s].classList.toggle('check', s === checked);
    }
  }

  function shiver(chip, hard) {
    if (!chip || reducedMotion()) return;
    const cls = hard ? 'shiver-hard' : 'shiver';
    chip.classList.remove('shiver', 'shiver-hard');
    void chip.offsetWidth;                       // restart the run, do not queue it
    chip.classList.add(cls);
    later(() => chip.classList.remove(cls), 220);
  }

  /** Under 30 s the mover's digits go red; the chip shivers on each tick. */
  function paintLow(snap) {
    const left = snap && typeof snap[active] === 'number' ? snap[active] : null;
    const low = left != null && left < T.lowMs && !over;
    for (const s of ['w', 'b']) {
      if (el.chip[s]) el.chip[s].classList.toggle('low', low && s === active);
    }
    if (!low) { lastSecond = -1; return; }
    const second = Math.ceil(left / 1000);
    if (second === lastSecond) return;
    lastSecond = second;
    shiver(el.chip[active], left < T.panicMs);
  }

  function paintTally() {
    if (!game || !game.rules) return;
    let position;
    try { position = game.rules.position(); } catch { return; }
    for (const s of ['w', 'b']) {
      if (el.tally[s]) el.tally[s].textContent = tallyWord(position, s);
    }
  }

  /* ---- the meter, as a warm frame ----------------------------------------- */

  function setMeter(v) {
    meter = clamp01(v);
    const still = reducedMotion();
    // the host can turn reduced motion on after boot, so this is re-read rather
    // than remembered; the class is what stops the light sliding
    root.classList.toggle('still', still);
    const strength = clamp01(meter / T.vigFull) * T.vigMax;
    if (el.vig) {
      el.vig.style.setProperty('--pbp-vig', strength.toFixed(3));
      el.vig.classList.toggle('breathe', meter >= T.vigBreathe && !still);
    }
    if (el.dots && !el.dots.hidden) {
      const lit = el.dots.querySelectorAll('i');
      for (let i = 0; i < lit.length; i++) lit[i].classList.toggle('lit', meter >= T.unlocks[i]);
    }
  }

  /* ---- copy --------------------------------------------------------------- */

  /**
   * hotseat.js already writes "white to move", "check", "checkmate, black wins",
   * "stalemate, a draw" and "time, white wins" into the same node, in the same
   * words. The one line it does not have is the kneel, so that is the only line
   * this overwrites, and it lands after hotseat's own paint.
   */
  function endLine(end) {
    if (!end || end.result !== 'resign' || !end.winner) return null;
    return sideWord(other(end.winner)) + ' kneels';
  }

  /* ---- wiring ------------------------------------------------------------- */

  on('clock', (snap) => {
    if (snap && snap.active && snap.active !== active && !over) setActive(snap.active);
    paintLow(snap);
  });
  on('turn', (p) => {
    setActive(p && p.side === 'b' ? 'b' : 'w');
    paintCheck();
    paintTally();
  });
  on('check', () => paintCheck());
  on('capture', () => paintTally());
  on('local', () => { paintTally(); place(true); });
  on('gameover', (p) => {
    over = p || { result: 'over' };
    const line = endLine(over);
    if (line && el.status) el.status.textContent = line;
    for (const s of ['w', 'b']) {
      if (!el.chip[s]) continue;
      el.chip[s].classList.remove('low', 'check', 'shiver', 'shiver-hard');
    }
    if (el.line) el.line.classList.remove('on');
  });

  // The one piece of teaching copy, and it leaves on its own. The first pointer
  // move means the player has arrived; three seconds later they have read it.
  function showHint() {
    if (hinted || !el.hint) return;
    hinted = true;
    el.hint.hidden = false;
    requestAnimationFrame(() => el.hint.classList.add('on'));
    later(() => {
      el.hint.classList.remove('on');
      later(() => { el.hint.hidden = true; }, 360);
    }, T.hintMs);
  }
  const onPointer = () => showHint();
  try { window.addEventListener('pointermove', onPointer, { once: true, passive: true }); } catch { /* no window */ }

  const onResize = () => place(true);
  try { window.addEventListener('resize', onResize, { passive: true }); } catch { /* no window */ }

  /* ---- start ------------------------------------------------------------- */

  try {
    const params = opts.params || new URLSearchParams(location.search);
    if (el.dots && params.get('debug') === '1') el.dots.hidden = false;
  } catch { /* no location: the dots stay hidden, which is the default */ }

  setActive(game && game.turn ? game.turn() : 'w', true);
  paintTally();
  paintCheck();
  setMeter(0);

  function dispose() {
    for (const off of unbind) { try { off(); } catch { /* already gone */ } }
    unbind.length = 0;
    for (const id of timers) clearTimeout(id);
    timers.clear();
    try { window.removeEventListener('pointermove', onPointer); } catch { /* gone */ }
    try { window.removeEventListener('resize', onResize); } catch { /* gone */ }
  }

  return {
    setMeter,
    dispose,
    /** Everything a harness needs to prove a state without reading pixels. */
    debug() {
      const cls = (node) => (node ? node.className : null);
      return {
        active,
        over,
        meter,
        vig: el.vig ? el.vig.style.getPropertyValue('--pbp-vig') : null,
        breathing: !!(el.vig && el.vig.classList.contains('breathe')),
        status: el.status ? el.status.textContent : null,
        hint: !!(el.hint && el.hint.classList.contains('on')),
        chips: { w: cls(el.chip.w), b: cls(el.chip.b) },
        tally: { w: el.tally.w ? el.tally.w.textContent : null, b: el.tally.b ? el.tally.b.textContent : null },
        line: el.line ? el.line.style.transform : null,
        reducedMotion: reducedMotion(),
      };
    },
  };
}

export default createHud;
