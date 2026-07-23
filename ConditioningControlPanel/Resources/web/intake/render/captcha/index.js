/* ============================================================================
 * render/captcha/index.js — CAPTCHA ITEM REGISTRY / DISPATCHER for "Graded Intake"
 *
 * The single seam between beats.js and the fake-captcha item family. beats.js
 * routes every Verify* Mechanic through this module; it dispatches to whichever
 * item module (grid / checkbox / transcribe / custody / stillness) is live, and
 * returns false when no live module handled the mechanic so beats.js can fall
 * back to plain rendering. The five item modules are loaded via GUARDED DYNAMIC
 * import at module-init (Promise.allSettled) — a failed module logs on the
 * 'intake-log' seam and leaves a null slot; NOTHING here throws, at import or at
 * dispatch. Import-safe with no DOM: chrome.js and this file both guard every
 * document/window touch.
 *
 *   canRender(mechanic) -> boolean   a LIVE module is registered for this mechanic
 *   render(mechanic, ctx, helpers) -> boolean
 *                                    true  = a live module handled the beat (owns the
 *                                            card body + answer commit)
 *                                    false = missing / failed / not-implemented — the
 *                                            caller must fall back to plain rendering
 *   chrome                           re-export of the shared chrome kit
 *
 * ============================================================================
 *  RENDER CONTRACT — the interface the five Wave-2 item modules code against.
 *  Each module exports `render(ctx, helpers) -> boolean` (a stub returns false =
 *  "not implemented, fall back"). Return true ONLY after the module has (a) built
 *  its card body into `ctx.root` AND (b) committed, or wired a path that will
 *  commit, exactly one answer through one of the ctx.submit* / forceComplete
 *  helpers. Returning true without a commit path wedges the beat.
 *
 *  @typedef {Object} CaptchaCtx  CONSTRUCTED BY beats.js, one per Verify* beat.
 *   -- the beat (read-only) --
 *   @property {BeatSpec} beat        the full engine BeatSpec (contracts.js)
 *   @property {Prompt}   prompt      beat.prompt (text/answer/heat/tags/options/…)
 *   @property {Array<{index:number,label:string,isCorrect:boolean,score:number}>} options
 *                                    normalized option list (empty for free-input shapes)
 *   @property {string}   mechanic    the Verify* Mechanic.* id (beat.mechanic)
 *   @property {string}   band        Band.* for this beat
 *   @property {number}   depth       0..1 depth at beat open
 *   @property {Object=}  meta        beat.meta (qIndex, qTotal, bandBeat, bandNew, streak…)
 *   @property {number}   timeoutMs   beat.timeoutMs (0 = untimed) — renderer owns its own clock;
 *                                    call ctx.submitTimeout() when it expires if it honors one
 *   @property {SteerRoll} steerRoll  beat.steerRoll (primary/secondary/intensity)
 *   @property {Object}   caps        user effect caps (DEFAULT_CAPS shape)
 *   @property {BankTheme} theme      resolved theme pack (accent/subjectNoun/praise/…)
 *   @property {MediaManifest|null} media  host-sampled user assets (gifs/images/bubbleSprite)
 *   @property {string}   niche       Niche.* for this run
 *   @property {boolean}  reduced     prefers-reduced-motion is active
 *   -- mount point --
 *   @property {HTMLElement} root     #intake-stage, ALREADY cleared. Mount the captcha
 *                                    card here. Persistent layers that must outlive the
 *                                    per-render innerHTML wipe parent to <body> themselves.
 *   -- answer submission (land the beat via EXACTLY ONE of these) --
 *   @property {(index:number, opts?:{steered?:boolean})=>void} submitIndex
 *                                    commit an OPTION answer by index into `options`
 *                                    (grid/custody serialize a derived scalar to a graded
 *                                    option index; MC-shaped items pick directly)
 *   @property {(value:*, opts?:{steered?:boolean})=>void} submitValue
 *                                    commit a FREE value (bool for checkbox/stillness,
 *                                    string for transcribe, number for a telemetry slider)
 *   @property {()=>void} submitTimeout   commit as timed-out (scores 0 / refusal-shaped)
 *   @property {(index?:number)=>void} forceComplete  invariant #1 escape hatch: an
 *                                    UN-VETOABLE commit (the hatch link / third-attempt /
 *                                    caps-expiry path routes here). Honors friction-not-lockout.
 *   -- steering + seams (no new coupling) --
 *   @property {(steerOptions:Array<{index:number,el:HTMLElement,isCorrect:boolean,score:number}>)=>void} installSteering
 *                                    wire the Phase-0 SteerContext to the captcha's own
 *                                    option elements (optional; captcha chrome may skip it)
 *   @property {(id:string, intensity?:number, opts?:Object)=>void} sfx  guarded SFX seam
 *                                    (audio.sfx / 'intake-sfx' CustomEvent). Hold no audio handle.
 *   @property {(id:string, opts?:Object)=>void} voice  guarded arbitrary-VO seam for verdict
 *                                    stingers (cap_*.json voExtra). Routes an explicit vo id
 *                                    through beats.js's audio handle; fails soft on a missing
 *                                    manifest entry. ctx.speakPrompt() still speaks q_<promptId>.
 *   @property {(fn:()=>void)=>void} onCleanup  register teardown (run on commit/teardown)
 *   @property {Object} chrome        the shared chrome kit (identical to helpers.chrome)
 *
 *  @typedef {Object} CaptchaHelpers  static conveniences (no per-beat state).
 *   @property {Object}   chrome        the chrome kit (ensureCss/frame/spinner/stamp/gridShell/…)
 *   @property {Object}   Mechanic      contracts.js Mechanic enum
 *   @property {Object}   Band          contracts.js Band enum
 *   @property {(n:number)=>number} clamp01
 *   @property {Function} makeAnswerEvent  contracts.js AnswerEvent builder (rarely needed —
 *                                    prefer ctx.submit*; exposed for parity/tests)
 * ==========================================================================*/

