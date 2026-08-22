# Cloud audit brief — YAGNI and efficiency passes run from a Linux sandbox

This is the standing brief for the scheduled cloud audit routine. It runs every 2 hours in an
Anthropic cloud sandbox with its own Linux checkout, starting from zero context.

## The one hard limit

**DO NOT LAND ANYTHING.** Never push to `feat/crossplatform`, never merge, and never weaken a test,
a pin, or a gate to make something pass.

You **cannot** run this repository's gates:

- `node client/tests/floor/check-floor.mjs` pins **2622 unit / 152 headless** tests, and that count
  includes Windows-only and headed-desktop tests. On Linux the count differs, so the floor cannot be
  satisfied — a green here would be meaningless and a red here proves nothing.
- `client/docs/verification-harness.md` requires a **real headed Windows or WSLg capture** for any
  `presentation-verified` claim. A headless frame never discharges a headed gate.
- The shipping WPF tree is `net8.0-windows` and does not build on Linux at all.

So a change that looks correct here is still unverified work, and landing unverified work is the one
thing `docs/constitution.md` exists to prevent.

**Deliver by opening a PR against `feat/crossplatform`** titled `audit: <area>`, with the cut-list in
the body. **Changing nothing and reporting accurately is a good outcome.** A confident cut that breaks
a Windows-only behaviour nobody can see from Linux is a bad one.

## What to produce

One focused pass per run. Rank findings **biggest cut first**. Tag each:

| Tag | Means |
|---|---|
| `delete:` | dead code, unused flexibility, speculative feature. Replacement: nothing |
| `stdlib:` | hand-rolled thing the standard library ships. **Name the function** |
| `native:` | code doing what the platform or Avalonia already does. **Name the feature** |
| `yagni:` | abstraction with one implementation, config nobody sets, layer with one caller |
| `shrink:` | same logic, fewer lines. **Show the shorter form** |

Format: `<tag> <what to cut>. <replacement>. [path]`, then `net: -<N> lines possible.`
Nothing to cut: `Lean already. Ship.` and stop.

Correctness bugs, security holes and performance are **out of scope** for this pass. If you find one,
name it in the PR body as a finding and do not fix it here.

## Scope, in priority order

1. **`client/src`** — ~74,500 lines of C# and XAML across 319 files. The actual product. Real cuts
   count here. Note `Assets/assets.manifest.json` is a 58,370-line generated file; ignore it.
2. **`client/tests`** — ~88,000 lines. Duplication and helper sprawl are fair game. **Deleting a test
   is not a cut** — a test that pins real behaviour stays, and the floor pin means a deletion has to be
   declared, not slipped in.
3. **`spine-tasks/`** — ~784,000 lines of process documentation and, as of 2026-08-22, roughly 139 MB
   of committed binary and log residue. Report it; a local session owns the deletion.

## What has already been measured, so you do not redo it

- **507 public types in `client/src`: zero dead.** 20 have no reference outside their declaring file
  and all 20 are used inside it (e.g. `PointW` is a `GetCursorPos` out-parameter).
- **`CA1823` unused-private-field: 0**, under a full CA analyzer run that emitted 2063 other warnings —
  so the probe demonstrably works.
- **58 of 66 interfaces** have a second implementation or a test double. Of the remaining 8, several are
  load-bearing: `ISecretStore` is the per-OS seam the cross-platform goal depends on, and the `*Clock`
  interfaces exist to satisfy the no-wall-clock-waits rule.
- **Genuinely unmeasured, and therefore the best leads:** `IDE0051`-class unused private *methods* (no
  IDE-family rule ran at all, so that axis is unmeasured rather than clean — it needs `.editorconfig`
  severity raised), and `CA1515`, which fired **1656** times suggesting public types that could be
  internal. That is an over-exposed surface rather than dead code, and tests reference many of them, so
  it is risky to change wholesale.

## Things that are NOT bloat, however they look

- `ConditioningControlPanel/**` is **read-only archaeology that must track `main` byte for byte**. Its
  2.7M lines under `Resources/web` are upstream payloads the client links read-only. **Never edit it**,
  and never propose deleting it.
- Interfaces with one production implementation **plus a test double** are testing seams, not YAGNI.
  Check `client/tests` before calling one speculative.
- `[ComImport]` interop declarations (`IMFSample`, `IMFSourceReader`, `ICoreWebView2*Native`) have zero
  C# implementations by construction. Deleting them breaks video and WebView.
