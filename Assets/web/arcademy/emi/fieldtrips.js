/* ============================================================================
 * emi/fieldtrips.js - THE POI REGISTRY AND THE SCHEDULER (wave W2a).
 *
 * `widget.apparate()` is HOW a trip happens. This file is WHEN, and the whole
 * design brief for it is one sentence: a scripted rare delight, not wandering.
 * If a player sees two of these in a night, or one on their first night, or one
 * while a class is up, the feature has failed even though every frame of it
 * rendered correctly.
 *
 * ---------------------------------------------------------------------------
 * THE FIVE GATES, and each one is a different kind of no
 *
 *   1. NEVER BEFORE THE THIRD SESSION. `voice.sessions >= TRIP_FROM_SESSION`.
 *      The counter is voice.js's - the same spine `sessionAtLeast` hangs off -
 *      and it is read, never minted. A second counter in a second blob is how
 *      two ledgers come to disagree about what night it is.
 *   2. ONE TRIP PER SESSION. A module-scope count, deliberately NOT persisted:
 *      "once a sitting" is a fact about this sitting.
 *   3. ONE VISIT PER FIXTURE, FOR EVER. Through voice.js's `seen` ledger
 *      (`hasSeen`/`markSeen`), which is the same map every one-shot beat uses.
 *      A POI id is therefore in the same namespace as a beat id: keep the
 *      `trip_` prefix and they cannot collide.
 *   4. THE RIGHT SCREEN, AND THE FIXTURE ACTUALLY THERE. Each entry declares
 *      the screen it lives on and answers a LIVE rect getter; the scheduler
 *      measures at candidate time and drops anything with no box.
 *   5. TRULY IDLE. No say, no chain, no press, not dismissed, not disabled,
 *      page visible. Most of that is `apparate`'s own refusal - the widget owns
 *      the truth about its own verbs - so this file asks about the two things
 *      the widget cannot see: the document's visibility and the screen.
 *
 * ---------------------------------------------------------------------------
 * WHAT WAKES IT, AND WHY IT IS NOT A TIMER
 *
 * There is NO interval and NO observer here, on purpose. The campus already
 * emits the exact edge this feature wants: `campus.js` fires
 * `fireMoment('idlePlayer', {where:'hub'})` when its attract loop decides the
 * player has gone quiet ON THE CAMPUS, with the comment "a mascot does not get
 * a second idle timer". So the scheduler is a passive `offer(name, payload)`
 * that `emi/index.js` calls on that moment and nothing else. At rest this file
 * costs nothing at all: no rects are measured, no callbacks are registered, and
 * the registry is a frozen array nobody walks.
 *
 * The one thing it registers is `widget.setPoiRects` - a getter the widget
 * calls ONCE per drag, for the carried `*_*`. That is also not a poll.
 *
 * ---------------------------------------------------------------------------
 * THE REGISTRY
 *
 *   { id, screen, anchor(), lineKey, seenKey }
 *
 *   id       stable, `trip_<place>`.
 *   screen   the shell screen this fixture lives on ('board' IS the campus).
 *   sel      the CSS selector `anchor` is built from, carried as data so a suite
 *            can audit what the registry actually points at (the `.facility`
 *            audit below is the reason it exists - a closure cannot be read).
 *   anchor   () => a LIVE rect getter, i.e. a function returning a function.
 *            Two levels on purpose, and it is the wave's central trap (73):
 *            `apparate` resolves the rect INSIDE the power-off, one frame
 *            before she lands, because a rect measured when the trip was
 *            offered has moved by the time the tube comes back on.
 *   lineKey  a key in barks.js's FIELD_TRIPS table. No row = no trip.
 *   seenKey  the ledger flag. Renaming one re-opens the visit. Don't.
 *
 * WHY THESE SIX. They are the fixtures that are always in the DOM on a campus
 * that has booted at all: two HTML nodes the campus builds unconditionally (the
 * timetable plaque, the student ID card), two pure-scenery SVG groups that
 * `campus.update()` never rebuilds (the bell tower, the notice-board dressing),
 * and the two class rooms whose `data-game` attribute campus.js declares
 * update-stable. Everything volatile is out: the route stops are rebuilt on
 * every update, the west-wing rooms do not exist unless Semester III opens, and
 * a sealed wing changes class the night its semester does.
 *
 * AND THE ONE THAT IS OUT ON PURPOSE. Records is `g.campus-room.facility
 * .records`, it is a perfectly stable node, and it is OFF LIMITS. voice.js's
 * geofence (SILENT_TARGETS) has no "probably not" branch and neither does this
 * file: EMI has no reaction to that door, so she does not go and stand next to
 * it either. The Registrar and the Entrance Hall are the same `.facility` class
 * with nothing to tell them apart, so they are out with it.
 * ==========================================================================*/

