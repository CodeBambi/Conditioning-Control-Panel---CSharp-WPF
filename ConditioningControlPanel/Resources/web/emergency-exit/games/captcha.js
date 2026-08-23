/* ============================================================================
 * games/captcha.js - "Confirm you are NOT a {honorific}" (Emergency Exit)
 *
 * A press-and-hold verify box (~4 s) under a shaming-but-playful headline about
 * leaving. An ember ring fills while held; releasing or leaving the box makes
 * progress decay at 2x speed. While holding, fake in-page popups advertising
 * CCP features (games/captcha-ads.js) spawn over the box and STEAL the hold:
 * the player must close them (the close X dodges the cursor once; "Try it" just
 * closes). GATE: until 3-4 popups have been closed the ring caps at 92% and
 * another popup spawns. After the gate the hold can complete.
 *
 * Hold fills  -> api.finish("completed", meta)
 * 90 s timer  -> api.finish("failed", meta)   (the verification expired)
 *
 * Shell contract: EMERGENCY_EXIT.md "Shell API". Plain script: calls
 * EE.registerGame at load, self-loads games/captcha-ads.js. Pure gate math is
 * exposed as EE_CAPTCHA_LOGIC for the Node smoke test (ee-logic-tests.js in the authoring scratchpad).
 * ==========================================================================*/
