# Daeron

A lean Windows app that turns the PC into a Bluetooth audio receiver. Pair a phone to the PC, choose it in Daeron, and the phone's audio plays through the PC's speakers.

Named for Daeron, minstrel of Doriath — the greatest singer of the Eldar, and deviser of the Cirth. A vessel for another's voice.

## Status

**Step 1** — empty WinUI 3 shell. The config window opens; no tray, no audio path yet.

Roadmap:

1. Empty WinUI 3 app — config window only. *(this step)*
2. Tray host — minimize-to-tray, "Open settings", "Exit".
3. Audio path — device enumeration, `AudioPlaybackConnection`, status reflects state.
4. Auto-reconnect — `DeviceWatcher` re-opens the connection when the chosen phone returns.
5. Persist — last-chosen device and toggles saved to `%LocalAppData%\Daeron\config.json`.

## Build

Requires:

- **Visual Studio Build Tools 2022** with these workloads/components:
  - `Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools`
  - `Microsoft.VisualStudio.Workload.UniversalBuildTools` (carries the AppxPackage MSBuild tasks WinUI 3 needs)
  - `Microsoft.VisualStudio.Component.VC.Tools.x86.x64` (MSVC, used by the WinUI 3 XAML markup compiler)
  - `Microsoft.VisualStudio.Component.Windows11SDK.22621`
- **Windows App Runtime 1.6** installed system-wide — the unpackaged app cannot launch without it. Install with `winget install Microsoft.WindowsAppRuntime.1.6`.
- Windows 10 build 19041 or later.

The .NET SDK alone is **not** enough — `dotnet build` will fail because the AppxPackage tasks live only with Visual Studio's MSBuild. Use the wrapper:

```
.\build.cmd            # Debug | x64 by default
.\build.cmd Release x64
```

The wrapper invokes VS Build Tools' MSBuild on the project. After build, run the produced exe:

```
.\src\Daeron\bin\x64\Debug\net8.0-windows10.0.22621.0\Daeron.exe
```
