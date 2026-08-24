namespace SshAgentGui.Ssh;

internal static class PageantMapping
{
    public const int MaxNameLength = 255;

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
