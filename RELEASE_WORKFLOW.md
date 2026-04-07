# Release Workflow

Quick reference for releasing new versions of Conditioning Control Panel.

---

## Recommended: Use the `/release` Skill

The `/release` skill automates version bumping across all files and localization key creation:

```
/release X.Y.Z "Release Subtitle"
```

This handles steps 1-2 below automatically. After running it, skip to step 3 (Build Installer).

---

## Manual Release Steps

### 1. Update All Version Locations

| File | Location | What to Change |
|------|----------|----------------|
| `ConditioningControlPanel/ConditioningControlPanel.csproj` | Line 12: `<Version>` | `<Version>X.X.X</Version>` |
| `ConditioningControlPanel/Services/UpdateService.cs` | Line 25: `AppVersion` | `public const string AppVersion = "X.X.X";` |
| `ConditioningControlPanel/Services/UpdateService.cs` | Line 31+: `CurrentPatchNotes` | Full patch notes string |
| `installer.iss` | Line 16: `MyAppVersion` | `#define MyAppVersion "X.X.X"` |
| `build-installer.bat` | Line 10: `VERSION` | `set VERSION=X.X.X` |
| `ConditioningControlPanel/MainWindow.xaml` | ~Line 1749: `BtnUpdateAvailable` | `Content="{loc:Str btn_vX_Y_Z_is_out}"` |
| `ConditioningControlPanel/MainWindow.xaml` | ~Line 1750: `ToolTip` | `ToolTip="{loc:Str tooltip_vX_Y_Z_slug}"` |

### 2. Add Localization Keys

Add two new keys to each of the 9 JSON files in `ConditioningControlPanel/Localization/Languages/`:

```json
"btn_vX_Y_Z_is_out": "💖 vX.Y.Z IS OUT! 💖",
"tooltip_vX_Y_Z_subtitle_slug": "vX.Y.Z - Release Subtitle",
```

Insert after the previous version's keys. Use English text for all languages (translations can come later; the fallback system handles it). Do not delete old version keys.

Languages: `de`, `en`, `es`, `fr`, `ja`, `ko`, `pt-BR`, `ru`, `zh-CN`

### 3. Build Installer

```batch
cd C:\Projects\Conditioning-Control-Panel---CSharp-WPF
build-installer.bat
```

This runs `dotnet publish` and then Inno Setup. Output: `installer-output/ConditioningControlPanel-X.X.X-Setup.exe`

**Note:** The publish TFM is `net8.0-windows10.0.19041.0` (must match csproj). If the build fails with "Access denied", delete `%LOCALAPPDATA%\Temp\Velopack` and retry.

### 4. Commit & Push

```bash
git add -A
git commit -m "Bump to vX.X.X — Release Subtitle"
git push
```

Or use the `/commit` skill.

### 5. Create GitHub Release

1. Go to: https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/new
2. Tag: `vX.X.X`
3. Title: `vX.X.X — Release Subtitle`
4. Upload: `installer-output/ConditioningControlPanel-X.X.X-Setup.exe`
5. Paste patch notes from `CurrentPatchNotes` in UpdateService.cs
6. Publish

### 6. Update Server Marquee & Update Banner

See the private repo `CC-Labs-llc/CCP-Server` for admin endpoint docs and curl examples.

---

## How Updates Work

1. `UpdateService.cs` checks the server banner at `/config/update-banner` on startup
2. Compares `AppVersion` constant with the banner version
3. If a newer version is available, shows update button in the UI
4. User clicks the button to download the new installer from GitHub

The `AppVersion` constant in UpdateService.cs is what the app uses to determine its current version. Always keep it in sync with the csproj `<Version>`.

---

## Troubleshooting

### Users not seeing updates
- Check that `AppVersion` in UpdateService.cs matches the csproj version
- Verify the GitHub release is published (not draft)
- Update the server banner as a fallback notification

### Build errors
- MSB3027 file lock errors: the app is running — close it before building
- "Access denied" during Velopack: delete `%LOCALAPPDATA%\Temp\Velopack`
- CA1416 platform warnings: safe to ignore for Windows-only app

---

## Pre-Release Checklist

### Version Updates
- [ ] `ConditioningControlPanel.csproj` — Version tag
- [ ] `UpdateService.cs` — AppVersion constant
- [ ] `UpdateService.cs` — CurrentPatchNotes
- [ ] `installer.iss` — MyAppVersion
- [ ] `build-installer.bat` — VERSION
- [ ] `MainWindow.xaml` — BtnUpdateAvailable Content & ToolTip loc keys
- [ ] Localization JSON files (9 files) — new btn + tooltip keys

### Build & Deploy
- [ ] Installer built (`build-installer.bat`)
- [ ] Changes committed and pushed
- [ ] GitHub release created with installer uploaded

### Post-Release
- [ ] Server marquee updated (see private repo for admin docs)
- [ ] Server update banner updated (see private repo for admin docs)
