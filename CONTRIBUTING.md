# Contributing to Conditioning Control Panel

Thanks for helping. This page is the short version of how work lands in this repository. If something here is out of date, the CI workflow in `.github/workflows/build.yml` is the source of truth for the build.

## Development setup

### Prerequisites
- Windows 10 or 11, 64-bit
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Git
- Visual Studio 2022 (Community is fine) or VS Code with the C# Dev Kit

### Build and run

```bash
git clone https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git
cd Conditioning-Control-Panel---CSharp-WPF
dotnet build ConditioningControlPanel.sln -c Release -p:ValidateExecutableReferencesMatchSelfContained=false
dotnet run --project ConditioningControlPanel
```

The `ValidateExecutableReferencesMatchSelfContained=false` flag is required when the .NET 10 SDK is installed alongside 8: the app is self-contained and the test project references it without being self-contained, which SDK 10 otherwise rejects. Build the app project alone (`dotnet build ConditioningControlPanel/ConditioningControlPanel.csproj`) and the flag is not needed.

### Run the tests

```bash
dotnet test Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:ValidateExecutableReferencesMatchSelfContained=false
```

The suite has about 4,900 tests, including real WPF render tests, and runs headless in roughly a minute. CI runs it on every pull request and every push to `main`; a red run blocks the merge.

### Where things live

```
ConditioningControlPanel/          the WPF app
  App.xaml.cs                      startup, service wiring, crash handlers
  MainWindow/                      the main window, one partial class per tab
  Windows/, Dialogs/, Overlays/    secondary windows and screen overlays
  Services/                        one folder per subsystem (Flash, Video, Audio, Arcademy, Chaos, Descent, Mods ...)
  Models/                          settings, achievements, sessions, mod manifests
  Localization/Languages/          the nine UI language files
  Resources/                       art, audio, and the web games under Resources/web
  docs/                            design docs, primers, and audit snapshots
  tools/                           asset generation and indexing scripts
CCP.Core/                          platform-agnostic engine library (in progress)
Tests/ConditioningControlPanel.Tests/
installer.iss, build-installer.bat the Inno Setup release pipeline
```

`ConditioningControlPanel/CLAUDE.md` is the maintainer's working notes on the codebase. It is written for an AI assistant but it is the most current map of the project and worth a read.

## Making changes

### Branch names
- `feature/description` or `feat/description` for new features
- `fix/description` for bug fixes
- `docs/description` for documentation
- `refactor/description`, `chore/description` as appropriate

### Commit messages
[Conventional Commits](https://www.conventionalcommits.org/), with a scope where it helps:

```
feat(arcademy): add the records room
fix(flash): GIF frames stalled after monitor sleep
docs: rewrite CONTRIBUTING for the current layout
```

### Pull requests

1. Fork the repository and branch from `main`.
2. Keep each PR under about **600 changed lines**. Larger changes go in as a stack of smaller PRs that each build and pass on their own. This is a hard preference of the maintainer, not a suggestion: oversized PRs are sent back to be split.
3. Every PR needs a passing CI run and a review from the maintainer. `CODEOWNERS` enforces the review; `main` is protected.
4. Describe what changed and why, and say how you tested it. A screenshot or short clip helps for anything visual.

### PR checklist
- [ ] Builds with the command above and the test suite passes
- [ ] No new build warnings beyond the existing CA1416 platform warnings
- [ ] Follows the surrounding code style
- [ ] User-facing strings go through localization (add the key to all nine files in `Localization/Languages/`)
- [ ] Documentation updated if behaviour changed

## Code style

- C# 12, nullable enabled. Follow Microsoft's [C# coding conventions](https://learn.microsoft.com/dotnet/csharp/fundamentals/coding-style/coding-conventions).
- `async`/`await` for I/O. Fire-and-forget continuations must check `Application.Current?.Dispatcher` and catch exceptions; see the crash notes in `CLAUDE.md`.
- Services are static properties on `App` (`App.Flash`, `App.Video`, ...). Add a new one there and initialise it in `App.OnStartup`.
- Settings live in `Models/AppSettings.cs` with `[JsonProperty]` and auto-save.
- Never write a literal line break inside a language-file string; use `\n`. All nine files must stay strict JSON.
- Language files are LF in git. Do not commit a whole-file line-ending change.

## Reporting bugs

Bug reports go to the dedicated tracker: **https://github.com/CC-Labs-llc/ccp-bugs/issues**. Include your Windows version, the app version from the title bar, steps to reproduce, and the tail of `%LOCALAPPDATA%\ConditioningControlPanel\logs\crash.log` with anything personal removed.

Feature requests and questions are best raised in the [CC Labs Discord](https://discord.gg/YxVAMt4qaZ).

## License

By contributing you agree that your contributions are licensed under the MIT License.
