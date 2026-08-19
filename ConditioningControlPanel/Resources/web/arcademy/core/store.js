/* ============================================================================
 * core/store.js - the meta-command client (SYNTHESIS #15, DTRH precedent).
 *
 * arcademy_meta.json lives in C# (ArcademyMetaStore, App.UserDataPath). The page
 * gets the whole blob once in init.meta and then talks to it over three ops:
 *
 *   bridge -> {type:'meta-command', op:'get'|'set'|'merge', key, value?}
 *   host   -> {type:'meta', key, value}
 *
 * LOCAL CACHE + WRITE-THROUGH. Reads are synchronous off the cache (a report
 * card must never await the disk); writes update the cache immediately AND post
 * the command, then reconcile when the host echoes `meta`. If the host never
 * answers, the cache is still coherent for this session - the worst case is a
 * lost write, never a wedged screen or a half-drawn card.
 *
 * SHAPE (v1). Top-level keys are the write unit; nothing deeper is addressable.
 *
 * HOST-OWNED (read-only here - ArcademyMetaStore mints these from `class-ended`
 * so a stale page cannot forge a streak, and a write naming one is refused):
 *   streak                  : number   consecutive local days with >= 1 class
 *   perfectAttendance       : number   lifetime all-three days
 *   lastAttendanceLocalDate : string
 *   todayClasses            : string[] game keys credited today
 *   xpPaidDays              : { '<utcDay>': [gameKey] }  the XP ledger: a class
 *                             pays once per UTC day, every later run that day is
 *                             a free replay (payout-result carries retake:true)
 *
 * PAGE-OWNED:
 *   days   : { '2026-08-19': { classes: { <gameKey>: {grade, zen, at} },
 *                              complete: bool, perfect: bool } }   the graded view
 *   games  : { <gameKey>: { tier, promotions, played, best, lastGrade, ...game } }
 * Games get a namespaced corner of `games` through gameMeta/mergeGameMeta so a
 * game can persist its own baselines (Impulse Control) without inventing keys at
 * the top level.
 *
 * TWO INBOUND FRAME SHAPES, both handled: `{type:'meta', key, value}` (the reply
 * to a meta-command) and `{type:'meta', rev, state}` (the whole-blob snapshot the
 * host pushes after it credits attendance). Ignoring the second one is the easy
 * mistake: the authoritative streak arrives only that way.
 *
 * ATTENDANCE IS LOCAL-DATE (regression #978): the host rolls the streak on the
 * local date; content is seeded by init.utcDateSeed. Do not cross the wires.
 * ==========================================================================*/

const DAY_HISTORY_MAX = 30;   // keep the local view small; C# owns the archive

/** Keys the host mints. The page never writes them (the write would be refused). */
export const HOST_OWNED_KEYS = Object.freeze([
  'streak', 'perfectAttendance', 'lastAttendanceLocalDate', 'todayClasses', 'xpPaidDays',
]);
const HOST_OWNED = new Set(HOST_OWNED_KEYS);

function isObj(v) { return !!v && typeof v === 'object' && !Array.isArray(v); }

/** Shallow-merge patch into base, returning a NEW object (no input mutation). */
function merged(base, patch) {
  const out = Object.assign({}, isObj(base) ? base : {});
  if (isObj(patch)) for (const k of Object.keys(patch)) out[k] = patch[k];
  return out;
}

/**
 * @param {Object} o
 * @param {Object} o.bridge         the bridge module (send/on)
 * @param {Object=} o.initialMeta   init.meta
 * @param {Function=} o.log
 */
