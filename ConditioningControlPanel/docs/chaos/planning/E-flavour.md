# Agent E — Chaos Mode Flavour & Skinnability Plan

> Scope: research + proposal only. No code changes. Engine is flavour-agnostic; base ships neutral-usable; each niche is a **content-only** per-mod skin (field-level merge), same philosophy as bark skins + `textReplacements`.

---

## 1. Current State (code is source of truth)

### How the mod system already does flavour (the pattern to copy)
`Services/ModService.cs` resolves every flavour field with a strict **fallback chain: `ActiveMod → BaseMod (CCP Default)`**:

- `GetValue<T>` / `GetStringValue` — field-level merge. An unset field on the active mod falls back to the base mod's value (`ModService.cs:619-631`). *This is the invariant we want for Chaos: unset → neutral base.*
- `MakeModAware(text)` — applies `TextReplacements` (a flat `Dictionary<string,string>`), **longest-key-first** to avoid partial matches (`ModService.cs:850-865`).
- `GetPhrases(category)` — `Phrases` is `Dictionary<string,string[]>`, category → pool, with base fallback (`ModService.cs:808-826`).
- Enhancement labels show the **two-tier pattern** worth reusing: explicit override field first, else `MakeModAware(neutralDefault)` (`ModService.cs:902-936`). E.g. `GetPinkRushName() => EnhancementOverrides?.PinkRushName ?? MakeModAware("PINK RUSH!")`.
- Art/sound override by file presence: `ModResourceResolver` checks `{mod}/resources/...` then falls back to embedded `pack://` (`ModResourceResolver.cs`). **Agent D's lane.**
- Voicelines already per-mod: `Resources/sounds/companion_audio/mods/{modId}/bark_rules.json` (content-only; no engine code touched). **Already done.**

Manifest identity fields already in `Models/ModManifest.cs`: `Identity.{CompanionName,UserTerm,ModeDisplayName,Affirmation,RankSubject,...}`, `Theme.*`, `Phrases`, `TextReplacements`, `EnhancementOverrides.*`, etc. Built-ins defined in `Models/BuiltInMods.cs` (CCP Default = neutral baseline at `CreateCCPDefault()`).

### Hardcoded Chaos flavour strings (the gap)
None of these read the manifest today — all are English string literals in `Services/Chaos/`:

| Surface | Location | Current (hardcoded) values |
|---|---|---|
| Currency name | `ChaosUpgrades.cs:19` (comment "in Sparks"), `ChaosMeta.AwardRunRewards`, UI/results | **"Sparks"** |
| Boon/curse Name+Desc | `ChaosModels.cs:113-133` (`ChaosBoonPool.All`) | "Slow Fuses / +30% fuse time…", "Hair Trigger", "Live Wire", etc. |
| Bubble labels (glyphs) + names | `ChaosBubbleVariants.cs:158-181` (`.Label`, `.Name`) | "Flash", "Pink Filter", "Spiral", "BrainDrain", "Freeze"… + glyphs ◑ ◎ ☁ ❄ |
| Codex blurbs | `ChaosBubbleVariants.cs:133-145` (`DescriptionFor`) | "A benign treat. Pop it for a quick flash burst…" |
| Upgrade names | `ChaosUpgrades.cs:50-77` (`.Name`) | "+1 Start Shield", "Slower Fuses", "Golden Touch"… |
| Rank names | `ChaosUpgrades.cs:103-111` (`ChaosMeta.Rank`) | Newcomer → Dabbler → Novice → Initiate → Adept → Paragon |
| HUD copy | `ChaosModels.cs:196` (`ActWaveText`) | "ACT I · WAVE 1/5" |
| Results flavour | `ChaosModeService.cs:476` `_overlay.ShowResults(...)` | (numeric; flavour line TBD) |
| Presets | `ChaosBubbleVariants.cs:150-156` | "Balanced", "Tease", "Overload", "Flash-only" |

> Note: `ChaosBoon` / `ChaosBubbleVariant` / `ChaosUpgrade` are **static pools with embedded `Action<...>` mutators** (the mechanic). Skinning must touch **only display strings**, never the `Apply` lambdas or numeric knobs — that keeps the engine mod-agnostic and the mechanic identical across skins.

