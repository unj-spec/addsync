@echo off
rem Build the installer (requires Inno Setup 6)
setlocal
pushd "%~dp0.."

call "%CD%\build.cmd"
if errorlevel 1 exit /b 1

rem 설치 화면 그림 (없으면 만든다)
if not exist "%CD%\installer\wizard-left.bmp" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\tools\make-wizard-images.ps1"
)

set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" (
  echo Inno Setup 6 not found. Install from https://jrsoftware.org/isdl.php
  exit /b 1
)

"%ISCC%" /Q installer.iss
if errorlevel 1 (
  echo.
  echo INSTALLER BUILD FAILED
  exit /b 1
)

echo.
echo Installer created in dist\
dir /b dist\*.exe