@echo off
title Mocha Multi-Utility Bot Setup
cd /d "%~dp0"
if not exist .env (
  copy .env.example .env >nul
  echo Created .env - open it and add your bot token, client ID, guild ID, and owner ID.
)
call npm install
if errorlevel 1 (
  echo.
  echo Installation failed. Make sure Node.js 18 or newer is installed.
  pause
  exit /b 1
)
echo.
echo Setup complete. Edit .env, then run deploy.bat and start.bat.
pause
