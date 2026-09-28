import { PORTAL_FAMILIES, populatePortalBoard } from './portal-layout.js';
import { createPendulums, advancePendulum, curtainPose } from './pendulum.js';
import { tidePose, TIDE_COLS } from './tide.js';
import { REFORM_WORDS, reformLayout } from './reform.js';

const TAU = Math.PI * 2;
const FAMILIES = ['pendulum-orbit', 'tidal-crown', 'tide-hinges', 'word-orbit', 'lace', 'rosettes', 'chimes'];
const PALETTES = [
  ['#f6accf', '#ba99fa', '#8ce0ed'], ['#8ce0d5', '#99bdfa', '#edb7eb'],
  ['#efbd83', '#f6a0b9', '#c5a4f1'], ['#b8afff', '#81d5e8', '#f0b6df'],
];
function seedValue(seed) {
  if (typeof seed === 'number' && Number.isFinite(seed)) return seed >>> 0;
  let n = 2166136261;
  for (const c of String(seed ?? 0)) n = Math.imul(n ^ c.charCodeAt(0), 16777619);
  return n >>> 0;
}
export function seededRandom(seed) {
  let n = seedValue(seed);
  return () => {
    let t = n = (n + 0x6d2b79f5) >>> 0;
    t = Math.imul(t ^ t >>> 15, t | 1);
    t ^= t + Math.imul(t ^ t >>> 7, t | 61);
    return ((t ^ t >>> 14) >>> 0) / 4294967296;
  };
}
function deck(seed, cycle, families=FAMILIES) {
  const rng = seededRandom(`${seed}:deck:${cycle}`), a = [...families];
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(rng() * (i + 1)); [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}
function familyAt(seed, index) {
  if (index % 4 === 3) return 'cascade';
  if (index === 0) return FAMILIES[0];
  if (index >= 10) {
    const seat=index-Math.floor((index+1)/4)-8, cycle=Math.floor(seat/10);
    const extended=n=>n===0?[...PORTAL_FAMILIES,...deck(seed,0)]:deck(seed,`portal:${n}`,[...FAMILIES,...PORTAL_FAMILIES]);
    const a=extended(cycle), previous=cycle?extended(cycle-1).at(-1):deck(seed,0).at(-1);
    if(a[0]===previous)[a[0],a[1]]=[a[1],a[0]];
    return a[seat%a.length];
  }
  const seat = index - Math.floor((index + 1) / 4) - 1;
  const cycle = Math.floor(seat / FAMILIES.length), a = deck(seed, cycle);
  const previous = cycle ? deck(seed, cycle - 1).at(-1) : FAMILIES[0];
  // Only the first two seats change, so the previous deck's last seat is stable.
  if (a[0] === previous) [a[0], a[1]] = [a[1], a[0]];
  return a[seat % a.length];
}

/** Geometry alone owns this random stream. A saved seed and board number replay exactly. */
export function endlessBoard(seed, index, w = 1280, h = 720) {
  if (!Number.isSafeInteger(index) || index < 0) throw new RangeError('Invalid Endless board');
  if (!Number.isFinite(w) || !Number.isFinite(h) || w < 640 || h < 360) throw new RangeError('Invalid Endless size');
  seed = seedValue(seed);
  const rng = seededRandom(`${seed}:board:${index}`), kind = familyAt(seed, index);
  const palette = PALETTES[Math.floor(rng() * PALETTES.length)];
  const unit = Math.min(w / 1280, h / 720), bricks = [];
  const board = { seed, index, kind, name:'', accent:palette[0], mechanics:[], breather:kind === 'cascade', bricks };
  const add = (cx, cy, bw, bh, row, col, extra = {}) => {
    bricks.push({ x:cx - bw / 2, y:cy - bh / 2, w:bw, h:bh, row, col,
      color:palette[row % palette.length], ...extra });
  };
  const arc = (cx, cy, rx, ry, count, row, from = Math.PI, to = TAU, extra = {}) => {
    for (let col = 0; col < count; col++) {
      const a = from + (col + .5) / count * (to - from);
      add(cx + Math.cos(a) * rx, cy + Math.sin(a) * ry, 36 * unit, 21 * unit, row, col,
        { angle:Math.atan2(ry * Math.cos(a), -rx * Math.sin(a)), ...extra });
    }
  };
  const tides = (rows, gap = 0) => {
    board.tide = true;
    for (const row of rows) for (let col = 0; col < TIDE_COLS; col++) {
      if (gap && (col === gap || col === TIDE_COLS - 1 - gap)) continue;
      const pose = tidePose(row, col, 0, w, h);
      add(pose.x + pose.w / 2, pose.y + pose.h / 2, pose.w, pose.h, row, col,
        { angle:pose.angle, endlessTide:true });
    }
  };
  const hinges = (low = false) => {
    const inset = .267 + rng() * .012, phase = .95 + rng() * .15;
    board.pendulums = createPendulums(w, h).slice(0, 2);
    for (const p of board.pendulums) {
      p.pivotX = w * (p.id ? 1 - inset : inset);
      p.pivotY = h * (low ? .215 : .13); p.length = h * (low ? .29 : .40);
      p.phase = p.id ? -phase : phase; p.r = 26 * unit;
      advancePendulum(p, 0, w, h, false);
      add(p.pivotX, p.pivotY, 34 * unit, 34 * unit, p.id, 0,
        { pendulumAnchor:true, pendulumId:p.id, hp:3 });
      for (let guard = 0; guard < 6; guard++) {
        const a = guard * TAU / 6;
        add(p.pivotX + Math.cos(a) * 42 * unit, p.pivotY + Math.sin(a) * 42 * unit,
          24 * unit, 14 * unit, p.id, guard,
          { pendulumGuard:true, pendulumId:p.id, angle:a + Math.PI / 2, strength:2, hp:2 });
      }
      // Short, open curtains keep the release satisfying without another three-wall grind.
      for (let row = 0; row < 4; row++) for (let col = 1; col < 5; col++) {
        const pose = curtainPose(p, row, col);
        add(pose.x + pose.w / 2, pose.y + pose.h / 2, pose.w, pose.h, row, col,
          { angle:pose.angle, pendulumId:p.id, curtain:true, curtainRow:row, curtainCol:col });
      }
    }
  };

  if (PORTAL_FAMILIES.includes(kind)) return populatePortalBoard(board,{w,h,rng,add,hinges});
  if (kind === 'pendulum-orbit') {
    board.name = 'Slingshot garden'; board.mechanics = ['dome', 'pendulums']; board.dome = true;
    hinges();
    for (let row = 0; row < 2; row++) arc(w / 2, h / 2, w * (.19 + row * .037),
      h * (.245 + row * .065), 15 + row * 2, row + 2);
  } else if (kind === 'tidal-crown') {
    board.name = 'Undertow'; board.mechanics = ['dome', 'tide']; board.dome = true;
    tides([0, 1], 3 + Math.floor(rng() * 3));
    for (let side = 0; side < 2; side++) {
      const start = side ? TAU - .66 : Math.PI;
      for (let row = 0; row < 3; row++) arc(w / 2, h / 2,
        w * (.26 + row * .046), h * .25, 7, row + 2, start, start + .66);
    }
  } else if (kind === 'tide-hinges') {
    board.name = 'Tidal chimes'; board.mechanics = ['tide', 'pendulums'];
    tides([0], 2 + Math.floor(rng() * 5)); hinges(true);
    for (let row = 0; row < 2; row++) for (let col = 0; col < 9; col++)
      add(w / 2 + (col - 4) * 40 * unit, h * .34 + row * 27 * unit,
        34 * unit, 20 * unit, row + 3, col, { split:row === 1 && (col === 1 || col === 7) });
  } else if (kind === 'word-orbit') {
    board.name = 'Moving mantra'; board.mechanics = ['reform', 'dome']; board.reform = board.dome = true;
    const count = 64 + Math.floor(rng() * 4) * 4;
    for (const [i, cell] of reformLayout(REFORM_WORDS[0], count, w).entries()) {
      add(cell.x + cell.w / 2, cell.y + cell.h / 2, cell.w, cell.h, i % 3, i,
        { angle:cell.angle, letter:cell.letter, endlessReform:true });
    }
  } else if (kind === 'lace') {
    board.name = 'Loose threads'; board.mechanics = ['spiral-bricks', 'split'];
    const columns = 17, rows = 6, pitch = w * .049, gap = 2 + Math.floor(rng() * 3);
    for (let row = 0; row < rows; row++) for (let col = 0; col < columns; col++) {
      const d = Math.abs(col - 8);
      if ((d + row) % 5 === gap || (row > 3 && d < 2)) continue;
      const payload = row === 2 && d === 5 ? { spiral:'whirl', spin:col < 8 ? 1 : -1 } :
        row === 4 && d === 4 ? { split:true } : {};
      add(w / 2 + (col - 8) * pitch, h * .105 + row * 38 * unit + Math.cos(d * .38) * 18 * unit,
        pitch - 7 * unit, 25 * unit, row, col, payload);
    }
  } else if (kind === 'rosettes') {
    board.name = 'Bloom'; board.mechanics = ['spiral-bricks', 'split'];
    const count = 12 + Math.floor(rng() * 3) * 2, turn = (rng() - .5) * .35;
    const rx = (105 + rng() * 16) * unit, ry = (72 + rng() * 12) * unit;
    for (let flower = 0; flower < 3; flower++) {
      const cx = w * (.22 + flower * .28), cy = h * (.25 + (flower === 1 ? .06 : 0));
      const angle = flower === 2 ? -turn : turn;
      arc(cx, cy, rx, ry, count, flower, angle, TAU + angle);
      const innerStart = bricks.length;
      arc(cx, cy, 62 * unit, 43 * unit, 8, flower + 1, -angle, TAU - angle);
      bricks[innerStart + flower * 2].split = true;
      add(cx, cy, 40 * unit, 26 * unit, flower + 2, 0,
        { spiral:'whirl', hue:flower * 18 - 18, spin:flower % 2 ? -1 : 1 });
    }
  } else if (kind === 'chimes') {
    board.name = 'Hanging lanterns'; board.mechanics = ['pendulums', 'spiral-bricks'];
    hinges();
    const count = 11 + Math.floor(rng() * 3) * 2;
    for (let row = 0; row < 3; row++) arc(w / 2, h * .37, (104 + row * 45) * unit,
      (65 + row * 34) * unit, count, row + 2, Math.PI, TAU);
    add(w / 2, h * .32, 42 * unit, 26 * unit, 1, 0, { spiral:'whirl' });
  } else {
    board.name = 'Exhale'; board.mechanics = ['split'];
    const columns = 10 + Math.floor(rng() * 3) * 2;
    for (let row = 0; row < 4; row++) for (let col = 0; col < columns; col++) {
      const u = (col + .5) / columns;
      add(w * (.15 + .70 * u), h * .12 + row * 39 * unit + Math.sin(u * Math.PI) * 40 * unit,
        w * .70 / columns - 9 * unit, 26 * unit, row, col,
        { split:row === 2 && (col === 2 || col === columns - 3) });
    }
  }
  return board;
}
