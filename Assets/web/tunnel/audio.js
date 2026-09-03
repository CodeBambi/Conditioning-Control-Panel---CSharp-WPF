/* ============================================================================
 * audio.js — SFX bridge.
 *
 * Per the app's audio design, one-shot SFX play through the C# host (ChaosSfx)
 * so they honour the master volume and the user's chosen output device. This
 * page never touches WebAudio; it just POSTs {type:'sfx', name, scale} to the
 * host, which resolves Resources/sounds/chaos/{name}.mp3. If there is no host
 * (opened in a plain browser for dev), it silently no-ops.
 * ==========================================================================*/
export class Sfx {
  constructor(post) { this._post = post; }
  play(name, scale = 0.6) {
    try { this._post({ type: 'sfx', name, scale }); } catch (_) {}
  }
}
