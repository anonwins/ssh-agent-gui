namespace SshAgentGui;

internal static class KeyFileDialog
{
    public static string DefaultSshDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

    public static string? OpenExisting(string title)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = "SSH keys|id_*;*.pem;*.key|All files|*.*",
            CheckFileExists = true,
        };
        dialog.InitialDirectory = UiSettings.Current.ExistingOpenDir() ?? ExistingDefaultSsh();
        if (dialog.ShowDialog() != true)
            return null;
        UiSettings.Current.RememberOpen(dialog.FileName);
        return dialog.FileName;
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
        var fromPath = Path.GetDirectoryName(currentPath);
        dialog.InitialDirectory =
            !string.IsNullOrEmpty(fromPath) && Directory.Exists(fromPath)
                ? fromPath
                : UiSettings.Current.ExistingSaveDir() ?? ExistingDefaultSsh();
        if (dialog.ShowDialog() != true)
            return null;
        UiSettings.Current.RememberSave(dialog.FileName);
        return dialog.FileName;
    }

    private static string? ExistingDefaultSsh() =>
        Directory.Exists(DefaultSshDirectory) ? DefaultSshDirectory : null;
}
