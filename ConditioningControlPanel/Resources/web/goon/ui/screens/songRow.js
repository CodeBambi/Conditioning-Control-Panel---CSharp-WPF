/* ============================================================================
 * ui/screens/songRow.js - Game Night: the lobby's "Pick a song" row.
 *
 *   buildSongRow({ ledger, match, audio, origin, probe }) -> { node, paint }
 *
 * The ONE new thing a first match asks. The host pastes a BambiCloud track link,
 * the row reads its length off the file's metadata and hands it to the engine
 * (match.setSong), which proposes it as the match length. Skip, or never touch
 * it, and the match runs today's default length. The guest sees what the host
 * picked and nothing to press.
 *
 * It sits OUTSIDE the consent box on purpose: that box is inert until the
 * opponent arrives, and picking a song is something a host does while waiting.
 *
 * NOTHING HERE BLOCKS THE MATCH. A link that is refused, a file that will not
 * answer, a metadata read that times out: one line on the row, and the match
 * keeps its current length. Links follow core/song.js (the race/cloud.js rules):
 * cdn.bambicloud.com or this page's own origin, no proxy, no credentials.
 * ==========================================================================*/

import { el, button } from '../router.js';
import { S } from '../strings.js';
import { GoonMatchPhase } from '../../core/contracts.js';
import { SONG_LOAD_TIMEOUT_MS, clampSongSec, parseSongLink, songClock } from '../../core/song.js';
import { lookupSongMeta } from '../../core/songMeta.js';

/**
 * The file's length in seconds, off its metadata only. `crossOrigin = anonymous`
 * so no cookie is ever sent; `preload = metadata` so nothing more than the header
 * is asked for. Rejects on error or timeout.
 */
export function probeSongDuration(url, { timeoutMs = SONG_LOAD_TIMEOUT_MS } = {}) {
  return new Promise((resolve, reject) => {
    if (typeof Audio !== 'function') { reject(new Error('no audio')); return; }
    const a = new Audio();
    a.crossOrigin = 'anonymous';
    a.preload = 'metadata';
    let done = false;
    const end = (fn, v) => {
      if (done) return;
      done = true;
      clearTimeout(t);
      try { a.removeAttribute('src'); a.load(); } catch (_e) { /* gone */ }
      fn(v);
    };
    const t = setTimeout(() => end(reject, new Error('timed out')), timeoutMs);
    a.addEventListener('loadedmetadata', () => {
      const d = Number(a.duration);
      if (d > 0 && Number.isFinite(d)) end(resolve, d); else end(reject, new Error('no duration'));
    }, { once: true });
    a.addEventListener('error', () => end(reject, new Error('would not load')), { once: true });
    a.src = url;
  });
}

export function buildSongRow({ ledger, match, audio = null, origin = null, probe = probeSongDuration, lookup = lookupSongMeta, prefs = null }) {
  const input = el('input', {
    type: 'url',
    class: 'gg-song-input',
    placeholder: S.song.placeholder,
    'aria-label': S.song.label,
    autocomplete: 'off',
    spellcheck: 'false',
  });
  const pickBtn = button(ledger, S.song.pick, () => { void pick(); }, { variant: 'primary', audio, sfx: 'ui-select' });
  const skipBtn = button(ledger, S.song.skip, () => clear(), { variant: 'ghost', audio, sfx: 'ui-back' });
  const picked = el('p', { class: 'gg-song-picked', text: '' });
  const sub = el('p', { class: 'gg-row-sub gg-song-sub', text: S.song.sub, role: 'status' });
  const form = el('div', { class: 'gg-song-form' }, [input, pickBtn]);
  const node = el('div', { class: 'gg-song' }, [
    el('div', { class: 'gg-row gg-song-head' }, [
      el('span', { class: 'gg-row-label', text: S.song.label }),
      skipBtn,
    ]),
    form, picked, sub,
  ]);

  let busy = false;
  let note = '';

  function editable() {
    return match.isHost && (match.phase === GoonMatchPhase.Lobby || match.phase === GoonMatchPhase.Consent);
  }

  async function pick() {
    if (busy || !editable()) return;
    const verdict = parseSongLink(input.value, origin);
    if (verdict.refused) {
      note = verdict.refused === 'page' ? S.song.refusedPage
        : (verdict.refused === 'empty' ? S.song.empty : S.song.refusedHost);
      paint();
      return;
    }
    busy = true;
    note = S.song.loading;
    paint();
    // The track's real name off BambiCloud's API (4 s cap). A miss keeps the parsed title.
    let meta = null;
    try { meta = lookup ? await lookup(verdict.url) : null; } catch (_e) { meta = null; }
    if (ledger.isDisposed) { busy = false; return; }
    const url = (meta && meta.url) || verdict.url;
    const title = (meta && meta.title) || verdict.title;
    let sec = 0;
    try { sec = await probe(url); } catch (_e) { sec = 0; }
    busy = false;
    if (ledger.isDisposed) return;
    const durSec = clampSongSec(sec);
    if (!durSec) { note = S.song.failed; paint(); return; }
    note = '';
    if (match.setSong({ url, title, durSec })) {
      input.value = '';
      stamp();
    }
    paint();
  }

  function clear() {
    if (!editable()) return;
    note = '';
    input.value = '';
    // Skip puts the usual length back (the remembered one, or the default).
    let fallbackSec = null;
    try { fallbackSec = prefs ? prefs.get('matchLengthSec') : null; } catch (_e) { fallbackSec = null; }
    match.setSong(null, { fallbackSec });
    paint();
  }

  /** The little rubber-stamp pop when a song lands. CSS owns the motion (and its off switch). */
  function stamp() {
    picked.classList.remove('is-stamped');
    void picked.offsetWidth;
    picked.classList.add('is-stamped');
    try { audio?.sfx?.('lamp-confirm'); } catch (_e) { /* stub bus */ }
  }

  function paint() {
    const song = match.song;
    const host = match.isHost;
    const edit = editable();
    form.hidden = !host || !!song;
    skipBtn.hidden = !host || !song;
    input.disabled = !edit || busy;
    pickBtn.disabled = !edit || busy;
    skipBtn.disabled = !edit;
    picked.hidden = !song;
    picked.textContent = song ? S.song.picked(song.title, songClock(song.durSec)) : '';
    if (note) sub.textContent = note;
    else if (song) sub.textContent = host ? S.song.lengthHost : S.song.lengthGuest;
    else sub.textContent = host ? S.song.sub : S.song.none;
    node.classList.toggle('has-song', !!song);
  }

  ledger.listen(input, 'keydown', (e) => { if (e && e.key === 'Enter') { e.preventDefault(); void pick(); } });
  if (typeof match.onSongChanged === 'function') {
    ledger.sub(match.onSongChanged(() => { if (!match.isHost && match.song) stamp(); paint(); }));
  }
  ledger.sub(match.onPhaseChanged(() => paint()));
  paint();
  return { node, paint };
}

export default buildSongRow;
