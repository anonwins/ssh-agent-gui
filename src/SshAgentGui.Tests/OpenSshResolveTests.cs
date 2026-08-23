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
}
