// Mandatory-video player page for the hybrid Browser Video Engine (plan §3).
//
// The page is deliberately dumb: C# stays the director (scheduling, XP, safety
// timers, attention checks) and this file is only a remote-controlled <video>
// that reports what the element does. It queues nothing — it posts {type:'ready'}
// once and the host holds its outbound messages until then.
//
// Decoder discipline is lifted from Resources/web/fyp/surfaces.js (do not import
// it — the player is standalone):
//   - teardown is pause() -> removeAttribute('src') -> load(), so Chromium frees
//     the decoder now instead of at GC time, and it runs before EVERY new load,
//   - an unrequested pause is an interruption, not a user action,
//   - a media error never throws; it is reported and the session ends.

(function () {
  'use strict';

  // ---------- tunables ----------

  const TICK_MS = 100;          // time posts (~10 Hz) + watchdog sampling
  const STALL_MS = 10000;       // live but no forward progress this long => error
  const NO_PLAYING_MS = 10000;  // never reached 'playing' this long => error
  const BG_DRIFT_S = 2;         // blur backdrop resync threshold
  const BG_RESYNC_GAP_MS = 4000;
  const BG_PLAY_RETRIES = 3;    // then give up on the backdrop (never on the clip)

  // Error codes 1-4 are MediaError.code verbatim (a real decode/network failure
  // => the file belongs in BrowserUnsafeVideoCache). 100+ are ours; only 100/101
  // mean "this file misbehaved", 102/103 are session-level.
  const ERR_STALLED = 100;
  const ERR_NO_PLAYING = 101;
  const ERR_PAUSE_LOOP = 102;
  const ERR_BAD_LOAD = 103;

  const MEDIA_ERR_NAME = {
    1: 'aborted',
    2: 'network',
    3: 'decode',
    4: 'src-not-supported',
  };

  // ---------- bridge ----------

  const bridge = window.chrome && window.chrome.webview ? window.chrome.webview : null;

  function post(msg) {
    try { if (bridge) bridge.postMessage(msg); } catch (e) { /* host gone */ }
  }

  function log(msg) {
    post({ type: 'log', msg: String(msg) });
  }

  // ---------- elements ----------

  const bg = document.getElementById('bg');
  const fg = document.getElementById('fg');
  const attnLayer = document.getElementById('attn');

  // Belt-and-braces alongside the HTML attributes: the element is born muted and
  // silent, and the host's volume only lands once a load says so.
  fg.muted = true;
  fg.volume = 0;
  bg.muted = true;
  bg.volume = 0;

  // ---------- state ----------

  // Volume survives between loads so a setVolume that arrives with no session
  // (or a load that omits the field) still means something.
  let volume = 1;
  let muted = false;

  // The live load, or null. Every media listener no-ops when this is null, which
  // is what makes teardown's own pause()/load() churn invisible.
  let cur = null;
  let ticker = null;
  let lastTimeMs = -1;
  let lastTimePostAt = 0;

  const clamp01 = (n) => (Number.isFinite(n) ? Math.min(1, Math.max(0, n)) : 1);

  function applyVolume() {
    try {
      fg.volume = clamp01(volume);
      fg.muted = !!muted;
    } catch (e) { /* out-of-range guard already applied */ }
    bg.muted = true; // the backdrop is NEVER audible, whatever the session says
    bg.volume = 0;
  }

  function playEl(el, tag) {
    const p = el.play();
    if (p && typeof p.catch === 'function') {
      p.catch((err) => {
        // --autoplay-policy=no-user-gesture-required makes this legal, so a
        // rejection here is a real problem — but not a fatal one on its own: the
        // no-playing watchdog is the thing that decides.
        log(tag + ' play() rejected: ' + (err && err.message ? err.message : err));
      });
    }
  }

  // ---------- teardown ----------

  /** Free both decoders NOW. Safe to call at any time, including with no session. */
  function teardown() {
    cur = null; // first, so the pause/error churn below reaches no handler
    clearAttention(); // targets belong to the clip that is going away
    stopTicker();
    lastTimeMs = -1;
    lastTimePostAt = 0;
    document.body.classList.remove('has-bg');
    [fg, bg].forEach((el) => {
      try { el.pause(); } catch (e) { /* ignore */ }
      el.removeAttribute('src');
      try { el.load(); } catch (e) { /* ignore */ }
    });
  }

  // ---------- error reporting ----------

  /** Report once per load and stop the clip. C# owns what happens next. */
  function fail(code, message) {
    if (!cur || cur.errored) return;
    cur.errored = true;
    stopTicker();
    try { fg.pause(); } catch (e) { /* ignore */ }
    try { bg.pause(); } catch (e) { /* ignore */ }
    post({ type: 'error', code: code, message: String(message) });
  }

  // ---------- load ----------

  function startLoad(d) {
    teardown();

    const url = typeof d.url === 'string' ? d.url : '';
    if (!url) {
      // No session exists yet, so fail() would no-op — report directly.
      post({ type: 'error', code: ERR_BAD_LOAD, message: 'load message carried no url' });
      return;
    }

    if (typeof d.volume === 'number') volume = clamp01(d.volume);
    if (typeof d.muted === 'boolean') muted = d.muted;

    // Opt-in, default off: the host decides, because the pointer has to stay
    // visible wherever the surface is mouse-driven (BubbleCount) or wherever the
    // LibVLC path it replaces would have shown one (mandatory video).
    document.body.classList.toggle('hide-cursor', !!d.hideCursor);

    const now = performance.now();
    cur = {
      url: url,
      blur: !!d.blurBackground,
      startAtMs: Number.isFinite(d.startAtMs) && d.startAtMs > 0 ? d.startAtMs : 0,
      startedAt: now,
      lastProgressAt: now,
      lastPos: -1,
      playingSent: false,
      ended: false,
      errored: false,
      hostPaused: false,
      unrequestedPauses: 0,
      startApplied: false,
      bgPlayFails: 0,
      bgResyncAt: 0,
      lastDurationMs: -1,
    };

    document.body.classList.toggle('has-bg', cur.blur);
    applyVolume();

    // Backdrop first so it is never the thing that delays the real clip.
    if (cur.blur) {
      bg.src = url;
      playEl(bg, 'bg');
    }
    fg.src = url;
    playEl(fg, 'fg');

    startTicker();
  }

  /** startAtMs, applied as soon as the element will accept a seek. */
  function applyStart() {
    if (!cur || cur.startApplied || cur.startAtMs <= 0) return;
    const sec = cur.startAtMs / 1000;
    const d = fg.duration;
    if (Number.isFinite(d) && d > 0 && sec >= d) { cur.startApplied = true; return; }
    try {
      fg.currentTime = sec;
      if (cur.blur) { try { bg.currentTime = sec; } catch (e) { /* backdrop only */ } }
      cur.startApplied = true;
    } catch (e) {
      // Not seekable yet — 'canplay' retries.
    }
  }

  // ---------- outbound: meta / time ----------

  function postMeta() {
    if (!cur) return;
    const d = fg.duration;
    // Some webm/fragmented mp4 report Infinity at loadedmetadata and settle on a
    // real number at durationchange, so meta can be posted more than once per
    // load — the last one wins. durationMs is 0 when the duration is unknowable.
    const durationMs = Number.isFinite(d) && d > 0 ? Math.round(d * 1000) : 0;
    if (durationMs === cur.lastDurationMs) return;
    cur.lastDurationMs = durationMs;
    post({
      type: 'meta',
      durationMs: durationMs,
      width: fg.videoWidth || 0,
      height: fg.videoHeight || 0,
    });
  }

  function postTime(force) {
    if (!cur) return;
    const now = performance.now();
    if (!force && now - lastTimePostAt < TICK_MS - 10) return;
    const ms = Math.round((fg.currentTime || 0) * 1000);
    if (!force && ms === lastTimeMs) return;
    lastTimeMs = ms;
    lastTimePostAt = now;
    post({ type: 'time', ms: ms });
  }

  // ---------- ticker: 10 Hz time + stall watchdogs + backdrop resync ----------

  function startTicker() {
    stopTicker();
    ticker = setInterval(tick, TICK_MS);
  }

  function stopTicker() {
    if (ticker != null) { clearInterval(ticker); ticker = null; }
  }

  function tick() {
    if (!cur || cur.errored) return;

    const now = performance.now();
    const pos = fg.currentTime || 0;
    if (pos !== cur.lastPos) {
      cur.lastPos = pos;
      cur.lastProgressAt = now;
    }

    if (!cur.ended && !cur.hostPaused) postTime(false);

    // Watchdogs are suspended while the host holds a grace pause, and after a
    // natural end (nothing is supposed to move then).
    if (cur.ended || cur.hostPaused) return;

    if (!cur.playingSent) {
      if (now - cur.startedAt > NO_PLAYING_MS) {
        fail(ERR_NO_PLAYING, 'no playing event within ' + NO_PLAYING_MS + 'ms of load');
      }
      return;
    }

    if (now - cur.lastProgressAt > STALL_MS) {
      fail(ERR_STALLED, 'playback made no progress for ' + STALL_MS + 'ms');
      return;
    }

    // Two elements decoding the same file drift apart; the backdrop is blurred
    // so small drift is invisible, but a couple of seconds means it is showing a
    // different scene. Nudge it, rarely.
    if (cur.blur && !bg.error && Number.isFinite(bg.duration)
        && now - cur.bgResyncAt > BG_RESYNC_GAP_MS
        && Math.abs((bg.currentTime || 0) - pos) > BG_DRIFT_S) {
      cur.bgResyncAt = now;
      try { bg.currentTime = pos; } catch (e) { /* backdrop only */ }
    }
  }

  // ---------- foreground media events ----------

  // Every listener below also stands down once the load has errored: the host is
  // already tearing the session down or replaying the file through LibVLC, and a
  // late meta/time post would land on a run that no longer owns them (it polluted
  // _lastWatchPositionMs during a fallback).

  fg.addEventListener('loadedmetadata', () => {
    if (!cur || cur.errored) return;
    applyStart();
    postMeta();
  });

  fg.addEventListener('durationchange', () => {
    if (!cur || cur.errored) return;
    postMeta();
  });

  fg.addEventListener('canplay', () => {
    if (!cur || cur.errored) return;
    applyStart(); // loadedmetadata may have been too early to seek
  });

  fg.addEventListener('playing', () => {
    if (!cur || cur.errored) return;
    cur.lastProgressAt = performance.now();
    if (cur.playingSent) return;
    cur.playingSent = true;
    post({ type: 'playing' });
  });

  fg.addEventListener('timeupdate', () => {
    if (!cur || cur.errored || cur.ended || cur.hostPaused) return;
    postTime(false);
  });

  fg.addEventListener('seeked', () => {
    if (!cur || cur.errored) return;
    cur.lastProgressAt = performance.now();
    postTime(true);
  });

  fg.addEventListener('ended', () => {
    if (!cur || cur.ended || cur.errored) return;
    cur.ended = true;
    postTime(true);
    // Nothing is supposed to move after the end, and the host keeps the surface
    // alive through its own result/teardown flow (BubbleCount's result screen runs
    // for as long as the user takes) — so stop the 10 Hz ticker rather than let it
    // spin for the whole of it.
    stopTicker();
    try { bg.pause(); } catch (e) { /* ignore */ }
    post({ type: 'ended' });
  });

  fg.addEventListener('error', () => {
    if (!cur) return;
    const err = fg.error;
    const code = err && err.code ? err.code : 0;
    const name = MEDIA_ERR_NAME[code] || 'unknown';
    const detail = err && err.message ? ' - ' + err.message : '';
    fail(code || ERR_BAD_LOAD, 'media error (' + name + ')' + detail);
  });

  // Anti-pause. There is no user-facing pause on a mandatory video, so a pause we
  // did not ask for is an interruption: resume it once, and if it happens again
  // in the same load report an error rather than fighting the browser forever.
  fg.addEventListener('pause', () => {
    if (!cur || cur.errored || cur.ended || cur.hostPaused) return;
    if (fg.ended || fg.error) return; // 'ended'/'error' own those transitions
    // A natural end fires 'pause' BEFORE 'ended' (spec order), so fg.ended is the
    // primary guard; this is the belt for a browser that sets the flag late.
    const dur = fg.duration;
    if (Number.isFinite(dur) && dur > 0 && (fg.currentTime || 0) >= dur - 0.5) return;
    cur.unrequestedPauses++;
    if (cur.unrequestedPauses === 1) {
      log('unrequested pause - resuming');
      playEl(fg, 'fg');
      if (cur.blur) playEl(bg, 'bg');
      return;
    }
    fail(ERR_PAUSE_LOOP, 'playback paused ' + cur.unrequestedPauses + ' times without a request');
  });

  // ---------- backdrop media events (never fatal) ----------

  bg.addEventListener('pause', () => {
    if (!cur || !cur.blur || cur.errored || cur.ended || cur.hostPaused) return;
    if (bg.ended || bg.error) return;
    if (cur.bgPlayFails >= BG_PLAY_RETRIES) return;
    cur.bgPlayFails++;
    playEl(bg, 'bg');
  });

  bg.addEventListener('error', () => {
    // A backdrop that will not decode is a cosmetic loss; the clip plays on black.
    if (!cur || !cur.blur) return;
    cur.blur = false;
    document.body.classList.remove('has-bg');
    bg.removeAttribute('src');
    try { bg.load(); } catch (e) { /* ignore */ }
    log('blur backdrop failed to decode - falling back to black');
  });

  // ---------- attention-check targets ----------

  // C# owns everything that decides an attention check: the trigger text (mod
  // pool + localization stay C#-side), the styling, when a target appears, when
  // it expires and what a hit is worth. The page owns only the paint and the
  // bounce — as transform/opacity on a promoted layer, which is the whole point
  // of moving them in here from their own topmost windows.
  //
  // Live from attentionShow until attentionHide; a click reports attentionClick
  // and then WAITS for the host to hide it, so C#'s _targets list stays the one
  // authority on which checks are outstanding (it also gates the grace pause).

  const attn = new Map();
  let attnRaf = 0;
  let attnLastTs = 0;
  let attnLastReport = 0;
  let attnPaused = false;

  function attnPump() {
    if (!attnRaf && !attnPaused && attn.size) attnRaf = requestAnimationFrame(attnFrame);
  }

  function attnFrame(ts) {
    attnRaf = 0;
    // A pause that landed while this frame was already queued must still win.
    if (attnPaused || !attn.size) { attnLastTs = 0; return; }

    // A dropped frame (or a page the compositor parked) must nudge the target,
    // not teleport it across the screen.
    const dt = attnLastTs ? Math.min(0.25, Math.max(0, (ts - attnLastTs) / 1000)) : 1 / 60;
    attnLastTs = ts;
    const report = ts - attnLastReport >= TICK_MS;
    if (report) attnLastReport = ts;
    const vw = window.innerWidth || 1;
    const vh = window.innerHeight || 1;

    attn.forEach((t) => {
      t.x += t.vx * dt;
      t.y += t.vy * dt;
      if (t.x < t.minX) { t.x = t.minX; t.vx = Math.abs(t.vx); }
      if (t.x + t.w > t.maxX) { t.x = t.maxX - t.w; t.vx = -Math.abs(t.vx); }
      if (t.y < t.minY) { t.y = t.minY; t.vy = Math.abs(t.vy); }
      if (t.y + t.h > t.maxY) { t.y = t.maxY - t.h; t.vy = -Math.abs(t.vy); }
      t.el.style.transform = 'translate3d(' + t.x + 'px,' + t.y + 'px,0)';
      // Gaze hit-testing runs C#-side against DIP bounds, and nothing else
      // consumes these — so they are only posted when the host asked for them.
      if (report && t.report && !t.hit) {
        post({
          type: 'attentionMove',
          id: t.id,
          xPct: t.x / vw,
          yPct: t.y / vh,
          wPct: t.w / vw,
          hPct: t.h / vh,
        });
      }
    });

    attnPump();
  }

  function attentionShow(d) {
    const id = d && d.id != null ? String(d.id) : '';
    if (!id || !attnLayer) return;
    attentionHide({ id: id }); // a repeated id replaces, never stacks

    const label = typeof d.text === 'string' ? d.text : '';
    const size = Number(d.size) > 0 ? Number(d.size) : 40;

    const el = document.createElement('div');
    el.className = 'attn' + (d.floating ? ' attn-floating' : '');
    el.dataset.id = id;

    const box = document.createElement('div');
    box.className = 'attn-box';
    if (!d.floating) {
      // LinearGradientBrush(color1, color2, 90) is top-to-bottom in WPF.
      box.style.background = 'linear-gradient(180deg,' + (d.color1 || '#FF1493') + ',' + (d.color2 || '#FF69B4') + ')';
      if (d.showBorder) {
        box.style.borderWidth = '3px';
        box.style.borderColor = d.borderColor || '#FF1493';
      }
    }

    const text = document.createElement('div');
    text.className = 'attn-text';
    text.style.fontSize = size + 'px';
    text.style.fontFamily = '"' + String(d.font || 'Segoe UI') + '","Segoe UI",Arial,sans-serif';

    // One span, outlined by paint-order (see .attn-text .fill). The second,
    // absolutely-positioned stroke-only span this used to build painted over
    // the fill and ate the glyph interiors (#873).
    const fill = document.createElement('span');
    fill.className = 'fill';
    fill.textContent = label;     // textContent, never innerHTML: the trigger is user data
    fill.style.color = d.textColor || '#FF1493';

    text.appendChild(fill);
    box.appendChild(text);
    el.appendChild(box);
    attnLayer.appendChild(el);

    // Only the page can know how big the target measured, so it resolves the
    // host's 0..1 spawn position against its own bounds.
    const r = el.getBoundingClientRect();
    const w = r.width || 150;
    const h = r.height || 60;
    const vw = window.innerWidth || w;
    const vh = window.innerHeight || h;
    const minX = Math.min(150, vw * 0.08);
    const minY = Math.min(100, vh * 0.08);
    const maxX = Math.max(minX + w, vw - minX);
    const maxY = Math.max(minY + h, vh - minY);
    const px = Number.isFinite(d.xPct) ? Math.min(1, Math.max(0, d.xPct)) : Math.random();
    const py = Number.isFinite(d.yPct) ? Math.min(1, Math.max(0, d.yPct)) : Math.random();

    const t = {
      id: id,
      el: el,
      w: w,
      h: h,
      minX: minX,
      minY: minY,
      maxX: maxX,
      maxY: maxY,
      x: minX + px * Math.max(0, (maxX - w) - minX),
      y: minY + py * Math.max(0, (maxY - h) - minY),
      vx: Number.isFinite(d.vx) ? d.vx : 130,
      vy: Number.isFinite(d.vy) ? d.vy : 130,
      report: !!d.reportMotion,
      hit: false,
    };
    el.style.transform = 'translate3d(' + t.x + 'px,' + t.y + 'px,0)';
    attn.set(id, t);
    attnPump();
  }

  function attentionHide(d) {
    const id = d && d.id != null ? String(d.id) : '';
    const t = attn.get(id);
    if (!t) return;
    attn.delete(id);
    if (!attn.size) attnLastTs = 0;
    const el = t.el;
    if (d && d.fade) {
      el.classList.add('gone');
      // The CSS transition owns the fade; this only reaps the node afterwards.
      setTimeout(() => { if (el.parentNode) el.parentNode.removeChild(el); }, 400);
      return;
    }
    if (el.parentNode) el.parentNode.removeChild(el);
  }

  function attentionHit(t) {
    if (!t || t.hit) return;
    t.hit = true;
    t.el.classList.add('attn-hit');
    // C# runs the whole hit pipeline (pop sound, XP, clearing this spawn's
    // mirrors on the other monitors) and answers with attentionHide.
    post({ type: 'attentionClick', id: t.id });
  }

  /**
   * Grace pause (#735): the targets freeze with the video. Same host-paused
   * state the media watchdogs use, so the two can never disagree — the bounce
   * stops, presses stop registering, and the targets stay on screen under the
   * host's Resume card. The lifespan countdown is C#-side and freezes there.
   */
  function attentionSetPaused(paused) {
    attnPaused = !!paused;
    document.body.classList.toggle('attn-paused', attnPaused);
    if (attnPaused) {
      if (attnRaf) { cancelAnimationFrame(attnRaf); attnRaf = 0; }
      attnLastTs = 0; // resume must step one frame, not the whole pause
      return;
    }
    attnPump();
  }

  function clearAttention() {
    attn.forEach((t) => { if (t.el.parentNode) t.el.parentNode.removeChild(t.el); });
    attn.clear();
    if (attnRaf) { cancelAnimationFrame(attnRaf); attnRaf = 0; }
    attnLastTs = 0;
    // A session torn down while paused must not leave the next clip's targets
    // unclickable - the resume that would have cleared it is never coming.
    attnPaused = false;
    document.body.classList.remove('attn-paused');
  }

  // ---------- inbound protocol ----------

  function doPause() {
    // Attention targets freeze even with no live load: the host can hold the
    // session at any point and a target must never drift on under the card.
    attentionSetPaused(true);
    if (!cur) return;
    cur.hostPaused = true;
    try { fg.pause(); } catch (e) { /* ignore */ }
    try { bg.pause(); } catch (e) { /* ignore */ }
    postTime(true);
  }

  function doResume() {
    attentionSetPaused(false);
    if (!cur || cur.errored || cur.ended) return;
    cur.hostPaused = false;
    // The watchdog clocks restart from here, or a 60s grace pause would look
    // like a 60s stall the moment we resume.
    cur.lastProgressAt = performance.now();
    if (!cur.playingSent) cur.startedAt = performance.now();
    playEl(fg, 'fg');
    if (cur.blur) playEl(bg, 'bg');
  }

  function doSeek(ms) {
    if (!cur || cur.errored) return;
    const n = Number(ms);
    if (!Number.isFinite(n)) return;
    let sec = Math.max(0, n / 1000);
    const d = fg.duration;
    if (Number.isFinite(d) && d > 0) sec = Math.min(sec, Math.max(0, d - 0.05));
    try { fg.currentTime = sec; } catch (e) { log('seek rejected: ' + e); return; }
    if (cur.blur) { try { bg.currentTime = sec; } catch (e) { /* backdrop only */ } }
    cur.ended = false; // seeking back out of the end re-arms normal reporting
    if (ticker == null) startTicker(); // ...including the ticker the end stopped
    cur.lastProgressAt = performance.now();
    // Seeking out of a finished clip leaves the element paused; without this the
    // stall watchdog would then "catch" a video nobody asked to stop.
    if (!cur.hostPaused && fg.paused) {
      playEl(fg, 'fg');
      if (cur.blur) playEl(bg, 'bg');
    }
    postTime(true);
  }

  function onHostMessage(data) {
    // Tolerate PostWebMessageAsString as well as PostWebMessageAsJson.
    if (typeof data === 'string') {
      try { data = JSON.parse(data); } catch (e) { return; }
    }
    if (!data || typeof data !== 'object') return;
    switch (data.type) {
      case 'load':
        startLoad(data);
        break;
      case 'pause':
        doPause();
        break;
      case 'resume':
        doResume();
        break;
      case 'stop':
        teardown();
        break;
      case 'setVolume':
        if (typeof data.volume === 'number') volume = clamp01(data.volume);
        if (typeof data.muted === 'boolean') muted = data.muted; // optional extra
        applyVolume();
        break;
      case 'seek':
        doSeek(data.ms);
        break;
      case 'attentionShow':
        attentionShow(data);
        break;
      case 'attentionHide':
        attentionHide(data);
        break;
      default:
        break;
    }
  }

  // ---------- input ----------

  // Any press anywhere: the host brings whatever it still has in its own
  // windows back to the front (window-level PreviewMouseDown is unreliable over
  // a WebView2 child HWND, so this message is the only signal C# gets).
  //
  // A press ON an attention target is EXCLUSIVELY that target's: it must never
  // also read as a generic surface click, or every hit would additionally
  // trigger the host's z-order lift. Capture phase, so no other listener can
  // reorder this.
  window.addEventListener('pointerdown', (e) => {
    const node = e.target && e.target.closest ? e.target.closest('.attn') : null;
    if (node) {
      e.preventDefault();
      e.stopPropagation();
      attentionHit(attn.get(node.dataset.id));
      return;
    }
    post({ type: 'click' });
  }, true);

  // The page is a surface, not a document.
  window.addEventListener('contextmenu', (e) => e.preventDefault());
  window.addEventListener('selectstart', (e) => e.preventDefault());
  window.addEventListener('dragstart', (e) => e.preventDefault());

  // Keys over a focused WebView2 go to Chromium, not to the WPF window, so the
  // page must never let Chromium act on the ones strict mode cares about. C#
  // decides policy from the {type:'key'} report; we just neutralise them here.
  window.addEventListener('keydown', (e) => {
    const k = e.key;
    const system = e.altKey || e.metaKey;
    const reload = k === 'F5' || (e.ctrlKey && (k === 'r' || k === 'R'));
    if (k === 'Escape' || k === 'F4' || k === 'F11' || system || reload || e.ctrlKey) {
      // Ctrl combos are swallowed wholesale: there is no text input on this page,
      // and Ctrl+R/W/N/P would each kill or hijack the session. (C# should also
      // set AreBrowserAcceleratorKeysEnabled = false; this is the second lock.)
      e.preventDefault();
      e.stopPropagation();
    }
    if (e.repeat) return; // holding a key must not flood the bridge
    post({
      type: 'key',
      key: k,
      alt: !!e.altKey,
      ctrl: !!e.ctrlKey,
      shift: !!e.shiftKey,
    });
  }, true);

  // ---------- containment ----------

  window.addEventListener('error', (e) => {
    // Reported as a log, NOT as a playback error: a script fault does not stop
    // the element, and C#'s duration guillotine / fallback timer still ends the
    // session. Turning it into 'error' would trigger a needless LibVLC replay of
    // a video the user is currently watching.
    log('page script error: ' + (e && e.message ? e.message : 'unknown'));
  });

  // Never call requestFullscreen (plan trap #6) — the host window is already
  // OS-level fullscreen and document.exitFullscreen is unreliable in WebView2.

  // ---------- debug hook (soak checks) ----------

  Object.defineProperty(window, '__ccpPlayerDebug', {
    value: {
      get live() { return cur != null; },
      get url() { return cur ? cur.url : null; },
      get playing() { return !!(cur && cur.playingSent && !fg.paused); },
      get positionMs() { return Math.round((fg.currentTime || 0) * 1000); },
      get durationMs() { return Number.isFinite(fg.duration) ? Math.round(fg.duration * 1000) : 0; },
      get blur() { return !!(cur && cur.blur); },
      get unrequestedPauses() { return cur ? cur.unrequestedPauses : 0; },
      get attentionCount() { return attn.size; },
      get errored() { return !!(cur && cur.errored); },
    },
  });

  // ---------- boot ----------

  if (bridge) {
    bridge.addEventListener('message', (e) => onHostMessage(e.data));
    post({ type: 'ready' });
  }
  // Opened in a plain browser (dev): nothing to do — the page stays black until
  // a host sends a load.
})();
