// exec/receivedStore.js — where a duel partner's media actually lands (spec §6).
//
// It implements the ReceivedStore contract documented in net/mediaChannel.js, verbatim:
// has / partialLength / begin / write / knowsBlocked are SYNCHRONOUS (the offer gate has to
// resolve in one turn), commit is async, abort is fire-and-forget.
//
// TWO BACKENDS, one contract:
//
//   HOSTED (WebView2)  disk, through the host: goon-recv-begin/chunk/commit/abort/drop ->
//     Services/GoonGame/TransferInboxStore.cs -> transfer-cache/recv/<sha>.<ext>, served back as
//     https://ccp.cache/recv/... A 24 MB blob: URL in a <video> forces the whole artifact into
//     renderer memory and breaks seeking; a virtual-host URL streams exactly like the existing
//     ccp.assets path, release() stays the no-op it already is, and cross-session persistence is free.
//
//   STANDALONE (a plain browser)  an in-memory Blob map capped at MEM_STORE_BYTES with a REAL
//     refcount: at zero the object URL is revoked, and the next view() mints a fresh one off the
//     retained Blob. It is what makes the feature exercisable without the app, and it is the one
//     backend where release() means anything (a floating window and a reSource retry can both hold
//     the same sha). commit() hashes page-side via crypto.subtle — the honest stand-in for the
//     host-side verification, which is the real security boundary.
//
// WHY THE PAGE BUFFERS AND THE HOST ACKS
//   `write(sha, offset, bytes)` is SYNCHRONOUS by contract and the bridge is asynchronous, so
//   writes accumulate in memory and a serialized chain drains them to the host in ~256 KiB
//   base64 chunks (TransferInboxStore.MaxChunkB64Chars is 350 000, and 262 144 binary bytes
//   encode to 349 528). Every verb — begin, chunk, commit, abort, drop — gets its own
//   `goon-recv-result`, so nothing is fire-and-forget: a chunk that fails poisons the job and
//   commit reports it. Because ALL of those replies carry the SAME job id, correlation is a
//   per-id FIFO queue rather than a map lookup; the host enqueues its replies in request order
//   (begin/chunk synchronously, commit last of all), so FIFO is exact.
//
// RESUME ACROSS A PAGE RELOAD IS NOT A REQUIREMENT. `partialLength` answers from the in-session
// job table, so a reload restarts a half-transfer from 0 even though the host still holds the
// .part file. That is a deliberate v1 simplification: the host CAN resume (TransferInboxStore
// keeps recv-<sha>.part and its Begin() adopts it), and teaching this module to ask for it is a
// bonus, not a correctness gap — a restarted transfer is slower, never wrong.
//
// bridge.on('goon-recv-result') is registered HERE, not in boot.js: bridge.on throws on a
// duplicate, and the module that owns the request/reply correlation owns the handler. Exactly the
// net-post-result precedent.

import * as defaultBridge from '../bridge.js';

/** The in-memory backend's whole budget. Nothing persists; this is the ceiling per
 * session. Sized to hold a few artifacts at the 64 MB wire cap — a phone browser
 * is the only runtime that lives on this backend, so it stays deliberately
 * smaller than MAX_SESSION_BYTES and old entries are evicted refcount-first. */
export const MEM_STORE_BYTES = 192 * 1024 * 1024;

/**
 * Binary bytes per bridge chunk. 262 144 -> 349 528 base64 chars, inside the host's
 * MaxChunkB64Chars = 350 000. Do not raise it without moving that constant too.
 */
export const CHUNK_BYTES = 256 * 1024;

/** How long a begin/chunk/commit round trip may hang before the job is called dead. */
export const RECV_TIMEOUT_MS = 30000;

/** The DtrhLoomStore error vocabulary, which net/mediaChannel.js maps onto xfer_fail. */
export const RECV_ERRORS = Object.freeze([
  'bad-name', 'too-big', 'bad-format', 'hash-mismatch', 'cap-reached', 'io-failed',
]);

const SHA_RE = /^[0-9a-f]{64}$/;

/** mime -> the kind exec/media.js speaks. Anything else is not ours to store. */
const MIME_KIND = Object.freeze({
  'image/png': 'image', 'image/jpeg': 'image', 'image/gif': 'image', 'image/webp': 'image',
  'video/mp4': 'video', 'video/webm': 'video', 'video/quicktime': 'video',
});

