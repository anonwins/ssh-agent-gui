using System.Text;

namespace SshAgentGui;

internal static class AskPassMode
{
    public const string Flag = "--askpass";
    public const string LaunchEnv = "SSH_AGENT_GUI_ASKPASS";
    public const string FileEnv = "SSH_AGENT_GUI_PASSPHRASE_FILE";

    public static bool IsLaunch(IReadOnlyList<string> args)
    {
        if (args.Count > 0 && string.Equals(args[0], Flag, StringComparison.Ordinal))
            return true;
        return string.Equals(Environment.GetEnvironmentVariable(LaunchEnv), "1", StringComparison.Ordinal);
    }

    public static int Run(IReadOnlyList<string> args)
    {
        var secret = TryReadOnce();
        if (secret is null)
        {
            var parts = args.Count > 0 && string.Equals(args[0], Flag, StringComparison.Ordinal)
                ? args.Skip(1)
                : args;
            var prompt = string.Join(" ", parts).Trim();
            var dialog = new PassphraseWindow(prompt);
            if (dialog.ShowDialog() != true)
                return 1;
            secret = dialog.Passphrase;
        }

        WriteStdout(secret);
        return 0;
    }

    private static string? TryReadOnce()
    {
        var file = Environment.GetEnvironmentVariable(FileEnv);
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            return null;
        try
        {
            var text = File.ReadAllText(file);
            return text;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // parent also deletes
            }
        }
    }

    private static void WriteStdout(string secret)
    {
        using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n",
            AutoFlush = true,
        };
        writer.Write(secret);
        if (!secret.EndsWith('\n'))
            writer.Write('\n');
    }
}
