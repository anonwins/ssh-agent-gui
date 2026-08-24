using System.IO.Pipes;

namespace SshAgentGui.Ssh;

internal sealed class OpenSshAgentPipe : IOpenSshAgentPipe
{
    public const string DefaultName = "openssh-ssh-agent";

    private readonly string _name;

    public OpenSshAgentPipe(string? name = null)
    {
        _name = string.IsNullOrWhiteSpace(name) ? DefaultName : name;
    }

    public byte[]? Transact(byte[] request)
    {
        try
        {
            return TransactAsync(request).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException or AggregateException)
        {
            return null;
        }
    }

    private async Task<byte[]?> TransactAsync(byte[] request)
    {
        if (request.Length < 5 || request.Length > SshAgentFrame.MaxLength)
            return null;

        using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

        using var ioCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.WriteAsync(request, ioCts.Token).ConfigureAwait(false);
        await pipe.FlushAsync(ioCts.Token).ConfigureAwait(false);

        var header = await ReadExactAsync(pipe, 4, ioCts.Token).ConfigureAwait(false);
        if (header is null)
            return null;
        var length = SshAgentFrame.ReadUInt32Be(header);
        if (length < 1 || length > SshAgentFrame.MaxLength - 4)
            return null;
        var body = await ReadExactAsync(pipe, (int)length, ioCts.Token).ConfigureAwait(false);
        if (body is null)
            return null;

        var frame = new byte[4 + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame, 4);
        return frame;
    }

    private static async Task<byte[]?> ReadExactAsync(PipeStream pipe, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await pipe.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
                return null;
            read += n;
        }

        return buffer;
    }
}
