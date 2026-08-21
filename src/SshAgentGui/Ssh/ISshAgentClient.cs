namespace SshAgentGui.Ssh;

internal interface ISshAgentClient
{
    Task<SshAgentResult<List<SshIdentity>>> ListAsync(CancellationToken cancellationToken = default);
    Task<SshAgentResult<List<string>>> ListPublicAsync(CancellationToken cancellationToken = default);
    Task<SshAgentResult> AddAsync(string keyPath, bool interactive, CancellationToken cancellationToken = default);
    Task<SshAgentResult> RemoveAsync(string keyPath, CancellationToken cancellationToken = default);
    Task<SshAgentResult> RemoveAllAsync(CancellationToken cancellationToken = default);
}
