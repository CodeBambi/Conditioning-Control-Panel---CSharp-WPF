/* ============================================================================
 * stats.js — LOCAL retention / longitudinal stats for "Graded Intake" (Agent G).
 *
 * Owns: append-only local persistence of every run, longitudinal aggregates,
 * and the feed-forward summary the NEXT intake opens against
 * (BootConfig.priorRun). Everything here is STRICTLY LOCAL to the user's
 * machine/browser, fully user-viewable, exportable, and deletable.
 *
 * NO FOMO. NO STREAK PRESSURE. There is no "don't break your streak", no
 * countdowns, no loss framing — only a neutral, factual record the user can
 * read, export, or wipe at will. Copy here stays descriptive, never coercive.
 *
 * ---------------------------------------------------------------------------
 * PERSISTENCE (degrades, never throws at import):
 *   1. IndexedDB   (primary — object store "runs", append-only)
 *   2. localStorage (fallback — one JSON array under 'intake.stats.runs')
 *   3. in-memory   (last resort — this session only; nothing persists)
 * The backend is chosen LAZILY on first use and feature-detected. Importing this
 * module touches no storage API, so a host with neither IndexedDB nor
 * localStorage (or one that throws on access) still loads the page.
 *
 * We persist a COMPACTED per-run summary (contracts.js §QuizRunResult allows
 * "or a compacted summary"): enough to rebuild every aggregate + feed-forward,
 * without hoarding full trajectories forever. See compactResult().
 *
 * ---------------------------------------------------------------------------
 * FACTORY (contract — see contracts.js "STATS (Agent G)"):
 *   createStats() -> {
 *     record(result: QuizRunResult, extras?): Promise<void>
 *     feedForward(): Promise<PriorRun|null>          // -> BootConfig.priorRun
 *     aggregates(): Promise<Aggregates>              // journey/drift/curiosities/commitment
 *     exportAll(): Promise<ExportBlob>               // JSON the user can download
 *     deleteAll(): Promise<void>                     // wipe everything, everywhere
 *     renderStatsInto(el): Promise<void>             // optional lightweight view
 *     recentRuns(limit?): Promise<Record[]>          // newest first — ui/history.js
 *     annotateLast(patch): Promise<void>             // late additions to the last run
 *   }
 * createStats() takes NO required args. An optional opts.backend ('idb' |
 * 'local' | 'memory') FORCES a backend — used by the headless node smoke test so
 * the aggregate/feed-forward logic is testable without IndexedDB.
 * ==========================================================================*/

import { BAND_DEPTH_FLOOR, Band } from './contracts.js';

/* Schema version stamped into every stored record + export, so a future reader
 * (or a migration) can tell what shape it's looking at. */
const STATS_VERSION = 1;
const LS_KEY = 'intake.stats.runs';
const IDB_NAME = 'intake_stats';
const IDB_STORE = 'runs';

/* Depth at/above which a beat's dwell time counts as "trance time". Deepening's
 * floor is 0.42 (contracts BAND_DEPTH_FLOOR); 0.40 captures the descent into it. */
const TRANCE_DEPTH = 0.40;
/* Cap on downsampled depth-curve points kept per run (keeps records small). */
const DEPTH_SAMPLE_CAP = 48;

/* ----------------------------------------------------------------------------
 * THE RECAP BLOCK (schema `recap`, additive — added for ui/history.js).
 *
 * Everything above is longitudinal AGGREGATE fuel. The recap block is the other
 * half: the run as the player experienced it — the closing card's grade and
 * judgment, the colours they named, the spirals those colours wove, the
 * payloads the run paid them with, and what was drafted out of it. boot.js
 * assembles it at run end (it is the only module that can see the resolved
 * bank/theme, the ack, and the two run ledgers) and hands it to record() as the
 * optional second argument. Absent/garbage extras degrade to no recap block at
 * all, so an older boot.js against this file still records exactly as before.
 *
 * WHAT FILLS AND WHAT HAPPENS THEN: nothing is capped by run COUNT — the
 * append-only ledger above is load-bearing for the lifetime aggregates and must
 * not lose rows. Instead the HEAVY halves of the recap (the media urls and the
 * spiral params — the only unbounded-ish parts) are stripped from every record
 * older than the DETAIL_KEEP newest, on every record(). The archive therefore
 * keeps full recaps for the last DETAIL_KEEP runs and thins older entries to
 * their grade/judgment/numbers, which is exactly how a filing cabinet behaves:
 * the recent files keep their photographs, the old ones keep their paperwork.
 * 30 is roughly a month of daily runs and costs well under a megabyte.
 * -------------------------------------------------------------------------- */
