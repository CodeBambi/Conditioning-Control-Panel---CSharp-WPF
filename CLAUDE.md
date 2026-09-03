# Conditioning Control Panel — repository context

This file covers the whole repo. `ConditioningControlPanel/CLAUDE.md` covers the WPF head and stays
authoritative for everything inside it (release steps, known WPF issues, localization rules).

## Architecture

The app is being split so one engine serves every platform. Targets: Windows, Linux (any distro),
and VR (Quest 2/3, Steam Frame). The Windows app must look and behave exactly as it does today.

```
ConditioningControlPanel.sln
  CCP.Core/                    net8.0            <- the engine. No WPF, no WinForms, no Win32.
  ConditioningControlPanel/    net8.0-windows    <- WPF head (Windows)
  CCP.Avalonia/                net8.0            <- Avalonia head (Linux)
  CCP.VR/                      net8.0            <- VR head (Quest / Steam Frame)
  CCP.LinuxSmoke/              net8.0            <- headless proof that Core runs off Windows
  Tests/ConditioningControlPanel.Tests/          <- existing suite (Windows-only)
  Tests/CCP.Core.Tests/        net8.0            <- engine tests, run on Linux CI
```

A previous cross-platform attempt failed as a separate fork that went stale against a fast-moving
`main`. **Do not propose a fork.** Heads live in this repo and share `CCP.Core`, so a feature is
written once and every head gets it in the same release.

## Where new code goes

Business logic, rules, models, math, protocol, persistence shape → **`CCP.Core`**.
Anything that draws, owns a window, touches a device, or calls the OS → **the head**.

If logic needs a platform capability, put an interface in Core and implement it in each head. Do not
add a second copy of the logic — that is the exact failure this structure exists to prevent.

## Core invariants

`CCP.Core` targets plain `net8.0`, so `System.Windows.*` cannot resolve there. That is deliberate:
**the target framework is the architecture test.** Also:

- Core has **no** `global using System.Windows;`. The WPF head does, which means a file using a bare
  `Point`, `Rect`, `Color`, `Visibility` or `Thickness` compiles in the head and fails in Core. This
  is the most common reason a file that "looks portable" is not.
- Do not add `<NoWarn>CA1416</NoWarn>` to Core. The head suppresses it because it is deliberately
  Windows-only; Core is exactly where a Windows-only API must be loud.
- `RootNamespace` is `ConditioningControlPanel` in **both** projects, so namespaces are unchanged by
  a move and no consumer needs editing.
- Core carries `InternalsVisibleTo` for the app and the test project, so `internal` is not a blocker.

## Traps that compile clean and fail later

- **A `partial` class cannot span two assemblies.** If any partial of a class uses `App.` or WPF, the
  whole class is pinned to the head. No symbol scan reveals this.
- **`typeof(X).Assembly` / `GetManifestResourceStream`** change meaning when a type changes assembly.
  Move an embedded resource in the same PR as the class that reads it, or it silently returns null.
- **XAML `clr-namespace:` without `;assembly=`** resolves in the local assembly only, so a moved type
  referenced from XAML dies at runtime with `XamlParseException` — invisible to the compiler.
- **Tests that scan or read source by path** must use `SourceRoots` (in the test project), which
  probes every product root. A hardcoded `ConditioningControlPanel/...` path silently stops covering
  anything that moves to Core.
- **`App`** is a `System.Windows.Application` subclass used as a static service locator. Anything
  reading `App.X` cannot live in Core. Prefer `CorePaths` and Serilog's static `Log` over
  `App.UserDataPath` / `App.EffectiveAssetsPath` / `App.Logger`.

## Build

On Linux, building the Windows head works and is the normal way to verify a change:

