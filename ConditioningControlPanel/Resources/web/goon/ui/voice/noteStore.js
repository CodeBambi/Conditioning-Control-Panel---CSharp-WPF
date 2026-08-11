/* ============================================================================
 * ui/voice/noteStore.js — the eight notes a player keeps.
 *
 * The LIBRARY half of voice notes (docs/GOON_VOICE_PLAN.md §Storage). A live
 * note is recorded, sent and forgotten inside one gesture; a PRE-RECORDED note
 * has to survive a page reload, a relaunch and a phone that was closed for a
 * week — so it lives in IndexedDB, blob and all, and this file is the only thing
 * that touches that database.
 *
 *   ┌───────────────────────────────────────────────────────────────────────┐
 *   │ gg-voice / notes / keyPath 'id'  →  {id, name, durMs, blob, createdAt} │
 *   │ prefs.voiceEmoteMap              →  { [emoteKey]: noteId }             │
 *   └───────────────────────────────────────────────────────────────────────┘
 *
 * WHY INDEXEDDB AND NOT localStorage. A blob is bytes, and localStorage is a
 * string store with a ~5 MB quota shared with every preference on the page.
 * Eight opus notes are ~320 KB, but base64'd into a preference blob they would
 * be ~430 KB of string that is re-serialized on EVERY pref write. IndexedDB
 * stores the Blob itself, and it is the one storage a phone PWA also keeps.
 *
 * WHY THE EMOTE MAP IS NOT IN HERE. It is a PREFERENCE — five bytes per entry,
 * read on the emote hot path (ui/emotes.js, mid-match, one lookup per emote),
 * and it has to be readable WITHOUT opening a database. Keeping it in prefs is
 * what lets the emote hook stay synchronous and non-blocking. The map is keyed
 * by emote and the store answers "which emote is this note on?" by REVERSE
 * lookup, because the question a row on the screen asks is the reverse one.
 *
 * THE CAP IS A REFUSAL, NOT A THROW. `add()` past VN_MAX_NOTES resolves
 * {ok:false, reason:'full'} — the screen turns that into one calm line
 * (S.voice.full). Nothing in this file ever rejects: every path resolves an
 * outcome object or null, because the callers are UI handlers and an unhandled
 * rejection in a click handler is a silent dead button.
 *
 * NO INDEXEDDB, NO PROBLEM. Node (the self-tests), a locked-down private-mode
 * browser and a file:// page with storage disabled all get the SAME API backed
 * by a Map — the library simply does not survive the reload, which is a fair
 * description of what those hosts promise anyway.
 *
 * NODE-IMPORT-SAFE: `indexedDB` is looked up inside a function, never at import.
 * ==========================================================================*/

import { VN_MAX_NOTES, VN_MAX_BYTES } from './voiceService.js';

/** The database, the store and the version — pinned by the contract. */
export const VOICE_DB_NAME = 'gg-voice';
export const VOICE_DB_VERSION = 1;
export const VOICE_STORE_NAME = 'notes';

/** Longest a note name may be. A name is furniture on a row, not a document. */
export const VN_NAME_MAX = 40;

/**
 * How long we will wait for `indexedDB.open` before giving up and going to
 * memory. An open can BLOCK indefinitely (another tab holding an older version,
 * Firefox private mode answering neither event), and a library screen that
 * spins forever is worse than one that says "eight notes, none of them kept".
 */
const OPEN_TIMEOUT_MS = 4000;

/* ----------------------------------------------------------------------------
 * THE EMOTE MAP — pure helpers over `prefs.voiceEmoteMap`.
 *
 * Exported and total, so ui/emotes.js can answer "is there a note on this
 * emote?" with one synchronous call on the send path, and so the self-tests can
 * prove the re-map rule without a database or a DOM.
 *
 * THE INVARIANT IS A BIJECTION: one note per emote (the contract), AND one emote
 * per note (the screen shows each row's single binding). Linking therefore does
 * TWO removals before the write — the emote's old note, and the note's old
 * emote — or a player who re-points a note would end up firing it from two
 * emotes and only see one of them on screen.
 * -------------------------------------------------------------------------- */

/** A defensive copy of whatever prefs handed us, with only string→string pairs. */
function cleanMap(map) {
  const out = {};
  if (!map || typeof map !== 'object' || Array.isArray(map)) return out;
  for (const k of Object.keys(map)) {
    const key = String(k || '');
    const val = map[k];
    if (!key) continue;
    if (typeof val !== 'string' || val === '') continue;
    out[key] = val;
  }
  return out;
}