const RECAP_VERSION = 1;
const DETAIL_KEEP = 30;
/** Hard ceilings on the recap's list-shaped fields (defence in depth: the
 *  ledgers cap themselves too, but a hostile/older caller might not). */
const RECAP_CAPS = { colors: 8, spirals: 8, media: 24, keepsakes: 16, mantras: 12 };

/* ----------------------------------------------------------------------------
 * priorRun / aggregate SHAPES  (documented for the driver + Agent A + Agent I)
 *
 * feedForward() returns `PriorRun | null`. This is EXACTLY what web-shim puts in
 * BootConfig.priorRun and what the next run (Agent A engine / the intro copy)
 * reads to "open referencing last run". null == first-ever run (no history).
 *
 *   PriorRun = {
 *     statsVersion: number,
 *     returning: true,               // always true when non-null (there IS history)
 *     sessionCount: number,          // total runs recorded, including the last
 *     lastRun: {
 *       at: number,                  // epoch ms (local wall clock) of last run
 *       niche: string,               // Niche.*
 *       route: {                     // last run's revealed Route (contracts.Route)
 *         primary, primaryArchetypeId, secondaryArchetypeId,
 *         primaryShare, secondaryShare
 *       },
 *       peakDepth: number,           // 0..1
 *       deepestBand: string,         // Band.*
 *       totalScore, maxScore: number,
 *       scoreRate: number,           // 0..1  (maxScore>0 ? total/max : 0)
 *       chasedReward: boolean,       // did they drift toward the decoupled reward
 *       chaseMagnitude: number,      // 0..1
 *       affirmedMantras: string[],   // verbatim, from the last run only
 *       topTags: [{ tag, count }],   // last run's strongest tags (<=5)
 *       endless: boolean
 *     },
 *     cumulative: {
 *       peakDepthEver: number,       // 0..1 max across all runs
 *       deepestBandEver: string,     // Band.* ranked by BAND_DEPTH_FLOOR
 *       cumulativeTranceMs: number,  // summed dwell time at depth >= TRANCE_DEPTH
 *       affirmedMantraCount: number, // count of UNIQUE mantras ever affirmed
 *       topTagsAllTime: [{ tag, count }]   // <=8
 *     },
 *     drift: {
 *       primaryArchetypeId: string,  // most recent run's primary archetype
 *       dominantArchetypeId: string, // most frequent primary across all runs
 *       trend: [{ at, niche, primaryArchetypeId, secondaryArchetypeId }]  // <=8, oldest->newest
 *     }
 *   }
 *
 * Consumers MUST treat every field as optional/defensive — a fresh user has no
 * priorRun at all (null), and a user who wiped mid-history may have sparse data.
 *
 * aggregates() returns { journey, drift, curiosities, commitment } — the full
 * stats view. See aggregates() below for the exact shape.
 * -------------------------------------------------------------------------- */

/* ============================================================================
 * BACKENDS
 * Each backend is an async append-only log with: append(rec), all(), clear(),
 * plus put(rec) — an IN-PLACE rewrite of an already-stored record, addressed by
 * its `seq`. put() is what lets a record grow a keepsake after the fact (the
 * outro's "Keep it" lands minutes after record()) and lets old records be
 * thinned; it is never used to append, so the log stays append-only in spirit.
 * All three share one interface so the aggregate logic is backend-agnostic.
 * ==========================================================================*/

function hasWindow() { return typeof window !== 'undefined'; }

/** Next `seq` for the backends that don't mint one themselves (idb does). */
function nextSeq(rows) {
  let max = 0;
  for (const r of _arr(rows)) { const s = _num(r && r.seq, 0); if (s > max) max = s; }
  return max + 1;
}

/** In-memory backend — always works, persists nothing beyond this session. */
function memoryBackend() {
  let rows = [];
  return {
    kind: 'memory',
    async append(rec) { rows.push(Object.assign({ seq: nextSeq(rows) }, rec)); },
    async all() { return rows.slice(); },
    async put(rec) {
      const i = rows.findIndex((r) => r && r.seq === (rec && rec.seq));
      if (i >= 0) rows[i] = rec;
    },
    async clear() { rows = []; },
  };
}

