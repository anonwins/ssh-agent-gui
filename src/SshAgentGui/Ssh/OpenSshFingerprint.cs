using System.Security.Cryptography;

namespace SshAgentGui.Ssh;

internal static class OpenSshFingerprint
{
    public static string Sha256(ReadOnlySpan<byte> keyBlob)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(keyBlob, hash);
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    public static bool TryParsePublicLine(string line, out byte[] blob)
    {
        blob = [];
        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;
        try
        {
            blob = Convert.FromBase64String(parts[1]);
            return blob.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
