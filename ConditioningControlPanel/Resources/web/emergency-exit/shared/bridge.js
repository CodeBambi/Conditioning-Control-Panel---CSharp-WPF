/* ============================================================================
 * shared/bridge.js - the postMessage contract with the WPF host
 * (EmergencyExitHostService via ChaosWebViewHost). Protocol: EMERGENCY_EXIT.md.
 *
 * Classic script (NOT an ES module) so the page also runs from file:// for
 * authoring. Exposes window.EE.bridge:
 *
 *   EE.bridge.isHosted          true inside WebView2 (window.chrome.webview)
 *   EE.bridge.isMock            true when ?mock=1 (or not hosted at all)
 *   EE.bridge.send(msg)         page -> host ({type:"ready"|"game-started"|...})
 *   EE.bridge.on(type, fn)      host -> page subscription; replays pre-buffered
 *                               frames of that type; returns unsubscribe()
 *   EE.bridge.log(level, msg)   console + {type:"log"} frame to the host
 *   EE.bridge.query             parsed ?query params (game, mock, verdict, ...)
 *
 * Hygiene (same as arcademy/bridge.js): host frames that land before anyone
 * subscribed are pre-buffered and replayed on subscribe; nothing here touches
 * the DOM.
 *
 * MOCK MODE (`index.html?game=<id>&mock=1`, or any non-hosted page):
 *   - `ready`          -> answers `init` after 60 ms with a placeholder mod
 *                         {id:"builtin-bambisleep", name:"Bambi Sleep",
 *                          honorific:"good girl", subject:"Bambi"} and three
 *                         CSS-gradient "gifs" (inline SVG data: URLs)
 *   - `game-finished`  -> answers `verdict` after 400 ms (random escape /
 *                         sendback; `&verdict=escape|sendback` forces it;
 *                         labyrinth is always sendback like the host)
 *   - `outro-done`, `quit`, `game-started`, `log` -> console only
 *   Extra mock knobs: &photosafe=1  &remaining=<sec>  &duration=<sec>
 *                     &attempt=<n>  &restarts=<n>  &seed=<n>  &lang=xx
 *                     &gifs=0 (empty asset list)
 *   Everything is logged to the console with the [EE] prefix.
 * ==========================================================================*/