---

## 2. Proposal

### 2a. The NEUTRAL BASE flavour (CCP Default)
Alluring + conditioning-themed but **persona-neutral** (no Bambi/sissy/drone vocabulary; usable as-is). The neutral base lives in code as the literal default that the resolver returns when no manifest override exists — so today's behaviour is byte-for-byte unchanged.

| Surface | Neutral base (CCP Default) |
|---|---|
| Currency | **Sparks** (keep — already neutral & evocative) |
| Run/HUD frame | **"ACT {N} · WAVE {w}/{W}"** (keep) |
| Rank ladder | Newcomer · Dabbler · Novice · Initiate · Adept · **Paragon** (keep — neutral progression) |
| Boon: `slow_fuses` | **"Slow Fuses"** — "+30% fuse time on live bubbles." |
| Boon: `golden_touch` | **"Golden Touch"** — "+15% run multiplier outright." |
| Curse: `hair_trigger` | **"Hair Trigger"** — "−25% fuse time, but +0.4x run multiplier." |
| Bubble: `pink` | name **"Pink Filter"**, codex "Live. Defuse before the fuse runs out, or it snaps a pink filter over the screen." |
| Bubble: `braindrain` | name **"BrainDrain"**, codex "Live and large. Detonates into a creeping mind-mist." |
| Results title | **"RUN COMPLETE"** + neutral line e.g. *"You held it together. Banked {n} Sparks."* |

### 2b. Skinnable surface map

| Surface | Skinnable? | Mechanism |
|---|---|---|
| Currency name | Yes | New `chaos.currencyName` (dedicated block — single global term, not a search/replace) |
| Boon/curse Name + Desc | Yes | `chaos.boons[id].{name,desc}` keyed by **stable boon id** |
| Curse Name + Desc | Yes | same `chaos.boons[id]` map (curses share the id space) |
| Bubble names | Yes | `chaos.bubbles[id].name` keyed by variant id |
| Bubble codex blurbs | Yes | `chaos.bubbles[id].codex` |
| Bubble glyph/label | Optional | `chaos.bubbles[id].label` (usually leave to base; art is Agent D) |
| Upgrade names | Yes | `chaos.upgrades[id].name` keyed by upgrade id |
| Rank names | Yes | `chaos.ranks` (ordered array, lowest→highest; index-mapped) |
| HUD act/wave copy | Yes | `chaos.hud.{actLabel,waveLabel}` format strings w/ `{n}` `{w}` `{W}` |
| Results flavour | Yes | `chaos.results.{title,line}` (or `Phrases["chaos.results"]` pool for variety) |
| Presets | Optional | `chaos.presets[name]` display rename via `TextReplacements` is enough |
| Voicelines | **Already done** | per-mod `bark_rules.json` (no change) |
| Art/sfx | **Agent D** | `ModResourceResolver` file presence |

**Mechanism rule of thumb:**
- **Dedicated `chaos` block** for *structured, id-keyed* content (boons, bubbles, upgrades, ranks) — cleaner than fragile global `.Replace`, and id-keying survives copy edits to the base English.
- **`TextReplacements`** for *incidental* one-off renames that also appear elsewhere (e.g. "Pink Filter" → "Cloud Filter" if the persona already remaps "Pink"). Field-level, longest-first, already wired.
- **`Phrases`** for *pools needing variety* (results lines) — reuses `GetPhrases(category)`.

### 2c. Manifest schema additions
Add one optional block. **Every field optional → unset falls back to neutral base** (matches `GetValue`/`GetStringValue` invariant). Keyed by **stable engine ids** (`slow_fuses`, `pink`, `start_shield`, …) so reskins survive base-copy edits.

