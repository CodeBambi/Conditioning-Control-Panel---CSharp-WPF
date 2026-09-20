// A single animated texture. No timers or allocations in the render loop.
export function createCrownDisplay(canvas, dirty) {
  const g = canvas.getContext('2d'), w = canvas.width, h = canvas.height;
  let last = -Infinity, key = '', revision = 0;
  return {
    update(now, { base = '', result = '', still = false, aspect = 4 } = {}) {
      const lines = [base, 'THE BACK ROOM', 'EMI IS KEEPING SCORE', 'MIND THE SPARKLES'];
      const index = still || result ? 0 : Math.floor(now / 4600) % lines.length;
      const text = result || lines[index];
      const nextKey = text + '|' + still + '|' + aspect;
      if (nextKey === key && (still || now - last < 50)) return;
      key = nextKey; last = now; revision++;
      const phase = still || result ? 1 : Math.min(1, (now % 4600) / 520);
      const reveal = 1 - Math.pow(1 - phase, 3);
      g.fillStyle = '#180b25'; g.fillRect(0, 0, w, h);
      g.strokeStyle = '#9f53aa'; g.lineWidth = 2; g.strokeRect(7, 7, w-14, h-14);
      g.save(); g.beginPath(); g.rect(14, 12, w-28, h-24); g.clip();
      // Correct the texture's glyph proportions for the actual stretched screen.
      const sx = (w/h) / Math.max(.5, aspect);
      g.translate(w/2, h/2 + (1-reveal)*h*.65); g.scale(sx, 1);
      g.font = '700 46px Segoe UI, sans-serif'; g.textAlign = 'center'; g.textBaseline = 'middle';
      const fit = Math.min(1, w*.82/(sx*Math.max(1,g.measureText(text).width)));
      g.scale(fit, fit); g.globalAlpha = reveal;
      g.fillStyle = result ? '#ffe09a' : '#ffe4f6'; g.shadowColor = '#ff67d3'; g.shadowBlur = 9;
      g.fillText(text, 0, 0); g.restore();
      if (!still) for (let i=0; i<16; i++) {
        const x = 15 + ((i*97 + now*.027) % (w-30));
        const y = i%2 ? 14 : h-14;
        const r = 1.2 + 1.4*(.5+.5*Math.sin(now*.003+i*2.4));
        g.fillStyle = ['#ff8dda','#ffe292','#8affe0'][i%3];
        g.fillRect(x-r, y-.8, r*2, 1.6); g.fillRect(x-.8, y-r, 1.6, r*2);
      }
      dirty();
    },
    debug: () => ({ revision, key }),
  };
}
