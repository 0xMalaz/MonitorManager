using System.Security.Principal;

namespace MonitorCenter.Services;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly string _mutexName;
    private readonly string _activationEventName;
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _registeredWait;
    private bool _ownsMutex;

    public SingleInstanceCoordinator()
    {
        var names = CreateNames(GetCurrentUserIdentity());
        _mutexName = names.MutexName;
        _activationEventName = names.ActivationEventName;
    }

    public bool TryAcquire(Action activationRequested)
    {
        _activationEvent = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            _activationEventName);

        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            _activationEvent.Set();
            return false;
        }

        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    activationRequested();
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    internal static (string MutexName, string ActivationEventName) CreateNames(string userIdentity)
    {
        var safeIdentity = string.Concat(userIdentity.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
        return (
            $@"Local\MonitorCenter.{safeIdentity}",
            $@"Local\MonitorCenter.Activate.{safeIdentity}");
    }

    private static string GetCurrentUserIdentity()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        }
        catch (PlatformNotSupportedException)
        {
            return Environment.UserName;
        }
    }

    public void Dispose()
    {
        _registeredWait?.Unregister(null);
        _registeredWait = null;
        _activationEvent?.Dispose();
        _activationEvent = null;

        if (_ownsMutex)
        {
            _mutex?.ReleaseMutex();
        }

        _mutex?.Dispose();
        _mutex = null;
        _ownsMutex = false;
    }
}
