@echo off
rem Build WowAddonManager.exe with the .NET Framework csc.exe (no SDK required)
setlocal

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found - .NET Framework 4.x is required.
  exit /b 1
)

if not exist "%~dp0src\app.ico" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\make-icon.ps1"
)

set REFS=/reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /utf8output ^
  /win32icon:"%~dp0src\app.ico" /resource:"%~dp0src\app.ico",app.ico ^
  /out:"%~dp0WowAddonManager.exe" %REFS% ^
  "%~dp0src\AppInfo.cs" "%~dp0src\WowCore.cs" "%~dp0src\Updater.cs" "%~dp0src\Theme.cs" "%~dp0src\MainForm.cs"

if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)

echo.
echo Built: %~dp0WowAddonManager.exe
