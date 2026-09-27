// Short approaches use two small bounds; longer travel crosses one square per bound.
const clamp = x => Math.max(0, Math.min(1, x));
export function hopPlan(from, to) {
  const distance = Math.max(Math.abs(to.x - from.x), Math.abs(to.z - from.z));
  const count = distance < .04 ? 0 : distance <= 1.25 ? 2 : Math.ceil(distance);
  const small = distance / Math.max(1, count) < .65;
  const beat = small ? .32 : .40, flightEnd = beat - .10;
  return { count, beat, flightEnd, height: small ? .13 : .26, duration: count * beat };
}
export function hopAt(plan, time) {
  if (!plan.count) return { travel: 1, height: 0, flight: 1, ring: 0 };
  const index = Math.min(plan.count - 1, Math.floor(Math.max(0, time) / plan.beat));
  const flight = clamp((time - index * plan.beat - .04) / (plan.flightEnd - .04));
  const ease = flight * flight * (3 - 2 * flight);
  let ring = 0;
  for (let i = 0; i < plan.count; i++) {
    const age = time - (i * plan.beat + plan.flightEnd);
    if (age >= 0) ring -= Math.sin(age * 29) * Math.exp(-age * 12);
  }
  return { travel: (index + ease) / plan.count, height: Math.sin(Math.PI * flight) * plan.height, flight, ring };
}
