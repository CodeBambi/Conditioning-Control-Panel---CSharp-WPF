# Agent D — Chaos Mode Art Plan

Exploration & planning only. No code, no art generation, no promotion. Code in
`Services/Chaos/` is source of truth; this doc is reconciled against it.

---

## Current state (verified against code)

- **Resolver** — `Services/Chaos/ChaosArt.cs`:
  - `Resolve(kind, id)` → `Assets/Chaos/{kind}/{id}.png`, checked under
    `App.UserAssetsPath` first, then `AppContext.BaseDirectory` (bundled). Returns
    `null` → caller draws a vector placeholder. **Game is fully playable with zero art.**
  - `ResolveBanner()` → `Assets/Chaos/banner.png`.
  - `PathFor(kind, id)` returns the resolved path for callers that need it.
  - Per-variant bubble sprites resolve via `Resolve("bubbles", spec.VariantId)`
    (`BubbleService.cs:821`); when present the tint overlay + glyph fallback are skipped
    (`BubbleService.cs:818-821, 1335`).
- **Bubble sprites already exist** (9 finals shipped) at
  `ConditioningControlPanel/assets/Chaos/bubbles/{id}.png`:
  `flash, subliminal, pink, spiral, braindrain, bambifreeze, video, htlink, darter`.
  A staging workflow is already in use: `assets/Chaos/bubbles/_staging/` holds
  `{id}_1/_2/_3.png` candidates plus a `_contact.png` contact sheet. **So the precedent
  in the memory note is now realized for bubbles — this plan extends it to the rest.**
- **No art exists yet** for: boon icons, curse icons, upgrade/branch crests, HUD glyphs,
  power-up FX, hero banner, results art, cosmetic skins. All currently render as vector
  placeholders / Unicode glyphs.
