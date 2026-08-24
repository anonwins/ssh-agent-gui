using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SshAgentGui.Ssh;

namespace SshAgentGui;

public partial class MainWindow : Window
{
    public static readonly RoutedCommand CreateKeyCommand = new();
    public static readonly RoutedCommand LoadKeyCommand = new();
    public static readonly RoutedCommand RefreshCommand = new();
    public static readonly RoutedCommand HideCommand = new();
    public static readonly RoutedCommand UnloadSelectedCommand = new();

    private readonly AgentSession _session;
    private bool _allowClose;
    private bool _exitPromptOpen;
    private DispatcherTimer? _copiedTimer;
    private System.Windows.Controls.Button? _copiedButton;
    private object? _copiedContent;

    internal MainWindow(AgentSession session)
    {
        _session = session;
        DataContext = session;
        InitializeComponent();
        LifetimeBox.ItemsSource = KeyLifetime.Presets;
        LifetimeBox.SelectedItem = KeyLifetime.FromSeconds(UiSettings.Current.LastLifetimeSeconds);
        CommandBindings.Add(new CommandBinding(CreateKeyCommand, (s, e) => OnCreateKeyClick(s, e), CanUseAgentExecute));
        CommandBindings.Add(new CommandBinding(LoadKeyCommand, (s, e) => OnAddKeyClick(s, e), CanUseAgentExecute));
        CommandBindings.Add(new CommandBinding(RefreshCommand, (s, e) => OnRefreshClick(s, e), CanRefreshExecute));
        CommandBindings.Add(new CommandBinding(HideCommand, (_, _) => HideToTray()));
        CommandBindings.Add(new CommandBinding(UnloadSelectedCommand, (s, e) => OnUnloadSelected(s, e), CanUseAgentExecute));
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

    private void OnLifetimeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (LifetimeBox.SelectedItem is KeyLifetime lifetime)
            UiSettings.Current.RememberLifetime(lifetime.Duration);
    }

    private void CanUseAgentExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _session.CanUseAgent;

    private void CanRefreshExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _session.CanRefresh;

    private async void OnRefreshClick(object sender, RoutedEventArgs e) =>
        await _session.RefreshAsync().ConfigureAwait(true);

    private async void OnStartAgentClick(object sender, RoutedEventArgs e) =>
        await _session.StartAgentAsync().ConfigureAwait(true);

    private async void OnAddKeyClick(object sender, RoutedEventArgs e)
    {
        var path = KeyFileDialog.OpenExisting("Load key");
        if (path is null)
            return;
        await _session.AddKeyAsync(path, SelectedLifetime()).ConfigureAwait(true);
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
        if (sender is not System.Windows.Controls.Button { DataContext: SshIdentity identity } button)
            return;

        var line = await _session.GetPublicKeyAsync(identity).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(line))
            return;
        System.Windows.Clipboard.SetText(line);
        FlashCopied(button);
    }

    private async void OnUnloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SshIdentity identity })
            return;

        await _session.UnloadAsync(identity).ConfigureAwait(true);
    }

    private async void OnUnloadSelected(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is not SshIdentity identity)
            return;

        await _session.UnloadAsync(identity).ConfigureAwait(true);
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SshIdentity identity })
            return;

        await _session.ReloadKeyAsync(identity).ConfigureAwait(true);
    }

    private async void OnRestampClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem { DataContext: KeyLifetime lifetime })
            return;
        if (sender is not ContextMenu { PlacementTarget: FrameworkElement { DataContext: SshIdentity identity } })
            return;

        await _session.RestampLifetimeAsync(identity, lifetime.Duration).ConfigureAwait(true);
    }

    private void OnFingerprintClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SshIdentity identity })
            return;

        System.Windows.Clipboard.SetText(identity.Fingerprint);
        _session.SetStatus("Fingerprint copied");
    }

    private void OnOptionalFeaturesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:optionalfeatures") { UseShellExecute = true });
        }
        catch
        {
            _session.SetStatus("Could not open Optional features.");
        }
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = HasDroppableKey(e) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDroppedFiles(e, out var files))
            return;

        var lifetime = SelectedLifetime();
        foreach (var path in files)
        {
            if (!IsDroppableKey(path))
                continue;
            await _session.AddKeyAsync(path, lifetime).ConfigureAwait(true);
        }
    }

    private TimeSpan? SelectedLifetime() =>
        (LifetimeBox.SelectedItem as KeyLifetime)?.Duration;

    private void FlashCopied(System.Windows.Controls.Button button)
    {
        if (_copiedTimer is not null && _copiedButton is not null)
        {
            _copiedTimer.Stop();
            _copiedButton.Content = _copiedContent;
        }

        _copiedButton = button;
        _copiedContent = button.Content;
        button.Content = new TextBlock
        {
            Text = "Copied",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer?.Stop();
            if (_copiedButton is not null)
                _copiedButton.Content = _copiedContent;
            _copiedTimer = null;
            _copiedButton = null;
            _copiedContent = null;
        };
        _copiedTimer.Start();
    }

    private static bool HasDroppableKey(System.Windows.DragEventArgs e)
    {
        if (!TryGetDroppedFiles(e, out var files))
            return false;
        return files.Any(IsDroppableKey);
    }

    private static bool TryGetDroppedFiles(System.Windows.DragEventArgs e, out string[] files)
    {
        files = [];
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            return false;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] dropped)
            return false;
        files = dropped;
        return true;
    }

    private static bool IsDroppableKey(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && !path.EndsWith(".pub", StringComparison.OrdinalIgnoreCase);
}
