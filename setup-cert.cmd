@echo off
REM Daeron one-time cert trust — run with admin.
REM
REM Imports the dev signing cert (public part) into Local Machine
REM \ Trusted People, so MSIX packages signed with it can be installed.
REM
REM Run once per machine. Re-run if the cert is regenerated.

setlocal

set "CER=%~dp0src\Daeron\Daeron_TemporaryKey.cer"

if not exist "%CER%" (
    echo ERROR: %CER% not found.
    echo Generate the cert first (see CLAUDE-set up notes).
    exit /b 1
)

net session >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: This script must be run as Administrator.
    echo Right-click and choose "Run as administrator".
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Import-Certificate -FilePath '%CER%' -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null; Write-Host 'Trusted Daeron-Dev cert in LocalMachine\TrustedPeople.'"
exit /b %ERRORLEVEL%
