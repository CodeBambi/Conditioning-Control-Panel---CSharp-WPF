/* ============================================================================
 * ui/zipReader.js — one .zip in, a list of media byte-blobs out.
 *
 * Phones cannot pick a folder, so the only way a player on a phone hands over
 * a library is a zip. This module is the whole of that path: assetsStore's
 * addLocalFiles() detects a zip and asks here for the media inside it, then
 * adopts each entry down the EXACT same road a hand-picked file takes.
 *
 * THE ARCHIVE IS NEVER READ WHOLE. This module reads a zip the way a browser
 * reads a REMOTE zip — by range — except the ranges are Blob.slice() calls on
 * the picked File. Four kinds of read, and nothing else:
 *
 *   1. the TAIL (≤64 KB + 22 B) to find the End Of Central Directory record;
 *   2. the CENTRAL DIRECTORY range the EOCD points at — names, methods and
 *      declared sizes for every entry, which is all the filtering needs;
 *   3. per surviving entry, its 30-byte LOCAL FILE HEADER, to learn where its
 *      data actually starts;
 *   4. per surviving entry, exactly its compressed bytes.
 *
 * A 1 GB library zip therefore costs a tail, a directory, and one entry at a
 * time — never 1 GB of a phone's heap. This replaces an earlier shape that read
 * the whole archive into an ArrayBuffer and refused anything over 512 MB; that
 * refusal was reported as `tooBig`, which the summary line renders with the
 * PER-FILE cap text, so a player with a 1 GB library was told "1 over 8 MB".
 * A big library is the USE CASE. There is no archive-size ceiling here at all.
 *
 * THINGS THAT ARE NOT DECORATION
 *   1. NOTHING HERE THROWS AT IMPORT, and fflate is loaded LAZILY, by dynamic
 *      import, and only when a surviving entry is actually DEFLATED. A static
 *      import would put 90 KB in front of every page load for a feature most
 *      sessions never touch — and an import that throws in WebView2 is a silent
 *      infinite loader, which is this project's worst failure mode.
 *   2. THE CEILINGS ARE THE FEATURE. A zip is attacker-shaped input even when
 *      the attacker is just a curious player: MAX_ENTRIES caps the count,
 *      MAX_TOTAL_BYTES caps the inflated total (the zip-bomb guard, checked
 *      against the DECLARED size before a single byte is inflated), and the
 *      per-entry verdict is the CALLER'S (`classify`) — the store now has a road
 *      for a 12 MB photo (compress it) that it does not have for a 30 MB clip
 *      (nothing a browser can do), and those two refusals are different
 *      sentences. What is gone is only the ceiling on the COMPRESSED archive,
 *      which no longer costs us anything.
 *      A ceiling that trims a LEGITIMATE library reports `trimmed`, never
 *      `failed`: "took the first 500" and "unreadable" are not the same news.
 *   3. THE LOCAL HEADER IS RE-READ ON PURPOSE. A local header's extra field is
 *      routinely a different length from the same entry's central-directory
 *      record (zip64 placeholders, unix timestamps, alignment padding). Taking
 *      the central copy lands the read a few bytes inside the data — the
 *      classic offset bug, and it corrupts silently.
 *   4. NO RECURSION. A zip inside the zip is skipped, not opened. There is no
 *      depth counter to get wrong because there is no depth.
 *   5. A corrupt archive is an ANSWER (`ok:false` + a `reason`), never an
 *      exception, and the caller must never render it as a per-file refusal.
 *
 * WHAT IS NOT CHECKED, AND WHY: entry CRCs. A truncated or scrambled deflate
 * stream makes inflate throw, and every entry's inflated length is compared to
 * its declared size, which catches the rest. CRC-32 over half a gigabyte on a
 * mid-range phone is seconds of main-thread work to re-confirm what two cheaper
 * checks already said.
 * ==========================================================================*/

/** At most this many entries are taken from one archive. */
export const ZIP_MAX_ENTRIES = 500;
/**
 * Inflated bytes per archive, total — the heap-spike backstop, NOT a library
 * ceiling. Entries are streamed one at a time (see `onEntry`), so this bound is
 * about how much work one pick may plan, and 1 GB is a real photo library. Past
 * it the remainder is `trimmed` — TAKEN THE FIRST N, never "unreadable".
 */
