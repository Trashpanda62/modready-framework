; ============================================================================
;  ModReady All-in-One Installer  (v2.0 goal)
;  One download that bundles BLSE + the four BUTR dependency modules so a new
;  user is modding-ready in a single step. (The ModReady framework module is NOT
;  bundled -- this installer is the dependency stack only.)
;
;  Bundles, with permission:
;    - BLSE (Bannerlord Software Extender) -- MIT, (c) 2021-2022 BUTR.
;      Its LICENSE ships in {app}\Modules\Bannerlord.Harmony\licenses\.
;    - The FOUR BUTR dependency modules ONLY (Bannerlord.Harmony,
;      Bannerlord.UIExtenderEx, Bannerlord.ButterLib, Bannerlord.MBOptionScreen).
;      The ModReady framework module itself is NOT bundled (separate mod).
;      ModReady.Foundation.dll still ships inside each dep's bin -- it is their
;      shared resolve-hook shim and the four modules cannot load without it.
;
;  Build with installer\Build-Installer.ps1 (stages payload, then runs ISCC).
;  Requires Inno Setup 6 (ISCC.exe) and a staged BLSE copy -- see README.md.
; ============================================================================

#define AppName "ModReady (All-in-One)"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppPublisher "Maxfield Management Group"
#define LauncherExe "Bannerlord.BLSE.LauncherEx.exe"

[Setup]
; Keep this AppId STABLE across releases so updates replace in place.
AppId={{8B0F2C7A-3D54-4E61-9A2B-1C7E9F4A0D21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; The install dir IS the Bannerlord root; resolved by GetBannerlordDir below.
DefaultDirName={code:GetBannerlordDir}
DisableProgramGroupPage=yes
DisableDirPage=no
DirExistsWarning=no
AppendDefaultDirName=no
UsePreviousAppDir=yes
OutputDir=..\dist
OutputBaseFilename=ModReady-AllInOne-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
; BLSE's MIT license shown on the license page (satisfies the notice requirement
; up front; the file is also copied into the install).
LicenseFile=payload\LICENSES\BLSE-LICENSE.txt
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut to the BLSE launcher"; GroupDescription: "Shortcuts:"

[Files]
; The four BUTR dependency module folders only (clean staging from dist\Modules;
; Build-Installer.ps1 copies only the 4 deps into payload\Modules -- the ModReady
; module folder is deliberately excluded, so this wildcard never sees it).
Source: "payload\Modules\*"; DestDir: "{app}\Modules"; Flags: recursesubdirs createallsubdirs ignoreversion
; BLSE binaries -> game bin (the documented BLSE install location).
Source: "payload\BLSE\*"; DestDir: "{app}\bin\Win64_Shipping_Client"; Flags: recursesubdirs createallsubdirs ignoreversion
; License notices (BLSE MIT, third-party MIT for Harmony/Cecil/MonoMod/Newtonsoft).
; Installed under the Harmony dependency module (always present) -- the ModReady
; module folder is intentionally NOT shipped by this installer, so we must NOT
; target {app}\Modules\ModReady\ or it would recreate that folder on disk.
Source: "payload\LICENSES\*"; DestDir: "{app}\Modules\Bannerlord.Harmony\licenses"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autodesktop}\Bannerlord (BLSE)"; Filename: "{app}\bin\Win64_Shipping_Client\{#LauncherExe}"; WorkingDir: "{app}\bin\Win64_Shipping_Client"; Tasks: desktopicon

[Run]
Filename: "{app}\bin\Win64_Shipping_Client\{#LauncherExe}"; Description: "Launch the BLSE launcher now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; M5 (Phase 4.2): remove the persistent User-scope CREST_SHOW_STUBS env var
; ModReady sets at runtime (BLSE launcher hide-stubs opt-out). Best-effort.
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Environment"" /v CREST_SHOW_STUBS /f"; Flags: runhidden; RunOnceId: "RemoveCrestShowStubsEnv"

[Code]
{ ---- Locate the Bannerlord install ------------------------------------------ }
{ Tries the Steam registry path + the common library location; falls back to the
  well-known default. The user can always browse/correct on the directory page. }
function SteamCommonBannerlord(): String;
var
  SteamPath: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, 'Software\Valve\Steam', 'SteamPath', SteamPath) then
  begin
    StringChangeEx(SteamPath, '/', '\', True);
    if DirExists(SteamPath + '\steamapps\common\Mount & Blade II Bannerlord') then
      Result := SteamPath + '\steamapps\common\Mount & Blade II Bannerlord';
  end;
end;

function GetBannerlordDir(Param: String): String;
var
  Candidate: String;
begin
  Candidate := SteamCommonBannerlord();
  if Candidate <> '' then
  begin
    Result := Candidate;
    Exit;
  end;
  Candidate := ExpandConstant('{commonpf32}') + '\Steam\steamapps\common\Mount & Blade II Bannerlord';
  if DirExists(Candidate) then
    Result := Candidate
  else
    Result := Candidate; { still the best default to show; user can browse }
end;

{ ---- Validate the chosen folder really is a Bannerlord install -------------- }
function LooksLikeBannerlord(Dir: String): Boolean;
begin
  Result := FileExists(Dir + '\bin\Win64_Shipping_Client\Bannerlord.exe');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    if not LooksLikeBannerlord(WizardDirValue) then
    begin
      Result := False;
      MsgBox('That folder does not look like a Mount & Blade II: Bannerlord install '
        + '(no bin\Win64_Shipping_Client\Bannerlord.exe found).' + #13#10#13#10
        + 'Browse to your Bannerlord folder -- the one that contains the "bin" and '
        + '"Modules" folders.', mbError, MB_OK);
    end;
  end;
end;
