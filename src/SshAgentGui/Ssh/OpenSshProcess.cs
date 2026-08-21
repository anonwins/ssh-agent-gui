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
        CancellationToken cancellationToken = default)
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

        using var process = new Process { StartInfo = psi };
        process.Start();
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

    public static async Task<int> RunAddConsoleAsync(string keyPath, CancellationToken cancellationToken = default)
    {
        var exe = FindExe("ssh-add.exe");
        if (exe is null)
            return -1;

        var workDir = Path.GetDirectoryName(keyPath);
        if (string.IsNullOrEmpty(workDir) || !Directory.Exists(workDir))
            workDir = Path.GetDirectoryName(exe);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + "\"" + Quote(exe) + " " + Quote(keyPath) + " || pause\"",
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = workDir!,
        };
        StripGitAgentEnv(psi);

        using var process = Process.Start(psi);
        if (process is null)
            return -1;
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    public static void StripGitAgentEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("SSH_AUTH_SOCK");
        psi.Environment.Remove("SSH_AGENT_PID");
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

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
