using SshAgentGui;

namespace SshAgentGui.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void ValidateGuiExecutable_rejects_dotnet_and_relative()
    {
        Assert.Null(AppPaths.ValidateGuiExecutable("dotnet.exe"));
        Assert.Null(AppPaths.ValidateGuiExecutable(@"C:\missing\SshAgentGui.exe"));
        var relative = Path.Combine("not-rooted-anyway");
        if (!Path.IsPathRooted(relative))
            Assert.Null(AppPaths.ValidateGuiExecutable(relative));
    }

    [Fact]
    public void ValidateGuiExecutable_accepts_existing_rooted_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            var resolved = AppPaths.ValidateGuiExecutable(path);
            Assert.Equal(Path.GetFullPath(path), resolved);
            Assert.True(Path.IsPathRooted(resolved));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
