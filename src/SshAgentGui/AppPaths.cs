using System.Security.AccessControl;
using System.Security.Principal;

namespace SshAgentGui;

internal static class AppPaths
{
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SshAgentGui");

    public static string KeysFile => Path.Combine(Directory, "keys.json");

    public static string UiFile => Path.Combine(Directory, "ui.json");

    public static string? Executable { get; } = TryResolveExecutable();

    public static void EnsureDirectory()
    {
        System.IO.Directory.CreateDirectory(Directory);
        try
        {
            ApplyDirectoryAcl(Directory);
            ApplyFileAcl(KeysFile);
            ApplyFileAcl(UiFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            // ACL is defense-in-depth; the app still runs if we cannot tighten the DACL.
        }
    }

    internal static string? TryResolveExecutable()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var resolved = ValidateGuiExecutable(processPath);
            if (resolved is not null)
                return resolved;

            if (IsDotNetHost(processPath))
                return ValidateGuiExecutable(Path.Combine(AppContext.BaseDirectory, "SshAgentGui.exe"));
        }

        return ValidateGuiExecutable(Path.Combine(AppContext.BaseDirectory, "SshAgentGui.exe"));
    }

    internal static string? ValidateGuiExecutable(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!Path.IsPathRooted(full) || !File.Exists(full) || IsDotNetHost(full))
            return null;

        return full;
    }

    private static bool IsDotNetHost(string path) =>
        Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    private static void ApplyDirectoryAcl(string path)
    {
        var user = WindowsIdentity.GetCurrent().User;
        if (user is null)
            return;

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        AddRule(security, user, FileSystemRights.Modify);
        AddRule(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl);
        AddRule(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl);
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void AddRule(DirectorySecurity security, IdentityReference id, FileSystemRights rights) =>
        security.AddAccessRule(new FileSystemAccessRule(
            id,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

    private static void ApplyFileAcl(string path)
    {
        if (!File.Exists(path))
            return;

        var user = WindowsIdentity.GetCurrent().User;
        if (user is null)
            return;

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.Modify, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
