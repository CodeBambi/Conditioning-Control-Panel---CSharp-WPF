/** Rigid mascot choreography, sampled on the paused room clock. */
export const EMI_REACTIONS = Object.freeze({ greet: 3.2, wave: 3.2, look: 4.2, bow: 3.4, present: 4, dust: 6.8, handout: 5.2 });
const REST = Object.freeze({ yaw: 0, pitch: 0, roll: 0, lift: 0, face: null, left: 0, right: 0, reach: 0, sweep: 0, tool: 0, dust: 0, brushX: 0 });
const PERIOD = { counter: 27, wheel: 19, cards: 23, roulette: 25 };
const ROUTINES = { counter: ['look', 'dust', 'present'], wheel: ['wave', 'look', 'bow'], cards: ['present', 'look', 'bow'], roulette: ['bow', 'present', 'look'] };
const smooth = v => { const x = Math.max(0, Math.min(1, v)); return x * x * (3 - 2 * x); };
const hold = (t, a, b, fade = .5) => smooth((t-a)/fade) * (1-smooth((t-b)/fade));
export function sampleEmiReaction(kind, t) {
  const duration = EMI_REACTIONS[kind];
  if (!duration || !Number.isFinite(t) || t <= 0 || t >= duration) return REST;
  const e = hold(t, 0, duration-.65, .65), p = { ...REST };
  if (kind === 'greet' || kind === 'wave') {
    p.right = (2.65 + .20 * Math.sin(t * 11)) * e; p.left = .13 * e;
    p.roll = -.045 * e; p.yaw = .12 * e; p.face = 3;
  } else if (kind === 'look') {
    p.yaw = .34 * Math.sin(t * 1.8) * e; p.pitch = -.035 * e;
    p.left = .12 * e; p.right = .07 * e;
  } else if (kind === 'bow') {
    p.pitch = .16 * e; p.left = .25 * e; p.right = .25 * e; p.reach = -.28 * e;
  } else if (kind === 'present') {
    p.yaw = -.20 * e; p.left = .85 * e; p.right = .45 * e; p.reach = -.7 * e;
    p.roll = .025 * Math.sin(t*3) * e;
  } else if (kind === 'dust') {
    // Raise behind the counter, sweep horizontally above the tray, then retract.
    p.dust = hold(t, .15, 5.5, .8);
    p.tool = hold(t, 1.25, 4.8, .35);
    p.brushX = .20 * Math.sin((t-1.6)*4.2);
    p.left = .12 * p.dust;
  } else if (kind === 'handout') {
    // Cosmetic presentation only. The purchase service owns any real reward transfer.
    const reach = hold(t, .6, 3.65, .8);
    p.left = .20 * e; p.right = .20 * e; p.reach = -1.55 * reach; p.pitch = .085 * reach;
  }
  return p;
}
export function sampleEmiGesture(id, time) {
  if (!PERIOD[id] || !Number.isFinite(time) || time < 0) return REST;
  const cycle = Math.floor(time / PERIOD[id]), kind = ROUTINES[id][cycle % 3];
  return sampleEmiReaction(kind, time % PERIOD[id] - 5);
}
