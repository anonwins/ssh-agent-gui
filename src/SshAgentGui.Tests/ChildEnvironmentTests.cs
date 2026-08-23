using System.Diagnostics;
using SshAgentGui;
using SshAgentGui.Ssh;

namespace SshAgentGui.Tests;

public sealed class ChildEnvironmentTests
{
    [Fact]
    public void Askpass_overwrites_hostile_inherited_values()
    {
        var psi = HostileStartInfo();
        OpenSshProcess.ConfigureChildEnvironment(psi, "ssh-agent-gui-pipe", @"C:\Windows\System32\OpenSSH-not-used\SshAgentGui.exe");

        Assert.Equal(@"C:\Windows\System32\OpenSSH-not-used\SshAgentGui.exe", psi.Environment["SSH_ASKPASS"]);
        Assert.Equal("force", psi.Environment["SSH_ASKPASS_REQUIRE"]);
        Assert.Equal("1", psi.Environment["DISPLAY"]);
        Assert.Equal("1", psi.Environment[AskPassMode.LaunchEnv]);
        Assert.Equal("ssh-agent-gui-pipe", psi.Environment[AskPassMode.PipeEnv]);
        Assert.False(psi.Environment.ContainsKey("SSH_AUTH_SOCK"));
        Assert.False(psi.Environment.ContainsKey("SSH_AGENT_PID"));
        Assert.False(psi.Environment.ContainsKey("SSH_ASKPASS_PROMPT"));
        Assert.False(psi.Environment.ContainsKey(AskPassMode.LegacyFileEnv));
        Assert.True(psi.Environment.TryGetValue("SSH_ASKPASS", out var askPass));
        Assert.True(Path.IsPathRooted(askPass));
        Assert.False(askPass.Contains("dotnet.exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Non_askpass_child_drops_leftover_pipe_env()
    {
        var psi = HostileStartInfo();
        OpenSshProcess.ConfigureChildEnvironment(psi, pipeName: null, askPassExe: null);

        Assert.False(psi.Environment.ContainsKey(AskPassMode.PipeEnv));
        Assert.False(psi.Environment.ContainsKey(AskPassMode.LegacyFileEnv));
        Assert.False(psi.Environment.ContainsKey("SSH_ASKPASS"));
        Assert.False(psi.Environment.ContainsKey("SSH_ASKPASS_REQUIRE"));
        Assert.False(psi.Environment.ContainsKey(AskPassMode.LaunchEnv));
        Assert.False(psi.Environment.ContainsKey("SSH_AUTH_SOCK"));
        Assert.False(psi.Environment.ContainsKey("SSH_AGENT_PID"));
        Assert.False(psi.Environment.ContainsKey("SSH_ASKPASS_PROMPT"));
    }

    [Fact]
    public void StripGitAgentEnv_removes_empty_auth_sock()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["SSH_AUTH_SOCK"] = "";
        psi.Environment["SSH_AGENT_PID"] = "123";
        OpenSshProcess.StripGitAgentEnv(psi);
        Assert.False(psi.Environment.ContainsKey("SSH_AUTH_SOCK"));
        Assert.False(psi.Environment.ContainsKey("SSH_AGENT_PID"));
    }

    private static ProcessStartInfo HostileStartInfo()
    {
        var psi = new ProcessStartInfo();
        psi.Environment["SSH_ASKPASS"] = @"C:\evil\askpass.exe";
        psi.Environment["SSH_ASKPASS_REQUIRE"] = "never";
        psi.Environment["DISPLAY"] = "hostile";
        psi.Environment["SSH_AUTH_SOCK"] = "";
        psi.Environment["SSH_AGENT_PID"] = "999";
        psi.Environment["SSH_ASKPASS_PROMPT"] = "confirm";
        psi.Environment[AskPassMode.PipeEnv] = "leftover-pipe";
        psi.Environment[AskPassMode.LegacyFileEnv] = @"C:\temp\ssh-agent-gui-old.tmp";
        psi.Environment[AskPassMode.LaunchEnv] = "1";
        return psi;
    }
}
