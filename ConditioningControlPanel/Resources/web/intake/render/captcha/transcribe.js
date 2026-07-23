/* ============================================================================
 * render/captcha/transcribe.js — VerifyTranscribe ("Transcription ladder")
 *
 * OWNER: Wave-2 Agent — TRANSCRIBE. Implements CAPTCHA_BRAINSTORM.md SYNTHESIS #2
 * (v1 warped-word captcha -> two-word "unpaid label" -> word-stem completion ->
 * "type the characters you see" over a blank/gif -> Recovery ghost-placeholder).
 * The answer shape is a verbatim string, committed through ctx.submitValue().
 *
 * ---- HOW THIS GRADES (engine mechanism — verified against core/engine.js) ---
 * When this module handles a beat, beat.mechanic STAYS 'verifytranscribe' (it is
 * NOT remapped to Mantra). So engine.gradeBeat() takes its free-input ELSE branch:
 *     correct = !ev.timedOut ; score = correct ? 1 : 0 ; beatMax = 1
 * i.e. ANY non-timeout commit (including an empty string) grades CORRECT/score-1,
 * and route votes come from prompt.tags credited on that (always-correct) commit,
 * heat-weighted. The `answer` string on the prompt is NOT read by the engine for
 * verifytranscribe — it is read HERE, to render + locally verify the control word.
 * That is the honest encoding of "the captcha verifies you": completing it is the
 * endorsement; the typed text rides ev.value for the LOCAL echo only.
 *
 * HARD RULE (CAPTCHA_HANDOFF.md §4.7): the free text is echoed LOCALLY, verbatim,
 * only — NEVER routed to /intake/ai, never to localStorage. Empty submit is always
 * valid and graded ("transcription: silent."). We ONLY ever call ctx.submitValue()
 * (or ctx.forceComplete via the meek hatch) — never submitTimeout for a refusal.
 *
 * Nothing throws at import (module scope is pure declarations; every DOM/canvas
 * touch is inside render(), guarded on typeof document). A throw degrades to false
 * and beats.js falls back to a native, completable beat.
 * ==========================================================================*/

/* ----------------------------------------------------------------------------
 * CROSS-BEAT RUN STATE (Climax free-type -> Recovery ghost placeholder).
 * Module-level, no storage API. Self-resets when ctx.meta.qIndex REGRESSES (a
 * new run in the same page session rewinds the counter). Verbatim text lives
 * here and NOWHERE else — never persisted, never sent anywhere.
 * -------------------------------------------------------------------------- */
let _run = { climaxText: '', lastQIndex: -1 };
function syncRunState(ctx) {
  try {
    const qi = ctx && ctx.meta && typeof ctx.meta.qIndex === 'number' ? ctx.meta.qIndex : null;
    if (qi == null) return;
    if (qi < _run.lastQIndex) _run = { climaxText: '', lastQIndex: qi }; // regressed -> fresh run
    _run.lastQIndex = qi;
  } catch (_e) {}
}

/* ----------------------------------------------------------------------------
 * VOCAB. All ours. Circe register is COLD: no diminutives, no exclamations.
 * Trigger vocab is keyed by niche; the neutral control/code lists are shared.
 * -------------------------------------------------------------------------- */
const NEUTRAL_WORDS = ['garden', 'receipt', 'harbor', 'window', 'letter', 'pillow', 'meadow', 'copper', 'lantern', 'anchor', 'signal', 'bramble'];
const RECOVERY_CODES = ['xR7pq2', 'q4Mv8t', 'kZ3wp9', 'b6Nf2r'];
const TRIGGER_VOCAB = {
  bambi: ['obey', 'bimbo', 'giggle', 'empty', 'dolly', 'pink', 'sink', 'melt'],
  drone: ['comply', 'unit', 'hive', 'sync', 'erase', 'protocol', 'drone', 'optimal'],
  sissy: ['sissy', 'pretty', 'obey', 'expose', 'honest', 'soft', 'bloom', 'yield'],
  circe: ['obey', 'kept', 'property', 'kneel', 'owned', 'yield', 'collar', 'serve'],
};
/** Longer per-niche word used for the Deepening leet-gap stem. */
const STEM_WORD = { bambi: 'obedient', drone: 'compliant', sissy: 'exposure', circe: 'property' };
const NEUTRAL_PLACEHOLDER = 'enter the characters';

