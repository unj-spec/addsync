; AddSync 설치 프로그램
; 빌드: tools\build-installer.cmd  (또는 ISCC.exe installer.iss)

#define AppName "AddSync"
#define AppNameEn "AddSync"
#define AppExe "AddSync.exe"
#define AppPublisher "Bowluvr"
#define AppAuthor "made by Bowluvr"
#define AppUrl "https://github.com/unj-spec/addsync"
; exe 의 버전 정보에서 가져온다 (1.0.0.0 → 1.0.0)
#define FullVer GetVersionNumbersString("AddSync.exe")
#define AppVersion Copy(FullVer, 1, RPos(".", FullVer) - 1)

[Setup]
AppId={{8F3C21D4-5B7A-4E29-9C64-2A1D7E6B0F13}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; 모든 사용자에게 설치 (Program Files) — 관리자 권한 필요
PrivilegesRequired=admin
DefaultDirName={autopf}\{#AppNameEn}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

OutputDir=dist
OutputBaseFilename=AddSync-Setup-{#AppVersion}
SetupIconFile=src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible

; ── 프로그램과 같은 톤 ────────────────────────────────────
WizardStyle=modern
WizardSizePercent=110
WizardResizable=no
WizardImageFile=installer\wizard-left.bmp
WizardSmallImageFile=installer\wizard-top.bmp
WizardImageStretch=yes
ShowLanguageDialog=no
DisableWelcomePage=no
AppCopyright={#AppAuthor}

; 업데이트 설치 시 실행 중인 프로그램을 닫고 진행한다
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; GroupDescription: "추가 작업:"

[Files]
Source: "AddSync.exe";         DestDir: "{app}"; Flags: ignoreversion
Source: "LICENSE";             DestDir: "{app}"; Flags: ignoreversion

; 이름을 바꾸기 전 설치본이 남긴 것.
; 예전 바로 가기는 {group} 아래가 아니라 예전 이름의 그룹 폴더에 통째로 들어 있다.
; ({group} 은 이제 'AddSync' 라서 {group}\WoW… 로 지우면 안 지워진다)
[InstallDelete]
Type: files;         Name: "{app}\WowAddonManager.exe"
Type: files;         Name: "{app}\README.md"
Type: filesandordirs; Name: "{autoprograms}\WoW 애드온 매니저"
Type: files;         Name: "{autodesktop}\WoW 애드온 매니저.lnk"
Type: files;         Name: "{autodesktop}\WowAddonManager.exe.lnk"

[Icons]
Name: "{group}\{#AppName}";          Filename: "{app}\{#AppExe}"
Name: "{group}\{#AppName} 제거";      Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";    Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{#AppName} 실행"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNet45OrNewer(): Boolean;
var
  release: Cardinal;
begin
  Result := RegQueryDWordValue(HKEY_LOCAL_MACHINE,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release)
    and (release >= 378389);
end;

function InitializeSetup(): Boolean;
var
  rc: Integer;
begin
  Result := True;
  if not IsDotNet45OrNewer() then
  begin
    if MsgBox('이 프로그램은 .NET Framework 4.5 이상이 필요합니다.' + #13#10 +
              '윈도우 10/11 에는 기본으로 들어 있습니다.' + #13#10#13#10 +
              '다운로드 페이지를 열까요?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework',
                '', '', SW_SHOW, ewNoWait, rc);
    Result := False;
  end;
end;

{ ── 프로그램과 같은 어두운 톤으로 ──────────────────────────
  델파이 색은 $00BBGGRR 순서다 (Theme.cs 의 RGB 를 뒤집은 값). }
const
  cPanel  = $00191412;   { 18, 20, 25  }
  cTitle  = $00120F0D;   { 13, 15, 18  }
  cField  = $00120E0C;   { 12, 14, 18  }
  cText   = $00ECE8E6;   { 230,232,236 }
  cSoft   = $00CBC0B9;   { 185,192,203 }
  cMuted  = $0083746C;   { 108,116,131 }
  cLine   = $00292320;   { 31, 35, 41  }

function SetWindowTheme(hwnd: HWND; pszSubAppName, pszSubIdList: WideString): HResult;
  external 'SetWindowTheme@uxtheme.dll stdcall';

var
  MadeBy: TNewStaticText;

{ 스크롤 막대까지 어둡게 (안 하면 흰 막대가 남는다) }
procedure DarkScroll(h: HWND);
begin
  try
    SetWindowTheme(h, 'DarkMode_Explorer', '');
  except
  end;
end;

procedure DarkenTree(Parent: TWinControl);
var
  i: Integer;
  c: TControl;
begin
  for i := 0 to Parent.ControlCount - 1 do
  begin
    c := Parent.Controls[i];

    if c is TNewStaticText then
    begin
      TNewStaticText(c).Color := cPanel;
      TNewStaticText(c).Font.Color := cSoft;
    end
    else if c is TNewCheckListBox then
    begin
      TNewCheckListBox(c).Color := cField;
      TNewCheckListBox(c).Font.Color := cText;
      TNewCheckListBox(c).BorderStyle := bsNone;
    end
    else if c is TNewEdit then
    begin
      TNewEdit(c).Color := cField;
      TNewEdit(c).Font.Color := cText;
    end
    else if c is TNewMemo then
    begin
      TNewMemo(c).Color := cField;
      TNewMemo(c).Font.Color := cText;
    end
    else if c is TNewListBox then
    begin
      TNewListBox(c).Color := cField;
      TNewListBox(c).Font.Color := cText;
    end
    else if c is TCheckBox then
    begin
      TCheckBox(c).Color := cPanel;
      TCheckBox(c).Font.Color := cSoft;
    end
    else if c is TRadioButton then
    begin
      TRadioButton(c).Color := cPanel;
      TRadioButton(c).Font.Color := cSoft;
    end
    else if c is TLabel then
    begin
      TLabel(c).Color := cPanel;
      TLabel(c).Font.Color := cSoft;
    end
    else if c is TBevel then
      TBevel(c).Visible := False
    else if c is TPanel then
      TPanel(c).Color := cPanel
    else if c is TNewNotebookPage then
      TNewNotebookPage(c).Color := cPanel;

    if c is TWinControl then
      DarkenTree(TWinControl(c));
  end;
end;

procedure ApplyDark();
begin
  WizardForm.Color := cPanel;
  WizardForm.MainPanel.Color := cTitle;
  WizardForm.Bevel.Visible := False;
  WizardForm.Bevel1.Visible := False;

  DarkenTree(WizardForm);

  { 머리글은 강조색 글씨로 }
  WizardForm.PageNameLabel.Font.Color := cText;
  WizardForm.PageDescriptionLabel.Font.Color := cMuted;
  WizardForm.PageNameLabel.Color := cTitle;
  WizardForm.PageDescriptionLabel.Color := cTitle;

  WizardForm.WelcomeLabel1.Font.Color := cText;
  WizardForm.WelcomeLabel2.Font.Color := cSoft;
  WizardForm.FinishedHeadingLabel.Font.Color := cText;
  WizardForm.FinishedLabel.Font.Color := cSoft;

  DarkScroll(WizardForm.ReadyMemo.Handle);
  DarkScroll(WizardForm.TasksList.Handle);
  DarkScroll(WizardForm.ComponentsList.Handle);
  DarkScroll(WizardForm.RunList.Handle);
  DarkScroll(WizardForm.LicenseMemo.Handle);
  DarkScroll(WizardForm.InfoBeforeMemo.Handle);
  DarkScroll(WizardForm.InfoAfterMemo.Handle);
end;

procedure InitializeWizard();
begin
  ApplyDark();

  MadeBy := TNewStaticText.Create(WizardForm);
  MadeBy.Parent := WizardForm;
  MadeBy.Caption := '{#AppAuthor}';
  MadeBy.Font.Color := cMuted;
  MadeBy.Color := cPanel;
  MadeBy.AutoSize := True;
  MadeBy.Left := ScaleX(18);
  MadeBy.Top := WizardForm.CancelButton.Top + ScaleY(8);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  { 페이지마다 새 컨트롤이 생기므로 다시 입힌다 }
  ApplyDark();
  MadeBy.BringToFront();
end;
