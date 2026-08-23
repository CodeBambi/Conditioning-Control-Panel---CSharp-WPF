/* ============================================================================
 * games/jigsaw.js - "rearrange the picture" (Emergency Exit minigame)
 *
 * 3x3 swap puzzle skinned with ONE of the user's own GIFs (init.assets.gifs).
 * Each tile is a CSS background-image with a per-tile background-position, so
 * the animated GIF keeps playing across the tiles. Drag a tile onto another to
 * swap (or tap one, then tap another). Correctly placed tiles wear a thin ember
 * outline (the tell). 60 s HUD countdown.
 *
 * THE GLITCH: at ~40% progress or after ~12 s (whichever first) a static burst
 * (photosafe: a soft ember breath + crossfade) swaps the picture to a DIFFERENT
 * gif (tile positions kept) and two tiles trade places - preferring tiles that
 * were correct. It may happen a second time near the end.
 *
 * All correct   -> api.finish("completed", meta)
 * Timer runs out -> a tile falls off the board ("it slipped out of your hands")
 *                  -> api.finish("failed", meta)
 *
 * Shell contract: EMERGENCY_EXIT.md "Shell API". Plain script (no module): it
 * calls EE.registerGame at load. Pure logic is exposed as EE_JIGSAW_LOGIC for
 * the Node smoke test (ee-logic-tests.js in the authoring scratchpad).
 * ==========================================================================*/
