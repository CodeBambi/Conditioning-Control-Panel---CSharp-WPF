// Small physical responses. The server result and the spin clock never enter these curves.
export function chipLanding(age, still = false) {
  if (still || age < 0 || age >= 420) return { lift: 0, tilt: 0 };
  if (age < 120) { const q = age / 120; return { lift: 2 * (1 - q * q), tilt: .18 * (1 - q) }; }
  const q = (age - 120) / 300;
  return { lift: .32 * Math.sin(q * Math.PI * 2) ** 2 * (1 - q), tilt: -.07 * Math.sin(q * Math.PI * 3) * (1 - q) };
}
export function traceProgress(age, still = false) {
  return still || age < 0 || age >= 650 ? null : age / 650;
}
