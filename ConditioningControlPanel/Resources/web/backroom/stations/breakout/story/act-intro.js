/* ============================================================================
 * story/act-intro.js - the act title card.
 *
 * The level-intro look (level-intro.js), with the act's own colour and title
 * and no per-level choreography. One small cached raster per act: the animation
 * never rasterizes text again. level-intro.js is NOT edited: the house game's
 * card is its own thing and stays exactly as it is.
 * ==========================================================================*/
export const ACT_INTRO_S = 1.6;      // how long the card is up, in seconds

export function createActIntro() {
  const tiles = new Map();
  function tile(key, actNo, title, colour) {
    if (tiles.has(key)) return tiles.get(key);
    const c = document.createElement('canvas'); c.width = 220; c.height = 34;
    const x = c.getContext('2d', { willReadFrequently: true });
    x.textAlign = 'center'; x.textBaseline = 'middle';
    x.font = 'bold 8px monospace'; x.fillStyle = colour;
    x.fillText('ACT ' + actNo, 110, 6);
    x.font = 'bold 17px monospace'; x.fillStyle = '#171321';
    x.fillText(title, 111, 24);
    x.fillStyle = colour; x.fillText(title, 110, 23);
    tiles.set(key, c);
    return c;
  }
  return {
    /** `card` = { actNo, title, colour }. `age` is seconds since the card was raised. */
    draw(g, card, age, w, h, reduced = false) {
      if (!card || age < 0 || age >= ACT_INTRO_S) return;
      const colour = card.colour || '#eeeeee';
      const enter = Math.min(1, age / 0.28), exit = Math.min(1, (ACT_INTRO_S - age) / 0.3);
      const ease = 1 - Math.pow(1 - enter, 3), t = 1 - ease;
      g.save(); g.imageSmoothingEnabled = false; g.globalAlpha = Math.min(enter, exit);
      g.translate(w / 2, h * 0.6);
      // One move, the same for every act: the card settles down onto its line. Still in reduced motion.
      if (!reduced) g.translate(0, Math.round(t * 9) * 4);
      g.fillStyle = 'rgba(12,10,22,.86)'; g.fillRect(-332, -58, 664, 116);
      g.fillStyle = colour;
      g.fillRect(-332, -58, 44, 4); g.fillRect(288, 54, 44, 4);
      g.drawImage(tile(card.actNo + '|' + card.title + '|' + colour, card.actNo, card.title, colour),
        -310, -51, 620, 102);
      g.restore();
    },
  };
}