export const ZIP_MAX_TOTAL_BYTES = 1024 * 1024 * 1024;
/** The tail we slice hunting the EOCD: the 22-byte record + a max-length comment. */
export const ZIP_EOCD_SCAN_BYTES = 22 + 0xFFFF;
/** Sanity ceiling on the central-directory slice itself (~1M entries' worth). */
export const ZIP_MAX_CENTRAL_BYTES = 64 * 1024 * 1024;
/** Yield to the event loop every this many inflated entries, so the UI paints. */
const YIELD_EVERY = 8;

const SIG_EOCD = 0x06054b50;      // end of central directory
const SIG_EOCD64 = 0x06064b50;    // zip64 end of central directory record
const SIG_LOC64 = 0x07064b50;     // zip64 end of central directory locator
const SIG_CEN = 0x02014b50;       // central directory file header
const SIG_LOC = 0x04034b50;       // local file header

/** Is this name (or File) a zip? Used both to route picks and to refuse nesting. */
export function isZipName(name) {
  return /\.zip$/i.test(String(name || ''));
}

/** A picked File the store should send here instead of adopting directly. */
export function isZipFile(file) {
  const type = String((file && file.type) || '').toLowerCase();
  if (type === 'application/zip' || type === 'application/x-zip-compressed') return true;
  return isZipName(file && file.name);
}

/**
 * Junk every archive from a real machine carries: directory records, macOS
 * resource forks, dotfiles, and anything living inside a hidden folder.
 */
export function isJunkEntry(name) {
  const n = String(name || '');
  if (!n || n.endsWith('/')) return true;                 // directory record
  if (n.indexOf('__MACOSX') === 0) return true;           // macOS resource forks
  for (const seg of n.split('/')) {
    if (seg.indexOf('.') === 0) return true;              // dotfile / ._ junk, at any depth
  }
  return false;
}

/** The archive's own name, without directories — what the player will see. */
export function baseName(name) {
  const n = String(name || '');
  return n.slice(n.lastIndexOf('/') + 1);
}

/* ------------------------------------------------------------------ fflate
 * Loaded once, on first use, and REMEMBERED as a promise so two zips picked in
 * the same breath do not fetch it twice. A failure to load is cached as null,
 * not retried forever: the runtime that cannot import it will not learn to.
 *
 * Only `inflateSync` is wanted now (RAW deflate — no zlib or gzip wrapper),
 * because this module does its own walking. An archive whose surviving entries
 * are all STORED never touches this at all.
 * ------------------------------------------------------------------------ */

let flatePromise = null;

async function loadFlate() {
  if (!flatePromise) {
    flatePromise = import('../vendor/fflate/fflate.module.js')
      .then((m) => (m && typeof m.inflateSync === 'function' ? m : null))
      .catch(() => null);
  }
  return flatePromise;
}

/** Test seam: forget the cached module (and any cached failure). */
export function _resetZipLib() { flatePromise = null; }

/* ------------------------------------------------------------ byte plumbing */

/**
 * Everything this module reads goes through here, so "how much of the archive
 * did we actually touch" is one function's worth of answer.
 */
async function sliceU8(blob, start, end) {
  const size = blob.size;
  const a = Math.max(0, Math.min(size, Math.floor(Number(start) || 0)));
  const b = Math.max(a, Math.min(size, Math.floor(Number(end) || 0)));
  if (b <= a) return new Uint8Array(0);
  const ab = await blob.slice(a, b).arrayBuffer();
  return new Uint8Array(ab);
}

/**
 * A 64-bit little-endian field, as a Number. Zip64 sizes past 2^53 would go
 * lossy here, but every one of them is orders of magnitude past the ceilings
 * that reject the entry anyway — and reading it as two u32s keeps BigInt (and
 * its older-WebView2 gaps) off this path entirely.
 */
function u64(dv, off) {
  return dv.getUint32(off + 4, true) * 4294967296 + dv.getUint32(off, true);
}

