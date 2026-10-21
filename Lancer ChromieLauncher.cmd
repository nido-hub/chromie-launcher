@echo off
if not exist "%~dp0dist\win-x64\ChromieLauncher.exe" (
  echo Le logiciel doit etre compile. Consulter README.md.
  pause
  exit /b 1
)
start "" "%~dp0dist\win-x64\ChromieLauncher.exe"
