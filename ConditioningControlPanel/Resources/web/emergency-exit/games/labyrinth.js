/* ============================================================================
 * games/labyrinth.js - TRACE THE EXIT (Emergency Exit game #1).
 *
 * Drag a path from the ember entry to the exit through a small maze without
 * touching a wall, inside 25 s. Touch a wall: ember glitch, the trace resets to
 * the entry. Around the halfway mark the exit GLITCHES: static burst, some walls
 * re-carve (a few open, a few close - never across the trace, never sealing the
 * maze) and the exit relocates to another edge. Time out: the walls close around
 * the trace ("locked in") -> finish("failed"). Reach the exit -> finish("completed")
 * (the host always sends `sendback` for this game; the shell's outro does the gag).
 *
 * Input: pointer events (mouse/touch/pen). Lifting the pointer keeps the trace;
 * press again near the trace tip to continue, or press the entry to restart.
 * Keyboard: focus the canvas, arrow keys step the tip one cell (walls = hit).
 *
 * Photosafe: no static burst (soft board slide + tint), no shake on hits (tint),
 * slower heartbeat. Everything via api.photosafe / the .ee-photosafe class.
 *
 * Test seam: EE.games.labyrinth._test = { carve, solvable, bfsPath } (pure).
 * ==========================================================================*/
