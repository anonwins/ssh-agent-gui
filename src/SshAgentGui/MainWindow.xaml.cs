using System.ComponentModel;
using System.Windows;
using SshAgentGui.Ssh;

namespace SshAgentGui;

public partial class MainWindow : Window
{
    private readonly AgentSession _session;
    private bool _allowClose;

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
        WindowState = WindowState.Normal;
        Hide();
        ShowInTaskbar = false;
    }

    public void AllowCloseForShutdown() => _allowClose = true;

    public async Task RequestExitAsync()
    {
        if (_allowClose)
            return;

        var dialog = new ExitConfirmWindow { Owner = IsVisible ? this : null };
        if (dialog.ShowDialog() != true)
            return;

        if (dialog.ClearKeys)
        {
            var ok = await _session.UnloadAllAsync().ConfigureAwait(true);
            if (!ok)
            {
                MessageBox.Show(
                    this,
                    _session.StatusText,
                    "SSH Agent",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void OnStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            HideToTray();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        HideToTray();
    }

    private async void OnExitClick(object sender, RoutedEventArgs e) =>
        await RequestExitAsync().ConfigureAwait(true);

    private async void OnRefreshClick(object sender, RoutedEventArgs e) =>
        await _session.RefreshAsync().ConfigureAwait(true);

    private async void OnAddKeyClick(object sender, RoutedEventArgs e)
    {
        var path = KeyFileDialog.OpenExisting("Add key");
        if (path is null)
            return;
        await _session.AddKeyAsync(path).ConfigureAwait(true);
    }

    private async void OnCreateKeyClick(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateKeyWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Request is null)
            return;
        await _session.CreateKeyAsync(dialog.Request).ConfigureAwait(true);
    }

    private async void OnUnloadAllClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Unload all keys from the agent? Tracked keys stay in the list as disabled.",
            "SSH Agent",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;
        await _session.UnloadAllAsync().ConfigureAwait(true);
    }

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SshIdentity identity })
            return;

        if (identity.IsLoaded)
        {
            var path = ResolvePath(identity, "Select the key file to disable");
            if (path is null)
                return;
            await _session.DisableAsync(identity, path).ConfigureAwait(true);
        }
        else
        {
            var path = ResolvePath(identity, "Select the key file to enable");
            if (path is null)
                return;
            await _session.EnableAsync(identity, path).ConfigureAwait(true);
        }
    }

    private async void OnUnloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SshIdentity identity })
            return;

        string? path = _session.ResolveExistingPath(identity);
        if (identity.IsLoaded && path is null)
        {
            path = KeyFileDialog.OpenExisting("Select the key file to unload");
            if (path is null)
                return;
        }

        await _session.UnloadAsync(identity, path).ConfigureAwait(true);
    }

    private string? ResolvePath(SshIdentity identity, string title)
    {
        var path = _session.ResolveExistingPath(identity);
        return path ?? KeyFileDialog.OpenExisting(title);
    }
}