```jsonc
// mod.json — new optional top-level "chaos" block
"chaos": {
  "currencyName": "Sparks",                 // overrides "Sparks" everywhere
  "hud": {
    "actLabel": "ACT {n}",                   // {n}=roman act
    "waveLabel": "WAVE {w}/{W}"              // {w}=wave, {W}=count
  },
  "ranks": ["Newcomer","Dabbler","Novice","Initiate","Adept","Paragon"], // low→high
  "results": {
    "title": "RUN COMPLETE",
    "line": "You held it together. Banked {sparks} {currency}."
  },
  "boons": {
    "slow_fuses":   { "name": "Slow Fuses",   "desc": "+30% fuse time on live bubbles." },
    "hair_trigger": { "name": "Hair Trigger", "desc": "−25% fuse time, but +0.4x run multiplier." }
  },
  "bubbles": {
    "pink":       { "name": "Pink Filter", "codex": "Live. Defuse it or it snaps a pink filter over the screen.", "label": "◑" },
    "braindrain": { "name": "BrainDrain",  "codex": "Live and large. Detonates into a creeping mind-mist." }
  },
  "upgrades": {
    "start_shield": { "name": "+1 Start Shield" },
    "golden_touch": { "name": "Golden Touch" }
  }
}
```

**ModManifest.cs C# additions (mirrors existing optional-section style):**

```csharp
[JsonProperty("chaos")] public ModChaos? Chaos { get; set; }

public class ModChaos {
    [JsonProperty("currencyName")] public string? CurrencyName { get; set; }
    [JsonProperty("hud")]          public ModChaosHud? Hud { get; set; }
    [JsonProperty("ranks")]        public List<string>? Ranks { get; set; }
    [JsonProperty("results")]      public ModChaosResults? Results { get; set; }
    [JsonProperty("boons")]        public Dictionary<string, ModChaosEntry>? Boons { get; set; }
    [JsonProperty("bubbles")]      public Dictionary<string, ModChaosBubble>? Bubbles { get; set; }
    [JsonProperty("upgrades")]     public Dictionary<string, ModChaosEntry>? Upgrades { get; set; }
}
public class ModChaosHud     { public string? ActLabel; public string? WaveLabel; } // [JsonProperty] each
public class ModChaosResults { public string? Title; public string? Line; }
public class ModChaosEntry   { public string? Name; public string? Desc; }
public class ModChaosBubble  { public string? Name; public string? Codex; public string? Label; }
```

Add length/count caps in `ModService.SanitizeManifest` exactly like the existing `EnhancementOverrides`/`TextReplacements` caps (e.g. ≤30 entries per dict, ≤500 chars per value, ≤12 ranks).

### 2d. Base-vs-skin examples (two personas)

Same surfaces, neutral base then per-persona override JSON.

| Surface | Base (CCP Default) | Bambi override | Drone override |
|---|---|---|---|
| Currency | Sparks | Sparkles | Credits |
| Boon `golden_touch` | "Golden Touch" — "+15% run multiplier outright." | "Bimbo Luck" — "everything pays +15% more, good girl~" | "GOLD.PROTOCOL" — "+15% yield. Optimal." |
| Curse `hair_trigger` | "Hair Trigger" — "−25% fuse time, but +0.4x run multiplier." | "Dumb & Fast" — "−25% fuse… but who needs to think? +0.4x" | "OVERCLOCK.RISK" — "−25% fuse window; +0.4x throughput." |
| Rank (top) | Paragon | Perfect Doll | TERMINAL.ADMIN |
| Results line | "You held it together. Banked {sparks} Sparks." | "good girl~ you banked {sparks} sparkles 💕" | "RUN COMPLETE. {sparks} credits archived." |

**Bambi** (`mods/builtin-bambisleep/mod.json`):
```jsonc
"chaos": {
  "currencyName": "Sparkles",
  "ranks": ["New Toy","Dizzy","Pink Pupil","Bimbo","Bambi Doll","Perfect Doll"],
  "results": { "line": "good girl~ you banked {sparks} sparkles 💕" },
  "boons": {
    "golden_touch": { "name": "Bimbo Luck", "desc": "everything pays +15% more, good girl~" }
  },
  "boonsCurses_example_hair_trigger": null,
  "bubbles": { "braindrain": { "name": "Brain Melt", "codex": "big, slow, dreamy… let it pop and float away~" } }
}
```

