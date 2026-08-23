using System.ComponentModel;
using System.Windows;
using SshAgentGui.Ssh;

namespace SshAgentGui;

public partial class MainWindow : Window
{
    private readonly AgentSession _session;
    private bool _allowClose;
    private bool _exitPromptOpen;

    internal MainWindow(AgentSession session)
    {
        _session = session;
        DataContext = session;
        InitializeComponent();
    }

    public void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        _ = _session.RefreshAsync();
    }

    public void HideToTray()
    {
        SaveUi();
        WindowState = WindowState.Normal;
        Hide();
        ShowInTaskbar = false;
    }

    public void AllowCloseForShutdown() => _allowClose = true;

    public async Task RequestExitAsync()
    {
        if (_allowClose || _exitPromptOpen)
            return;

        if (!_session.IsBusy && _session.LoadedCount == 0)
        {
            ShutdownApp();
            return;
        }

        _exitPromptOpen = true;
        try
        {
            var dialog = new ExitConfirmWindow { Owner = IsVisible ? this : null };
            if (dialog.ShowDialog() != true)
                return;

            if (dialog.UnloadKeys)
            {
                var ok = await _session.UnloadAllAsync().ConfigureAwait(true);
                if (!ok)
                {
                    MessageBox.Show(
                        this,
                        _session.StatusText,
                        "SSH Agent GUI",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            ShutdownApp();
        }
        finally
        {
            _exitPromptOpen = false;
        }
    }

    private void ShutdownApp()
    {
        SaveUi();
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void SaveUi()
    {
        UiSettings.Current.Capture(this);
        UiSettings.Current.Save();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        UiSettings.Current.Apply(this);
        TitleBarDarkMode.Apply(this);
    }

    private void OnStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            SaveUi();
            return;
        }

        e.Cancel = true;
        _ = RequestExitAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) =>
        await _session.RefreshAsync().ConfigureAwait(true);

    private async void OnStartAgentClick(object sender, RoutedEventArgs e) =>
        await _session.StartAgentAsync().ConfigureAwait(true);

    private async void OnAddKeyClick(object sender, RoutedEventArgs e)
    {
        var path = KeyFileDialog.OpenExisting("Load key");
        if (path is null)
            return;
        await _session.AddKeyAsync(path).ConfigureAwait(true);
    }

    private async void OnCreateKeyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateKeyWindow { Owner = IsVisible ? this : null };
        if (dialog.ShowDialog() != true || dialog.Request is null)
            return;
        await _session.CreateKeyAsync(dialog.Request).ConfigureAwait(true);
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SshIdentity identity })
            return;

        var line = await _session.GetPublicKeyAsync(identity).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(line))
            return;
        System.Windows.Clipboard.SetText(line);
    }

    private async void OnUnloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SshIdentity identity })
            return;

        await _session.UnloadAsync(identity).ConfigureAwait(true);
    }
}
