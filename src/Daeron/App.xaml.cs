using System;
using System.IO;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;

namespace Daeron;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Daeron",
        "startup.log");

    private TaskbarIcon? trayIcon;
    private ConfigWindow? configWindow;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log($"UnhandledException: {e.Message}\n{e.Exception}");
            e.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log($"AppDomain.UnhandledException: {e.ExceptionObject}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            bool atBoot = IsStartupTaskActivation();
            Log($"OnLaunched: atBoot={atBoot}");
            BuildTrayIcon();
            EnsureConfigWindow();
            configWindow!.StartBackgroundOperations();
            if (!atBoot)
            {
                configWindow.Activate();
            }
            Log("OnLaunched: complete");
        }
        catch (Exception ex)
        {
            Log($"OnLaunched threw: {ex}");
            throw;
        }
    }

    private static bool IsStartupTaskActivation()
    {
        try
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            return activation.Kind == ExtendedActivationKind.StartupTask;
        }
        catch (Exception ex)
        {
            Log($"IsStartupTaskActivation failed: {ex.Message}");
            return false;
        }
    }

    private void BuildTrayIcon()
    {
        var iconUri = new Uri("ms-appx:///Assets/tray.ico");

        var openItem = new MenuFlyoutItem
        {
            Text = "Open settings…",
            Command = new RelayCommand(() =>
            {
                Log("Tray menu: Open settings invoked");
                ShowConfigWindow();
            }),
        };

        var exitItem = new MenuFlyoutItem
        {
            Text = "Exit",
            Command = new RelayCommand(() =>
            {
                Log("Tray menu: Exit invoked");
                ExitApp();
            }),
        };

        var menu = new MenuFlyout();
        menu.Items.Add(openItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exitItem);

        trayIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(iconUri),
            ToolTipText = "Daeron",
            ContextFlyout = menu,
        };
        trayIcon.LeftClickCommand = new RelayCommand(() =>
        {
            Log("Tray icon: left-click");
            ShowConfigWindow();
        });
        trayIcon.ForceCreate();
    }

    private void EnsureConfigWindow()
    {
        if (configWindow != null) return;
        configWindow = new ConfigWindow();
        configWindow.AppWindow.Closing += OnConfigWindowClosing;
        configWindow.Closed += OnConfigWindowClosed;
    }

    private void ShowConfigWindow()
    {
        try
        {
            EnsureConfigWindow();
            configWindow!.StartBackgroundOperations();
            configWindow.AppWindow.Show();
            configWindow.Activate();
            Log("ShowConfigWindow: shown + activated");
        }
        catch (Exception ex)
        {
            Log($"ShowConfigWindow failed: {ex}");
            // Reset and try once more.
            configWindow = null;
            try
            {
                EnsureConfigWindow();
                configWindow!.StartBackgroundOperations();
                configWindow.Activate();
                Log("ShowConfigWindow: recovered with new window");
            }
            catch (Exception ex2)
            {
                Log($"ShowConfigWindow recovery failed: {ex2}");
            }
        }
    }

    private void OnConfigWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        Log("ConfigWindow: AppWindow.Closing fired — cancelling and hiding");
        args.Cancel = true;
        sender.Hide();
    }

    private void OnConfigWindowClosed(object sender, WindowEventArgs args)
    {
        Log($"ConfigWindow: Window.Closed fired (Handled was {args.Handled})");
        configWindow = null;
    }

    private void ExitApp()
    {
        Log("ExitApp: disposing tray and exiting");
        trayIcon?.Dispose();
        trayIcon = null;
        if (configWindow != null)
        {
            configWindow.AppWindow.Closing -= OnConfigWindowClosing;
            configWindow.Closed -= OnConfigWindowClosed;
            configWindow.Close();
            configWindow = null;
        }
        Exit();
    }

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
}

internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action action;
    public RelayCommand(Action action) => this.action = action;
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
