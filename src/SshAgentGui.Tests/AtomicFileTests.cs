using SshAgentGui;

namespace SshAgentGui.Tests;

public sealed class AtomicFileTests
{
    [Fact]
    public void First_create_and_replace_leave_no_tmp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-atomic-" + Guid.NewGuid().ToString("n"));
        var file = Path.Combine(dir, "keys.json");
        try
        {
            AtomicFile.WriteAllText(file, "{\"a\":1}");
            Assert.True(File.Exists(file));
            Assert.False(File.Exists(file + ".tmp"));
            Assert.Equal("{\"a\":1}", File.ReadAllText(file));

            AtomicFile.WriteAllText(file, "{\"a\":2}");
            Assert.Equal("{\"a\":2}", File.ReadAllText(file));
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
