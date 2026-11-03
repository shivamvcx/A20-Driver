; Redgear A20 Control Center - per-user installer (Inno Setup 6)
; Installs exactly the same binary as the portable build; no extra features, services or drivers.
; Built by build.bat after the portable exe:  ISCC installer\RedgearA20.iss

#define AppName "Redgear A20 Control Center"
#define AppExe "RedgearA20.exe"
#define SourceExe AddBackslash(SourcePath) + "..\release\RedgearA20-win-x64.exe"
#define FullVersion GetVersionNumbersString(SourceExe)
#define AppVersion Copy(FullVersion, 1, RPos(".", FullVersion) - 1)

[Setup]
AppId={{0D6C3141-13D7-410F-A775-B2301C4B7061}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=shivamvcx
AppPublisherURL=https://github.com/shivamvcx/A20-Driver
AppSupportURL=https://github.com/shivamvcx/A20-Driver/issues
AppUpdatesURL=https://github.com/shivamvcx/A20-Driver/releases/latest
VersionInfoVersion={#FullVersion}
; Same folder the app already uses for its auto-start copy and profiles.
DefaultDirName={localappdata}\RedgearA20
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=6.1sp1
OutputDir=..\release
OutputBaseFilename=RedgearA20-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
CloseApplications=no
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppExe}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The app writes this value itself when a background mode is chosen; the installer only
; makes sure it is removed again on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "RedgearA20ControlCenter"; Flags: uninsdeletevalue

[UninstallDelete]
; Leftovers from in-app updates. Profiles (profile.json, profiles\) are kept.
Type: files; Name: "{app}\*.old"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure StopRunningApp();
var
  ResultCode: Integer;
  Exe: String;
begin
  Exe := ExpandConstant('{app}\{#AppExe}');
  // Polite shutdown first (the app saves nothing on exit, but releases the mouse cleanly).
  if FileExists(Exe) then
    Exec(Exe, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM RedgearA20-win-x64.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopRunningApp();
end;
