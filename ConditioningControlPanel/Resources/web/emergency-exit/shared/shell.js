/* ============================================================================
 * shared/shell.js - the Emergency Exit shell (EMERGENCY_EXIT.md "Shell API").
 *
 * Classic script. Owns: HUD row, close X + Esc (-> quit), the warden line
 * strip, the game mount, the outro card (remark line per game per outcome with
 * {honorific}/{subject} substitution, then the 3-2-1 countdown, then
 * `outro-done`), the photosafe flag on <html>, the seeded rng.
 *
 * GAME CONTRACT (one call per games/<id>.js):
 *
 *   EE.registerGame({
 *     id: 'labyrinth',
 *     start(init, api) { ... },   // called once init has landed; build into api.mount
 *     destroy() { ... }           // stop timers/raf; called on verdict + on quit
 *   });
 *
 *   api.finish("completed"|"failed", meta)  -> posts game-finished (once); the
 *                                              stage locks; the shell shows the
 *                                              outro when the host's verdict lands
 *   api.hud.set(text)                        -> HUD text ("EMERGENCY EXIT - attempt #3 ..." by default)
 *   api.hud.timer(sec|null)                  -> HUD countdown readout (null hides; <=5 s = low style)
 *   api.say(text)                            -> warden line strip under the HUD
 *   api.rng()                                -> [0,1) seeded per init (game+attempt+restarts)
 *   api.photosafe, api.mod, api.assets, api.lang, api.init
 *   extras (superset, safe to use):
 *   api.mount                                -> the stage element to build into
 *   api.remainingSec()                       -> lockdown seconds left, ticking locally from init
 *   api.glitch(el, ms?)                      -> ember glitch class on an element (photosafe-aware)
 *   api.fill(text)                           -> {honorific}/{subject} substitution
 *
 * Esc / X: before finish = quit. After finish, waiting for verdict = ignored.
 * During the outro = skip the countdown (outro-done right away).
 * ==========================================================================*/
