using SshAgentGui;
using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class AutoUnloadTests
{
    [Fact]
    public void ExpiryText_formats_remaining_time()
    {
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.Equal("", SshIdentity.FormatExpiry(null, now));
        Assert.Equal("Expired", SshIdentity.FormatExpiry(now.AddMinutes(-1), now));
        Assert.Equal("2:30:00", SshIdentity.FormatExpiry(now.AddHours(2.5), now));
        Assert.Equal("0:25:00", SshIdentity.FormatExpiry(now.AddMinutes(25), now));
        Assert.Equal("0:00:30", SshIdentity.FormatExpiry(now.AddSeconds(30), now));
    }

    [Fact]
    public async Task Restamp_updates_expiry_without_ssh_add()
    {
        using var session = CreateSession(out var client, out var store);
        var identity = new SshIdentity("fp-re", "comment", "ED25519", 256)
        {
            Lifetime = TimeSpan.FromMinutes(30),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            LoadGeneration = 2,
        };
        session.Identities.Add(identity);
        store.Upsert(identity, @"C:\keys\id");

        await session.RestampLifetimeAsync(identity, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(1), session.Identities[0].Lifetime);
        Assert.Equal(3, session.Identities[0].LoadGeneration);
        Assert.True(session.Identities[0].ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50));
        Assert.Equal(3600, store.TryGet("fp-re")?.LifetimeSeconds);
        Assert.Equal(0, client.AddCalls);
        Assert.Equal(0, client.RemoveCalls);

        await session.RestampLifetimeAsync(identity, null);
        Assert.Null(session.Identities[0].Lifetime);
        Assert.Null(session.Identities[0].ExpiresAt);
        Assert.Null(store.TryGet("fp-re")?.LifetimeSeconds);
        Assert.Equal(0, client.AddCalls);
    }

    [Fact]
    public async Task Stale_generation_does_not_unload_readded_key()
    {
        using var session = CreateSession(out var client, out _);
        var identity = new SshIdentity("fp-gen", "comment", "ED25519", 256)
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            LoadGeneration = 8,
        };
        session.Identities.Add(identity);
        client.Loaded.Add(identity);

        var stale = new ExpirySnapshot(identity.Fingerprint, 7, DateTimeOffset.UtcNow.AddHours(-1));
        Assert.False(await session.TryExpireAsync(stale));
        Assert.Equal(0, client.RemoveCalls);
        Assert.Single(session.Identities);
        Assert.Equal(8, session.Identities[0].LoadGeneration);
    }

    [Fact]
    public async Task Failed_removal_keeps_the_row()
    {
        using var session = CreateSession(out var client, out _);
        var identity = new SshIdentity("fp-keep", "comment", "ED25519", 256)
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
            LoadGeneration = 1,
        };
        session.Identities.Add(identity);
        client.Loaded.Add(identity);

        var snapshot = new ExpirySnapshot(identity.Fingerprint, 1, identity.ExpiresAt!.Value);
        Assert.False(await session.TryExpireAsync(snapshot));
        Assert.Single(session.Identities);
        Assert.Equal("Could not unload the key from the agent.", session.StatusText);
    }

    [Fact]
    public async Task Failed_list_keeps_existing_rows()
    {
        using var session = CreateSession(out var client, out _);
        session.Identities.Add(new SshIdentity("fp-stay", "comment", "ED25519", 256));
        client.ListOverride = SshAgentResult<List<SshIdentity>>.Fail(OpenSshText.AddFailed);

        await session.RefreshAsync();

        Assert.Single(session.Identities);
        Assert.Equal(OpenSshText.AddFailed, session.StatusText);
    }

    [Fact]
    public async Task Successful_expire_drops_the_row()
    {
        if (OpenSshProcess.FindExe("ssh-keygen.exe") is null)
            return;

        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ed25519_clear");
        var dir = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-expire-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ed25519_clear");
        File.Copy(source, path);
        File.Copy(source + ".pub", path + ".pub");

        try
        {
            var printed = await new WindowsSshKeygen().FingerprintAsync(path);
            Assert.True(printed.Ok);
            Assert.NotNull(printed.Value);

            using var session = CreateSession(out var client, out var store);
            var identity = new SshIdentity(printed.Value!.Fingerprint, printed.Value.Comment, printed.Value.KeyType, printed.Value.Bits, path)
            {
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
                LoadGeneration = 1,
            };
            session.Identities.Add(identity);
            client.Loaded.Add(identity);
            store.Upsert(identity, path);

            var snapshot = new ExpirySnapshot(identity.Fingerprint, 1, identity.ExpiresAt!.Value);
            Assert.True(await session.TryExpireAsync(snapshot));
            Assert.True(client.RemoveCalls >= 1);
            Assert.Empty(session.Identities);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static AgentSession CreateSession(out FakeSshAgentClient client, out TrackedKeyStore store)
    {
        client = new FakeSshAgentClient();
        var file = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-keys-" + Guid.NewGuid().ToString("n") + ".json");
        store = new TrackedKeyStore(file);
        return new AgentSession(client, store: store);
    }
}
