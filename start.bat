@echo off
setlocal
cd /d "%~dp0"

if exist "%~dp0dist\win-x64\WifiTraffic.exe" (
  start "" "%~dp0dist\win-x64\WifiTraffic.exe"
  exit /b 0
)

echo WiFi Traffic has not been built yet.
echo Run windows-install.bat first.
pause