/** Blob-like in, Blob-like out. Bytes are wrapped rather than refused. */
function toBlob(src) {
  if (!src) return null;
  if (typeof src.slice === 'function' && typeof src.arrayBuffer === 'function'
      && typeof src.size === 'number') {
    return src;
  }
  // A legacy caller handing over raw bytes still works: wrap them so there is
  // exactly ONE reading path below.
  try {
    const u8 = src instanceof Uint8Array ? src : new Uint8Array(src);
    if (typeof Blob === 'function') return new Blob([u8]);
  } catch (_e) { /* not bytes either */ }
  return null;
}

/** Entry names are UTF-8 in every archive a phone will produce. */
function makeDecoder() {
  try {
    if (typeof TextDecoder === 'function') {
      const td = new TextDecoder('utf-8');
      return (u8) => td.decode(u8);
    }
  } catch (_e) { /* fall through */ }
  return (u8) => {
    let s = '';
    for (let i = 0; i < u8.length; i++) s += String.fromCharCode(u8[i]);
    return s;
  };
}

function yieldTick() {
  return new Promise((res) => { try { setTimeout(res, 0); } catch (_e) { res(); } });
}

/* ------------------------------------------------------------------- EOCD */

/**
 * Find the End Of Central Directory record by slicing ONLY the tail.
 *
 * The signature is scanned for backwards, and a candidate is only trusted when
 * its declared comment length lands the record exactly at the end of the file —
 * without that, a .png inside the archive that happens to contain the four
 * signature bytes can win the scan.
 *
 * @returns {Promise<{entries:number, cdSize:number, cdOff:number}|{err:string}>}
 */
async function findEocd(blob) {
  const size = blob.size;
  if (size < 22) return { err: 'not-a-zip' };
  const tailLen = Math.min(size, ZIP_EOCD_SCAN_BYTES);
  const base = size - tailLen;
  const tail = await sliceU8(blob, base, size);
  if (tail.length < 22) return { err: 'not-a-zip' };
  const dv = new DataView(tail.buffer, tail.byteOffset, tail.byteLength);

  let at = -1;
  let loose = -1;
  for (let i = tail.length - 22; i >= 0; i--) {
    if (dv.getUint32(i, true) !== SIG_EOCD) continue;
    if (i + 22 + dv.getUint16(i + 20, true) === tail.length) { at = i; break; }
    if (loose < 0) loose = i;
  }
  if (at < 0) at = loose;
  if (at < 0) return { err: 'not-a-zip' };

  const entries = dv.getUint16(at + 10, true);
  const cdSize = dv.getUint32(at + 12, true);
  const cdOff = dv.getUint32(at + 16, true);
  const needs64 = entries === 0xFFFF || cdSize === 0xFFFFFFFF || cdOff === 0xFFFFFFFF;
  if (!needs64) return { entries, cdSize, cdOff };

  /* ZIP64. The locator is the 20 bytes immediately before the EOCD and points
   * at the real record. Anything unexpected on this leg is reported as 'zip64'
   * rather than guessed at: a misparsed directory reads as a corrupt archive. */
  const eocdAbs = base + at;
  if (eocdAbs < 20) return { err: 'zip64' };
  const loc = await sliceU8(blob, eocdAbs - 20, eocdAbs);
  if (loc.length < 20) return { err: 'zip64' };
  const lv = new DataView(loc.buffer, loc.byteOffset, loc.byteLength);
  if (lv.getUint32(0, true) !== SIG_LOC64) return { err: 'zip64' };
  const recAt = u64(lv, 8);
  if (!(recAt >= 0) || recAt + 56 > size) return { err: 'zip64' };
  const rec = await sliceU8(blob, recAt, recAt + 56);
  if (rec.length < 56) return { err: 'zip64' };
  const rv = new DataView(rec.buffer, rec.byteOffset, rec.byteLength);
  if (rv.getUint32(0, true) !== SIG_EOCD64) return { err: 'zip64' };
  return { entries: u64(rv, 32), cdSize: u64(rv, 40), cdOff: u64(rv, 48) };
}

