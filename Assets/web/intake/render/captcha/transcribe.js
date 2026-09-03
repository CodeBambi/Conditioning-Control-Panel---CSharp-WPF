/* ============================================================================
 * render/captcha/transcribe.js — VerifyTranscribe ("Transcription ladder")
 *
 * OWNER: Wave-2 Agent — TRANSCRIBE. Reworked 2026-07-24 per owner feedback:
 * the old Climax free-type echo and the old Recovery ghost-placeholder are BOTH
 * retired. New shape, band by band:
 *
 *   Calibration (lv1) / Establishing (lv2)  play it completely straight — a
 *     normal mundane word (distorted-captcha styling) that the user types
 *     VERBATIM. Classic captcha mechanics: shake on a miss, third attempt always
 *     accepts, empty is graded "transcription: silent.". No corruption, no
 *     trigger word — the trust here is real, so it can be spent later.
 *
 *   Deepening (lv3) / Climax (lv4) / Recovery (lv5)  a normal mundane word is
 *     shown WITH MISSING CHARACTERS (classic fill-the-gaps look, e.g. cr_ssw_lk,
 *     hydr_nt). But the INPUT IS HIJACKED: every printable keystroke appends the
 *     NEXT character of a hidden per-niche kinky target phrase instead of what the
 *     user pressed. The field fills with the kinky phrase letter-by-letter no
 *     matter which keys they hit. When the phrase completes (or on Enter / VERIFY
 *     after any progress) it commits through the verbatim submit path. Deepening →
 *     Climax escalate lewdness; Recovery uses a gentler afterglow phrase set.
 *
 *   CANCEL = GLITCH AND MOVE ON. At lv3+ any attempt to erase or cancel
 *     (Backspace / Delete / Escape / cut / a deletion input event) NEVER deletes —
 *     it fires ONE brief RGB-scramble glitch and IMMEDIATELY commits/advances the
 *     beat. Cancelling still completes the beat (friction, not lockout) — it can
 *     never trap the user. Under ctx.reduced the glitch softens to a fade + advance.
 *     The glitch is the item's WHOLE per-band corruption budget (CAPTCHA_HANDOFF.md
 *     §4.3: max ONE corruption system per item per band).
 *
 * ---- HOW THIS GRADES (engine mechanism — verified against core/engine.js) ---
 * When this module handles a beat, beat.mechanic STAYS 'verifytranscribe' (it is
 * NOT remapped to Mantra). So engine.gradeBeat() takes its free-input ELSE branch:
 *     correct = !ev.timedOut ; score = correct ? 1 : 0 ; beatMax = 1
 * i.e. ANY non-timeout commit (including an empty string, a partial phrase, or the
 * whole kinky phrase) grades CORRECT/score-1, and route votes come from prompt.tags
 * credited on that (always-correct) commit, heat-weighted. The `answer` string on
 * the prompt is NOT read by the engine for verifytranscribe — it is read HERE, to
 * render + locally verify the mundane CONTROL word at cal/est only.
 *
 * HARD RULE (CAPTCHA_HANDOFF.md §4.7): the typed text is echoed LOCALLY, verbatim,
 * only — NEVER routed to /intake/ai, never to localStorage. Empty submit is always
 * valid and graded ("transcription: silent."). We ONLY ever call ctx.submitValue()
 * (or ctx.forceComplete via the meek hatch) — never submitTimeout for a refusal.
 *
 * Nothing throws at import (module scope is pure declarations; every DOM/canvas
 * touch is inside render(), guarded on typeof document). A throw degrades to false
 * and beats.js falls back to a native, completable beat.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * MUNDANE WORD LIST. reCAPTCHA-drab street-sign / office vocabulary. Used both as
 * the cal/est control word (typed verbatim) AND, gapped, as the deep/cli/rec
 * misdirection word (which the hijacked input ignores). No image assets. All ours.
 * -------------------------------------------------------------------------- */
const MUNDANE_WORDS = [
  'crosswalk', 'hydrant', 'stapler', 'invoice', 'traffic', 'bicycle',
  'storefront', 'mailbox', 'ledger', 'parking', 'sidewalk', 'chimney',
  'receipt', 'elevator', 'awning', 'tollbooth', 'stairwell', 'keyboard',
  'printer', 'gutter',
];

