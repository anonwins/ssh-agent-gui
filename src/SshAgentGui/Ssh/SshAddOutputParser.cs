using System.Text.RegularExpressions;

namespace SshAgentGui.Ssh;

internal static class SshAddOutputParser
{
    private static readonly Regex LineRegex = new(
        @"^(?<bits>\d+)\s+(?<fp>\S+)\s+(?<comment>.*)\s+\((?<type>[^)]+)\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsEmptyAgent(string text) =>
        text.Contains("The agent has no identities.", StringComparison.OrdinalIgnoreCase);

    public static bool IsAgentUnavailable(string text)
    {
        return Contains(text, "Error connecting to agent")
               || Contains(text, "Could not open a connection")
               || Contains(text, "No such file or directory")
               || Contains(text, "No such process")
               || Contains(text, "Connection refused")
               || Contains(text, "Bad file descriptor");
    }

    public static List<SshIdentity> ParseList(string stdout)
    {
        var list = new List<SshIdentity>();
        foreach (var raw in stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var match = LineRegex.Match(line);
            if (!match.Success)
                continue;

            var bits = int.TryParse(match.Groups["bits"].Value, out var b) ? b : 0;
            var comment = match.Groups["comment"].Value.Trim();
            list.Add(new SshIdentity(
                fingerprint: match.Groups["fp"].Value,
                comment: comment,
                keyType: match.Groups["type"].Value,
                bits: bits,
                isLoaded: true));
        }

        return list;
    }

    public static string Combine(string stdout, string stderr)
    {
        stdout = stdout.Trim();
        stderr = stderr.Trim();
        if (stdout.Length == 0)
            return stderr;
        if (stderr.Length == 0)
            return stdout;
        return stdout + Environment.NewLine + stderr;
    }

    private static bool Contains(string text, string phrase) =>
        text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
