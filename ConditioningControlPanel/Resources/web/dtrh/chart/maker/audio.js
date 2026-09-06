/* ============================================================================
 * chart/maker/audio.js - the file, the peaks, the clock (chart/MAKER.md, M1).
 *
 * The audio never leaves the machine. It is decoded once in a throwaway 16 kHz
 * context, turned into the min/max peaks the waveform draws from, and then the
 * AudioBuffer is dropped on the floor: a forty minute file is 46 MB decoded and
 * 290 KB as peaks, and holding the buffer is what makes a page like this crawl.
 * Playback is a plain <audio> on an object URL, so the browser owns the clock.
 * ==========================================================================*/

import { peaksFromChannels, hashFile } from '../editor/audio.js';

const PEAKS_PER_SEC = 50;
const DECODE_RATE = 16000;
export const AUDIO_EXT = /\.(mp3|wav|ogg|oga|m4a|aac|flac|webm|opus)$/i;
export const isAudioFile = (f) => !!f && ((f.type || '').startsWith('audio/') || AUDIO_EXT.test(f.name || ''));
export const stemOf = (name) => String(name || '').replace(/\.[^.]+$/, '');

/**
 * Decode, measure, hash, and hand back everything the page needs but the buffer.
 * `onStatus` gets a line per step, because a long file takes a few seconds.
 */
export async function loadAudio(file, onStatus = () => {}) {
  const name = file.name || 'track';
  onStatus('reading ' + name);
  const [hash, bytes] = await Promise.all([hashFile(file), file.arrayBuffer()]);
  onStatus('listening to it once, for the waveform');
  const Ctx = window.OfflineAudioContext || window.webkitOfflineAudioContext;
  let peaks = null, durationSec = 0;
  try {
    const ctx = new Ctx(1, 1, DECODE_RATE);
    const buf = await ctx.decodeAudioData(bytes);
    const chans = [];
    for (let c = 0; c < buf.numberOfChannels; c++) chans.push(buf.getChannelData(c));
    peaks = peaksFromChannels(chans, buf.length, buf.sampleRate, PEAKS_PER_SEC);
    durationSec = buf.duration;
    chans.length = 0;                       // and the buffer goes with the scope
  } catch (e) {
    onStatus('that file will not decode here: ' + (e && e.message ? e.message : 'unknown'));
  }
  return { file, name, stem: stemOf(name), hash, durationSec, peaks, perSec: PEAKS_PER_SEC, url: URL.createObjectURL(file) };
}

/** The <audio> element and the four things the page asks of it. */
export function createPlayer() {
  const el = new Audio();
  el.preload = 'auto';
  let url = null;
  return {
    el,
    open(next) {
      if (url) URL.revokeObjectURL(url);
      url = next;
      el.src = url;
      el.currentTime = 0;
    },
    get time() { return el.currentTime || 0; },
    get playing() { return !el.paused && !el.ended; },
    seek(t) { try { el.currentTime = Math.max(0, t); } catch (e) { /* not seekable yet */ } },
    toggle() { if (el.paused) el.play().catch(() => {}); else el.pause(); },
    stop() { el.pause(); },
  };
}
