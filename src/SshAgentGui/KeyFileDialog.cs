namespace SshAgentGui;

internal static class KeyFileDialog
{
    public static string DefaultSshDirectory
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            return dir;
        }
    }

    public static string? OpenExisting(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "SSH keys|id_*;*.pem;*.key|All files|*.*",
            CheckFileExists = true,
        };
        if (Directory.Exists(DefaultSshDirectory))
            dialog.InitialDirectory = DefaultSshDirectory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public static string? BrowseNew(string title, string currentPath)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = "All files|*.*",
            FileName = Path.GetFileName(currentPath),
            OverwritePrompt = false,
            AddExtension = false,
        };
        var dir = Path.GetDirectoryName(currentPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            dialog.InitialDirectory = dir;
        else if (Directory.Exists(DefaultSshDirectory))
            dialog.InitialDirectory = DefaultSshDirectory;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
