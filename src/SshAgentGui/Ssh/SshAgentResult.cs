namespace SshAgentGui.Ssh;

internal enum SshAgentStatus
{
    Success,
    Empty,
    AgentUnavailable,
    BinaryMissing,
    Failed,
}

internal class SshAgentResult
{
    public SshAgentStatus Status { get; }
    public string Message { get; }

    public bool Ok => Status is SshAgentStatus.Success or SshAgentStatus.Empty;

    public SshAgentResult(SshAgentStatus status, string message = "")
    {
        Status = status;
        Message = message;
    }

    public static SshAgentResult Success(string message = "") => new(SshAgentStatus.Success, message);
    public static SshAgentResult Empty() => new(SshAgentStatus.Empty, "The agent has no identities.");
    public static SshAgentResult Unavailable(string message) => new(SshAgentStatus.AgentUnavailable, message);
    public static SshAgentResult Missing(string message) => new(SshAgentStatus.BinaryMissing, message);
    public static SshAgentResult Fail(string message) => new(SshAgentStatus.Failed, message);
}

internal sealed class SshAgentResult<T> : SshAgentResult
{
    public T? Value { get; }

    public SshAgentResult(SshAgentStatus status, T? value, string message = "")
        : base(status, message)
    {
        Value = value;
    }

    public static SshAgentResult<T> OkValue(T value, string message = "") =>
        new(SshAgentStatus.Success, value, message);

    public static new SshAgentResult<T> Empty() =>
        new(SshAgentStatus.Empty, default, "The agent has no identities.");

    public static new SshAgentResult<T> Unavailable(string message) =>
        new(SshAgentStatus.AgentUnavailable, default, message);

    public static new SshAgentResult<T> Missing(string message) =>
        new(SshAgentStatus.BinaryMissing, default, message);

    public static new SshAgentResult<T> Fail(string message) =>
        new(SshAgentStatus.Failed, default, message);
}
