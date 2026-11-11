; WoW 애드온 매니저 설치 프로그램
; 빌드: tools\build-installer.cmd  (또는 ISCC.exe installer.iss)

#define AppName "WoW 애드온 매니저"
#define AppNameEn "WoW Addon Manager"
#define AppExe "WowAddonManager.exe"
#define AppPublisher "unj-spec"
#define AppUrl "https://github.com/unj-spec/wow-addon-manager"
; exe 의 버전 정보에서 가져온다 (1.0.0.0 → 1.0.0)
#define FullVer GetVersionNumbersString("WowAddonManager.exe")
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
OutputBaseFilename=WowAddonManager-Setup-{#AppVersion}
SetupIconFile=src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

; 업데이트 설치 시 실행 중인 프로그램을 닫고 진행한다
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; GroupDescription: "추가 작업:"

[Files]
Source: "WowAddonManager.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "README.md";           DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "LICENSE";             DestDir: "{app}"; Flags: ignoreversion

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
