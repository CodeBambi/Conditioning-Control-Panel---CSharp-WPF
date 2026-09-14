/* ============================================================================
 * zip.js - a dropped archive, opened where it lands.
 *
 * People drop folders as zips, so the room reads one itself rather than asking
 * for the gifs again. No library: a zip's directory is at the end of the file,
 * so the whole job is three reads.
 *
 *   1. Find the End Of Central Directory record by scanning backwards. It can
 *      sit up to 64 KB from the end because a zip may carry a comment.
 *   2. Walk the central directory. That is where the truth lives: names,
 *      sizes, method, and where each entry's local header starts. Central
 *      order is the order the room takes them in, so a drop is stable.
 *   3. For each entry we want, read its LOCAL header to find the byte the data
 *      actually starts on. Only the local header knows its own name and extra
 *      field lengths, and they often differ from the central ones. Sizes and
 *      method still come from the central record: macOS Archive Utility writes
 *      data-descriptor entries (general purpose bit 3) whose local header
 *      carries zeroes for all three.
 *
 * Stored entries are a slice. Deflated ones go through DecompressionStream,
 * which every browser we ship to has. One bad entry never takes the archive
 * down with it: it is skipped and its siblings still land.
 *
 * ZIP64 is refused rather than half read. An archive that big is not a drop,
 * it is a backup.
 * ==========================================================================*/

/** Most files the room takes out of one archive. */
export const ZIP_CAP = 100;

/** Extension to mime, and the default idea of what is worth taking. */
export const MEDIA_TYPES = {
  gif: 'image/gif',
  png: 'image/png',
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  webp: 'image/webp',
  avif: 'image/avif',
  bmp: 'image/bmp',
  mp4: 'video/mp4',
  webm: 'video/webm',
  mov: 'video/quicktime',
  m4v: 'video/x-m4v',
};

const SIG_EOCD = 0x06054b50;
const SIG_CENTRAL = 0x02014b50;
const SIG_LOCAL = 0x04034b50;
const EOCD_MIN = 22;
const MAX_COMMENT = 0xffff;
const FLAG_UTF8 = 0x800;
const DOS_DIRECTORY = 0x10;
const STORED = 0;
const DEFLATED = 8;
const U16_MAX = 0xffff;
const U32_MAX = 0xffffffff;

const NOT_A_ZIP = 'That is not a zip file';
const NOTHING_USABLE = 'That zip has nothing the room can use';
const TOO_BIG = 'That zip is too big for the room';
const NO_INFLATE = 'This browser cannot open zips. Drop the gifs themselves.';

const asUtf8 = new TextDecoder('utf-8');
// Names without the utf8 flag are DOS-era bytes. 'latin1' is the one label
// Node and every browser both take for them.
const asLatin1 = new TextDecoder('latin1');

/** Does this look like an archive rather than something we can decode? */
export function isZipFile(file) {
  const type = String((file && file.type) || '').toLowerCase();
  const name = String((file && file.name) || '').toLowerCase();
  if (type === 'application/zip' || type === 'application/x-zip-compressed') return true;
  return name.endsWith('.zip');
}

const extOf = name => {
  const m = /\.([a-z0-9]+)$/i.exec(String(name || ''));
  return m ? m[1].toLowerCase() : '';
};

const baseOf = name => String(name || '').split('/').pop();

/** The default `keep`: media the decoder has a route for. */
export function keepsMedia(name) {
  return Object.prototype.hasOwnProperty.call(MEDIA_TYPES, extOf(name));
}

/** Folder rows, Apple's resource fork mirror, and every dotfile under it. */
function isJunk(name, attrs) {
  if (!name || name.endsWith('/')) return true;
  if ((attrs & DOS_DIRECTORY) !== 0) return true;
  for (const part of name.split('/')) {
    if (!part || part === '__MACOSX' || part.startsWith('.')) return true;
  }
  return false;
}

/** The last EOCD in the file, or -1. It can be buried under a comment. */
function findEocd(dv, len) {
  const floor = Math.max(0, len - (MAX_COMMENT + EOCD_MIN));
  for (let at = len - EOCD_MIN; at >= floor; at--) {
    if (dv.getUint32(at, true) === SIG_EOCD) return at;
  }
  return -1;
}

