// Slow only the capture actors, after their first visible compression. Game time stays live.
export function impactDelay(motion) { return motion === 'b' ? .024 : motion === 'r' ? .038 : .048; }
export function impactTime(time, hit, delay) {
  const age = Math.max(0, time - hit - .018), span = delay * 2;
  if (!span || !age) return time;
  if (age >= span) return time - delay;
  // The derivative eases from 1 down to 0 and back to 1, with no position jump.
  return time - delay * (age / span - Math.sin(2 * Math.PI * age / span) / (2 * Math.PI));
}

// A quick local compression followed by a later, softer travelling response.
export function impactPulse(age, height) {
  if (age <= 0) return { dent: 0, wave: 0, travel: 0 };
  const tall = Math.max(.65, Math.min(1.45, height));
  return {
    dent: (1 - Math.exp(-age * 160)) * Math.exp(-age * 19),
    wave: Math.sin(Math.max(0, age - .028) * 30) * Math.exp(-age * 12) * tall,
    travel: Math.min(1, age / .18),
  };
}
