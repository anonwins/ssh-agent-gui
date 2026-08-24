namespace SshAgentGui.Ssh;

internal interface IOpenSshAgentPipe
{
    byte[]? Transact(byte[] request);
}
