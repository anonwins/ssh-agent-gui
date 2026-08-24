using System.Globalization;

namespace SshAgentGui.Ssh;

internal static class PageantMapping
{
    public const int MaxNameLength = 255;
    public const string PuttyRequestPrefix = "PageantRequest";
    public const string SshAgentRequestPrefix = "SSHAgentRequest";

    public static bool TryGetPuttyRequestThreadId(string name, out uint threadId)
    {
        threadId = 0;
        var rest = RestAfterPrefix(name);
        if (rest.IsEmpty || rest.Length > 8)
            return false;
        return uint.TryParse(rest, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out threadId)
            && threadId != 0;
    }

    private static ReadOnlySpan<char> RestAfterPrefix(string name)
    {
        if (name.StartsWith(PuttyRequestPrefix, StringComparison.Ordinal))
            return name.AsSpan(PuttyRequestPrefix.Length);
        if (name.StartsWith(SshAgentRequestPrefix, StringComparison.Ordinal))
            return name.AsSpan(SshAgentRequestPrefix.Length);
        return [];
    }

    public static bool IsSafeName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return false;

        foreach (var c in name)
        {
            if (c is < (char)0x21 or > (char)0x7E)
                return false;
            if (c is '\\' or '/' or ':')
                return false;
        }

        return true;
    }
}
