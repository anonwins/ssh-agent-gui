using System.Text;

namespace SshAgentGui.Ssh;

internal static class PrivateKeyFile
{
    public static bool LooksEncrypted(string path)
    {
        if (!File.Exists(path))
            return false;

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            return true;
        }

        if (text.Contains("ENCRYPTED", StringComparison.OrdinalIgnoreCase))
            return true;

        var payload = ExtractPemPayload(text, "OPENSSH PRIVATE KEY");
        if (payload is null)
            return false;

        try
        {
            var data = Convert.FromBase64String(payload);
            return OpenSshBlobEncrypted(data);
        }
        catch (FormatException)
        {
            return true;
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

    private static bool OpenSshBlobEncrypted(byte[] data)
    {
        ReadOnlySpan<byte> magic = "openssh-key-v1\0"u8;
        if (data.Length < magic.Length + 8 || !data.AsSpan().StartsWith(magic))
            return true;

        var offset = magic.Length;
        if (!TryReadSshString(data, ref offset, out var cipher))
            return true;
        return !string.Equals(cipher, "none", StringComparison.Ordinal);
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
