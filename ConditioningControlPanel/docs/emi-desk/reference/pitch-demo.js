/* EMI Desk pitch - live stage. Face renderer + chains are the locked ones from
 * emi/face.js + emi/chains.js (numbers verbatim). Everything else is pitch. */
(function () {
  'use strict';
  var PINK = '#FF69B4';
  var FALLBACK = ",'Noto Sans Symbols 2','Segoe UI Symbol','Segoe UI Emoji','MS Gothic',monospace";
  var FLAT_SET = ['._.', '^_^', '^_~', '>.<', '@_@', '-_-', 'o_o', 'T_T', '>_<', '=_=', '¬_¬', '^___^', 'x_x', '*_*', '0_0', ';_;', '(◉_◉)', '(⊙_⊙)', '(◔_◔)'];
  var SIDE_SET = [':)', ':D', ';)', ":'(", '>:(', ':O', ':P', ':|', '<3', 'XD', ':3', '>:)', ':/', 'B)'];
  var KAO_SET = ['( ͡° ͜ʖ ͡°)', '(¬‿¬)', '(◠‿◠)', '(⌐■_■)', '(ಠ‿ಠ)', '(✖╭╮✖)', '(✿◡‿◡)', '(◕‿◕)', '(ಥ_ಥ)', '(｡♥‿♥｡)', '(≧◡≦)'];
  var SPECIAL_SET = ['\\o/', 'GG', '#ERR', 'ZzZ', '!!!', '???', 'LV UP', '♥♥♥', '★★★', '404', 'brb'];
  var SIDE_RE = /^[>]?[:;=8xXB][-'^]?[)(DPOop|\/\\3]$/;
  function isSide(t) { return typeof t === 'string' && SIDE_RE.test(t) && t.length <= 4; }
  function isKao(t) {
    if (KAO_SET.indexOf(t) >= 0) return true;
    return FLAT_SET.indexOf(t) < 0 && SIDE_SET.indexOf(t) < 0 && SPECIAL_SET.indexOf(t) < 0 && /[^\x00-\x7F]/.test(t) && t.replace(/[¬]/g, '').length >= 5;
  }
  function createFace(canvas) {
    var o = { res: 152, font: '"Noto Sans Mono", monospace', thick: 5, fill: 0.95, lift: 2 };
    var ctx = canvas.getContext('2d');
    function size() { var w = 152, h = Math.round(w * 0.903); if (canvas.width !== w) canvas.width = w; if (canvas.height !== h) canvas.height = h; return { w: w, h: h }; }
    function fontStr(fs) { return fs + 'px ' + o.font + FALLBACK; }
    function draw(text, fo) {
      var s = size(), w = s.w, h = s.h; fo = fo || {};
      var t = String(text == null ? '' : text);
      ctx.clearRect(0, 0, w, h); ctx.imageSmoothingEnabled = false;
      if (!t) return;
      var side = !fo.flat && isSide(t), kao = isKao(t), small = !!fo.small;
      var fill = o.fill * (kao ? 1.10 : 1);
      ctx.textBaseline = 'alphabetic'; ctx.textAlign = 'left';
      var boxW = (side ? h : w) * fill, boxH = (side ? w : h) * fill;
      var meas = function (fs) { ctx.font = fontStr(fs); var m = ctx.measureText(t); var l = m.actualBoundingBoxLeft || 0, r = m.actualBoundingBoxRight || m.width, asc = m.actualBoundingBoxAscent || fs * .8, desc = m.actualBoundingBoxDescent || fs * .2; return { l: l, r: r, asc: asc, desc: desc, w: l + r, h: asc + desc }; };
      var fs = Math.max(6, Math.floor(boxH)); if (small) fs = Math.max(6, Math.floor(boxH * .30));
      var m = meas(fs), pad = o.thick, fitW = boxW - pad * 2, fitH = boxH - pad * 2;
      var k = small ? 1 : Math.min(fitW / Math.max(1, m.w), fitH / Math.max(1, m.h), 1);
      if (k < 1) { fs = Math.max(4, Math.floor(fs * k)); m = meas(fs); }
      if (!small && (m.w > fitW || m.h > fitH)) { fs = Math.max(4, Math.floor(fs * Math.min(fitW / m.w, fitH / m.h))); m = meas(fs); }
      var liftPct = small ? -0.28 : (o.lift + (kao ? 10 : 0)) / 100;
      ctx.save(); ctx.translate(w / 2, h / 2); if (side) ctx.rotate(Math.PI / 2);
      var cx = -m.w / 2 + m.l, cy = (m.asc - m.desc) / 2, lift = -liftPct * (side ? w : h);
      ctx.fillStyle = PINK; ctx.strokeStyle = PINK; ctx.lineJoin = 'round'; ctx.lineCap = 'round';
      ctx.lineWidth = o.thick; ctx.strokeText(t, cx, cy + lift); ctx.fillText(t, cx, cy + lift); ctx.restore();
    }
    size(); return { draw: draw, clear: function () { var s = size(); ctx.clearRect(0, 0, s.w, s.h); } };
  }
  var CHAINS = {
    wink: { body: 'idle', seq: [['^_^', 420], ['^_~', 260], ['^_^', 600]] },
    blink: { body: 'idle', seq: [['0_0', 1400], ['-_-', 110], ['0_0', 1200]] },
    shock: { body: 'shock', seq: [['o_o', 160], ['0_0', 160], ['(◉_◉)', 900]], move: 'bounce' },
    sus: { body: 'smug', seq: [['¬_¬', 500], ['-_-', 220], ['¬_¬', 800]] },
    glance: { body: 'idle', seq: [['0_0', 300], ['o_o', 640], ['0_0', 900]] },
    nod: { body: 'idle', seq: [['0_0', 1400]], move: 'nod' },
    glitch: { body: 'shock', seq: [['x_x', 90], ['#ERR', 90], ['@_@', 90], ['#ERR', 90], ['x_x', 90], ['0_0', 600]] },
    love: { body: 'pet', seq: [['0_0', 260], ['*_*', 420], ['(｡♥‿♥｡)', 1400]], move: 'bounce', fx: 'hearts' },
    glee: { body: 'idle', seq: [['^_^', 300], ['(≧◡≦)', 1400]], move: 'bounce', fx: 'hearts' },
    cool: { body: 'idle', seq: [['-_-', 300], ['(⌐■_■)', 1400]], move: 'bounce' },
    smug: { body: 'smug', seq: [['^_^', 300], ['(¬‿¬)', 1200]] },
    cry: { body: 'sad', seq: [[';_;', 500], ['T_T', 1400]], move: 'droop' },
    dizzy: { body: 'idle', seq: [['@_@', 260], ['=_=', 260], ['@_@', 260], ['x_x', 800]] },
    thinking: { body: 'idle', flat: true, seq: [['.', 260, { small: 1 }], ['..', 260, { small: 1 }], ['...', 260, { small: 1 }], ['.', 260, { small: 1 }], ['..', 260, { small: 1 }], ['...', 420, { small: 1 }], ['0_0', 900]] }
  };
  function makeSay(line, face, hold) {
    return { body: 'idle', seq: [['0_0', 420, { bubble: '.' }], ['0_0', 420, { bubble: '..' }], ['0_0', 520, { bubble: '...' }], [face || '^_^', Math.max(3000, 1400 + 45 * line.length, hold || 0), { bubble: line }], ['0_0', 200, { bubble: null }]] };
  }

  /* ---------------- the stage ---------------- */
  var stage = document.getElementById('stage');
  var emi = document.getElementById('emi');
  var body = emi.querySelector('.emi-body');
  var screen = emi.querySelector('.emi-screen');
  var glass = emi.querySelector('.emi-glass');
  var bubble = emi.querySelector('.emi-bubble');
  var fx = emi.querySelector('.emi-fx');
  var strip = emi.querySelector('.emi-strip');
  var ring = document.getElementById('ring');
  var mover = emi.querySelector('.emi-move');
  var log = document.getElementById('stagelog');
  var face = createFace(screen);
  var chainTimer = null, live = false, channel = null, idleTimer = null, askOpen = false, ringOpen = false;
  var gctx = glass.getContext('2d'); glass.width = 152; glass.height = 137;
  var raf = null;

  function say_log(s) { if (!log) return; var li = document.createElement('div'); li.textContent = s; log.prepend(li); while (log.children.length > 6) log.lastChild.remove(); }
  function setBody(pose) { body.src = BODY[pose] || BODY.idle; }
  function setBubble(t) { if (t == null) { bubble.hidden = true; bubble.textContent = ''; return; } bubble.hidden = false; bubble.textContent = t; bubble.classList.toggle('dots', /^\.+$/.test(t)); bubble.classList.remove('pop'); void bubble.offsetWidth; bubble.classList.add('pop'); }
  function move(cls) { mover.classList.remove('bounce', 'nod', 'droop', 'shiver', 'thud'); void mover.offsetWidth; mover.classList.add(cls); setTimeout(function () { mover.classList.remove(cls); }, cls === 'nod' ? 1800 : cls === 'droop' ? 2400 : 400); }
  function hearts() { for (var i = 0; i < 7; i++) { var p = document.createElement('div'); p.className = 'emi-px rise'; p.style.left = (35 + Math.random() * 40) + '%'; p.style.top = (25 + Math.random() * 20) + '%'; p.style.setProperty('--dx', (Math.random() * 60 - 30) + 'px'); p.style.setProperty('--dy', (-40 - Math.random() * 50) + 'px'); p.style.setProperty('--t', (700 + Math.random() * 500) + 'ms'); p.style.background = i % 2 ? PINK : '#FFB0D6'; fx.appendChild(p); setTimeout(function (q) { q.remove(); }, 1300, p); } }
  function cancelChain() { if (chainTimer) { clearTimeout(chainTimer); chainTimer = null; } live = false; }
  function play(chain, done) {
    cancelChain(); killChannel('say'); live = true; setBubble(null);
    if (chain.body) setBody(chain.body); if (chain.move) move(chain.move); if (chain.fx === 'hearts') hearts();
    var i = 0;
    (function step() {
      var f = chain.seq[i]; var o = f[2] || {};
      if ('bubble' in o) setBubble(o.bubble);
      face.draw(f[0], { small: !!o.small, flat: !!chain.flat });
      i++;
      chainTimer = setTimeout(function () { if (i < chain.seq.length) step(); else { live = false; chainTimer = null; setBody('idle'); face.draw('0_0'); if (done) done(); } }, f[1]);
    })();
  }
  function say(line, faceT, done) { play(makeSay(line, faceT), done); }
  function idleBlink() { if (!live && !channel && !askOpen && Math.random() < .5) play(CHAINS.blink); }
  setInterval(idleBlink, 4200);
  face.draw('0_0'); setBody('idle');

  /* ---------------- drag + resize ---------------- */
  var drag = null, moved = false;
  emi.addEventListener('pointerdown', function (e) {
    if (e.target.closest('.emi-chip') || e.target.closest('.emi-resize')) return;
    drag = { x: e.clientX, y: e.clientY, l: emi.offsetLeft, t: emi.offsetTop }; moved = false; emi.setPointerCapture(e.pointerId); emi.classList.add('grabbing');
  });
  emi.addEventListener('pointermove', function (e) {
    if (!drag) return; var dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 6) { moved = true; if (ringOpen) closeRing(); }
    if (!moved) return;
    var sw = stage.clientWidth, sh = stage.clientHeight;
    emi.style.left = Math.max(0, Math.min(sw - emi.offsetWidth, drag.l + dx)) + 'px';
    emi.style.top = Math.max(0, Math.min(sh - emi.offsetHeight, drag.t + dy)) + 'px';
  });
  emi.addEventListener('pointerup', function (e) {
    emi.classList.remove('grabbing');
    if (!drag) return; drag = null;
    if (moved) { say_log('dropped. position remembered per monitor.'); if (Math.random() < .3) play(CHAINS.dizzy); return; }
    if (e.target.closest('.emi-glass') && channel) { fireChannel(); return; }
    if (askOpen) return;
    toggleRing();
  });
  var rs = emi.querySelector('.emi-resize'), rz = null;
  rs.addEventListener('pointerdown', function (e) { rz = { x: e.clientX, w: emi.offsetWidth }; rs.setPointerCapture(e.pointerId); e.stopPropagation(); });
  rs.addEventListener('pointermove', function (e) { if (!rz) return; var w = Math.max(96, Math.min(260, rz.w + (e.clientX - rz.x))); emi.style.width = w + 'px'; emi.style.setProperty('--emi-w', w + 'px'); if (ringOpen) layoutRing(); });
  rs.addEventListener('pointerup', function () { if (rz) { rz = null; say_log('resized. min 96px, max 260px, bubble stays 8px grid.'); } });

  /* ---------------- the ring ---------------- */
  var CARDS = window.RING_CARDS || [];
  var pinned = {}; CARDS.forEach(function (c) { if (c.pinned) pinned[c.id] = true; });
  function layoutRing() {
    var r = Math.max(84, emi.offsetWidth * .62 + 34);
    var cx = emi.offsetLeft + emi.offsetWidth / 2, cy = emi.offsetTop + emi.offsetHeight * .48;
    var sw = stage.clientWidth, sh = stage.clientHeight;
    /* fan AWAY from the nearest edges so the ring never leaves the desktop */
    var start = -90, span = 360; var n = CARDS.length;
    var nearR = cx > sw - r - 70, nearL = cx < r + 70, nearT = cy < r + 40, nearB = cy > sh - r - 40;
    if (nearR && !nearL) { start = 90; span = 180; } else if (nearL && !nearR) { start = -90; span = 180; }
    if (nearB && !nearT && span === 360) { start = 180; span = 180; } else if (nearT && !nearB && span === 360) { start = 0; span = 180; }
    ring.style.left = cx + 'px'; ring.style.top = cy + 'px';
    Array.prototype.forEach.call(ring.children, function (el, i) {
      var a = (start + (span === 360 ? i * (360 / n) : (i + .5) * (span / n))) * Math.PI / 180;
      el.style.setProperty('--x', (Math.cos(a) * r) + 'px'); el.style.setProperty('--y', (Math.sin(a) * r) + 'px');
      el.style.setProperty('--d', (i * 28) + 'ms');
    });
  }
  function buildRing() {
    ring.innerHTML = '';
    CARDS.forEach(function (c) {
      var el = document.createElement('button'); el.className = 'rcard' + (pinned[c.id] ? ' pinned' : '') + (c.locked ? ' locked' : ''); el.type = 'button';
      el.style.setProperty('--hue', c.hue);
      var img = c.img && window.THUMBS && THUMBS[c.img] ? ' style="background-image:url(' + THUMBS[c.img] + ')"' : '';
      el.innerHTML = '<span class="rimg" aria-hidden="true"' + img + '></span><span class="rname">' + c.name + '</span><span class="rpin" title="' + (pinned[c.id] ? 'pinned' : 'suggested from your usage') + '">' + (pinned[c.id] ? '📌' : (c.locked ? '🔒' : '·')) + '</span>';
      el.title = (pinned[c.id] ? 'Pinned. ' : 'Suggested: you open this a lot. ') + 'Click opens ' + c.name + '. Right-click pins.';
      el.addEventListener('click', function (e) { e.stopPropagation(); pick(c); });
      el.addEventListener('contextmenu', function (e) { e.preventDefault(); e.stopPropagation(); pinned[c.id] = !pinned[c.id]; buildRing(); layoutRing(); say_log((pinned[c.id] ? 'pinned ' : 'unpinned ') + c.name + (pinned[c.id] ? ' - it keeps this slot for good.' : ' - the slot goes back to the suggester.')); if (pinned[c.id]) play(CHAINS.nod); });
      ring.appendChild(el);
    });
  }
  function toggleRing() { if (ringOpen) closeRing(); else openRing(); }
  function openRing() { if (channel) killChannel('ring'); buildRing(); layoutRing(); ring.hidden = false; ringOpen = true; emi.classList.add('ring-open'); play(CHAINS.glance); say_log('ring open. 6 slots: ' + CARDS.filter(function (c) { return pinned[c.id]; }).length + ' pinned, rest suggested from usage.'); }
  function closeRing() { ring.hidden = true; ringOpen = false; emi.classList.remove('ring-open'); }
  function pick(c) { closeRing(); say_log('opens ' + c.name + (c.gate ? ' (' + c.gate + ')' : '') + ' directly - ShowTab / host launch, no menu hunting.'); var lines = window.PICK_LINES || {}; var l = lines[c.id] || null; if (l) say(l.t, l.face); else play(CHAINS.wink); flashStage(c.name, c.hue); }
  document.addEventListener('pointerdown', function (e) { if (ringOpen && !e.target.closest('#emi') && !e.target.closest('#ring')) closeRing(); });

  /* ---------------- the glass channels ---------------- */
  var CHANNELS = {
    spiral: { label: 'a spiral', fire: 'Spiral overlay on the real screen, 6s', paint: function (t) { var c = gctx, w = 152, h = 137; c.clearRect(0, 0, w, h); c.fillStyle = '#0E0E1C'; c.fillRect(0, 0, w, h); c.strokeStyle = PINK; c.lineWidth = 5; c.lineCap = 'round'; c.beginPath(); for (var a = 0; a < 40; a += .15) { var r = a * 2.1, x = w / 2 + Math.cos(a + t / 400) * r, y = h / 2 + Math.sin(a + t / 400) * r; if (a === 0) c.moveTo(x, y); else c.lineTo(x, y); } c.stroke(); } },
    video: { label: 'a video', fire: 'Starts the mandatory video; EMI rides on top and comments', paint: function (t) { var c = gctx, w = 152, h = 137; c.clearRect(0, 0, w, h); var g = c.createLinearGradient(0, 0, w, h); g.addColorStop(0, '#3A2450'); g.addColorStop(1, '#8B2C6A'); c.fillStyle = g; c.fillRect(0, 0, w, h); for (var i = 0; i < 6; i++) { c.fillStyle = 'rgba(255,255,255,' + (0.04 + 0.03 * Math.sin(t / 300 + i)) + ')'; c.fillRect(0, i * 24 + (t / 40 % 24), w, 6); } c.fillStyle = 'rgba(0,0,0,.55)'; c.beginPath(); c.arc(w / 2, h / 2, 26, 0, 7); c.fill(); c.fillStyle = PINK; c.beginPath(); c.moveTo(w / 2 - 9, h / 2 - 14); c.lineTo(w / 2 + 15, h / 2); c.lineTo(w / 2 - 9, h / 2 + 14); c.fill(); c.fillStyle = '#F5F0E1'; c.font = 'bold 14px "Noto Sans Mono", monospace'; c.fillText('02:14', w - 48, h - 8); } },
    burst: { label: 'gifs', fire: 'A gif burst across the screen, like the Flashes tab does', paint: function (t) { var c = gctx, w = 152, h = 137; c.clearRect(0, 0, w, h); c.fillStyle = '#0E0E1C'; c.fillRect(0, 0, w, h); var cols = ['#FF69B4', '#B980FF', '#7FE3FF', '#FFC65C', '#FF8FA3', '#8CF5C8']; for (var i = 0; i < 9; i++) { var ph = (t / 900 + i * .37) % 1, s = 10 + ph * 46, x = (i % 3) * 50 + 26 - s / 2, y = Math.floor(i / 3) * 44 + 24 - s / 2; c.globalAlpha = 1 - ph; c.fillStyle = cols[i % cols.length]; c.fillRect(x, y, s, s * .8); } c.globalAlpha = 1; } },
    rain: { label: 'rain', fire: 'Gif rain on the desktop', paint: function (t) { var c = gctx, w = 152, h = 137; c.clearRect(0, 0, w, h); c.fillStyle = '#0E0E1C'; c.fillRect(0, 0, w, h); for (var i = 0; i < 18; i++) { var x = (i * 37) % w, y = ((t / (5 + i % 4)) + i * 41) % (h + 30) - 20; c.fillStyle = i % 3 ? PINK : '#F5F0E1'; c.fillRect(x, y, 6, 14 + (i % 3) * 6); } } }
  };
  var chanT0 = 0;
  function showChannel(id) {
    if (live || askOpen || ringOpen || !out || transit) return;
    killChannel('swap'); channel = id; emi.classList.add('glitching'); glass.hidden = false; setBody('shock');
    var g = 0; var gl = setInterval(function () { face.draw(['x_x', '#ERR', '@_@'][g++ % 3]); }, 90);
    setTimeout(function () { clearInterval(gl); emi.classList.remove('glitching'); setBody('idle'); face.draw('0_0'); }, 460);
    chanT0 = performance.now();
    (function loop() { if (channel !== id) return; CHANNELS[id].paint(performance.now() - chanT0); raf = requestAnimationFrame(loop); })();
    emi.classList.add('chan'); say_log('glass: ' + CHANNELS[id].label + '. click the screen to fire it; any other click dismisses.');
    idleTimer = setTimeout(function () { if (channel === id) { killChannel('timeout'); say_log('nobody clicked. blip off, no line, she does not push.'); } }, 10000);
  }
  function killChannel(why) { if (!channel) return; channel = null; if (raf) cancelAnimationFrame(raf); clearTimeout(idleTimer); glass.hidden = true; emi.classList.remove('chan'); gctx.clearRect(0, 0, 152, 137); }
  function fireChannel() {
    var id = channel; killChannel('fire');
    var fires = window.FIRE_LINES || {};
    if (id === 'spiral') { spiralOverlay(); play(CHAINS.dizzy); }
    else if (id === 'video') { openVideo(); var l = fires.video; if (l) setTimeout(function () { say(l.t, l.face); }, 900); }
    else if (id === 'burst') { burstOverlay(); play(CHAINS.shock); }
    else if (id === 'rain') { rainOverlay(); play(CHAINS.glee); }
    say_log('fired: ' + CHANNELS[id].fire + '.');
  }
  var ov = document.getElementById('overlay');
  function spiralOverlay() { ov.className = 'ov spiral'; ov.hidden = false; var c = ov.querySelector('canvas'), x = c.getContext('2d'), t0 = performance.now(); c.width = stage.clientWidth; c.height = stage.clientHeight; (function f() { if (ov.hidden || !ov.classList.contains('spiral')) return; var t = performance.now() - t0; x.clearRect(0, 0, c.width, c.height); x.strokeStyle = 'rgba(255,105,180,.85)'; x.lineWidth = 6; x.beginPath(); for (var a = 0; a < 90; a += .08) { var r = a * 7, px = c.width / 2 + Math.cos(a - t / 350) * r, py = c.height / 2 + Math.sin(a - t / 350) * r; if (a === 0) x.moveTo(px, py); else x.lineTo(px, py); } x.stroke(); if (t < 4000) requestAnimationFrame(f); else ov.hidden = true; })(); }
  function burstOverlay() { ov.className = 'ov burst'; ov.hidden = false; var w = ov.querySelector('.tiles'); w.innerHTML = ''; var cols = ['#FF69B4', '#B980FF', '#7FE3FF', '#FFC65C', '#FF8FA3', '#8CF5C8']; for (var i = 0; i < 14; i++) { var d = document.createElement('div'); d.style.left = (5 + Math.random() * 85) + '%'; d.style.top = (5 + Math.random() * 80) + '%'; d.style.background = cols[i % 6]; d.style.animationDelay = (i * 60) + 'ms'; d.style.width = (40 + Math.random() * 70) + 'px'; d.style.height = (30 + Math.random() * 60) + 'px'; w.appendChild(d); } setTimeout(function () { ov.hidden = true; }, 2600); }
  function rainOverlay() { ov.className = 'ov rain'; ov.hidden = false; var w = ov.querySelector('.tiles'); w.innerHTML = ''; for (var i = 0; i < 22; i++) { var d = document.createElement('div'); d.style.left = (Math.random() * 96) + '%'; d.style.animationDelay = (Math.random() * 1200) + 'ms'; d.style.animationDuration = (1400 + Math.random() * 1200) + 'ms'; d.style.width = (22 + Math.random() * 30) + 'px'; d.style.height = (22 + Math.random() * 30) + 'px'; d.style.background = i % 2 ? '#FF69B4' : '#B980FF'; w.appendChild(d); } setTimeout(function () { ov.hidden = true; }, 3600); }
  function openVideo() { var v = document.getElementById('mockvideo'); v.hidden = false; setTimeout(function () { v.hidden = true; }, 6500); }
  function flashStage(name, hue) { var w = document.getElementById('win'); w.style.setProperty('--hue', hue); w.querySelector('.wt').textContent = name; w.classList.remove('flash'); void w.offsetWidth; w.classList.add('flash'); }

  /* ---------------- offers (asks) ---------------- */
  function offer(a) {
    if (live || askOpen) return; if (ringOpen) closeRing(); killChannel('ask');
    askOpen = true;
    play(makeSay(a.q, a.face || '0_0', 40000), null);
    setTimeout(function () {
      strip.innerHTML = ''; a.chips.forEach(function (label, i) { var b = document.createElement('button'); b.className = 'emi-chip'; b.type = 'button'; b.textContent = label; b.addEventListener('click', function (e) { e.stopPropagation(); answer(a, i === 0); }); strip.appendChild(b); });
      strip.hidden = false; say_log('offer: "' + a.q + '" chips ' + a.chips.join(' / ') + '. 40s, then she gives up wordlessly.');
    }, 1360);
    clearTimeout(idleTimer); idleTimer = setTimeout(function () { if (askOpen) ignored(); }, 40000 + 1360);
  }
  function endAsk() { askOpen = false; strip.hidden = true; strip.innerHTML = ''; cancelChain(); setBubble(null); }
  function ignored() { endAsk(); face.draw('-_-'); setTimeout(function () { setBubble('...'); setTimeout(function () { setBubble(null); face.draw('0_0'); }, 900); }, 1400); say_log('ignored. -_- then ... then idle. no line, ever.'); }
  function answer(a, yes) {
    endAsk(); var r = yes ? a.yes : a.no;
    say_log((yes ? 'yes' : 'no') + ' -> ' + (yes ? a.effect : 'nothing happens, and it costs nothing.'));
    if (r.chain) { play(CHAINS[r.chain], function () { say(r.t, r.face); }); } else say(r.t, r.face);
    if (yes && a.fire) setTimeout(function () { if (a.fire === 'spiral') spiralOverlay(); if (a.fire === 'rain') rainOverlay(); if (a.fire === 'burst') burstOverlay(); if (a.fire === 'video') openVideo(); }, 1500);
  }

  /* ---------------- summon / dismiss ---------------- */
  var dock = document.getElementById('dock'), dockFace = createFace(dock.querySelector('canvas')), mutePill = document.getElementById('mute');
  var out = true, transit = false, petted = false;
  dockFace.draw('0_0');
  setInterval(function () { if (transit) return; if (!out) { dockFace.draw('0_0'); setTimeout(function () { if (!out) dockFace.draw('-_-'); }, 1400); setTimeout(function () { if (!out) dockFace.draw('0_0'); }, 1510); } else dockFace.draw(mutePill.hidden ? '^_^' : '-_-'); }, 3000);
  function smoke(x, y, kind) {
    var s = document.createElement('div'); s.className = 'smoke'; s.style.left = x + 'px'; s.style.top = y + 'px';
    var n = kind === 'spark' ? 14 : 22;
    for (var i = 0; i < n; i++) { var p = document.createElement('i'); var a = Math.random() * 6.28, r = (kind === 'spark' ? 30 : 22) + Math.random() * 40; p.className = kind === 'spark' ? (petted && i % 3 === 0 ? 'p' : 's') : (i % 4 === 0 ? 'p' : ''); p.style.left = (Math.random() * 30 - 15) + 'px'; p.style.top = (Math.random() * 30 - 15) + 'px'; p.style.setProperty('--dx', (Math.cos(a) * r) + 'px'); p.style.setProperty('--dy', (Math.sin(a) * r - (kind === 'spark' ? 10 : 20)) + 'px'); p.style.setProperty('--t', (380 + Math.random() * 320) + 'ms'); s.appendChild(p); }
    stage.appendChild(s); setTimeout(function () { s.remove(); }, 900);
  }
  function centre() { return { x: emi.offsetLeft + emi.offsetWidth / 2, y: emi.offsetTop + emi.offsetHeight * .55 }; }
  function summon() {
    if (out || transit) return; transit = true; dock.classList.remove('out');
    var c = centre(); smoke(c.x, c.y, 'smoke'); say_log('summon: smoke bomb (400ms), CRT power-on (200ms), wake chain. ~1.1s.');
    setTimeout(function () {
      emi.classList.remove('gone'); emi.classList.add('crt-on'); setBody('idle'); face.draw('-_-');
      setTimeout(function () { emi.classList.remove('crt-on'); out = true; transit = false; mutePill.hidden = false; play(CHAINS.wake); }, 240);
    }, 380);
  }
  CHAINS.wake = { body: 'shock', seq: [['-_-', 500], ['o_o', 220], ['0_0', 260], ['(⊙_⊙)', 700]] };
  function dismiss() {
    if (!out || transit) return; transit = true;
    endAsk(); killChannel('dismiss'); closeRing(); cancelChain();
    say_log('dismiss: wink, CRT power-off (200ms), sparkles' + (petted ? ' (hearts: she was petted)' : '') + '. ~1s. Avatar unmuted after its 20s tail.');
    play(CHAINS.wink, function () {
      emi.classList.add('crt-off');
      setTimeout(function () { var c = centre(); smoke(c.x, c.y, 'spark'); emi.classList.remove('crt-off'); emi.classList.add('gone'); out = false; transit = false; mutePill.hidden = true; dock.classList.add('out'); dockFace.draw('0_0'); }, 230);
    });
  }
  function toggleEmi() { if (out) dismiss(); else summon(); }
  dock.addEventListener('click', toggleEmi);
  emi.querySelector('.emi-x').addEventListener('pointerdown', function (e) { e.stopPropagation(); });
  emi.querySelector('.emi-x').addEventListener('click', function (e) { e.stopPropagation(); dismiss(); });
  document.addEventListener('keydown', function (e) { if (e.ctrlKey && e.altKey && (e.key === 'e' || e.key === 'E')) { e.preventDefault(); toggleEmi(); say_log('hotkey Ctrl+Alt+E.'); } });
  mutePill.hidden = false;

  /* ---------------- controls ---------------- */
  var ASKS = window.DEMO_ASKS || [];
  var askI = 0;
  document.querySelectorAll('[data-act]').forEach(function (b) {
    b.addEventListener('click', function () {
      var a = b.getAttribute('data-act');
      if (a === 'toggle') { toggleEmi(); return; }
      if (!out && a !== 'reset') { summon(); return; }
      if (a === 'ring') toggleRing();
      else if (a.indexOf('chan:') === 0) showChannel(a.slice(5));
      else if (a === 'offer') { offer(ASKS[askI++ % ASKS.length]); }
      else if (a === 'pet') { petted = true; play(CHAINS.love); say_log('pet. positive cycle, same as the campus.'); }
      else if (a === 'dork') { var d = window.DORK_LINE; if (d) play(CHAINS.cool, function () { say(d.t, d.face); }); }
      else if (a === 'reset') { endAsk(); killChannel('reset'); closeRing(); cancelChain(); setBody('idle'); face.draw('0_0'); emi.style.width = '160px'; emi.style.left = ''; emi.style.top = ''; ov.hidden = true; document.getElementById('mockvideo').hidden = true; }
    });
  });
  /* an ambient channel every so often while the reader is idle on the stage */
  var ambient = ['spiral', 'video', 'burst', 'rain'], ai = 0;
  setInterval(function () { if (!document.hidden && !live && !channel && !askOpen && !ringOpen && stageVisible()) showChannel(ambient[ai++ % ambient.length]); }, 14000);
  function stageVisible() { var r = stage.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight; }
  setTimeout(function () { if (!channel && !live) showChannel('spiral'); }, 2600);
})();