export function createStore({ bridge, initialMeta, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const cache = isObj(initialMeta) ? Object.assign({}, initialMeta) : {};
  const listeners = new Set();

  function emit(key) {
    for (const fn of Array.from(listeners)) {
      try { fn(key, cache[key]); } catch (e) { say('store listener threw: ' + ((e && e.message) || e)); }
    }
  }

  /* The host is the owner: an inbound `meta` frame always wins over the cache.
   * Two shapes (see header) - a keyed reply, or a whole-blob snapshot with a rev. */
  let rev = -1;
  if (bridge && typeof bridge.on === 'function') {
    bridge.on('meta', (m) => {
      if (!m) return;
      if (typeof m.key === 'string') {
        cache[m.key] = m.value;
        emit(m.key);
        return;
      }
      if (m.state && typeof m.state === 'object') {
        if (typeof m.rev === 'number') rev = m.rev;
        for (const k of Object.keys(m.state)) cache[k] = m.state[k];
        emit(null);          // "everything moved"
      }
    });
  }

  let refusedWarned = false;

  function post(op, key, value) {
    if (!bridge || typeof bridge.send !== 'function') return;
    if (op !== 'get' && HOST_OWNED.has(String(key))) {
      // The host refuses these and logs; posting anyway would just answer with an
      // echo that overwrites our cache with the host's own value - harmless, but
      // it hides the mistake. Refuse locally and say so once.
      if (!refusedWarned) {
        refusedWarned = true;
        say('store: "' + key + '" is host-owned (minted from class-ended) - local write dropped');
      }
      return;
    }
    const msg = { type: 'meta-command', op, key: String(key) };
    if (value !== undefined) msg.value = value;
    bridge.send(msg);
  }

  const api = {
    /** Synchronous read off the cache. */
    get(key, fallback) {
      const v = cache[key];
      return v === undefined ? fallback : v;
    },

    /** Everything (shallow copy). */
    all() { return Object.assign({}, cache); },

    /** Replace a top-level key. Write-through. */
    set(key, value) {
      cache[key] = value;
      emit(key);
      post('set', key, value);
      return value;
    },

    /** Shallow-merge into a top-level key. Write-through. */
    merge(key, patch) {
      const next = merged(cache[key], patch);
      cache[key] = next;
      emit(key);
      post('merge', key, patch);
      return next;
    },

    /** Ask the host to re-send a key (it replies with `meta`). Fire and forget. */
    refresh(key) { post('get', key); },

    /** Subscribe to cache changes. @returns {()=>void} unsubscribe */
    onChange(fn) {
      if (typeof fn !== 'function') return () => {};
      listeners.add(fn);
      return () => listeners.delete(fn);
    },

    /* ---------------------- per-game corner ------------------------------ */
    gameMeta(gameKey) {
      const games = isObj(cache.games) ? cache.games : {};
      const g = games[gameKey];
      return isObj(g) ? g : {};
    },

    mergeGameMeta(gameKey, patch) {
      const games = isObj(cache.games) ? Object.assign({}, cache.games) : {};
      games[gameKey] = merged(games[gameKey], patch);
      cache.games = games;
      emit('games');
      // The host merges at the top level, so send the whole `games` sub-object
      // for this key - one op, and a concurrent write to another game survives.
      post('merge', 'games', { [gameKey]: games[gameKey] });
      return games[gameKey];
    },

    /* ---------------------- the day ------------------------------------- */
    day(localDate) {
      const days = isObj(cache.days) ? cache.days : {};
      const d = days[localDate];
      return isObj(d) ? d : { classes: {}, complete: false, perfect: false };
    },

    /** Record one finished class into today's row. */
    recordClass(localDate, gameKey, entry) {
      const days = isObj(cache.days) ? Object.assign({}, cache.days) : {};
      const day = Object.assign({ classes: {}, complete: false, perfect: false }, days[localDate]);
      day.classes = Object.assign({}, day.classes, { [gameKey]: entry });
      days[localDate] = day;
      // Trim the local view (oldest first) so a long-lived install cannot grow
      // an unbounded object in page memory. C# keeps the real history.
      const keys = Object.keys(days).sort();
      while (keys.length > DAY_HISTORY_MAX) delete days[keys.shift()];
      cache.days = days;
      emit('days');
      post('merge', 'days', { [localDate]: day });
      return day;
    },

    /** Mark the day complete (+ perfect attendance) once all classes are graded. */
    completeDay(localDate, { classCount, perfect } = {}) {
      const day = Object.assign({}, api.day(localDate));
      const graded = Object.keys(day.classes || {}).length;
      day.complete = classCount ? graded >= classCount : true;
      day.perfect = !!perfect && day.complete;
      const days = Object.assign({}, isObj(cache.days) ? cache.days : {}, { [localDate]: day });
      cache.days = days;
      emit('days');
      post('merge', 'days', { [localDate]: day });
      return day;
    },

    /* ---------------------- attendance (HOST-OWNED, read-only) ------------ */
    /**
     * The attendance numbers, normalized. ArcademyMetaStore mints them from
     * `class-ended` and stores them as bare scalars (streak / perfectAttendance /
     * todayClasses); the object form is accepted too so a legacy or standalone
     * blob still reads. The page never writes them - see HOST_OWNED_KEYS.
     * @returns {{count:number, perfectDays:number, classesToday:number, lastLocalDate:?string}}
     */
    streak() {
      const s = cache.streak;
      const today = Array.isArray(cache.todayClasses) ? cache.todayClasses.length : 0;
      if (isObj(s)) {
        return {
          count: s.count | 0,
          perfectDays: s.perfectDays | 0,
          classesToday: today,
          lastLocalDate: s.lastLocalDate || null,
        };
      }
      const num = (v) => (Number(v) > 0 ? Math.round(Number(v)) : 0);
      return {
        count: num(s),
        perfectDays: num(cache.perfectAttendance),
        classesToday: today,
        lastLocalDate: cache.lastAttendanceLocalDate || null,
      };
    },

    /**
     * Fold the numbers `payout-result` carries into the cache. The host credits
     * attendance while it pays out and ships the authoritative figures on that
     * frame, so the chip is right the instant a class ends instead of waiting for
     * the snapshot. Same-frame truth beats a locally guessed streak.
     */
    applyPayout(m) {
      if (!m) return api.streak();
      if (typeof m.streak === 'number') cache.streak = m.streak;
      if (typeof m.perfectAttendance === 'number') cache.perfectAttendance = m.perfectAttendance;
      if (typeof m.classesToday === 'number') {
        const n = Math.max(0, Math.round(m.classesToday));
        const cur = Array.isArray(cache.todayClasses) ? cache.todayClasses : [];
        if (cur.length !== n) cache.todayClasses = new Array(n).fill('');
      }
      emit('streak');
      return api.streak();
    },
  };

  return api;
}

export default createStore;