/* -------------------------------------------------------- central directory */

/**
 * The zip64 extended-information extra field (id 0x0001) carries ONLY the
 * fields whose base record was written as all-ones, in a fixed order. Reading
 * it positionally is the entire trick.
 */
function applyZip64Extra(cd, from, extraLen, rec) {
  const end = Math.min(cd.length, from + extraLen);
  let p = from;
  const dv = new DataView(cd.buffer, cd.byteOffset, cd.byteLength);
  while (p + 4 <= end) {
    const id = dv.getUint16(p, true);
    const len = dv.getUint16(p + 2, true);
    const body = p + 4;
    if (body + len > end) break;
    if (id === 0x0001) {
      let q = body;
      if (rec.oSize === 0xFFFFFFFF && q + 8 <= body + len) { rec.oSize = u64(dv, q); q += 8; }
      if (rec.cSize === 0xFFFFFFFF && q + 8 <= body + len) { rec.cSize = u64(dv, q); q += 8; }
      if (rec.lho === 0xFFFFFFFF && q + 8 <= body + len) { rec.lho = u64(dv, q); q += 8; }
      return;
    }
    p = body + len;
  }
}

/**
 * Slice the central directory and turn it into records. Nothing is decompressed
 * and no entry data is touched: this is the pass that decides what is worth
 * reading at all.
 */
async function readCentral(blob, cdOff, cdSize) {
  if (!(cdSize > 0) || !(cdOff >= 0) || cdOff + cdSize > blob.size) return { err: 'unreadable' };
  if (cdSize > ZIP_MAX_CENTRAL_BYTES) return { err: 'unreadable' };
  const cd = await sliceU8(blob, cdOff, cdOff + cdSize);
  if (cd.length < 46) return { err: 'unreadable' };
  const dv = new DataView(cd.buffer, cd.byteOffset, cd.byteLength);
  const dec = makeDecoder();
  const recs = [];
  let p = 0;
  while (p + 46 <= cd.length) {
    if (dv.getUint32(p, true) !== SIG_CEN) break;
    const nameLen = dv.getUint16(p + 28, true);
    const extraLen = dv.getUint16(p + 30, true);
    const cmtLen = dv.getUint16(p + 32, true);
    const nameEnd = p + 46 + nameLen;
    if (nameEnd > cd.length) break;
    const rec = {
      name: dec(cd.subarray(p + 46, nameEnd)),
      flags: dv.getUint16(p + 8, true),
      method: dv.getUint16(p + 10, true),
      cSize: dv.getUint32(p + 20, true),
      oSize: dv.getUint32(p + 24, true),
      lho: dv.getUint32(p + 42, true),
    };
    if (rec.oSize === 0xFFFFFFFF || rec.cSize === 0xFFFFFFFF || rec.lho === 0xFFFFFFFF) {
      applyZip64Extra(cd, nameEnd, extraLen, rec);
    }
    recs.push(rec);
    p = nameEnd + extraLen + cmtLen;
  }
  return { recs };
}

/* ------------------------------------------------------------- entry bytes */

/**
 * Read ONE entry: its local header (for the real data offset), then exactly its
 * compressed bytes, then stored-or-inflated. Returns null for every way an
 * entry can be unreadable — the caller counts one `failed`, never a dead
 * archive.
 */
async function readEntryBytes(blob, w, flate) {
  const head = await sliceU8(blob, w.lho, w.lho + 30);
  if (head.length < 30) return null;
  const hv = new DataView(head.buffer, head.byteOffset, head.byteLength);
  if (hv.getUint32(0, true) !== SIG_LOC) return null;
  // THE LOCAL EXTRA FIELD IS ITS OWN LENGTH. See note 3 in the header: taking
  // the central record's extraLen here is the classic silent-corruption bug.
  const dataAt = w.lho + 30 + hv.getUint16(26, true) + hv.getUint16(28, true);
  if (dataAt + w.cSize > blob.size) return null;

  const comp = await sliceU8(blob, dataAt, dataAt + w.cSize);
  if (comp.length !== w.cSize) return null;

  if (w.method === 0) return comp.length === w.oSize ? comp : null;
  if (w.method !== 8) return null;                       // bzip2/lzma/xz — not ours to read
  if (!flate) return null;
  const inf = flate.inflateSync(comp);
  // The declared size is the contract. A stream that inflates to a different
  // length is either lying or damaged, and either way it is not adoptable.
  return (inf && inf.length === w.oSize) ? inf : null;
}

