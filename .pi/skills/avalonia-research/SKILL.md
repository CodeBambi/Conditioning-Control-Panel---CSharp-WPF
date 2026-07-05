---
name: avalonia-research
description: "Mandatory research protocol for ALL Avalonia UI work in this repo. The project targets Avalonia v12+, which is brand-new in 2026: training data is stale, v11 answers are actively wrong, and real fixes live only in recent GitHub issues and the official docs. Use this skill every time you touch Avalonia code, AXAML, controls, styling, windowing, rendering, input, application lifetime, packaging, or third-party Avalonia packages, and whenever an Avalonia API behaves unexpectedly, throws, or seems to be missing. Also use it BEFORE adding any NuGet package to the port."
---

# avalonia-research

Applies to all work in `ConditioningControlPanel/CCP.Core`, `CCP.Avalonia`, `CCP.Avalonia.Desktop*`, and `CCP.Avalonia.Android`.

## Why this skill exists

- The project targets **Avalonia UI v12+** (currently 12.0.x). v12 shipped in 2026 with renamed APIs, new platform behavior, and unresolved bugs that are not in offline training data.
- v11-and-earlier documentation, blog posts, Stack Overflow answers, and NuGet packages are frequently **actively wrong** for v12.
- The project has already paid for a large amount of v12 research. That knowledge is written down in the repo. Re-deriving it wastes time; contradicting it introduces bugs that were already fixed once.

## The three-step protocol

### Step 1: Check what the project already knows (before searching the web)

The port maintains a curated v12 knowledge base. Consult it first; most day-to-day questions are already answered there:

| Where | What |
|---|---|
| `ConditioningControlPanel/docs/crossplatform-rebuild-plan.md` section 21 | The canonical v12 gotcha list (compiled bindings / `x:DataType`, `WindowDecorations` rename, `TransparencyLevelHint` list type, LibVLC discovery, DI override pattern, click-through levels, theme accents, and more) |
| Same file, section 23 | Official v12 documentation links. On conflict, official docs beat the plan doc |
| `ConditioningControlPanel/docs/unified-compositor-engine-plan.md` | Researched v12 rendering facts with sources (custom draw ops, `ISkiaSharpApiLeaseFeature`, `CompositionCustomVisualHandler`, invalidation behavior) |
| Code comments near P/Invoke and compositor code | Hard-won crash workarounds (for example the `SetWindowSubclass` ban and staggered window creation in `CCP.Avalonia/Compositor/`) |

The plan doc is over 100KB: Grep for section headers first, then Read only the relevant slice.

### Step 2: Research the current v12 answer on the web

For anything not already settled locally, verify against current sources before using it:

1. **Official docs** - search `https://docs.avaloniaui.net` and confirm the page applies to v12.
2. **GitHub issues / PRs / discussions** - search `https://github.com/AvaloniaUI/Avalonia` for the exact exception, API name, or behavior. Recent issues are where real workarounds live.
3. **Release notes** - `https://github.com/AvaloniaUI/Avalonia/releases` for v12.x breaking changes and fixes.
4. **NuGet** - before adding any third-party Avalonia package, verify its latest stable version explicitly supports v12. A package whose newest release targets v11 is a rejection, not a risk to accept.

Use `WebSearch` with time-biased queries:

- `Avalonia 12 <topic>`
- `Avalonia v12 <exception message>`
- `site:docs.avaloniaui.net <topic>`
- `site:github.com/AvaloniaUI/Avalonia <topic>`

Reject without re-verification: v11/v10 docs and samples, tutorials from 2025 or earlier that do not mention v12, and WPF-era workarounds copied from old Avalonia code. When sources conflict, prefer the newer source, and prefer official docs / the Avalonia repo over third-party content.

### Step 3: Record what you learned

Research that stays in the conversation is lost when the context ends. If you established a new v12 fact, bug, or workaround that future sessions will need:

- Add it to the gotcha list in `crossplatform-rebuild-plan.md` section 21 (one concise bullet, with the source URL or issue number).
- If it concerns compositor/rendering, add it to the "Avalonia v12 idiomatic confirmation" list in `unified-compositor-engine-plan.md` instead.
- If it invalidates something a doc claims, fix the doc. Code and official docs outrank stale repo docs.

Cite the sources you used in your response so the result is auditable.

## When to escalate

If web research contradicts existing project code, or no reliable v12 information exists, stop and say so before changing anything. Do not guess from v11 experience. The failure mode this skill exists to prevent is confidently applying a pre-v12 pattern that compiles but breaks at runtime.

## Example invocations

> "Why is this Avalonia Window not showing?"
> "How do I render video frames into a Skia surface in Avalonia?"
> "Which Avalonia dialog/toast library should I use?"
> "Can I make a window ignore mouse input on Linux?"

Each starts with Step 1 (local knowledge), then Step 2 (current v12 sources), never with offline assumptions.

## Related skills

- `unified-compositor-engine` - compositor architecture and its settled rendering facts
- `overlay-clickthrough` - settled click-through/topmost interop knowledge
- `port-feature` - the implementation workflow this research feeds into
