@echo off
title JournalApp Installer
echo ====================================================
echo             Installing JournalApp...
echo ====================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
echo.
echo ====================================================
echo Done. You can close this window.
echo ====================================================
pause
