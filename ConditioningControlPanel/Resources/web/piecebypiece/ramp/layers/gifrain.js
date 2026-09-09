/* ============================================================================
 * layers/gifrain.js - gifs falling down the screen, sprite count capped.
 *
 * The DtRH gif cascade (payloadFx.gifCascade), inverted: DtRH ran a 6s window
 * with its own spawn timer, but here the ramp's schedule already fires this
 * kind on a heat-scaled cadence, so ONE fire() is ONE sprite and the density
 * falls out of the meter for free. A capture burst simply calls fire() several
 * times in a row.
 *
 * When the media pool has no gifs the sprites become generated pink noise tiles
 * rather than nothing, so an empty library still rains.
 * ==========================================================================*/

export function createGifRain(ctx) {
  const t = (ctx.tuning && ctx.tuning.gifRain) || { maxLive: 12, fallMsMin: 2400, fallMsMax: 3900 };
  let live = 0;
  let disposed = false;
  const nodes = new Set();

  function fire(opts = {}) {
    if (disposed || !ctx.hasRoot() || live >= t.maxLive) return;
    const heat = Math.min(1, Math.max(0, opts.heat == null ? 0.5 : opts.heat));
    const url = ctx.tile();
    if (!url) return;
    const img = ctx.el('img', 'pbp-rain');
    if (!img) return;
    // hotter rain falls faster and lands bigger; the left lane is free choice
    const fall = Math.round(ctx.rand(t.fallMsMax, t.fallMsMin) - heat * 500);
    img.style.left = ctx.rand(-2, 92).toFixed(1) + 'vw';
    img.style.setProperty('--pbp-fall', Math.max(900, fall) + 'ms');
    img.style.setProperty('--pbp-drift', ctx.rand(-9, 9).toFixed(1) + 'vw');
    img.style.setProperty('--pbp-size', (11 + heat * 10).toFixed(1) + 'vmin');
    img.style.setProperty('--pbp-peak', (0.5 + heat * 0.4).toFixed(2));
    img.style.setProperty('--pbp-spin', ctx.rand(-24, 24).toFixed(1) + 'deg');
    img.decoding = 'async';
    img.src = url;

    live += 1;
    nodes.add(img);
    const kill = () => {
      if (!nodes.delete(img)) return;
      live = Math.max(0, live - 1);
      try { img.remove(); } catch { /* already gone */ }
    };
    img.addEventListener('animationend', kill, { once: true });
    img.addEventListener('error', kill, { once: true });
    setTimeout(kill, Math.max(900, fall) + 800);
    ctx.mount(img);
  }

  function clear() {
    for (const n of [...nodes]) { nodes.delete(n); try { n.remove(); } catch { /* gone */ } }
    live = 0;
  }

  return { fire, clear, dispose() { disposed = true; clear(); }, set() {}, show() {}, grab() {}, move() {}, drop() {} };
}

export default createGifRain;
