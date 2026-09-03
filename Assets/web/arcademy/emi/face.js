// emi/face.js — EMI's face renderer.
//
// The face is TEXT drawn on a tiny canvas (152 virtual px wide) and upscaled with
// nearest-neighbour (`image-rendering: pixelated` in emi.css), so any font becomes pixel art.
// Ported VERBATIM from the owner-approved prototype (scratchpad/mascot4/emoticon-screen.html)
// and EMI-DESIGN-LOCK.md. The numbers here are LOCKED: 152 px wide, height = w x 0.903,
// fill+stroke #FF69B4, size 95% of the fit box, raise +2% (sideways too), stroke thickness 5,
// kaomoji +10% size / +10% lift, THINKING dots 30% size and -28% lift.
//
// Do not "improve" the fit maths. It uses measureText's actualBoundingBox* metrics (not
// `width`) because combining marks and fallback fonts lie about advance width, and it pads by
// the stroke width because strokeText grows the glyph outside its ink box.

const PINK = '#FF69B4';

// Windows system fallbacks for the exotic kaomoji glyphs Noto Sans Mono (latin subset) lacks.
export const FALLBACK =
  ",'Noto Sans Symbols 2','Noto Sans Kannada','Noto Serif Tibetan','Noto Sans Georgian'," +
  "'Noto Sans JP','Segoe UI Symbol','Segoe UI Emoji','Nirmala UI','MS Gothic',monospace";

// Canon sets, duplicated here (not imported from chains.js) so face.js stays standalone —
// isKao() has to answer for arbitrary caller text, not just chain frames.
const FLAT_SET = ['._.', '^_^', '^_~', '>.<', '@_@', '-_-', 'o_o', 'T_T', '>_<', '=_=', '¬_¬',
  '^___^', 'x_x', '*_*', '0_0', ';_;', '(◉_◉)', '(⊙_⊙)', '(◔_◔)'];
const SIDE_SET = [':)', ':D', ';)', ":'(", '>:(', ':O', ':P', ':|', '<3', 'XD', ':3', '>:)', ':/', 'B)'];
const KAO_SET = ['( ͡° ͜ʖ ͡°)', '(¬‿¬)', '(◠‿◠)', '(⌐■_■)', '(ಠ‿ಠ)', '(✖╭╮✖)', '(✿◡‿◡)', '(◕‿◕)',
  '(ಥ_ಥ)', '(｡♥‿♥｡)', '(≧◡≦)'];
const SPECIAL_SET = ['\\o/', 'GG', '#ERR', 'ZzZ', '!!!', '???', 'LV UP', '6.7', '♥♥♥', '★★★', '404', 'brb'];

