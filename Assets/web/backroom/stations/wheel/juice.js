// A start reaction on the cabinet only; the rotor keeps its exact authoritative path.
export function startRecoil(age, direction = 1, still = false) {
  if (still || age < 0 || age >= 600) return 0;
  const q = age / 600;
  return -Math.sign(direction) * .035 * Math.sin(q * Math.PI * 2) * (1 - q) ** 2;
}