/**
 * Pull the eligible media out of one zip, reading only the ranges it needs.
 *
 * The caller owns what "eligible" means — assetsStore passes its own mime
 * table, so the picker, the wire's allowlist and this filter can never drift
 * apart into "the zip adopted a file the offer gate then refused".
 *
 * @param {Blob|File|ArrayBuffer|Uint8Array} src  the archive; a File is the point
 * @param {object} [o]
 * @param {(name:string)=>boolean} [o.isEligible]  name -> keep it? (default: keep all non-junk)
 * @param {(name:string, size:number)=>('take'|'too-big'|'too-big-video')} [o.classify]
 *   THE PER-ENTRY SIZE POLICY, owned by the caller. A single `maxEntryBytes`
 *   cannot express what the store actually does now — a still up to the decode
 *   cap is compressible, a video past the wire's artifact cap is not sendable at
 *   all, and those two refusals are DIFFERENT SENTENCES to the player. Without
 *   it, `maxEntryBytes` alone decides, exactly as before.
 * @param {number} [o.maxEntryBytes]     hard backstop; bigger entries count as tooBig
 * @param {number} [o.maxEntries]        default ZIP_MAX_ENTRIES
 * @param {number} [o.maxTotalBytes]     default ZIP_MAX_TOTAL_BYTES (INFLATED bytes)
 * @param {(planned:number)=>any} [o.onPlan]  how many entries survived pass 1, called
 *   BEFORE the first one is inflated. The standalone card needs it to say
 *   "adding 37 / 214" instead of counting up to a total nobody knows.
 * @param {(e:{name:string,bytes:Uint8Array})=>any} [o.onEntry]  hand each entry over as
 *   it comes out (awaited) INSTEAD of collecting it. Without this the result
 *   array holds every extracted entry at once — bounded by maxTotalBytes, but
 *   that bound is a gigabyte, which is not a thing to hold on a phone.
 *   assetsStore streams; the tests read the array.
 * @returns {Promise<{ok:boolean, reason:string, entries:Array<{name:string, bytes:Uint8Array}>,
 *   tooBig:number, tooBigVideo:number, failed:number, trimmed:number, skipped:number,
 *   truncated:boolean}>}
 *   `reason` on failure is one of: 'not-a-zip' | 'unreadable' | 'zip64' | 'no-zip-lib'.
 *   `trimmed` is the count the CEILINGS left behind — a legitimate library that
 *   ran past 500 entries or the inflated total. It is deliberately not `failed`:
 *   "took the first 500" and "unreadable" are not the same news.
 */
