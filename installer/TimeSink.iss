; TimeSink per-user installer. Compile with: iscc /DAppVersion=1.0.0 /DStageDir=<artifacts\staging\TimeSink> /O<artifacts> TimeSink.iss
; (installer\Build-Package.ps1 does this when iscc is available.)
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
DisableWelcomePage=no
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
Filename: "{app}\TimeSink.Agent.exe"; Description: "Start TimeSink now"; Flags: nowait postinstall skipifsilent; Check: NotIsUpdate

; "Start with Windows" is owned by the app (Settings > General writes/removes HKCU\...\Run\TimeSink, decision 46),
; so the installer writes no Run entry. It only removes it on uninstall.

[Code]
const
  WM_CLOSE = $0010;

function FindWindowByClassName(lpClassName, lpWindowName: String): HWND;
  external 'FindWindowW@user32.dll stdcall';
function PostMessage(hWnd: HWND; Msg: Cardinal; wParam, lParam: LongInt): Boolean;
  external 'PostMessageW@user32.dll stdcall';

var
  IsUpdate: Boolean;
  WasRunning: Boolean;
  InstalledVersion: String;

function NotIsUpdate(): Boolean;
begin
  Result := not IsUpdate;
end;

function IsProcessRunning(const ExeName: String): Boolean;
var
  rc: Integer;
begin
  Exec(ExpandConstant('{sys}\cmd.exe'),
       Format('/c tasklist /FI "IMAGENAME eq %s" | findstr /I "%s"', [ExeName, ExeName]),
       '', SW_HIDE, ewWaitUntilTerminated, rc);
  Result := (rc = 0);
end;

procedure StopTimeSink();
var
  rc: Integer;
  hWnd: HWND;
  I: Integer;
  AgentRunning, UiRunning: Boolean;
begin
  AgentRunning := IsProcessRunning('TimeSink.Agent.exe');
  UiRunning := IsProcessRunning('TimeSink.UI.exe');

  if AgentRunning or UiRunning then
    WasRunning := True;

  if not (AgentRunning or UiRunning) then
    Exit;

  // 1. Graceful close: send WM_CLOSE to the Agent message window so SQLite flushes and services stop cleanly
  hWnd := FindWindowByClassName('TimeSink.Agent.MessageWindow', '');
  if hWnd <> 0 then
    PostMessage(hWnd, WM_CLOSE, 0, 0);

  // Ask UI process to terminate gracefully
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM TimeSink.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);

  // Allow up to 1.5 seconds for processes to cleanly shut down and release file handles
  for I := 1 to 15 do
  begin
    Sleep(100);
    if (not IsProcessRunning('TimeSink.Agent.exe')) and (not IsProcessRunning('TimeSink.UI.exe')) then
      Break;
  end;

  // 2. Fallback: force kill if still running to ensure files are not locked
  if IsProcessRunning('TimeSink.UI.exe') then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TimeSink.UI.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  if IsProcessRunning('TimeSink.Agent.exe') then
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TimeSink.Agent.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

function CheckInstalled(): Boolean;
var
  InstallPath: String;
  Ver: String;
begin
  Result := False;
  // 1. Check registry under HKCU Uninstall
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F1D2C58-3B7E-4C0A-9A53-71D4E2B8C9A1}_is1', 'InstallLocation', InstallPath) then
  begin
    if DirExists(InstallPath) and FileExists(AddBackslash(InstallPath) + 'TimeSink.Agent.exe') then
      Result := True;
  end;

  if (not Result) and RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F1D2C58-3B7E-4C0A-9A53-71D4E2B8C9A1}_is1', 'Inno Setup: App Path', InstallPath) then
  begin
    if DirExists(InstallPath) and FileExists(AddBackslash(InstallPath) + 'TimeSink.Agent.exe') then
      Result := True;
  end;

  // 2. Check default install directory (%LOCALAPPDATA%\Programs\TimeSink)
  if not Result then
  begin
    InstallPath := ExpandConstant('{localappdata}\Programs\TimeSink');
    if FileExists(AddBackslash(InstallPath) + 'TimeSink.Agent.exe') then
      Result := True;
  end;

  if Result and (InstalledVersion = '') then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F1D2C58-3B7E-4C0A-9A53-71D4E2B8C9A1}_is1', 'DisplayVersion', Ver) then
      InstalledVersion := Ver;
  end;
end;

function InitializeSetup(): Boolean;
begin
  IsUpdate := CheckInstalled();
  Result := True;
end;

