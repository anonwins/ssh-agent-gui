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
}
