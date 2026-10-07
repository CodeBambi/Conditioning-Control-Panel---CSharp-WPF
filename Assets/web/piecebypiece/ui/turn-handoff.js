// The change-of-turn card. A short visual handoff; it never changes turns, clocks, or human input.
//
// Three looks (owner, 2026-09-28), picked in Options: 'slam' (a word stamped over a
// band), 'ribbon' (a tilted strip swiping across) and 'tag' (a pill from the top
// edge and a glow along the mover's side). Your side is pink, the other lilac.
// The GATE is handoffSeconds, as before: the computer's reply waits exactly that
// long. The card's own exit may run a little past it and never holds anything.
// As it comes in, the card says so on the bus ('turn-card' {side, style, short}):
// the card never shares the air with a capture replay, whose sound fades out under
// its entrance (owner, 2026-09-29; audio/sfx.js).
import { presentation } from '../game/preferences.js';
import { formatClock } from '../game/clock.js';

export function handoffSeconds(clock) {
  const state = clock?.snapshot?.();
  if (!state || state.untimed || state.total === 0) return .65;
  const least = Math.min(state.w ?? Infinity, state.b ?? Infinity);
  return least < 10000 ? 0 : least < 30000 ? .20 : .65;
}

/** The pause between the move coming to rest and the card: a second, less when time is short. */
export function settleSeconds(duration, afterReplay = false) {
  if (duration < .5) return .15;
  return afterReplay ? .45 : 1.0;
}

export const CARD_STYLES = Object.freeze(['slam', 'ribbon', 'tag']);
// Short and small (owner, 2026-10-01): it marks the turn, it does not hold the eye.
const LIFE = { slam: .7, ribbon: .75, tag: .8 };
const clamp01 = v => Math.max(0, Math.min(1, v));
const easeOut = t => 1 - (1 - t) ** 3;
const easeIn = t => t * t * t;
const easeOutBack = t => { const c1 = 1.7, c3 = c1 + 1; return 1 + c3 * (t - 1) ** 3 + c1 * (t - 1) ** 2; };

function stillMotion() {
  const w = globalThis.window;
  return !!(w?.PBP?.reducedMotion || w?.PBP?.settings?.reducedMotion || w?.matchMedia?.('(prefers-reduced-motion: reduce)').matches);
}
function chosenStyle() {
  try { const s = presentation().turnCard; return CARD_STYLES.includes(s) ? s : 'slam'; } catch { return 'slam'; }
}