/** ext -> mime, for the manifest's `received` rows (which carry both anyway). */
const EXT_MIME = Object.freeze({
  png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', gif: 'image/gif',
  webp: 'image/webp', mp4: 'video/mp4', webm: 'video/webm', mov: 'video/quicktime',
});

export function kindForMime(mime) { return MIME_KIND[String(mime || '').toLowerCase()] || ''; }

/** ArrayBuffer|TypedArray -> Uint8Array, or null. The wire is untrusted. */
function bytesOf(v) {
  if (v instanceof ArrayBuffer) return new Uint8Array(v);
  if (v && typeof v === 'object' && v.buffer instanceof ArrayBuffer
    && typeof v.byteLength === 'number' && typeof v.byteOffset === 'number') {
    return new Uint8Array(v.buffer, v.byteOffset, v.byteLength);
  }
  return null;
}

/** Base64 without a 24 MB argument spread (String.fromCharCode(...bytes) blows the stack). */
export function toBase64(u8) {
  const b = bytesOf(u8) || new Uint8Array(0);
  if (typeof btoa === 'function') {
    let s = '';
    const STEP = 0x8000;
    for (let i = 0; i < b.length; i += STEP) {
      s += String.fromCharCode.apply(null, b.subarray(i, Math.min(i + STEP, b.length)));
    }
    return btoa(s);
  }
  // node without btoa (very old) — Buffer is the only other thing that can do this.
  try { return Buffer.from(b).toString('base64'); } catch (_e) { return ''; }
}

/** Lower-case hex sha256 of the bytes, or '' when this runtime cannot hash. */
export async function sha256Hex(u8) {
  const b = bytesOf(u8) || new Uint8Array(0);
  let subtle = null;
  try {
    const c = (typeof crypto !== 'undefined' && crypto) ? crypto : null;
    subtle = c && c.subtle ? c.subtle : null;
  } catch (_e) { subtle = null; }
  if (!subtle) return '';
  try {
    const copy = new Uint8Array(b);               // detach from any pooled buffer
    const digest = await subtle.digest('SHA-256', copy.buffer);
    const out = new Uint8Array(digest);
    let s = '';
    for (let i = 0; i < out.length; i++) s += out[i].toString(16).padStart(2, '0');
    return s;
  } catch (_e) { return ''; }
}

/**
 * @param {object} [o]
 * @param {object} [o.bridge]  injectable for the node selftests (defaults to the real one)
 * @param {boolean} [o.hosted] force a backend; defaults to bridge.isHosted
 * @param {object} [o.logger]
 * @param {number} [o.memCapBytes] TEST AFFORDANCE for the memory backend
 * @param {number} [o.timeoutMs] TEST AFFORDANCE
 */