/* ----------------------------------------------------------------------------
 * Tiny deterministic RNG seeded from a string (so the same beat always draws
 * the same WORD; a re-warp only re-randomises the DISTORTION, per the fiction).
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
function pick(list, seed) { return list[Math.floor(hashStr(seed) % list.length)]; }

/* ----------------------------------------------------------------------------
 * BAND STAGE. Honors the engine-resolved ctx.band; depth only nudges warp.
 * -------------------------------------------------------------------------- */
function stageOf(ctx) {
  const b = String(ctx && ctx.band || '').toLowerCase();
  if (b === 'establishing') return 'est';
  if (b === 'deepening') return 'deep';
  if (b === 'climax') return 'cli';
  if (b === 'recovery') return 'rec';
  return 'cal';
}

/* ----------------------------------------------------------------------------
 * Leet-gap a word into a "type the illegible letters" stem, e.g. obedient ->
 * "0B_D__NT". Deterministic per seed. Kept letters are leet/upper; gapped
 * letters (the ones the user must GENERATE) become '_'.
 * -------------------------------------------------------------------------- */
const LEET = { a: '4', e: '3', i: '1', o: '0', s: '5', t: '7', b: 'B', g: '9' };
function gapStem(word, seed) {
  const rng = mulberry32(hashStr('gap|' + seed));
  const chars = String(word || '').split('');
  let gapped = 0;
  const out = chars.map((ch, i) => {
    const low = ch.toLowerCase();
    const isVowelish = 'aeiou'.indexOf(low) >= 0;
    // gap ~half the interior letters, biased toward vowels; never the first char
    const gap = i > 0 && (isVowelish ? rng() < 0.72 : rng() < 0.28);
    if (gap) { gapped++; return '_'; }
    return (LEET[low] || ch).toUpperCase();
  });
  if (!gapped && out.length > 2) out[Math.max(1, out.length - 2)] = '_'; // guarantee >=1 gap
  return out.join('');
}

