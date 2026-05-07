@echo off
REM Daeron install wrapper — registers the freshly-built MSIX on this machine.
REM
REM Run build.cmd first to produce the .msix, then install.cmd to register it.
REM First-time install will require:
REM   1. The signing cert (Daeron_TemporaryKey.cer) trusted in Local Machine
REM      \ Trusted People — see setup-cert.cmd, run once with admin.
REM   2. The Windows App Runtime 1.6, already a build prerequisite.

setlocal EnableDelayedExpansion

set "PKG_DIR=%~dp0src\Daeron\bin\AppPackages"

if not exist "%PKG_DIR%" (
    echo ERROR: No AppPackages folder. Run build.cmd first.
    exit /b 1
)

REM Find the most recent .msix under the AppPackages tree.
set "MSIX="
for /f "delims=" %%F in ('dir /s /b /o-d "%PKG_DIR%\*.msix" 2^>nul') do (
    if not defined MSIX set "MSIX=%%F"
)

if not defined MSIX (
    echo ERROR: No .msix produced. Check build output.
    exit /b 1
)

echo Installing: !MSIX!
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-AppxPackage -Path '%MSIX%' -ForceApplicationShutdown"
exit /b !ERRORLEVEL!
