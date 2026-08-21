using System.Diagnostics;
using System.Text;

namespace SshAgentGui.Ssh;

internal sealed class ProcessOutput
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = "";
    public string Stderr { get; init; } = "";
    public string Combined => SshAddOutputParser.Combine(Stdout, Stderr);
}

internal static class OpenSshProcess
{
    private static string? _directory;

    public static string? DirectoryPath => _directory ??= ResolveDirectory();

    public static string? FindExe(string fileName)
    {
        var dir = DirectoryPath;
        if (dir is null)
            return null;
        var path = Path.Combine(dir, fileName);
        return File.Exists(path) ? path : null;
    }

    public static async Task<ProcessOutput> RunHiddenAsync(
        string exeName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        Action<ProcessStartInfo>? configure = null)
    {
        var exe = FindExe(exeName);
        if (exe is null)
        {
            return new ProcessOutput
            {
                ExitCode = -1,
                Stderr = $"{exeName} was not found. Install the Windows OpenSSH client.",
            };
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exe)!,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);
        StripGitAgentEnv(psi);
        configure?.Invoke(psi);

        psi.RedirectStandardInput = true;

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessOutput
        {
            ExitCode = process.ExitCode,
            Stdout = await stdoutTask.ConfigureAwait(false),
            Stderr = await stderrTask.ConfigureAwait(false),
        };
    }

    public static async Task<ProcessOutput> RunAddAsync(
        string keyPath,
        string? passphrase,
        CancellationToken cancellationToken = default)
    {
        var workDir = Path.GetDirectoryName(keyPath);
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
            workDir = DirectoryPath;

        string? secretFile = null;
        try
        {
            if (passphrase is not null)
                secretFile = WritePassphraseFile(passphrase);

            return await RunHiddenAsync(
                    "ssh-add.exe",
                    [keyPath],
                    workDir,
                    cancellationToken,
                    psi => ApplyAskPass(psi, secretFile))
                .ConfigureAwait(false);
        }
        finally
        {
            if (secretFile is not null)
            {
                try
                {
                    File.Delete(secretFile);
                }
                catch (IOException)
                {
                    // askpass deletes on read
                }
            }
        }
    }

    public static void StripGitAgentEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("SSH_AUTH_SOCK");
        psi.Environment.Remove("SSH_AGENT_PID");
    }

    private static void ApplyAskPass(ProcessStartInfo psi, string? secretFile)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || !File.Exists(self))
            return;

        psi.Environment["SSH_ASKPASS"] = self;
        psi.Environment["SSH_ASKPASS_REQUIRE"] = "force";
        psi.Environment["DISPLAY"] = "1";
        psi.Environment[AskPassMode.LaunchEnv] = "1";
        psi.Environment.Remove("SSH_ASKPASS_PROMPT");
        if (secretFile is not null)
            psi.Environment[AskPassMode.FileEnv] = secretFile;
        else
            psi.Environment.Remove(AskPassMode.FileEnv);
    }

    private static string WritePassphraseFile(string passphrase)
    {
        var path = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-" + Guid.NewGuid().ToString("n") + ".tmp");
        File.WriteAllText(path, passphrase);
        return path;
    }

    private static string? ResolveDirectory()
    {
        foreach (var candidate in CandidateDirectories())
        {
            if (File.Exists(Path.Combine(candidate, "ssh-add.exe")))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        yield return Path.Combine(system, "OpenSSH");
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf, "OpenSSH");

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            yield break;
        foreach (var part in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim().Trim('"');
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }

}
