namespace SshAgentGui.Ssh;

internal static class OpenSshText
{
    public const string AddFailed = "ssh-add failed.";
    public const string KeygenFailed = "ssh-keygen failed.";
    public const string IncorrectPassphrase = "The passphrase was incorrect.";
    public const string AccessDenied = "Access to the key file was denied.";
    public const string FileNotFound = "The key file was not found.";
    public const string UnusableKey = "That file is not a usable private key.";
    public const string InvalidLifetime = "The key lifetime is not valid.";
    public const string NotEncrypted = "The new key was not encrypted. It was removed.";

    public static string ForAdd(string text, int exitCode, bool successIfEmpty)
    {
        if (exitCode != 0 && string.IsNullOrWhiteSpace(text) && !successIfEmpty)
            return IncorrectPassphrase;
        return Classify(text, AddFailed);
    }

    public static string ForKeygen(string text, string? secret = null) =>
        Classify(Redact(text, secret), KeygenFailed);

    public static string ForList(string text) => Classify(text, AddFailed);

    public static string Classify(string text, string generic)
    {
        if (string.IsNullOrWhiteSpace(text))
            return generic;

        if (Contains(text, "incorrect passphrase")
            || Contains(text, "wrong passphrase")
            || Contains(text, "bad passphrase"))
            return IncorrectPassphrase;

        if (Contains(text, "too open")
            || Contains(text, "bad permissions")
            || Contains(text, "permission denied")
            || Contains(text, "access is denied"))
            return AccessDenied;

        if (Contains(text, "no such file or directory")
            || Contains(text, "the system cannot find the file")
            || Contains(text, "cannot find the path"))
            return FileNotFound;

        if (Contains(text, "error loading key")
            || Contains(text, "invalid format")
            || Contains(text, "not a key")
            || Contains(text, "is not a valid"))
            return UnusableKey;

        if (Contains(text, "invalid lifetime"))
            return InvalidLifetime;

        return generic;
    }

    public static string Redact(string text, string? secret)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(text))
            return text;
        return text.Replace(secret, "********", StringComparison.Ordinal);
    }

    private static bool Contains(string text, string phrase) =>
        text.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
