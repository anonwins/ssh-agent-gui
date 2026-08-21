namespace SshAgentGui.Ssh;

internal sealed class WindowsSshKeygen
{
    public async Task<SshAgentResult> CreateAsync(
        string type,
        string path,
        string comment,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-keygen.exe") is null)
            return SshAgentResult.Missing("Windows OpenSSH ssh-keygen.exe was not found.");

        var args = new List<string> { "-q", "-t", type, "-f", path, "-C", comment, "-N", passphrase };
        if (string.Equals(type, "rsa", StringComparison.OrdinalIgnoreCase))
        {
            args.Insert(3, "-b");
            args.Insert(4, "4096");
        }

        var workDir = Path.GetDirectoryName(path);
        var output = await OpenSshProcess.RunHiddenAsync("ssh-keygen.exe", args, workDir, cancellationToken)
            .ConfigureAwait(false);

        if (output.ExitCode == 0)
            return SshAgentResult.Success();

        var text = Sanitize(output.Combined, passphrase);
        return SshAgentResult.Fail(string.IsNullOrWhiteSpace(text)
            ? "ssh-keygen failed."
            : text);
    }

    public async Task<SshAgentResult<SshIdentity>> FingerprintAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-keygen.exe") is null)
            return SshAgentResult<SshIdentity>.Missing("Windows OpenSSH ssh-keygen.exe was not found.");

        var workDir = Path.GetDirectoryName(path);
        var output = await OpenSshProcess.RunHiddenAsync("ssh-keygen.exe", ["-l", "-f", path], workDir, cancellationToken)
            .ConfigureAwait(false);
        var parsed = SshAddOutputParser.ParseList(output.Stdout);
        if (parsed.Count > 0)
            return SshAgentResult<SshIdentity>.OkValue(parsed[0]);

        var text = output.Combined;
        return SshAgentResult<SshIdentity>.Fail(string.IsNullOrWhiteSpace(text)
            ? "Could not read the new key fingerprint."
            : text);
    }

    private static string Sanitize(string text, string secret)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(text))
            return text;
        return text.Replace(secret, "********", StringComparison.Ordinal);
    }
}
