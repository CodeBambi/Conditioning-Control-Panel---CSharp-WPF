# CCP Portability Assessment — OpenSpec Bundle

Generated 2026-06-15. A spec-driven portability assessment of the Conditioning Control Panel
(WPF + WinForms, .NET 8, Windows-only). Eight recon agents mapped every subsystem into OpenSpec
capability specs and classified each **Portable / Mixed / OS-Specific**.

## Start here

1. **[PORTABILITY_REPORT.md](PORTABILITY_REPORT.md)** — the consolidated answer: size, headline
   verdict, per-cluster table, cross-cutting seams, dead deps, and the phased plan. **Read this first.**
2. **[PORTABILITY_RUBRIC.md](PORTABILITY_RUBRIC.md)** — the classification rules + the OpenSpec
   capability-spec template used throughout.

## Per-subsystem OpenSpec specs (`specs/`)

Each file contains capability specs with `SHALL` requirements + `WHEN/THEN` scenarios, and a
Portable/Mixed/OS-Specific classification + blocking dependency + extraction seam per capability.

| Spec | Subsystem | Portable |
|---|---|---|
| [01-core-lifecycle.md](specs/01-core-lifecycle.md) | App bootstrap, settings, update, tray, hooks, hotkeys, wallpaper | ~10–15% |
| [02-visual-overlays.md](specs/02-visual-overlays.md) | Flash, subliminal, overlays, brain-drain, chaos render layer | ~25–30% |
| [03-media.md](specs/03-media.md) | Video (LibVLC), audio (NAudio), ducking, bubble-count | ~30–35% |
| [04-ai-companion.md](specs/04-ai-companion.md) | Sessions, AI orchestration, autonomy, bark, personality | ~65–70% |
| [05-gamification.md](specs/05-gamification.md) | XP, quests, achievements, skill tree, Chaos economy | ~70–75% |
| [06-networking-auth.md](specs/06-networking-auth.md) | OAuth, V2 auth, profile sync, content packs, mods, secret stores | ~70–75% |
| [07-sensors-io.md](specs/07-sensors-io.md) | Webcam/gaze, haptics, OCR, window awareness | ~40% |
| [08-ui-shell.md](specs/08-ui-shell.md) | MainWindow, dialogs, viewmodels, models, localization | ~12% |

## Verdict in one line

The **engine ports** (~70% of the IP-bearing logic, behind ~6 interfaces dominated by one — the
dispatcher/timer seam); the **~133K-LOC WPF UI is a rewrite**, and several signature features
(global hooks/lockdown, wallpaper, DWM, WebView2, screen capture, WASAPI ducking) are
irreducibly Windows. Recommended: extract a `CCP.Core` library regardless; pursue a full
cross-platform UI only on real demand.

## Real codebase size

~202,500 LOC C# + 30,369 XAML (excludes the three `.claude/worktrees/` repo copies that inflate
naive counts ~4×).
