import { identity, onIdentity } from './bridge.js';
import { presentation, setPresentation, onPresentation } from './game/preferences.js';
import { attachPictures } from './ui/pictures.js';

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
 * ONLINE, TWO MORE WORDS. An online seat (net/match.js) can resign and can
 * offer, accept or decline a draw, and a hotseat cannot: there is nobody to
 * say it to. So the resign and draw buttons exist only while `game.isOnline`
 * says an online seat is in the chair (the `local` event carries the mode, and
 * the switch in net/online.js exposes the getter), and are hidden outright in
 * a hotseat, not merely disabled. Resign asks once - "resign? yes / no" - so a
 * stray tap cannot end a game; his offer is a line with accept and decline,
 * fed by the `draw-offer` / `draw-decline` events the seat emits.
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
  askMs: 6000,         // "resign?" withdraws itself if nobody answers it
  noteMs: 2600,        // how long a passing note ("draw declined") stays
  iqPopMs: 1100,       // the "-N" that rises off the IQ readout when a move costs points
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
    names: { w: pick('name-w'), b: pick('name-b') },
    tags: { w: pick('turn-w'), b: pick('turn-b') },
    captures: { w: pick('captured-w'), b: pick('captured-b') },
    focus: pick('piece-focus'),
    intensity: pick('game-intensity'),
    intensityMeter: pick('intensity-meter'),
    intensityLabel: pick('intensity-label'),
    // the online block; every node optional, a page without it is a quieter HUD
    online: {
      root: pick('hud-online'),
      note: pick('hud-online-note'),
      btns: pick('hud-online-btns'),
      resign: pick('hud-resign'),
      draw: pick('hud-draw'),
      ask: pick('hud-resign-ask'),
      yes: pick('hud-resign-yes'),
      no: pick('hud-resign-no'),
      offer: pick('hud-draw-ask'),
      accept: pick('hud-draw-accept'),
      decline: pick('hud-draw-decline'),
    },
    // the stands (net/watch.js), and "N watching" for everyone; optional like the rest
    watch: { root: pick('hud-watch'), tag: pick('hud-watch-tag'), flip: pick('hud-flip'), leave: pick('hud-leave') },
    watchers: pick('hud-watchers'),
  };

  // The IQ readout (game/iq.js), beside the name on a LOCAL seat's card only. The opponent's card
  // never gets one, online or not. Built here so the page markup stays as it was.
  const iqEl = { w: null, b: null };
  for (const s of ['w', 'b']) {
    const head = el.chip[s] && el.chip[s].querySelector('.player-head');
    if (!head) continue;
    const wrap = document.createElement('span');
    wrap.className = 'iq';
    wrap.id = 'iq-' + s;
    wrap.hidden = true;
    wrap.appendChild(document.createElement('span')).className = 'iq-n';
    const who = head.querySelector('.who');
    head.insertBefore(wrap, who ? who.nextSibling : null);
    iqEl[s] = wrap;
  }
  const shownIq = { w: null, b: null };

  if (el.status) el.status.dataset.hudOwned = 'true';
  let deal = null;
  let active = 'w';         // whose clock is running
  let over = null;          // the gameover payload, once it lands
  let meter = 0;
  let lastSecond = -1;      // so the shiver fires once a second, not every tick
  let hinted = false;
  let online = false;       // an online seat is in the chair (see the header)
  let asking = false;       // "resign?" is up
  let askTimer = null;
  let away = false;         // the server's last word: he is not there
  let flash = '';           // a passing note, and the timer that takes it down
  let flashTimer = null;
  let watching = false;     // a spectator's seat is in the chair: both IQs, no verbs
  let watcherCount = 0;     // "N watching", from the seat's or the stands' poll
  let viewSide = 'w';       // the side the stands look from; flip board turns it
  const timers = new Set();
  const unbind = [];
  const undom = [];         // DOM listeners, taken off on dispose

  const later = (fn, ms) => { const id = setTimeout(() => { timers.delete(id); fn(); }, ms); timers.add(id); return id; };
  const on = (type, fn) => {
    if (!bus) return;
    const safe = (p) => { try { fn(p); } catch (e) { console.warn('[pbp/hud] ' + type + ': ' + (e && e.message)); } };
    try { unbind.push(bus.on(type, safe)); } catch { /* a bus that will not take listeners is still a bus */ }
  };

  /* ---- the sliding light -------------------------------------------------- */

  // The turn changes inside fixed cards, so controls never move under a pointer.
  function place() {
    if (el.line) el.line.classList.toggle('on', !over);
  }

  function paintNames() {
    const own = identity().displayName || window.PBP?.settings?.playerName;
    const seat = deal?.match?.side || (game?.seats?.length === 1 ? game.seats[0] : null);
    for (const side of ['w', 'b']) {
      const supplied = deal?.players?.[side];
      const name = typeof supplied === 'string' ? supplied : supplied?.name;
      const label = name || (online && side === seat ? own : online ? deal?.match?.opponent?.name : '') || (side === 'w' ? 'White' : 'Black');
      if (el.names[side]) { el.names[side].textContent = label; el.names[side].title = label; }
      // a rating, when the deal carries one (the stands), rides on the seat label: "White 1520"
      const seatLabel = el.chip[side] && el.chip[side].querySelector('.seat-label');
      const rating = supplied && typeof supplied === 'object' && supplied.rating !== null && Number.isFinite(Number(supplied.rating)) ? Math.round(Number(supplied.rating)) : null;
      if (seatLabel) seatLabel.textContent = (side === 'w' ? 'White' : 'Black') + (rating !== null ? ' ' + rating : '');
    }
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
      if (el.tags[s]) el.tags[s].textContent = over ? '' : s === checked ? 'In check' : s === active ? 'To move' : '';
    }
    if (!over && el.status) {
      const name = el.names[active]?.textContent || sideWord(active);
      el.status.textContent = checked ? name + ' is in check' : name + ' to move';
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
    const low = !snap?.untimed && left != null && Number.isFinite(left) && left < T.lowMs && !over;
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
    const history = game.rules.chess?.history({ verbose: true }) || [];
    const glyph = { p: '♟', n: '♞', b: '♝', r: '♜', q: '♛' };
    for (const s of ['w', 'b']) {
      if (el.tally[s]) el.tally[s].textContent = tallyWord(position, s);
      const taken = history.filter(m => m.color === s && m.captured).map(m => m.captured);
      if (el.captures[s]) {
        el.captures[s].textContent = taken.map(t => glyph[t] || '').join('');
        const label = taken.length ? 'Captured: ' + taken.map(t => NAMES[t]).join(', ') : 'No captures';
        el.captures[s].setAttribute('aria-label', label);
        el.captures[s].title = label;
      }
    }
  }

  /* ---- the meter, as a warm frame ----------------------------------------- */

  function setMeter(v) {
    meter = clamp01(v);
    if (el.intensityMeter) el.intensityMeter.value = meter;
    if (el.intensityLabel) el.intensityLabel.textContent = meter < .25 ? 'Calm' : meter < .55 ? 'Building' : meter < .8 ? 'Intense' : 'Full tilt';
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

  /* ---- the IQ readout ----------------------------------------------------- */

  /** What window.PBP.iq says for each seat; a seat it does not grade shows nothing. */
  function paintIq() {
    const iq = typeof window !== 'undefined' ? window.PBP?.iq : null;
    for (const s of ['w', 'b']) {
      const node = iqEl[s];
      if (!node) continue;
      let v = null;
      try { v = iq ? iq.value(s) : null; } catch { v = null; }
      shownIq[s] = v;
      node.hidden = v == null;
      if (v != null) node.firstChild.textContent = 'IQ ' + v;
    }
  }

  /** A graded move: the number changes, and what it cost rises off it (not under reduced motion). */
  function iqLanded(g) {
    const s = g && (g.side === 'w' || g.side === 'b') ? g.side : null;
    const before = s ? shownIq[s] : null;
    paintIq();
    const node = s && iqEl[s];
    const drop = before != null && shownIq[s] != null ? before - shownIq[s] : 0;
    if (!node || !(drop > 0) || reducedMotion()) return;
    const pop = document.createElement('span');
    pop.className = 'iq-pop';
    pop.setAttribute('aria-hidden', 'true');
    pop.textContent = '-' + drop;
    node.appendChild(pop);
    later(() => pop.remove(), T.iqPopMs);
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

  /* ---- resign and the draw, online only ---------------------------------- */

  /** What the seat says stands: 'me' | 'them' | null. A hotseat says null. */
  function drawOffer() {
    try { return game && game.drawOffer ? game.drawOffer() : null; } catch { return null; }
  }

  /** The seat this client is in, for telling his decline from the echo of ours. */
  function mySide() {
    try { const s = game && game.seats; return Array.isArray(s) && s.length === 1 ? s[0] : null; } catch { return null; }
  }

  /** The one line over the buttons. A passing note wins; then whatever stands. */
  function noteText() {
    if (flash) return flash;
    if (drawOffer() === 'me') return 'draw offered';
    if (away) return 'Opponent disconnected';
    return '';
  }

  /**
   * Show the block as the state says. Everything reads from state rather than
   * being toggled in place, so an event arriving in any order lands on the
   * same picture: no online seat, or a finished game, and the whole thing is
   * gone; his offer takes the buttons' row; the question takes it too.
   */
  function paintOnline() {
    const o = el.online;
    if (!o.root) return;
    const show = online && !over;
    o.root.hidden = !show;
    if (!show) return;
    const offer = drawOffer();
    const theirs = offer === 'them';
    if (o.btns) o.btns.hidden = asking || theirs;
    if (o.ask) o.ask.hidden = !asking;
    if (o.offer) o.offer.hidden = asking || !theirs;
    if (o.draw) o.draw.disabled = offer === 'me';
    const text = noteText();
    if (o.note) { o.note.textContent = text; o.note.hidden = !text; }
  }

  function stopAsking() {
    asking = false;
    if (askTimer) { clearTimeout(askTimer); timers.delete(askTimer); askTimer = null; }
  }

  function note(text) {
    flash = text;
    if (flashTimer) { clearTimeout(flashTimer); timers.delete(flashTimer); }
    flashTimer = later(() => { flash = ''; flashTimer = null; paintOnline(); }, T.noteMs);
    paintOnline();
  }

  /** The stands' block, and the "N watching" line every seat gets once somebody is. */
  function paintWatch() {
    const w = el.watch;
    if (w.root) w.root.hidden = !watching;
    if (w.tag && watching) {
      let ms = 10000;
      try { const d = game?.current?.delayMs?.(); if (Number.isFinite(d)) ms = d; } catch { /* the default */ }
      w.tag.textContent = 'Watching - ' + Math.round(ms / 1000) + ' s behind';
    }
    if (el.watchers) {
      const show = !!deal && watcherCount > 0;
      el.watchers.hidden = !show;
      if (show) el.watchers.textContent = watcherCount + ' watching';
    }
  }

  /** A new deal. The mode on the `local` event is the word; the getter is the fallback. */
  function newDeal(p) {
    if (p) deal = p;
    try { watching = p ? p.mode === 'watch' : !!(game && game.isWatch); } catch { watching = false; }
    watcherCount = 0;
    viewSide = 'w';
    let isOnline = false;
    try { isOnline = !!(game && game.isOnline); } catch { isOnline = false; }
    if (p && typeof p.mode === 'string') isOnline = p.mode === 'online';
    online = isOnline;
    over = null;
    away = false;
    flash = '';
    stopAsking();
    paintNames();
    paintCheck();
    paintOnline();
    paintWatch();
    paintMenu();
  }

  const click = (node, fn) => {
    if (!node) return;
    const h = (e) => {
      e.preventDefault();
      try { fn(); } catch (err) { console.warn('[pbp/hud] ' + (err && err.message)); }
    };
    node.addEventListener('click', h);
    undom.push(() => node.removeEventListener('click', h));
  };
  const verb = (name) => { try { if (game && typeof game[name] === 'function') game[name](); } catch { /* the seat said no */ } };

  click(el.online.resign, () => {
    // Asked, never done: the tap that ends a game is the second one.
    stopAsking();
    asking = true;
    askTimer = later(() => { askTimer = null; asking = false; paintOnline(); }, T.askMs);
    paintOnline();
  });
  click(el.online.no, () => { stopAsking(); paintOnline(); });
  click(el.online.yes, () => { stopAsking(); verb('resign'); paintOnline(); });
  click(el.online.draw, () => { verb('offerDraw'); paintOnline(); });
  click(el.online.accept, () => { verb('acceptDraw'); paintOnline(); });
  click(el.online.decline, () => { verb('declineDraw'); paintOnline(); });
  click(el.watch.leave, () => bus?.emit('watch-leave'));
  click(el.watch.flip, () => {
    viewSide = other(viewSide);
    try { opts.board?.setSide?.(viewSide, reducedMotion()); } catch { /* no rig */ }
  });

  const seenNotices = new Set();
  on('notice', p => {
    const message = typeof p?.text === 'string' ? p.text : '';
    const node = pick('game-notice');
    if (!node || !message || seenNotices.has(message)) return;
    seenNotices.add(message); node.textContent = message; node.hidden = false;
    later(() => { node.hidden = true; }, 6000);
  });
  on('local', () => { seenNotices.clear(); const node = pick('game-notice'); if (node) node.hidden = true; });
  const options = pick('game-options');
  const panel = pick('game-options-panel');
  const sound = pick('game-sound');
  const motion = pick('game-reduced');
  const closeOptions = () => { if (panel) panel.hidden = true; options?.setAttribute('aria-expanded', 'false'); };
  click(options, () => {
    if (!panel) return;
    panel.hidden = !panel.hidden;
    options.setAttribute('aria-expanded', String(!panel.hidden));
    stopSurrender();
  });
  for (const button of root.querySelectorAll('[data-experience]')) click(button, () => setPresentation({ experience: button.dataset.experience }));
  // SURRENDER (owner, 2026-10-02): in Options for every mode, asked once.
  // Solo gives the game to the computer, hotseat gives it to the side not on
  // the move, online is the server's resign. A local loss is THE FALL.
  const surrenderBox = pick('game-surrender-box');
  const surrenderBtn = pick('game-surrender');
  const surrenderAsk = pick('game-surrender-ask');
  let surrendering = null;   // the "give up this game?" question, while it stands
  function stopSurrender(repaint = true) {
    if (surrendering) { clearTimeout(surrendering); timers.delete(surrendering); surrendering = null; }
    if (repaint) paintSurrender();
  }
  function paintSurrender() {
    let live = !!deal && !over && !watching;   // the stands have nothing to give up
    try { if (window.PBP?.door?.isUp?.() || game?.isOver?.()) live = false; } catch { /* no referee yet */ }
    if (!live) stopSurrender(false);
    if (surrenderBox) surrenderBox.hidden = !live;
    if (surrenderBtn) surrenderBtn.hidden = !!surrendering;
    if (surrenderAsk) surrenderAsk.hidden = !surrendering;
  }
  click(surrenderBtn, () => {
    stopSurrender(false);
    surrendering = later(() => { surrendering = null; paintSurrender(); }, T.askMs);
    paintSurrender();
  });
  click(pick('game-surrender-no'), () => stopSurrender());
  click(pick('game-surrender-yes'), () => {
    stopSurrender(false);
    closeOptions();
    if (online) verb('resign');
    else if (game?.isSolo) verb('resign');
    else if (game && typeof game.resign === 'function') { try { game.resign(game.turn()); } catch { /* already over */ } }
    paintSurrender();
  });
  const menuButton = pick('game-menu');
  function paintMenu() {
    paintSurrender();
    if (menuButton) { menuButton.textContent = deal?.mode === 'solo' ? 'Save and menu' : watching ? 'Leave' : 'Menu'; menuButton.disabled = online && !over; }
    const note = pick('game-menu-note');
    if (note) note.hidden = !online || !!over;
  }
  click(menuButton, () => { if (online && !over) return; closeOptions(); bus?.emit(watching ? 'watch-leave' : 'menu-request'); });
  const watchable = pick('game-watchable');
  const watchableChange = () => setPresentation({ letPeopleWatch: watchable.checked });
  watchable?.addEventListener('change', watchableChange);
  undom.push(() => watchable?.removeEventListener('change', watchableChange));
  const soundChange = () => setPresentation({ volume: sound.checked ? .6 : 0 });
  const motionChange = () => setPresentation({ reducedMotion: motion.checked });
  const follow = pick('game-follow');
  const replays = pick('game-replays');
  const followChange = () => setPresentation({ followCam: follow.checked });
  const replaysChange = () => setPresentation({ replays: replays.checked });
  follow?.addEventListener('change', followChange);
  replays?.addEventListener('change', replaysChange);
  undom.push(() => { follow?.removeEventListener('change', followChange); replays?.removeEventListener('change', replaysChange); });
  const invertX = pick('game-invert-x');
  const invertY = pick('game-invert-y');
  const invertXChange = () => setPresentation({ invertX: invertX.checked });
  const invertYChange = () => setPresentation({ invertY: invertY.checked });
  invertX?.addEventListener('change', invertXChange);
  invertY?.addEventListener('change', invertYChange);
  undom.push(() => { invertX?.removeEventListener('change', invertXChange); invertY?.removeEventListener('change', invertYChange); });
  for (const button of root.querySelectorAll('[data-turncard]')) click(button, () => setPresentation({ turnCard: button.dataset.turncard }));
  for (const button of root.querySelectorAll('[data-amount]')) click(button, () => setPresentation({ amount: button.dataset.amount }));
  for (const button of root.querySelectorAll('[data-ramp]')) click(button, () => setPresentation({ rampSpeed: button.dataset.ramp }));
  const strength = pick('game-strength');
  const strengthOut = pick('game-strength-out');
  const strengthInput = () => {
    if (strengthOut) strengthOut.textContent = strength.value + '%';
    setPresentation({ strength: Number(strength.value) / 100 });
  };
  strength?.addEventListener('input', strengthInput);
  undom.push(() => strength?.removeEventListener('input', strengthInput));
  sound?.addEventListener('change', soundChange);
  motion?.addEventListener('change', motionChange);
  undom.push(() => { sound?.removeEventListener('change', soundChange); motion?.removeEventListener('change', motionChange); });
  const outside = e => { if (!panel?.hidden && !e.target.closest('.game-settings')) closeOptions(); };
  const optionKey = e => {
    if (e.key !== 'Escape' || panel?.hidden) return;
    e.preventDefault(); e.stopPropagation(); closeOptions(); options?.focus();
    // the panel has already spent this press as the game's pause, so the game pauses too
    window.PBP?.escapePause?.();
  };
  document.addEventListener('keydown', optionKey);
  undom.push(() => document.removeEventListener('keydown', optionKey));
  document.addEventListener('pointerdown', outside);
  undom.push(() => document.removeEventListener('pointerdown', outside));
  const pictures = attachPictures(pick('game-pictures'));
  undom.push(() => pictures?.dispose?.());
  const preferenceOff = onPresentation(p => {
    if (el.intensity) el.intensity.hidden = p.experience !== 'distraction';
    pictures?.setVisible?.(p.experience === 'distraction');
    for (const button of root.querySelectorAll('[data-experience]')) button.setAttribute('aria-pressed', String(button.dataset.experience === p.experience));
    const copy = pick('experience-description');
    if (copy) copy.textContent = p.experience === 'classic' ? 'The board, animated captures and sound.' : 'Effects build while you think and let go when you move.';
    if (sound) { sound.checked = p.volume > 0; sound.disabled = p.soundLocked; }
    if (motion) { motion.checked = p.reducedMotion; motion.disabled = p.motionLocked; }
    // Reduced motion keeps the camera at the seat and drops the replay; the boxes say so.
    if (follow) { follow.checked = p.followCam && !p.reducedMotion; follow.disabled = p.reducedMotion; }
    if (replays) { replays.checked = p.replays && !p.reducedMotion; replays.disabled = p.reducedMotion; }
    if (watchable) watchable.checked = p.letPeopleWatch !== false;
    if (invertX) invertX.checked = p.invertX;
    if (invertY) invertY.checked = p.invertY;
    for (const button of root.querySelectorAll('[data-turncard]')) button.setAttribute('aria-pressed', String(button.dataset.turncard === p.turnCard));
    const dials = pick('game-dials');
    if (dials) dials.hidden = p.experience !== 'distraction';
    for (const button of root.querySelectorAll('[data-amount]')) button.setAttribute('aria-pressed', String(button.dataset.amount === p.amount));
    for (const button of root.querySelectorAll('[data-ramp]')) button.setAttribute('aria-pressed', String(button.dataset.ramp === p.rampSpeed));
    if (strength && document.activeElement !== strength) strength.value = String(Math.round(p.strength * 100));
    if (strengthOut) strengthOut.textContent = Math.round(p.strength * 100) + '%';
    const note = pick('game-preference-note');
    if (note) { note.hidden = !p.motionLocked && !p.soundLocked; note.textContent = 'App and system preferences stay in effect.'; }
    setMeter(p.experience === 'classic' ? 0 : meter);
  });
  unbind.push(preferenceOff);
  on('local', closeOptions);

  /* ---- wiring ------------------------------------------------------------- */

  unbind.push(onIdentity(() => { paintNames(); paintCheck(); }));
  on('piece-focus', (p) => {
    if (!el.focus) return;
    const names = { p: 'Pawn', n: 'Knight', b: 'Bishop', r: 'Rook', q: 'Queen', k: 'King' };
    el.focus.textContent = p?.square ? (names[p.piece] || 'Piece') + ' · ' + p.square + (p.selected ? ' · Choose a highlighted square' : '') : 'Click a piece, then a square. Or drag.';
    el.focus.classList.toggle('selected', !!p?.selected);
  });
  on('clock', (snap) => {
    if (snap && snap.active && snap.active !== active && !over) { setActive(snap.active); paintCheck(); }
    paintLow(snap);
  });
  on('turn', (p) => {
    setActive(p && p.side === 'b' ? 'b' : 'w');
    paintCheck();
    paintTally();
    // a move clears any standing offer, server-side and in the seat, so the
    // block is repainted off the seat's word rather than told
    paintOnline();
  });
  on('check', () => paintCheck());
  on('capture', () => paintTally());
  on('local', (p) => { newDeal(p); paintTally(); place(true); paintIq(); });
  on('iq', iqLanded);
  on('newgame', paintIq);
  on('takeback', paintIq);
  // the server corrected a guessed online seat; the grader (subscribed first, at boot) has moved over already
  on('seat', paintIq);
  on('draw-offer', () => { stopAsking(); paintOnline(); });
  on('draw-decline', (p) => {
    const by = p && (p.by === 'w' || p.by === 'b') ? p.by : null;
    // his no, not the echo of ours
    if (by && by !== mySide()) note('draw declined'); else paintOnline();
  });
  on('opponent', (p) => { away = !!(p && p.online === false); paintOnline(); });
  on('watchers', (p) => { watcherCount = Math.max(0, Number(p && p.count) || 0); paintWatch(); });
  // the stands learn both names off the server's first answer, not off the deal
  on('players', (p) => {
    if (!watching || !p) return;
    deal = Object.assign({}, deal, { players: { w: p.white || null, b: p.black || null } });
    paintNames();
    paintCheck();
  });
  on('gameover', (p) => {
    over = p || { result: 'over' };
    const line = endLine(over);
    if (line && el.status) el.status.textContent = line;
    for (const s of ['w', 'b']) {
      if (!el.chip[s]) continue;
      el.chip[s].classList.remove('low', 'check', 'shiver', 'shiver-hard');
    }
    if (el.line) el.line.classList.remove('on');
    for (const tag of Object.values(el.tags)) if (tag) tag.textContent = '';
    paintMenu();
    stopAsking();
    paintOnline();
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

  // The list is HUD furniture too, but it is the only part that takes a click,
  // so it lives in its own file and is loaded late and guarded like the rest.
  let moves = null;
  import('./movelist.js').then((m) => {
    moves = m.createMoveList({ bus, game, board: opts.board || null, root: pick('moves') });
  }).catch((e) => console.warn('[pbp] move list missing', e));

  setActive(game && game.turn ? game.turn() : 'w', true);
  paintTally();
  paintCheck();
  setMeter(0);
  paintIq();
  // The HUD may be built mid-game (it is loaded late), so the block starts
  // from whatever is in the chair rather than waiting for the next deal.
  newDeal(null);

  function dispose() {
    if (el.status) delete el.status.dataset.hudOwned;
    if (moves) { try { moves.dispose(); } catch { /* already gone */ } moves = null; }
    for (const off of unbind) { try { off(); } catch { /* already gone */ } }
    unbind.length = 0;
    for (const off of undom) { try { off(); } catch { /* already gone */ } }
    undom.length = 0;
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
        iq: { w: iqEl.w && !iqEl.w.hidden ? iqEl.w.firstChild.textContent : null, b: iqEl.b && !iqEl.b.hidden ? iqEl.b.firstChild.textContent : null },
        line: el.line ? el.line.style.transform : null,
        reducedMotion: reducedMotion(),
        moves: moves ? moves.debug() : null,
        online: {
          on: online,
          shown: !!(el.online.root && !el.online.root.hidden),
          asking,
          offer: drawOffer(),
          away,
          note: el.online.note && !el.online.note.hidden ? el.online.note.textContent : null,
          buttons: !!(el.online.btns && !el.online.btns.hidden),
          offerRow: !!(el.online.offer && !el.online.offer.hidden),
        },
        watch: {
          on: watching,
          shown: !!(el.watch.root && !el.watch.root.hidden),
          watchers: el.watchers && !el.watchers.hidden ? el.watchers.textContent : null,
          names: { w: el.names.w ? el.names.w.textContent : null, b: el.names.b ? el.names.b.textContent : null },
        },
      };
    },
  };
}

export default createHud;
