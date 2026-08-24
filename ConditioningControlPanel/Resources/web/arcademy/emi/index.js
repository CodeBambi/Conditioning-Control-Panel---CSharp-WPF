/* ============================================================================
 * emi/index.js - mountEmi() and the ONE controller the shell holds (agent B).
 *
 *   import { mountEmi } from '../emi/index.js';
 *   const emi = mountEmi({ layer: document.getElementById('arc-emi'), store });
 *   emi.emote('wink');            // a chain id from CHAINS
 *   emi.emote('(≧◡≦)');           // ...or a raw face string, held then idle
 *   emi.say('Top of the class!'); // the bubble types . .. ... then the line
 *
 * THE RENDERER IS LOADED OPTIONALLY. `face.js` / `chains.js` / `fx.js` are agent
 * A's and are pulled in with a dynamic import inside a try/catch - the same
 * `loadOptional` discipline `shell/shell.js` uses for `engine/` and `provider/`.
 * A broken or absent renderer therefore costs EMI's face, never the shell's
 * boot, and the node DOM double never evaluates a canvas module at all.
 *
 * The controller is returned SYNCHRONOUSLY (the widget's DOM is up on the same
 * frame); the face attaches a tick later. Exactly ONE call made before then is
 * remembered and replayed when it lands, and only inside a short grace window -
 * a mascot that replays a stale reaction three seconds after the moment is
 * worse than one that missed it.
 * ==========================================================================*/

import { createWidget, DIALS, HINT_LINE, STORE_KEY, sayHoldMs, SAY_LEAD_MS } from './widget.js';

export { DIALS, HINT_LINE, STORE_KEY, sayHoldMs, SAY_LEAD_MS };

let singleton = null;
let voice = null;
let voicePending = null;
/** () => bool: can EMI actually PERFORM right now (is her face attached). */
let voiceGate = null;

/* THE BOOT WINDOW. The opening `greet` goes out on the same frame as the mount
 * and three things have to land before it can be a bubble: voice.js, its two
 * data modules, and the renderer. On a cold WebView2 (first paint, a font
 * fetch, six module requests) that is comfortably more than the renderer's own
 * 2.5s replay grace, and losing the race cost the day-1 introduction. */
const VOICE_PENDING_MS = 6000;

/** Replay the one buffered moment - but only once EMI could actually say it. */
function flushVoice() {
  const q = voicePending;
  if (!q || !voice || !voice.ready || (voiceGate && !voiceGate())) return false;
  voicePending = null;
  if (Date.now() - q.when >= VOICE_PENDING_MS) return false;
  try { return !!voice.onMoment(q.name, q.payload); } catch (e) { return false; }
}

/** The shell's accessor - `moments.js` and any later caller read EMI here. */
export function getEmi() { return singleton; }

/** The voice (emi/voice.js), once it has loaded. Null is the normal answer. */
export function getVoice() { return voice; }

/**
 * ASK THE VOICE, SAFELY. `moments.js` routes every moment through here before
 * it reaches the wordless table.
 *
 * Two things this wrapper buys, and both matter: a voice that throws can never
 * reach a shell screen transition, and a moment fired BEFORE the voice module
 * has landed is remembered. The shell's opening `greet` goes out on the same
 * frame as the mount and `voice.js` is one dynamic import away, so without the
 * one-slot buffer the very first beat of the game could never fire.
 * @returns {boolean} true when the voice performed and the table should stand down
 */
export function voiceMoment(name, payload) {
  try {
    if (voice && voice.ready && (!voiceGate || voiceGate())) return !!voice.onMoment(name, payload);
    if (singleton) voicePending = { name, payload, when: Date.now() };
  } catch (e) { /* a mascot may never break a screen transition */ }
  return false;
}

/**
 * @param {Object} o
 * @param {Element} o.layer    the `#arc-emi` layer (absent = EMI simply does not exist)
 * @param {Object=} o.store    core/store.js - position/hidden/telemetry persistence
 * @param {Function=} o.toast  the SHELL's toast (createShell's `shout`) - the first
 *                             dismiss ever spends one line through it, then never again
 * @param {boolean=} o.enabled default true; `setEnabled(false)` is the API-only off switch
 * @param {Function=} o.log
 * @returns {Object|null} the controller, or null when there is nothing to mount
 */
