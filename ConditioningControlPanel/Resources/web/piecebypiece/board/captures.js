// Capture choreography, in board units. Arrival stays fast; the victim owns the tail.
export const TRAVEL = Object.freeze({
  p: { hop: 0.23, lean: 0.09, gain: 0.70, damping: 1.8 },
  n: { hop: 0.95, lean: 0.34, gain: 1.15, damping: 1.05 },
  b: { hop: 0.13, lean: 0.16, gain: 0.8, damping: 0.95 },
  r: { hop: 0.045, lean: 0.045, gain: 0.65, damping: 1.7 },
  q: { hop: 0.10, lean: 0.11, gain: 0.65, damping: 1.35 },
  k: { hop: 0.075, lean: 0.06, gain: 0.75, damping: 1.55 },
});

const move = (id, push, lift, angle, spin, bounce = 0, curl = 0) =>
  Object.freeze({ id, push, lift, angle, spin, bounce, curl });
export const CAPTURES = Object.freeze({
  p: [move('bump', .48, .20, 1.35, 0), move('stomp', .34, .36, 1.48, 0, 2), move('tumble', .50, .27, 2.6, .5)],
  n: [move('pounce', .45, .38, 1.50, 0, 2), move('somersault', .58, .55, 5.8, .3), move('ricochet', .52, .32, 1.40, 1.2, 2, .18)],
  b: [move('sweep', .58, .13, 1.5, 1.6, 0, .18), move('twirl', .40, .42, .65, 5.4, 0, -.14), move('flick', .55, .48, 2.2, .6)],
  r: [move('shove', .65, .07, 1.5, 0), move('bowl', .60, .10, 3.8, 0), move('bumpers', .52, .20, 1.5, .2, 2)],
  q: [move('pirouette', .36, .45, .50, 5.6, 0, .12), move('dismiss', .64, .18, 1.45, 1.1, 0, -.16), move('curtsy', .38, .13, 1.45, .2, 1)],
  k: [move('bow', .40, .07, 1.48, 0), move('royal-bump', .48, .25, 1.5, .35, 2), move('topple', .56, .10, 1.65, .75)],
});

// A shuffled bag per type: every variation is seen, with no repeat at bag boundaries.
export function createCaptureDeck(random = Math.random) {
  const bags = new Map(), last = new Map();
  return (type) => {
    const key = CAPTURES[type] ? type : 'p';
    let bag = bags.get(key);
    if (!bag?.length) {
      bag = [...CAPTURES[key]];
      for (let i = bag.length - 1; i > 0; i--) {
        const j = Math.min(i, Math.floor(Math.max(0, random()) * (i + 1)));
        [bag[i], bag[j]] = [bag[j], bag[i]];
      }
      if (bag[bag.length - 1] === last.get(key)) [bag[0], bag[bag.length - 1]] = [bag[bag.length - 1], bag[0]];
      bags.set(key, bag);
    }
    const picked = bag.pop();
    last.set(key, picked);
    return picked;
  };
}

const clamp = (v) => Math.max(0, Math.min(1, v));
export const CAPTURE_SECONDS = 1.04;
export function capturePose(style, seconds, reduced = false) {
  const p = clamp(seconds / (reduced ? .20 : .64));
  const e = 1 - (1 - p) ** 3;
  const fade = clamp((seconds - (reduced ? .12 : .72)) / (reduced ? .12 : .32));
  const hop = reduced ? 0 : Math.abs(Math.sin(Math.PI * p * (style.bounce || 1))) * (1 - p * .6) * style.lift;
  return {
    forward: (reduced ? .16 : style.push) * e,
    sideways: reduced ? 0 : (Math.sign(style.curl) || (style.id.length % 2 ? 1 : -1)) * .30 * e + style.curl * Math.sin(Math.PI * p) * .4,
    lift: hop, tip: (reduced ? .5 : style.angle) * e,
    spin: reduced ? 0 : style.spin * e,
    sink: fade * .35, opacity: 1 - fade,
    done: fade >= 1,
  };
}

// Presentation only: the real game clock is never paused or sped up.
export function presentationRate(clock) {
  const state = clock?.snapshot?.();
  if (!state || state.total === 0) return 1;
  const remaining = [state.w, state.b].filter(value => Number.isFinite(value) && value >= 0);
  const least = remaining.length ? Math.min(...remaining) : Infinity;
  return least < 10000 ? 2.8 : least < 30000 ? 1.8 : 1;
}
