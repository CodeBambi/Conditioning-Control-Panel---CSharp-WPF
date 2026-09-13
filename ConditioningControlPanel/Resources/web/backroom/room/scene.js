/* ============================================================================
 * backroom/room/scene.js - the room you look down on, and the player in it.
 *
 * One SVG, viewBox = the art's own pixels, preserveAspectRatio meet: the
 * picture scales to the window and every coordinate in geometry.js and
 * stations.json stays true at any size.
 *
 * THE WALK borrows the Arcademy's pure helpers (shell/walk.js: pathLength,
 * walkDurationMs, walkAt) and its student sprite (shell/ghosts.js), so the
 * player crosses this room at the campus's pace in the campus's body. What is
 * NOT borrowed is the campus walker itself: it paths along campus corridors and
 * pays every onDone, while here a second click must CANCEL the first walk (you
 * changed your mind about the slot) rather than open the station anyway.
 *
 * Reduced motion takes the state, not a faster version of the travel (Law VI):
 * the player stands at the destination on the same frame.
 * ==========================================================================*/

import { pathLength, walkDurationMs, walkAt } from '../../arcademy/shell/walk.js';
import { buildStudentSprite } from '../../arcademy/shell/ghosts.js';
import { ROOM_W, ROOM_H, DOOR, SPRITE_SCALE, resolveClick, toArt } from './geometry.js';

const SVGNS = 'http://www.w3.org/2000/svg';

function node(tag, attrs, cls) {
  const n = document.createElementNS(SVGNS, tag);
  if (cls) n.setAttribute('class', cls);
  for (const k of Object.keys(attrs || {})) n.setAttribute(k, String(attrs[k]));
  return n;
}

/**
 * @param {Object} o
 * @param {Element} o.mount
 * @param {Array} o.stations   normaliseStations() rows
 * @param {Function} o.label   (station) => display name (lexicon)
 * @param {boolean} o.reduced
 * @param {Function} o.onArrive (station) => void, when a walk to a hotspot lands
 * @param {Function=} o.log
 */
export function createScene(o) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  let reduced = !!o.reduced;
  let pos = DOOR.slice();
  let facing = 1;
  let run = null;
  let paused = false;

  const svg = node('svg', { viewBox: `0 0 ${ROOM_W} ${ROOM_H}`, preserveAspectRatio: 'xMidYMid meet', role: 'img' }, 'br-scene');
  svg.appendChild(node('image', { href: 'room/backroom_final.png', x: 0, y: 0, width: ROOM_W, height: ROOM_H }, 'br-art'));

  const spots = node('g', {}, 'br-spots');
  for (const s of o.stations) {
    if (!s.placed) continue;
    const [x, y, w, h] = s.hotspot;
    const r = node('rect', { x, y, width: w, height: h, rx: 18 }, 'br-spot is-' + s.state);
    r.dataset.station = s.id;
    const title = node('title');
    title.textContent = o.label(s);
    r.appendChild(title);
    spots.appendChild(r);
  }
  svg.appendChild(spots);

  const target = node('ellipse', { cx: 0, cy: 0, rx: 16, ry: 6 }, 'br-target');
  svg.appendChild(target);

  const you = node('g', {}, 'br-you gh-you');
  you.appendChild(node('ellipse', { cx: 0, cy: 1, rx: 22, ry: 8 }, 'br-youring'));
  const body = node('g', {}, 'br-youbody');
  try {
    const sprite = buildStudentSprite('self|backroom');
    if (sprite) body.appendChild(sprite);
  } catch (e) { say('sprite failed: ' + ((e && e.message) || e)); }
  you.appendChild(body);
  svg.appendChild(you);
  o.mount.appendChild(svg);

  function draw(t) {
    you.setAttribute('transform', `translate(${Math.round(pos[0] * 10) / 10},${Math.round(pos[1] * 10) / 10})`);
    const bob = (t != null && !reduced) ? -Math.abs(Math.sin((t / 220) * Math.PI)) * 2 : 0;
    body.setAttribute('transform', `translate(0,${bob.toFixed(2)}) scale(${facing * SPRITE_SCALE},${SPRITE_SCALE})`);
  }

  function land(r, arrived) {
    run = null;
    pos = r.legs[r.legs.length - 1].slice();
    draw(null);
    target.classList.remove('is-on');
    svg.classList.remove('is-walking');
    if (arrived && r.station) {
      try { o.onArrive(r.station); } catch (e) { say('onArrive threw: ' + ((e && e.message) || e)); }
    }
  }

  function frame() {
    const r = run;
    if (!r) return;
    if (paused) { r.t0 = performance.now() - r.at; r.raf = requestAnimationFrame(frame); return; }
    r.at = performance.now() - r.t0;
    if (r.at >= r.total) { land(r, true); return; }
    const p = walkAt(r.legs, r.at, r.total);
    pos = [p.x, p.y];
    if (p.facing) facing = p.facing;
    draw(r.at);
    r.raf = requestAnimationFrame(frame);
  }

  /** Walk to [x, y]; `station` (optional) opens on arrival. A new walk cancels the old one. */
  function walkTo(to, station) {
    if (run) { cancelAnimationFrame(run.raf); run = null; }
    const legs = [pos.slice(), to.slice()];
    const dx = to[0] - pos[0];
    if (Math.abs(dx) >= 0.5) facing = dx > 0 ? 1 : -1;
    target.setAttribute('cx', to[0]);
    target.setAttribute('cy', to[1]);
    target.classList.add('is-on');
    const r = { legs, station: station || null, total: walkDurationMs(pathLength(legs)), t0: performance.now(), at: 0, raf: 0 };
    if (reduced || pathLength(legs) < 2) {
      // THE STATE, NOT THE TRAVEL. Arrival still waits one turn, so a caller is
      // never re-entered from inside its own click.
      run = r;
      pos = to.slice();
      draw(null);
      setTimeout(() => { if (run === r) land(r, true); }, 0);
      return r.total;
    }
    run = r;
    svg.classList.add('is-walking');
    r.raf = requestAnimationFrame(frame);
    return r.total;
  }

  function onClick(e) {
    if (e.button !== 0) return;
    const pt = toArt(e.clientX, e.clientY, svg.getBoundingClientRect());
    const hit = resolveClick(pt, o.stations);
    if (hit.kind === 'none') return;
    // LAW VIII: the ring lands under the cursor on this frame, before any walk.
    walkTo(hit.to, hit.kind === 'station' ? hit.station : null);
  }
  svg.addEventListener('pointerdown', onClick);
  draw(null);

  return {
    walkTo,
    /** Click at art coordinates, for the smoke check and keyboard use. */
    clickArt(pt) { const hit = resolveClick(pt, o.stations); if (hit.kind !== 'none') walkTo(hit.to, hit.kind === 'station' ? hit.station : null); return hit.kind; },
    at: () => pos.slice(),
    walking: () => !!run,
    setReduced(v) { reduced = !!v; if (reduced && run) { cancelAnimationFrame(run.raf); land(run, true); } },
    pause(on) { paused = !!on; },
    /** Stop any walk without arriving (a station opened some other way, or the room is leaving). */
    halt() { if (run) { cancelAnimationFrame(run.raf); const r = run; r.station = null; land(r, false); } },
    destroy() {
      if (run) cancelAnimationFrame(run.raf);
      run = null;
      svg.removeEventListener('pointerdown', onClick);
      svg.remove();
    },
  };
}
