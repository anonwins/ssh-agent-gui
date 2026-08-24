using System.Windows;
using Microsoft.Win32;
using SshAgentGui.Ssh;

namespace SshAgentGui;

public partial class App : System.Windows.Application
{
    private SingleInstance? _single;
    private AgentSession? _session;
    private MainWindow? _main;
    private TrayController? _tray;
    private PageantBridge? _pageant;
    private bool _watchingAccent;

    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        WindowsAccent.Apply(Resources);

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

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _watchingAccent = true;
        UiSettings.Load();
        _session = new AgentSession();
        _main = new MainWindow(_session);
        _tray = new TrayController(
            _session,
            () => _main.RestoreFromTray(),
            () => _main.RequestExitAsync(),
            () => _session.UnloadAllAsync());
        _single.ShowRequested += () => Dispatcher.BeginInvoke(() => _main.RestoreFromTray());
        _main.Show();
        StartPageantBridge();
        _ = _session.RefreshAsync();
    }

    private void StartPageantBridge()
    {
        if (_session is null || _main is null)
            return;
        if (PageantBridge.IsTaken())
        {
            _session.SetPageantStatus(PageantStatusText.Taken);
            return;
        }

        _pageant = PageantBridge.TryStart(
            new OpenSshAgentPipe(),
            (blob, caller) => SignConfirmWindow.Ask(_session, blob, caller),
            Dispatcher);
        _session.SetPageantStatus(_pageant is null ? PageantStatusText.Off : PageantStatusText.On);
    }

    private void App_OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _main?.AllowCloseForShutdown();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.Color or UserPreferenceCategory.General))
            return;
        Dispatcher.BeginInvoke(() => WindowsAccent.Apply(Resources));
    }

    private void App_OnExit(object sender, ExitEventArgs e)
    {
        if (_watchingAccent)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _watchingAccent = false;
        }

        _pageant?.Dispose();
        _pageant = null;
        _session?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
    }
}