(function () {
  'use strict';

  var HOLD_MS = 4000;            // full ring from empty while held
  var DECAY_MULT = 2;            // decay speed vs fill speed while not held
  var CAP = 0.92;                // the ring stalls here until the gate clears
  var GATE_MIN = 3, GATE_MAX = 4;// popups to close before the hold can complete
  var TIME_LIMIT_MS = 90000;
  var LEAVE_SLOP = 10;           // px outside the box still counts as inside

  /* ---------------------------------------------------------------------------
   * Pure logic (no DOM) - testable.
   * ------------------------------------------------------------------------ */
  function gateNeed(rng) { return GATE_MIN + (rng() < 0.5 ? 1 : 0) * (GATE_MAX - GATE_MIN); }

  /* Progress threshold (0..1) at which the k-th popup (0-based) spawns during a
   * hold. Early popups come early so the decay cycle stays cheap; the last one
   * is the cap itself. */
  function nextThreshold(k, rng) {
    var t = 0.30 + k * 0.17 + rng() * 0.10;
    return Math.min(CAP, t);
  }

  /* One simulation step. state: { p, closed, gate, popupOpen }. held: bool.
   * Returns the event that happened: null | 'spawn' | 'complete'. Mutates p. */
  function step(state, dtMs, held, threshold) {
    var gated = state.closed < state.gate;
    if (held && !state.popupOpen) state.p += dtMs / HOLD_MS;
    else state.p -= DECAY_MULT * dtMs / HOLD_MS;
    if (state.p < 0) state.p = 0;
    if (gated) {
      var capped = state.p >= CAP;
      if (capped) state.p = CAP;
      if (held && !state.popupOpen && (capped || state.p >= threshold)) return 'spawn';
      return null;
    }
    if (state.p >= 1) { state.p = 1; return 'complete'; }
    return null;
  }

  var LOGIC = { HOLD_MS: HOLD_MS, DECAY_MULT: DECAY_MULT, CAP: CAP, GATE_MIN: GATE_MIN, GATE_MAX: GATE_MAX,
    gateNeed: gateNeed, nextThreshold: nextThreshold, step: step };
  var G = (typeof window !== 'undefined') ? window : globalThis;
  G.EE_CAPTCHA_LOGIC = LOGIC;
  if (typeof module !== 'undefined' && module.exports) module.exports = LOGIC;
  if (typeof document === 'undefined') return;   // Node test import stops here

  /* ---------------------------------------------------------------------------
   * Copy. {honorific} / {subject} substituted from init.mod. No em-dashes.
   * Tease the ACT of leaving, never the person.
   * ------------------------------------------------------------------------ */
  var SUBCOPY = [
    'Only a {honorific} would be leaving this early. Prove it is not you. Press and hold the box.',
    'The door only opens for people who are NOT a {honorific}. Hold the box and swear it.',
    '{subject} does not quit. So this should be easy for someone who is definitely not {subject}. Hold.',
    'Quitting is such a {honorific} move. Hold the box and tell it otherwise. With a straight face.',
    'This check is for leavers. A {honorific} would let go halfway. You will not. Will you.',
    'Verification required before the exit unlocks. Hold still. Do not think about staying.',
  ];
  var HOLD_LINES = [          // while holding, by progress rung
    'hold still.',
    'keep holding. the box is not convinced.',
    'almost. a {honorific} would let go about now.',
    'nearly there. do not blink.',
  ];
  var STEAL_LINES = [
    'oh look, an offer. how inconvenient.',
    'a word from our sponsor. the sponsor is the lockdown.',
    'interrupted. close it to keep going.',
    'that popup stole your hold. rude of it.',
  ];
  var CLOSED_LINES = [
    'declined. hold again.',
    'closed. the box forgot where you were.',
    'no thank you, apparently. hold.',
    'rejected. it will be back. hold.',
  ];
  var CAP_LINES = [
    'the ring is stuck at 92%. the offers have not finished.',
    '92%. funny. the box wants you to see more first.',
  ];

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
    for (var i = 0; i < links.length; i++) if (/games\/captcha\.css/i.test(links[i].getAttribute('href') || '')) return;
    var l = document.createElement('link'); l.rel = 'stylesheet'; l.href = 'games/captcha.css';
    document.head.appendChild(l);
  }
  function loadAds(cb) {
    if (G.EE_CAPTCHA_ADS) { cb(G.EE_CAPTCHA_ADS); return; }
    var done = false;
    var finish = function () { if (done) return; done = true; cb(G.EE_CAPTCHA_ADS || fallbackAds()); };
    var s = document.createElement('script');
    s.src = 'games/captcha-ads.js'; s.onload = finish; s.onerror = finish;
    document.head.appendChild(s);
    setTimeout(finish, 2500);
  }
  function fallbackAds() {
    return { ads: [
      { id: 'flash', name: 'Flash', tagline: 'Blink and you will miss it.', body: 'Pictures from your own folder, faster than a second thought.', cta: 'Flash me' },
      { id: 'companion', name: 'Companion', tagline: 'She talks. She remembers.', body: 'Your AI girl with a voice and a long memory. She will remember this.', cta: 'Say hi' },
      { id: 'lockcard', name: 'Lock Card', tagline: 'Type the phrase. Again.', body: 'The screen locks and your fingers learn the phrase for you.', cta: 'Lock me' },
    ], titles: ['CCP SHOPPING CHANNEL'], ticker: ['operators are standing by'] };
  }
  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  function pickNoRepeat(arr, rng, used) {
    if (!arr.length) return null;
    if (used.length >= arr.length) used.length = 0;
    var cands = [];
    for (var i = 0; i < arr.length; i++) if (used.indexOf(i) < 0) cands.push(i);
    var idx = cands[Math.floor(rng() * cands.length)];
    used.push(idx);
    return arr[idx];
  }

  /* ---------------------------------------------------------------------------
   * The game
   * ------------------------------------------------------------------------ */
  var game = {
    id: 'captcha',
    _root: null, _timers: [], _raf: 0, _alive: false, _pops: [],

    start: function (init, api) {
      var self = this;
      this._alive = true;
      ensureCss();
      var rng = (api && typeof api.rng === 'function') ? api.rng : Math.random;
      var photosafe = !!(api && api.photosafe) || !!(init && init.photosafe);
      var mod = (api && api.mod) || (init && init.mod) || {};
      var hon = mod.honorific || 'good girl';
      var subj = mod.subject || mod.name || 'you';
      var fill = function (t) { return String(t == null ? '' : t).replace(/\{honorific\}/g, hon).replace(/\{subject\}/g, subj); };
      var say = function (t) { try { if (api && api.say) api.say(t); } catch (_e) {} };
      var hud = (api && api.hud) || {};
      var stage = (api && (api.mount || api.root || api.stage)) || document.querySelector('.ee-stage') || document.body;

      // ---- DOM ------------------------------------------------------------
      var root = el('div', 'eecap'); this._root = root;
      var card = el('div', 'eecap-card ee-card');
      var head = el('div', 'eecap-head');
      head.appendChild(el('span', null, 'exit verification'));
      head.appendChild(el('span', null, 'step 1 of 1 (allegedly)'));
      var title = el('h1', 'eecap-title');
      title.appendChild(document.createTextNode('Confirm you are '));
      title.appendChild(el('em', null, 'NOT'));
      title.appendChild(document.createTextNode(' a ' + hon));
      var sub = el('p', 'eecap-sub', fill(SUBCOPY[Math.floor(rng() * SUBCOPY.length)]));
      var row = el('div', 'eecap-row');
      var box = el('div', 'eecap-box');
      box.setAttribute('role', 'button'); box.setAttribute('aria-label', 'press and hold to verify'); box.tabIndex = 0;
      var svgNS = 'http://www.w3.org/2000/svg';
      var svg = document.createElementNS(svgNS, 'svg'); svg.setAttribute('class', 'eecap-ring'); svg.setAttribute('viewBox', '0 0 92 92');
      var R = 40, C = 2 * Math.PI * R;
      var track = document.createElementNS(svgNS, 'circle'); track.setAttribute('class', 'track'); track.setAttribute('cx', '46'); track.setAttribute('cy', '46'); track.setAttribute('r', String(R));
      var ring = document.createElementNS(svgNS, 'circle'); ring.setAttribute('class', 'fill'); ring.setAttribute('cx', '46'); ring.setAttribute('cy', '46'); ring.setAttribute('r', String(R));
      ring.setAttribute('stroke-dasharray', C.toFixed(2)); ring.setAttribute('stroke-dashoffset', C.toFixed(2));
      svg.appendChild(track); svg.appendChild(ring);
      var glyph = el('div', 'eecap-glyph', '●');
      box.appendChild(svg); box.appendChild(glyph);
      var lbl = el('div', 'eecap-lbl');
      var lblMain = el('div', 'eecap-lbl-main');
      lblMain.appendChild(document.createTextNode('I am '));
      lblMain.appendChild(el('em', null, 'NOT'));
      lblMain.appendChild(document.createTextNode(' a ' + hon));
      var lblHint = el('div', 'eecap-lbl-hint', 'press and hold to verify');
      var status = el('div', 'eecap-status');
      var pct = el('b', null, '0%');
      var statusText = el('span', null, 'waiting for your hand.');
      status.appendChild(pct); status.appendChild(statusText);
      lbl.appendChild(lblMain); lbl.appendChild(lblHint); lbl.appendChild(status);
      row.appendChild(box); row.appendChild(lbl);
      var foot = el('div', 'eecap-foot');
      var seal = el('span', 'eecap-seal', 'CCP exit desk');
      var declined = el('span'); var declinedB = el('b', null, '0');
      declined.appendChild(document.createTextNode('offers declined: ')); declined.appendChild(declinedB);
      foot.appendChild(seal); foot.appendChild(declined);
      card.appendChild(head); card.appendChild(title); card.appendChild(sub); card.appendChild(row); card.appendChild(foot);
      root.appendChild(card);
      stage.appendChild(root);

      try { if (hud.set) hud.set('attempt #' + Math.max(1, (init && init.attempt) | 0) + '  ·  confirm you are not a ' + hon); } catch (_e) {}
      try { if (hud.timer) hud.timer(Math.ceil(TIME_LIMIT_MS / 1000)); } catch (_e) {}
      say('press and hold. do not let go. do not read the ads.');

      // ---- state ----------------------------------------------------------
      var state = { p: 0, closed: 0, gate: gateNeed(rng), popupOpen: false };
      var held = false, pointerId = null, done = false;
      var spawned = 0, threshold = nextThreshold(0, rng);
      var t0 = performance.now(), last = t0, lastRung = -1, lastPct = -1;
      var usedAds = [], usedSteal = [], usedClosed = [];
      var ads = fallbackAds();
      loadAds(function (a) { if (a && a.ads && a.ads.length) ads = a; });

      function setStatus(text, warn) {
        statusText.textContent = fill(text);
        status.classList.toggle('is-warn', !!warn);
      }
      function setRing(p) {
        ring.setAttribute('stroke-dashoffset', (C * (1 - p)).toFixed(2));
        var n = Math.round(p * 100);
        if (n !== lastPct) { lastPct = n; pct.textContent = n + '%'; }
      }

      // ---- pointer: press and hold ----------------------------------------
      function insideBox(x, y) {
        var r = box.getBoundingClientRect();
        return x >= r.left - LEAVE_SLOP && x <= r.right + LEAVE_SLOP && y >= r.top - LEAVE_SLOP && y <= r.bottom + LEAVE_SLOP;
      }
      function onDown(ev) {
        if (done) return;
        if (ev.button != null && ev.button !== 0) return;
        ev.preventDefault();
        if (state.popupOpen) { setStatus('close the offer first.', true); return; }
        held = true; pointerId = ev.pointerId;
        box.classList.add('is-held');
        try { box.setPointerCapture(ev.pointerId); } catch (_e) {}
      }
      function onMove(ev) {
        if (!held || ev.pointerId !== pointerId) return;
        var inside = insideBox(ev.clientX, ev.clientY);
        if (!inside) { release('you left the box. it noticed.'); }
      }
      function onUp(ev) {
        if (!held || (pointerId != null && ev && ev.pointerId != null && ev.pointerId !== pointerId)) return;
        release('released. it drains twice as fast. hold.');
      }
      function release(msg) {
        if (!held) return;
        held = false; lastRung = -1;
        box.classList.remove('is-held');
        try { if (pointerId != null) box.releasePointerCapture(pointerId); } catch (_e) {}
        pointerId = null;
        if (!done && msg && state.p > 0.02) setStatus(msg, true);
      }
      box.addEventListener('pointerdown', onDown);
      box.addEventListener('pointermove', onMove);
      box.addEventListener('pointerup', onUp);
      box.addEventListener('pointercancel', onUp);
      box.addEventListener('lostpointercapture', function () { if (held) release(); });
      box.addEventListener('contextmenu', function (ev) { ev.preventDefault(); });
      // keyboard: space/enter hold
      var keyHeld = false;
      function onKeyDown(ev) {
        if (done || (ev.key !== ' ' && ev.key !== 'Enter')) return;
        if (document.activeElement !== box) return;
        ev.preventDefault();
        if (state.popupOpen || keyHeld) return;
        keyHeld = true; held = true; box.classList.add('is-held');
      }
      function onKeyUp(ev) {
        if (ev.key !== ' ' && ev.key !== 'Enter') return;
        if (keyHeld) { keyHeld = false; release('released. hold.'); }
      }
      document.addEventListener('keydown', onKeyDown);
      document.addEventListener('keyup', onKeyUp);
      this._off = [function () {
        document.removeEventListener('keydown', onKeyDown);
        document.removeEventListener('keyup', onKeyUp);
      }];

      // ---- popups -----------------------------------------------------------
      function spawnPopup() {
        spawned++;
        state.popupOpen = true;
        release();
        keyHeld = false;
        box.classList.add('is-blocked');
        var ad = pickNoRepeat(ads.ads, rng, usedAds) || fallbackAds().ads[0];
        var pop = el('div', 'eecap-pop');
        var bar = el('div', 'eecap-pop-bar', (ads.titles && ads.titles.length) ? ads.titles[Math.floor(rng() * ads.titles.length)] : 'CCP SHOPPING CHANNEL');
        var x = el('button', 'eecap-pop-x', '×'); x.type = 'button'; x.setAttribute('aria-label', 'close');
        bar.appendChild(x);
        var body = el('div', 'eecap-pop-body');
        body.appendChild(el('div', 'eecap-pop-kicker', 'you could be using'));
        body.appendChild(el('h3', 'eecap-pop-name', ad.name));
        body.appendChild(el('p', 'eecap-pop-tag', fill(ad.tagline)));
        body.appendChild(el('p', 'eecap-pop-copy', fill(ad.body)));
        var cta = el('button', 'eecap-pop-cta', fill(ad.cta || 'Try it')); cta.type = 'button';
        body.appendChild(cta);
        var ticker = el('div', 'eecap-pop-ticker');
        var tick = el('span', null, (ads.ticker && ads.ticker.length) ? ads.ticker[Math.floor(rng() * ads.ticker.length)] : 'operators are standing by');
        ticker.appendChild(tick);
        pop.appendChild(bar); pop.appendChild(body); pop.appendChild(ticker);
        stage.appendChild(pop);
        self._pops.push(pop);

        // place it over / near the box, clamped to the stage
        var sr = stage.getBoundingClientRect(), br = box.getBoundingClientRect();
        var pw = pop.offsetWidth || 330, ph = pop.offsetHeight || 240;
        var cx = br.left + br.width / 2 - sr.left, cy = br.top + br.height / 2 - sr.top;
        var left = cx - pw * 0.5 + (rng() - 0.5) * 160;
        var top = cy - ph * 0.45 + (rng() - 0.5) * 120;
        left = Math.max(6, Math.min(sr.width - pw - 6, left));
        top = Math.max(6, Math.min(sr.height - ph - 6, top));
        pop.style.left = Math.round(left) + 'px'; pop.style.top = Math.round(top) + 'px';

        // the X dodges once
        var dodged = false, dodgedAt = 0;
        function dodge() {
          dodged = true; dodgedAt = performance.now();
          var bw = bar.clientWidth, xw = x.offsetWidth;
          x.style.transform = 'translateX(' + (-(bw - xw - 14)) + 'px)';
          pop.classList.remove('is-shake'); void pop.offsetWidth; pop.classList.add('is-shake');
          setStatus('the X moved. they do that.', true);
        }
        x.addEventListener('pointerenter', function () { if (!dodged) dodge(); });
        x.addEventListener('pointerdown', function (ev) {
          ev.stopPropagation(); ev.preventDefault();
          if (!dodged) { dodge(); return; }
          if (performance.now() - dodgedAt < 250) return;   // touch: enter+down of the same tap
          closePopup(pop);
        });
        x.addEventListener('click', function (ev) { ev.stopPropagation(); if (dodged && performance.now() - dodgedAt >= 250 && pop.parentNode) closePopup(pop); });
        cta.addEventListener('pointerdown', function (ev) { ev.stopPropagation(); });
        cta.addEventListener('click', function (ev) { ev.stopPropagation(); closePopup(pop); });

        // draggable by the title bar
        var drag = null;
        bar.addEventListener('pointerdown', function (ev) {
          if (ev.target === x) return;
          ev.preventDefault();
          drag = { id: ev.pointerId, dx: ev.clientX - pop.offsetLeft, dy: ev.clientY - pop.offsetTop };
          bar.classList.add('is-dragging');
          try { bar.setPointerCapture(ev.pointerId); } catch (_e) {}
        });
        bar.addEventListener('pointermove', function (ev) {
          if (!drag || ev.pointerId !== drag.id) return;
          var sr2 = stage.getBoundingClientRect();
          var l = Math.max(-pw * 0.5, Math.min(sr2.width - pw * 0.5, ev.clientX - drag.dx));
          var t = Math.max(0, Math.min(sr2.height - 40, ev.clientY - drag.dy));
          pop.style.left = l + 'px'; pop.style.top = t + 'px';
        });
        var endDrag = function (ev) { if (drag && ev.pointerId === drag.id) { drag = null; bar.classList.remove('is-dragging'); } };
        bar.addEventListener('pointerup', endDrag); bar.addEventListener('pointercancel', endDrag);

        say(pickNoRepeat(STEAL_LINES, rng, usedSteal));
        setStatus('interrupted. close the offer to continue.', true);
      }
      function closePopup(pop) {
        if (!pop.parentNode || pop.classList.contains('is-out')) return;
        pop.classList.add('is-out');
        self._after(200, function () { if (pop.parentNode) pop.parentNode.removeChild(pop); });
        state.popupOpen = false;
        state.closed++;
        declinedB.textContent = String(state.closed);
        box.classList.remove('is-blocked');
        threshold = nextThreshold(spawned, rng);
        if (state.closed >= state.gate) {
          setStatus('no more offers. hold the box and finish it.', false);
          say('fine. no more ads. hold it all the way this time.');
        } else {
          setStatus(pickNoRepeat(CLOSED_LINES, rng, usedClosed), false);
        }
      }

      // ---- loop -------------------------------------------------------------
      var capSaid = false;
      function frame(now) {
        if (!self._alive || done) return;
        var dt = Math.min(80, now - last); last = now;
        var ev = step(state, dt, held, threshold);
        setRing(state.p);
        if (held && !state.popupOpen) {
          var rung = Math.min(HOLD_LINES.length - 1, Math.floor(state.p * HOLD_LINES.length));
          if (rung !== lastRung && state.p < CAP - 0.02) { lastRung = rung; setStatus(HOLD_LINES[rung], false); }
          if (state.p >= CAP && state.closed < state.gate && !capSaid) {
            capSaid = true; box.classList.add('is-capped'); say(CAP_LINES[Math.floor(rng() * CAP_LINES.length)]);
            setStatus('stuck at 92%. something wants your attention first.', true);
            self._after(600, function () { box.classList.remove('is-capped'); });
          }
        }
        if (ev === 'spawn') { capSaid = false; spawnPopup(); }
        else if (ev === 'complete') { complete(); return; }
        self._raf = requestAnimationFrame(frame);
      }
      self._raf = requestAnimationFrame(frame);

      // ---- end states ------------------------------------------------------
      function meta() {
        return { popupsClosed: state.closed, popupsShown: spawned, gate: state.gate, elapsedMs: Math.round(performance.now() - t0) };
      }
      function complete() {
        if (done) return;
        done = true; release();
        box.classList.remove('is-held', 'is-blocked'); box.classList.add('is-done');
        glyph.textContent = '✓';
        setRing(1);
        setStatus('verified. apparently.', false);
        lblHint.textContent = 'verification complete';
        say('not a ' + hon + '? the box says so. the box is very trusting.');
        self._after(800, function () { try { api.finish('completed', meta()); } catch (_e) {} });
      }
      function fail() {
        if (done) return;
        done = true; release();
        for (var i = 0; i < self._pops.length; i++) { try { self._pops[i].classList.add('is-out'); } catch (_e) {} }
        setStatus('verification expired.', true);
        lblHint.textContent = 'expired';
        say('the verification expired. the exit shrugs.');
        self._after(900, function () { try { api.finish('failed', meta()); } catch (_e) {} });
      }

      // ---- clock ------------------------------------------------------------
      var lastShown = -1;
      var clock = setInterval(function () {
        if (!self._alive || done) return;
        var left = TIME_LIMIT_MS - (performance.now() - t0);
        var sec = Math.max(0, Math.ceil(left / 1000));
        if (sec !== lastShown) { lastShown = sec; try { if (hud.timer) hud.timer(sec); } catch (_e) {} }
        if (left <= 0) fail();
      }, 200);
      this._timers.push(clock);

      // dev hooks (authoring only): ?autopop=1 spawns a popup right away,
      // ?autohold=1 simulates a hold, ?autodone=1 completes after 1.5 s
      try {
        var q = new URLSearchParams(location.search);
        if (q.get('autopop') === '1') this._after(500, function () { state.p = 0.38; setRing(state.p); spawnPopup(); });
        if (q.get('autohold') === '1') this._after(300, function () { held = true; box.classList.add('is-held'); });
        if (q.get('autodone') === '1') this._after(1500, complete);
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
      if (this._raf) cancelAnimationFrame(this._raf);
      this._raf = 0;
      for (var i = 0; i < this._timers.length; i++) { clearTimeout(this._timers[i]); clearInterval(this._timers[i]); }
      this._timers = [];
      for (var k = 0; k < (this._off || []).length; k++) { try { this._off[k](); } catch (_e) {} }
      this._off = [];
      for (var j = 0; j < this._pops.length; j++) { try { if (this._pops[j].parentNode) this._pops[j].parentNode.removeChild(this._pops[j]); } catch (_e) {} }
      this._pops = [];
      if (this._root && this._root.parentNode) this._root.parentNode.removeChild(this._root);
      this._root = null;
    },
  };

  var tries = 0;
  (function reg() {
    if (G.EE && typeof G.EE.registerGame === 'function') { G.EE.registerGame(game); return; }
    if (++tries < 100) setTimeout(reg, 50);
  })();
})();