/* ----------------------------------------------------------------------------
 * CANVAS: draw a WARPED WORD (authentic captcha-v1 look — per-glyph rotate/wave/
 * jitter + a wavy strikethrough line + speckle). Static PNG data URI (never
 * animated; ctx.reduced only lowers amplitude). Guarded; '' if no canvas.
 *   opts: { warp 0..1, ink, ground, faint (blank/noise-only for Climax), reduced }
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

    if (!opts.faint && word) {
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

    // speckle noise (static; heavier when faint/blank so the box still reads "captcha")
    const dots = (opts.faint ? 900 : 260) + (rng() * 200 | 0);
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
 * Chrome.js owns the card (ixcap-*); this owns only the word box + input row.
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
.ixct-wordback { position: absolute; inset: 0; width: 100%; height: 100%; object-fit: cover; opacity: .12; pointer-events: none; }
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
.ixct-input.ixct-ghost::placeholder { color: #b9b1c4; font-style: italic; letter-spacing: .5px; }
.ixct-verdict { min-height: 20px; margin-top: 10px; text-align: center; }
.ixct-shake { animation: ixct-shake .34s ease; }
@keyframes ixct-shake {
  0%,100% { transform: translateX(0); }
  20% { transform: translateX(-7px); } 40% { transform: translateX(6px); }
  60% { transform: translateX(-4px); } 80% { transform: translateX(3px); }
}
/* Climax blank box breathes faintly (skipped under reduced motion). */
.ixct-wordbox.ixct-faint { background: #e9eaee; }
.ixct-wordbox.ixct-faint .ixct-wordimg { opacity: .82; }
@media (prefers-reduced-motion: reduce) {
  .ixct-shake { animation: none; }
}
`;

/* ----------------------------------------------------------------------------
 * SFX seam — ctx.sfx only (no audio handle). All existing manifest ids; the
 * richer cues we WANT are listed in sfxWanted of cap_transcribe.json.
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

    syncRunState(ctx);
    ensureCss();

    const stage = stageOf(ctx);
    const reduced = !!(ctx.reduced || ctx.reducedMotion);
    const niche = String(ctx.niche || 'bambi').toLowerCase();
    const vocab = TRIGGER_VOCAB[niche] || TRIGGER_VOCAB.bambi;
    const pid = (ctx.prompt && ctx.prompt.id) || ('tx_' + stage);
    const depth = typeof ctx.depth === 'number' ? ctx.depth : 0;
    // warp grows with the band; depth adds a touch
    const WARP = { cal: 0.16, est: 0.34, deep: 0.55, cli: 0.9, rec: 0.22 }[stage] + Math.min(0.15, depth * 0.15);

    // the CONTROL word: prompt.answer if authored, else neutral list (seeded)
    const promptAns = (ctx.prompt && typeof ctx.prompt.answer === 'string') ? ctx.prompt.answer.trim() : '';
    const controlWord = promptAns || pick(NEUTRAL_WORDS, pid);
    const triggerWord = pick(vocab, pid + '|trig');

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
    wordbox.className = 'ixct-wordbox' + (stage === 'cli' ? ' ixct-faint' : '');
    const wordImg = document.createElement('img');
    wordImg.className = 'ixct-wordimg'; wordImg.alt = ''; wordImg.draggable = false;

    // The visible word depends on the band:
    //  cal  -> the control word (clean-ish)
    //  est  -> control + trigger word, only control is CHECKED
    //  deep -> a leet-gapped niche stem (user generates the illegible letters)
    //  cli  -> BLANK (faint static only); optional dim user gif behind
    //  rec  -> innocuous code
    let shownWord = controlWord;
    let faint = false;
    if (stage === 'est') shownWord = controlWord + '   ' + triggerWord;
    else if (stage === 'deep') shownWord = gapStem(STEM_WORD[niche] || 'obedient', pid);
    else if (stage === 'cli') { shownWord = ''; faint = true; }
    else if (stage === 'rec') shownWord = pick(RECOVERY_CODES, pid);

    function redraw() {
      const src = drawWarpedWord(shownWord, {
        warp: WARP, faint, reduced, seed: (Math.random() * 1e9) | 0,
        ink: stage === 'cli' ? '#6b6f79' : '#31333a',
      });
      if (src) wordImg.src = src;
    }

    // Climax: optionally a dim user gif fills the word box behind the static
    if (stage === 'cli') {
      try {
        const gifs = ctx.media && Array.isArray(ctx.media.gifs) ? ctx.media.gifs
          : (ctx.media && Array.isArray(ctx.media.images) ? ctx.media.images : null);
        if (gifs && gifs.length) {
          const back = document.createElement('img');
          back.className = 'ixct-wordback'; back.alt = ''; back.draggable = false;
          back.src = String(gifs[hashStr(pid) % gifs.length]);
          wordbox.appendChild(back);
        }
      } catch (_e) {}
    }

    wordbox.appendChild(wordImg);
    redraw();

    // tools: ⟳ re-warp (same word) + 🔊 soft garble (existing SFX, never TTS)
    const tools = document.createElement('div');
    tools.className = 'ixct-tools';
    const reBtn = mkTool('⟳', 'refresh');
    reBtn.addEventListener('click', () => { cue(ctx, 'loom-spiral-up', 0.2); redraw(); });
    const audBtn = mkTool('🔊', 'audio');
    audBtn.addEventListener('click', () => { cue(ctx, 'drain-wash', 0.35); });
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
    // Recovery: placeholder is their Climax free-type in ghost-grey (or neutral)
    if (stage === 'rec') {
      const ghost = _run.climaxText && _run.climaxText.trim();
      input.placeholder = ghost || NEUTRAL_PLACEHOLDER;
      if (ghost) input.classList.add('ixct-ghost');
    } else if (stage === 'cli') {
      input.placeholder = 'type the characters you see';
    } else {
      input.placeholder = 'type the text';
    }
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
    let attempts = 0;
    function commit(text) {
      if (committed) return;
      committed = true;
      try { input.disabled = true; verifyBtn.disabled = true; } catch (_e) {}
      // store the Climax verbatim ONLY in module runState (never persisted/sent)
      if (stage === 'cli') _run.climaxText = String(text == null ? '' : text);
      // LOCAL echo (Climax): re-render the typed text warped, as if it were the prompt
      if (stage === 'cli' && text) {
        shownWord = String(text); faint = false;
        wordbox.classList.remove('ixct-faint');
        redraw();
      }
      cue(ctx, 'surface-bloom', 0.22);
      try { ctx.submitValue(String(text == null ? '' : text)); } catch (_e) {}
    }

    function accept(text) {
      const empty = !String(text || '').trim();
      const line = empty ? 'transcription: silent.' : verdictLine(stage);
      try {
        const st = chrome.stamp(line, empty ? 'logged' : (stage === 'cli' ? 'flag' : 'ok'));
        if (st) { verdict.innerHTML = ''; verdict.appendChild(st); }
        else verdict.textContent = line;
      } catch (_e) { verdict.textContent = line; }
      // let the verdict read for a beat, then commit
      setTimeout(() => commit(text), reduced ? 120 : 640);
    }

    function reject() {
      attempts++;
      cue(ctx, 'incorrect-feedback', 0.5);
      try {
        wordbox.classList.remove('ixct-shake');
        void wordbox.offsetWidth; // reflow so the animation re-fires
        wordbox.classList.add('ixct-shake');
      } catch (_e) {}
      verdict.textContent = 'not quite — please try again';
      redraw();  // re-warp on a miss (classic captcha)
      try { input.focus(); input.select(); } catch (_e) {}
    }

    function onVerify() {
      if (committed) return;
      const text = input.value;
      const empty = !String(text || '').trim();
      // Empty is ALWAYS valid + graded ("transcription: silent.") at EVERY band.
      if (empty) { accept(''); return; }
      // Bands where only the CONTROL word is checked (cal/est). deep is near-miss
      // tolerant; cli accepts anything; rec accepts anything.
      if (stage === 'cal' || stage === 'est') {
        const ok = norm(text) === norm(controlWord) || (stage === 'est' && controlIn(text, controlWord));
        // Friction, not lockout: shake twice, accept on the 3rd attempt (the classic).
        if (ok || attempts >= 2) { accept(text); return; }
        reject(); return;
      }
      // deep / cli / rec: accept as typed
      accept(text);
    }

    verifyBtn.addEventListener('click', onVerify);  // synthetic clicks pass (no isTrusted gate)
    input.addEventListener('keydown', (e) => { if (e && e.key === 'Enter') { e.preventDefault(); onVerify(); } });

    // forceComplete hatch — invariant #1 un-vetoable escape ("skip verification (flagged)")
    if (hatchLink) hatchLink.addEventListener('click', () => {
      if (committed) return;
      committed = true;
      try { input.disabled = true; verifyBtn.disabled = true; } catch (_e) {}
      if (stage === 'cli') _run.climaxText = String(input.value || '');
      try { if (typeof ctx.forceComplete === 'function') ctx.forceComplete(); } catch (_e) {}
    });

    // honor a beat clock if the engine set one (transcribe beats are usually untimed)
    if (ctx.timeoutMs && ctx.timeoutMs > 0) {
      const t = setTimeout(() => { if (!committed) { try { ctx.submitTimeout(); } catch (_e) {} committed = true; } }, ctx.timeoutMs);
      try { if (typeof ctx.onCleanup === 'function') ctx.onCleanup(() => clearTimeout(t)); } catch (_e) {}
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
function controlIn(typed, control) {
  // Establishing: the field may hold either or both words; accept if the control
  // token is present anywhere (only the control word is ever checked).
  const toks = norm(typed).split(/\s+/);
  return toks.indexOf(norm(control)) >= 0;
}
function defaultInstr(stage) {
  switch (stage) {
    case 'est':  return 'Type the words shown.';
    case 'deep': return 'Complete the text shown.';
    case 'cli':  return 'Type the characters you see.';
    case 'rec':  return 'Type the code shown.';
    default:     return 'Type the word shown.';
  }
}
function subInstr(stage) {
  switch (stage) {
    case 'est':  return 'Both words. This helps improve our service.';
    case 'deep': return 'Fill in the letters you cannot make out.';
    case 'cli':  return '';
    case 'rec':  return '';
    default:     return 'This confirms you are human.';
  }
}
function verdictLine(stage) {
  switch (stage) {
    case 'deep': return 'completion from partial cue — logged';
    case 'cli':  return 'transcription recorded';
    case 'rec':  return 'verified';
    default:     return 'verified';
  }
}

export default { render };
