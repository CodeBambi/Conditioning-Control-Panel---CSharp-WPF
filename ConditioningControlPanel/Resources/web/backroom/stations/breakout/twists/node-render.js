/* ============================================================================
 * stations/breakout/twists/node-render.js - the twist's own drawing.
 *
 * Twist: Node (The Hive). The rule has to read without a word of text, so:
 *   POWERED  dark plate, a lit border leaking into the gaps, a live cable, a
 *            white pulse crawling outward from the core.
 *   CUT      pale, washed out, the cable a flat dead trace with a break in it.
 *   CORE     the brightest thing on the board, breathing, ringed, and it blooms
 *            over everything.
 *
 * `under` lays the cable and the glow (the gaps between the plates are what a
 * glow can leak into). `brick` redraws the stub on top of its own face so the
 * cable is continuous across the wall. `over` is the core's bloom alone.
 * Reduced motion: no pulse, no breathing. The glow stays, because it IS the state.
 *
 * The node lane owns this file. See twists/CONTRACT.md.
 * ==========================================================================*/

const COLS = 16;
/** The Hive's green, if the door ever forgets to say. */
const FALLBACK = '#2FCB72';
const DEAD = '#332f42';
/** Drained, and DIMMER than a plain brick: glow means armoured, dull means easy. */
const PALE = 'rgba(92,88,112,.58)';
/** One cell per fifth of a second, and a gap of four cells between pulses. */
const PULSE_SPEED = 5, PULSE_CYCLE = 4;

const hex = c => [parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)];
const rgba = (c, a) => { const t = hex(c); return 'rgba(' + t[0] + ',' + t[1] + ',' + t[2] + ',' + a + ')'; };
/** `a` toward `b`, both hex, as a css colour. */
function mix(a, b, k) {
  const p = hex(a), q = hex(b);
  return 'rgb(' + Math.round(p[0] + (q[0] - p[0]) * k) + ',' + Math.round(p[1] + (q[1] - p[1]) * k) + ',' + Math.round(p[2] + (q[2] - p[2]) * k) + ')';
}
const colourOf = snap => (snap && snap.doorColour) || FALLBACK;
const alive = br => !!br && br.alive === true;
const isWire = br => alive(br) && !!br.wire;
const isCore = br => alive(br) && !!br.core;
const isNet = br => isWire(br) || isCore(br);
/** A core is always lit. A wire is lit only while the current reaches it. */
const lit = br => isCore(br) || (isWire(br) && !!br.powered);
const cx = br => br.x + br.w / 2;
const cy = br => br.y + br.h / 2;
const depthOf = br => (isCore(br) ? 0 : Math.max(0, br.netDepth | 0));

/** Living net bricks by cell, so a neighbour lookup is one Map read a frame. */
function netGrid(snap) {
  const grid = new Map();
  const list = snap && Array.isArray(snap.bricks) ? snap.bricks : [];
  for (const br of list) if (isNet(br)) grid.set(br.row * COLS + br.col, br);
  return grid;
}
const near = (grid, br, dr, dc) => grid.get((br.row + dr) * COLS + (br.col + dc)) || null;

/**
 * Where the pulse sits on the segment that runs from `low` out to `high`, as a
 * fraction 0..1, or -1 when this stretch of cable is dark between pulses.
 */
export function pulseAt(low, t) {
  const phase = ((t * PULSE_SPEED - depthOf(low)) % PULSE_CYCLE + PULSE_CYCLE) % PULSE_CYCLE;
  return phase < 1 ? phase : -1;
}

/** One length of cable between two brick centres, live or dead. */
function cable(c2d, x1, y1, x2, y2, live, tint) {
  c2d.lineCap = 'round';
  if (live) {
    c2d.strokeStyle = rgba(tint, 0.34); c2d.lineWidth = 7;
    c2d.beginPath(); c2d.moveTo(x1, y1); c2d.lineTo(x2, y2); c2d.stroke();
    c2d.strokeStyle = mix(tint, '#ffffff', 0.72); c2d.lineWidth = 2.2;
  } else {
    c2d.strokeStyle = DEAD; c2d.lineWidth = 2;
  }
  c2d.beginPath(); c2d.moveTo(x1, y1); c2d.lineTo(x2, y2); c2d.stroke();
}