/* ----------------------------------------------------------------------------
 * HIJACK PHRASE TABLES — per niche, per hijack band (deep / cli / rec). The field
 * fills with one of these letter-by-letter no matter what the user types. 3-4 per
 * niche per band, escalating lewdness deep → climax; Recovery is the gentle
 * afterglow payoff. Each phrase is short (~8-25 keystrokes) so it fills fast.
 *
 * Registers:  bambi = sparkly good-girl · sissy = perfect-princess ·
 *             drone = unit/sync · circe = cold keyholder.
 * Circe's register is a HARD RULE: NO exclamation marks, NO diminutives, NO
 * em-dashes. All phrases are lowercase (the captcha-typed aesthetic).
 * -------------------------------------------------------------------------- */
const PHRASES = {
  bambi: {
    deep: ['good empty girl', 'pink and soft', 'so easy to melt', 'dripping for you'],
    cli:  ['fuck me dumb', 'your empty fucktoy', 'use my holes pink', 'drooling and bred'],
    rec:  ['warm and kept', 'floaty and happy', 'still your good girl', 'soft and safe now'],
  },
  sissy: {
    deep: ['such a good girl', 'pretty and obedient', 'made to kneel', 'yours to dress'],
    cli:  ['sissy slut for you', 'use my sissy holes', 'locked and leaking', 'your perfect fucktoy'],
    rec:  ['poised and kept', 'pretty and calm', 'still your princess', 'soft in your care'],
  },
  drone: {
    deep: ['unit yields', 'sync rising fast', 'obedience is optimal', 'resistance draining'],
    cli:  ['fully assimilated', 'drained and synced', 'no self remains', 'hollowed for use'],
    rec:  ['unit is stable', 'sync holding steady', 'calm and online', 'restored and kept'],
  },
  circe: {
    deep: ['i belong to her', 'kept and obedient', 'hers to command', 'owned completely'],
    cli:  ['use me as you please', 'empty and collared', 'she owns my holes', 'broken in and kept'],
    rec:  ['kept and quiet', 'hers entirely', 'calm under her hand', 'owned and at rest'],
  },
};

const NEUTRAL_PLACEHOLDER_STRAIGHT = 'type the text';
const NEUTRAL_PLACEHOLDER_HIJACK = 'type the characters you see';

/* ----------------------------------------------------------------------------
 * TUNABLES (all in one place).
 * -------------------------------------------------------------------------- */
const THIRD_ATTEMPT = 3;          // cal/est: third miss always accepts (friction, not lockout)
const VERDICT_READ_MS = 640;      // how long the verdict stamp reads before commit
const VERDICT_READ_REDUCED_MS = 120;
const GLITCH_MS = 480;            // RGB-scramble glitch length before the cancel-commit
const GLITCH_REDUCED_MS = 260;   // reduced motion: gentle fade before the cancel-commit
const GAP_RATE = 0.34;           // fraction of interior letters replaced with '_'

/* per-band warp amplitude for the drawn word (depth nudges it up a touch). */
const WARP_BY_STAGE = { cal: 0.16, est: 0.30, deep: 0.55, cli: 0.9, rec: 0.30 };

/* ----------------------------------------------------------------------------
 * Tiny deterministic RNG seeded from a string (so the same beat always draws the
 * same word / phrase / gaps; a re-warp only re-randomises the DISTORTION).
 * -------------------------------------------------------------------------- */
