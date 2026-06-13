; ============================================================================
;  BetaDeps All-in-One Installer  (v2.0 goal)
;  One download that bundles BLSE + the five BetaDeps modules so a new user is
;  modding-ready in a single step.
;
;  Bundles, with permission:
;    - BLSE (Bannerlord Software Extender) -- MIT, (c) 2021-2022 BUTR.
;      Its LICENSE ships in {app}\Modules\BetaDeps\licenses\.
;    - The five BetaDeps modules (BetaDeps + the four BUTR-named dep modules).
;
;  Build with installer\Build-Installer.ps1 (stages payload, then runs ISCC).
;  Requires Inno Setup 6 (ISCC.exe) and a staged BLSE copy -- see README.md.
; ============================================================================

#define AppName "BetaDeps (All-in-One)"
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
OutputBaseFilename=BetaDeps-AllInOne-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
; BLSE's MIT license shown on the license page (satisfies the notice requirement
; up front; the file is also copied into the install).
LicenseFile=payload\LICENSES\BLSE-LICENSE.txt
SetupLogging=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut to the BLSE launcher"; GroupDescription: "Shortcuts:"

[Files]
; The five BetaDeps module folders (clean staging from dist\Modules).
Source: "payload\Modules\*"; DestDir: "{app}\Modules"; Flags: recursesubdirs createallsubdirs ignoreversion
; BLSE binaries -> game bin (the documented BLSE install location).
Source: "payload\BLSE\*"; DestDir: "{app}\bin\Win64_Shipping_Client"; Flags: recursesubdirs createallsubdirs ignoreversion
; License notices (BLSE MIT, BetaDeps, third-party).
Source: "payload\LICENSES\*"; DestDir: "{app}\Modules\BetaDeps\licenses"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autodesktop}\Bannerlord (BLSE)"; Filename: "{app}\bin\Win64_Shipping_Client\{#LauncherExe}"; WorkingDir: "{app}\bin\Win64_Shipping_Client"; Tasks: desktopicon

[Run]
Filename: "{app}\bin\Win64_Shipping_Client\{#LauncherExe}"; Description: "Launch the BLSE launcher now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; M5 (Phase 4.2): remove the persistent User-scope CREST_SHOW_STUBS env var
; BetaDeps sets at runtime (BLSE launcher hide-stubs opt-out). Best-effort.
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
