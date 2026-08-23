# Checkpoint plan — open/save seam + phrase backup (census #9)

Branch `worktree-agent-a508040de3bd8c489`, worktree
`.claude/worktrees/agent-a508040de3bd8c489`. Scaffolding only; removed before the final report.

## Research settled (primary sources, all read this session)

- API: `TopLevel.StorageProvider` → `IStorageProvider`, `SaveFilePickerAsync(FilePickerSaveOptions)`
  / `OpenFilePickerAsync(FilePickerOpenOptions)`. Sources: docs.avaloniaui.net
  `/docs/services/file-dialogs`, `/docs/services/storage/storage-provider`,
  `/docs/services/storage/file-picker-options` (fetched 2026-08-24) and the shipped
  `avalonia/12.1.1/ref/net8.0/Avalonia.Base.xml` doc comments.
- `TopLevel.StorageProvider` is NON-nullable and falls back to `NoopStorageProvider`
  (`TopLevel.cs:521-524`, tag 12.1.1) whose `CanOpen`/`CanSave` are false and whose pickers return
  empty. So the seam MUST probe `CanSave`/`CanOpen` or a missing backend is a silent no-op.
- Linux: `UsePlatformDetect()` → `UseX11()` on Linux (`AppBuilderDesktopExtensions.cs:30-34`).
  `X11Window.cs:280-292` builds `FallbackStorageProvider` over, in order, `DBusSystemDialog`
  (xdg-desktop-portal, when `X11PlatformOptions.UseDBusFilePicker`), `GtkSystemDialog`, then
  `ManagedStorageProvider` — so an X11 desktop always has a picker. The native Wayland backend
  (opt-in only) exposes no storage provider (`WindowImplBase.TryGetFeature` handles IScreenImpl,
  IClipboard, ILauncher only) → Noop → typed refusal.
- Doc-vs-source discrepancy: the website calls the save result member `StorageFile`; 12.1.1 ships
  `SaveFilePickerResult.File`. Avoided by using `SaveFilePickerAsync`.
- Win32 passes `DefaultExtension` straight to `IFileSaveDialog::SetDefaultExtension`
  (`Win32StorageProvider.cs:142-147`) → no leading dot.
- `BclStorageItem.OpenWriteCore` is `FileMode.Create` (truncating) — but that is not an interface
  guarantee, so the seam truncates itself.

## Upstream (read)

`Services/PhraseBackupService.cs` (180) + `MainWindow/MainWindow.PresetIO.cs:62-135` (export/import
buttons). Schema `ccp-phrases/v1` (`:24`), 17 whitelisted pool names (`:32-49`), envelope
schema/exported_at/app_version/phrases (`:72-78`), Validate (`:90-107`), Import REPLACES and
tolerates one bad member (`:109-152`), CountEntries (`:155-178`), confirm-then-import order
(`PresetIO.cs:107-122`).

## Shape

- `client/src/CcpClient.Desktop/Storage/UserFilePicker.cs` — seam interface + typed outcomes, and
  the doc comment carrying the four constraints.
- `client/src/CcpClient.Desktop/Storage/StoragePickerOptions.cs` — pure option builders (never sets
  `SuggestedStartLocation`).
- `client/src/CcpClient.Desktop/Storage/AvaloniaUserFilePicker.cs` — the real `IStorageProvider`
  implementation. Text in, text out; no path, no file name, no folder ever crosses the boundary.
- `client/src/CcpClient.Desktop/Session/PhraseBackupFile.cs` — envelope build/parse, upstream's
  schema and pool names so a file moves BOTH ways between the WPF product and this client.
- `client/src/CcpClient.Desktop/Session/PhraseBackup.cs` — the consumer over the three phrase
  documents (`SubliminalPresetDocument`, `LockCardPresetDocument`, `BouncingTextPresetDocument`).
- `client/tests/CcpClient.Tests/PhraseBackupTests.cs` — ~20 facts, every one mutation-checked.

## Known scope edge (report as a discovery, do not widen)

No user-reachable button ships in this slice: the natural home is `Views/Pages/SystemPage.axaml`
(upstream's Settings → Data lives on WPF's Home "System / App Info & Data" row, which is where this
port put that class of surface) and that file is outside this packet's File Scope.