```bash
export DOTNET_ROLL_FORWARD=Major                            # SDK 10 host, net8.0 projects
bash .github/scripts/core-guards.sh                         # the greps the compiler cannot do
dotnet build CCP.Core/CCP.Core.csproj -c Release            # portability proof
dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release    # the Linux head
dotnet build ConditioningControlPanel.sln -c Release \
    -p:EnableWindowsTargeting=true \
    -p:ValidateExecutableReferencesMatchSelfContained=false  # both flags required
```

`EnableWindowsTargeting` is needed for the `-windows` TFM off-Windows. The second flag is needed
because `ConditioningControlPanel.Tests` references the self-contained app without being
self-contained itself, which the .NET 10 SDK rejects with `NETSDK1151`. Do not "fix" that by editing
a `.csproj` — it changes what ships. Do not use `-p:SelfContained=true` either: `-p:` sets a *global*
property, so it also hits `Tests/CCP.Core.Tests`, which has no `RuntimeIdentifier` by design, and
that combination fails with `NETSDK1191`.

`DOTNET_ROLL_FORWARD=Major` is needed because no 8.0 runtime is installed alongside the .NET 10 SDK,
so `dotnet run` on any of these projects otherwise refuses to start. It affects running, not building.

Three runners prove behaviour rather than compilation, and each answers a different question:

```bash
dotnet run --project CCP.LinuxSmoke/CCP.LinuxSmoke.csproj -c Release          # Core, no head at all
dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --smoke   # head links + strings
dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --nav-check
```

CI runs the first two; `--nav-check` is local-only, and is the one check that exercises a *click*
rather than a frame — run it after touching a `MainShellWindow` partial or `NavCheck`.

**`CCP.LinuxSmoke` used to exit 0 even when an assertion printed `[FAIL]`.** Every check sat in a
block whose last line was `return 0`, so a broken seam printed a failure, incremented the counter and
still left the ubuntu job green — CI could not have caught a seam regression. Fixed at
`CCP.LinuxSmoke/Program.cs:163`, and worth recording as a shape: *a proof that cannot fail is not a
proof.* Any assertion added to any of these runners must be verified to actually fail when the thing
it asserts is broken — break it once on purpose and watch the exit code.

The Windows test suite cannot execute on Linux (`win-x64` testhost). Compile-verify locally; CI runs
it on `windows-latest`.

## Porting a WPF view to Avalonia

Measured on the first ported view (`Views/Tabs/AchievementsTabView`). The mapping:

| WPF | Avalonia |
|---|---|
| `Style="{StaticResource X}"` | `Theme="{StaticResource X}"` |
| `<Style x:Key TargetType>` | `<ControlTheme x:Key TargetType>` |
| `Visibility="Collapsed"` | `IsVisible="False"` |
| `Panel.ZIndex` | `ZIndex` |
| `{loc:Str key}` | `{loc:Str key}` unchanged, with `xmlns:loc="clr-namespace:ConditioningControlPanel.Avalonia.Localization"` (the Avalonia twin lives in the head; the strings come from Core) |

Things that will bite, all found by rendering or running rather than by reading:

1. **Avalonia's `Button` parses `_` in `Content` as an access key.** `btn_visit_patreon` renders as
   "btnvisit_patreon" with a stray underline. WPF only does this with `RecognizesAccessKey`, so it
   never bit the original - but every loc key here is snake_case. Put a `TextBlock` inside the
   button, which opts out.

2. **Read the code-behind before writing a binding.** `{loc:Str …}` covers only static strings;
   anything with a number in it is set from code with `Loc.GetF` and format arguments. Inventing a
   key name produces a plausible-looking string that is also structurally wrong.

3. **`EmojiToImageSource` is not needed on Avalonia.** It exists because "WPF's TextBlock can't
   render COLR/CPAL color fonts" (see `Helpers/EmojiImage.cs`), so the app ships Twemoji SVGs and
   renders them through SharpVectors from `pack://` URIs. Avalonia renders colour emoji natively -
   verified on Linux with Noto Color Emoji. That collapses ~103 converter usages, the SharpVectors
   dependency and those `pack://` URIs to a plain `<TextBlock Text="🔒"/>` on that head.
   `BoolToVisibility` (~27 usages) likewise disappears: Avalonia binds `IsVisible` to a bool directly.
   `helpers:EmojiTextBlock` (128 usages) is a `TextBlock` subclass for the same reason and becomes
   a plain `TextBlock`.