**Drone** (`mods/builtin-drone/mod.json`):
```jsonc
"chaos": {
  "currencyName": "Credits",
  "hud": { "actLabel": "PHASE {n}", "waveLabel": "CYCLE {w}/{W}" },
  "ranks": ["Blank Unit","Booting","Standard Unit","Calibrated","Optimized","Terminal Admin"],
  "results": { "title": "RUN COMPLETE", "line": "{sparks} credits archived. Compliance logged." },
  "boons": {
    "golden_touch": { "name": "GOLD.PROTOCOL", "desc": "+15% yield. Optimal." }
  },
  "bubbles": { "pink": { "name": "Filter.exe", "codex": "Live process. Terminate it or it overlays the display." } }
}
```
*(Unset ids — e.g. Bambi never naming `start_shield` — fall through to neutral base. Field-level merge, exactly like `EnhancementOverrides`.)*

---

## 3. Engine Refactor Implied (flag the seam only — do NOT design code)

**The seam:** introduce a thin `ChaosFlavor` resolver (read-only facade over `App.Mods`) that every Chaos display site calls *instead of* reading the hardcoded literal. Conceptually identical to `ModService.GetPinkRushName()`'s pattern: **`manifest override ?? neutral base literal`** (optionally `?? MakeModAware(base)` so `TextReplacements` still catches incidental terms).

Touch points to reroute (display only — never the `Apply`/numeric fields):
- `ChaosBoonPool.All[*].{Name,Desc}` → resolve by boon `Id` at *display* time (keep pool records as the mechanic + neutral default).
- `ChaosBubbleVariants` `.Name` / `.Label` / `DescriptionFor(id)` → resolve by variant `Id`.
- `ChaosUpgrades.All[*].Name` → resolve by upgrade `Id`.
- `ChaosMeta.Rank` → map `RunsCompleted` thresholds to an index into resolved `ranks[]` (neutral list is the default).
- `ChaosRunState.ActWaveText` → format from resolved `hud` labels.
- `ChaosModeService.ShowResults` → resolved `results.title/line` with `{sparks}`/`{currency}` tokens.
- Every "Sparks" literal → resolved `currencyName`.

**The invariant (must hold):** with **no** mod / CCP Default active and **no** `chaos` block present, the resolver returns the exact current English literals → **neutral default is byte-for-byte unchanged**, same guarantee the run-config knobs already document ("an unmodified run is byte-for-byte unchanged", `ChaosModels.cs:40`). The resolver is the *only* new code path; pools/lambdas stay put.

---

## 4. Open Questions

1. **Currency: global vs textReplacements?** Proposal uses dedicated `chaos.currencyName` because "Sparks" is one canonical term shown formatted with counts (`{sparks}`); a global `.Replace("Sparks", …)` would be brittle and could hit unrelated copy. Confirm OK.
2. **Rank mapping:** ranks are threshold-keyed in code (`>=100 Paragon`…). Index-mapping a flat `ranks[]` assumes the **same 6 thresholds**. If a skin wants different rank *count*, do we expose thresholds too, or lock to 6 tiers? (Recommend: lock to 6 for v1.)
3. **Results variety:** single `results.line` vs a `Phrases["chaos.results"]` pool (reuses `GetPhrases`, gives the companion-style randomness). Pool is cheap — worth it?
4. **Token grammar:** standardize placeholders (`{sparks}`,`{currency}`,`{n}`,`{w}`,`{W}`) — confirm naming before authors write skins (changing later breaks shipped mods).
5. **Glyph labels:** leave bubble glyphs (◑ ◎ ☁ ❄) base-only, or expose `bubbles[id].label`? They double as fallback art if Agent D's sprite is missing — coordinate with Agent D so we don't skin the same surface twice.
6. **Where do built-in skins live?** Identity/theme are in `BuiltInMods.cs` (code); but Drone already ships as a bundled `.ccpmod` (`drone-mode.ccpmod`, `ModService.cs:1129`). New `chaos` blocks for built-ins should go in whichever is authoritative per mod (code for Bambi/Sissy/CCP Default; the `.ccpmod` manifest for Drone). Confirm.
