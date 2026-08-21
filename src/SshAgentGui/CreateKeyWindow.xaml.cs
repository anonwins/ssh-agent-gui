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
        CommentBox.Text = $"{Environment.UserName}@{Environment.MachineName}";
        PathBox.Text = DefaultPathForType(ed25519: true);
        PathBox.TextChanged += (_, _) => _pathTouched = true;
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        if (_pathTouched && !IsCurrentDefaultPath())
            return;
        _pathTouched = false;
        PathBox.Text = DefaultPathForType(IsEd25519);
    }

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
        ErrorText.Text = "";
        var path = PathBox.Text.Trim();
        var comment = CommentBox.Text.Trim();
        var passphrase = PassphraseBox.Password;
        var confirm = ConfirmBox.Password;

        if (string.IsNullOrWhiteSpace(path))
        {
            ErrorText.Text = "Choose a file path.";
            return;
        }

        if (passphrase != confirm)
        {
            ErrorText.Text = "Passphrase and confirmation do not match.";
            return;
        }

        if (File.Exists(path) || File.Exists(path + ".pub"))
        {
            ErrorText.Text = "That key file already exists. Choose another name (this app will not overwrite).";
            return;
        }

        Request = new CreateKeyRequest(
            Type: IsEd25519 ? "ed25519" : "rsa",
            Path: path,
            Comment: string.IsNullOrWhiteSpace(comment) ? $"{Environment.UserName}@{Environment.MachineName}" : comment,
            Passphrase: passphrase,
            LoadIntoAgent: LoadBox.IsChecked == true);
        DialogResult = true;
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
        return Path.Combine(KeyFileDialog.DefaultSshDirectory, name);
    }
}
