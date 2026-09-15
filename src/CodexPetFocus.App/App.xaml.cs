using System.Windows;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexPetFocus.App;

public partial class App : Application
{
    private SingleInstanceCoordinator? instance;
    private MainWindow? mainWindow;
    private Forms.NotifyIcon? trayIcon;
    private Forms.ContextMenuStrip? trayMenu;
    private bool systemEventsSubscribed;
    private bool shuttingDown;

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var options = CommandLineOptions.Parse(args.Args);
            if (options.Diagnose)
            {
                DiagnosticWriter.Write(options.OutputPath!);
                Shutdown(0);
                return;
            }

            instance = new SingleInstanceCoordinator(options.DataDirectory);
            if (!instance.IsFirstInstance)
            {
                instance.SignalFirstInstance();
                Shutdown(0);
                return;
            }

            mainWindow = new MainWindow(options.DataDirectory, options.HasCustomDataDirectory, options.SmokeTest);
            MainWindow = mainWindow;
            instance.ActivationRequested += OnActivationRequested;
            CreateTrayIcon();
            SubscribeSystemEvents();
            mainWindow.ShowAndActivate();

            if (options.SmokeTest)
            {
                var smokeTimer = new System.Windows.Threading.DispatcherTimer(
                    TimeSpan.FromSeconds(12),
                    System.Windows.Threading.DispatcherPriority.Background,
                    (_, _) => RequestExit(),
                    Dispatcher);
                smokeTimer.Start();
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(
                $"Codex Pet Focus 无法启动。\n\n{error.GetType().Name}：{error.Message}\n\n任务数据不会被覆盖。",
                "Codex Pet Focus",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void CreateTrayIcon()
    {
        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("打开", null, (_, _) => Dispatcher.BeginInvoke(ShowMainWindow));
        trayMenu.Items.Add("暂停", null, (_, _) => Dispatcher.BeginInvoke(PauseFromTray));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => Dispatcher.BeginInvoke(RequestExit));

        trayIcon = new Forms.NotifyIcon
        {
            Text = "Codex Pet Focus",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
    }

    private void SubscribeSystemEvents()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        systemEventsSubscribed = true;
    }

    private void UnsubscribeSystemEvents()
    {
        if (!systemEventsSubscribed)
            return;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        systemEventsSubscribed = false;
    }

    private void OnActivationRequested(object? sender, EventArgs args) =>
        Dispatcher.BeginInvoke(ShowMainWindow);

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs args)
    {
        if (args.Reason is not (SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff))
            return;
        if (!shuttingDown)
            mainWindow?.RequestSystemPause(args.Reason == SessionSwitchReason.SessionLock ? "锁屏" : "注销");
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs args)
    {
        if (args.Mode == PowerModes.Suspend && !shuttingDown)
            mainWindow?.RequestSystemPause("睡眠");
    }

    private void PauseFromTray()
    {
        if (shuttingDown || mainWindow is null)
            return;
        if (!mainWindow.PauseForSystem("从托盘暂停"))
            ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (!shuttingDown)
            mainWindow?.ShowAndActivate();
    }

    internal void RequestExit()
    {
        if (shuttingDown || mainWindow is null)
            return;
        if (!mainWindow.TryPrepareExit())
        {
            ShowMainWindow();
            MessageBox.Show(
                mainWindow,
                "保存失败，退出已取消。请先修复存储问题，以免丢失尚未写入的数据。",
                "Codex Pet Focus",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        shuttingDown = true;
        UnsubscribeSystemEvents();
        if (instance is not null)
            instance.ActivationRequested -= OnActivationRequested;
        if (trayIcon is not null)
            trayIcon.Visible = false;
        mainWindow.AllowClose = true;
        mainWindow.DisposeWindows();
        mainWindow.Close();
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs args)
    {
        UnsubscribeSystemEvents();
        if (instance is not null)
            instance.ActivationRequested -= OnActivationRequested;
        trayIcon?.Dispose();
        trayMenu?.Dispose();
        instance?.Dispose();
        base.OnExit(args);
    }
}
