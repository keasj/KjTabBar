using System;
using System.Threading;
using KjTabBar.Helpers;

namespace KjTabBar.Services
{
    // A Win32 input backlog can delay DispatcherTimer and Explorer WinEvents together.
    // Schedule independently, but keep all Explorer state access on the UI dispatcher.
    internal sealed class ExplorerMonitorTimer : IDisposable
    {
        private readonly Action<Action> _enqueue;
        private readonly Action _tick;
        private readonly Timer _timer;
        private int _pending;
        private int _disposed;

        internal ExplorerMonitorTimer(TimeSpan interval, Action<Action> enqueue, Action tick)
        {
            _enqueue = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
            _tick = tick ?? throw new ArgumentNullException(nameof(tick));
            _timer = new Timer(state => RequestTick(), null, interval, interval);
        }

        internal void RequestTick()
        {
            if (Volatile.Read(ref _disposed) != 0 || Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
                return;
            try
            {
                _enqueue(delegate
                {
                    try
                    {
                        if (Volatile.Read(ref _disposed) == 0) _tick();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pending, 0);
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                Interlocked.Exchange(ref _pending, 0);
                if (Volatile.Read(ref _disposed) == 0)
                    AppLogger.LogErrorThrottled("ExplorerMonitorTimer", "Queue", "Could not queue monitor tick.", ex, TimeSpan.FromMinutes(5));
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            _timer.Dispose();
        }
    }
}
