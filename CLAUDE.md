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
dotnet build CCP.Core/CCP.Core.csproj -c Release            # portability proof
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
| `{loc:Str key}` | a binding - `LocExtension` is a WPF `MarkupExtension` and stays in the head |

Three things that will bite, all found by rendering rather than by reading:

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

## Moving a file into Core

Pure `git mv`, zero content edits, namespace unchanged. Verify with `git diff -M --stat` showing a
rename at 100% similarity, then build both projects. If Core rejects the file, leave it in the head
and record why — a partial move is a correct outcome, not a failure.