/**
 * The bytes of one entry, or null when the local header does not line up.
 * `size` and `method` are the central directory's, never the local one's.
 */
async function readEntry(dv, buf, localAt, method, size) {
  const len = buf.byteLength;
  if (localAt + 30 > len || dv.getUint32(localAt, true) !== SIG_LOCAL) return null;
  const nameLen = dv.getUint16(localAt + 26, true);
  const extraLen = dv.getUint16(localAt + 28, true);
  const from = localAt + 30 + nameLen + extraLen;
  if (from + size > len) return null;
  const packed = new Uint8Array(buf, from, size);
  if (method === STORED) return packed;
  const stream = new Blob([packed]).stream().pipeThrough(new DecompressionStream('deflate-raw'));
  return new Uint8Array(await new Response(stream).arrayBuffer());
}

/**
 * Open a dropped zip and hand back the media inside it, in central directory
 * order.
 *
 * opts: { cap = ZIP_CAP, keep = keepsMedia }, where `keep` is called with the
 * entry's full path inside the archive.
 *
 * Throws when the file is not a zip, when it is ZIP64, when nothing inside it
 * is usable, or when a deflated entry needs an inflater this browser lacks.
 */
export async function unzip(file, opts = {}) {
  const cap = Number.isFinite(opts.cap) ? Math.max(0, Math.floor(opts.cap)) : ZIP_CAP;
  const keep = typeof opts.keep === 'function' ? opts.keep : keepsMedia;

  const buf = await file.arrayBuffer();
  const len = buf.byteLength;
  const dv = new DataView(buf);

  const eocd = findEocd(dv, len);
  if (eocd < 0) throw new Error(NOT_A_ZIP);

  const count = dv.getUint16(eocd + 10, true);
  const cdSize = dv.getUint32(eocd + 12, true);
  const cdAt = dv.getUint32(eocd + 16, true);
  if (count === U16_MAX || cdSize === U32_MAX || cdAt === U32_MAX) throw new Error(TOO_BIG);

  const out = [];
  let at = cdAt;
  for (let i = 0; i < count && out.length < cap; i++) {
    if (at + 46 > len || dv.getUint32(at, true) !== SIG_CENTRAL) break;
    const flags = dv.getUint16(at + 8, true);
    const method = dv.getUint16(at + 10, true);
    // Sizes from the central record only: a data descriptor entry (general
    // purpose bit 3) carries zeroes for these in its local header.
    const size = dv.getUint32(at + 20, true);
    const plain = dv.getUint32(at + 24, true);
    const nameLen = dv.getUint16(at + 28, true);
    const extraLen = dv.getUint16(at + 30, true);
    const commentLen = dv.getUint16(at + 32, true);
    const attrs = dv.getUint32(at + 38, true);
    const localAt = dv.getUint32(at + 42, true);
    if (at + 46 + nameLen > len) break;
    const decoder = (flags & FLAG_UTF8) !== 0 ? asUtf8 : asLatin1;
    const name = decoder.decode(new Uint8Array(buf, at + 46, nameLen));
    at += 46 + nameLen + extraLen + commentLen;

    if (size === U32_MAX || plain === U32_MAX || localAt === U32_MAX) throw new Error(TOO_BIG);
    const path = name.replace(/\\/g, '/');
    if (isJunk(path, attrs)) continue;
    if (!keep(path)) continue;
    if (method !== STORED && method !== DEFLATED) continue;
    if (method === DEFLATED && typeof DecompressionStream === 'undefined') throw new Error(NO_INFLATE);

    let bytes = null;
    try {
      bytes = await readEntry(dv, buf, localAt, method, size);
    } catch {
      // A truncated or scrambled entry loses itself, not the archive.
      continue;
    }
    if (!bytes) continue;
    out.push(new File([bytes], baseOf(path), { type: MEDIA_TYPES[extOf(path)] || '' }));
  }

  if (out.length === 0) throw new Error(NOTHING_USABLE);
  return out;
}
