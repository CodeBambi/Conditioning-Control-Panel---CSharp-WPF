; Conditioning Control Panel - Inno Setup Installer Script
; This creates a proper Windows installer with install path selection
;
; Requirements:
; 1. Install Inno Setup from https://jrsoftware.org/isinfo.php
; 2. Build the app first: dotnet publish -c Release
; 3. Compile this script with Inno Setup Compiler
;
; The installer will:
; - Allow users to choose installation directory
; - Create Start Menu and Desktop shortcuts
; - Register uninstaller
; - Store install path in registry for Velopack updates

#define MyAppName "Conditioning Control Panel"
#define MyAppVersion "6.8.0"
#define MyAppPublisher "CodeBambi"
#define MyAppURL "https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF"
#define MyAppExeName "ConditioningControlPanel.exe"
#define MyAppDescription "A professional visual conditioning application with gamification features"

; Path to the published output (adjust if needed)
; PublishDir can be overridden from the command line (ISCC /DPublishDir=...), which
; build-installer.bat uses to point at a SHORT staging path. The publish tree is nested
; ~131 chars deep, and a handful of builtin-sissyhypno audio files push past MAX_PATH (260)
; from there, aborting the ISCC compile. Staging to e.g. C:\ccpb\pub keeps every path short.
#ifndef PublishDir
  #define PublishDir "ConditioningControlPanel\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif

