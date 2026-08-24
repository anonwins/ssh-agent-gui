namespace SshAgentGui.Ssh;

internal static class SshAgentFrame
{
    public const int MaxLength = 262144;
    public const byte FailureType = 5;
    public const byte RequestIdentities = 11;
    public const byte SignRequest = 13;
    public const byte Ssh1RequestIdentities = 1;
    public const byte Ssh1Challenge = 3;

    public static readonly byte[] FailureBytes = [0, 0, 0, 1, FailureType];

    public static byte[] Failure() => FailureBytes;

    public static bool IsSsh1(byte type) => type is Ssh1RequestIdentities or Ssh1Challenge;

    public static bool TryRead(ReadOnlySpan<byte> frame, out byte type, out ReadOnlySpan<byte> body)
    {
        type = 0;
        body = default;
        if (frame.Length < 5)
            return false;
        var length = ReadUInt32Be(frame);
        if (length < 1 || length > MaxLength - 4 || frame.Length < 4 + length)
            return false;
        type = frame[4];
        body = frame.Slice(5, (int)length - 1);
        return true;
    }

    public static bool TryGetSignKeyBlob(ReadOnlySpan<byte> body, out byte[] blob)
    {
        blob = [];
        if (!TryReadString(body, out var key, out _))
            return false;
        blob = key.ToArray();
        return blob.Length > 0;
    }

    public static bool TryReadString(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> value, out ReadOnlySpan<byte> rest)
    {
        value = default;
        rest = data;
        if (data.Length < 4)
            return false;
        var length = ReadUInt32Be(data);
        if (length > data.Length - 4)
            return false;
        value = data.Slice(4, (int)length);
        rest = data.Slice(4 + (int)length);
        return true;
    }

    public static uint ReadUInt32Be(ReadOnlySpan<byte> data) =>
        ((uint)data[0] << 24) | ((uint)data[1] << 16) | ((uint)data[2] << 8) | data[3];

    public static byte[] Prefix(byte type, ReadOnlySpan<byte> payload)
    {
        var length = 1 + payload.Length;
        var frame = new byte[4 + length];
        frame[0] = (byte)(length >> 24);
        frame[1] = (byte)(length >> 16);
        frame[2] = (byte)(length >> 8);
        frame[3] = (byte)length;
        frame[4] = type;
        payload.CopyTo(frame.AsSpan(5));
        return frame;
    }
}