export function createTurnHandoff({ bus, game, board, root = null, menuOpen = () => false }) {
  let lastPly = null, pending = null, shown = null;
  const doc = typeof document === 'undefined' ? null : document;
  const card = doc ? doc.createElement('div') : null;
  let parts = null;
  if (card) {
    card.className = 'turn-card';
    card.setAttribute('role', 'status');
    card.setAttribute('aria-live', 'polite');
    card.hidden = true;
    card.innerHTML = '<div class="tc-slam"><i class="tc-band"></i><b class="tc-word"></b><span class="tc-clock"></span></div>'
      + '<div class="tc-ribbon"><i class="tc-rband"></i><b class="tc-rword"></b></div>'
      + '<div class="tc-tag"><i class="tc-edge"></i><div class="tc-pill"><i class="tc-pip"></i><b class="tc-tword"></b><span class="tc-tclock"></span></div></div>';
    (root || doc.body).append(card);
    const q = s => card.querySelector(s);
    parts = { slam: q('.tc-slam'), ribbon: q('.tc-ribbon'), tag: q('.tc-tag'), band: q('.tc-band'), word: q('.tc-word'), clock: q('.tc-clock'),
      rband: q('.tc-rband'), rword: q('.tc-rword'), edge: q('.tc-edge'), pill: q('.tc-pill'), tword: q('.tc-tword'), tclock: q('.tc-tclock') };
  }
  function hide() { shown = null; if (card) card.hidden = true; }
  function clear() { pending = null; }
  function reset() { clear(); hide(); lastPly = null; }
  function mine(side) { return !game.isSolo && !game.isOnline ? side === 'w' : !!game.seats?.includes(side); }
  function label(side) {
    if (game.isSolo || game.isOnline) {
      if (game.seats?.includes(side)) return 'Your turn';
      return game.isSolo ? "Computer's turn" : "Opponent's turn";
    }
    return side === 'w' ? 'White to move' : 'Black to move';
  }
  function clockText(side) {
    const s = game.clock?.snapshot?.();
    if (!s || s.untimed || s.total === 0 || !Number.isFinite(s[side])) return '';
    try { return formatClock(s[side]); } catch { return ''; }
  }
  function show(side, short) {
    const still = stillMotion();
    const style = short || still ? 'tag' : chosenStyle();
    shown = { side, style, age: 0, life: short ? .5 : LIFE[style], still, text: label(side) };
    bus.emit?.('turn-card', { side, style, short: !!short, mine: mine(side) });
    if (!card) return;
    card.hidden = false;
    card.dataset.style = style;
    card.style.setProperty('--tc', mine(side) ? '#ff5aa5' : '#b99cff');
    card.classList.toggle('theirs', !mine(side));
    const clock = clockText(side);
    parts.word.textContent = parts.rword.textContent = parts.tword.textContent = shown.text;
    parts.clock.textContent = parts.tclock.textContent = clock;
    parts.clock.hidden = !clock;
    for (const k of CARD_STYLES) parts[k].hidden = k !== style;
    paint();
  }
  // One frame of the card, from its own age. Driven by update(), so a paused tab freezes it.
  function paint() {
    if (!shown || !parts) return;
    const t = shown.age, life = shown.life, out = clamp01((t - (life - .16)) / .16);
    if (shown.still) {
      parts.pill.style.transform = 'translateX(-50%)';
      parts.pill.style.opacity = String(clamp01(t / .15) * (1 - out));
      parts.edge.style.opacity = '0';
      return;
    }
    if (shown.style === 'slam') {
      const i = clamp01(t / .14);
      let s = t < .14 ? 1.4 - .45 * easeOut(i) : .95 + .05 * clamp01((t - .14) / .1);
      s += .06 * out;
      const shake = t > .14 && t < .36 ? Math.sin(t * 120) * 4 * (1 - (t - .14) / .22) : 0;
      parts.word.style.transform = `translate(calc(-50% + ${shake.toFixed(2)}px), -50%) scale(${s.toFixed(3)}) rotate(-2deg)`;
      parts.word.style.opacity = String(Math.min(1, i * 1.5) * (1 - out));
      parts.band.style.transform = `skewY(-2deg) scaleX(${easeOut(clamp01((t - .03) / .14)).toFixed(3)})`;
      parts.band.style.opacity = String(.93 * (1 - out));
      parts.clock.style.transform = `translate(-50%, ${((1 - easeOut(clamp01((t - .2) / .2))) * 14).toFixed(1)}px)`;
      parts.clock.style.opacity = String(clamp01((t - .2) / .15) * (1 - out));
    } else if (shown.style === 'ribbon') {
      const inT = easeOut(clamp01(t / .2)), outT = easeIn(clamp01((t - (life - .3)) / .3));
      parts.rband.style.transform = `translateX(${((1 - inT) * -115 + outT * 115).toFixed(2)}%) skewY(-6deg)`;
      const xw = (1 - easeOut(clamp01((t - .05) / .22))) * -70 + easeIn(clamp01((t - (life - .32)) / .3)) * 90;
      parts.rword.style.transform = `translate(calc(-50% + ${xw.toFixed(2)}vw), -50%) rotate(-6deg) skewX(-10deg)`;
    } else {
      const inT = easeOutBack(clamp01(t / .24));
      parts.pill.style.transform = `translate(-50%, ${((1 - inT) * -70 - easeIn(out) * 70).toFixed(1)}px)`;
      parts.pill.style.opacity = '1';
      parts.edge.style.opacity = String(((.35 + .35 * Math.sin(t * 9)) * (1 - out) * clamp01(t / .15)).toFixed(3));
    }
  }
  const off = [bus.on('turn', ({ side, ply }) => {
    const previous = lastPly;
    lastPly = ply;
    if (previous === ply) return;
    if (previous === null || ply < previous || game.isOver() || menuOpen()) { clear(); hide(); return; }
    pending = { side, ply, age: 0, shown: false };
    hide();
  }), ...['local', 'newgame', 'gameover', 'menu', 'replay'].map(name => bus.on(name, reset)),
  bus.on('takeback', () => { clear(); hide(); lastPly = game.plies(); })];
  return {
    clear: reset,
    ready: () => !pending || handoffSeconds(game.clock) === 0,
    update(dt) {
      if (menuOpen() || game.isOver()) { reset(); return; }
      if (shown) { shown.age += Math.max(0, dt); if (shown.age >= shown.life) hide(); else paint(); }
      if (!pending) return;
      const duration = handoffSeconds(game.clock);
      if (!duration) { clear(); hide(); return; }
      // The move settles first, and a full-screen capture replay plays out, before the card.
      if (board.anim?.busy?.() || board.director?.holding?.()) {
        pending.age = 0; pending.still = 0;
        // Only a full-screen replay that held the card earns the shorter beat. The
        // recording before a corner replay holds too, but a corner never makes anyone
        // wait: it keeps the whole beat, so its slowed hit is heard before the card.
        if (board.director?.holding?.() && board.director?.active?.()) pending.replayed = true;
        return;
      }
      // Then a beat with the man standing still on his square (owner, 2026-09-28): the card
      // never lands on top of the capture. Shorter after a replay, and under clock pressure.
      if (!pending.shown) {
        pending.still = (pending.still || 0) + Math.max(0, dt);
        if (pending.still < settleSeconds(duration, pending.replayed)) return;
        pending.shown = true; show(pending.side, duration < .5);
        return;   // the card's own time starts on the next frame
      }
      pending.age += Math.max(0, dt);
      if (pending.age >= duration) clear();
    },
    dispose() { for (const stop of off) stop(); reset(); card?.remove(); },
    debug: () => pending ? { ...pending, text: label(pending.side) } : null,
    shown: () => shown ? { side: shown.side, style: shown.style, text: shown.text } : null,
  };
}
