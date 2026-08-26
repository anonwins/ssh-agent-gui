namespace SshAgentGui.Ssh;

internal delegate bool PageantConfirm(byte[] blob, PageantCallerInfo? caller);

internal static class PageantDispatch
{
    public static byte[]? Handle(byte[] frame, IOpenSshAgentPipe pipe, PageantConfirm confirm, PageantCallerInfo? caller = null)
    {
        if (!SshAgentFrame.TryRead(frame, out var type, out var body))
            return null;

        if (SshAgentFrame.IsSsh1(type))
            return SshAgentFrame.Failure();

        if (!IsAllowedPageantRequest(type))
            return SshAgentFrame.Failure();

        if (type == SshAgentFrame.SignRequest)
        {
            if (!SshAgentFrame.TryGetSignKeyBlob(body, out var blob))
                return SshAgentFrame.Failure();
            try
            {
                if (!confirm(blob, caller))
                    return SshAgentFrame.Failure();
            }
            catch
            {
                return SshAgentFrame.Failure();
            }
        }

        return pipe.Transact(frame) ?? SshAgentFrame.Failure();
    }

    private static bool IsAllowedPageantRequest(byte type) =>
        type is SshAgentFrame.RequestIdentities or SshAgentFrame.SignRequest;
}