/** localStorage backend — one JSON array. Feature-detected before selection. */
function localBackend() {
  const read = () => {
    try { const s = localStorage.getItem(LS_KEY); const a = s ? JSON.parse(s) : []; return Array.isArray(a) ? a : []; }
    catch (_e) { return []; }
  };
  const write = (a) => { try { localStorage.setItem(LS_KEY, JSON.stringify(a)); } catch (_e) { /* quota/denied */ } };
  return {
    kind: 'local',
    async append(rec) { const a = read(); a.push(Object.assign({ seq: nextSeq(a) }, rec)); write(a); },
    async all() { return read(); },
    async put(rec) {
      const a = read();
      const i = a.findIndex((r) => r && r.seq === (rec && rec.seq));
      if (i < 0) return;
      a[i] = rec;
      write(a);
    },
    async clear() { try { localStorage.removeItem(LS_KEY); } catch (_e) { /* ignore */ } },
  };
}

/** Probe whether localStorage is usable (present AND writable — private mode can throw). */
function localStorageUsable() {
  try {
    if (!hasWindow() || !window.localStorage) return false;
    const k = '__intake_probe__';
    window.localStorage.setItem(k, '1');
    window.localStorage.removeItem(k);
    return true;
  } catch (_e) { return false; }
}

/** Open (or create) the IndexedDB database. Rejects on any failure. */
function openIDB() {
  return new Promise((resolve, reject) => {
    let req;
    try { req = indexedDB.open(IDB_NAME, 1); }
    catch (e) { reject(e); return; }
    req.onupgradeneeded = () => {
      const db = req.result;
      if (!db.objectStoreNames.contains(IDB_STORE)) {
        db.createObjectStore(IDB_STORE, { keyPath: 'seq', autoIncrement: true });
      }
    };
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error || new Error('idb open error'));
    req.onblocked = () => reject(new Error('idb blocked'));
  });
}

function idbBackend(db) {
  const tx = (mode) => db.transaction(IDB_STORE, mode).objectStore(IDB_STORE);
  const wrap = (request) => new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error || new Error('idb request error'));
  });
  return {
    kind: 'idb',
    async append(rec) { await wrap(tx('readwrite').add(rec)); },
    async all() { const rows = await wrap(tx('readonly').getAll()); return Array.isArray(rows) ? rows : []; },
    async put(rec) { if (rec && rec.seq != null) await wrap(tx('readwrite').put(rec)); },
    async clear() { await wrap(tx('readwrite').clear()); },
  };
}

/**
 * Choose the best available backend, once. Never throws — worst case memory.
 * @param {string=} force 'idb' | 'local' | 'memory'
 */
async function chooseBackend(force) {
  if (force === 'memory') return memoryBackend();
  if (force === 'local')  return localStorageUsable() ? localBackend() : memoryBackend();
  if (force === 'idb') {
    try { return idbBackend(await openIDB()); } catch (_e) { /* fall through */ }
  }
  // auto: IndexedDB -> localStorage -> memory
  if (!force && typeof indexedDB !== 'undefined') {
    try { return idbBackend(await openIDB()); } catch (_e) { /* fall through */ }
  }
  if (localStorageUsable()) return localBackend();
  return memoryBackend();
}

/* ============================================================================
 * COMPACTION — QuizRunResult -> stored record.
 * ==========================================================================*/

const _num = (n, d = 0) => (typeof n === 'number' && isFinite(n) ? n : d);
const _clamp01 = (n) => (n < 0 ? 0 : n > 1 ? 1 : _num(n));
const _str = (s) => (typeof s === 'string' ? s : '');
const _arr = (a) => (Array.isArray(a) ? a : []);
const _obj = (o) => (o && typeof o === 'object' ? o : {});

/** Downsample a numeric series to at most `cap` evenly-spaced points. */
function downsample(series, cap) {
  const a = _arr(series).map((n) => _clamp01(n));
  if (a.length <= cap) return a;
  const out = [];
  const step = (a.length - 1) / (cap - 1);
  for (let i = 0; i < cap; i++) out.push(a[Math.round(i * step)]);
  return out;
}

/* ----------------------------------------------------------------------------
 * ANSWER ANALYTICS — the numbers the archive's detail card reads.
 *
 * Every field below is derived from AnswerRecord fields that engine.js actually
 * writes (contracts.js §AnswerRecord): band, mechanic, correct, latencyMs,
 * steered, promptHeat, isTrick, isFreeChoice. NOTHING is invented — in
 * particular there is no timeout count here, because the trajectory does not
 * carry `timedOut` (the engine consumes it but never records it).
 *
 * ACCURACY is measured over GRADED answers only: interludes have no answer at
 * all, and freeChoice beats (the Calibration colour picks) mark every option
 * correct, so counting either would quietly inflate the rate.
 * -------------------------------------------------------------------------- */
