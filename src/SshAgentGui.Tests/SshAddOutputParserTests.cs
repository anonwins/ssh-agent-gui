using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class SshAddOutputParserTests
{
    [Fact]
    public void Missing_key_is_not_agent_down() =>
        Assert.False(SshAddOutputParser.IsAgentUnavailable(@"C:\Users\me\.ssh\id_ed25519: No such file or directory"));

    [Theory]
    [InlineData("Error connecting to agent: No such file or directory")]
    [InlineData("Could not open a connection to your authentication agent.")]
    [InlineData("communication with agent failed")]
    public void Agent_phrases_are_unavailable(string text) =>
        Assert.True(SshAddOutputParser.IsAgentUnavailable(text));

    [Theory]
    [InlineData("No such process")]
    [InlineData("Connection refused")]
    [InlineData("Bad file descriptor")]
    public void Standalone_errnos_are_not_agent_down(string text) =>
        Assert.False(SshAddOutputParser.IsAgentUnavailable(text));
}
