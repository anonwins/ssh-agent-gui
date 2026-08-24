using System.Windows;
using System.Windows.Controls;

namespace SshAgentGui;

public partial class CreateKeyWindow : Window
{
    private bool _pathTouched;

    internal CreateKeyRequest? Request { get; private set; }

    public CreateKeyWindow()
    {
        InitializeComponent();
        LifetimeBox.ItemsSource = KeyLifetime.Presets;
        LifetimeBox.SelectedItem = KeyLifetime.FromSeconds(UiSettings.Current.LastLifetimeSeconds);
        CommentBox.Text = $"{Environment.UserName}@{Environment.MachineName}";
        PathBox.Text = DefaultPathForType(ed25519: true);
        PathBox.TextChanged += (_, _) => _pathTouched = true;
        UpdatePassphraseHint();
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => TitleBarDarkMode.Apply(this);

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (_pathTouched && !IsCurrentDefaultPath())
            return;
        _pathTouched = false;
        PathBox.Text = DefaultPathForType(IsEd25519);
    }

    private void OnLifetimeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (LifetimeBox.SelectedItem is KeyLifetime lifetime)
            UiSettings.Current.RememberLifetime(lifetime.Duration);
    }

    private void OnPassphraseChanged(object sender, RoutedEventArgs e) => UpdatePassphraseHint();

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var picked = KeyFileDialog.BrowseNew("New key file", PathBox.Text);
        if (picked is null)
            return;
        PathBox.Text = picked;
        _pathTouched = true;
    }

    private void OnCreateClick(object sender, RoutedEventArgs e)
    {
        SetError("", danger: true);
        var path = PathBox.Text.Trim();
        var comment = CommentBox.Text.Trim();
        var passphrase = PassphraseBox.Password;
        var confirm = ConfirmBox.Password;

        if (string.IsNullOrWhiteSpace(path))
        {
            SetError("Choose a file path.", danger: true);
            return;
        }

        if (passphrase != confirm)
        {
            SetError("Passphrase and confirmation do not match.", danger: true);
            return;
        }

        if (File.Exists(path) || File.Exists(path + ".pub"))
        {
            SetError("That key file already exists. Choose another name (this app will not overwrite).", danger: true);
            return;
        }

        var lifetime = LifetimeBox.SelectedItem as KeyLifetime;
        Request = new CreateKeyRequest(
            Type: IsEd25519 ? "ed25519" : "rsa",
            Path: path,
            Comment: string.IsNullOrWhiteSpace(comment) ? $"{Environment.UserName}@{Environment.MachineName}" : comment,
            Passphrase: passphrase,
            LoadIntoAgent: LoadBox.IsChecked == true,
            Lifetime: LoadBox.IsChecked == true ? lifetime?.Duration : null);
        DialogResult = true;
    }

    private void UpdatePassphraseHint()
    {
        var passphrase = PassphraseBox.Password;
        var confirm = ConfirmBox.Password;
        if (string.IsNullOrEmpty(passphrase) && string.IsNullOrEmpty(confirm))
        {
            SetError("Key will not be encrypted.", danger: false);
            return;
        }

        if (passphrase != confirm)
        {
            SetError("Passphrase and confirmation do not match.", danger: true);
            return;
        }

        SetError("", danger: true);
    }

    private void SetError(string text, bool danger)
    {
        ErrorText.Text = text;
        ErrorText.Foreground = (System.Windows.Media.Brush)FindResource(danger ? "DangerBrush" : "MutedBrush");
    }

    private bool IsEd25519 => TypeBox.SelectedIndex == 0;

    private bool IsCurrentDefaultPath()
    {
        var current = PathBox.Text.Trim();
        return string.Equals(current, DefaultPathForType(true), StringComparison.OrdinalIgnoreCase)
               || string.Equals(current, DefaultPathForType(false), StringComparison.OrdinalIgnoreCase);
    }

    private static string DefaultPathForType(bool ed25519)
    {
        var name = ed25519 ? "id_ed25519" : "id_rsa";
        var dir = UiSettings.Current.ExistingSaveDir() ?? KeyFileDialog.DefaultSshDirectory;
        return Path.Combine(dir, name);
    }
}