/* ----------------------------------------------------------------- under */
/** The glow behind the lit plates, then the whole cable run, then the nodes. */
export function under(c2d, snap, t) {
  const grid = netGrid(snap);
  if (!grid.size) return;
  const tint = colourOf(snap), reduced = !!snap.reduced;
  const breath = reduced ? 0.5 : 0.5 + 0.5 * Math.sin(t * 3.4);
  // The lit border: a rectangle a few pixels proud of the plate, so light leaks into the gaps.
  for (const br of grid.values()) {
    if (!lit(br)) continue;
    if (isCore(br)) {
      c2d.fillStyle = rgba(tint, 0.18 + 0.12 * breath); c2d.fillRect(br.x - 11, br.y - 11, br.w + 22, br.h + 22);
      c2d.fillStyle = rgba(tint, 0.34); c2d.fillRect(br.x - 5, br.y - 5, br.w + 10, br.h + 10);
    } else {
      c2d.fillStyle = rgba(tint, 0.15 + 0.07 * breath); c2d.fillRect(br.x - 4, br.y - 4, br.w + 8, br.h + 8);
    }
  }
  // Down and right only, so every pair of neighbours is one length of cable.
  for (const br of grid.values()) {
    for (const step of [[1, 0], [0, 1]]) {
      const nb = near(grid, br, step[0], step[1]);
      if (!nb) continue;
      cable(c2d, cx(br), cy(br), cx(nb), cy(nb), lit(br) && lit(nb), tint);
    }
  }
  // A node on every wire junction. The core has no node: it is the node.
  for (const br of grid.values()) {
    if (isCore(br)) continue;
    c2d.fillStyle = lit(br) ? '#ffffff' : DEAD;
    c2d.beginPath(); c2d.arc(cx(br), cy(br), 2, 0, 7); c2d.fill();
  }
}

/* ----------------------------------------------------------------- brick */
/** The plate itself: tinted dark while lit, washed pale once cut, cable on top. */
export function brick(c2d, br, snap, t) {
  if (!br || (!br.wire && !br.core)) return;
  const grid = netGrid(snap);
  const tint = colourOf(snap), reduced = !!snap.reduced;
  const breath = reduced ? 0.5 : 0.5 + 0.5 * Math.sin(t * 3.4);
  const hw = br.w / 2, hh = br.h / 2, on = lit(br);

  if (isCore(br)) {
    c2d.fillStyle = rgba(tint, 0.34 + 0.22 * breath); c2d.fillRect(-hw, -hh, br.w, br.h);
    c2d.fillStyle = 'rgba(255,255,255,' + (0.3 + 0.22 * breath) + ')'; c2d.fillRect(-hw, -hh, br.w, br.h);
  } else if (on) {
    // Armoured: the plate goes dark and takes the current's colour at its edge.
    c2d.fillStyle = 'rgba(10,9,20,.42)'; c2d.fillRect(-hw, -hh, br.w, br.h);
    c2d.strokeStyle = rgba(tint, 0.5 + 0.2 * breath); c2d.lineWidth = 1.4;
    c2d.strokeRect(-hw + 1, -hh + 1, br.w - 2, br.h - 2);
  } else {
    // Cut: the colour drains out of it. This is the brick that takes one hit.
    c2d.fillStyle = PALE; c2d.fillRect(-hw, -hh, br.w, br.h);
  }

  // The cable, redrawn over this plate so the run is continuous across the wall.
  for (const step of [[-1, 0], [1, 0], [0, -1], [0, 1]]) {
    const nb = near(grid, br, step[0], step[1]);
    if (!nb) continue;
    const dx = cx(nb) - cx(br), dy = cy(nb) - cy(br);
    const live = on && lit(nb);
    cable(c2d, 0, 0, dx / 2, dy / 2, live, tint);
    if (!live) continue;
    // The pulse crawls from the shallower brick outward. Each end draws its own half.
    const low = depthOf(br) <= depthOf(nb) ? br : nb;
    const f = reduced ? -1 : pulseAt(low, t);
    if (f < 0) continue;
    const mine = low === br ? f : 1 - f;
    if (mine > 0.5) continue;
    c2d.fillStyle = '#ffffff';
    c2d.beginPath(); c2d.arc(dx * mine, dy * mine, 2.6, 0, 7); c2d.fill();
  }

  if (isCore(br)) {
    c2d.strokeStyle = 'rgba(0,0,0,.55)'; c2d.lineWidth = 1.6;
    c2d.beginPath(); c2d.arc(0, -1, 4 + breath * 2, 0, 7); c2d.stroke();
  } else if (!on) {
    // A dead trace wears its break: two stubs and a gap where the current stopped.
    c2d.fillStyle = 'rgba(30,26,42,.6)';
    c2d.fillRect(-2.5, -2.5, 5, 5);
  } else {
    c2d.fillStyle = '#ffffff';
    c2d.beginPath(); c2d.arc(0, 0, 2, 0, 7); c2d.fill();
  }
}

/* ------------------------------------------------------------------ over */
/** The core's bloom, over everything: on this board it is the brightest thing there is. */
export function over(c2d, snap, t) {
  const list = snap && Array.isArray(snap.bricks) ? snap.bricks : [];
  const reduced = !!snap.reduced;
  const tint = colourOf(snap);
  const breath = reduced ? 0.5 : 0.5 + 0.5 * Math.sin(t * 3.4);
  c2d.globalCompositeOperation = 'lighter';
  for (const br of list) {
    if (!isCore(br)) continue;
    const r = 40 + breath * 12;
    const glow = c2d.createRadialGradient(cx(br), cy(br), 2, cx(br), cy(br), r);
    glow.addColorStop(0, rgba(tint, 0.42));
    glow.addColorStop(0.45, rgba(tint, 0.15));
    glow.addColorStop(1, rgba(tint, 0));
    c2d.fillStyle = glow;
    c2d.beginPath(); c2d.arc(cx(br), cy(br), r, 0, 7); c2d.fill();
  }
}

export default { brick, under, over, pulseAt };
