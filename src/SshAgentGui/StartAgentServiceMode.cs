using SshAgentGui.Ssh;

namespace SshAgentGui;

internal static class StartAgentServiceMode
{
    public const string Flag = WindowsSshAgentService.ElevateArgument;

    public static bool IsLaunch(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], Flag, StringComparison.Ordinal);

    public static int Run()
    {
        var result = new WindowsSshAgentService().TryStart();
        return result.Succeeded ? 0 : 1;
    }
}
