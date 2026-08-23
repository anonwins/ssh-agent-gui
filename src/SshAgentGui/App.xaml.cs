using System.Windows;

namespace SshAgentGui;

public partial class App : System.Windows.Application
{
    private SingleInstance? _single;
    private AgentSession? _session;
    private MainWindow? _main;
    private TrayController? _tray;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        if (AskPassMode.IsLaunch(e.Args))
        {
            Shutdown(AskPassMode.Run(e.Args));
            return;
        }

        if (StartAgentServiceMode.IsLaunch(e.Args))
        {
            Shutdown(StartAgentServiceMode.Run());
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        if (!SingleInstance.TryStart(out var single) || single is null)
        {
            Shutdown();
            return;
        }

        _single = single;
        if (!AppPaths.TryEnsureDirectory())
        {
            MessageBox.Show(
                "Could not protect application data. The app will not start.",
                "SSH Agent GUI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        UiSettings.Load();
        _session = new AgentSession();
        _main = new MainWindow(_session);
        _tray = new TrayController(_session, () => _main.RestoreFromTray(), () => _ = _main.RequestExitAsync());
        _single.ShowRequested += () => Dispatcher.BeginInvoke(() => _main.RestoreFromTray());
        _main.Show();
        _ = _session.RefreshAsync();
    }

    private void App_OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _main?.AllowCloseForShutdown();
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        _session?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
    }
}
