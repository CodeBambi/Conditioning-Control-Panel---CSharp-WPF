// One small cached raster per title. Animation never rasterizes text again.
export const LEVEL_TITLES = ['FIRST LIGHT', 'THE TIDE', 'THE SPELL', 'THE DOME', 'THE RHYTHM', 'THE IRIS', 'THE PENDULUM', 'ENOUGH'];
const COLOURS = ['#f2c888','#8ee4ed','#ecabff','#bca2ff','#ffabd1','#b4f0cc','#ffda9c','#eeeeee'];
export function createLevelIntro() {
  const tiles = new Map();
  return { draw(g, level, age, w, h, reduced = false) {
    if (age < 0 || age >= 1.15) return;
    const i = Math.min(7, Math.max(0, level));
    if (!tiles.has(i)) {
      const c = document.createElement('canvas'); c.width = 200; c.height = 34;
      const x = c.getContext('2d', { willReadFrequently: true });
      x.textAlign = 'center'; x.textBaseline = 'middle';
      x.font = 'bold 8px monospace'; x.fillStyle = COLOURS[i];
      x.fillText(`LEVEL ${i + 1}`, 100, 6);
      x.font = 'bold 17px monospace'; x.fillStyle = '#171321';
      x.fillText(LEVEL_TITLES[i], 101, 24);
      x.fillStyle = COLOURS[i]; x.fillText(LEVEL_TITLES[i], 100, 23);
      tiles.set(i, c);
    }
    const enter = Math.min(1, age / .22), exit = Math.min(1, (1.15 - age) / .2);
    const ease = 1 - Math.pow(1 - enter, 3), t = 1 - ease;
    g.save(); g.imageSmoothingEnabled = false; g.globalAlpha = Math.min(enter, exit);
    g.translate(w / 2, h * .60);
    if (!reduced) {
      if (i === 0) g.translate(0, Math.round(t * 12) * 4);
      if (i === 1) g.translate(Math.round(-t * 38) * 4, Math.sin(age * 8) * 6 * t);
      if (i === 2) g.scale(1 + t * .3, 1 - t * .25);
      if (i === 3) g.scale(1 - t * .65, 1 - t * .65);
      if (i === 4) g.translate(0, -Math.abs(Math.sin(age * 16)) * 22 * (1 - age / 1.15));
      if (i === 5) g.rotate(-t * .35);
      if (i === 6) { g.translate(0, -90); g.rotate(Math.sin(age * 9) * .22 * (1 - age / 1.15)); g.translate(0, 90); }
      if (i === 7) g.translate(Math.round(Math.sin(age * 61) * t * 3) * 4, 0);
    }
    g.fillStyle = 'rgba(12,10,22,.82)'; g.fillRect(-322, -58, 644, 116);
    g.fillStyle = COLOURS[i];
    g.fillRect(-322, -58, 40, 4); g.fillRect(282, 54, 40, 4);
    const tile = tiles.get(i);
    if (!reduced && i === 2) {
      const reveal = Math.ceil(ease * 20) * 10;
      g.drawImage(tile, 0, 0, reveal, 34, -300, -51, reveal * 3, 102);
    } else g.drawImage(tile, -300, -51, 600, 102);
    g.restore();
  }};
}
