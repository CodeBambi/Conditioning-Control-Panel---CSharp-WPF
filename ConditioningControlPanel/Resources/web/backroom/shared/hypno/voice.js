/* ============================================================================
 * shared/hypno/voice.js - who actually SAYS the subliminal word (CONTRACT 10.21).
 *
 * Only RECORDED audio ever says it (owner, 2026-09-25, no synthetic speech: no browser voice and no
 * Windows speech). Hosted, the word goes to the app: the player's own clip for that phrase, else a
 * bundled Back Room recording, through the app's chosen audio output device. When the host says
 * `none`, or there is no host at all (the Vercel phone playtest), the word is silent.
 *
 * CONTRACT (stable):
 *   createVoice({ bridge, hosted })   -> null with no host; else { available, speak, stop }
 *   .available === true               the host is there and owns the line
 *   .speak({ text, reversed, seed })  -> Promise<{ source, durationMs }>, NEVER rejects.
 *                                     source: 'clip' | 'preset' | 'none' ('tts' is a retired
 *                                     value still read for wire compatibility; no host sends it)
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
 * The host voice, or null when this page is not hosted (every word is then silent).
 * @param {Object} [o]
 * @param {Object} [o.bridge]  the bridge module (tests pass a fake)
 * @param {boolean} [o.hosted] override the bridge's own host detection (tests)
 */
export function createVoice({ bridge = defaultBridge, hosted = defaultHosted } = {}) {
  if (!hosted || !bridge || typeof bridge.request !== 'function') return null;
  return {
    available: true,
    speak({ text, reversed = false, seed = 0, volume } = {}) {
      const token = typeof bridge.mintId === 'function' ? bridge.mintId() : mintId();
      const msg = {
        type: 'word.speak', token, text: String(text == null ? '' : text),
        reversed: reversed === true, seed: (Number(seed) || 0) >>> 0,
      };
      // Optional level 0..1 (Breakout speaks at half); a host that does not know it plays at its own level.
      if (Number.isFinite(volume)) msg.volume = Math.max(0, Math.min(1, volume));
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
