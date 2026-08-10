namespace PCModeSwitcher.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly string _mutexName;
    private readonly string _activationEventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private bool _ownsMutex;
    private bool _disposed;

    public SingleInstanceCoordinator(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        _mutexName = $"Local\\{applicationId}.Mutex";
        _activationEventName = $"Local\\{applicationId}.Activate";
    }

    public event EventHandler? ActivationRequested;

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mutex is not null)
        {
            throw new InvalidOperationException("多重起動制御は一度だけ開始できます。");
        }

        // イベントを先に作ることで、同時起動でも後発プロセスからの表示要求を失わない。
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            _activationEventName);
        _mutex = new Mutex(false, _mutexName);

        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            _activationEvent.Set();
            return false;
        }

        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    ActivationRequested?.Invoke(this, EventArgs.Empty);
                }
            },
            null,
            Timeout.Infinite,
            false);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activationRegistration?.Unregister(null);
        _activationRegistration = null;
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
