import { createPixelWallpaper } from './pixel-background.js';
const mixedStep = progress => Math.floor(Math.max(0, Math.min(1, Number.isFinite(progress) ? progress : 0)) * 16);
/** Mostly grey at first; colour replaces it across the three finale acts. */
export const mixedShardCount = progress => Math.round(456 * (1 - mixedStep(progress) / 16));
/** Cached pixel worlds with a short, bounded grey-glass reveal. */
export function createLandscape(W, H, rng, reduced) {
  let grey, greyBase, colour, colourBase, mixedSource, shards = [], age = 2, colourAge = 0, glyphAge = 0;
  let scaled = null, nativeScale = 1;
  const mixedCache = new Map();
  const wallpaper = createPixelWallpaper(W, H);
  function init() { if (!grey) { grey = wallpaper.paint(true); greyBase=wallpaper.paint(true,false); colour = wallpaper.paint(false); colourBase = wallpaper.paint(false, false); } }
  function mixedLandscape(progress) {
    const step = mixedStep(progress);
    if (mixedCache.has(step)) return mixedCache.get(step);
    const c = document.createElement('canvas'); c.width = W; c.height = H;
    const g = c.getContext('2d', { willReadFrequently: true });
    g.drawImage(reduced?colour:colourBase, 0, 0);
    // Small grey shards disappear in stable order, revealing more colour.
    const cols = 20, rows = 12, points = [];
    for (let y = 0; y <= rows; y++) {
      points[y] = [];
      for (let x = 0; x <= cols; x++) points[y][x] = [
        W * (x + (x && x < cols ? Math.sin(x * 2.7 + y * 1.9) * .27 : 0)) / cols,
        H * (y + (y && y < rows ? Math.cos(x * 1.7 + y * 2.3) * .25 : 0)) / rows];
    }
    for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) {
      const a = points[y][x], b = points[y][x + 1], d = points[y + 1][x], e = points[y + 1][x + 1];
      const triangles = (x + y) % 2 ? [[a,b,d],[b,e,d]] : [[a,b,e],[a,e,d]];
      for (let i = 0; i < 2; i++) {
        const rank = ((y * cols + x) * 2 + i) * 137 % (cols * rows * 2);
        if (rank >= mixedShardCount(step / 16)) continue;
        const triangle = triangles[i];
        g.beginPath(); g.moveTo(...triangle[0]); g.lineTo(...triangle[1]); g.lineTo(...triangle[2]); g.closePath();
        g.save(); g.clip(); g.drawImage(reduced?grey:greyBase, 0, 0); g.restore();
        g.strokeStyle = 'rgba(193,175,218,.16)'; g.lineWidth = 1; g.stroke();
      }
    }
    if (mixedCache.size >= 2) mixedCache.delete(mixedCache.keys().next().value);
    mixedCache.set(step, c);
    return c;
  }
  function reset() { age = 2; colourAge = 0; }
  function shatter() {
    init(); age = 0; colourAge = 0;
    if (shards.length) return;
    if (reduced) return;
    const cols = 12, rows = 7, pts = [];
    for (let y = 0; y <= rows; y++) {
      pts[y] = [];
      for (let x = 0; x <= cols; x++) pts[y][x] = [
        x * W / cols + (x && x < cols ? (rng() - .5) * W / cols * .65 : 0),
        y * H / rows + (y && y < rows ? (rng() - .5) * H / rows * .65 : 0)];
    }
    for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) {
      const a = pts[y][x], b = pts[y][x + 1], c = pts[y + 1][x + 1], d = pts[y + 1][x];
      for (const points of [[a,b,c], [a,c,d]]) {
        const cx = points.reduce((n,p) => n+p[0],0)/3, cy = points.reduce((n,p) => n+p[1],0)/3;
        const minX = Math.floor(Math.min(...points.map(p=>p[0]))), minY = Math.floor(Math.min(...points.map(p=>p[1])));
        const maxX = Math.ceil(Math.max(...points.map(p=>p[0]))), maxY = Math.ceil(Math.max(...points.map(p=>p[1])));
        const sprite = document.createElement('canvas'); sprite.width = maxX-minX+2; sprite.height = maxY-minY+2;
        const sg = sprite.getContext('2d', { willReadFrequently: true }); sg.translate(1-minX,1-minY);
        sg.beginPath(); points.forEach((p,i)=>i ? sg.lineTo(...p) : sg.moveTo(...p)); sg.closePath();
        sg.save(); sg.clip(); sg.drawImage(grey,0,0); sg.restore();
        sg.strokeStyle='rgba(220,230,240,.3)'; sg.lineWidth=.65; sg.stroke();
        shards.push({ sprite, points, cx, cy, minX, minY, w:maxX-minX, h:maxY-minY,
          vx:(cx-W/2)*.45, vy:-60-rng()*100, spin:(rng()-.5)*3 });
      }
    }
  }
  function draw(g, dull, dt, mixed = false, beatPhase = 1, endingProgress = 0) {
    init();
    if (!reduced && !shards.length) { shatter(); age = 2; }
    glyphAge+=Math.max(0,Math.min(.1,Number.isFinite(dt)?dt:0));
    colourAge = dull ? 0 : colourAge + Math.max(0, Math.min(.1, Number.isFinite(dt) ? dt : 0));
    const source = dull ? (reduced?grey:greyBase) : mixed ? mixedLandscape(endingProgress) : reduced ? colour : colourBase;
    const index = dull ? 0 : mixed ? 2 : 1;
    if (index === 2 && source !== mixedSource) {
      mixedSource = source;
      if (scaled) scaled.images[2] = null;
    }
    const transform = g.getTransform?.();
    // The static landscape used to resample almost a million pixels each frame.
    // Retain its actual display size; moving cameras still use the original art.
    if (transform && Math.abs(transform.a - nativeScale) < 1e-8 && Math.abs(transform.d - nativeScale) < 1e-8 && !transform.b && !transform.c) {
      const width = Math.ceil(W * transform.a), height = Math.ceil(H * transform.d);
      if (!scaled || scaled.width !== width || scaled.height !== height) {
        const images = [grey, reduced ? colour : colourBase].map(source => {
          const c = document.createElement('canvas'); c.width = width; c.height = height;
          c.getContext('2d', { willReadFrequently: true }).drawImage(source, 0, 0, width, height);
          return c;
        });
        scaled = { width, height, images };
      }
      if (!scaled.images[index]) {
        const c = document.createElement('canvas'); c.width = width; c.height = height;
        c.getContext('2d', { willReadFrequently: true }).drawImage(source, 0, 0, width, height);
        scaled.images[index] = c;
      }
      g.save(); g.setTransform(1, 0, 0, 1, 0, 0);
      g.drawImage(scaled.images[index], Math.floor(transform.e), Math.floor(transform.f)); g.restore();
    } else g.drawImage(source, 0, 0);
    if(!reduced){
      if(mixed&&!dull)wallpaper.symbols(g,mixedShardCount(endingProgress)/480,glyphAge,beatPhase,false);
      else {if(!dull)wallpaper.waves(g,colourAge,beatPhase,false);wallpaper.symbols(g,dull,glyphAge,beatPhase,false);}
    }
    if (dull || age >= 1.1) return;
    age += dt;
    if (reduced) { g.save(); g.globalAlpha = Math.max(0,1-age/.3); g.drawImage(grey,0,0); g.restore(); return; }
    const fade = Math.max(0, 1-age/1.1);
    for (const s of shards) {
      g.save(); g.globalAlpha = fade;
      g.translate(s.cx+s.vx*age, s.cy+s.vy*age+360*age*age); g.rotate(s.spin*age);
      g.drawImage(s.sprite,s.minX-s.cx-1,s.minY-s.cy-1); g.restore();
    }

  }
  return { draw, shatter, reset, resize(value) { if (value !== nativeScale) { nativeScale = value; scaled = null; } } };
}
