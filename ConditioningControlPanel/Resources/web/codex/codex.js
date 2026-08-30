/* ==========================================================================
 * codex.js - navigation, rendering, page turn, stamps, bridge.
 *
 * Ask EMI wave 2. See docs/emi-desk/WAVE2-CONTRACT.md; the ids in here are
 * load-bearing and shared with the C# lane.
 *
 * A classic script, no modules, no build step, no framework, NO NETWORK.
 * The only thing this file fetches is ./codex.json and ./chapters/<id>.json,
 * both from the folder the host maps for us. There is no CDN call, no font
 * service, no analytics beacon and no image request anywhere in the bundle.
 *
 * It runs identically with or without a host. window.chrome.webview is
 * treated as optional throughout, so the page opens in a plain browser for
 * development and simply drops its bridge messages on the floor.
 * ========================================================================== */
(function () {
  'use strict';

  /* ------------------------------------------------------------------ *
   * 1. THE BRIDGE
   *
   * Five messages, JS -> C#, exactly the contract's table:
   *
   *   codex:ready   -            log, stop the loading state
   *   codex:open    { chapter }  store the bookmark
   *   codex:target  { id }       EmiTargets.Find(id); locked -> say so
   *   codex:tour    { type }     StartTutorial(Enum.Parse<TutorialType>)
   *   codex:close   -            close the window
   *
   * ENVELOPE. Each message is posted with its fields BOTH flat on the
   * object and mirrored under `payload`:
   *
   *   { type: 'codex:open', chapter: 'first-light',
   *     payload: { chapter: 'first-light' } }
   *
   * That is deliberate. The two lanes were built in parallel against a
   * table that names a message and a payload without pinning the wire
   * shape, and a mismatch here is a silent dead button rather than a loud
   * failure. Reading either shape works; the C# side can pick one and
   * ignore the other, and nothing needs a second round trip to find out.
   *
   * INBOUND is optional and the page never depends on it. Two are
   * understood if the host chooses to send them:
   *   codex:goto { chapter }  jump to a chapter (used to restore the
   *                           bookmark the host kept in EmiState)
   *   codex:note { text }     show one line in the footer, which is how
   *                           "that door is locked" gets back to the page
   * ------------------------------------------------------------------ */
  var webview = (window.chrome && window.chrome.webview) || null;

  function send(type, fields) {
    var payload = {};
    var msg = {};
    if (fields) {
      for (var k in fields) {
        if (Object.prototype.hasOwnProperty.call(fields, k)) {
          payload[k] = fields[k];
          /* NEVER flatten a field called "type". codex:tour's payload key is
             literally `type` (a TutorialType name), and copying it flat
             overwrites the envelope, so the host receives
             {type:"ShortWalk"} and can never tell which message it was.
             It stays in `payload`, where the contract puts it, and rides
             flat under the alias `tour` instead. */
          if (k !== 'type') msg[k] = fields[k];
        }
      }
    }
    msg.type = type;
    msg.payload = payload;
    try {
      if (webview) webview.postMessage(msg);
    } catch (e) {
      /* the host went away mid-click. the book keeps working. */
    }
  }

  if (webview) {
    try {
      webview.addEventListener('message', function (e) {
        var m = e && e.data;
        if (!m || typeof m.type !== 'string') return;
        var body = m.payload && typeof m.payload === 'object' ? m.payload : m;
        if (m.type === 'codex:goto' && typeof body.chapter === 'string') {
          openChapterById(body.chapter, true);
        } else if (m.type === 'codex:note') {
          note(typeof body.text === 'string' ? body.text : '');
        }
      });
    } catch (e) { /* no inbound channel; outbound still works */ }
  }

  /* ------------------------------------------------------------------ *
   * 2. THE FIGURE VOCABULARY
   *
   * Exactly ten, CSS and SVG only, no image asset anywhere. Writers pick
   * from this list and never invent an eleventh, so all ten exist and all
   * ten are drawn deliberately rather than as a placeholder box.
   *
   * They share one 160x100 grid and one shape-rendering, so a chapter that
   * uses three of them still looks like three drawings from one book.
   * They are STATIC. Nothing in a figure moves, because a figure lives on
   * a reading surface and motion in this book is for navigation only.
   * ------------------------------------------------------------------ */
  var FIGURES = {

    /* images arriving on top of whatever you were already doing */
    'stack-drop':
      '<rect x="26" y="66" width="108" height="30" class="f-soft"/>' +
      '<rect x="29" y="69" width="102" height="24" class="f-paper"/>' +
      '<rect x="52" y="40" width="56" height="26" class="f-pink"/>' +
      '<rect x="43" y="22" width="56" height="26" class="f-rose"/>' +
      '<rect x="60" y="6"  width="56" height="26" class="f-ink"/>' +
      '<rect x="18" y="30" width="4" height="10" class="f-soft"/>' +
      '<rect x="18" y="46" width="4" height="6"  class="f-soft"/>' +
      '<rect x="138" y="24" width="4" height="10" class="f-soft"/>' +
      '<rect x="138" y="40" width="4" height="6"  class="f-soft"/>',

    /* something that fires, waits, fires again: a rate */
    'pulse':
      '<rect x="10" y="62" width="140" height="2" class="f-soft"/>' +
      '<rect x="18" y="26" width="6" height="36" class="f-rose"/>' +
      '<rect x="52" y="26" width="6" height="36" class="f-rose"/>' +
      '<rect x="86" y="26" width="6" height="36" class="f-rose"/>' +
      '<rect x="120" y="26" width="6" height="36" class="f-rose"/>' +
      '<rect x="24" y="76" width="28" height="2" class="f-ink"/>' +
      '<rect x="24" y="72" width="2" height="10" class="f-ink"/>' +
      '<rect x="50" y="72" width="2" height="10" class="f-ink"/>' +
      '<rect x="18" y="14" width="6" height="6" class="f-pink"/>' +
      '<rect x="52" y="14" width="6" height="6" class="f-pink"/>' +
      '<rect x="86" y="14" width="6" height="6" class="f-pink"/>' +
      '<rect x="120" y="14" width="6" height="6" class="f-pink"/>',

    /* planes stacked over the desktop: overlays, filters, the screen under it */
    'layers':
      '<path d="M16 76 L80 44 L144 76 L80 108 Z" class="f-soft" transform="translate(0,-14)"/>' +
      '<path d="M16 76 L80 44 L144 76 L80 108 Z" class="f-paper" transform="translate(0,-17)"/>' +
      '<path d="M16 76 L80 44 L144 76 L80 108 Z" class="f-pink" transform="translate(0,-38)"/>' +
      '<path d="M16 76 L80 44 L144 76 L80 108 Z" class="f-paper" transform="translate(0,-41)"/>' +
      '<path d="M16 76 L80 44 L144 76 L80 108 Z" class="f-rose" transform="translate(0,-62)"/>' +
      '<rect x="150" y="14" width="2" height="48" class="f-ink"/>' +
      '<rect x="146" y="14" width="10" height="2" class="f-ink"/>' +
      '<rect x="146" y="60" width="10" height="2" class="f-ink"/>',

    /* a run with a beginning, a middle and an end */
    'timeline':
      '<rect x="12" y="46" width="136" height="8" class="f-soft"/>' +
      '<rect x="12" y="46" width="64" height="8" class="f-rose"/>' +
      '<rect x="10" y="34" width="4" height="32" class="f-ink"/>' +
      '<rect x="74" y="34" width="4" height="32" class="f-ink"/>' +
      '<rect x="144" y="34" width="4" height="32" class="f-ink"/>' +
      '<rect x="8" y="74" width="8" height="8" class="f-rose"/>' +
      '<rect x="72" y="74" width="8" height="8" class="f-pink"/>' +
      '<rect x="142" y="74" width="8" height="8" class="f-soft"/>' +
      '<rect x="30" y="22" width="20" height="4" class="f-pink"/>' +
      '<rect x="96" y="22" width="20" height="4" class="f-soft"/>',

    /* an amount you choose, between none and all of it.
       Deliberately NOT a ring of twelve evenly spaced marks: that reads as
       the clock figure. A dial is a knob with a SWEEP - a track with two
       ends, part of it filled - so the two never say the same thing. */
    'dial':
      '<path d="M50 82 A 38 38 0 1 1 110 82" class="s-soft" stroke-width="6"/>' +
      '<path d="M50 82 A 38 38 0 0 1 104 26" class="s-rose" stroke-width="6"/>' +
      '<circle cx="80" cy="54" r="24" class="f-soft"/>' +
      '<circle cx="80" cy="54" r="20" class="f-paper"/>' +
      '<path d="M80 54 L98 36" class="s-rose"/>' +
      '<rect x="76" y="50" width="8" height="8" class="f-rose"/>' +
      '<rect x="38" y="88" width="10" height="4" class="f-soft"/>' +
      '<rect x="112" y="88" width="10" height="4" class="f-ink"/>',

    /* a short list you work down before you start */
    'checklist':
      '<rect x="22" y="16" width="16" height="16" class="s-ink"/>' +
      '<path d="M26 24 L29 28 L35 19" class="s-rose"/>' +
      '<rect x="48" y="21" width="88" height="5" class="f-soft"/>' +
      '<rect x="22" y="44" width="16" height="16" class="s-ink"/>' +
      '<path d="M26 52 L29 56 L35 47" class="s-rose"/>' +
      '<rect x="48" y="49" width="72" height="5" class="f-soft"/>' +
      '<rect x="22" y="72" width="16" height="16" class="s-ink"/>' +
      '<rect x="48" y="77" width="56" height="5" class="f-soft"/>',

    /* the spiral, drawn the only way this book draws a curve: in squares */
    'spiral':
      '<path class="s-rose" d="M80 54 L80 44 L92 44 L92 64 L66 64 L66 32 L106 32 ' +
      'L106 78 L52 78 L52 20 L120 20 L120 90 L38 90"/>' +
      '<rect x="76" y="50" width="8" height="8" class="f-rose"/>',

    /* sound: level around a centre line, stepped, never a smooth curve */
    'wave':
      '<rect x="8" y="52" width="144" height="2" class="f-soft"/>' +
      '<rect x="14"  y="44" width="8" height="18" class="f-soft"/>' +
      '<rect x="26"  y="34" width="8" height="38" class="f-pink"/>' +
      '<rect x="38"  y="20" width="8" height="66" class="f-rose"/>' +
      '<rect x="50"  y="30" width="8" height="46" class="f-pink"/>' +
      '<rect x="62"  y="42" width="8" height="22" class="f-soft"/>' +
      '<rect x="74"  y="26" width="8" height="54" class="f-rose"/>' +
      '<rect x="86"  y="36" width="8" height="34" class="f-pink"/>' +
      '<rect x="98"  y="46" width="8" height="14" class="f-soft"/>' +
      '<rect x="110" y="30" width="8" height="46" class="f-pink"/>' +
      '<rect x="122" y="40" width="8" height="26" class="f-soft"/>' +
      '<rect x="134" y="48" width="8" height="10" class="f-soft"/>',

    /* coverage filling in, cell by cell */
    'grid-fill': (function () {
      var on = { '0': 1, '1': 1, '2': 1, '3': 1, '4': 1, '5': 1,
                 '6': 1, '7': 1, '8': 1, '9': 1, '10': 1,
                 '12': 1, '13': 1, '14': 1, '18': 1, '19': 1 };
      var s = '', i = 0;
      for (var r = 0; r < 4; r++) {
        for (var c = 0; c < 6; c++, i++) {
          var x = 14 + c * 23, y = 10 + r * 22;
          s += '<rect x="' + x + '" y="' + y + '" width="19" height="18" class="' +
               (on[i] ? 'f-rose' : 'f-soft') + '"/>';
          if (!on[i]) s += '<rect x="' + (x + 2) + '" y="' + (y + 2) +
                           '" width="15" height="14" class="f-paper"/>';
        }
      }
      return s;
    })(),

    /* a time something happens whether you are watching or not */
    'clock':
      '<circle cx="80" cy="52" r="38" class="s-ink"/>' +
      '<rect x="78" y="18" width="4" height="8" class="f-ink"/>' +
      '<rect x="78" y="78" width="4" height="8" class="f-ink"/>' +
      '<rect x="46" y="50" width="8" height="4" class="f-ink"/>' +
      '<rect x="106" y="50" width="8" height="4" class="f-ink"/>' +
      '<path d="M80 52 L80 30" class="s-ink"/>' +
      '<path d="M80 52 L102 62" class="s-rose"/>' +
      '<rect x="77" y="49" width="6" height="6" class="f-rose"/>'
  };

  var FIGURE_KINDS = Object.keys(FIGURES);

  /* small ornaments, also drawn not fetched */
  var SVG_LOCK =
    '<svg class="cdx-lock" viewBox="0 0 11 13" aria-hidden="true">' +
    '<path d="M3 6 V4 a2.5 2.5 0 0 1 5 0 V6" fill="none" stroke="currentColor" stroke-width="1.5"/>' +
    '<rect x="1" y="6" width="9" height="7" fill="currentColor"/></svg>';

  var SVG_ARROW =
    '<svg class="cdx-arrow" viewBox="0 0 62 22" aria-hidden="true">' +
    '<path d="M60 4 C40 4 34 14 12 14"/><path d="M12 14 L20 9"/><path d="M12 14 L20 19"/></svg>';

  /* ------------------------------------------------------------------ *
   * 3. STATE
   * ------------------------------------------------------------------ */
  var READ_KEY = 'codex.read.v1';

  var state = {
    index: null,          // codex.json, once loaded
    flat: [],             // every chapter in reading order: {id,title,vol}
    current: null,        // chapter id on screen
    volume: 1,            // open volume number
    read: Object.create(null),
    stamped: Object.create(null),  // volumes that already wore their stamp
    booted: false
  };

  var el = {};
  function $(id) { return document.getElementById(id); }

  var reduceMotion = false;
  try {
    reduceMotion = window.matchMedia &&
      window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch (e) { reduceMotion = false; }

  /* localStorage is a convenience, not a contract: the host owns the real
     bookmark (codex:open -> EmiState). Some embeddings refuse storage
     outright, so every touch is wrapped and a refusal is not an error. */
  function loadRead() {
    try {
      var raw = window.localStorage.getItem(READ_KEY);
      if (!raw) return;
      var ids = JSON.parse(raw);
      if (!Array.isArray(ids)) return;
      for (var i = 0; i < ids.length; i++) {
        if (typeof ids[i] === 'string') state.read[ids[i]] = true;
      }
    } catch (e) { /* private mode, disabled storage, corrupt value */ }
  }
  function saveRead() {
    try {
      window.localStorage.setItem(READ_KEY, JSON.stringify(Object.keys(state.read)));
    } catch (e) { /* nothing to do and nothing worth saying */ }
  }

  /* ------------------------------------------------------------------ *
   * 4. SMALL DOM HELPERS
   *
   * Chapter text is written by somebody else and lands in this page as
   * data. It is only ever put on screen through textContent - innerHTML
   * is reserved for the SVG constants above, which this file owns.
   * ------------------------------------------------------------------ */
  function make(tag, cls, text) {
    var n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = String(text);
    return n;
  }
  function clear(n) { while (n.firstChild) n.removeChild(n.firstChild); }
  function str(v) { return typeof v === 'string' ? v : ''; }

  var noteTimer = 0;
  function note(text) {
    if (!el.note) return;
    el.note.textContent = str(text);
    if (noteTimer) { clearTimeout(noteTimer); noteTimer = 0; }
    if (text) noteTimer = setTimeout(function () { el.note.textContent = ''; }, 6000);
  }

  /* ------------------------------------------------------------------ *
   * 5. THE INDEX (codex.json)
   * ------------------------------------------------------------------ */
  function fetchJson(url) {
    return fetch(url, { cache: 'no-store' }).then(function (r) {
      if (!r.ok) {
        var err = new Error('http ' + r.status);
        err.kind = r.status === 404 ? 'missing' : 'http';
        throw err;
      }
      return r.text().then(function (t) {
        try {
          return JSON.parse(t);
        } catch (e) {
          var pe = new Error(e.message);
          pe.kind = 'parse';
          throw pe;
        }
      });
    }, function (e) {
      /* fetch itself refused: offline host gone, or file:// with no server */
      var ne = new Error(e && e.message ? e.message : 'request failed');
      ne.kind = location.protocol === 'file:' ? 'file' : 'missing';
      throw ne;
    });
  }

  function normaliseIndex(raw) {
    var vols = (raw && Array.isArray(raw.volumes)) ? raw.volumes : [];
    var out = [];
    for (var i = 0; i < vols.length; i++) {
      var v = vols[i] || {};
      var chapters = [];
      var list = Array.isArray(v.chapters) ? v.chapters : [];
      for (var j = 0; j < list.length; j++) {
        var c = list[j];
        if (typeof c === 'string') chapters.push({ id: c, title: c });
        else if (c && typeof c.id === 'string') chapters.push({ id: c.id, title: str(c.title) || c.id });
      }
      out.push({
        n: typeof v.n === 'number' ? v.n : (i + 1),
        roman: str(v.roman) || roman(i + 1),
        title: str(v.title),
        locked: !!v.locked || chapters.length === 0,
        chapters: chapters
      });
    }
    return out;
  }

  function roman(n) {
    return ['', 'I', 'II', 'III', 'IV', 'V', 'VI', 'VII', 'VIII'][n] || String(n);
  }

  function volumeOf(n) {
    for (var i = 0; i < state.index.length; i++) {
      if (state.index[i].n === n) return state.index[i];
    }
    return null;
  }

  function flatten() {
    state.flat = [];
    for (var i = 0; i < state.index.length; i++) {
      var v = state.index[i];
      if (v.locked) continue;
      for (var j = 0; j < v.chapters.length; j++) {
        state.flat.push({ id: v.chapters[j].id, title: v.chapters[j].title, vol: v.n });
      }
    }
  }

  /* ------------------------------------------------------------------ *
   * 6. THE SPINE
   * ------------------------------------------------------------------ */
  function buildSpine() {
    clear(el.spine);
    for (var i = 0; i < state.index.length; i++) {
      (function (v) {
        var tab = make('div', 'cdx-tab');
        tab.setAttribute('role', 'button');
        tab.setAttribute('tabindex', v.locked ? '-1' : '0');
        tab.dataset.vol = String(v.n);

        var label = v.locked
          ? 'volume ' + v.roman + ', not written yet'
          : 'volume ' + v.roman + ', ' + (v.title || '');
        tab.setAttribute('aria-label', label);
        tab.title = label;

        tab.appendChild(make('span', 'cdx-tab-num', v.roman));

        if (v.locked) {
          tab.classList.add('is-locked');
          var lock = document.createElement('span');
          lock.innerHTML = SVG_LOCK;          /* our own constant, not data */
          tab.appendChild(lock.firstChild);
        } else {
          tab.appendChild(make('span', 'cdx-tab-title', v.title));
          tab.addEventListener('click', function () { openVolume(v.n); });
          tab.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openVolume(v.n); }
          });
        }
        el.spine.appendChild(tab);
      })(state.index[i]);
    }
    paintSpine();
  }

  /* A volume is finished when every chapter in it has been read. The stamp
     lands ONCE, the first time that becomes true while the book is open;
     after that it is simply there, because a stamp that re-lands every time
     you glance at the tab is motion on a reading surface. */
  function paintSpine() {
    var tabs = el.spine.querySelectorAll('.cdx-tab');
    for (var i = 0; i < tabs.length; i++) {
      var tab = tabs[i];
      var n = parseInt(tab.dataset.vol, 10);
      var v = volumeOf(n);
      tab.classList.toggle('is-open', n === state.volume);
      if (!v || v.locked) continue;

      var done = v.chapters.length > 0;
      for (var j = 0; j < v.chapters.length; j++) {
        if (!state.read[v.chapters[j].id]) { done = false; break; }
      }

      var stamp = tab.querySelector('.cdx-stamp');
      if (done && !stamp) {
        stamp = make('div', 'cdx-stamp', 'read');
        if (!reduceMotion && state.booted && !state.stamped[n]) {
          stamp.classList.add('is-landing');
        }
        state.stamped[n] = true;
        tab.appendChild(stamp);
      } else if (!done && stamp) {
        tab.removeChild(stamp);
        state.stamped[n] = false;
      }
    }
  }

  /* ------------------------------------------------------------------ *
   * 7. THE CHAPTER STRIP AND THE RUNNING HEAD
   * ------------------------------------------------------------------ */
  function paintHead() {
    var v = volumeOf(state.volume);
    if (!v) return;
    /* the numeral stays a numeral: no css lowercasing on the running head,
       or volume I reads as "vol i" */
    el.runhead.textContent = 'vol ' + v.roman + '  .  ' +
      ((v.title || 'not written yet').toLowerCase());

    clear(el.strip);
    for (var i = 0; i < v.chapters.length; i++) {
      (function (c, n) {
        var chip = make('div', 'cdx-chip', String(n));
        chip.setAttribute('role', 'button');
        chip.setAttribute('tabindex', '0');
        chip.title = c.title || c.id;
        chip.setAttribute('aria-label', c.title || c.id);
        if (state.read[c.id]) chip.classList.add('is-read');
        if (c.id === state.current) chip.classList.add('is-here');
        chip.addEventListener('click', function () { openChapterById(c.id); });
        chip.addEventListener('keydown', function (e) {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openChapterById(c.id); }
        });
        el.strip.appendChild(chip);
      })(v.chapters[i], i + 1);
    }
  }

  /* ------------------------------------------------------------------ *
   * 8. THE FOOTER
   * ------------------------------------------------------------------ */
  function paintFoot() {
    var total = state.flat.length;
    var read = 0;
    for (var i = 0; i < state.flat.length; i++) if (state.read[state.flat[i].id]) read++;

    el.progressText.textContent = total ? ('read ' + read + ' of ' + total) : 'no pages yet';

    clear(el.meter);
    for (var j = 0; j < state.flat.length; j++) {
      var cell = document.createElement('i');
      if (state.read[state.flat[j].id]) cell.className = 'is-read';
      if (state.flat[j].id === state.current) cell.className = 'is-here';
      el.meter.appendChild(cell);
    }
  }

  /* The two buttons exist only when the chapter declares the thing they do.
     A button that is always there and sometimes does nothing teaches people
     to stop pressing buttons. */
  function paintActions(ch) {
    var tour = ch && typeof ch.tour === 'string' && ch.tour ? ch.tour : null;
    var target = ch && typeof ch.target === 'string' && ch.target ? ch.target : null;

    el.tourBtn.hidden = !tour;
    el.targetBtn.hidden = !target;
    el.tourBtn.dataset.tour = tour || '';
    el.targetBtn.dataset.target = target || '';
  }

  /* ------------------------------------------------------------------ *
   * 9. THE MARGIN
   * ------------------------------------------------------------------ */
  function paintMargin(ch) {
    clear(el.margin);
    var m = ch && ch.margin && typeof ch.margin === 'object' ? ch.margin : null;
    var t = m ? str(m.t) : '';
    var face = m ? str(m.face) : '';

    if (!t && !face) {
      el.margin.appendChild(make('div', 'cdx-margin-empty', 'no note'));
      return;
    }
    if (face) el.margin.appendChild(make('div', 'cdx-face', face));
    if (t) {
      var wrap = document.createElement('div');
      wrap.innerHTML = SVG_ARROW;             /* our own constant, not data */
      el.margin.appendChild(wrap.firstChild);
      el.margin.appendChild(make('div', 'cdx-hand', t));
    }
  }

  /* ------------------------------------------------------------------ *
   * 10. THE BLOCKS
   *
   * Every type in the schema renders: p, steps, figure, callout, limit.
   * An unknown type is shown as an unknown type rather than dropped, so a
   * schema drift is visible on the page instead of silently eating prose.
   * ------------------------------------------------------------------ */
  function renderBlock(b) {
    if (!b || typeof b !== 'object') return null;

    switch (b.type) {

      case 'p':
        return make('p', 'cdx-p', str(b.text));

      case 'steps': {
        var ol = make('ol', 'cdx-steps');
        var items = Array.isArray(b.items) ? b.items : [];
        for (var i = 0; i < items.length; i++) {
          ol.appendChild(make('li', null, str(items[i])));
        }
        if (!items.length) return null;
        return ol;
      }

      case 'figure': {
        var kind = str(b.kind);
        var fig = make('figure', 'cdx-fig fig-' + (kind || 'unknown'));
        if (FIGURES[kind]) {
          var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
          svg.setAttribute('viewBox', '0 0 160 100');
          svg.setAttribute('role', 'img');
          svg.setAttribute('aria-label', str(b.caption) || kind);
          svg.innerHTML = FIGURES[kind];      /* our own constant, not data */
          fig.appendChild(svg);
        } else {
          /* the vocabulary is closed on purpose. an eleventh kind says so
             out loud, and names the ten, rather than leaving a hole. */
          var miss = make('div', 'cdx-fixture', 'no figure "' + (kind || '?') + '"');
          fig.appendChild(miss);
          fig.appendChild(make('figcaption', null, 'the ten are: ' + FIGURE_KINDS.join(', ')));
          return fig;
        }
        if (b.caption) fig.appendChild(make('figcaption', null, str(b.caption)));
        return fig;
      }

      case 'callout': {
        var co = make('div', 'cdx-callout');
        co.appendChild(make('span', 'cdx-tag', 'note'));
        co.appendChild(make('p', null, str(b.text)));
        return co;
      }

      case 'limit': {
        var li = make('div', 'cdx-limit');
        li.appendChild(make('span', 'cdx-tag', 'the limit'));
        li.appendChild(make('p', null, str(b.text)));
        return li;
      }

      default: {
        var un = make('div', 'cdx-limit');
        un.appendChild(make('span', 'cdx-tag', 'unknown block'));
        un.appendChild(make('p', null,
          'this page asked for a "' + str(b.type) + '" block, which this book does not know how to draw.'));
        return un;
      }
    }
  }

  function renderChapter(ch) {
    clear(el.body);

    if (ch.fixture) {
      el.body.appendChild(make('div', 'cdx-fixture', 'fixture page - not the written chapter'));
    }

    el.body.appendChild(make('h1', 'cdx-title', str(ch.title) || ch.id));
    if (ch.blurb) el.body.appendChild(make('div', 'cdx-blurb', str(ch.blurb)));
    el.body.appendChild(make('div', 'cdx-rule'));

    var blocks = Array.isArray(ch.blocks) ? ch.blocks : [];
    for (var i = 0; i < blocks.length; i++) {
      var n = renderBlock(blocks[i]);
      if (n) el.body.appendChild(n);
    }
    if (!blocks.length) {
      el.body.appendChild(make('p', 'cdx-p', 'this chapter has a title and no body yet.'));
    }
    el.body.scrollTop = 0;
  }

  /* A file that is absent, unreadable or not the shape we expect says so on
     the page, in the book's own voice, with the reason underneath. There is
     no state in which the reader gets a blank screen. */
  function renderMissing(id, err) {
    clear(el.body);
    el.margin && clear(el.margin);

    var kind = (err && err.kind) || 'missing';
    var head, body;

    if (kind === 'file') {
      head = 'no pages from here';
      body = 'the book is open from a file:// path, and a browser will not read the ' +
             'chapter files that way. In the app this never happens - the host serves ' +
             'the folder. To read it here, serve this folder over http and open it again.';
    } else if (kind === 'parse') {
      head = 'this page is damaged';
      body = 'the chapter file exists but it is not valid json, so nothing can be laid out from it.';
    } else if (kind === 'shape') {
      head = 'this page is damaged';
      body = 'the chapter file was read, but it does not carry the fields a chapter needs.';
    } else {
      head = 'not written yet';
      body = 'this chapter is listed in the book but its page has not been written. ' +
             'Nothing is broken; there is simply nothing here to read.';
    }

    var wrap = make('div', 'cdx-missing');
    wrap.appendChild(make('h1', 'cdx-title', head));
    wrap.appendChild(make('div', 'cdx-blurb', id));
    wrap.appendChild(make('div', 'cdx-rule'));
    wrap.appendChild(make('p', 'cdx-p', body));
    wrap.appendChild(make('pre', null,
      'chapters/' + id + '.json\n' + (err && err.message ? err.message : 'not found')));
    el.body.appendChild(wrap);
    el.body.scrollTop = 0;

    el.margin.appendChild(make('div', 'cdx-margin-empty', 'no note'));
    paintActions(null);
  }

  /* ------------------------------------------------------------------ *
   * 11. NAVIGATION AND THE PAGE TURN
   *
   * The wipe is the only motion in the book and it only ever runs because
   * somebody navigated. The content swap happens at the midpoint, under
   * the curtain, so the change is never seen happening. Under reduced
   * motion the curtain does not exist and the swap is immediate.
   * ------------------------------------------------------------------ */
  var turning = false;

  function turnPage(swap) {
    if (reduceMotion || !el.wipe) { swap(); return; }
    if (turning) { swap(); return; }
    turning = true;

    el.wipe.classList.remove('is-turning');
    void el.wipe.offsetWidth;              /* restart the animation */
    el.wipe.classList.add('is-turning');

    setTimeout(swap, 90);                  /* the midpoint of the 180ms wipe */
    setTimeout(function () {
      el.wipe.classList.remove('is-turning');
      turning = false;
    }, 200);
  }

  function indexOfChapter(id) {
    for (var i = 0; i < state.flat.length; i++) if (state.flat[i].id === id) return i;
    return -1;
  }

  function openVolume(n) {
    var v = volumeOf(n);
    if (!v || v.locked || !v.chapters.length) return;
    if (n === state.volume && state.current) return;
    /* land on the first chapter of the volume you have not read, or its first */
    var pick = v.chapters[0].id;
    for (var i = 0; i < v.chapters.length; i++) {
      if (!state.read[v.chapters[i].id]) { pick = v.chapters[i].id; break; }
    }
    openChapterById(pick);
  }

  function openChapterById(id, quiet) {
    if (!id || typeof id !== 'string') return;

    var at = indexOfChapter(id);
    var vol = at >= 0 ? state.flat[at].vol : state.volume;

    fetchJson('./chapters/' + encodeURIComponent(id) + '.json').then(function (ch) {
      if (!ch || typeof ch !== 'object' || !Array.isArray(ch.blocks)) {
        var e = new Error('a chapter needs an object with a blocks array');
        e.kind = 'shape';
        throw e;
      }
      turnPage(function () {
        state.current = id;
        state.volume = typeof ch.volume === 'number' ? ch.volume : vol;
        renderChapter(ch);
        paintMargin(ch);
        paintActions(ch);
        /* one screen, one chapter: rendering it IS reading it */
        state.read[id] = true;
        saveRead();
        paintHead();
        paintFoot();
        paintSpine();
        if (!quiet) send('codex:open', { chapter: id });
      });
    }).catch(function (err) {
      turnPage(function () {
        state.current = id;
        state.volume = vol;
        renderMissing(id, err);
        paintHead();
        paintFoot();
        paintSpine();
      });
    });
  }

  function step(delta) {
    var at = indexOfChapter(state.current);
    if (at < 0) { if (state.flat.length) openChapterById(state.flat[0].id); return; }
    var next = at + delta;
    if (next < 0 || next >= state.flat.length) return;
    openChapterById(state.flat[next].id);
  }

  /* ------------------------------------------------------------------ *
   * 12. BOOT
   * ------------------------------------------------------------------ */
  function firstChapter() {
    /* the host may hand us a bookmark on the url; otherwise page one */
    var want = '';
    try {
      var q = new URLSearchParams(location.search);
      want = str(q.get('chapter')) || str((location.hash || '').replace(/^#/, ''));
    } catch (e) { want = str((location.hash || '').replace(/^#/, '')); }

    /* An explicitly asked-for chapter wins even when the index does not list
       it. A bookmark for a chapter that has since been renamed then lands on
       the "not written yet" page, which is the truth, instead of silently
       dumping the reader on page one as though nothing had happened. */
    if (want) return want;
    return state.flat.length ? state.flat[0].id : '';
  }

  function bootFailed(err) {
    /* Even the index failing is a page, never a hole. */
    clear(el.body);
    var wrap = make('div', 'cdx-missing');
    wrap.appendChild(make('h1', 'cdx-title', 'the book will not open'));
    wrap.appendChild(make('div', 'cdx-blurb', 'codex.json'));
    wrap.appendChild(make('div', 'cdx-rule'));
    wrap.appendChild(make('p', 'cdx-p',
      (err && err.kind === 'file')
        ? 'the book is open from a file:// path and a browser will not read its index that way. ' +
          'In the app the host serves this folder. To read it here, serve the folder over http.'
        : 'the index that lists the volumes could not be read, so there is no book to lay out. ' +
          'Nothing else is wrong with the app.'));
    wrap.appendChild(make('pre', null, 'codex.json\n' + (err && err.message ? err.message : 'not found')));
    el.body.appendChild(wrap);
    el.margin.appendChild(make('div', 'cdx-margin-empty', 'no note'));
    el.progressText.textContent = 'no pages yet';
  }

  function boot() {
    el.spine = $('cdx-spine');
    el.page = $('cdx-page');
    el.body = $('cdx-body');
    el.margin = $('cdx-margin');
    el.strip = $('cdx-strip');
    el.runhead = $('cdx-runhead');
    el.foot = $('cdx-foot');
    el.note = $('cdx-note');
    el.progressText = $('cdx-progress-text');
    el.meter = $('cdx-progress-meter');
    el.tourBtn = $('cdx-tour');
    el.targetBtn = $('cdx-target');
    el.wipe = $('cdx-wipe');

    loadRead();

    /* the two buttons, and the only two places this page asks C# for a door */
    el.tourBtn.addEventListener('click', function () {
      var t = el.tourBtn.dataset.tour;
      if (!t) return;
      send('codex:tour', { type: t, tour: t });
    });
    el.targetBtn.addEventListener('click', function () {
      var t = el.targetBtn.dataset.target;
      if (!t) return;
      send('codex:target', { id: t });
    });

    document.addEventListener('keydown', function (e) {
      if (e.defaultPrevented) return;
      if (e.key === 'Escape') { send('codex:close'); return; }
      if (e.key === 'ArrowRight' || e.key === 'PageDown') { e.preventDefault(); step(1); }
      else if (e.key === 'ArrowLeft' || e.key === 'PageUp') { e.preventDefault(); step(-1); }
    });

    fetchJson('./codex.json').then(function (raw) {
      state.index = normaliseIndex(raw);
      if (!state.index.length) {
        var e = new Error('codex.json lists no volumes');
        e.kind = 'shape';
        throw e;
      }
      flatten();
      buildSpine();

      var first = firstChapter();
      if (first) {
        var at = indexOfChapter(first);
        state.volume = at >= 0 ? state.flat[at].vol : state.index[0].n;
        /* not quiet: the host should learn which page the book actually
           opened on, including when the book picked page one itself */
        openChapterById(first);
      } else {
        state.volume = state.index[0].n;
        paintHead();
        paintFoot();
        renderMissing('(none)', { kind: 'missing', message: 'the book lists no chapters' });
      }
      /* the stamp only LANDS after boot; a book you reopen is already stamped */
      setTimeout(function () { state.booted = true; }, 0);
      send('codex:ready');
    }).catch(function (err) {
      bootFailed(err);
      send('codex:ready');
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
