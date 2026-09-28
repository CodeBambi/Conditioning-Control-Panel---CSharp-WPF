/* ============================================================================
 * core/songMeta.js - Game Night: a BambiCloud track's real name.
 *
 * A picked link carries no name (a page link is a uuid, a CDN link is
 * `<uuid>.mp3`), so the row used to read "bambicloud track". BambiCloud's own
 * API answers a file's metadata with no account:
 *
 *   GET https://api.bambicloud.com/files?uuid=<uuid>
 *   -> {"files":[{"name":"Rapid Induction","uuid":..., "duration":162000 (ms),
 *                 "audioURL":"https://cdn.bambicloud.com/<uuid>.mp3", ...}]}
 *
 * CORS reflects the page origin, so the page asks it directly. No cookie is ever
 * sent (credentials: 'omit'). The fetch is INJECTED so the selftest runs it with a
 * fake; nothing here touches the network on import.
 *
 * NOTHING HERE CAN FAIL A PICK. Timeout, refusal, bad JSON, an empty list, a url
 * off the CDN: the answer is null and the row keeps "bambicloud track". The audio
 * element's own length stays the match length; `durSec` here is a hint only.
 * ==========================================================================*/

import { SONG_HOST, SONG_TITLE_MAX, wireSongUrl } from './song.js';
import { sanitizeText } from '../exec/sanitize.js';

export const SONG_META_API = 'https://api.bambicloud.com/files?uuid=';
export const SONG_META_TIMEOUT_MS = 4000;
const FILE_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** `https://cdn.bambicloud.com/<uuid>.mp3` -> the uuid, or ''. Pure. */
export function bambicloudFileId(url) {
  const href = wireSongUrl(url);
  if (!href) return '';
  const u = new URL(href);
  if (u.host !== SONG_HOST || u.search || u.hash) return '';
  const m = /^\/([^/]+)\.mp3$/i.exec(u.pathname);
  return m && FILE_ID.test(m[1]) ? m[1].toLowerCase() : '';
}

/**
 * The API's answer -> { title, url, durSec } or null. Pure.
 * `url` is the file's audioURL only when it is a legal CDN url, else ''.
 */
export function readSongMeta(body) {
  const f = body && Array.isArray(body.files) ? body.files[0] : null;
  if (!f || typeof f !== 'object') return null;
  const title = sanitizeText(typeof f.name === 'string' ? f.name.replace(/\s+/g, ' ') : '', SONG_TITLE_MAX);
  if (!title) return null;
  const url = typeof f.audioURL === 'string' ? wireSongUrl(f.audioURL) : '';
  const ms = Number(f.duration);
  const durSec = Number.isFinite(ms) && ms > 0 ? Math.round(ms / 1000) : 0;
  return { title, url, durSec };
}

/**
 * Look a picked CDN url up. Resolves { title, url, durSec } or null, never rejects.
 * @param {string} url  a url parseSongLink handed back
 * @param {{fetchFn?: Function, timeoutMs?: number}} opts
 */
export async function lookupSongMeta(url, { fetchFn = globalThis.fetch, timeoutMs = SONG_META_TIMEOUT_MS } = {}) {
  const id = bambicloudFileId(url);
  if (!id || typeof fetchFn !== 'function') return null;
  const ctl = typeof AbortController === 'function' ? new AbortController() : null;
  let timer = null;
  const timeout = new Promise((resolve) => {
    timer = setTimeout(() => { try { ctl?.abort(); } catch (_e) { /* gone */ } resolve(null); }, timeoutMs);
  });
  const ask = (async () => {
    try {
      const res = await fetchFn(SONG_META_API + id, {
        credentials: 'omit',
        headers: { Accept: 'application/json' },
        signal: ctl ? ctl.signal : undefined,
      });
      if (!res || !res.ok) return null;
      return readSongMeta(await res.json());
    } catch (_e) {
      return null;
    }
  })();
  try {
    return await Promise.race([ask, timeout]);
  } finally {
    clearTimeout(timer);
  }
}
