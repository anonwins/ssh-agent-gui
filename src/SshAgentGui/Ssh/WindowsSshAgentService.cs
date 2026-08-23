using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;

namespace SshAgentGui.Ssh;

internal enum SshAgentServiceState
{
    Running,
    Stopped,
    Disabled,
    Missing,
}

internal enum SshAgentServiceStartKind
{
    Ok,
    NeedsElevation,
    Cancelled,
    Failed,
}

internal sealed class SshAgentServiceStartResult
{
    public SshAgentServiceStartKind Kind { get; }
    public string Message { get; }
    public bool Succeeded => Kind == SshAgentServiceStartKind.Ok;

    private SshAgentServiceStartResult(SshAgentServiceStartKind kind, string message)
    {
        Kind = kind;
        Message = message;
    }

    public static SshAgentServiceStartResult Ok() => new(SshAgentServiceStartKind.Ok, "");

    public static SshAgentServiceStartResult NeedsElevation() =>
        new(SshAgentServiceStartKind.NeedsElevation, "");

    public static SshAgentServiceStartResult Cancelled() =>
        new(SshAgentServiceStartKind.Cancelled, "Start cancelled.");

    public static SshAgentServiceStartResult Failed(string message) =>
        new(SshAgentServiceStartKind.Failed, message);
}

internal sealed class WindowsSshAgentService
{
    public const string ServiceName = "ssh-agent";
    public const string ElevateArgument = "--start-ssh-agent";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private const int ErrorAccessDenied = 5;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceDisabled = 1058;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorCancelled = 1223;

    public SshAgentServiceState Query()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.StartType == ServiceStartMode.Disabled)
                return SshAgentServiceState.Disabled;

            controller.Refresh();
            return controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending
                ? SshAgentServiceState.Running
                : SshAgentServiceState.Stopped;
        }
        catch (InvalidOperationException ex) when (HasNativeError(ex, ErrorServiceDoesNotExist))
        {
            return SshAgentServiceState.Missing;
        }
        catch (InvalidOperationException)
        {
            return SshAgentServiceState.Missing;
        }
        catch (Win32Exception)
        {
            return SshAgentServiceState.Stopped;
        }
    }

    public SshAgentServiceStartResult TryStart()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            if (controller.StartType == ServiceStartMode.Disabled)
                return SshAgentServiceStartResult.Failed(DisabledMessage());

            controller.Refresh();
            if (controller.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            {
                if (controller.Status == ServiceControllerStatus.StartPending)
                    controller.WaitForStatus(ServiceControllerStatus.Running, StartTimeout);
                return SshAgentServiceStartResult.Ok();
            }

            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, StartTimeout);
            return SshAgentServiceStartResult.Ok();
        }
        catch (Exception ex)
        {
            return MapStartException(ex);
        }
    }

    public async Task<SshAgentServiceStartResult> TryStartElevatedAsync(
        CancellationToken cancellationToken = default)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrEmpty(self) || !File.Exists(self))
            return SshAgentServiceStartResult.Failed("Could not locate this program to start the service.");

        var psi = new ProcessStartInfo
        {
            FileName = self,
            Arguments = ElevateArgument,
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return SshAgentServiceStartResult.Failed("Could not start the OpenSSH Authentication Agent service.");

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return Query() == SshAgentServiceState.Running
                ? SshAgentServiceStartResult.Ok()
                : SshAgentServiceStartResult.Failed("Could not start the OpenSSH Authentication Agent service.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return SshAgentServiceStartResult.Cancelled();
        }
        catch (Win32Exception)
        {
            return SshAgentServiceStartResult.Failed("Could not start the OpenSSH Authentication Agent service.");
        }
    }

    private static SshAgentServiceStartResult MapStartException(Exception ex)
    {
        if (HasNativeError(ex, ErrorServiceAlreadyRunning))
            return SshAgentServiceStartResult.Ok();
        if (ex is UnauthorizedAccessException || HasNativeError(ex, ErrorAccessDenied))
            return SshAgentServiceStartResult.NeedsElevation();
        if (HasNativeError(ex, ErrorServiceDisabled))
            return SshAgentServiceStartResult.Failed(DisabledMessage());
        if (HasNativeError(ex, ErrorServiceDoesNotExist))
            return SshAgentServiceStartResult.Failed("The OpenSSH Authentication Agent service was not found.");
        if (ex is System.ServiceProcess.TimeoutException or System.TimeoutException)
            return SshAgentServiceStartResult.Failed("Timed out waiting for the OpenSSH Authentication Agent to start.");

        return SshAgentServiceStartResult.Failed("Could not start the OpenSSH Authentication Agent service.");
    }

    private static bool HasNativeError(Exception ex, int code)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is Win32Exception win && win.NativeErrorCode == code)
                return true;
        }

        return false;
    }

    private static string DisabledMessage() =>
        "The OpenSSH Authentication Agent service is disabled. Set it to Manual or Automatic in Services, then start it.";
}
