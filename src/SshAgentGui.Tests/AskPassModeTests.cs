using SshAgentGui;

namespace SshAgentGui.Tests;

public sealed class AskPassModeTests
{
    [Theory]
    [InlineData(new[] { "--askpass" }, null, null, true)]
    [InlineData(new[] { "--askpass", "Enter passphrase for key:" }, null, null, true)]
    [InlineData(new string[0], "1", "ssh-agent-gui-abc", true)]
    [InlineData(new[] { "Enter passphrase for C:\\Users\\me\\.ssh\\id_ed25519:" }, "1", null, true)]
    [InlineData(new string[0], "1", null, false)]
    [InlineData(new string[0], null, null, false)]
    [InlineData(new string[0], "0", null, false)]
    public void IsLaunch_table(string[] args, string? launchEnv, string? pipeName, bool expected) =>
        Assert.Equal(expected, AskPassMode.IsLaunch(args, launchEnv, pipeName));

    [Fact]
    public void SanitizePrompt_keeps_filename_only()
    {
        var prompt = AskPassMode.SanitizePrompt(@"Enter passphrase for C:\Users\me\.ssh\id_ed25519:");
        Assert.DoesNotContain(@"C:\Users", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id_ed25519", prompt, StringComparison.Ordinal);
    }
}