/** The note bound to an emote, or ''. */
export function noteIdForEmote(map, emoteKey) {
  const key = String(emoteKey || '');
  if (!key) return '';
  const m = cleanMap(map);
  return m[key] || '';
}

/** The emote a note is bound to, or '' — the REVERSE lookup a note row needs. */
export function emoteForNote(map, noteId) {
  const id = String(noteId || '');
  if (!id) return '';
  const m = cleanMap(map);
  for (const k of Object.keys(m)) { if (m[k] === id) return k; }
  return '';
}

/**
 * Bind `noteId` to `emoteKey`, keeping the bijection.
 * @returns {{map:object, moved:string, changed:boolean}}
 *   `moved` is the note that WAS on this emote and has just lost it (''
 *   when the emote was free) — the screen turns that into S.voice.linkMoved.
 */
export function linkEmote(map, emoteKey, noteId) {
  const key = String(emoteKey || '');
  const id = String(noteId || '');
  const next = cleanMap(map);
  if (!key || !id) return { map: next, moved: '', changed: false };
  const moved = next[key] && next[key] !== id ? next[key] : '';
  if (next[key] === id) return { map: next, moved: '', changed: false };
  // The note's PREVIOUS emote goes first: a note may only be in one place.
  for (const k of Object.keys(next)) { if (next[k] === id) delete next[k]; }
  next[key] = id;
  return { map: next, moved, changed: true };
}

/** Remove every binding pointing at `noteId` (the "on its own" option, and delete). */
export function unlinkNote(map, noteId) {
  const id = String(noteId || '');
  const next = cleanMap(map);
  let changed = false;
  for (const k of Object.keys(next)) { if (next[k] === id) { delete next[k]; changed = true; } }
  return { map: next, changed };
}

/** Drop bindings whose note no longer exists (a note deleted on another tab). */
export function pruneMap(map, liveIds) {
  const live = new Set((liveIds || []).map((x) => String(x)));
  const next = cleanMap(map);
  let changed = false;
  for (const k of Object.keys(next)) { if (!live.has(next[k])) { delete next[k]; changed = true; } }
  return { map: next, changed };
}

/* ----------------------------------------------------------------------------
 * ids and shapes
 * -------------------------------------------------------------------------- */

let idSeq = 0;

/**
 * A note id. Time-ordered so a plain sort is chronological, with a counter and a
 * random tail because two notes recorded inside the same millisecond (a
 * self-test loop does exactly that) must not collide.
 */
function makeId() {
  idSeq = (idSeq + 1) % 1000;
  let rand = 0;
  try { rand = Math.floor(Math.random() * 0xffff); } catch (_e) { rand = 0; }
  return 'vn-' + Date.now().toString(36) + '-' + idSeq.toString(36) + '-' + rand.toString(36);
}

/** The metadata half of a record — everything except the bytes. */
function metaOf(rec, emote) {
  return {
    id: rec.id,
    name: rec.name || '',
    durMs: Math.max(0, Math.floor(Number(rec.durMs) || 0)),
    bytes: Math.max(0, Math.floor(Number(rec.bytes) || 0)),
    createdAt: Math.max(0, Math.floor(Number(rec.createdAt) || 0)),
    emote: emote || '',
  };
}

/** Blob-ish → byte length, without reading it. 0 when it cannot be measured. */
function sizeOf(blob) {
  try {
    if (!blob) return 0;
    if (typeof blob.size === 'number') return blob.size;
    if (typeof blob.byteLength === 'number') return blob.byteLength;
  } catch (_e) { /* fall through */ }
  return 0;
}

/* ----------------------------------------------------------------------------
 * THE STORE
 * -------------------------------------------------------------------------- */

/**
 * @param {object}  o
 * @param {object}  [o.prefs]   ui/prefs.js — owns `voiceEmoteMap`. Absent = no
 *                              associations at all (get() still returns blobs).
 * @param {object}  [o.logger]  console-shaped
 * @param {object}  [o.idb]     TEST AFFORDANCE — an IDBFactory to use instead of
 *                              the global one. Pass `null` to FORCE the in-memory
 *                              backend (which is what node gets anyway).
 * @param {number}  [o.maxNotes]  defaults to VN_MAX_NOTES. Never raised in prod.
 */
