/* ============================================================================
 * shell/audio.js - THE ONE consumer of the engine's `arcademy-sfx` requests.
 *
 * GROUND-RULES §6: the engine holds no audio handle, it SHOUTS
 * `CustomEvent('arcademy-sfx', {detail:{name, level, bus, duck?}})` on `document`
 * and somebody else owns the sound. That somebody is this file.
 *
 *   createAudio({ init, bridge, log })  once, from boot.js
 *
 * WHY IT SYNTHESISES: there are no sfx files in the build yet, and a page that
 * 404s six wav files on the first beat is worse than one that beeps. Every sound
 * is a short oscillator/noise envelope, so the mixer is real today and swapping
 * in real samples later only touches `play()`.
 *
 * LEVELS: gain = sqrt(sfx level) x group level x masterVolume x (audioMute ? 0 : 1).
 * The group levels arrive already clamped in `init.audioLevels` and move ONLY on
 * the host's `setting` echo (`audioLevels.fx`, `audioMute`, `masterVolume`) -
 * same law as the settings page: the echo is the truth, never the control.
 *
 * DUCKING: `detail.duck = {target, mult, ms}` is already scaled by the player's
 * duckDepth cap (engine/curves.js DUCK .4/.25/.15). We ramp the affected buses to
 * `mult` and back over ~200ms. A duck is never a snap.
 *
 * PITCH: `detail.pitch` (optional, 0.5-2, default 1) multiplies every frequency in
 * the recipe - oscillator sweeps, arpeggio steps, the noise band and the stamp's
 * body thunk. That is the whole feature: three games wanted a pitch ratchet (a
 * rising chain of hits) and were reduced to ratcheting the LEVEL instead, which
 * reads as "louder", not as "climbing". Garbage clamps to 1, so an older engine
 * that never sends the field sounds exactly as it did.
 *
 * AUTOPLAY: no AudioContext is created until the first user gesture; requests
 * before that are dropped silently (a beep the browser refuses is not an error).
 * No AudioContext at all (headless, old webview) -> every call is a no-op.
 * THE ONE EXCEPTION is `autoplayOk` (AV CLUB, 2026-08-24): the app's own host
 * launches this WebView2 with `--autoplay-policy=no-user-gesture-required`, so
 * inside the app there is nothing to wait for and waiting COSTS us the only cue
 * that can never be re-fired - the opening splash, which is over before the
 * player has touched anything. The caller passes the option (boot.js reads the
 * host's `init.autoplayOk`); it is OFF by default, because a page served
 * anywhere else is still bound by the browser's policy and a context created
 * into a refusal is a context that plays nothing.
 *
 * SAMPLES (AV CLUB, 2026-08-24). A handful of cues want a RECORDED sound, not a
 * synthesised one - a school bell and a paper swish are exactly the two things
 * an oscillator is worst at. `SAMPLES` maps those names onto files under
 * `./assets/sfx/`, and a sample name resolves CLIP-FIRST: the same clip path a
 * `detail.url` takes (same bus, same mute/level/duck laws, keyed on the cue's
 * own name so a re-fire cuts itself), with the SOUNDS recipe as the floor
 * underneath. Two names - `intro_bed` and `flap_deal` - have no recipe at all
 * and are deliberately SILENT until their file ships: a stitched-together
 * imitation of a bed track is worse than no bed track.
 * A sample is only tried when the host has SAID the file is there
 * (`init.sfxSamples`, and see `available` below) - an absent field means no
 * samples, which is exactly the shape of the build today and sounds precisely
 * as it did before this wave.
 *
 * WHY THE ONE-SHOTS ARE PRE-DECODED (owner report, 2026-08-26: "opening a paper
 * arrives late and shouts"). A sample used to be a fresh `new Audio()` per fire:
 * every tap paid a fetch off the virtual host, an mp3 decode and a media
 * pipeline spin-up before a single sample left the speaker - 300-700ms in
 * WebView2, on a cue whose whole job is to be simultaneous with a click. (The
 * files are clean; ffmpeg says there is no leading silence in any of them.) So
 * the moment the context comes up we fetch and `decodeAudioData` every AVAILABLE
 * one-shot once, keep the AudioBuffer, and fire it through an
 * AudioBufferSourceNode into the same bus graph: no element, no decode, no wait.
 * Three consequences worth knowing:
 *   - A CUE THAT BEATS ITS OWN DECODE FALLS TO THE RECIPE, never to the old
 *     element path. Trap 70's doctrine, applied to loudness as well as time: an
 *     instant oscillator impression beats a late recording, and re-minting the
 *     element here would only reintroduce the lateness we are deleting. A
 *     SAMPLE_ONLY name with nothing decoded yet drops, exactly as a missing file
 *     drops - the imitation it does not have is still the imitation it may not
 *     make up.
 *   - A FETCH OR DECODE FAILURE STRIKES THE NAME OFF `available`, the same
 *     verdict the element's `error` handler passes: the name is a recipe for the
 *     rest of the session rather than a decoder spent once a beat.
 *   - THE BEDS STAY ON THE ELEMENT. `NEVER_BUFFERED` below: the five room tones
 *     loop on `hold` (an element loops for free and holds no buffer for the
 *     night), and `intro_bed` is the splash's 4s jingle - fired once, at boot,
 *     before any decode could plausibly have finished, and a bed that arrives
 *     half a beat late is a bed, while a bed that drops is a silent opening.
 *
 * SAMPLES ARE LOUDER THAN RECIPES, AND THE TABLE ADMITS IT. The recorded files
 * are mastered hot (peaks -0.5 to -3 dBFS) where the recipes they replaced were
 * built quiet by construction, so `paper` and `door` - shell furniture, the two
 * cues the Records Annex fires most - arrived shouting. `SAMPLE_TRIM` is a
 * per-name multiplier on the one-shot gain, applied on both the buffer path and
 * the element one. It is deliberately NOT a change to CLIP_GAIN: the whisper
 * clips and every other url ride that number too, and they were never the ones
 * that were too loud.
 *
 * CLIPS (2026-08-23, Echo's trigger bubbles). A cue may carry `detail.url` - a
 * SAME-ORIGIN `ccp.*` media file (the app's own whisper clips). It is then
 * played from an HTMLAudioElement routed through the requested bus, so every
 * law above still holds: mute, master, bus level, ducking. Three rules make it
 * safe for a game that fires one per press:
 *   - `detail.key` is a VOICE SLOT. A second clip on the same key cuts the
 *     first (a fast sequence must not pile six whispers on top of each other);
 *     with no key the url is the slot.
 *   - `detail.maxMs` truncates playback (default CLIP_MAX_MS) with a short
 *     fade, so a 6-second phrase does not outlive the round. An EXPLICIT
 *     maxMs may buy more than the default - up to CLIP_REQ_MAX_MS - because
 *     the intro bed is a 4s piece and the 1.2s governor was guillotining it
 *     mid-phrase right as the splash exited (owner report, 2026-08-24). The
 *     silent default is unchanged: a caller who says nothing still gets 1.2s.
 *   - a clip is never louder than a recipe at the same level: CLIP_GAIN is the
 *     same headroom the SOUNDS table's `gain` gives an oscillator.
 * A url the browser will not decode is silently dropped, exactly like a name
 * that is not in SOUNDS - a cue must never be the thing that throws.
 *
 * THE ENDED HOOK (2026-08-25). A caller may pass `detail.onEnded` and be told,
 * EXACTLY ONCE and never fatally, what became of its cue:
 *     'ended'   the element played the file out
 *     'cap'     the maxMs governor cut it
 *     'stopped' something took the slot (a re-fire, the voice cap, teardown)
 *     'error'   the file will not load and the name has been struck off
 *     'recipe'  the name fell back to its oscillator impression
 *     'dropped' the cue never sounded at all - muted, zero master, zero bus,
 *               no context, or a SAMPLE_ONLY name with no file behind it
 * THE 'dropped' ANSWER IS SYNCHRONOUS, inside the dispatch, and that is the
 * point of it: boot.js holds the intro splash until the 4s bed is over, so it
 * has to learn INSTANTLY when there is no bed rather than sit out a timeout.
 * The hook is a courtesy, not a contract - a caller that waits on it must still
 * carry its own cap (boot.js does), because a host with no consumer at all on
 * the `arcademy-sfx` bus will never answer anything.
 * ==========================================================================*/

const BUSES = ['fx', 'voice', 'tutorial', 'drops', 'music'];
const DEFAULT_LEVELS = { fx: 0.85, voice: 0.85, tutorial: 0.85, drops: 0.4, music: 1.0 };

/** Which buses a duck target pulls down (DTRH's sidechain hierarchy). */
const DUCK_TARGETS = {
  voice: ['fx', 'music', 'drops'],
  spotlight: ['fx', 'voice', 'tutorial', 'music'],
  voiceUnderSpotlight: ['voice', 'tutorial', 'fx', 'music'],
};

/** name -> recipe. `noise` = filtered white noise, else an oscillator sweep.
 *  Unknown names fall through to `blip` (a game may invent an sfx id freely). */
