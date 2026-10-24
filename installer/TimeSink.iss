; TimeSink per-user installer. Compile with: iscc /DAppVersion=1.0.0 /DStageDir=<artifacts\staging\TimeSink> /O<artifacts> TimeSink.iss
; (installer\Build-Package.ps1 does this when iscc is available.) Not compiled on the dev machine that wrote it.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef StageDir
  #define StageDir "..\artifacts\staging\TimeSink"
#endif

[Setup]
AppId={{6F1D2C58-3B7E-4C0A-9A53-71D4E2B8C9A1}
AppName=TimeSink
AppVersion={#AppVersion}
AppPublisher=TimeSink
DefaultDirName={localappdata}\Programs\TimeSink
DefaultGroupName=TimeSink
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=TimeSink-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\TimeSink.Agent.exe
WizardStyle=modern
CloseApplications=force

[Tasks]
Name: "startmenu"; Description: "Create a Start Menu shortcut"; Flags: checkedonce

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[InstallDelete]
; Older versions kept the UI and runtime flat in the root; clear them when upgrading into the ui\ layout.
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\TimeSink.UI.*"
Type: filesandordirs; Name: "{app}\themes"

[Icons]
Name: "{userprograms}\TimeSink"; Filename: "{app}\TimeSink.Agent.exe"; Tasks: startmenu

[Run]
Filename: "{app}\TimeSink.Agent.exe"; Description: "Start TimeSink now"; Flags: nowait postinstall skipifsilent

; "Start with Windows" is owned by the app (Settings > General writes/removes HKCU\...\Run\TimeSink, decision 46),
; so the installer writes no Run entry. It only removes it on uninstall.

[Code]
procedure StopTimeSink();
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TimeSink.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TimeSink.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopTimeSink();  // upgrade over a running copy
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopTimeSink();
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TimeSink');
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    if DirExists(ExpandConstant('{localappdata}\TimeSink')) then
      if MsgBox('Also delete your TimeSink data (usage history, settings, backups) in ' +
                ExpandConstant('{localappdata}\TimeSink') + '?' + #13#10 + 'This cannot be undone.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{localappdata}\TimeSink'), True, True, True);
  end;
end;
