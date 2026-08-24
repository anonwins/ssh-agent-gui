using SshAgentGui;
using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class TrackedKeyStoreTests
{
    [Fact]
    public void Remember_does_not_wipe_expiry()
    {
        var file = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-store-" + Guid.NewGuid().ToString("n") + ".json");
        try
        {
            var store = new TrackedKeyStore(file);
            store.Remember("fp", @"C:\keys\id_ed25519", "comment", "ED25519", 256);
            var expires = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            store.SetExpiry("fp", expires, TimeSpan.FromMinutes(30));
            store.Remember("fp", @"C:\keys\id_ed25519", "comment", "ED25519", 256, persist: false);

            Assert.Equal(expires, store.TryGet("fp")?.ExpiresAtUtc);
            Assert.Equal(1800, store.TryGet("fp")?.LifetimeSeconds);

            var reloaded = new TrackedKeyStore(file);
            reloaded.Load();
            Assert.Equal(expires, reloaded.TryGet("fp")?.ExpiresAtUtc);
            Assert.Equal(1800, reloaded.TryGet("fp")?.LifetimeSeconds);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        var file = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-missing-" + Guid.NewGuid().ToString("n") + ".json");
        var store = new TrackedKeyStore(file);
        store.Load();
        Assert.Empty(store.Items);
    }

    [Fact]
    public void Corrupt_json_loads_empty()
    {
        var file = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-corrupt-" + Guid.NewGuid().ToString("n") + ".json");
        try
        {
            File.WriteAllText(file, "{not-json");
            var store = new TrackedKeyStore(file);
            store.Load();
            Assert.Empty(store.Items);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    [Fact]
    public void Malicious_path_is_stored_as_data_only()
    {
        var file = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-path-" + Guid.NewGuid().ToString("n") + ".json");
        try
        {
            var store = new TrackedKeyStore(file);
            store.Remember("fp", @"..\..\Windows\System32\config\SAM", "comment", "ED25519", 256);
            Assert.Equal(@"..\..\Windows\System32\config\SAM", store.TryGet("fp")?.Path);
            Assert.False(File.Exists(file + ".tmp"));

            var reloaded = new TrackedKeyStore(file);
            reloaded.Load();
            Assert.Equal(@"..\..\Windows\System32\config\SAM", reloaded.TryGet("fp")?.Path);
        }
        finally
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }
}
