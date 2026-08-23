using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
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
    private static readonly ConcurrentDictionary<string, string?> Resolved = new(StringComparer.OrdinalIgnoreCase);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan AskPassTimeout = TimeSpan.FromSeconds(30);
    private const int MaxAskPassAccepts = 2;

    public static string? FindExe(string fileName) => ResolveOpenSshExecutable(fileName);

    public static string? ResolveOpenSshExecutable(string fileName, IReadOnlyList<string>? roots = null)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            return null;

        if (roots is null && Resolved.TryGetValue(fileName, out var cached))
            return cached;

        var path = TryResolve(fileName, roots ?? DefaultRoots());
        if (roots is null)
            Resolved[fileName] = path;
        return path;
    }

    public static async Task<ProcessOutput> RunHiddenAsync(
        string exeName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default,
        Action<ProcessStartInfo>? configure = null)
    {
        _ = workingDirectory;
        return await RunCoreAsync(exeName, arguments, pipeGuid: null, passphrase: null, configure, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<ProcessOutput> RunAddAsync(
        string keyPath,
        string? passphrase,
        TimeSpan? lifetime = null,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>();
        if (lifetime is { } life && life.TotalSeconds >= 1)
        {
            args.Add("-t");
            args.Add(((int)Math.Ceiling(life.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        args.Add(keyPath);

        if (passphrase is null)
        {
            return await RunHiddenAsync("ssh-add.exe", args, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return await RunWithAskPassAsync("ssh-add.exe", args, passphrase, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<ProcessOutput> RunWithAskPassAsync(
        string exeName,
        IReadOnlyList<string> arguments,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AppPaths.Executable))
        {
            return new ProcessOutput
            {
                ExitCode = -1,
                Stderr = "Could not locate this program to unlock the key.",
            };
        }

        return await RunCoreAsync(exeName, arguments, Guid.NewGuid().ToString("n"), passphrase, configure: null, cancellationToken)
            .ConfigureAwait(false);
    }

    public static void StripGitAgentEnv(ProcessStartInfo psi)
    {
        psi.Environment.Remove("SSH_AUTH_SOCK");
        psi.Environment.Remove("SSH_AGENT_PID");
    }

    public static void ConfigureChildEnvironment(ProcessStartInfo psi, string? pipeName, string? askPassExe)
    {
        StripGitAgentEnv(psi);
        psi.Environment.Remove("SSH_ASKPASS_PROMPT");
        psi.Environment.Remove(AskPassMode.LegacyFileEnv);
        psi.Environment.Remove(AskPassMode.PipeEnv);

        if (pipeName is null)
        {
            psi.Environment.Remove("SSH_ASKPASS");
            psi.Environment.Remove("SSH_ASKPASS_REQUIRE");
            psi.Environment.Remove(AskPassMode.LaunchEnv);
            return;
        }

        psi.Environment["SSH_ASKPASS"] = askPassExe ?? "";
        psi.Environment["SSH_ASKPASS_REQUIRE"] = "force";
        // Windows OpenSSH 9.5.4.1 hangs without DISPLAY when askpass is forced; this is not a Unix DISPLAY requirement.
        psi.Environment["DISPLAY"] = "1";
        psi.Environment[AskPassMode.LaunchEnv] = "1";
        psi.Environment[AskPassMode.PipeEnv] = pipeName;
    }

    private static async Task<ProcessOutput> RunCoreAsync(
        string exeName,
        IReadOnlyList<string> arguments,
        string? pipeGuid,
        string? passphrase,
        Action<ProcessStartInfo>? configure,
        CancellationToken cancellationToken)
    {
        var exe = ResolveOpenSshExecutable(exeName);
        if (exe is null)
        {
            return new ProcessOutput
            {
                ExitCode = -1,
                Stderr = $"{exeName} was not found. Install the Windows OpenSSH client.",
            };
        }

        string? pipeName = null;
        NamedPipeServerStream? pipe = null;
        Process? process = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (pipeGuid is not null)
            cts.CancelAfter(AskPassTimeout);

        try
        {
            var askPass = pipeGuid is null ? null : AppPaths.Executable;
            if (pipeGuid is not null && string.IsNullOrEmpty(askPass))
            {
                return new ProcessOutput
                {
                    ExitCode = -1,
                    Stderr = "Could not locate this program to unlock the key.",
                };
            }

            if (pipeGuid is not null)
            {
                pipeName = "ssh-agent-gui-" + pipeGuid;
                pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            ConfigureChildEnvironment(psi, pipeName, askPass);
            configure?.Invoke(psi);

            process = new Process { StartInfo = psi };
            process.Start();
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            if (pipe is not null && passphrase is not null)
                await ServeAskPassAsync(pipe, passphrase, process, cts.Token).ConfigureAwait(false);

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return new ProcessOutput
            {
                ExitCode = process.ExitCode,
                Stdout = await stdoutTask.ConfigureAwait(false),
                Stderr = await stderrTask.ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new ProcessOutput
            {
                ExitCode = -1,
                Stderr = "The operation timed out.",
            };
        }
        finally
        {
            pipe?.Dispose();
            process?.Dispose();
        }
    }

    private static async Task ServeAskPassAsync(
        NamedPipeServerStream pipe,
        string passphrase,
        Process process,
        CancellationToken cancellationToken)
    {
        var payload = Utf8NoBom.GetBytes(passphrase);
        for (var i = 0; i < MaxAskPassAccepts; i++)
        {
            if (process.HasExited)
                return;

            var connect = pipe.WaitForConnectionAsync(cancellationToken);
            var exited = process.WaitForExitAsync(cancellationToken);
            var done = await Task.WhenAny(connect, exited).ConfigureAwait(false);
            if (done != connect)
                return;

            await connect.ConfigureAwait(false);
            try
            {
                await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (pipe.IsConnected)
                    pipe.Disconnect();
            }
        }
    }

    private static void TryKill(Process? process)
    {
        if (process is null)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static IReadOnlyList<string> DefaultRoots() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenSSH"),
    ];

    private static string? TryResolve(string fileName, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            string fullRoot;
            try
            {
                fullRoot = Path.GetFullPath(root);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(fullRoot, fileName));
            var dir = Path.GetDirectoryName(candidate);
            if (dir is null || !dir.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!File.Exists(candidate))
                continue;
            // Defense-in-depth only; not binary authenticity.
            if (IsReparsePoint(candidate) || HasReparseBelowRoot(fullRoot, candidate))
                continue;
            return candidate;
        }

        return null;
    }

    private static bool HasReparseBelowRoot(string root, string filePath)
    {
        var current = Path.GetDirectoryName(filePath);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!string.IsNullOrEmpty(current))
        {
            string fullCurrent;
            try
            {
                fullCurrent = Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
            {
                return true;
            }

            if (fullCurrent.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
                break;
            if (!fullCurrent.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                break;
            if (IsReparsePoint(current))
                return true;
            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
