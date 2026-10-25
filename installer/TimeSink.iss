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
