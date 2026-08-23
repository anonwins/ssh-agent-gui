using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class KeygenInvariantTests
{
    [Theory]
    [InlineData("ed25519_clear")]
    [InlineData("rsa_clear")]
    public void Unencrypted_generated_key_is_deleted(string name)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var dir = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-keygen-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        var pub = path + ".pub";
        try
        {
            File.Copy(source, path);
            File.Copy(source + ".pub", pub);
            Assert.False(PrivateKeyFile.TryConfirmEncrypted(path));

            var result = WindowsSshKeygen.EnsureCreatedKeyEncrypted(path);
            Assert.False(result.Ok);
            Assert.Equal(OpenSshText.NotEncrypted, result.Message);
            Assert.False(File.Exists(path));
            Assert.False(File.Exists(pub));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LooksEncrypted_does_not_confirm_unknown_file()
    {
        var missing = Path.Combine(Path.GetTempPath(), "no-such-key-" + Guid.NewGuid().ToString("n"));
        Assert.False(PrivateKeyFile.LooksEncrypted(missing));
        Assert.False(PrivateKeyFile.TryConfirmEncrypted(missing));
    }
}
