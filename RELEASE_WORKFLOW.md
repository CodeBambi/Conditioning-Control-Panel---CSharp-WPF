# Release Workflow

Quick reference for releasing new versions of Conditioning Control Panel.

---

## Version Locations (Update ALL of these)

### Code Files
| File | Location | Example |
|------|----------|---------|
| `ConditioningControlPanel.csproj` | Line 11: `<Version>` | `<Version>5.1.1</Version>` |
| `Services/UpdateService.cs` | Line ~25: `AppVersion` | `public const string AppVersion = "5.1.1";` |
| `Services/UpdateService.cs` | Line ~31: `CurrentPatchNotes` | Update patch notes text |
| `installer.iss` | Line 17: `MyAppVersion` | `#define MyAppVersion "5.1.1"` |
| `build-installer.bat` | Line 10: `VERSION` | `set VERSION=5.1.1` |
| `MainWindow.xaml` | Line ~741: `BtnUpdateAvailable` | `Content="🩷 v5.1.1 IS OUT! 🩷"` |
| `MainWindow.xaml` | Line ~742: `ToolTip` | `ToolTip="v5.1.1 - [message]"` |

### GitHub Pages (docs/index.html)
| Line | What to Update |
|------|----------------|
| ~854 | Download button URL and text: `Download vX.X.X` |
| ~861 | Version banner: `vX.X.X Available Now` |
| ~867-873 | Hero download button URL and text |

Update the download URLs to point to the new release:
```
https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/download/vX.X.X/ConditioningControlPanel-X.X.X-Setup.exe
```

### Server Configuration
| Location | Purpose |
|----------|---------|
| `codebambi-proxy.vercel.app/config/update-banner` | Server-side banner for users who haven't updated |

Update the server endpoint to return:
```json
{
  "enabled": true,
  "version": "5.1.1",
  "message": "UPDATE 5.1.1 is live!"
}
```

---

## Quick Release Steps

### 1. Update All Version Locations
Edit all the code locations listed above with the new version number.

### 2. Build Installer (Inno Setup)
```batch
cd C:\Projects\Conditioning-Control-Panel---CSharp-WPF
build-installer.bat
```
Output: `installer-output/ConditioningControlPanel-X.X.X-Setup.exe`

### 3. Build Velopack Release (for auto-updates)
```powershell
cd C:\Projects\Conditioning-Control-Panel---CSharp-WPF

# Clean temp folder first (prevents file lock errors)
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Temp\Velopack" -ErrorAction SilentlyContinue

# Build release
.\build.ps1 -Publish -CreateRelease
```
Output in `releases/` folder:
- `ConditioningControlPanel-X.X.X-full.nupkg` (required)
- `ConditioningControlPanel-X.X.X-delta.nupkg` (optional, for smaller updates)
- `releases.win.json` (required for Velopack)
- `RELEASES` (required for Velopack)

### 4. Update GitHub Pages
Edit `docs/index.html`:
- Update version text in download buttons
- Update download URLs to point to new release tag

### 5. Commit & Push
```bash
git add -A
git commit -m "Bump to vX.X.X with [summary of changes]"
git push
```

### 6. Create GitHub Release
1. Go to: https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases/new
2. Tag: `vX.X.X` (e.g., `v5.1.1`)
3. Title: `vX.X.X - [Short Description]`
4. Upload files:
   - `installer-output/ConditioningControlPanel-X.X.X-Setup.exe`
   - `releases/ConditioningControlPanel-X.X.X-full.nupkg`
   - `releases/ConditioningControlPanel-X.X.X-delta.nupkg` (if exists)
   - `releases/releases.win.json`
   - `releases/RELEASES`
5. Write release notes (or copy from `CurrentPatchNotes`)
6. Publish release

### 7. Update Server Banner
Update the proxy server's `/config/update-banner` endpoint with:
```json
{
  "enabled": true,
  "version": "X.X.X",
  "message": "UPDATE X.X.X is live!"
}
```
This shows a banner to users on older versions who haven't auto-updated.

---

## How Auto-Updates Work

1. **UpdateService.cs** checks GitHub releases via Velopack
2. Velopack reads `releases.win.json` from the latest release
3. Compares `AppVersion` constant with latest release version
4. If newer, prompts user to update
5. Downloads `.nupkg` file and applies update

**Fallback:** If Velopack update fails, the server banner notifies users to update manually.

**Important:** The `AppVersion` constant in UpdateService.cs is what the app uses to determine its current version. Always keep this in sync!

---

## Troubleshooting

### Velopack build fails with "Access denied"
```powershell
Remove-Item -Recurse -Force "$env:LOCALAPPDATA\Temp\Velopack"
```
Then retry the build.

### Users not seeing updates
- Check that `releases.win.json` is uploaded to the GitHub release
- Check that `AppVersion` in UpdateService.cs matches the csproj version
- Verify the GitHub release is published (not draft)
- Update the server banner as a fallback notification

### Fresh install required (major updates)
For major version jumps (e.g., 5.0 -> 5.1), the app may require fresh install.
This is controlled by `FreshInstallVersion` in UpdateService.cs (~line 108).

---

## File Checklist Before Release

### Version Updates
- [ ] `ConditioningControlPanel.csproj` - Version tag
- [ ] `UpdateService.cs` - AppVersion constant
- [ ] `UpdateService.cs` - CurrentPatchNotes
- [ ] `installer.iss` - MyAppVersion
- [ ] `build-installer.bat` - VERSION
- [ ] `MainWindow.xaml` - BtnUpdateAvailable Content & ToolTip
- [ ] `docs/index.html` - Download button text and URLs

### Build & Deploy
- [ ] Installer built (`build-installer.bat`)
- [ ] Velopack release built (`build.ps1 -Publish -CreateRelease`)
- [ ] Changes committed and pushed
- [ ] GitHub release created with all files uploaded

### Post-Release
- [ ] Server banner updated (`/config/update-banner`)
- [ ] Test auto-update from previous version
- [ ] Verify GitHub Pages download links work