/* ---------------------- dials ------------------------------------------ */
export const TRIP_DIALS = Object.freeze({
  TRIP_FROM_SESSION: 3,     // gate 1: never on the first two sittings
  TRIPS_PER_SESSION: 1,     // gate 2
  TRIP_ODDS: 0.5,           // ...and even then, only half the eligible idles
  MIN_ON_SCREEN_PX: 24,     // a fixture smaller than this is not somewhere to go
});

/** The moments this module is willing to be woken by. */
export const TRIP_TRIGGERS = Object.freeze(['idlePlayer']);

/** The moment fired when she gets HOME from a trip (story.js b28 rides it). */
export const TRIP_HOME_MOMENT = 'fieldTripHome';

/** The campus root. Present exactly while the campus screen is mounted. */
const CAMPUS = '.campus-stage';

/** One-shot `querySelector` -> rect, the house pattern (voice.js `hitTest`):
 *  measure at event time inside a try/catch that answers null. */
function bySelector(sel) {
  return () => {
    try {
      if (typeof document === 'undefined' || !document.querySelector) return null;
      const node = document.querySelector(sel);
      if (!node || !node.getBoundingClientRect) return null;
      return node.getBoundingClientRect();
    } catch (e) { return null; }
  };
}

/* ---------------------- the registry ----------------------------------- */
export const POIS = Object.freeze([
  {
    // The hanging timetable plaque. A real <button>, built unconditionally.
    id: 'trip_timetable',
    screen: 'board',
    sel: CAMPUS + ' .campus-boardtab',
    anchor() { return bySelector(this.sel); },
    lineKey: 'timetable',
    seenKey: 'trip_timetable',
  },
  {
    // The bell tower. Pure scenery: campus.update() never touches it.
    id: 'trip_belltower',
    screen: 'board',
    sel: CAMPUS + ' g.campus-tower',
    anchor() { return bySelector(this.sel); },
    lineKey: 'belltower',
    seenKey: 'trip_belltower',
  },
  {
    // The Entrance Hall dressing - the felt board and its four pins. NOTE this
    // is `campus-halldress`, the SCENERY group, and NOT the clickable hall,
    // which is a `.facility` and geofenced.
    id: 'trip_noticeboard',
    screen: 'board',
    sel: CAMPUS + ' g.campus-halldress',
    anchor() { return bySelector(this.sel); },
    lineKey: 'noticeboard',
    seenKey: 'trip_noticeboard',
  },
  {
    // The student ID card, bottom-left. HTML, always present.
    id: 'trip_idcard',
    screen: 'board',
    sel: CAMPUS + ' .campus-idcard',
    anchor() { return bySelector(this.sel); },
    lineKey: 'idcard',
    seenKey: 'trip_idcard',
  },
  {
    // Room 101, Homeroom. A Semester I class, so it is dealt every night.
    // `data-game` is the stable hook: update() rewrites the room's CLASS
    // (open/retake/dark) and never the attribute.
    id: 'trip_homeroom',
    screen: 'board',
    sel: CAMPUS + ' g.campus-room[data-game="daily_trigger"]',
    anchor() { return bySelector(this.sel); },
    lineKey: 'homeroom',
    seenKey: 'trip_homeroom',
  },
  {
    // Room 201, The Sorting Room. Semester II, so on a build with that semester
    // closed the node is simply ABSENT - the rect getter answers null and the
    // scheduler drops the entry. That is the gate working, not a bug.
    id: 'trip_sortroom',
    screen: 'board',
    sel: CAMPUS + ' g.campus-room[data-game="sort"]',
    anchor() { return bySelector(this.sel); },
    lineKey: 'sortroom',
    seenKey: 'trip_sortroom',
  },
]);