export function mountEmi({ layer, store, toast, enabled = true, log } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  if (!layer) return null;
  if (singleton) return singleton;

  const widget = createWidget({ root: layer, store, toast, log: say });
  if (!widget) return null;

  /* THE OPENING BEAT LANDS LATE OR NOT AT ALL. The renderer is one dynamic
   * import away and the shell's first `greet` fires on the same frame as the
   * mount, so ONE pending call is remembered and replayed when the face lands -
   * and only if it lands promptly. A reaction three seconds after its moment is
   * worse than a reaction that was missed. */
  if (!enabled) widget.setEnabled(false);

  const PENDING_GRACE_MS = 2500;
  let pending = null;
  /** emi/vox.js, once it lands. Null is a perfectly good answer (a silent EMI). */
  let vox = null;
  function remember(fn) { if (!widget.hasFace()) pending = { fn: fn, when: Date.now() }; }

  /* THE RENDERER, LATE AND OPTIONAL. Three modules, one failure mode: EMI keeps
   * her body and her verbs and loses her face. */
  Promise.all([
    import('./face.js').catch((e) => { say('emi: face.js unavailable (' + ((e && e.message) || e) + ')'); return null; }),
    import('./chains.js').catch((e) => { say('emi: chains.js unavailable (' + ((e && e.message) || e) + ')'); return null; }),
    import('./fx.js').catch((e) => { say('emi: fx.js unavailable (' + ((e && e.message) || e) + ')'); return null; }),
    /* HER VOICE, on the same terms as her face. `vox.js` owns no audio node -
     * it asks shell/audio.js for blips - so a broken one costs the babble and
     * nothing else: the bubble, the cadence and the whole wordless table stand. */
    import('./vox.js').catch((e) => { say('emi: vox.js unavailable (' + ((e && e.message) || e) + ')'); return null; }),
  ]).then(([f, c, x, v]) => {
    if (v && typeof v.createVox === 'function') {
      try { vox = v.createVox({ log: say }); }
      catch (e) { vox = null; say('emi: createVox threw - ' + ((e && e.message) || e)); }
    }
    try { widget.attach({ face: f, chains: c, fx: x, vox }); }
    catch (e) { say('emi: attach threw - ' + ((e && e.message) || e)); }
    const p = pending;
    pending = null;
    if (p && widget.hasFace() && Date.now() - p.when < PENDING_GRACE_MS) {
      try { p.fn(); } catch (e) { /* noop */ }
    }
    // THE FACE WAS THE OTHER HALF OF THE RACE. If the voice is already up and
    // holding the opening moment, this is the tick it can finally speak on.
    flushVoice();
  }).catch(() => {});

  const api = {
    /** The `.emi` node. Read-only to callers; the widget owns its geometry. */
    get el() { return widget.el; },
    /** The dock button (bottom-right edge, up only while EMI is dismissed). */
    get dock() { return widget.dock; },

    /**
     * A chain id from CHAINS (`wink`, `shock`, `love`, ...) or a RAW face string.
     * A raw string is drawn, held `opts.hold` (default 1400ms) and released back
     * to idle - which is what makes `(≧◡≦)` and `#ERR` first-class without
     * minting a chain for every one-frame feeling.
     * @param {string} idOrText
     * @param {{hold?:number, fx?:string, body?:string, force?:boolean}=} opts
     */
    emote(idOrText, opts) {
      const o = opts || {};
      if (typeof idOrText !== 'string' || !idOrText) return false;
      if (!widget.hasFace()) { remember(() => api.emote(idOrText, o)); return false; }
      const table = widget.chainsTable();
      const chain = table && Object.prototype.hasOwnProperty.call(table, idOrText) ? table[idOrText] : null;
      if (chain) return widget.play(chain, { force: !!o.force });
      return widget.raw(idOrText, {
        hold: typeof o.hold === 'number' ? o.hold : DIALS.RAW_HOLD_MS,
        fx: o.fx || null,
        body: o.body || null,
        force: !!o.force,
      });
    },

    /**
     * THE TALK RULE (locked): EMI never mouths words. The face holds `0_0` while
     * the bubble types `.` `..` `...`, then the reaction face lands with the
     * line. A say is PROTECTED - a pet or a drag cannot cut it mid-sentence.
     * THE HOLD IS A FLOOR, NOT A CONSTANT (owner, 2026-08-24): a landed line
     * stays up DIALS.SAY_HOLD_MIN_MS at the very least and longer the longer it
     * is. `opts.hold` still wins when it asks for MORE; it can never ask for
     * less. The typing cadence is untouched.
     * @param {string} line
     * @param {{face?:string, hold?:number, nod?:boolean}=} opts
     */
    say(line, opts) {
      const o = opts || {};
      if (typeof line !== 'string' || !line.trim()) return false;
      if (!widget.hasFace()) { remember(() => api.say(line, o)); return false; }
      const make = widget.makeSayFn();
      if (!make) return false;
      let chain = null;
      try { chain = make(line, o.face || '^_^', sayHoldMs(line, o.hold)); }
      catch (e) { say('emi: makeSay threw - ' + ((e && e.message) || e)); return false; }
      if (o.nod) chain = Object.assign({}, chain, { body: 'nod' });
      return widget.play(chain, { protect: true, force: true });
    },

    /** Back to the resting state (0_0 + blink + breath). */
    idle() { widget.idle(); },

    /** Dismiss to the dock. NOT the x: an API hide spends no first-time hint. */
    hide() { widget.hide(); },
    /** Restore from the dock, at the last saved position. */
    show() { widget.show(); },
    get hidden() { return widget.hidden; },

    /**
     * The whole-feature off switch. NOT the same thing as `hide()`: hide is the
     * player dismissing EMI to the dock (persisted), this is "EMI is not part of
     * this build/session at all" and is deliberately NOT persisted - there is no
     * settings row for it yet (see CLAUDE.md §2, emi/).
     */
    setEnabled(on) { widget.setEnabled(!!on); },
    get enabled() { return widget.enabled; },

    /**
     * Set her width in px and REMEMBER it (clamped to DIALS.W_MIN..W_MAX). Until
     * this is called she follows the window: DIALS.W_DEFAULT on a viewport at
     * least DIALS.W_NARROW_VW wide, DIALS.W_NARROW below it.
     */
    setWidth(px) { return widget.setWidth(px); },
    get width() { return widget.width; },

    /** Lifetime telemetry, read-only. A later Records Office beat reads this. */
    stats() { return widget.stats(); },
    /** Force the debounced persistence write out now (host shutdown, tests). */
    flush() { widget.flush(); },

    /** The decision engine, once it has loaded. A test/debug handle only. */
    get voice() { return voice; },
    /** Debug: the one moment waiting on the voice + the face, by name. */
    get pendingMoment() { return voicePending ? voicePending.name : null; },

    /** Her babble (emi/vox.js), once it has loaded. Test/debug handle only. */
    get vox() { return vox; },

    destroy() {
      try { widget.destroy(); } catch (e) { /* noop */ }
      try { if (voice) voice.destroy(); } catch (e) { /* noop */ }
      try { if (vox) vox.destroy(); } catch (e) { /* noop */ }
      vox = null;
      voice = null;
      voicePending = null;
      voiceGate = null;
      if (singleton === api) singleton = null;
    },
  };

  singleton = api;

  /* THE VOICE, LATE AND OPTIONAL (emi/voice.js). Exactly the discipline the
   * renderer gets above: a dynamic import in a catch, so a broken decision
   * engine costs EMI her lines and nothing else - she keeps her face, her
   * verbs and the whole wordless table. It loads AFTER the controller exists
   * because it speaks through it. */
  voiceGate = () => widget.hasFace();
  import('./voice.js').then((m) => {
    if (singleton !== api || !m || typeof m.createVoice !== 'function') return;
    voice = m.createVoice({
      store,
      emi: api,
      stats: () => api.stats(),
      onGesture: widget.onGesture,
      // The two halves of the boot race, handed to the engine so it can never
      // spend a one-shot beat on a face that cannot draw it yet.
      canPerform: () => widget.hasFace(),
      onReady: () => { flushVoice(); },
      log: say,
    });
    flushVoice();
  }).catch((e) => { say('emi: voice.js unavailable (' + ((e && e.message) || e) + ')'); });

  return api;
}

export default mountEmi;