const SOUNDS = {
  blip:      { type: 'sine',     f0: 660, f1: 660, ms: 60,  gain: 0.6 },
  sting:     { type: 'triangle', f0: 440, f1: 520, ms: 140, gain: 0.7 },
  glitch:    { noise: true, hp: 900,  lp: 5200, ms: 90,  gain: 0.8, bits: 6 },
  whisper:   { noise: true, hp: 1400, lp: 4200, ms: 240, gain: 0.35 },
  wash:      { noise: true, hp: 120,  lp: 900,  ms: 900, gain: 0.5, attack: 0.35 },
  flash:     { noise: true, hp: 2000, lp: 9000, ms: 50,  gain: 0.7 },
  burst:     { noise: true, hp: 700,  lp: 6000, ms: 120, gain: 0.7 },
  pop:       { type: 'sine',     f0: 900, f1: 260, ms: 80,  gain: 0.8 },
  bubble_pop:{ type: 'sine',     f0: 1200, f1: 380, ms: 70, gain: 0.7 },
  stamp:     { type: 'sine',     f0: 150, f1: 58,  ms: 190, gain: 1.0, thunk: true },
  stamp_bad: { type: 'sawtooth', f0: 96,  f1: 44,  ms: 240, gain: 0.9, thunk: true },
  /* W0 (2026-08-24): Anomaly's wrong-tap / round-timeout beats requested `thud`
     since Semester II and were silently degrading to `blip` - a bright tick on
     the game's two LOSS moments, the exact opposite of the muted thud the House
     Book asks for. Softer and shorter than either stamp: a body knock, no bite. */
  thud:      { type: 'sine',     f0: 130, f1: 52,  ms: 150, gain: 0.85, thunk: true },
  streak:    { arp: [523.25, 659.25], ms: 90,  gain: 0.6 },
  jackpot:   { arp: [523.25, 659.25, 783.99, 1046.5], ms: 110, gain: 0.9 },
  near_miss: { type: 'sawtooth', f0: 180, f1: 760, ms: 520, gain: 0.5, riser: true },
  /* The Deep End, pass 2: the slide is a short filtered-noise whoosh (level =
     tiles moved, pitch = depth); the wall is a low sawtooth buzz with a body
     thunk - a muted loss, never silence. */
  slide:     { noise: true, hp: 260,  lp: 2600, ms: 150, gain: 0.55, attack: 0.18 },
  bump:      { type: 'sawtooth', f0: 92,  f1: 46,  ms: 150, gain: 0.85, thunk: true },
  /* Semesters II/III (2026-08-23). `pad` is Echo's instrument: an UNSWEPT triangle
     so `pitch` alone carries pad identity (six pads = six pitches off one recipe,
     +1 semitone per streak link); `decoy` is the telegraphed false pad. `tell` /
     `lift` are Misdirection's trackability tell and the decoy-lid sting; `near` the
     Anomaly near-miss ping (distinct from the long `near_miss` riser); `chime` a
     clean bell for the streak ladders; `shutter` the darkroom's camera click. */
  /* ECHO'S SILENCE (2026-08-25, "no sound in Echo on mobile or web"). The pad
     was a lone 392Hz triangle at gain .55 - after the engine's binaural scaler,
     the perceptual sqrt and the bus/master law it landed near -25 dBFS, which a
     phone speaker rounds down to nothing. Three changes, one recipe: gain up to
     .9 (a game NOTE, not chrome - it may sit at the top of the table), f0 down
     to 330 (phone speakers roll off less there, and the pitch ladder's 0.67..2x
     spread keeps every pad inside the mixer's clamp), and a sine OCTAVE layer at
     .3 - the existing `layer` field, so it rides pitch, mute and duck for free
     and gives the tone a second partial a tiny speaker can actually find. */
  pad:       { type: 'triangle', f0: 330, f1: 330, ms: 200, gain: 0.9,
               layer: { type: 'sine', f0: 660, f1: 660, ms: 200, gain: 0.3 } },
  decoy:     { noise: true, hp: 600,  lp: 3800, ms: 120, gain: 0.6, bits: 5 },
  tell:      { type: 'sine',     f0: 880, f1: 880, ms: 70,  gain: 0.7 },
  lift:      { type: 'triangle', f0: 330, f1: 495, ms: 120, gain: 0.55 },
  near:      { type: 'sine',     f0: 740, f1: 620, ms: 110, gain: 0.55 },
  chime:     { type: 'sine',     f0: 1046.5, f1: 1046.5, ms: 160, gain: 0.7 },
  shutter:   { noise: true, hp: 1800, lp: 9000, ms: 40,  gain: 0.7 },
  /* EMI's VOICE, "Blipese" (2026-08-24, emi/vox.js). Two recipes and no third:
     the mascot babbles by firing `emi_blip` many times with a per-blip `pitch`,
     so the TIMBRE lives here, once, and the melody lives over there.
     TRIANGLE, not square. The owner's directive is "pleasant at low volume - she
     has to be someone you get attached to", and a square up here is a beeper.
     The chirp falls ~a whole tone across the blip so each one reads as a little
     syllable rather than a tone; `attack` 0.3 of the duration (~17ms of the 56)
     is what keeps it from clicking; and gain 0.35 is HALF a game pop. A mascot
     murmurs - she is never the loudest thing on the screen.
     TASTE ROUND (owner): the square variant is kept here rather than in a doc so
     the comparison is a two-line swap.
       emi_blip:  { type: 'square', f0: 760, f1: 680, ms: 52, gain: 0.26, attack: 0.34 },
   */
  emi_blip:  { type: 'triangle', f0: 760, f1: 680, ms: 56, gain: 0.35, attack: 0.3 },
  /* ...and the tick under the `.` `..` `...` typing frames: low, short, a fifth
     of the blip's gain. It is anticipation, not a sound in its own right - the
     moment a player NOTICES it, it is wrong (vox.js DOT_TICKS turns it off). */
  emi_tick:  { type: 'triangle', f0: 336, f1: 322, ms: 30, gain: 0.16, attack: 0.35 },
  /* THE SHELL'S OWN VOICE (AV CLUB, 2026-08-24). Everything above this line
     belongs to a game or to the mascot; these seven are the SCHOOL - a board
     that flaps, a door that shuts, the bell, the clock over it, a form being
     stamped, paper, and the air moving when EMI leaves the room. They are
     chrome, so they are quiet by construction: every gain here sits under a
     game one-shot's, because a player notices shell furniture only when it is
     too loud.

     `flap` is the split-flap board's single vane - 35ms of dry high noise, and
     it is short on purpose: twelve of them cascade inside half a second, and
     any tail at all turns the deal into a hiss. `clock_tick` is its opposite
     number, the same gesture with the brightness taken off, because a clock
     under a bell should read as pressure rather than as a tick you count.

     `door` and `commit` both end in a `thunk` (the tone plus a lowpassed body
     hit) - that is what makes a school sound like a building. `commit` then
     LAYERS a two-note fifth over the thunk: a confirmation needs a low half
     that says "landed" and a bright half that says "and it was good", and one
     oscillator cannot be both.

     `bell` is the only long sound in the table. Two sine partials a slightly
     sharp fifth apart, both drooping across the decay, is the cheapest thing
     that reads as a strike rather than as a beep - the sharpness is the point
     (a clean fifth sounds like a chord; an inharmonic one sounds like metal).

     `paper` and `whoosh` are the same noise band with different manners:
     paper is a short swish that gets out of the way, whoosh is 350ms with a
     soft attack and a RISING filter, so it arrives instead of just existing. */
  flap:      { noise: true, hp: 1400, lp: 6200, ms: 35,  gain: 0.5 },
  door:      { type: 'sine',     f0: 118, f1: 62,  ms: 180, gain: 0.9, thunk: true },
  bell:      { type: 'sine',     f0: 784, f1: 712, ms: 600, gain: 0.5, attack: 0.012,
               layer: { type: 'sine', f0: 1179, f1: 1064, ms: 520, gain: 0.26, attack: 0.012 } },
  clock_tick:{ noise: true, hp: 620,  lp: 2400, ms: 40,  gain: 0.4 },
  commit:    { type: 'sine',     f0: 165, f1: 84,  ms: 250, gain: 0.85, thunk: true,
               layer: { arp: [392, 587.33], ms: 90, gain: 0.34 } },
  paper:     { noise: true, hp: 1500, lp: 6000, ms: 140, gain: 0.45, attack: 0.25 },
  whoosh:    { noise: true, hp: 300,  lp: 1800, ms: 350, gain: 0.5, attack: 0.4, sweep: 3.2 },
  /* THE HUM (THE SEEP, tell 04). Fluorescent ballast and a tape motor somewhere
     under the floor: two detuned partials at mains frequency and its octave,
     700ms, gone before the ear names it. The sound design of the reveal (the
     THUD) gets an ancestor.
     TRIANGLE AND SINE, NOT SAWTOOTH, and that is the whole recipe decision. The
     pitch's sketch is "two detuned sawtooths through a 420Hz lowpass" - but this
     table's oscillator branch has no filter (only `noise` recipes are filtered),
     and a raw 50Hz saw is all upper harmonics on a laptop speaker, i.e. a buzz
     rather than a hum. A triangle plus a sine IS the lowpassed saw: partials
     falling off as 1/n^2 with almost nothing above the third. The `layer` field
     (bell / commit's own) carries the second partial, deliberately a hair sharp
     of the octave so the two BEAT against each other - the beat is the tell.
     Quiet by construction: gain .3 under a fired level of .22 is roughly a
     quarter of a game one-shot, which is where a thing you are not sure you
     heard has to sit. Rides every mute / duck / bus law for free. */
  seep_hum:  { type: 'triangle', f0: 50, f1: 49.4, ms: 700, gain: 0.3, attack: 0.16,
               layer: { type: 'sine', f0: 100.6, f1: 99.2, ms: 640, gain: 0.2, attack: 0.18 } },
  /* W3 "EVERY INPUT ANSWERED" (2026-08-25). Eleven recipes the moment table
     asked for and the table did not have. Each is named for the GESTURE, not
     the game, so a second class can borrow it without a rename.
       queue        the mixer's "heard you, not yet": a 40ms sine, quieter than
                    a blip, for a press that was accepted into a queue rather
                    than acted on (Composure). Distinct from `bump` (refused).
       pip          the report card's per-cell tally: a rising sine so a ladder
                    of six reads as a count, not a beep repeated.
       step         a footstep. Dry low noise, 30ms; the caller alternates
                    pitch .96/1.04 so two feet are two feet.
       record       a lifetime best, the RAREST sound in the building: a three
                    note triangle arp with a real tail. Louder than jackpot on
                    purpose - a record is allowed to be the loudest thing.
       false_solve  the Sort trickster's fake solve: jackpot's shape a hair
                    flat (pitch .983 in the caller) over a wash, so it sounds
                    like a win to the ear and wrong to the gut.
       descend      the Impulse Control tube slide: a 1.2s saw riser under a
                    noise band sweeping up. Faded by the caller on reveal.
       neon_strike  a neon tube striking: one square burst here; the VN fires
                    the second and third through `steps` at 180/420ms, and the
                    third carries the saw hum layer.
       campus_wake  the campus revealing itself, 4.2s, music bus, the only
                    recipe that slow. Never under reducedMotion (caller's law).
       drain_bed    water leaving a tank, 1.6s falling band (Deep End resurface)
                    and the floor under the `water_drain` sample.
       bubble_bed   the bubble sustain's own air, per WAVE never per node.
       spiral_hum   the Loom's spiral on mount: 62Hz triangle under a sine a
                    fifth up, 1.4s, quieter than seep_hum's fired level. */
  queue:       { type: 'sine',     f0: 520, f1: 520, ms: 40,  gain: 0.35, attack: 0.2 },
  pip:         { type: 'sine',     f0: 660, f1: 990, ms: 70,  gain: 0.55 },
  step:        { noise: true, hp: 120,  lp: 900,  ms: 30,  gain: 0.3 },
  record:      { arp: [660, 880, 1320], ms: 300, gain: 0.8 },
  false_solve: { arp: [523.25, 659.25, 783.99], ms: 110, gain: 0.5,
                 layer: { noise: true, hp: 120, lp: 900, ms: 400, gain: 0.3, attack: 0.3 } },
  descend:     { type: 'sawtooth', f0: 90, f1: 180, ms: 1200, gain: 0.25, attack: 0.5, riser: true,
                 layer: { noise: true, hp: 300, lp: 1400, ms: 1200, gain: 0.2, attack: 0.5, sweep: 8 } },
  neon_strike: { type: 'square',   f0: 120, f1: 120, ms: 60,  gain: 0.5,
                 layer: { type: 'sawtooth', f0: 120, f1: 120, ms: 300, gain: 0.08, attack: 0.1 } },
  campus_wake: { type: 'triangle', f0: 110, f1: 220, ms: 4200, gain: 0.5, attack: 0.38,
                 layer: { type: 'triangle', f0: 165, f1: 330, ms: 4200, gain: 0.25, attack: 0.38 } },
  drain_bed:   { noise: true, hp: 90,   lp: 900,  ms: 1600, gain: 0.5, attack: 0.3, sweep: 0.35 },
  bubble_bed:  { noise: true, hp: 180,  lp: 600,  ms: 1200, gain: 0.4, attack: 0.5, sweep: 1.8 },
  spiral_hum:  { type: 'triangle', f0: 62, f1: 62, ms: 1400, gain: 0.3, attack: 0.4,
                 layer: { type: 'sine', f0: 93.4, f1: 93.4, ms: 1300, gain: 0.2, attack: 0.4 } },
};

