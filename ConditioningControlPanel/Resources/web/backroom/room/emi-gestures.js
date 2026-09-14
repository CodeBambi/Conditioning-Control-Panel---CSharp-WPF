/** Station personality over the shared EMI idle. Angles are local radians.
 * Sample from the controller's paused clock, never wall time. These gestures
 * acknowledge the room without implying a spin, card deal or prize award.
 */
const REST = Object.freeze({ yaw: 0, pitch: 0, roll: 0, lift: 0, face: null });
const TIMING = Object.freeze({
  counter: { period: 19, start: 4, duration: 4.8 },
  wheel: { period: 17, start: 7, duration: 4.2 },
  cards: { period: 23, start: 10, duration: 5.5 },
  roulette: { period: 21, start: 2, duration: 4.6 },
});

// A zero-velocity arrival and departure, including every periodic seam.
function pulse(t, start, end) {
  if (t <= start || t >= end) return 0;
  return Math.sin(Math.PI * (t - start) / (end - start)) ** 2;
}

export function sampleEmiGesture(stationId, elapsedSeconds) {
  const timing = TIMING[stationId];
  if (!timing || !Number.isFinite(elapsedSeconds) || elapsedSeconds < 0) return REST;
  const t = (elapsedSeconds % timing.period - timing.start) / timing.duration;
  if (t <= 0 || t >= 1) return REST;
  let yaw = 0, pitch = 0, roll = 0;
  if (stationId === 'counter') {
    // Two polite dips, then a small glance towards the prize shelves.
    pitch = .025 * (pulse(t, 0, .32) + pulse(t, .22, .55));
    yaw = .075 * pulse(t, .38, 1);
    roll = -.012 * pulse(t, .4, 1);
  } else if (stationId === 'wheel') {
    // Check the wheel, linger, then turn back to the visitor.
    yaw = -.085 * pulse(t, 0, 1);
    pitch = -.018 * pulse(t, .16, .9);
    roll = .012 * pulse(t, .2, .95);
  } else if (stationId === 'cards') {
    // Survey both sides of the felt without moving cards or chips.
    yaw = .065 * pulse(t, 0, .56) - .065 * pulse(t, .42, 1);
    pitch = .022 * pulse(t, .1, .9);
    roll = .016 * pulse(t, .3, .85);
  } else {
    // A restrained host's bow followed by a sideways acknowledgement.
    pitch = .05 * pulse(t, 0, .66);
    yaw = -.06 * pulse(t, .42, 1);
    roll = -.013 * pulse(t, .5, 1);
  }
  return { yaw, pitch, roll, lift: 0, face: null };
}