/* ============================================================================
 * ui/audio.js — the sound bus.  *** STUB — NO ASSETS WIRED YET ***
 *
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │ TODO(audio): every call below is a NO-OP that only bookkeeps and      │
 *   │ (optionally) logs. The real implementation lands with the sfx pack:   │
 *   │   * one WebAudio graph, three gain nodes (master -> music | sfx),     │
 *   │   * `sfx(id)` decodes from an assets/sfx manifest — an UNKNOWN id must │
 *   │     be a loud dev-time warning, never a silent fallback tone,         │
 *   │   * `duck(true)` rides the music bus down ~12 dB over 120 ms for      │
 *   │     lock cards / mandatory video and back up over 400 ms.             │
 *   │ Until then callers may call any of this freely: the API is FINAL and  │
 *   │ nothing here can throw, so no screen has to branch on "is audio real". │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * Volume comes from ui/prefs.js and is tracked live, so when the real graph
 * lands it inherits the player's existing settings with no migration.
 *
 * Import-safe under node: no AudioContext is constructed anywhere in this file.
 * ==========================================================================*/

/** Every sfx id the screens currently ask for. The real pack must cover these. */
export const SFX_IDS = Object.freeze([
  'ui-move', 'ui-select', 'ui-back', 'ui-error',
  'code-cell', 'code-copy',
  'lamp-confirm', 'lamp-clear',
  'draft-pick', 'draft-drop', 'draft-lock',
  'countdown-tick', 'countdown-go',
  'payload-in', 'payload-out', 'charge-earned',
  'recap-reveal', 'title-unlock',
]);

const LOG_BUDGET = 40;   // enough to see the shape of a session, not enough to spam

/**
 * @param {object} [o]
 * @param {object} [o.prefs] ui/prefs.js handle — read for volumes, subscribed for changes
 * @param {object} [o.logger] console-shaped; omit for total silence
 * @param {boolean} [o.trace] log every call (default: only the first of each id)
 */
export function createAudio({ prefs = null, logger = null, trace = false } = {}) {
  const log = logger;
  const seen = new Set();
  let logged = 0;
  let ducked = false;
  let currentMusic = null;
  let disposed = false;

  const vol = {
    master: prefs ? prefs.get('masterVolume') : 0.8,
    music: prefs ? prefs.get('musicVolume') : 0.55,
    sfx: prefs ? prefs.get('sfxVolume') : 0.85,
  };

  const unsubPrefs = prefs ? prefs.subscribe((key, value) => {
    if (key === 'masterVolume') vol.master = value;
    else if (key === 'musicVolume') vol.music = value;
    else if (key === 'sfxVolume') vol.sfx = value;
  }) : () => {};

  function note(what) {
    if (!log || !log.debug) return;
    if (!trace) {
      if (seen.has(what)) return;
      seen.add(what);
      if (++logged > LOG_BUDGET) return;
    }
    log.debug('[GG audio] ' + what + ' (stub)');
  }

  const api = {
    /** True once the real graph exists. Screens may use it to hide a mute button. */
    get isReal() { return false; },
    get volumes() { return Object.assign({}, vol); },
    get isDucked() { return ducked; },
    /** Name of the bed that would be playing. NOT `music` — that is the verb. */
    get currentMusic() { return currentMusic; },

    /** One-shot cue. Unknown ids are tolerated here; the real bus will complain. */
    sfx(id) {
      if (disposed || !id) return;
      note('sfx:' + id);
    },

    /** Start (or cross-fade to) a music bed. `null` is the same as stopMusic(). */
    music(name) {
      if (disposed) return;
      if (!name) { api.stopMusic(); return; }
      if (currentMusic === name) return;
      currentMusic = String(name);
      note('music:' + currentMusic);
    },

    stopMusic() {
      if (disposed || currentMusic === null) return;
      note('music:stop');
      currentMusic = null;
    },

    /** Ride the music bus down under speech/lock cards. Idempotent. */
    duck(on) {
      if (disposed) return;
      const next = !!on;
      if (next === ducked) return;
      ducked = next;
      note('duck:' + (ducked ? 'on' : 'off'));
    },

    /** Direct volume write (options drawer drives prefs, which drives us). */
    setVolume(bus, value) {
      const v = Math.max(0, Math.min(1, Number(value) || 0));
      if (bus === 'master' || bus === 'music' || bus === 'sfx') vol[bus] = v;
      if (prefs) prefs.set(bus + 'Volume', v);
    },

    dispose() {
      if (disposed) return;
      disposed = true;
      api.stopMusic();
      try { unsubPrefs(); } catch (_e) { /* ignore */ }
    },
  };

  return api;
}

export default createAudio;