/** ALIASES (W3). A cue name that is really another recipe, optionally re-pitched.
 *  Two families live here:
 *    - Misdirection's VERDICT names (`hit` `miss` `ride` `bank` `reveal`): the
 *      game fired them since Semester II and every one degraded to `blip`, so
 *      its five most important beats were the same tick. Named here so the
 *      call sites keep their vocabulary and the ear gets five sounds.
 *    - the FLOOR under a sampled name: `knock` with no file is a `door`,
 *      `tape_stop` with no file is a `glitch` slowed to .7. The sample plays
 *      when the host says the file is there; the alias plays when it is not.
 *  Resolution is ONE level deep, on purpose. A sample lookup uses the name as
 *  fired; only the recipe fallback walks the alias. */
const ALIASES = {
  hit:    { name: 'sting' },
  miss:   { name: 'thud' },
  ride:   { name: 'pop' },
  bank:   { name: 'commit' },
  reveal: { name: 'tell' },
  knock:          { name: 'door' },
  bell_short:     { name: 'bell' },
  card_deal:      { name: 'paper' },
  shutter_close:  { name: 'shutter', pitch: 0.8 },
  mail_drop:      { name: 'flap' },
  water_drain:    { name: 'drain_bed' },
  tape_stop:      { name: 'glitch', pitch: 0.7 },
  /* COUNTER STOCK: the brass bell's FLOOR is the ordinary bell. `cueFor` only
   * swaps the name when the host says the mp3 is on disk, so this is the third
   * rung under it - the host lied, the file will not decode, the element could
   * not be minted - and on every one of them the school still rings. */
  bell_brass:     { name: 'bell' },
};

/** THE SAMPLE DOOR (AV CLUB, 2026-08-24): cue name -> a file beside the page.
 *  Same origin as index.html (the host maps the whole folder), which is what
 *  lets the element feed the bus graph rather than slipping the mixer. */
const SAMPLES = {
  intro_bed:  './assets/sfx/intro_bed.mp3',
  bell:       './assets/sfx/bell.mp3',
  flap_deal:  './assets/sfx/flap_deal.mp3',
  stamp:      './assets/sfx/stamp.mp3',
  stamp_bad:  './assets/sfx/stamp_bad.mp3',
  door:       './assets/sfx/door.mp3',
  paper:      './assets/sfx/paper.mp3',
  whoosh:     './assets/sfx/whoosh.mp3',
  /* W3 (2026-08-25): seven one-shots with a recipe floor (see ALIASES)... */
  knock:          './assets/sfx/knock.mp3',
  bell_short:     './assets/sfx/bell_short.mp3',
  card_deal:      './assets/sfx/card_deal.mp3',
  shutter_close:  './assets/sfx/shutter_close.mp3',
  mail_drop:      './assets/sfx/mail_drop.mp3',
  water_drain:    './assets/sfx/water_drain.mp3',
  tape_stop:      './assets/sfx/tape_stop.mp3',
  /* ...and five ROOM-TONE BEDS, sample-only, meant for `hold` (below). */
  records_bed:    './assets/sfx/records_bed.mp3',
  campus_idle:    './assets/sfx/campus_idle.mp3',
  vn_bed_ext:     './assets/sfx/vn_bed_ext.mp3',
  vn_bed_int:     './assets/sfx/vn_bed_int.mp3',
  cam_bed:        './assets/sfx/cam_bed.mp3',
  /* COUNTER STOCK (2026-08-26). THE BRASS BELL: the old bell out of the
   * storage room, and it is a SAMPLE STANDING BESIDE `bell` rather than a
   * replacement for it. The swap is one line in `cueFor` below and it only
   * happens when the file is actually here, so a player who owns the prize on
   * a build with no mp3 in it hears the ordinary school bell - never a blip
   * and never silence (trap 115: an unknown name is a tick, and a bell that
   * ticks is the exact bug that table was written for). */
  bell_brass:     './assets/sfx/bell_brass.mp3',
};

/** THE PA PACK (Counter Stock). Thirty-six spoken announcements, and they are
 *  generated into the table rather than typed out because the only thing that
 *  varies is a two-digit number - thirty-six literal rows is thirty-six
 *  chances to fat-finger a path that then fails as SILENCE.
 *
 *  THEY SHIP FLAT. `BuildSfxSamples` on the host scans `assets/sfx`
 *  TopDirectoryOnly, so a `pa/` subfolder would never reach `init.sfxSamples`
 *  and every line would be a file the page has been told does not exist. The
 *  files are `assets/sfx/pa_01.mp3` .. `pa_36.mp3`, flat, beside the bells. */
export const PA_COUNT = 36;
/** `pa_07` from 7. The zero pad is part of the name, not decoration. */
export function paName(n) {
  const i = Math.max(1, Math.min(PA_COUNT, Math.round(Number(n) || 0)));
  return 'pa_' + String(i).padStart(2, '0');
}
for (let i = 1; i <= PA_COUNT; i += 1) {
  SAMPLES[paName(i)] = './assets/sfx/' + paName(i) + '.mp3';
}

/** THE SOUNDTRACK (2026-08-27). Five Suno tracks, one per place, held on the
 *  `music` bus by shell/ost.js exactly the way the beds are held. This file
 *  imports nothing (trap 18), so the names are spelled here as well as in
 *  ost.js TRACKS; the two lists are the same list and a track added to one
 *  without the other is a file the page has been told does not exist. Flat in
 *  `assets/sfx` beside the bells, same host scan as the PA lines. */
