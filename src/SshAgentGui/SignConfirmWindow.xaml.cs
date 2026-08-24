using System.Windows;
using System.Windows.Threading;
using SshAgentGui.Ssh;

namespace SshAgentGui;

public partial class SignConfirmWindow : Window
{
    private readonly DispatcherTimer _timer;

    internal static bool Ask(AgentSession session, byte[] blob, string? caller)
    {
        var identity = session.FindByFingerprint(OpenSshFingerprint.Sha256(blob));
        var dialog = new SignConfirmWindow(identity, caller);
        return dialog.ShowDialog() == true;
    }

    internal SignConfirmWindow(SshIdentity? identity, string? caller = null)
    {
        InitializeComponent();
        CallerText.Text = PageantCaller.PromptLine(caller);
        if (identity is null)
        {
            KeyText.Text = "Unknown key";
            FingerprintText.Text = "";
        }
        else
        {
            KeyText.Text = identity.DisplayComment;
            FingerprintText.Text = identity.Fingerprint;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            DialogResult = false;
        };
        Loaded += (_, _) =>
        {
            Activate();
            DenyButton.Focus();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => TitleBarDarkMode.Apply(this);

    private void OnAllowClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = true;
    }

    private void OnDenyClick(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = false;
    }
}
