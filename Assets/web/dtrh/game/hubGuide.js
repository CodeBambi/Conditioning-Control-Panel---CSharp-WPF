/* hubGuide.js - the Warren's FTUE director. Two once-ever guided moments in the hub:
 * the first-ever welcome (VN beats + a portal guide card) and the first-return moment
 * after run 1 (a beat, then the TOYBOX/DIALS station flash, then guide cards). Both are
 * flag-gated on meta (seenWarrenWelcome / seenFirstReturn) so a reset-onboarding
 * replays them live; every beat is click-skippable and any station/portal click
 * cancels the remainder of a sequence. The instances behind vnBeat/teach are the SAME
 * vn + lessonCard chaosRun uses in-run - same world-hold contract, same look. */

export function createHubGuide({ vnBeat, vnCancel, teach, teachBusy, setFlag, log }) {
  let running = false;    // a sequence is in flight (also bridges the set-flag -> snapshot latency)
  let cancelToken = 0;

  /** Called by warren on show() AND refresh(). Returns true when the guide takes
   * ownership of this render's reveal-flash pass (it will call io.flashPass itself,
   * ordered after its beat); false = warren runs the pass as usual. */
  function maybeStart(v, io) {
    if (!v || running || (teachBusy && teachBusy())) return false;
    if (!v.seenWarrenWelcome) { welcome(); return false; }
    if (!v.seenFirstReturn && v.runs >= 1) { firstReturn(io && io.flashPass); return true; }
    return false;
  }

  async function welcome() {
    running = true;
    const t = ++cancelToken;
    // Flag first (openIntro's pattern): the one-way write makes re-entry impossible
    // even if the snapshot lands mid-sequence.
    setFlag('seenWarrenWelcome');
    log && log('hubGuide: welcome sequence');
    try {
      await vnBeat('welcome1');
      if (t !== cancelToken) return;
      await vnBeat('welcome2');
      if (t !== cancelToken) return;
      teach({
        glyph: '🕳', name: 'THE HOLE',
        desc: 'the dark thing below is the way down. click it when you are ready to fall.',
        flavor: 'your first fall is a gentle one. she will show you the verbs.',
      }, { kicker: 'her burrow' });
    } finally {
      if (t === cancelToken) running = false;
    }
  }

  async function firstReturn(flashPass) {
    running = true;
    const t = ++cancelToken;
    setFlag('seenFirstReturn');
    log && log('hubGuide: first-return sequence');
    try {
      // Beat first - the station flashes would be invisible under the VN dim, and
      // vn-speaking suppresses the reveal bark until the beat ends.
      await vnBeat('return1');
      if (flashPass) flashPass();   // dollhouse pending -> toybox+dials 3D flash + chime
      if (t !== cancelToken) return;
      window.setTimeout(() => {
        if (t !== cancelToken) return;
        // Guide cards queue back-to-back; the flash gets 1.4s to breathe first.
        teach({
          glyph: '🧸', name: 'THE TOYBOX', accent: '102,224,208',
          desc: 'your emotes ✦ land here. level what you grabbed in the fall - and train habits you keep forever.',
        }, { kicker: 'new in the burrow' });
        teach({
          glyph: '🎛', name: 'THE DIALS', accent: '232,67,147',
          desc: 'your gold 🪙 spends here. buy back your first dial - she will cover you if you are short.',
        }, { kicker: 'new in the burrow' });
      }, 1400);
    } finally {
      if (t === cancelToken) running = false;
    }
  }

  return {
    maybeStart,
    /** A station/portal click or Esc lands mid-sequence: drop the remaining beats.
     * Flags were set up-front, so nothing re-fires. */
    onInterrupt() { cancelToken++; running = false; try { vnCancel && vnCancel(); } catch (e) { /* ignore */ } },
    isBusy: () => running,
    dispose() { cancelToken++; running = false; },
  };
}
