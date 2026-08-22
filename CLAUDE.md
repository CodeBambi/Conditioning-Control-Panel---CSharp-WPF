# Repository Guide

Read existing code and documentation before changing it. Verify APIs, package versions, flags,
commit identifiers, and behavior from the repository or primary documentation rather than guessing.
Keep changes focused, preserve unrelated working-tree changes, and do not commit or push unless the
owner explicitly asks.

## Product Trees

| Tree | Purpose | Framework | Solution |
|---|---|---|---|
| `ConditioningControlPanel/` | Shipping Windows-only WPF product | `net8.0-windows10.0.19041.0`, WPF and WinForms | `ConditioningControlPanel.sln` |
| `client/` | Greenfield Windows/Linux Avalonia client | `net10.0`, Avalonia 12.1.1 | `client/CcpClient.sln` |

Both .NET 8 and .NET 10 SDKs are required. Greenfield work treats the WPF product as read-only
behavioral evidence; the new implementation belongs under `client/` and should preserve outcomes,
not legacy architecture.

## Commands

### Shipping WPF Product

```powershell
dotnet build ConditioningControlPanel.sln
dotnet run --project ConditioningControlPanel/ConditioningControlPanel.csproj
dotnet test Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
```

The installer uses `build-installer.bat`, which publishes self-contained `win-x64`, pauses for
manual code signing, then builds `installer.iss`. Its `VERSION` must match the project version.

### Greenfield Client

Run the warning gate and exact test floor for changes requiring the standard client checks:

```powershell
node client/tests/floor/check-warnings.mjs
node client/tests/floor/check-floor.mjs
```

The warning gate uses a non-incremental build. Run it with `--cold` after project, property,
target, or lock-file changes. The floor runs both test projects, reads TRX output, and compares
against `client/tests/floor/floor.json`; bump a total only with its test change. Do not export
`CCP_DATA_ROOT` process-wide, because that invalidates a data-root isolation check.

Focused test commands remain useful during development:

```powershell
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo
dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo
```

Use `client/tools/gate/with-slot.mjs` to limit concurrent expensive build and test commands.

Headed verification commands:

```powershell
pwsh client/tools/verify/capture.ps1 -Surface rail-door -State selected
dotnet run --project client/tools/verify/CcpVerify/CcpVerify.csproj -- --capture <png|bmp> --surface rail-door --state selected
pwsh client/tools/verify/self-test.ps1
```

## Client Architecture

The greenfield product has one desktop project at `client/src/CcpClient.Desktop`. Its composition
root is constructor-injected: `App` and `MainWindow` intentionally have no parameterless
constructors and are not built by the runtime XAML loader. `client/Directory.Build.props` is the
single version authority and prevents MSBuild properties from leaking in from the repository root.

Web payloads (`dtrh`, `intake`, `tunnel`, and `vendor`) are linked read-only from the legacy tree
and copied to `payload/`; their source bytes remain owned by the legacy product. Several client
contracts are enforced by tests that read documentation at runtime, including
`UpstreamPayloadInventoryTests`, `AiOperationContractTests`, and `VersionDerivationTests`. Re-run
their relevant guards after changing a consumed document or JSON file.

## Greenfield Work

Read `docs/constitution.md`, `client/docs/port-workflow.md`, and the relevant task-board row before
greenfield work. The task board is the only live queue. Use isolated workspaces for concurrent
changes, make shared chokepoint ownership explicit, and do not impose a fixed workflow limit where
scope and available resources permit safe concurrency.

Visual, input, media, focus, scaling, and window claims need task-appropriate headed evidence.
Windows-only execution, a stub, a no-op, or a successful build never establishes Linux support.
New test waits must use deterministic signals or the shared bounded helper, never wall-clock polls
or bare sleeps.

## Conventions

- Use conventional commits: `feat:`, `fix:`, `docs:`, or `refactor:`.
- Localization files under `ConditioningControlPanel/Localization/Languages/` use escaped `\n` in
  strings and LF line endings. Avoid whole-file line-ending changes.
- Useful references include `GUIDE.md`, `MODDING.md`, `AI_AUDIT.md`,
  `ConditioningControlPanel/docs/`, and `client/docs/port-digest.md`.
