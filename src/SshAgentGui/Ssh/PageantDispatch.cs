namespace SshAgentGui.Ssh;

internal static class PageantDispatch
{
    public static byte[]? Handle(byte[] frame, IOpenSshAgentPipe pipe, Func<byte[], bool> confirm)
    {
        if (!SshAgentFrame.TryRead(frame, out var type, out var body))
            return null;

        if (SshAgentFrame.IsSsh1(type))
            return SshAgentFrame.Failure();

        if (type == SshAgentFrame.SignRequest)
        {
            if (!SshAgentFrame.TryGetSignKeyBlob(body, out var blob))
                return SshAgentFrame.Failure();
            try
            {
                if (!confirm(blob))
                    return SshAgentFrame.Failure();
            }
            catch
            {
                return SshAgentFrame.Failure();
            }
        }

        return pipe.Transact(frame) ?? SshAgentFrame.Failure();
    }
}
