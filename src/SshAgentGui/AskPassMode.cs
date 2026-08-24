using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;

namespace SshAgentGui;

internal static class AskPassMode
{
    public const string Flag = "--askpass";
    public const string LaunchEnv = "SSH_AGENT_GUI_ASKPASS";
    public const string PipeEnv = "SSH_AGENT_GUI_PASSPHRASE_PIPE";
    public const string LegacyFileEnv = "SSH_AGENT_GUI_PASSPHRASE_FILE";
    internal const int MaxPassphraseBytes = 16384;

    private static readonly Regex WinPath = new(
        @"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UncPath = new(
        @"\\\\[^\\/:*?""<>|\r\n]+(?:\\[^\\/:*?""<>|\r\n]+)+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsLaunch(IReadOnlyList<string> args) =>
        IsLaunch(args, Environment.GetEnvironmentVariable(LaunchEnv), Environment.GetEnvironmentVariable(PipeEnv));

    public static bool IsLaunch(IReadOnlyList<string> args, string? launchEnv, string? pipeName)
    {
        if (args.Count > 0 && string.Equals(args[0], Flag, StringComparison.Ordinal))
            return true;
        if (args.Count > 0 && string.Equals(args[0], StartAgentServiceMode.Flag, StringComparison.Ordinal))
            return false;
        if (!string.Equals(launchEnv, "1", StringComparison.Ordinal))
            return false;
        return ExtraArgCount(args) > 0 || !string.IsNullOrWhiteSpace(pipeName);
    }

    public static int Run(IReadOnlyList<string> args)
    {
        var pipeName = Environment.GetEnvironmentVariable(PipeEnv);
        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            if (!TryReadPassphraseFromPipe(pipeName, out var fromPipe) || fromPipe is null)
                return 1;
            WriteStdout(fromPipe);
            return 0;
        }

        if (!IsAskPassInvoke(args))
            return 1;

        var prompt = SanitizePrompt(PromptFromArgs(args));
        var dialog = new PassphraseWindow(prompt);
        if (dialog.ShowDialog() != true)
            return 1;

        WriteStdout(dialog.Passphrase);
        return 0;
    }

    public static bool TryReadPassphraseFromPipe(string name, out string? secret)
    {
        secret = null;
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.In, PipeOptions.CurrentUserOnly);
            client.Connect(5000);
            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            var total = 0;
            int n;
            while ((n = client.Read(chunk, 0, chunk.Length)) > 0)
            {
                total += n;
                if (total > MaxPassphraseBytes)
                    return false;
                buffer.Write(chunk, 0, n);
            }

            secret = Utf8NoBom.GetString(buffer.ToArray());
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string SanitizePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return "Enter the passphrase for this key.";

        var text = prompt.Trim();
        text = WinPath.Replace(text, m => Path.GetFileName(m.Value.TrimEnd(':', ' ')));
        text = UncPath.Replace(text, m => Path.GetFileName(m.Value.TrimEnd(':', ' ')));
        return string.IsNullOrWhiteSpace(text) ? "Enter the passphrase for this key." : text;
    }

    private static bool IsAskPassInvoke(IReadOnlyList<string> args) =>
        (args.Count > 0 && string.Equals(args[0], Flag, StringComparison.Ordinal))
        || ExtraArgCount(args) > 0;

    private static int ExtraArgCount(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return 0;
        return string.Equals(args[0], Flag, StringComparison.Ordinal) ? args.Count - 1 : args.Count;
    }

    private static string PromptFromArgs(IReadOnlyList<string> args)
    {
        var parts = args.Count > 0 && string.Equals(args[0], Flag, StringComparison.Ordinal)
            ? args.Skip(1)
            : args;
        return string.Join(" ", parts).Trim();
    }

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static void WriteStdout(string secret)
    {
        using var writer = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom)
        {
            NewLine = "\n",
            AutoFlush = true,
        };
        writer.Write(secret);
        if (!secret.EndsWith('\n'))
            writer.Write('\n');
    }
}