export function createReceivedStore({
  bridge = defaultBridge, hosted, logger = null, memCapBytes, timeoutMs,
} = {}) {
  const isHosted = hosted === undefined ? !!(bridge && bridge.isHosted) : !!hosted;
  const MEM_CAP = Number.isFinite(memCapBytes) ? memCapBytes : MEM_STORE_BYTES;
  const TIMEOUT = Number.isFinite(timeoutMs) ? timeoutMs : RECV_TIMEOUT_MS;

  const info = (m) => { try { logger?.info?.('[GG recv] ' + m); } catch (_e) { /* ignore */ } };
  const warn = (m) => { try { logger?.warn?.('[GG recv] ' + m); } catch (_e) { /* ignore */ } };

  /** sha -> {sha, ext, mime, kind, bytes, url} — everything COMMITTED and usable. */
  const index = new Map();
  /** sha -> job — everything half-written. */
  const jobs = new Map();
  /** Shas that landed during THIS page's lifetime (the recap report card's shortlist). */
  const thisSession = new Set();

  /** Memory backend only: sha -> {blob, url, refs, bytes, mime, kind}. */
  const mem = new Map();
  let memBytes = 0;

  let seq = 0;
  let disposed = false;

  /* ------------------------------------------------------------------ bridge I/O */

  /** id -> [{resolve, timer}] — FIFO, because every verb replies with the same job id. */
  const waiters = new Map();

  function onResult(m) {
    if (!m || typeof m.id !== 'string') return;
    const q = waiters.get(m.id);
    if (!q || !q.length) return;                       // late reply after a timeout — drop
    const w = q.shift();
    if (!q.length) waiters.delete(m.id);
    try { clearTimeout(w.timer); } catch (_e) { /* ignore */ }
    w.resolve({
      ok: !!m.ok,
      url: typeof m.url === 'string' ? m.url : null,
      bytes: Number(m.bytes) || 0,
      error: typeof m.error === 'string' && m.error ? m.error : null,
    });
  }

  if (isHosted) {
    // THE ONE HANDLER. boot.js must not register this (bridge.on would throw) — the
    // module that correlates the replies owns them.
    try { bridge.on('goon-recv-result', onResult); }
    catch (e) { warn('could not own goon-recv-result: ' + ((e && e.message) || e)); }
  }

  /** Post one verb and wait for ITS reply. Never rejects: a dead host resolves io-failed. */
  function ask(msg) {
    if (!isHosted || disposed) return Promise.resolve({ ok: false, url: null, bytes: 0, error: 'io-failed' });
    return new Promise((resolve) => {
      let timer = 0;
      try {
        timer = setTimeout(() => {
          const q = waiters.get(msg.id);
          if (q) {
            const at = q.findIndex((x) => x.timer === timer);
            if (at >= 0) q.splice(at, 1);
            if (!q.length) waiters.delete(msg.id);
          }
          warn(`${msg.type} timed out after ${TIMEOUT}ms`);
          resolve({ ok: false, url: null, bytes: 0, error: 'io-failed' });
        }, TIMEOUT);
        if (timer && typeof timer.unref === 'function') timer.unref();
      } catch (_e) { timer = 0; }

      if (!waiters.has(msg.id)) waiters.set(msg.id, []);
      waiters.get(msg.id).push({ resolve, timer });
      try { bridge.send(msg); } catch (e) {
        warn(`${msg.type} send failed: ${(e && e.message) || e}`);
      }
    });
  }

  /* ------------------------------------------------------------------ the jobs */

  function newJob(sha, mime, bytes, origin) {
    return {
      id: 'r' + (++seq) + '-' + sha.slice(0, 8),
      sha, mime, declared: bytes,
      kind: kindForMime(mime),
      /* 'gif' or ''. ADVISORY, and it never touches a filename, a size or a hash — it is only
       * ever read back by exec/media.js to keep a converted gif loop out of the VIDEO lane while
       * real footage is available. An old peer sends none and the row reads as footage. */
      origin: origin === 'gif' ? 'gif' : '',
      written: 0,            // bytes handed to us by the protocol (the resume offset)
      seq: 0,                // next chunk sequence number the host expects
      buf: [],               // Uint8Array pieces not yet flushed
      buffered: 0,
      chain: Promise.resolve(),
      error: null,           // the first failure poisons the job; commit reports it
      begun: false,
    };
  }

  /** Queue work behind everything already queued for this job. Order IS the contract. */
  function after(job, fn) {
    job.chain = job.chain.then(fn).catch((e) => {
      if (!job.error) job.error = 'io-failed';
      warn(`job ${job.id} chain threw: ${(e && e.message) || e}`);
    });
    return job.chain;
  }

  /** Take up to `want` bytes off the front of the job's buffer. */
  function take(job, want) {
    const out = new Uint8Array(want);
    let at = 0;
    while (at < want && job.buf.length) {
      const head = job.buf[0];
      const n = Math.min(head.length, want - at);
      out.set(head.subarray(0, n), at);
      at += n;
      if (n === head.length) job.buf.shift();
      else job.buf[0] = head.subarray(n);
    }
    job.buffered -= at;
    return at === want ? out : out.subarray(0, at);
  }

  /** Drain full chunks (or everything, at commit) to the host, strictly in order. */
  function drain(job, all) {
    while (job.buffered >= CHUNK_BYTES || (all && job.buffered > 0)) {
      const piece = take(job, Math.min(CHUNK_BYTES, job.buffered));
      const s = job.seq++;
      after(job, async () => {
        if (job.error) return;                    // a poisoned job stops spending bridge traffic
        const r = await ask({
          type: 'goon-recv-chunk', id: job.id, sha256: job.sha, seq: s, b64: toBase64(piece),
        });
        if (!r.ok) job.error = r.error || 'io-failed';
      });
    }
  }

  /* ------------------------------------------------------------ memory backend */

  function memBegin(sha, mime, bytes) {
    if (memBytes + bytes > MEM_CAP) return false;
    return true;
  }

  function memUrl(rec) {
    if (rec.url) return rec.url;
    try { rec.url = URL.createObjectURL(rec.blob); } catch (_e) { rec.url = ''; }
    return rec.url;
  }

  async function memCommit(job) {
    const all = new Uint8Array(job.written);
    let at = 0;
    for (const piece of job.buf) { all.set(piece, at); at += piece.length; }
    job.buf.length = 0;
    job.buffered = 0;

    if (all.length !== job.declared) return { ok: false, error: 'too-big' };

    // The page-side stand-in for the host's verification. A runtime with no
    // crypto.subtle (an http: dev origin) skips it rather than failing everything —
    // this backend is a dev affordance and its threat model is "my own browser".
    const actual = await sha256Hex(all);
    if (actual && actual !== job.sha) return { ok: false, error: 'hash-mismatch' };

    if (memBytes + all.length > MEM_CAP) return { ok: false, error: 'cap-reached' };

    let blob = null;
    try { blob = new Blob([all], { type: job.mime }); } catch (_e) { blob = null; }
    if (!blob) return { ok: false, error: 'io-failed' };

    const rec = { blob, url: '', refs: 0, bytes: all.length, mime: job.mime, kind: job.kind };
    mem.set(job.sha, rec);
    memBytes += all.length;
    return { ok: true, url: memUrl(rec), bytes: all.length };
  }

  function memDrop(sha) {
    const rec = mem.get(sha);
    if (!rec) return false;
    mem.delete(sha);
    memBytes = Math.max(0, memBytes - rec.bytes);
    if (rec.url) { try { URL.revokeObjectURL(rec.url); } catch (_e) { /* ignore */ } rec.url = ''; }
    rec.blob = null;
    return true;
  }

  /* ------------------------------------------------------------------ contract */

  /** Is this artifact COMMITTED and usable right now? Answers decline:'have'. */
  function has(sha) {
    return typeof sha === 'string' && SHA_RE.test(sha) && index.has(sha);
  }

  /**
   * The resume offset. IN-SESSION ONLY — see the header. 0 after a page reload even
   * though the host still holds the partial.
   */
  function partialLength(sha) {
    const job = jobs.get(sha);
    return job ? job.written : 0;
  }

  /**
   * Open — or REOPEN, for a resume — the partial for `sha`. Idempotent by contract.
   * @param {{origin?:string, codec?:string}} [meta] the offer's advisory metadata. OPTIONAL in
   *   the ReceivedStore contract, so nothing breaks when a caller (or a test stub) omits it.
   */
  function begin(sha, mime, bytes, meta) {
    if (disposed) return false;
    if (typeof sha !== 'string' || !SHA_RE.test(sha)) return false;
    if (!kindForMime(mime)) return false;
    if (!Number.isInteger(bytes) || bytes < 1) return false;

    const live = jobs.get(sha);
    if (live) {
      // A resume: keep the partial and everything already acked. Re-opening must
      // never truncate — the protocol has already told the sender from_offset.
      live.error = null;
      return true;
    }

    if (!isHosted && !memBegin(sha, mime, bytes)) {
      warn(`memory store full — refusing ${sha.slice(0, 8)} (${bytes} bytes)`);
      return false;
    }

    const job = newJob(sha, mime, bytes, meta && meta.origin);
    jobs.set(sha, job);

    if (isHosted) {
      // Sent optimistically: `begin` is synchronous by contract, so a host refusal
      // (cap-reached, bad-format) poisons the job and surfaces at commit as the
      // same store failure the protocol would have reported here.
      // `origin` rides along so the host can persist it: without that, a gif-origin clip
      // received today comes back from recv_index.json tomorrow looking like footage.
      after(job, async () => {
        const r = await ask({
          type: 'goon-recv-begin', id: job.id, sha256: sha, mime, bytes, origin: job.origin,
        });
        job.begun = r.ok;
        if (!r.ok) {
          job.error = r.error || 'io-failed';
          warn(`host refused ${sha.slice(0, 8)}: ${job.error}`);
        }
      });
    } else {
      job.begun = true;
    }
    return true;
  }

  /** Append at exactly `offset`. Synchronous; the flush happens behind it. */
  function write(sha, offset, bytes) {
    const job = jobs.get(sha);
    if (!job || disposed) return false;
    if (job.error) return false;
    if (offset !== job.written) return false;              // never a seek past the end
    const u8 = bytesOf(bytes);
    if (!u8 || u8.length === 0) return false;
    if (job.written + u8.length > job.declared) return false;

    // COPY. The protocol hands us a slice of a channel frame and reuses nothing,
    // but a buffer we hold across an await must be ours.
    const piece = new Uint8Array(u8);
    job.buf.push(piece);
    job.buffered += piece.length;
    job.written += piece.length;
    if (isHosted) drain(job, false);
    return true;
  }

  /** Finish the artifact. THE HOST verifies the sha; the page's is a claim. */
  async function commit(sha) {
    const job = jobs.get(sha);
    if (!job) return { ok: false, error: 'io-failed' };

    let result;
    if (isHosted) {
      drain(job, true);                                    // everything left, however short
      await job.chain;                                     // …and wait for the whole queue
      if (job.error) result = { ok: false, error: job.error };
      else {
        const r = await ask({ type: 'goon-recv-commit', id: job.id, sha256: sha });
        result = r.ok
          ? { ok: true, url: r.url || '', bytes: r.bytes || job.declared }
          : { ok: false, error: r.error || 'io-failed' };
      }
    } else {
      result = await memCommit(job);
    }

    jobs.delete(sha);

    if (!result.ok) {
      info(`commit rejected for ${sha.slice(0, 8)}: ${result.error}`);
      return result;
    }

    // The host's URL carries the SNIFFED extension, which is the authoritative one;
    // a blob: URL carries nothing, so the memory backend falls back to the mime.
    const ext = isHosted ? (extFromUrl(result.url) || extFromMime(job.mime)) : extFromMime(job.mime);
    index.set(sha, {
      sha, ext, mime: job.mime, kind: job.kind, origin: job.origin,
      bytes: result.bytes || job.declared,
      url: result.url || '',
    });
    thisSession.add(sha);
    info(`received ${sha.slice(0, 8)} (${Math.round((result.bytes || job.declared) / 1024)} KB, ${job.mime})`);
    return { ok: true, url: result.url || '', bytes: result.bytes || job.declared };
  }

  /** Throw the partial away. Cancel, terminal channel death, every failure. */
  function abort(sha) {
    const job = jobs.get(sha);
    if (!job) return;
    jobs.delete(sha);
    job.buf.length = 0;
    job.buffered = 0;
    job.error = job.error || 'aborted';
    if (isHosted) void ask({ type: 'goon-recv-abort', id: job.id });
  }

  /* ------------------------------------------------------------------ the rest */

  function extFromUrl(url) {
    const m = /\.([a-z0-9]{2,5})(?:\?|#|$)/i.exec(String(url || ''));
    return m ? m[1].toLowerCase() : '';
  }
  function extFromMime(mime) {
    for (const [ext, mm] of Object.entries(EXT_MIME)) if (mm === mime) return ext;
    return '';
  }

  return {
    /** Which backend is live. The whole difference the rest of the page can see. */
    get backend() { return isHosted ? 'host' : 'memory'; },

    // --- the ReceivedStore contract net/mediaChannel.js consumes -----------------
    has,
    partialLength,
    begin,
    write,
    commit,
    abort,

    /**
     * Prime the index from the manifest frame's `received:[{sha,ext,mime,bytes,origin?}]`.
     * boot.js owns the 'manifest' handler and passes the array in — this module
     * registers no manifest listener of its own (one handler per type, always).
     * @returns {string[]} the shas now known, for the blocklist prefetch.
     */
    primeReceived(list) {
      const out = [];
      for (const raw of (Array.isArray(list) ? list : [])) {
        const e = raw || {};
        const sha = typeof e.sha === 'string' ? e.sha : '';
        if (!SHA_RE.test(sha)) continue;
        const ext = String(e.ext || '').toLowerCase();
        const mime = String(e.mime || EXT_MIME[ext] || '');
        const kind = kindForMime(mime);
        if (!kind) continue;
        index.set(sha, {
          sha, ext, mime, kind,
          // Absent on rows written before the host persisted it — and absent reads as footage,
          // which is exactly how those rows behaved before the preference existed.
          origin: e.origin === 'gif' ? 'gif' : '',
          bytes: Math.max(0, Number(e.bytes) || 0),
          url: 'https://ccp.cache/recv/' + sha + '.' + ext,
        });
        out.push(sha);
      }
      if (out.length) info(`primed ${out.length} artifact(s) from a previous session`);
      return out;
    },

    /** Everything committed, as exec/media.js wants it (`origin` included — addReceived reads it). */
    list() {
      return Array.from(index.values()).map((e) => ({
        sha: e.sha, kind: e.kind, mime: e.mime, bytes: e.bytes, url: e.url, origin: e.origin || '',
      }));
    },

    /** Only what landed during THIS page's life — the recap report card's shortlist. */
    thisMatch() {
      return Array.from(thisSession).map((sha) => index.get(sha)).filter(Boolean).map((e) => ({
        sha: e.sha, kind: e.kind, mime: e.mime, bytes: e.bytes, url: e.url, origin: e.origin || '',
      }));
    },

    /**
     * A refcounted view for a renderer. On the disk backend release() is the no-op
     * it has always been; on the memory backend the object URL is revoked at zero
     * and re-minted on the next view, which is what makes release() real.
     */
    view(sha) {
      const e = index.get(sha);
      if (!e) return null;
      if (!isHosted) {
        const rec = mem.get(sha);
        if (!rec || !rec.blob) return null;
        rec.refs++;
        let released = false;
        return {
          url: memUrl(rec), kind: e.kind, mime: e.mime, bytes: e.bytes,
          release() {
            if (released) return;
            released = true;
            rec.refs = Math.max(0, rec.refs - 1);
            if (rec.refs === 0 && rec.url) {
              try { URL.revokeObjectURL(rec.url); } catch (_e) { /* ignore */ }
              rec.url = '';                    // re-minted lazily by the next view()
            }
          },
        };
      }
      return { url: e.url, kind: e.kind, mime: e.mime, bytes: e.bytes, release() {} };
    },

    /** Blocklist sweep / user delete. Removes the file AND the index row. */
    drop(sha) {
      if (typeof sha !== 'string' || !SHA_RE.test(sha)) return false;
      const had = index.delete(sha);
      thisSession.delete(sha);
      if (isHosted) void ask({ type: 'goon-recv-drop', id: 'd' + (++seq) + '-' + sha.slice(0, 8), sha256: sha });
      else memDrop(sha);
      if (had) info(`dropped ${sha.slice(0, 8)}`);
      return had;
    },

    stats() {
      let bytes = 0;
      for (const e of index.values()) bytes += e.bytes;
      return {
        backend: isHosted ? 'host' : 'memory',
        held: index.size, bytes,
        inFlight: jobs.size,
        thisSession: thisSession.size,
        memBytes: isHosted ? 0 : memBytes,
        memCap: isHosted ? 0 : MEM_CAP,
        waiting: waiters.size,
      };
    },

    /** Page teardown. Committed artifacts survive on disk; memory ones do not. */
    dispose() {
      if (disposed) return;
      // Abort BEFORE the flag goes up: `ask` refuses to post once disposed, and the
      // host would be left holding .part files nobody is ever going to finish.
      for (const sha of Array.from(jobs.keys())) abort(sha);
      disposed = true;
      if (!isHosted) for (const sha of Array.from(mem.keys())) memDrop(sha);
      for (const q of waiters.values()) {
        for (const w of q) {
          try { clearTimeout(w.timer); } catch (_e) { /* ignore */ }
          try { w.resolve({ ok: false, url: null, bytes: 0, error: 'io-failed' }); } catch (_e) { /* ignore */ }
        }
      }
      waiters.clear();
    },
  };
}

export default createReceivedStore;
