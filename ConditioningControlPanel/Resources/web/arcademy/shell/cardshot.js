/* ============================================================================
 * shell/cardshot.js - the report card, drawn to a canvas and handed out as a
 * PNG. The DRAWING HALF of the one share pipeline (trap 13); reportcard.js is
 * still the only thing that decides WHAT goes on the paper and is still the
 * only caller. Nothing here imports the lexicon, the bridge, a store or EMI:
 * it is handed a finished plain model and it paints it.
 *
 * WHY THE STRINGS ON THE IMAGE ARE LITERALS. The same reason the share header
 * is the literal 'The Arcademy' and never t('arcademy') (trap 13): a paste in
 * Discord must not out the player's mod. So the copy BAKED INTO the picture is
 * neutral English and the mod-skinnable half - the button, the toasts - stays
 * in the DOM where the lexicon serves it. The palette is the house palette by
 * literal for the same reason, not init.palette: two players who both went
 * perfect should be able to post the same card.
 *
 * NO SERIF, anywhere in the stack (house law). The CSS --disp chain ends in
 * `serif` as a last resort; the canvas one deliberately does not.
 * ==========================================================================*/

/** House palette, verbatim from styles.css :root. Never init.palette. */
const C = Object.freeze({
  ground: '#14142B', navy: '#1A1A2E', line: '#3A3A5E',
  ink: '#F2EBDD', dim: '#B9B3CE', faint: '#8A84A8',
  pink: '#FF69B4', lav: '#B8A6E8', gold: '#F0C24B', slate: '#7A7594',
});
const GRADE_INK = Object.freeze({
  s: C.gold, splus: C.gold, a: C.pink, b: C.lav, c: C.slate, pass: C.lav,
});
const F_DISP = "'Arcademy Display','Arial Black',Impact,sans-serif";
const F_BODY = "'Segoe UI Variable Text','Segoe UI',system-ui,-apple-system,sans-serif";
const F_MONO = "'Cascadia Mono','Consolas','Courier New',monospace";

const W = 640;          // logical width; the bitmap is this times SCALE
const SCALE = 2;        // 1280px wide reads sharp in a Discord embed
const PAD = 28;

function gradeInk(grade) {
  const k = String(grade || '').toLowerCase().replace('+', 'plus');
  return GRADE_INK[k] || C.faint;
}

function box(ctx, x, y, w, h, r) {
  ctx.beginPath();
  if (typeof ctx.roundRect === 'function') ctx.roundRect(x, y, w, h, r);
  else ctx.rect(x, y, w, h);
}

/**
 * Draw the card.
 *
 * @param {Object} m  the model reportcard.js builds:
 *   {header, title, date, rows:[{name, grade, xp}], streak, perfectDays,
 *    perfect, tickets, token, totalXp, tier, grid:[string]}
 * @returns {HTMLCanvasElement|null}
 */
