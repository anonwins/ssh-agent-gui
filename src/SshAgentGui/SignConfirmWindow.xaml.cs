using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SshAgentGui.Ssh;

namespace SshAgentGui;

public partial class SignConfirmWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly string? _path;
    private readonly string? _fingerprint;

    internal static bool Ask(AgentSession session, byte[] blob, PageantCallerInfo? caller)
    {
        var identity = session.FindByFingerprint(OpenSshFingerprint.Sha256(blob));
        var dialog = new SignConfirmWindow(identity, caller);
        return dialog.ShowDialog() == true;
    }

    internal SignConfirmWindow(SshIdentity? identity, PageantCallerInfo? caller = null)
    {
        InitializeComponent();

        var info = caller ?? new PageantCallerInfo();
        CallerName.Text = info.DisplayName;
        AutomationProperties.SetName(IconWell, info.DisplayName);
        if (info.WindowSubtitle is { } subtitle)
        {
            CallerSubtitle.Text = subtitle;
            CallerSubtitle.Visibility = Visibility.Visible;
        }

        if (TryLoadProcessIcon(info.ImagePath) is { } icon)
        {
            CallerIcon.Source = icon;
            CallerIcon.Visibility = Visibility.Visible;
            CallerGlyph.Visibility = Visibility.Collapsed;
        }

        var hasProcess = false;
        if (info.ProcessLine is { } process)
        {
            ProcessRow.Text = process;
            ProcessRow.Visibility = Visibility.Visible;
            hasProcess = true;
        }

        _path = info.ImagePath;
        if (!string.IsNullOrWhiteSpace(_path))
        {
            PathValue.Text = _path;
            PathValue.ToolTip = _path + Environment.NewLine + "Copy path";
            PathRow.Visibility = Visibility.Visible;
            if (hasProcess)
                PathRow.Margin = new Thickness(0, 8, 0, 0);
        }

        if (hasProcess || PathRow.Visibility == Visibility.Visible)
            DetailsStack.Visibility = Visibility.Visible;

        if (identity is null)
        {
            KeyText.Text = "Unknown key";
            _fingerprint = null;
        }
        else
        {
            KeyText.Text = identity.DisplayComment;
            if (!string.IsNullOrWhiteSpace(identity.KeyType))
            {
                KeyTypeText.Text = identity.KeyType;
                KeyTypePill.Visibility = Visibility.Visible;
            }

            _fingerprint = identity.Fingerprint;
            if (!string.IsNullOrWhiteSpace(_fingerprint))
            {
                FingerprintText.Text = _fingerprint;
                FingerprintRow.Visibility = Visibility.Visible;
            }
        }

        AutomationProperties.SetName(KeyWell, KeyText.Text);

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

    private void OnPathClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement clicked)
            CopyFeedback.TryCopy(_path, CopyFeedback.FindMark(clicked));
    }

    private void OnFingerprintClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement clicked)
            CopyFeedback.TryCopy(_fingerprint, CopyFeedback.FindMark(clicked));
    }

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

    private static ImageSource? TryLoadProcessIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

}
