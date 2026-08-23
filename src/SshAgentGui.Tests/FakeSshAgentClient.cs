using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

internal sealed class FakeSshAgentClient : ISshAgentClient
{
    public List<SshIdentity> Loaded { get; } = [];
    public SshAgentResult<List<SshIdentity>>? ListOverride { get; set; }
    public SshAgentResult? RemoveOverride { get; set; }
    public SshAgentResult? AddOverride { get; set; }
    public int RemoveCalls { get; private set; }
    public string? LastRemovedPath { get; private set; }

    public Task<SshAgentResult<List<SshIdentity>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (ListOverride is not null)
            return Task.FromResult(ListOverride);
        if (Loaded.Count == 0)
            return Task.FromResult(SshAgentResult<List<SshIdentity>>.Empty());
        return Task.FromResult(SshAgentResult<List<SshIdentity>>.OkValue(
            Loaded.Select(i => new SshIdentity(i.Fingerprint, i.Comment, i.KeyType, i.Bits, i.Path)).ToList()));
    }

    public Task<SshAgentResult<List<string>>> ListPublicAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SshAgentResult<List<string>>.OkValue(new List<string>()));

    public Task<SshAgentResult> AddAsync(
        string keyPath,
        string? passphrase = null,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AddOverride ?? SshAgentResult.Success());

    public Task<SshAgentResult> RemoveAsync(string keyPath, CancellationToken cancellationToken = default)
    {
        RemoveCalls++;
        LastRemovedPath = keyPath;
        if (RemoveOverride is not null)
            return Task.FromResult(RemoveOverride);

        Loaded.RemoveAll(i =>
            string.Equals(i.Path, keyPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.Fingerprint, Path.GetFileNameWithoutExtension(keyPath), StringComparison.Ordinal));
        return Task.FromResult(SshAgentResult.Success());
    }

    public Task<SshAgentResult> RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        Loaded.Clear();
        return Task.FromResult(SshAgentResult.Success());
    }
}