// Short classic ASCII faces rotate 90°; everything else stays flat.
const SIDE_RE = /^[>]?[:;=8xXB][-'^]?[)(DPOop|\/\\3]$/;

export function isSide(t) {
  return typeof t === 'string' && SIDE_RE.test(t) && t.length <= 4;
}

// Anything outside the FLAT/SIDE/SPECIAL canon that carries exotic glyphs is a kaomoji:
// +10% size and +10% lift. The promoted round-eye faces are in FLAT_SET, so they count as flat.
export function isKao(t) {
  if (typeof t !== 'string') return false;
  if (KAO_SET.indexOf(t) >= 0) return true;
  return FLAT_SET.indexOf(t) < 0 && SIDE_SET.indexOf(t) < 0 && SPECIAL_SET.indexOf(t) < 0 &&
    /[^\x00-\x7F]/.test(t) && t.replace(/[¬]/g, '').length >= 5;
}

const DEFAULTS = {
  res: 152,               // virtual px across the glass
  font: '"Noto Sans Mono", monospace',
  thick: 5,               // strokeText line width, same pink
  fill: 0.95,             // glyph fills 95% of the box
  lift: 2,                // raise +2% of box height
  liftSide: true          // sideways faces get the raise too
};

/**
 * createFace(canvas, opts) -> { draw, clear, setOpts, isKao, isSide, ready, canvas }
 * opts: {res=152, font='"Noto Sans Mono", monospace', thick=5, fill=0.95, lift=2, liftSide=true}
 */
export function createFace(canvas, opts = {}) {
  const o = Object.assign({}, DEFAULTS, opts);
  const ctx = canvas.getContext('2d');
  let last = null;                 // {text, frameOpts} so a font load can repaint

  function sizeCanvas() {
    const w = Math.max(8, Math.round(o.res));
    const h = Math.round(w * 0.903);   // screen aspect 41.68 : 37.63
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
    return { w, h };
  }

  function fontStr(fs) { return `${fs}px ${o.font}${FALLBACK}`; }

  function paint(text, frameOpts) {
    const { w, h } = sizeCanvas();
    const fo = frameOpts || {};
    const small = !!fo.small;
    const t = String(text == null ? '' : text);
    ctx.clearRect(0, 0, w, h);
    ctx.imageSmoothingEnabled = false;
    if (!t) return;

    const side = !fo.flat && isSide(t);
    const kao = isKao(t);
    const fill = o.fill * (kao ? 1.10 : 1);

    ctx.textBaseline = 'alphabetic';
    ctx.textAlign = 'left';

    // Available box in TEXT space (i.e. pre-rotation): a sideways face is measured against the
    // swapped axes because the canvas is rotated under it.
    const boxW = (side ? h : w) * fill;
    const boxH = (side ? w : h) * fill;

    // Real ink bounds — handles combining marks and glyphs served by a fallback font.
    const meas = (fs) => {
      ctx.font = fontStr(fs);
      const m = ctx.measureText(t);
      const l = m.actualBoundingBoxLeft || 0;
      const r = m.actualBoundingBoxRight || m.width;
      const asc = m.actualBoundingBoxAscent || fs * 0.8;
      const desc = m.actualBoundingBoxDescent || fs * 0.2;
      return { l, r, asc, desc, w: l + r, h: asc + desc };
    };

    let fs = Math.max(6, Math.floor(boxH));
    if (small) fs = Math.max(6, Math.floor(boxH * 0.30));   // THINKING dots: fixed 30%, no fit
    let m = meas(fs);

    const pad = o.thick;                    // the stroke grows the glyph past its ink box
    const fitW = boxW - pad * 2, fitH = boxH - pad * 2;
    const k = small ? 1 : Math.min(fitW / Math.max(1, m.w), fitH / Math.max(1, m.h), 1);
    if (k < 1) { fs = Math.max(4, Math.floor(fs * k)); m = meas(fs); }
    // One guard pass: glyph metrics are not perfectly linear in font size.
    if (!small && (m.w > fitW || m.h > fitH)) {
      fs = Math.max(4, Math.floor(fs * Math.min(fitW / m.w, fitH / m.h)));
      m = meas(fs);
    }

    // Vertical lift as a fraction of box height. Negative = down the glass.
    const liftPct = small
      ? -0.28
      : ((side ? (o.liftSide ? o.lift : 0) : o.lift) + (kao ? 10 : 0)) / 100;

    ctx.save();
    ctx.translate(w / 2, h / 2);
    if (side) ctx.rotate(Math.PI / 2);
    const cx = -m.w / 2 + m.l;              // centre the ACTUAL ink box, not the advance box
    const cy = (m.asc - m.desc) / 2;
    const lift = -liftPct * (side ? w : h);
    ctx.fillStyle = PINK;
    ctx.strokeStyle = PINK;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    if (o.thick > 0) { ctx.lineWidth = o.thick; ctx.strokeText(t, cx, cy + lift); }
    ctx.fillText(t, cx, cy + lift);
    ctx.restore();
  }

  function draw(text, frameOpts) {
    last = { text, frameOpts: frameOpts || {} };
    paint(text, last.frameOpts);
  }

  function clear() {
    last = null;
    const { w, h } = sizeCanvas();
    ctx.clearRect(0, 0, w, h);
  }

  function setOpts(partial) {
    if (!partial) return;
    Object.assign(o, partial);
    if (last) paint(last.text, last.frameOpts);
  }

  // Wait for the bundled face before the first real frame; draw anyway if the API is absent
  // or the load rejects (a fallback monospace face is better than an empty screen).
  let ready = Promise.resolve();
  if (typeof document !== 'undefined' && document.fonts && document.fonts.load) {
    ready = Promise.all([
      document.fonts.load('16px "Noto Sans Mono"').catch(() => null),
      document.fonts.load('bold 16px "Noto Sans Mono"').catch(() => null)
    ]).then(() => { if (last) paint(last.text, last.frameOpts); }).catch(() => {});
  }

  sizeCanvas();
  return { draw, clear, setOpts, isKao, isSide, ready, canvas, get opts() { return Object.assign({}, o); } };
}

export default createFace;
