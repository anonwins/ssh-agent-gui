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

        var args = new List<string> { "-q", "-t", type, "-f", path, "-C", comment };
        if (string.Equals(type, "rsa", StringComparison.OrdinalIgnoreCase))
        {
            args.Insert(3, "-b");
            args.Insert(4, "4096");
        }

        var requestedSecret = !string.IsNullOrEmpty(passphrase);
        ProcessOutput output;
        if (requestedSecret)
        {
            output = await OpenSshProcess.RunWithAskPassAsync("ssh-keygen.exe", args, passphrase, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            args.Add("-N");
            args.Add("");
            output = await OpenSshProcess.RunHiddenAsync("ssh-keygen.exe", args, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (output.ExitCode == 0 && requestedSecret)
        {
            var verified = EnsureCreatedKeyEncrypted(path);
            if (!verified.Ok)
                return verified;
        }

        if (output.ExitCode == 0)
            return SshAgentResult.Success();

        return SshAgentResult.Fail(OpenSshText.ForKeygen(output.Combined, passphrase));
    }

    public async Task<SshAgentResult<SshIdentity>> FingerprintAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (OpenSshProcess.FindExe("ssh-keygen.exe") is null)
            return SshAgentResult<SshIdentity>.Missing("Windows OpenSSH ssh-keygen.exe was not found.");

        var output = await OpenSshProcess.RunHiddenAsync(
                "ssh-keygen.exe",
                ["-l", "-f", path],
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var parsed = SshAddOutputParser.ParseList(output.Stdout);
        if (parsed.Count > 0)
            return SshAgentResult<SshIdentity>.OkValue(parsed[0]);

        return SshAgentResult<SshIdentity>.Fail("Could not read the new key fingerprint.");
    }

    public async Task<SshAgentResult<SshIdentity>> FingerprintPublicLineAsync(
        string publicKeyLine,
        CancellationToken cancellationToken = default)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-" + Guid.NewGuid().ToString("n") + ".pub");
        try
        {
            await File.WriteAllTextAsync(tmp, publicKeyLine.Trim() + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
            return await FingerprintAsync(tmp, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
                // temp leftover is harmless
            }
        }
    }

    // Backstop inspector, not the definition of encryption: OpenSSH treats a failed
    // askpass as an empty passphrase and may exit 0 with an unencrypted key.
    internal static SshAgentResult EnsureCreatedKeyEncrypted(string path)
    {
        if (PrivateKeyFile.TryConfirmEncrypted(path))
            return SshAgentResult.Success();
        DeleteCreatedKey(path);
        return SshAgentResult.Fail(OpenSshText.NotEncrypted);
    }

    internal static void DeleteCreatedKey(string path)
    {
        TryDelete(path);
        TryDelete(path + ".pub");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