const OST_SAMPLES = Object.freeze([
  'ost_campus', 'ost_deep_end', 'ost_sort', 'ost_records', 'ost_lost_found',
  'ost_instant_recall', 'ost_anomaly', 'ost_daily_trigger', 'ost_impulse_control',
  'ost_prizes', 'ost_misdirection', 'ost_deja_vu',
]);
for (const n of OST_SAMPLES) SAMPLES[n] = './assets/sfx/' + n + '.mp3';

/** PER-NAME HEADROOM ON A SAMPLE (owner report, 2026-08-26: "too loud").
 *  A recorded one-shot at `amp * CLIP_GAIN` is not the same loudness as the
 *  recipe it replaced - the mp3s are mastered near full scale and the recipes
 *  were written to sit under a game cue. This is the correction, as DATA rather
 *  than as a re-master: the number is what the file needs, the name is what the
 *  ear complained about, and anything not listed here is 1 (untouched).
 *  `paper` and `door` are the Records Annex's own furniture and fire on every
 *  page turn and every slide, so they take the deepest cuts. */
const SAMPLE_TRIM = {
  paper:  0.45,
  door:   0.55,
  whoosh: 0.65,
};
const trimFor = (name) => {
  const t = SAMPLE_TRIM[name];
  return (Number.isFinite(t) && t > 0) ? t : 1;
};

/** Samples that are NEVER pre-decoded and always play from an element: the five
 *  room-tone beds (only ever fired with `hold`, and an element loops for free)
 *  plus the splash jingle, which is struck once at boot ahead of any decode.
 *  Everything else in SAMPLES is a short one-shot and wants to be instant. */
const NEVER_BUFFERED = new Set([
  'records_bed', 'campus_idle', 'vn_bed_ext', 'vn_bed_int', 'cam_bed',
  'intro_bed',
]);
/* THE PA LINES ARE NEVER PRE-DECODED EITHER, and the reason is arithmetic:
 * `prebufferSamples` decodes every available one-shot at context-up, and
 * thirty-six seconds-long spoken lines decoded to float PCM is tens of
 * megabytes of resident memory bought for a feature that plays at most two of
 * them a night. Latency is the whole case for pre-decoding (a page turn half a
 * beat late), and an announcement over a school PA has no beat to be late for.
 * They keep the element path, like the beds. */
for (let i = 1; i <= PA_COUNT; i += 1) NEVER_BUFFERED.add(paName(i));
/* A SOUNDTRACK IS A BED WITH A TUNE IN IT: element path, never decoded (a
 * 140s track as float PCM is a hundred megabytes for one loop). */
for (const n of OST_SAMPLES) NEVER_BUFFERED.add(n);

/** The names with NO recipe under them. A missing file is SILENCE here, not
 *  a fallback: an oscillator impression of a bed track or of a whole board
 *  dealing at once would be a different sound wearing the same name, and the
 *  cue sites that fire these are the ones nobody gets to hear twice. */
const SAMPLE_ONLY = new Set([
  'intro_bed', 'flap_deal',
  'records_bed', 'campus_idle', 'vn_bed_ext', 'vn_bed_int', 'cam_bed',
  /* COUNTER STOCK. A PA line is a person speaking: there is no oscillator
   * impression of "the schedule has moved to the Music Room", so a missing
   * file is silence and the announcement simply did not happen tonight.
   *
   * `bell_brass` is deliberately NOT in this set - it is an ALIAS onto `bell`
   * instead (see ALIASES). Sample-only would make a brass bell with no file
   * behind it SILENT, and a school bell that stops ringing the night you buy a
   * nicer one is a worse bug than the blip the rule exists to prevent. The
   * alias gives it the third floor the bells have always had: the file, then
   * the school bell's own sample, then the school bell's recipe. */
]);
for (let i = 1; i <= PA_COUNT; i += 1) SAMPLE_ONLY.add(paName(i));
/* And no oscillator impression of a soundtrack either (ost.js law 4). */
for (const n of OST_SAMPLES) SAMPLE_ONLY.add(n);

/* ----------------------------------------------------------------------------
 * THE BELL COSMETIC (Counter Stock, `brass_bell`)
 *
 * MODULE LEVEL ON PURPOSE. boot.js builds the one consumer long before the
 * shell exists, so the shell has no handle on the mixer and never should - but
 * it CAN import a function. This is that function, and it is the road the shell
 * takes: `setBellCosmetic(() => ownsSku('brass_bell'))`, once, at shell build.
 *
 * A GETTER, not a boolean, so a bell bought mid-session rings brass on the very
 * next cue and a lapsed entitlement goes back to the school bell without a
 * reload. audio.js still imports nothing and still asks nobody for a wallet
 * (trap 18's discipline): it is handed an answer and it calls it when a bell
 * rings. Passing `null` (or nothing) clears it, which is what a teardown wants.
 * -------------------------------------------------------------------------- */

/** @type {(boolean|Function|null)} */
let bellCosmetic = null;

/** @param {(boolean|Function|null)=} v */
export function setBellCosmetic(v) {
  bellCosmetic = (typeof v === 'function' || v === true) ? v : null;
}

/** The module-level answer, never a throw. */
function bellCosmeticOwned() {
  if (typeof bellCosmetic === 'function') {
    try { return bellCosmetic() === true; } catch (e) { return false; }
  }
  return bellCosmetic === true;
}

/* HOLD (W3, 2026-08-25) - the mixer's first and only SUSTAIN. Until this wave
 * the mixer could not loop anything: every sound was a one-shot capped at 8s,
 * and the rooms (Records Office, the campus at idle, the VN's gate and hallway,
 * the annex cams) had no air under them. The contract, all of it:
 *   - `detail.hold: true` on a SAMPLED name (or a `detail.url`) LOOPS the
 *     element in the slot `detail.key || name`, ignores `maxMs`, and fades IN
 *     over CLIP_FADE_MS. Same bus, mute, master, level and duck laws as a clip.
 *   - `detail.stop: true` fades that slot OUT over CLIP_FADE_MS. `stop` is
 *     honoured even when muted, so a room can always be left.
 *   - `stop_clips` (class teardown) and the mute echo cut holds too.
 *   - a RECIPE cannot hold. A hold asked of a name with no file behind it is
 *     'dropped' - a looping oscillator impression of a room is a different
 *     room. So a bed without its mp3 is honest silence, same as intro_bed.
 *   - a held slot is not evicted by the voice cap while a one-shot slot exists.
 * EVERY HOLD HAS AN OWNER: the code that starts a bed stops it in its own
 * teardown (trap 114). The mixer will not guess when a room has been left. */

const clamp01 = (v) => (Number.isFinite(+v) ? Math.max(0, Math.min(1, +v)) : 0);

/** Clip playback ceilings. MAX_MS truncates, FADE_MS is the way out, VOICES is
 *  the hard cap on simultaneous slots (Echo needs six, one per pad). */
const CLIP_MAX_MS = 1200;
/** What an explicit `detail.maxMs` may buy. The default above is the SILENT
 *  ceiling; this is the spoken one - long enough for the 4s intro bed with
 *  air to spare, short enough that no cue can annex the night. */
const CLIP_REQ_MAX_MS = 8000;
/** THE PA'S OWN CEILING (2026-08-27). The round-3 announcements are one
 *  breath each but a warm read of a long sentence under the tannoy's echo tail
 *  runs to eleven seconds, and a spoken line cut off mid-word is worse than no
 *  line. A `pa_NN` name may buy up to this; everything else keeps the 8s cap,
 *  so no cue that is not a person speaking can annex the night. */
const PA_REQ_MAX_MS = 12000;
const isPaName = (n) => /^pa_\d\d$/.test(String(n || ''));
const CLIP_FADE_MS = 180;
const CLIP_VOICES = 6;
/** The headroom a recipe gets from its own `gain`, given to clips too, so a
 *  clip at level L is never louder than an oscillator at level L. */
const CLIP_GAIN = 0.5;

/** Playback-rate multiplier for a cue. Anything unusable is 1 (unpitched). */
const PITCH_MIN = 0.5;
const PITCH_MAX = 2;
const clampPitch = (v) => (
  Number.isFinite(+v) && +v > 0 ? Math.max(PITCH_MIN, Math.min(PITCH_MAX, +v)) : 1
);

/** A `detail.onEnded` callback, wrapped so it fires at most once and so a
 *  listener that throws can never break the cue bus (header: THE ENDED HOOK). */
function onceCb(fn) {
  let f = (typeof fn === 'function') ? fn : null;
  return (reason) => {
    if (!f) return;
    const g = f;
    f = null;
    try { g(String(reason || 'ended')); } catch { /* a listener may never break the bus */ }
  };
}

/**
 * @param {Object} o
 * @param {Object} o.init    the init projection (audioLevels / audioMute / masterVolume)
 * @param {Object=} o.bridge the shell bridge (for the `setting` echo). Optional.
 * @param {Function=} o.log
 * @param {boolean=} o.autoplayOk  ARM AT CREATION (default false). The app's host
 *   launches the view with `--autoplay-policy=no-user-gesture-required`, so no
 *   gesture is owed; the caller passes `init.autoplayOk === true` rather than
 *   this file reading the projection, because "may I make noise unasked" is a
 *   property of the HOST the page is in, not of the page.
 * @param {(boolean|Function)=} o.brassBell  COUNTER STOCK: does the player own
 *   `brass_bell`? A boolean or a getter, and a getter is the useful shape - it
 *   is asked on every cue, so a bell bought mid-session rings brass on the next
 *   one without a reload. This file learns ownership and NOTHING else: no sku
 *   table, no wallet, no inventory, one question with a yes/no answer.
 */