function analyzeTrajectory(traj) {
  const byBand = {};        // band -> { n, correct }
  const mechanics = {};     // mechanic -> n
  const latencies = [];     // graded answers only
  let graded = 0, correct = 0, steered = 0, answered = 0, heatMax = 0, tricks = 0;

  for (const a of _arr(traj)) {
    const rec = _obj(a);
    const mech = _str(rec.mechanic) || 'unknown';
    mechanics[mech] = (mechanics[mech] || 0) + 1;
    if (mech === 'interlude') continue;      // a valley, not an answer
    answered++;
    if (rec.steered) steered++;
    if (rec.isTrick) tricks++;
    const heat = _num(rec.promptHeat);
    if (heat > heatMax) heatMax = heat;
    if (rec.isFreeChoice) continue;          // no right answer -> no signal
    graded++;
    if (rec.correct) correct++;
    latencies.push(Math.max(0, _num(rec.latencyMs)));
    const band = _str(rec.band) || Band.Calibration;
    const b = byBand[band] || (byBand[band] = { n: 0, correct: 0 });
    b.n++;
    if (rec.correct) b.correct++;
  }

  const sorted = latencies.slice().sort((x, y) => x - y);
  const median = sorted.length
    ? (sorted.length % 2
      ? sorted[(sorted.length - 1) / 2]
      : Math.round((sorted[sorted.length / 2 - 1] + sorted[sorted.length / 2]) / 2))
    : 0;

  return {
    answered,                                     // every beat that took an input
    graded,                                       // ...minus the no-wrong-answer ones
    correct,
    steered,
    tricks,
    heatMax,
    medianLatencyMs: median,
    fastestMs: sorted.length ? sorted[0] : 0,
    slowestMs: sorted.length ? sorted[sorted.length - 1] : 0,
    byBand,
    mechanics,
  };
}

/** Trim an array to `cap`, keeping the FIRST entries (arrival order matters). */
function capList(list, cap) { return _arr(list).slice(0, Math.max(0, cap | 0)); }

/**
 * Build the stored `recap` block from boot.js's extras. Every field is optional
 * and independently defensive: a caller that supplies nothing gets null (no
 * block at all), and a caller that supplies half of it gets that half.
 */
function compactRecap(extras) {
  const x = _obj(extras);
  if (!extras || typeof extras !== 'object') return null;

  const cls = _obj(x.classification);
  const ses = _obj(x.session);

  const colors = capList(_arr(x.colors).map(_str).filter(Boolean), RECAP_CAPS.colors);
  const spirals = capList(_arr(x.spirals), RECAP_CAPS.spirals).map((s) => {
    // Params are plain JSON (loomField schema-v2). Clone defensively so a live
    // object can never be aliased into storage.
    try { return JSON.parse(JSON.stringify(_obj(s && s.params ? s.params : s))); }
    catch (_e) { return null; }
  }).filter(Boolean);
  const media = capList(_arr(x.media), RECAP_CAPS.media).map((m) => {
    const o = _obj(m);
    const url = _str(o.url);
    return url ? { url, kind: o.kind === 'image' ? 'image' : 'gif', count: Math.max(1, _num(o.count, 1)) } : null;
  }).filter(Boolean);

  return {
    recapVersion: RECAP_VERSION,
    grade: _str(x.grade),
    subject: _str(x.subject),
    sectionTitle: _str(x.sectionTitle),
    classification: {
      primaryName: _str(cls.primaryName),
      primaryShare: _clamp01(cls.primaryShare),
      primaryBlurb: _str(cls.primaryBlurb),
      secondaryName: _str(cls.secondaryName),
      secondaryShare: _clamp01(cls.secondaryShare),
      hue: (typeof cls.hue === 'number' && isFinite(cls.hue)) ? cls.hue : null,
    },
    session: {
      delivered: ses.delivered === 'host' ? 'host' : 'local',
      name: _str(ses.name),
      derived: !!ses.derived,   // true = mirrored from the host's naming rule, not read back
    },
    colors,
    spirals,
    spiralsRolled: Math.max(0, _num(x.spiralsRolled)),
    media,
    mediaShown: Math.max(0, _num(x.mediaShown)),
    keepsakes: [],              // filled later by annotateLast (the outro's actions)
  };
}

/**
 * Turn a QuizRunResult into the compact, append-only record we store.
 * Defensive: any malformed field degrades to a sane default rather than throwing.
 * `seq` is NOT set here — the backend/order assigns ordering; we stamp `at`.
 * @param {object} result   QuizRunResult
 * @param {object=} extras  boot.js's run-end recap material (see compactRecap)
 */