export function drawCard(m) {
  if (typeof document === 'undefined' || !document.createElement) return null;
  const model = m || {};
  const rows = Array.isArray(model.rows) ? model.rows.slice(0, 8) : [];
  const grid = Array.isArray(model.grid) ? model.grid.slice(0, 12) : [];

  /* The height is the layout, added up in advance: 120 for the header block,
   * one ROW_H per class, 56 for the XP + attendance lines, then whatever the
   * night actually earned, then 46 for the wordmark's air. Every one of these
   * is the same number the draw below walks past, so a block that grows has to
   * grow in both places or the card gets a hole in it. */
  const ROW_H = 40;
  const gridH = grid.length ? 22 + grid.length * 22 : 0;
  const stampH = model.perfect ? 54 : 0;
  const tillH = (model.tickets || model.token) ? 32 : 0;
  const H = 120 + rows.length * ROW_H + 56 + tillH + gridH + stampH + 46;

  const cv = document.createElement('canvas');
  cv.width = W * SCALE; cv.height = H * SCALE;
  const ctx = cv.getContext && cv.getContext('2d');
  if (!ctx) return null;
  ctx.scale(SCALE, SCALE);
  ctx.textBaseline = 'middle';

  /* the ground, then the paper laid on it */
  ctx.fillStyle = C.ground; ctx.fillRect(0, 0, W, H);
  ctx.fillStyle = C.navy;
  box(ctx, 12, 12, W - 24, H - 24, 16); ctx.fill();
  ctx.strokeStyle = C.line; ctx.lineWidth = 1;
  box(ctx, 12.5, 12.5, W - 25, H - 25, 16); ctx.stroke();

  const mid = W / 2;
  let y = 48;

  /* THE HEADER, mod-anonymous by ruling. Centre line, like every other room. */
  ctx.textAlign = 'center';
  ctx.fillStyle = C.ink;
  ctx.font = '24px ' + F_DISP;
  ctx.fillText(String(model.header || 'The Arcademy'), mid, y);
  y += 26;
  ctx.fillStyle = C.faint;
  ctx.font = '12px ' + F_BODY;
  ctx.fillText([model.title, model.date].filter(Boolean).join('   -   '), mid, y);
  y += 24;

  ctx.strokeStyle = C.line;
  ctx.beginPath(); ctx.moveTo(PAD + 12, y); ctx.lineTo(W - PAD - 12, y); ctx.stroke();
  y += 22;

  /* one row per class: badge, neutral class name, the XP the HOST paid */
  const left = PAD + 12;
  const right = W - PAD - 12;
  for (const r of rows) {
    const g = String(r.grade || '').toUpperCase();
    const ink = gradeInk(r.grade);
    if (g) { ctx.fillStyle = ink; box(ctx, left, y - 13, 38, 26, 8); ctx.fill(); }
    else { ctx.strokeStyle = C.line; box(ctx, left + 0.5, y - 12.5, 37, 25, 8); ctx.stroke(); }
    ctx.textAlign = 'center';
    ctx.fillStyle = g ? C.navy : C.faint;
    ctx.font = 'bold 13px ' + F_BODY;
    ctx.fillText(g || '--', left + 19, y + 1);

    ctx.textAlign = 'left';
    ctx.fillStyle = g ? C.ink : C.faint;
    ctx.font = '14px ' + F_BODY;
    ctx.fillText(String(r.name || ''), left + 52, y);

    if (r.xp != null) {
      ctx.textAlign = 'right';
      ctx.fillStyle = C.dim;
      ctx.font = '13px ' + F_MONO;
      ctx.fillText('+' + Math.round(r.xp) + ' XP', right, y);
    }
    y += ROW_H;
  }

  /* the day's totals, centred and loud - the number people screenshot */
  y += 6;
  ctx.textAlign = 'center';
  ctx.fillStyle = C.pink;
  ctx.font = '20px ' + F_DISP;
  ctx.fillText('+' + Math.round(model.totalXp || 0) + ' XP', mid, y);
  y += 24;
  ctx.fillStyle = C.dim;
  ctx.font = '13px ' + F_BODY;
  const att = ['Attendance ' + (model.streak | 0)];
  if (model.perfectDays) att.push('Perfect x' + (model.perfectDays | 0));
  if (model.tier) att.push(String(model.tier));
  ctx.fillText(att.join('   |   '), mid, y);
  y += 26;

  if (model.tickets || model.token) {
    ctx.fillStyle = C.gold;
    ctx.font = '13px ' + F_BODY;
    const till = [];
    if (model.tickets) till.push((model.tickets | 0) + ' tickets');
    if (model.token) till.push('1 token');
    ctx.fillText(till.join('   +   '), mid, y);
    y += 32;
  }

  if (grid.length) {
    y += 10;
    ctx.fillStyle = C.ink;
    ctx.font = '17px ' + F_MONO;
    for (const line of grid) { ctx.fillText(String(line), mid, y); y += 22; }
    y += 12;
  }

  /* the seal. A tilted box, the way the ceremony stamps the paper. */
  if (model.perfect) {
    ctx.save();
    ctx.translate(mid, y + 14);
    ctx.rotate(-0.05);
    ctx.strokeStyle = C.pink; ctx.lineWidth = 2;
    box(ctx, -112, -18, 224, 36, 8); ctx.stroke();
    ctx.fillStyle = C.pink;
    ctx.textAlign = 'center';
    ctx.font = '15px ' + F_DISP;
    ctx.fillText('PERFECT ATTENDANCE', 0, 1);
    ctx.restore();
    y += stampH;
  }

  /* the wordmark, so a card posted with no caption still says where it came from */
  ctx.fillStyle = C.lav;
  ctx.font = '11px ' + F_DISP;
  ctx.textAlign = 'center';
  ctx.globalAlpha = 0.85;
  ctx.fillText('A R C A D E M Y', mid, H - 30);
  ctx.globalAlpha = 1;
  return cv;
}

