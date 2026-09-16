/* One bounded canvas for the cabinet's continuous apron display. State messages
 * come from the station, never from guesses about the next reel or payout. */
export const APRON = Object.freeze({ width: 2048, height: 88, frameMs: 50, pixelsPerSecond: 72, holdMs: 14000 });
export const HOUSE_LINES = Object.freeze([
  'THE BACK ROOM', 'EMI IS KEEPING SCORE', 'THREE REELS. ONE VERY SMALL STAGE.',
  'PLEASE MIND THE SPARKLES', 'THE HOUSE HAS EXCELLENT LIGHTING', 'ALL DRESSED UP WITH SOMEWHERE TO GLOW',
]);
const clean = value => String(value ?? '').replace(/[\r\n\t]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 180);

export function createApronTicker({ canvas = null, onDirty = () => {}, reduced = false } = {}) {
  canvas ||= document.createElement('canvas');
  canvas.width = APRON.width; canvas.height = APRON.height;
  const ctx = canvas.getContext('2d');
  let disposed = false, motionOff = !!reduced, lastTime = null, paintedAt = -Infinity;
  let aspect = 23.5;
  let elapsed = 0, age = 0, event = '', dirty = true, paints = 0;
  const ambient = HOUSE_LINES.join('   /   ');
  function paint() {
    const { width: w, height: h } = canvas;
    ctx.fillStyle = '#100a20'; ctx.fillRect(0, 0, w, h);
    // A dim inner rail keeps the text inside the cabinet rather than overlaid on it.
    ctx.fillStyle = '#512646'; ctx.fillRect(0, 3, w, 2); ctx.fillRect(0, h - 5, w, 2);
    ctx.save(); ctx.beginPath(); ctx.rect(20, 12, w - 40, h - 24); ctx.clip();
    const glyphScale = (w / h) / aspect, textWidth = w / glyphScale;
    ctx.scale(glyphScale, 1);
    ctx.font = '600 52px monospace'; ctx.textBaseline = 'middle'; ctx.fillStyle = event ? '#fff0bf' : '#f6a4d6';
    ctx.shadowColor = event ? '#ffaf53' : '#d746bc'; ctx.shadowBlur = 8;
    if (motionOff) {
      const text = event || HOUSE_LINES[0];
      const size = Math.min(52, 52 * (textWidth - 64) / Math.max(1, ctx.measureText(text).width));
      ctx.font = `600 ${size}px monospace`; ctx.textAlign = 'center'; ctx.fillText(text, textWidth / 2, h / 2);
    } else {
      ctx.textAlign = 'left';
      const text = (event ? `${event}   /   ` : '') + ambient + '   /   ';
      const span = ctx.measureText(text).width;
      const offset = Math.max(0, elapsed - 900) * APRON.pixelsPerSecond / 1000;
      const first = 28 - (offset % span);
      for (let x = first; x < textWidth; x += span) ctx.fillText(text, x, h / 2);
    }
    ctx.restore();
    // Fixed scan lines, never a flicker or another animation clock.
    ctx.fillStyle = 'rgba(0,0,0,0.10)';
    for (let y = 0; y < h; y += 4) ctx.fillRect(0, y, w, 1);
    paints++; dirty = false; onDirty();
  }
  return {
    canvas,
    setAspect(value) {
      if (disposed || !Number.isFinite(value) || value <= 0 || Math.abs(value - aspect) < .01) return;
      aspect = value; dirty = true;
    },
    message(value) {
      if (disposed) return;
      const line = clean(value);
      if (line === event) return;
      event = line; age = elapsed = 0; dirty = true;
    },
    update(now, off = motionOff) {
      if (disposed || !Number.isFinite(now)) return false;
      const dt = lastTime === null ? 0 : Math.min(100, Math.max(0, now - lastTime));
      lastTime = now;
      if (!!off !== motionOff) { motionOff = !!off; dirty = true; }
      if (!motionOff) { elapsed += dt; age += dt; }
      if (event && age >= APRON.holdMs) { event = ''; age = elapsed = 0; dirty = true; }
      if (!dirty && (motionOff || now - paintedAt < APRON.frameMs)) return false;
      paint(); paintedAt = now; return true;
    },
    dispose() { disposed = true; },
    debug() { return { message: event, aspect, motionOff, elapsed, paints, disposed, width: canvas.width, height: canvas.height }; },
  };
}
