// node smoke run for the pure motion functions in motion-pitch.html (no DOM, no canvas)
const fs = require('fs');
const html = fs.readFileSync(__dirname + '/motion-pitch.html', 'utf8');
const a = html.indexOf('/* ----------------------------------------------------------------- tokens */');
const b = html.indexOf('/* ------------------------------------------------------------- renderer */');
if (a < 0 || b < 0) { console.log('markers missing'); process.exit(1); }
const src = html.slice(a, b) + '\nreturn {PROPS, tableRects, ring, codeFor};';
const M = new Function(src)();

function sig(c, rects) {
  // what a viewer would see: drop invisible and off-canvas rects, then anything hidden behind a full-frame cover
  let list = rects.map((r, i) => ({ r, i })).filter(o => (o.r.a === undefined || o.r.a > 0.0005));
  list = list.filter(o => !(o.r.x + o.r.w <= 0 || o.r.x >= 1 || o.r.y + o.r.h <= 0 || o.r.y >= 1));
  list.sort((p, q) => ((p.r.z || 0) - (q.r.z || 0)) || (p.i - q.i));
  // overlap check for equal-z rects is done by the sort below; pinwheel spokes share z and never overlap
  let cut = -1;
  list.forEach((o, k) => { const r = o.r; if (!r.rot && (r.a === undefined || r.a >= 0.9995) && r.x <= 1e-6 && r.y <= 1e-6 && r.x + r.w >= 1 - 1e-6 && r.y + r.h >= 1 - 1e-6 && !r.sc) cut = k; });
  if (cut > 0) list = list.slice(cut);
  const rows = list.map(o => {
    const r = o.r, ph = ((((c.f + (r.ph || 0)) % c.S) + c.S) % c.S);
    const cr = r.crop ? [r.crop.cx, r.crop.cy, r.crop.z] : [0.5, 0.5, 1];
    let rot = (((r.rot || 0) % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI); if (rot > 2 * Math.PI - 1e-6) rot = 0;
    return { z: r.z || 0, s: [r.g, r.x, r.y, r.w, r.h, rot, r.sc || 1, ph, cr[0], cr[1], cr[2]].map(v => +v.toFixed(5)).join(',') };
  });
  // rects that share a z are drawn in index order; treat them as a set so a rotated ring is not a pop
  rows.sort((p, q) => (p.z - q.z) || (p.s < q.s ? -1 : p.s > q.s ? 1 : 0));
  return rows.map(o => o.s);
}

let fails = 0, calls = 0, maxRects = 0;
const seeds = ['pitch', 'a1b2c3', 'zz9'];
for (const p of M.PROPS) {
  for (const seed of seeds) for (const land of [true, false]) for (const F of [45, 75, 120]) for (let n = 1; n <= 8; n++) {
    const sz = land ? { W: 480, H: 270 } : { W: 270, H: 480 };
    const c = { n, F, S: p.S || 15, seed: seed + p.id, land, W: sz.W, H: sz.H, code: M.codeFor(seed) };
    let s0 = null;
    for (let f = 0; f <= F; f++) {
      let rects;
      try { rects = p.rects(c, f); } catch (e) { console.log('THROW', p.id, { n, F, land, seed }, 'f=' + f, e.message); fails++; break; }
      calls++;
      if (!Array.isArray(rects) || !rects.length) { console.log('EMPTY', p.id, { n, F, land }, 'f=' + f); fails++; break; }
      maxRects = Math.max(maxRects, rects.length);
      for (const r of rects) {
        for (const k of ['x', 'y', 'w', 'h']) if (typeof r[k] !== 'number' || !isFinite(r[k])) { console.log('NAN', p.id, { n, F, land }, 'f=' + f, k, r); fails++; }
        if (r.w <= 0 || r.h <= 0) { console.log('NEG', p.id, { n, F, land }, 'f=' + f, r); fails++; }
        if (r.g === undefined || r.g < 0 || r.g >= n) { console.log('BADGIF', p.id, { n, F, land }, 'f=' + f, r.g); fails++; }
      }
      if (f === 0) s0 = sig({ ...c, f: 0 }, rects);
      if (f === F) {
        const sF = sig({ ...c, f: F }, rects);
        if (s0.join('|') !== sF.join('|')) {
          fails++;
          console.log('LOOP POP', p.id, { n, F, land, seed });
          console.log('  f=0 ', s0.slice(0, 6).join(' / '));
          console.log('  f=F ', sF.slice(0, 6).join(' / '));
        }
      }
    }
  }
}
// ring order sanity: each consecutive pair (and the wrap) must be adjacent slots
for (let n = 1; n <= 8; n++) for (const land of [true, false]) {
  const rects = M.tableRects(n).map(r => land ? r : { x: r.y, y: r.x, w: r.h, h: r.w });
  const order = M.ring(rects);
  if (order.length !== n) { console.log('RING SIZE', n, land); fails++; }
  for (let i = 0; i < n && n > 2; i++) {
    const a = order[i], b = order[(i + 1) % n];
    const dx = Math.abs((a.x + a.w / 2) - (b.x + b.w / 2)), dy = Math.abs((a.y + a.h / 2) - (b.y + b.h / 2));
    const touch = (dx <= (a.w + b.w) / 2 + 1e-6) && (dy <= (a.h + b.h) / 2 + 1e-6);
    if (!touch) { console.log('RING JUMP', n, land ? 'land' : 'port', i); fails++; }
  }
}
console.log('calls', calls, 'maxRects', maxRects, 'fails', fails);
process.exit(fails ? 1 : 0);
