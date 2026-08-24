using System.IO.Pipes;
using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class ProcessAncestryTests
{
    [Fact]
    public void Current_process_is_descendant_or_self_of_itself() =>
        Assert.True(ProcessAncestry.IsDescendantOrSelf(Environment.ProcessId, Environment.ProcessId));

    [Fact]
    public void Current_process_is_descendant_of_its_parent()
    {
        Assert.True(ProcessAncestry.TryGetParentProcessId(Environment.ProcessId, out var parent));
        Assert.True(parent > 0);
        Assert.True(ProcessAncestry.IsDescendantOrSelf(Environment.ProcessId, parent));
        Assert.False(ProcessAncestry.IsDescendantOrSelf(parent, Environment.ProcessId));
    }

    [Fact]
    public void Unrelated_pid_is_not_trusted() =>
        Assert.False(ProcessAncestry.IsDescendantOrSelf(Environment.ProcessId, 1));

    [Fact]
    public async Task Pipe_client_pid_is_this_process()
    {
        var name = "ssh-agent-gui-ancestry-" + Guid.NewGuid().ToString("n");
        using var server = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accepted = server.WaitForConnectionAsync();
        await client.ConnectAsync(2000);
        await accepted;

        Assert.True(ProcessAncestry.TryGetNamedPipeClientProcessId(server.SafePipeHandle, out var pid));
        Assert.Equal(Environment.ProcessId, pid);
        Assert.True(ProcessAncestry.IsTrustedPipeClient(server.SafePipeHandle, Environment.ProcessId, out _));
        Assert.False(ProcessAncestry.IsTrustedPipeClient(server.SafePipeHandle, 1, out _));
    }

    [Fact]
    public void Invalid_ids_are_rejected()
    {
        Assert.False(ProcessAncestry.IsDescendantOrSelf(0, Environment.ProcessId));
        Assert.False(ProcessAncestry.IsDescendantOrSelf(Environment.ProcessId, 0));
        Assert.False(ProcessAncestry.TryGetParentProcessId(-1, out _));
    }
}
