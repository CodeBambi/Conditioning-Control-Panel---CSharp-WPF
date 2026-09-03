// emi/chains.js — the canon face sets and the canon chains.
//
// VERBATIM from EMI-DESIGN-LOCK.md (2026-08-23, owner-approved) and the prototype
// scratchpad/mascot4/emoticon-screen.html. These strings and millisecond numbers ARE the
// design. Do not retune them here; a re-tune is an owner call and lands in the lock first.
//
// Talk rule (locked): EMI never mouths words. Words go in the SPEECH BUBBLE — the face stays
// 0_0 while the bubble types `.` `..` `...` and switches to the reaction face when the line
// lands. Wordless yes = NOD. Timer/load = THINKING dots. Noticing the player = GLANCE.

export const FACES = {
  // FLAT (everyday) — 16 ascii + 3 promoted round-eye faces. CUT: OwO, UwU.
  FLAT: ['._.', '^_^', '^_~', '>.<', '@_@', '-_-', 'o_o', 'T_T', '>_<', '=_=', '¬_¬',
    '^___^', 'x_x', '*_*', '0_0', ';_;', '(◉_◉)', '(⊙_⊙)', '(◔_◔)'],
  // KAOMOJI (rare / special) — 11 picked. Render +10% size and +10% lift, in chains too.
  KAO: ['( ͡° ͜ʖ ͡°)', '(¬‿¬)', '(◠‿◠)', '(⌐■_■)', '(ಠ‿ಠ)', '(✖╭╮✖)', '(✿◡‿◡)', '(◕‿◕)',
    '(ಥ_ಥ)', '(｡♥‿♥｡)', '(≧◡≦)'],
  // SIDEWAYS CLASSIC (kept, secondary) — these auto-rotate 90°.
  SIDE: [':)', ':D', ';)', ":'(", '>:(', ':O', ':P', ':|', '<3', 'XD', ':3', '>:)', ':/', 'B)'],
  // SPECIAL / EVENT TEXT (+ any release-name string).
  SPECIAL: ['\\o/', 'GG', '#ERR', 'ZzZ', '!!!', '???', 'LV UP', '6.7', '♥♥♥', '★★★', '404', 'brb']
};

// The prototype hung a body move off each fx kind. Kept, but written out on each chain as an
// explicit `body` so playChain's rule stays exactly "hooks.body once if chain.body".
export const BODY_FOR_FX = { hearts: 'bounce', sparks: 'bounce', tears: 'droop', storm: 'shiver', bang: 'bounce' };

/**
 * THE BODY FRAMES. EMI's body is a PNG and there are six of them now (art/emi/):
 * `celebration` (body.png, arms up - the ceremony pose), `idle` (arms down, the
 * RESTING pose), `sad`, `shock`, `smug` and `pet`. The pairing is DATA and it
 * lives here, one `bodyFrame` per chain, because a chain IS the feeling; the
 * widget owns only the swapping and a by-face fallback for raw holds.
 * A chain with no `bodyFrame` (every `makeSay` line) is resolved FRAME BY FRAME
 * off the face instead, which is what keeps the typing dots at `idle` and lands
 * the pose with the reaction face.
 */
export const BODY_FRAMES = Object.freeze(['celebration', 'idle', 'sad', 'shock', 'smug', 'pet']);

/**
 * CHAINS[id] = { label, seq:[[text, ms, frameOpts?], ...], fx?, body?, flat?, bodyFrame? }
 * frameOpts: {small:true} for the THINKING dots, {bubble:'text'|null} for the speech bubble.
 * `bodyFrame` is one of BODY_FRAMES and is held for the WHOLE chain (a pose that
 * flickered per 90ms glitch frame would read as a broken sprite, not as a mood).
 */
