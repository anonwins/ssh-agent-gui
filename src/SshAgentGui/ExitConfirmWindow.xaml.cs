using System.Windows;

namespace SshAgentGui;

public partial class ExitConfirmWindow : Window
{
    public bool ClearKeys { get; private set; }

    public ExitConfirmWindow()
    {
        InitializeComponent();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ClearKeys = true;
        DialogResult = true;
    }

    private void OnLeaveClick(object sender, RoutedEventArgs e)
    {
        ClearKeys = false;
        DialogResult = true;
    }
}
