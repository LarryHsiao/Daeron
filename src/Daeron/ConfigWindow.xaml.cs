using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.Devices.Enumeration;
using Windows.Graphics;
using Windows.Media.Audio;

namespace Daeron;

public sealed partial class ConfigWindow : Window
{
    private const string StartupTaskId = "DaeronStart";

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Daeron",
        "startup.log");

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly DispatcherQueue dispatcher;
    private readonly ObservableCollection<DeviceInformation> devices = new();
    private readonly Config config;
    private const int ReconnectDelayMs = 1500;

    private DeviceWatcher? watcher;
    private AudioPlaybackConnection? activeConnection;
    private string activeDeviceName = "device";
    private string? activeDeviceId;
    private string? pendingReconnectId;
    private bool watcherStarted;
    private bool autoReconnect;
    private bool togglesInitialized;
    private bool autoConnectAttempted;
    private bool reconnectScheduled;
    private bool connecting;
    private bool needsPostConnectCycle;

    public ConfigWindow()
    {
        InitializeComponent();
        Title = "Daeron";
        try
        {
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico"));
        }
        catch (Exception ex)
        {
            Log($"SetIcon failed: {ex.Message}");
        }
        ResizeForDpi(logicalWidth: 440, logicalHeight: 440);
        dispatcher = DispatcherQueue.GetForCurrentThread();
        DeviceList.ItemsSource = devices;

        config = ConfigStore.Load();
        ApplyConfigToToggles();
        autoReconnect = config.AutoReconnect;

        Activated += OnFirstActivated;
        Closed += OnClosed;
    }

    private void ResizeForDpi(int logicalWidth, int logicalHeight)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var dpi = GetDpiForWindow(hwnd);
            var scale = dpi == 0 ? 1.0 : dpi / 96.0;
            AppWindow.Resize(new SizeInt32(
                (int)Math.Round(logicalWidth * scale),
                (int)Math.Round(logicalHeight * scale)));
        }
        catch
        {
            AppWindow.Resize(new SizeInt32(logicalWidth, logicalHeight));
        }
    }

    private async void ApplyConfigToToggles()
    {
        AutoReconnectToggle.IsChecked = config.AutoReconnect;

        // Reflect actual StartupTask state, not just the saved bool — Windows
        // can disable the task by user/policy independently of the app.
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            StartWithWindowsToggle.IsChecked = task.State == StartupTaskState.Enabled;
        }
        catch (Exception ex)
        {
            Log($"StartupTask.GetAsync failed: {ex.Message}");
            StartWithWindowsToggle.IsChecked = config.StartWithWindows;
        }

        togglesInitialized = true;
    }

    public void StartBackgroundOperations()
    {
        if (watcherStarted) return;
        watcherStarted = true;
        StartDeviceWatcher();
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        StartBackgroundOperations();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        StopDeviceWatcher();
        DisposeConnection();
    }

    private void StartDeviceWatcher()
    {
        try
        {
            var selector = AudioPlaybackConnection.GetDeviceSelector();
            Log($"Starting DeviceWatcher with selector: {selector}");
            watcher = DeviceInformation.CreateWatcher(selector);
            watcher.Added += OnDeviceAdded;
            watcher.Updated += OnDeviceUpdated;
            watcher.Removed += OnDeviceRemoved;
            watcher.EnumerationCompleted += OnEnumerationCompleted;
            watcher.Start();
        }
        catch (Exception ex)
        {
            SetStatus($"Watcher start failed: {ex.Message}");
        }
    }

    private void StopDeviceWatcher()
    {
        if (watcher == null) return;
        try
        {
            watcher.Added -= OnDeviceAdded;
            watcher.Updated -= OnDeviceUpdated;
            watcher.Removed -= OnDeviceRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            if (watcher.Status == DeviceWatcherStatus.Started ||
                watcher.Status == DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            Log($"StopDeviceWatcher: {ex.Message}");
        }
        watcher = null;
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        Log($"DeviceWatcher Added: {info.Name} (id={info.Id})");
        dispatcher.TryEnqueue(() =>
        {
            if (devices.All(d => d.Id != info.Id)) devices.Add(info);
            RefreshDropdownState();
            TryResumePendingReconnect(info.Id);
        });
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        dispatcher.TryEnqueue(() =>
        {
            var existing = devices.FirstOrDefault(d => d.Id == update.Id);
            existing?.Update(update);
            TryResumePendingReconnect(update.Id);
        });
    }

    private void TryResumePendingReconnect(string deviceId)
    {
        if (pendingReconnectId == null || pendingReconnectId != deviceId) return;
        if (activeConnection != null) return;
        if (connecting) return;
        var target = devices.FirstOrDefault(d => d.Id == deviceId);
        if (target == null) return;
        Log($"Pending auto-reconnect: device {target.Name} returned — reconnecting");
        _ = ConnectAsync(target);
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        Log($"DeviceWatcher Removed: id={update.Id}");
        dispatcher.TryEnqueue(() =>
        {
            var existing = devices.FirstOrDefault(d => d.Id == update.Id);
            if (existing != null) devices.Remove(existing);
            RefreshDropdownState();
        });
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        Log($"DeviceWatcher EnumerationCompleted: {devices.Count} device(s)");
        dispatcher.TryEnqueue(() =>
        {
            RefreshDropdownState();
            TryAutoConnect();
        });
    }

    private void RefreshDropdownState()
    {
        if (devices.Count == 0)
        {
            DeviceList.IsEnabled = false;
            DeviceList.PlaceholderText = "Pair a phone over Bluetooth, then it will appear here.";
            if (activeConnection == null) SetStatus("No paired audio sources.");
        }
        else
        {
            if (activeConnection == null)
            {
                DeviceList.IsEnabled = true;
                DeviceList.PlaceholderText = "Select a device";

                // Restore the saved device selection on first enumeration.
                if (DeviceList.SelectedItem == null && !string.IsNullOrEmpty(config.DeviceId))
                {
                    var saved = devices.FirstOrDefault(d => d.Id == config.DeviceId);
                    if (saved != null) DeviceList.SelectedItem = saved;
                }

                if (StatusText.Text == "No paired audio sources." || string.IsNullOrEmpty(StatusText.Text))
                {
                    SetStatus("Idle");
                }
            }
        }
        NotifyConnectionMenu();
    }

    private async void TryAutoConnect()
    {
        if (autoConnectAttempted) return;
        autoConnectAttempted = true;

        if (!config.AutoReconnect) return;
        if (string.IsNullOrEmpty(config.DeviceId)) return;
        if (activeConnection != null) return;

        var saved = devices.FirstOrDefault(d => d.Id == config.DeviceId);
        if (saved == null)
        {
            Log("TryAutoConnect: saved device not present in enumeration; skipping");
            return;
        }

        Log($"TryAutoConnect: connecting to {saved.Name}");
        await ConnectAsync(saved);
    }

    private void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (activeConnection != null) return;
        ConnectButton.IsEnabled = DeviceList.SelectedItem is DeviceInformation;

        if (DeviceList.SelectedItem is DeviceInformation device)
        {
            config.DeviceId = device.Id;
            ConfigStore.Save(config);
        }
        NotifyConnectionMenu();
    }

    private string? GetTargetDeviceName()
    {
        if (activeConnection != null) return activeDeviceName;
        if (DeviceList.SelectedItem is DeviceInformation selected) return selected.Name;
        if (!string.IsNullOrEmpty(config.DeviceId))
        {
            var saved = devices.FirstOrDefault(d => d.Id == config.DeviceId);
            if (saved != null) return saved.Name;
        }
        return null;
    }

    private void NotifyConnectionMenu()
    {
        bool connected = activeConnection != null;
        var name = GetTargetDeviceName();
        (Application.Current as App)?.UpdateConnectionMenu(connected, name);
    }

    private void AutoReconnectToggle_Changed(object sender, RoutedEventArgs e)
    {
        autoReconnect = AutoReconnectToggle.IsChecked == true;
        Log($"AutoReconnect toggled: {autoReconnect}");
        if (!togglesInitialized) return;
        config.AutoReconnect = autoReconnect;
        ConfigStore.Save(config);
    }

    private async void StartWithWindowsToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!togglesInitialized) return;

        var requested = StartWithWindowsToggle.IsChecked == true;
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId);
            if (requested)
            {
                var result = await task.RequestEnableAsync();
                Log($"StartupTask RequestEnableAsync: {result}");
                if (result != StartupTaskState.Enabled)
                {
                    // Windows declined (user clicked No, or policy). Revert.
                    StartWithWindowsToggle.Checked -= StartWithWindowsToggle_Changed;
                    StartWithWindowsToggle.Unchecked -= StartWithWindowsToggle_Changed;
                    StartWithWindowsToggle.IsChecked = false;
                    StartWithWindowsToggle.Checked += StartWithWindowsToggle_Changed;
                    StartWithWindowsToggle.Unchecked += StartWithWindowsToggle_Changed;
                    SetStatus($"Start with Windows: {result}");
                    requested = false;
                }
            }
            else
            {
                task.Disable();
                Log("StartupTask Disable");
            }
            config.StartWithWindows = requested;
            ConfigStore.Save(config);
        }
        catch (Exception ex)
        {
            SetStatus($"StartupTask failed: {ex.Message}");
            Log($"StartupTask failed: {ex}");
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (activeConnection != null)
        {
            Disconnect();
            return;
        }
        if (DeviceList.SelectedItem is not DeviceInformation device) return;
        await ConnectAsync(device);
    }

    public async void TriggerConnectOrDisconnect()
    {
        if (activeConnection != null)
        {
            Disconnect();
            return;
        }
        // Prefer the dropdown selection; fall back to the saved DeviceId.
        DeviceInformation? device = DeviceList.SelectedItem as DeviceInformation;
        if (device == null && !string.IsNullOrEmpty(config.DeviceId))
        {
            device = devices.FirstOrDefault(d => d.Id == config.DeviceId);
        }
        if (device == null)
        {
            Log("TriggerConnectOrDisconnect: no device available");
            SetStatus("No device to connect to. Open settings and pick one.");
            return;
        }
        await ConnectAsync(device);
    }

    private async Task ConnectAsync(DeviceInformation device)
    {
        if (connecting)
        {
            Log($"ConnectAsync: already in flight, skipping {device.Name}");
            return;
        }
        connecting = true;

        Log($"ConnectAsync: device={device.Name} id={device.Id}");
        dispatcher.TryEnqueue(() =>
        {
            ConnectButton.IsEnabled = false;
            SetStatus($"Opening {device.Name}…");
        });

        AudioPlaybackConnection? conn = null;
        try
        {
            conn = AudioPlaybackConnection.TryCreateFromId(device.Id);
            Log($"TryCreateFromId returned {(conn == null ? "null" : "connection")}");
            if (conn == null)
            {
                HandleConnectFailure(device, "Could not create connection for this device.");
                return;
            }

            conn.StateChanged += OnConnectionStateChanged;
            await conn.StartAsync();
            Log($"StartAsync complete; state={conn.State}");
            var openResult = await conn.OpenAsync();
            Log($"OpenAsync returned status={openResult.Status}; state={conn.State}");

            if (openResult.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                conn.StateChanged -= OnConnectionStateChanged;
                conn.Dispose();
                HandleConnectFailure(device, $"Open failed: {openResult.Status}");
                return;
            }

            var capturedConn = conn;
            var capturedName = device.Name;
            var capturedId = device.Id;
            var capturedDevice = device;
            dispatcher.TryEnqueue(() =>
            {
                activeConnection = capturedConn;
                activeDeviceName = capturedName;
                activeDeviceId = capturedId;
                pendingReconnectId = null;
                DeviceList.IsEnabled = false;
                ConnectButton.Content = "Disconnect";
                ConnectButton.IsEnabled = true;
                UpdateStatusFromState(capturedConn.State, activeDeviceName);
                NotifyConnectionMenu();
                Log("ConnectAsync: UI updated to connected state");

                // After-cycle: an auto-reconnect that "succeeds" sometimes
                // leaves the audio stack stale. Mimic a manual user re-click
                // (disconnect → connect) to flush it — but wait first so the
                // connection actually settles into the Opened state, the way a
                // real user's hand-timed cycle does.
                if (needsPostConnectCycle)
                {
                    needsPostConnectCycle = false;
                    Log("ConnectAsync: scheduling post-connect cycle in 3s");
                    var scheduledForId = capturedId;
                    var scheduledForDevice = capturedDevice;
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000);
                        dispatcher.TryEnqueue(() =>
                        {
                            if (activeConnection == null)
                            {
                                Log("Post-connect cycle aborted: no active connection");
                                return;
                            }
                            if (activeDeviceId != scheduledForId)
                            {
                                Log("Post-connect cycle aborted: device changed");
                                return;
                            }
                            Log("Running scheduled post-connect cycle");
                            StartReconnectCycle(scheduledForDevice, requestPostConnectCycle: false);
                        });
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Log($"ConnectAsync threw: {ex}");
            if (conn != null)
            {
                conn.StateChanged -= OnConnectionStateChanged;
                conn.Dispose();
            }
            HandleConnectFailure(device, $"Connect failed: {ex.Message}");
        }
        finally
        {
            connecting = false;
        }
    }

    private void Disconnect()
    {
        DisposeConnection();
        pendingReconnectId = null;
        needsPostConnectCycle = false;
        ConnectButton.Content = "Connect";
        DeviceList.IsEnabled = devices.Count > 0;
        ConnectButton.IsEnabled = DeviceList.SelectedItem is DeviceInformation;
        SetStatus("Idle");
        NotifyConnectionMenu();
    }

    private void DisposeConnection()
    {
        if (activeConnection == null) return;
        activeConnection.StateChanged -= OnConnectionStateChanged;
        activeConnection.Dispose();
        activeConnection = null;
        activeDeviceId = null;
    }

    private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
    {
        var state = sender.State;
        Log($"StateChanged: state={state}; autoReconnect={autoReconnect}");
        dispatcher.TryEnqueue(() =>
        {
            if (state != AudioPlaybackConnectionState.Closed)
            {
                UpdateStatusFromState(state, activeDeviceName);
                return;
            }

            if (!autoReconnect)
            {
                Log("Closed with autoReconnect off — disconnecting fully");
                Disconnect();
                return;
            }

            // The connection is one-shot: a closed session cannot be re-opened.
            // Dispose, park the device id, and let the scheduled retry attempt
            // a fresh connection once the Bluetooth stack has settled.
            var name = activeDeviceName;
            var pendingId = activeDeviceId;
            DisposeConnection();
            pendingReconnectId = pendingId;
            ConnectButton.Content = "Connect";
            ConnectButton.IsEnabled = false;
            DeviceList.IsEnabled = false;
            SetStatus($"Waiting for {name} to return…");
            NotifyConnectionMenu();
            ScheduleReconnect();
        });
    }

    private void ScheduleReconnect()
    {
        if (reconnectScheduled) return;
        reconnectScheduled = true;
        Log($"Reconnect scheduled in {ReconnectDelayMs}ms");
        _ = Task.Run(async () =>
        {
            await Task.Delay(ReconnectDelayMs);
            dispatcher.TryEnqueue(RunScheduledReconnect);
        });
    }

    private void RunScheduledReconnect()
    {
        reconnectScheduled = false;
        if (!autoReconnect) return;
        if (activeConnection != null) return;
        if (connecting) return;
        if (pendingReconnectId == null) return;
        var target = devices.FirstOrDefault(d => d.Id == pendingReconnectId);
        if (target == null)
        {
            Log("RunScheduledReconnect: device not enumerated yet, rescheduling");
            ScheduleReconnect();
            return;
        }
        Log($"RunScheduledReconnect: starting cycle for {target.Name}");
        StartReconnectCycle(target, requestPostConnectCycle: true);
    }

    private void StartReconnectCycle(DeviceInformation target, bool requestPostConnectCycle)
    {
        // Mimic the manual workaround: run the disconnect routine first to
        // flush any stale state, give the audio stack a brief settle, then
        // open a fresh connection.
        var parkedId = target.Id;
        Disconnect();
        pendingReconnectId = parkedId;
        if (requestPostConnectCycle) needsPostConnectCycle = true;
        SetStatus($"Reconnecting to {target.Name}…");
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            dispatcher.TryEnqueue(() =>
            {
                if (pendingReconnectId != parkedId) return;
                if (activeConnection != null || connecting) return;
                _ = ConnectAsync(target);
            });
        });
    }

    private void HandleConnectFailure(DeviceInformation device, string message)
    {
        Log($"HandleConnectFailure: {message}; autoReconnect={autoReconnect}; pending={pendingReconnectId}");
        dispatcher.TryEnqueue(() =>
        {
            if (autoReconnect && pendingReconnectId != null)
            {
                SetStatus($"Waiting for {device.Name} to return…");
                ConnectButton.IsEnabled = false;
                ScheduleReconnect();
                return;
            }
            SetStatus(message);
            ConnectButton.IsEnabled = true;
        });
    }

    private void UpdateStatusFromState(AudioPlaybackConnectionState state, string deviceName)
    {
        var text = state switch
        {
            AudioPlaybackConnectionState.Closed => autoReconnect
                ? $"Waiting for {deviceName} to return…"
                : $"Closed: {deviceName}",
            AudioPlaybackConnectionState.Opened => $"Streaming from {deviceName}",
            _ => $"State: {state}",
        };
        SetStatus(text);
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        (Application.Current as App)?.UpdateTrayStatus(text);
    }
}