/* ---------------------- helpers ---------------------------------------- */
/* ----------------------------------------------------------------------------
 * SHE IS GOING SOMEWHERE. Two cues, both on the trip itself.
 * shell/audio.js holds the only audio node on the page (trap 18), so this is a
 * REQUEST on `document` and never a sound - the exact defensive shape
 * shell/ceremonies.js sfx() set. A dropped cue is not an error.
 * -------------------------------------------------------------------------- */
/* The CRT power-off is the picture; this is the air it moves. Departure and
 * return are the same sweep, the way home pitched down a little so the pair
 * reads as one round trip rather than as two separate events. A CANCELLED trip
 * never sounds its return - she did not get home.
 */
function sfx(name, level, extra) {
  try {
    if (typeof document === 'undefined' || typeof document.dispatchEvent !== 'function') return;
    const Ctor = (typeof CustomEvent === 'function') ? CustomEvent : null;
    if (!Ctor) return;
    document.dispatchEvent(new Ctor('arcademy-sfx', {
      detail: Object.assign(
        { name: String(name || 'blip'), level: Number(level) || 0.5, bus: 'fx' },
        extra || {}
      ),
    }));
  } catch (e) { /* a cue must never be the thing that throws */ }
}

function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }

/** Is the page actually being looked at? An idle edge on a hidden tab is not
 *  an audience, and a trip nobody saw still spends the POI for ever. */
function pageVisible() {
  try {
    if (typeof document === 'undefined') return true;
    if (typeof document.visibilityState !== 'string') return true;
    return document.visibilityState === 'visible';
  } catch (e) { return true; }
}

/** A rect big enough, and on screen enough, to be worth walking to. */
function usableRect(r, minPx) {
  if (!isObj(r)) return false;
  const w = Number(r.width);
  const h = Number(r.height);
  if (!(w >= minPx) || !(h >= minPx)) return false;
  let vw = 1280;
  let vh = 800;
  try {
    if (typeof window !== 'undefined') {
      vw = Number(window.innerWidth) || vw;
      vh = Number(window.innerHeight) || vh;
    }
  } catch (e) { /* noop */ }
  const left = Number(r.left);
  const top = Number(r.top);
  if (!Number.isFinite(left) || !Number.isFinite(top)) return false;
  // Fully off one edge is not visible. Partly clipped is fine - she stands
  // beside what IS on screen and apparate clamps her the rest of the way.
  return left + w > 0 && top + h > 0 && left < vw && top < vh;
}

/* ============================================================================
 * createFieldTrips
 * ==========================================================================*/
/**
 * @param {Object} o
 * @param {Object}   o.widget    the widget handle (apparate / setPoiRects / tripping)
 * @param {Object=}  o.voice     emi/voice.js - the sessions counter and the seen ledger
 * @param {Function=} o.screen   () => the shell's current screen name
 * @param {Function=} o.fire     (name, payload) - how the return beat is announced
 * @param {Object=}  o.lines     the FIELD_TRIPS table (injected; else barks.js)
 * @param {Array=}   o.pois      the registry (injected; else POIS)
 * @param {Function=} o.rng      () => 0..1
 * @param {Object=}  o.dials
 * @param {Function=} o.log
 */
