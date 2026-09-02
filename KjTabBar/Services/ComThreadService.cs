using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using KjTabBar.Helpers;
using KjTabBar.Models;

namespace KjTabBar.Services
{
    public sealed class ComThreadService : IDisposable
    {
        private const int DefaultQueueCapacity = 64;
        private static readonly TimeSpan DefaultInvocationTimeout = TimeSpan.FromSeconds(5);
        private static readonly Lazy<ComThreadService> _instance = new Lazy<ComThreadService>(() => new ComThreadService());
        public static ComThreadService Instance => _instance.Value;
        public static bool IsCreated { get { return _instance.IsValueCreated; } }

        private readonly Thread _thread;
        private readonly BlockingCollection<Action> _queue;
        private readonly TimeSpan _invocationTimeout;
        private int _disposed;

        private ComThreadService()
            : this(DefaultQueueCapacity, DefaultInvocationTimeout)
        {
        }

        internal ComThreadService(int queueCapacity, TimeSpan invocationTimeout)
        {
            if (queueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException("queueCapacity");
            }
            if (invocationTimeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException("invocationTimeout");
            }

            _queue = new BlockingCollection<Action>(new ConcurrentQueue<Action>(), queueCapacity);
            _invocationTimeout = invocationTimeout;
            _thread = new Thread(RunLoop);
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Name = "KjTabBar_STA_Worker";
            _thread.Start();
        }

        private void RunLoop()
        {
            try
            {
                foreach (Action action in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("ComThreadService", "Unhandled action failed on COM worker thread.", ex);
                    }
                }
            }
            finally
            {
                try
                {
                    ShellWindowCacheManager.ResetShellApplication();
                    System.Runtime.InteropServices.Marshal.CleanupUnusedObjectsInCurrentContext();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("ComThreadService", "Failed to clean up COM cache at thread exit.", ex);
                }
            }
        }

        public Task<T> InvokeAsync<T>(Func<T> func)
        {
            if (func == null)
            {
                throw new ArgumentNullException("func");
            }

            TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Timer timeoutTimer = CreateTimeoutTimer(tcs);
            if (!TryAdd(() =>
            {
                if (tcs.Task.IsCompleted)
                {
                    timeoutTimer.Dispose();
                    return;
                }

                try { tcs.TrySetResult(func()); }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { timeoutTimer.Dispose(); }
            }))
            {
                timeoutTimer.Dispose();
                tcs.TrySetException(new InvalidOperationException("The COM worker queue is unavailable or full."));
            }
            return tcs.Task;
        }

        public Task InvokeAsync(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Timer timeoutTimer = CreateTimeoutTimer(tcs);
            if (!TryAdd(() =>
            {
                if (tcs.Task.IsCompleted)
                {
                    timeoutTimer.Dispose();
                    return;
                }

                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally { timeoutTimer.Dispose(); }
            }))
            {
                timeoutTimer.Dispose();
                tcs.TrySetException(new InvalidOperationException("The COM worker queue is unavailable or full."));
            }
            return tcs.Task;
        }

        private Timer CreateTimeoutTimer<T>(TaskCompletionSource<T> tcs)
        {
            Timer timeoutTimer = null;
            timeoutTimer = new Timer(
                delegate
                {
                    if (tcs.TrySetException(new TimeoutException("The COM worker operation timed out.")))
                    {
                        timeoutTimer.Dispose();
                    }
                },
                null,
                _invocationTimeout,
                Timeout.InfiniteTimeSpan);
            return timeoutTimer;
        }

        private bool TryAdd(Action action)
        {
            if (_queue.IsAddingCompleted)
            {
                return false;
            }

            try
            {
                return _queue.TryAdd(action);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _queue.CompleteAdding();
            }
        }
    }
}