- **Tooling** lives in the parent dir (gitignored), **`C:\Projects\Conditioning-Control-Panel---CSharp-WPF\Tools\asset_gen\`** and `Tools\avatar-emotes\`:
  - Model: `gemini-2.5-flash-image` (GA, "nano-banana") via `gen_avatars.DEFAULT_MODEL`;
    `gemini-3.1-flash-image` is the pixel/seed model used by `pixelate_levels.py` (falls
    back to GA). Shared helpers: `load_api_key`, `extract_image_bytes`, `crop_to_alpha`.
  - Luma→alpha + crop precedent: `gen_locked_skills.py` (`luma_key()`,
    `fit_contain()`) — generate neon-on-pure-black, alpha = brightest channel, crop to glyph.
  - Programmatic recolour-by-luminance LUT precedent (for skins/recolours without a model
    round-trip): `gen_locked_bubbles.py`.
  - Style-transfer/pixelate precedent (ref-first, fixed `SEED`): `pixelate_levels.py`.
  - Interactive review-gate precedent: `avatar-emotes/review_emotes.py` (writes
    `review_verdicts.json/.txt`, resumes on first unreviewed).
- **csproj wiring already present** (`ConditioningControlPanel.csproj:148-157`): a
  `<None Remove="assets\Chaos\**\*.png;...">` precedes the `<Content Include=...>` so the
  default None glob doesn't suppress the Content copy; `_staging` is excluded so only
  promoted finals ship. **This is exactly the gotcha from the memory note — already handled
  for `assets/Chaos/**`. Any NEW art class that lands outside that glob must repeat the
  `<None Remove>` + `<Content Include>` pair, or it won't copy to output.**

> **Casing note (low risk, flag for wiring):** the resolver builds paths as
> `Assets\Chaos\...` (capital A) while the csproj globs `assets\Chaos\...` (lowercase).
> Windows is case-insensitive so both work today; keep one casing if this ever ships to a
> case-sensitive path or build agent.

---

## Proposal

### 1. Prioritized art manifest

`kind` = the folder segment under `Assets/Chaos/{kind}/`. Ids are the exact code ids
(verbatim from `ChaosBubbleVariants.cs`, `ChaosBoonPool`, `ChaosUpgrades`). Everything is
optional: a missing file falls back to a vector placeholder, so priority = polish order,
not blocker order.

#### P0 — ship-first (most on-screen, biggest brand impact)

| Asset class | Ids | Path `Assets/Chaos/{kind}/{id}.png` | Gen vs hand |
|---|---|---|---|
| Bubble sprites (9) | `flash, subliminal, pink, spiral, braindrain, bambifreeze, video, htlink, darter` | `bubbles/{id}.png` | **Generatable** (DONE — finals shipped; this plan covers regen/reskin) |
| Hero banner | `banner` | `banner.png` (root, not a `{kind}` folder) | Generatable |

#### P1 — high-frequency UI (draft + HUD every wave)

| Asset class | Ids | Path | Gen vs hand |
|---|---|---|---|
| Boon icons (5) | `slow_fuses, defuse_chain, golden_touch, magnet, extra_shield` | `boons/{id}.png` | Generatable (glyph) |
| Curse icons (3) | `hair_trigger, live_wire, double_or_nothing` | `boons/{id}.png` (same kind; `IsCurse` is data) | Generatable (glyph) |
| Branch crests (3) | `control, greed, depth` | `branches/{id}.png` (or `crests/`) | Generatable (emblem) |
| HUD: shield pips | `shield_full, shield_empty` | `hud/{id}.png` | **Hand-work** (tiny ♥/♡, must read at ~16px) |

#### P2 — flavor / juice

| Asset class | Ids | Path | Gen vs hand |
|---|---|---|---|
| Upgrade icons (12) | `start_shield, slow_fuses, bigger_hitboxes, shield_recharge, base_mult, golden_touch, magnet, spark_gain, max_bubbles, draft4, extreme_tier, take_more` | `upgrades/{id}.png` | Generatable (glyph) |
| HUD: act numerals | `act_1 … act_5` (+ fallback to Unicode roman) | `hud/{id}.png` | **Hand-work** (numerals — model misspells; prefer styled font/vector) |
| HUD: combo flame | `combo_flame` | `hud/{id}.png` | Generatable (FX), but small → review tight |
| HUD: heat meter fill/frame | `heat_fill, heat_frame` | `hud/{id}.png` | Hand-work (must tile/stretch cleanly) |
| Power-up FX: slow-mo | `slowmo_burst, slowmo_edge` | `fx/{id}.png` | Generatable (FX sheet) |
| Power-up FX: freeze | `freeze_burst, freeze_edge` | `fx/{id}.png` | Generatable (FX sheet) |
| Draft card frame/back | `card_frame, card_back` | `cards/{id}.png` | Generatable (frame), 9-slice by hand if stretched |
| Results-screen art | `results_bg, rank_badge` | `results/{id}.png` (+ per-rank `rank_{newcomer…paragon}`) | Generatable (bg) / hand (rank tiers, legibility) |

#### P3 — cosmetic / future

| Asset class | Ids | Path | Gen vs hand |
|---|---|---|---|
| Boon-pool headroom | future ids as pool grows (~8 → 25-30) | `boons/{id}.png` | Generatable (reuse boon template) |
| Cosmetic skin sets (per-mod) | full duplicate set under user assets | user `Assets/Chaos/**` overrides | Generatable per niche (content-only override) |
| Codex thumbnails | `bubble:{id}`, `boon:{id}` (reuse the above) | reuse `bubbles/` + `boons/` | Reuse |

> Code uses **roman-numeral acts** in text (`ChaosRunState.ToRoman`, up to `n`), so act art
> beyond V is unbounded — ship V plus the Unicode fallback rather than generating per-act PNGs.

### 2. Style lock

Locks to the existing dark brand language while staying reskinnable:

- **Palette (shared, fixed):** deep near-black base (`#1A1A2E` / `#252542`), hot-pink/magenta
  accent (`#FF69B4`, brighter flash-pink `#FF4DC4`), purple secondary. A **BrandGradient**
  (pink→purple) for fills/rims on hero/results/cards. Per-variant **tints come from code**
  (`ChaosBubbleVariants.All` Tint column) — bubble sprites should be **near-monochrome /
  luminance-driven** so the runtime tint and the per-mod recolour LUT both read correctly;
  don't bake a saturated hue that fights the code tint.
- **Silhouette:** single centered emblem/glyph filling most of the frame with a small margin;
  bold, instantly readable at HUD size (16-48px). One concept per icon, no scene clutter.
- **Rim-light & bloom:** thin bright magenta rim + soft neon bloom (the `gen_locked_skills`
  look) — this is the unifying "Chaos glow."
- **Transparency:** generate **neon-on-pure-solid-black**, then **luma→alpha** (alpha =
  brightest channel) and crop to content. Never rely on the model to emit a clean checkerboard
  alpha (it bakes the checkerboard into pixels — see gotcha below).
- **Text-free:** no baked words/numbers (model misspells). Tiers/counts via pips, chevrons,
  or styled vector/font in WPF.
- **Reskinnable seam:** base ships **neutral-but-on-brand** finals under the bundled
  `Assets/Chaos/`. Per-mod skins are **content-only overrides** dropped into the user assets
  folder (resolver checks user path first). No code change to reskin — same ids, same paths.

### 3. Reusable nano-banana prompt templates

All use `gen_avatars` helpers (`extract_image_bytes`, `crop_to_alpha`) and the
`luma_key`/`fit_contain` pattern from `gen_locked_skills.py`. Generate at high res
(~1024²), luma-key, crop, contain-fit to the target canvas. **Use a fixed `seed` per SET**
for cross-asset coherence (as `pixelate_levels.py` does).

**Shared STYLE block (prepend to every prompt):**

```
A single centered glowing neon ICON/EMBLEM, glossy HOT MAGENTA-PINK over deep BLACK with
PURPLE accents and a thin bright magenta rim-light and soft neon bloom. Flat vector
game-icon style — NOT 3D, NOT photorealistic, NOT a render. Clean, bold, instantly
readable, filling most of the frame with a small margin. On a PURE SOLID BLACK background,
nothing else. NO text, NO numbers, NO letters, NO words, NO border, NO frame, NO UI, NO
watermark, no human figure or face.
```

**(a) Bubble sprite** — `assets/Chaos/bubbles/{id}.png`, ~512², fixed seed across the 9 so the
glass reads as one set; near-monochrome so the code tint + recolour LUT apply cleanly:

```
{STYLE}
A glossy translucent floating SOAP BUBBLE / orb, mostly light/white glass with a bright
specular highlight and a thin luminous rim, near-monochrome (let an external tint colour
it). Inside the bubble, faintly, a small emblem reading as: {concept}.
Square framing, the orb centered.  // concept e.g. flash="a lightning/flash burst",
spiral="a hypnotic spiral", braindrain="a heavy storm cloud / mist",
bambifreeze="a snowflake", video="a play triangle", htlink="a chain link",
darter="a small fast comet/dart".
```

**(b) Boon icon** — `boons/{id}.png`, ~512², fixed seed for the boon family:

```
{STYLE}
The emblem depicts a BENEVOLENT power-up: {concept}.  // slow_fuses="a slowing/long fuse
with a clock", defuse_chain="linked shield-rings", golden_touch="a golden sparkling hand/
touch", magnet="a horseshoe magnet with pull lines", extra_shield="a heart-shield with a
plus".
Warm, inviting glow (more pink than red).
```

**(c) Curse icon** — `boons/{id}.png` (same kind), **separate seed**, hotter/red-shifted to
read as risk:

```
{STYLE}  (shift accents toward RED with crackle/glitch energy to read as DANGER)
The emblem depicts a RISKY CURSE: {concept}.  // hair_trigger="a sparking short fuse",
live_wire="a crackling live wire/bolt", double_or_nothing="two dice / a double-or-nothing
coin".
```

**(d) HUD glyph** — `hud/{id}.png`, small target (~128² source → fit ~16-48px), **maximize
contrast & line weight, minimal detail** (tiny — see hand-work caveat):

```
{STYLE}
A MINIMAL, HIGH-CONTRAST single glyph designed to stay legible at very small size (16px):
{concept}.  // shield_full="a filled heart-shield", shield_empty="an empty heart-shield
outline", combo_flame="a single rising flame".
Thick clean strokes, no fine interior detail.
```

**(e) FX sheet** — `fx/{id}.png`, ~1024², transparent radial FX (additive in WPF):

```
{STYLE}
A transparent screen-space EFFECT element on pure black for additive blending:
{concept}.  // slowmo_burst="a radial slow-motion shockwave ring", freeze_burst="an icy
crystalline shatter burst (white-blue)", freeze_edge="a frosted screen-edge vignette",
slowmo_edge="a soft pink screen-edge pulse".
Radially symmetric where applicable, soft falloff to fully black at the frame edge.
```

### 4. Pipeline flow

```
gen (gemini-2.5-flash-image, neon-on-black, fixed seed per set)
  → luma_key() : alpha = max(RGB channel), crop_to_alpha()      [no checkerboard from model]
  → [optional] pixelate_levels.py style-transfer pass (if a pixel skin is wanted)
  → fit_contain() onto exact target canvas
  → write CANDIDATES to STAGING:  assets/Chaos/{kind}/_staging/{id}_1.png, _2, _3
  → build a _contact.png contact sheet of all candidates
  ─────────────── MANUAL REVIEW GATE (no auto-promote) ───────────────
  → human reviews _contact.png (review_emotes.py-style verdict file optional)
  → on approval: copy the chosen candidate  _staging/{id}_N.png → {kind}/{id}.png
  ─────────────── PR (no direct main) ───────────────
```

- **Staging path:** `assets/Chaos/{kind}/_staging/` (matches the existing
  `assets/Chaos/bubbles/_staging/`). `_staging` is **excluded from the csproj Content glob**
  (`Exclude="assets\Chaos\**\_staging\**"`) so candidates never ship — only promoted finals.
- **Review gate checks:** (1) on-brand palette/glow/rim; (2) reads at runtime size (HUD glyphs
  at 16-48px, bubbles at their size band); (3) clean luma→alpha edges, no checkerboard
  bleed, no halo; (4) **no baked text/numerals**; (5) near-monochrome bubbles tint correctly
  under the code Tint; (6) set coherence (same seed family looks unified).
- **csproj gotcha to watch:** the `<None Remove>` + `<Content Include>` pair at
  `ConditioningControlPanel.csproj:148-157` already covers `assets\Chaos\**`. **Any new art
  class placed OUTSIDE that glob (e.g. a different root) won't copy to output** unless you add
  a matching `<None Remove>` before its `<Content Include>` — otherwise the default None glob
  silently suppresses the copy and the resolver returns null (silent vector fallback, easy to
  miss). Keep new art under `assets/Chaos/**` to inherit the existing rule.

### 5. Generatable vs hand-work

- **Generatable (model + luma pipeline):** bubble sprites, boon icons, curse icons, upgrade
  icons, branch crests, power-up FX sheets, hero banner, results background, card frames,
  per-mod skin recolours (LUT, no model needed — `gen_locked_bubbles.py` pattern).
- **Needs hand-work / non-model:**
  - **Act roman numerals & any numbers** — model misspells; use the existing Unicode
    `ToRoman` text or a styled vector/font in WPF, not generated PNGs.
  - **Tiny HUD glyphs (shield pip ♥/♡, combo flame, heat fill/frame)** — legibility at
    16-48px usually needs a hand cleanup pass or a pure vector; generate as a starting point
    but expect manual touch-up. Heat fill/frame must tile/9-slice cleanly.
  - **Rank badges / draft-card frames** if they stretch — 9-slice by hand to avoid corner
    smear.

---

## Open questions

1. **`{kind}` names not yet in code** (boons/curses/branches/upgrades/hud/fx/cards/results):
   only `bubbles` and `banner` are wired today. Confirm exact folder strings WHEN the
   consuming UI lands so prompts/paths match `ChaosArt.Resolve(kind, id)` calls 1:1.
2. **Curse vs boon kind:** both are in `ChaosBoonPool` with an `IsCurse` flag — share one
   `boons/` kind (proposed) or split into `curses/`? Affects path table.
3. **HUD: PNG vs vector.** Recommend keeping shield pips / act numerals / heat meter as
   styled WPF vector/font (already works as Unicode) and only generating the FX/flame.
   Confirm the HUD designer wants PNG slots at all.
4. **Per-mod skin delivery:** overrides via the user assets folder (resolver-first) vs a
   content-pack? `ContentPackService` exists — decide whether Chaos skins ride that channel.
5. **No chaos-specific gen script exists** (`gen_locked_chaos*.py`); the 9 bubbles were made
   ad-hoc. A dedicated script (boon/curse/upgrade/fx batches, fixed seeds, staging output) is
   the natural next build step — out of scope for this planning pass.
