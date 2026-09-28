// Hosted access comes from the native entitlement projection, never from a URL.
export function breakoutAccess(value, { hosted = false, demo = false } = {}) {
  const full = !demo && (hosted
    ? value?.storyLimit === 8 && value?.endless === true && value?.demo === false
    : value ? value.storyLimit === 8 && value.endless === true && !value.demo : true);
  return { storyLimit: full ? 8 : 3, endless: full, demo: !full };
}
