@echo off
chcp 65001 >nul
set SRC=%~1
if "%SRC%"=="" set SRC=%~dp0export
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Addons.ps1" -Source "%SRC%"
echo.
pause
