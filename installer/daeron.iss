; Daeron — Inno Setup script
; Compile from the repo root with:
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\daeron.iss
; Output lands in build\installer\.
;
; The installer expects a Release MSIX produced by `build.cmd Release x64`
; at src\Daeron\bin\AppPackages\Daeron_<MyAppVersion>_x64_Test\. Bump
; MyAppVersion in lockstep with Package.appxmanifest.

#define MyAppName        "Daeron"
#define MyAppVersion     "0.1.1.0"
#define MyAppPublisher   "Larry Hsiao"
#define MyAppURL         "https://github.com/LarryHsiao/Daeron"
#define MyAppPkgDir      "..\src\Daeron\bin\AppPackages\Daeron_" + MyAppVersion + "_x64_Test"
#define MyAppMsix        "Daeron_" + MyAppVersion + "_x64.msix"
#define MyAppCer         "Daeron_" + MyAppVersion + "_x64.cer"
#define MyAppRuntimeMsix "Microsoft.WindowsAppRuntime.1.6.msix"
#define MyAppOutputDir   "..\build\installer"
#define MyAppFamily      "Daeron_md9s12cc7npx8"
#define MyAppAumid       MyAppFamily + "!App"

[Setup]
; A fixed GUID — keep this constant across versions so upgrades replace cleanly.
AppId={{36CD37F6-2109-48CD-9830-28AB8930C04F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#MyAppOutputDir}
OutputBaseFilename=daeron-setup-{#MyAppVersion}
SetupIconFile=..\src\Daeron\Assets\tray.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The cert-trust step writes to LocalMachine\TrustedPeople — admin required.
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#MyAppPkgDir}\{#MyAppMsix}";                          DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppPkgDir}\{#MyAppCer}";                           DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppPkgDir}\Dependencies\x64\{#MyAppRuntimeMsix}";  DestDir: "{app}"; Flags: ignoreversion

[Run]
; 1. Trust the self-signed signing cert in LocalMachine\TrustedPeople.
Filename: "certutil.exe"; \
    Parameters: "-addstore TrustedPeople ""{app}\{#MyAppCer}"""; \
    StatusMsg: "Trusting the Daeron signing certificate…"; \
    Flags: runhidden waituntilterminated

; 2. Register the Windows App Runtime 1.6 framework dependency. Use
;    powershell.exe (Windows PowerShell 5.1); pwsh 7 lacks the Appx module.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-AppxPackage -Path '{app}\{#MyAppRuntimeMsix}' -ForceApplicationShutdown"""; \
    StatusMsg: "Installing Windows App Runtime…"; \
    Flags: runhidden waituntilterminated

; 3. Register Daeron itself.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-AppxPackage -Path '{app}\{#MyAppMsix}' -ForceApplicationShutdown"""; \
    StatusMsg: "Installing Daeron…"; \
    Flags: runhidden waituntilterminated

; 4. Offer to launch the freshly-installed app.
Filename: "explorer.exe"; \
    Parameters: "shell:AppsFolder\{#MyAppAumid}"; \
    Description: "Launch Daeron"; \
    Flags: postinstall nowait skipifsilent

[UninstallRun]
; Remove the registered Daeron package. WindowsAppRuntime is shared with
; other apps — leave it alone. The self-signed cert is harmless to leave
; in TrustedPeople; removing it would break re-install from the same .exe.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-AppxPackage -Name Daeron | Remove-AppxPackage"""; \
    RunOnceId: "RemoveDaeronPackage"; \
    Flags: runhidden waituntilterminated
