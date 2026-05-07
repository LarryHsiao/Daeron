@echo off
REM Daeron build wrapper — invokes VS Build Tools' MSBuild on the project.
REM
REM Produces a signed MSIX in src\Daeron\bin\AppPackages\.
REM Run install.cmd afterwards to register the package on this machine.
REM
REM Usage: build.cmd [Configuration] [Platform]
REM   Configuration: Debug | Release   (default: Debug)
REM   Platform:      x64 | x86 | ARM64 (default: x64)

setlocal EnableDelayedExpansion

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

set "PLATFORM=%~2"
if "%PLATFORM%"=="" set "PLATFORM=x64"

set "MSBUILD=!ProgramFiles(x86)!\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"

if not exist "!MSBUILD!" goto :missing

"!MSBUILD!" "%~dp0src\Daeron\Daeron.csproj" -t:Restore,Build -p:Configuration=!CONFIG! -p:Platform=!PLATFORM! -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never -nologo -v:minimal
exit /b !ERRORLEVEL!

:missing
echo ERROR: VS Build Tools MSBuild not found at:
echo   !MSBUILD!
echo.
echo Install Visual Studio Build Tools 2022 with the
echo "Managed Desktop Build Tools" workload, or adjust this script.
exit /b 1