(function () {
  'use strict';
  var EE = window.EE = window.EE || {};
  EE.games = EE.games || {};

  var TIME_LIMIT = 25;          // seconds
  var WALL_W = 4;               // px, wall stroke
  var HIT_TOL = WALL_W / 2 + 4; // px from a wall centreline that counts as touching
  var TIP_GRAB = 18;            // px, press within this of the trace tip to resume
  var DIRS = [ [0, -1, 't', 'b'], [1, 0, 'r', 'l'], [0, 1, 'b', 't'], [-1, 0, 'l', 'r'] ]; // dx, dy, wall, opposite

  /* ---------------------------------------------------------------------------
   * PURE MAZE LOGIC
   * grid[y][x] = { t, r, b, l } (true = wall present)
   * ------------------------------------------------------------------------- */
  function carve(n, rng) {
    var grid = [], y, x;
    for (y = 0; y < n; y++) { grid[y] = []; for (x = 0; x < n; x++) grid[y][x] = { t: true, r: true, b: true, l: true }; }
    var seen = [], stack = [];
    for (y = 0; y < n; y++) { seen[y] = []; for (x = 0; x < n; x++) seen[y][x] = false; }
    var cx = Math.floor(rng() * n), cy = Math.floor(rng() * n);
    seen[cy][cx] = true; stack.push([cx, cy]);
    while (stack.length) {
      var cur = stack[stack.length - 1]; x = cur[0]; y = cur[1];
      var opts = [];
      for (var d = 0; d < 4; d++) {
        var nx = x + DIRS[d][0], ny = y + DIRS[d][1];
        if (nx >= 0 && ny >= 0 && nx < n && ny < n && !seen[ny][nx]) opts.push(d);
      }
      if (!opts.length) { stack.pop(); continue; }
      var pick = opts[Math.floor(rng() * opts.length)];
      var tx = x + DIRS[pick][0], ty = y + DIRS[pick][1];
      grid[y][x][DIRS[pick][2]] = false;
      grid[ty][tx][DIRS[pick][3]] = false;
      seen[ty][tx] = true; stack.push([tx, ty]);
    }
    return grid;
  }

  function bfsPath(grid, from, to) {
    var n = grid.length;
    var prev = {}, q = [from], key = function (c) { return c[0] + ',' + c[1]; };
    prev[key(from)] = null;
    while (q.length) {
      var c = q.shift();
      if (c[0] === to[0] && c[1] === to[1]) {
        var path = [], k = key(c), cur = c;
        while (cur) { path.unshift(cur); cur = prev[key(cur)]; }
        return path;
      }
      for (var d = 0; d < 4; d++) {
        if (grid[c[1]][c[0]][DIRS[d][2]]) continue;
        var nx = c[0] + DIRS[d][0], ny = c[1] + DIRS[d][1];
        if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
        var nk = nx + ',' + ny;
        if (nk in prev) continue;
        prev[nk] = c; q.push([nx, ny]);
      }
    }
    return null;
  }
  function solvable(grid, from, to) { return !!bfsPath(grid, from, to); }

  /** A random cell on edge e (0 top, 1 right, 2 bottom, 3 left). */
  function edgeCell(n, e, rng) {
    var i = Math.floor(rng() * n);
    return e === 0 ? [i, 0] : e === 1 ? [n - 1, i] : e === 2 ? [i, n - 1] : [0, i];
  }
  var EDGE_SIDE = ['t', 'r', 'b', 'l'];

  /**
   * Re-carve: open `open` random interior walls and close `close` random
   * interior walls, never a wall that sits between two consecutive cells of
   * `protect` (the player's trace cells), never one that makes `from`->`to`
   * unsolvable. Returns the number of changes made.
   */
  function recarve(grid, rng, open, close, protect, from, to) {
    var n = grid.length, changes = 0, tries, x, y, d, nx, ny;
    var protectSet = {};
    for (var i = 0; i + 1 < protect.length; i++) {
      protectSet[protect[i][0] + ',' + protect[i][1] + '>' + protect[i + 1][0] + ',' + protect[i + 1][1]] = true;
      protectSet[protect[i + 1][0] + ',' + protect[i + 1][1] + '>' + protect[i][0] + ',' + protect[i][1]] = true;
    }
    for (tries = 0; tries < 200 && open > 0; tries++) {
      x = Math.floor(rng() * n); y = Math.floor(rng() * n); d = Math.floor(rng() * 4);
      nx = x + DIRS[d][0]; ny = y + DIRS[d][1];
      if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
      if (!grid[y][x][DIRS[d][2]]) continue;
      grid[y][x][DIRS[d][2]] = false; grid[ny][nx][DIRS[d][3]] = false;
      open--; changes++;
    }
    for (tries = 0; tries < 300 && close > 0; tries++) {
      x = Math.floor(rng() * n); y = Math.floor(rng() * n); d = Math.floor(rng() * 4);
      nx = x + DIRS[d][0]; ny = y + DIRS[d][1];
      if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
      if (grid[y][x][DIRS[d][2]]) continue;
      if (protectSet[x + ',' + y + '>' + nx + ',' + ny]) continue;
      // never wall the cell the player is standing in, or the entry/exit cells
      var last = protect.length ? protect[protect.length - 1] : null;
      if (last && ((last[0] === x && last[1] === y) || (last[0] === nx && last[1] === ny))) continue;
      if ((from[0] === x && from[1] === y) || (from[0] === nx && from[1] === ny)) continue;
      if ((to[0] === x && to[1] === y) || (to[0] === nx && to[1] === ny)) continue;
      grid[y][x][DIRS[d][2]] = true; grid[ny][nx][DIRS[d][3]] = true;
      if (!solvable(grid, last || from, to) || !solvable(grid, from, to)) {
        grid[y][x][DIRS[d][2]] = false; grid[ny][nx][DIRS[d][3]] = false;
        continue;
      }
      close--; changes++;
    }
    return changes;
  }

  /* ---------------------------------------------------------------------------
   * GEOMETRY
   * ------------------------------------------------------------------------- */
  function distPointSeg(px, py, ax, ay, bx, by) {
    var dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
    var t = l2 ? ((px - ax) * dx + (py - ay) * dy) / l2 : 0;
    t = Math.max(0, Math.min(1, t));
    var qx = ax + t * dx, qy = ay + t * dy;
    return Math.hypot(px - qx, py - qy);
  }
  function segsCross(ax, ay, bx, by, cx, cy, dx, dy) {
    function o(px, py, qx, qy, rx, ry) { var v = (qy - py) * (rx - qx) - (qx - px) * (ry - qy); return v > 1e-9 ? 1 : v < -1e-9 ? 2 : 0; }
    var o1 = o(ax, ay, bx, by, cx, cy), o2 = o(ax, ay, bx, by, dx, dy), o3 = o(cx, cy, dx, dy, ax, ay), o4 = o(cx, cy, dx, dy, bx, by);
    return (o1 !== o2 && o3 !== o4);
  }

  /* ---------------------------------------------------------------------------
   * GAME
   * ------------------------------------------------------------------------- */
  var G = null; // live game state

  function start(init, api) {
    var rng = api.rng;
    var n = 9 + Math.floor(rng() * 3);             // 9..11
    var mount = api.mount;

    // --- size to the stage (design 960x640; tolerate 800x560)
    var stageH = mount.clientHeight || 520, stageW = mount.clientWidth || 920;
    var size = Math.max(360, Math.min(480, stageH - 30, Math.floor(stageW * 0.55)));
    var margin = 22;
    var cs = Math.floor((size - margin * 2) / n);
    var ox = Math.round((size - cs * n) / 2), oy = ox;

    // --- DOM
    mount.innerHTML = '';
    var wrap = document.createElement('div'); wrap.className = 'lab-wrap';
    var board = document.createElement('div'); board.className = 'lab-board ee-card';
    var canvas = document.createElement('canvas'); canvas.tabIndex = 0;
    canvas.setAttribute('role', 'application');
    canvas.setAttribute('aria-label', 'Maze. Drag from the ember entry to the exit. Arrow keys also move the trace.');
    var dpr = Math.max(1, Math.min(2, window.devicePixelRatio || 1));
    canvas.width = size * dpr; canvas.height = size * dpr;
    canvas.style.width = size + 'px'; canvas.style.height = size + 'px';
    board.appendChild(canvas);
    var side = document.createElement('div'); side.className = 'lab-side';
    side.innerHTML =
      '<h2 class="lab-title">trace <em>the exit</em></h2>' +
      '<p class="lab-sub">Press the ember, drag to the door, do not touch the walls. ' +
      'You have ' + TIME_LIMIT + ' seconds. The exit has been... restless lately.</p>' +
      '<div class="lab-legend">' +
      '<div><span class="lab-dot is-entry"></span>entry: press here to begin (or to start over)</div>' +
      '<div><span class="lab-dot is-exit"></span>exit: where it is right now</div>' +
      '<div><span class="lab-dot is-wall"></span>walls: touching one resets your trace</div>' +
      '</div>' +
      '<div class="lab-stat"><span>resets <b id="lab-resets">0</b></span><span>exit moved <b id="lab-moves">0</b></span></div>' +
      '<div class="lab-kbd">keyboard: focus the maze, <kbd>arrows</kbd> step the trace</div>';
    wrap.appendChild(board); wrap.appendChild(side);
    mount.appendChild(wrap);
    var elResets = side.querySelector('#lab-resets'), elMoves = side.querySelector('#lab-moves');

    // --- maze
    var grid = carve(n, rng);
    var entryEdge = Math.floor(rng() * 4);
    var exitEdge = (entryEdge + 2) % 4;
    var entry = edgeCell(n, entryEdge, rng);
    var exit = edgeCell(n, exitEdge, rng);
    grid[entry[1]][entry[0]][EDGE_SIDE[entryEdge]] = false;   // outer opening
    grid[exit[1]][exit[0]][EDGE_SIDE[exitEdge]] = false;

    var ctx = canvas.getContext('2d');
    ctx.scale(dpr, dpr);

    G = {
      api: api, n: n, cs: cs, ox: ox, oy: oy, size: size, grid: grid, canvas: canvas, ctx: ctx, board: board,
      entry: entry, entryEdge: entryEdge, exit: exit, exitEdge: exitEdge,
      trace: [], cells: [], dragging: false, pointerId: null,
      resets: 0, moves: 0, done: false,
      t0: performance.now(), limit: TIME_LIMIT, left: TIME_LIMIT,
      glitchAt: TIME_LIMIT * (0.45 + rng() * 0.1), glitched: false, glitchFx: 0, oldExit: null, oldExitFade: 0,
      hitFx: 0, winFx: 0, lockFx: 0, locking: false, heart: false,
      embers: [], raf: 0, tick: 0, rng: rng, photosafe: !!api.photosafe
    };

    api.hud.timer(TIME_LIMIT);
    api.say('press the ember and trace your way out. the walls are not shy.');

    // --- input
    canvas.addEventListener('pointerdown', onDown);
    canvas.addEventListener('pointermove', onMove);
    canvas.addEventListener('pointerup', onUp);
    canvas.addEventListener('pointercancel', onUp);
    canvas.addEventListener('lostpointercapture', onUp);
    canvas.addEventListener('keydown', onKey);
    canvas.addEventListener('contextmenu', function (e) { e.preventDefault(); });

    G.tick = setInterval(onSecond, 100);
    G.raf = requestAnimationFrame(frame);
    setTimeout(function () { try { canvas.focus({ preventScroll: true }); } catch (_e) {} }, 50);
  }

  function cellCenter(c) { return [G.ox + (c[0] + 0.5) * G.cs, G.oy + (c[1] + 0.5) * G.cs]; }
  function cellAt(px, py) {
    var x = Math.floor((px - G.ox) / G.cs), y = Math.floor((py - G.oy) / G.cs);
    if (x < 0 || y < 0 || x >= G.n || y >= G.n) return null;
    return [x, y];
  }
  function canvasPoint(e) {
    var r = G.canvas.getBoundingClientRect();
    return [(e.clientX - r.left) * (G.size / r.width), (e.clientY - r.top) * (G.size / r.height)];
  }

  /** wall segments (canvas px) of cell (x,y) that are present */
  function cellWalls(x, y, out) {
    if (x < 0 || y < 0 || x >= G.n || y >= G.n) return;
    var c = G.grid[y][x], X = G.ox + x * G.cs, Y = G.oy + y * G.cs, s = G.cs;
    if (c.t) out.push([X, Y, X + s, Y]);
    if (c.r) out.push([X + s, Y, X + s, Y + s]);
    if (c.b) out.push([X, Y + s, X + s, Y + s]);
    if (c.l) out.push([X, Y, X, Y + s]);
  }
  function wallsNear(px, py) {
    var out = [], cx = Math.floor((px - G.ox) / G.cs), cy = Math.floor((py - G.oy) / G.cs);
    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++) cellWalls(cx + dx, cy + dy, out);
    return out;
  }
  /** true when point (px,py) touches a wall, or the sweep from (ax,ay) crosses one */
  function hitsWall(px, py, ax, ay) {
    var walls = wallsNear(px, py), i, w;
    for (i = 0; i < walls.length; i++) {
      w = walls[i];
      if (distPointSeg(px, py, w[0], w[1], w[2], w[3]) < HIT_TOL) return true;
    }
    if (ax != null) {
      var d = Math.hypot(px - ax, py - ay);
      if (d > 1) {
        var steps = Math.max(1, Math.ceil(d / (G.cs * 0.4)));
        var lx = ax, ly = ay;
        for (var s = 1; s <= steps; s++) {
          var t = s / steps, qx = ax + (px - ax) * t, qy = ay + (py - ay) * t;
          var ws = wallsNear(qx, qy);
          for (i = 0; i < ws.length; i++) {
            w = ws[i];
            if (segsCross(lx, ly, qx, qy, w[0], w[1], w[2], w[3])) return true;
          }
          lx = qx; ly = qy;
        }
      }
    }
    return false;
  }
  /** Is (px,py) inside the maze rect (with the entry/exit openings outside it)? */
  function insideMaze(px, py) {
    return px >= G.ox && py >= G.oy && px <= G.ox + G.n * G.cs && py <= G.oy + G.n * G.cs;
  }
  function atExit(px, py) {
    var e = G.exit, X = G.ox + e[0] * G.cs, Y = G.oy + e[1] * G.cs, s = G.cs, side = G.exitEdge;
    // the outer third of the exit cell, or just beyond the opening
    if (px < X - 2 || px > X + s + 2 || py < Y - 2 || py > Y + s + 2) {
      // beyond the maze through the opening?
      if (side === 0 && py < G.oy && px > X && px < X + s) return true;
      if (side === 2 && py > G.oy + G.n * G.cs && px > X && px < X + s) return true;
      if (side === 3 && px < G.ox && py > Y && py < Y + s) return true;
      if (side === 1 && px > G.ox + G.n * G.cs && py > Y && py < Y + s) return true;
      return false;
    }
    if (side === 0) return py < Y + s * 0.34;
    if (side === 2) return py > Y + s * 0.66;
    if (side === 3) return px < X + s * 0.34;
    return px > X + s * 0.66;
  }
  function nearEntry(px, py) {
    var c = cellCenter(G.entry);
    return Math.hypot(px - c[0], py - c[1]) < G.cs * 0.8;
  }
  function tip() { return G.trace.length ? G.trace[G.trace.length - 1] : null; }

  function onDown(e) {
    if (!G || G.done || G.locking) return;
    var p = canvasPoint(e), t = tip();
    if (t && Math.hypot(p[0] - t[0], p[1] - t[1]) < TIP_GRAB) {
      G.dragging = true;
    } else if (nearEntry(p[0], p[1])) {
      G.trace = [cellCenter(G.entry)]; G.cells = [G.entry.slice()];
      G.dragging = true;
      G.api.say('go.');
    } else {
      G.api.say(t ? 'pick the trace back up at its tip, or press the ember to start over.' : 'start at the ember.');
      return;
    }
    G.pointerId = e.pointerId;
    try { G.canvas.setPointerCapture(e.pointerId); } catch (_e) {}
    try { G.canvas.focus({ preventScroll: true }); } catch (_e) {}
    e.preventDefault();
  }
  function onMove(e) {
    if (!G || !G.dragging || G.done || G.locking) return;
    if (G.pointerId != null && e.pointerId !== G.pointerId) return;
    var p = canvasPoint(e);
    advance(p[0], p[1]);
    e.preventDefault();
  }
  function onUp(e) {
    if (!G || !G.dragging) return;
    if (G.pointerId != null && e.pointerId != null && e.pointerId !== G.pointerId && e.type !== 'lostpointercapture') return;
    G.dragging = false; G.pointerId = null;
  }
  function onKey(e) {
    if (!G || G.done || G.locking) return;
    var d = e.key === 'ArrowUp' ? 0 : e.key === 'ArrowRight' ? 1 : e.key === 'ArrowDown' ? 2 : e.key === 'ArrowLeft' ? 3 : -1;
    if (d < 0) {
      if (e.key === ' ' || e.key === 'Enter') { if (!G.trace.length) { G.trace = [cellCenter(G.entry)]; G.cells = [G.entry.slice()]; G.api.say('go.'); } e.preventDefault(); }
      return;
    }
    e.preventDefault();
    if (!G.trace.length) { G.trace = [cellCenter(G.entry)]; G.cells = [G.entry.slice()]; }
    var cur = G.cells[G.cells.length - 1];
    var cell = G.grid[cur[1]][cur[0]];
    var nx = cur[0] + DIRS[d][0], ny = cur[1] + DIRS[d][1];
    if (cell[DIRS[d][2]]) { onHit(); return; }              // wall = hit
    if (nx < 0 || ny < 0 || nx >= G.n || ny >= G.n) {
      // stepping out through an opening: exit wins, entry opening does nothing
      if (cur[0] === G.exit[0] && cur[1] === G.exit[1] && d === G.exitEdge) { onWin(); }
      return;
    }
    var c = cellCenter([nx, ny]);
    G.trace.push(c); G.cells.push([nx, ny]); spawnEmbers(c[0], c[1], 3);
    if (nx === G.exit[0] && ny === G.exit[1]) { G.api.say('one more step...'); }
  }

  function advance(px, py) {
    var t = tip();
    if (!t) return;
    var outside = !insideMaze(px, py);
    if (outside && !atExit(px, py)) {
      // wandered outside the maze not through the exit (e.g. back out the entry): clamp, ignore
      return;
    }
    if (hitsWall(px, py, t[0], t[1])) { onHit(); return; }
    if (Math.hypot(px - t[0], py - t[1]) < 2) return;
    G.trace.push([px, py]);
    var c = cellAt(px, py);
    if (c) {
      var last = G.cells[G.cells.length - 1];
      if (!last || last[0] !== c[0] || last[1] !== c[1]) G.cells.push(c);
    }
    if (G.trace.length % 2 === 0) spawnEmbers(px, py, 1);
    if (atExit(px, py)) onWin();
  }

  function onHit() {
    if (!G || G.done) return;
    G.dragging = false; G.pointerId = null;
    G.resets++; if (G.resets > 0) document.getElementById('lab-resets').textContent = String(G.resets);
    G.hitFx = performance.now();
    G.trace = []; G.cells = [];
    var b = G.board; b.classList.remove('is-hit'); void b.offsetWidth; b.classList.add('is-hit');
    var lines = ['oops. the walls are not shy. back to the ember.', 'touched. the trace cools. start again, {honorific}.', 'so close to the wall. so far from the door. again.', 'hihi. the maze felt that. from the top.'];
    G.api.say(lines[Math.floor(G.rng() * lines.length)]);
  }

  function onWin() {
    if (!G || G.done) return;
    G.done = true; G.dragging = false;
    G.winFx = performance.now();
    clearInterval(G.tick);
    G.api.hud.timer(G.left);
    G.api.say('you found the exit! ...wait.');
    var left = G.left;
    setTimeout(function () {
      if (!G) return;
      G.api.finish('completed', { timeLeft: Math.round(left * 10) / 10, resets: G.resets, exitMoves: G.moves, n: G.n });
    }, 700);
  }

  function onTimeout() {
    if (!G || G.done || G.locking) return;
    G.locking = true; G.dragging = false;
    G.lockFx = performance.now();
    G.api.hud.timer(0);
    G.api.say('time. the walls close in. locked in, {honorific}.');
    setTimeout(function () {
      if (!G) return;
      G.done = true;
      G.api.finish('failed', { resets: G.resets, exitMoves: G.moves, reason: 'timeout', n: G.n });
    }, 1400);
  }

  function onSecond() {
    if (!G || G.done || G.locking) return;
    var el = (performance.now() - G.t0) / 1000;
    G.left = Math.max(0, G.limit - el);
    G.api.hud.timer(G.left);
    if (!G.glitched && el >= G.glitchAt) doGlitch();
    var heart = G.left <= 5 && G.left > 0;
    if (heart !== G.heart) { G.heart = heart; G.board.classList.toggle('is-heart', heart); if (heart) G.api.say('five seconds. hear that? that is the door, thinking about it.'); }
    if (G.left <= 0) onTimeout();
  }

  function doGlitch() {
    G.glitched = true;
    G.moves++; document.getElementById('lab-moves').textContent = String(G.moves);
    G.glitchFx = performance.now();
    G.dragging = false; G.pointerId = null;
    // relocate the exit to a different edge than both the entry and the old exit
    var edges = [0, 1, 2, 3].filter(function (e) { return e !== G.entryEdge && e !== G.exitEdge; });
    var newEdge = edges[Math.floor(G.rng() * edges.length)];
    var newExit = edgeCell(G.n, newEdge, G.rng);
    var tries = 0;
    while (tries++ < 20 && ((newExit[0] === G.entry[0] && newExit[1] === G.entry[1]) || (newExit[0] === G.exit[0] && newExit[1] === G.exit[1]))) newExit = edgeCell(G.n, newEdge, G.rng);
    G.grid[G.exit[1]][G.exit[0]][EDGE_SIDE[G.exitEdge]] = true;      // old door bricks up
    G.oldExit = { cell: G.exit.slice(), edge: G.exitEdge };
    G.exit = newExit; G.exitEdge = newEdge;
    G.grid[newExit[1]][newExit[0]][EDGE_SIDE[newEdge]] = false;     // new door opens
    // re-carve a few walls, protected around the trace
    var cur = G.cells.length ? G.cells[G.cells.length - 1] : G.entry;
    recarve(G.grid, G.rng, 3 + Math.floor(G.rng() * 3), 3 + Math.floor(G.rng() * 3), G.cells, G.entry, G.exit);
    if (!solvable(G.grid, cur, G.exit)) {
      // should not happen (recarve guards), but never strand the player: open the bfs-blocking walls by re-opening along a fresh carve
      G.grid = carve(G.n, G.rng);
      G.grid[G.entry[1]][G.entry[0]][EDGE_SIDE[G.entryEdge]] = false;
      G.grid[G.exit[1]][G.exit[0]][EDGE_SIDE[G.exitEdge]] = false;
      G.trace = []; G.cells = [];
    } else {
      trimTrace();
    }
    if (G.photosafe) {
      var b = G.board; b.classList.remove('is-slide'); void b.offsetWidth; b.classList.add('is-slide');
    }
    G.api.say('...did the exit just move? it did. hihi. the walls shuffled too.');
  }

  /** Cut the trace back to the last point that is still clear of every wall. */
  function trimTrace() {
    var keep = 0;
    for (var i = 0; i < G.trace.length; i++) {
      var p = G.trace[i], prev = i ? G.trace[i - 1] : null;
      if (hitsWall(p[0], p[1], prev ? prev[0] : null, prev ? prev[1] : null)) break;
      keep = i + 1;
    }
    if (keep < G.trace.length) {
      G.trace.length = keep;
      // rebuild cells from the remaining trace
      var cells = [];
      for (var j = 0; j < G.trace.length; j++) {
        var c = cellAt(G.trace[j][0], G.trace[j][1]);
        if (c && (!cells.length || cells[cells.length - 1][0] !== c[0] || cells[cells.length - 1][1] !== c[1])) cells.push(c);
      }
      G.cells = cells;
    }
    if (!G.trace.length) { G.cells = []; }
  }

  /* ---- embers ---------------------------------------------------------- */
  function spawnEmbers(x, y, k) {
    for (var i = 0; i < k; i++) {
      G.embers.push({ x: x, y: y, vx: (G.rng() - 0.5) * 30, vy: -10 - G.rng() * 30, life: 0.5 + G.rng() * 0.5, t: 0 });
    }
    if (G.embers.length > 140) G.embers.splice(0, G.embers.length - 140);
  }

  /* ---- render ---------------------------------------------------------- */
  var lastFrame = 0;
  function frame() {
    if (!G) return;
    G.raf = requestAnimationFrame(frame);
    // one clock for everything: the rAF timestamp can lag performance.now() (a lot under
    // virtual time), which would turn the 0.4 s static window into a negative age.
    var now = performance.now();
    var dt = lastFrame ? Math.min(0.05, (now - lastFrame) / 1000) : 0.016; lastFrame = now;
    var ctx = G.ctx, s = G.size;
    ctx.clearRect(0, 0, s, s);
    // board
    ctx.fillStyle = '#1A1A2E'; ctx.fillRect(0, 0, s, s);
    // faint grid floor
    ctx.strokeStyle = 'rgba(220,20,60,0.06)'; ctx.lineWidth = 1;
    for (var i = 0; i <= G.n; i++) {
      ctx.beginPath(); ctx.moveTo(G.ox + i * G.cs, G.oy); ctx.lineTo(G.ox + i * G.cs, G.oy + G.n * G.cs); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(G.ox, G.oy + i * G.cs); ctx.lineTo(G.ox + G.n * G.cs, G.oy + i * G.cs); ctx.stroke();
    }

    // lock-in: walls thicken + crimson floods the trace cells
    var lockT = G.locking ? Math.min(1, (now - G.lockFx) / 1200) : 0;

    // walls (glow pass + crisp pass)
    drawWalls(ctx, 'rgba(220,20,60,0.28)', WALL_W + 6 + lockT * 10);
    drawWalls(ctx, '#DC143C', WALL_W + lockT * 8);

    // trace cells flood when locking
    if (lockT > 0) {
      ctx.fillStyle = 'rgba(220,20,60,' + (0.15 + 0.55 * lockT) + ')';
      var cellsToFlood = G.cells.length ? G.cells : [G.entry];
      for (i = 0; i < cellsToFlood.length; i++) {
        var fc = cellsToFlood[i], inset = (1 - lockT) * G.cs * 0.5;
        ctx.fillRect(G.ox + fc[0] * G.cs + inset, G.oy + fc[1] * G.cs + inset, G.cs - inset * 2, G.cs - inset * 2);
      }
    }

    // old exit fading
    if (G.oldExit) {
      var age = (now - G.glitchFx) / 1000;
      if (age < 1.2) {
        var oc = cellCenter(G.oldExit.cell);
        ctx.globalAlpha = Math.max(0, 1 - age / 1.2);
        ctx.strokeStyle = '#FF69B4'; ctx.lineWidth = 2; ctx.setLineDash([4, 4]);
        ctx.beginPath(); ctx.arc(oc[0], oc[1], G.cs * 0.28, 0, Math.PI * 2); ctx.stroke();
        ctx.setLineDash([]); ctx.globalAlpha = 1;
      } else G.oldExit = null;
    }

    // entry marker (ember)
    var ec = cellCenter(G.entry), pulse = 0.5 + 0.5 * Math.sin(now / 400);
    ctx.fillStyle = 'rgba(255,138,92,' + (0.18 + 0.12 * pulse) + ')';
    ctx.beginPath(); ctx.arc(ec[0], ec[1], G.cs * 0.42, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#FF8A5C';
    ctx.beginPath(); ctx.arc(ec[0], ec[1], G.cs * 0.16, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#FF8A5C'; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.arc(ec[0], ec[1], G.cs * (0.28 + 0.06 * pulse), 0, Math.PI * 2); ctx.stroke();

    // exit marker (pink door on the outer edge + ring)
    drawExit(ctx, now);

    // trace
    if (G.trace.length > 1) {
      ctx.lineCap = 'round'; ctx.lineJoin = 'round';
      ctx.strokeStyle = 'rgba(255,138,92,0.22)'; ctx.lineWidth = 12;
      ctx.beginPath(); ctx.moveTo(G.trace[0][0], G.trace[0][1]);
      for (i = 1; i < G.trace.length; i++) ctx.lineTo(G.trace[i][0], G.trace[i][1]);
      ctx.stroke();
      ctx.strokeStyle = G.locking ? '#A99CC0' : '#FF8A5C'; ctx.lineWidth = 4.5;
      ctx.beginPath(); ctx.moveTo(G.trace[0][0], G.trace[0][1]);
      for (i = 1; i < G.trace.length; i++) ctx.lineTo(G.trace[i][0], G.trace[i][1]);
      ctx.stroke();
    }
    var tp = tip();
    if (tp) {
      ctx.fillStyle = '#FFE3D3';
      ctx.beginPath(); ctx.arc(tp[0], tp[1], 4.5, 0, Math.PI * 2); ctx.fill();
      ctx.strokeStyle = 'rgba(255,138,92,' + (0.5 + 0.4 * pulse) + ')'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(tp[0], tp[1], 9 + 3 * pulse, 0, Math.PI * 2); ctx.stroke();
    }

    // embers
    for (i = G.embers.length - 1; i >= 0; i--) {
      var em = G.embers[i]; em.t += dt;
      if (em.t >= em.life) { G.embers.splice(i, 1); continue; }
      em.x += em.vx * dt; em.y += em.vy * dt; em.vy += 20 * dt;
      var a = 1 - em.t / em.life;
      ctx.fillStyle = 'rgba(255,' + Math.round(138 + 60 * a) + ',92,' + (0.8 * a) + ')';
      ctx.beginPath(); ctx.arc(em.x, em.y, 1.2 + 1.8 * a, 0, Math.PI * 2); ctx.fill();
    }

    // hit tint
    if (G.hitFx) {
      var ha = (now - G.hitFx) / 1000;
      if (ha < 0.45) {
        ctx.fillStyle = 'rgba(255,138,92,' + (0.22 * (1 - ha / 0.45)) + ')';
        ctx.fillRect(0, 0, s, s);
      } else G.hitFx = 0;
    }
    // win burst
    if (G.winFx) {
      var wa = (now - G.winFx) / 1000;
      var xc = cellCenter(G.exit);
      ctx.strokeStyle = 'rgba(255,105,180,' + Math.max(0, 0.9 - wa) + ')'; ctx.lineWidth = 3;
      ctx.beginPath(); ctx.arc(xc[0], xc[1], 10 + wa * 160, 0, Math.PI * 2); ctx.stroke();
    }
    // glitch static
    if (G.glitchFx) {
      var ga = (now - G.glitchFx) / 1000;
      if (ga < 0.4) {
        if (!G.photosafe) {
          var k = Math.floor(60 * (1 - ga / 0.4));
          for (i = 0; i < k; i++) {
            var rx = Math.random() * s, ry = Math.random() * s, rw = 6 + Math.random() * 70, rh = 1 + Math.random() * 4;
            ctx.fillStyle = (i % 3 === 0) ? 'rgba(255,138,92,0.55)' : (i % 3 === 1 ? 'rgba(220,20,60,0.5)' : 'rgba(243,233,246,0.35)');
            ctx.fillRect(rx, ry, rw, rh);
          }
          ctx.fillStyle = 'rgba(243,233,246,' + (0.08 * (1 - ga / 0.4)) + ')'; ctx.fillRect(0, 0, s, s);
        } else {
          ctx.fillStyle = 'rgba(255,138,92,' + (0.16 * (1 - ga / 0.4)) + ')'; ctx.fillRect(0, 0, s, s);
        }
      } else if (ga > 1.5) G.glitchFx = 0;
    }
  }

  function drawWalls(ctx, style, width) {
    ctx.strokeStyle = style; ctx.lineWidth = width; ctx.lineCap = 'square';
    ctx.beginPath();
    for (var y = 0; y < G.n; y++) for (var x = 0; x < G.n; x++) {
      var c = G.grid[y][x], X = G.ox + x * G.cs, Y = G.oy + y * G.cs, s = G.cs;
      if (c.t) { ctx.moveTo(X, Y); ctx.lineTo(X + s, Y); }
      if (c.l) { ctx.moveTo(X, Y); ctx.lineTo(X, Y + s); }
      if (y === G.n - 1 && c.b) { ctx.moveTo(X, Y + s); ctx.lineTo(X + s, Y + s); }
      if (x === G.n - 1 && c.r) { ctx.moveTo(X + s, Y); ctx.lineTo(X + s, Y + s); }
    }
    ctx.stroke();
  }

  function drawExit(ctx, now) {
    var e = G.exit, X = G.ox + e[0] * G.cs, Y = G.oy + e[1] * G.cs, s = G.cs, side = G.exitEdge;
    var pulse = 0.5 + 0.5 * Math.sin(now / 300);
    var gl = (G.glitchFx && now - G.glitchFx < 1200) ? 1 : 0;
    // door bar on the outer edge
    ctx.fillStyle = '#FF69B4';
    var bw = s * 0.5, bh = 5;
    if (side === 0) ctx.fillRect(X + s * 0.25, Y - 2, bw, bh);
    if (side === 2) ctx.fillRect(X + s * 0.25, Y + s - 3, bw, bh);
    if (side === 3) ctx.fillRect(X - 2, Y + s * 0.25, bh, bw);
    if (side === 1) ctx.fillRect(X + s - 3, Y + s * 0.25, bh, bw);
    // glow + ring in the cell
    var c = cellCenter(e);
    ctx.fillStyle = 'rgba(255,105,180,' + (0.12 + 0.12 * pulse + 0.2 * gl) + ')';
    ctx.beginPath(); ctx.arc(c[0], c[1], s * 0.42, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = 'rgba(255,105,180,' + (0.7 + 0.3 * pulse) + ')'; ctx.lineWidth = 2.5;
    ctx.beginPath(); ctx.arc(c[0], c[1], s * (0.22 + 0.08 * pulse), 0, Math.PI * 2); ctx.stroke();
    // "EXIT" glyph: tiny arrow pointing out
    ctx.strokeStyle = '#FFE3F0'; ctx.lineWidth = 2; ctx.lineCap = 'round';
    var ax = c[0], ay = c[1], L = s * 0.16;
    ctx.beginPath();
    if (side === 0) { ctx.moveTo(ax, ay + L); ctx.lineTo(ax, ay - L); ctx.moveTo(ax - L * 0.6, ay - L * 0.4); ctx.lineTo(ax, ay - L); ctx.lineTo(ax + L * 0.6, ay - L * 0.4); }
    if (side === 2) { ctx.moveTo(ax, ay - L); ctx.lineTo(ax, ay + L); ctx.moveTo(ax - L * 0.6, ay + L * 0.4); ctx.lineTo(ax, ay + L); ctx.lineTo(ax + L * 0.6, ay + L * 0.4); }
    if (side === 3) { ctx.moveTo(ax + L, ay); ctx.lineTo(ax - L, ay); ctx.moveTo(ax - L * 0.4, ay - L * 0.6); ctx.lineTo(ax - L, ay); ctx.lineTo(ax - L * 0.4, ay + L * 0.6); }
    if (side === 1) { ctx.moveTo(ax - L, ay); ctx.lineTo(ax + L, ay); ctx.moveTo(ax + L * 0.4, ay - L * 0.6); ctx.lineTo(ax + L, ay); ctx.lineTo(ax + L * 0.4, ay + L * 0.6); }
    ctx.stroke();
  }

  function destroy() {
    if (!G) return;
    clearInterval(G.tick);
    cancelAnimationFrame(G.raf);
    try {
      G.canvas.removeEventListener('pointerdown', onDown);
      G.canvas.removeEventListener('pointermove', onMove);
      G.canvas.removeEventListener('pointerup', onUp);
      G.canvas.removeEventListener('pointercancel', onUp);
      G.canvas.removeEventListener('keydown', onKey);
    } catch (_e) {}
    G.board.classList.remove('is-heart');
    G = null;
  }

  EE.games.labyrinth = {
    id: 'labyrinth', start: start, destroy: destroy,
    _test: { carve: carve, solvable: solvable, bfsPath: bfsPath, recarve: recarve, edgeCell: edgeCell, DIRS: DIRS, state: function () { return G; } }
  };
  if (typeof EE.registerGame === 'function') EE.registerGame(EE.games.labyrinth);
})();