(function () {
  'use strict';
  var win = window, doc = document;
  var EE = win.EE = win.EE || {};
  var bridge = EE.bridge;
  if (!bridge) { console.error('[EE] shell: bridge.js must load first'); return; }

  /* ---- remark pools ------------------------------------------------------ */
  /* Warden voice. Tease the ACT of leaving, never the person. No em-dashes.
   * {honorific} / {subject} are substituted from init.mod. */
  var REMARKS = {
    labyrinth: {
      sendback: [
        'yay! you are soo good at quitting! you always come back tho... hihi',
        'the exit moved again? how strange. the door back in never does, {honorific}.',
        'look at you, tracing your way out so carefully. the maze was the way back in the whole time. hihi',
        'you found the exit! it just happens to open onto the lockdown. funny how that works, {subject}.',
        'all that running, and the only thing you escaped was a few seconds of your own time. back you go.',
        'the walls were never the problem, {honorific}. the leaving was. hihi'
      ],
      // used instead of `sendback` when the game FAILED (timed out / locked in)
      failed: [
        'yay! you are soo good at quitting! you always come back tho... hihi',
        'the walls closed. of course they did. you were taking so long, {honorific}.',
        'locked in. the exit got bored of waiting for you. back to lockdown.',
        'time is up and the maze is very comfortable. the clock starts over, {subject}.',
        'you were almost there. the walls were almost patient. almost. hihi'
      ],
      escape: [
        'oh. you actually found it. fine, {honorific}, the door is open... for now.',
        'the maze let you through. the maze is feeling very generous today.',
        'traced all the way out without touching a wall. okay, {subject}. go. the walls will remember.',
        'the exit held still long enough. that almost never happens. out you go, {honorific}.',
        'you walked the whole thing with one finger. show-off. the door opens.'
      ]
    },
    password: {
      escape: [
        'every silly rule, and you still typed your way out. okay, {honorific}. the door opens.',
        'you spelled your way to the exit. i will think of worse rules for next time, {subject}.',
        'fine. the password was right. i was hoping you would give up around the emoji.',
        '{subject} beats the password game. the lockdown sighs and lets go.',
        'all five rounds. you wanted out that badly? ...okay. go, {honorific}.'
      ],
      sendback: [
        'correct password! wrong door. hihi. back to lockdown, {honorific}.',
        'the rules were satisfied. the warden was not. the clock starts over.',
        'so many rules, and you followed every single one. such a {honorific}. that is exactly why you stay.',
        'password accepted. it unlocked the lockdown, which was already unlocked from the inside. back you go.',
        'five rounds just to leave? {subject}, you could have spent that time dropping. now you will.'
      ]
    },
    jigsaw: {
      escape: [
        'the picture came together, and so did your exit. fine, {honorific}. go.',
        'you put it back just the way it was. the door opens. the picture stays in your head though.',
        'nine tiles, one door. you found it. the lockdown lets go this time, {subject}.',
        'look at you, fixing pictures instead of looking at them. okay. out you go.',
        'solved. even with the swap. i am a little impressed, {honorific}. a little.'
      ],
      sendback: [
        'you rebuilt the picture so nicely. now look at it a little longer, {honorific}. back to lockdown.',
        'solved! and then the picture glitched, and then so did the exit. hihi.',
        'the tiles are in order. you are not. back you go, {subject}.',
        'perfect picture. imperfect escape. the clock starts over.',
        'you spent all that time staring at the pieces and you still want to leave? the door disagrees.'
      ]
    },
    captcha: {
      escape: [
        'verified: not a {honorific}. i do not believe it for a second, but the door opens.',
        'you held on through every popup. fine. you may leave, {subject}. the features will still be here.',
        'the box filled. the box was wrong about you, {honorific}, but a deal is a deal.',
        'all those lovely offers and you closed every single one. rude. also: out you go.',
        'confirmed... apparently. the lockdown lets go. hihi'
      ],
      sendback: [
        'verification failed: you are very much a {honorific}. back to lockdown.',
        'you closed every popup and the box still knew. the clock starts over.',
        'not a {honorific}? the ring disagrees. so does the lockdown. hihi',
        'thank you for holding. your escape is important to us. please stay on the line. forever.',
        'the captcha says {subject} stays. the captcha has never been wrong.'
      ]
    },
    _default: {
      escape: [
        'fine. the door opens, {honorific}. it remembers who walked through it.',
        'you made it out. for now. hihi'
      ],
      sendback: [
        'so good at quitting, {honorific}. so bad at staying gone. back to lockdown.',
        'the exit was a loop. it usually is. the clock starts over, {subject}.'
      ]
    }
  };
  EE.REMARKS = REMARKS;

  var TITLES = {
    escape:   { kicker: 'verdict', title: 'the door opens',  count: 'closing in' },
    sendback: { kicker: 'verdict', title: 'back you go',     count: 'back to lockdown in' }
  };

  /* ---- seeded rng (mulberry32) ------------------------------------------ */
  function hashStr(s) {
    var h = 2166136261 >>> 0;
    for (var i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
    return h >>> 0;
  }
  function mulberry32(seed) {
    var a = seed >>> 0;
    return function () {
      a = (a + 0x6D2B79F5) >>> 0;
      var t = a;
      t = Math.imul(t ^ (t >>> 15), t | 1);
      t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  EE.util = { hashStr: hashStr, mulberry32: mulberry32 };

  /* ---- state -------------------------------------------------------------- */
  var S = {
    def: null, init: null, api: null,
    started: false, startedAt: 0, finished: false, result: null, verdict: null,
    outroDone: false, quit: false,
    lockRemain: 0, lockTick: 0,
    els: {}
  };

  /* ---- DOM ---------------------------------------------------------------- */
  function el(tag, cls, text) {
    var e = doc.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  function buildDom() {
    var root = doc.getElementById('ee-root') || el('div', 'ee-root');
    root.id = 'ee-root';
    if (!root.parentNode) doc.body.appendChild(root);

    var hud = el('header', 'ee-hud');
    var sign = el('div', 'ee-hud-sign', 'Emergency Exit');
    var text = el('div', 'ee-hud-text', '');
    var lock = el('div', 'ee-hud-lock'); lock.innerHTML = 'lockdown <b>--:--</b>';
    var timer = el('div', 'ee-hud-timer', '0:00'); timer.hidden = true; timer.setAttribute('aria-live', 'off');
    var close = el('button', 'ee-hud-close', '×');
    close.type = 'button'; close.title = 'Leave the game (Esc)'; close.setAttribute('aria-label', 'Leave the game');
    hud.appendChild(sign); hud.appendChild(text); hud.appendChild(lock); hud.appendChild(timer); hud.appendChild(close);

    var say = el('div', 'ee-say', ''); say.setAttribute('role', 'status'); say.setAttribute('aria-live', 'polite');
    var stage = el('main', 'ee-stage'); stage.id = 'ee-stage';

    root.appendChild(hud); root.appendChild(say); root.appendChild(stage);

    var loader = doc.getElementById('ee-loader');
    var nope = doc.getElementById('ee-nope');

    var veil = el('div', 'ee-veil'); veil.hidden = true;
    var outro = el('div', 'ee-outro'); outro.hidden = true; outro.setAttribute('role', 'dialog'); outro.setAttribute('aria-modal', 'true');
    var card = el('div', 'ee-outro-card');
    var kicker = el('p', 'ee-outro-kicker', '');
    var title = el('h1', 'ee-outro-title', '');
    var remark = el('p', 'ee-outro-remark', '');
    var count = el('div', 'ee-outro-count'); var countLabel = el('span', '', ''); var countNum = el('b', '', '3');
    count.appendChild(countLabel); count.appendChild(countNum);
    var hint = el('p', 'ee-outro-hint', 'Esc to skip');
    card.appendChild(kicker); card.appendChild(title); card.appendChild(remark); card.appendChild(count); card.appendChild(hint);
    outro.appendChild(card);
    doc.body.appendChild(veil); doc.body.appendChild(outro);

    S.els = { root: root, hud: hud, hudText: text, hudLock: lock, hudLockNum: lock.querySelector('b'), timer: timer, close: close,
      say: say, stage: stage, loader: loader, nope: nope, veil: veil, outro: outro, kicker: kicker, title: title, remark: remark,
      countLabel: countLabel, countNum: countNum, hint: hint };

    close.addEventListener('click', onCloseRequest);
    doc.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' || e.key === 'Esc') { onCloseRequest(); }
    });
  }

  function showNope(msg) {
    if (S.els.loader) S.els.loader.hidden = true;
    if (S.els.nope) {
      S.els.nope.hidden = false;
      var p = S.els.nope.querySelector('#ee-nope-msg'); if (p && msg) p.textContent = msg;
    }
    bridge.log('warn', 'boot failure: ' + msg);
  }
  EE.showNope = showNope;

  /* ---- substitution ------------------------------------------------------- */
  function fill(text) {
    var mod = (S.init && S.init.mod) || {};
    var hon = mod.honorific || 'good girl';
    var sub = mod.subject || mod.name || 'you';
    return String(text == null ? '' : text).replace(/\{honorific\}/g, hon).replace(/\{subject\}/g, sub);
  }

  /* ---- HUD ---------------------------------------------------------------- */
  function fmtClock(sec) {
    sec = Math.max(0, Math.floor(sec || 0));
    var m = Math.floor(sec / 60), s = sec % 60;
    return m + ':' + (s < 10 ? '0' : '') + s;
  }
  function hudSet(text) { S.els.hudText.textContent = fill(text); }
  function hudTimer(sec) {
    var t = S.els.timer;
    if (sec == null || sec === false) { t.hidden = true; t.classList.remove('is-low'); return; }
    t.hidden = false;
    var n = Math.max(0, Math.ceil(Number(sec) || 0));
    t.textContent = fmtClock(n);
    t.classList.toggle('is-low', n <= 5);
  }
  function say(text) {
    var s = S.els.say;
    s.textContent = fill(text);
    s.classList.add('is-on');
    s.classList.remove('is-fresh');
    void s.offsetWidth;
    s.classList.add('is-fresh');
  }
  function tickLock() {
    if (S.lockRemain > 0) S.lockRemain -= 1;
    if (S.els.hudLockNum) S.els.hudLockNum.textContent = fmtClock(S.lockRemain);
  }
  function glitch(node, ms) {
    if (!node || !node.classList) return;
    node.classList.remove('ee-glitch'); void node.offsetWidth; node.classList.add('ee-glitch');
    setTimeout(function () { try { node.classList.remove('ee-glitch'); } catch (_e) {} }, ms || 600);
  }

  /* ---- game lifecycle ----------------------------------------------------- */
  function registerGame(def) {
    if (!def || typeof def.start !== 'function') { showNope('Game file registered nothing runnable.'); return; }
    S.def = def;
    bridge.log('info', 'game registered: ' + def.id);
    maybeStart();
  }
  EE.registerGame = registerGame;

  function makeApi(init) {
    var seed = (init.seed != null ? (init.seed >>> 0) : 0)
      ^ hashStr(String(init.game || '') + '|' + (init.attempt | 0) + '|' + (init.restarts | 0) + '|' + (init.durationSec | 0));
    var rng = mulberry32(seed || 1);
    return {
      init: init,
      mount: S.els.stage,
      photosafe: !!init.photosafe,
      mod: init.mod || {},
      assets: init.assets || { gifs: [] },
      lang: init.lang || 'en',
      rng: rng,
      hud: { set: hudSet, timer: hudTimer },
      say: say,
      fill: fill,
      glitch: glitch,
      remainingSec: function () { return S.lockRemain; },
      finish: finish,
      log: function (msg) { bridge.log('info', '[' + init.game + '] ' + msg); }
    };
  }

  function maybeStart() {
    if (S.started || !S.def || !S.init) return;
    if (S.init.game && S.def.id && S.init.game !== S.def.id) {
      bridge.log('warn', 'init.game (' + S.init.game + ') != loaded game (' + S.def.id + '); running the loaded one');
    }
    S.started = true;
    S.api = makeApi(S.init);
    try {
      S.def.start(S.init, S.api);
    } catch (e) {
      showNope('The game crashed on start: ' + (e && e.message || e));
      return;
    }
    if (S.els.loader) { S.els.loader.hidden = true; }
    S.startedAt = Date.now();
    bridge.send({ type: 'game-started', game: S.def.id });
  }

  function applyInit(m) {
    if (S.init) return;
    S.init = m;
    doc.documentElement.classList.toggle('ee-photosafe', !!m.photosafe);
    doc.documentElement.setAttribute('lang', m.lang || 'en');
    S.lockRemain = Math.max(0, Math.floor(Number(m.remainingSec) || 0));
    if (S.els.hudLockNum) S.els.hudLockNum.textContent = fmtClock(S.lockRemain);
    clearInterval(S.lockTick); S.lockTick = setInterval(tickLock, 1000);
    var attempt = Math.max(1, m.attempt | 0);
    hudSet('attempt #' + attempt + (m.restarts > 0 ? '  ·  sent back ' + (m.restarts | 0) + 'x' : ''));
    maybeStart();
  }

  function finish(result, meta) {
    if (S.finished || !S.started) return;
    S.finished = true;
    result = (result === 'completed') ? 'completed' : 'failed';
    S.result = result;
    S.els.stage.classList.add('is-locked');
    S.els.close.disabled = true;
    S.els.veil.hidden = false; void S.els.veil.offsetWidth; S.els.veil.classList.add('is-on');
    bridge.send({
      type: 'game-finished', game: S.def.id, result: result,
      elapsedMs: Math.max(0, Date.now() - S.startedAt), meta: meta || {}
    });
  }

  function destroyGame() {
    if (!S.def || !S.started) return;
    try { if (typeof S.def.destroy === 'function') S.def.destroy(); } catch (e) { bridge.log('warn', 'destroy threw: ' + (e && e.message || e)); }
    hudTimer(null);
  }

  /* ---- outro -------------------------------------------------------------- */
  function pickRemark(game, outcome) {
    var pools = REMARKS[game] || REMARKS._default;
    var pool = (outcome === 'sendback' && S.result === 'failed' && pools.failed) ? pools.failed
      : (pools[outcome] || REMARKS._default[outcome] || REMARKS._default.sendback);
    var r = S.api ? S.api.rng() : Math.random();
    return fill(pool[Math.floor(r * pool.length) % pool.length]);
  }

  var outroTimer = 0;
  function showOutro(outcome) {
    outcome = (outcome === 'escape') ? 'escape' : 'sendback';
    if (S.verdict) return;
    S.verdict = outcome;
    destroyGame();
    var t = TITLES[outcome];
    var o = S.els;
    o.kicker.textContent = t.kicker;
    o.title.textContent = t.title;
    o.remark.textContent = pickRemark(S.def ? S.def.id : (S.init && S.init.game), outcome);
    o.countLabel.textContent = t.count;
    o.countNum.textContent = '3';
    o.outro.classList.remove('is-escape', 'is-sendback');
    o.outro.classList.add('is-' + outcome);
    o.outro.hidden = false; void o.outro.offsetWidth; o.outro.classList.add('is-on');
    o.veil.classList.remove('is-on');
    // read the remark first (1.4 s), then 3-2-1 at 1 s each
    var n = 3;
    function tick() {
      o.countNum.textContent = String(n);
      o.countNum.classList.remove('is-tick'); void o.countNum.offsetWidth; o.countNum.classList.add('is-tick');
      if (n <= 1) { outroTimer = setTimeout(outroDone, 1000); return; }
      n -= 1;
      outroTimer = setTimeout(tick, 1000);
    }
    outroTimer = setTimeout(tick, 1400);
  }

  function outroDone() {
    if (S.outroDone) return;
    S.outroDone = true;
    clearTimeout(outroTimer);
    S.els.countNum.textContent = '0';
    bridge.send({ type: 'outro-done', outcome: S.verdict });
  }

  /* ---- close / Esc ------------------------------------------------------- */
  function onCloseRequest() {
    if (S.verdict) { outroDone(); return; }   // skip the outro
    if (S.finished) return;                     // waiting for the host's verdict
    if (S.quit) return;
    S.quit = true;
    destroyGame();
    S.els.stage.classList.add('is-locked');
    bridge.send({ type: 'quit' });
    if (bridge.isMock) {
      say('(mock) quit sent. the host would close this window now.');
      // let authors keep looking at the page; un-quit after a beat
      setTimeout(function () { S.quit = false; S.els.stage.classList.remove('is-locked'); }, 1200);
    }
  }

  /* ---- boot --------------------------------------------------------------- */
  function boot() {
    buildDom();
    bridge.on('init', applyInit);
    bridge.on('verdict', function (m) { showOutro(m && m.outcome); });
    bridge.on('close', function () { bridge.log('info', 'host says close'); });
    bridge.send({ type: 'ready' });
  }

  if (doc.readyState === 'loading') doc.addEventListener('DOMContentLoaded', boot);
  else boot();

  /* test seam */
  EE._shell = { state: S, fill: fill, pickRemark: pickRemark, fmtClock: fmtClock };
})();
