/* symbols.js - paints one reel cell for a symbol id (CONTRACT.md section 5), ported from the
 * preview's drawSymbol. The canvas is already centred on the cell (-128..128) and rotated and
 * reflected by the caller. `look` carries the dealt media: look.gif(i) -> a drawable (an animated
 * <img> or null) and look.word(i) -> text or null. Missing media gets the preview's fallback art. */

const TILE = ['#ec79b3', '#8160c6', '#c698db', '#53a5b3'];
const WORDS = ['DROP', 'RELAX', 'LET GO', 'SINK'];

export function kindOf(id) {
  const m = /^(gif|sub|spiral)(\d)$/.exec(id || '');
  if (m) return { kind: m[1], n: Number(m[2]) };
  return { kind: id === 'emi' || id === 'melt' ? id : 'unknown', n: 0 };
}

function fallbackTile(ctx, n, t, reduced) {
  const aspect = [1.6, 0.72, 1, 1.35][n % 4], w = 220 * Math.min(1, aspect), h = 220 * Math.min(1, 1 / aspect);
  ctx.save();
  ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, 12); ctx.clip();
  const g = ctx.createLinearGradient(-w / 2, -h / 2, w / 2, h / 2);
  g.addColorStop(0, TILE[n % 4]); g.addColorStop(1, '#342044');
  ctx.fillStyle = g; ctx.fillRect(-w / 2, -h / 2, w, h);
  const tt = reduced ? 0 : t;
  for (let k = 0; k < 5; k++) {
    ctx.strokeStyle = k % 2 ? '#ffcfeb' : '#e6c3ff'; ctx.lineWidth = 4; ctx.beginPath();
    ctx.arc(Math.sin(tt / 1300 + k + n) * w * 0.22, Math.cos(tt / 1700 + k) * h * 0.22, 18 + k * 15, 0, Math.PI * 2);
    ctx.stroke();
  }
  ctx.restore();
}

function drawMedia(ctx, img) {
  const iw = img.naturalWidth || img.width, ih = img.naturalHeight || img.height;
  const s = 220 / Math.max(iw, ih), w = iw * s, h = ih * s;
  ctx.save();
  ctx.beginPath(); ctx.roundRect(-w / 2, -h / 2, w, h, 12); ctx.clip();
  ctx.drawImage(img, -w / 2, -h / 2, w, h);
  ctx.restore();
}

export function drawSymbol(ctx, id, t, look = {}) {
  const { kind, n } = kindOf(id);
  ctx.fillStyle = '#271632'; ctx.fillRect(-128, -128, 256, 256);
  if (kind === 'gif') {
    const img = look.gif && look.gif(n);
    if (img) drawMedia(ctx, img); else fallbackTile(ctx, n, t, look.reduced);
  } else if (kind === 'spiral') {
    ctx.strokeStyle = n % 2 ? '#f49aca' : '#c4a6ef'; ctx.lineWidth = 12; ctx.beginPath();
    for (let k = 0; k < 200; k++) {
      const a = k * 0.12 + n * 2.1 + (look.reduced ? 0 : t / 2400), r = k * 0.48;
      k ? ctx.lineTo(Math.cos(a) * r, Math.sin(a) * r) : ctx.moveTo(Math.cos(a) * r, Math.sin(a) * r);
    }
    ctx.stroke();
  } else if (kind === 'sub') {
    const text = (look.word && look.word(n)) || WORDS[n % 4];
    ctx.fillStyle = '#ffd7eb'; ctx.font = '700 42px Segoe UI, Arial, sans-serif';
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText(String(text).toUpperCase(), 0, 0, 220);
  } else if (kind === 'emi') {
    ctx.fillStyle = '#534467'; ctx.beginPath(); ctx.roundRect(-95, -87, 190, 170, 22); ctx.fill();
    if (look.face) ctx.drawImage(look.face, 3 * 152, 0, 152, 137, -77, -68, 154, 136);
    ctx.fillStyle = '#f094c4'; ctx.beginPath(); ctx.arc(0, -108, 12, 0, 7); ctx.fill();
  } else if (kind === 'melt') {
    ctx.fillStyle = '#b48acb'; ctx.beginPath(); ctx.roundRect(-75, -68, 150, 75, 16); ctx.fill();
    for (let k = 0; k < 4; k++) { ctx.beginPath(); ctx.roundRect(-75 + k * 40, -5, 25, 40 + (k % 2) * 35, 12); ctx.fill(); }
    ctx.fillStyle = '#261330'; ctx.fillRect(-45, -42, 20, 12); ctx.fillRect(25, -42, 20, 12);
  }
}