import { chrome } from './chrome.js';

export { chrome };

/* Which module owns which mechanic. The five NEXT-TIER placeholder mechanics
 * (verifyregen/segment/rotate/gaze/recall) are intentionally ABSENT — canRender
 * returns false for them, so beats.js falls back until their modules ship. */
const MECHANIC_MODULE = Object.freeze({
  verifygrid:       './grid.js',
  verifycheckbox:   './checkbox.js',
  verifytranscribe: './transcribe.js',
  verifycustody:    './custody.js',
  verifystillness:  './stillness.js',
});

/* mechanic -> live module (or null if it failed / hasn't resolved yet). */
const registry = Object.create(null);

/** Log on the shared seam (never throws, harmless if unheard). */
function ilog(msg) {
  try {
    if (typeof window !== 'undefined' && typeof CustomEvent === 'function') {
      window.dispatchEvent(new CustomEvent('intake-log', { detail: { msg: 'ixcap/index: ' + msg } }));
    }
  } catch (_e) {}
}

/* ----------------------------------------------------------------------------
 * MODULE-INIT LOAD — guarded dynamic import of every item module in parallel.
 * Promise.allSettled: one module failing NEVER blocks the others and NEVER
 * throws. A rejected/invalid module leaves its registry slot null (fall back).
 * Kicked off at import so modules are usually resolved before the first captcha
 * beat; a Verify* beat that arrives first simply falls back for that one beat.
 * -------------------------------------------------------------------------- */
const _ready = Promise.allSettled(
  Object.keys(MECHANIC_MODULE).map((mech) => {
    const path = MECHANIC_MODULE[mech];
    return import(path)
      .then((m) => {
        const mod = m && (m.default && typeof m.default.render === 'function' ? m.default : m);
        registry[mech] = (mod && typeof mod.render === 'function') ? mod : null;
        if (!registry[mech]) ilog('module ' + path + ' has no render() — slot left empty');
      })
      .catch((e) => { registry[mech] = null; ilog('module ' + path + ' failed to load: ' + (e && e.message)); });
  })
).catch(() => {});   // allSettled never rejects, but belt-and-braces

/** Resolves once every item-module import has settled (for tests/harness). */
export function ready() { return _ready; }

/** True iff a LIVE module is registered for this mechanic. */
export function canRender(mechanic) {
  return !!(mechanic && registry[mechanic] && typeof registry[mechanic].render === 'function');
}

/**
 * Dispatch a Verify* beat to its item module.
 * @returns {boolean} true if a live module handled it; false to fall back.
 */
export function render(mechanic, ctx, helpers) {
  const mod = mechanic && registry[mechanic];
  if (!mod || typeof mod.render !== 'function') return false;
  try {
    return mod.render(ctx, helpers) === true;
  } catch (e) {
    ilog('render(' + mechanic + ') threw: ' + (e && e.message));
    return false;   // a throwing module degrades to plain rendering
  }
}

export default { canRender, render, ready, chrome };