export function createAudio({ init, bridge, log, autoplayOk, brassBell } = {}) {
  const say = typeof log === 'function' ? log : () => {};
  const src = init || {};
  const doc = (typeof document !== 'undefined') ? document : null;

  const levels = Object.assign({}, DEFAULT_LEVELS);
  const initLevels = (src.audioLevels && typeof src.audioLevels === 'object') ? src.audioLevels : {};
  for (const b of BUSES) if (initLevels[b] != null) levels[b] = clamp01(initLevels[b]);
  let mute = !!src.audioMute;
  let master = src.masterVolume == null ? 1 : clamp01(src.masterVolume);

  const Ctor = (typeof AudioContext !== 'undefined') ? AudioContext
    : (typeof window !== 'undefined' && window.webkitAudioContext) ? window.webkitAudioContext : null;

  let ac = null;
  let out = null;                    // master gain
  const busGain = Object.create(null);   // bus -> {level: GainNode, duck: GainNode}
  let gestured = autoplayOk === true;
  /** `clips` counts every VOICE taken (element or buffer); `buffered` is the
   *  subset that fired from a pre-decoded AudioBuffer and `decoded` how many
   *  names are ready to do so - between them they say whether the latency fix is
   *  actually running in this host or whether everything fell to a recipe. */
  const stats = {
    handled: 0, played: 0, dropped: 0, ducks: 0, clips: 0, samples: 0,
    buffered: 0, decoded: 0, last: null,
  };
  /** Names that fell through to `blip`, logged once each (trap 115). */
  const unknownNames = new Set();

  /* WHICH SAMPLES ARE ACTUALLY THERE, and why the page is not the one to guess.
   * A media element cannot answer "does this file exist" synchronously - it 404s
   * asynchronously, long after the beat it was meant to land on - so a page that
   * probed would either drop the first cue of every sampled name or lie to
   * `hasSample()`. The HOST already knows: it serves the folder off disk. So it
   * says so, once, in `init.sfxSamples` (bare names, no path), and this set is
   * that list intersected with SAMPLES. No field, no samples: every name falls
   * to its recipe and the two sample-only names stay silent, which is precisely
   * how the build sounds before any file ships. A name that then fails to load
   * anyway (a truncated file, a codec this webview will not take) is struck off
   * on its `error` and never tried again this session. */
  const available = new Set();
  if (Array.isArray(src.sfxSamples)) {
    for (const n of src.sfxSamples) { if (SAMPLES[String(n)]) available.add(String(n)); }
  }

  /* ---------------------------------------------------------- THE ONE SWAP
   * COUNTER STOCK's brass bell, and it is ONE function on purpose. Every road
   * a cue can take below - the buffer, the element, SAMPLE_ONLY, the recipe
   * floor - is keyed off `name`, so resolving the name ONCE at the top of the
   * dispatch is the difference between a cosmetic and four of them.
   *
   * TWO CONDITIONS, AND THE SECOND ONE IS THE SAFETY. The player owns it, AND
   * the host says the file is on disk. Without the second test a build that
   * ships the sku ahead of the mp3 would resolve `bell` to a name with no
   * sample and no recipe - which SAMPLE_ONLY turns into silence, and a school
   * bell that goes quiet the day you buy a nicer one is the worst possible
   * shape for this feature. With it, the fallback is the bell that was already
   * ringing. `strikeSample` taking a bad file off `available` mid-session
   * un-swaps it the same way, on the next cue.
   */
  /* OWNERSHIP ARRIVES LATE, WHICH IS WHY IT IS A `let`. boot.js builds this
   * consumer BEFORE the shell exists (trap 18) - a cue fired during the shell's
   * own boot has to be heard - so at construction there is nobody to ask what
   * the player owns. Three roads in, all of them ending here: the constructor
   * arg (a boolean or a getter, for a host that already knows), the MODULE-LEVEL
   * `setBellCosmetic` above - which is the road the shell takes, because it can
   * import a function without holding a mixer - and the `set_bell` control
   * message on the sfx bus, for a caller that has neither. */
  let brass = (typeof brassBell === 'function') ? brassBell : (brassBell === true);
  function ownsBrass() {
    if (typeof brass === 'function') {
      try { return brass() === true; } catch (e) { return false; }
    }
    if (brass === true) return true;
    /* The module-level answer is asked SECOND, so a host that handed this
     * consumer its own answer keeps it. */
    return bellCosmeticOwned();
  }
  function cueFor(raw) {
    const name = String(raw == null ? '' : raw);
    if (name !== 'bell') return name;
    if (!available.has('bell_brass')) return name;
    return ownsBrass() ? 'bell_brass' : name;
  }

  function ensureContext() {
    if (ac || !Ctor || !gestured) return ac;
    try {
      ac = new Ctor();
      out = ac.createGain();
      out.gain.value = 1;
      out.connect(ac.destination);
      for (const b of BUSES) {
        const level = ac.createGain();
        const duck = ac.createGain();
        level.gain.value = levels[b];
        duck.gain.value = 1;
        level.connect(duck); duck.connect(out);
        busGain[b] = { level, duck };
      }
      applyMaster();
      say('[audio] context up (' + (ac.sampleRate || '?') + 'Hz)');
      // The graph exists, so the one-shots can start decoding into it. This is
      // the earliest possible moment: everything before it had no decoder.
      prebufferSamples();
    } catch (e) { ac = null; say('[audio] no context: ' + ((e && e.message) || e)); }
    return ac;
  }

  function applyMaster() { if (out) try { out.gain.value = mute ? 0 : master; } catch { /* ignore */ } }
  function applyLevel(b) {
    const g = busGain[b];
    if (g) try { g.level.gain.value = levels[b]; } catch { /* ignore */ }
  }

  /** One noise buffer, reused: allocating 900ms of noise per beat is a stutter. */
  let noiseBuf = null;
  function noiseBuffer() {
    if (noiseBuf || !ac) return noiseBuf;
    const len = Math.max(1, Math.floor((ac.sampleRate || 44100) * 1.2));
    noiseBuf = ac.createBuffer(1, len, ac.sampleRate || 44100);
    const d = noiseBuf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
    return noiseBuf;
  }

  function envelope(node, peak, ms, attackFrac, at) {
    // `at` (seconds, optional): schedule the whole envelope ahead on the
    // context timeline - the cascade's follow-up blips ride one dispatch.
    const t = ac.currentTime + (at > 0 ? at : 0);
    const dur = Math.max(0.02, ms / 1000);
    const atk = Math.max(0.004, dur * (attackFrac == null ? 0.06 : attackFrac));
    const g = node.gain;
    g.setValueAtTime(0.0001, t);
    g.linearRampToValueAtTime(Math.max(0.0002, peak), t + atk);
    g.exponentialRampToValueAtTime(0.0001, t + dur);
    return { t, dur };
  }

  function voiceOut(bus, node) {
    const g = busGain[bus] || busGain.fx;
    if (g) node.connect(g.level);
  }

  /** Frequencies live in a 20Hz..20kHz sanity window whatever the pitch asks for. */
  const hz = (f, pitch) => Math.max(20, Math.min(20000, f * pitch));

  /* TWO FIELDS THE TABLE GREW (AV CLUB, 2026-08-24), both of them one branch:
   *   `layer` - a SECOND recipe fired with the same amp and pitch, so a cue can
   *     be two things at once (commit's low thunk under its bright fifth, the
   *     bell's second partial). One level deep only: `depth` refuses a recipe
   *     that layers itself, because a table is data and data can be edited into
   *     a loop by somebody who never reads this file.
   *   `sweep` - a multiplier on a NOISE recipe's filter centre, ramped across
   *     the envelope. A static band is a texture; a moving one is a gesture,
   *     and `whoosh` needed to be a gesture.
   * Neither field exists on any older recipe, so nothing above sounds different. */
  function playRecipe(rec, bus, amp, pitch, depth, at) {
    const p = clampPitch(pitch);
    if (rec.layer && !(depth > 0)) {
      try { playRecipe(rec.layer, bus, amp, pitch, 1, at); } catch { /* a layer is a garnish */ }
    }
    const env = ac.createGain();
    voiceOut(bus, env);
    // Duration is deliberately NOT scaled: a pitch ratchet should climb, not
    // speed up - the cadence belongs to whoever is firing the cues.
    const { t, dur } = envelope(env, amp * (rec.gain == null ? 0.7 : rec.gain), rec.ms, rec.attack, at);
    const stop = t + dur + 0.02;

    if (rec.noise) {
      const s = ac.createBufferSource();
      s.buffer = noiseBuffer();
      s.loop = true;
      const f = ac.createBiquadFilter();
      f.type = 'bandpass';
      const centre = hz(Math.sqrt(Math.max(40, rec.hp) * Math.max(80, rec.lp)), p);
      f.frequency.setValueAtTime(centre, t);
      if (rec.sweep > 0 && rec.sweep !== 1) {
        f.frequency.linearRampToValueAtTime(hz(centre * rec.sweep, 1), t + dur);
      }
      f.Q.value = rec.bits ? 1.6 : 0.7;
      s.connect(f); f.connect(env);
      s.start(t); s.stop(stop);
      return;
    }
    if (rec.arp) {
      rec.arp.forEach((f0, i) => {
        const o = ac.createOscillator();
        const g = ac.createGain();
        o.type = 'triangle';
        o.frequency.value = hz(f0, p);
        const at = t + i * (rec.ms / 1000) * 0.75;
        g.gain.setValueAtTime(0.0001, at);
        g.gain.linearRampToValueAtTime(amp * rec.gain * 0.6, at + 0.012);
        g.gain.exponentialRampToValueAtTime(0.0001, at + rec.ms / 1000);
        o.connect(g); g.connect((busGain[bus] || busGain.fx).level);
        o.start(at); o.stop(at + rec.ms / 1000 + 0.02);
      });
      return;
    }
    const o = ac.createOscillator();
    o.type = rec.type || 'sine';
    o.frequency.setValueAtTime(hz(rec.f0, p), t);
    if (rec.riser) o.frequency.linearRampToValueAtTime(hz(rec.f1, p), t + dur);
    else o.frequency.exponentialRampToValueAtTime(hz(rec.f1, p), t + dur);
    o.connect(env);
    o.start(t); o.stop(stop);
    if (rec.thunk) {                     // a stamp is a tone AND a body hit
      const n = ac.createBufferSource();
      n.buffer = noiseBuffer();
      const f = ac.createBiquadFilter();
      f.type = 'lowpass'; f.frequency.value = hz(420, p);
      const g = ac.createGain();
      envelope(g, amp * 0.5, Math.min(90, rec.ms), undefined, at);
      n.connect(f); f.connect(g); voiceOut(bus, g);
      n.start(t); n.stop(t + 0.12);
    }
  }

  /* ---- CLIPS: a url played through a bus ------------------------------- */
  /** key -> {el, gain, node, timer} . One live clip per slot, cut on re-fire. */
  const clips = new Map();

  function killClip(rec, fadeMs) {
    if (!rec) return;
    // Every teardown road passes through here, so this is the one place that can
    // promise a waiting caller it will always be told (header: THE ENDED HOOK).
    // It is a no-op for whichever path already named a more specific reason.
    if (rec.settle) rec.settle('stopped');
    try { if (rec.timer) clearTimeout(rec.timer); } catch { /* ignore */ }
    rec.timer = 0;
    const fade = Math.max(0, Number(fadeMs) || 0);
    const stop = () => {
      // A BUFFER VOICE HAS NO ELEMENT, and stopping its source is the whole
      // teardown: no decoder to release, no src to take back. Everything below
      // is the element's business and is skipped for it.
      try { if (rec.src) rec.src.stop(); } catch { /* already ended */ }
      try { if (rec.src) rec.src.disconnect(); } catch { /* ignore */ }
      if (!rec.el) {
        try { if (rec.gain) rec.gain.disconnect(); } catch { /* ignore */ }
        return;
      }
      try { rec.el.pause(); } catch { /* ignore */ }
      // Releasing the src lets the decoder go; a MediaElementSource cannot be
      // re-created for the same element, so the element is never reused.
      // REMOVE THE ATTRIBUTE, never `src = ''`: an EMPTY string is still a
      // resource the element goes and tries to select, so it fails and fires an
      // `error` at us on every ordinary teardown. Attribute gone + load() aborts
      // selection with `emptied` instead - no error, same released decoder.
      try { rec.el.removeAttribute('src'); rec.el.load(); } catch { /* ignore */ }
      try { if (rec.node) rec.node.disconnect(); } catch { /* ignore */ }
      try { if (rec.gain) rec.gain.disconnect(); } catch { /* ignore */ }
    };
    if (fade > 0 && ac && rec.gain) {
      try {
        const t = ac.currentTime;
        rec.gain.gain.cancelScheduledValues(t);
        rec.gain.gain.setValueAtTime(Math.max(0.0001, rec.gain.gain.value), t);
        rec.gain.gain.exponentialRampToValueAtTime(0.0001, t + fade / 1000);
      } catch { /* ignore */ }
      setTimeout(stop, fade + 20);
      return;
    }
    stop();
  }

  /** @param {string=} sampleName  set when the url came from SAMPLES, so a file
   *   that turns out not to be playable can strike itself off `available`.
   *  @param {Function=} settle  the one-shot `detail.onEnded` reporter. Only the
   *   paths that TAKE the clip pay it; a `false` return leaves it to the caller.
   *  @returns {boolean} true if the clip was taken (played or scheduled). */
  function playClip(d, bus, amp, sampleName, settle) {
    if (typeof Audio !== 'function') return false;
    const url = String(d.url == null ? '' : d.url);
    if (!url) return false;
    const key = String(d.key == null ? url : d.key);

    const hold = d.hold === true;
    const prev = clips.get(key);
    if (prev) { clips.delete(key); killClip(prev, 60); }
    // A game that forgets to key its slots must still not run away with the
    // decoders: the oldest ONE-SHOT slot goes first; a held bed is evicted only
    // when there is nothing else left to evict (HOLD, above).
    while (clips.size >= CLIP_VOICES) {
      let victim = null;
      for (const [k, r] of clips) { if (!r.hold) { victim = k; break; } }
      if (victim == null) victim = clips.keys().next().value;
      const rec = clips.get(victim);
      clips.delete(victim);
      killClip(rec, 60);
    }

    let el;
    try {
      el = new Audio();
      // The ccp.* origins are mapped CORS-clean, which is what lets the element
      // feed a WebAudio graph at all; a tainted stream would only play direct.
      el.crossOrigin = 'anonymous';
      el.preload = 'auto';
      el.src = url;
    } catch { return false; }

    // Asked-for time is honoured up to CLIP_REQ_MAX_MS; silence means the
    // 1.2s default. (The old Math.min(CLIP_MAX_MS, asked) clamped every
    // request DOWN to the default, which cut the 4s intro bed at 1.2s.)
    const askedMs = Number(d.maxMs) || 0;
    const reqCap = isPaName(sampleName) ? PA_REQ_MAX_MS : CLIP_REQ_MAX_MS;
    const maxMs = Math.max(80, askedMs > 0 ? Math.min(askedMs, reqCap) : CLIP_MAX_MS);
    const fadeMs = Math.max(0, Math.min(maxMs / 2, Number(d.fadeMs) || CLIP_FADE_MS));
    const rec = { el, src: null, gain: null, node: null, timer: 0, settle: settle || null, hold };
    if (hold) { try { el.loop = true; } catch { /* ignore */ } }
    // The per-name trim rides the element path too, so a build that cannot
    // pre-decode (or a bed, which never does) is not the loud one.
    const trim = sampleName ? trimFor(sampleName) : 1;

    let routed = false;
    try {
      const g = ac.createGain();
      const target = Math.max(0.0001, amp * CLIP_GAIN * trim);
      if (hold) {
        // A bed arrives, it does not start: fade in over the same window the
        // fade-out uses, so entering and leaving a room are the same gesture.
        const t0 = ac.currentTime;
        g.gain.setValueAtTime(0.0001, t0);
        g.gain.linearRampToValueAtTime(target, t0 + CLIP_FADE_MS / 1000);
      } else {
        g.gain.value = target;
      }
      const node = ac.createMediaElementSource(el);
      node.connect(g);
      voiceOut(bus, g);
      rec.gain = g;
      rec.node = node;
      routed = true;
    } catch {
      // No MediaElementSource (an older webview, a tainted stream): fall back to
      // the element's own volume, folding in every level the graph would have.
      routed = false;
    }
    if (!routed) {
      try {
        el.volume = clamp01(amp * CLIP_GAIN * trim * (levels[bus] == null ? 1 : levels[bus]) * master * (mute ? 0 : 1));
      } catch { /* ignore */ }
    }

    clips.set(key, rec);
    // A held bed has no governor: its owner stops it (HOLD contract).
    if (!hold) {
      rec.timer = setTimeout(() => {
        rec.timer = 0;
        if (clips.get(key) === rec) clips.delete(key);
        if (rec.settle) rec.settle('cap');
        killClip(rec, fadeMs);
      }, maxMs);
    }
    try {
      el.addEventListener('ended', () => {
        if (clips.get(key) !== rec) return;
        clips.delete(key);
        if (rec.settle) rec.settle('ended');
        killClip(rec, 0);
      });
    } catch { /* ignore */ }
    try {
      el.addEventListener('error', () => {
        // STILL IN THE MAP IS WHAT MAKES THIS A VERDICT ON THE FILE. Every
        // legitimate teardown - the maxMs timer, `ended`, a re-fire on the same
        // key, the voice-cap eviction, stopAllClips - deletes the record BEFORE
        // it calls killClip, and killClip releases the src; a release used to
        // fire a spurious `error` here (and, once, struck a perfectly good
        // sample off `available` a second after its one and only play). So that
        // ORDERING is load-bearing: a teardown path that kills first and deletes
        // after would look exactly like a broken file. Kill the event where it
        // is not ours and the rest of this handler reads as it always did.
        if (clips.get(key) !== rec) return;
        clips.delete(key);
        if (rec.settle) rec.settle('error');
        killClip(rec, 0);
        // The file the host promised is not playable. Take the name back rather
        // than spending a decoder on it once a beat for the rest of the night;
        // from the next cue on it is a recipe again (or, for the sample-only
        // pair, honest silence).
        strikeSample(sampleName);
      });
    } catch { /* ignore */ }

    try {
      const p = el.play();
      if (p && typeof p.catch === 'function') p.catch(() => { /* a refused clip is not an error */ });
    } catch { /* ignore */ }
    return true;
  }

  function stopAllClips() {
    for (const [k, rec] of Array.from(clips.entries())) { clips.delete(k); killClip(rec, 0); }
  }

  /* ---- PRE-DECODED ONE-SHOTS (header: WHY THE ONE-SHOTS ARE PRE-DECODED) --
   * The same laws as a clip, minus the wait. A voice lives in the same `clips`
   * map so every road that already took a clip down - a re-fire on the key, the
   * voice cap, `stop_clips`, the mute echo, teardown - takes a buffer voice down
   * too, and killClip stays the one place a waiting caller is answered. */
  const buffers = new Map();     // name -> AudioBuffer, ready to fire
  const decoding = new Set();    // names with a fetch/decode in the air
  /** Can this host pre-decode at all? A node harness with no `fetch` and a
   *  webview with no `decodeAudioData` both say no, and then a sampled one-shot
   *  keeps the element path it has always had rather than losing its sound. */
  let canBuffer = false;

  /** THE VERDICT ON A FILE, in one place. The element's `error` handler and a
   *  failed decode reach the same conclusion - the host promised a file this
   *  browser will not play - so they say it the same way: the name comes off
   *  `available` and is a recipe (or honest silence) for the rest of the night. */
  function strikeSample(name, why) {
    if (!name || !available.delete(name)) return;
    say('[audio] sample ' + name + ' will not load - falling back'
      + (why ? ' (' + why + ')' : ''));
  }

  /** Idempotent by construction (`buffers` / `decoding` / a struck name is no
   *  longer in `available`), so it is safe to re-arm from a cue that found no
   *  buffer waiting - which is how a sample that became known after the context
   *  came up still ends up decoded. */
  function prebufferSamples() {
    if (!ac) return;
    // No fetch, no decoder: leave `canBuffer` false and the element path stands.
    if (typeof fetch !== 'function' || typeof ac.decodeAudioData !== 'function') return;
    canBuffer = true;
    for (const name of Array.from(available)) {
      if (NEVER_BUFFERED.has(name)) continue;
      if (buffers.has(name) || decoding.has(name)) continue;
      const url = SAMPLES[name];
      if (!url) continue;
      decoding.add(name);
      try { fetchAndDecode(name, url); }
      catch (e) { decoding.delete(name); strikeSample(name, (e && e.message) || e); }
    }
  }

  function fetchAndDecode(name, url) {
    Promise.resolve(fetch(url))
      .then((res) => {
        // `ok === false` is a 404 wearing a 200's clothes as far as decode goes.
        if (res && res.ok === false) throw new Error('HTTP ' + res.status);
        if (!res || typeof res.arrayBuffer !== 'function') throw new Error('no body');
        return res.arrayBuffer();
      })
      .then((bytes) => new Promise((resolve, reject) => {
        if (!ac) { reject(new Error('no context')); return; }
        // BOTH SHAPES: the modern promise and the old callback pair. An impl
        // that honours both simply resolves twice, which a promise ignores.
        const p = ac.decodeAudioData(bytes, resolve, reject);
        if (p && typeof p.then === 'function') p.then(resolve, reject);
      }))
      .then((buf) => {
        decoding.delete(name);
        if (!buf) throw new Error('empty decode');
        buffers.set(name, buf);
        stats.decoded += 1;
      })
      .catch((e) => {
        decoding.delete(name);
        strikeSample(name, (e && e.message) || e);
      });
  }

  /** Fire a decoded one-shot into the bus graph. Same slot semantics, same
   *  governor and the same `onEnded` reasons as the element path - the only
   *  differences are that it starts NOW and that it takes its per-name trim.
   *  @returns {boolean} true if the voice was taken. */
  function playBuffer(d, bus, amp, name, pitch, settle) {
    const buf = buffers.get(name);
    if (!ac || !buf) return false;
    const key = String(d.key == null ? name : d.key);
    const prev = clips.get(key);
    if (prev) { clips.delete(key); killClip(prev, 60); }
    while (clips.size >= CLIP_VOICES) {
      let victim = null;
      for (const [k, r] of clips) { if (!r.hold) { victim = k; break; } }
      if (victim == null) victim = clips.keys().next().value;
      const dead = clips.get(victim);
      clips.delete(victim);
      killClip(dead, 60);
    }

    const askedMs = Number(d.maxMs) || 0;
    // PA lines never take this path (NEVER_BUFFERED), the ceiling is kept
    // symmetrical with playClip so the two doors never disagree on a name.
    const reqCap = isPaName(name) ? PA_REQ_MAX_MS : CLIP_REQ_MAX_MS;
    const maxMs = Math.max(80, askedMs > 0 ? Math.min(askedMs, reqCap) : CLIP_MAX_MS);
    const fadeMs = Math.max(0, Math.min(maxMs / 2, Number(d.fadeMs) || CLIP_FADE_MS));
    const rec = { el: null, src: null, gain: null, node: null, timer: 0, settle: settle || null, hold: false };

    try {
      const g = ac.createGain();
      g.gain.value = Math.max(0.0001, amp * CLIP_GAIN * trimFor(name));
      const s = ac.createBufferSource();
      s.buffer = buf;
      // Pitch is the CALLER's only: an alias's pitch belongs to the recipe
      // impression underneath, never to the recording it stands in for.
      try { if (s.playbackRate) s.playbackRate.value = pitch; } catch { /* ignore */ }
      s.connect(g);
      voiceOut(bus, g);
      rec.gain = g;
      rec.src = s;
      s.onended = () => {
        // Still ours? A cap, a re-fire or a teardown deletes the record first
        // and has already named its own reason (playClip's `error` block has
        // the long version of why that ordering is load-bearing).
        if (clips.get(key) !== rec) return;
        clips.delete(key);
        if (rec.settle) rec.settle('ended');
        killClip(rec, 0);
      };
      s.start();
    } catch {
      try { if (rec.src) rec.src.disconnect(); } catch { /* ignore */ }
      try { if (rec.gain) rec.gain.disconnect(); } catch { /* ignore */ }
      return false;
    }

    clips.set(key, rec);
    rec.timer = setTimeout(() => {
      rec.timer = 0;
      if (clips.get(key) === rec) clips.delete(key);
      if (rec.settle) rec.settle('cap');
      killClip(rec, fadeMs);
    }, maxMs);
    return true;
  }

  function duck(spec) {
    if (!ac || !spec) return;
    const targets = DUCK_TARGETS[spec.target] || DUCK_TARGETS.voice;
    const mult = clamp01(spec.mult == null ? 0.4 : spec.mult);
    const ms = Math.max(60, Math.min(2000, Number(spec.ms) || 200));
    const t = ac.currentTime;
    for (const b of targets) {
      const g = busGain[b];
      if (!g) continue;
      try {
        g.duck.gain.cancelScheduledValues(t);
        g.duck.gain.setValueAtTime(g.duck.gain.value, t);
        g.duck.gain.linearRampToValueAtTime(mult, t + 0.05);
        g.duck.gain.setValueAtTime(mult, t + ms / 1000);
        g.duck.gain.linearRampToValueAtTime(1, t + ms / 1000 + 0.2);
      } catch { /* ignore */ }
    }
    stats.ducks += 1;
  }

  function onSfx(e) {
    const d = (e && e.detail) || {};
    stats.handled += 1;
    /* THE ENDED HOOK (header). Wrapped once here, paid exactly once on every
     * road out of this function - including the four early returns below, which
     * answer 'dropped' inside the dispatch so a caller waiting on the cue is
     * never left holding a timeout for a sound that was never going to happen. */
    const settle = onceCb(d.onEnded);
    // The one CONTROL message on the sfx bus: the shell sends it when a class is
    // torn down so a trigger clip (<=1.2s) never leaks into the lobby. No audio
    // handle crosses into shell.js for this; the bus was already the seam.
    if (d.name === 'stop_clips') { stopAllClips(); settle('dropped'); return; }
    // The second control message (HOLD): leave a room. Honoured before the mute
    // check so a bed started before the mute echo can still be let go of.
    if (d.stop === true) {
      const k = String(d.key == null ? (d.name || '') : d.key);
      const held = clips.get(k);
      if (held) { clips.delete(k); killClip(held, CLIP_FADE_MS); }
      settle('stopped');
      return;
    }
    /* THE THIRD CONTROL MESSAGE (Counter Stock). `stop_clips` and `stop` are
     * the other two, and this one is the same idea: a fact about the mixer,
     * carried on the bus that is already the seam, so nothing outside this file
     * ever needs a handle to it. It sounds nothing and it is honoured while
     * muted - what the player owns is not audible either way. */
    if (d.name === 'set_bell') { setBellCosmetic(d.brass === true); settle('dropped'); return; }
    const alias = ALIASES[String(d.name || '')] || null;
    const pitch = clampPitch(d.pitch) * (alias && alias.pitch ? alias.pitch : 1);
    stats.last = {
      name: d.name || null, level: d.level, bus: d.bus || 'fx', duck: d.duck || null, pitch,
      url: d.url || null,
    };
    if (mute || master <= 0) { stats.dropped += 1; settle('dropped'); return; }
    if (!ensureContext()) { stats.dropped += 1; settle('dropped'); return; }
    const bus = BUSES.indexOf(d.bus) >= 0 ? d.bus : 'fx';
    // PERCEPTUAL CURVE (2026-08-24): engine levels are fractions of fractions - a 0.25
    // cue under the bus and master gains landed near -29 dB and the whole campus read
    // as silent. sqrt lifts the quiet floor (0.25 -> 0.5) while 1.0 stays 1.0, so the
    // relative loudness ladder the games ratchet is preserved, just audible.
    const amp = Math.sqrt(clamp01(d.level == null ? 0.5 : d.level));
    if (amp <= 0 || levels[bus] <= 0) { stats.dropped += 1; settle('dropped'); return; }
    /* THE NAME AS RESOLVED, and every line under this one keys off it (see THE
     * ONE SWAP). `stats.last.name` above is deliberately what was FIRED - a
     * suite and a log both want to know what the page asked for, not what the
     * cosmetic turned it into, and `cueFor` on the handle answers the other
     * half of that question. */
    const name = cueFor(d.name);
    try {
      // A url is a CLIP, whatever the name says. If the host cannot play one we
      // fall through to the recipe rather than going silent.
      let took = d.url ? playClip(d, bus, amp, undefined, settle) : false;
      // ...and a SAMPLED name is a url the caller did not have to know about.
      // The slot is the name, so a cue that re-fires cuts its own tail instead
      // of stacking (twelve flaps in half a second is the case that matters).
      if (!took && available.has(name)) {
        // A ONE-SHOT SAMPLE COMES OFF A BUFFER OR IT DOES NOT COME AT ALL.
        // Minting an element here is what made a page turn arrive half a beat
        // after the page turned; if the decode has not landed yet we take the
        // recipe below instead, which is instant and quiet (header, trap 70).
        // Beds and `url` clips are not one-shots and keep the element path.
        const oneShot = d.hold !== true && !d.url && !NEVER_BUFFERED.has(name);
        if (oneShot && canBuffer) {
          if (buffers.has(name)) {
            took = playBuffer(d, bus, amp, name, clampPitch(d.pitch), settle);
            if (took) { stats.samples += 1; stats.buffered += 1; }
          } else {
            // Nothing decoded yet: re-arm (idempotent) and let the floor answer.
            prebufferSamples();
          }
        } else {
          took = playClip(
            Object.assign({}, d, { url: SAMPLES[name], key: d.key == null ? name : d.key }),
            bus, amp, name, settle
          );
          if (took) stats.samples += 1;
        }
      }
      if (took) { stats.clips += 1; stats.played += 1; }
      else if (SAMPLE_ONLY.has(name) || d.hold === true) {
        // No file, no imitation. The cue is spent, not queued (trap 70: a beat
        // played late is worse than a beat missed). A HOLD with no file behind
        // it lands here too: a recipe cannot loop (HOLD contract).
        stats.dropped += 1;
        settle('dropped');
      } else {
        /* THE SWAP'S LAST FLOOR. `alias` was keyed on what the page ASKED for;
         * a cosmetic may have moved `name` since (THE ONE SWAP), and the moved
         * name carries its own ALIASES row - `bell_brass -> bell` - so a brass
         * bell whose file is present but will not decode still rings the school
         * bell's recipe instead of the 660Hz unknown-cue blip (trap 115). For
         * every cue the swap did not touch this is the line it always was. */
        const swapped = (name !== String(d.name || '')) ? (ALIASES[name] || null) : null;
        const rName = swapped ? swapped.name : (alias ? alias.name : name);
        const rec = SOUNDS[rName];
        if (!rec && !unknownNames.has(name)) {
          // Trap 110: an unknown name is a blip, not an error - which is how
          // Misdirection's verdicts went a semester as ticks. Say so, once.
          unknownNames.add(name);
          say('[audio] unknown cue "' + name + '" - playing blip');
        }
        playRecipe(rec || SOUNDS.blip, bus, amp, pitch);
        stats.played += 1;
        // A recipe has no element and no `ended`: the impression is not the
        // recording, so a caller waiting for the FILE is told so and moves on.
        settle('recipe');
      }
    } catch (err) {
      stats.dropped += 1;
      settle('dropped');
      say('[audio] ' + (name || '?') + ' failed: ' + ((err && err.message) || err));
    }
    /* THE CASCADE (2026-08-26, deep-end choreography). `detail.steps` are
     * follow-up blips pre-scheduled on the SAME context timeline inside this
     * one dispatch - one graph build for a whole run of merge pops instead of
     * one per pop. Each step: {atMs, name?, pitch?, level?}; a missing field
     * inherits the main cue's. Recipes only (a clip cannot be scheduled ahead
     * without a decoder per step, which is the cost this exists to avoid). */
    if (ac && Array.isArray(d.steps) && d.steps.length) {
      for (const s of d.steps.slice(0, 16)) {
        if (!s) continue;
        const at = Math.max(0, Math.min(4000, Number(s.atMs) || 0)) / 1000;
        const sAmp = Math.sqrt(clamp01(s.level == null ? (d.level == null ? 0.5 : d.level) : s.level));
        if (sAmp <= 0) continue;
        try {
          const sn = String(s.name || name);
          const sa = ALIASES[sn] || null;
          playRecipe(SOUNDS[sa ? sa.name : sn] || SOUNDS.blip, bus, sAmp,
            clampPitch(s.pitch == null ? pitch : s.pitch), 0, at);
        } catch { /* a step must never break the bus */ }
      }
    }
    if (d.duck) duck(d.duck);
  }

  /** The host's echo is the ONLY thing that moves a level (settings.js trap 1). */
  function onSetting(m) {
    const key = m && m.key;
    if (typeof key !== 'string') return;
    if (key === 'audioMute') {
      mute = !!m.value;
      applyMaster();
      // A muted master silences the graph, but a clip that fell back to the
      // element's own volume is outside it - cut them rather than trust it.
      if (mute) stopAllClips();
      return;
    }
    if (key === 'masterVolume') { master = clamp01(m.value); applyMaster(); return; }
    if (key.indexOf('audioLevels.') === 0) {
      const b = key.slice('audioLevels.'.length);
      if (BUSES.indexOf(b) >= 0) { levels[b] = clamp01(m.value); applyLevel(b); }
    }
  }

  /* The gesture listeners stay wired even when we are armed at creation, and
   * they no longer bail on the flag alone: a browser that suspends the context
   * anyway (a backgrounded tab, a policy the flag did not actually lift) is
   * fixed by the first real touch, which is the cheapest safety net there is. */
  function onGesture() {
    const first = !gestured;
    gestured = true;
    if (first || !ac) ensureContext();
    if (ac && ac.state === 'suspended' && typeof ac.resume === 'function') { try { ac.resume(); } catch { /* ignore */ } }
  }

  if (doc && doc.addEventListener) {
    doc.addEventListener('arcademy-sfx', onSfx);
    doc.addEventListener('pointerdown', onGesture, true);
    doc.addEventListener('keydown', onGesture, true);
  }
  // Armed: build the graph now rather than on the first cue, because the first
  // cue this exists for is the splash's, and a context spun up inside that beat
  // arrives after it.
  if (gestured) ensureContext();
  const offSetting = (bridge && typeof bridge.on === 'function') ? bridge.on('setting', onSetting) : () => {};
  if (!Ctor) say('[audio] no AudioContext in this host - sfx are inert');

  return {
    onSfx,                             // exported for the harness / a manual cue
    onSetting,
    stopClips: stopAllClips,           // the shell cuts every clip on teardown
    liveClips: () => clips.size,
    /** Is there a REAL file behind this cue name right now? The one honest
     *  answer to "may I use the recording instead of the impression" - the
     *  intro asks before it decides whether to play a bed or to stitch the
     *  beats by hand, and a false here has to mean "stitch", never "be quiet". */
    hasSample: (name) => available.has(String(name || '')),
    /** What a fired name actually resolves to right now (COUNTER STOCK's one
     *  swap). The honest answer to "is the brass bell live" - it folds in the
     *  ownership getter AND whether the file is really there. Test seam, and
     *  the only way anything outside this file may ask. */
    cueFor: (name) => cueFor(name),
    /** Say the player owns (or has stopped owning) the brass bell. The same
     *  fact `setBellCosmetic` carries, scoped to THIS consumer, for a caller
     *  that has the handle in its hand (a suite, mostly). It is asked first, so
     *  a consumer told `true` here ignores the module-level answer; told
     *  `false`, it falls through to it. */
    setBrassBell: (v) => { brass = (typeof v === 'function') ? v : (v === true); },
    stats: () => Object.assign(
      { mute, master, levels: Object.assign({}, levels), gestured, autoplayOk: autoplayOk === true, live: !!ac },
      stats
    ),
    destroy() {
      if (doc && doc.removeEventListener) {
        doc.removeEventListener('arcademy-sfx', onSfx);
        doc.removeEventListener('pointerdown', onGesture, true);
        doc.removeEventListener('keydown', onGesture, true);
      }
      try { offSetting(); } catch { /* ignore */ }
      stopAllClips();
      if (ac && typeof ac.close === 'function') { try { ac.close(); } catch { /* ignore */ } }
      ac = null; out = null; noiseBuf = null;
      // An AudioBuffer belongs to the context that decoded it, so it dies with
      // it; a fresh context decodes afresh.
      buffers.clear(); decoding.clear(); canBuffer = false;
    },
  };
}

export default createAudio;
