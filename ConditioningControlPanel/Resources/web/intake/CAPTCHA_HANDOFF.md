# Captcha Items — Implementation Handoff (desktop-first)

> **For the desktop session implementing the fake-captcha item family in Graded Intake.**
> Decision (owner, 2026-07-22): build and tune these on DESKTOP first, evaluate against the
> existing item deck, then the mobile port picks them up for free via `npm run sync:web`
> (the mobile app vendors this exact web tree — see the ccpmobile repo).
>
> Read first: `CLAUDE.md` in this folder (the primer — §2 invariants, §4 engine/steering,
> §5 bank shape, §10 gotchas), then `CAPTCHA_BRAINSTORM.md` (28 curated templates with
> full 5-band ladders). This file is the actionable subset + integration map.
> Neither CAPTCHA_ doc ships: csproj `Exclude` and the mobile sync script both drop
> `CAPTCHA_*.md` (pattern-based — keep the prefix if you add more docs).

---

## 1. Concept in one paragraph

Fake captchas as intake items. The assessment "verifies the Subject" with pitch-perfect
captcha chrome (reCAPTCHA grids, the I-am-not-a-robot checkbox, distorted-text transcription,
GeeTest sliders, Turnstile interstitials) — played 100% straight in Calibration to build
trust, then degraded band by band while the chrome stays constant. The fiction spine is the
real-world reCAPTCHA inversion: you were never verifying, you were labeling data, and the
dataset is you. Framing stays psychological-evaluation deadpan; register/flavor comes from
the niche theme pack as usual.

## 2. Starter set (5 items — covers every answer shape + one detonation)

Full ladders in CAPTCHA_BRAINSTORM.md; short names here are suggested `Mechanic` ids.

| Mechanic | Brainstorm ref | Answer shape | Why first |
|---|---|---|---|
| `VerifyGrid` | #1 Hydrant Grid | tile bitmask → index list + count | The seed; exercises grid chrome, gif tile swaps, the Recovery frozen-tile callback |
| `VerifyCheckbox` | #3 Checkbox | bool + hold telemetry | Cheapest (no images); click→hold ladder; per-niche label inversion |
| `VerifyTranscribe` | #2 Transcription ladder | verbatim | Deepest compliance channel; Climax free-type echo is the richest tag-vote source |
| `VerifyCustody` | #8 Chain of Custody | bool per row | THE metadata detonation (real filenames) — needs desktop tuning most |
| `VerifyStillness` | #9 Passive Integrity Scan | bool + interference count | Consent-shaped cousin of the freeze gate; zero assets |

Defer the rest until these five survive a play-test. Next tier if they land: #6 regenerating
grid, #7 segmentation unblur, #4b spiral rotate, #12 fake eye-tracking, #11 Recovery
recognition audit (needs its Calibration lure-style seed planted, so it wants deck-wide
placement control).

## 3. Integration map (where each piece lands)

- **Mechanic enum** — `core/contracts.js` (one entry per template above).
- **Renderer** — one case each in the switch at `render/beats.js:1218`. Captcha chrome CSS as
  ONE injected template literal keyed by id (follow `IB_CSS`/`IXFZ_CSS` precedent — do NOT
  put it in styles.css, grep will already miss the others).
- **Band recurrence** — same item template at multiple bands is the core pattern. Author one
  bank entry per band-stage with `requires: {band}` / `minDepth`, shared id prefix
  (e.g. `shr_cap_grid_cal`, `shr_cap_grid_est`, ...). Calibration stages are heat 0 and play
  it completely straight — that trust is spent later; don't skip them.
- **Bank entries** — captcha items are strong candidates for the `shr_` shared-baseline
  block (chrome is niche-agnostic; theme pack supplies labels/verdicts). Per-niche strings
  (checkbox labels, verdict lines, archetype phrase banks) go in `banks/<niche>.json → theme`
  or per-niche prompt variants. Respect circe's register: no exclamations, no diminutives.
- **Grading** — everything reduces to the existing shapes. Grid selections: serialize the
  bitmask, grade a derived scalar (count / consistency / cycles-endured) and feed tag votes
  from WHICH user-asset tiles were chosen. Telemetry-flavored metrics (hold ms, interference
  count, clicks-past-futility) map to the slider shape. Latency already exists in velocity
  pacing — reuse, don't re-derive.