function compactResult(result, extras) {
  const r = _obj(result);
  const traj = _arr(r.trajectory);

  const route = _obj(r.route);
  const rewardProfile = _obj(r.rewardProfile);

  let correct = 0, rewardFired = 0, rewardDecoupled = 0, tranceMs = 0;
  const depthSeries = [];
  for (const a of traj) {
    const rec = _obj(a);
    if (rec.correct) correct++;
    if (rec.rewardFired) rewardFired++;
    if (rec.rewardDecoupled) rewardDecoupled++;
    const d = _clamp01(rec.depth);
    depthSeries.push(d);
    if (d >= TRANCE_DEPTH) tranceMs += Math.max(0, _num(rec.latencyMs));
  }

  const totalScore = _num(r.totalScore);
  const maxScore = _num(r.maxScore);

  return {
    statsVersion: STATS_VERSION,
    at: Date.now(),                    // local wall clock, for ordering + display
    runMs: _num(r.endedAtMs),          // relative run duration (performance.now at emit)
    product: _str(r.product),
    niche: _str(r.niche),
    route: {
      primary: _str(route.primary),
      primaryArchetypeId: _str(route.primaryArchetypeId),
      secondaryArchetypeId: route.secondaryArchetypeId ? _str(route.secondaryArchetypeId) : '',
      primaryShare: _clamp01(route.primaryShare),
      secondaryShare: _clamp01(route.secondaryShare),
    },
    peakDepth: _clamp01(r.peakDepth),
    // compliance-discounted headline figure (see engine finalize); older records
    // predate it and fall back to peakDepth at read time.
    susceptibility: _clamp01(r.susceptibility != null ? r.susceptibility : r.peakDepth),
    deepestBand: _str(r.deepestBand) || Band.Calibration,
    totalScore,
    maxScore,
    scoreRate: maxScore > 0 ? _clamp01(totalScore / maxScore) : 0,
    endless: !!r.endless,
    beatCount: traj.length,
    correctCount: correct,
    rewardFired,
    rewardDecoupled,
    tranceMs,
    chasedReward: !!rewardProfile.chasedReward,
    chaseMagnitude: _clamp01(rewardProfile.chaseMagnitude),
    affirmedMantras: _arr(r.affirmedMantras).map(_str).filter(Boolean),
    tagTallies: sanitizeTallies(r.tagTallies),
    depthSamples: downsample(depthSeries, DEPTH_SAMPLE_CAP),
    // --- additive (ui/history.js) ---------------------------------------
    answers: analyzeTrajectory(traj),
    recap: compactRecap(extras),
  };
}

/** Coerce a {tag:count} map into a clean {string: positive-int} object. */
function sanitizeTallies(tallies) {
  const out = {};
  const t = _obj(tallies);
  for (const k of Object.keys(t)) {
    const v = _num(t[k]);
    if (k && v > 0) out[String(k)] = v;
  }
  return out;
}

/* ============================================================================
 * AGGREGATE HELPERS (pure — operate on an array of stored records)
 * ==========================================================================*/

/** Rank two bands by descent depth (BAND_DEPTH_FLOOR); returns the deeper one. */
function deeperBand(a, b) {
  const fa = BAND_DEPTH_FLOOR[a];
  const fb = BAND_DEPTH_FLOOR[b];
  if (fa == null) return b || Band.Calibration;
  if (fb == null) return a || Band.Calibration;
  return fb > fa ? b : a;
}

/** Merge tag tallies from one record into an accumulator. */
function mergeTallies(acc, tallies) {
  for (const k of Object.keys(_obj(tallies))) acc[k] = (acc[k] || 0) + _num(tallies[k]);
  return acc;
}

/** Sort a {key:count} map into a descending [{tag,count}] list, top `n`. */
function topEntries(map, n) {
  return Object.keys(_obj(map))
    .map((k) => ({ tag: k, count: _num(map[k]) }))
    .sort((x, y) => y.count - x.count)
    .slice(0, n);
}

/* ============================================================================
 * FACTORY
 * ==========================================================================*/

/**
 * @param {{backend?: 'idb'|'local'|'memory'}=} opts  backend override (tests/host tuning)
 */
