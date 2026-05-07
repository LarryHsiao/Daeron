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
    private DeviceWatcher? watcher;
    private AudioPlaybackConnection? activeConnection;
    private string activeDeviceName = "device";
    private bool watcherStarted;
    private bool autoReconnect;
    private bool togglesInitialized;
    private bool autoConnectAttempted;

    public ConfigWindow()
    {
        InitializeComponent();
        Title = "Daeron";
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

    private async void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (watcherStarted) return;
        watcherStarted = true;
        StartDeviceWatcher();
        await Task.CompletedTask;
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
            StatusText.Text = $"Watcher start failed: {ex.Message}";
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
        });
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        dispatcher.TryEnqueue(() =>
        {
            var existing = devices.FirstOrDefault(d => d.Id == update.Id);
            existing?.Update(update);
        });
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
            if (activeConnection == null) StatusText.Text = "No paired audio sources.";
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
                    StatusText.Text = "Idle";
                }
            }
        }
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
                    StatusText.Text = $"Start with Windows: {result}";
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
            StatusText.Text = $"StartupTask failed: {ex.Message}";
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

    private async Task ConnectAsync(DeviceInformation device)
    {
        Log($"ConnectAsync: device={device.Name} id={device.Id}");
        ConnectButton.IsEnabled = false;
        StatusText.Text = $"Opening {device.Name}…";

        AudioPlaybackConnection? conn = null;
        try
        {
            conn = AudioPlaybackConnection.TryCreateFromId(device.Id);
            if (conn == null)
            {
                StatusText.Text = "Could not create connection for this device.";
                ConnectButton.IsEnabled = true;
                return;
            }

            conn.StateChanged += OnConnectionStateChanged;
            await conn.StartAsync();
            var openResult = await conn.OpenAsync();
            Log($"OpenAsync returned status={openResult.Status}; state={conn.State}");

            if (openResult.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                StatusText.Text = $"Open failed: {openResult.Status}";
                conn.StateChanged -= OnConnectionStateChanged;
                conn.Dispose();
                ConnectButton.IsEnabled = true;
                return;
            }

            activeConnection = conn;
            activeDeviceName = device.Name;
            DeviceList.IsEnabled = false;
            ConnectButton.Content = "Disconnect";
            ConnectButton.IsEnabled = true;
            UpdateStatusFromState(conn.State, activeDeviceName);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Connect failed: {ex.Message}";
            if (conn != null)
            {
                conn.StateChanged -= OnConnectionStateChanged;
                conn.Dispose();
            }
            ConnectButton.IsEnabled = true;
        }
    }

    private void Disconnect()
    {
        DisposeConnection();
        ConnectButton.Content = "Connect";
        DeviceList.IsEnabled = devices.Count > 0;
        ConnectButton.IsEnabled = DeviceList.SelectedItem is DeviceInformation;
        StatusText.Text = "Idle";
    }

    private void DisposeConnection()
    {
        if (activeConnection == null) return;
        activeConnection.StateChanged -= OnConnectionStateChanged;
        activeConnection.Dispose();
        activeConnection = null;
    }

    private void OnConnectionStateChanged(AudioPlaybackConnection sender, object args)
    {
        var state = sender.State;
        Log($"StateChanged: state={state}; autoReconnect={autoReconnect}");
        dispatcher.TryEnqueue(() =>
        {
            if (state == AudioPlaybackConnectionState.Closed && !autoReconnect)
            {
                Log("Closed with autoReconnect off — disconnecting fully");
                Disconnect();
                return;
            }
            UpdateStatusFromState(state, activeDeviceName);
        });
    }

    private void UpdateStatusFromState(AudioPlaybackConnectionState state, string deviceName)
    {
        StatusText.Text = state switch
        {
            AudioPlaybackConnectionState.Closed => autoReconnect
                ? $"Waiting for {deviceName} to return…"
                : $"Closed: {deviceName}",
            AudioPlaybackConnectionState.Opened => $"Streaming from {deviceName}",
            _ => $"State: {state}",
        };
    }
}
