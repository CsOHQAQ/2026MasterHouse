@echo off
setlocal
cd /d "%~dp0"
title The Guesthouse of Meros - Web Demo

where node >nul 2>nul
if errorlevel 1 goto node_missing

where npm >nul 2>nul
if errorlevel 1 goto node_missing

if not exist "node_modules\.bin\vinext.cmd" (
  echo [Guesthouse] Installing dependencies for the first launch...
  call npm install
  if errorlevel 1 goto launch_failed
)

echo.
echo ========================================================
echo   The Guesthouse of Meros - Web Demo
echo   Local:   http://localhost:3000
echo   LAN:     http://YOUR-LAN-IP:3000
echo   Stop:    Press Ctrl+C
echo ========================================================
echo.

call npm run dev -- --host 0.0.0.0 --port 3000
if errorlevel 1 goto launch_failed
exit /b 0

:node_missing
echo.
echo [Guesthouse] Node.js 22.13 or newer is required.
echo Download: https://nodejs.org/
echo.
pause
exit /b 1

:launch_failed
echo.
echo [Guesthouse] Launch failed. Check the error message above.
echo If port 3000 is already in use, close the previous demo window first.
echo.
pause
exit /b 1
