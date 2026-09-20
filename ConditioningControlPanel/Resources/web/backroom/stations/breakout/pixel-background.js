/** Pixel wallpaper. Sprite faces are cached; movement never touches gameplay RNG. */
const clamp = n => Math.max(0, Math.min(1, n));
const smooth = n => { n = clamp(n); return n * n * (3 - 2 * n); };
const hash = n => { const v = Math.sin(n * 127.1 + 311.7) * 43758.5453; return v - Math.floor(v); };
export function backgroundMotion(age, phase, reduced = false) {
  const ramp = reduced ? 0 : smooth((age - 5) / 18);
  return { ramp, pulse: ramp * Math.pow(1 - clamp(Number.isFinite(phase) ? phase : 1), 4) };
}
// All faces share a 12-pixel cell. Open silhouettes replace the old flower and door blobs.
const faces = [
  ['000000000000/011111111110/010000000010/011000000110/010100001010/010010010010/010001100010/010000000010/011111111110',
   '000000000000/010000000000/011000000010/011100000110/001110001100/000111111000/000011111110/000001110000/000011000000'],
  ['011111111110/011111111110/010010010010/011111111110/010010010010/011111111110/010010010010/011111111110/010010010010/011111111110',
   '000011111100/000010000100/000010000100/000010000100/000010000100/000010000100/001110011100/011110111100/001100011000'],
  ['000111100000/001000010000/010010001000/010010001000/010011101000/010000001000/001000010000/000111100000',
   '000001000000/010000000100/000011100000/000100010000/101000001010/001000001000/000100010000/000011100000/010000000100/000001000000'],
  ['001111110000/001000011000/001011001000/001000001000/001011101000/001000001000/001011101000/001000001000/001111111000',
   '000000000000/000000000000/000110000000/001001000000/010000100000/100000010000/000000001000/000000000100/000000000010'],
  ['001000010000/011111111100/010000000100/011111111100/010101010100/010000000100/010101010100/011111111100',
   '001100110000/011111111000/011111111000/001111110000/000111100000/000011000000'],
  ['000111100000/001000010000/001000010000/011111111100/010011000100/010011000100/011111111100',
   '000001000000/000011100000/000111110000/001111111000/000011100000/000011100000/000011100000/000000000000/000001000000'],
  ['011111111110/010000000010/010111110010/010111110010/010000000010/011111111110',
   '000000001000/000000011100/000000001000/000001100000/000011000000/000110000000/001100000000/011000000000/010000000000']
];
export function createPixelWallpaper(W, H) {
  const cell = 3, columns = Math.max(5, Math.round(W / 100)), rows = Math.max(5, Math.round(H / 96));
  const atlas = faces.map((pair, kind) => pair.map((bits, colour) => {
    const c = document.createElement('canvas'); c.width = c.height = 36;
    const g = c.getContext('2d', { willReadFrequently: true });
    const grad = g.createLinearGradient(0, 0, 0, 36);
    grad.addColorStop(0, colour ? '#a269d8' : '#909196');
    grad.addColorStop(1, colour ? (kind === 6 ? '#b7a1c5' : '#ff91cf') : '#676a72');
    g.fillStyle = grad;
    bits.split('/').forEach((row, y) => [...row].forEach((v, x) => { if (v === '1') g.fillRect(x * cell, y * cell, cell, cell); }));
    return c;
  }));
  const glyphs = Array.from({ length: columns * rows }, (_, i) => {
    const col = i % columns, row = Math.floor(i / columns);
    const x = (col + .5) * W / columns - 18, y = (row + .5) * H / rows - 18;
    const edge = col < 2 || col >= columns - 2 || row === rows - 1;
    return { x, y, kind: (row * 3 + col) % faces.length, phase: hash(i + 10) * Math.PI * 2,
      speed: .65+hash(i+80)*.7, amplitude: 3+hash(i+90)*3, mixRank:hash(i+100),
      dx: (hash(i + 30) - .5) * 28, dy: (hash(i + 60) - .5) * 22, alpha: edge ? .24 : .10 };
  });
  function symbols(g, mono, age = 0, phase = 1, reduced = false) {
    g.save(); g.imageSmoothingEnabled = false;
    for (const p of glyphs) {
      const grey = typeof mono==='number' ? p.mixRank<mono : mono;
      const t=age*(grey?.22:.65)*p.speed;
      const dy=reduced?0:p.amplitude*(Math.sin(t+p.phase)+.25*Math.sin(t*.61+p.phase*2));
      const x=p.x+(grey?0:p.dx), y=p.y+(grey?0:p.dy)+dy;
      g.globalAlpha = p.alpha * (grey ? .9 : 1.7);
      g.drawImage(atlas[p.kind][grey ? 0 : 1], x, y);
    }
    g.restore();
  }
  function waves(g, age = 0, phase = 1, reduced = false) {
    const { ramp, pulse } = backgroundMotion(age, phase, reduced);
    g.save();
    for (let band = 0; band < 3; band++) {
      g.fillStyle = ['#8b4cbe', '#b15aaf', '#d970aa'][band]; g.globalAlpha = .10 + band * .015;
      for (let x = 0; x < W; x += 6) {
        const y = H * (.70 + band * .035) + Math.sin(x / 125 + band * .52 + age * .10 * ramp) * (17 + ramp * 4 + pulse);
        g.fillRect(x, Math.round(y / 3) * 3, 6, 3);
      }
    }
    g.restore();
  }
  function paint(mono, includeSymbols = true) {
    const c = document.createElement('canvas'); c.width = W; c.height = H;
    const g = c.getContext('2d', { willReadFrequently: true });
    const sky = g.createLinearGradient(0, 0, W * .25, H);
    sky.addColorStop(0, mono ? '#202126' : '#24133e');
    sky.addColorStop(.55, mono ? '#23242a' : '#3c1c51');
    sky.addColorStop(1, mono ? '#25262c' : '#642747');
    g.fillStyle = sky; g.fillRect(0, 0, W, H);
    if (mono) {
      g.fillStyle = '#2e3036';
      for (let x = 0; x < W; x += 24) g.fillRect(x, 0, 1, H);
      for (let y = 0; y < H; y += 24) g.fillRect(0, y, W, 1);
    }
    if (includeSymbols) { if (!mono) waves(g); symbols(g, mono); }
    return c;
  }
  return { paint, symbols, waves, draw(g, age, phase, reduced, mono=false) { if(mono!==true)waves(g, age, phase, reduced); symbols(g, mono, age, phase, reduced); } };
}
