/* ============================================================================
 * shared/hypno/voice.js - who actually SAYS the subliminal word (CONTRACT 10.21).
 *
 * The page used to speak it with the browser's own speechSynthesis, which is whatever placeholder
 * voice the WebView happened to have. Hosted, the word goes to the app instead, which owns a real
 * chain: the player's own clip for that phrase, else a bundled Back Room clip, else Windows speech,
 * all through the app's chosen audio output device. The page only speaks for itself when the host
 * says `none`, or when there is no host at all (the Vercel phone playtest).
 *
 * CONTRACT (stable):
 *   createVoice({ bridge, hosted })   -> null with no host; else { available, speak, stop }
 *   .available === true               the host is there; the caller must NOT also use speechSynthesis
 *                                     until the ack comes back saying `none`
 *   .speak({ text, reversed, seed })  -> Promise<{ source, durationMs }>, NEVER rejects.
 *                                     source: 'clip' | 'preset' | 'tts' | 'none'
 *                                     durationMs: how long the host's audio runs, 0 when silent.
 *                                     `reversed` is the easter egg: the host plays the samples
 *                                     BACKWARDS, so the page must not also spell it backwards out
 *                                     loud (it still mirrors and reverses the TEXT, which is the
 *                                     page's half of the gag).
 *   .stop()                           Law VI: cancel, suspend and leave drop the line at once.
 * ==========================================================================*/

import { bridge as defaultBridge, isHosted as defaultHosted, mintId } from '../../bridge.js';

/** The four answers the host may give. Anything else reads as 'none'. */
export const SOURCES = Object.freeze(['clip', 'preset', 'tts', 'none']);
/** A host that has not answered by now is treated as silent, so a word is never mute. */
export const ACK_MS = 1200;
/** Nothing the host reports can hold a chain longer than this (callout.js caps the wait too). */
export const MAX_DURATION_MS = 8000;

/** One ack, cleaned: an unknown source, a NaN duration or a missing reply all read as silence. */
export function readAck(m) {
  const source = m && typeof m.source === 'string' && SOURCES.includes(m.source) ? m.source : 'none';
  const raw = m ? Number(m.durationMs) : 0;
  const durationMs = Number.isFinite(raw) ? Math.max(0, Math.min(MAX_DURATION_MS, Math.round(raw))) : 0;
  return { source, durationMs: source === 'none' ? 0 : durationMs };
}

/**
 * The host voice, or null when this page is not hosted (which leaves the caller on speechSynthesis).
 * @param {Object} [o]
 * @param {Object} [o.bridge]  the bridge module (tests pass a fake)
 * @param {boolean} [o.hosted] override the bridge's own host detection (tests)
 */
export function createVoice({ bridge = defaultBridge, hosted = defaultHosted } = {}) {
  if (!hosted || !bridge || typeof bridge.request !== 'function') return null;
  return {
    available: true,
    speak({ text, reversed = false, seed = 0 } = {}) {
      const token = typeof bridge.mintId === 'function' ? bridge.mintId() : mintId();
      const msg = {
        type: 'word.speak', token, text: String(text == null ? '' : text),
        reversed: reversed === true, seed: (Number(seed) || 0) >>> 0,
      };
      return bridge
        .request(msg, 'word-ack', (m) => m && m.token === token, ACK_MS, { source: 'none', durationMs: 0 })
        .then(readAck)
        .catch(() => ({ source: 'none', durationMs: 0 }));
    },
    stop() {
      try { bridge.send({ type: 'word.stop' }); } catch (e) { /* host gone */ }
    },
  };
}

export default createVoice;
