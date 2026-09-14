/** Conservative defaults plus a one-way resolution fallback for slow devices. */
export function createRenderBudget(device = {}, pixelRatio = 1) {
  const mobile = /Android|iPhone|iPad|Mobile/i.test(device.userAgent || '') ||
    (device.maxTouchPoints > 1 && /Macintosh/i.test(device.userAgent || '')) ||
    (device.deviceMemory > 0 && device.deviceMemory <= 4) ||
    (device.hardwareConcurrency > 0 && device.hardwareConcurrency <= 4);
  let scale = 1, samples = 0, slow = 0;
  return {
    mobile,
    dpr(width = 1, height = 1) {
      const pixels = mobile ? 900000 : 1500000;
      return Math.min(pixelRatio, mobile ? 1 : 1.25, Math.sqrt(pixels / Math.max(1, width * height))) * scale;
    },
    sample(elapsed, target) {
      // Ignore loading/resume stalls; avoid oscillating quality and allocation churn.
      if (elapsed > 250 || elapsed <= 0) return false;
      samples++; if (elapsed > target * 1.45) slow++;
      if (samples < 150) return false;
      const reduce = slow / samples > .3 && scale > .6;
      samples = slow = 0;
      if (reduce) scale = Math.max(.6, scale * .85);
      return reduce;
    },
    debug: () => ({mobile, scale}),
  };
}
