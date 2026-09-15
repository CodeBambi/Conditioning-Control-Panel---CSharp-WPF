// The wheel's runtime dressing is independent of the model's placeholder slice count.
export const REQUIRED = ['wheel_station', 'wheel_rotor', 'pointer', 'hub_lip'];
export function checkWheelNodes(names) {
  const have = new Set(names); return REQUIRED.filter((name) => !have.has(name));
}
