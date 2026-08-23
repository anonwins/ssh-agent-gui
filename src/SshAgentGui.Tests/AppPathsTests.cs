using SshAgentGui;

namespace SshAgentGui.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void ValidateGuiExecutable_rejects_dotnet_and_unqualified()
    {
        Assert.Null(AppPaths.ValidateGuiExecutable("dotnet.exe"));
        Assert.Null(AppPaths.ValidateGuiExecutable(@"C:\missing\SshAgentGui.exe"));
        Assert.Null(AppPaths.ValidateGuiExecutable(@"C:foo.exe"));
    }

    [Fact]
    public void ValidateGuiExecutable_rejects_existing_relative_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-rel-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var previous = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(dir);
            File.WriteAllBytes("SshAgentGui.exe", [0]);
            Assert.True(File.Exists("SshAgentGui.exe"));
            Assert.False(Path.IsPathFullyQualified("SshAgentGui.exe"));
            Assert.Null(AppPaths.ValidateGuiExecutable("SshAgentGui.exe"));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ValidateGuiExecutable_accepts_existing_fully_qualified_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            var resolved = AppPaths.ValidateGuiExecutable(path);
            Assert.Equal(Path.GetFullPath(path), resolved);
            Assert.True(Path.IsPathFullyQualified(resolved!));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryProtectDirectory_protects_a_temp_dir_not_appdata()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-acl-" + Guid.NewGuid().ToString("n"));
        var file = Path.Combine(dir, "keys.json");
        try
        {
            Assert.True(AppPaths.TryProtectDirectory(dir, file));
            Assert.True(Directory.Exists(dir));
            File.WriteAllText(file, "{}");
            Assert.True(AppPaths.TryProtectDirectory(dir, file));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
