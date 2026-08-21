using System.Windows;

namespace SshAgentGui;

public partial class PassphraseWindow : Window
{
    public string Passphrase { get; private set; } = "";

    public PassphraseWindow(string prompt)
    {
        InitializeComponent();
        PromptText.Text = string.IsNullOrWhiteSpace(prompt)
            ? "Enter the passphrase for this key."
            : prompt;
        Loaded += (_, _) => PassBox.Focus();
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => TitleBarDarkMode.Apply(this);

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        Passphrase = PassBox.Password;
        DialogResult = true;
    }
}