export function createNoteStore({ prefs = null, logger = null, idb = undefined, maxNotes = VN_MAX_NOTES } = {}) {
  const log = logger;
  const info = (m) => { try { log?.info?.('[GG notes] ' + m); } catch (_e) { /* ignore */ } };
  const warn = (m) => { try { log?.warn?.('[GG notes] ' + m); } catch (_e) { /* ignore */ } };

  const cap = Math.max(1, Math.floor(Number(maxNotes) || VN_MAX_NOTES));
  let disposed = false;

  /* The in-memory mirror is not a cache — it is the FALLBACK BACKEND, and it is
   * also what every list() answer is built from once a database has been read
   * once. IndexedDB reads are async and a list repaint happens on every click;
   * keeping the metadata here means the screen never waits to redraw a row. */
  const mem = new Map();       // id -> {id, name, durMs, bytes, blob, createdAt}
  let backend = '';            // '' until ready() settles, then 'idb' | 'memory'
  let db = null;
  let readyPromise = null;

  /* ---- prefs / the emote map -------------------------------------------- */

  function readMap() {
    try { return cleanMap(prefs && prefs.get ? prefs.get('voiceEmoteMap') : null); }
    catch (_e) { return {}; }
  }

  function writeMap(map) {
    try { return !!(prefs && prefs.set && prefs.set('voiceEmoteMap', map)); }
    catch (e) { warn('voiceEmoteMap write threw: ' + ((e && e.message) || e)); return false; }
  }

  /* ---- the IndexedDB backend --------------------------------------------
   * Every one of these wraps a request in a promise that RESOLVES on failure
   * rather than rejecting. A storage error is a fact about this machine, not an
   * exception the click handler that caused it should have to catch. */

  function idbFactory() {
    // `idb: null` is an explicit "use memory"; `undefined` means "look it up".
    if (idb !== undefined) return idb;
    try { return (typeof indexedDB !== 'undefined' && indexedDB) || globalThis.indexedDB || null; }
    catch (_e) { return null; }
  }

  function openDb() {
    const factory = idbFactory();
    if (!factory || typeof factory.open !== 'function') return Promise.resolve(null);
    return new Promise((resolve) => {
      let settled = false;
      const done = (value) => { if (!settled) { settled = true; resolve(value); } };
      // The blocked-open guard. See OPEN_TIMEOUT_MS.
      let timer = 0;
      try {
        timer = setTimeout(() => { warn('indexedDB.open timed out — using memory'); done(null); }, OPEN_TIMEOUT_MS);
        if (timer && typeof timer.unref === 'function') timer.unref();
      } catch (_e) { /* a host with no timers simply waits */ }
      let req = null;
      try { req = factory.open(VOICE_DB_NAME, VOICE_DB_VERSION); }
      catch (e) { warn('indexedDB.open threw: ' + ((e && e.message) || e)); done(null); return; }
      req.onupgradeneeded = () => {
        try {
          const d = req.result;
          if (!d.objectStoreNames.contains(VOICE_STORE_NAME)) d.createObjectStore(VOICE_STORE_NAME, { keyPath: 'id' });
        } catch (e) { warn('createObjectStore threw: ' + ((e && e.message) || e)); }
      };
      req.onsuccess = () => { try { clearTimeout(timer); } catch (_e) { /* ignore */ } done(req.result || null); };
      req.onerror = () => { try { clearTimeout(timer); } catch (_e) { /* ignore */ } warn('indexedDB.open failed'); done(null); };
      req.onblocked = () => { warn('indexedDB.open blocked by another tab'); };
    });
  }

  /** One transaction, one operation, never a throw. `fn(store)` returns a request. */
  function tx(mode, fn) {
    if (!db) return Promise.resolve({ ok: false, value: null });
    return new Promise((resolve) => {
      let t = null;
      try { t = db.transaction(VOICE_STORE_NAME, mode); }
      catch (e) { warn('transaction threw: ' + ((e && e.message) || e)); resolve({ ok: false, value: null }); return; }
      let req = null;
      try { req = fn(t.objectStore(VOICE_STORE_NAME)); }
      catch (e) { warn('store op threw: ' + ((e && e.message) || e)); resolve({ ok: false, value: null }); return; }
      if (!req) { resolve({ ok: true, value: null }); return; }
      req.onsuccess = () => resolve({ ok: true, value: req.result });
      req.onerror = () => { warn('store op failed'); resolve({ ok: false, value: null }); };
      try { t.onabort = () => resolve({ ok: false, value: null }); } catch (_e) { /* ignore */ }
    });
  }

  /**
   * Settle the backend and fill the memory mirror. Called by every public verb,
   * memoized, so nothing on the screen has to know that "open the database" is a
   * thing that happens.
   */
  function ready() {
    if (readyPromise) return readyPromise;
    readyPromise = (async () => {
      db = await openDb();
      if (disposed) return backend || 'memory';
      if (!db) { backend = 'memory'; info('no indexedDB — notes are session-only'); return backend; }
      backend = 'idb';
      const all = await tx('readonly', (s) => s.getAll());
      const rows = (all.ok && Array.isArray(all.value)) ? all.value : [];
      for (const r of rows) {
        if (!r || typeof r.id !== 'string') continue;
        mem.set(r.id, {
          id: r.id,
          name: String(r.name || ''),
          durMs: Math.max(0, Math.floor(Number(r.durMs) || 0)),
          bytes: Math.max(0, Math.floor(Number(r.bytes) || sizeOf(r.blob))),
          createdAt: Math.max(0, Math.floor(Number(r.createdAt) || 0)),
          blob: r.blob || null,
        });
      }
      info(`opened (${mem.size} note(s))`);
      return backend;
    })().catch((e) => {
      // Belt and braces: the whole point is that nothing above can reject, but a
      // library screen must not be able to die of a storage bug either.
      warn('open threw: ' + ((e && e.message) || e));
      backend = 'memory';
      return backend;
    });
    return readyPromise;
  }

  /** Oldest first — the order notes were made is the order the player thinks in. */
  function sorted() {
    return Array.from(mem.values()).sort((a, b) => (a.createdAt - b.createdAt) || (a.id < b.id ? -1 : 1));
  }

  /* ------------------------------------------------------------ public API */

  const api = {
    /** Resolves 'idb' | 'memory' once the backend is decided. Never rejects. */
    ready,

    /** 'idb' | 'memory' | '' (not settled yet). Diagnostics and the self-tests. */
    get backend() { return backend; },

    /** The cap this store was built with (VN_MAX_NOTES in production). */
    get max() { return cap; },

    /** Metadata only, oldest first, each row carrying its bound `emote` ('' = none). */
    async list() {
      await ready();
      if (disposed) return [];
      const map = readMap();
      return sorted().map((r) => metaOf(r, emoteForNote(map, r.id)));
    },

    /** How many notes are stored. */
    async count() {
      await ready();
      return mem.size;
    },

    /**
     * THE SHAPE voiceService.sendNote() CONSUMES: {blob, durMs, emote}. It reads
     * exactly those three and nothing else, so the emote is resolved HERE (from
     * the map, by reverse lookup) rather than being something the caller has to
     * know to look up first.
     * @returns {Promise<{id,name,durMs,bytes,createdAt,emote,blob}|null>}
     */
    async get(id) {
      await ready();
      const key = String(id || '');
      if (!key || disposed) return null;
      let rec = mem.get(key) || null;
      // A note the mirror does not have (another tab wrote it): ask the database
      // before answering "no such note".
      if (!rec && backend === 'idb') {
        const got = await tx('readonly', (s) => s.get(key));
        if (got.ok && got.value && got.value.id === key) {
          rec = {
            id: key,
            name: String(got.value.name || ''),
            durMs: Math.max(0, Math.floor(Number(got.value.durMs) || 0)),
            bytes: Math.max(0, Math.floor(Number(got.value.bytes) || sizeOf(got.value.blob))),
            createdAt: Math.max(0, Math.floor(Number(got.value.createdAt) || 0)),
            blob: got.value.blob || null,
          };
          mem.set(key, rec);
        }
      }
      if (!rec) return null;
      return Object.assign(metaOf(rec, emoteForNote(readMap(), rec.id)), { blob: rec.blob || null });
    },

    /**
     * Store one recording.
     * @param {Blob|ArrayBuffer|Uint8Array} blob
     * @param {object} [o]
     * @param {number} [o.durMs]
     * @param {string} [o.name]  the screen supplies it (S.voice.noteName) — this
     *                           tier holds no copy
     * @returns {Promise<{ok:boolean, reason:string, note:object|null}>}
     *   reason: 'added' | 'full' | 'empty' | 'too-big' | 'store-failed' | 'disposed'
     */
    async add(blob, o = {}) {
      await ready();
      if (disposed) return { ok: false, reason: 'disposed', note: null };
      const bytes = sizeOf(blob);
      if (!blob || bytes <= 0) return { ok: false, reason: 'empty', note: null };
      // The SEND ceiling, applied at record time. A note too big to ever leave
      // the machine is not a note — refusing it here is the difference between
      // one honest line now and a mystery failure mid-duel.
      if (bytes > VN_MAX_BYTES) return { ok: false, reason: 'too-big', note: null };
      if (mem.size >= cap) return { ok: false, reason: 'full', note: null };

      const rec = {
        id: makeId(),
        name: String((o && o.name) || '').slice(0, VN_NAME_MAX),
        durMs: Math.max(0, Math.floor(Number(o && o.durMs) || 0)),
        bytes,
        createdAt: Date.now(),
        blob,
      };

      if (backend === 'idb') {
        const put = await tx('readwrite', (s) => s.put(rec));
        // A quota error, a Blob the host cannot structured-clone (very old
        // Safari), a database deleted underneath us: the note is NOT kept in
        // memory either, because a row that vanishes on reload with no warning
        // is the worst of the three outcomes.
        if (!put.ok) return { ok: false, reason: 'store-failed', note: null };
      }
      mem.set(rec.id, rec);
      return { ok: true, reason: 'added', note: metaOf(rec, '') };
    },

    /**
     * Forget one note — and its emote binding with it, in the same call. A map
     * entry pointing at a deleted note would make the emote hook look up a blob
     * that is not there on every fire.
     */
    async remove(id) {
      await ready();
      const key = String(id || '');
      if (!key || !mem.has(key)) return { ok: false, reason: 'missing' };
      if (backend === 'idb') {
        const del = await tx('readwrite', (s) => s.delete(key));
        if (!del.ok) return { ok: false, reason: 'store-failed' };
      }
      mem.delete(key);
      const un = unlinkNote(readMap(), key);
      if (un.changed) writeMap(un.map);
      return { ok: true, reason: 'removed' };
    },

    /** Rename. Kept because the list is auto-named and a player may want better. */
    async rename(id, name) {
      await ready();
      const key = String(id || '');
      const rec = mem.get(key);
      if (!rec) return { ok: false, reason: 'missing' };
      const next = String(name || '').slice(0, VN_NAME_MAX);
      if (next === rec.name) return { ok: true, reason: 'unchanged' };
      const updated = Object.assign({}, rec, { name: next });
      if (backend === 'idb') {
        const put = await tx('readwrite', (s) => s.put(updated));
        if (!put.ok) return { ok: false, reason: 'store-failed' };
      }
      mem.set(key, updated);
      return { ok: true, reason: 'renamed' };
    },

    /* ---- the emote map, through the store so there is ONE writer --------- */

    /** The emote this note fires with, or ''. Synchronous: it is a preference. */
    emoteFor(id) { return emoteForNote(readMap(), id); },

    /** The note an emote fires, or ''. Synchronous, and the emote hook's verb. */
    noteFor(emoteKey) { return noteIdForEmote(readMap(), emoteKey); },

    /** The whole map, copied. The screen paints every row's binding from one read. */
    map() { return readMap(); },

    /**
     * Bind a note to an emote, keeping one-note-per-emote AND one-emote-per-note.
     * @returns {{ok:boolean, moved:string}} `moved` = the note that lost this emote.
     */
    link(emoteKey, noteId) {
      const res = linkEmote(readMap(), emoteKey, noteId);
      if (!res.changed) return { ok: true, moved: '' };
      const wrote = writeMap(res.map);
      return { ok: wrote, moved: res.moved };
    },

    /** "on its own" — the note keeps existing, it just stops riding an emote. */
    unlink(noteId) {
      const res = unlinkNote(readMap(), noteId);
      if (!res.changed) return { ok: true };
      return { ok: writeMap(res.map) };
    },

    /** Drop bindings whose note is gone (another tab, a cleared database). */
    async prune() {
      await ready();
      const res = pruneMap(readMap(), Array.from(mem.keys()));
      if (res.changed) writeMap(res.map);
      return res.changed;
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      mem.clear();
      // The database handle, not the data: closing lets another tab upgrade.
      try { db?.close?.(); } catch (_e) { /* ignore */ }
      db = null;
    },
  };

  return api;
}

export default createNoteStore;
