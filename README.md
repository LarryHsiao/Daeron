# Daeron

A lean Windows app that turns the PC into a Bluetooth audio receiver. Pair a phone to the PC, choose it in Daeron, and the phone's audio plays through the PC's speakers.

Named for Daeron, minstrel of Doriath — the greatest singer of the Eldar, and deviser of the Cirth. A vessel for another's voice.

## Status

Current build: `v0.1.1.0`. The original roadmap stands in place:

1. WinUI 3 config window.
2. Tray host — minimize-to-tray, "Open settings", "Exit", live state surfaced on the icon.
3. Audio path — device enumeration via `DeviceWatcher`, connection via `AudioPlaybackConnection`.
4. Auto-reconnect — the watcher re-opens the connection when the chosen phone returns.
5. Persisted config — last-chosen device, auto-reconnect, and start-with-Windows saved to `%LocalAppData%\Daeron\config.json`.

Signed with a self-signed development certificate (`CN=Daeron-Dev`). End-user installation therefore requires a one-time trust step — see **Install** below.

## Install (from a release)

Download `Daeron-<version>-x64.zip` from the GitHub release page and unzip it. The folder contains the `.msix`, the `.cer` it was signed with, the `Microsoft.WindowsAppRuntime.1.6` framework dependency, and the `Add-AppDevPackage.ps1` helper.

Two steps:

1. **Trust the certificate.** Right-click `Daeron_<version>_x64.cer` → **Install Certificate** → choose **Local Machine** → **Place all certificates in the following store** → **Trusted People**. UAC will prompt; admin is required, once per machine.
2. **Register the package.** Right-click `Add-AppDevPackage.ps1` → **Run with PowerShell**. The helper installs the WindowsAppRuntime dependency from `Dependencies\x64\` and then registers the `.msix`.

> Use **Windows PowerShell 5.1** (`powershell.exe`), not PowerShell 7 (`pwsh.exe`). The latter does not auto-load the `Appx` module, and the helper will fail with `Add-AppxPackage` reported as an unknown command.

After install, Daeron appears in the Start Menu and lives in the tray. The config window opens via the tray's "Open settings."

Requires Windows 10 build 19041 or later, x64.

## Build

Requires:

- **Visual Studio Build Tools 2022** with these workloads/components:
  - `Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools`
  - `Microsoft.VisualStudio.Workload.UniversalBuildTools` (carries the AppxPackage MSBuild tasks WinUI 3 needs)
  - `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` (MSVC, used by the WinUI 3 XAML markup compiler)
  - `Microsoft.VisualStudio.Component.Windows11SDK.22621`
- **Windows App Runtime 1.6** installed system-wide for unpackaged dev runs — `winget install Microsoft.WindowsAppRuntime.1.6`. (The MSIX install path bundles its own copy.)
- Windows 10 build 19041 or later.

The .NET SDK alone is **not** enough — `dotnet build` will fail because the AppxPackage tasks live only with Visual Studio's MSBuild. Use the wrapper:

```
.\build.cmd            # Debug | x64 by default
.\build.cmd Release x64
```

The wrapper invokes VS Build Tools' MSBuild and produces a signed MSIX under `src\Daeron\bin\AppPackages\`. Register it on this machine with:

```
.\setup-cert.cmd       # once, with admin — trusts the dev cert in LocalMachine\TrustedPeople
.\install.cmd          # registers the most recently built MSIX
```
