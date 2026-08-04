/* ============================================================================
 * ui/zipReader.js — one .zip in, a list of media byte-blobs out.
 *
 * Phones cannot pick a folder, so the only way a player on a phone hands over
 * a library is a zip. This module is the whole of that path: assetsStore's
 * addLocalFiles() detects a zip and asks here for the media inside it, then
 * adopts each entry down the EXACT same road a hand-picked file takes.
 *
 * THE APPROACH IS BORROWED, ON PURPOSE. cclabs-site's sissy-fall minigame has
 * shipped this shape for a while (js/sissy-fall/media.js -> addZip): walk the
 * central directory with a filter that inflates NOTHING, decide from the entry
 * names and their declared sizes what is worth having, and only then inflate
 * the survivors one at a time. Same vendored library (vendor/fflate, MIT),
 * same two-pass walk, same reason: a 2 GB archive must never be inflated whole
 * into a tab's heap to find out it held one gif.
 *
 * THINGS THAT ARE NOT DECORATION
 *   1. NOTHING HERE THROWS AT IMPORT, and fflate is loaded LAZILY, by dynamic
 *      import, the first time a player actually picks a zip. A static import
 *      would put 90 KB in front of every page load for a feature most sessions
 *      never touch — and an import that throws in WebView2 is a silent infinite
 *      loader, which is this project's worst failure mode.
 *   2. THE CEILINGS ARE THE FEATURE. A zip is attacker-shaped input even when
 *      the attacker is just a curious player: MAX_ENTRIES caps the count,
 *      MAX_TOTAL_BYTES caps the inflated total (the zip-bomb guard, checked
 *      against the DECLARED size before a single byte is inflated), and the
 *      per-entry cap is the caller's exempt ceiling.
 *   3. NO RECURSION. A zip inside the zip is skipped, not opened. There is no
 *      depth counter to get wrong because there is no depth.
 *   4. A corrupt archive is an ANSWER (`ok:false`), never an exception: the
 *      caller counts it as one failed pick and the player keeps their screen.
 * ==========================================================================*/

/** At most this many entries are taken from one archive. */
export const ZIP_MAX_ENTRIES = 500;
/** Inflated bytes per archive, total. Past this the rest is dropped as failed. */
export const ZIP_MAX_TOTAL_BYTES = 512 * 1024 * 1024;
/** Yield to the event loop every this many inflated entries, so the UI paints. */
const YIELD_EVERY = 8;

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
 * ------------------------------------------------------------------------ */

let flatePromise = null;

async function loadFlate() {
  if (!flatePromise) {
    flatePromise = import('../vendor/fflate/fflate.module.js')
      .then((m) => (m && typeof m.unzipSync === 'function' ? m : null))
      .catch(() => null);
  }
  return flatePromise;
}

/** Test seam: forget the cached module (and any cached failure). */
export function _resetZipLib() { flatePromise = null; }

/**
 * Pull the eligible media out of one zip.
 *
 * The caller owns what "eligible" means — assetsStore passes its own mime
 * table, so the picker, the wire's allowlist and this filter can never drift
 * apart into "the zip adopted a file the offer gate then refused".
 *
 * @param {ArrayBuffer|Uint8Array} buf   the whole archive
 * @param {object} [o]
 * @param {(name:string)=>boolean} [o.isEligible]  name -> keep it? (default: keep all non-junk)
 * @param {number} [o.maxEntryBytes]     per-entry ceiling; bigger entries count as tooBig
 * @param {number} [o.maxEntries]        default ZIP_MAX_ENTRIES
 * @param {number} [o.maxTotalBytes]     default ZIP_MAX_TOTAL_BYTES
 * @returns {Promise<{ok:boolean, reason:string, entries:Array<{name:string, bytes:Uint8Array}>,
 *   tooBig:number, failed:number, skipped:number, truncated:boolean}>}
 */
export async function readZipMedia(buf, o = {}) {
  const out = { ok: false, reason: '', entries: [], tooBig: 0, failed: 0, skipped: 0, truncated: false };
  const eligible = typeof o.isEligible === 'function' ? o.isEligible : () => true;
  const maxEntry = Math.max(1, Number(o.maxEntryBytes) || Infinity);
  const maxEntries = Math.max(1, Math.floor(Number(o.maxEntries) || ZIP_MAX_ENTRIES));
  const maxTotal = Math.max(1, Number(o.maxTotalBytes) || ZIP_MAX_TOTAL_BYTES);

  let data = null;
  try {
    data = buf instanceof Uint8Array ? buf : new Uint8Array(buf || 0);
  } catch (_e) { data = null; }
  if (!data || data.length < 22) { out.reason = 'not-a-zip'; return out; }   // shorter than an EOCD

  const flate = await loadFlate();
  if (!flate) { out.reason = 'no-zip-lib'; return out; }

  /* Pass 1 — walk the central directory and DECOMPRESS NOTHING. The filter
   * returning false for every record is what makes this a cheap read: fflate
   * hands us each header, we answer "no", and the archive is never inflated. */
  const wanted = [];
  let planned = 0;
  try {
    flate.unzipSync(data, {
      filter: (f) => {
        const name = String((f && f.name) || '');
        if (isJunkEntry(name)) { out.skipped++; return false; }
        if (isZipName(name)) { out.skipped++; return false; }        // no recursion, ever
        if (!eligible(baseName(name))) { out.skipped++; return false; }
        const size = Math.max(0, Number(f && f.originalSize) || 0);
        if (!size || size > maxEntry) { out.tooBig++; return false; }
        if (wanted.length >= maxEntries) { out.truncated = true; out.failed++; return false; }
        if (planned + size > maxTotal) { out.truncated = true; out.failed++; return false; }
        planned += size;
        wanted.push({ name, base: baseName(name) });
        return false;                                                 // never inflate during the walk
      },
    });
  } catch (_e) {
    // A header we cannot read at all. Whatever was collected before the throw
    // is still honest, so keep going only if there IS something.
    if (!wanted.length) { out.reason = 'unreadable'; return out; }
  }

  /* Pass 2 — inflate the survivors one at a time, yielding periodically so the
   * standalone card can repaint between a hundred photos. One entry that will
   * not inflate is one failed count, not a dead archive. */
  for (let i = 0; i < wanted.length; i++) {
    const w = wanted[i];
    try {
      const bag = flate.unzipSync(data, { filter: (f) => f && f.name === w.name });
      const bytes = bag ? bag[w.name] : null;
      if (bytes && bytes.length) out.entries.push({ name: w.base, bytes });
      else out.failed++;
    } catch (_e) { out.failed++; }
    if ((i % YIELD_EVERY) === YIELD_EVERY - 1) {
      await new Promise((res) => { try { setTimeout(res, 0); } catch (_e) { res(); } });
    }
  }

  out.ok = true;
  return out;
}

export default readZipMedia;
