namespace SshAgentGui.Ssh;

internal sealed class WindowsOpenSshClient : ISshAgentClient
{
    public async Task<SshAgentResult<List<SshIdentity>>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-add.exe") is null)
            return SshAgentResult<List<SshIdentity>>.Missing(MissingMessage());

        var output = await OpenSshProcess.RunHiddenAsync("ssh-add.exe", ["-l"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ClassifyList(output);
    }

    public async Task<SshAgentResult<List<string>>> ListPublicAsync(CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-add.exe") is null)
            return SshAgentResult<List<string>>.Missing(MissingMessage());

        var output = await OpenSshProcess.RunHiddenAsync("ssh-add.exe", ["-L"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ClassifyPublic(output);
    }

    public async Task<SshAgentResult> AddAsync(
        string keyPath,
        string? passphrase = null,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-add.exe") is null)
            return SshAgentResult.Missing(MissingMessage());

        var output = await OpenSshProcess.RunAddAsync(keyPath, passphrase, lifetime, cancellationToken)
            .ConfigureAwait(false);
        return ClassifyMutation(output, successIfEmpty: false);
    }

    public async Task<SshAgentResult> RemoveAsync(string keyPath, CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-add.exe") is null)
            return SshAgentResult.Missing(MissingMessage());

        var output = await OpenSshProcess.RunHiddenAsync(
                "ssh-add.exe",
                ["-d", keyPath],
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ClassifyMutation(output, successIfEmpty: true);
    }

    public async Task<SshAgentResult> RemoveAllAsync(CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-add.exe") is null)
            return SshAgentResult.Missing(MissingMessage());

        var output = await OpenSshProcess.RunHiddenAsync("ssh-add.exe", ["-D"], cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ClassifyMutation(output, successIfEmpty: true);
    }

    private static SshAgentResult<List<SshIdentity>> ClassifyList(ProcessOutput output)
    {
        var text = output.Combined;
        if (SshAddOutputParser.IsEmptyAgent(text))
            return SshAgentResult<List<SshIdentity>>.Empty();
        if (SshAddOutputParser.IsAgentUnavailable(text))
            return SshAgentResult<List<SshIdentity>>.Unavailable(UnavailableMessage());

        var rows = SshAddOutputParser.ParseList(output.Stdout);
        if (output.ExitCode == 0)
            return SshAgentResult<List<SshIdentity>>.OkValue(rows);

        if (rows.Count == 0)
            return SshAgentResult<List<SshIdentity>>.Fail(OpenSshText.ForList(text));

        return SshAgentResult<List<SshIdentity>>.OkValue(rows);
    }

    private static SshAgentResult<List<string>> ClassifyPublic(ProcessOutput output)
    {
        var text = output.Combined;
        if (SshAddOutputParser.IsEmptyAgent(text))
            return SshAgentResult<List<string>>.Empty();
        if (SshAddOutputParser.IsAgentUnavailable(text))
            return SshAgentResult<List<string>>.Unavailable(UnavailableMessage());

        var rows = SshAddOutputParser.ParsePublicKeys(output.Stdout);
        if (output.ExitCode == 0 || rows.Count > 0)
            return SshAgentResult<List<string>>.OkValue(rows);

        return SshAgentResult<List<string>>.Fail(OpenSshText.ForList(text));
    }

    private static SshAgentResult ClassifyMutation(ProcessOutput output, bool successIfEmpty)
    {
        var text = output.Combined;
        if (SshAddOutputParser.IsAgentUnavailable(text))
            return SshAgentResult.Unavailable(UnavailableMessage());
        if (output.ExitCode == 0)
            return SshAgentResult.Success();
        if (successIfEmpty && SshAddOutputParser.IsEmptyAgent(text))
            return SshAgentResult.Success();
        return SshAgentResult.Fail(OpenSshText.ForAdd(text, output.ExitCode, successIfEmpty));
    }

    private static string MissingMessage() =>
        "Windows OpenSSH ssh-add.exe was not found. Install the OpenSSH Client optional feature.";

    private static string UnavailableMessage() =>
        "The OpenSSH Authentication Agent is not running.";
}
