// emi/fx.js — the pixel-glyph FX that fly around EMI.
//
// Each glyph is ONE 4-6px div whose `box-shadow` list paints the rest of its pixels: a 7x6
// heart is a single node with ~30 shadows, so a burst of nine sparkles is nine nodes, not 300.
// Ported from the prototype (scratchpad/mascot4/emoticon-screen.html). The keyframes
// (rise/fall/pop) live in emi.css; this file only spawns and removes nodes.
//
// `host` is the `.emi-fx` box: absolutely positioned, inset 0, pointer-events:none. Nothing in
// here may take a click — Arcademy's input-trust law (CLAUDE.md) says EMI never steals one.

const PX = {
  heart: ['.##.##.', '#######', '#######', '.#####.', '..###..', '...#...'],
  spark: ['..#..', '..#..', '#####', '..#..', '..#..'],
  tear:  ['.#.', '###', '###', '.#.'],
  bolt:  ['..##', '.##.', '####', '..#.', '.#..'],
  hash:  ['.#.#.', '#####', '.#.#.', '#####', '.#.#.'],
  bang:  ['#', '#', '#', '#', '.', '#']
};

const PINK = '#FF69B4', GOLD = '#FFD700', CREAM = '#F5F0E1';
const R = (a, b) => a + Math.random() * (b - a);

function reduced() {
  try {
    return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (_) { return false; }
}

function glyph(name, color, s) {
  const rows = PX[name] || PX.spark;
  const sh = [];
  for (let y = 0; y < rows.length; y++) {
    const row = rows[y];
    for (let x = 0; x < row.length; x++) {
      if (row[x] === '#') sh.push(`${x * s}px ${y * s}px 0 0 ${color}`);
    }
  }
  const el = document.createElement('div');
  el.className = 'emi-px';
  el.style.width = el.style.height = s + 'px';
  el.style.boxShadow = sh.join(',');
  return el;
}

function spawn(host, name, color, x, y, anim, dx, dy, t, s, still) {
  const el = glyph(name, color, s);
  el.style.left = x + '%';
  el.style.top = y + '%';
  el.style.setProperty('--dx', dx + 'px');
  el.style.setProperty('--dy', dy + 'px');
  el.style.setProperty('--t', t + 'ms');
  if (!still) el.classList.add(anim);
  host.appendChild(el);
  setTimeout(() => { if (el.parentNode) el.remove(); }, (still ? 420 : t) + 50);
}

// Each recipe: (host, still) — `still` means reduced motion, so paint one static frame and let
// the removal timer take it away (the frames still CHANGE for reduced motion, they just don't fly).
const KINDS = {
  hearts(host, still) {
    for (let i = 0; i < 7; i++) {
      const d = still ? 0 : i * 70;
      setTimeout(() => spawn(host, Math.random() < 0.7 ? 'heart' : 'spark',
        Math.random() < 0.6 ? PINK : GOLD, R(18, 78), R(18, 55), 'rise',
        R(-30, 30), R(-70, -120), R(900, 1400), Math.random() < 0.5 ? 3 : 4, still), d);
    }
  },
  sparks(host, still) {
    for (let i = 0; i < 9; i++) {
      const d = still ? 0 : i * 60;
      setTimeout(() => spawn(host, 'spark', GOLD, R(10, 85), R(10, 60), 'pop',
        0, 0, R(500, 800), Math.random() < 0.5 ? 3 : 4, still), d);
    }
  },
  tears(host, still) {
    for (let i = 0; i < 4; i++) {
      const d = still ? 0 : i * 260;
      setTimeout(() => spawn(host, 'tear', CREAM, R(40, 62), R(62, 68), 'fall',
        R(-6, 6), R(70, 110), R(900, 1300), 3, still), d);
    }
  },
  storm(host, still) {
    for (let i = 0; i < 6; i++) {
      const d = still ? 0 : i * 90;
      setTimeout(() => spawn(host, Math.random() < 0.5 ? 'bolt' : 'hash',
        Math.random() < 0.5 ? GOLD : PINK, R(5, 85), R(5, 40), 'pop',
        0, 0, R(400, 650), Math.random() < 0.5 ? 4 : 5, still), d);
    }
  },
  bang(host, still) {
    spawn(host, 'bang', GOLD, 50, 6, 'pop', 0, 0, 700, 6, still);
    spawn(host, 'bang', PINK, 34, 10, 'pop', 0, 0, 600, 4, still);
    spawn(host, 'bang', PINK, 64, 10, 'pop', 0, 0, 600, 4, still);
  },
  none() {}
};

export const FX_KINDS = ['hearts', 'sparks', 'tears', 'storm', 'bang'];

/** showFx(host, kind) — kind: 'hearts'|'sparks'|'tears'|'storm'|'bang'. Nodes remove themselves. */
export function showFx(host, kind) {
  if (!host || !host.appendChild) return;
  (KINDS[kind] || KINDS.none)(host, reduced());
}

/** Which body move the prototype paired with each fx kind (chains.js carries the same table). */
export const BODY_FOR_FX = { hearts: 'bounce', sparks: 'bounce', tears: 'droop', storm: 'shiver', bang: 'bounce' };

export default showFx;
