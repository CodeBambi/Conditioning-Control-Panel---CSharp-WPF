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
/** emi/fieldtrips.js, once the voice it reads has landed. Null is normal. */
let trips = null;
/** emi/asks.js (wave EMI ASKS), on the same terms. Null is normal. */
let asks = null;
let voicePending = null;
/** () => bool: can EMI actually PERFORM right now (is her face attached). */
let voiceGate = null;

/* THE BOOT WINDOW. The opening `greet` goes out on the same frame as the mount
 * and three things have to land before it can be a bubble: voice.js, its two
 * data modules, and the renderer. On a cold WebView2 (first paint, a font
 * fetch, six module requests) that is comfortably more than the renderer's own
 * 2.5s replay grace, and losing the race cost the day-1 introduction. */
const VOICE_PENDING_MS = 6000;

/**
 * THE INTRO OWNS THE SCREEN, AND SHE WAITS IT OUT (owner playtest, 2026-08-24:
 * "the mascot is speaking underneath the intro and first few bits"). Exactly two
 * things are ever laid over the whole page during an opening - boot.js's
 * `#arc-loader` splash, whose entire contract is `hidden`, and a FIRST BELL
 * scene, whose `#arc-vn` layer exists only while a scene is playing - and she
 * may speak under neither.
 *
 * This reads the DOM rather than a flag the shell hands down, and that is the
 * choice: both ids are already the page's contract (shell.js keeps its own
 * `splashUp()` read of the loader, vn/index.js mounts and drops the layer as one
 * verb), and a boolean would have to be told about the WALK and the MAIL scenes
 * too - they mount that same layer minutes after any "the boot is over" latch
 * would have flipped. Reading the screen every time is honest for every scene
 * there will ever be. A page with no document (the node DOM double) answers
 * false, so every suite runs exactly as it did.
 */
function introHolding() {
  try {
    if (typeof document === 'undefined' || !document.getElementById) return false;
    const loader = document.getElementById('arc-loader');
    if (loader && !loader.hidden) return true;
    return !!document.getElementById('arc-vn');
  } catch (e) { return false; }
}

/** Replay the one buffered moment - but only once EMI could actually say it.
 *  A shut gate returns WITHOUT spending the slot, which is what makes the wait
 *  a deferral: the held moment sits there until `noteIntroDone` comes for it. */
function flushVoice() {
  const q = voicePending;
  if (!q || !voice || !voice.ready || (voiceGate && !voiceGate())) return false;
  voicePending = null;
  if (Date.now() - q.when >= VOICE_PENDING_MS) return false;
  try { return !!voice.onMoment(q.name, q.payload); } catch (e) { return false; }
}

/**
 * THE INTRO IS OVER (shell.js's first-bell edge is the one caller). Nothing that
 * waited on it is stale - the moment was never allowed to happen, it was held -
 * so the slot is RE-STAMPED before the flush. A splash plus a cold open runs far
 * longer than VOICE_PENDING_MS, and the day-1 introduction is the exact line
 * that window was written to protect: expiring it here would lose the beat the
 * gate was added to save.
 */
function noteIntroDone() {
  if (voicePending) voicePending.when = Date.now();
  return flushVoice();
}

/** The shell's accessor - `moments.js` and any later caller read EMI here. */
export function getEmi() { return singleton; }

/** The voice (emi/voice.js), once it has loaded. Null is the normal answer. */
export function getVoice() { return voice; }

/** The field-trip scheduler (emi/fieldtrips.js), once it has loaded. */
export function getTrips() { return trips; }

