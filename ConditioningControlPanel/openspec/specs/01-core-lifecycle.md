# Cluster 01 — Core App & Lifecycle / System Integration

Generated 2026-06-15 against HEAD (v6.1.4). Classification per `openspec/PORTABILITY_RUBRIC.md`.

This cluster covers application bootstrap, the static service registry, single-instance/file-open
handoff, settings persistence, auto-update, OS integration (tray, global hooks/hotkeys, wallpaper,
title-bar chrome), in-app notifications, the hang watchdog, performance tiering, and security/log
utilities.

Source LOC verified via `wc -l`. Worktree copies under `.claude/worktrees/` were ignored.

---

## Capability: Application Bootstrap & Lifecycle
**Files:** App.xaml.cs (2720 LOC), App.xaml (30 LOC), GlobalUsings.cs (6 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** WPF `System.Windows.Application`/`Window`/`MessageBox`/`SplashScreen`/`Dispatcher`;
WinForms `System.Windows.Forms.Screen` (screen enumeration cache); Win32 P/Invoke `user32.dll`
`GetWindowRect`/`IsWindowVisible` (CCP self-window-rect cache for OCR self-exclusion);
`Microsoft.Win32.Registry` (`ApplyInstallerAssetsPath` reads `Software\CodeBambi\...\AssetsPath`);
`System.Media.SystemSounds` (achievement sound); WPF `WindowInteropHelper`; XAML resource
dictionaries (`App.xaml`); `Environment.Exit` hard-shutdown.
**Seam (if MIXED):** n/a — this is the WPF `Application` subclass and the composition root. Its
*ordering logic* (the service-init sequence) and the bootstrap concerns below (single-instance,
file-open handoff, asset migration) are individually extractable, but `App` itself is the WPF entry
point and cannot run off-Windows. A re-platform reimplements this class against the new UI toolkit
while reusing the extracted concerns.

### Requirement: The system SHALL initialize all services in a defined order on startup.
The system SHALL construct logging (Serilog), settings, localization, the mod system, then core
feature services, then auth/update/optional services, then the main window, in the fixed order in
`OnStartup`, surfacing progress on a splash screen.

#### Scenario: Cold start
- WHEN the executable is launched with no other instance running
- THEN a splash shows, services initialize in order, the main window is shown, and deferred dialogs
  (age verification, update, cloud-restore) run after the splash closes.

### Requirement: The system SHALL catch and log unhandled exceptions instead of crashing.
The system SHALL register `DispatcherUnhandledException`, `AppDomain.UnhandledException`, and
`TaskScheduler.UnobservedTaskException` handlers, write full stack traces to `logs/crash.log`, and
hard-exit immediately on render-thread failure / OOM (HResult `0x88980406` or `OutOfMemoryException`)
to prevent a dialog cascade.

#### Scenario: Render-thread failure
- WHEN a `0x88980406` or OOM exception reaches the dispatcher handler
- THEN the app logs the failure and calls `Environment.Exit(1)` without attempting any UI.

### Requirement: The system SHALL clean up all services and persist state on exit.
The system SHALL, in `OnExit`, flush settings immediately, best-effort cloud-sync, dispose every
service in dependency-safe order, clear in-memory secrets, release the single-instance mutex, flush
the logger, and `Environment.Exit(0)`.

#### Scenario: Normal exit
- WHEN the application shuts down
- THEN settings are saved before cloud sync, all `IDisposable` services are disposed, and the process
  terminates cleanly with no lingering background threads.

---

## Capability: Single-Instance Guard & "Open With" File Handoff
**Files:** App.xaml.cs (single-instance + file-open region)
**Class:** MIXED
**Blocking deps:** `System.Threading.Mutex` (named) and `EventWaitHandle` (named) are cross-platform
on .NET (named sync primitives work on Linux/macOS via filesystem-backed handles). The handoff *replay*
path calls into WPF (`MainWindowRef.ShowFromTray`, dispatcher) — that part is UI-bound.
**Seam (if MIXED):** The mutex/named-event mechanism and the handoff file (`fileopen.pending` in
LocalAppData, parsing `--play`/`--edit` args with extension allowlist) are portable; extract the
"on second instance, foreground + replay file action" callback behind an interface so the portable
guard fires a `Action<string action, string path>` that the UI layer implements.

### Requirement: The system SHALL allow only one running instance.
The system SHALL acquire a named mutex on startup; a second launch SHALL signal the first via a named
event and then exit.

#### Scenario: Second launch
- WHEN the app is launched while an instance is already running
- THEN the new process writes any pending file-open handoff, signals the existing instance to show its
  window, and exits.

### Requirement: The system SHALL route "Open with" media files to the running instance.
The system SHALL parse `--play <path>` / `--edit <path>` args (validated against a local-path,
extension allowlist), and either dispatch directly (first instance) or via a handoff file the listener
replays on the dispatcher (second instance).

#### Scenario: Open-with on running app
- WHEN a user opens a `.mp4` via the app while it is minimized to tray
- THEN the path is validated, handed off to the running instance, the window is restored, and the
  player/editor opens for that file.

---

## Capability: Asset / Temp / Data Path Management & Migration
**Files:** App.xaml.cs (UserDataPath, EffectiveAssetsPath, CleanupStaleTempFiles, MigrateAssets*)
**Class:** PORTABLE
**Blocking deps:** none — pure `System.IO` + `Environment.GetFolderPath(LocalApplicationData)`
(resolves to `~/.local/share` on Linux, `~/Library/Application Support` on macOS) + `AppDomain.BaseDirectory`.
**Seam (if MIXED):** n/a.
**Note:** `ApplyInstallerAssetsPath` (registry read) belongs to the Bootstrap capability, not here.

### Requirement: The system SHALL resolve user data, assets, and temp paths and migrate legacy assets.
The system SHALL expose `UserDataPath`/`UserAssetsPath`/`EffectiveAssetsPath`, clean stale
`ccp_temp_*`/`haptic_video_*`/update-download files on startup, and one-shot-migrate assets from old
install-dir locations into the persistent user folder.

#### Scenario: Update with legacy in-folder assets
- WHEN the app starts and finds `assets/` in an old install/version directory and no custom path is set
- THEN it copies images/videos/spirals into the user data folder without overwriting existing files.

---

## Capability: Settings Persistence
**Files:** Services/SettingsService.cs (456 LOC), Models/AppSettings.cs (4332 LOC)
**Class:** PORTABLE
**Blocking deps:** none in these files — `AppSettings` is a pure `INotifyPropertyChanged` POCO
serialized with Newtonsoft.Json; `SettingsService` is JSON file I/O with debounced writes, migration,
and cloud backup. The only Windows touchpoints are *delegated out*: `OpenRouterApiKey`/`AuthToken`
proxy to `SecureApiKeyStore`/`SecureAuthTokenStore` (DPAPI — see Secure Token Storage capability,
outside this cluster), and monitor identifiers are stored as Windows `Screen.DeviceName` strings
(a data-schema concern, not a code dependency).
**Seam (if MIXED):** Already clean. The DPAPI dependency is isolated behind the secure-store services;
those need an `ISecureStore` abstraction (Windows DPAPI / Linux libsecret / macOS Keychain). Monitor
device-name strings need a one-time schema migration off-Windows.

### Requirement: The system SHALL load, migrate, and persist user settings as JSON.
The system SHALL load `settings.json` from the user data folder (flagging fresh installs), run one-shot
migrations, debounce-save changes to disk, and expose `SaveImmediate` for shutdown flushes.

#### Scenario: Fresh install
- WHEN no settings file exists at startup
- THEN defaults are created, `WasSettingsFileMissing` is set true (enabling the cloud-restore offer),
  and a new file is written.

### Requirement: The system SHALL merge updated built-in presets on launch.
The system SHALL detect version-bumped built-in Awareness/keyword presets and queue them for re-install
so live trigger lists pick up changes.

#### Scenario: Preset version bump
- WHEN a built-in preset's `Version` increased since last launch
- THEN the preset id is queued in `PendingPresetReinstalls` and re-cloned into the live trigger list.

---

## Capability: Auto-Update
**Files:** Services/UpdateService.cs (1098 LOC), Models/UpdateInfo.cs (37 LOC)
**Class:** MIXED
**Blocking deps:** GitHub Releases API check is portable HTTP. The **install** path is Windows-bound:
`Microsoft.Win32.Registry` reads `Software\CodeBambi\Conditioning Control Panel` (InstallPath/Version,
`IsInstalledViaInstaller`); the downloaded asset is an Inno Setup `Setup.exe` run via
`ProcessStartInfo { UseShellExecute = true }` with Inno args
(`/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS`); `wmic` is shelled out
to find/kill `msedgewebview2.exe`. `UpdateInfo` itself is a pure POCO (PORTABLE).
**Seam (if MIXED):** Extract `IUpdateInstaller { bool IsInstalledViaInstaller; Task<string> DownloadAsync();
void RunAndExit(string path); }`. The version check (`CheckForUpdatesAsync` → GitHub API + semver compare)
stays portable; per-OS installers (Inno `.exe` on Windows, AppImage/zsync on Linux, DMG/pkg on macOS)
implement the interface. Replace registry install-detection with a file-based marker off-Windows.

### Requirement: The system SHALL detect newer releases via the GitHub Releases API.
The system SHALL query the latest GitHub release, compare its tag to `AppVersion`, and report whether a
newer version exists (honoring a 24h skip unless force-checked).

#### Scenario: Newer release available
- WHEN the latest GitHub tag is greater than `AppVersion`
- THEN an `UpdateInfo` with `IsNewer = true` is returned and the in-app update button/dialog is surfaced.

### Requirement: The system SHALL download and apply an update in place (Windows).
The system SHALL download the release `Setup.exe` and, for Inno-installed copies, run it silently and
exit so it upgrades the existing install and restarts the app.

#### Scenario: User accepts a silent update
- WHEN the user confirms the update and the install was made via the Inno installer
- THEN windows are hidden, the installer downloads, WebView2 processes are killed, and the installer runs
  silently while the app exits.

---

## Capability: Windows Startup Registration
**Files:** Services/StartupManager.cs (207 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** COM Interop `IShellLink`/`IPersistFile` (`ShellLink` CLSID
`00021401-0000-0000-C000-000000000046`) to write a `.lnk` shortcut into
`Environment.SpecialFolder.Startup`. The Startup-folder shortcut approach is intentionally
registry-free (AV-friendly) but is wholly a Windows shell concept.
**Seam (if MIXED):** Extract `IAutostartProvider { bool IsRegistered(); bool SetEnabled(bool); }` —
Windows writes a `.lnk`; Linux writes a `.desktop` to `~/.config/autostart`; macOS writes a
LaunchAgent plist. Call sites only use `SyncWithSettings`/`SetStartupState`, so the surface is small.

### Requirement: The system SHALL optionally launch with the OS session.
The system SHALL create/remove a Startup-folder shortcut (with `--startup` arg) to match the user's
"run at login" setting, and reconcile actual state to settings on launch.

#### Scenario: Enable run-at-login
- WHEN the user enables start-with-Windows
- THEN a shortcut to the executable is written to the Startup folder; disabling deletes it.

---

## Capability: System Tray Integration
**Files:** Services/TrayIconService.cs (194 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** WinForms `System.Windows.Forms.NotifyIcon`, `ContextMenuStrip`, `ToolStripMenuItem`,
`ToolTipIcon`; `System.Drawing.Icon`/`SystemIcons`; `user32.dll SetForegroundWindow`; WPF
`WindowInteropHelper`. Entirely WinForms/Win32 — no abstraction present.
**Seam (if MIXED):** n/a (full rewrite per platform). If abstracted later:
`ITrayIcon { void Show(); event ShowRequested; event ExitRequested; void Notify(text); }`.

### Requirement: The system SHALL provide a tray icon with show/wake/exit actions.
The system SHALL show a tray icon while minimized, with a context menu (Show, Wake Bambi, Exit) and
balloon notifications, restoring/foregrounding the main window on activation.

#### Scenario: Restore from tray
- WHEN the user double-clicks the tray icon or selects Show
- THEN the main window is restored and foregrounded via `SetForegroundWindow`.

---

## Capability: Global Hotkey Registration
**Files:** Services/GlobalHotkeyService.cs (146 LOC)
**Class:** MIXED
**Blocking deps:** `user32.dll RegisterHotKey`/`UnregisterHotKey`; WPF `HwndSource.AddHook` +
`WindowInteropHelper` to receive `WM_HOTKEY`; `KeyInterop.VirtualKeyFromKey`. The dispatch logic
(invoke a callback when the combo fires) is platform-agnostic.
**Seam (if MIXED):** `IHotkeyProvider { bool Register(ModifierKeys, Key, Action); void Unregister(); }` —
Windows uses `RegisterHotKey` + Hwnd hook; Linux X11/Wayland grabs; macOS Carbon/Cocoa hotkeys. App falls
back to in-app key binding when the OS rejects the combo, so a no-op provider is acceptable.

### Requirement: The system SHALL register a system-wide hotkey to summon companion chat.
The system SHALL register a global hotkey (e.g. Ctrl+Alt+C) that triggers the avatar chat input from any
window, falling back to an in-app binding if the OS rejects registration.

#### Scenario: Global hotkey pressed
- WHEN the registered combo is pressed while another app has focus
- THEN the companion chat input is invoked.

---

## Capability: Low-Level Global Keyboard Hook (Lockdown)
**Files:** Services/GlobalKeyboardHook.cs (140 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** `user32.dll SetWindowsHookEx(WH_KEYBOARD_LL=13)`, `UnhookWindowsHookEx`,
`CallNextHookEx`, `GetAsyncKeyState`; `kernel32.dll GetModuleHandle`; `KeyInterop.KeyFromVirtualKey`;
hardcoded VK codes / `WM_KEYDOWN`/`WM_SYSKEYDOWN`. A system-wide low-level keyboard hook is a Win32
primitive with no portable equivalent.
**Seam (if MIXED):** n/a — full rewrite. Linux needs an evdev/X11 grab (typically root/privileged);
macOS needs a CGEventTap with accessibility permission. Behavior may not be reproducible cross-platform.

### Requirement: The system SHALL intercept system keys during lockdown.
The system SHALL install a low-level keyboard hook that detects/suppresses Windows keys, Alt+Tab,
Alt+F4, Escape, and Ctrl+Shift+Esc and raise key events for lockdown enforcement.

#### Scenario: Lockdown active
- WHEN lockdown is engaged and the user presses Alt+Tab
- THEN the keypress is suppressed and a `KeyPressed` event is raised.

---

## Capability: Low-Level Global Mouse Hook (Chaos Ripple)
**Files:** Services/GlobalMouseHook.cs (106 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** `user32.dll SetWindowsHookEx(WH_MOUSE_LL=14)`, `UnhookWindowsHookEx`,
`CallNextHookEx`; `kernel32.dll GetModuleHandle`; `MSLLHOOKSTRUCT`; `WM_RBUTTONDOWN`. Win32-only.
**Seam (if MIXED):** n/a — full rewrite (CGEventTap on macOS, evdev/X11 on Linux).

### Requirement: The system SHALL intercept desktop right-clicks for the Ripple effect.
The system SHALL install a low-level mouse hook that reports right-button-down events anywhere on the
desktop (with screen coordinates) and can optionally swallow the click.

#### Scenario: Right-click during Chaos Ripple
- WHEN the Ripple effect is armed and the user right-clicks the desktop
- THEN a callback fires with the cursor position and the click may be consumed.

---

## Capability: Desktop Wallpaper Override
**Files:** Services/WallpaperService.cs (183 LOC)
**Class:** MIXED
**Blocking deps:** `user32.dll SystemParametersInfo(SPI_GETDESKWALLPAPER/SPI_SETDESKWALLPAPER)` for
get/set/restore of the desktop wallpaper. Folder scanning, format filtering, randomization, and
save/restore lifecycle are portable.
**Seam (if MIXED):** `IWallpaperProvider { string GetCurrent(); bool SetWallpaper(string path); }` —
Windows uses `SystemParametersInfo`; Linux uses gsettings/dconf or DE-specific tools; macOS uses
osascript/Cocoa. The pool/randomization/restore logic stays platform-independent.

### Requirement: The system SHALL temporarily replace and later restore the desktop wallpaper.
The system SHALL save the current wallpaper, set a random image from the `wallpapers/` folder while
active, and restore the original on deactivation/exit.

#### Scenario: Wallpaper effect toggled
- WHEN the wallpaper override activates
- THEN the current wallpaper is recorded and a random pool image is applied; deactivation restores it.

---

## Capability: OS Title-Bar Chrome Theming
**Files:** Services/WindowChromeHelper.cs (116 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** `dwmapi.dll DwmSetWindowAttribute` with `DWMWA_USE_IMMERSIVE_DARK_MODE` /
`DWMWA_BORDER_COLOR` / `DWMWA_CAPTION_COLOR` / `DWMWA_TEXT_COLOR`; WPF `WindowInteropHelper` for the
HWND; `System.Windows.Application`/`Window`. DWM caption tinting is a Win10 1809+ feature with no
Linux/macOS equivalent. (The luminance→text-color math is reusable; the DWM calls are not.)
**Seam (if MIXED):** n/a — the whole capability is the Windows DWM feature; off-Windows it is a no-op.

### Requirement: The system SHALL tint OS window captions to match the active mod theme.
The system SHALL apply dark-mode and mod-accent caption/border/text colors to every window's non-client
area via DWM, re-applying on mod change.

#### Scenario: Mod switch
- WHEN the active mod changes
- THEN open windows' title bars are re-tinted to the new accent color.

---

## Capability: In-App Notifications (Toasts)
**Files:** Services/NotificationService.cs (267 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** WPF UI — `Border`/`Grid`/`TextBlock`/`Button`/`StackPanel`, `Brush`/`Color`,
`DispatcherTimer`, `System.Windows.Media.Animation` (`DoubleAnimation`, easing),
`DropShadowEffect`. Hosts toast visuals in MainWindow's root grid.
**Seam (if MIXED):** The pending-queue, sticky-dismiss persistence, and content model are extractable
(`INotificationHost` for the rendering), but rendering/animation are wholly WPF.

### Requirement: The system SHALL show non-blocking in-app toast notifications.
The system SHALL render queued toast banners (fade in/out) attached to the main window, supporting
optional persistent "sticky" toasts whose dismissal is remembered across sessions; calls before the
host attaches are queued and replayed on `MainWindow.Loaded`.

#### Scenario: Toast before window ready
- WHEN a notification is requested before the host grid is attached
- THEN it is enqueued and replayed once the main window loads.

---

## Capability: UI Hang Watchdog
**Files:** Services/UiHangWatchdog.cs (156 LOC)
**Class:** OS-SPECIFIC
**Blocking deps:** Heartbeat detection uses WPF `Dispatcher` (`HasShutdownStarted`, posted beats).
The dump *response* is Windows-only: `dbghelp.dll MiniDumpWriteDump`, plus a `rundll32 comsvcs.dll
MiniDump` external-dump fallback. Win32 `Process` handles.
**Seam (if MIXED):** Detection loop (post heartbeat every 2s, alarm after 10s of silence) is portable;
extract `IDumpWriter { void WriteDump(string path); }` — Windows minidump, Linux `gcore`/core, macOS
`lldb`. Detection also needs an abstract "UI thread liveness" probe off WPF.

### Requirement: The system SHALL detect UI-thread hangs and capture a diagnostic dump.
The system SHALL post heartbeats to the dispatcher every 2s and, if the UI thread is unresponsive for
10s, write one minidump per session to the logs folder for post-mortem analysis.

#### Scenario: Render-thread deadlock
- WHEN the dispatcher fails to process a heartbeat for 10s
- THEN a minidump is written (preferring the external `rundll32 comsvcs.dll MiniDump`).

---

## Capability: Performance Tiering
**Files:** Services/PerformanceProfile.cs (99 LOC)
**Class:** MIXED
**Blocking deps:** Only `System.Windows.Media.BitmapScalingMode` (a WPF enum returned by
`ScalingMode`). All other knobs (decode dimension, glow allow/blur radius, Brain Drain FPS/downscale,
tier selection from live heavy-element count) are pure logic.
**Seam (if MIXED):** Replace the `BitmapScalingMode` return with a tier→neutral-enum mapping the UI
layer translates; everything else is portable as-is. Tiny surface.

### Requirement: The system SHALL select a rendering tier and expose per-tier quality knobs.
The system SHALL resolve a `Quality`/`Balanced`/`Performance` tier from settings (forced, auto by live
heavy-element count, or quality) and expose per-tier decode/scaling/glow/Brain-Drain parameters.

#### Scenario: Auto escalation under load
- WHEN auto-performance is on and active heavy elements exceed the performance threshold
- THEN `CurrentTier` returns `Performance` and the per-tier knobs tighten accordingly.

---

## Capability: Safe Dispatcher Helpers
**Files:** Helpers/DispatcherHelper.cs (62 LOC)
**Class:** MIXED
**Blocking deps:** WPF `System.Windows.Application.Current.Dispatcher` / `DispatcherPriority`
(`CheckAccess`/`BeginInvoke`/`InvokeAsync`/`Invoke`, `HasShutdownStarted`). The marshal-to-UI-thread
pattern with null/shutdown guards is universal.
**Seam (if MIXED):** `IUiDispatcher { void RunOnUI(Action); Task RunOnUIAsync(Action); void RunOnUISync(Action); }`
— WPF/Avalonia/GTK implementations; the guard logic moves to a shared base.

### Requirement: The system SHALL marshal work to the UI thread safely.
The system SHALL provide fire-and-forget, awaitable, and synchronous UI-thread dispatch that no-ops
during shutdown and executes inline when already on the UI thread.

#### Scenario: Dispatch during shutdown
- WHEN a UI dispatch is requested after the dispatcher has begun shutting down
- THEN the call silently no-ops instead of throwing.

---

## Capability: Log Scrubbing (PII Redaction)
**Files:** Services/LogScrubber.cs (176 LOC)
**Class:** PORTABLE
**Blocking deps:** none — pure `System.Text.RegularExpressions` + `DateTimeOffset`. Redacts user paths,
emails, OAuth/bearer/Discord tokens, `%APPDATA%`-style vars, and normalizes timestamps to UTC-minute.
The Windows `C:\Users\<name>` path regex matches both slash forms and is harmless off-Windows.
**Seam (if MIXED):** n/a.

### Requirement: The system SHALL redact PII from bug-report payloads.
The system SHALL apply a fixed regex set to remove home paths, emails, tokens, and env-var references,
normalize timestamps, and return per-category redaction counts.

#### Scenario: Scrub a log
- WHEN a log string containing a user path and an access token is scrubbed
- THEN the path is redacted to `Users\<redacted>`, the token to `[token redacted]`, with counts returned.

---

## Capability: Security Utilities
**Files:** Services/SecurityHelper.cs (220 LOC)
**Class:** MIXED
**Blocking deps:** Mostly portable: `Path` traversal validation, filename sanitization (incl. Windows
reserved names CON/PRN/etc.), `RandomNumberGenerator`, constant-time compare, SHA-256 hashing, a rate
limiter. Two Windows-bound members: `IsRunningAsAdmin` (`WindowsIdentity`/`WindowsPrincipal`) and
`IsDebuggerAttached` (`kernel32.dll IsDebuggerPresent`).
**Seam (if MIXED):** Guard the two OS-specific methods behind an `IPlatformSecurity` probe (or
`OperatingSystem.IsWindows()` checks); the crypto/path/filename/rate-limit core is fully portable.

### Requirement: The system SHALL provide path/crypto/rate-limit security primitives.
The system SHALL validate paths against an allowed base, sanitize filenames, generate secure tokens,
compare strings in constant time, hash files (SHA-256), and rate-limit actions.

#### Scenario: Path traversal attempt
- WHEN a path that resolves outside the allowed base is checked
- THEN `IsPathSafe` returns false.

---

## Summary table

| Capability | Class | Blocking dep | ~LOC |
|---|---|---|---|
| Application Bootstrap & Lifecycle | OS-SPECIFIC | WPF `Application`/`Window`/`MessageBox`/`SplashScreen`, WinForms `Screen`, user32 `GetWindowRect`, registry, XAML | ~2750 |
| Single-Instance Guard & File Handoff | MIXED | replay path calls WPF (mutex/named-event are portable) | ~120 |
| Asset/Temp/Data Path Mgmt & Migration | PORTABLE | none (System.IO + LocalApplicationData) | ~250 |
| Settings Persistence | PORTABLE | none here (DPAPI delegated to secure-store; monitor name = data schema) | ~4790 |
| Auto-Update | MIXED | registry detect + Inno `Setup.exe` + `wmic` (GitHub check is portable) | ~1135 |
| Windows Startup Registration | OS-SPECIFIC | COM `IShellLink` `.lnk` in Startup folder | 207 |
| System Tray Integration | OS-SPECIFIC | WinForms `NotifyIcon`, user32 `SetForegroundWindow` | 194 |
| Global Hotkey Registration | MIXED | user32 `RegisterHotKey` + `HwndSource` hook | 146 |
| Low-Level Keyboard Hook (Lockdown) | OS-SPECIFIC | user32 `SetWindowsHookEx(WH_KEYBOARD_LL)`, `GetAsyncKeyState` | 140 |
| Low-Level Mouse Hook (Ripple) | OS-SPECIFIC | user32 `SetWindowsHookEx(WH_MOUSE_LL)`, `MSLLHOOKSTRUCT` | 106 |
| Desktop Wallpaper Override | MIXED | user32 `SystemParametersInfo(SPI_*DESKWALLPAPER)` | 183 |
| OS Title-Bar Chrome Theming | OS-SPECIFIC | dwmapi `DwmSetWindowAttribute`, `WindowInteropHelper` | 116 |
| In-App Notifications (Toasts) | OS-SPECIFIC | WPF UI (Border/TextBlock/animation/DropShadow) | 267 |
| UI Hang Watchdog | OS-SPECIFIC | dbghelp `MiniDumpWriteDump`, `rundll32 comsvcs`, WPF Dispatcher | 156 |
| Performance Tiering | MIXED | WPF `BitmapScalingMode` enum only | 99 |
| Safe Dispatcher Helpers | MIXED | WPF `Dispatcher`/`DispatcherPriority` | 62 |
| Log Scrubbing (PII Redaction) | PORTABLE | none (regex) | 176 |
| Security Utilities | MIXED | `WindowsIdentity` + kernel32 `IsDebuggerPresent` (rest portable) | 220 |

---

## Portability verdict (whole cluster)

This cluster is **the OS-integration spine of the app and is predominantly Windows-bound — roughly
30–35% portable by capability count, but much lower (~10–15%) if weighted by the load-bearing role of
the WPF entry point.** The pure-domain capabilities port cleanly with zero work: data/asset path
management, settings persistence (`AppSettings` is a 4.3k-line POCO + JSON service whose only Windows
ties — DPAPI and monitor device-name strings — are already delegated out or are data-schema concerns),
log scrubbing, and `UpdateInfo`. A second band is **MIXED with small, well-defined seams**: auto-update
(portable GitHub check vs. Inno-`Setup.exe`/registry install — extract `IUpdateInstaller`), global
hotkey (`IHotkeyProvider`), wallpaper (`IWallpaperProvider`), startup registration (`IAutostartProvider`),
the dispatcher helpers (`IUiDispatcher`), performance tiering (drop one WPF enum), and security utils
(guard two OS probes). The hard core is **irreducibly Windows**: the WPF `Application` bootstrap itself,
the two low-level `SetWindowsHookEx` hooks (lockdown keyboard, chaos mouse — no portable analog and
they require privileged event taps elsewhere), the WinForms tray icon, DWM title-bar tinting, the
WPF-rendered toast service, and the dbghelp/comsvcs minidump watchdog — each needs a full per-platform
rewrite. Net: the *logic* survives a re-platform, but every point where the app touches the desktop
shell, the input stack, or the window manager is a rewrite, and `App.xaml.cs` would be reimplemented
against whatever UI toolkit replaces WPF.
