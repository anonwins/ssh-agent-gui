namespace SshAgentGui;

internal sealed class SingleInstance : IDisposable
{
    public const string MutexName = @"Local\SshAgentGui.SingleInstance";
    public const string EventName = @"Local\SshAgentGui.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _waitTask;
    private bool _disposed;

    public event Action? ShowRequested;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _waitTask = Task.Run(WaitLoop);
    }

    public static bool TryStart(out SingleInstance? instance)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var created);
        if (!created)
        {
            mutex.Dispose();
            try
            {
                using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                ev.Set();
            }
            catch (AbandonedMutexException)
            {
                // ignore
            }

            instance = null;
            return false;
        }

        instance = new SingleInstance(mutex);
        return true;
    }

    private void WaitLoop()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_showEvent.WaitOne(500))
                    ShowRequested?.Invoke();
            }
        }
        catch (ObjectDisposedException)
        {
            // shutting down
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        try
        {
            _waitTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // ignored
        }

        _showEvent.Dispose();
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // not owned
        }

        _mutex.Dispose();
        _cts.Dispose();
    }
}