/** The ask engine (emi/asks.js), once it has loaded. Null is normal. */
export function getAsks() { return asks; }

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
    /* ASKS: THE LATCHES FIRST, AND UNCONDITIONALLY. `offer` below only ever
     * sees the moments the voice AND the trips both declined, but the ask
     * engine's bookkeeping - is a class up, did a dare just resolve, has the
     * gaze bias expired - has to be right whether she spoke or not. Two entry
     * points, one order, and this is the one that runs every time. */
    if (asks) { try { asks.note(name, payload); } catch (e) { /* noop */ } }
    if (voice && voice.ready && (!voiceGate || voiceGate())) {
      /* ...AND THE ONE THING THAT OUTRANKS THE VOICE. Three skipped "bed?"s
       * buy a groggy morning, and it REPLACES the greet pool for exactly one
       * greet - asked after the voice it would be a second line on one beat. */
      if (name === 'greet' && asks) {
        let took = false;
        try { took = !!asks.greetIntercept(payload); } catch (e) { took = false; }
        if (took) return true;
      }
      if (voice.onMoment(name, payload)) return true;
      /* ...AND THEN THE FIELD TRIP (wave W2a). Asked LAST and only about the
       * moments it declares, so a night with a line in it is never also a night
       * she walks off in the middle of one. A launched trip answers true the
       * same way a spoken line does: the wordless table stands down, because
       * EMI is not there to make a face - she is on her way across the quad.
       * The trips module is asked through the same wrapper the voice is, so a
       * scheduler that throws can no more reach a screen transition than a
       * decision engine that does. */
      if (trips && trips.offer(name, payload)) return true;
      /* ...AND THEN THE ASK (wave EMI ASKS). Last, for the same reason the
       * trips are: a night with a line in it is never also a night she stops
       * the player to ask a question. A launched ask answers true - the
       * wordless table stands down because the bubble is already hers. */
      if (asks) return !!asks.offer(name, payload);
      return false;
    }
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
export function mountEmi({ layer, store, toast, enabled = true, log, assets, settings } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  if (!layer) return null;
  if (singleton) return singleton;

  /* OFF CHANNELS (W3): `assets` is the shell's provider handle and `settings`
   * is `init.settings`. NOW WATCHING is the only thing in EMI that wants
   * either, and both are optional - a host that hands neither simply plans that
   * channel out of the wheel (never a stub, never a black glass). */
  const widget = createWidget({ root: layer, store, toast, log: say, assets, settings });
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
      /* NOT UNDER THE INTRO. `voiceGate` already stops the decision engine from
       * spending a beat while the splash or a scene is up, but the bubble is a
       * door of its own - the wordless table, the off channels and the Annex
       * reveal all reach it without passing the voice - so the law is enforced
       * where the line actually lands. Deliberately NOT `remember`ed: the ONE
       * held moment is the voice's slot and it replays through `introDone`, and
       * a say buffered here as well would land the same line twice.
       * AND THE BLIPS. If a babble slipped in on the frame before the layer went
       * up, this is where it is cut: `idle()` runs setBubble(null), which is the
       * one path in widget.js that stops the blip ladder (trap 70). Nothing else
       * reaches emi/vox.js's setTimeouts. */
      if (introHolding()) {
        try { if (widget.saying()) widget.idle(); } catch (e) { /* noop */ }
        return false;
      }
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

    /** True while a PROTECTED line is on the bubble (a say in flight). A
     *  read-only seam for beats that must not cut her off mid-sentence - the
     *  Annex reveal probes it before its hard cut to black. */
    get saying() { try { return !!widget.saying(); } catch (e) { return false; } },

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

    /* ---- THE OFF CHANNELS (W3) ---------------------------------------- */
    /**
     * Lay a channel over her glass. `painter` is a painter object or one of the
     * ids in `emi/channels.js` CHANNELS. Returns false when the deck refused,
     * which is the answer most of the time and is never an error.
     */
    screenTakeover(painter, opts) { return widget.screenTakeover(painter, opts); },
    /** The deck (emi/takeover.js), once it has loaded. Test/debug handle only. */
    get channels() { return widget.channels; },
    /** A shell moment changed the screen's owner. moments.js is the one caller. */
    noteMoment(name) { widget.noteMoment(name); },

    /** The decision engine, once it has loaded. A test/debug handle only. */
    get voice() { return voice; },
    /** The field-trip scheduler (W2a), once it has loaded. Test/debug only. */
    get trips() { return trips; },
    /** The ask engine (EMI ASKS), once it has loaded. The shell reads its
     *  `flags` (a01's comfort faces) and its `classResult` (the dare payout);
     *  everything else on it is a test/debug handle. */
    get asks() { return asks; },
    /** Debug: the one moment waiting on the voice + the face, by name. */
    get pendingMoment() { return voicePending ? voicePending.name : null; },

    /**
     * THE INTRO HAS FINISHED - the splash is down and the opening has closed.
     * `shell.js maybeFirstBell` is the one caller, because that is the single
     * place on the page that knows both latches are set. It is a FLUSH, not a
     * latch: the gate keeps reading the screen afterwards, so a later FIRST BELL
     * scene still gets its silence. Held moments land on this call.
     */
    introDone() { return noteIntroDone(); },

    /** Debug: is a splash or a scene holding her tongue right now? */
    get introHeld() { return introHolding(); },

    /** Her babble (emi/vox.js), once it has loaded. Test/debug handle only. */
    get vox() { return vox; },

    destroy() {
      try { widget.destroy(); } catch (e) { /* noop */ }
      try { if (asks) asks.destroy(); } catch (e) { /* noop */ }
      try { if (trips) trips.destroy(); } catch (e) { /* noop */ }
      try { if (voice) voice.destroy(); } catch (e) { /* noop */ }
      try { if (vox) vox.destroy(); } catch (e) { /* noop */ }
      vox = null;
      voice = null;
      trips = null;
      asks = null;
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
  /* THE GATE IS NOW TWO QUESTIONS: can she draw a bubble, and is the screen
   * hers to speak on. Both are cheap and both are read at the instant a moment
   * arrives, so a beat that finds either shut is HELD rather than spent. */
  voiceGate = () => widget.hasFace() && !introHolding();
  import('./voice.js').then((m) => {
    if (singleton !== api || !m || typeof m.createVoice !== 'function') return;
    voice = m.createVoice({
      store,
      emi: api,
      stats: () => api.stats(),
      onGesture: widget.onGesture,
      // The boot race, handed to the engine so it can never spend a one-shot
      // beat on a face that cannot draw it yet - or on a screen the intro still
      // owns, which is the same waste for the same reason. voice.js banks the
      // refusal in its own one slot and answers honestly either way.
      canPerform: () => widget.hasFace() && !introHolding(),
      onReady: () => { flushVoice(); },
      log: say,
    });
    flushVoice();
    /* THE FIELD TRIPS, ONE MORE STEP OUT (wave W2a). Loaded after the voice
     * because it reads the voice's session counter and writes the voice's seen
     * ledger, and optionally for the same reason everything else in this file
     * is optional: a broken scheduler costs EMI one rare delight, never her
     * face, her verbs or the shell's boot. */
    import('./fieldtrips.js').then((f) => {
      if (singleton !== api || !f || typeof f.createFieldTrips !== 'function') return;
      trips = f.createFieldTrips({
        widget,
        voice,
        // The return beat rides the ordinary moment path, so story.js owns
        // whether b28 is spent and this module never writes a story flag.
        fire: (n, p) => voiceMoment(n, p),
        log: say,
      });
    }).catch((e) => { say('emi: fieldtrips.js unavailable (' + ((e && e.message) || e) + ')'); });
    /* THE ASKS, ONE MORE STEP OUT (wave EMI ASKS, 2026-08-25). After the voice
     * because it reads the voice's session counter and spends the voice's
     * quirk slot, and optionally for the same reason everything else in this
     * file is optional: a broken ask engine costs EMI her questions, never her
     * face, her verbs or the shell's boot. */
    import('./asks.js').then((a) => {
      if (singleton !== api || !a || typeof a.createAsks !== 'function') return;
      asks = a.createAsks({ widget, emi: api, voice, store, log: say });
    }).catch((e) => { say('emi: asks.js unavailable (' + ((e && e.message) || e) + ')'); });
  }).catch((e) => { say('emi: voice.js unavailable (' + ((e && e.message) || e) + ')'); });

  return api;
}

export default mountEmi;
