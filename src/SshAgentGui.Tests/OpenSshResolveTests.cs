using System.Diagnostics;
using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class OpenSshResolveTests
{
    [Fact]
    public void Default_resolution_is_not_git_on_path()
    {
        var add = OpenSshProcess.FindExe("ssh-add.exe");
        if (add is null)
            return;

        Assert.DoesNotContain("Git", add, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            add.Contains(@"\System32\OpenSSH\", StringComparison.OrdinalIgnoreCase)
            || add.Contains(@"\Program Files\OpenSSH\", StringComparison.OrdinalIgnoreCase),
            add);
        Assert.True(Path.IsPathRooted(add));
    }

    [Fact]
    public void Rejects_names_with_directory_separators()
    {
        Assert.Null(OpenSshProcess.ResolveOpenSshExecutable(@"foo\ssh-add.exe"));
        Assert.Null(OpenSshProcess.ResolveOpenSshExecutable("foo/ssh-add.exe"));
    }

    [Fact]
    public void Resolves_from_injected_roots_only()
    {
        var root = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-roots-" + Guid.NewGuid().ToString("n"));
        var other = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-roots-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(other);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "ssh-add.exe"), [0]);
            File.WriteAllBytes(Path.Combine(other, "ssh-keygen.exe"), [0]);

            var add = OpenSshProcess.ResolveOpenSshExecutable("ssh-add.exe", [root, other]);
            var keygen = OpenSshProcess.ResolveOpenSshExecutable("ssh-keygen.exe", [root, other]);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "ssh-add.exe")), add);
            Assert.Equal(Path.GetFullPath(Path.Combine(other, "ssh-keygen.exe")), keygen);
            Assert.Null(OpenSshProcess.ResolveOpenSshExecutable("ssh-add.exe", [other]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void Rejects_reparse_root()
    {
        var target = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-reparse-tgt-" + Guid.NewGuid().ToString("n"));
        var junction = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-reparse-junc-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(target);
        try
        {
            File.WriteAllBytes(Path.Combine(target, "ssh-add.exe"), [0]);
            if (!TryCreateJunction(junction, target))
                return;

            Assert.Null(OpenSshProcess.ResolveOpenSshExecutable("ssh-add.exe", [junction]));
        }
        finally
        {
            TryDeleteJunction(junction);
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
        }
    }

    private static bool TryCreateJunction(string junction, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(junction, target);
            return Directory.Exists(junction);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;
            if (!process.WaitForExit(5000))
                return false;
            return process.ExitCode == 0 && Directory.Exists(junction);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteJunction(string junction)
    {
        try
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction);
        }
        catch
        {
        }
    }
}
