using System.IO.Pipes;
using System.Text;
using SshAgentGui;

namespace SshAgentGui.Tests;

public sealed class PipeTests
{
    [Fact]
    public async Task Same_user_in_client_reads_server_out()
    {
        var name = "ssh-agent-gui-test-" + Guid.NewGuid().ToString("n");
        using var server = new NamedPipeServerStream(
            name,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var bytes = new UTF8Encoding(false).GetBytes("passphrase");
            await server.WriteAsync(bytes);
            await server.FlushAsync();
            server.Disconnect();
        });

        Assert.True(AskPassMode.TryReadPassphraseFromPipe(name, out var secret));
        Assert.Equal("passphrase", secret);
        await serve;
    }

    [Fact]
    public void Random_pipe_name_is_denied() =>
        Assert.False(AskPassMode.TryReadPassphraseFromPipe("ssh-agent-gui-no-such-" + Guid.NewGuid().ToString("n"), out _));

    [Fact]
    public async Task Oversized_payload_is_rejected()
    {
        var name = "ssh-agent-gui-oversize-" + Guid.NewGuid().ToString("n");
        using var server = new NamedPipeServerStream(
            name,
            PipeDirection.Out,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var bytes = new byte[AskPassMode.MaxPassphraseBytes + 1];
            await server.WriteAsync(bytes);
            await server.FlushAsync();
            server.Disconnect();
        });

        Assert.False(AskPassMode.TryReadPassphraseFromPipe(name, out var secret));
        Assert.Null(secret);
        await serve;
    }
}