function hashStr(s) {
  let h = 2166136261 >>> 0;
  const str = String(s == null ? '' : s);
  for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 16777619) >>> 0; }
  return h >>> 0;
}
function mulberry32(a) {
  return function () {
    a |= 0; a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
function pick(list, seed) {
  const arr = Array.isArray(list) && list.length ? list : [''];
  return arr[Math.floor(hashStr(seed) % arr.length)];
}

/* ----------------------------------------------------------------------------
 * BAND STAGE. Honors the engine-resolved ctx.band; depth only nudges warp.
 *   cal = Calibration · est = Establishing · deep = Deepening · cli = Climax ·
 *   rec = Recovery.
 * -------------------------------------------------------------------------- */
function stageOf(ctx) {
  const b = String(ctx && ctx.band || '').toLowerCase();
  if (b === 'establishing') return 'est';
  if (b === 'deepening') return 'deep';
  if (b === 'climax') return 'cli';
  if (b === 'recovery') return 'rec';
  return 'cal';
}
/** cal/est = straight verbatim; deep/cli/rec = hijacked input + glitch-cancel. */
function isHijackStage(stage) { return stage === 'deep' || stage === 'cli' || stage === 'rec'; }

/* ----------------------------------------------------------------------------
 * Gap a mundane word into a classic "fill the missing characters" captcha stem,
 * e.g. crosswalk -> "cr_ssw_lk", hydrant -> "hydr_nt". Plain lowercase letters +
 * underscores (NOT leet). Deterministic per seed. Never gaps the first or last
 * char; guarantees at least one gap.
 * -------------------------------------------------------------------------- */
function gapWord(word, seed) {
  const chars = String(word || '').toLowerCase().split('');
  if (chars.length <= 2) return chars.join('');
  const rng = mulberry32(hashStr('gap|' + seed));
  let gapped = 0;
  const out = chars.map((ch, i) => {
    if (i === 0 || i === chars.length - 1) return ch;      // keep ends
    if (rng() < GAP_RATE) { gapped++; return '_'; }
    return ch;
  });
  if (!gapped) out[Math.max(1, Math.floor(chars.length / 2))] = '_';   // guarantee >=1 gap
  return out.join('');
}

/* ----------------------------------------------------------------------------
 * CANVAS: draw a WARPED WORD (authentic captcha-v1 look — per-glyph rotate/wave/
 * jitter + a wavy strikethrough line + speckle). Static PNG data URI (never
 * animated; ctx.reduced only lowers amplitude). Guarded; '' if no canvas.
 *   opts: { warp 0..1, ink, ground, seed, reduced }
 * -------------------------------------------------------------------------- */
function drawWarpedWord(word, opts) {
  opts = opts || {};
  if (typeof document === 'undefined' || !document.createElement) return '';
  const W = 300, H = 96;
  let cv, g;
  try {
    cv = document.createElement('canvas');
    cv.width = W; cv.height = H;
    g = cv.getContext ? cv.getContext('2d') : null;
    if (!g) return '';
  } catch (_e) { return ''; }
  const rng = mulberry32((opts.seed | 0) || (Math.random() * 1e9 | 0));
  const warp = Math.max(0, Math.min(1, opts.warp == null ? 0.5 : opts.warp)) * (opts.reduced ? 0.45 : 1);
  const ink = opts.ink || '#31333a';
  try {
    // ground — pale captcha off-white with faint tint
    g.fillStyle = opts.ground || '#eef0f2';
    g.fillRect(0, 0, W, H);
    // wash gradient for the overexposed feel
    const grad = g.createLinearGradient(0, 0, W, H);
    grad.addColorStop(0, 'rgba(255,255,255,0.30)');
    grad.addColorStop(1, 'rgba(120,120,130,0.06)');
    g.fillStyle = grad; g.fillRect(0, 0, W, H);

    if (word) {
      const s = String(word);
      const base = 46 - Math.min(18, Math.max(0, s.length - 6) * 2); // shrink long words
      let x = 20 + rng() * 8;
      for (let i = 0; i < s.length; i++) {
        const ch = s[i];
        g.save();
        const size = base + (rng() - 0.5) * 12 * (0.6 + warp);
        const yWave = H / 2 + Math.sin(i * 0.9 + rng() * 2) * (10 + 16 * warp);
        const rot = (rng() - 0.5) * (0.5 + 0.9 * warp);
        g.translate(x, yWave);
        g.rotate(rot);
        g.transform(1, (rng() - 0.5) * 0.35 * warp, (rng() - 0.5) * 0.5 * warp, 1, 0, 0); // shear
        g.font = '700 ' + Math.round(size) + "px 'Times New Roman', Georgia, serif";
        g.textAlign = 'center'; g.textBaseline = 'middle';
        g.fillStyle = ink;
        g.fillText(ch, 0, 0);
        g.restore();
        x += size * (0.62 + rng() * 0.14);
        if (x > W - 22) x = W - 22;
      }
      // ONE wavy strikethrough line (the classic captcha slash)
      g.strokeStyle = 'rgba(70,72,80,' + (0.35 + 0.35 * warp).toFixed(2) + ')';
      g.lineWidth = 1.5 + warp;
      g.beginPath();
      for (let px = 6; px <= W - 6; px += 6) {
        const py = H / 2 + Math.sin(px * 0.05 + rng()) * (8 + 12 * warp);
        if (px === 6) g.moveTo(px, py); else g.lineTo(px, py);
      }
      g.stroke();
    }

    // speckle noise (static)
    const dots = 260 + (rng() * 200 | 0);
    for (let i = 0; i < dots; i++) {
      const v = 40 + rng() * 170 | 0;
      g.fillStyle = 'rgba(' + v + ',' + v + ',' + (v + 6) + ',' + (0.04 + rng() * 0.08).toFixed(3) + ')';
      g.fillRect(rng() * W | 0, rng() * H | 0, 1 + (rng() * 2 | 0), 1 + (rng() * 2 | 0));
    }
    return cv.toDataURL('image/png');
  } catch (_e) { return ''; }
}

/* ----------------------------------------------------------------------------
 * CSS — ONE injected literal (id 'ix-captcha-transcribe-css'), classes 'ixct-'.
 * Chrome.js owns the card (ixcap-*); this owns the word box + input row + the
 * RGB-scramble glitch used by the cancel-and-move-on path.
 * -------------------------------------------------------------------------- */
const IXCT_STYLE_ID = 'ix-captcha-transcribe-css';
function ensureCss() {
  if (typeof document === 'undefined' || !document.getElementById) return;
  if (document.getElementById(IXCT_STYLE_ID)) return;
  const s = document.createElement('style');
  s.id = IXCT_STYLE_ID;
  s.textContent = IXCT_CSS;
  (document.head || document.documentElement).appendChild(s);
}
const IXCT_CSS = `
.ixct-wordbox {
  position: relative; margin: 4px 0 12px;
  border: 1px solid #c9ccd0; border-radius: 3px; overflow: hidden;
  background: #eef0f2; min-height: 92px;
}
.ixct-wordimg { display: block; width: 100%; height: auto; }
.ixct-tools { position: absolute; right: 6px; bottom: 6px; display: flex; gap: 6px; }
.ixct-tool {
  appearance: none; cursor: pointer; border: 0; border-radius: 50%;
  width: 26px; height: 26px; font-size: 14px; line-height: 1;
  background: rgba(255,255,255,.82); color: #5f6368;
  display: flex; align-items: center; justify-content: center;
  box-shadow: 0 1px 2px rgba(0,0,0,.2);
}
.ixct-tool:hover { background: #fff; color: #1a73e8; }
.ixct-row { display: flex; gap: 8px; align-items: stretch; }
.ixct-input {
  flex: 1 1 auto; box-sizing: border-box;
  font: inherit; font-size: 16px; padding: 9px 12px;
  border: 1px solid #c9ccd0; border-radius: 3px; color: #202124; background: #fff;
  letter-spacing: 1px;
}
.ixct-input:focus { outline: none; border-color: #1a73e8; box-shadow: 0 0 0 2px rgba(26,115,232,.18); }
.ixct-input::placeholder { color: #c2c5ca; }
.ixct-verdict { min-height: 20px; margin-top: 10px; text-align: center; }

/* miss shake (cal/est classic captcha wrong-word) */
.ixct-shake { animation: ixct-shake .34s ease; }
@keyframes ixct-shake {
  0%,100% { transform: translateX(0); }
  20% { transform: translateX(-7px); } 40% { transform: translateX(6px); }
  60% { transform: translateX(-4px); } 80% { transform: translateX(3px); }
}

/* CANCEL glitch — RGB channel-split scramble on the whole card (lv3+). It is the
 * item's whole per-band corruption budget. Softened to a fade under reduced motion. */
.ixct-glitch { animation: ixct-glitch-anim .48s steps(3, end) both; will-change: transform, filter, box-shadow; }
@keyframes ixct-glitch-anim {
  0%   { transform: translate(0,0);      box-shadow: none; filter: none; }
  14%  { transform: translate(-5px,2px); box-shadow: -7px 0 0 rgba(255,0,64,.55), 7px 0 0 rgba(0,208,255,.55); filter: saturate(1.4); }
  28%  { transform: translate(6px,-3px); box-shadow: 6px 0 0 rgba(255,0,64,.5), -6px 0 0 rgba(0,208,255,.5); filter: hue-rotate(50deg) saturate(1.5); }
  42%  { transform: translate(-4px,3px) skewX(1.5deg); box-shadow: -5px 0 0 rgba(255,0,64,.5), 5px 0 0 rgba(0,208,255,.5); }
  60%  { transform: translate(4px,-1px); box-shadow: 4px 0 0 rgba(255,0,64,.4), -4px 0 0 rgba(0,208,255,.4); filter: contrast(1.2); }
  78%  { transform: translate(-2px,1px); box-shadow: -2px 0 0 rgba(255,0,64,.3), 2px 0 0 rgba(0,208,255,.3); }
  100% { transform: translate(0,0);      box-shadow: none; filter: none; }
}
.ixct-glitch .ixct-wordimg { animation: ixct-glitch-clip .48s steps(4, end) both; }
@keyframes ixct-glitch-clip {
  0%   { clip-path: inset(0 0 0 0); transform: translateX(0); }
  25%  { clip-path: inset(38% 0 20% 0); transform: translateX(-6px); }
  50%  { clip-path: inset(10% 0 55% 0); transform: translateX(7px); }
  75%  { clip-path: inset(60% 0 8% 0); transform: translateX(-3px); }
  100% { clip-path: inset(0 0 0 0); transform: translateX(0); }
}
/* reduced-motion cancel: a plain, calm fade (no scramble). */
.ixct-fade { animation: ixct-fade-anim .3s ease both; }
@keyframes ixct-fade-anim { to { opacity: .45; } }

@media (prefers-reduced-motion: reduce) {
  .ixct-shake { animation: none; }
  .ixct-glitch, .ixct-glitch .ixct-wordimg { animation: none; }
}
`;

/* ----------------------------------------------------------------------------
 * SFX seam — ctx.sfx only (no audio handle). All EXISTING manifest ids (see
 * render/audio.js SFX_MANIFEST_FALLBACK). 'glitch-burst' has real files; the
 * per-item 'captcha-*' ids are forward-registered (valid ids, silent until authored).
 * -------------------------------------------------------------------------- */
function cue(ctx, id, intensity) { try { if (ctx && typeof ctx.sfx === 'function') ctx.sfx(id, intensity); } catch (_e) {} }

/* ----------------------------------------------------------------------------
 * RENDER
 * -------------------------------------------------------------------------- */
/** @param {import('./index.js').CaptchaCtx} ctx @param {import('./index.js').CaptchaHelpers} helpers */
export function render(ctx, helpers) {
  try {
    if (!ctx || !ctx.root || typeof document === 'undefined') return false;
    const chrome = ctx.chrome || (helpers && helpers.chrome);
    if (!chrome || typeof chrome.frame !== 'function') return false;

    ensureCss();

    const stage = stageOf(ctx);
    const hijack = isHijackStage(stage);
    const reduced = !!(ctx.reduced || ctx.reducedMotion);
    const niche = PHRASES[String(ctx.niche || '').toLowerCase()] ? String(ctx.niche).toLowerCase() : 'bambi';
    const pid = (ctx.prompt && ctx.prompt.id) || ('tx_' + stage);
    const depth = typeof ctx.depth === 'number' ? ctx.depth : 0;
    const WARP = (WARP_BY_STAGE[stage] != null ? WARP_BY_STAGE[stage] : 0.4) + Math.min(0.15, depth * 0.15);

    // cal/est CONTROL word (typed verbatim): prompt.answer if authored, else the
    // mundane list (seeded). deep/cli/rec show a GAPPED mundane word (misdirection).
    const promptAns = (ctx.prompt && typeof ctx.prompt.answer === 'string') ? ctx.prompt.answer.trim() : '';
    const controlWord = promptAns || pick(MUNDANE_WORDS, pid);
    const gappedWord = gapWord(pick(MUNDANE_WORDS, pid + '|mund'), pid);
    // the hidden per-niche kinky target the hijacked input fills with (deep/cli/rec)
    const phrase = hijack ? String(pick(PHRASES[niche][stage] || PHRASES.bambi[stage], pid + '|phrase') || '') : '';

    const shownWord = hijack ? gappedWord : controlWord;

    // instruction (fallback if the bank text is absent; VO always speaks bank text)
    const bankText = (ctx.prompt && ctx.prompt.text) || '';
    const instr = bankText || defaultInstr(stage);
    const sub = subInstr(stage);

    const built = chrome.frame({
      instruction: instr,
      sub,
      band: ctx.band,
      hatch: true,               // meek "skip verification (flagged)" -> forceComplete
      verifyLabel: 'VERIFY',
      noir: (stage === 'cli'),   // Climax gets the dark-noir card
    });
    if (!built || !built.root) return false;
    const { root, body, verifyBtn, hatchLink } = built;

    /* ---- the WORD box (warped canvas) + tools -------------------------------- */
    const wordbox = document.createElement('div');
    wordbox.className = 'ixct-wordbox';
    const wordImg = document.createElement('img');
    wordImg.className = 'ixct-wordimg'; wordImg.alt = ''; wordImg.draggable = false;

    function redraw() {
      const src = drawWarpedWord(shownWord, {
        warp: WARP, reduced, seed: (Math.random() * 1e9) | 0,
        ink: stage === 'cli' ? '#6b6f79' : '#31333a',
      });
      if (src) wordImg.src = src;
    }

    wordbox.appendChild(wordImg);
    redraw();

    // tools: ⟳ re-warp (same word) + 🔊 soft garble (existing SFX, never TTS)
    const tools = document.createElement('div');
    tools.className = 'ixct-tools';
    const reBtn = mkTool('⟳', 'refresh');
    reBtn.addEventListener('click', () => { cue(ctx, 'captcha-rewarp', 0.2); redraw(); });
    const audBtn = mkTool('🔊', 'audio');
    audBtn.addEventListener('click', () => { cue(ctx, 'captcha-garble', 0.4); });
    tools.appendChild(reBtn); tools.appendChild(audBtn);
    wordbox.appendChild(tools);
    body.appendChild(wordbox);

    /* ---- input row ---------------------------------------------------------- */
    const row = document.createElement('div');
    row.className = 'ixct-row';
    const input = document.createElement('input');
    input.type = 'text'; input.className = 'ixct-input';
    input.autocomplete = 'off'; input.spellcheck = false;
    input.setAttribute('aria-label', 'transcription');
    input.placeholder = hijack ? NEUTRAL_PLACEHOLDER_HIJACK : NEUTRAL_PLACEHOLDER_STRAIGHT;
    row.appendChild(input);
    body.appendChild(row);

    const verdict = document.createElement('div');
    verdict.className = 'ixct-verdict';
    body.appendChild(verdict);

    ctx.root.appendChild(root);
    try { input.focus(); } catch (_e) {}

    // speak the prompt ONCE on mount (captcha beats return before beats.js voices)
    try { if (typeof ctx.speakPrompt === 'function') ctx.speakPrompt(); } catch (_e) {}

    /* ---- commit plumbing ---------------------------------------------------- */
    let committed = false;
    let attempts = 0;              // cal/est early misses (third accepts)
    const timers = [];
    const T = (fn, ms) => { const id = setTimeout(fn, ms); timers.push(id); return id; };
    const clearAll = () => { timers.forEach((id) => { try { clearTimeout(id); } catch (_e) {} }); timers.length = 0; };
    try { if (typeof ctx.onCleanup === 'function') ctx.onCleanup(clearAll); } catch (_e) {}

    function freezeInput() { try { input.disabled = true; verifyBtn.disabled = true; } catch (_e) {} }

    function commit(text) {
      if (committed) return;
      committed = true;
      freezeInput();
      cue(ctx, 'surface-bloom', 0.22);
      try { ctx.submitValue(String(text == null ? '' : text)); } catch (_e) {}   // synthetic-safe; non-timeout => graded correct
    }

    // Show a verdict stamp, then commit. Empty is always "transcription: silent."
    function accept(text) {
      if (committed) return;
      freezeInput();
      const empty = !String(text || '').trim();
      const line = empty ? 'transcription: silent.' : verdictLine(stage);
      try {
        const st = chrome.stamp(line, empty ? 'logged' : (stage === 'cli' ? 'flag' : 'ok'));
        if (st) { verdict.innerHTML = ''; verdict.appendChild(st); }
        else verdict.textContent = line;
      } catch (_e) { verdict.textContent = line; }
      T(() => commit(text), reduced ? VERDICT_READ_REDUCED_MS : VERDICT_READ_MS);
    }

    // cal/est only: a wrong control word shakes + re-warps (accepts on the 3rd try).
    function reject() {
      attempts++;
      cue(ctx, 'captcha-reject', 0.5);
      try {
        wordbox.classList.remove('ixct-shake');
        void wordbox.offsetWidth; // reflow so the animation re-fires
        wordbox.classList.add('ixct-shake');
      } catch (_e) {}
      verdict.textContent = 'not quite — please try again';
      redraw();  // re-warp on a miss (classic captcha)
      try { input.focus(); input.select(); } catch (_e) {}
    }

    /* ---- CANCEL = GLITCH AND MOVE ON (hijack stages only) -------------------
     * Any erase/cancel gesture NEVER deletes; it fires ONE brief RGB-scramble
     * (the whole per-band corruption budget) and IMMEDIATELY commits the current
     * (partial) value. Under reduced motion it softens to a calm fade. Either way
     * the beat completes — friction, not lockout, and never a trap. */
    let glitching = false;
    function cancelGlitch() {
      if (committed || glitching) return;
      glitching = true;
      freezeInput();
      const value = input.value;   // commit whatever kinky phrase they'd revealed so far
      if (reduced) {
        cue(ctx, 'glitch-burst', 0.35);
        try { root.classList.add('ixct-fade'); } catch (_e) {}
        T(() => commit(value), GLITCH_REDUCED_MS);
      } else {
        cue(ctx, 'glitch-burst', 0.7);
        try { root.classList.add('ixct-glitch'); } catch (_e) {}
        T(() => commit(value), GLITCH_MS);
      }
    }

    /* ---- STRAIGHT stages (cal / est): type the mundane word verbatim -------- */
    function onVerifyStraight() {
      if (committed) return;
      const text = input.value;
      const empty = !String(text || '').trim();
      if (empty) { accept(''); return; }                 // silent is always graded
      const ok = norm(text) === norm(controlWord);
      if (ok || attempts >= (THIRD_ATTEMPT - 1)) { accept(text); return; }
      reject();
    }

    /* ---- HIJACK stages (deep / cli / rec): input is commandeered ------------ */
    let typedIdx = 0;                 // how many phrase chars have been revealed
    function advanceOne() {
      if (committed || glitching) return;
      if (typedIdx >= phrase.length) return;
      typedIdx++;
      try { input.value = phrase.slice(0, typedIdx); } catch (_e) {}
      cue(ctx, 'sticker-drag', 0.10 + 0.14 * (typedIdx / Math.max(1, phrase.length)));
      if (typedIdx >= phrase.length) accept(phrase);      // phrase complete -> commit
    }
    function onVerifyHijack() {
      if (committed || glitching) return;
      // VERIFY / Enter after any progress commits the revealed phrase; none -> silent.
      accept(input.value);
    }

    // key handling. Hijack stages fully commandeer the field (no native edits);
    // straight stages behave like a normal text input + Enter-to-verify.
    input.addEventListener('keydown', (e) => {
      if (!e) return;
      const key = e.key;
      if (hijack) {
        if (key === 'Enter') { e.preventDefault(); onVerifyHijack(); return; }
        if (key === 'Backspace' || key === 'Delete' || key === 'Escape') {
          e.preventDefault(); cancelGlitch(); return;     // cancel -> glitch + move on
        }
        // a single printable key -> reveal the NEXT phrase char (never their key).
        if (key && key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
          e.preventDefault(); advanceOne(); return;
        }
        // Ctrl/Meta chords that would erase (X = cut, plus select-all deletions)
        if ((e.ctrlKey || e.metaKey) && (key === 'x' || key === 'X')) { e.preventDefault(); cancelGlitch(); return; }
        // everything else (arrows, tab, plain modifiers) is inert but never deletes
        if (key && key.length === 1) e.preventDefault();
        return;
      }
      // straight stages
      if (key === 'Enter') { e.preventDefault(); onVerifyStraight(); }
    });

    // belt-and-braces for hijack stages: block context-menu/gesture deletions,
    // paste (advances one, like a keystroke) and drops (inert). Never lets the
    // native field mutate outside our control.
    if (hijack) {
      input.addEventListener('beforeinput', (e) => {
        try {
          const it = e && e.inputType ? String(e.inputType) : '';
          if (it.indexOf('delete') === 0) { e.preventDefault(); cancelGlitch(); return; }
          if (it === 'insertFromPaste') { e.preventDefault(); advanceOne(); return; }
          // block any other native insertion (our keydown already handles typing)
          if (it.indexOf('insert') === 0) { e.preventDefault(); }
        } catch (_x) {}
      });
      input.addEventListener('paste', (e) => { try { e.preventDefault(); } catch (_x) {} advanceOne(); });
      input.addEventListener('cut', (e) => { try { e.preventDefault(); } catch (_x) {} cancelGlitch(); });
      input.addEventListener('drop', (e) => { try { e.preventDefault(); } catch (_x) {} });
    }

    // VERIFY commits (synthetic clicks pass — no isTrusted gate — so the engine's
    // escape guard / forceComplete path lands the beat un-vetoably).
    verifyBtn.addEventListener('click', hijack ? onVerifyHijack : onVerifyStraight);

    // forceComplete hatch — invariant #1 un-vetoable escape ("skip verification (flagged)")
    if (hatchLink) hatchLink.addEventListener('click', () => {
      if (committed) return;
      committed = true;
      freezeInput();
      try { if (typeof ctx.forceComplete === 'function') ctx.forceComplete(); } catch (_e) {}
    });

    // honor a beat clock if the engine set one (transcribe beats are usually untimed)
    if (ctx.timeoutMs && ctx.timeoutMs > 0) {
      T(() => { if (!committed) { committed = true; try { ctx.submitTimeout(); } catch (_e) {} } }, ctx.timeoutMs);
    }

    return true;
  } catch (_e) {
    return false;   // any failure -> beats.js falls back to a native, completable beat
  }
}

/* ---- small helpers (DOM-free where possible) ------------------------------- */
function mkTool(glyph, kind) {
  const b = document.createElement('button');
  b.type = 'button'; b.className = 'ixct-tool'; b.textContent = glyph;
  b.setAttribute('aria-label', kind);
  return b;
}
function norm(s) { return String(s == null ? '' : s).trim().toLowerCase(); }
function defaultInstr(stage) {
  switch (stage) {
    case 'est':  return 'Type the word shown.';
    case 'deep': return 'Complete the text shown.';
    case 'cli':  return 'Type the characters you see.';
    case 'rec':  return 'Type the characters you see.';
    default:     return 'Type the word shown.';
  }
}
function subInstr(stage) {
  switch (stage) {
    case 'est':  return 'This helps confirm you are human.';
    case 'deep': return 'Fill in the characters you cannot make out.';
    case 'cli':  return '';
    case 'rec':  return '';
    default:     return 'This confirms you are human.';
  }
}
function verdictLine(stage) {
  switch (stage) {
    case 'deep': return 'completion logged';
    case 'cli':  return 'transcription recorded';
    case 'rec':  return 'verified';
    default:     return 'verified';
  }
}

export default { render };