export function createStats(opts = {}) {
  const force = opts && opts.backend;
  let backendPromise = null;
  const backend = () => (backendPromise || (backendPromise = chooseBackend(force)));

  /** All stored records, oldest -> newest (best-effort ordering). */
  async function allRecords() {
    try {
      const be = await backend();
      const rows = await be.all();
      // Order by seq if present (idb), else by stamped `at`, else insertion order.
      return _arr(rows).slice().sort((a, b) => {
        const sa = _num(a && a.seq, NaN), sb = _num(b && b.seq, NaN);
        if (isFinite(sa) && isFinite(sb) && sa !== sb) return sa - sb;
        return _num(a && a.at) - _num(b && b.at);
      });
    } catch (_e) { return []; }
  }

  /* ------------------------------------------------------------------ record */
  /**
   * @param {object} result   QuizRunResult
   * @param {object=} extras  optional recap material (see compactRecap). Older
   *                          callers pass nothing and behave exactly as before.
   */
  async function record(result, extras) {
    try {
      const rec = compactResult(result, extras);
      const be = await backend();
      await be.append(rec);
    } catch (_e) { /* stats must never break a run */ }
    // Thinning is best-effort and deliberately AFTER the append: a failure here
    // costs an old run its photographs, never this run its record.
    try { await pruneDetail(); } catch (_e) { /* ignore */ }
  }

  /**
   * Strip the heavy recap halves (media urls + spiral params) from every record
   * older than the DETAIL_KEEP newest. Rows keep their grade, judgment, numbers
   * and keepsakes; only the re-renderable/re-showable payload lists go.
   */
  async function pruneDetail() {
    const rows = await allRecords();
    if (rows.length <= DETAIL_KEEP) return;
    const be = await backend();
    if (!be || typeof be.put !== 'function') return;
    const stale = rows.slice(0, rows.length - DETAIL_KEEP);
    for (const r of stale) {
      const rec = _obj(r).recap;
      if (!rec || (!_arr(rec.media).length && !_arr(rec.spirals).length)) continue;
      rec.media = [];
      rec.spirals = [];
      rec.thinned = true;                 // the UI says so rather than pretending
      try { await be.put(r); } catch (_e) { /* one stubborn row is not fatal */ }
    }
  }

  /* ------------------------------------------------- history read + annotate */
  /**
   * The most recent stored runs, NEWEST FIRST — the archive's list.
   * @param {number=} limit  0/absent = all
   */
  async function recentRuns(limit) {
    const rows = await allRecords();
    const out = rows.slice().reverse();
    const n = _num(limit, 0);
    return n > 0 ? out.slice(0, n) : out;
  }

  /**
   * Merge `patch` into the MOST RECENT record's recap block. Used by the outro's
   * weave actions, which land after record(): keeping a spiral in the Loom or
   * printing it as a PNG is part of the run's story, but it happens minutes
   * later. `patch.keepsake` appends (capped); every other key overwrites.
   * No-ops silently when there is no record yet or the backend can't rewrite.
   */
  async function annotateLast(patch) {
    try {
      const p = _obj(patch);
      const rows = await allRecords();
      if (!rows.length) return;
      const row = rows[rows.length - 1];
      const rec = _obj(row).recap;
      if (!rec) return;                    // an older record with no recap block
      if (p.keepsake) {
        const k = _obj(p.keepsake);
        rec.keepsakes = _arr(rec.keepsakes);
        if (rec.keepsakes.length < RECAP_CAPS.keepsakes) {
          rec.keepsakes.push({
            kind: _str(k.kind) || 'loom',
            index: Math.max(0, _num(k.index)),
            label: _str(k.label),
            at: _num(k.at, Date.now()),
          });
        }
      }
      for (const key of Object.keys(p)) {
        if (key === 'keepsake') continue;
        rec[key] = p[key];
      }
      const be = await backend();
      if (be && typeof be.put === 'function') await be.put(row);
    } catch (_e) { /* an annotation must never break the ceremony */ }
  }

  /* ------------------------------------------------------------- feedForward */
  async function feedForward() {
    const rows = await allRecords();
    if (rows.length === 0) return null;

    const last = rows[rows.length - 1];

    // cumulative rollups
    let peakDepthEver = 0;
    let deepestBandEver = Band.Calibration;
    let cumulativeTranceMs = 0;
    const uniqueMantras = new Set();
    const allTags = {};
    const primaryCounts = {};
    const trend = [];
    for (const r of rows) {
      peakDepthEver = Math.max(peakDepthEver, _clamp01(r.peakDepth));
      deepestBandEver = deeperBand(deepestBandEver, r.deepestBand);
      cumulativeTranceMs += Math.max(0, _num(r.tranceMs));
      for (const m of _arr(r.affirmedMantras)) uniqueMantras.add(m);
      mergeTallies(allTags, r.tagTallies);
      const pa = _str(_obj(r.route).primaryArchetypeId);
      if (pa) primaryCounts[pa] = (primaryCounts[pa] || 0) + 1;
      trend.push({
        at: _num(r.at),
        niche: _str(r.niche),
        primaryArchetypeId: pa,
        secondaryArchetypeId: _str(_obj(r.route).secondaryArchetypeId),
      });
    }

    const dominantArchetypeId = (topEntries(primaryCounts, 1)[0] || {}).tag || '';

    return {
      statsVersion: STATS_VERSION,
      returning: true,
      sessionCount: rows.length,
      lastRun: {
        at: _num(last.at),
        niche: _str(last.niche),
        route: {
          primary: _str(_obj(last.route).primary),
          primaryArchetypeId: _str(_obj(last.route).primaryArchetypeId),
          secondaryArchetypeId: _str(_obj(last.route).secondaryArchetypeId),
          primaryShare: _clamp01(_obj(last.route).primaryShare),
          secondaryShare: _clamp01(_obj(last.route).secondaryShare),
        },
        peakDepth: _clamp01(last.peakDepth),
        deepestBand: _str(last.deepestBand) || Band.Calibration,
        totalScore: _num(last.totalScore),
        maxScore: _num(last.maxScore),
        scoreRate: _clamp01(last.scoreRate),
        chasedReward: !!last.chasedReward,
        chaseMagnitude: _clamp01(last.chaseMagnitude),
        affirmedMantras: _arr(last.affirmedMantras).slice(),
        topTags: topEntries(last.tagTallies, 5),
        endless: !!last.endless,
      },
      cumulative: {
        peakDepthEver,
        deepestBandEver,
        cumulativeTranceMs,
        affirmedMantraCount: uniqueMantras.size,
        topTagsAllTime: topEntries(allTags, 8),
      },
      drift: {
        primaryArchetypeId: _str(_obj(last.route).primaryArchetypeId),
        dominantArchetypeId,
        trend: trend.slice(-8),
      },
    };
  }

  /* -------------------------------------------------------------- aggregates */
  async function aggregates() {
    const rows = await allRecords();

    // --- journey ---------------------------------------------------------
    let peakDepth = 0;
    let deepestBand = Band.Calibration;
    let cumulativeTranceMs = 0;
    const depthOverTime = [];

    // --- drift -----------------------------------------------------------
    const primaryCounts = {};
    const driftTrend = [];

    // --- curiosities -----------------------------------------------------
    const tagTallies = {};
    let totalRewardsFired = 0;
    let decoupledRewards = 0;
    let chaseSum = 0;
    let chasedSessions = 0;

    // --- commitment ------------------------------------------------------
    const mantraLedger = {};   // mantra -> { count, firstAt, lastAt }

    for (const r of rows) {
      // journey
      peakDepth = Math.max(peakDepth, _clamp01(r.peakDepth));
      deepestBand = deeperBand(deepestBand, r.deepestBand);
      cumulativeTranceMs += Math.max(0, _num(r.tranceMs));
      const samples = _arr(r.depthSamples).map(_clamp01);
      const avgDepth = samples.length ? samples.reduce((s, v) => s + v, 0) / samples.length : 0;
      depthOverTime.push({
        at: _num(r.at),
        niche: _str(r.niche),
        band: _str(r.deepestBand) || Band.Calibration,
        peakDepth: _clamp01(r.peakDepth),
        avgDepth,
        samples,
      });

      // drift
      const pa = _str(_obj(r.route).primaryArchetypeId);
      if (pa) primaryCounts[pa] = (primaryCounts[pa] || 0) + 1;
      driftTrend.push({
        at: _num(r.at),
        niche: _str(r.niche),
        primaryArchetypeId: pa,
        secondaryArchetypeId: _str(_obj(r.route).secondaryArchetypeId),
        primaryShare: _clamp01(_obj(r.route).primaryShare),
        secondaryShare: _clamp01(_obj(r.route).secondaryShare),
      });

      // curiosities
      mergeTallies(tagTallies, r.tagTallies);
      totalRewardsFired += Math.max(0, _num(r.rewardFired));
      decoupledRewards += Math.max(0, _num(r.rewardDecoupled));
      if (r.chasedReward) { chasedSessions++; chaseSum += _clamp01(r.chaseMagnitude); }

      // commitment
      for (const m of _arr(r.affirmedMantras)) {
        const e = mantraLedger[m] || (mantraLedger[m] = { count: 0, firstAt: _num(r.at), lastAt: _num(r.at) });
        e.count++;
        e.lastAt = _num(r.at);
      }
    }

    const ledger = Object.keys(mantraLedger)
      .map((m) => ({ mantra: m, count: mantraLedger[m].count, firstAt: mantraLedger[m].firstAt, lastAt: mantraLedger[m].lastAt }))
      .sort((a, b) => b.count - a.count);
    const totalAffirmations = ledger.reduce((s, e) => s + e.count, 0);

    return {
      journey: {
        sessionCount: rows.length,
        cumulativeTranceMs,
        deepestBand,
        peakDepth,
        depthOverTime,
      },
      drift: {
        trend: driftTrend,
        primaryCounts,
        dominantArchetypeId: (topEntries(primaryCounts, 1)[0] || {}).tag || '',
      },
      curiosities: {
        tagTallies,
        topTags: topEntries(tagTallies, 12),
        rewardResponse: {
          totalRewardsFired,
          decoupledRewards,
          chasedSessions,
          avgChaseMagnitude: chasedSessions ? chaseSum / chasedSessions : 0,
        },
      },
      commitment: {
        affirmedMantras: ledger.reduce((o, e) => { o[e.mantra] = e.count; return o; }, {}),
        uniqueMantras: ledger.length,
        totalAffirmations,
        ledger,
      },
    };
  }

  /* ------------------------------------------------------------ export/wipe */
  async function exportAll() {
    const rows = await allRecords();
    let agg = {};
    try { agg = await aggregates(); } catch (_e) { agg = {}; }
    return {
      product: 'Graded Intake',
      kind: 'intake-stats-export',
      statsVersion: STATS_VERSION,
      exportedAt: Date.now(),
      sessionCount: rows.length,
      runs: rows,
      aggregates: agg,
    };
  }

  async function deleteAll() {
    try { const be = await backend(); await be.clear(); } catch (_e) { /* ignore */ }
    // Also proactively clear the localStorage mirror in case the active backend
    // is idb/memory but a prior session wrote a localStorage array.
    try { if (localStorageUsable()) window.localStorage.removeItem(LS_KEY); } catch (_e) { /* ignore */ }
  }

  /* --------------------------------------------- optional lightweight view */
  async function renderStatsInto(el) {
    if (!el || typeof document === 'undefined') return;
    let a;
    try { a = await aggregates(); } catch (_e) { a = null; }
    if (!a || a.journey.sessionCount === 0) {
      el.textContent = 'No sessions recorded yet.';
      return;
    }
    const j = a.journey, d = a.drift, c = a.curiosities, m = a.commitment;
    const mins = (ms) => (ms / 60000).toFixed(1);
    const pct = (n) => Math.round(_clamp01(n) * 100) + '%';
    const esc = (s) => String(s).replace(/[&<>]/g, (ch) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' }[ch]));

    const tagList = c.topTags.slice(0, 8).map((t) => `${esc(t.tag)} (${t.count})`).join(', ') || '—';
    const mantraList = m.ledger.slice(0, 8).map((e) => `${esc(e.mantra)} ×${e.count}`).join('<br>') || '—';
    const driftList = d.trend.slice(-6)
      .map((t) => `${esc(t.primaryArchetypeId || '—')}${t.secondaryArchetypeId ? ' / ' + esc(t.secondaryArchetypeId) : ''}`)
      .join(' → ') || '—';

    el.innerHTML = `
      <section class="intake-stats" style="font:13px/1.5 system-ui,sans-serif">
        <h3 style="margin:0 0 .5em">Your record</h3>
        <p style="opacity:.7;margin:.2em 0 1em">Kept only on this device. You can export or erase it anytime.</p>
        <dl style="display:grid;grid-template-columns:auto 1fr;gap:.25em .75em;margin:0">
          <dt>Sessions</dt><dd>${j.sessionCount}</dd>
          <dt>Deepest reached</dt><dd>${esc(j.deepestBand)} (peak ${pct(j.peakDepth)})</dd>
          <dt>Time in depth</dt><dd>${mins(j.cumulativeTranceMs)} min</dd>
          <dt>Drift</dt><dd>${driftList}${d.dominantArchetypeId ? ` <span style="opacity:.6">(most often ${esc(d.dominantArchetypeId)})</span>` : ''}</dd>
          <dt>Curiosities</dt><dd>${tagList}</dd>
          <dt>Rewards followed</dt><dd>${c.rewardResponse.chasedSessions}/${j.sessionCount} sessions</dd>
          <dt>Affirmed</dt><dd>${m.uniqueMantras} unique (${m.totalAffirmations} total)<br>${mantraList}</dd>
        </dl>
      </section>`;
  }

  return {
    record, feedForward, aggregates, exportAll, deleteAll, renderStatsInto,
    recentRuns, annotateLast,
  };
}

export default createStats;