4. **A property-only `ControlTheme` on a templated control draws nothing.** A WPF keyed `<Style>`
   overrides setters and keeps the control's default template. An Avalonia `ControlTheme` replaces
   the theme wholesale, so a `ControlTheme` for `Button`, `CheckBox`, `Slider`, `ComboBox` etc. that
   carries only setters leaves `Template` null. It compiles clean and throws nothing. Give it a
   `Template`, or `BasedOn="{StaticResource {x:Type Button}}"`. `Border` and `TextBlock` are not
   templated, so property-only is safe there.

5. **Avalonia never re-polls `ICommand.CanExecute`.** WPF's `CommandManager.RequerySuggested`
   re-queried every command on each input event, so a `CanExecute` that reads mutable state just
   worked. Avalonia only re-evaluates when the command raises `CanExecuteChanged`. A ported command
   with dynamic enablement must raise it itself when that state changes, or the button stays in
   its first state forever. Found porting `ChatThresholdView`.

6. **A bare `<ContentPresenter/>` in an inline template draws nothing.** WPF's presenter finds its
   content implicitly; Avalonia's needs `Content="{TemplateBinding Content}"` (and usually
   `ContentTemplate="{TemplateBinding ContentTemplate}"`). Compiles clean, renders an empty
   button. Found porting `SeasonRecapCard`; every `ControlTheme` in `Theme/Styles.xaml` carries
   the binding for this reason.

7. **`AvaloniaXamlLoader.Load(this)` does not assign `x:Name` fields.** Only the generated
   `InitializeComponent()` does. `CCP.Avalonia/Views/Windows/MainShellWindow.axaml.cs:87` loads the
   first way, so every generated field on that window — `AppSettingsTab`, `StudioTab`,
   `MainTutorialOverlay` — is permanently null. `SomeTab?.DoThing()` therefore compiles, renders,
   reviews clean and is a silent no-op forever: the `?.` reads as a safe guard and is actually the
   bug hiding. Found by a `--nav-check` crash, not by reading. On that window the only sanctioned
   way for any partial to reach a control is the `Named<T>(name)` helper in
   `MainShellWindow.TabNavigation.cs`; never a bare `x:Name` field. In any NEW file, call
   `InitializeComponent()`. The same trap had already produced a NullReferenceException in
   `PerformanceSettingsSection`, so this is the second time it has bitten.

Two more that bite on setting text from code, both because Avalonia keeps a binding alive under a
local value where WPF would have cleared it: assigning `.Text` to a control that carries
`{loc:Str}` is undone on the next language change, so choose the key in code and bind it instead;
and a `$parent[Window]` binding inside a dialog resolves to whatever Window actually hosts it.

Static strings use `{loc:Str key}` with `xmlns:loc="clr-namespace:ConditioningControlPanel.Avalonia.Localization"`
- the Avalonia twin of the WPF extension, backed by the same `LocalizationManager` in Core.
Formatted strings still come from `Loc.GetF` in code-behind, keys and argument order read from the
WPF code-behind that populates the same control.

**Definition of done for a ported view:** it renders headless on Linux with real strings and no
blank templated controls. Prove it per view, not per app:

```bash
dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --render-view <TypeName> out.png
dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release -- --render-all <dir>   # what CI uploads
```

Read the PNG. A raw `snake_case` key, an empty region where a button sits, or a swallowed
underscore is a failed port, whatever the compiler said. There is no WPF render on Linux, so
fidelity against the original is not what this proves; drawing, strings and templates are.
`--render-all` fails if any one view throws, which is why CI runs it and uploads the frames.