export const CHAINS = {
  wink:     { label: 'WINK',          bodyFrame: 'idle',        seq: [['^_^', 420], ['^_~', 260], ['^_^', 600]] },
  blink:    { label: 'BLINK (idle)',  bodyFrame: 'idle',        seq: [['0_0', 1400], ['-_-', 110], ['0_0', 1200]] },
  wake:     { label: 'WAKE UP',       bodyFrame: 'shock',       seq: [['-_-', 500], ['o_o', 220], ['0_0', 260], ['(⊙_⊙)', 700]] },
  shock:    { label: 'SHOCK',         bodyFrame: 'shock',       seq: [['o_o', 160], ['0_0', 160], ['(◉_◉)', 900]], fx: 'bang', body: 'bounce' },
  sus:      { label: 'SUSPICIOUS',    bodyFrame: 'smug',        seq: [['¬_¬', 500], ['-_-', 220], ['¬_¬', 800]] },
  thinking: { label: 'THINKING',      bodyFrame: 'idle',        seq: [['.', 260, { small: true }], ['..', 260, { small: true }],
                                            ['...', 260, { small: true }], ['.', 260, { small: true }],
                                            ['..', 260, { small: true }], ['...', 420, { small: true }],
                                            ['0_0', 900]], flat: true },
  glance:   { label: 'GLANCE',        bodyFrame: 'idle',        seq: [['0_0', 300], ['o_o', 640], ['0_0', 900]] },
  nod:      { label: 'NOD',           bodyFrame: 'idle',        seq: [['0_0', 1400]], body: 'nod' },
  say:      { label: 'SAY (bubble)',  bodyFrame: 'idle',        seq: [['0_0', 420, { bubble: '.' }], ['0_0', 420, { bubble: '..' }],
                                            ['0_0', 520, { bubble: '...' }], ['^_^', 1800, { bubble: 'nice one!' }],
                                            ['0_0', 200, { bubble: null }]] },
  sayNod:   { label: 'SAY + NOD',     bodyFrame: 'idle',        seq: [['0_0', 420, { bubble: '.' }], ['0_0', 420, { bubble: '..' }],
                                            ['0_0', 520, { bubble: '...' }], ['0_0', 1800, { bubble: 'again?' }],
                                            ['0_0', 200, { bubble: null }]], body: 'nod' },
  cry:      { label: 'CRY',           bodyFrame: 'sad',         seq: [[';_;', 500], ['T_T', 1400]], fx: 'tears', body: 'droop' },
  rage:     { label: 'RAGE',          bodyFrame: 'shock',       seq: [['>.<', 200], ['>_<', 200], ['>.<', 200], ['>_<', 700]], fx: 'storm', body: 'shiver' },
  reveal:   { label: 'EVENT REVEAL',  bodyFrame: 'celebration', seq: [['._.', 420], ['0_0', 1200]], fx: 'sparks', body: 'bounce' },
  glitch:   { label: 'GLITCH',        bodyFrame: 'shock',       seq: [['x_x', 90], ['#ERR', 90], ['@_@', 90], ['#ERR', 90], ['x_x', 90], ['0_0', 600]] },
  love:     { label: 'LOVESTRUCK',    bodyFrame: 'pet',         seq: [['0_0', 260], ['*_*', 420], ['(｡♥‿♥｡)', 1400]], fx: 'hearts', body: 'bounce' },
  /* GLEE is the lock's "three pets in a row" and "a streak stamp lands" beat: the
   * squeezed-eye kaomoji, not the lovestruck one. Short run-up so it can ride a
   * ceremony without holding the screen. */
  glee:     { label: 'GLEE',          bodyFrame: 'celebration', seq: [['^_^', 300], ['(≧◡≦)', 1400]], fx: 'hearts', body: 'bounce' },
  cool:     { label: 'COOL',          bodyFrame: 'celebration', seq: [['-_-', 300], ['(⌐■_■)', 1400]], fx: 'sparks', body: 'bounce' },
  dizzy:    { label: 'DIZZY',         bodyFrame: 'idle',        seq: [['@_@', 260], ['=_=', 260], ['@_@', 260], ['x_x', 800]] },
  smug:     { label: 'SMUG',          bodyFrame: 'smug',        seq: [['^_^', 300], ['(¬‿¬)', 1200]] },
  ko:       { label: 'K.O.',          bodyFrame: 'sad',         seq: [['>_<', 220], ['x_x', 420], ['(✖╭╮✖)', 1400]], fx: 'tears', body: 'droop' }
};

/**
 * makeSay(line, reactionFace='^_^', holdMs=1800) -> a one-off SAY chain.
 * The `say`/`sayNod` entries above are templates for the tester; real speech is built here so
 * the caller's line rides the same locked . / .. / ... cadence.
 */
export function makeSay(line, reactionFace = '^_^', holdMs = 1800) {
  return {
    label: 'SAY',
    seq: [
      ['0_0', 420, { bubble: '.' }],
      ['0_0', 420, { bubble: '..' }],
      ['0_0', 520, { bubble: '...' }],
      [reactionFace, Math.max(400, holdMs | 0), { bubble: String(line == null ? '' : line) }],
      ['0_0', 200, { bubble: null }]
    ]
  };
}

/**
 * playChain(chain, hooks) -> { cancel() }
 * hooks: { draw(text, frameOpts), bubble(textOrNull), body(cls), fx(kind), done() } — all optional.
 * Drives its own setTimeout ladder. fx fires once at frame 0 (if chain.fx); body fires once at
 * frame 0 (if chain.body). `done()` fires after the LAST frame's duration has elapsed.
 * cancel() kills the pending timer and does NOT call done().
 */
export function playChain(chain, hooks = {}) {
  const noop = () => {};
  const draw = hooks.draw || noop;
  const bubble = hooks.bubble || noop;
  const body = hooks.body || noop;
  const fx = hooks.fx || noop;
  const done = hooks.done || noop;

  const seq = (chain && Array.isArray(chain.seq)) ? chain.seq : [];
  let timer = null;
  let cancelled = false;

  const cancel = () => {
    cancelled = true;
    if (timer !== null) { clearTimeout(timer); timer = null; }
  };
  if (!seq.length) { done(); return { cancel }; }

  bubble(null);                        // a new chain always starts from a clean bubble
  if (chain.body) body(chain.body);

  let i = 0;
  const step = () => {
    if (cancelled) return;
    const frame = seq[i];
    const text = frame[0], ms = frame[1], o = frame[2];
    if (o && 'bubble' in o) bubble(o.bubble);
    draw(text, { small: !!(o && o.small), flat: !!chain.flat });
    if (i === 0 && chain.fx) fx(chain.fx);
    i++;
    timer = setTimeout(() => {
      timer = null;
      if (cancelled) return;
      if (i < seq.length) step();
      else done();
    }, ms);
  };
  step();
  return { cancel };
}

export default { FACES, CHAINS, playChain, makeSay, BODY_FOR_FX };