(function () {
  'use strict';

  var N = 3;                       // grid side
  var TILES = N * N;
  var TIME_LIMIT_MS = 60000;
  var GLITCH_PROGRESS = 0.4;       // first glitch at 40% correct ...
  var GLITCH_AFTER_MS = 12000;     // ... or after 12 s, whichever first
  var GLITCH2_PROGRESS = 7 / 9;    // second glitch window opens near the end
  var GLITCH2_MIN_GAP_MS = 6000;   // ... but not right after the first one
  var TAP_SLOP = 7;                // px; under this a pointer down/up is a tap

  /* ---------------------------------------------------------------------------
   * Pure logic (no DOM) - testable.
   * state.slots[slot] = piece id (0..8). Solved when slots[i] === i for all i.
   * ------------------------------------------------------------------------ */
  function countCorrect(slots) {
    var n = 0;
    for (var i = 0; i < slots.length; i++) if (slots[i] === i) n++;
    return n;
  }
  function isSolved(slots) { return countCorrect(slots) === slots.length; }

  /* Fisher-Yates with an injected rng. Every permutation is reachable by swaps,
   * so "solvable" is guaranteed; "non-trivial" = at most 1 piece already home
   * and at least 6 pieces displaced (tries a few times, then forces it). */
  function shuffle(rng, size) {
    size = size || TILES;
    var best = null, bestScore = Infinity;
    for (var attempt = 0; attempt < 40; attempt++) {
      var a = [];
      for (var i = 0; i < size; i++) a.push(i);
      for (var j = size - 1; j > 0; j--) {
        var k = Math.floor(rng() * (j + 1));
        var t = a[j]; a[j] = a[k]; a[k] = t;
      }
      var fixed = countCorrect(a);
      if (fixed <= 1 && size - fixed >= Math.min(6, size)) return a;
      if (fixed < bestScore) { bestScore = fixed; best = a; }
    }
    // force: rotate any pieces still at home
    var out = best.slice();
    for (var s = 0; s < size; s++) {
      if (out[s] === s) {
        var o = (s + 1) % size;
        var tmp = out[s]; out[s] = out[o]; out[o] = tmp;
      }
    }
    return out;
  }

  function swapSlots(slots, a, b) {
    var t = slots[a]; slots[a] = slots[b]; slots[b] = t;
    return slots;
  }

  /* Pick two slots to swap for the glitch: prefer two CORRECT tiles (so the tell
   * disappears and progress drops), else one correct + one wrong, else any two.
   * Never produces a solved board. */
  function pickGlitchSwap(slots, rng) {
    var ok = [], bad = [];
    for (var i = 0; i < slots.length; i++) (slots[i] === i ? ok : bad).push(i);
    var pick = function (arr) { return arr.splice(Math.floor(rng() * arr.length), 1)[0]; };
    var a, b;
    if (ok.length >= 2) { a = pick(ok); b = pick(ok); }
    else if (ok.length === 1) { a = pick(ok); b = pick(bad); }
    else { a = pick(bad); b = pick(bad); }
    // guard: a swap of two wrong tiles could accidentally solve it; re-roll once
    var test = slots.slice(); swapSlots(test, a, b);
    if (isSolved(test)) {
      var c = (b + 1) % slots.length; if (c === a) c = (c + 1) % slots.length;
      return [a, c];
    }
    return [a, b];
  }

  /* Which gif to show next: a DIFFERENT one than the current (when possible). */
  function pickOtherIndex(current, count, rng) {
    if (count <= 1) return current;
    var idx = Math.floor(rng() * (count - 1));
    if (idx >= current) idx++;
    return idx;
  }

  /* Background-position percent for piece id p in an N x N grid. */
  function piecePosition(p, n) {
    n = n || N;
    var r = Math.floor(p / n), c = p % n;
    var step = n > 1 ? 100 / (n - 1) : 0;
    return { x: c * step, y: r * step };
  }

  var LOGIC = {
    N: N, TILES: TILES,
    shuffle: shuffle, isSolved: isSolved, countCorrect: countCorrect,
    swapSlots: swapSlots, pickGlitchSwap: pickGlitchSwap, pickOtherIndex: pickOtherIndex,
    piecePosition: piecePosition,
    GLITCH_PROGRESS: GLITCH_PROGRESS, GLITCH_AFTER_MS: GLITCH_AFTER_MS,
  };
  var G = (typeof window !== 'undefined') ? window : globalThis;
  G.EE_JIGSAW_LOGIC = LOGIC;
  if (typeof module !== 'undefined' && module.exports) module.exports = LOGIC;
  if (typeof document === 'undefined') return;   // Node test import stops here

  /* ---------------------------------------------------------------------------
   * Fabricated "gifs" for mock mode / empty libraries: SVG data URIs with bold
   * shapes so the puzzle is still readable. Never loaded from the network.
   * ------------------------------------------------------------------------ */
  function svgUri(svg) { return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg); }
  function fabricated() {
    var a = svgUri(
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 600">' +
      '<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#2d2d52"/><stop offset="1" stop-color="#DC143C"/></linearGradient></defs>' +
      '<rect width="600" height="600" fill="url(#g)"/>' +
      '<circle cx="300" cy="300" r="210" fill="none" stroke="#FF69B4" stroke-width="28"/>' +
      '<circle cx="300" cy="300" r="120" fill="#FF8A5C"/>' +
      '<path d="M60 540 L540 60" stroke="#F3E9F6" stroke-width="18" stroke-linecap="round"/>' +
      '<text x="300" y="338" font-family="Segoe UI, sans-serif" font-size="120" font-weight="900" text-anchor="middle" fill="#1A1A2E">A</text>' +
      '</svg>');
    var b = svgUri(
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 600">' +
      '<defs><radialGradient id="g" cx=".3" cy=".3"><stop offset="0" stop-color="#FF69B4"/><stop offset="1" stop-color="#1A1A2E"/></radialGradient></defs>' +
      '<rect width="600" height="600" fill="url(#g)"/>' +
      '<rect x="90" y="90" width="420" height="420" rx="40" fill="none" stroke="#FF8A5C" stroke-width="26"/>' +
      '<polygon points="300,130 470,470 130,470" fill="#DC143C" opacity=".9"/>' +
      '<circle cx="300" cy="380" r="70" fill="#F3E9F6"/>' +
      '<text x="300" y="410" font-family="Segoe UI, sans-serif" font-size="90" font-weight="900" text-anchor="middle" fill="#DC143C">B</text>' +
      '</svg>');
    var c = svgUri(
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 600 600">' +
      '<defs><linearGradient id="g" x1="0" y1="1" x2="1" y2="0"><stop offset="0" stop-color="#FF8A5C"/><stop offset=".5" stop-color="#252542"/><stop offset="1" stop-color="#FF69B4"/></linearGradient></defs>' +
      '<rect width="600" height="600" fill="url(#g)"/>' +
      '<g stroke="#F3E9F6" stroke-width="16" fill="none" opacity=".85">' +
      '<path d="M0 200 Q150 100 300 200 T600 200"/><path d="M0 400 Q150 300 300 400 T600 400"/></g>' +
      '<circle cx="150" cy="150" r="80" fill="#DC143C"/><circle cx="450" cy="450" r="80" fill="#DC143C"/>' +
      '<text x="300" y="340" font-family="Segoe UI, sans-serif" font-size="120" font-weight="900" text-anchor="middle" fill="#1A1A2E">C</text>' +
      '</svg>');
    return [a, b, c];
  }

  /* init.assets.gifs entries may be URLs (ccp.assets / data:) or, in mock mode,
   * raw CSS gradient strings. Normalise to a background-image value. */
  function toBgImage(src) {
    if (typeof src !== 'string' || !src) return null;
    var s = src.trim();
    if (/^(linear|radial|conic|repeating-[a-z]+)-gradient\(/i.test(s)) return s;
    if (/^url\(/i.test(s)) return s;
    return 'url("' + s.replace(/["\\]/g, '\\$&') + '")';
  }

  function ensureCss() {
    // dev hook (authoring only): ?noanim=1 freezes animations/transitions so a
    // headless --screenshot shows end states instead of first frames
    try {
      if (/[?&]noanim=1/.test(location.search) && !document.getElementById('ee-noanim')) {
        var st = document.createElement('style'); st.id = 'ee-noanim';
        st.textContent = '*,*::before,*::after{animation:none!important;transition:none!important}';
        document.head.appendChild(st);
      }
    } catch (_e) {}
    var links = document.querySelectorAll('link[rel="stylesheet"]');
    for (var i = 0; i < links.length; i++) if (/games\/jigsaw\.css/i.test(links[i].getAttribute('href') || '')) return;
    var l = document.createElement('link');
    l.rel = 'stylesheet'; l.href = 'games/jigsaw.css'; l.setAttribute('data-eejig', '1');
    document.head.appendChild(l);
  }

  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  /* ---------------------------------------------------------------------------
   * The game
   * ------------------------------------------------------------------------ */
  var game = {
    id: 'jigsaw',
    _root: null, _timers: [], _raf: 0, _off: [], _alive: false,

    start: function (init, api) {
      var self = this;
      this._alive = true;
      ensureCss();
      var rng = (api && typeof api.rng === 'function') ? api.rng : Math.random;
      var photosafe = !!(api && api.photosafe) || !!(init && init.photosafe);
      var say = function (t) { try { if (api && api.say) api.say(t); } catch (_e) {} };
      var hud = (api && api.hud) || {};

      // ---- mount ---------------------------------------------------------
      var stage = (api && (api.root || api.stage || api.mount)) || document.querySelector('.ee-stage') || document.body;
      var root = el('div', 'eejig');
      this._root = root;
      var board = el('div', 'eejig-board');
      var stat = el('div', 'eejig-static');
      var panel = el('div', 'eejig-panel');
      panel.appendChild(el('p', 'eejig-kicker', 'emergency exit'));
      panel.appendChild(el('h2', 'eejig-title', 'Rearrange the picture'));
      panel.appendChild(el('p', 'eejig-sub', 'Drag a tile onto another to swap them. Or tap one, then the other. Put it back the way it was.'));
      var pips = el('div', 'eejig-pips');
      var pipEls = [];
      for (var p = 0; p < TILES; p++) { var pe = el('i', 'eejig-pip'); pips.appendChild(pe); pipEls.push(pe); }
      panel.appendChild(pips);
      var count = el('p', 'eejig-count');
      var countB = el('b', null, '0'); count.appendChild(countB); count.appendChild(document.createTextNode(' / ' + TILES + ' in place'));
      panel.appendChild(count);
      var legend = el('p', 'eejig-legend'); legend.appendChild(el('i')); legend.appendChild(document.createTextNode('ember edge = that one is right'));
      panel.appendChild(legend);
      var fine = el('p', 'eejig-fine', 'it is your own picture. you know what it looks like. probably.');
      panel.appendChild(fine);
      root.appendChild(board); board.appendChild(stat); root.appendChild(panel);
      stage.appendChild(root);

      // ---- pictures ------------------------------------------------------
      var gifs = [];
      try { gifs = ((init && init.assets && init.assets.gifs) || []).map(toBgImage).filter(Boolean); } catch (_e) { gifs = []; }
      if (gifs.length < 2) gifs = gifs.concat(fabricated().map(toBgImage)).slice(0, Math.max(3, gifs.length));
      var picIdx = Math.floor(rng() * gifs.length);

      // ---- state ---------------------------------------------------------
      var slots = shuffle(rng);            // slots[slot] = piece
      var tiles = [];                      // tiles[piece] = element
      var tileSize = 140, gap = 6, boardSize = 460;
      var moves = 0, glitches = 0, done = false;
      var t0 = performance.now(), firstGlitchAt = 0, glitch1 = false, glitch2 = false;
      var glitch2Roll = rng() < 0.75;      // the second glitch is likely, not certain
      var sel = -1;                        // selected piece (tap mode)

      for (var i = 0; i < TILES; i++) {
        var t = el('div', 'eejig-tile');
        t.setAttribute('data-piece', String(i));
        tiles.push(t); board.appendChild(t);
      }
      stat && board.appendChild(stat);     // keep the static layer on top

      function applyPicture() {
        for (var q = 0; q < TILES; q++) {
          var pos = piecePosition(q, N);
          tiles[q].style.backgroundImage = gifs[picIdx];
          tiles[q].style.backgroundPosition = pos.x + '% ' + pos.y + '%';
        }
      }
      function slotOf(piece) { return slots.indexOf(piece); }
      function slotXY(slot) {
        var r = Math.floor(slot / N), c = slot % N;
        return { x: gap + c * (tileSize + gap), y: gap + r * (tileSize + gap) };
      }
      function place(piece) {
        var s = slotOf(piece), xy = slotXY(s);
        tiles[piece].style.transform = 'translate(' + xy.x + 'px,' + xy.y + 'px)';
      }
      function layout() {
        var sw = stage.clientWidth || 924, sh = stage.clientHeight || 520;
        var panelW = sw < 860 ? 190 : 250;
        boardSize = Math.max(240, Math.min(sh - 8, sw - panelW - 70, 470));
        boardSize = Math.floor(boardSize);
        gap = Math.max(4, Math.round(boardSize * 0.013));
        tileSize = Math.floor((boardSize - gap * (N + 1)) / N);
        boardSize = tileSize * N + gap * (N + 1);
        board.style.width = board.style.height = boardSize + 'px';
        // no fly-in: tiles land instantly on (re)layout, transitions resume after
        for (var q = 0; q < TILES; q++) {
          tiles[q].style.transition = 'none';
          tiles[q].style.width = tiles[q].style.height = tileSize + 'px';
          place(q);
        }
        void board.offsetWidth;
        for (var q2 = 0; q2 < TILES; q2++) tiles[q2].style.transition = '';
      }
      function refreshTells() {
        var ok = 0;
        for (var s = 0; s < TILES; s++) {
          var good = slots[s] === s;
          if (good) ok++;
          tiles[slots[s]].classList.toggle('is-ok', good);
          pipEls[s].classList.toggle('is-ok', good);
        }
        countB.textContent = String(ok);
        return ok;
      }

      applyPicture();
      layout();
      refreshTells();
      try { if (hud.set) hud.set('attempt #' + Math.max(1, (init && init.attempt) | 0) + '  ·  rearrange the picture'); } catch (_e) {}
      try { if (hud.timer) hud.timer(Math.ceil(TIME_LIMIT_MS / 1000)); } catch (_e) {}
      say('put it back together. quickly.');

      var ro = null;
      try { ro = new ResizeObserver(function () { if (!dragging) layout(); }); ro.observe(stage); } catch (_e) {}
      this._off.push(function () { try { ro && ro.disconnect(); } catch (_e) {} });

      // ---- swapping -------------------------------------------------------
      function doSwap(pa, pb, animate) {
        var sa = slotOf(pa), sb = slotOf(pb);
        swapSlots(slots, sa, sb);
        if (animate) { tiles[pa].classList.add('is-hop'); tiles[pb].classList.add('is-hop'); }
        place(pa); place(pb);
        self._after(320, function () { tiles[pa].classList.remove('is-hop'); tiles[pb].classList.remove('is-hop'); });
        refreshTells();
      }
      function playerSwap(pa, pb) {
        if (done || pa === pb) return;
        moves++;
        doSwap(pa, pb, true);
        checkWin();
        maybeGlitch();
      }
      function setSel(piece) {
        if (sel >= 0) tiles[sel].classList.remove('is-sel');
        sel = piece;
        if (sel >= 0) tiles[sel].classList.add('is-sel');
      }

      // ---- pointer handling (mouse + touch) ----------------------------------
      var dragging = null;   // { piece, startX, startY, offX, offY, moved, target }
      function slotAtPoint(clientX, clientY) {
        var r = board.getBoundingClientRect();
        var x = clientX - r.left, y = clientY - r.top;
        if (x < 0 || y < 0 || x > r.width || y > r.height) return -1;
        var scale = r.width / boardSize || 1;
        var c = Math.floor((x / scale) / (tileSize + gap)), rr = Math.floor((y / scale) / (tileSize + gap));
        if (c < 0 || c >= N || rr < 0 || rr >= N) return -1;
        return rr * N + c;
      }
      function onDown(ev) {
        if (done) return;
        var tEl = ev.target && ev.target.closest ? ev.target.closest('.eejig-tile') : null;
        if (!tEl || !board.contains(tEl)) return;
        if (ev.button != null && ev.button !== 0) return;
        ev.preventDefault();
        var piece = parseInt(tEl.getAttribute('data-piece'), 10);
        var r = board.getBoundingClientRect(), xy = slotXY(slotOf(piece));
        dragging = {
          piece: piece, startX: ev.clientX, startY: ev.clientY, moved: false, target: -1,
          offX: ev.clientX - (r.left + xy.x), offY: ev.clientY - (r.top + xy.y), id: ev.pointerId,
        };
        try { tEl.setPointerCapture(ev.pointerId); } catch (_e) {}
      }
      function onMove(ev) {
        if (!dragging || ev.pointerId !== dragging.id) return;
        var dx = ev.clientX - dragging.startX, dy = ev.clientY - dragging.startY;
        if (!dragging.moved && Math.abs(dx) + Math.abs(dy) > TAP_SLOP) {
          dragging.moved = true;
          tiles[dragging.piece].classList.add('is-drag');
          setSel(-1);
        }
        if (!dragging.moved) return;
        var r = board.getBoundingClientRect();
        var x = ev.clientX - r.left - dragging.offX, y = ev.clientY - r.top - dragging.offY;
        tiles[dragging.piece].style.transform = 'translate(' + x + 'px,' + y + 'px)';
        var s = slotAtPoint(ev.clientX, ev.clientY);
        var tp = s >= 0 ? slots[s] : -1;
        if (tp === dragging.piece) tp = -1;
        if (tp !== dragging.target) {
          if (dragging.target >= 0) tiles[dragging.target].classList.remove('is-target');
          dragging.target = tp;
          if (tp >= 0) tiles[tp].classList.add('is-target');
        }
      }
      function onUp(ev) {
        if (!dragging || ev.pointerId !== dragging.id) return;
        var d = dragging; dragging = null;
        var tEl = tiles[d.piece];
        try { tEl.releasePointerCapture(ev.pointerId); } catch (_e) {}
        if (d.target >= 0) tiles[d.target].classList.remove('is-target');
        if (!d.moved) {
          // tap mode
          if (sel < 0) { setSel(d.piece); }
          else if (sel === d.piece) { setSel(-1); }
          else { var a = sel; setSel(-1); playerSwap(a, d.piece); }
          return;
        }
        tEl.classList.remove('is-drag');
        if (d.target >= 0) playerSwap(d.piece, d.target);
        else place(d.piece);   // snap back
      }
      function onCancel(ev) {
        if (!dragging) return;
        var d = dragging; dragging = null;
        tiles[d.piece].classList.remove('is-drag');
        if (d.target >= 0) tiles[d.target].classList.remove('is-target');
        place(d.piece);
      }
      board.addEventListener('pointerdown', onDown);
      board.addEventListener('pointermove', onMove);
      board.addEventListener('pointerup', onUp);
      board.addEventListener('pointercancel', onCancel);
      board.addEventListener('lostpointercapture', function (ev) { if (dragging && ev.pointerId === dragging.id) onCancel(ev); });
      board.addEventListener('contextmenu', function (ev) { ev.preventDefault(); });
      this._off.push(function () {
        board.removeEventListener('pointerdown', onDown);
        board.removeEventListener('pointermove', onMove);
        board.removeEventListener('pointerup', onUp);
        board.removeEventListener('pointercancel', onCancel);
      });

      // ---- the glitch ------------------------------------------------------
      function glitchNow(second) {
        glitches++;
        firstGlitchAt = performance.now();
        var pair = pickGlitchSwap(slots, rng);
        var nextPic = pickOtherIndex(picIdx, gifs.length, rng);
        var burstMs = photosafe ? 900 : 420;
        stat.classList.remove('is-on'); void stat.offsetWidth; stat.classList.add('is-on');
        if (!photosafe) { board.classList.remove('is-jit'); void board.offsetWidth; board.classList.add('is-jit'); }
        else board.classList.add('is-dim');
        if (dragging) onCancel({});
        setSel(-1);
        // swap the picture in the middle of the burst, the tiles right after
        self._after(Math.round(burstMs * 0.45), function () {
          picIdx = nextPic; applyPicture();
          if (photosafe) board.classList.remove('is-dim');
        });
        self._after(Math.round(burstMs * 0.8), function () {
          var pa = slots[pair[0]], pb = slots[pair[1]];
          doSwap(pa, pb, true);
          say(second ? 'did it always look like that?' : 'oops. wrong picture. keep going.');
          fine.textContent = second ? 'it keeps changing. so do you, apparently.' : 'that is not the picture you started with. it is still yours.';
          fine.classList.add('is-warn');
        });
        self._after(burstMs + 40, function () { stat.classList.remove('is-on'); board.classList.remove('is-jit'); });
      }
      function maybeGlitch() {
        if (done) return;
        var now = performance.now();
        var prog = countCorrect(slots) / TILES;
        if (!glitch1) {
          if (prog >= GLITCH_PROGRESS || now - t0 >= GLITCH_AFTER_MS) { glitch1 = true; glitchNow(false); }
          return;
        }
        if (!glitch2 && glitch2Roll && prog >= GLITCH2_PROGRESS && now - firstGlitchAt >= GLITCH2_MIN_GAP_MS && now - t0 < TIME_LIMIT_MS - 8000) {
          glitch2 = true; glitchNow(true);
        }
      }

      // ---- end states ------------------------------------------------------
      function meta() {
        return { moves: moves, glitches: glitches, correct: countCorrect(slots), elapsedMs: Math.round(performance.now() - t0) };
      }
      function checkWin() {
        if (done || !isSolved(slots)) return;
        done = true;
        board.classList.add('is-done');
        try { if (hud.timer) hud.timer(Math.max(0, Math.ceil((TIME_LIMIT_MS - (performance.now() - t0)) / 1000))); } catch (_e) {}
        say('fine. it is a picture again. for now.');
        fine.textContent = 'verified: you remember what it looked like.'; fine.classList.remove('is-warn');
        self._after(700, function () { try { api.finish('completed', meta()); } catch (_e) {} });
      }
      function fail() {
        if (done) return;
        done = true;
        if (dragging) onCancel({});
        setSel(-1);
        say('it slipped out of your hands.');
        fine.textContent = 'time. the picture stays broken.'; fine.classList.add('is-warn');
        // a tile falls off the board
        var wrong = [];
        for (var s = 0; s < TILES; s++) if (slots[s] !== s) wrong.push(slots[s]);
        var piece = wrong.length ? wrong[Math.floor(rng() * wrong.length)] : Math.floor(rng() * TILES);
        var xy = slotXY(slotOf(piece));
        var tEl = tiles[piece];
        tEl.classList.remove('is-ok');
        tEl.classList.add('is-fall');
        var tilt = (rng() < 0.5 ? -1 : 1) * (18 + rng() * 22);
        tEl.style.transform = 'translate(' + (xy.x + tilt * 1.5) + 'px,' + (xy.y + boardSize + 260) + 'px) rotate(' + tilt + 'deg)';
        self._after(1000, function () { try { api.finish('failed', meta()); } catch (_e) {} });
      }

      // ---- clock ------------------------------------------------------------
      var lastShown = -1, glitchTimeChecked = false;
      var clock = setInterval(function () {
        if (!self._alive || done) return;
        var left = TIME_LIMIT_MS - (performance.now() - t0);
        var sec = Math.max(0, Math.ceil(left / 1000));
        if (sec !== lastShown) { lastShown = sec; try { if (hud.timer) hud.timer(sec); } catch (_e) {} }
        if (!glitch1 && !glitchTimeChecked && performance.now() - t0 >= GLITCH_AFTER_MS && !dragging) { glitchTimeChecked = true; maybeGlitch(); }
        if (left <= 0) fail();
      }, 200);
      this._timers.push(clock);

      // dev hook: ?autosolve=1 solves it after 2 s (authoring only)
      try {
        var q = new URLSearchParams(location.search);
        if (q.get('autosolve') === '1') this._after(2000, function () { for (var s = 0; s < TILES; s++) slots[s] = s; for (var p2 = 0; p2 < TILES; p2++) place(p2); refreshTells(); checkWin(); });
        if (q.get('autoglitch') === '1') this._after(600, function () { glitch1 = true; glitchNow(false); });
        if (q.get('autofail') === '1') this._after(800, fail);
      } catch (_e) {}
    },

    _after: function (ms, fn) {
      var self = this;
      var id = setTimeout(function () { if (self._alive) { try { fn(); } catch (_e) {} } }, ms);
      this._timers.push(id);
      return id;
    },

    destroy: function () {
      this._alive = false;
      for (var i = 0; i < this._timers.length; i++) { clearTimeout(this._timers[i]); clearInterval(this._timers[i]); }
      this._timers = [];
      for (var j = 0; j < this._off.length; j++) { try { this._off[j](); } catch (_e) {} }
      this._off = [];
      if (this._root && this._root.parentNode) this._root.parentNode.removeChild(this._root);
      this._root = null;
    },
  };

  // register (the shell may load us before or after shared/shell.js)
  var tries = 0;
  (function reg() {
    if (G.EE && typeof G.EE.registerGame === 'function') { G.EE.registerGame(game); return; }
    if (++tries < 100) setTimeout(reg, 50);
  })();
})();