### Restoring the logic a ported view is missing

Each view was copied across with its logic cut out and a `// ponytail:` note left in its place
naming what was missing. **A `ponytail:` note is a claim about the past, not the present.** Most say
"wired when X moves to Core" and X moved layers ago — four waves of restoration found the majority of
them stale. The procedure is always the same: grep `CCP.Core` for what the note names and believe the
grep, never the note.

The stub *count* is not a progress bar either. One wave that made thirteen feature cards work raised
it from 1,267 to 1,288, because restoring a view turns one vague note into four precise ones. Count
files still carrying notes, or count distinct named blockers:

```bash
grep -rl "ponytail:" CCP.Avalonia --include="*.cs" --include="*.axaml" | wc -l
```

## What actually remains in the UI

Measured across all 183 `.xaml` files and their code-behind (131,562 LOC), bucketed by what blocks
each one. "192 views to port" is the wrong mental model - they are not uniformly hard:

| Bucket | Files | % | LOC | What it needs |
|---|---:|---:|---:|---|
| **A. straight port** | 62 | 33% | 23,604 | Nothing. The mapping above applies. |
| **B. custom control first** | 44 | 24% | 31,977 | Its `fx:`/`cmp:`/`helpers:` control ported first. |
| **D. WebView2-hosted** | 12 | 6% | 29,435 | `Avalonia.Controls.WebView` (12.x only - the reason the head targets 12). |
| **E. Win32 / layered window** | 65 | 35% | 46,546 | Per-platform reimplementation, or dropping where the platform forbids it. |

Bucket E is the Chaos overlays, the compositor and the click-through transparent windows. Those are
not ports - Wayland and Quest do not permit desktop-wide always-on-top click-through surfaces at all.
Plan to reimplement per platform or to lose the feature there, and decide which before starting.

Bucket A is where to work first: a third of the UI, and the procedure is already written down.

## Moving a file into Core

Pure `git mv`, zero content edits, namespace unchanged. Verify with `git diff -M --stat` showing a
rename at 100% similarity, then build both projects. If Core rejects the file, leave it in the head
and record why — a partial move is a correct outcome, not a failure.

**The guard scan does not tell you whether a file can move.** `core-guards.sh` greps `CCP.Core/` only
— it never reads a candidate still sitting in the head — and even then it counts *forbidden APIs*,
not *type coupling*. Copy `ModService.cs` into Core and the guard flags exactly one line
(`App.Settings`), while the compiler finds ten head-only types it has no pattern for
(`AvatarTubeWindow`, `SessionEngine`, `UpdateService`, `BambiSprite`, …). So dry-run the move — copy
the file in, build Core, read the errors, delete the copy — *before* promising the move in a layer.
That is thirty seconds and it is the only honest estimate.

## Where the art lives

`/Assets` at the repo root, not inside any head. Every head LINKS what it needs with `Link=` so the
logical path stays the same on all of them; nobody copies bytes and no head depends on another to
get a picture. See `Assets/README.md` for the item shapes and for what the Avalonia head
deliberately leaves out.

## Stacked PRs

Every change to this repo lands as a layer of the port's stacked-PR chain, never as a direct push
to `main`. One-time setup per clone:

```bash
gh extension install github/gh-stack
gh skill install github/gh-stack gh-stack --agent claude-code   # agent instructions, gitignored under .claude/
git config rerere.enabled true
```

A layer is one concern, ≤ ~1,000 changed lines (rename-aware), and builds and renders on its
own. Use the non-interactive forms only: `gh stack view --json`, `gh stack submit --auto`,
`gh stack merge <pr> --yes`.

## Agent skills

### Issue tracker

GitHub Issues on this repo, via `gh`. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical labels, unchanged (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `CONTEXT.md` at the repo root and ADRs in `docs/adr/`. See `docs/agents/domain.md`.