[Setup]
; Application identity
AppId={{A7B9C3D1-E5F2-4A8B-9C1D-2E3F4A5B6C7D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases

; Default installation directory (user can change)
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}

; Allow user to change install directory
DisableDirPage=no
DirExistsWarning=auto

; Output settings
OutputDir=.\installer-output
OutputBaseFilename=ConditioningControlPanel-{#MyAppVersion}-Setup
SetupIconFile=ConditioningControlPanel\Resources\app.ico

; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Privileges - allow per-user or admin install
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

; Appearance
WizardStyle=modern
WizardSizePercent=120

; Uninstaller
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

; Restart Manager: name the app's real single-instance mutex (App.xaml.cs MutexName) so
; Inno can detect the running app and cleanly close+restart it when files are in use during
; a silent auto-update. Without this, /CLOSEAPPLICATIONS has nothing registered to close and
; the in-app updater could race the still-locked exe (#499).
AppMutex=ConditioningControlPanel_SingleInstance_Mutex
CloseApplications=yes
RestartApplications=yes

; Other settings
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
AllowNoIcons=yes
ShowLanguageDialog=auto

; License and info pages (optional - create these files if desired)
; LicenseFile=LICENSE.txt
; InfoBeforeFile=INSTALL_INFO.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "fileassoc"; Description: "Add 'Open with CCP' to right-click menu for media files (.mp4, .mp3, etc.)"; GroupDescription: "File associations:"; Flags: unchecked

[Files]
; Microsoft Visual C++ 2015-2022 x64 runtime bootstrapper. OpenCvSharpExtern.dll
; (webcam capture) is built against the MSVC runtime and fails to load with
; DllNotFoundException 0x8007007E on machines that don't have it — which silently
; broke all webcam/eye-tracking features (BUG-XQCPKGE2Q8). Only staged + run when
; the runtime is actually missing (see VCRedistNeeded). build-installer.bat
; downloads this into redist\ before compiling.
Source: "redist\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: VCRedistNeeded

; Microsoft Edge WebView2 Evergreen runtime bootstrapper (~1.6 MB; it downloads the
; real runtime itself). WebView2 is no longer optional: the FYP feed, the Exclusives
; tab, DtRH, the Goon Game and — from 6.7 — the default video engine all render in
; WebView2. It is preinstalled on Windows 11 and on Windows 10 machines with modern
; Edge, so most installs skip this entirely (see WebView2RuntimeNeeded). Committed in
; redist\ rather than downloaded at build time because a silent skip here costs half
; the app's content. Official Microsoft fwlink for refreshes:
;   https://go.microsoft.com/fwlink/p/?LinkId=2124703
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: WebView2RuntimeNeeded

; Main executable
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; All other files from publish directory
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,cs\*,de\*,es\*,fr\*,it\*,ja\*,ko\*,pl\*,pt-BR\*,ru\*,tr\*,zh-Hans\*,zh-Hant\*"

; NOTE: Don't include user data files - those go to %APPDATA%

[InstallDelete]
; =============================================================================================
; CONTENT PACKS - upgrader convergence.  See ConditioningControlPanel\docs\CONTENT_PACKS_PLAN.md
; sections 1 (what moves) and 5 (why this section exists).
;
; From 6.7 on, ~1.36 GB of version-stable audio and the two bundled .ccpmod archives no longer
; ship in the installer (stripped in ConditioningControlPanel.csproj via the
; ContentPack*Exclude properties) and are downloaded instead into
;   %LOCALAPPDATA%\ConditioningControlPanel\content\.
; Inno leaves behind files it did not install, so without this section an upgrader keeps the old
; bundled copies under {app} forever - and ContentLocator probes BaseDirectory first, so those
; stale copies would permanently shadow every downloaded pack.  [InstallDelete] runs before
; [Files], so this is a clean sweep followed by a fresh install.
;
; RULES:
;   * The deletion list is GENERATED - installer-content-deletions.iss (#included below) names
;     every moved file EXPLICITLY, one exact path per line, NO wildcards.  A file a USER
;     hand-dropped into one of these folders is therefore never matched and survives the
;     upgrade - and keeps working, because ContentLocator unions the install dir with the
;     downloaded content root.  Shipped manifests (bark_rules.json, mantras.json,
;     avatar_manifest.json, vo_manifest.json, sfx_manifest.json, dtrh barks\manifest.js,
;     vn\manifest.json) are not pack payload, so they can never appear in the list; a missing
;     manifest is not a graceful degrade, it is a hang or a crash.
;   * NEVER hand-edit installer-content-deletions.iss and NEVER add wildcard deletions here -
;     a wildcard cannot tell a hand-added user file from a shipped one.  Regenerate (and
;     commit) after any change to the pack file set:
;       powershell -ExecutionPolicy Bypass -File ConditioningControlPanel\Scripts\build-content-packs.ps1 -DeletionsOnly
;     (a full pack build regenerates it too, and hard-fails on strip/pack drift first).
;   * Never touch %LOCALAPPDATA%\ConditioningControlPanel - user settings, progress, assets,
;     mods and downloaded packs all live there.  Everything in the list is {app}-only.
;   * "dirifempty" only removes a folder that the sweep left empty, so a surviving user file
;     also keeps its folder alive.
; =============================================================================================

; NOTE the include also covers the two bundled .ccpmod archives ({app}\DroneMod\,
; {app}\LockedMod\).  ModService's already-extracted copies under %LOCALAPPDATA%\...\builtin_mods\
; are user data and are deliberately left alone - phase C re-stamps them from the downloaded
; pack instead.
#include "installer-content-deletions.iss"

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"; Tasks: startmenuicon

; Desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Store install path for the application and Velopack to find
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\{#MyAppPublisher}\{#MyAppName}"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey
; Assets path will be written by [Code] section during install

; --- "Open with CCP" file association (per-user, no admin required) ---
; Two ProgIDs so the Open With list shows two entries with the app icon:
;   "CCP Player" (--play)  →  EnhancementPlayerWindow
;   "CCP Editor" (--edit)  →  DeeperEditorWindow with blank enhancement
; Note: this only adds CCP to the Open With list. It does NOT change which
; app is the default for .mp4/.mp3/etc. Users promote to default manually.

Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1"; ValueType: string; ValueName: ""; ValueData: "Conditioning Control Panel - Player"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "CCP Player"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "CCP Player"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Player.1\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --play ""%1"""; Tasks: fileassoc

Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1"; ValueType: string; ValueName: ""; ValueData: "Conditioning Control Panel - Editor"; Flags: uninsdeletekey; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "CCP Editor"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1\shell\open"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "CCP Editor"; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\CCPanel.Editor.1\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --edit ""%1"""; Tasks: fileassoc

; --- Add CCP to OpenWith for each supported media extension ---
; uninsdeletevalue (NOT uninsdeletekey) — we only own our two values, must not
; nuke the whole OpenWithProgids subkey on uninstall (other apps live there too).

; Video
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mp4\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.webm\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mkv\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mov\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.avi\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4v\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

; Audio
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.mp3\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.wav\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.m4a\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.aac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.flac\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Player.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc
Root: HKCU; Subkey: "Software\Classes\.ogg\OpenWithProgids"; ValueType: string; ValueName: "CCPanel.Editor.1"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc

[Run]
; Install the VC++ runtime first (only if missing) so the webcam native library
; can load on first launch. Self-elevates via UAC; /norestart keeps setup flowing.
Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft Visual C++ Runtime (required for webcam features)..."; Check: VCRedistNeeded

; Then the WebView2 Evergreen runtime (only if missing) - the browser-backed features
; (FYP feed, Exclusives, DtRH, Goon Game, the default video engine) have no fallback
; renderer without it. The bootstrapper pulls the runtime down itself and self-elevates
; via UAC; if it fails, setup continues and the app degrades exactly as it does today.
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime (required for video and web features)..."; Check: WebView2RuntimeNeeded

; Option to launch app after interactive installation (shows checkbox)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; NOTE: No silent-install self-launch here. The in-app updater's external helper
; (UpdateService.WriteUpdateHelperScript) owns the relaunch after a silent auto-update, so a
; second launch here would double-start the app. (The single-instance handshake would dedup
; it, but we keep exactly one relauncher to avoid a stray window flash.)

[UninstallRun]
; Ensure app is closed before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillApp"

[UninstallDelete]
; Clean up any generated files (optional)
Type: filesandordirs; Name: "{app}\logs"

[Code]
// Pascal Script for custom installer logic

// Returns True when the VC++ 2015-2022 x64 runtime is NOT installed, so the
// bundled bootstrapper only runs on machines that need it. The runtime sets
// Installed=1 under this key; setup runs in 64-bit mode (see
// ArchitecturesInstallIn64BitMode) so this resolves to the real 64-bit hive.
function VCRedistNeeded(): Boolean;
var
  Installed: Cardinal;
begin
  Result := True;
  if RegQueryDWordValue(HKEY_LOCAL_MACHINE,
       'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    if Installed = 1 then
      Result := False;
  end;
end;

// Returns True when the Edge WebView2 Evergreen runtime is NOT installed, so the
// bundled bootstrapper only runs on machines that need it (Windows 11 and any
// Windows 10 with modern Edge already have it).
//
// Detection follows Microsoft's documented method: the Evergreen runtime's client
// key carries a 'pv' version value, and "present and not 0.0.0.0" is the install
// signal - EdgeUpdate leaves the key behind with pv=0.0.0.0 after an uninstall.
// Per-machine installs land in the 32-bit hive (EdgeUpdate is a 32-bit component)
// which this 64-bit setup has to reach through WOW6432Node explicitly; per-user
// installs land in HKCU without it. Check both, plus the native 64-bit path for
// good measure - any hit means "already there, skip".
function WebView2VersionPresent(const RootKey: Integer; const SubKey: String): Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(RootKey, SubKey, 'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
      Result := True;
  end;
end;

function WebView2RuntimeNeeded(): Boolean;
begin
  Result := not (
    WebView2VersionPresent(HKEY_LOCAL_MACHINE,
      'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    WebView2VersionPresent(HKEY_LOCAL_MACHINE,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    WebView2VersionPresent(HKEY_CURRENT_USER,
      'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'));
end;

var
  VelopackPath: String;
  HasOldVelopackInstall: Boolean;
  HasExistingAssets: Boolean;
  AssetsPath: String;
  ImageCount, VideoCount: Integer;

  // Custom page for assets confirmation
  AssetsPage: TWizardPage;
  AssetsPathLabel: TNewStaticText;
  AssetsPathEdit: TNewEdit;
  AssetsBrowseButton: TNewButton;
  AssetsInfoLabel: TNewStaticText;
  AssetsPreserveLabel: TNewStaticText;

// Count files in a directory with specific extensions
function CountFilesInDir(const Dir: String; const Extensions: String): Integer;
var
  FindRec: TFindRec;
  Ext: String;
begin
  Result := 0;
  if FindFirst(Dir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          Ext := LowerCase(ExtractFileExt(FindRec.Name));
          if Pos(Ext, Extensions) > 0 then
            Result := Result + 1;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Check if old Velopack installation exists
function CheckForVelopackInstall(): Boolean;
var
  CurrentPath: String;
begin
  VelopackPath := ExpandConstant('{localappdata}\ConditioningControlPanel');
  CurrentPath := VelopackPath + '\current';

  // Velopack installs to a 'current' subfolder
  Result := DirExists(CurrentPath) and FileExists(CurrentPath + '\{#MyAppExeName}');
end;

// Check for existing assets and count them
procedure DetectExistingAssets();
var
  NewAssetsPath, OldVelopackAssetsPath, ImagesPath, VideosPath: String;
  NewImageCount, NewVideoCount, OldImageCount, OldVideoCount: Integer;
begin
  // New location (AppData root)
  NewAssetsPath := ExpandConstant('{localappdata}\ConditioningControlPanel\assets');
  // Old Velopack location (inside current folder)
  OldVelopackAssetsPath := ExpandConstant('{localappdata}\ConditioningControlPanel\current\assets');

  // Count assets in new location
  NewImageCount := 0;
  NewVideoCount := 0;
  if DirExists(NewAssetsPath) then
  begin
    if DirExists(NewAssetsPath + '\images') then
      NewImageCount := CountFilesInDir(NewAssetsPath + '\images', '.png.jpg.jpeg.gif.webp.bmp');
    if DirExists(NewAssetsPath + '\videos') then
      NewVideoCount := CountFilesInDir(NewAssetsPath + '\videos', '.mp4.webm.mkv.avi.mov.wmv');
  end;

  // Count assets in old Velopack location
  OldImageCount := 0;
  OldVideoCount := 0;
  if DirExists(OldVelopackAssetsPath) then
  begin
    if DirExists(OldVelopackAssetsPath + '\images') then
      OldImageCount := CountFilesInDir(OldVelopackAssetsPath + '\images', '.png.jpg.jpeg.gif.webp.bmp');
    if DirExists(OldVelopackAssetsPath + '\videos') then
      OldVideoCount := CountFilesInDir(OldVelopackAssetsPath + '\videos', '.mp4.webm.mkv.avi.mov.wmv');
  end;

  // Use whichever location has more assets (prefer old Velopack location if equal)
  if (OldImageCount + OldVideoCount) >= (NewImageCount + NewVideoCount) then
  begin
    if (OldImageCount + OldVideoCount) > 0 then
    begin
      AssetsPath := OldVelopackAssetsPath;
      HasExistingAssets := True;
      ImageCount := OldImageCount;
      VideoCount := OldVideoCount;
    end
    else if (NewImageCount + NewVideoCount) > 0 then
    begin
      AssetsPath := NewAssetsPath;
      HasExistingAssets := True;
      ImageCount := NewImageCount;
      VideoCount := NewVideoCount;
    end
    else
    begin
      AssetsPath := NewAssetsPath;
      HasExistingAssets := False;
      ImageCount := 0;
      VideoCount := 0;
    end;
  end
  else
  begin
    AssetsPath := NewAssetsPath;
    HasExistingAssets := True;
    ImageCount := NewImageCount;
    VideoCount := NewVideoCount;
  end;
end;

// Browse button click handler
procedure AssetsBrowseButtonClick(Sender: TObject);
var
  Dir: String;
begin
  Dir := AssetsPathEdit.Text;
  if BrowseForFolder('Select your assets folder:', Dir, False) then
  begin
    AssetsPathEdit.Text := Dir;
    // Recount files in new location
    if DirExists(Dir + '\images') then
      ImageCount := CountFilesInDir(Dir + '\images', '.png.jpg.jpeg.gif.webp.bmp')
    else
      ImageCount := 0;
    if DirExists(Dir + '\videos') then
      VideoCount := CountFilesInDir(Dir + '\videos', '.mp4.webm.mkv.avi.mov.wmv')
    else
      VideoCount := 0;
    AssetsInfoLabel.Caption := 'Found: ' + IntToStr(ImageCount) + ' images, ' + IntToStr(VideoCount) + ' videos';
  end;
end;

// Copy all files from source to dest directory (non-recursive for a single folder)
procedure CopyFilesFromDir(const SourceDir, DestDir: String);
var
  FindRec: TFindRec;
  SourceFile, DestFile: String;
begin
  if not DirExists(SourceDir) then Exit;
  ForceDirectories(DestDir);

  if FindFirst(SourceDir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) = 0 then
        begin
          SourceFile := SourceDir + '\' + FindRec.Name;
          DestFile := DestDir + '\' + FindRec.Name;
          // Only copy if destination doesn't exist (don't overwrite)
          if not FileExists(DestFile) then
            CopyFile(SourceFile, DestFile, False);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

// Migrate assets from old Velopack location to new AppData location
procedure MigrateAssetsFromVelopack();
var
  OldAssetsPath, NewAssetsPath: String;
  OldImagesPath, OldVideosPath, NewImagesPath, NewVideosPath: String;
  OldSpiralsPath, NewSpiralsPath: String;
begin
  OldAssetsPath := VelopackPath + '\current\assets';
  NewAssetsPath := VelopackPath + '\assets';
  OldSpiralsPath := VelopackPath + '\current\Spirals';
  NewSpiralsPath := VelopackPath + '\Spirals';

  // Migrate images
  OldImagesPath := OldAssetsPath + '\images';
  NewImagesPath := NewAssetsPath + '\images';
  if DirExists(OldImagesPath) then
  begin
    Log('Migrating images from ' + OldImagesPath + ' to ' + NewImagesPath);
    CopyFilesFromDir(OldImagesPath, NewImagesPath);
  end;

  // Migrate videos
  OldVideosPath := OldAssetsPath + '\videos';
  NewVideosPath := NewAssetsPath + '\videos';
  if DirExists(OldVideosPath) then
  begin
    Log('Migrating videos from ' + OldVideosPath + ' to ' + NewVideosPath);
    CopyFilesFromDir(OldVideosPath, NewVideosPath);
  end;

  // Migrate spirals
  if DirExists(OldSpiralsPath) then
  begin
    Log('Migrating spirals from ' + OldSpiralsPath + ' to ' + NewSpiralsPath);
    CopyFilesFromDir(OldSpiralsPath, NewSpiralsPath);
  end;
end;

// Clean up old Velopack installation (preserves user data)
procedure CleanupVelopackInstall();
var
  CurrentPath, PackagesPath, UpdatePath: String;
begin
  CurrentPath := VelopackPath + '\current';
  PackagesPath := VelopackPath + '\packages';
  UpdatePath := VelopackPath + '\Update.exe';

  // IMPORTANT: Migrate assets BEFORE deleting current folder!
  MigrateAssetsFromVelopack();

  // Remove Velopack-specific folders only (assets already migrated)
  if DirExists(CurrentPath) then
    DelTree(CurrentPath, True, True, True);

  if DirExists(PackagesPath) then
    DelTree(PackagesPath, True, True, True);

  // Remove Velopack's Update.exe if it exists
  if FileExists(UpdatePath) then
    DeleteFile(UpdatePath);

  // Remove any .velopack files
  DelTree(VelopackPath + '\*.velopack', False, True, False);

  // Remove old Velopack uninstall registry entry (shows in Add/Remove Programs)
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\ConditioningControlPanel');

  // Also clean up old app registry entries if they exist
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\ConditioningControlPanel');
end;

// Remove file-association registry entries (called on upgrade when user opts out)
procedure CleanupFileAssociations();
var
  Exts: array of String;
  i: Integer;
begin
  // Remove both ProgID keys entirely
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\CCPanel.Player.1');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\CCPanel.Editor.1');

  // Remove our entries from each extension's OpenWithProgids list (leave other apps' entries alone)
  SetArrayLength(Exts, 12);
  Exts[0] := '.mp4';  Exts[1] := '.webm'; Exts[2] := '.mkv';  Exts[3] := '.mov';
  Exts[4] := '.avi';  Exts[5] := '.m4v';  Exts[6] := '.mp3';  Exts[7] := '.wav';
  Exts[8] := '.m4a';  Exts[9] := '.aac';  Exts[10] := '.flac'; Exts[11] := '.ogg';
  for i := 0 to GetArrayLength(Exts) - 1 do
  begin
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Classes\' + Exts[i] + '\OpenWithProgids', 'CCPanel.Player.1');
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Classes\' + Exts[i] + '\OpenWithProgids', 'CCPanel.Editor.1');
  end;
end;

// Prompt to close app if running
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  // Check for old Velopack install
  HasOldVelopackInstall := CheckForVelopackInstall();

  // Detect existing assets
  DetectExistingAssets();

  // Check if already running. Use the app's REAL single-instance mutex name
  // (App.xaml.cs MutexName) — the old '{#MyAppName}_Mutex' string never matched, so this
  // guard was dead and never detected a running app (#499).
  if CheckForMutexes('ConditioningControlPanel_SingleInstance_Mutex') then
  begin
    if MsgBox('{#MyAppName} is currently running.' + #13#10 + #13#10 +
              'Please close it before continuing installation.' + #13#10 + #13#10 +
              'Click OK to attempt to close it automatically, or Cancel to exit setup.',
              mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      // Try to close gracefully first
      ShellExec('', 'taskkill', '/IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
      Sleep(2000);

      // Force kill if still running
      if CheckForMutexes('{#MyAppName}_Mutex') then
      begin
        ShellExec('', 'taskkill', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
        Sleep(1000);
      end;
    end
    else
    begin
      Result := False;
    end;
  end;
end;

// Determine if assets page should be shown
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  // ALWAYS show assets page - users need to choose where their content lives
  // This folder stores images, videos, and downloaded packs - survives updates
end;

// Called after installation completes successfully
procedure CurStepChanged(CurStep: TSetupStep);
var
  SelectedAssetsPath: String;
begin
  if CurStep = ssPostInstall then
  begin
    // ALWAYS save the selected assets path to registry (read by app on startup)
    // This ensures the user's choice is respected, whether new or existing install
    SelectedAssetsPath := AssetsPathEdit.Text;
    if SelectedAssetsPath <> '' then
    begin
      RegWriteStringValue(HKEY_CURRENT_USER, 'Software\{#MyAppPublisher}\{#MyAppName}',
        'AssetsPath', SelectedAssetsPath);

      // Create the folder structure if it doesn't exist
      if not DirExists(SelectedAssetsPath) then
        ForceDirectories(SelectedAssetsPath);
      if not DirExists(SelectedAssetsPath + '\images') then
        ForceDirectories(SelectedAssetsPath + '\images');
      if not DirExists(SelectedAssetsPath + '\videos') then
        ForceDirectories(SelectedAssetsPath + '\videos');
    end;

    // Remove any pre-existing file associations if the user opted out (or silent install)
    if not WizardIsTaskSelected('fileassoc') then
      CleanupFileAssociations();

    // Offer to clean up old Velopack installation
    if HasOldVelopackInstall then
    begin
      if MsgBox('A previous installation (via auto-updater) was detected.' + #13#10 + #13#10 +
                'Location: ' + VelopackPath + '\current' + #13#10 + #13#10 +
                'Would you like to remove the old installation?' + #13#10 +
                '(Your settings, assets, and progress will be preserved)',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        CleanupVelopackInstall();
        MsgBox('Old installation removed successfully!' + #13#10 + #13#10 +
               'Your user data has been preserved in:' + #13#10 +
               VelopackPath,
               mbInformation, MB_OK);
      end;
    end;
  end;
end;

// Clean uninstall - prompt to remove user data
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserDataPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    UserDataPath := ExpandConstant('{localappdata}\ConditioningControlPanel');

    if DirExists(UserDataPath) then
    begin
      if MsgBox('Do you want to remove your user data (settings, progress, logs)?' + #13#10 + #13#10 +
                'Location: ' + UserDataPath + #13#10 + #13#10 +
                'Click Yes to remove all data, or No to keep it.',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(UserDataPath, True, True, True);
      end;
    end;
  end;
end;

// Create the assets confirmation page
procedure CreateAssetsPage();
var
  InfoText: String;
begin
  // Create custom page after the directory selection page
  AssetsPage := CreateCustomPage(wpSelectDir,
    'Content Folder',
    'Choose where to store your images, videos, and downloaded packs');

  // Description label - explain clearly what this folder is for
  AssetsPathLabel := TNewStaticText.Create(AssetsPage);
  AssetsPathLabel.Parent := AssetsPage.Surface;
  AssetsPathLabel.Caption :=
    'Select a folder for your personal content. This folder will contain:' + #13#10 +
    '  - Your images (flash images)' + #13#10 +
    '  - Your videos (mandatory videos)' + #13#10 +
    '  - Downloaded content packs' + #13#10 + #13#10 +
    'IMPORTANT: This folder is separate from the app and survives updates!';
  AssetsPathLabel.Left := 0;
  AssetsPathLabel.Top := 0;
  AssetsPathLabel.Width := AssetsPage.SurfaceWidth;
  AssetsPathLabel.Height := 85;
  AssetsPathLabel.AutoSize := False;
  AssetsPathLabel.WordWrap := True;

  // Path edit box (editable now)
  AssetsPathEdit := TNewEdit.Create(AssetsPage);
  AssetsPathEdit.Parent := AssetsPage.Surface;
  AssetsPathEdit.Left := 0;
  AssetsPathEdit.Top := 95;
  AssetsPathEdit.Width := AssetsPage.SurfaceWidth - 100;
  AssetsPathEdit.Text := AssetsPath;
  AssetsPathEdit.ReadOnly := False;

  // Browse button
  AssetsBrowseButton := TNewButton.Create(AssetsPage);
  AssetsBrowseButton.Parent := AssetsPage.Surface;
  AssetsBrowseButton.Left := AssetsPage.SurfaceWidth - 90;
  AssetsBrowseButton.Top := 93;
  AssetsBrowseButton.Width := 90;
  AssetsBrowseButton.Height := 25;
  AssetsBrowseButton.Caption := 'Browse...';
  AssetsBrowseButton.OnClick := @AssetsBrowseButtonClick;

  // Assets count info (or new folder notice)
  AssetsInfoLabel := TNewStaticText.Create(AssetsPage);
  AssetsInfoLabel.Parent := AssetsPage.Surface;
  AssetsInfoLabel.Left := 0;
  AssetsInfoLabel.Top := 130;
  AssetsInfoLabel.Width := AssetsPage.SurfaceWidth;
  AssetsInfoLabel.Height := 20;
  AssetsInfoLabel.Font.Style := [fsBold];

  if HasExistingAssets then
    AssetsInfoLabel.Caption := 'Found existing content: ' + IntToStr(ImageCount) + ' images, ' + IntToStr(VideoCount) + ' videos'
  else
    AssetsInfoLabel.Caption := 'New installation - folders will be created automatically';

  // Important notice about packs
  AssetsPreserveLabel := TNewStaticText.Create(AssetsPage);
  AssetsPreserveLabel.Parent := AssetsPage.Surface;
  AssetsPreserveLabel.Left := 0;
  AssetsPreserveLabel.Top := 165;
  AssetsPreserveLabel.Width := AssetsPage.SurfaceWidth;
  AssetsPreserveLabel.Height := 100;
  AssetsPreserveLabel.AutoSize := False;
  AssetsPreserveLabel.WordWrap := True;

  if HasExistingAssets then
  begin
    AssetsPreserveLabel.Caption :=
      'Your existing content will be preserved:' + #13#10 +
      '  - All images and videos remain intact' + #13#10 +
      '  - Downloaded packs will NOT need to be re-downloaded' + #13#10 +
      '  - Settings and progress are kept' + #13#10 + #13#10 +
      'Updates only replace app files, never your content!';
    AssetsPreserveLabel.Font.Color := clGreen;
  end
  else
  begin
    AssetsPreserveLabel.Caption :=
      'Tip: Choose a location with enough space for videos and packs.' + #13#10 +
      'Content packs can be several GB each.' + #13#10 + #13#10 +
      'You can change this folder later in Settings > Assets.' + #13#10 +
      'Downloaded packs will follow your content folder.';
    AssetsPreserveLabel.Font.Color := clNavy;
  end;
end;

// Custom welcome text with upgrade notice
procedure InitializeWizard();
var
  WelcomeText: String;
begin
  // Create the assets confirmation page
  CreateAssetsPage();

  // Set welcome text
  WelcomeText := 'This will install {#MyAppName} version {#MyAppVersion} on your computer.' + #13#10 + #13#10 +
                 '{#MyAppDescription}' + #13#10 + #13#10 +
                 'You will be able to choose where to install the application.' + #13#10 + #13#10;

  if HasOldVelopackInstall or HasExistingAssets then
    WelcomeText := WelcomeText +
                   'NOTE: A previous installation was detected. Your settings and assets will be preserved.' + #13#10 + #13#10;

  WelcomeText := WelcomeText + 'Click Next to continue, or Cancel to exit Setup.';

  WizardForm.WelcomeLabel2.Caption := WelcomeText;
end;
