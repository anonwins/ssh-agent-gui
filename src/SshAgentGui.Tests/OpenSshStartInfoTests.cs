using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class OpenSshStartInfoTests
{
    [Theory]
    [InlineData(@"C:\Users\me\.ssh\id ed25519")]
    [InlineData(@"C:\Users\me\.ssh\id""quoted""")]
    [InlineData(@"C:\Users\me\.ssh\ключ")]
    [InlineData(@"C:\Users\me\.ssh\id&|;<>^%")]
    [InlineData(@"\\server\share\id_ed25519")]
    public void ArgumentList_keeps_one_entry_and_does_not_use_shell(string path)
    {
        var exe = Path.Combine(Path.GetTempPath(), "ssh-add.exe");
        var psi = OpenSshProcess.CreateHiddenStartInfo(exe, [path]);
        Assert.False(psi.UseShellExecute);
        Assert.True(string.IsNullOrEmpty(psi.Arguments));
        Assert.Equal(path, Assert.Single(psi.ArgumentList));
        Assert.Equal(Path.GetDirectoryName(exe), psi.WorkingDirectory);
        Assert.True(psi.RedirectStandardInput);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
        Assert.True(psi.CreateNoWindow);
    }
}
