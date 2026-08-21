using System.Windows;

namespace SshAgentGui;

public partial class ExitConfirmWindow : Window
{
    public bool UnloadKeys { get; private set; }

    public ExitConfirmWindow()
    {
        InitializeComponent();
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => TitleBarDarkMode.Apply(this);

    private void OnUnloadClick(object sender, RoutedEventArgs e)
    {
        UnloadKeys = true;
        DialogResult = true;
    }

    private void OnLeaveClick(object sender, RoutedEventArgs e)
    {
        UnloadKeys = false;
        DialogResult = true;
    }
}