export function createFieldTrips(o = {}) {
  const say = typeof o.log === 'function' ? o.log : () => {};
  const widget = o.widget;
  if (!widget || typeof widget.apparate !== 'function') return null;

  const D = Object.assign({}, TRIP_DIALS, isObj(o.dials) ? o.dials : {});
  const rng = typeof o.rng === 'function' ? o.rng : Math.random;
  const voice = isObj(o.voice) ? o.voice : null;
  const fire = typeof o.fire === 'function' ? o.fire : () => false;
  const screenOf = typeof o.screen === 'function' ? o.screen : null;

  const pois = (Array.isArray(o.pois) ? o.pois : POIS).filter(
    (p) => isObj(p) && typeof p.id === 'string' && typeof p.anchor === 'function');

  /* THE LINES ARE OPTIONAL AND LATE, like every other data module in this
   * folder (`loadOptional`'s discipline). A missing barks.js costs the trips
   * and nothing else - a POI with no line simply never becomes a candidate. */
  let LINES = isObj(o.lines) ? o.lines : null;
  if (!LINES) {
    import('./barks.js')
      .then((m) => { if (m && isObj(m.FIELD_TRIPS)) LINES = m.FIELD_TRIPS; })
      .catch((e) => say('emi trips: barks.js unavailable (' + ((e && e.message) || e) + ')'));
  }

  /* SESSION STATE, deliberately not persisted. "Once a sitting" is a fact about
   * the sitting; banking it would make a reload the way to farm a second one. */
  let tripsThisSession = 0;
  let last = null;              // {id, at} - debug/read-only
  let cancelLive = null;

  function seen(key) {
    if (!voice || typeof voice.hasSeen !== 'function') return false;
    try { return !!voice.hasSeen(key); } catch (e) { return false; }
  }
  function bank(key) {
    if (!voice || typeof voice.markSeen !== 'function') return false;
    try { return !!voice.markSeen(key); } catch (e) { return false; }
  }
  function sessions() {
    if (!voice) return 0;
    const n = Number(voice.sessions);
    return Number.isFinite(n) ? n : 0;
  }
  function lineFor(key) {
    if (!LINES || typeof key !== 'string') return null;
    const row = Object.prototype.hasOwnProperty.call(LINES, key) ? LINES[key] : null;
    if (!isObj(row) || typeof row.t !== 'string' || !row.t.trim()) return null;
    return row;
  }

  /** Gate 4a: is this POI's screen the one that is up? With no screen getter
   *  the campus root itself answers - it is in the DOM exactly while it is up. */
  function onScreen(p) {
    if (screenOf) {
      let cur = null;
      try { cur = screenOf(); } catch (e) { cur = null; }
      if (typeof cur === 'string' && cur !== p.screen) return false;
    }
    return true;
  }

  /**
   * EVERY POI THAT COULD BE VISITED RIGHT NOW, measured. This is the only place
   * that touches the DOM, and it runs only on an offered idle edge.
   */
  function candidates() {
    const out = [];
    for (const p of pois) {
      if (seen(p.seenKey || p.id)) continue;
      if (!lineFor(p.lineKey)) continue;
      if (!onScreen(p)) continue;
      let get = null;
      try { get = p.anchor(); } catch (e) { continue; }
      if (typeof get !== 'function') continue;
      let r = null;
      try { r = get(); } catch (e) { continue; }
      if (!usableRect(r, D.MIN_ON_SCREEN_PX)) continue;
      out.push({ poi: p, get });
    }
    return out;
  }

  /** Everything the WIDGET needs for the carried `*_*`: live rects, no gating.
   *  A fixture she has already visited is still a fixture she likes. */
  function poiRects() {
    const out = [];
    for (const p of pois) {
      let get = null;
      try { get = p.anchor(); } catch (e) { continue; }
      if (typeof get !== 'function') continue;
      let r = null;
      try { r = get(); } catch (e) { continue; }
      if (r && Number(r.width) > 0 && Number(r.height) > 0) out.push(r);
    }
    return out;
  }
  try { widget.setPoiRects(poiRects); } catch (e) { /* noop */ }

  /** Force one, ignoring the dice and the session cap. Test/debug seam. */
  function go(id) {
    const list = candidates();
    const pick = list.find((c) => c.poi.id === id) || null;
    return pick ? launch(pick) : false;
  }

  function launch(c) {
    const row = lineFor(c.poi.lineKey);
    if (!row) return false;
    const key = c.poi.seenKey || c.poi.id;
    /* THE LEDGER IS BANKED ON DEPARTURE, NOT ON ARRIVAL. A trip that is
     * cancelled half way (a finger, a resize) still SHOWED the player the
     * power-off, and re-offering the same fixture ten seconds later would read
     * as a stutter, not as a second chance. One offer, one fixture, for ever. */
    const cancel = widget.apparate(c.get, {
      line: row.t,
      face: row.face,
      onDone: (info) => {
        cancelLive = null;
        if (info && info.cancelled) return;
        sfx('whoosh', 0.3, { pitch: 0.9 });
        /* HOME AGAIN. The beat rides the ordinary moment path, so voice.js owns
         * whether b28 is spent - this file never writes a story flag. */
        try { fire(TRIP_HOME_MOMENT, { id: c.poi.id, lineKey: c.poi.lineKey }); }
        catch (e) { /* a mascot may never break a screen transition */ }
      },
    });
    if (!cancel) return false;
    // DEPARTURE, and only once the widget has actually accepted the trip: a
    // refusal (mid-say, mid-drag, dismissed...) answers null and stays silent.
    sfx('whoosh', 0.35);
    bank(key);
    tripsThisSession += 1;
    cancelLive = cancel;
    last = { id: c.poi.id, at: Date.now() };
    say('emi trips: ' + c.poi.id);
    return true;
  }

  /**
   * THE ONE ENTRY POINT. `emi/index.js` offers every moment; this answers true
   * only when it actually launched a trip, which is the caller's signal that the
   * wordless reaction for that moment should stand down (EMI is not there to
   * make a face - she is on her way across the quad).
   */
  function offer(name, payload) {
    try {
      if (typeof name !== 'string' || TRIP_TRIGGERS.indexOf(name) < 0) return false;
      if (tripsThisSession >= D.TRIPS_PER_SESSION) return false;
      if (sessions() < D.TRIP_FROM_SESSION) return false;
      if (!pageVisible()) return false;
      // The widget refuses over any live verb of its own (say, chain, press,
      // drag, dock, disabled, a trip already running) - one owner of that truth.
      if (typeof widget.tripping === 'function' && widget.tripping()) return false;
      void payload;
      const list = candidates();
      if (!list.length) return false;
      if (D.TRIP_ODDS < 1 && rng() >= D.TRIP_ODDS) return false;
      const pick = list[Math.min(list.length - 1, Math.floor(rng() * list.length))];
      return launch(pick);
    } catch (e) {
      say('emi trips: offer threw (ignored) - ' + ((e && e.message) || e));
      return false;
    }
  }

  return {
    offer,
    go,
    /** Live rects for every registered fixture (the widget's drag probe). */
    poiRects,
    /** Read-only: what could be visited right now, by id. */
    candidates() { return candidates().map((c) => c.poi.id); },
    /** Abort a trip in flight; she comes home. */
    cancel() {
      if (typeof cancelLive !== 'function') return false;
      const fn = cancelLive;
      cancelLive = null;
      try { fn(); } catch (e) { /* noop */ }
      return true;
    },
    get tripping() { return typeof widget.tripping === 'function' ? widget.tripping() : false; },
    state() { return { trips: tripsThisSession, last: last ? Object.assign({}, last) : null }; },
    dials: D,
    destroy() {
      try { if (cancelLive) cancelLive(); } catch (e) { /* noop */ }
      cancelLive = null;
      try { widget.setPoiRects(null); } catch (e) { /* noop */ }
    },
  };
}

export default createFieldTrips;
