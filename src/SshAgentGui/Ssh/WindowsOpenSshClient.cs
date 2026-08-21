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

    public async Task<SshAgentResult> AddAsync(string keyPath, bool interactive, CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-add.exe") is null)
            return SshAgentResult.Missing(MissingMessage());

        if (interactive)
        {
            var code = await OpenSshProcess.RunAddConsoleAsync(keyPath, cancellationToken).ConfigureAwait(false);
            if (code == 0)
                return SshAgentResult.Success();
            if (code == -1)
                return SshAgentResult.Missing(MissingMessage());
            return SshAgentResult.Fail("ssh-add did not add the key (closed or failed).");
        }

        var workDir = Path.GetDirectoryName(keyPath);
        var output = await OpenSshProcess.RunHiddenAsync(
                "ssh-add.exe",
                [keyPath],
                workDir,
                cancellationToken)
            .ConfigureAwait(false);
        return ClassifyMutation(output, successIfEmpty: false);
    }

    public async Task<SshAgentResult> RemoveAsync(string keyPath, CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-add.exe") is null)
            return SshAgentResult.Missing(MissingMessage());

        var workDir = Path.GetDirectoryName(keyPath);
        var output = await OpenSshProcess.RunHiddenAsync(
                "ssh-add.exe",
                ["-d", keyPath],
                workDir,
                cancellationToken)
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
            return SshAgentResult<List<SshIdentity>>.Unavailable(UnavailableMessage(text));

        var rows = SshAddOutputParser.ParseList(output.Stdout);
        if (output.ExitCode == 0)
            return SshAgentResult<List<SshIdentity>>.OkValue(rows);

        if (rows.Count == 0)
            return SshAgentResult<List<SshIdentity>>.Fail(string.IsNullOrWhiteSpace(text) ? "ssh-add -l failed." : text);

        return SshAgentResult<List<SshIdentity>>.OkValue(rows);
    }

    private static SshAgentResult ClassifyMutation(ProcessOutput output, bool successIfEmpty)
    {
        var text = output.Combined;
        if (SshAddOutputParser.IsAgentUnavailable(text))
            return SshAgentResult.Unavailable(UnavailableMessage(text));
        if (output.ExitCode == 0)
            return SshAgentResult.Success();
        if (successIfEmpty && SshAddOutputParser.IsEmptyAgent(text))
            return SshAgentResult.Success();
        return SshAgentResult.Fail(string.IsNullOrWhiteSpace(text) ? "ssh-add failed." : text);
    }

    private static string MissingMessage() =>
        "Windows OpenSSH ssh-add.exe was not found. Install the OpenSSH Client optional feature.";

    private static string UnavailableMessage(string detail)
    {
        var hint = "The OpenSSH Authentication Agent (ssh-agent) is not running. Start it from Services.";
        return string.IsNullOrWhiteSpace(detail) ? hint : hint + Environment.NewLine + detail;
    }
}
