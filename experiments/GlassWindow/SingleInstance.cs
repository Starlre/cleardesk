using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Threading;

namespace ClearDesk.GlassWindow;

// One UI instance per Windows user/session. A second launch only signals the existing UI.
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _listener;
    private bool _disposed;

    private SingleInstance(Mutex mutex, string eventName, Action show)
    {
        _mutex = mutex;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
        var dispatcher = Dispatcher.CurrentDispatcher;
        _listener = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
        {
            if (!_disposed) dispatcher.BeginInvoke(() => { if (!_disposed) show(); });
        }, null, Timeout.Infinite, false);
    }

    internal static async Task<SingleInstance?> AcquireAsync(Action show, string? testKey = null)
    {
        string key = testKey ?? $"{WindowsIdentity.GetCurrent().User!.Value}-{Process.GetCurrentProcess().SessionId}";
        string name = @"Local\ClearDesk-" + key;
        var mutex = new Mutex(false, name);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (acquired)
        {
            try { return new SingleInstance(mutex, name + "-show", show); }
            catch { mutex.ReleaseMutex(); mutex.Dispose(); throw; }
        }
        mutex.Dispose();
        // The first launch may still be creating its event. Give it time to finish startup.
        for (int attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(name + "-show");
                if (testKey is null) AllowExistingForeground();
                signal.Set(); return null;
            }
            catch (WaitHandleCannotBeOpenedException) { await Task.Delay(50); }
        }
        throw new InvalidOperationException("澄桌正在启动，请稍后再次打开。");
    }

    private static void AllowExistingForeground()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id != current.Id && process.SessionId == current.SessionId
                        && process.MainModule?.FileName == current.MainModule?.FileName)
                        Native.AllowSetForegroundWindow((uint)process.Id);
                }
                catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _listener.Unregister(null); _showEvent.Dispose();
        _mutex.ReleaseMutex(); _mutex.Dispose();
    }
}