(function () {
  'use strict';
  var win = (typeof window !== 'undefined') ? window : null;
  if (!win) return;
  var EE = win.EE = win.EE || {};

  var webview = win.chrome && win.chrome.webview;
  var isHosted = !!webview;

  /* ---- query ------------------------------------------------------------ */
  var query = {};
  try {
    var qs = (win.location && win.location.search) ? win.location.search.slice(1) : '';
    qs.split('&').forEach(function (kv) {
      if (!kv) return;
      var i = kv.indexOf('=');
      var k = decodeURIComponent(i < 0 ? kv : kv.slice(0, i));
      var v = i < 0 ? '1' : decodeURIComponent(kv.slice(i + 1));
      query[k] = v;
    });
  } catch (_e) {}

  var isMock = !isHosted || query.mock === '1' || query.mock === 'true';

  /* ---- console / host log ---------------------------------------------- */
  function clog(level, msg) {
    try {
      var fn = (level === 'warn' || level === 'error') ? console.warn : console.log;
      fn.call(console, '[EE] ' + msg);
    } catch (_e) {}
  }

  /* ---- HOST -> PAGE ------------------------------------------------------ */
  var handlers = {};     // type -> [fn]
  var preBuffer = [];
  var MAX_PRE = 100;

  function dispatch(m) {
    if (!m || typeof m.type !== 'string') return;
    var list = handlers[m.type];
    if (!list || !list.length) {
      if (preBuffer.length < MAX_PRE) preBuffer.push(m);
      return;
    }
    list.slice().forEach(function (fn) {
      try { fn(m); } catch (e) { clog('warn', 'handler ' + m.type + ' threw: ' + (e && e.message || e)); }
    });
  }

  function on(type, fn) {
    if (typeof fn !== 'function') return function () {};
    (handlers[type] = handlers[type] || []).push(fn);
    for (var i = 0; i < preBuffer.length; i++) {
      if (preBuffer[i].type === type) {
        var m = preBuffer.splice(i, 1)[0]; i--;
        try { fn(m); } catch (e) { clog('warn', 'replay ' + type + ' threw: ' + (e && e.message || e)); }
      }
    }
    return function () {
      var list = handlers[type]; if (!list) return;
      var at = list.indexOf(fn); if (at >= 0) list.splice(at, 1);
    };
  }

  if (webview) {
    try {
      webview.addEventListener('message', function (e) {
        var data = e && e.data;
        if (typeof data === 'string') { try { data = JSON.parse(data); } catch (_e) { return; } }
        try { dispatch(data); } catch (_err) {}
      });
    } catch (_e) {}
  }

  /* ---- PAGE -> HOST ------------------------------------------------------ */
  function send(msg) {
    if (!msg || typeof msg.type !== 'string') return;
    if (isMock) { mockHandle(msg); }
    if (webview) {
      try { webview.postMessage(msg); } catch (_e) { /* host gone */ }
    }
  }

  function log(level, msg) {
    level = (level === 'warn') ? 'warn' : 'info';
    clog(level, msg);
    if (webview) {
      try { webview.postMessage({ type: 'log', level: level, msg: String(msg) }); } catch (_e) {}
    }
  }

  /* ---- MOCK HOST --------------------------------------------------------- */
  function svgGif(a, b, c, label) {
    var svg =
      '<svg xmlns="http://www.w3.org/2000/svg" width="480" height="480" viewBox="0 0 480 480">' +
      '<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">' +
      '<stop offset="0" stop-color="' + a + '"/><stop offset=".55" stop-color="' + b + '"/><stop offset="1" stop-color="' + c + '"/>' +
      '</linearGradient><radialGradient id="r" cx=".5" cy=".45" r=".6"><stop offset="0" stop-color="#fff" stop-opacity=".35"/><stop offset="1" stop-color="#000" stop-opacity=".25"/></radialGradient></defs>' +
      '<rect width="480" height="480" fill="url(#g)"/><rect width="480" height="480" fill="url(#r)"/>' +
      '<circle cx="240" cy="200" r="88" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="14"/>' +
      '<circle cx="240" cy="200" r="46" fill="#fff" fill-opacity=".5"/>' +
      '<text x="240" y="380" font-family="Segoe UI, sans-serif" font-size="44" font-weight="800" fill="#fff" fill-opacity=".85" text-anchor="middle" letter-spacing="6">' + label + '</text>' +
      '</svg>';
    return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
  }

  var MOCK_GIFS = [
    svgGif('#FF69B4', '#DC143C', '#1A1A2E', 'MOCK 1'),
    svgGif('#FF8A5C', '#FF69B4', '#252542', 'MOCK 2'),
    svgGif('#7B5CFF', '#DC143C', '#FF8A5C', 'MOCK 3')
  ];

  function num(v, d) { var n = parseInt(v, 10); return isFinite(n) ? n : d; }

  function fabricateInit() {
    var game = query.game || 'labyrinth';
    var duration = num(query.duration, 600);
    var remaining = num(query.remaining, 412);
    return {
      type: 'init',
      game: game,
      attempt: num(query.attempt, 3),
      restarts: num(query.restarts, 1),
      remainingSec: remaining,
      durationSec: duration,
      photosafe: query.photosafe === '1' || query.photosafe === 'true',
      lang: query.lang || 'en',
      seed: query.seed != null ? num(query.seed, 0) : undefined,
      mod: { id: 'builtin-bambisleep', name: 'Bambi Sleep', honorific: 'good girl', subject: 'Bambi' },
      assets: { gifs: (query.gifs === '0') ? [] : MOCK_GIFS.slice() },
      mock: true
    };
  }

  function mockHandle(msg) {
    clog('info', 'mock <- page ' + JSON.stringify(msg));
    switch (msg.type) {
      case 'ready':
        setTimeout(function () {
          var init = fabricateInit();
          clog('info', 'mock -> page init ' + JSON.stringify(init).slice(0, 200) + '...');
          dispatch(init);
        }, 60);
        break;
      case 'game-finished':
        setTimeout(function () {
          var outcome;
          if (query.verdict === 'escape' || query.verdict === 'sendback') outcome = query.verdict;
          else if (msg.game === 'labyrinth' || msg.result === 'failed') outcome = 'sendback';
          else outcome = (Math.random() < 0.67) ? 'escape' : 'sendback';
          clog('info', 'mock -> page verdict ' + outcome);
          dispatch({ type: 'verdict', outcome: outcome });
        }, 400);
        break;
      case 'outro-done':
        clog('info', 'mock: outro-done (' + msg.outcome + ') - host would close the window now');
        break;
      case 'quit':
        clog('info', 'mock: quit - host would close the window, lockdown keeps running');
        break;
      default:
        break;
    }
  }

  EE.bridge = {
    isHosted: isHosted,
    isMock: isMock,
    query: query,
    send: send,
    on: on,
    log: log,
    /** test seam: feed a host frame by hand (mock / node tests) */
    _dispatch: dispatch
  };

  clog('info', 'bridge up (hosted=' + isHosted + ', mock=' + isMock + ')');
})();