- **VO** — automatic for bank prompts (`q_<promptId>` via the ccp-trailer pipeline). New
  non-prompt lines (verdict stingers) need `audio.voice(id)` call sites + manifest entries.
  SFX (spinner resolve, LOGGED stamp, tile melt tick) via `gen_intake_sfx.py` SPEC +
  `SFX_MANIFEST_FALLBACK` in `render/audio.js`.
- **Media** — user assets arrive via BootConfig `media` (ccp.assets URLs). Grid items want
  15–20 samples vs today's 10 → bump in `IntakeHostService.OnPageReady`. **Mobile parity
  note**: BootConfig field CHANGES (new fields) must also land in `web-shim.js
  fromHostInit`; a sample-count bump is host-side only and mobile will mirror it in its own
  bootConfig later. PROTOCOL 1 itself must not change.
- **Real filenames** (`VerifyCustody` only): derive from the `media` URLs client-side.
  Sanitize (length, unicode) and prefer descriptive names; fall back to fabricated
  realistic ones — an `Untitled(3).gif` deflates the whole beat.

## 4. Hard rules (from invariants + brainstorm synthesis)

1. **Friction, not lockout** — every template has a graded refusal (`no hydrants reported —
   noted`, `transcription: silent.`, deny-everything). Third attempts accept; caps expire;
   forceComplete hatches styled in-fiction (`skip verification (flagged)`). Honour
   `ctx.forceComplete` and let synthetic clicks through (see the escape-guard pattern in
   steering.js).
2. **One metadata detonation.** Real filenames appear in `VerifyCustody` and NOWHERE else.
   Every other item implies; that one proves.
3. **Glitch budget**: max ONE existing corruption system (RGB scramble / melt / freeze gate)
   per item per band — don't exhaust the vocabulary before the non-captcha Climax items.
4. **Gif tiles are whole `<img>`s** in `overflow:hidden` wrappers (CSS crop). Canvas
   `drawImage` freezes animation — only ever intentionally ("the file stopped moving", the
   Recovery frozen tile). Cap concurrent DISTINCT animated gifs per grid at 3–4; incoming
   tiles arrive frozen and animate on settle. (Mobile WebView memory is the binding
   constraint — this app has an OOM history there.)
5. **`is-correct` / `is-answer` stay unstyled** (primer gotcha #9). Captcha "correct" verdicts
   are their own chrome, never those hooks.
6. **Nothing throws at import; stage.innerHTML is wiped per render** — persistent layers
   (spinner overlays, flight layers) parent to `<body>` like effects/steering do.
7. **Climax free-text is echoed locally, verbatim, only.** Never route it to the `/intake/ai`
   seam — the AI never sees transcripts (standing rule).
8. **No new coupling**: fire audio via the `intake-sfx`/`audio.voice` seams; hold no audio
   handles in the new renderer code.

## 5. nanobanana authoring batch (all offline, static PNGs → `intake/assets/captcha/`)

Glob-include means no csproj edit. One consistent style pass per set:

- **Mundane tile set** (~16): hydrants, crosswalks, buses, staplers — deliberately
  reCAPTCHA-drab (overexposed, JPEG-mushy, beige). User tiles must POP against these.
- **Niche icon language** (4 packs × ~8 icons, flat style): eye / spiral / kneel / lock etc.
  Reusable across sequence items and slider pieces later.
- **Stamps/seals**: LOGGED check, `VERIFICATION WAIVED`, per-niche register (circe's:
  `provisionally human.` — cold serif, no exclamation available to it).
- Deferred with their items: archetype portraits, inkblot sheets, distorted-word sheets,
  heat-map blobs, TAT plates.

## 6. Test path

`?m2test` (strips pacing/menu/briefing) for per-item iteration; `harness.html` strategies
(`best`/`worst`/`chase`/`random`) to confirm grading math and that refusal paths complete;
the import sweep stays the standing gate; hot-copy into
`bin\Debug\...\Resources\web\intake\` works for web-only changes. Play-test focus: does the
Calibration-straight → Climax-rot arc read, and does the Custody item land or overreach.

## 7. After it lands on desktop

Mobile follow-up (ccpmobile repo, not this session's job): re-run `npm run sync:web`
(hash-keyed re-extract, rides OTA), mirror the media sample bump in mobile `bootConfig.ts`,
and touch-adapt any new pointer interactions (hold gestures, drag rails) behind the existing
`isCoarsePointer()` gate in `steering.js` — desktop mouse paths must stay byte-identical.