procedure InitializeWizard();
begin
  if (not IsUpdate) and (WizardDirValue <> '') then
  begin
    if FileExists(AddBackslash(WizardDirValue) + 'TimeSink.Agent.exe') then
      IsUpdate := True;
  end;

  if IsUpdate then
  begin
    WizardForm.Caption := 'Update - TimeSink';
    WizardForm.WelcomeLabel1.Caption := 'Update TimeSink';
    if InstalledVersion <> '' then
      WizardForm.WelcomeLabel2.Caption :=
        'TimeSink version ' + InstalledVersion + ' is currently installed.' + #13#10#13#10 +
        'Click Update to install version ' + ExpandConstant('{#AppVersion}') + '.' + #13#10#13#10 +
        'Your tracking history, settings, and data will be kept intact.'
    else
      WizardForm.WelcomeLabel2.Caption :=
        'TimeSink is currently installed.' + #13#10#13#10 +
        'Click Update to install version ' + ExpandConstant('{#AppVersion}') + '.' + #13#10#13#10 +
        'Your tracking history, settings, and data will be kept intact.';

    WizardForm.NextButton.Caption := '&Update';
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if IsUpdate then
  begin
    // Fast update: jump straight from Welcome page to installation
    if (PageID = wpSelectDir) or (PageID = wpSelectProgramGroup) or (PageID = wpSelectTasks) or (PageID = wpReady) then
      Result := True;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if IsUpdate then
  begin
    if CurPageID = wpWelcome then
      WizardForm.NextButton.Caption := '&Update'
    else if CurPageID = wpFinished then
    begin
      WizardForm.FinishedHeadingLabel.Caption := 'Update Complete';
      WizardForm.FinishedLabel.Caption := 'TimeSink has been successfully updated to version ' + ExpandConstant('{#AppVersion}') + '.';
      WizardForm.NextButton.Caption := '&Close';
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopTimeSink();  // Stop running instances cleanly before file replacement
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  rc: Integer;
begin
  if (CurStep = ssPostInstall) and IsUpdate and WasRunning then
  begin
    // Auto-relaunch TimeSink in the background if it was running before update
    Exec(ExpandConstant('{app}\TimeSink.Agent.exe'), '', '', SW_SHOWNORMAL, ewNoWait, rc);
  end;
end;

function SetCursorPos(X, Y: Integer): Boolean;
  external 'SetCursorPos@user32.dll stdcall';
function ClientToScreen(hWnd: HWND; var Pt: TPoint): Boolean;
  external 'ClientToScreen@user32.dll stdcall';

var
  AskNoButton: TNewButton;

procedure AskFormShown(Sender: TObject);
var
  Pt: TPoint;
begin
  // Park the mouse on "No" so a stray click keeps the data.
  Pt.X := AskNoButton.Width div 2;
  Pt.Y := AskNoButton.Height div 2;
  ClientToScreen(AskNoButton.Handle, Pt);
  SetCursorPos(Pt.X, Pt.Y);
end;

// True only if the user clicks Yes. "No", Esc and the close box all answer No.
function AskDeleteData(const Question: String): Boolean;
var
  Form: TSetupForm;
  Msg: TNewStaticText;
  YesButton: TNewButton;
begin
  Form := CreateCustomForm(ScaleX(420), ScaleY(150), False, True);
  try
    Form.Caption := 'Delete TimeSink data?';
    Form.Position := poScreenCenter;
    Form.OnShow := @AskFormShown;

    Msg := TNewStaticText.Create(Form);
    Msg.Parent := Form;
    Msg.AutoSize := False;
    Msg.WordWrap := True;
    Msg.SetBounds(ScaleX(16), ScaleY(16), Form.ClientWidth - ScaleX(32), ScaleY(90));
    Msg.Caption := Question;

    YesButton := TNewButton.Create(Form);
    YesButton.Parent := Form;
    YesButton.Caption := '&Yes, delete';
    YesButton.ModalResult := mrYes;
    YesButton.SetBounds(Form.ClientWidth - ScaleX(184), Form.ClientHeight - ScaleY(40), ScaleX(80), ScaleY(26));

    AskNoButton := TNewButton.Create(Form);
    AskNoButton.Parent := Form;
    AskNoButton.Caption := '&No, keep it';
    AskNoButton.ModalResult := mrNo;
    AskNoButton.Cancel := True;
    AskNoButton.Default := True;
    AskNoButton.SetBounds(Form.ClientWidth - ScaleX(96), Form.ClientHeight - ScaleY(40), ScaleX(80), ScaleY(26));
    Form.ActiveControl := AskNoButton;

    Result := Form.ShowModal = mrYes;
  finally
    Form.Free;
  end;
end;

// Three separate confirmations; any "No" keeps the data.
function ConfirmDeleteData(const Dir: String): Boolean;
begin
  if UninstallSilent then
  begin
    Result := False;  // nobody to ask: keep the data
    Exit;
  end;
  Result :=
    AskDeleteData('Also delete your TimeSink data (usage history, settings, backups) in ' + Dir + '?') and
    AskDeleteData('Are you sure? Your usage history will be gone for good.') and
    AskDeleteData('Last chance: really delete all TimeSink data? This cannot be undone.');
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
      if ConfirmDeleteData(ExpandConstant('{localappdata}\TimeSink')) then
        DelTree(ExpandConstant('{localappdata}\TimeSink'), True, True, True);
  end;
end;