/** Canvas -> Blob, never throwing. */
function toBlob(cv) {
  return new Promise((resolve) => {
    try {
      if (typeof cv.toBlob === 'function') { cv.toBlob((b) => resolve(b || null), 'image/png'); return; }
    } catch (e) { /* fall through */ }
    resolve(null);
  });
}

/** Is there a native host under us? A download there may go nowhere. */
export function hosted() {
  if (typeof window === 'undefined') return false;
  const w = window;
  return !!((w.chrome && w.chrome.webview) || w.ReactNativeWebView);
}

/**
 * The last rung: paint the PNG into the page so a phone can long-press it.
 * An overlay, never a screen (traps 48/50) - it owns no Esc rung and no state,
 * and a press anywhere takes it away again.
 */
function showSheet(url, closeLabel) {
  const doc = document;
  const wrap = doc.createElement('div');
  wrap.className = 'arc-shotsheet';
  const img = doc.createElement('img');
  img.src = url;
  img.alt = '';
  wrap.appendChild(img);
  const shut = doc.createElement('button');
  shut.type = 'button';
  shut.className = 'btn';
  shut.textContent = String(closeLabel || 'Close');
  wrap.appendChild(shut);
  const close = () => { try { wrap.remove(); } catch (e) { /* noop */ } };
  wrap.addEventListener('click', close);
  doc.body.appendChild(wrap);
}

/**
 * Hand the drawn card out, by whichever door this host actually has.
 *
 * WEB: download it AND try the clipboard, so the file is on disk and the paste
 * into Discord works too. HOSTED (WebView2 / react-native-webview): the
 * clipboard first, because a download in a fullscreen kiosk either opens a
 * flyout over the beat or is dropped on the floor; the download is the second
 * rung and the long-press sheet is the last. There is no host message for
 * saving a file, so the page never asks for one.
 *
 * @returns {Promise<'download'|'clipboard'|'both'|'view'|null>}
 */
export async function handOut(cv, opts) {
  const o = opts || {};
  if (!cv) return null;
  const blob = await toBlob(cv);
  const name = String(o.filename || 'arcademy-report.png');
  let clip = false;
  let down = false;

  const tryClip = async () => {
    try {
      if (!blob || typeof ClipboardItem !== 'function') return false;
      if (!navigator.clipboard || typeof navigator.clipboard.write !== 'function') return false;
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
      return true;
    } catch (e) { return false; }
  };
  const tryDown = () => {
    try {
      if (!blob || typeof URL === 'undefined' || !URL.createObjectURL) return false;
      const a = document.createElement('a');
      if (!('download' in a)) return false;
      const url = URL.createObjectURL(blob);
      a.href = url; a.download = name;
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => { try { URL.revokeObjectURL(url); } catch (e) { /* noop */ } }, 8000);
      return true;
    } catch (e) { return false; }
  };

  if (hosted()) {
    clip = await tryClip();
    if (!clip) down = tryDown();
  } else {
    down = tryDown();
    clip = await tryClip();
  }
  if (down && clip) return 'both';
  if (down) return 'download';
  if (clip) return 'clipboard';

  try {
    showSheet(cv.toDataURL('image/png'), o.closeLabel);
    return 'view';
  } catch (e) { return null; }
}

export default { drawCard, handOut, hosted };
