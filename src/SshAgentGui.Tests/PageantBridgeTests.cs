using System.IO.Pipes;
using System.Text;
using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class PageantBridgeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..\\x")]
    [InlineData("a/b")]
    [InlineData("name:withcolon")]
    public void Mapping_name_rejects_unsafe(string? name) =>
        Assert.False(PageantMapping.IsSafeName(name));

    [Fact]
    public void Mapping_name_rejects_too_long() =>
        Assert.False(PageantMapping.IsSafeName(new string('A', PageantMapping.MaxNameLength + 1)));

    [Fact]
    public void Mapping_name_accepts_pageant_request() =>
        Assert.True(PageantMapping.IsSafeName("PageantRequest00001234"));

    [Fact]
    public void Failure_frame_is_single_failure_byte() =>
        Assert.Equal(new byte[] { 0, 0, 0, 1, 5 }, SshAgentFrame.Failure());

    [Fact]
    public void TryRead_list_is_not_sign()
    {
        var frame = SshAgentFrame.Prefix(SshAgentFrame.RequestIdentities, []);
        Assert.True(SshAgentFrame.TryRead(frame, out var type, out var body));
        Assert.Equal(SshAgentFrame.RequestIdentities, type);
        Assert.True(body.IsEmpty);
        Assert.False(SshAgentFrame.IsSsh1(type));
        Assert.False(SshAgentFrame.TryGetSignKeyBlob(body, out _));
    }

    [Fact]
    public void TryRead_extracts_sign_blob()
    {
        var blob = Encoding.ASCII.GetBytes("key-blob");
        var frame = SignFrame(blob, "data");
        Assert.True(SshAgentFrame.TryRead(frame, out var type, out var body));
        Assert.Equal(SshAgentFrame.SignRequest, type);
        Assert.True(SshAgentFrame.TryGetSignKeyBlob(body, out var parsed));
        Assert.Equal(blob, parsed);
    }

    [Fact]
    public void Ssh1_is_local_failure()
    {
        var pipe = new RecordingPipe();
        var frame = SshAgentFrame.Prefix(SshAgentFrame.Ssh1RequestIdentities, []);
        var response = PageantDispatch.Handle(frame, pipe, _ => true);
        Assert.Equal(SshAgentFrame.Failure(), response);
        Assert.Empty(pipe.Requests);
    }

    [Fact]
    public void Sign_deny_does_not_call_pipe()
    {
        var pipe = new RecordingPipe();
        var frame = SignFrame(Encoding.ASCII.GetBytes("blob"), "data");
        var response = PageantDispatch.Handle(frame, pipe, _ => false);
        Assert.Equal(SshAgentFrame.Failure(), response);
        Assert.Empty(pipe.Requests);
    }

    [Fact]
    public void List_forwards_to_pipe_without_confirm()
    {
        var expected = SshAgentFrame.Prefix(12, [0, 0, 0, 0]);
        var pipe = new RecordingPipe { Reply = expected };
        var confirmed = false;
        var frame = SshAgentFrame.Prefix(SshAgentFrame.RequestIdentities, []);
        var response = PageantDispatch.Handle(frame, pipe, _ =>
        {
            confirmed = true;
            return true;
        });
        Assert.False(confirmed);
        Assert.Single(pipe.Requests);
        Assert.Equal(expected, response);
    }

    [Fact]
    public void Sign_allow_forwards_original_bytes()
    {
        var blob = Encoding.ASCII.GetBytes("blob");
        var frame = SignFrame(blob, "data");
        var pipe = new RecordingPipe { Reply = SshAgentFrame.Prefix(14, [1, 2, 3]) };
        var response = PageantDispatch.Handle(frame, pipe, seen => seen.SequenceEqual(blob));
        Assert.Single(pipe.Requests);
        Assert.Equal(frame, pipe.Requests[0]);
        Assert.Equal(pipe.Reply, response);
    }

    [Fact]
    public async Task Fingerprint_matches_public_line_blob()
    {
        var pub = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ed25519_clear.pub"));
        Assert.True(OpenSshFingerprint.TryParsePublicLine(pub, out var blob));
        var computed = OpenSshFingerprint.Sha256(blob);

        if (OpenSshProcess.FindExe("ssh-keygen.exe") is null)
            return;

        var printed = await new WindowsSshKeygen().FingerprintAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ed25519_clear.pub"));
        Assert.True(printed.Ok);
        Assert.Equal(printed.Value!.Fingerprint, computed);
    }

    [Fact]
    public async Task Pipe_round_trips_a_list_frame()
    {
        var name = "ssh-agent-gui-pageant-" + Guid.NewGuid().ToString("n");
        var request = SshAgentFrame.Prefix(SshAgentFrame.RequestIdentities, []);
        var reply = SshAgentFrame.Prefix(12, [0, 0, 0, 0]);
        using var server = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync();
        var serve = Task.Run(async () =>
        {
            await connected;
            var header = new byte[4];
            await server.ReadExactlyAsync(header);
            var length = (int)SshAgentFrame.ReadUInt32Be(header);
            var body = new byte[length];
            await server.ReadExactlyAsync(body);
            await server.WriteAsync(reply);
            await server.FlushAsync();
        });

        var client = new OpenSshAgentPipe(name);
        var response = await Task.Run(() => client.Transact(request));
        await serve.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(reply, response);
    }

    [Fact]
    public void Pageant_pipe_name_is_stable_hex()
    {
        var first = PageantPipeName.Obfuscate("Pageant");
        var second = PageantPipeName.Obfuscate("Pageant");
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
        Assert.Equal(first, second);
        Assert.Equal($"pageant.{PageantPipeName.UserName()}.{first}", PageantPipeName.ForCurrentUser());
    }

    [Fact]
    public async Task Pageant_pipe_server_lists_without_confirm()
    {
        var name = "ssh-agent-gui-pageant-listen-" + Guid.NewGuid().ToString("n");
        var request = SshAgentFrame.Prefix(SshAgentFrame.RequestIdentities, []);
        var reply = SshAgentFrame.Prefix(12, [0, 0, 0, 0]);
        var agent = new RecordingPipe { Reply = reply };
        var confirmed = false;
        using var server = new PageantPipeServer(name, agent, _ =>
        {
            confirmed = true;
            return true;
        });
        server.Start();

        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connected = false;
        for (var i = 0; i < 50 && !connected; i++)
        {
            try
            {
                await client.ConnectAsync(100);
                connected = true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
            }
        }

        Assert.True(connected);
        await client.WriteAsync(request);
        await client.FlushAsync();
        var header = new byte[4];
        await client.ReadExactlyAsync(header);
        var length = (int)SshAgentFrame.ReadUInt32Be(header);
        var body = new byte[length];
        await client.ReadExactlyAsync(body);
        var response = new byte[4 + body.Length];
        header.CopyTo(response, 0);
        body.CopyTo(response, 4);

        Assert.False(confirmed);
        Assert.Single(agent.Requests);
        Assert.Equal(reply, response);
    }

    [Fact]
    public void Pipe_connect_fail_returns_null()
    {
        var client = new OpenSshAgentPipe("ssh-agent-gui-missing-" + Guid.NewGuid().ToString("n"));
        Assert.Null(client.Transact(SshAgentFrame.Prefix(SshAgentFrame.RequestIdentities, [])));
    }

    private static byte[] SignFrame(byte[] blob, string data)
    {
        var payload = new List<byte>();
        WriteString(payload, blob);
        WriteString(payload, Encoding.ASCII.GetBytes(data));
        payload.AddRange([0, 0, 0, 0]);
        return SshAgentFrame.Prefix(SshAgentFrame.SignRequest, payload.ToArray());
    }

    private static void WriteString(List<byte> dest, byte[] value)
    {
        dest.Add((byte)(value.Length >> 24));
        dest.Add((byte)(value.Length >> 16));
        dest.Add((byte)(value.Length >> 8));
        dest.Add((byte)value.Length);
        dest.AddRange(value);
    }

    private sealed class RecordingPipe : IOpenSshAgentPipe
    {
        public List<byte[]> Requests { get; } = [];
        public byte[]? Reply { get; set; } = SshAgentFrame.Failure();

        public byte[]? Transact(byte[] request)
        {
            Requests.Add(request);
            return Reply;
        }
    }
}
