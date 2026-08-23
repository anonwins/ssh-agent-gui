using System.Text;

namespace SshAgentGui.Ssh;

internal static class PrivateKeyFile
{
    public static bool LooksEncrypted(string path) =>
        File.Exists(path) && Inspect(path) != EncryptionState.Clear;

    public static bool TryConfirmEncrypted(string path) =>
        Inspect(path) == EncryptionState.Encrypted;

    private enum EncryptionState
    {
        Clear,
        Encrypted,
        Unknown,
    }

    private static EncryptionState Inspect(string path)
    {
        if (!File.Exists(path))
            return EncryptionState.Unknown;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return EncryptionState.Unknown;
        }

        if (text.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase))
            return EncryptionState.Encrypted;

        var payload = ExtractPemPayload(text, "OPENSSH PRIVATE KEY");
        if (payload is null)
            return EncryptionState.Clear;

        try
        {
            var data = Convert.FromBase64String(payload);
            return OpenSshBlobState(data);
        }
        catch (FormatException)
        {
            return EncryptionState.Unknown;
        }
    }

    private static string? ExtractPemPayload(string text, string label)
    {
        var begin = "-----BEGIN " + label + "-----";
        var end = "-----END " + label + "-----";
        var start = text.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += begin.Length;
        var stop = text.IndexOf(end, start, StringComparison.Ordinal);
        if (stop < 0)
            return null;
        return text[start..stop].Replace("\r", "").Replace("\n", "").Trim();
    }

    private static EncryptionState OpenSshBlobState(byte[] data)
    {
        ReadOnlySpan<byte> magic = "openssh-key-v1\0"u8;
        if (data.Length < magic.Length + 8 || !data.AsSpan().StartsWith(magic))
            return EncryptionState.Unknown;

        var offset = magic.Length;
        if (!TryReadSshString(data, ref offset, out var cipher))
            return EncryptionState.Unknown;
        return string.Equals(cipher, "none", StringComparison.Ordinal)
            ? EncryptionState.Clear
            : EncryptionState.Encrypted;
    }

    private static bool TryReadSshString(byte[] data, ref int offset, out string value)
    {
        value = "";
        if (offset + 4 > data.Length)
            return false;
        var length = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        offset += 4;
        if (length < 0 || offset + length > data.Length)
            return false;
        value = Encoding.ASCII.GetString(data, offset, length);
        offset += length;
        return true;
    }
}
