using System.IO.Pipes;

namespace SshAgentGui.Ssh;

internal sealed class PageantPipeServer : IDisposable
{
    private readonly string _name;
    private readonly IOpenSshAgentPipe _agent;
    private readonly Func<byte[], bool> _confirm;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;
    private NamedPipeServerStream? _listening;
    private bool _disposed;

    public PageantPipeServer(string name, IOpenSshAgentPipe agent, Func<byte[], bool> confirm)
    {
        _name = name;
        _agent = agent;
        _confirm = confirm;
    }

    public void Start() => _loop = Task.Run(() => RunAsync(_cts.Token));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        try
        {
            _listening?.Dispose();
        }
        catch
        {
            // closing the waiter unblocks Accept
        }

        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _name,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                _listening = server;
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _listening = null;
                var connection = server;
                server = null;
                _ = Task.Run(() => ServeAsync(connection, cancellationToken), cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                server?.Dispose();
                break;
            }
            catch (IOException)
            {
                server?.Dispose();
                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using (server)
        {
            try
            {
                while (server.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var header = await ReadExactAsync(server, 4, cancellationToken).ConfigureAwait(false);
                    if (header is null)
                        return;
                    var length = SshAgentFrame.ReadUInt32Be(header);
                    if (length < 1 || length > SshAgentFrame.MaxLength - 4)
                        return;
                    var body = await ReadExactAsync(server, (int)length, cancellationToken).ConfigureAwait(false);
                    if (body is null)
                        return;

                    var frame = new byte[4 + body.Length];
                    header.CopyTo(frame, 0);
                    body.CopyTo(frame, 4);
                    var response = PageantDispatch.Handle(frame, _agent, _confirm) ?? SshAgentFrame.Failure();
                    await server.WriteAsync(response, cancellationToken).ConfigureAwait(false);
                    await server.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<byte[]?> ReadExactAsync(
        PipeStream pipe,
        int count,
        CancellationToken cancellationToken)
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