export async function readZipMedia(src, o = {}) {
  const out = {
    ok: false, reason: '', entries: [], took: 0,
    tooBig: 0, tooBigVideo: 0, failed: 0, trimmed: 0, skipped: 0, truncated: false,
  };
  const eligible = typeof o.isEligible === 'function' ? o.isEligible : () => true;
  const classify = typeof o.classify === 'function' ? o.classify : null;
  const onEntry = typeof o.onEntry === 'function' ? o.onEntry : null;
  const onPlan = typeof o.onPlan === 'function' ? o.onPlan : null;
  const maxEntry = Math.max(1, Number(o.maxEntryBytes) || Infinity);
  const maxEntries = Math.max(1, Math.floor(Number(o.maxEntries) || ZIP_MAX_ENTRIES));
  const maxTotal = Math.max(1, Number(o.maxTotalBytes) || ZIP_MAX_TOTAL_BYTES);

  const blob = toBlob(src);
  if (!blob || blob.size < 22) { out.reason = 'not-a-zip'; return out; }   // shorter than an EOCD

  /* Pass 0 — the tail, then the directory. Two slices, whatever the archive
   * weighs. Every failure here is ARCHIVE-level and must be reported as such:
   * rendering it as a per-file refusal is how a 1 GB library once got told it
   * was "over 8 MB". */
  let dir = null;
  try {
    const eocd = await findEocd(blob);
    if (eocd.err) { out.reason = eocd.err; return out; }
    if (!eocd.entries && !eocd.cdSize) { out.ok = true; return out; }      // a genuinely empty archive
    const central = await readCentral(blob, eocd.cdOff, eocd.cdSize);
    if (central.err) { out.reason = central.err; return out; }
    if (!central.recs.length) { out.reason = 'unreadable'; return out; }
    dir = central.recs;
  } catch (_e) {
    out.reason = 'unreadable';
    return out;
  }

  /* Pass 1 — decide from names and DECLARED sizes alone. Nothing is read, and
   * nothing is decompressed: this is what makes a 1 GB archive holding one gif
   * cost one gif. */
  const wanted = [];
  let planned = 0;
  let needFlate = false;
  for (const rec of dir) {
    const name = String(rec.name || '');
    if (isJunkEntry(name)) { out.skipped++; continue; }
    if (isZipName(name)) { out.skipped++; continue; }             // no recursion, ever
    if (!eligible(baseName(name))) { out.skipped++; continue; }
    if (rec.flags & 0x0001) { out.failed++; continue; }           // encrypted — unreadable, honestly
    const size = Math.max(0, Number(rec.oSize) || 0);
    if (!size) { out.tooBig++; continue; }
    // THE POLICY RUNS FIRST, the backstop second: a 90 MB video must be counted
    // as "too big to send", not as "over the decode cap" — the player is told
    // two different true things and only one of them is actionable.
    const verdict = classify ? String(classify(baseName(name), size) || 'take') : (size > maxEntry ? 'too-big' : 'take');
    if (verdict === 'too-big-video') { out.tooBigVideo++; continue; }
    if (verdict !== 'take' || size > maxEntry) { out.tooBig++; continue; }
    // Compressed bytes are what we actually slice, so they get their own bound:
    // deflate cannot inflate a stream materially larger than its output, and a
    // record claiming otherwise is malformed.
    if (!(rec.cSize >= 0) || rec.cSize > size + 65536) { out.failed++; continue; }
    if (wanted.length >= maxEntries) { out.truncated = true; out.trimmed++; continue; }
    if (planned + size > maxTotal) { out.truncated = true; out.trimmed++; continue; }
    planned += size;
    if (rec.method === 8) needFlate = true;
    wanted.push({
      name, base: baseName(name),
      method: rec.method, cSize: rec.cSize, oSize: size, lho: rec.lho,
    });
  }

  /* The plan is the only honest denominator for a progress line, and it is known
   * BEFORE any inflating happens — pass 1 read nothing but the directory. */
  if (onPlan) { try { onPlan(wanted.length); } catch (_e) { /* a reporter never fails a read */ } }

  /* fflate is asked for ONLY here, and only if something survived that is
   * actually deflated. A STORED-only archive never pays the 90 KB. */
  let flate = null;
  if (needFlate) {
    flate = await loadFlate();
    if (!flate) { out.reason = 'no-zip-lib'; return out; }
  }

  /* Pass 2 — one entry at a time: its local header, its compressed bytes, its
   * inflate. Peak memory is ONE ENTRY, not one archive — provided the caller
   * takes each one via onEntry rather than letting the array grow. Yields
   * periodically so the standalone card can repaint between a hundred photos. */
  for (let i = 0; i < wanted.length; i++) {
    const w = wanted[i];
    try {
      const bytes = await readEntryBytes(blob, w, flate);
      if (bytes && bytes.length) {
        out.took++;
        if (onEntry) await onEntry({ name: w.base, bytes });
        else out.entries.push({ name: w.base, bytes });
      } else out.failed++;
    } catch (_e) { out.failed++; }
    if ((i % YIELD_EVERY) === YIELD_EVERY - 1) await yieldTick();
  }

  out.ok = true;
  return out;
}

export default readZipMedia;
